using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.Core.Tests.Policies;

public sealed class PathValidatorTests
{
    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Temp\a.tmp")]
    [InlineData(@"c:\temp\a.tmp")]
    [InlineData(@"D:\Data\Sub Folder\file with spaces.tmp")]
    [InlineData(@"C:\Temp\.leading-dot.tmp")]
    [InlineData(@"C:\Temp\ leading space.tmp")]
    [InlineData(@"C:\Temp\~DF1234.tmp")]
    [InlineData(@"C:\Temp\report 😀.tmp")]
    [InlineData(@"C:\Temp\تقرير.tmp")]
    [InlineData("C:\\Temp\\cafe\u0301.tmp")]
    [InlineData("C:\\Temp\\\u202Egpj.exe")]
    [InlineData(@"C:\Temp\CONSOLE.txt")]
    [InlineData(@"C:\Temp\nullify.tmp")]
    [InlineData(@"C:\Temp\COM10")]
    [InlineData(@"C:\Temp\LPT.txt")]
    [InlineData(@"C:\Temp\COMX.txt")]
    [InlineData(@"C:\Temp\a.b.c.tmp")]
    public void AcceptsCanonicalDrivePaths(string path)
    {
        Assert.Null(PathValidator.FindProblem(path));
        Assert.True(PathValidator.IsValid(path));
    }

    [Theory]
    [InlineData(null, PathProblem.Empty)]
    [InlineData("", PathProblem.Empty)]
    [InlineData("   ", PathProblem.Empty)]
    [InlineData("a.tmp", PathProblem.NotFullyQualified)]
    [InlineData(@"Temp\a.tmp", PathProblem.NotFullyQualified)]
    [InlineData("C:a.tmp", PathProblem.NotFullyQualified)]
    [InlineData(@"\Temp\a.tmp", PathProblem.NotFullyQualified)]
    [InlineData(@"1:\Temp\a.tmp", PathProblem.NotFullyQualified)]
    [InlineData(@"\\server\share\a.tmp", PathProblem.NetworkPath)]
    [InlineData(@"\\?\C:\Temp\a.tmp", PathProblem.DevicePath)]
    [InlineData(@"\\.\C:\Temp\a.tmp", PathProblem.DevicePath)]
    [InlineData(@"\??\C:\Temp\a.tmp", PathProblem.DevicePath)]
    [InlineData(@"\\?\UNC\server\share\a.tmp", PathProblem.DevicePath)]
    [InlineData("C:/Temp/a.tmp", PathProblem.ForwardSlash)]
    [InlineData(@"C:\Temp/a.tmp", PathProblem.ForwardSlash)]
    [InlineData(@"C:\Temp\\a.tmp", PathProblem.EmptySegment)]
    [InlineData(@"C:\Temp\", PathProblem.EmptySegment)]
    [InlineData(@"C:\Temp\.\a.tmp", PathProblem.DotSegment)]
    [InlineData(@"C:\Temp\..\Users\Example\Documents\a.docx", PathProblem.DotSegment)]
    [InlineData(@"C:\Temp\..", PathProblem.DotSegment)]
    [InlineData(@"C:\Temp\a.txt:secret", PathProblem.AlternateDataStream)]
    [InlineData(@"C:\Temp\a.txt::$DATA", PathProblem.AlternateDataStream)]
    [InlineData(@"C:\Temp\a<b.tmp", PathProblem.InvalidCharacter)]
    [InlineData(@"C:\Temp\a>b.tmp", PathProblem.InvalidCharacter)]
    [InlineData("C:\\Temp\\a\"b.tmp", PathProblem.InvalidCharacter)]
    [InlineData(@"C:\Temp\a|b.tmp", PathProblem.InvalidCharacter)]
    [InlineData(@"C:\Temp\a?.tmp", PathProblem.InvalidCharacter)]
    [InlineData(@"C:\Temp\*.tmp", PathProblem.InvalidCharacter)]
    [InlineData("C:\\Temp\\a\u0001.tmp", PathProblem.ControlCharacter)]
    [InlineData("C:\\Temp\\a\0.tmp", PathProblem.ControlCharacter)]
    [InlineData("C:\\Temp\\a\t.tmp", PathProblem.ControlCharacter)]
    [InlineData(@"C:\Temp\a.tmp.", PathProblem.TrailingDotOrSpace)]
    [InlineData(@"C:\Temp\a.tmp ", PathProblem.TrailingDotOrSpace)]
    [InlineData(@"C:\Temp\folder.\a.tmp", PathProblem.TrailingDotOrSpace)]
    [InlineData(@"C:\Temp\CON", PathProblem.ReservedDeviceName)]
    [InlineData(@"C:\Temp\con", PathProblem.ReservedDeviceName)]
    [InlineData(@"C:\Temp\nul.txt", PathProblem.ReservedDeviceName)]
    [InlineData(@"C:\Temp\NUL.tar.gz", PathProblem.ReservedDeviceName)]
    [InlineData(@"C:\Temp\CON .txt", PathProblem.ReservedDeviceName)]
    [InlineData(@"C:\Temp\PRN\a.tmp", PathProblem.ReservedDeviceName)]
    [InlineData(@"C:\Temp\AUX.log", PathProblem.ReservedDeviceName)]
    [InlineData(@"C:\Temp\COM1.log", PathProblem.ReservedDeviceName)]
    [InlineData(@"C:\Temp\com0", PathProblem.ReservedDeviceName)]
    [InlineData(@"C:\Temp\LPT9", PathProblem.ReservedDeviceName)]
    [InlineData("C:\\Temp\\COM\u00B9.txt", PathProblem.ReservedDeviceName)]
    [InlineData("C:\\Temp\\LPT\u00B3", PathProblem.ReservedDeviceName)]
    [InlineData(@"C:\Temp\CONIN$", PathProblem.ReservedDeviceName)]
    [InlineData(@"C:\Temp\conout$.txt", PathProblem.ReservedDeviceName)]
    public void RejectsUnsafePaths(string? path, PathProblem expected)
    {
        Assert.Equal(expected, PathValidator.FindProblem(path));
        Assert.False(PathValidator.IsValid(path));
    }

    [Fact]
    public void RejectsNameLongerThan255Characters()
    {
        Assert.Equal(PathProblem.SegmentTooLong, PathValidator.FindProblem(@"C:\Temp\" + new string('a', 256)));
        Assert.Null(PathValidator.FindProblem(@"C:\Temp\" + new string('a', 255)));
    }

    [Fact]
    public void RejectsPathLongerThanWindowsMaximum()
    {
        Assert.Equal(PathProblem.TooLong, PathValidator.FindProblem(@"C:\" + new string('a', PathValidator.MaxPathLength)));
    }

    [Fact]
    public void AcceptsLongPathsBeyondTheOld260CharacterLimit()
    {
        var path = @"C:\Temp\" + string.Join('\\', Enumerable.Repeat(new string('d', 60), 6)) + @"\file.tmp";

        Assert.True(path.Length > 260);
        Assert.Null(PathValidator.FindProblem(path));
    }

    [Fact]
    public void RejectsLoneSurrogates()
    {
        // Built in code, not [InlineData]: attribute strings are stored as UTF-8, which silently
        // replaces a lone surrogate with U+FFFD and would hide the case being tested.
        Assert.Equal(PathProblem.InvalidCharacter, PathValidator.FindProblem(@"C:\Temp\bad" + '\uD800' + ".tmp"));
        Assert.Equal(PathProblem.InvalidCharacter, PathValidator.FindProblem(@"C:\Temp\bad" + '\uDC00' + ".tmp"));
        Assert.Equal(PathProblem.InvalidCharacter, PathValidator.FindProblem(@"C:\Temp\end" + '\uD834'));
        Assert.Equal(PathProblem.InvalidCharacter, PathValidator.FindProblem(@"C:\Temp\" + '\uDD1E' + '\uD834' + ".tmp"));
    }

    [Fact]
    public void AcceptsValidSurrogatePairs()
    {
        // "𝄞" (U+1D11E) is stored as two UTF-16 code units.
        Assert.Null(PathValidator.FindProblem("C:\\Temp\\music\uD834\uDD1E.tmp"));
    }
}
