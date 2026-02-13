using System.Windows;
using PrintGuard.App.ViewModels;

namespace PrintGuard.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly Func<Task<bool>>? _changePasswordAction;
    private readonly Action? _showAboutAction;

    public SettingsWindow(
        SettingsViewModel viewModel,
        Func<Task<bool>>? changePasswordAction = null,
        Action? showAboutAction = null)
    {
        ViewModel = viewModel;
        _changePasswordAction = changePasswordAction;
        _showAboutAction = showAboutAction;
        DataContext = ViewModel;
        InitializeComponent();

        ChangePasswordButton.IsEnabled = _changePasswordAction is not null;
    }

    public SettingsViewModel ViewModel { get; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var errors = ValidateValues();
        if (errors.Count > 0)
        {
            System.Windows.MessageBox.Show(string.Join(Environment.NewLine, errors), "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_changePasswordAction is null)
        {
            return;
        }

        ChangePasswordButton.IsEnabled = false;
        try
        {
            await _changePasswordAction();
        }
        finally
        {
            ChangePasswordButton.IsEnabled = true;
        }
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        _showAboutAction?.Invoke();
    }

    private List<string> ValidateValues()
    {
        var errors = new List<string>();

        if (ViewModel.MaxFailedAttempts < 1)
        {
            errors.Add("Max password failures must be at least 1.");
        }

        if (ViewModel.LockoutSeconds < 5)
        {
            errors.Add("Lockout duration must be at least 5 seconds.");
        }

        if (ViewModel.PollingIntervalMs < 300 || ViewModel.PollingIntervalMs > 800)
        {
            errors.Add("Polling interval must be between 300 and 800 ms.");
        }

        if (ViewModel.PromptUnlockMinutes < 1)
        {
            errors.Add("Prompt unlock minutes must be at least 1.");
        }

        if (ViewModel.EnableAutoCancelPausedJobs && ViewModel.AutoCancelMinutes < 1)
        {
            errors.Add("Auto-cancel minutes must be at least 1 when auto-cancel is enabled.");
        }

        if (ViewModel.CancelJobAfterFailedUnlockAttempts && ViewModel.CancelJobFailedAttemptThreshold < 1)
        {
            errors.Add("Failed attempt threshold must be at least 1 when job-cancel is enabled.");
        }

        return errors;
    }
}

