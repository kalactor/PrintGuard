using System.Diagnostics;
using System.Windows;
using PrintGuard.App.Services;
using PrintGuard.App.ViewModels;
using PrintGuard.App.Windows;
using PrintGuard.Core.Configuration;
using PrintGuard.Core.Logging;
using PrintGuard.Core.Models;
using PrintGuard.Core.Printing;
using PrintGuard.Core.Security;
using PrintGuard.Core.Startup;
using WinForms = System.Windows.Forms;

namespace PrintGuard.App;

public partial class App : System.Windows.Application
{
    private JsonConfigService? _configService;
    private FileLogger? _logger;
    private PasswordService? _passwordService;
    private PrintQueueController? _queueController;
    private WmiPrintJobWatcher? _wmiWatcher;
    private PollingPrintJobWatcher? _pollingWatcher;
    private PrintFirewallService? _firewallService;
    private StartupManager? _startupManager;
    private TrayManager? _trayManager;
    private GlobalHotkeyService? _hotkeyService;

    private readonly Queue<PrintJobInfo> _promptQueue = new();
    private readonly object _promptLock = new();

    private bool _isPromptLoopRunning;
    private bool _shutdownStarted;

    private LogsWindow? _logsWindow;
    private AboutWindow? _aboutWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _configService = new JsonConfigService();
            await _configService.LoadAsync();

            _logger = new FileLogger(_configService.ConfigDirectory);
            _passwordService = new PasswordService(_configService, _logger);
            _queueController = new PrintQueueController(_logger);
            _wmiWatcher = new WmiPrintJobWatcher(_logger);
            _pollingWatcher = new PollingPrintJobWatcher(_configService, _queueController, _logger);
            _firewallService = new PrintFirewallService(_configService, _queueController, _wmiWatcher, _pollingWatcher, _logger);
            _startupManager = new StartupManager();

            if (!await EnsurePasswordConfiguredAsync())
            {
                await RequestExitAsync();
                return;
            }

            ApplyStartupPreference();
            InitializeTray();
            ConfigureHotkey();

            FirewallService.JobBlocked += OnJobBlocked;
            FirewallService.StateChanged += OnFirewallStateChanged;

            await FirewallService.StartAsync();
            UpdateTrayState();

            if (ConfigService.Current.EnableNotifications)
            {
                TrayService.ShowBalloon("Print Guard", "Print firewall is running.");
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Print Guard failed to start.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Print Guard",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            await RequestExitAsync();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await CleanupAsync();
        base.OnExit(e);
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

    private TrayManager TrayService => _trayManager
        ?? throw new InvalidOperationException("Tray manager not initialized.");

    private async Task<bool> EnsurePasswordConfiguredAsync()
    {
        if (PasswordService.HasPasswordConfigured)
        {
            return true;
        }

        var firstRunWindow = new FirstRunWindow();
        var result = firstRunWindow.ShowDialog();

        if (result != true)
        {
            return false;
        }

        await PasswordService.SetPasswordAsync(firstRunWindow.Password);
        return true;
    }

    private async Task<bool> RequestAdminAuthorizationAsync(string actionDescription)
    {
        while (true)
        {
            var prompt = new AdminPasswordWindow(actionDescription);
            var result = prompt.ShowDialog();

            if (result != true)
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
                System.Windows.MessageBox.Show(
                    $"Too many failed attempts. Try again in {waitSeconds} second(s).",
                    "Print Guard",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            System.Windows.MessageBox.Show(
                $"Incorrect password. Remaining attempts before lockout: {verification.RemainingAttempts}.",
                "Print Guard",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void InitializeTray()
    {
        _trayManager = new TrayManager();
        _trayManager.ToggleProtectionRequested += async (_, _) => await ToggleProtectionAsync();
        _trayManager.UnlockRequested += async (_, minutes) => await UnlockForMinutesAsync(minutes);
        _trayManager.SettingsRequested += async (_, _) => await OpenSettingsAsync();
        _trayManager.LogsRequested += (_, _) => OpenLogsWindow();
        _trayManager.AboutRequested += (_, _) => OpenAboutWindow();
        _trayManager.ExitRequested += async (_, _) => await RequestExitWithAuthorizationAsync();
        _trayManager.Show();
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
            TrayService.ShowBalloon("Print Guard", $"Protection {status}.");
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
            TrayService.ShowBalloon("Print Guard", $"Printing unlocked for {minutes} minute(s).");
        }
    }

    private async Task OpenSettingsAsync()
    {
        var authorized = await RequestAdminAuthorizationAsync("Open Print Guard settings.");
        if (!authorized)
        {
            return;
        }

        var viewModel = SettingsViewModel.FromConfig(ConfigService.Current, QueueController.GetInstalledPrinters());
        var window = new SettingsWindow(viewModel, RequestPasswordChangeAsync, OpenAboutWindow);
        var result = window.ShowDialog();

        if (result != true)
        {
            return;
        }

        await ConfigService.UpdateAsync(cfg => viewModel.ApplyToConfig(cfg));
        ApplyStartupPreference();
        ConfigureHotkey();

        if (FirewallService.IsEnabled != ConfigService.Current.IsProtectionEnabled)
        {
            await FirewallService.SetEnabledAsync(ConfigService.Current.IsProtectionEnabled, "Settings update");
        }

        UpdateTrayState();

        if (ConfigService.Current.EnableNotifications)
        {
            TrayService.ShowBalloon("Print Guard", "Settings saved.");
        }

        Logger.Info("Settings updated from UI.");
    }

    private void OpenLogsWindow()
    {
        if (_logsWindow is not null)
        {
            _logsWindow.Activate();
            return;
        }

        _logsWindow = new LogsWindow(Logger);
        _logsWindow.Closed += (_, _) => _logsWindow = null;
        _logsWindow.Show();
    }

    private void OpenAboutWindow()
    {
        if (_aboutWindow is not null)
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow();
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
    }

    private async Task<bool> RequestPasswordChangeAsync()
    {
        while (true)
        {
            var prompt = new ChangePasswordWindow();
            var result = prompt.ShowDialog();

            if (result != true)
            {
                Logger.Info("Password change canceled.");
                return false;
            }

            var verification = await PasswordService.VerifyAsync(prompt.CurrentPassword);
            if (verification.Succeeded)
            {
                await PasswordService.SetPasswordAsync(prompt.NewPassword);
                Logger.Info("Admin password changed from settings.");
                System.Windows.MessageBox.Show(
                    "Password updated successfully.",
                    "Print Guard",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return true;
            }

            if (verification.LockedOut)
            {
                var waitSeconds = Math.Ceiling(verification.LockoutRemaining.TotalSeconds);
                System.Windows.MessageBox.Show(
                    $"Too many failed attempts. Try again in {waitSeconds} second(s).",
                    "Print Guard",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            System.Windows.MessageBox.Show(
                $"Current password is incorrect. Remaining attempts before lockout: {verification.RemainingAttempts}.",
                "Print Guard",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnJobBlocked(object? sender, PrintJobInfo job)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnJobBlocked(sender, job));
            return;
        }

        Logger.Info($"Password prompt queued for {job.Key} (source={job.Source}, paused={job.IsPaused}).");

        if (ConfigService.Current.EnableNotifications)
        {
            try
            {
                TrayService.ShowBalloon(
                    "Printing blocked",
                    $"{job.DocumentName} on {job.Key.PrinterName}",
                    WinForms.ToolTipIcon.Warning);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Unable to show tray notification for blocked job {job.Key}. {ex.Message}");
            }
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

        Dispatcher.InvokeAsync(StartPromptLoop);
    }

    private async void StartPromptLoop()
    {
        await ProcessPromptQueueAsync();
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
            var prompt = new PasswordPromptWindow(
                job,
                ConfigService.Current.PromptUnlockMinutes,
                fallbackModeMessage);
            var result = prompt.ShowDialog();

            if (result != true)
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
                    TrayService.ShowBalloon("Print Guard", message);
                }

                return;
            }

            if (verification.LockedOut)
            {
                var waitSeconds = Math.Ceiling(verification.LockoutRemaining.TotalSeconds);
                System.Windows.MessageBox.Show(
                    $"Too many failed attempts. Try again in {waitSeconds} second(s).",
                    "Print Guard",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                continue;
            }

            if (!fallbackPrompt)
            {
                var failedOutcome = FirewallService.RegisterFailedUnlockAttempt(job.Key);
                if (failedOutcome.JobCanceled)
                {
                    System.Windows.MessageBox.Show(
                        "Too many incorrect password attempts. The print job was canceled.",
                        "Print Guard",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    if (ConfigService.Current.EnableNotifications)
                    {
                        TrayService.ShowBalloon(
                            "Print job canceled",
                            "The blocked job was canceled after repeated incorrect passwords.",
                            WinForms.ToolTipIcon.Warning);
                    }

                    return;
                }
            }

            System.Windows.MessageBox.Show(
                $"Incorrect password. Remaining attempts before lockout: {verification.RemainingAttempts}.",
                "Print Guard",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnFirewallStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateTrayState);
    }

    private void UpdateTrayState()
    {
        if (_trayManager is null || _firewallService is null)
        {
            return;
        }

        _trayManager.UpdateState(_firewallService.IsEnabled, _firewallService.UnlockUntilUtc);
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

    private void ConfigureHotkey()
    {
        if (!ConfigService.Current.EnablePanicHotkey)
        {
            _hotkeyService?.Dispose();
            _hotkeyService = null;
            return;
        }

        _hotkeyService ??= new GlobalHotkeyService();
        _hotkeyService.HotkeyPressed -= OnPanicHotkeyPressed;
        _hotkeyService.HotkeyPressed += OnPanicHotkeyPressed;

        if (!_hotkeyService.Register(ConfigService.Current.PanicHotkey, out var errorMessage))
        {
            Logger.Warning($"Panic hotkey registration failed: {errorMessage}");

            if (ConfigService.Current.EnableNotifications)
            {
                TrayService.ShowBalloon("Print Guard", errorMessage, WinForms.ToolTipIcon.Warning);
            }
        }
    }

    private async void OnPanicHotkeyPressed(object? sender, EventArgs e)
    {
        await FirewallService.SetEnabledAsync(false, "Panic hotkey");
        UpdateTrayState();

        if (ConfigService.Current.EnableNotifications)
        {
            TrayService.ShowBalloon("Print Guard", "Protection disabled by panic hotkey.", WinForms.ToolTipIcon.Warning);
        }
    }

    private async Task RequestExitAsync()
    {
        await CleanupAsync();
        Shutdown();
    }

    private async Task RequestExitWithAuthorizationAsync()
    {
        var authorized = await RequestAdminAuthorizationAsync("Exit Print Guard.");
        if (!authorized)
        {
            return;
        }

        await RequestExitAsync();
    }

    private async Task CleanupAsync()
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
        catch
        {
        }

        _hotkeyService?.Dispose();
        _trayManager?.Dispose();

        if (_logsWindow is not null)
        {
            _logsWindow.Close();
            _logsWindow = null;
        }

        if (_aboutWindow is not null)
        {
            _aboutWindow.Close();
            _aboutWindow = null;
        }
    }
}


