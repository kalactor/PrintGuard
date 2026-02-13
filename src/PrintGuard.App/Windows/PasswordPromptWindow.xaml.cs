using System.Windows;
using PrintGuard.Core.Models;

namespace PrintGuard.App.Windows;

public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow(
        PrintJobInfo job,
        int unlockMinutes,
        string? modeMessage = null)
    {
        InitializeComponent();

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

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PasswordInput.Password))
        {
            ErrorText.Text = "Password is required.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
