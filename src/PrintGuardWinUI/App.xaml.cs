using System.Diagnostics;
using Microsoft.UI.Dispatching;
using PrintGuard.Core.Configuration;
using PrintGuard.Core.Logging;
using PrintGuard.Core.Models;
using PrintGuard.Core.Printing;
using PrintGuard.Core.Security;
using PrintGuard.Core.Startup;
using PrintGuardWinUI.Interop;
using PrintGuardWinUI.Services;
using PrintGuardWinUI.ViewModels;
using PrintGuardWinUI.Windows;

namespace PrintGuardWinUI;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    private JsonConfigService? _configService;
    private FileLogger? _logger;
    private PasswordService? _passwordService;
    private PrintQueueController? _queueController;
    private WmiPrintJobWatcher? _wmiWatcher;
    private PollingPrintJobWatcher? _pollingWatcher;
    private PrintFirewallService? _firewallService;
    private StartupManager? _startupManager;
    private TrayIconService? _trayService;
    private DispatcherQueue? _uiDispatcherQueue;

    private readonly Queue<PrintJobInfo> _promptQueue = new();
    private readonly object _promptLock = new();
    private bool _isPromptLoopRunning;
    private bool _shutdownStarted;

    private LogsWindow? _logsWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _mainWindow = new MainWindow();
            _mainWindow.Activate();
            WindowInterop.Hide(_mainWindow);

            var configRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PrintGuardWinUI");

            _configService = new JsonConfigService(configRoot);
            await _configService.LoadAsync();

            _logger = new FileLogger(configRoot);
            _passwordService = new PasswordService(_configService, _logger);
            _queueController = new PrintQueueController(_logger);
            _wmiWatcher = new WmiPrintJobWatcher(_logger);
            _pollingWatcher = new PollingPrintJobWatcher(_configService, _queueController, _logger);
            _firewallService = new PrintFirewallService(_configService, _queueController, _wmiWatcher, _pollingWatcher, _logger);
            _startupManager = new StartupManager();

            if (!await EnsurePasswordConfiguredAsync())
            {
                await ShutdownAppAsync();
                return;
            }

            ApplyStartupPreference();
            InitializeTray();

            FirewallService.JobBlocked += OnJobBlocked;
            FirewallService.StateChanged += OnFirewallStateChanged;

            await FirewallService.StartAsync();
            UpdateTrayState();

            if (ConfigService.Current.EnableNotifications)
            {
                TrayService.ShowBalloon("PrintGuard", "Print firewall is running.");
            }
        }
        catch (Exception ex)
        {
            NativeMessageBox.ShowError(
                $"PrintGuard failed to start.{Environment.NewLine}{Environment.NewLine}{ex.Message}");

            await ShutdownAppAsync();
        }
    }

    private JsonConfigService ConfigService => _configService
        ?? throw new InvalidOperationException("Config service not initialized.");

    private FileLogger Logger => _logger
        ?? throw new InvalidOperationException("Logger not initialized.");

    private PasswordService PasswordService => _passwordService
        ?? throw new InvalidOperationException("Password service not initialized.");

    private PrintQueueController QueueController => _queueController
        ?? throw new InvalidOperationException("Queue controller not initialized.");

    private PrintFirewallService FirewallService => _firewallService
        ?? throw new InvalidOperationException("Firewall service not initialized.");

    private StartupManager StartupManager => _startupManager
        ?? throw new InvalidOperationException("Startup manager not initialized.");

    private TrayIconService TrayService => _trayService
        ?? throw new InvalidOperationException("Tray service not initialized.");

    private DispatcherQueue UiDispatcherQueue => _uiDispatcherQueue
        ?? throw new InvalidOperationException("UI dispatcher queue not initialized.");

    private async Task<bool> EnsurePasswordConfiguredAsync()
    {
        if (PasswordService.HasPasswordConfigured)
        {
            return true;
        }

        var firstRunWindow = new FirstRunWindow();
        var result = await firstRunWindow.ShowDialogAsync();

        if (!result)
        {
            return false;
        }

        await PasswordService.SetPasswordAsync(firstRunWindow.Password);
        return true;
    }

    private void InitializeTray()
    {
        _trayService = new TrayIconService();

        _trayService.ToggleProtectionRequested += (_, _) => EnqueueUiAction(ToggleProtectionAsync, "toggle protection");
        _trayService.UnlockRequested += (_, minutes) => EnqueueUiAction(() => UnlockForMinutesAsync(minutes), "unlock printing");
        _trayService.SettingsRequested += (_, _) => EnqueueUiAction(OpenSettingsAsync, "open settings");
        _trayService.LogsRequested += (_, _) => EnqueueUiAction(OpenLogsWindowAsync, "open logs");
        _trayService.ExitRequested += (_, _) => EnqueueUiAction(ShutdownAppAsync, "exit");

        _trayService.Show();
    }

    private void EnqueueUiAction(Func<Task> action, string operation)
    {
        UiDispatcherQueue.TryEnqueue(() => _ = RunUiActionAsync(action, operation));
    }

    private async Task RunUiActionAsync(Func<Task> action, string operation)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to {operation}.", ex);
            NativeMessageBox.ShowError(
                $"PrintGuard failed to {operation}.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
        }
    }

    private async Task ToggleProtectionAsync()
    {
        if (FirewallService.IsEnabled)
        {
            var authorized = await RequestAdminAuthorizationAsync("Disable print protection.");
            if (!authorized)
            {
                return;
            }
        }

        await FirewallService.SetEnabledAsync(!FirewallService.IsEnabled, "Tray toggle");
        UpdateTrayState();

        if (ConfigService.Current.EnableNotifications)
        {
            var status = FirewallService.IsEnabled ? "enabled" : "disabled";
            TrayService.ShowBalloon("PrintGuard", $"Protection {status}.");
        }
    }

    private async Task UnlockForMinutesAsync(int minutes)
    {
        var authorized = await RequestAdminAuthorizationAsync($"Unlock printing for {minutes} minute(s).");
        if (!authorized)
        {
            return;
        }

        await FirewallService.UnlockForAsync(TimeSpan.FromMinutes(minutes), "Tray quick action");
        UpdateTrayState();

        if (ConfigService.Current.EnableNotifications)
        {
            TrayService.ShowBalloon("PrintGuard", $"Printing unlocked for {minutes} minute(s).");
        }
    }

    private async Task OpenSettingsAsync()
    {
        var authorized = await RequestAdminAuthorizationAsync("Open PrintGuard settings.");
        if (!authorized)
        {
            return;
        }

        var viewModel = SettingsViewModel.FromConfig(ConfigService.Current, QueueController.GetInstalledPrinters());
        var settingsWindow = new SettingsWindow(viewModel);
        var result = await settingsWindow.ShowDialogAsync();

        if (!result)
        {
            return;
        }

        await ConfigService.UpdateAsync(cfg => viewModel.ApplyToConfig(cfg));
        ApplyStartupPreference();

        if (FirewallService.IsEnabled != ConfigService.Current.IsProtectionEnabled)
        {
            await FirewallService.SetEnabledAsync(ConfigService.Current.IsProtectionEnabled, "Settings update");
        }

        UpdateTrayState();

        if (ConfigService.Current.EnableNotifications)
        {
            TrayService.ShowBalloon("PrintGuard", "Settings saved.");
        }

        Logger.Info("Settings updated from UI.");
    }

    private Task OpenLogsWindowAsync()
    {
        if (_logsWindow is not null)
        {
            WindowInterop.BringToFront(_logsWindow);
            return Task.CompletedTask;
        }

        _logsWindow = new LogsWindow(Logger);
        _logsWindow.Closed += (_, _) => _logsWindow = null;
        _logsWindow.Activate();
        return Task.CompletedTask;
    }

    private async Task<bool> RequestAdminAuthorizationAsync(string actionDescription)
    {
        while (true)
        {
            var prompt = new AdminPasswordWindow(actionDescription);
            var result = await prompt.ShowDialogAsync();

            if (!result)
            {
                Logger.Info($"Admin authorization canceled for action: {actionDescription}");
                return false;
            }

            var verification = await PasswordService.VerifyAsync(prompt.EnteredPassword);
            if (verification.Succeeded)
            {
                return true;
            }

            if (verification.LockedOut)
            {
                var waitSeconds = Math.Ceiling(verification.LockoutRemaining.TotalSeconds);
                NativeMessageBox.ShowWarning(
                    $"Too many failed attempts. Try again in {waitSeconds} second(s).");
                return false;
            }

            NativeMessageBox.ShowWarning(
                $"Incorrect password. Remaining attempts before lockout: {verification.RemainingAttempts}.");
        }
    }

    private void OnJobBlocked(object? sender, PrintJobInfo job)
    {
        UiDispatcherQueue.TryEnqueue(() =>
        {
            Logger.Info($"Password prompt queued for {job.Key} (source={job.Source}, paused={job.IsPaused}).");

            if (ConfigService.Current.EnableNotifications)
            {
                TrayService.ShowBalloon(
                    "Printing blocked",
                    $"{job.DocumentName} on {job.Key.PrinterName}",
                    TrayNotificationLevel.Warning);
            }

            lock (_promptLock)
            {
                _promptQueue.Enqueue(job);
                if (_isPromptLoopRunning)
                {
                    return;
                }

                _isPromptLoopRunning = true;
            }

            _ = ProcessPromptQueueAsync();
        });
    }

    private async Task ProcessPromptQueueAsync()
    {
        while (true)
        {
            PrintJobInfo currentJob;

            lock (_promptLock)
            {
                if (_promptQueue.Count == 0)
                {
                    _isPromptLoopRunning = false;
                    return;
                }

                currentJob = _promptQueue.Dequeue();
            }

            if (currentJob.IsPaused && !FirewallService.IsJobBlocked(currentJob.Key))
            {
                continue;
            }

            await ShowPromptForJobAsync(currentJob);
        }
    }

    private async Task ShowPromptForJobAsync(PrintJobInfo job)
    {
        var fallbackPrompt = !job.IsPaused;
        var fallbackCanceledJob = job.Source.Contains("CanceledFallback", StringComparison.OrdinalIgnoreCase);
        var fallbackModeMessage = fallbackPrompt
            ? fallbackCanceledJob
                ? "This printer queue could not hold the job, so the job was canceled for safety. " +
                  "Keep the 15-minute option unchecked to allow only your next reprint once."
                : "This printer driver does not support secure pause for this job. " +
                  "Keep the 15-minute option unchecked to allow only the next print once."
            : null;

        while (FirewallService.IsEnabled && (fallbackPrompt || FirewallService.IsJobBlocked(job.Key)))
        {
            var prompt = new PasswordPromptWindow(job, ConfigService.Current.PromptUnlockMinutes, fallbackModeMessage);
            var result = await prompt.ShowDialogAsync();

            if (!result)
            {
                Logger.Info($"Unlock prompt canceled for {job.Key}.");
                return;
            }

            var verification = await PasswordService.VerifyAsync(prompt.EnteredPassword);
            if (verification.Succeeded)
            {
                if (fallbackPrompt)
                {
                    if (prompt.UnlockForDuration)
                    {
                        await FirewallService.UnlockForAsync(
                            TimeSpan.FromMinutes(ConfigService.Current.PromptUnlockMinutes),
                            "Fallback timed authorization");
                    }
                    else
                    {
                        FirewallService.ArmSinglePrintBypass(
                            job.Key.PrinterName,
                            "Fallback single-print authorization");
                    }
                }
                else if (prompt.UnlockForDuration)
                {
                    await FirewallService.UnlockForAsync(
                        TimeSpan.FromMinutes(ConfigService.Current.PromptUnlockMinutes),
                        "Prompt checkbox");
                }
                else
                {
                    await FirewallService.UnlockJobAsync(job.Key, "Password prompt");
                }

                UpdateTrayState();

                if (ConfigService.Current.EnableNotifications)
                {
                    var message = fallbackPrompt
                        ? prompt.UnlockForDuration
                            ? $"Printing unlocked for {ConfigService.Current.PromptUnlockMinutes} minute(s)." +
                              (fallbackCanceledJob ? " Reprint your document." : string.Empty)
                            : fallbackCanceledJob
                                ? "Single reprint authorized. Send print once."
                                : "Single next print authorized."
                        : "Print job released.";
                    TrayService.ShowBalloon("PrintGuard", message);
                }

                return;
            }

            if (verification.LockedOut)
            {
                var waitSeconds = Math.Ceiling(verification.LockoutRemaining.TotalSeconds);
                NativeMessageBox.ShowWarning(
                    $"Too many failed attempts. Try again in {waitSeconds} second(s).");

                continue;
            }

            if (!fallbackPrompt)
            {
                var failedOutcome = FirewallService.RegisterFailedUnlockAttempt(job.Key);
                if (failedOutcome.JobCanceled)
                {
                    NativeMessageBox.ShowWarning(
                        "Too many incorrect password attempts. The print job was canceled.");

                    if (ConfigService.Current.EnableNotifications)
                    {
                        TrayService.ShowBalloon(
                            "Print job canceled",
                            "The blocked job was canceled after repeated incorrect passwords.",
                            TrayNotificationLevel.Warning);
                    }

                    return;
                }
            }

            NativeMessageBox.ShowWarning(
                $"Incorrect password. Remaining attempts before lockout: {verification.RemainingAttempts}.");
        }
    }

    private void OnFirewallStateChanged(object? sender, EventArgs e)
    {
        UiDispatcherQueue.TryEnqueue(UpdateTrayState);
    }

    private void UpdateTrayState()
    {
        if (_trayService is null || _firewallService is null)
        {
            return;
        }

        _trayService.UpdateState(_firewallService.IsEnabled, _firewallService.UnlockUntilUtc);
    }

    private void ApplyStartupPreference()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            StartupManager.SetEnabled(ConfigService.Current.EnableStartup, executablePath);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Unable to apply startup setting. {ex.Message}");
        }
    }

    private async Task ShutdownAppAsync()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;

        try
        {
            if (_firewallService is not null)
            {
                _firewallService.JobBlocked -= OnJobBlocked;
                _firewallService.StateChanged -= OnFirewallStateChanged;
                await _firewallService.StopAsync();
                _firewallService.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Error while stopping firewall service.", ex);
        }

        _trayService?.Dispose();

        if (_logsWindow is not null)
        {
            _logsWindow.Close();
            _logsWindow = null;
        }

        _mainWindow?.Close();
        Exit();
    }
}
