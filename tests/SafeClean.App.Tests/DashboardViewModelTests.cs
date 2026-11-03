using SafeClean.App.Presentation;
using SafeClean.App.ViewModels;
using SafeClean.Core.Models;

namespace SafeClean.App.Tests;

/// <summary>Phase 9 dashboard: honest placeholders, real figures, refreshing only while visible.</summary>
public sealed class DashboardViewModelTests
{
    private readonly FakeSystemMonitor _monitor = new();
    private readonly ManualTimerFactory _timers = new();

    private DashboardViewModel Dashboard(JunkCleanerViewModel? junk = null) =>
        Pages.Dashboard(_monitor, junk ?? Pages.JunkCleaner(monitor: _monitor), _timers);

    // The dashboard is always created first in these tests, so its timer is the first one made.
    private ManualTimer Timer => _timers.Created[0];

    [Fact]
    public void BeforeAnyReading_ShowsDashesAndMeasuring_NeverZeros()
    {
        var dashboard = Dashboard();

        Assert.Equal("—", dashboard.MemoryValue);
        Assert.Equal("Measuring…", dashboard.MemoryDetail);
        Assert.Equal("—", dashboard.CpuValue);
        Assert.Equal("Measuring…", dashboard.CpuDetail);
        Assert.Equal("—", dashboard.DriveValue);
        Assert.Equal("—", dashboard.JunkValue);
        Assert.False(dashboard.HasMemory);
        Assert.False(dashboard.HasCpu);
        Assert.False(dashboard.HasDrive);
    }

    [Fact]
    public void WhenShown_ShowsRealFigures_AndTheFirstProcessorReadingIsMeasuring()
    {
        _monitor.CpuReadings.Enqueue(null);
        _monitor.CpuReadings.Enqueue(42.4);
        var dashboard = Dashboard();

        dashboard.OnShown();

        Assert.Equal("75%", dashboard.MemoryValue);
        Assert.Equal($"{DisplayText.FormatBytes(12 * FakeSystemMonitor.GiB)} of {DisplayText.FormatBytes(16 * FakeSystemMonitor.GiB)} in use", dashboard.MemoryDetail);
        Assert.Equal("—", dashboard.CpuValue);
        Assert.Equal("Measuring…", dashboard.CpuDetail);
        Assert.Equal("75%", dashboard.DriveValue);

        Timer.Fire();

        Assert.Equal("42%", dashboard.CpuValue);
        Assert.True(dashboard.HasCpu);
    }

    [Fact]
    public void WhenWindowsGivesNoAnswer_ShowsNotAvailable_NotZero()
    {
        _monitor.Memory = null;
        _monitor.Drives = [];
        var dashboard = Dashboard();

        dashboard.OnShown();

        Assert.Equal("—", dashboard.MemoryValue);
        Assert.Equal("Not available", dashboard.MemoryDetail);
        Assert.Equal("—", dashboard.DriveValue);
        Assert.Equal("Not available", dashboard.DriveDetail);
    }

    [Fact]
    public void RefreshesOnlyWhileVisible()
    {
        var dashboard = Dashboard();
        Assert.False(Timer.IsRunning); // nothing runs before the page is shown

        dashboard.OnShown();
        Assert.True(Timer.IsRunning);
        Assert.True(dashboard.IsRefreshing);

        dashboard.OnHidden();
        Assert.False(Timer.IsRunning);
        _monitor.Memory = new MemorySnapshot(16 * FakeSystemMonitor.GiB, 8 * FakeSystemMonitor.GiB);
        Timer.Fire(); // a stopped timer doesn't tick
        Assert.Equal("75%", dashboard.MemoryValue);
    }

    [Fact]
    public async Task JunkCard_ShowsTheScanEstimate_ThenSpaceFreedFromTheRealReport()
    {
        var junk = Pages.JunkCleaner(cleanup: new FakeCleanupService(), confirmation: new FakeConfirmation(true), monitor: _monitor);
        var dashboard = Dashboard(junk);
        Assert.Equal("No scan yet. A scan only reads your Temp folder.", dashboard.JunkDetail);

        await junk.ScanCommand.ExecuteAsync();
        Assert.Equal(DisplayText.FormatBytes(junk.Session!.Summary.EstimatedRecoverableBytes), dashboard.JunkValue);
        Assert.Contains("estimate", dashboard.JunkDetail, StringComparison.Ordinal);

        junk.AllItems.Single(i => i.Name == "a.tmp").IsSelected = true;
        junk.IsRealMode = true;
        await junk.DeleteCommand.ExecuteAsync();

        Assert.Equal(DisplayText.FormatBytes(100), dashboard.JunkValue);
        Assert.Equal("Freed by the last cleanup (1 file deleted)", dashboard.JunkDetail);
    }

    [Fact]
    public async Task JunkCard_NeverShowsAFreedNumber_WithoutARealReport()
    {
        var refused = new FakeCleanupService((_, _, _, _) =>
            Task.FromResult(CleanupReport.Refused(CleanupMode.Real, TestScans.Now, CleanupRefusal.RootChanged)));
        var junk = Pages.JunkCleaner(cleanup: refused, confirmation: new FakeConfirmation(true), monitor: _monitor);
        var dashboard = Dashboard(junk);
        await junk.ScanCommand.ExecuteAsync();
        junk.AllItems.Single(i => i.Name == "a.tmp").IsSelected = true;
        junk.IsRealMode = true;

        await junk.DeleteCommand.ExecuteAsync();

        Assert.Equal("—", dashboard.JunkValue);
        Assert.Contains("nothing was deleted", dashboard.JunkDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void SafetyCard_FollowsTheRealModeSwitch()
    {
        var junk = Pages.JunkCleaner(monitor: _monitor);
        var dashboard = Dashboard(junk);
        Assert.StartsWith("Dry Run", dashboard.ModeText, StringComparison.Ordinal);

        junk.IsRealMode = true;

        Assert.True(dashboard.IsRealMode);
        Assert.StartsWith("Real mode is on", dashboard.ModeText, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanButton_OpensTheJunkCleaner_AndStartsAReadOnlyScan_NeverACleanup()
    {
        var scanner = FakeTempScanner.Returning(TestScans.Standard());
        var cleanup = new FakeCleanupService();
        var junk = Pages.JunkCleaner(scanner: scanner, cleanup: cleanup, monitor: _monitor);
        var dashboard = Dashboard(junk);
        var main = Pages.Shell(dashboard, junk, Pages.Performance(_monitor, _timers));

        dashboard.ScanCommand.Execute(null);

        Assert.Same(junk, main.CurrentPage);
        Assert.Equal(1, scanner.Calls);
        Assert.Empty(cleanup.Calls);
    }

    [Fact]
    public void OnlyTheVisiblePageRefreshes_AndNothingWhileMinimised()
    {
        var junk = Pages.JunkCleaner(monitor: _monitor);
        var dashboard = Dashboard(junk);
        var performance = Pages.Performance(_monitor, _timers);
        var main = Pages.Shell(dashboard, junk, performance);
        var performanceTimer = _timers.Created[1]; // the performance page's 2-second timer

        main.SetWindowVisible(true);
        Assert.True(Timer.IsRunning);
        Assert.False(performanceTimer.IsRunning);

        main.SelectedItem = main.NavigationItems.Single(n => n.Page == performance);
        Assert.False(Timer.IsRunning);
        Assert.True(performanceTimer.IsRunning);

        main.SetWindowVisible(false); // minimised
        Assert.False(Timer.IsRunning);
        Assert.False(performanceTimer.IsRunning);
    }
}
