using SafeClean.Core.Models;

namespace SafeClean.Core.Interfaces;

/// <summary>Lists and classifies files in the approved Temp folder. Strictly read-only: never changes anything on disk.</summary>
public interface ITempScanner
{
    /// <summary>Runs a scan in the background. Throws <see cref="OperationCanceledException"/> if cancelled.</summary>
    Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken);
}
