namespace SafeClean.Core.Models;

/// <summary>One entry found during a scan, with the policy's verdict.</summary>
/// <param name="Id">Unique within its scan session.</param>
/// <param name="Path">Full path where the entry was found.</param>
/// <param name="Facts">What Windows reported about it, or null if it could not be inspected.</param>
/// <param name="Decision">Eligible, needs review or skipped, with reasons.</param>
public sealed record ScanCandidate(CandidateId Id, string Path, FileFacts? Facts, CandidateDecision Decision)
{
    /// <summary>Size in bytes as reported at scan time (0 when unknown or for folders).</summary>
    public long Size => Facts is { Size: > 0 } facts ? facts.Size : 0;
}
