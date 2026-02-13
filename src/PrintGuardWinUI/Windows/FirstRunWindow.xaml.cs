using Microsoft.UI.Xaml;
using PrintGuardWinUI.Interop;

namespace PrintGuardWinUI.Windows;

public sealed partial class FirstRunWindow : Window
{
    private readonly TaskCompletionSource<bool> _completion = new();
    private bool _resultSet;

    public FirstRunWindow()
    {
        InitializeComponent();
        WindowInterop.Resize(this, 420, 330);
        Activated += OnActivated;
        Closed += OnClosed;
    }

    public string Password { get; private set; } = string.Empty;

    public Task<bool> ShowDialogAsync()
    {
        Activate();
        return _completion.Task;
    }

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
        Complete(true);
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
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

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
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
