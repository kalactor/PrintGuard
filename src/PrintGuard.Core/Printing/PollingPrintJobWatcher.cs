using PrintGuard.Core.Configuration;
using PrintGuard.Core.Logging;
using PrintGuard.Core.Models;

namespace PrintGuard.Core.Printing;

public sealed class PollingPrintJobWatcher : IPrintJobWatcher
{
    private readonly IConfigService _configService;
    private readonly IPrintQueueController _queueController;
    private readonly ILoggerService _logger;
    private readonly object _sync = new();

    private readonly Dictionary<string, DateTimeOffset> _seenJobs = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public PollingPrintJobWatcher(
        IConfigService configService,
        IPrintQueueController queueController,
        ILoggerService logger)
    {
        _configService = configService;
        _queueController = queueController;
        _logger = logger;
    }

    public event EventHandler<PrintJobInfo>? JobDetected;
    public event EventHandler<Exception>? WatcherError;

    public void Start()
    {
        lock (_sync)
        {
            if (_loopTask is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
            _logger.Info("Polling print watcher started.");
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_loopTask is null || _cts is null)
            {
                return;
            }

            _cts.Cancel();
        }

        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        lock (_sync)
        {
            _cts?.Dispose();
            _cts = null;
            _loopTask = null;
            _seenJobs.Clear();
        }

        _logger.Info("Polling print watcher stopped.");
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var discovered = new List<PrintJobInfo>();
                var jobs = _queueController.GetCurrentJobs();

                lock (_sync)
                {
                    foreach (var job in jobs)
                    {
                        var composite = job.Key.ToCompositeKey();
                        if (!_seenJobs.ContainsKey(composite))
                        {
                            discovered.Add(job with
                            {
                                Source = "Polling",
                                DetectedAtUtc = now
                            });
                        }

                        _seenJobs[composite] = now;
                    }

                    CleanupSeenNoLock(now);
                }

                foreach (var job in discovered)
                {
                    JobDetected?.Invoke(this, job);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Polling watcher loop error. {ex.Message}");
                WatcherError?.Invoke(this, ex);
            }

            var delay = Math.Clamp(_configService.Current.PollingIntervalMs, 300, 800);

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void CleanupSeenNoLock(DateTimeOffset now)
    {
        var cutoff = now.AddMinutes(-30);
        var stale = _seenJobs
            .Where(x => x.Value < cutoff)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in stale)
        {
            _seenJobs.Remove(key);
        }
    }
}
