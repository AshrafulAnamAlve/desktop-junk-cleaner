using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using static SafeClean.Core.Tests.Policies.TestData;

namespace SafeClean.Core.Tests.Policies;

/// <summary>The cleanup checks of docs/SECURITY.md §5, with synthetic data only. Nothing here touches the disk.</summary>
public sealed class CleanupPolicyTests
{
    private const string OtherTempRoot = @"C:\Users\Example\AppData\Local\Temp2";

    private static ScanCandidate Candidate(FileFacts facts, int id = 1) =>
        new(new CandidateId(id), facts.Path, facts, CandidatePolicy.Evaluate(facts, Context()));

    private static ScanSession Session(DateTimeOffset? startedAt = null)
    {
        var start = startedAt ?? Now;
        return new ScanSession(Guid.NewGuid(), ApprovedRoot(), start, start.AddSeconds(5), [], 1, 0, false);
    }

    private static RootDecision RootNow(RootFacts facts) => RootPolicy.Evaluate(facts, Protected);

    // ---- Whole request, before any file is touched ----

    [Theory]
    [InlineData(CleanupMode.DryRun)]
    [InlineData(CleanupMode.Real)]
    public void FreshScan_NormalUser_IsAccepted(CleanupMode mode) =>
        Assert.Null(CleanupPolicy.CheckRequest(mode, isElevated: false, Session(), Now.AddMinutes(5)));

    [Theory]
    [InlineData(CleanupMode.DryRun)]
    [InlineData(CleanupMode.Real)]
    public void Elevated_IsRefused_InEveryMode(CleanupMode mode) =>
        Assert.Equal(CleanupRefusal.Elevated, CleanupPolicy.CheckRequest(mode, isElevated: true, Session(), Now));

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void UnknownMode_IsRefused_NeverTreatedAsReal(int value) =>
        Assert.Equal(CleanupRefusal.InvalidMode, CleanupPolicy.CheckRequest((CleanupMode)value, isElevated: false, Session(), Now));

    [Fact]
    public void ScanExactlyAtTheAgeLimit_IsAccepted() =>
        Assert.Null(CleanupPolicy.CheckRequest(CleanupMode.Real, false, Session(), Now + SafetyDefaults.MaxScanSessionAge));

    [Fact]
    public void ScanPastTheAgeLimit_IsRefused() =>
        Assert.Equal(
            CleanupRefusal.SessionExpired,
            CleanupPolicy.CheckRequest(CleanupMode.Real, false, Session(), Now + SafetyDefaults.MaxScanSessionAge + TimeSpan.FromTicks(1)));

    [Fact]
    public void ScanFromTheFuture_IsRefused() =>
        Assert.Equal(CleanupRefusal.SessionExpired, CleanupPolicy.CheckRequest(CleanupMode.Real, false, Session(), Now.AddSeconds(-1)));

    // ---- The Temp folder, checked again ----

    [Fact]
    public void SameRoot_IsAccepted() =>
        Assert.Null(CleanupPolicy.CheckRoot(Session(), RootNow(GoodRootFacts())));

    [Fact]
    public void RootThatNowFailsTheRules_IsRefused() =>
        Assert.Equal(CleanupRefusal.RootRefused, CleanupPolicy.CheckRoot(Session(), RootNow(GoodRootFacts() with { IsFixedDrive = false })));

    [Fact]
    public void RootAtAnotherPath_IsRefusedAsChanged() =>
        Assert.Equal(CleanupRefusal.RootChanged, CleanupPolicy.CheckRoot(Session(), RootNow(GoodRootFacts(OtherTempRoot))));

    [Fact]
    public void SamePathButDifferentFolder_IsRefusedAsChanged()
    {
        var replacement = new FileIdentity(0x1234, 99UL);
        var decision = RootNow(GoodRootFacts() with { Identity = replacement, ExpectedIdentity = replacement });

        Assert.Equal(CleanupRefusal.RootChanged, CleanupPolicy.CheckRoot(Session(), decision));
    }

    // ---- D1 + D2: the candidate as recorded ----

    [Fact]
    public void EligibleCandidate_PassesTheRecordedChecks() =>
        Assert.Null(CleanupPolicy.CheckRecorded(Candidate(OldFile("a.tmp")), Context()));

    [Fact]
    public void NeedsReviewCandidate_PassesTheRecordedChecks() =>
        Assert.Null(CleanupPolicy.CheckRecorded(Candidate(OldFile("setup.exe")), Context()));

    [Fact]
    public void SkippedCandidate_IsNeverActedOn() =>
        Assert.Equal(CleanupOutcome.SkippedPolicy, CleanupPolicy.CheckRecorded(Candidate(OldFile("fresh.tmp") with { LastWriteTimeUtc = Now }), Context()));

    [Fact]
    public void CandidateWithoutFacts_IsNeverActedOn() =>
        Assert.Equal(CleanupOutcome.SkippedPolicy, CleanupPolicy.CheckRecorded(Candidate(OldFile("a.tmp")) with { Facts = null }, Context()));

    [Fact]
    public void CandidateWhosePathDisagreesWithItsFacts_IsNeverActedOn() =>
        Assert.Equal(CleanupOutcome.SkippedPolicy, CleanupPolicy.CheckRecorded(Candidate(OldFile("a.tmp")) with { Path = TempRoot + @"\b.tmp" }, Context()));

    [Fact]
    public void ForgedEligibleDecision_ForAPersonalFile_IsCaughtAsProtected()
    {
        // The engine never trusts the recorded verdict: it judges the recorded facts again.
        var thesis = OldFile("a.tmp") with { Path = Profile + @"\Documents\thesis.docx", FinalPath = Profile + @"\Documents\thesis.docx" };
        var forged = new ScanCandidate(new CandidateId(1), thesis.Path, thesis, new CandidateDecision(CandidateStatus.Eligible, [], CandidateCategory.Other, "thesis.docx"));

        Assert.Equal(CleanupOutcome.SkippedProtected, CleanupPolicy.CheckRecorded(forged, Context()));
    }

    [Fact]
    public void ForgedEligibleDecision_OutsideTheRoot_IsRefused()
    {
        var elsewhere = OldFile("a.tmp") with { Path = @"D:\Data\a.tmp", FinalPath = @"D:\Data\a.tmp" };
        var forged = new ScanCandidate(new CandidateId(1), elsewhere.Path, elsewhere, new CandidateDecision(CandidateStatus.Eligible, [], CandidateCategory.TemporaryFiles, "a.tmp"));

        Assert.Equal(CleanupOutcome.SkippedPolicy, CleanupPolicy.CheckRecorded(forged, Context()));
    }

    [Fact]
    public void FolderProtectedSinceTheScan_IsSkippedAsProtected()
    {
        var stricter = new ProtectedLocations(Protected.Subtrees.Append(TempRoot + @"\app"), Protected.ExactPaths);

        Assert.Equal(CleanupOutcome.SkippedProtected, CleanupPolicy.CheckRecorded(Candidate(OldFile(@"app\a.tmp")), Context(stricter)));
    }

    [Fact]
    public void FileExcludedSinceTheScan_IsSkipped() =>
        Assert.Equal(CleanupOutcome.SkippedPolicy, CleanupPolicy.CheckRecorded(Candidate(OldFile(@"app\a.tmp")), Context(exclusions: [TempRoot + @"\app"])));

    [Fact]
    public void CandidateFromADifferentRoot_IsSkipped()
    {
        var otherRoot = new CandidatePolicyContext(ApprovedRoot(OtherTempRoot), Protected, Now);

        Assert.Equal(CleanupOutcome.SkippedPolicy, CleanupPolicy.CheckRecorded(Candidate(OldFile("a.tmp")), otherRoot));
    }

    [Fact]
    public void FileTooRecentUnderStricterAge_IsSkipped() =>
        Assert.Equal(CleanupOutcome.SkippedPolicy, CleanupPolicy.CheckRecorded(Candidate(OldFile("a.tmp")), Context(minimumAge: TimeSpan.FromDays(60))));

    // ---- D4: what Windows reports now, through the cleanup handle ----

    [Fact]
    public void UnchangedFile_PassesTheCurrentChecks()
    {
        var facts = OldFile("a.tmp");

        Assert.Null(CleanupPolicy.CheckCurrent(Candidate(facts), facts, Context()));
    }

    [Fact]
    public void UnchangedNeedsReviewFile_PassesTheCurrentChecks()
    {
        var facts = OldFile("setup.exe");

        Assert.Null(CleanupPolicy.CheckCurrent(Candidate(facts), facts, Context()));
    }

    [Theory]
    [InlineData("new identity")]
    [InlineData("unknown identity")]
    [InlineData("size")]
    [InlineData("last write time")]
    [InlineData("creation time")]
    [InlineData("now read-only")]
    [InlineData("now encrypted")]
    [InlineData("now sparse")]
    [InlineData("archive bit cleared")]
    [InlineData("final path outside root")]
    [InlineData("final path elsewhere in root")]
    [InlineData("different path")]
    [InlineData("hard link added")]
    [InlineData("delete pending")]
    [InlineData("now a reparse point")]
    [InlineData("now a directory")]
    [InlineData("modified just now")]
    public void AnyDifferenceFromTheScan_IsSkippedAsChanged(string change)
    {
        var recorded = OldFile("a.tmp");
        var current = change switch
        {
            "new identity" => recorded with { Identity = new FileIdentity(0x1234, 2000UL) },
            "unknown identity" => recorded with { Identity = default },
            "size" => recorded with { Size = recorded.Size + 1 },
            "last write time" => recorded with { LastWriteTimeUtc = recorded.LastWriteTimeUtc.AddSeconds(1) },
            "creation time" => recorded with { CreationTimeUtc = recorded.CreationTimeUtc.AddSeconds(-1) },
            "now read-only" => recorded with { Attributes = recorded.Attributes | FileAttributes.ReadOnly },
            "now encrypted" => recorded with { Attributes = recorded.Attributes | FileAttributes.Encrypted },
            "now sparse" => recorded with { Attributes = recorded.Attributes | FileAttributes.SparseFile },
            "archive bit cleared" => recorded with { Attributes = FileAttributes.Normal },
            "final path outside root" => recorded with { FinalPath = Profile + @"\Documents\a.tmp" },
            "final path elsewhere in root" => recorded with { FinalPath = TempRoot + @"\other.tmp" },
            "different path" => recorded with { Path = TempRoot + @"\b.tmp", FinalPath = TempRoot + @"\b.tmp" },
            "hard link added" => recorded with { LinkCount = 2 },
            "delete pending" => recorded with { LinkCount = 0 },
            "now a reparse point" => recorded with { Attributes = recorded.Attributes | FileAttributes.ReparsePoint },
            "now a directory" => recorded with { Attributes = FileAttributes.Directory },
            "modified just now" => recorded with { LastWriteTimeUtc = Now },
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        Assert.Equal(CleanupOutcome.SkippedChanged, CleanupPolicy.CheckCurrent(Candidate(recorded), current, Context()));
    }

    [Fact]
    public void CurrentCheck_OfASkippedCandidate_NeverPasses_EvenIfNothingChanged()
    {
        // "Still skipped" must not count as "unchanged": this step can't rely on D1 having run first.
        var fresh = OldFile("fresh.tmp") with { LastWriteTimeUtc = Now.AddHours(-1) };
        var skipped = Candidate(fresh);
        Assert.Equal(CandidateStatus.Skipped, skipped.Decision.Status);

        Assert.Equal(CleanupOutcome.SkippedPolicy, CleanupPolicy.CheckCurrent(skipped, fresh, Context()));
    }

    [Fact]
    public void RecordedCheck_OfAForgedEligibleVerdict_ForATooRecentFile_NeverPasses()
    {
        // A stored "eligible" verdict doesn't help a file whose own facts fail the rules today.
        var fresh = OldFile("fresh.tmp") with { LastWriteTimeUtc = Now.AddHours(-1) };
        var claimsEligible = new ScanCandidate(new CandidateId(1), fresh.Path, fresh, new CandidateDecision(CandidateStatus.Eligible, [], CandidateCategory.TemporaryFiles, "fresh.tmp"));

        Assert.Equal(CleanupOutcome.SkippedPolicy, CleanupPolicy.CheckRecorded(claimsEligible, Context()));
    }

    [Fact]
    public void CurrentCheck_WithoutRecordedFacts_IsSkipped()
    {
        var facts = OldFile("a.tmp");

        Assert.Equal(CleanupOutcome.SkippedPolicy, CleanupPolicy.CheckCurrent(Candidate(facts) with { Facts = null }, facts, Context()));
    }
}
