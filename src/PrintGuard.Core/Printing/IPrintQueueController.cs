using PrintGuard.Core.Models;

namespace PrintGuard.Core.Printing;

public interface IPrintQueueController
{
    IReadOnlyList<string> GetInstalledPrinters();
    IReadOnlyList<PrintJobInfo> GetCurrentJobs();
    bool TryPauseJob(PrintJobKey key, out string message);
    bool TryResumeJob(PrintJobKey key, out string message);
    bool TryCancelJob(PrintJobKey key, out string message);
    bool TryPauseQueue(string printerName, out string message);
    bool TryResumeQueue(string printerName, out string message);
    bool JobExists(PrintJobKey key);
}
