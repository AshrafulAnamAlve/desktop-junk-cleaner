using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.Core.Tests;

public sealed class SafetyDefaultsTests
{
    [Fact]
    public void UninitialisedCleanupMode_IsDryRun()
    {
        Assert.Equal(CleanupMode.DryRun, default(CleanupMode));
    }

    [Fact]
    public void AppStartsInDryRunMode()
    {
        Assert.Equal(CleanupMode.DryRun, SafetyDefaults.StartupMode);
    }

    [Fact]
    public void MinimumFileAge_IsAtLeastSevenDays()
    {
        Assert.True(SafetyDefaults.MinimumFileAge >= TimeSpan.FromDays(7));
    }

    [Fact]
    public void MaxScanDepth_IsBounded()
    {
        Assert.InRange(SafetyDefaults.MaxScanDepth, 1, 32);
    }
}
