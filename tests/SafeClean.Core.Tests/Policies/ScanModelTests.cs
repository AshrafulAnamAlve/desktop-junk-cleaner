using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using static SafeClean.Core.Tests.Policies.TestData;

namespace SafeClean.Core.Tests.Policies;

public sealed class ScanModelTests
{
    [Fact]
    public void Skip_IsAlwaysSkippedWithGivenReasons()
    {
        var decision = CandidatePolicy.Skip(TempRoot + @"\locked-folder", Context(), SkipReason.IsDirectory, SkipReason.Inaccessible);

        Assert.Equal(CandidateStatus.Skipped, decision.Status);
        Assert.False(decision.IsSelectable);
        Assert.Equal([SkipReason.IsDirectory, SkipReason.Inaccessible], decision.Reasons);
        Assert.Equal("locked-folder", decision.RelativePath);
    }

    [Fact]
    public void Skip_RemovesDuplicateReasons_AndRequiresAtLeastOne()
    {
        var decision = CandidatePolicy.Skip(TempRoot + @"\x.tmp", Context(), SkipReason.Inaccessible, SkipReason.Inaccessible);

        Assert.Equal([SkipReason.Inaccessible], decision.Reasons);
        Assert.Throws<ArgumentException>(() => CandidatePolicy.Skip(TempRoot + @"\x.tmp", Context()));
    }

    [Theory]
    [InlineData(@"C:\Users\Example\Documents\x.tmp")]
    [InlineData(@"C:\Temp\..\x.tmp")]
    public void Skip_PathOutsideRootOrInvalidHasNoRelativePath(string path)
    {
        Assert.Null(CandidatePolicy.Skip(path, Context(), SkipReason.Inaccessible).RelativePath);
    }

    [Fact]
    public void Summary_SeparatesStatusesAndCountsBytesFromKnownSizesOnly()
    {
        var context = Context();
        ScanCandidate Candidate(int id, string name, long size, CandidateStatus status)
        {
            var facts = OldFile(name) with { Size = size };
            var decision = status == CandidateStatus.Skipped
                ? CandidatePolicy.Skip(facts.Path, context, SkipReason.TooRecent)
                : CandidatePolicy.Evaluate(facts, context);
            Assert.Equal(status, decision.Status);
            return new ScanCandidate(new CandidateId(id), facts.Path, facts, decision);
        }

        var unreadable = new ScanCandidate(new CandidateId(9), TempRoot + @"\unreadable", null, CandidatePolicy.Skip(TempRoot + @"\unreadable", context, SkipReason.Inaccessible));
        var summary = ScanSummary.From(
        [
            Candidate(1, "a.tmp", 100, CandidateStatus.Eligible),
            Candidate(2, "b.tmp", 50, CandidateStatus.Eligible),
            Candidate(3, "setup.exe", 1000, CandidateStatus.NeedsReview),
            Candidate(4, "new.tmp", 7, CandidateStatus.Skipped),
            unreadable,
        ]);

        Assert.Equal(new ScanSummary(2, 150, 1, 1000, 2, 7), summary);
        Assert.Equal(5, summary.TotalCount);
        Assert.Equal(1150, summary.EstimatedRecoverableBytes);
        Assert.Equal(0, unreadable.Size);
    }

    [Fact]
    public void NegativeSizeNeverReducesTotals()
    {
        var facts = OldFile("odd.tmp") with { Size = -500 };
        var candidate = new ScanCandidate(new CandidateId(1), facts.Path, facts, CandidatePolicy.Evaluate(facts, Context()));

        Assert.Equal(0, candidate.Size);
        Assert.Equal(0, ScanSummary.From([candidate]).SkippedBytes);
    }

    [Fact]
    public void ScanResult_RefusalMustNotBeApproved()
    {
        var approved = RootPolicy.Evaluate(GoodRootFacts(), Protected);

        Assert.Throws<ArgumentException>(() => ScanResult.Refused(approved));
    }
}
