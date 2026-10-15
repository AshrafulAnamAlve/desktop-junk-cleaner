using SafeClean.Core.Models;

namespace SafeClean.Core.Policies;

/// <summary>
/// Conservative default values from docs/SECURITY.md. Changing any of these requires
/// updating the security document and the tests that pin them.
/// </summary>
public static class SafetyDefaults
{
    /// <summary>The app always starts in preview mode.</summary>
    public const CleanupMode StartupMode = CleanupMode.DryRun;

    /// <summary>
    /// Files created or modified more recently than this are never offered for cleanup (rule C7).
    /// Seven days matches the default used by Windows Disk Cleanup for temporary files.
    /// </summary>
    public static readonly TimeSpan MinimumFileAge = TimeSpan.FromDays(7);

    /// <summary>How many folder levels below the Temp root the scanner may descend (rule C1).</summary>
    public const int MaxScanDepth = 16;
}
