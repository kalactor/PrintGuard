using System.Runtime.InteropServices;

namespace PrintGuardWinUI.Services;

public static class NativeMessageBox
{
    private const uint MbOk = 0x00000000;
    private const uint MbIconError = 0x00000010;
    private const uint MbIconWarning = 0x00000030;
    private const uint MbIconInformation = 0x00000040;

    public static void ShowInfo(string message, string title = "PrintGuard") =>
        Show(message, title, MbIconInformation);

    public static void ShowWarning(string message, string title = "PrintGuard") =>
        Show(message, title, MbIconWarning);

    public static void ShowError(string message, string title = "PrintGuard") =>
        Show(message, title, MbIconError);

    private static void Show(string message, string title, uint icon)
    {
        MessageBoxW(IntPtr.Zero, message, title, MbOk | icon);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}
