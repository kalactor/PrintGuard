using System.Collections.ObjectModel;
using System.Windows;
using PrintGuard.Core.Logging;
using PrintGuard.Core.Models;

namespace PrintGuard.App.Windows;

public partial class LogsWindow : Window
{
    private readonly ILoggerService _logger;

    public LogsWindow(ILoggerService logger)
    {
        _logger = logger;
        Entries = new ObservableCollection<LogEntry>(_logger.GetRecentEntries(500));

        InitializeComponent();
        DataContext = this;

        _logger.EntryAdded += OnLogEntryAdded;
    }

    public ObservableCollection<LogEntry> Entries { get; }

    protected override void OnClosed(EventArgs e)
    {
        _logger.EntryAdded -= OnLogEntryAdded;
        base.OnClosed(e);
    }

    private void OnLogEntryAdded(object? sender, LogEntry entry)
    {
        Dispatcher.Invoke(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > 1_000)
            {
                Entries.RemoveAt(0);
            }
        });
    }
}
