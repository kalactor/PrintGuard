using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace PrintGuard.App.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4512;

    private HwndSource? _source;
    private bool _isRegistered;

    public event EventHandler? HotkeyPressed;

    public bool Register(string gesture, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (!TryParseGesture(gesture, out var modifiers, out var key, out errorMessage))
        {
            return false;
        }

        EnsureSource();
        Unregister();

        var nativeModifiers = (uint)ToNativeModifiers(modifiers);
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);

        _isRegistered = RegisterHotKey(_source!.Handle, HotkeyId, nativeModifiers, virtualKey);
        if (!_isRegistered)
        {
            errorMessage = "Unable to register panic hotkey. It may already be in use.";
            return false;
        }

        return true;
    }

    public void Unregister()
    {
        if (_isRegistered && _source is not null)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _isRegistered = false;
        }
    }

    public void Dispose()
    {
        Unregister();

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        GC.SuppressFinalize(this);
    }

    private void EnsureSource()
    {
        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("PrintGuardHotkeyHost")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static bool TryParseGesture(string gesture, out ModifierKeys modifiers, out Key key, out string error)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            error = "Hotkey cannot be empty.";
            return false;
        }

        var tokens = gesture
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length < 2)
        {
            error = "Use a format like Ctrl+Shift+F12.";
            return false;
        }

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (TryParseModifier(token, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (!Enum.TryParse<Key>(token, ignoreCase: true, out key))
            {
                error = $"Unsupported key token '{token}'.";
                return false;
            }
        }

        if (modifiers == ModifierKeys.None)
        {
            error = "At least one modifier is required (Ctrl, Alt, Shift, Win).";
            return false;
        }

        if (key == Key.None || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            error = "A non-modifier key is required.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryParseModifier(string token, out ModifierKeys modifier)
    {
        modifier = token.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => ModifierKeys.Control,
            "ALT" => ModifierKeys.Alt,
            "SHIFT" => ModifierKeys.Shift,
            "WIN" or "WINDOWS" => ModifierKeys.Windows,
            _ => ModifierKeys.None
        };

        return modifier != ModifierKeys.None;
    }

    private static int ToNativeModifiers(ModifierKeys modifiers)
    {
        var result = 0;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= 0x0001;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= 0x0002;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= 0x0004;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= 0x0008;
        }

        return result;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
