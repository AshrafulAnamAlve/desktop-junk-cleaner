using SafeClean.App.Presentation;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.App.ViewModels;

/// <summary>
/// The result screen after a real cleanup. Shows exactly what the cleanup service reported:
/// deleted files and space freed (from deletions only), then skipped files grouped by reason, then failures.
/// </summary>
public sealed class CleanupReportViewModel
{
    public CleanupReportViewModel(CleanupReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Report = report;

        if (report.WasRefused)
        {
            Headline = "Nothing was deleted";
            Notes = [PolicyText.Describe(report.Refusal.Value), .. report.RootProblems.Select(PolicyText.Describe)];
            SkippedGroups = [];
            FailedLines = [];
            return;
        }

        Headline = report.DeletedCount == 0
            ? "No files were deleted"
            : $"Deleted {DisplayText.Count(report.DeletedCount, "file", "files")}";
        SpaceFreedText = $"Space freed: {DisplayText.FormatBytes(report.BytesFreed)} (only files that were actually deleted are counted)";

        SkippedGroups = report.Skipped
            .GroupBy(e => e.Outcome)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{DisplayText.Count(g.Count(), "file", "files")} — {PolicyText.Describe(g.Key)}")
            .ToList();

        FailedLines = report.Failed
            .Select(e => $"{(e.RelativePath is null ? "(unknown file)" : DisplayText.Reveal(e.RelativePath))} — Windows error {e.Win32Error}")
            .ToList();

        var notes = new List<string>();
        if (report.WasCancelled)
        {
            notes.Add("You cancelled the cleanup. Files it hadn't reached yet were not touched.");
        }

        if (report.StoppedByError)
        {
            notes.Add("The cleanup stopped early because of an unexpected error. Files it hadn't reached yet were not touched. Details are in the log.");
        }

        notes.Add("Skipped and failed files were left exactly as they were. Scan again to see what remains.");
        Notes = notes;
    }

    private CleanupReportViewModel(string headline, IReadOnlyList<string> notes)
    {
        Headline = headline;
        Notes = notes;
        SkippedGroups = [];
        FailedLines = [];
    }

    /// <summary>The report from the cleanup service, or null if the cleanup failed before returning one.</summary>
    public CleanupReport? Report { get; }

    public string Headline { get; }

    public string SpaceFreedText { get; } = string.Empty;

    public string DeletedText => Report is null ? string.Empty : DisplayText.Count(Report.DeletedCount, "file deleted", "files deleted");

    public string SkippedText => Report is null ? string.Empty : DisplayText.Count(Report.SkippedCount, "file skipped", "files skipped");

    public string FailedText => Report is null ? string.Empty : DisplayText.Count(Report.FailedCount, "file failed", "files failed");

    /// <summary>"3 files — Skipped: another program is using it." — one line per reason.</summary>
    public IReadOnlyList<string> SkippedGroups { get; }

    public IReadOnlyList<string> FailedLines { get; }

    public bool HasFailures => FailedLines.Count > 0;

    public bool HasSkipped => SkippedGroups.Count > 0;

    public IReadOnlyList<string> Notes { get; }

    /// <summary>The cleanup service itself failed, so it is unknown which files were deleted.</summary>
    public static CleanupReportViewModel ForUnexpectedError() => new(
        "The cleanup stopped because of an unexpected error",
        [
            "Some files may already have been deleted before the error. Details are in the log.",
            "Scan again to see what remains.",
        ]);
}
