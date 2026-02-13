using Microsoft.UI.Xaml;
using PrintGuardWinUI.Interop;
using PrintGuardWinUI.ViewModels;
using PrintGuardWinUI.Services;

namespace PrintGuardWinUI.Windows;

public sealed partial class SettingsWindow : Window
{
    private readonly TaskCompletionSource<bool> _completion = new();
    private bool _resultSet;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        WindowInterop.Resize(this, 620, 760);

        Closed += OnClosed;
        Activated += OnActivated;

        PrintersListView.ItemsSource = ViewModel.Printers;
        LoadFromViewModel();
    }

    public SettingsViewModel ViewModel { get; }

    public Task<bool> ShowDialogAsync()
    {
        Activate();
        return _completion.Task;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryApplyToViewModel(out var errors))
        {
            NativeMessageBox.ShowWarning(string.Join(Environment.NewLine, errors), "Invalid settings");
            return;
        }

        Complete(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Complete(false);
    }

    private void ProtectAllPrintersCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        PrintersListView.IsEnabled = ProtectAllPrintersCheckBox.IsChecked != true;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        WindowInterop.BringToFront(this);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (!_resultSet)
        {
            _completion.TrySetResult(false);
        }
    }

    private void LoadFromViewModel()
    {
        ProtectionEnabledCheckBox.IsChecked = ViewModel.IsProtectionEnabled;
        ProtectAllPrintersCheckBox.IsChecked = ViewModel.ProtectAllPrinters;
        EnableStartupCheckBox.IsChecked = ViewModel.EnableStartup;
        EnableNotificationsCheckBox.IsChecked = ViewModel.EnableNotifications;

        EnableAutoCancelControls(ViewModel.EnableAutoCancelPausedJobs);

        AutoCancelPausedCheckBox.IsChecked = ViewModel.EnableAutoCancelPausedJobs;
        AutoCancelMinutesBox.Text = ViewModel.AutoCancelMinutes.ToString();
        FallbackCancelCheckBox.IsChecked = ViewModel.FallbackCancelWhenPauseFails;
        MaxFailedAttemptsBox.Text = ViewModel.MaxFailedAttempts.ToString();
        LockoutSecondsBox.Text = ViewModel.LockoutSeconds.ToString();
        CancelAfterFailedCheckBox.IsChecked = ViewModel.CancelJobAfterFailedUnlockAttempts;
        CancelFailedThresholdBox.Text = ViewModel.CancelJobFailedAttemptThreshold.ToString();
        PollingIntervalBox.Text = ViewModel.PollingIntervalMs.ToString();
        ReassertPausedCheckBox.IsChecked = ViewModel.ReassertPausedJobsOnStartup;
        PromptUnlockMinutesBox.Text = ViewModel.PromptUnlockMinutes.ToString();

        PrintersListView.IsEnabled = !ViewModel.ProtectAllPrinters;

        AutoCancelPausedCheckBox.Checked += (_, _) => EnableAutoCancelControls(true);
        AutoCancelPausedCheckBox.Unchecked += (_, _) => EnableAutoCancelControls(false);
    }

    private void EnableAutoCancelControls(bool enabled)
    {
        AutoCancelMinutesBox.IsEnabled = enabled;
    }

    private bool TryApplyToViewModel(out List<string> errors)
    {
        errors = [];

        var isValid =
            TryParseInt(MaxFailedAttemptsBox.Text, 1, 20, "Max failures must be between 1 and 20.", out var maxFailed, errors) &
            TryParseInt(LockoutSecondsBox.Text, 5, 900, "Lockout seconds must be between 5 and 900.", out var lockoutSeconds, errors) &
            TryParseInt(CancelFailedThresholdBox.Text, 1, 20, "Cancel threshold must be between 1 and 20.", out var cancelThreshold, errors) &
            TryParseInt(PollingIntervalBox.Text, 500, 1000, "Polling interval must be between 500 and 1000 ms.", out var pollingMs, errors) &
            TryParseInt(PromptUnlockMinutesBox.Text, 1, 240, "Prompt unlock minutes must be between 1 and 240.", out var promptMinutes, errors) &
            TryParseInt(AutoCancelMinutesBox.Text, 1, 240, "Auto-cancel minutes must be between 1 and 240.", out var autoCancelMinutes, errors);

        if (!isValid)
        {
            return false;
        }

        ViewModel.IsProtectionEnabled = ProtectionEnabledCheckBox.IsChecked == true;
        ViewModel.ProtectAllPrinters = ProtectAllPrintersCheckBox.IsChecked == true;
        ViewModel.EnableStartup = EnableStartupCheckBox.IsChecked == true;
        ViewModel.EnableNotifications = EnableNotificationsCheckBox.IsChecked == true;
        ViewModel.EnableAutoCancelPausedJobs = AutoCancelPausedCheckBox.IsChecked == true;
        ViewModel.AutoCancelMinutes = autoCancelMinutes;
        ViewModel.FallbackCancelWhenPauseFails = FallbackCancelCheckBox.IsChecked == true;
        ViewModel.MaxFailedAttempts = maxFailed;
        ViewModel.LockoutSeconds = lockoutSeconds;
        ViewModel.CancelJobAfterFailedUnlockAttempts = CancelAfterFailedCheckBox.IsChecked == true;
        ViewModel.CancelJobFailedAttemptThreshold = cancelThreshold;
        ViewModel.PollingIntervalMs = pollingMs;
        ViewModel.ReassertPausedJobsOnStartup = ReassertPausedCheckBox.IsChecked == true;
        ViewModel.PromptUnlockMinutes = promptMinutes;
        return true;
    }

    private static bool TryParseInt(string text, int min, int max, string error, out int value, List<string> errors)
    {
        value = 0;

        if (!int.TryParse(text, out var parsed) || parsed < min || parsed > max)
        {
            errors.Add(error);
            return false;
        }

        value = parsed;
        return true;
    }

    private void Complete(bool result)
    {
        if (_resultSet)
        {
            return;
        }

        _resultSet = true;
        _completion.TrySetResult(result);
        Close();
    }
}
