using SafeClean.Core.Models;
using SafeClean.Infrastructure.Native;
using SafeClean.Infrastructure.Scanning;
using SafeClean.Infrastructure.Tests.Sandbox;

namespace SafeClean.Infrastructure.Tests.Scanning;

public sealed class FileInspectorTests : IDisposable
{
    private readonly TestSandbox _sandbox = TestSandbox.Create();
    private readonly FileInspector _inspector = new();

    public void Dispose() => _sandbox.Dispose();

    [Fact]
    public void RequestsOnlyTheReadAttributesRight()
    {
        Assert.Equal(NativeMethods.FILE_READ_ATTRIBUTES, FileInspector.DesiredAccess);
        Assert.Equal(0x7u, FileInspector.ShareMode); // read + write + delete sharing: never blocks other programs
    }

    [Fact]
    public void ReportsTheSameFactsAsDotNet()
    {
        var path = _sandbox.CreateFile("plain.tmp", content: new string('x', 1234));

        var result = _inspector.InspectFile(path);

        Assert.True(result.Succeeded);
        var facts = result.Facts;
        Assert.Equal(path, facts.Path);
        Assert.Equal(path, facts.FinalPath);
        Assert.Equal(1234, facts.Size);
        Assert.Equal(File.GetAttributes(path), facts.Attributes);
        Assert.Equal(File.GetCreationTimeUtc(path), facts.CreationTimeUtc.UtcDateTime);
        Assert.Equal(File.GetLastWriteTimeUtc(path), facts.LastWriteTimeUtc.UtcDateTime);
        Assert.Equal(1u, facts.LinkCount);
        Assert.False(facts.Identity.IsUnknown);
    }

    [Fact]
    public void DifferentFilesHaveDifferentIdentities_ReplacedFileGetsNewIdentity()
    {
        var a = _sandbox.CreateFile("a.tmp");
        var b = _sandbox.CreateFile("b.tmp");
        var before = _inspector.InspectFile(a).Facts!.Identity;

        File.Delete(a);
        _sandbox.CreateFile("a.tmp");
        var after = _inspector.InspectFile(a).Facts!.Identity;

        Assert.NotEqual(before, _inspector.InspectFile(b).Facts!.Identity);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void FolderInspectionReportsDirectoryAndFileSystem()
    {
        var folder = _sandbox.CreateDirectory("folder");

        var result = _inspector.InspectDirectory(folder);

        Assert.True(result.Succeeded);
        Assert.True(result.Facts.Attributes.HasFlag(FileAttributes.Directory));
        Assert.Equal("NTFS", result.FileSystemName);
    }

    [Fact]
    public void MissingFileIsReportedAsMissing()
    {
        var result = _inspector.InspectFile(_sandbox.PathFor("does-not-exist.tmp"));

        Assert.False(result.Succeeded);
        Assert.True(result.IsMissing);
    }

    [Theory]
    [InlineData(@"relative.tmp")]
    [InlineData(@"C:\Temp\a.tmp:stream")]
    [InlineData(@"C:\Temp\..\a.tmp")]
    [InlineData(@"\\?\C:\Temp\a.tmp")]
    public void InvalidPathIsRefusedWithoutAskingWindows(string path)
    {
        var result = _inspector.InspectFile(path);

        Assert.False(result.Succeeded);
        Assert.Equal(SkipReason.InvalidPath, result.FailureReason);
        Assert.Equal(0, result.Win32Error);
    }

    [Fact]
    public void FileLockedByAnotherProgramCanStillBeInspected_AndIsNotChanged()
    {
        var path = _sandbox.CreateFile("locked.tmp", content: "keep me");
        var before = _sandbox.Snapshot();

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // If SafeClean asked for read, write or delete access, this would fail with a sharing violation.
            Assert.True(_inspector.InspectFile(path).Succeeded);
        }

        Assert.Equal(before, _sandbox.Snapshot());
    }

    [Fact]
    public void LongPathBeyond260CharactersIsInspected()
    {
        // Windows' long-path setting is off on this PC, so this only works because of the \\?\ prefix.
        var relative = Path.Combine(string.Join('\\', Enumerable.Repeat(new string('d', 50), 5)), "long.tmp");
        var path = _sandbox.CreateFile(relative);

        var result = _inspector.InspectFile(path);

        Assert.True(path.Length > 260, $"Path is only {path.Length} characters.");
        Assert.True(result.Succeeded, $"Win32 error {result.Win32Error}");
        Assert.Equal(path, result.Facts.FinalPath);
    }

    [Fact]
    public void JunctionIsInspectedAsItself_NotItsTarget()
    {
        var target = _sandbox.CreateDirectory(@"outside\target");
        var link = _sandbox.PathFor(@"root\link");
        Directory.CreateDirectory(_sandbox.PathFor("root"));
        FileSystemLinks.CreateJunction(link, target);

        var result = _inspector.InspectDirectory(link);

        Assert.True(result.Succeeded);
        Assert.True(result.Facts.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(link, result.Facts.FinalPath);
    }

    [Fact]
    public void HardLinkedFileReportsTwoLinks()
    {
        var original = _sandbox.CreateFile(@"outside\original.tmp");
        var link = _sandbox.PathFor(@"root\hardlink.tmp");
        Directory.CreateDirectory(_sandbox.PathFor("root"));
        FileSystemLinks.CreateHardLink(link, original);

        Assert.Equal(2u, _inspector.InspectFile(link).Facts!.LinkCount);
        Assert.Equal(_inspector.InspectFile(original).Facts!.Identity, _inspector.InspectFile(link).Facts!.Identity);
    }

    [Fact]
    public void FileSymbolicLinkIsInspectedAsItself()
    {
        var target = _sandbox.CreateFile(@"outside\target.tmp");
        var link = _sandbox.PathFor(@"root\symlink.tmp");
        Directory.CreateDirectory(_sandbox.PathFor("root"));
        FileSystemLinks.CreateFileSymbolicLinkOrSkip(link, target);

        var result = _inspector.InspectFile(link);

        Assert.True(result.Succeeded);
        Assert.True(result.Facts.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(link, result.Facts.FinalPath);
    }

    [Fact]
    public void ImpossibleFileTimesBecomeExtremeDates()
    {
        Assert.Equal(DateTimeOffset.MinValue, HandleQueries.FromFileTime(-1));
        Assert.Equal(DateTimeOffset.MaxValue, HandleQueries.FromFileTime(long.MaxValue));
        Assert.Equal(new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero), HandleQueries.FromFileTime(0));
    }
}
