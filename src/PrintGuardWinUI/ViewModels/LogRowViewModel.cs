using PrintGuard.Core.Models;

namespace PrintGuardWinUI.ViewModels;

public sealed class LogRowViewModel
{
    public LogRowViewModel(string timestamp, string level, string message)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message;
    }

    public string Timestamp { get; }
    public string Level { get; }
    public string Message { get; }

    public static LogRowViewModel FromLogEntry(LogEntry entry) =>
        new(entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), entry.Level.ToString(), entry.Message);
}
