namespace SafeClean.Core.Models;

/// <summary>The result for one selected ID in a cleanup report.</summary>
/// <param name="Id">The selected candidate ID.</param>
/// <param name="Path">Full path recorded by the scan, or empty if the ID was not part of the scan.</param>
/// <param name="RelativePath">Path below the Temp folder (what the log records), or null if unknown.</param>
/// <param name="Outcome">Deleted, would delete, skipped (with the reason) or failed.</param>
/// <param name="Bytes">Size of a deleted (or, in Dry Run, deletable) file; 0 for every other outcome.</param>
/// <param name="Win32Error">The Windows error code behind a skip or failure, or 0.</param>
public sealed record CleanupEntry(
    CandidateId Id,
    string Path,
    string? RelativePath,
    CleanupOutcome Outcome,
    long Bytes,
    int Win32Error);
