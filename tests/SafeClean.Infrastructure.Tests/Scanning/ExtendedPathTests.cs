using SafeClean.Infrastructure.Native;

namespace SafeClean.Infrastructure.Tests.Scanning;

public sealed class ExtendedPathTests
{
    [Theory]
    [InlineData(@"\\?\C:\Users\A\AppData\Local\Temp", @"C:\Users\A\AppData\Local\Temp")]
    [InlineData(@"\\?\D:\x.tmp", @"D:\x.tmp")]
    [InlineData(@"\\?\UNC\server\share\x", @"\\?\UNC\server\share\x")]
    [InlineData(@"\\?\unc\server\share\x", @"\\?\unc\server\share\x")]
    [InlineData(@"C:\already\plain", @"C:\already\plain")]
    [InlineData(@"\\?\Volume{1234}\x", @"Volume{1234}\x")]
    public void Remove_StripsOnlyTheDrivePrefix(string input, string expected)
    {
        Assert.Equal(expected, ExtendedPath.Remove(input));
    }

    [Fact]
    public void Add_PrefixesValidatedPath()
    {
        Assert.Equal(@"\\?\C:\Temp\a.tmp", ExtendedPath.Add(@"C:\Temp\a.tmp"));
    }
}
