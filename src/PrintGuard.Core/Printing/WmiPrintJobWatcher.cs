using System.Management;
using PrintGuard.Core.Logging;
using PrintGuard.Core.Models;

namespace PrintGuard.Core.Printing;

public sealed class WmiPrintJobWatcher : IPrintJobWatcher
{
    private readonly ILoggerService _logger;
    private readonly object _sync = new();

    private ManagementEventWatcher? _watcher;
    private bool _isStarted;

    public WmiPrintJobWatcher(ILoggerService logger)
    {
        _logger = logger;
    }

    public event EventHandler<PrintJobInfo>? JobDetected;
    public event EventHandler<Exception>? WatcherError;

    public void Start()
    {
        lock (_sync)
        {
            if (_isStarted)
            {
                return;
            }

            try
            {
                var scope = new ManagementScope("\\\\.\\root\\CIMV2");
                scope.Connect();

                var query = new WqlEventQuery(
                    "__InstanceCreationEvent",
                    TimeSpan.FromSeconds(1),
                    "TargetInstance ISA 'Win32_PrintJob'");

                _watcher = new ManagementEventWatcher(scope, query);
                _watcher.EventArrived += OnEventArrived;
                _watcher.Start();
                _isStarted = true;

                _logger.Info("WMI print watcher started.");
            }
            catch (Exception ex)
            {
                _logger.Warning($"WMI watcher failed to start. {ex.Message}");
                WatcherError?.Invoke(this, ex);
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_isStarted)
            {
                return;
            }

            if (_watcher is not null)
            {
                _watcher.EventArrived -= OnEventArrived;

                try
                {
                    _watcher.Stop();
                }
                catch
                {
                }

                _watcher.Dispose();
                _watcher = null;
            }

            _isStarted = false;
            _logger.Info("WMI print watcher stopped.");
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var target = e.NewEvent["TargetInstance"] as ManagementBaseObject;
            if (target is null)
            {
                return;
            }

            var jobIdValue = target["JobId"];
            if (jobIdValue is null)
            {
                return;
            }

            var jobId = Convert.ToInt32(jobIdValue);
            var rawName = target["Name"]?.ToString() ?? string.Empty;
            var printerName = ExtractPrinterName(rawName);

            if (string.IsNullOrWhiteSpace(printerName))
            {
                return;
            }

            var document = target["Document"]?.ToString() ?? "(Unnamed document)";
            var owner = target["Owner"]?.ToString() ?? "Unknown";

            JobDetected?.Invoke(this, new PrintJobInfo(
                new PrintJobKey(printerName, jobId),
                document,
                owner,
                DateTimeOffset.UtcNow,
                IsPaused: false,
                Source: "WMI"));
        }
        catch (Exception ex)
        {
            _logger.Warning($"WMI watcher event parse failed. {ex.Message}");
            WatcherError?.Invoke(this, ex);
        }
    }

    private static string ExtractPrinterName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        var separator = rawName.LastIndexOf(',');
        if (separator <= 0)
        {
            return rawName.Trim();
        }

        return rawName[..separator].Trim();
    }
}
