using SafeClean.App.Presentation;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.App.ViewModels;

/// <summary>What the confirmation dialog says before a real cleanup. Built from the same plan as the Dry Run preview.</summary>
public sealed class CleanupConfirmationViewModel
{
    private static readonly CandidateCategory[] CategoryOrder =
        [CandidateCategory.TemporaryFiles, CandidateCategory.LogFiles, CandidateCategory.InstallerLeftovers, CandidateCategory.Other];

    public CleanupConfirmationViewModel(DryRunPreview plan, string? sandboxRoot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        FileCount = plan.FileCount;
        EstimatedBytes = plan.EstimatedBytes;
        NeedsReviewCount = plan.NeedsReviewCount;
        Breakdown = DescribeCategories(plan);
        SandboxRoot = sandboxRoot;
    }

    public int FileCount { get; }

    public long EstimatedBytes { get; }

    public int NeedsReviewCount { get; }

    public string Title => $"Permanently delete {DisplayText.Count(FileCount, "file", "files")}?";

    public string SizeText => $"About {DisplayText.FormatBytes(EstimatedBytes)} (estimate)";

    public IReadOnlyList<string> Breakdown { get; }

    public bool HasReviewWarning => NeedsReviewCount > 0;

    public string ReviewWarning => NeedsReviewCount > 0
        ? $"{DisplayText.Count(NeedsReviewCount, "selected file is", "selected files are")} a program, script or installer (marked \"Needs review\")."
        : string.Empty;

    public string PermanentText => "Deleted files are not moved to the Recycle Bin. Recovery is not guaranteed.";

    public string RecheckText =>
        "Each file is checked again right before it is deleted. Files that are in use, have changed since the scan, " +
        "or no longer pass the safety rules are skipped and kept.";

    public string DeleteButtonText => $"Delete {DisplayText.Count(FileCount, "file", "files")}";

    public string? SandboxRoot { get; }

    public bool IsSandbox => SandboxRoot is not null;

    public string SandboxText => SandboxRoot is null ? string.Empty : $"SANDBOX: only files in {SandboxRoot} can be affected.";

    /// <summary>"Installer leftovers: 27 files" — one line per category, in a fixed order.</summary>
    internal static IReadOnlyList<string> DescribeCategories(DryRunPreview plan) =>
        CategoryOrder
            .Where(plan.FilesByCategory.ContainsKey)
            .Select(c => $"{PolicyText.Describe(c)}: {DisplayText.Count(plan.FilesByCategory[c], "file", "files")}")
            .ToList();
}
