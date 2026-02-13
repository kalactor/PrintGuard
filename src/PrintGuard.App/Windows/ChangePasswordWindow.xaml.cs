using System.Windows;

namespace PrintGuard.App.Windows;

public partial class ChangePasswordWindow : Window
{
    public ChangePasswordWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => CurrentPasswordInput.Focus();
    }

    public string CurrentPassword => CurrentPasswordInput.Password;

    public string NewPassword { get; private set; } = string.Empty;

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var currentPassword = CurrentPasswordInput.Password;
        var newPassword = NewPasswordInput.Password;
        var confirmPassword = ConfirmNewPasswordInput.Password;

        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            ShowError("Current password is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            ShowError("New password must be at least 6 characters.");
            return;
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            ShowError("New passwords do not match.");
            return;
        }

        NewPassword = newPassword;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
