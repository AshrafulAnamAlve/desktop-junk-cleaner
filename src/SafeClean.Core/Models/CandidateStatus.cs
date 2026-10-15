namespace SafeClean.Core.Models;

/// <summary>The outcome of checking one file against the cleanup policy.</summary>
public enum CandidateStatus
{
    /// <summary>Failed at least one check. Shown with its reasons; can never be selected.</summary>
    Skipped = 0,

    /// <summary>Passed every check; may be selected.</summary>
    Eligible = 1,

    /// <summary>Passed every check but is a type worth a human look (programs, scripts, installers). Never auto-selected.</summary>
    NeedsReview = 2,
}
