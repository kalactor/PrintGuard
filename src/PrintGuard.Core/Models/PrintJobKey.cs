namespace PrintGuard.Core.Models;

public readonly record struct PrintJobKey(string PrinterName, int JobId)
{
    public string ToCompositeKey() =>
        $"{PrinterName.Trim().ToUpperInvariant()}|{JobId}";

    public override string ToString() => $"{PrinterName} (Job {JobId})";
}
