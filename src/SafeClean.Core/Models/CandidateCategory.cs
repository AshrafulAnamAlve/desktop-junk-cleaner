namespace SafeClean.Core.Models;

/// <summary>Grouping for display only. A category never makes a file safe or unsafe.</summary>
public enum CandidateCategory
{
    Other = 0,
    TemporaryFiles = 1,
    LogFiles = 2,
    InstallerLeftovers = 3,
}
