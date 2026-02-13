using System.Text.Json.Serialization;

namespace PrintGuard.Core.Models;

public sealed class AppConfig
{
    public bool IsProtectionEnabled { get; set; } = true;
    public bool ProtectAllPrinters { get; set; } = true;
    public List<string> ProtectedPrinters { get; set; } = [];
    public bool EnableStartup { get; set; }
    public bool EnableNotifications { get; set; } = true;
    public int PollingIntervalMs { get; set; } = 500;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutSeconds { get; set; } = 60;
    public bool EnableAutoCancelPausedJobs { get; set; }
    public int AutoCancelMinutes { get; set; } = 10;
    public bool FallbackCancelWhenPauseFails { get; set; } = true;
    public bool ReassertPausedJobsOnStartup { get; set; } = true;
    public bool CancelJobAfterFailedUnlockAttempts { get; set; }
    public int CancelJobFailedAttemptThreshold { get; set; } = 3;
    public bool EnablePanicHotkey { get; set; }
    public string PanicHotkey { get; set; } = "Ctrl+Shift+F12";
    public int PromptUnlockMinutes { get; set; } = 15;
    public string PasswordAlgorithm { get; set; } = "PBKDF2-SHA256";
    public int PasswordIterations { get; set; } = 210_000;
    public string? PasswordSaltBase64 { get; set; }
    public string? PasswordHashBase64 { get; set; }

    [JsonIgnore]
    public bool HasPasswordConfigured =>
        !string.IsNullOrWhiteSpace(PasswordSaltBase64) &&
        !string.IsNullOrWhiteSpace(PasswordHashBase64);

    public bool IsPrinterProtected(string printerName)
    {
        if (ProtectAllPrinters)
        {
            return true;
        }

        return ProtectedPrinters.Any(x =>
            string.Equals(x, printerName, StringComparison.OrdinalIgnoreCase));
    }

    public void Normalize()
    {
        ProtectedPrinters ??= [];
        ProtectedPrinters = ProtectedPrinters
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PollingIntervalMs = Math.Clamp(PollingIntervalMs, 500, 1000);
        MaxFailedAttempts = Math.Clamp(MaxFailedAttempts, 1, 20);
        LockoutSeconds = Math.Clamp(LockoutSeconds, 5, 900);
        AutoCancelMinutes = Math.Clamp(AutoCancelMinutes, 1, 240);
        CancelJobFailedAttemptThreshold = Math.Clamp(CancelJobFailedAttemptThreshold, 1, 20);
        PromptUnlockMinutes = Math.Clamp(PromptUnlockMinutes, 1, 240);
        PasswordIterations = Math.Clamp(PasswordIterations, 100_000, 1_000_000);

        if (string.IsNullOrWhiteSpace(PanicHotkey))
        {
            PanicHotkey = "Ctrl+Shift+F12";
        }

        if (string.IsNullOrWhiteSpace(PasswordAlgorithm))
        {
            PasswordAlgorithm = "PBKDF2-SHA256";
        }
    }
}
