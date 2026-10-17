namespace SafeClean.App.ViewModels;

/// <summary>Where the Junk Cleaner page is in its scan and cleanup life cycle.</summary>
public enum ScanState
{
    NotStarted,
    Scanning,
    Completed,
    Refused,
    Cancelled,
    Failed,

    /// <summary>A real cleanup is running.</summary>
    Cleaning,

    /// <summary>A cleanup finished; its report is shown and a new scan is needed before cleaning again.</summary>
    CleanedUp,
}

/// <summary>Which files the list shows.</summary>
public enum ResultFilter
{
    All,
    Eligible,
    NeedsReview,
    Skipped,
}
