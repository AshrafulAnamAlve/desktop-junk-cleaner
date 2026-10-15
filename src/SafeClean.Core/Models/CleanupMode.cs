namespace SafeClean.Core.Models;

/// <summary>How a cleanup request is carried out.</summary>
public enum CleanupMode
{
    /// <summary>
    /// Preview only: nothing on disk is changed. This is the default (value 0),
    /// so an uninitialised setting can never mean "really delete".
    /// </summary>
    DryRun = 0,

    /// <summary>Selected, revalidated files are actually deleted. Must be explicitly enabled by the user.</summary>
    Real = 1,
}
