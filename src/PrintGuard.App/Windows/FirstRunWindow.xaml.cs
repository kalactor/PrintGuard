using System.Windows;

namespace PrintGuard.App.Windows;

public partial class FirstRunWindow : Window
{
    public FirstRunWindow()
    {
        InitializeComponent();
    }

    public string Password { get; private set; } = string.Empty;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordInput.Password;
        var confirm = ConfirmPasswordInput.Password;

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            ShowError("Password must be at least 6 characters.");
            return;
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            ShowError("Passwords do not match.");
            return;
        }

        Password = password;
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
