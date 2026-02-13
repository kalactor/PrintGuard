using Microsoft.UI.Xaml;
using PrintGuard.Core.Models;
using PrintGuardWinUI.Interop;

namespace PrintGuardWinUI.Windows;

public sealed partial class PasswordPromptWindow : Window
{
    private readonly TaskCompletionSource<bool> _completion = new();
    private bool _resultSet;

    public PasswordPromptWindow(
        PrintJobInfo job,
        int unlockMinutes,
        string? modeMessage = null)
    {
        InitializeComponent();
        WindowInterop.Resize(this, 430, 360);

        Activated += OnActivated;
        Closed += OnClosed;

        PrinterText.Text = job.Key.PrinterName;
        DocumentText.Text = job.DocumentName;
        UnlockDurationCheckBox.Content = $"Unlock printing for {unlockMinutes} minutes";

        if (!string.IsNullOrWhiteSpace(modeMessage))
        {
            ModeText.Text = modeMessage;
            ModeText.Visibility = Visibility.Visible;
        }
    }

    public string EnteredPassword => PasswordInput.Password;

    public bool UnlockForDuration => UnlockDurationCheckBox.IsChecked == true;

    public Task<bool> ShowDialogAsync()
    {
        Activate();
        return _completion.Task;
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PasswordInput.Password))
        {
            ErrorText.Text = "Password is required.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Complete(true);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Complete(false);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        WindowInterop.SetTopMost(this, true);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (!_resultSet)
        {
            _completion.TrySetResult(false);
        }
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
