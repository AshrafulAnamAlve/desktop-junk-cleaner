using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Cleanup;
using SafeClean.Infrastructure.Scanning;
using SafeClean.Infrastructure.Tests.Sandbox;

namespace SafeClean.Infrastructure.Tests.Cleanup;

/// <summary>
/// The Phase 6 gate ("Gate before enabling real deletion" in docs/TESTING.md); tests are named after their
/// gate number. Synthetic sandbox files only. Tests end by proving the "outside" folder — stand-ins for
/// personal files — is byte-for-byte unchanged, and <see cref="Dispose"/> checks it again (gate 24).
/// </summary>
public sealed class CleanupGateTests : IDisposable
{
    private const int ConcurrentFiles = 20;

    private readonly TestSandbox _sandbox = TestSandbox.Create();
    private readonly string _root;
    private readonly List<Action> _restoreAfterTest = [];
    private SortedDictionary<string, string> _outsideBefore;

    public CleanupGateTests()
    {
        _root = _sandbox.CreateDirectory("root");
        _sandbox.CreateFile(@"outside\personal\thesis.docx", "irreplaceable work");
        _sandbox.CreateFile(@"outside\photo.tmp", "looks like a temp file, but is outside");
        _outsideBefore = _sandbox.Snapshot("outside");
    }

    public void Dispose()
    {
        try
        {
            foreach (var restore in _restoreAfterTest)
            {
                restore();
            }

            AssertOutsideUnchanged();
        }
        finally
        {
            _sandbox.Dispose();
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>For tests that add their own files to "outside".</summary>
    private void RecordOutside() => _outsideBefore = _sandbox.Snapshot("outside");

    private void AssertOutsideUnchanged() => Assert.Equal(_outsideBefore, _sandbox.Snapshot("outside"));

    private async Task<ScanSession> ScanAsync(string? root = null)
    {
        var scanner = new TempScanner(new SandboxRootResolver(root ?? _root), new FileInspector(), TimeProvider.System, NullLogger<TempScanner>.Instance);
        var result = await scanner.ScanAsync(null, Token);
        Assert.True(result.Succeeded, "Root refused: " + string.Join(", ", result.RootDecision.Problems));
        return result.Session;
    }

    private Task<CleanupReport> CleanAsync(
        ScanSession session,
        IReadOnlyCollection<CandidateId> ids,
        CleanupMode mode = CleanupMode.Real,
        ITempRootResolver? resolver = null,
        IProgress<CleanupProgress>? progress = null) =>
        new CleanupService(resolver ?? new SandboxRootResolver(_root), new FakeElevationProbe(false), new FileInspector(), TimeProvider.System, NullLogger<CleanupService>.Instance)
            .CleanAsync(session, ids, mode, progress, Token);

    private static CandidateId IdOf(ScanSession session, string relativePath) =>
        Assert.Single(session.Candidates, c => c.Decision.RelativePath == relativePath).Id;

    private static CleanupOutcome OutcomeOf(CleanupReport report, CandidateId id) =>
        Assert.Single(report.Entries, e => e.Id == id).Outcome;

    private static FileFacts FactsOf(string path) =>
        new FileInspector().InspectFile(path).Facts ?? throw new InvalidOperationException("Test setup: could not inspect " + path);

    /// <summary>A hand-made candidate claiming to be eligible, as a buggy or malicious caller might send.</summary>
    private static ScanCandidate Forged(int id, FileFacts facts) =>
        new(new CandidateId(id), facts.Path, facts, new CandidateDecision(CandidateStatus.Eligible, [], CandidateCategory.TemporaryFiles, "forged.tmp"));

    private static ScanSession WithForged(ScanSession session, params ScanCandidate[] forged) =>
        new(session.Id, session.Root, session.StartedAt, session.CompletedAt, [.. session.Candidates, .. forged], session.FoldersScanned, session.VanishedDuringScan, session.WasTruncated);

    private static readonly CandidateId ForgedId = new(10_001);

    // Gate 1 (valid file deleted, bytes counted): CleanupServiceTests.SelectedEligibleFiles_AreDeleted_BytesCounted_FoldersKept.

    [Fact]
    public async Task Gate02_CandidateOutsideTheRoot_IsRefused_AndSurvives()
    {
        var session = WithForged(await ScanAsync(), Forged(ForgedId.Value, FactsOf(_sandbox.PathFor(@"outside\photo.tmp"))));

        var report = await CleanAsync(session, [ForgedId]);

        Assert.Equal(CleanupOutcome.SkippedPolicy, OutcomeOf(report, ForgedId));
        AssertOutsideUnchanged();
    }

    [Theory]
    [InlineData(@"{root}\..\outside\photo.tmp")]
    [InlineData(@"{root}/../outside/photo.tmp")]
    [InlineData(@"{root}\sub\..\..\outside\photo.tmp")]
    [InlineData(@"{root}\.\a.tmp")]
    [InlineData(@"\\?\{outside}\photo.tmp")]
    [InlineData(@"\\?\{root}\..\outside\photo.tmp")]
    [InlineData(@"\\.\{outside}\photo.tmp")]
    [InlineData(@"{root}\a.tmp:hidden-stream")]
    [InlineData(@"{root}\a.tmp.")]
    public async Task Gate03_TraversalAndSpecialPathForms_AreRejected_BeforeAnythingIsOpened(string template)
    {
        var a = _sandbox.CreateFile(@"root\a.tmp", "keep");
        var session = await ScanAsync();
        var path = template.Replace("{root}", _root, StringComparison.Ordinal).Replace("{outside}", _sandbox.PathFor("outside"), StringComparison.Ordinal);

        // The forged facts even "agree" with the odd path, as a lying caller would make them.
        var lie = FactsOf(a) with { Path = path, FinalPath = path };
        var report = await CleanAsync(WithForged(session, Forged(ForgedId.Value, lie)), [ForgedId]);

        Assert.Equal(CleanupOutcome.SkippedPolicy, OutcomeOf(report, ForgedId));
        Assert.Equal("keep", File.ReadAllText(a));
        AssertOutsideUnchanged();
    }

    [Fact]
    public async Task Gate03_ShortNameAlias_IsResolvedAndRefused()
    {
        // Windows also opens a file by its 8.3 short name; the OS-reported long name gives the alias away.
        var path = _sandbox.CreateFile(@"root\this-is-a-long-file-name.tmp", "keep");
        var shortName = SpecialFiles.ShortName(path);
        if (shortName is null)
        {
            Assert.Skip("8.3 short names are turned off on this drive, so there is no alias to test.");
            return;
        }

        var session = await ScanAsync();
        var alias = Path.Combine(_root, shortName);
        var lie = FactsOf(path) with { Path = alias, FinalPath = alias };

        var report = await CleanAsync(WithForged(session, Forged(ForgedId.Value, lie)), [ForgedId]);

        Assert.Equal(CleanupOutcome.SkippedChanged, OutcomeOf(report, ForgedId));
        Assert.Equal("keep", File.ReadAllText(path));
    }

    [Fact]
    public async Task Gate04_ProtectedFolderInsideTheRoot_RefusesTheWholeCleanup()
    {
        _sandbox.CreateFile(@"root\Protected\important.tmp", "protected");
        var session = await ScanAsync();
        var stricter = WithExtraProtectedFolder(_sandbox.PathFor(@"root\Protected"));
        var before = _sandbox.Snapshot(@"root\Protected");

        var report = await CleanAsync(session, [IdOf(session, @"Protected\important.tmp")], resolver: new SandboxRootResolver(_root, stricter));

        Assert.Equal(CleanupRefusal.RootRefused, report.Refusal);
        Assert.Contains(RootProblem.ContainsProtectedLocation, report.RootProblems);
        Assert.Equal(before, _sandbox.Snapshot(@"root\Protected"));
        AssertOutsideUnchanged();
    }

    [Fact]
    public async Task Gate04_ProtectedFolder_IsAlsoSkippedByTheEngineItself()
    {
        _sandbox.CreateFile(@"root\Protected\important.tmp", "protected");
        var session = await ScanAsync();
        var resolution = new SandboxRootResolver(_root).Resolve();
        var context = new CandidatePolicyContext(resolution.Decision.Root!, WithExtraProtectedFolder(_sandbox.PathFor(@"root\Protected")), DateTimeOffset.UtcNow);
        var before = _sandbox.Snapshot(@"root\Protected");

        var result = HandleBasedFileDeleter.TryDelete(Assert.Single(session.Candidates, c => c.Decision.RelativePath == @"Protected\important.tmp"), context);

        Assert.Equal(CleanupOutcome.SkippedProtected, result.Outcome);
        Assert.Equal(before, _sandbox.Snapshot(@"root\Protected"));
    }

    private static ProtectedLocations WithExtraProtectedFolder(string folder)
    {
        var known = KnownProtectedLocations.Create(NullLogger.Instance);
        return new ProtectedLocations(known.Subtrees.Append(folder), known.ExactPaths);
    }

    [Fact]
    public async Task Gate05_JunctionToOutside_IsNeverFollowed()
    {
        FileSystemLinks.CreateJunction(Path.Combine(_root, "j"), _sandbox.PathFor("outside"));
        var session = await ScanAsync();

        // A forged candidate *through* the junction, whose facts lie that it is inside the root. Even its
        // identity and size are genuine, so only the location reported for the open handle gives it away.
        var through = Path.Combine(_root, @"j\photo.tmp");
        var lie = FactsOf(through) with { FinalPath = through };
        var junctionId = IdOf(session, "j");

        var report = await CleanAsync(WithForged(session, Forged(ForgedId.Value, lie)), [junctionId, ForgedId]);

        Assert.Equal(CleanupOutcome.SkippedPolicy, OutcomeOf(report, junctionId));
        Assert.Equal(CleanupOutcome.SkippedChanged, OutcomeOf(report, ForgedId));
        Assert.True(new DirectoryInfo(Path.Combine(_root, "j")).Attributes.HasFlag(FileAttributes.ReparsePoint));
        AssertOutsideUnchanged();
    }

    [Fact]
    public async Task Gate06_FileSymbolicLink_IsNeitherDeletedNorFollowed()
    {
        var link = Path.Combine(_root, "s.tmp");
        FileSystemLinks.CreateFileSymbolicLinkOrSkip(link, _sandbox.PathFor(@"outside\photo.tmp"));
        var session = await ScanAsync();
        var old = DateTimeOffset.UtcNow.AddDays(-30);
        var lie = FactsOf(link) with { Attributes = FileAttributes.Archive, CreationTimeUtc = old, LastWriteTimeUtc = old };
        var linkId = IdOf(session, "s.tmp");

        var report = await CleanAsync(WithForged(session, Forged(ForgedId.Value, lie)), [linkId, ForgedId]);

        Assert.Equal(CleanupOutcome.SkippedPolicy, OutcomeOf(report, linkId));
        Assert.Equal(CleanupOutcome.SkippedChanged, OutcomeOf(report, ForgedId));
        Assert.True(new FileInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint));
        AssertOutsideUnchanged();
    }

    [Fact]
    public async Task Gate07_HardLinkedFile_IsSkipped_AndBothNamesSurvive()
    {
        var outsideName = _sandbox.CreateFile(@"outside\h.tmp", "shared data");
        var inRoot = Path.Combine(_root, "h.tmp");
        FileSystemLinks.CreateHardLink(inRoot, outsideName);
        RecordOutside();
        var session = await ScanAsync();
        var lie = FactsOf(inRoot) with { LinkCount = 1 }; // claims a single name
        var linkId = IdOf(session, "h.tmp");

        var report = await CleanAsync(WithForged(session, Forged(ForgedId.Value, lie)), [linkId, ForgedId]);

        Assert.Equal(CleanupOutcome.SkippedPolicy, OutcomeOf(report, linkId));
        Assert.Equal(CleanupOutcome.SkippedChanged, OutcomeOf(report, ForgedId));
        Assert.Equal("shared data", File.ReadAllText(inRoot));
        AssertOutsideUnchanged();
    }

    // Gate 8 (missing at cleanup): CleanupServiceTests.FileDeletedAfterTheScan_IsSkippedAsMissing.

    [Fact]
    public async Task Gate09_AccessDenied_IsSkipped_AndTheFileSurvives()
    {
        var path = _sandbox.CreateFile(@"root\locked-down\denied.tmp", "no delete permission");
        var session = await ScanAsync();

        // Deny delete on the file AND "delete child" on its folder (either one alone would still allow it).
        // Only this synthetic sandbox file and folder are changed, and both are restored after the test.
        var user = WindowsIdentity.GetCurrent().User!;
        var file = new FileInfo(path);
        var folder = file.Directory!;
        Deny(file.GetAccessControl(), new FileSystemAccessRule(user, FileSystemRights.Delete, AccessControlType.Deny), file.SetAccessControl);
        Deny(folder.GetAccessControl(), new FileSystemAccessRule(user, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny), folder.SetAccessControl);

        var entry = Assert.Single((await CleanAsync(session, [IdOf(session, @"locked-down\denied.tmp")])).Entries);

        Assert.Equal(CleanupOutcome.SkippedAccessDenied, entry.Outcome);
        Assert.Equal(5, entry.Win32Error); // ERROR_ACCESS_DENIED
        Assert.Equal("no delete permission", File.ReadAllText(path));
        AssertOutsideUnchanged();
    }

    private void Deny<TSecurity>(TSecurity security, FileSystemAccessRule rule, Action<TSecurity> apply)
        where TSecurity : FileSystemSecurity
    {
        security.AddAccessRule(rule);
        apply(security);
        _restoreAfterTest.Add(() =>
        {
            security.RemoveAccessRule(rule);
            apply(security);
        });
    }

    [Theory]
    [InlineData(FileShare.None)]
    [InlineData(FileShare.Read)]
    [InlineData(FileShare.ReadWrite | FileShare.Delete)]
    public async Task Gate10_FileOpenInAnotherProgram_IsSkipped_AndSurvives(FileShare othersAllow)
    {
        var path = _sandbox.CreateFile(@"root\busy.tmp", "in use");
        var session = await ScanAsync();

        CleanupEntry entry;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, othersAllow))
        {
            entry = Assert.Single((await CleanAsync(session, [IdOf(session, "busy.tmp")])).Entries);
        }

        Assert.Equal(CleanupOutcome.SkippedInUse, entry.Outcome);
        Assert.Equal(32, entry.Win32Error); // ERROR_SHARING_VIOLATION
        Assert.Equal("in use", File.ReadAllText(path));
    }

    [Fact]
    public async Task Gate10_DryRunCannotPredictAFileInUse_SoItOnlySaysWouldTry()
    {
        // Known limitation, pinned so the screen's wording stays honest ("would try to delete").
        var path = _sandbox.CreateFile(@"root\busy.tmp", "in use");
        var session = await ScanAsync();

        CleanupReport report;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            report = await CleanAsync(session, [IdOf(session, "busy.tmp")], CleanupMode.DryRun);
        }

        Assert.Equal(CleanupOutcome.WouldDelete, Assert.Single(report.Entries).Outcome);
        Assert.Equal("in use", File.ReadAllText(path));
    }

    // Gates 11–13 (modified, replaced, parent swapped for a junction): CleanupServiceTests.
    // Gate 14 (cancel during scan): TempScannerTests.CancellationStopsTheScanPromptly.
    // Gate 15 (cancel before cleanup): CleanupServiceTests.CancelledBeforeStart_DeletesNothing.

    [Theory]
    [InlineData(FileAttributes.ReadOnly)]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.System)]
    [InlineData(FileAttributes.Offline)]
    [InlineData(FileAttributes.SparseFile)]
    public async Task Gate16_FileWithAProtectiveAttribute_IsNeverActedOn_AndKeepsIt(FileAttributes attribute)
    {
        var path = _sandbox.CreateFile(@"root\special.tmp", "special");
        Apply(path, attribute);
        var session = await ScanAsync();
        var candidate = Assert.Single(session.Candidates, c => c.Decision.RelativePath == "special.tmp");
        Assert.Contains(SkipReason.DisallowedAttributes, candidate.Decision.Reasons);

        var report = await CleanAsync(session, [candidate.Id]);

        Assert.Equal(CleanupOutcome.SkippedPolicy, Assert.Single(report.Entries).Outcome);
        Assert.True(File.GetAttributes(path).HasFlag(attribute));
        Assert.Equal("special", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(FileAttributes.ReadOnly)]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.System)]
    [InlineData(FileAttributes.Offline)]
    [InlineData(FileAttributes.SparseFile)]
    public async Task Gate16_AttributeAddedAfterTheScan_IsCaught_AndKept(FileAttributes attribute)
    {
        var path = _sandbox.CreateFile(@"root\special.tmp", "special");
        var session = await ScanAsync();
        Apply(path, attribute);

        var report = await CleanAsync(session, [IdOf(session, "special.tmp")]);

        Assert.Equal(CleanupOutcome.SkippedChanged, Assert.Single(report.Entries).Outcome);
        Assert.True(File.GetAttributes(path).HasFlag(attribute));
        Assert.Equal("special", File.ReadAllText(path));
    }

    // Gate 16, encrypted (EFS): deliberately not run — see docs/TESTING.md (it would create an encryption
    // certificate for the Windows account, a security change outside the sandbox; Windows Home has no EFS).

    private static void Apply(string path, FileAttributes attribute)
    {
        if (attribute == FileAttributes.SparseFile)
        {
            SpecialFiles.MakeSparse(path);
        }
        else
        {
            File.SetAttributes(path, File.GetAttributes(path) | attribute);
        }
    }

    [Fact]
    public async Task Gate17_ProgressCallbackThatThrows_StillReturnsTheFullReport()
    {
        var a = _sandbox.CreateFile(@"root\a.tmp", "0123456789");
        var b = _sandbox.CreateFile(@"root\b.tmp");
        var session = await ScanAsync();
        CandidateId aId = IdOf(session, "a.tmp"), bId = IdOf(session, "b.tmp");
        var failingProgress = new SyncProgress<CleanupProgress>(_ => throw new InvalidOperationException("UI went away"));

        var report = await CleanAsync(session, [aId, bId], progress: failingProgress);

        // The run stops, but what was already done is still reported.
        Assert.True(report.StoppedByError);
        Assert.Equal(CleanupOutcome.Deleted, OutcomeOf(report, aId));
        Assert.Equal(CleanupOutcome.SkippedCancelled, OutcomeOf(report, bId));
        Assert.Equal(10, report.BytesFreed);
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(b));
    }

    // Gates 17–18 (partial failure, byte accounting): CleanupServiceTests.MixedBatch_ReportsEachFileCorrectly_AndCountsOnlyDeletedBytes.

    [Fact]
    public async Task Gate19_EmptyRoot_GivesCleanEmptyReports()
    {
        var session = await ScanAsync();
        Assert.Empty(session.Candidates);

        var nothing = await CleanAsync(session, []);
        var unknown = await CleanAsync(session, [new CandidateId(1)]);

        Assert.False(nothing.WasRefused);
        Assert.Empty(nothing.Entries);
        Assert.Equal(CleanupOutcome.SkippedPolicy, Assert.Single(unknown.Entries).Outcome);
        Assert.Equal(0, nothing.BytesFreed + unknown.BytesFreed);
        AssertOutsideUnchanged();
    }

    [Fact]
    public async Task Gate20_ZeroByteAndVeryLargeFiles_HaveExactSizes()
    {
        const long Large = (4L << 30) + 1; // just over 4 GiB: would expose any 32-bit size arithmetic
        var drive = new DriveInfo(Path.GetPathRoot(_root)!);
        if (drive.AvailableFreeSpace < 20L << 30)
        {
            Assert.Skip($"Needs 20 GB free on {drive.Name} for a temporary {Large:N0}-byte test file.");
        }

        var empty = _sandbox.CreateFile(@"root\empty.tmp", content: string.Empty);
        var big = _sandbox.PathFor(@"root\big.tmp");
        using (var stream = new FileStream(big, FileMode.CreateNew))
        {
            stream.SetLength(Large); // a real (not sparse) file, created without writing 4 GB of data
        }

        TestSandbox.SetAge(big, 30);
        await SpecialFiles.WaitUntilNotInUseAsync(big, Token); // let antivirus finish looking at it
        var session = await ScanAsync();
        CandidateId emptyId = IdOf(session, "empty.tmp"), bigId = IdOf(session, "big.tmp");
        Assert.Equal(0, Assert.Single(session.Candidates, c => c.Id == emptyId).Size);
        Assert.Equal(Large, Assert.Single(session.Candidates, c => c.Id == bigId).Size);

        var report = await CleanAsync(session, [emptyId, bigId]);

        Assert.Equal(CleanupOutcome.Deleted, OutcomeOf(report, emptyId));
        Assert.Equal(CleanupOutcome.Deleted, OutcomeOf(report, bigId));
        Assert.Equal(Large, report.BytesFreed);
        Assert.False(File.Exists(empty));
        Assert.False(File.Exists(big));
    }

    [Theory]
    [InlineData("report 😀.tmp")]
    [InlineData("تقرير.tmp")]
    [InlineData("日本語ファイル.tmp")]
    [InlineData("cafe\u0301 decomposed.tmp")]
    [InlineData("\u202Egpj.exe")]
    [InlineData(".leading-dot.tmp")]
    [InlineData("  leading spaces.tmp")]
    [InlineData("name  with   spaces.tmp")]
    public async Task Gate21_UnicodeAndUnusualNames_AreDeletedExactly(string name)
    {
        var path = _sandbox.CreateFile(Path.Combine("root", name), "unusual");
        var neighbour = _sandbox.CreateFile(@"root\neighbour.tmp", "not selected");
        var session = await ScanAsync();
        var candidate = Assert.Single(session.Candidates, c => c.Path == path);
        Assert.True(candidate.Decision.IsSelectable);

        var report = await CleanAsync(session, [candidate.Id]);

        Assert.Equal(CleanupOutcome.Deleted, Assert.Single(report.Entries).Outcome);
        Assert.False(File.Exists(path));
        Assert.Equal("not selected", File.ReadAllText(neighbour));
    }

    [Fact]
    public async Task Gate21_NamesThatLookIdentical_AreNeverConfused()
    {
        var composed = _sandbox.CreateFile("root\\caf\u00e9.tmp", "composed");     // "é" as one character
        var decomposed = _sandbox.CreateFile("root\\cafe\u0301.tmp", "decomposed"); // "e" + combining accent
        var session = await ScanAsync();
        var target = Assert.Single(session.Candidates, c => c.Path == decomposed);

        var report = await CleanAsync(session, [target.Id]);

        Assert.Equal(CleanupOutcome.Deleted, Assert.Single(report.Entries).Outcome);
        Assert.False(File.Exists(decomposed));
        Assert.Equal("composed", File.ReadAllText(composed));
    }

    [Fact]
    public async Task Gate22_PathLongerThan260Characters_IsDeletedExactly()
    {
        var folder = string.Join('\\', Enumerable.Repeat(new string('f', 45), 5));
        var relative = Path.Combine(folder, "deep-long.tmp");
        var path = _sandbox.CreateFile(Path.Combine("root", relative), "deep");
        var sibling = _sandbox.CreateFile(Path.Combine("root", folder, "sibling.tmp"), "stay");
        Assert.True(path.Length > 260, $"Path is only {path.Length} characters.");
        var session = await ScanAsync();

        var report = await CleanAsync(session, [IdOf(session, relative)]);

        Assert.Equal(CleanupOutcome.Deleted, Assert.Single(report.Entries).Outcome);
        Assert.False(File.Exists(path));
        Assert.Equal("stay", File.ReadAllText(sibling));
        Assert.True(Directory.Exists(Path.Combine(_root, folder)));
    }

    [Fact]
    public async Task Gate23_ConcurrentChanges_NeverLeadToAWrongDeletion_RepeatedFiftyTimes()
    {
        int totalReplaced = 0, totalDeleted = 0, totalRaced = 0;
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var folder = $@"concurrent\r{iteration:D2}";
            var root = _sandbox.CreateDirectory(folder);
            for (var k = 0; k < ConcurrentFiles; k++)
            {
                _sandbox.CreateFile($@"{folder}\f{k:D2}.tmp", "original content");
            }

            var session = await ScanAsync(root);
            var ids = session.Candidates.Select(c => c.Id).ToList();
            Assert.Equal(ConcurrentFiles, ids.Count);

            // Replacements get the same size and dates as the originals, so only their identity differs.
            var dates = session.Candidates.ToDictionary(c => c.Path, c => (c.Facts!.CreationTimeUtc.UtcDateTime, c.Facts.LastWriteTimeUtc.UtcDateTime), StringComparer.OrdinalIgnoreCase);
            var replaced = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
            using var stop = new CancellationTokenSource();
            var seed = iteration;
            var churn = Task.Run(() => Churn(root, dates, replaced, seed, stop.Token), Token);
            CleanupReport report;
            try
            {
                report = await CleanAsync(session, ids, resolver: new SandboxRootResolver(root));
            }
            finally
            {
                await stop.CancelAsync();
                await churn;
            }

            Assert.Equal(ConcurrentFiles, report.Entries.Count);
            Assert.Equal(report.Deleted.Sum(e => e.Bytes), report.BytesFreed);
            Assert.All(report.Deleted, e => Assert.Equal("original content".Length, e.Bytes));
            foreach (var path in replaced.Keys)
            {
                // A file swapped in after the scan has a new identity, so it must never be deleted.
                Assert.True(File.Exists(path), $"Iteration {iteration}: a file replaced after the scan was deleted: {path}");
                Assert.Equal(Replacement, File.ReadAllText(path));
            }

            totalReplaced += replaced.Count;
            totalDeleted += report.DeletedCount;
            totalRaced += report.Entries.Count(e => e.Outcome is CleanupOutcome.SkippedChanged or CleanupOutcome.SkippedMissing
                or CleanupOutcome.SkippedInUse or CleanupOutcome.SkippedAccessDenied);
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"50 rounds × {ConcurrentFiles} files: {totalDeleted} deleted, {totalRaced} skipped because of a concurrent change, {totalReplaced} files replaced during cleanup (all survived).");

        // Guards against a test that passes only because the changes never overlapped with a cleanup.
        Assert.True(totalReplaced > 0 && totalRaced > 0, $"The concurrent changes didn't overlap with the cleanups (replaced {totalReplaced}, raced {totalRaced}).");
        AssertOutsideUnchanged();
    }

    /// <summary>Same length as "original content", so a replacement differs from the original only in identity.</summary>
    private const string Replacement = "replaced content";

    /// <summary>Changes the folder while a cleanup runs: replaces scanned files (same name, size and dates; new identity) and adds/removes unrelated files.</summary>
    private static void Churn(
        string root,
        Dictionary<string, (DateTime Created, DateTime Written)> dates,
        ConcurrentDictionary<string, bool> replaced,
        int seed,
        CancellationToken stop)
    {
        var random = new Random(seed);
        var n = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (random.Next(2) == 0)
                {
                    var path = Path.Combine(root, $"f{random.Next(ConcurrentFiles):D2}.tmp");
                    if (replaced.ContainsKey(path))
                    {
                        continue;
                    }

                    File.Delete(path);
                    File.WriteAllText(path, Replacement);
                    File.SetCreationTimeUtc(path, dates[path].Created);
                    File.SetLastWriteTimeUtc(path, dates[path].Written);
                    replaced[path] = true;
                }
                else
                {
                    var extra = Path.Combine(root, $"new{n++ % 10}.tmp");
                    if (File.Exists(extra))
                    {
                        File.Delete(extra);
                    }
                    else
                    {
                        File.WriteAllText(extra, "x");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Racing with SafeClean's exclusive handle ("in use" / "delete pending") is expected; keep going.
            }
        }
    }

    // Gate 24 (integrity after every destructive test): AssertOutsideUnchanged() in each test, again in Dispose(),
    // plus the snapshot checks in CleanupServiceTests.
}
