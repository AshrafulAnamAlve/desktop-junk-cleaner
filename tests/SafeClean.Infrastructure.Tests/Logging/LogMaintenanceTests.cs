using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Infrastructure.Logging;
using SafeClean.Infrastructure.Scanning;
using SafeClean.Infrastructure.Tests.Cleanup;
using SafeClean.Infrastructure.Tests.Sandbox;

namespace SafeClean.Infrastructure.Tests.Logging;

/// <summary>
/// "Delete old SafeClean logs", against a synthetic log folder in a test sandbox only. Among many look-alike
/// files, only genuinely old <c>safeclean-YYYYMMDD.log</c> files directly in the folder may go.
/// </summary>
public sealed class LogMaintenanceTests : IDisposable
{
    private readonly TestSandbox _sandbox = TestSandbox.Create();
    private readonly string _logs;
    private readonly string _oldName = Name(DateTime.Today.AddDays(-60));
    private readonly string _olderName = Name(DateTime.Today.AddDays(-45));

    public LogMaintenanceTests()
    {
        _logs = _sandbox.CreateDirectory(@"SafeClean\Logs");

        _sandbox.CreateFile($@"SafeClean\Logs\{_oldName}", "old log", ageDays: 60);              // goes
        _sandbox.CreateFile($@"SafeClean\Logs\{_olderName}", "older log", ageDays: 45);          // goes
        _sandbox.CreateFile($@"SafeClean\Logs\{Name(DateTime.Today)}", "today's log", ageDays: 0);           // too recent
        _sandbox.CreateFile($@"SafeClean\Logs\{Name(DateTime.Today.AddDays(-5))}", "recent name", ageDays: 60); // name too recent
        _sandbox.CreateFile($@"SafeClean\Logs\{Name(DateTime.Today.AddDays(-50)).Replace(".log", "", StringComparison.Ordinal)}-copy.log", "x", ageDays: 60);
        _sandbox.CreateFile(@"SafeClean\Logs\notes.txt", "user notes", ageDays: 60);                        // not a log name
        _sandbox.CreateFile(@"SafeClean\Logs\safeclean-2026.log", "short date", ageDays: 60);               // wrong date form
        _sandbox.CreateFile(@"SafeClean\Logs\safeclean-20261399.log", "impossible date", ageDays: 60);      // not a real date
        _sandbox.CreateFile($@"SafeClean\Logs\{_oldName}.bak", "backup", ageDays: 60);                     // extra extension
        _sandbox.CreateFile($@"SafeClean\Logs\{_oldName.ToUpperInvariant()}x", "upper", ageDays: 60);       // different name
        _sandbox.CreateFile($@"SafeClean\Logs\archive\{Name(DateTime.Today.AddDays(-70))}", "in a sub-folder", ageDays: 70);
        _sandbox.CreateFile(@"outside\personal.txt", "personal", ageDays: 90);
    }

    public void Dispose() => _sandbox.Dispose();

    private static string Name(DateTime day) => $"safeclean-{day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log";

    private LogMaintenance Maintenance(string? folder = null, bool elevated = false, IUserSettingsProvider? settings = null) =>
        new(folder ?? _logs, new FakeElevationProbe(elevated), new FileInspector(), TimeProvider.System, NullLoggerFactory.Instance, settings);

    /// <summary>Saved settings whose readability a test can change.</summary>
    private sealed class ChangingSettings : IUserSettingsProvider
    {
        public UserSettings Current => UserSettings.Default;

        public string? LoadProblem { get; set; }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnlyOldSafeCleanLogs_DirectlyInTheFolder_AreFound()
    {
        var plan = await Maintenance().FindOldLogsAsync(30, Token);

        Assert.Null(plan.Problem);
        var names = plan.Ids.Select(id => plan.Session!.Candidates.Single(c => c.Id == id).Decision.RelativePath).Order().ToList();
        Assert.Equal(new[] { _oldName, _olderName }.Order(), names);
        Assert.Equal("old log".Length + "older log".Length, plan.TotalBytes);
    }

    [Fact]
    public async Task Deleting_RemovesExactlyThoseFiles_AndEverythingElseSurvives()
    {
        var everythingElse = _sandbox.Snapshot()
            .Where(e => !e.Key.EndsWith(_oldName, StringComparison.Ordinal) && !e.Key.EndsWith(_olderName, StringComparison.Ordinal))
            .ToDictionary();
        var maintenance = Maintenance();
        var plan = await maintenance.FindOldLogsAsync(30, Token);

        var report = await maintenance.DeleteOldLogsAsync(plan, Token);

        Assert.Equal(2, report.DeletedCount);
        Assert.False(File.Exists(Path.Combine(_logs, _oldName)));
        Assert.False(File.Exists(Path.Combine(_logs, _olderName)));
        var after = _sandbox.Snapshot().ToDictionary();
        Assert.Equal(everythingElse.Count, after.Count);
        Assert.All(everythingElse, e => Assert.Equal(e.Value, after[e.Key])); // byte-for-byte unchanged
    }

    [Fact]
    public async Task UnreadableSettings_OfferNothing_BecauseTheChosenRetentionIsUnknown()
    {
        var plan = await Maintenance(settings: new ChangingSettings { LoadProblem = "unreadable" }).FindOldLogsAsync(30, Token);

        Assert.Contains("retention", plan.Problem, StringComparison.Ordinal);
        Assert.Empty(plan.Ids);
        Assert.Null(plan.Session);
    }

    [Fact]
    public async Task SettingsBecomingUnreadable_AfterThePlan_RefuseTheDeletion()
    {
        var settings = new ChangingSettings();
        var maintenance = Maintenance(settings: settings);
        var plan = await maintenance.FindOldLogsAsync(30, Token);
        Assert.NotEmpty(plan.Ids);
        var before = _sandbox.Snapshot().ToDictionary();

        settings.LoadProblem = "unreadable";
        var report = await maintenance.DeleteOldLogsAsync(plan, Token);

        Assert.Equal(CleanupRefusal.SettingsUnreadable, report.Refusal);
        Assert.Equal(0, report.DeletedCount);
        Assert.Equal(before, _sandbox.Snapshot().ToDictionary());
    }

    [Fact]
    public async Task LongerRetention_FindsFewerFiles()
    {
        var plan = await Maintenance().FindOldLogsAsync(50, Token);

        Assert.Equal([_oldName], plan.Ids.Select(id => plan.Session!.Candidates.Single(c => c.Id == id).Decision.RelativePath));
    }

    [Fact]
    public async Task AFolderThatIsntSafeCleansLogFolder_IsRefused()
    {
        var other = _sandbox.CreateDirectory("SomeOtherLogs");
        _sandbox.CreateFile($@"SomeOtherLogs\{_oldName}", "not ours", ageDays: 60);

        var plan = await Maintenance(other).FindOldLogsAsync(30, Token);

        Assert.NotNull(plan.Problem);
        Assert.Empty(plan.Ids);
    }

    [Fact]
    public async Task WhenElevated_DeletionIsRefused_AndNothingChanges()
    {
        var before = _sandbox.Snapshot();
        var plan = await Maintenance().FindOldLogsAsync(30, Token);

        var report = await Maintenance(elevated: true).DeleteOldLogsAsync(plan, Token);

        Assert.Equal(CleanupRefusal.Elevated, report.Refusal);
        Assert.Equal(before, _sandbox.Snapshot());
    }

    [Fact]
    public async Task APlanCannotBeStretched_ToIncludeOtherFiles()
    {
        var maintenance = Maintenance();
        var plan = await maintenance.FindOldLogsAsync(30, Token);
        var notes = plan.Session!.Candidates.Single(c => c.Decision.RelativePath == "notes.txt").Id;

        var report = await maintenance.DeleteOldLogsAsync(plan with { Ids = [.. plan.Ids, notes] }, Token);

        Assert.True(File.Exists(Path.Combine(_logs, "notes.txt")));
        Assert.DoesNotContain(report.Entries, e => e.Id == notes); // filtered out before reaching the engine
    }

    [Theory]
    [InlineData(6)]
    [InlineData(91)]
    public async Task RetentionOutsideTheAllowedRange_IsRefused(int days) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Maintenance().FindOldLogsAsync(days, Token));

    [Theory]
    [InlineData("safeclean-20260101.log", true)]
    [InlineData("SAFECLEAN-20260101.LOG", false)]
    [InlineData("safeclean-20260101.log.bak", false)]
    [InlineData(@"archive\safeclean-20260101.log", false)]
    [InlineData("safeclean-2026011.log", false)]
    [InlineData("safeclean-20260231.log", false)]
    [InlineData("safeclean-20260101-copy.log", false)]
    [InlineData(null, false)]
    public void OnlyTheExactLogNameForm_Counts(string? relativePath, bool expected) =>
        Assert.Equal(expected, LogMaintenance.IsOldLogName(relativePath, new DateOnly(2026, 6, 1)));
}
