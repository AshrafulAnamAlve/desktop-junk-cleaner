using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using static SafeClean.Core.Tests.Policies.TestData;

namespace SafeClean.Core.Tests.Policies;

public sealed class DryRunPlannerTests
{
    private static ScanSession Session(params FileFacts[] files)
    {
        var context = Context();
        var candidates = files
            .Select((facts, i) => new ScanCandidate(new CandidateId(i + 1), facts.Path, facts, CandidatePolicy.Evaluate(facts, context)))
            .ToList();
        return new ScanSession(Guid.NewGuid(), context.Root, Now, Now.AddSeconds(1), candidates, 1, 0, false);
    }

    private static readonly ScanSession Sample = Session(
        OldFile("a.tmp") with { Size = 100 },                                          // 1 eligible
        OldFile("b.log") with { Size = 200 },                                          // 2 eligible
        OldFile("setup.exe") with { Size = 1000 },                                     // 3 needs review
        OldFile("fresh.tmp") with { Size = 50, LastWriteTimeUtc = Now.AddHours(-1) }); // 4 skipped

    [Fact]
    public void CountsSelectedSelectableFiles()
    {
        var preview = DryRunPlanner.Create(Sample, [new CandidateId(1), new CandidateId(2), new CandidateId(3)]);

        Assert.Equal(3, preview.FileCount);
        Assert.Equal(1300, preview.EstimatedBytes);
        Assert.Equal(1, preview.NeedsReviewCount);
        Assert.Equal(0, preview.IgnoredCount);
        Assert.Equal(1, preview.FilesByCategory[CandidateCategory.TemporaryFiles]);
        Assert.Equal(1, preview.FilesByCategory[CandidateCategory.LogFiles]);
        Assert.Equal(1, preview.FilesByCategory[CandidateCategory.InstallerLeftovers]);
    }

    [Fact]
    public void SkippedAndUnknownIdsAreIgnored_NeverCounted()
    {
        var preview = DryRunPlanner.Create(Sample, [new CandidateId(4), new CandidateId(99), new CandidateId(1)]);

        Assert.Equal(1, preview.FileCount);
        Assert.Equal(100, preview.EstimatedBytes);
        Assert.Equal(2, preview.IgnoredCount);
    }

    [Fact]
    public void DuplicateIdsAreCountedOnce()
    {
        var preview = DryRunPlanner.Create(Sample, [new CandidateId(1), new CandidateId(1)]);

        Assert.Equal(1, preview.FileCount);
        Assert.Equal(100, preview.EstimatedBytes);
    }

    [Fact]
    public void EmptySelectionGivesEmptyPreview()
    {
        var preview = DryRunPlanner.Create(Sample, []);

        Assert.Equal(0, preview.FileCount);
        Assert.Equal(0, preview.EstimatedBytes);
        Assert.Empty(preview.FilesByCategory);
    }
}
