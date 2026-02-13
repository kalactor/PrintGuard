using System.Printing;
using System.Management;
using System.Runtime.InteropServices;
using PrintGuard.Core.Logging;
using PrintGuard.Core.Models;

namespace PrintGuard.Core.Printing;

public sealed class PrintQueueController : IPrintQueueController
{
    private const uint JobControlPause = 1;
    private const uint JobControlResume = 2;
    private const uint JobControlCancel = 3;

    private readonly ILoggerService _logger;

    public PrintQueueController(ILoggerService logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> GetInstalledPrinters()
    {
        var names = new List<string>();

        try
        {
            using var server = new LocalPrintServer();
            foreach (PrintQueue queue in server.GetPrintQueues(GetQueueTypes()))
            {
                try
                {
                    names.Add(queue.Name);
                }
                finally
                {
                    queue.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Unable to enumerate installed printers.", ex);
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<PrintJobInfo> GetCurrentJobs()
    {
        var results = new List<PrintJobInfo>();

        try
        {
            using var server = new LocalPrintServer();
            foreach (PrintQueue queue in server.GetPrintQueues(GetQueueTypes()))
            {
                try
                {
                    queue.Refresh();
                    foreach (var job in queue.GetPrintJobInfoCollection())
                    {
                        job.Refresh();

                        if ((job.JobStatus & PrintJobStatus.Deleted) == PrintJobStatus.Deleted)
                        {
                            continue;
                        }

                        var isPaused = (job.JobStatus & PrintJobStatus.Paused) == PrintJobStatus.Paused;
                        results.Add(new PrintJobInfo(
                            new PrintJobKey(queue.Name, job.JobIdentifier),
                            string.IsNullOrWhiteSpace(job.Name) ? "(Unnamed document)" : job.Name,
                            string.IsNullOrWhiteSpace(job.Submitter) ? "Unknown" : job.Submitter,
                            DateTimeOffset.UtcNow,
                            isPaused,
                            "QueueScan"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to scan queue '{queue.Name}'. {ex.Message}");
                }
                finally
                {
                    queue.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Unable to enumerate print jobs.", ex);
        }

        return results;
    }

    public bool TryPauseJob(PrintJobKey key, out string message)
    {
        if (TryApplyToJob(key, job =>
        {
            job.Refresh();
            var isPaused = (job.JobStatus & PrintJobStatus.Paused) == PrintJobStatus.Paused;
            if (!isPaused)
            {
                job.Pause();
                job.Commit();
            }
        }, out message))
        {
            return true;
        }

        var jobError = message;
        if (TrySetJobCommand(key, JobControlPause, out var setJobError))
        {
            message = "OK (SetJob fallback)";
            return true;
        }

        message = $"JobPause='{jobError}', SetJobPause='{setJobError}'";
        return false;
    }

    public bool TryResumeJob(PrintJobKey key, out string message)
    {
        if (TryApplyToJob(key, job =>
        {
            job.Refresh();
            var isPaused = (job.JobStatus & PrintJobStatus.Paused) == PrintJobStatus.Paused;
            if (isPaused)
            {
                job.Resume();
                job.Commit();
            }
        }, out message))
        {
            return true;
        }

        var jobError = message;
        if (TrySetJobCommand(key, JobControlResume, out var setJobError))
        {
            message = "OK (SetJob fallback)";
            return true;
        }

        message = $"JobResume='{jobError}', SetJobResume='{setJobError}'";
        return false;
    }

    public bool TryCancelJob(PrintJobKey key, out string message)
    {
        if (TryApplyToJob(key, job =>
        {
            job.Cancel();
            job.Commit();
        }, out message))
        {
            return true;
        }

        var jobError = message;
        if (TrySetJobCommand(key, JobControlCancel, out var setJobError))
        {
            message = "OK (SetJob fallback)";
            return true;
        }

        message = $"JobCancel='{jobError}', SetJobCancel='{setJobError}'";
        return false;
    }

    public bool TryPauseQueue(string printerName, out string message)
    {
        if (TryApplyToQueue(printerName, queue =>
        {
            queue.Refresh();
            if (!queue.IsPaused)
            {
                queue.Pause();
                queue.Commit();
            }
        }, out message))
        {
            return true;
        }

        var queueError = message;
        if (TryInvokeWmiPrinterMethod(printerName, "Pause", out var wmiError))
        {
            message = "OK (WMI fallback)";
            return true;
        }

        message = $"QueuePause='{queueError}', WmiPause='{wmiError}'";
        return false;
    }

    public bool TryResumeQueue(string printerName, out string message)
    {
        if (TryApplyToQueue(printerName, queue =>
        {
            queue.Refresh();
            if (queue.IsPaused)
            {
                queue.Resume();
                queue.Commit();
            }
        }, out message))
        {
            return true;
        }

        var queueError = message;
        if (TryInvokeWmiPrinterMethod(printerName, "Resume", out var wmiError))
        {
            message = "OK (WMI fallback)";
            return true;
        }

        message = $"QueueResume='{queueError}', WmiResume='{wmiError}'";
        return false;
    }

    public bool JobExists(PrintJobKey key)
    {
        return TryApplyToJob(key, _ => { }, out _);
    }

    private bool TryApplyToJob(PrintJobKey key, Action<PrintSystemJobInfo> action, out string message)
    {
        try
        {
            using var server = new LocalPrintServer();
            using var queue = FindQueue(server, key.PrinterName);

            if (queue is null)
            {
                message = "Printer queue not found.";
                return false;
            }

            queue.Refresh();
            var job = queue.GetPrintJobInfoCollection().FirstOrDefault(x => x.JobIdentifier == key.JobId);

            if (job is null)
            {
                message = "Print job no longer exists.";
                return false;
            }

            action(job);
            message = "OK";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private bool TryApplyToQueue(string printerName, Action<PrintQueue> action, out string message)
    {
        try
        {
            using var server = new LocalPrintServer();
            using var queue = FindQueue(server, printerName);

            if (queue is null)
            {
                message = "Printer queue not found.";
                return false;
            }

            action(queue);
            message = "OK";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private static bool TryInvokeWmiPrinterMethod(string printerName, string methodName, out string message)
    {
        try
        {
            var escapedName = EscapeWqlString(printerName);
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                $"SELECT * FROM Win32_Printer WHERE Name = '{escapedName}'");

            using var results = searcher.Get();
            var printer = results.OfType<ManagementObject>().FirstOrDefault();
            if (printer is null)
            {
                message = "WMI printer object not found.";
                return false;
            }

            var returnValue = printer.InvokeMethod(methodName, null, null);
            var code = returnValue is null ? 0u : Convert.ToUInt32(returnValue);
            if (code == 0)
            {
                message = "OK";
                return true;
            }

            message = $"WMI method returned {code}.";
            return false;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private static string EscapeWqlString(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");

    private static bool TrySetJobCommand(PrintJobKey key, uint command, out string message)
    {
        if (!OpenPrinter(key.PrinterName, out var printerHandle, IntPtr.Zero))
        {
            message = $"OpenPrinter failed with Win32 error {Marshal.GetLastWin32Error()}.";
            return false;
        }

        try
        {
            var succeeded = SetJob(printerHandle, (uint)key.JobId, 0, IntPtr.Zero, command);
            if (succeeded)
            {
                message = "OK";
                return true;
            }

            message = $"SetJob failed with Win32 error {Marshal.GetLastWin32Error()}.";
            return false;
        }
        finally
        {
            ClosePrinter(printerHandle);
        }
    }

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool SetJob(IntPtr hPrinter, uint jobId, uint level, IntPtr pJob, uint command);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    private static PrintQueue? FindQueue(LocalPrintServer server, string queueName)
    {
        PrintQueue? found = null;

        foreach (PrintQueue queue in server.GetPrintQueues(GetQueueTypes()))
        {
            if (string.Equals(queue.Name, queueName, StringComparison.OrdinalIgnoreCase))
            {
                found = queue;
                break;
            }

            queue.Dispose();
        }

        return found;
    }

    private static EnumeratedPrintQueueTypes[] GetQueueTypes() =>
    [
        EnumeratedPrintQueueTypes.Local,
        EnumeratedPrintQueueTypes.Connections
    ];
}
