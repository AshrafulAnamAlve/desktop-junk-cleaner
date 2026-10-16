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

    /// <summary>Safety cap on how many entries one scan lists; beyond this the scan stops and says it was cut short.</summary>
    public const int MaxScanEntries = 250_000;

    /// <summary>
    /// A cleanup is refused if its scan started longer ago than this; the Temp folder changes all the time,
    /// so old results must be refreshed by scanning again.
    /// </summary>
    public static readonly TimeSpan MaxScanSessionAge = TimeSpan.FromMinutes(30);
}
