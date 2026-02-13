using PrintGuard.Core.Configuration;
using PrintGuard.Core.Logging;
using PrintGuard.Core.Models;

namespace PrintGuard.Core.Printing;

public sealed class PrintFirewallService : IDisposable
{
    private readonly IConfigService _configService;
    private readonly IPrintQueueController _queueController;
    private readonly IPrintJobWatcher _wmiWatcher;
    private readonly IPrintJobWatcher _pollingWatcher;
    private readonly ILoggerService _logger;
    private readonly object _stateLock = new();

    private readonly Dictionary<string, BlockedJobState> _blockedJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _seenJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _recentlyReleasedJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _singlePrintBypassByPrinter = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _recentlyAllowedJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _queuePausedFallbackPrinters = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _housekeepingCts;
    private Task? _housekeepingTask;

    private bool _isStarted;

    public PrintFirewallService(
        IConfigService configService,
        IPrintQueueController queueController,
        IPrintJobWatcher wmiWatcher,
        IPrintJobWatcher pollingWatcher,
        ILoggerService logger)
    {
        _configService = configService;
        _queueController = queueController;
        _wmiWatcher = wmiWatcher;
        _pollingWatcher = pollingWatcher;
        _logger = logger;
    }

    public event EventHandler<PrintJobInfo>? JobBlocked;
    public event EventHandler? StateChanged;

    public bool IsEnabled { get; private set; }

    public DateTimeOffset? UnlockUntilUtc { get; private set; }

    public bool IsWithinUnlockWindow =>
        UnlockUntilUtc is { } unlockedUntil && unlockedUntil > DateTimeOffset.UtcNow;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_isStarted)
        {
            return Task.CompletedTask;
        }

        IsEnabled = _configService.Current.IsProtectionEnabled;

        _wmiWatcher.JobDetected += OnJobDetected;
        _wmiWatcher.WatcherError += OnWatcherError;
        _pollingWatcher.JobDetected += OnJobDetected;
        _pollingWatcher.WatcherError += OnWatcherError;

        _wmiWatcher.Start();
        _pollingWatcher.Start();

        _housekeepingCts = new CancellationTokenSource();
        _housekeepingTask = Task.Run(() => HousekeepingLoopAsync(_housekeepingCts.Token), _housekeepingCts.Token);

        _isStarted = true;

        if (IsEnabled && _configService.Current.ReassertPausedJobsOnStartup)
        {
            ScanExistingJobsOnStartup();
        }

        _logger.Info($"Print firewall started. Enabled={IsEnabled}.");
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_isStarted)
        {
            return Task.CompletedTask;
        }

        _wmiWatcher.JobDetected -= OnJobDetected;
        _wmiWatcher.WatcherError -= OnWatcherError;
        _pollingWatcher.JobDetected -= OnJobDetected;
        _pollingWatcher.WatcherError -= OnWatcherError;

        _wmiWatcher.Stop();
        _pollingWatcher.Stop();

        if (_housekeepingCts is not null)
        {
            _housekeepingCts.Cancel();

            try
            {
                _housekeepingTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            _housekeepingCts.Dispose();
            _housekeepingCts = null;
            _housekeepingTask = null;
        }

        _isStarted = false;
        _logger.Info("Print firewall stopped.");
        return Task.CompletedTask;
    }

    public async Task SetEnabledAsync(bool enabled, string reason, CancellationToken cancellationToken = default)
    {
        IsEnabled = enabled;
        UnlockUntilUtc = null;

        if (!enabled)
        {
            lock (_stateLock)
            {
                _singlePrintBypassByPrinter.Clear();
                _recentlyAllowedJobs.Clear();
                _queuePausedFallbackPrinters.Clear();
            }
        }

        await _configService.UpdateAsync(cfg => cfg.IsProtectionEnabled = enabled, cancellationToken)
            .ConfigureAwait(false);

        if (!enabled)
        {
            ResumeAllBlockedJobs("Protection disabled");
        }
        else if (_configService.Current.ReassertPausedJobsOnStartup)
        {
            ScanExistingJobsOnStartup();
        }

        _logger.Info($"Protection {(enabled ? "enabled" : "disabled")} ({reason}).");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task UnlockForAsync(TimeSpan duration, string reason)
    {
        if (duration <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        UnlockUntilUtc = DateTimeOffset.UtcNow.Add(duration);
        ResumeAllBlockedJobs($"Temporary unlock ({reason})");
        _logger.Info($"Printing unlocked for {duration.TotalMinutes:F0} minutes ({reason}).");
        StateChanged?.Invoke(this, EventArgs.Empty);

        return Task.CompletedTask;
    }

    public Task<bool> UnlockJobAsync(PrintJobKey key, string reason)
    {
        var composite = key.ToCompositeKey();
        var queueFallback = false;

        lock (_stateLock)
        {
            if (_blockedJobs.TryGetValue(composite, out var state))
            {
                queueFallback = state.QueuePausedFallback;
            }
        }

        var succeeded = false;
        string message;

        if (queueFallback || IsQueuePausedFallbackActive(key.PrinterName))
        {
            succeeded = _queueController.TryResumeQueue(key.PrinterName, out message);

            lock (_stateLock)
            {
                var printerMatches = _blockedJobs
                    .Where(x => x.Value.QueuePausedFallback && string.Equals(x.Value.Job.Key.PrinterName, key.PrinterName, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Key)
                    .ToList();

                foreach (var blockedKey in printerMatches)
                {
                    _blockedJobs.Remove(blockedKey);
                    _recentlyReleasedJobs[blockedKey] = DateTimeOffset.UtcNow;
                }

                _queuePausedFallbackPrinters.Remove(NormalizePrinterName(key.PrinterName));
            }
        }
        else
        {
            succeeded = _queueController.TryResumeJob(key, out message);

            lock (_stateLock)
            {
                _blockedJobs.Remove(composite);
                _recentlyReleasedJobs[composite] = DateTimeOffset.UtcNow;
            }
        }

        if (succeeded)
        {
            if (queueFallback)
            {
                _logger.Info($"Released fallback-paused queue '{key.PrinterName}' ({reason}).");
            }
            else
            {
                _logger.Info($"Released blocked job {key} ({reason}).");
            }
        }
        else
        {
            if (queueFallback)
            {
                _logger.Warning($"Unable to resume queue '{key.PrinterName}'. {message}");
            }
            else
            {
                _logger.Warning($"Unable to resume job {key}. {message}");
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(succeeded);
    }

    public void ArmSinglePrintBypass(string printerName, string reason, TimeSpan? lifetime = null)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(lifetime ?? TimeSpan.FromMinutes(2));
        var normalized = NormalizePrinterName(printerName);

        lock (_stateLock)
        {
            _singlePrintBypassByPrinter[normalized] = expiresAt;
        }

        _logger.Info($"Single-print bypass armed for '{printerName}' until {expiresAt:HH:mm:ss} ({reason}).");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public FailedUnlockOutcome RegisterFailedUnlockAttempt(PrintJobKey key)
    {
        var composite = key.ToCompositeKey();
        var cfg = _configService.Current;

        BlockedJobState? state;

        lock (_stateLock)
        {
            _blockedJobs.TryGetValue(composite, out state);
            if (state is null)
            {
                return new FailedUnlockOutcome(JobCanceled: false, FailedAttempts: 0, CancelThreshold: null);
            }

            state.FailedUnlockAttempts++;

            if (state.QueuePausedFallback)
            {
                return new FailedUnlockOutcome(
                    JobCanceled: false,
                    FailedAttempts: state.FailedUnlockAttempts,
                    CancelThreshold: null);
            }

            if (!cfg.CancelJobAfterFailedUnlockAttempts)
            {
                return new FailedUnlockOutcome(
                    JobCanceled: false,
                    FailedAttempts: state.FailedUnlockAttempts,
                    CancelThreshold: null);
            }

            if (state.FailedUnlockAttempts < cfg.CancelJobFailedAttemptThreshold)
            {
                return new FailedUnlockOutcome(
                    JobCanceled: false,
                    FailedAttempts: state.FailedUnlockAttempts,
                    CancelThreshold: cfg.CancelJobFailedAttemptThreshold);
            }
        }

        var canceled = _queueController.TryCancelJob(key, out var reason);
        if (canceled)
        {
            lock (_stateLock)
            {
                _blockedJobs.Remove(composite);
            }

            _logger.Warning($"Blocked job {key} canceled after too many failed unlock attempts.");
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new FailedUnlockOutcome(true, cfg.CancelJobFailedAttemptThreshold, cfg.CancelJobFailedAttemptThreshold);
        }

        _logger.Warning($"Failed to cancel job {key} after failed unlock attempts. {reason}");
        return new FailedUnlockOutcome(false, cfg.CancelJobFailedAttemptThreshold, cfg.CancelJobFailedAttemptThreshold);
    }

    public bool IsJobBlocked(PrintJobKey key)
    {
        lock (_stateLock)
        {
            return _blockedJobs.ContainsKey(key.ToCompositeKey());
        }
    }

    public IReadOnlyList<PrintJobInfo> GetBlockedJobsSnapshot()
    {
        lock (_stateLock)
        {
            return _blockedJobs
                .Values
                .OrderBy(x => x.BlockedAtUtc)
                .Select(x => x.Job)
                .ToList();
        }
    }

    public void Dispose()
    {
        _ = StopAsync();
        _wmiWatcher.Dispose();
        _pollingWatcher.Dispose();
    }

    private void OnJobDetected(object? sender, PrintJobInfo job)
    {
        HandleJobDetected(job, raisePrompt: true);
    }

    private void OnWatcherError(object? sender, Exception exception)
    {
        _logger.Warning($"Print watcher warning: {exception.Message}");
    }

    private void HandleJobDetected(PrintJobInfo job, bool raisePrompt)
    {
        if (!IsEnabled || IsWithinUnlockWindow)
        {
            return;
        }

        var cfg = _configService.Current;
        if (!cfg.IsPrinterProtected(job.Key.PrinterName))
        {
            return;
        }

        if (IsRecentlyAllowedJob(job.Key))
        {
            _logger.Info($"Skipping already-authorized print job {job.Key}.");
            return;
        }

        if (TryConsumeSinglePrintBypass(job.Key))
        {
            MarkJobAsAllowed(job.Key, TimeSpan.FromMinutes(3));
            return;
        }

        var composite = job.Key.ToCompositeKey();
        var now = DateTimeOffset.UtcNow;

        lock (_stateLock)
        {
            if (_blockedJobs.TryGetValue(composite, out var existing))
            {
                existing.LastSeenUtc = now;
                return;
            }

            if (_queuePausedFallbackPrinters.Contains(NormalizePrinterName(job.Key.PrinterName)))
            {
                return;
            }

            if (_recentlyReleasedJobs.TryGetValue(composite, out var releasedAt) && now - releasedAt < TimeSpan.FromMinutes(5))
            {
                return;
            }

            if (_seenJobs.TryGetValue(composite, out var seenAt) && now - seenAt < TimeSpan.FromMinutes(15))
            {
                return;
            }

            _seenJobs[composite] = now;
        }

        var paused = _queueController.TryPauseJob(job.Key, out var pauseResult);

        if (!paused)
        {
            var queuePaused = _queueController.TryPauseQueue(job.Key.PrinterName, out var queuePauseResult);
            if (queuePaused)
            {
                var queueBlockedJob = job with
                {
                    IsPaused = true,
                    DetectedAtUtc = now,
                    Source = $"{job.Source}-QueuePausedFallback"
                };

                lock (_stateLock)
                {
                    _blockedJobs[composite] = new BlockedJobState
                    {
                        Job = queueBlockedJob,
                        BlockedAtUtc = now,
                        LastSeenUtc = now,
                        FailedUnlockAttempts = 0,
                        QueuePausedFallback = true
                    };
                    _queuePausedFallbackPrinters.Add(NormalizePrinterName(job.Key.PrinterName));
                }

                _logger.Warning(
                    $"Job pause unsupported for {job.Key}. Queue '{job.Key.PrinterName}' paused as fallback. " +
                    $"JobPause='{pauseResult}', QueuePause='{queuePauseResult}'.");
                StateChanged?.Invoke(this, EventArgs.Empty);

                if (raisePrompt)
                {
                    JobBlocked?.Invoke(this, queueBlockedJob);
                }

                return;
            }

            string? cancelResult = null;
            var canceledAsFallback = false;

            if (cfg.FallbackCancelWhenPauseFails)
            {
                canceledAsFallback = _queueController.TryCancelJob(job.Key, out var cancelMessage);
                cancelResult = cancelMessage;
            }

            if (canceledAsFallback)
            {
                _logger.Warning($"Pause failed for {job.Key}. Job canceled as fallback. {cancelResult}");

                if (raisePrompt)
                {
                    var canceledFallbackJob = job with
                    {
                        IsPaused = false,
                        DetectedAtUtc = now,
                        Source = $"{job.Source}-CanceledFallback"
                    };

                    JobBlocked?.Invoke(this, canceledFallbackJob);
                }
            }
            else
            {
                var message = cfg.FallbackCancelWhenPauseFails
                    ? $"Unable to pause/cancel print job {job.Key}. Pause='{pauseResult}', Cancel='{cancelResult ?? "n/a"}'"
                    : $"Unable to pause print job {job.Key}. {pauseResult}";

                _logger.Warning(message);

                if (raisePrompt)
                {
                    var fallbackPromptJob = job with
                    {
                        IsPaused = false,
                        DetectedAtUtc = now,
                        Source = $"{job.Source}-PauseFallbackUnavailable"
                    };

                    JobBlocked?.Invoke(this, fallbackPromptJob);
                }
            }

            return;
        }

        var blockedJob = job with { IsPaused = true, DetectedAtUtc = now };

        lock (_stateLock)
        {
            _blockedJobs[composite] = new BlockedJobState
            {
                Job = blockedJob,
                BlockedAtUtc = now,
                LastSeenUtc = now,
                FailedUnlockAttempts = 0
            };
        }

        _logger.Info($"Blocked print job {job.Key} ({job.DocumentName}).");
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (raisePrompt)
        {
            JobBlocked?.Invoke(this, blockedJob);
        }
    }

    private void ScanExistingJobsOnStartup()
    {
        foreach (var job in _queueController.GetCurrentJobs())
        {
            HandleJobDetected(job, raisePrompt: false);
        }
    }

    private int ResumeAllBlockedJobs(string reason)
    {
        List<BlockedJobState> states;

        lock (_stateLock)
        {
            states = _blockedJobs.Values.ToList();
        }

        var resumedJobs = 0;
        var resumedQueues = 0;
        var attemptedQueuePrinters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resumedQueuePrinters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var releasedComposites = new List<string>();

        foreach (var state in states)
        {
            var composite = state.Job.Key.ToCompositeKey();

            if (state.QueuePausedFallback)
            {
                var printerKey = NormalizePrinterName(state.Job.Key.PrinterName);
                if (attemptedQueuePrinters.Add(printerKey))
                {
                    if (_queueController.TryResumeQueue(state.Job.Key.PrinterName, out var queueResumeMessage))
                    {
                        resumedQueuePrinters.Add(printerKey);
                        resumedQueues++;
                    }
                    else
                    {
                        _logger.Warning($"Unable to resume queue '{state.Job.Key.PrinterName}'. {queueResumeMessage}");
                    }
                }

                if (resumedQueuePrinters.Contains(printerKey))
                {
                    releasedComposites.Add(composite);
                }
            }
            else if (_queueController.TryResumeJob(state.Job.Key, out _))
            {
                resumedJobs++;
                releasedComposites.Add(composite);
            }
        }

        lock (_stateLock)
        {
            foreach (var composite in releasedComposites)
            {
                _blockedJobs.Remove(composite);
                _recentlyReleasedJobs[composite] = DateTimeOffset.UtcNow;
            }

            foreach (var printer in resumedQueuePrinters)
            {
                _queuePausedFallbackPrinters.Remove(printer);
            }
        }

        var resumed = resumedJobs + resumedQueues;
        if (resumed > 0)
        {
            _logger.Info(
                $"Resumed {resumedJobs} blocked job(s) and {resumedQueues} paused queue(s). Reason: {reason}.");
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return resumed;
    }

    private async Task HousekeepingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                CleanupInMemoryTracking();
                RemoveMissingBlockedJobs();
                AutoCancelExpiredJobs();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Housekeeping warning: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void CleanupInMemoryTracking()
    {
        var now = DateTimeOffset.UtcNow;
        var seenCutoff = now.AddMinutes(-30);
        var releasedCutoff = now.AddMinutes(-10);

        lock (_stateLock)
        {
            var staleSeen = _seenJobs.Where(x => x.Value < seenCutoff).Select(x => x.Key).ToList();
            foreach (var key in staleSeen)
            {
                _seenJobs.Remove(key);
            }

            var staleReleased = _recentlyReleasedJobs.Where(x => x.Value < releasedCutoff).Select(x => x.Key).ToList();
            foreach (var key in staleReleased)
            {
                _recentlyReleasedJobs.Remove(key);
            }

            var staleBypass = _singlePrintBypassByPrinter
                .Where(x => x.Value <= now)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in staleBypass)
            {
                _singlePrintBypassByPrinter.Remove(key);
            }

            var staleAllowed = _recentlyAllowedJobs
                .Where(x => x.Value <= now)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in staleAllowed)
            {
                _recentlyAllowedJobs.Remove(key);
            }
        }
    }

    private void RemoveMissingBlockedJobs()
    {
        List<string> toRemove;

        lock (_stateLock)
        {
            toRemove = _blockedJobs
                .Where(x => !x.Value.QueuePausedFallback)
                .Where(x => !_queueController.JobExists(x.Value.Job.Key))
                .Select(x => x.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _blockedJobs.Remove(key);
            }
        }

        if (toRemove.Count > 0)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AutoCancelExpiredJobs()
    {
        var cfg = _configService.Current;
        if (!cfg.EnableAutoCancelPausedJobs)
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-cfg.AutoCancelMinutes);
        List<BlockedJobState> expired;

        lock (_stateLock)
        {
            expired = _blockedJobs
                .Values
                .Where(x => !x.QueuePausedFallback)
                .Where(x => x.BlockedAtUtc <= cutoff)
                .ToList();
        }

        foreach (var state in expired)
        {
            if (_queueController.TryCancelJob(state.Job.Key, out _))
            {
                lock (_stateLock)
                {
                    _blockedJobs.Remove(state.Job.Key.ToCompositeKey());
                }

                _logger.Warning($"Auto-canceled blocked job {state.Job.Key} after {cfg.AutoCancelMinutes} minute(s).");
            }
        }
    }

    private sealed class BlockedJobState
    {
        public required PrintJobInfo Job { get; init; }
        public required DateTimeOffset BlockedAtUtc { get; init; }
        public DateTimeOffset LastSeenUtc { get; set; }
        public int FailedUnlockAttempts { get; set; }
        public bool QueuePausedFallback { get; set; }
    }

    private bool TryConsumeSinglePrintBypass(PrintJobKey key)
    {
        var normalized = NormalizePrinterName(key.PrinterName);
        var now = DateTimeOffset.UtcNow;

        lock (_stateLock)
        {
            if (!_singlePrintBypassByPrinter.TryGetValue(normalized, out var expiresAt))
            {
                return false;
            }

            _singlePrintBypassByPrinter.Remove(normalized);

            if (expiresAt <= now)
            {
                return false;
            }
        }

        _logger.Info($"Consumed single-print bypass for {key}.");
        return true;
    }

    private bool IsRecentlyAllowedJob(PrintJobKey key)
    {
        var composite = key.ToCompositeKey();
        var now = DateTimeOffset.UtcNow;

        lock (_stateLock)
        {
            if (!_recentlyAllowedJobs.TryGetValue(composite, out var until))
            {
                return false;
            }

            if (until <= now)
            {
                _recentlyAllowedJobs.Remove(composite);
                return false;
            }
        }

        return true;
    }

    private void MarkJobAsAllowed(PrintJobKey key, TimeSpan ttl)
    {
        var composite = key.ToCompositeKey();
        var until = DateTimeOffset.UtcNow.Add(ttl);

        lock (_stateLock)
        {
            _recentlyAllowedJobs[composite] = until;
        }
    }

    private bool IsQueuePausedFallbackActive(string printerName)
    {
        var normalized = NormalizePrinterName(printerName);

        lock (_stateLock)
        {
            return _queuePausedFallbackPrinters.Contains(normalized);
        }
    }

    private static string NormalizePrinterName(string printerName) => printerName.Trim().ToUpperInvariant();
}
