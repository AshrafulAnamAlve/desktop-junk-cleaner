using SafeClean.Core.Models;
using static SafeClean.Infrastructure.Native.NativeMethods;

namespace SafeClean.Infrastructure.Cleanup;

/// <summary>What happened when one file was attempted (or, in Dry Run, re-checked).</summary>
internal readonly record struct AttemptResult(CleanupOutcome Outcome, long Bytes, int Win32Error)
{
    public static AttemptResult Deleted(long bytes) => new(CleanupOutcome.Deleted, bytes, 0);

    public static AttemptResult WouldDelete(long bytes) => new(CleanupOutcome.WouldDelete, bytes, 0);

    public static AttemptResult Skipped(CleanupOutcome outcome) =>
        outcome is CleanupOutcome.Deleted or CleanupOutcome.WouldDelete
            ? throw new ArgumentException("A skip can't be a deletion.", nameof(outcome))
            : new(outcome, 0, 0);

    public static AttemptResult Failed(int win32Error) => new(CleanupOutcome.Failed, 0, win32Error);

    /// <summary>Turns a failure to open a file into a skip or failure. None of these can ever count as deleted.</summary>
    public static AttemptResult FromOpenError(int win32Error) => win32Error switch
    {
        0 => new(CleanupOutcome.SkippedPolicy, 0, 0), // refused before reaching Windows (invalid path)
        ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND or ERROR_DELETE_PENDING => new(CleanupOutcome.SkippedMissing, 0, win32Error),
        ERROR_ACCESS_DENIED => new(CleanupOutcome.SkippedAccessDenied, 0, win32Error),
        ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION => new(CleanupOutcome.SkippedInUse, 0, win32Error),
        _ => Failed(win32Error),
    };
}
