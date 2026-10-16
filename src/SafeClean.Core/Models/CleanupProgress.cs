namespace SafeClean.Core.Models;

/// <summary>How far a running cleanup has got.</summary>
public sealed record CleanupProgress(int Processed, int Total);
