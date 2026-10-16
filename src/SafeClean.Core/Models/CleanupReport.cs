using System.Diagnostics.CodeAnalysis;

namespace SafeClean.Core.Models;

/// <summary>
/// The outcome of one cleanup request: either a refusal (nothing touched), or one entry per selected ID.
/// Space freed counts <b>only</b> files that were actually deleted.
/// </summary>
public sealed class CleanupReport
{
    private CleanupReport(
        CleanupMode mode,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CleanupRefusal? refusal,
        IReadOnlyList<RootProblem> rootProblems,
        IReadOnlyList<CleanupEntry> entries,
        bool wasCancelled,
        bool stoppedByError)
    {
        // A preview can never report a deletion, and a real cleanup reports what it did, not what it would do.
        if (mode != CleanupMode.Real && entries.Any(e => e.Outcome == CleanupOutcome.Deleted))
        {
            throw new ArgumentException("Only a real cleanup can report deleted files.", nameof(entries));
        }

        if (mode == CleanupMode.Real && entries.Any(e => e.Outcome == CleanupOutcome.WouldDelete))
        {
            throw new ArgumentException("A real cleanup can't report 'would delete'.", nameof(entries));
        }

        Mode = mode;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Refusal = refusal;
        RootProblems = rootProblems;
        Entries = entries;
        WasCancelled = wasCancelled;
        StoppedByError = stoppedByError;

        foreach (var entry in entries)
        {
            switch (entry.Outcome)
            {
                case CleanupOutcome.Deleted:
                    DeletedCount++;
                    BytesFreed += entry.Bytes;
                    break;
                case CleanupOutcome.WouldDelete:
                    WouldDeleteCount++;
                    BytesWouldFree += entry.Bytes;
                    break;
                case CleanupOutcome.Failed:
                    FailedCount++;
                    break;
                default:
                    SkippedCount++;
                    break;
            }
        }
    }

    public CleanupMode Mode { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CompletedAt { get; }

    /// <summary>Why the whole request was refused, or null if it ran.</summary>
    public CleanupRefusal? Refusal { get; }

    [MemberNotNullWhen(true, nameof(Refusal))]
    public bool WasRefused => Refusal is not null;

    /// <summary>For <see cref="CleanupRefusal.RootRefused"/>: what is wrong with the Temp folder now.</summary>
    public IReadOnlyList<RootProblem> RootProblems { get; }

    /// <summary>One entry per distinct selected ID, in the order they were selected.</summary>
    public IReadOnlyList<CleanupEntry> Entries { get; }

    /// <summary>The user cancelled; files not reached are listed as <see cref="CleanupOutcome.SkippedCancelled"/>.</summary>
    public bool WasCancelled { get; }

    /// <summary>An unexpected error stopped the cleanup; files not reached are listed as <see cref="CleanupOutcome.SkippedCancelled"/>.</summary>
    public bool StoppedByError { get; }

    public int DeletedCount { get; }

    public int WouldDeleteCount { get; }

    public int SkippedCount { get; }

    public int FailedCount { get; }

    /// <summary>Sum of the sizes of files that were actually deleted. Nothing else ever counts.</summary>
    public long BytesFreed { get; }

    /// <summary>Dry Run only: sum of the sizes of files that would be deleted.</summary>
    public long BytesWouldFree { get; }

    public IEnumerable<CleanupEntry> Deleted => Entries.Where(e => e.Outcome == CleanupOutcome.Deleted);

    public IEnumerable<CleanupEntry> Failed => Entries.Where(e => e.Outcome == CleanupOutcome.Failed);

    public IEnumerable<CleanupEntry> Skipped => Entries.Where(e => e.Outcome is not (CleanupOutcome.Deleted or CleanupOutcome.WouldDelete or CleanupOutcome.Failed));

    public static CleanupReport Refused(CleanupMode mode, DateTimeOffset at, CleanupRefusal refusal, IReadOnlyList<RootProblem>? rootProblems = null) =>
        new(mode, at, at, refusal, rootProblems ?? [], [], wasCancelled: false, stoppedByError: false);

    public static CleanupReport Completed(
        CleanupMode mode,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IReadOnlyList<CleanupEntry> entries,
        bool wasCancelled,
        bool stoppedByError)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new(mode, startedAt, completedAt, null, [], entries, wasCancelled, stoppedByError);
    }
}
