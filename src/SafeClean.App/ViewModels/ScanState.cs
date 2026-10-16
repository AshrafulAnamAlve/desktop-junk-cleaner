namespace SafeClean.App.ViewModels;

/// <summary>Where the Junk Cleaner page is in its scan life cycle.</summary>
public enum ScanState
{
    NotStarted,
    Scanning,
    Completed,
    Refused,
    Cancelled,
    Failed,
}

/// <summary>Which files the list shows.</summary>
public enum ResultFilter
{
    All,
    Eligible,
    NeedsReview,
    Skipped,
}
