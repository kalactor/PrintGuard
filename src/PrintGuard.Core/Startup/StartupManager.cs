using Microsoft.Win32;

namespace PrintGuard.Core.Startup;

public sealed class StartupManager
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "PrintGuard";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
        var value = key?.GetValue(AppValueName)?.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunRegistryPath, writable: true);

        if (enabled)
        {
            key.SetValue(AppValueName, $"\"{executablePath}\"");
            return;
        }

        if (key.GetValue(AppValueName) is not null)
        {
            key.DeleteValue(AppValueName, throwOnMissingValue: false);
        }
    }
}
