using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.Graphics;

namespace PrintGuardWinUI.Interop;

internal static class WindowInterop
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwShowNormal = 1;
    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndNoTopMost = new(-2);

    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;

    public static IntPtr GetWindowHandle(Window window) => WindowNative.GetWindowHandle(window);

    public static void Hide(Window window)
    {
        var hwnd = GetWindowHandle(window);
        ShowWindow(hwnd, SwHide);
    }

    public static void Show(Window window)
    {
        var hwnd = GetWindowHandle(window);
        ShowWindow(hwnd, SwShow);
    }

    public static void BringToFront(Window window)
    {
        var hwnd = GetWindowHandle(window);
        ShowWindow(hwnd, SwShowNormal);
        SetForegroundWindow(hwnd);
    }

    public static void Resize(Window window, int width, int height)
    {
        var hwnd = GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(width, height));
    }

    public static void SetTopMost(Window window, bool topMost)
    {
        var hwnd = GetWindowHandle(window);
        var hWndInsertAfter = topMost ? HwndTopMost : HwndNoTopMost;
        SetWindowPos(hwnd, hWndInsertAfter, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
