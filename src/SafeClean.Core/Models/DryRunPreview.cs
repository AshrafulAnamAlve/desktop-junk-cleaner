namespace SafeClean.Core.Models;

/// <summary>What a cleanup of the selected files would attempt. Producing it changes nothing on disk.</summary>
/// <param name="FileCount">Selected files that could be attempted.</param>
/// <param name="EstimatedBytes">Their combined size at scan time — an estimate, not a promise.</param>
/// <param name="NeedsReviewCount">How many of them are programs, scripts or installers.</param>
/// <param name="FilesByCategory">File count per display category.</param>
/// <param name="IgnoredCount">Selected IDs that were ignored because they are unknown or not selectable.</param>
public sealed record DryRunPreview(
    int FileCount,
    long EstimatedBytes,
    int NeedsReviewCount,
    IReadOnlyDictionary<CandidateCategory, int> FilesByCategory,
    int IgnoredCount);
