namespace PrintGuard.Core.Models;

public sealed record PasswordVerificationResult(
    bool Succeeded,
    bool LockedOut,
    TimeSpan LockoutRemaining,
    int FailedAttempts,
    int RemainingAttempts);
