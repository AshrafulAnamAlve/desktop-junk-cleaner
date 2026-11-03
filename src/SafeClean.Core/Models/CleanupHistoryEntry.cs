namespace SafeClean.Core.Models;

/// <summary>
/// One line of cleanup history. Counts and sizes only — <b>never file names or paths</b> (privacy).
/// </summary>
/// <param name="StartedAt">When the cleanup started.</param>
/// <param name="What">"Temp folder" or "SafeClean logs".</param>
/// <param name="Deleted">Files deleted.</param>
/// <param name="Skipped">Files skipped (kept).</param>
/// <param name="Failed">Files that failed (kept).</param>
/// <param name="BytesFreed">Space freed by deleted files only.</param>
/// <param name="DurationSeconds">How long it took.</param>
/// <param name="Refusal">Why the whole cleanup was refused (nothing deleted), or null.</param>
public sealed record CleanupHistoryEntry(
    DateTimeOffset StartedAt,
    string What,
    int Deleted,
    int Skipped,
    int Failed,
    long BytesFreed,
    double DurationSeconds,
    CleanupRefusal? Refusal)
{
    public const string TempFolder = "Temp folder";
    public const string SafeCleanLogs = "SafeClean logs";

    /// <summary>Summarises a real cleanup's report. Dry Run reports aren't recorded.</summary>
    public static CleanupHistoryEntry From(CleanupReport report, string what)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new CleanupHistoryEntry(
            report.StartedAt,
            what,
            report.DeletedCount,
            report.SkippedCount,
            report.FailedCount,
            report.BytesFreed,
            Math.Max(0, (report.CompletedAt - report.StartedAt).TotalSeconds),
            report.Refusal);
    }
}

/// <summary>The history read from disk (newest first) and how many unreadable lines were skipped.</summary>
public sealed record HistoryLoadResult(IReadOnlyList<CleanupHistoryEntry> Entries, int SkippedLines);
