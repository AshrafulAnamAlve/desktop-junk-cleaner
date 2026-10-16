using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.Core.Tests.Policies;

/// <summary>Every status, reason and problem the user can see must have a plain-English explanation.</summary>
public sealed class PolicyTextTests
{
    [Fact]
    public void EverySkipReasonHasText() =>
        Assert.All(Enum.GetValues<SkipReason>(), value => Assert.False(string.IsNullOrWhiteSpace(PolicyText.Describe(value))));

    [Fact]
    public void EveryRootProblemHasText() =>
        Assert.All(Enum.GetValues<RootProblem>(), value => Assert.False(string.IsNullOrWhiteSpace(PolicyText.Describe(value))));

    [Fact]
    public void EveryStatusHasText() =>
        Assert.All(Enum.GetValues<CandidateStatus>(), value => Assert.False(string.IsNullOrWhiteSpace(PolicyText.Describe(value))));

    [Fact]
    public void EveryCategoryHasText() =>
        Assert.All(Enum.GetValues<CandidateCategory>(), value => Assert.False(string.IsNullOrWhiteSpace(PolicyText.Describe(value))));

    [Fact]
    public void EveryCleanupOutcomeHasText() =>
        Assert.All(Enum.GetValues<CleanupOutcome>(), value => Assert.False(string.IsNullOrWhiteSpace(PolicyText.Describe(value))));

    [Fact]
    public void EveryCleanupRefusalHasText() =>
        Assert.All(Enum.GetValues<CleanupRefusal>(), value => Assert.False(string.IsNullOrWhiteSpace(PolicyText.Describe(value))));

    [Fact]
    public void UninitialisedCleanupOutcome_IsASkip_NeverADeletion()
    {
        Assert.Equal(CleanupOutcome.SkippedPolicy, default(CleanupOutcome));
    }

    [Fact]
    public void SkippedIsTheDefaultStatus()
    {
        // An uninitialised status must never mean "eligible".
        Assert.Equal(CandidateStatus.Skipped, default(CandidateStatus));
    }
}
