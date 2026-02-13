using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using PrintGuard.Core.Logging;
using PrintGuard.Core.Models;
using PrintGuardWinUI.Interop;
using PrintGuardWinUI.ViewModels;

namespace PrintGuardWinUI.Windows;

public sealed partial class LogsWindow : Window
{
    private readonly ILoggerService _logger;

    public LogsWindow(ILoggerService logger)
    {
        _logger = logger;
        Entries = new ObservableCollection<LogRowViewModel>(
            _logger.GetRecentEntries(500).Select(LogRowViewModel.FromLogEntry));

        InitializeComponent();
        WindowInterop.Resize(this, 860, 480);

        LogsListView.ItemsSource = Entries;
        Activated += OnActivated;
        Closed += OnClosed;

        _logger.EntryAdded += OnEntryAdded;
    }

    public ObservableCollection<LogRowViewModel> Entries { get; }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        WindowInterop.BringToFront(this);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _logger.EntryAdded -= OnEntryAdded;
    }

    private void OnEntryAdded(object? sender, LogEntry entry)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                Entries.Add(LogRowViewModel.FromLogEntry(entry));
                while (Entries.Count > 1000)
                {
                    Entries.RemoveAt(0);
                }
            }))
        {
            // Ignore if window dispatcher is unavailable during shutdown.
        }
    }
}
