namespace PrintGuard.Core.Models;

public sealed record PrintJobInfo(
    PrintJobKey Key,
    string DocumentName,
    string Owner,
    DateTimeOffset DetectedAtUtc,
    bool IsPaused,
    string Source);
