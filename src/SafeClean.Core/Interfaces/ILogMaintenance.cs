using SafeClean.Core.Models;

namespace SafeClean.Core.Interfaces;

/// <summary>
/// Removing SafeClean's <b>own</b> old log files, on request only. It reuses the whole cleanup pipeline
/// (approved root → scan → rules → re-check → exclusive-handle delete), limited to SafeClean's log folder and to
/// files named exactly <c>safeclean-YYYYMMDD.log</c>. It is never run automatically.
/// </summary>
public interface ILogMaintenance
{
    /// <summary>Read-only: finds SafeClean log files older than <paramref name="retentionDays"/>.</summary>
    Task<LogCleanupPlan> FindOldLogsAsync(int retentionDays, CancellationToken cancellationToken);

    /// <summary>Deletes exactly the files in <paramref name="plan"/>, each re-checked first.</summary>
    Task<CleanupReport> DeleteOldLogsAsync(LogCleanupPlan plan, CancellationToken cancellationToken);
}

/// <summary>What "Delete old logs" would remove.</summary>
/// <param name="Session">The read-only scan of the log folder, or null if the folder was refused or doesn't exist.</param>
/// <param name="Ids">The log files that may be removed.</param>
/// <param name="TotalBytes">Their combined size at scan time.</param>
/// <param name="RetentionDays">Only files older than this many days are included.</param>
/// <param name="Problem">Why nothing can be offered, or null.</param>
public sealed record LogCleanupPlan(ScanSession? Session, IReadOnlyList<CandidateId> Ids, long TotalBytes, int RetentionDays, string? Problem)
{
    public int FileCount => Ids.Count;
}
