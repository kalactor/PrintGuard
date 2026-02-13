namespace PrintGuard.Core.Models;

public enum LogLevel
{
    Info,
    Warning,
    Error
}

public sealed record LogEntry(DateTimeOffset TimestampUtc, LogLevel Level, string Message);
