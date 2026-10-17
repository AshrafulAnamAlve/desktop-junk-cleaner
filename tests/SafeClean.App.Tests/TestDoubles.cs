using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.App.Services;
using SafeClean.App.ViewModels;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;

namespace SafeClean.App.Tests;

/// <summary>A cleanup service that records every call and returns what each test decides. It never touches the disk.</summary>
internal sealed class FakeCleanupService(
    Func<ScanSession, IReadOnlyCollection<CandidateId>, IProgress<CleanupProgress>?, CancellationToken, Task<CleanupReport>>? handler = null)
    : ICleanupService
{
    public List<(ScanSession Session, IReadOnlyList<CandidateId> Ids, CleanupMode Mode)> Calls { get; } = [];

    public Task<CleanupReport> CleanAsync(
        ScanSession session,
        IReadOnlyCollection<CandidateId> selected,
        CleanupMode mode,
        IProgress<CleanupProgress>? progress,
        CancellationToken cancellationToken)
    {
        Calls.Add((session, selected.ToList(), mode));
        return handler is null
            ? Task.FromResult(Report(session, selected, CleanupOutcome.Deleted))
            : handler(session, selected, progress, cancellationToken);
    }

    /// <summary>A real-mode report giving every listed ID the same outcome.</summary>
    public static CleanupReport Report(ScanSession session, IEnumerable<CandidateId> ids, CleanupOutcome outcome, bool cancelled = false) =>
        CleanupReport.Completed(
            CleanupMode.Real,
            TestScans.Now,
            TestScans.Now,
            ids.Select(id =>
            {
                var candidate = session.Candidates.Single(c => c.Id == id);
                var bytes = outcome == CleanupOutcome.Deleted ? candidate.Size : 0;
                return new CleanupEntry(id, candidate.Path, candidate.Decision.RelativePath, outcome, bytes, outcome == CleanupOutcome.Failed ? 5 : 0);
            }).ToList(),
            cancelled,
            stoppedByError: false);
}

/// <summary>Answers the confirmation dialog as the test decides, and remembers what it was shown.</summary>
internal sealed class FakeConfirmation(bool answer) : ICleanupConfirmation
{
    public List<CleanupConfirmationViewModel> Requests { get; } = [];

    public bool Confirm(CleanupConfirmationViewModel request)
    {
        Requests.Add(request);
        return answer;
    }
}

internal sealed class FakeElevation(bool isElevated) : IElevationProbe
{
    public bool IsElevated => isElevated;
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Builds a Junk Cleaner view model with fakes; every part can be replaced per test.</summary>
internal static class Pages
{
    public static JunkCleanerViewModel JunkCleaner(
        ITempScanner? scanner = null,
        ICleanupService? cleanup = null,
        ICleanupConfirmation? confirmation = null,
        bool elevated = false,
        TimeProvider? clock = null,
        string? sandboxRoot = null) =>
        new(
            scanner ?? FakeTempScanner.Returning(TestScans.Standard()),
            cleanup ?? new FakeCleanupService(),
            new FakeElevation(elevated),
            confirmation ?? new FakeConfirmation(false),
            clock ?? new FixedClock(TestScans.Now.AddMinutes(1)),
            new AppInfo("test", "logs", sandboxRoot),
            NullLogger<JunkCleanerViewModel>.Instance);
}
