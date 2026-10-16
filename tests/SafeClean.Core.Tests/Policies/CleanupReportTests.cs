using SafeClean.Core.Models;
using static SafeClean.Core.Tests.Policies.TestData;

namespace SafeClean.Core.Tests.Policies;

public sealed class CleanupReportTests
{
    private static CleanupEntry Entry(int id, CleanupOutcome outcome, long bytes) =>
        new(new CandidateId(id), $@"{TempRoot}\{id}.tmp", $"{id}.tmp", outcome, bytes, 0);

    [Fact]
    public void BytesFreed_CountsOnlyDeletedFiles()
    {
        // Skipped and failed entries carry sizes here on purpose: they must still never be counted.
        var report = CleanupReport.Completed(
            CleanupMode.Real,
            Now,
            Now.AddSeconds(1),
            [
                Entry(1, CleanupOutcome.Deleted, 100),
                Entry(2, CleanupOutcome.Deleted, 50),
                Entry(3, CleanupOutcome.SkippedChanged, 999),
                Entry(4, CleanupOutcome.SkippedInUse, 999),
                Entry(5, CleanupOutcome.Failed, 999),
                Entry(6, CleanupOutcome.SkippedCancelled, 999),
            ],
            wasCancelled: false,
            stoppedByError: false);

        Assert.Equal(150, report.BytesFreed);
        Assert.Equal(0, report.BytesWouldFree);
        Assert.Equal(2, report.DeletedCount);
        Assert.Equal(3, report.SkippedCount);
        Assert.Equal(1, report.FailedCount);
        Assert.Equal([1, 2], report.Deleted.Select(e => e.Id.Value));
        Assert.Equal([3, 4, 6], report.Skipped.Select(e => e.Id.Value));
        Assert.Equal([5], report.Failed.Select(e => e.Id.Value));
        Assert.False(report.WasRefused);
    }

    [Fact]
    public void DryRunReport_CountsWouldDeleteSeparately_AndFreesNothing()
    {
        var report = CleanupReport.Completed(
            CleanupMode.DryRun,
            Now,
            Now,
            [Entry(1, CleanupOutcome.WouldDelete, 100), Entry(2, CleanupOutcome.SkippedMissing, 0)],
            wasCancelled: false,
            stoppedByError: false);

        Assert.Equal(0, report.BytesFreed);
        Assert.Equal(0, report.DeletedCount);
        Assert.Equal(1, report.WouldDeleteCount);
        Assert.Equal(100, report.BytesWouldFree);
        Assert.Equal(1, report.SkippedCount);
    }

    [Fact]
    public void DryRunReport_CanNeverContainADeletion() =>
        Assert.Throws<ArgumentException>(() =>
            CleanupReport.Completed(CleanupMode.DryRun, Now, Now, [Entry(1, CleanupOutcome.Deleted, 1)], false, false));

    [Fact]
    public void RealReport_CanNeverContainWouldDelete() =>
        Assert.Throws<ArgumentException>(() =>
            CleanupReport.Completed(CleanupMode.Real, Now, Now, [Entry(1, CleanupOutcome.WouldDelete, 1)], false, false));

    [Fact]
    public void RefusedReport_HasNoEntries_AndExplainsWhy()
    {
        var report = CleanupReport.Refused(CleanupMode.Real, Now, CleanupRefusal.RootRefused, [RootProblem.NotFixedDrive]);

        Assert.True(report.WasRefused);
        Assert.Equal(CleanupRefusal.RootRefused, report.Refusal);
        Assert.Equal([RootProblem.NotFixedDrive], report.RootProblems);
        Assert.Empty(report.Entries);
        Assert.Equal(0, report.BytesFreed);
    }
}
