using System.Windows;

namespace PrintGuard.App.Windows;

public partial class AdminPasswordWindow : Window
{
    public AdminPasswordWindow(string actionDescription)
    {
        InitializeComponent();
        ActionText.Text = actionDescription;

        Loaded += (_, _) => PasswordInput.Focus();
    }

    public string EnteredPassword => PasswordInput.Password;

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PasswordInput.Password))
        {
            System.Windows.MessageBox.Show(
                "Password is required.",
                "Print Guard",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
