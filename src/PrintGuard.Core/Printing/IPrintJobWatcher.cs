using PrintGuard.Core.Models;

namespace PrintGuard.Core.Printing;

public interface IPrintJobWatcher : IDisposable
{
    event EventHandler<PrintJobInfo>? JobDetected;
    event EventHandler<Exception>? WatcherError;

    void Start();
    void Stop();
}
