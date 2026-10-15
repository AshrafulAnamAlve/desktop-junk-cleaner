using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using static SafeClean.Core.Tests.Policies.TestData;

namespace SafeClean.Core.Tests.Policies;

public sealed class RootPolicyTests
{
    [Fact]
    public void HealthyTempFolderIsApproved_UsingTheOsReportedPath()
    {
        var decision = RootPolicy.Evaluate(GoodRootFacts(), Protected);

        Assert.True(decision.IsApproved);
        Assert.Empty(decision.Problems);
        Assert.Equal(TempRoot, decision.Root.CanonicalPath);
        Assert.Equal(RootIdentity, decision.Root.Identity);
    }

    [Fact]
    public void ShortNameRequestedPath_CanonicalRootIsTheFinalLongPath()
    {
        var facts = GoodRootFacts() with { RequestedPath = @"C:\Users\EXAMPL~1\AppData\Local\Temp\" };

        var decision = RootPolicy.Evaluate(facts, Protected);

        Assert.True(decision.IsApproved);
        Assert.Equal(TempRoot, decision.Root.CanonicalPath);
    }

    public static TheoryData<string, RootFacts, RootProblem> SingleProblemCases() => new()
    {
        { "UNC final path", GoodRootFacts() with { FinalPath = @"\\server\share\Temp" }, RootProblem.InvalidPath },
        { "device prefix left on final path", GoodRootFacts() with { FinalPath = @"\\?\" + TempRoot }, RootProblem.InvalidPath },
        { "empty requested path", GoodRootFacts() with { RequestedPath = "" }, RootProblem.InvalidPath },
        { "traversal in requested path", GoodRootFacts() with { RequestedPath = TempRoot + @"\..\Temp" }, RootProblem.InvalidPath },
        { "removable or network drive", GoodRootFacts() with { IsFixedDrive = false }, RootProblem.NotFixedDrive },
        { "FAT32 drive", GoodRootFacts() with { FileSystemName = "FAT32" }, RootProblem.NotNtfs },
        { "exFAT drive", GoodRootFacts() with { FileSystemName = "exFAT" }, RootProblem.NotNtfs },
        { "a file, not a folder", GoodRootFacts() with { Attributes = FileAttributes.Archive }, RootProblem.NotADirectory },
        { "root is a junction", GoodRootFacts() with { Attributes = FileAttributes.Directory | FileAttributes.ReparsePoint }, RootProblem.RootIsReparsePoint },
        { "parent folder is a junction", GoodRootFacts() with { AnyAncestorIsReparsePoint = true }, RootProblem.AncestorIsReparsePoint },
        { "TEMP points to a different folder", GoodRootFacts() with { Identity = new FileIdentity(0x1234, 7UL) }, RootProblem.NotUserTempDirectory },
        { "identity could not be read", GoodRootFacts() with { Identity = default, ExpectedIdentity = default }, RootProblem.NotUserTempDirectory },
    };

    [Theory]
    [MemberData(nameof(SingleProblemCases))]
    public void EachRuleRefusesOnItsOwn(string scenario, RootFacts facts, RootProblem expected)
    {
        var decision = RootPolicy.Evaluate(facts, Protected);

        Assert.False(decision.IsApproved, scenario);
        Assert.Null(decision.Root);
        Assert.Equal([expected], decision.Problems);
    }

    [Theory]
    [InlineData(@"C:\", RootProblem.IsDriveRoot)]
    [InlineData(@"C:\Users\Example", RootProblem.IsProtectedLocation)]
    [InlineData(@"C:\Users\Example\Documents", RootProblem.IsProtectedLocation)]
    [InlineData(@"C:\Windows\Temp", RootProblem.InsideProtectedLocation)]
    [InlineData(@"C:\Users", RootProblem.IsProtectedLocation)]
    public void DangerousTempLocationsAreRefused(string tempPath, RootProblem expected)
    {
        // Even if identity matched (e.g. a misconfigured profile), these locations are never acceptable.
        var decision = RootPolicy.Evaluate(GoodRootFacts(tempPath), Protected);

        Assert.False(decision.IsApproved);
        Assert.Contains(expected, decision.Problems);
    }

    [Fact]
    public void ProtectedRequestedPathIsRefusedEvenIfFinalPathLooksFine()
    {
        var facts = GoodRootFacts() with { RequestedPath = @"C:\Users\Example\Documents" };

        var decision = RootPolicy.Evaluate(facts, Protected);

        Assert.Contains(RootProblem.IsProtectedLocation, decision.Problems);
    }

    [Fact]
    public void AllProblemsAreReportedTogether()
    {
        var facts = GoodRootFacts() with
        {
            IsFixedDrive = false,
            FileSystemName = "FAT32",
            AnyAncestorIsReparsePoint = true,
            Identity = new FileIdentity(9, 9UL),
        };

        var decision = RootPolicy.Evaluate(facts, Protected);

        Assert.Equal(
            [RootProblem.NotFixedDrive, RootProblem.NotNtfs, RootProblem.AncestorIsReparsePoint, RootProblem.NotUserTempDirectory],
            decision.Problems);
    }

    [Fact]
    public void FileIdentity_UnknownNeverMatches()
    {
        Assert.False(default(FileIdentity).Matches(default));
        Assert.False(RootIdentity.Matches(default));
        Assert.True(RootIdentity.Matches(new FileIdentity(0x1234, 42UL)));
        Assert.False(RootIdentity.Matches(new FileIdentity(0x9999, 42UL)));
    }
}
