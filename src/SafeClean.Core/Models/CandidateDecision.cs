namespace SafeClean.Core.Models;

/// <summary>The policy's verdict on one file.</summary>
/// <param name="Status">Eligible, needs review, or skipped.</param>
/// <param name="Reasons">Every rule the file failed (empty unless skipped).</param>
/// <param name="Category">Display grouping.</param>
/// <param name="RelativePath">Path below the approved root, or null if the file is not inside it.</param>
public sealed record CandidateDecision(
    CandidateStatus Status,
    IReadOnlyList<SkipReason> Reasons,
    CandidateCategory Category,
    string? RelativePath)
{
    /// <summary>Only eligible and needs-review files may ever be selected.</summary>
    public bool IsSelectable => Status is CandidateStatus.Eligible or CandidateStatus.NeedsReview;
}
