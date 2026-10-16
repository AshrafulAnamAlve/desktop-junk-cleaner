namespace SafeClean.Core.Models;

/// <summary>Totals for one scan. All byte counts are sizes at scan time, so recoverable space is an estimate.</summary>
public sealed record ScanSummary(
    int EligibleCount,
    long EligibleBytes,
    int NeedsReviewCount,
    long NeedsReviewBytes,
    int SkippedCount,
    long SkippedBytes)
{
    public int TotalCount => EligibleCount + NeedsReviewCount + SkippedCount;

    /// <summary>
    /// The most that could be freed if every selectable file were selected and still unchanged at cleanup time.
    /// It is an estimate, never a promise: files can change, be in use, or be skipped during cleanup.
    /// </summary>
    public long EstimatedRecoverableBytes => EligibleBytes + NeedsReviewBytes;

    public static ScanSummary From(IEnumerable<ScanCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        int eligible = 0, review = 0, skipped = 0;
        long eligibleBytes = 0, reviewBytes = 0, skippedBytes = 0;
        foreach (var candidate in candidates)
        {
            switch (candidate.Decision.Status)
            {
                case CandidateStatus.Eligible:
                    eligible++;
                    eligibleBytes += candidate.Size;
                    break;
                case CandidateStatus.NeedsReview:
                    review++;
                    reviewBytes += candidate.Size;
                    break;
                default:
                    skipped++;
                    skippedBytes += candidate.Size;
                    break;
            }
        }

        return new ScanSummary(eligible, eligibleBytes, review, reviewBytes, skipped, skippedBytes);
    }
}
