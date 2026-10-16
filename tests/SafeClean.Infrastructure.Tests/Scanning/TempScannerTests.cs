using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Scanning;
using SafeClean.Infrastructure.Tests.Sandbox;

namespace SafeClean.Infrastructure.Tests.Scanning;

/// <summary>
/// End-to-end scans of synthetic sandbox folders. Every sandbox has a "root" (what is scanned) and an
/// "outside" folder (what must never appear or change).
/// </summary>
public sealed class TempScannerTests : IDisposable
{
    private readonly TestSandbox _sandbox = TestSandbox.Create();
    private readonly string _root;

    public TempScannerTests()
    {
        _root = _sandbox.CreateDirectory("root");
        _sandbox.CreateDirectory("outside");
    }

    public void Dispose() => _sandbox.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<ScanSession> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken? token = null)
    {
        var scanner = new TempScanner(new SandboxRootResolver(_root), new FileInspector(), TimeProvider.System, NullLogger<TempScanner>.Instance);
        var result = await scanner.ScanAsync(progress, token ?? Token);
        Assert.True(result.Succeeded, "Root refused: " + string.Join(", ", result.RootDecision.Problems));
        return result.Session;
    }

    private static ScanCandidate Find(ScanSession session, string relativePath) =>
        Assert.Single(session.Candidates, c => c.Decision.RelativePath == relativePath);

    [Fact]
    public async Task EmptyRootGivesEmptySession()
    {
        var session = await ScanAsync();

        Assert.Empty(session.Candidates);
        Assert.Equal(0, session.Summary.TotalCount);
        Assert.Equal(1, session.FoldersScanned);
        Assert.False(session.WasTruncated);
    }

    [Fact]
    public async Task OldFilesInRootAndSubfoldersAreEligible_WithCorrectFacts()
    {
        var top = _sandbox.CreateFile(@"root\top.tmp", content: "12345");
        var nested = _sandbox.CreateFile(@"root\app\cache\nested.log", content: "1234567890");

        var session = await ScanAsync();

        var topCandidate = Find(session, "top.tmp");
        Assert.Equal(CandidateStatus.Eligible, topCandidate.Decision.Status);
        Assert.Equal(top, topCandidate.Path);
        Assert.Equal(5, topCandidate.Size);
        Assert.Equal(File.GetLastWriteTimeUtc(top), topCandidate.Facts!.LastWriteTimeUtc.UtcDateTime);

        var nestedCandidate = Find(session, @"app\cache\nested.log");
        Assert.Equal(CandidateStatus.Eligible, nestedCandidate.Decision.Status);
        Assert.Equal(CandidateCategory.LogFiles, nestedCandidate.Decision.Category);
        Assert.Equal(nested, nestedCandidate.Path);

        Assert.Equal(2, session.Summary.EligibleCount);
        Assert.Equal(15, session.Summary.EligibleBytes);
        Assert.Equal(15, session.Summary.EstimatedRecoverableBytes);
        Assert.Equal(3, session.FoldersScanned); // root, app, app\cache
    }

    [Fact]
    public async Task RecentFileIsSkippedAsTooRecent()
    {
        _sandbox.CreateFile(@"root\fresh.tmp", ageDays: 0);

        var candidate = Find(await ScanAsync(), "fresh.tmp");

        Assert.Equal(CandidateStatus.Skipped, candidate.Decision.Status);
        Assert.Equal([SkipReason.TooRecent], candidate.Decision.Reasons);
    }

    [Fact]
    public async Task ExecutableNeedsReview()
    {
        _sandbox.CreateFile(@"root\setup.exe");

        Assert.Equal(CandidateStatus.NeedsReview, Find(await ScanAsync(), "setup.exe").Decision.Status);
    }

    [Fact]
    public async Task JunctionIsListedAsSkipped_AndItsTargetIsNeverScanned()
    {
        _sandbox.CreateFile(@"outside\personal\thesis.docx");
        FileSystemLinks.CreateJunction(Path.Combine(_root, "link-to-personal"), _sandbox.PathFor(@"outside\personal"));

        var session = await ScanAsync();

        var link = Find(session, "link-to-personal");
        Assert.Equal(CandidateStatus.Skipped, link.Decision.Status);
        Assert.Contains(SkipReason.ReparsePoint, link.Decision.Reasons);
        Assert.Contains(SkipReason.IsDirectory, link.Decision.Reasons);
        Assert.Single(session.Candidates);
        Assert.DoesNotContain(session.Candidates, c => c.Path.Contains("thesis", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, session.FoldersScanned);
    }

    [Fact]
    public async Task FileSymbolicLinkIsSkipped()
    {
        var target = _sandbox.CreateFile(@"outside\target.tmp");
        FileSystemLinks.CreateFileSymbolicLinkOrSkip(Path.Combine(_root, "symlink.tmp"), target);

        var candidate = Find(await ScanAsync(), "symlink.tmp");

        Assert.Equal(CandidateStatus.Skipped, candidate.Decision.Status);
        Assert.Contains(SkipReason.ReparsePoint, candidate.Decision.Reasons);
    }

    [Fact]
    public async Task HardLinkedFileIsSkipped()
    {
        var outside = _sandbox.CreateFile(@"outside\shared.tmp");
        FileSystemLinks.CreateHardLink(Path.Combine(_root, "shared.tmp"), outside);

        var candidate = Find(await ScanAsync(), "shared.tmp");

        Assert.Equal([SkipReason.UnexpectedLinkCount], candidate.Decision.Reasons);
    }

    [Theory]
    [InlineData(FileAttributes.ReadOnly)]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.System)]
    public async Task FilesWithProtectiveAttributesAreSkipped_AndAttributesUnchanged(FileAttributes attribute)
    {
        var path = _sandbox.CreateFile(@"root\special.tmp");
        File.SetAttributes(path, FileAttributes.Archive | attribute);

        var candidate = Find(await ScanAsync(), "special.tmp");

        Assert.Equal([SkipReason.DisallowedAttributes], candidate.Decision.Reasons);
        Assert.Equal(FileAttributes.Archive | attribute, File.GetAttributes(path));
    }

    [Fact]
    public async Task LockedFileIsScannedWithoutError_AndLeftUntouched()
    {
        var path = _sandbox.CreateFile(@"root\in-use.tmp", content: "busy");

        ScanSession session;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            session = await ScanAsync();
        }

        var candidate = Find(session, "in-use.tmp");
        Assert.True(candidate.Decision.Status is CandidateStatus.Eligible || candidate.Decision.Reasons.Contains(SkipReason.Inaccessible));
        Assert.Equal("busy", File.ReadAllText(path));
    }

    [Fact]
    public async Task FolderWithoutPermissionIsSkipped_AndScanContinues()
    {
        var blocked = _sandbox.CreateDirectory(@"root\no-access");
        _sandbox.CreateFile(@"root\no-access\hidden-inside.tmp");
        _sandbox.CreateFile(@"root\visible.tmp");

        var directory = new DirectoryInfo(blocked);
        var security = directory.GetAccessControl();
        var denyListing = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        security.AddAccessRule(denyListing);
        directory.SetAccessControl(security);
        try
        {
            var session = await ScanAsync();

            var folder = Find(session, "no-access");
            Assert.Equal([SkipReason.IsDirectory, SkipReason.Inaccessible], folder.Decision.Reasons);
            Assert.Equal(CandidateStatus.Eligible, Find(session, "visible.tmp").Decision.Status);
            Assert.DoesNotContain(session.Candidates, c => c.Path.EndsWith("hidden-inside.tmp", StringComparison.Ordinal));
        }
        finally
        {
            security.RemoveAccessRule(denyListing);
            directory.SetAccessControl(security);
        }
    }

    [Theory]
    [InlineData("report 😀.tmp")]
    [InlineData("تقرير.tmp")]
    [InlineData("café.tmp")]
    [InlineData(".leading-dot.tmp")]
    [InlineData("name  with   spaces.tmp")]
    [InlineData("日本語ファイル.tmp")]
    public async Task UnicodeAndUnusualNamesAreFoundExactly(string name)
    {
        _sandbox.CreateFile(Path.Combine("root", name));

        var candidate = Find(await ScanAsync(), name);

        Assert.Equal(CandidateStatus.Eligible, candidate.Decision.Status);
        Assert.Equal(Path.Combine(_root, name), candidate.Path);
    }

    [Fact]
    public async Task PathLongerThan260CharactersIsScanned()
    {
        var relative = Path.Combine(string.Join('\\', Enumerable.Repeat(new string('f', 45), 5)), "deep-long.tmp");
        var path = _sandbox.CreateFile(Path.Combine("root", relative));

        var candidate = Find(await ScanAsync(), relative);

        Assert.True(path.Length > 260, $"Path is only {path.Length} characters.");
        Assert.Equal(CandidateStatus.Eligible, candidate.Decision.Status);
        Assert.Equal(path, candidate.Facts!.FinalPath);
    }

    [Fact]
    public async Task FoldersBeyondTheDepthLimitAreNotEntered()
    {
        string Nested(int levels) => string.Join('\\', Enumerable.Range(0, levels).Select(i => "d" + i));
        var deepest = SafetyDefaults.MaxScanDepth;
        _sandbox.CreateFile(Path.Combine("root", Nested(deepest), "at-limit.tmp"));       // 16 folders deep: allowed
        _sandbox.CreateFile(Path.Combine("root", Nested(deepest + 1), "too-deep.tmp"));   // 17 folders deep: not reached

        var session = await ScanAsync();

        Assert.Equal(CandidateStatus.Eligible, Find(session, Path.Combine(Nested(deepest), "at-limit.tmp")).Decision.Status);
        var tooDeepFolder = Find(session, Nested(deepest + 1));
        Assert.Equal([SkipReason.IsDirectory, SkipReason.TooDeep], tooDeepFolder.Decision.Reasons);
        Assert.DoesNotContain(session.Candidates, c => c.Path.EndsWith("too-deep.tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellationStopsTheScanPromptly()
    {
        for (var i = 0; i < 1200; i++)
        {
            _sandbox.CreateFile($@"root\many\file{i:D4}.tmp", ageDays: 0);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var progress = new SyncProgress<ScanProgress>(_ => cts.Cancel());
        var snapshot = _sandbox.Snapshot();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScanAsync(progress, cts.Token));
        Assert.Equal(snapshot, _sandbox.Snapshot());
    }

    [Fact]
    public async Task ProgressIsReported()
    {
        for (var i = 0; i < 600; i++)
        {
            _sandbox.CreateFile($@"root\file{i:D4}.tmp", ageDays: 0);
        }

        var reports = new List<ScanProgress>();
        await ScanAsync(new SyncProgress<ScanProgress>(reports.Add));

        Assert.True(reports.Count >= 3, $"Only {reports.Count} progress reports.");
        Assert.Equal(600, reports[^1].FilesExamined);
    }

    [Fact]
    public async Task FilesChangingDuringTheScanCauseNoErrors()
    {
        for (var i = 0; i < 800; i++)
        {
            _sandbox.CreateFile($@"root\stable\file{i:D4}.tmp");
        }

        var churnFolder = _sandbox.CreateDirectory(@"root\churn");
        using var stop = new CancellationTokenSource();
        var churn = Task.Run(() =>
        {
            var n = 0;
            while (!stop.IsCancellationRequested)
            {
                var path = Path.Combine(churnFolder, $"c{n++ % 50}.tmp");
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    else
                    {
                        File.WriteAllText(path, "x");
                    }
                }
                catch (IOException)
                {
                    // Racing with the scanner's read-only handle is fine; just keep churning.
                }
            }
        }, Token);

        ScanSession session;
        try
        {
            session = await ScanAsync();
        }
        finally
        {
            await stop.CancelAsync();
            await churn;
        }

        Assert.Equal(800, session.Candidates.Count(c => c.Path.Contains(@"\stable\", StringComparison.Ordinal)));
        Assert.All(session.Candidates, c => Assert.StartsWith(_root + @"\", c.Path, StringComparison.Ordinal));
        Assert.All(session.Candidates.Where(c => c.Path.Contains(@"\churn\", StringComparison.Ordinal)),
            c => Assert.Equal(CandidateStatus.Skipped, c.Decision.Status)); // all brand new → too recent
    }

    [Fact]
    public async Task ScanChangesNothingOnDisk()
    {
        _sandbox.CreateFile(@"root\a.tmp");
        _sandbox.CreateFile(@"root\sub\b.log", ageDays: 0);
        var readOnly = _sandbox.CreateFile(@"root\readonly.tmp");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        _sandbox.CreateFile(@"outside\personal\photo.jpg");
        FileSystemLinks.CreateJunction(Path.Combine(_root, "junction"), _sandbox.PathFor(@"outside\personal"));
        FileSystemLinks.CreateHardLink(Path.Combine(_root, "hard.tmp"), _sandbox.CreateFile(@"outside\hard-original.tmp"));

        var before = _sandbox.Snapshot();
        await ScanAsync();
        var after = _sandbox.Snapshot();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task RefusedRootProducesNoSession()
    {
        var real = _sandbox.CreateDirectory("real");
        var link = _sandbox.PathFor("link");
        FileSystemLinks.CreateJunction(link, real);
        var scanner = new TempScanner(new SandboxRootResolver(link), new FileInspector(), TimeProvider.System, NullLogger<TempScanner>.Instance);

        var result = await scanner.ScanAsync(null, Token);

        Assert.False(result.Succeeded);
        Assert.Null(result.Session);
        Assert.Contains(RootProblem.RootIsReparsePoint, result.RootDecision.Problems);
    }
}
