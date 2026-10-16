namespace SafeClean.Core.Models;

/// <summary>How far a running scan has got. There is no percentage because the total isn't known in advance.</summary>
public sealed record ScanProgress(int FilesExamined, int FoldersScanned);
