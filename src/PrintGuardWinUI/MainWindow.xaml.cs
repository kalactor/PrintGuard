using PrintGuardWinUI.Interop;

namespace PrintGuardWinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowInterop.Resize(this, 460, 240);
    }
}
