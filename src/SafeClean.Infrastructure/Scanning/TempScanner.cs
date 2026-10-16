using System.Diagnostics;
using System.Security;
using Microsoft.Extensions.Logging;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.Infrastructure.Scanning;

/// <summary>
/// Walks the approved Temp folder and classifies every entry. Strictly read-only:
/// <list type="bullet">
/// <item>folders are listed one level at a time, and links (junctions, symlinks) are never entered;</item>
/// <item>each folder is re-checked just before it is listed, in case it was swapped for a link;</item>
/// <item>every file is inspected through a read-attributes-only handle and judged by <see cref="CandidatePolicy"/>;</item>
/// <item>depth and entry-count limits keep the scan bounded; cancellation is checked between entries.</item>
/// </list>
/// </summary>
public sealed class TempScanner(
    ITempRootResolver rootResolver,
    FileInspector inspector,
    TimeProvider timeProvider,
    ILogger<TempScanner> logger) : ITempScanner
{
    public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Scan(progress, cancellationToken), cancellationToken);

    private ScanResult Scan(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();

        var resolution = rootResolver.Resolve();
        if (!resolution.Decision.IsApproved)
        {
            return ScanResult.Refused(resolution.Decision);
        }

        var context = new CandidatePolicyContext(resolution.Decision.Root, resolution.ProtectedLocations, startedAt);
        var run = new ScanRun(inspector, context, progress, cancellationToken);
        run.Execute();

        var session = new ScanSession(
            Guid.NewGuid(),
            resolution.Decision.Root,
            startedAt,
            timeProvider.GetUtcNow(),
            run.Candidates,
            run.FoldersScanned,
            run.Vanished,
            run.Truncated);

        logger.LogInformation(
            "Scan finished in {Elapsed} ms: {Eligible} eligible, {Review} need review, {Skipped} skipped, {Folders} folders, {Vanished} vanished, truncated: {Truncated}.",
            stopwatch.ElapsedMilliseconds,
            session.Summary.EligibleCount,
            session.Summary.NeedsReviewCount,
            session.Summary.SkippedCount,
            session.FoldersScanned,
            session.VanishedDuringScan,
            session.WasTruncated);

        return ScanResult.Completed(resolution.Decision, session);
    }

    /// <summary>State for one scan.</summary>
    private sealed class ScanRun(
        FileInspector inspector,
        CandidatePolicyContext context,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        private const int ProgressInterval = 250;

        private static readonly EnumerationOptions ListOneLevel = new()
        {
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            MatchType = MatchType.Simple,
        };

        public List<ScanCandidate> Candidates { get; } = [];

        public int FoldersScanned { get; private set; }

        public int FilesExamined { get; private set; }

        public int Vanished { get; private set; }

        public bool Truncated { get; private set; }

        public void Execute()
        {
            // Level = number of separators in a path relative to the root; the root's own entries are level 0.
            var pending = new Stack<(string Path, int Level)>();
            pending.Push((context.Root.CanonicalPath, -1));

            while (pending.Count > 0 && !Truncated)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (folder, level) = pending.Pop();
                ScanFolder(folder, level, pending);
            }

            ReportProgress();
        }

        private void ScanFolder(string folder, int level, Stack<(string Path, int Level)> pending)
        {
            if (level >= 0 && !IsStillAPlainFolder(folder))
            {
                return;
            }

            FoldersScanned++;
            IEnumerator<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", ListOneLevel).GetEnumerator();
            }
            catch (DirectoryNotFoundException)
            {
                Vanished++;
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                AddUnreadableFolder(folder);
                return;
            }

            using (entries)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileSystemInfo entry;
                    try
                    {
                        if (!entries.MoveNext())
                        {
                            break;
                        }

                        entry = entries.Current;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        Vanished++;
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        AddUnreadableFolder(folder);
                        break;
                    }

                    if (Candidates.Count >= SafetyDefaults.MaxScanEntries)
                    {
                        Truncated = true;
                        return;
                    }

                    HandleEntry(folder + @"\" + entry.Name, entry.Attributes, level + 1, pending);
                }
            }
        }

        /// <summary>
        /// Re-checks a sub-folder just before listing it. If it was swapped for a link (or moved) since it was
        /// found, it is reported as skipped instead of being entered.
        /// </summary>
        private bool IsStillAPlainFolder(string folder)
        {
            var check = inspector.InspectDirectory(folder);
            if (!check.Succeeded)
            {
                if (check.IsMissing)
                {
                    Vanished++;
                }
                else
                {
                    Add(folder, null, CandidatePolicy.Skip(folder, context, SkipReason.IsDirectory, check.FailureReason));
                }

                return false;
            }

            if (check.Facts.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || !string.Equals(check.Facts.FinalPath, folder, StringComparison.Ordinal))
            {
                Add(folder, check.Facts, CandidatePolicy.Evaluate(check.Facts, context));
                return false;
            }

            return true;
        }

        private void HandleEntry(string path, FileAttributes listedAttributes, int level, Stack<(string Path, int Level)> pending)
        {
            var isDirectory = listedAttributes.HasFlag(FileAttributes.Directory);
            var isLink = listedAttributes.HasFlag(FileAttributes.ReparsePoint);

            if (isDirectory && !isLink)
            {
                // Its contents would be one level deeper; enter only if that stays within the limit.
                if (level + 1 > SafetyDefaults.MaxScanDepth)
                {
                    Add(path, null, CandidatePolicy.Skip(path, context, SkipReason.IsDirectory, SkipReason.TooDeep));
                }
                else
                {
                    pending.Push((path, level));
                }

                return;
            }

            // Files, file links and folder links: inspect the entry itself (links are never followed).
            FilesExamined++;
            var result = isDirectory ? inspector.InspectDirectory(path) : inspector.InspectFile(path);
            if (result.Succeeded)
            {
                Add(path, result.Facts, CandidatePolicy.Evaluate(result.Facts, context));
            }
            else if (result.IsMissing)
            {
                Vanished++;
            }
            else
            {
                Add(path, null, CandidatePolicy.Skip(path, context, result.FailureReason));
            }

            if (FilesExamined % ProgressInterval == 0)
            {
                ReportProgress();
            }
        }

        private void AddUnreadableFolder(string folder) =>
            Add(folder, null, CandidatePolicy.Skip(folder, context, SkipReason.IsDirectory, SkipReason.Inaccessible));

        private void Add(string path, FileFacts? facts, CandidateDecision decision) =>
            Candidates.Add(new ScanCandidate(new CandidateId(Candidates.Count + 1), path, facts, decision));

        private void ReportProgress() => progress?.Report(new ScanProgress(FilesExamined, FoldersScanned));
    }
}
