namespace SafeClean.Core.Models;

/// <summary>The complete, read-only result of one scan of the approved root.</summary>
public sealed class ScanSession
{
    public ScanSession(
        Guid id,
        ApprovedRoot root,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IReadOnlyList<ScanCandidate> candidates,
        int foldersScanned,
        int vanishedDuringScan,
        bool wasTruncated)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(candidates);

        Id = id;
        Root = root;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Candidates = candidates;
        FoldersScanned = foldersScanned;
        VanishedDuringScan = vanishedDuringScan;
        WasTruncated = wasTruncated;
        Summary = ScanSummary.From(candidates);
    }

    public Guid Id { get; }

    public ApprovedRoot Root { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CompletedAt { get; }

    public IReadOnlyList<ScanCandidate> Candidates { get; }

    public int FoldersScanned { get; }

    /// <summary>Entries that disappeared between being listed and being inspected (normal for a busy Temp folder).</summary>
    public int VanishedDuringScan { get; }

    /// <summary>True if the scan stopped at <see cref="Policies.SafetyDefaults.MaxScanEntries"/> entries.</summary>
    public bool WasTruncated { get; }

    public ScanSummary Summary { get; }
}
