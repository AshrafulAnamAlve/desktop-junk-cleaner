namespace SafeClean.Core.Models;

/// <summary>
/// What happened to one selected file during a cleanup. The default value (0) is a skip, so an
/// uninitialised outcome can never mean "deleted".
/// </summary>
public enum CleanupOutcome
{
    /// <summary>D1/D2: not a selectable file from this scan, or it no longer passes the candidate rules.</summary>
    SkippedPolicy = 0,

    /// <summary>D2: inside a protected location.</summary>
    SkippedProtected,

    /// <summary>D4: identity, size, dates, attributes, links or location differ from the scan.</summary>
    SkippedChanged,

    /// <summary>D3: the file no longer exists (or is already being deleted by someone else).</summary>
    SkippedMissing,

    /// <summary>D3: another program has the file open.</summary>
    SkippedInUse,

    /// <summary>D3: Windows denied access.</summary>
    SkippedAccessDenied,

    /// <summary>Not attempted, because the cleanup was cancelled or stopped before reaching this file.</summary>
    SkippedCancelled,

    /// <summary>Windows reported an unexpected error; the file was not deleted. Never retried or forced.</summary>
    Failed,

    /// <summary>Dry Run only: the file passed every check and would be deleted in a real cleanup.</summary>
    WouldDelete,

    /// <summary>Real cleanup only: the file was marked for deletion through the checked handle and the handle was closed.</summary>
    Deleted,
}
