using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Infrastructure.Cleanup;
using SafeClean.Infrastructure.Scanning;
using SafeClean.Infrastructure.Tests.Sandbox;

namespace SafeClean.Infrastructure.Tests.Cleanup;

/// <summary>
/// The cleanup service against synthetic sandbox files only. Each sandbox has a "root" (scanned and cleaned)
/// and an "outside" folder with stand-ins for personal files; every test proves "outside" is unchanged.
/// </summary>
public sealed class CleanupServiceTests : IDisposable
{
    private readonly TestSandbox _sandbox = TestSandbox.Create();
    private readonly string _root;
    private readonly ListLogger<CleanupService> _log = new();
    private SortedDictionary<string, string> _outsideBefore;

    public CleanupServiceTests()
    {
        _root = _sandbox.CreateDirectory("root");
        _sandbox.CreateFile(@"outside\personal\thesis.docx", "irreplaceable work");
        _sandbox.CreateFile(@"outside\photo.tmp", "looks like a temp file, but is outside");
        _outsideBefore = OutsideSnapshot();
    }

    public void Dispose() => _sandbox.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private SortedDictionary<string, string> OutsideSnapshot() =>
        new(_sandbox.Snapshot().Where(e => e.Key.StartsWith("outside", StringComparison.Ordinal)).ToDictionary(), StringComparer.Ordinal);

    /// <summary>For tests that add their own files to "outside".</summary>
    private void RecordOutside() => _outsideBefore = OutsideSnapshot();

    private void AssertOutsideUnchanged() => Assert.Equal(_outsideBefore, OutsideSnapshot());

    private async Task<ScanSession> ScanAsync()
    {
        var scanner = new TempScanner(new SandboxRootResolver(_root), new FileInspector(), TimeProvider.System, NullLogger<TempScanner>.Instance);
        var result = await scanner.ScanAsync(null, Token);
        Assert.True(result.Succeeded, "Root refused: " + string.Join(", ", result.RootDecision.Problems));
        return result.Session;
    }

    private CleanupService Service(bool elevated = false, TimeProvider? clock = null, ITempRootResolver? resolver = null) =>
        new(resolver ?? new SandboxRootResolver(_root), new FakeElevationProbe(elevated), new FileInspector(), clock ?? TimeProvider.System, _log);

    private Task<CleanupReport> CleanAsync(
        ScanSession session,
        IReadOnlyCollection<CandidateId> ids,
        CleanupMode mode = CleanupMode.Real,
        CleanupService? service = null,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken? token = null) =>
        (service ?? Service()).CleanAsync(session, ids, mode, progress, token ?? Token);

    private static CandidateId IdOf(ScanSession session, string relativePath) =>
        Assert.Single(session.Candidates, c => c.Decision.RelativePath == relativePath).Id;

    private static CleanupOutcome OutcomeOf(CleanupReport report, CandidateId id) =>
        Assert.Single(report.Entries, e => e.Id == id).Outcome;

    // ---- Deleting ----

    [Fact]
    public async Task SelectedEligibleFiles_AreDeleted_BytesCounted_FoldersKept()
    {
        var a = _sandbox.CreateFile(@"root\a.tmp", "0123456789");
        var b = _sandbox.CreateFile(@"root\cache\b.log", new string('x', 20));
        var session = await ScanAsync();

        var report = await CleanAsync(session, [IdOf(session, "a.tmp"), IdOf(session, @"cache\b.log")]);

        Assert.False(report.WasRefused);
        Assert.All(report.Entries, e => Assert.Equal(CleanupOutcome.Deleted, e.Outcome));
        Assert.Equal(2, report.DeletedCount);
        Assert.Equal(30, report.BytesFreed);
        Assert.False(File.Exists(a));
        Assert.False(File.Exists(b));
        Assert.True(Directory.Exists(Path.Combine(_root, "cache"))); // folders are never deleted
        AssertOutsideUnchanged();
    }

    [Fact]
    public async Task OnlySelectedFiles_AreTouched_IncludingANeedsReviewFile()
    {
        var chosen = _sandbox.CreateFile(@"root\setup.exe", "needs review");
        var other = _sandbox.CreateFile(@"root\other.tmp", "not selected");
        var session = await ScanAsync();

        var report = await CleanAsync(session, [IdOf(session, "setup.exe")]);

        Assert.Equal(CleanupOutcome.Deleted, Assert.Single(report.Entries).Outcome);
        Assert.False(File.Exists(chosen));
        Assert.Equal("not selected", File.ReadAllText(other));
        AssertOutsideUnchanged();
    }

    // ---- Dry Run ----

    [Fact]
    public async Task DryRun_ChangesNothing_AndReportsWhatWouldBeDeleted()
    {
        _sandbox.CreateFile(@"root\a.tmp", "0123456789");
        _sandbox.CreateFile(@"root\b.tmp", "01234");
        var session = await ScanAsync();
        var before = _sandbox.Snapshot();

        var report = await CleanAsync(session, [IdOf(session, "a.tmp"), IdOf(session, "b.tmp")], CleanupMode.DryRun);

        Assert.Equal(2, report.WouldDeleteCount);
        Assert.Equal(15, report.BytesWouldFree);
        Assert.Equal(0, report.DeletedCount);
        Assert.Equal(0, report.BytesFreed);
        Assert.Equal(before, _sandbox.Snapshot());
    }

    [Fact]
    public async Task DryRun_AlsoNoticesChangedAndMissingFiles()
    {
        var changed = _sandbox.CreateFile(@"root\changed.tmp");
        var missing = _sandbox.CreateFile(@"root\missing.tmp");
        _sandbox.CreateFile(@"root\fine.tmp");
        var session = await ScanAsync();
        CandidateId changedId = IdOf(session, "changed.tmp"), missingId = IdOf(session, "missing.tmp"), fineId = IdOf(session, "fine.tmp");
        File.AppendAllText(changed, " and more");
        File.Delete(missing);
        var before = _sandbox.Snapshot();

        var report = await CleanAsync(session, [changedId, missingId, fineId], CleanupMode.DryRun);

        Assert.Equal(CleanupOutcome.SkippedChanged, OutcomeOf(report, changedId));
        Assert.Equal(CleanupOutcome.SkippedMissing, OutcomeOf(report, missingId));
        Assert.Equal(CleanupOutcome.WouldDelete, OutcomeOf(report, fineId));
        Assert.Equal(before, _sandbox.Snapshot());
    }

    // ---- Whole request refused ----

    [Theory]
    [InlineData(CleanupMode.DryRun)]
    [InlineData(CleanupMode.Real)]
    public async Task Elevated_RefusesTheWholeRequest(CleanupMode mode)
    {
        _sandbox.CreateFile(@"root\a.tmp");
        var session = await ScanAsync();
        var before = _sandbox.Snapshot();

        var report = await CleanAsync(session, [IdOf(session, "a.tmp")], mode, Service(elevated: true));

        Assert.Equal(CleanupRefusal.Elevated, report.Refusal);
        Assert.Empty(report.Entries);
        Assert.Equal(before, _sandbox.Snapshot());
    }

    [Fact]
    public async Task UnknownMode_IsRefused_NeverTreatedAsReal()
    {
        _sandbox.CreateFile(@"root\a.tmp");
        var session = await ScanAsync();
        var before = _sandbox.Snapshot();

        var report = await CleanAsync(session, [IdOf(session, "a.tmp")], (CleanupMode)7);

        Assert.Equal(CleanupRefusal.InvalidMode, report.Refusal);
        Assert.Equal(before, _sandbox.Snapshot());
    }

    [Fact]
    public async Task RootReplacedByANewFolderWithTheSameName_IsRefused()
    {
        _sandbox.CreateFile(@"root\a.tmp");
        var session = await ScanAsync();
        Directory.Move(_root, _sandbox.PathFor("root-old"));
        _sandbox.CreateFile(@"root\a.tmp"); // a look-alike at the same path
        var before = _sandbox.Snapshot();

        var report = await CleanAsync(session, [IdOf(session, "a.tmp")]);

        Assert.Equal(CleanupRefusal.RootChanged, report.Refusal);
        Assert.Equal(before, _sandbox.Snapshot());
    }

    [Fact]
    public async Task CleanupResolvingADifferentFolder_IsRefused()
    {
        _sandbox.CreateFile(@"root\a.tmp");
        var other = _sandbox.CreateDirectory("other");
        var session = await ScanAsync();
        var before = _sandbox.Snapshot();

        var report = await CleanAsync(session, [IdOf(session, "a.tmp")], service: Service(resolver: new SandboxRootResolver(other)));

        Assert.Equal(CleanupRefusal.RootChanged, report.Refusal);
        Assert.Equal(before, _sandbox.Snapshot());
    }

    [Fact]
    public async Task RootSwappedForAJunction_IsRefused_AndTheTargetIsUntouched()
    {
        // The root holds a file with the same name as one in "outside"; after the swap, root\photo.tmp
        // would lead to outside\photo.tmp.
        _sandbox.CreateFile(@"root\photo.tmp");
        var session = await ScanAsync();
        Directory.Move(_root, _sandbox.PathFor("root-old"));
        FileSystemLinks.CreateJunction(_root, _sandbox.PathFor("outside"));

        var report = await CleanAsync(session, [IdOf(session, "photo.tmp")]);

        Assert.Equal(CleanupRefusal.RootRefused, report.Refusal);
        Assert.Contains(RootProblem.RootIsReparsePoint, report.RootProblems);
        Assert.True(File.Exists(_sandbox.PathFor(@"root-old\photo.tmp")));
        AssertOutsideUnchanged();
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(-1, false)]
    [InlineData(29, true)]
    public async Task OldOrFutureScans_AreRefused(int minutesAfterScan, bool accepted)
    {
        var path = _sandbox.CreateFile(@"root\a.tmp");
        var session = await ScanAsync();
        var clock = new ManualTimeProvider(session.StartedAt.AddMinutes(minutesAfterScan));

        var report = await CleanAsync(session, [IdOf(session, "a.tmp")], service: Service(clock: clock));

        if (accepted)
        {
            Assert.Equal(CleanupOutcome.Deleted, Assert.Single(report.Entries).Outcome);
        }
        else
        {
            Assert.Equal(CleanupRefusal.SessionExpired, report.Refusal);
            Assert.True(File.Exists(path));
        }
    }

    // ---- Selection ----

    [Fact]
    public async Task UnknownIds_AndSkippedEntries_AreNeverActedOn()
    {
        var fresh = _sandbox.CreateFile(@"root\fresh.tmp", ageDays: 0);
        FileSystemLinks.CreateJunction(Path.Combine(_root, "link"), _sandbox.PathFor(@"outside\personal"));
        var session = await ScanAsync();
        var unknown = new CandidateId(9999);

        var report = await CleanAsync(session, [IdOf(session, "fresh.tmp"), IdOf(session, "link"), unknown]);

        Assert.Equal(3, report.Entries.Count);
        Assert.All(report.Entries, e => Assert.Equal(CleanupOutcome.SkippedPolicy, e.Outcome));
        Assert.Equal(string.Empty, Assert.Single(report.Entries, e => e.Id == unknown).Path);
        Assert.Equal(0, report.BytesFreed);
        Assert.True(File.Exists(fresh));
        Assert.True(Directory.Exists(Path.Combine(_root, "link")));
        AssertOutsideUnchanged();
    }

    [Fact]
    public async Task EachSelectedId_IsReportedOnce_InSelectionOrder()
    {
        _sandbox.CreateFile(@"root\a.tmp");
        _sandbox.CreateFile(@"root\b.tmp");
        var session = await ScanAsync();
        CandidateId a = IdOf(session, "a.tmp"), b = IdOf(session, "b.tmp"), unknown = new(9999);

        var report = await CleanAsync(session, [b, a, b, unknown], CleanupMode.DryRun);

        Assert.Equal([b, a, unknown], report.Entries.Select(e => e.Id));
    }

    [Fact]
    public async Task EmptySelection_GivesAnEmptyReport()
    {
        _sandbox.CreateFile(@"root\a.tmp");
        var session = await ScanAsync();
        var before = _sandbox.Snapshot();

        var report = await CleanAsync(session, []);

        Assert.False(report.WasRefused);
        Assert.False(report.WasCancelled);
        Assert.Empty(report.Entries);
        Assert.Equal(0, report.BytesFreed);
        Assert.Equal(before, _sandbox.Snapshot());
    }

    // ---- Cancellation ----

    [Fact]
    public async Task CancelledBeforeStart_DeletesNothing()
    {
        _sandbox.CreateFile(@"root\a.tmp");
        _sandbox.CreateFile(@"root\b.tmp");
        var session = await ScanAsync();
        var before = _sandbox.Snapshot();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var report = await CleanAsync(session, [IdOf(session, "a.tmp"), IdOf(session, "b.tmp")], token: cancelled.Token);

        Assert.True(report.WasCancelled);
        Assert.All(report.Entries, e => Assert.Equal(CleanupOutcome.SkippedCancelled, e.Outcome));
        Assert.Equal(before, _sandbox.Snapshot());
    }

    [Fact]
    public async Task CancelledDuringCleanup_StopsBetweenFiles_AndReportsWhatWasDone()
    {
        var a = _sandbox.CreateFile(@"root\a.tmp", "0123456789");
        var b = _sandbox.CreateFile(@"root\b.tmp");
        var c = _sandbox.CreateFile(@"root\c.tmp");
        var session = await ScanAsync();
        CandidateId aId = IdOf(session, "a.tmp"), bId = IdOf(session, "b.tmp"), cId = IdOf(session, "c.tmp");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var cancelAfterFirstFile = new SyncProgress<CleanupProgress>(_ => cancellation.Cancel());

        var report = await CleanAsync(session, [aId, bId, cId], progress: cancelAfterFirstFile, token: cancellation.Token);

        Assert.True(report.WasCancelled);
        Assert.Equal(CleanupOutcome.Deleted, OutcomeOf(report, aId));
        Assert.Equal(CleanupOutcome.SkippedCancelled, OutcomeOf(report, bId));
        Assert.Equal(CleanupOutcome.SkippedCancelled, OutcomeOf(report, cId));
        Assert.Equal(10, report.BytesFreed);
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.True(File.Exists(c));
    }

    // ---- Files that changed after the scan ----

    [Fact]
    public async Task FileDeletedAfterTheScan_IsSkippedAsMissing()
    {
        var path = _sandbox.CreateFile(@"root\gone.tmp");
        var session = await ScanAsync();
        File.Delete(path);

        var entry = Assert.Single((await CleanAsync(session, [IdOf(session, "gone.tmp")])).Entries);

        Assert.Equal(CleanupOutcome.SkippedMissing, entry.Outcome);
        Assert.Equal(0, entry.Bytes);
    }

    [Fact]
    public async Task FileInUse_IsSkipped_AndSurvives()
    {
        var path = _sandbox.CreateFile(@"root\busy.tmp", "in use");
        var session = await ScanAsync();

        CleanupEntry entry;
        // Another program has it open. Even though it allows sharing, SafeClean's exclusive open must fail.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            entry = Assert.Single((await CleanAsync(session, [IdOf(session, "busy.tmp")])).Entries);
        }

        Assert.Equal(CleanupOutcome.SkippedInUse, entry.Outcome);
        Assert.Equal(32, entry.Win32Error); // ERROR_SHARING_VIOLATION
        Assert.Equal("in use", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("content appended, dates restored")]
    [InlineData("only the modified date changed")]
    public async Task FileModifiedAfterTheScan_IsSkippedAsChanged(string change)
    {
        var path = _sandbox.CreateFile(@"root\a.tmp", "original");
        var created = File.GetCreationTimeUtc(path);
        var written = File.GetLastWriteTimeUtc(path);
        var session = await ScanAsync();

        if (change.StartsWith("content", StringComparison.Ordinal))
        {
            File.AppendAllText(path, " + more");
            File.SetCreationTimeUtc(path, created);
            File.SetLastWriteTimeUtc(path, written);
        }
        else
        {
            File.SetLastWriteTimeUtc(path, written.AddMinutes(-1));
        }

        var report = await CleanAsync(session, [IdOf(session, "a.tmp")]);

        Assert.Equal(CleanupOutcome.SkippedChanged, Assert.Single(report.Entries).Outcome);
        Assert.StartsWith("original", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileReplacedAfterTheScan_IsSkipped_AndTheReplacementSurvives()
    {
        var path = _sandbox.CreateFile(@"root\a.tmp", "same content");
        var created = File.GetCreationTimeUtc(path);
        var written = File.GetLastWriteTimeUtc(path);
        var session = await ScanAsync();

        // Same name, size, content and dates; only the file's identity differs.
        File.Delete(path);
        File.WriteAllText(path, "same content");
        File.SetCreationTimeUtc(path, created);
        File.SetLastWriteTimeUtc(path, written);

        var report = await CleanAsync(session, [IdOf(session, "a.tmp")]);

        Assert.Equal(CleanupOutcome.SkippedChanged, Assert.Single(report.Entries).Outcome);
        Assert.Equal("same content", File.ReadAllText(path));
    }

    [Fact]
    public async Task ParentFolderSwappedForAJunction_IsSkipped_AndTheLookAlikeOutsideSurvives()
    {
        var original = _sandbox.CreateFile(@"root\sub\f.tmp", "look-alike");
        var lookAlike = _sandbox.CreateFile(@"outside\sub\f.tmp", "look-alike");
        File.SetCreationTimeUtc(lookAlike, File.GetCreationTimeUtc(original));
        File.SetLastWriteTimeUtc(lookAlike, File.GetLastWriteTimeUtc(original));
        RecordOutside();
        var session = await ScanAsync();
        var id = IdOf(session, @"sub\f.tmp");

        Directory.Move(Path.Combine(_root, "sub"), Path.Combine(_root, "sub-old"));
        FileSystemLinks.CreateJunction(Path.Combine(_root, "sub"), _sandbox.PathFor(@"outside\sub"));

        var report = await CleanAsync(session, [id]);

        Assert.Equal(CleanupOutcome.SkippedChanged, Assert.Single(report.Entries).Outcome);
        Assert.True(File.Exists(Path.Combine(_root, @"sub-old\f.tmp")));
        AssertOutsideUnchanged();
    }

    [Fact]
    public async Task HardLinkAddedAfterTheScan_IsSkipped_AndBothNamesSurvive()
    {
        var path = _sandbox.CreateFile(@"root\h.tmp", "shared");
        var session = await ScanAsync();
        var outsideName = _sandbox.PathFor(@"outside\h.tmp");
        FileSystemLinks.CreateHardLink(outsideName, path);
        RecordOutside();

        var report = await CleanAsync(session, [IdOf(session, "h.tmp")]);

        Assert.Equal(CleanupOutcome.SkippedChanged, Assert.Single(report.Entries).Outcome);
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(outsideName));
        AssertOutsideUnchanged();
    }

    [Fact]
    public async Task FileMadeReadOnlyAfterTheScan_IsSkipped_AndTheAttributeIsLeftAlone()
    {
        var path = _sandbox.CreateFile(@"root\r.tmp");
        var session = await ScanAsync();
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

        var report = await CleanAsync(session, [IdOf(session, "r.tmp")]);

        Assert.Equal(CleanupOutcome.SkippedChanged, Assert.Single(report.Entries).Outcome);
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
    }

    // ---- Mixed batch, accounting and audit log ----

    [Fact]
    public async Task MixedBatch_ReportsEachFileCorrectly_AndCountsOnlyDeletedBytes()
    {
        var good = _sandbox.CreateFile(@"root\good.tmp", "0123456789");
        var busy = _sandbox.CreateFile(@"root\busy.tmp", "busy busy busy");
        var changed = _sandbox.CreateFile(@"root\changed.tmp", "changed");
        var missing = _sandbox.CreateFile(@"root\missing.tmp", "missing");
        var fresh = _sandbox.CreateFile(@"root\fresh.tmp", "fresh", ageDays: 0);
        var session = await ScanAsync();
        CandidateId goodId = IdOf(session, "good.tmp"), busyId = IdOf(session, "busy.tmp"), changedId = IdOf(session, "changed.tmp");
        CandidateId missingId = IdOf(session, "missing.tmp"), freshId = IdOf(session, "fresh.tmp"), unknown = new(9999);
        File.AppendAllText(changed, "!");
        File.Delete(missing);

        CleanupReport report;
        using (new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            report = await CleanAsync(session, [goodId, busyId, changedId, missingId, freshId, unknown]);
        }

        Assert.Equal(CleanupOutcome.Deleted, OutcomeOf(report, goodId));
        Assert.Equal(CleanupOutcome.SkippedInUse, OutcomeOf(report, busyId));
        Assert.Equal(CleanupOutcome.SkippedChanged, OutcomeOf(report, changedId));
        Assert.Equal(CleanupOutcome.SkippedMissing, OutcomeOf(report, missingId));
        Assert.Equal(CleanupOutcome.SkippedPolicy, OutcomeOf(report, freshId));
        Assert.Equal(CleanupOutcome.SkippedPolicy, OutcomeOf(report, unknown));
        Assert.Equal(1, report.DeletedCount);
        Assert.Equal(5, report.SkippedCount);
        Assert.Equal(0, report.FailedCount);
        Assert.Equal(10, report.BytesFreed);
        Assert.False(File.Exists(good));
        Assert.True(File.Exists(busy));
        Assert.True(File.Exists(changed));
        Assert.True(File.Exists(fresh));
        AssertOutsideUnchanged();
    }

    [Fact]
    public async Task AuditLog_UsesPathsRelativeToTheTempFolderOnly()
    {
        _sandbox.CreateFile(@"root\cache\a.tmp");
        var session = await ScanAsync();

        await CleanAsync(session, [IdOf(session, @"cache\a.tmp"), new CandidateId(9999)]);

        var messages = _log.Messages;
        Assert.Contains(messages, m => m.Contains(@"Deleted: cache\a.tmp", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("SkippedPolicy: (item 9999, path not shown)", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("finished: 1 deleted", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m.Contains(_sandbox.Root, StringComparison.OrdinalIgnoreCase));
        var profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        Assert.DoesNotContain(messages, m => m.Contains(profile, StringComparison.OrdinalIgnoreCase));
    }
}
