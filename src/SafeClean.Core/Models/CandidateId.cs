namespace SafeClean.Core.Models;

/// <summary>
/// Identifies one entry within one scan session. The UI refers to selected files by these IDs,
/// never by path text, so it cannot point the cleanup at an arbitrary path.
/// </summary>
public readonly record struct CandidateId(int Value);
