using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using static SafeClean.Core.Tests.Policies.TestData;

namespace SafeClean.Core.Tests.Policies;

public sealed class ProtectedLocationsTests
{
    [Theory]
    [InlineData(@"C:\Users\Example\Documents\thesis.docx")]
    [InlineData(@"C:\Users\Example\Documents")]
    [InlineData(@"C:\USERS\EXAMPLE\DOCUMENTS\thesis.docx")]
    [InlineData(@"C:\Windows\System32\kernel32.dll")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21\file")]
    [InlineData(@"C:\Users\Example")]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    public void ProtectsSubtreesExactPathsAndDriveRoots(string path)
    {
        Assert.True(Protected.Protects(path));
    }

    [Theory]
    [InlineData(@"C:\Users\Example\AppData\Local\Temp\a.tmp")]
    [InlineData(@"C:\Users\Example\Documents2\a.tmp")]
    [InlineData(@"C:\WindowsApps\a.tmp")]
    public void DoesNotProtectOrdinaryPathsBelowExactPaths(string path)
    {
        Assert.False(Protected.Protects(path));
    }

    [Theory]
    [InlineData(@"C:\", RootProblem.IsDriveRoot)]
    [InlineData(@"C:\Users\Example", RootProblem.IsProtectedLocation)]
    [InlineData(@"C:\Users\Example\AppData", RootProblem.IsProtectedLocation)]
    [InlineData(@"C:\Users\Example\Documents", RootProblem.IsProtectedLocation)]
    [InlineData(@"C:\Windows\Temp", RootProblem.InsideProtectedLocation)]
    [InlineData(@"C:\Users\Example\Documents\Temp", RootProblem.InsideProtectedLocation)]
    [InlineData(@"C:\Users\Example\AppData\Local\..x", null)]
    public void FindRootConflict(string root, RootProblem? expected)
    {
        Assert.Equal(expected, Protected.FindRootConflict(root));
    }

    [Fact]
    public void RootThatContainsAProtectedFolderIsRefused()
    {
        var locations = new ProtectedLocations([@"D:\Data\Desktop"], []);

        Assert.Equal(RootProblem.ContainsProtectedLocation, locations.FindRootConflict(@"D:\Data"));
    }

    [Fact]
    public void NormalTempRootHasNoConflict()
    {
        Assert.Null(Protected.FindRootConflict(TempRoot));
    }

    [Fact]
    public void EmptyEntriesAreIgnored_TrailingSeparatorsTrimmed_DuplicatesRemoved()
    {
        var locations = new ProtectedLocations([null, "", "  ", @"C:\Windows\", @"c:\windows"], []);

        Assert.Equal([@"C:\Windows"], locations.Subtrees);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData(@"\\server\share")]
    [InlineData(@"C:\a\..\b")]
    public void InvalidEntriesAreRejected(string entry)
    {
        Assert.Throws<ArgumentException>(() => new ProtectedLocations([entry], []));
        Assert.Throws<ArgumentException>(() => new ProtectedLocations([], [entry]));
    }
}
