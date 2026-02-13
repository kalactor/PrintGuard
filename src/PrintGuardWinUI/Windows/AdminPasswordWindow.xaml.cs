using Microsoft.UI.Xaml;
using PrintGuardWinUI.Interop;
using PrintGuardWinUI.Services;

namespace PrintGuardWinUI.Windows;

public sealed partial class AdminPasswordWindow : Window
{
    private readonly TaskCompletionSource<bool> _completion = new();
    private bool _resultSet;

    public AdminPasswordWindow(string actionDescription)
    {
        InitializeComponent();
        WindowInterop.Resize(this, 400, 250);
        Activated += OnActivated;
        Closed += OnClosed;
        ActionText.Text = actionDescription;
    }

    public string EnteredPassword => PasswordInput.Password;

    public Task<bool> ShowDialogAsync()
    {
        Activate();
        return _completion.Task;
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PasswordInput.Password))
        {
            NativeMessageBox.ShowWarning("Password is required.");
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
