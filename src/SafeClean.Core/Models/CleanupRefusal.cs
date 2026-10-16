namespace SafeClean.Core.Models;

/// <summary>Why a whole cleanup request was refused before any file was touched (docs/SECURITY.md §5, "before any cleanup").</summary>
public enum CleanupRefusal
{
    /// <summary>The requested mode is neither Dry Run nor Real.</summary>
    InvalidMode,

    /// <summary>R8: SafeClean is running as administrator.</summary>
    Elevated,

    /// <summary>The scan is too old to trust, or the clock moved so its age can't be measured.</summary>
    SessionExpired,

    /// <summary>The Temp folder, checked again now, no longer passes the root rules.</summary>
    RootRefused,

    /// <summary>The Temp folder is no longer the same folder (path or identity) that was scanned.</summary>
    RootChanged,
}
