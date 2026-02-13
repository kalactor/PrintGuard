using System.Collections.Concurrent;
using PrintGuard.Core.Models;

namespace PrintGuard.Core.Logging;

public sealed class FileLogger : ILoggerService
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly object _fileLock = new();
    private readonly int _maxEntryCount;

    public FileLogger(string? baseDirectory = null, int maxEntryCount = 2_000)
    {
        var root = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintGuard")
            : baseDirectory;

        var logsDir = Path.Combine(root, "logs");
        Directory.CreateDirectory(logsDir);

        LogPath = Path.Combine(logsDir, "printguard.log");
        _maxEntryCount = Math.Max(100, maxEntryCount);
    }

    public string LogPath { get; }

    public event EventHandler<LogEntry>? EntryAdded;

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warning(string message) => Write(LogLevel.Warning, message);

    public void Error(string message, Exception? exception = null)
    {
        var suffix = exception is null ? string.Empty : $" | {exception.GetType().Name}: {exception.Message}";
        Write(LogLevel.Error, message + suffix);
    }

    public IReadOnlyList<LogEntry> GetRecentEntries(int maxCount = 500)
    {
        var clamped = Math.Max(1, maxCount);
        return _entries
            .Reverse()
            .Take(clamped)
            .OrderBy(x => x.TimestampUtc)
            .ToList();
    }

    private void Write(LogLevel level, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var entry = new LogEntry(DateTimeOffset.UtcNow, level, message.Trim());
        _entries.Enqueue(entry);

        while (_entries.Count > _maxEntryCount && _entries.TryDequeue(out _))
        {
        }

        try
        {
            lock (_fileLock)
            {
                File.AppendAllText(LogPath, FormatEntry(entry) + Environment.NewLine);
            }
        }
        catch
        {
            // Keep application logic running even if log writing fails.
        }

        EntryAdded?.Invoke(this, entry);
    }

    private static string FormatEntry(LogEntry entry) =>
        $"[{entry.TimestampUtc:yyyy-MM-dd HH:mm:ss}] [{entry.Level}] {entry.Message}";
}
