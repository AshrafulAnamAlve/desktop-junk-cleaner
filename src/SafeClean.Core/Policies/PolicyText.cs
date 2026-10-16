using SafeClean.Core.Models;

namespace SafeClean.Core.Policies;

/// <summary>Plain-English explanations shown to users. Every enum value must have one (enforced by tests).</summary>
public static class PolicyText
{
    public static string Describe(SkipReason reason) => reason switch
    {
        SkipReason.InvalidPath => "Its path contains parts that Windows treats specially, so it can't be checked safely.",
        SkipReason.OutsideRoot => "It isn't inside your Temp folder.",
        SkipReason.PathMismatch => "Its real location differs from where it was found, so it may have been moved or redirected.",
        SkipReason.IsDirectory => "It's a folder. SafeClean only removes individual files.",
        SkipReason.ReparsePoint => "It's a link to another location or a cloud placeholder, not a regular file.",
        SkipReason.DisallowedAttributes => "It's marked read-only, hidden, system, offline, encrypted or with another protective attribute.",
        SkipReason.UnexpectedLinkCount => "It has more than one name on disk (a hard link) or is already being deleted.",
        SkipReason.TooRecent => "It was created or changed recently and may still be in use.",
        SkipReason.UntrustworthyTimestamp => "Its dates are in the future or implausibly old, so its age can't be trusted.",
        SkipReason.Protected => "It's inside a protected location.",
        SkipReason.Excluded => "It matches one of your exclusions.",
        SkipReason.TooDeep => "It's nested in too many folders to be checked.",
        SkipReason.UnexpectedSize => "Windows reported an impossible size for it.",
        SkipReason.Inaccessible => "Windows didn't allow SafeClean to read its details.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    public static string Describe(RootProblem problem) => problem switch
    {
        RootProblem.InvalidPath => "The Temp folder's path isn't a plain local drive path.",
        RootProblem.NotFixedDrive => "The Temp folder isn't on an internal drive.",
        RootProblem.NotNtfs => "The Temp folder's drive isn't formatted with NTFS.",
        RootProblem.NotADirectory => "The Temp location isn't a folder.",
        RootProblem.RootIsReparsePoint => "The Temp folder is a link to another location.",
        RootProblem.AncestorIsReparsePoint => "A folder above the Temp folder is a link to another location.",
        RootProblem.NotUserTempDirectory => "The Temp setting doesn't point to your own %LOCALAPPDATA%\\Temp folder.",
        RootProblem.IsDriveRoot => "The Temp setting points to the top of a drive.",
        RootProblem.IsProtectedLocation => "The Temp setting points to a protected location.",
        RootProblem.InsideProtectedLocation => "The Temp folder is inside a protected location.",
        RootProblem.ContainsProtectedLocation => "The Temp folder contains a protected location.",
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, null),
    };

    public static string Describe(CandidateStatus status) => status switch
    {
        CandidateStatus.Eligible => "Safe to remove",
        CandidateStatus.NeedsReview => "Needs review",
        CandidateStatus.Skipped => "Skipped",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string Describe(CleanupOutcome outcome) => outcome switch
    {
        CleanupOutcome.SkippedPolicy => "Skipped: it isn't a selectable file from this scan, or it no longer passes the safety rules.",
        CleanupOutcome.SkippedProtected => "Skipped: it's inside a protected location.",
        CleanupOutcome.SkippedChanged => "Skipped: it changed after the scan (size, date, location or identity).",
        CleanupOutcome.SkippedMissing => "Skipped: it no longer exists.",
        CleanupOutcome.SkippedInUse => "Skipped: another program is using it.",
        CleanupOutcome.SkippedAccessDenied => "Skipped: Windows didn't allow SafeClean to remove it.",
        CleanupOutcome.SkippedCancelled => "Not attempted: the cleanup was cancelled or stopped before reaching it.",
        CleanupOutcome.Failed => "Not deleted: Windows reported an error.",
        CleanupOutcome.WouldDelete => "Would be deleted (preview only — nothing was changed).",
        CleanupOutcome.Deleted => "Deleted.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    public static string Describe(CleanupRefusal refusal) => refusal switch
    {
        CleanupRefusal.InvalidMode => "The cleanup mode wasn't recognised, so nothing was done.",
        CleanupRefusal.Elevated => "SafeClean is running as administrator, so cleanup is disabled. Start it normally to clean.",
        CleanupRefusal.SessionExpired => $"The scan is more than {SafetyDefaults.MaxScanSessionAge.TotalMinutes:0} minutes old (or the clock changed). Scan again first.",
        CleanupRefusal.RootRefused => "The Temp folder no longer passes the safety checks, so nothing was done.",
        CleanupRefusal.RootChanged => "The Temp folder isn't the same folder that was scanned, so nothing was done. Scan again.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, null),
    };

    public static string Describe(CandidateCategory category) => category switch
    {
        CandidateCategory.TemporaryFiles => "Temporary files",
        CandidateCategory.LogFiles => "Logs and diagnostic files",
        CandidateCategory.InstallerLeftovers => "Installer leftovers",
        CandidateCategory.Other => "Other files",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };
}
