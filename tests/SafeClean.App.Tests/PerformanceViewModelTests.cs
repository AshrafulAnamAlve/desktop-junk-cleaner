using System.Reflection;
using System.Windows.Input;
using SafeClean.App.ViewModels;

namespace SafeClean.App.Tests;

/// <summary>Phase 9 performance page: live figures, history, sortable read-only program list, timers only while visible.</summary>
public sealed class PerformanceViewModelTests
{
    private readonly FakeSystemMonitor _monitor = new();
    private readonly ManualTimerFactory _timers = new();
    private readonly PerformanceViewModel _page;

    public PerformanceViewModelTests() => _page = Pages.Performance(_monitor, _timers);

    private ManualTimer LiveTimer => _timers.Created[0];

    private ManualTimer ProcessTimer => _timers.Timer(PerformanceViewModel.ProcessRefreshInterval);

    [Fact]
    public void BeforeShown_NothingIsMeasuredOrRunning()
    {
        Assert.Equal("—", _page.MemoryValue);
        Assert.Equal("Measuring…", _page.MemoryDetail);
        Assert.Equal("—", _page.CpuValue);
        Assert.Empty(_page.Processes);
        Assert.Equal(0, _monitor.ProcessCalls);
        Assert.False(LiveTimer.IsRunning);
        Assert.False(ProcessTimer.IsRunning);
    }

    [Fact]
    public async Task WhenShown_ShowsLiveFigures_AndLoadsTheProgramListOnce()
    {
        _monitor.CpuReadings.Enqueue(null);
        _monitor.CpuReadings.Enqueue(12.6);

        _page.OnShown();
        await _page.InitialProcessLoad;

        Assert.Equal("75%", _page.MemoryValue);
        Assert.Equal("Measuring…", _page.CpuDetail);
        Assert.Single(_page.Drives);
        Assert.Equal("75% used", _page.Drives[0].UsedText);
        Assert.Equal(4, _page.Processes.Count);
        Assert.Equal(1, _monitor.ProcessCalls);

        LiveTimer.Fire();
        Assert.Equal("13%", _page.CpuValue);

        _page.OnHidden();
        _page.OnShown(); // already loaded: no second automatic read
        await _page.InitialProcessLoad;
        Assert.Equal(1, _monitor.ProcessCalls);
    }

    [Fact]
    public void History_KeepsTheLast60Readings_AndSkipsUnknownOnes()
    {
        _page.OnShown();
        for (var i = 0; i < 70; i++)
        {
            _monitor.CpuReadings.Enqueue(i % 10 == 0 ? null : i);
            LiveTimer.Fire();
        }

        Assert.Equal(PerformanceViewModel.HistoryLength, _page.CpuHistory.Count);
        Assert.DoesNotContain(60.0, _page.CpuHistory); // unknown readings leave a gap, never an invented value
        Assert.Equal(69, _page.CpuHistory[^1]);
        Assert.Equal(PerformanceViewModel.HistoryLength, _page.MemoryHistory.Count);
    }

    [Fact]
    public void Timers_RunOnlyWhileVisible_AutoRefreshIncluded()
    {
        _page.OnShown();
        Assert.True(LiveTimer.IsRunning);
        Assert.False(ProcessTimer.IsRunning); // auto-refresh is off by default

        _page.AutoRefreshProcesses = true;
        Assert.True(ProcessTimer.IsRunning);

        _page.OnHidden();
        Assert.False(LiveTimer.IsRunning);
        Assert.False(ProcessTimer.IsRunning);
        Assert.True(_page.AutoRefreshProcesses); // the choice is kept…

        _page.OnShown();
        Assert.True(ProcessTimer.IsRunning); // …and resumes only when visible again
    }

    [Fact]
    public async Task ProgramList_SortsByEachColumn_AndShowsNotAvailableValues()
    {
        _page.OnShown();
        await _page.InitialProcessLoad;

        Assert.Equal(["Code", "chrome", "explorer", "n/a"], _page.Processes.Select(p => p.Name));
        Assert.Equal("In RAM ▼", _page.WorkingSetHeader);
        var unknown = _page.Processes.Single(p => p.Id == 4);
        Assert.Equal("n/a", unknown.WorkingSetText);
        Assert.Equal("n/a", unknown.PrivateText);

        _page.SortCommand.Execute(ProcessSortColumn.Id);
        Assert.Equal([4, 100, 200, 300], _page.Processes.Select(p => p.Id));
        Assert.Equal("PID ▲", _page.IdHeader);

        _page.SortCommand.Execute(ProcessSortColumn.Id); // same column again: reverse
        Assert.Equal([300, 200, 100, 4], _page.Processes.Select(p => p.Id));

        _page.SortCommand.Execute(ProcessSortColumn.PrivateBytes);
        Assert.Equal(["Code", "chrome", "explorer", "n/a"], _page.Processes.Select(p => p.Name));
        Assert.Equal("Program", _page.NameHeader);
    }

    [Fact]
    public void ThePage_HasNoCommandThatActsOnPrograms()
    {
        // Read-only by design: the only commands are refreshing and sorting — no "end task", no "free memory".
        var commands = typeof(PerformanceViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
            .Select(p => p.Name)
            .Order()
            .ToList();

        Assert.Equal(["RefreshProcessesCommand", "SortCommand"], commands);
    }
}
