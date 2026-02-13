using PrintGuard.Core.Models;

namespace PrintGuard.Core.Logging;

public interface ILoggerService
{
    event EventHandler<LogEntry>? EntryAdded;

    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
    IReadOnlyList<LogEntry> GetRecentEntries(int maxCount = 500);
}
