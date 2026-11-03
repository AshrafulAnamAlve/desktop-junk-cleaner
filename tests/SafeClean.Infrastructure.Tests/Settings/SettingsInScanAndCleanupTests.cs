using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Infrastructure.Cleanup;
using SafeClean.Infrastructure.Scanning;
using SafeClean.Infrastructure.Tests.Cleanup;
using SafeClean.Infrastructure.Tests.Sandbox;

namespace SafeClean.Infrastructure.Tests.Settings;

/// <summary>The user's settings reach both scanning and cleanup (sandbox files only).</summary>
public sealed class SettingsInScanAndCleanupTests : IDisposable
{
    private readonly TestSandbox _sandbox = TestSandbox.Create();
    private readonly string _root;

    public SettingsInScanAndCleanupTests() => _root = _sandbox.CreateDirectory("root");

    public void Dispose() => _sandbox.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<ScanSession> ScanAsync(IUserSettingsProvider? settings = null)
    {
        var result = await new TempScanner(new SandboxRootResolver(_root), new FileInspector(), TimeProvider.System, NullLogger<TempScanner>.Instance, settings)
            .ScanAsync(null, Token);
        Assert.True(result.Succeeded);
        return result.Session;
    }

    private CleanupService Cleanup(IUserSettingsProvider? settings) =>
        new(new SandboxRootResolver(_root), new FakeElevationProbe(false), new FileInspector(), TimeProvider.System, NullLogger<CleanupService>.Instance, settings);

    private static ScanCandidate Find(ScanSession session, string relative) => Assert.Single(session.Candidates, c => c.Decision.RelativePath == relative);

    [Fact]
    public async Task Scan_AppliesExcludedFolders_Patterns_AndALongerAge()
    {
        _sandbox.CreateFile(@"root\keep\installer.tmp", ageDays: 200);
        _sandbox.CreateFile(@"root\setup.log", ageDays: 200);
        _sandbox.CreateFile(@"root\month-old.tmp", ageDays: 30);
        _sandbox.CreateFile(@"root\ancient.tmp", ageDays: 200);
        var settings = new FixedUserSettings(new UserSettings(90, ["keep", "*.log"], SettingsLogLevel.Information, 30));

        var session = await ScanAsync(settings);

        Assert.Equal([SkipReason.Excluded], Find(session, @"keep\installer.tmp").Decision.Reasons);
        Assert.Equal([SkipReason.Excluded], Find(session, "setup.log").Decision.Reasons);
        Assert.Equal([SkipReason.TooRecent], Find(session, "month-old.tmp").Decision.Reasons);
        Assert.Equal(CandidateStatus.Eligible, Find(session, "ancient.tmp").Decision.Status);
    }

    [Fact]
    public async Task ExclusionAddedAfterTheScan_IsHonouredByTheCleanup()
    {
        // Phase 6 follow-up: the cleanup must use the same (or newer) exclusions as the scan.
        var path = _sandbox.CreateFile(@"root\later-excluded\file.tmp");
        var session = await ScanAsync(); // no exclusions at scan time: the file is eligible
        var id = Find(session, @"later-excluded\file.tmp").Id;
        var now = new FixedUserSettings(UserSettings.Default with { Exclusions = ["later-excluded"] });

        var report = await Cleanup(now).CleanAsync(session, [id], CleanupMode.Real, null, Token);

        Assert.Equal(CleanupOutcome.SkippedPolicy, Assert.Single(report.Entries).Outcome);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task UnreadableSettings_RefuseTheWholeCleanup()
    {
        var path = _sandbox.CreateFile(@"root\a.tmp");
        var session = await ScanAsync();

        var report = await Cleanup(new BrokenSettings()).CleanAsync(session, [Find(session, "a.tmp").Id], CleanupMode.Real, null, Token);

        Assert.Equal(CleanupRefusal.SettingsUnreadable, report.Refusal);
        Assert.True(File.Exists(path));
    }

    private sealed class BrokenSettings : IUserSettingsProvider
    {
        public UserSettings Current => UserSettings.Default;

        public string? LoadProblem => "the settings file couldn't be read (test)";
    }
}
