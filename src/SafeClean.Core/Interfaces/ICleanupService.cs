using SafeClean.Core.Models;

namespace SafeClean.Core.Interfaces;

/// <summary>
/// Removes selected files from the approved Temp folder, one at a time, after checking each one again
/// (docs/SECURITY.md §5). In <see cref="CleanupMode.DryRun"/> it only re-checks and reports what it would do.
/// </summary>
public interface ICleanupService
{
    /// <summary>
    /// Runs a cleanup in the background. Only IDs from <paramref name="session"/> can ever be acted on.
    /// Cancellation never throws: it stops between files, and the report lists every file already handled.
    /// </summary>
    Task<CleanupReport> CleanAsync(
        ScanSession session,
        IReadOnlyCollection<CandidateId> selected,
        CleanupMode mode,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken);
}
