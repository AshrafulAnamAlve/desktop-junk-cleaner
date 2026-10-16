using Microsoft.Extensions.Logging;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Scanning;

namespace SafeClean.Infrastructure.Cleanup;

/// <summary>
/// Carries out a cleanup request (docs/SECURITY.md §5):
/// <list type="bullet">
/// <item>the whole request is refused if SafeClean is elevated, the scan is too old, or the Temp folder
/// fails its checks again or is no longer the folder that was scanned;</item>
/// <item>only IDs from the scan session are acted on, one file at a time, each re-checked;</item>
/// <item>in Real mode, only <see cref="HandleBasedFileDeleter"/> deletes; in Dry Run, files are only re-inspected read-only;</item>
/// <item>cancellation is checked between files, never in the middle of one, and every outcome is logged
/// with its path relative to the Temp folder.</item>
/// </list>
/// Not connected to the user interface until Phase 7 (enforced by an architecture test).
/// </summary>
public sealed class CleanupService(
    ITempRootResolver rootResolver,
    IElevationProbe elevationProbe,
    FileInspector inspector,
    TimeProvider timeProvider,
    ILogger<CleanupService> logger) : ICleanupService
{
    public Task<CleanupReport> CleanAsync(
        ScanSession session,
        IReadOnlyCollection<CandidateId> selected,
        CleanupMode mode,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(selected);

        // The token is deliberately not given to Task.Run: even a cancelled request returns a report.
        return Task.Run(() => Clean(session, selected, mode, progress, cancellationToken), CancellationToken.None);
    }

    private CleanupReport Clean(
        ScanSession session,
        IReadOnlyCollection<CandidateId> selected,
        CleanupMode mode,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();

        if (CleanupPolicy.CheckRequest(mode, elevationProbe.IsElevated, session, startedAt) is { } refusal)
        {
            return Refuse(mode, startedAt, refusal, []);
        }

        var resolution = rootResolver.Resolve();
        var rootRefusal = CleanupPolicy.CheckRoot(session, resolution.Decision);
        if (rootRefusal is not null || !resolution.Decision.IsApproved)
        {
            return Refuse(mode, startedAt, rootRefusal ?? CleanupRefusal.RootRefused, resolution.Decision.Problems);
        }

        var context = new CandidatePolicyContext(resolution.Decision.Root, resolution.ProtectedLocations, startedAt);
        var candidates = session.Candidates.ToDictionary(c => c.Id);
        var ids = selected.Distinct().ToList();
        var entries = new List<CleanupEntry>(ids.Count);
        var wasCancelled = false;
        var stoppedByError = false;

        foreach (var id in ids)
        {
            candidates.TryGetValue(id, out var candidate);

            // Cancellation is honoured between files only. Files not reached are listed, never dropped.
            if (!stoppedByError && cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
            }

            if (wasCancelled || stoppedByError)
            {
                entries.Add(Entry(id, candidate, AttemptResult.Skipped(CleanupOutcome.SkippedCancelled)));
                continue;
            }

            AttemptResult result;
            try
            {
                result = Attempt(candidate, mode, context);
            }
            catch (Exception ex)
            {
                // A bug, not a file problem: stop instead of carrying on in an unknown state.
                logger.LogError(ex, "Cleanup stopped by an unexpected error.");
                result = AttemptResult.Failed(0);
                stoppedByError = true;
            }

            var entry = Entry(id, candidate, result);
            entries.Add(entry);
            LogEntry(mode, entry);
            progress?.Report(new CleanupProgress(entries.Count, ids.Count));
        }

        var report = CleanupReport.Completed(mode, startedAt, timeProvider.GetUtcNow(), entries, wasCancelled, stoppedByError);
        logger.LogInformation(
            "Cleanup ({Mode}) finished: {Deleted} deleted, {WouldDelete} would be deleted, {Skipped} skipped, {Failed} failed, {Bytes} bytes freed, cancelled: {Cancelled}, stopped by error: {Stopped}.",
            mode,
            report.DeletedCount,
            report.WouldDeleteCount,
            report.SkippedCount,
            report.FailedCount,
            report.BytesFreed,
            report.WasCancelled,
            report.StoppedByError);
        return report;
    }

    private AttemptResult Attempt(ScanCandidate? candidate, CleanupMode mode, CandidatePolicyContext context)
    {
        // D1: an ID that isn't part of this scan is never acted on.
        if (candidate is null)
        {
            return AttemptResult.Skipped(CleanupOutcome.SkippedPolicy);
        }

        // D1 + D2
        if (CleanupPolicy.CheckRecorded(candidate, context) is { } refused)
        {
            return AttemptResult.Skipped(refused);
        }

        // D3–D6 only in Real mode. Any other value was refused above, and is never treated as Real.
        return mode == CleanupMode.Real
            ? HandleBasedFileDeleter.TryDelete(candidate, context)
            : Preview(candidate, context);
    }

    /// <summary>Dry Run: the same D4 comparison through a read-only handle. Nothing is opened exclusively or changed.</summary>
    private AttemptResult Preview(ScanCandidate candidate, CandidatePolicyContext context)
    {
        var inspection = inspector.InspectFile(candidate.Path);
        if (!inspection.Succeeded)
        {
            return AttemptResult.FromOpenError(inspection.Win32Error);
        }

        return CleanupPolicy.CheckCurrent(candidate, inspection.Facts, context) is { } changed
            ? AttemptResult.Skipped(changed)
            : AttemptResult.WouldDelete(inspection.Facts.Size);
    }

    private CleanupReport Refuse(CleanupMode mode, DateTimeOffset at, CleanupRefusal refusal, IReadOnlyList<RootProblem> problems)
    {
        logger.LogWarning("Cleanup ({Mode}) refused before touching any file: {Refusal} {Problems}.", mode, refusal, string.Join(", ", problems));
        return CleanupReport.Refused(mode, at, refusal, problems);
    }

    /// <summary>D7: one audit line per file, with the path relative to the Temp folder only (no user name, no contents).</summary>
    private void LogEntry(CleanupMode mode, CleanupEntry entry)
    {
        var where = entry.RelativePath ?? $"(item {entry.Id.Value}, path not shown)";
        var level = entry.Outcome == CleanupOutcome.Failed ? LogLevel.Warning : LogLevel.Information;
        logger.Log(level, "Cleanup ({Mode}) {Outcome}: {RelativePath} (Windows error {Error}).", mode, entry.Outcome, where, entry.Win32Error);
    }

    private static CleanupEntry Entry(CandidateId id, ScanCandidate? candidate, AttemptResult result) =>
        new(id, candidate?.Path ?? string.Empty, candidate?.Decision.RelativePath, result.Outcome, result.Bytes, result.Win32Error);
}
