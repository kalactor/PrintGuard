namespace PrintGuard.Core.Printing;

public sealed record FailedUnlockOutcome(bool JobCanceled, int FailedAttempts, int? CancelThreshold);
