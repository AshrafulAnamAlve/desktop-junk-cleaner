using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Cleanup;
using SafeClean.Infrastructure.Native;
using SafeClean.Infrastructure.Scanning;
using SafeClean.Infrastructure.Tests.Sandbox;

namespace SafeClean.Infrastructure.Tests.Cleanup;

/// <summary>The deletion engine on its own, against synthetic sandbox files only.</summary>
public sealed class HandleBasedFileDeleterTests : IDisposable
{
    private readonly TestSandbox _sandbox = TestSandbox.Create();
    private readonly string _root;

    public HandleBasedFileDeleterTests()
    {
        _root = _sandbox.CreateDirectory("root");
        _sandbox.CreateDirectory("outside");
    }

    public void Dispose() => _sandbox.Dispose();

    private async Task<(ScanSession Session, CandidatePolicyContext Context)> ScanAsync()
    {
        var resolver = new SandboxRootResolver(_root);
        var scanner = new TempScanner(resolver, new FileInspector(), TimeProvider.System, NullLogger<TempScanner>.Instance);
        var result = await scanner.ScanAsync(null, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);

        var resolution = resolver.Resolve();
        return (result.Session, new CandidatePolicyContext(resolution.Decision.Root!, resolution.ProtectedLocations, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AsksForDeleteAndReadAttributesOnly_SharesNothing_AndCanNeverOpenAFolder()
    {
        // DELETE (0x10000) | SYNCHRONIZE (0x100000) | FILE_READ_ATTRIBUTES (0x80): no reading or writing of contents.
        Assert.Equal(0x0011_0080u, HandleBasedFileDeleter.DesiredAccess);
        Assert.Equal(0u, HandleBasedFileDeleter.ShareMode);

        // Links are opened as themselves; without "backup semantics" Windows refuses to open folders at all.
        Assert.Equal(NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT, HandleBasedFileDeleter.Flags);
    }

    [Fact]
    public void DispositionStructure_IsOneByte_LikeTheWindowsDefinition() =>
        Assert.Equal(1u, HandleBasedFileDeleter.DispositionInfoSize);

    [Fact]
    public async Task MatchingFile_IsGoneAfterTheCall_AndItsSizeIsReturned()
    {
        var path = _sandbox.CreateFile(@"root\a.tmp", "12345");
        var (session, context) = await ScanAsync();

        var result = HandleBasedFileDeleter.TryDelete(Assert.Single(session.Candidates), context);

        Assert.Equal(CleanupOutcome.Deleted, result.Outcome);
        Assert.Equal(5, result.Bytes);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task FileThatFailsTheHandleCheck_IsLeftInPlace_EvenThoughItWasOpenedExclusively()
    {
        var path = _sandbox.CreateFile(@"root\a.tmp", "keep me");
        var (session, context) = await ScanAsync();
        var candidate = Assert.Single(session.Candidates);

        // Pretend the scan saw a different size: D4 must refuse before D5 marks anything for deletion.
        var mismatched = candidate with { Facts = candidate.Facts! with { Size = candidate.Facts.Size + 1 } };
        var result = HandleBasedFileDeleter.TryDelete(mismatched, context);

        Assert.Equal(CleanupOutcome.SkippedChanged, result.Outcome);
        Assert.Equal(0, result.Bytes);
        Assert.Equal("keep me", File.ReadAllText(path)); // still there and not stuck "delete pending"
    }

    [Fact]
    public async Task CannotBePointedAtAnotherRoot()
    {
        var path = _sandbox.CreateFile(@"root\a.tmp");
        var (session, _) = await ScanAsync();
        var other = new SandboxRootResolver(_sandbox.PathFor("outside")).Resolve();
        var wrongContext = new CandidatePolicyContext(other.Decision.Root!, other.ProtectedLocations, DateTimeOffset.UtcNow);

        var result = HandleBasedFileDeleter.TryDelete(Assert.Single(session.Candidates), wrongContext);

        Assert.Equal(CleanupOutcome.SkippedPolicy, result.Outcome);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ForgedEligibleCandidate_ForAFileOutsideTheRoot_IsRefused()
    {
        var personal = _sandbox.CreateFile(@"outside\personal.tmp", "personal");
        var (_, context) = await ScanAsync();
        var facts = new FileInspector().InspectFile(personal).Facts!;
        var forged = new ScanCandidate(
            new CandidateId(1),
            personal,
            facts,
            new CandidateDecision(CandidateStatus.Eligible, [], CandidateCategory.TemporaryFiles, "personal.tmp"));

        var result = HandleBasedFileDeleter.TryDelete(forged, context);

        Assert.Equal(CleanupOutcome.SkippedPolicy, result.Outcome);
        Assert.Equal("personal", File.ReadAllText(personal));
    }

    [Theory]
    [InlineData(0, CleanupOutcome.SkippedPolicy)]
    [InlineData(2, CleanupOutcome.SkippedMissing)]
    [InlineData(3, CleanupOutcome.SkippedMissing)]
    [InlineData(303, CleanupOutcome.SkippedMissing)]
    [InlineData(5, CleanupOutcome.SkippedAccessDenied)]
    [InlineData(32, CleanupOutcome.SkippedInUse)]
    [InlineData(33, CleanupOutcome.SkippedInUse)]
    [InlineData(1, CleanupOutcome.Failed)]
    [InlineData(87, CleanupOutcome.Failed)]
    public void OpenErrors_BecomeSkipsOrFailures_NeverDeletions(int win32Error, CleanupOutcome expected)
    {
        var result = AttemptResult.FromOpenError(win32Error);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(0, result.Bytes);
    }

    [Theory]
    [InlineData(CleanupOutcome.Deleted)]
    [InlineData(CleanupOutcome.WouldDelete)]
    public void ASkipCanNeverBeADeletion(CleanupOutcome outcome) =>
        Assert.Throws<ArgumentException>(() => AttemptResult.Skipped(outcome));
}
