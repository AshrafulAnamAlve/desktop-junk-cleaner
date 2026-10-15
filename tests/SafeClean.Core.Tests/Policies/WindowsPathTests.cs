using SafeClean.Core.Policies;

namespace SafeClean.Core.Tests.Policies;

public sealed class WindowsPathTests
{
    [Theory]
    [InlineData(@"C:\Temp", @"C:\Temp\a.tmp", true)]
    [InlineData(@"C:\Temp", @"C:\Temp\sub\deep\a.tmp", true)]
    [InlineData(@"C:\Temp", @"C:\Temp", false)]
    [InlineData(@"C:\Temp", @"C:\Temp\", false)]
    [InlineData(@"C:\Temp", @"C:\Temp2\a.tmp", false)]
    [InlineData(@"C:\Temp", @"C:\Tem", false)]
    [InlineData(@"C:\Temp", @"C:\Other\a.tmp", false)]
    [InlineData(@"C:\Temp", @"D:\Temp\a.tmp", false)]
    [InlineData(@"C:\", @"C:\a.tmp", true)]
    [InlineData(@"C:\", @"C:\", false)]
    [InlineData(@"C:\", @"D:\a.tmp", false)]
    public void IsStrictlyInside_RequiresSeparatorAfterPrefix(string parent, string candidate, bool expected)
    {
        Assert.Equal(expected, WindowsPath.IsStrictlyInside(parent, candidate, StringComparison.Ordinal));
    }

    [Fact]
    public void IsStrictlyInside_CaseHandlingDependsOnComparison()
    {
        Assert.False(WindowsPath.IsStrictlyInside(@"C:\Temp", @"C:\TEMP\a.tmp", StringComparison.Ordinal));
        Assert.True(WindowsPath.IsStrictlyInside(@"C:\Temp", @"C:\TEMP\a.tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(StringComparison.CurrentCulture)]
    [InlineData(StringComparison.CurrentCultureIgnoreCase)]
    [InlineData(StringComparison.InvariantCulture)]
    [InlineData(StringComparison.InvariantCultureIgnoreCase)]
    public void CultureAwareComparisonsAreRefused(StringComparison comparison)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowsPath.IsStrictlyInside(@"C:\Temp", @"C:\Temp\a", comparison));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowsPath.IsSameOrInside(@"C:\Temp", @"C:\Temp\a", comparison));
    }

    [Theory]
    [InlineData(@"C:\Temp", @"C:\Temp", true)]
    [InlineData(@"C:\Temp", @"C:\Temp\a", true)]
    [InlineData(@"C:\Temp", @"C:\Temp2", false)]
    public void IsSameOrInside(string parent, string candidate, bool expected)
    {
        Assert.Equal(expected, WindowsPath.IsSameOrInside(parent, candidate, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"C:\Temp\", @"C:\Temp")]
    [InlineData(@"C:\Temp", @"C:\Temp")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\Temp\\", @"C:\Temp\")]
    public void TrimTrailingSeparator_RemovesOneSeparatorButKeepsDriveRoot(string input, string expected)
    {
        Assert.Equal(expected, WindowsPath.TrimTrailingSeparator(input));
    }

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"z:\", true)]
    [InlineData(@"C:\Temp", false)]
    [InlineData("C:", false)]
    [InlineData(@"\\", false)]
    public void IsDriveRoot(string path, bool expected)
    {
        Assert.Equal(expected, WindowsPath.IsDriveRoot(path));
    }

    [Fact]
    public void GetRelativePath_ReturnsPartBelowRoot()
    {
        Assert.Equal(@"sub\a.tmp", WindowsPath.GetRelativePath(@"C:\Temp", @"C:\Temp\sub\a.tmp"));
        Assert.Equal("a.tmp", WindowsPath.GetRelativePath(@"C:\", @"C:\a.tmp"));
        Assert.Throws<ArgumentException>(() => WindowsPath.GetRelativePath(@"C:\Temp", @"C:\Temp2\a.tmp"));
    }

    [Theory]
    [InlineData("a.tmp", 0)]
    [InlineData(@"x\a.tmp", 1)]
    [InlineData(@"x\y\z\a.tmp", 3)]
    public void CountFolderLevels(string relativePath, int expected)
    {
        Assert.Equal(expected, WindowsPath.CountFolderLevels(relativePath));
    }

    [Theory]
    [InlineData("a.tmp", ".tmp")]
    [InlineData("archive.tar.gz", ".gz")]
    [InlineData(".hidden", ".hidden")]
    [InlineData("noextension", "")]
    public void GetExtension(string name, string expected)
    {
        Assert.Equal(expected, WindowsPath.GetExtension(name));
    }
}
