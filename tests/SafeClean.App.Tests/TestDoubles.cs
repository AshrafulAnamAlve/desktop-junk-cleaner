using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.App.Services;
using SafeClean.App.ViewModels;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

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

/// <summary>Monitoring readings decided by each test. Nothing is read from the real PC.</summary>
internal sealed class FakeSystemMonitor : ISystemMonitor
{
    public const long GiB = 1L << 30;

    public static readonly string SystemDrive = Path.GetPathRoot(System.Environment.SystemDirectory) ?? @"C:\";

    public MemorySnapshot? Memory { get; set; } = new(16 * GiB, 4 * GiB);

    /// <summary>Processor readings handed out in order; when empty, "measuring".</summary>
    public Queue<double?> CpuReadings { get; } = new();

    public List<DriveSnapshot> Drives { get; set; } = [new(SystemDrive, "System", "NTFS", 500 * GiB, 125 * GiB)];

    public List<ProcessMemoryInfo> Processes { get; set; } =
    [
        new("chrome", 300, 300 * 1024 * 1024L, 200 * 1024 * 1024L),
        new("Code", 100, 400 * 1024 * 1024L, 350 * 1024 * 1024L),
        new(null, 4, null, null), // a protected system process
        new("explorer", 200, 100 * 1024 * 1024L, 90 * 1024 * 1024L),
    ];

    public int ProcessCalls { get; private set; }

    /// <summary>When set, reading the drives throws this.</summary>
    public Exception? DrivesError { get; set; }

    /// <summary>When set, reading the program list throws this.</summary>
    public Exception? ProcessesError { get; set; }

    public MemorySnapshot? GetMemory() => Memory;

    public CpuSnapshot GetCpuUsage() => new(CpuReadings.Count > 0 ? CpuReadings.Dequeue() : null);

    public IReadOnlyList<DriveSnapshot> GetDrives() => DrivesError is null ? Drives : throw DrivesError;

    public IReadOnlyList<ProcessMemoryInfo> GetTopProcesses(int count)
    {
        ProcessCalls++;
        if (ProcessesError is not null)
        {
            throw ProcessesError;
        }

        return Processes.OrderByDescending(p => p.WorkingSetBytes ?? -1).Take(count).ToList();
    }
}

/// <summary>Timers that only tick when a test says so, to prove exactly when pages refresh.</summary>
internal sealed class ManualTimerFactory : IUiTimerFactory
{
    public List<ManualTimer> Created { get; } = [];

    public IUiTimer Create(TimeSpan interval)
    {
        var timer = new ManualTimer(interval);
        Created.Add(timer);
        return timer;
    }

    public ManualTimer Timer(TimeSpan interval) => Created.Single(t => t.Interval == interval);
}

internal sealed class ManualTimer(TimeSpan interval) : IUiTimer
{
    public event EventHandler? Tick;

    public TimeSpan Interval { get; } = interval;

    public bool IsRunning { get; private set; }

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    /// <summary>Raises a tick only if the timer is running, like a real timer.</summary>
    public void Fire()
    {
        if (IsRunning)
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>Builds page view models with fakes; every part can be replaced per test.</summary>
internal static class Pages
{
    public static JunkCleanerViewModel JunkCleaner(
        ITempScanner? scanner = null,
        ICleanupService? cleanup = null,
        ICleanupConfirmation? confirmation = null,
        bool elevated = false,
        TimeProvider? clock = null,
        string? sandboxRoot = null,
        ISystemMonitor? monitor = null,
        ISettingsService? settings = null,
        ICleanupHistory? history = null) =>
        new(
            scanner ?? FakeTempScanner.Returning(TestScans.Standard()),
            cleanup ?? new FakeCleanupService(),
            new FakeElevation(elevated),
            confirmation ?? new FakeConfirmation(false),
            monitor ?? new FakeSystemMonitor(),
            settings ?? new FakeSettingsService(),
            history ?? new FakeHistory(),
            clock ?? new FixedClock(TestScans.Now.AddMinutes(1)),
            new AppInfo("test", "logs", sandboxRoot),
            NullLogger<JunkCleanerViewModel>.Instance);

    public static DashboardViewModel Dashboard(ISystemMonitor monitor, JunkCleanerViewModel junkCleaner, IUiTimerFactory timers, bool elevated = false) =>
        new(monitor, new FakeElevation(elevated), junkCleaner, timers);

    public static PerformanceViewModel Performance(ISystemMonitor monitor, IUiTimerFactory timers) =>
        new(monitor, timers, new FixedClock(TestScans.Now), NullLogger<PerformanceViewModel>.Instance);

    public static SettingsViewModel Settings(
        FakeSettingsService? settings = null,
        ILogMaintenance? logs = null,
        ICleanupHistory? history = null,
        IUserPrompt? prompt = null) =>
        new(settings ?? new FakeSettingsService(), logs ?? new FakeLogMaintenance(), history ?? new FakeHistory(), prompt ?? new FakePrompt(false), new AppInfo("test", "logs"), NullLogger<SettingsViewModel>.Instance);

    public static MainViewModel Shell(DashboardViewModel dashboard, JunkCleanerViewModel junkCleaner, PerformanceViewModel performance, ILogStatus? logStatus = null) =>
        new(
            dashboard,
            junkCleaner,
            performance,
            new HistoryViewModel(new FakeHistory(), new FakePrompt(false)),
            Settings(),
            new FakeElevation(false),
            logStatus ?? new FakeLogStatus(),
            new AppInfo("test", "logs"));
}

/// <summary>Settings kept in memory, validated by the real Core rules.</summary>
internal sealed class FakeSettingsService(UserSettings? initial = null, string? loadProblem = null) : ISettingsService
{
    public event EventHandler? Changed;

    public UserSettings Current { get; private set; } = initial ?? UserSettings.Default;

    public string? LoadProblem { get; private set; } = loadProblem;

    public List<UserSettings> Saved { get; } = [];

    public SettingsValidation Save(UserSettings proposed)
    {
        var result = SettingsPolicy.Validate(proposed);
        if (result.IsValid)
        {
            Current = result.Settings!;
            LoadProblem = null;
            Saved.Add(Current);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }
}

/// <summary>History kept in memory.</summary>
internal sealed class FakeHistory : ICleanupHistory
{
    public List<CleanupHistoryEntry> Entries { get; } = [];

    public int SkippedLines { get; set; }

    public int ClearCalls { get; private set; }

    /// <summary>When set, adding an entry throws this.</summary>
    public Exception? AppendError { get; set; }

    public HistoryLoadResult Load() => new(Entries.OrderByDescending(e => e.StartedAt).ToList(), SkippedLines);

    public void Append(CleanupHistoryEntry entry)
    {
        if (AppendError is not null)
        {
            throw AppendError;
        }

        Entries.Add(entry);
    }

    public void Clear()
    {
        ClearCalls++;
        Entries.Clear();
    }
}

/// <summary>Log maintenance decided by each test; records whether deletion was asked for.</summary>
internal sealed class FakeLogMaintenance(int oldFiles = 0, string? problem = null) : ILogMaintenance
{
    public int DeleteCalls { get; private set; }

    public int? LastRetention { get; private set; }

    /// <summary>When set, finding old logs fails with this.</summary>
    public Exception? FindError { get; set; }

    public Task<LogCleanupPlan> FindOldLogsAsync(int retentionDays, CancellationToken cancellationToken)
    {
        LastRetention = retentionDays;
        if (FindError is not null)
        {
            return Task.FromException<LogCleanupPlan>(FindError);
        }

        var ids = Enumerable.Range(1, oldFiles).Select(i => new CandidateId(i)).ToList();
        var session = oldFiles == 0 ? null : TestScans.Standard().Session;
        return Task.FromResult(new LogCleanupPlan(session, ids, oldFiles * 1024L, retentionDays, problem));
    }

    public Task<CleanupReport> DeleteOldLogsAsync(LogCleanupPlan plan, CancellationToken cancellationToken)
    {
        DeleteCalls++;
        var entries = plan.Ids.Select(id => new CleanupEntry(id, $"log{id.Value}", $"log{id.Value}", CleanupOutcome.Deleted, 1024, 0)).ToList();
        return Task.FromResult(CleanupReport.Completed(CleanupMode.Real, TestScans.Now, TestScans.Now.AddSeconds(1), entries, false, false));
    }
}

/// <summary>Whether logging is on, decided by each test.</summary>
internal sealed class FakeLogStatus : ILogStatus
{
    public event EventHandler? Disabled;

    public bool IsDisabled { get; private set; }

    public void TurnOff()
    {
        IsDisabled = true;
        Disabled?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Answers yes/no questions as the test decides, and remembers them.</summary>
internal sealed class FakePrompt(bool answer) : IUserPrompt
{
    public List<string> Questions { get; } = [];

    public bool Confirm(string title, string message)
    {
        Questions.Add(title + ": " + message);
        return answer;
    }
}
