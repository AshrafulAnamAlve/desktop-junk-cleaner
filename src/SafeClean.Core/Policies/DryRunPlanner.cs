using SafeClean.Core.Models;

namespace SafeClean.Core.Policies;

/// <summary>
/// Turns a selection into a Dry Run preview. It never trusts the selection: IDs that don't belong to the
/// scan session, and entries that aren't selectable, are ignored and counted.
/// </summary>
public static class DryRunPlanner
{
    public static DryRunPreview Create(ScanSession session, IEnumerable<CandidateId> selectedIds)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(selectedIds);

        var candidates = session.Candidates.ToDictionary(c => c.Id);
        var byCategory = new Dictionary<CandidateCategory, int>();
        int files = 0, review = 0, ignored = 0;
        long bytes = 0;

        foreach (var id in selectedIds.Distinct())
        {
            if (!candidates.TryGetValue(id, out var candidate) || !candidate.Decision.IsSelectable)
            {
                ignored++;
                continue;
            }

            files++;
            bytes += candidate.Size;
            if (candidate.Decision.Status == CandidateStatus.NeedsReview)
            {
                review++;
            }

            byCategory[candidate.Decision.Category] = byCategory.GetValueOrDefault(candidate.Decision.Category) + 1;
        }

        return new DryRunPreview(files, bytes, review, byCategory, ignored);
    }
}
