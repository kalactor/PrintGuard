using System.Collections.ObjectModel;
using PrintGuard.Core.Models;

namespace PrintGuard.App.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private bool _isProtectionEnabled;
    private bool _protectAllPrinters;
    private bool _enableStartup;
    private bool _enableNotifications;
    private bool _enableAutoCancelPausedJobs;
    private int _autoCancelMinutes;
    private bool _fallbackCancelWhenPauseFails;
    private int _maxFailedAttempts;
    private int _lockoutSeconds;
    private bool _cancelJobAfterFailedUnlockAttempts;
    private int _cancelJobFailedAttemptThreshold;
    private int _pollingIntervalMs;
    private bool _reassertPausedJobsOnStartup;
    private bool _enablePanicHotkey;
    private string _panicHotkey = "Ctrl+Shift+F12";
    private int _promptUnlockMinutes;

    public ObservableCollection<PrinterSelectionItemViewModel> Printers { get; } = [];

    public bool IsProtectionEnabled
    {
        get => _isProtectionEnabled;
        set => SetProperty(ref _isProtectionEnabled, value);
    }

    public bool ProtectAllPrinters
    {
        get => _protectAllPrinters;
        set
        {
            if (SetProperty(ref _protectAllPrinters, value))
            {
                OnPropertyChanged(nameof(IsPrinterSelectionEnabled));
            }
        }
    }

    public bool IsPrinterSelectionEnabled => !ProtectAllPrinters;

    public bool EnableStartup
    {
        get => _enableStartup;
        set => SetProperty(ref _enableStartup, value);
    }

    public bool EnableNotifications
    {
        get => _enableNotifications;
        set => SetProperty(ref _enableNotifications, value);
    }

    public bool EnableAutoCancelPausedJobs
    {
        get => _enableAutoCancelPausedJobs;
        set => SetProperty(ref _enableAutoCancelPausedJobs, value);
    }

    public int AutoCancelMinutes
    {
        get => _autoCancelMinutes;
        set => SetProperty(ref _autoCancelMinutes, value);
    }

    public bool FallbackCancelWhenPauseFails
    {
        get => _fallbackCancelWhenPauseFails;
        set => SetProperty(ref _fallbackCancelWhenPauseFails, value);
    }

    public int MaxFailedAttempts
    {
        get => _maxFailedAttempts;
        set => SetProperty(ref _maxFailedAttempts, value);
    }

    public int LockoutSeconds
    {
        get => _lockoutSeconds;
        set => SetProperty(ref _lockoutSeconds, value);
    }

    public bool CancelJobAfterFailedUnlockAttempts
    {
        get => _cancelJobAfterFailedUnlockAttempts;
        set => SetProperty(ref _cancelJobAfterFailedUnlockAttempts, value);
    }

    public int CancelJobFailedAttemptThreshold
    {
        get => _cancelJobFailedAttemptThreshold;
        set => SetProperty(ref _cancelJobFailedAttemptThreshold, value);
    }

    public int PollingIntervalMs
    {
        get => _pollingIntervalMs;
        set => SetProperty(ref _pollingIntervalMs, value);
    }

    public bool ReassertPausedJobsOnStartup
    {
        get => _reassertPausedJobsOnStartup;
        set => SetProperty(ref _reassertPausedJobsOnStartup, value);
    }

    public bool EnablePanicHotkey
    {
        get => _enablePanicHotkey;
        set => SetProperty(ref _enablePanicHotkey, value);
    }

    public string PanicHotkey
    {
        get => _panicHotkey;
        set => SetProperty(ref _panicHotkey, value);
    }

    public int PromptUnlockMinutes
    {
        get => _promptUnlockMinutes;
        set => SetProperty(ref _promptUnlockMinutes, value);
    }

    public static SettingsViewModel FromConfig(AppConfig config, IReadOnlyList<string> installedPrinters)
    {
        var vm = new SettingsViewModel
        {
            IsProtectionEnabled = config.IsProtectionEnabled,
            ProtectAllPrinters = config.ProtectAllPrinters,
            EnableStartup = config.EnableStartup,
            EnableNotifications = config.EnableNotifications,
            EnableAutoCancelPausedJobs = config.EnableAutoCancelPausedJobs,
            AutoCancelMinutes = config.AutoCancelMinutes,
            FallbackCancelWhenPauseFails = config.FallbackCancelWhenPauseFails,
            MaxFailedAttempts = config.MaxFailedAttempts,
            LockoutSeconds = config.LockoutSeconds,
            CancelJobAfterFailedUnlockAttempts = config.CancelJobAfterFailedUnlockAttempts,
            CancelJobFailedAttemptThreshold = config.CancelJobFailedAttemptThreshold,
            PollingIntervalMs = config.PollingIntervalMs,
            ReassertPausedJobsOnStartup = config.ReassertPausedJobsOnStartup,
            EnablePanicHotkey = config.EnablePanicHotkey,
            PanicHotkey = config.PanicHotkey,
            PromptUnlockMinutes = config.PromptUnlockMinutes
        };

        var selected = new HashSet<string>(config.ProtectedPrinters, StringComparer.OrdinalIgnoreCase);
        var names = installedPrinters
            .Concat(config.ProtectedPrinters)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var printerName in names)
        {
            vm.Printers.Add(new PrinterSelectionItemViewModel(printerName, selected.Contains(printerName)));
        }

        return vm;
    }

    public void ApplyToConfig(AppConfig config)
    {
        config.IsProtectionEnabled = IsProtectionEnabled;
        config.ProtectAllPrinters = ProtectAllPrinters;
        config.ProtectedPrinters = Printers
            .Where(x => x.IsSelected)
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        config.EnableStartup = EnableStartup;
        config.EnableNotifications = EnableNotifications;
        config.EnableAutoCancelPausedJobs = EnableAutoCancelPausedJobs;
        config.AutoCancelMinutes = AutoCancelMinutes;
        config.FallbackCancelWhenPauseFails = FallbackCancelWhenPauseFails;
        config.MaxFailedAttempts = MaxFailedAttempts;
        config.LockoutSeconds = LockoutSeconds;
        config.CancelJobAfterFailedUnlockAttempts = CancelJobAfterFailedUnlockAttempts;
        config.CancelJobFailedAttemptThreshold = CancelJobFailedAttemptThreshold;
        config.PollingIntervalMs = PollingIntervalMs;
        config.ReassertPausedJobsOnStartup = ReassertPausedJobsOnStartup;
        config.EnablePanicHotkey = EnablePanicHotkey;
        config.PanicHotkey = PanicHotkey;
        config.PromptUnlockMinutes = PromptUnlockMinutes;
        config.Normalize();
    }
}
