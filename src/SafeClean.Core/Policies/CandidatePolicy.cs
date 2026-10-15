using System.Collections.Frozen;
using SafeClean.Core.Models;

namespace SafeClean.Core.Policies;

/// <summary>
/// Decides whether one file may be offered for cleanup (rules C1–C9 in docs/SECURITY.md).
/// Every rule is checked and every failure is reported, so the user sees all the reasons at once.
/// A file's name, extension or age alone never makes it eligible.
/// </summary>
public static class CandidatePolicy
{
    /// <summary>The only attributes an eligible file may carry (rule C4).</summary>
    public const FileAttributes AllowedAttributes =
        FileAttributes.Normal | FileAttributes.Archive | FileAttributes.Temporary
        | FileAttributes.NotContentIndexed | FileAttributes.Compressed;

    /// <summary>Timestamps before this are implausible for a Temp file (e.g. zeroed dates) and can't be trusted.</summary>
    public static readonly DateTimeOffset EarliestTrustedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Programs, scripts and installers: may be removed, but deserve a human look first.
    private static readonly FrozenSet<string> ReviewExtensions = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        ".exe", ".dll", ".sys", ".com", ".scr", ".msi", ".msp", ".msu", ".ps1", ".psm1",
        ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".jar", ".lnk");

    private static readonly FrozenSet<string> TemporaryExtensions = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, ".tmp", ".temp");

    private static readonly FrozenSet<string> LogExtensions = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, ".log", ".etl", ".dmp");

    private static readonly FrozenSet<string> InstallerExtensions = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, ".msi", ".msp", ".msu", ".cab", ".exe", ".dll");

    public static CandidateDecision Evaluate(FileFacts facts, CandidatePolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(context);

        var reasons = new List<SkipReason>();
        string? relativePath = null;
        var root = context.Root.CanonicalPath;

        // C8 + C5 + C9 + C1: location checks.
        if (!PathValidator.IsValid(facts.Path) || !PathValidator.IsValid(facts.FinalPath))
        {
            reasons.Add(SkipReason.InvalidPath);
        }
        else
        {
            // Exact comparison: both strings come from Windows, so any difference is suspicious.
            if (WindowsPath.IsStrictlyInside(root, facts.Path, StringComparison.Ordinal)
                && WindowsPath.IsStrictlyInside(root, facts.FinalPath, StringComparison.Ordinal))
            {
                relativePath = WindowsPath.GetRelativePath(root, facts.FinalPath);
            }
            else
            {
                reasons.Add(SkipReason.OutsideRoot);
            }

            if (!string.Equals(facts.Path, facts.FinalPath, StringComparison.Ordinal))
            {
                reasons.Add(SkipReason.PathMismatch);
            }

            if (context.Protected.Protects(facts.Path) || context.Protected.Protects(facts.FinalPath))
            {
                reasons.Add(SkipReason.Protected);
            }

            if (context.IsExcluded(facts.Path) || context.IsExcluded(facts.FinalPath))
            {
                reasons.Add(SkipReason.Excluded);
            }

            if (relativePath is not null && WindowsPath.CountFolderLevels(relativePath) > SafetyDefaults.MaxScanDepth)
            {
                reasons.Add(SkipReason.TooDeep);
            }
        }

        // C2, C3, C4: what kind of entry it is.
        if (facts.Attributes.HasFlag(FileAttributes.Directory))
        {
            reasons.Add(SkipReason.IsDirectory);
        }

        if (facts.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            reasons.Add(SkipReason.ReparsePoint);
        }

        // Allow-list, so attribute bits .NET has no name for (cloud "pinned", "recall on open"…) are rejected too.
        const FileAttributes reportedSeparately = FileAttributes.Directory | FileAttributes.ReparsePoint;
        if ((facts.Attributes & ~(AllowedAttributes | reportedSeparately)) != 0)
        {
            reasons.Add(SkipReason.DisallowedAttributes);
        }

        // C6
        if (facts.LinkCount != 1)
        {
            reasons.Add(SkipReason.UnexpectedLinkCount);
        }

        if (facts.Size < 0)
        {
            reasons.Add(SkipReason.UnexpectedSize);
        }

        // C7: age uses the newer of creation and modification, because extracted files keep old modified dates.
        var newest = facts.CreationTimeUtc > facts.LastWriteTimeUtc ? facts.CreationTimeUtc : facts.LastWriteTimeUtc;
        var oldest = facts.CreationTimeUtc < facts.LastWriteTimeUtc ? facts.CreationTimeUtc : facts.LastWriteTimeUtc;
        if (newest > context.Now || oldest < EarliestTrustedTimestamp)
        {
            reasons.Add(SkipReason.UntrustworthyTimestamp);
        }
        else if (context.Now - newest < context.MinimumAge)
        {
            reasons.Add(SkipReason.TooRecent);
        }

        var name = PathValidator.IsValid(facts.FinalPath) ? WindowsPath.GetName(facts.FinalPath) : string.Empty;
        var extension = WindowsPath.GetExtension(name);

        var status = reasons.Count > 0
            ? CandidateStatus.Skipped
            : ReviewExtensions.Contains(extension) ? CandidateStatus.NeedsReview : CandidateStatus.Eligible;

        return new CandidateDecision(status, reasons, Categorize(name, extension), relativePath);
    }

    private static CandidateCategory Categorize(string name, string extension)
    {
        if (TemporaryExtensions.Contains(extension) || name.StartsWith('~'))
        {
            return CandidateCategory.TemporaryFiles;
        }

        if (LogExtensions.Contains(extension))
        {
            return CandidateCategory.LogFiles;
        }

        if (InstallerExtensions.Contains(extension))
        {
            return CandidateCategory.InstallerLeftovers;
        }

        return CandidateCategory.Other;
    }
}
