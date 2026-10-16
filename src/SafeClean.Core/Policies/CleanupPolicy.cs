using SafeClean.Core.Models;

namespace SafeClean.Core.Policies;

/// <summary>
/// The checks every cleanup must pass (docs/SECURITY.md §5). Pure logic: the cleanup service gathers the
/// facts, these methods decide. Each method returns null for "continue", or the reason to stop.
/// </summary>
public static class CleanupPolicy
{
    /// <summary>Before anything else: mode, elevation (R8) and scan age. Any answer refuses the whole request.</summary>
    public static CleanupRefusal? CheckRequest(CleanupMode mode, bool isElevated, ScanSession session, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (mode is not (CleanupMode.DryRun or CleanupMode.Real))
        {
            return CleanupRefusal.InvalidMode;
        }

        if (isElevated)
        {
            return CleanupRefusal.Elevated;
        }

        // A scan "from the future" means the clock moved, so its age can't be trusted either.
        if (now < session.StartedAt || now - session.StartedAt > SafetyDefaults.MaxScanSessionAge)
        {
            return CleanupRefusal.SessionExpired;
        }

        return null;
    }

    /// <summary>The Temp folder, checked again right now (R1–R7), must be approved and be the very folder that was scanned.</summary>
    public static CleanupRefusal? CheckRoot(ScanSession session, RootDecision current)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(current);

        if (!current.IsApproved)
        {
            return CleanupRefusal.RootRefused;
        }

        if (!string.Equals(current.Root.CanonicalPath, session.Root.CanonicalPath, StringComparison.Ordinal)
            || !current.Root.Identity.Matches(session.Root.Identity))
        {
            return CleanupRefusal.RootChanged;
        }

        return null;
    }

    /// <summary>
    /// D1 + D2: the candidate as the scan recorded it must have been selectable, and must still pass every
    /// candidate rule (path, root, protected locations, exclusions, age) judged again now.
    /// </summary>
    public static CleanupOutcome? CheckRecorded(ScanCandidate candidate, CandidatePolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);

        // D1
        if (!candidate.Decision.IsSelectable || candidate.Facts is null
            || !string.Equals(candidate.Path, candidate.Facts.Path, StringComparison.Ordinal))
        {
            return CleanupOutcome.SkippedPolicy;
        }

        // D2
        var again = CandidatePolicy.Evaluate(candidate.Facts, context);
        if (again.Reasons.Contains(SkipReason.Protected))
        {
            return CleanupOutcome.SkippedProtected;
        }

        return again.IsSelectable && again.Status == candidate.Decision.Status ? null : CleanupOutcome.SkippedPolicy;
    }

    /// <summary>
    /// D4: what Windows reports <b>now</b> (through the cleanup's own handle) must still pass every candidate
    /// rule and match the scan exactly: same location, identity, size, dates and attributes, one link.
    /// </summary>
    public static CleanupOutcome? CheckCurrent(ScanCandidate candidate, FileFacts current, CandidatePolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(context);

        // Checked here too, so this step never depends on D1 having run first.
        var recorded = candidate.Facts;
        if (recorded is null || !candidate.Decision.IsSelectable)
        {
            return CleanupOutcome.SkippedPolicy;
        }

        var now = CandidatePolicy.Evaluate(current, context);
        if (!now.IsSelectable || now.Status != candidate.Decision.Status)
        {
            return CleanupOutcome.SkippedChanged;
        }

        var unchanged =
            string.Equals(current.Path, recorded.Path, StringComparison.Ordinal)
            && string.Equals(current.FinalPath, recorded.FinalPath, StringComparison.Ordinal)
            && current.Identity.Matches(recorded.Identity)
            && current.Size == recorded.Size
            && current.LastWriteTimeUtc == recorded.LastWriteTimeUtc
            && current.CreationTimeUtc == recorded.CreationTimeUtc
            && current.Attributes == recorded.Attributes
            && current.LinkCount == 1;

        return unchanged ? null : CleanupOutcome.SkippedChanged;
    }
}
