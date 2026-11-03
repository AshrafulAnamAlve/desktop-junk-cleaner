using Microsoft.Extensions.Logging;
using SafeClean.App.Mvvm;
using SafeClean.App.Presentation;
using SafeClean.App.Services;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;

namespace SafeClean.App.ViewModels;

/// <summary>
/// Live, <b>read-only</b> view of memory, processor, drives and the programs using the most memory.
/// <list type="bullet">
/// <item>Memory, processor and drives refresh every 2 seconds, only while this page is visible.</item>
/// <item>The program list refreshes on demand, or every 5 seconds if the user switches that on (again only while visible).</item>
/// <item>There is nothing here that can close programs or change memory: information only.</item>
/// </list>
/// </summary>
public sealed class PerformanceViewModel : PageViewModel
{
    internal const int HistoryLength = 60;
    internal const int ProcessCount = 25;
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan ProcessRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly ISystemMonitor _monitor;
    private readonly TimeProvider _time;
    private readonly ILogger<PerformanceViewModel> _logger;
    private readonly IUiTimer _liveTimer;
    private readonly IUiTimer _processTimer;
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _memoryHistory = new();
    private MemorySnapshot? _memory;
    private CpuSnapshot? _cpu;
    private bool _hasRefreshed;
    private bool _isVisible;
    private bool _processesLoaded;
    private bool _autoRefreshProcesses;
    private IReadOnlyList<DriveRowViewModel> _drives = [];
    private IReadOnlyList<ProcessMemoryInfo> _rawProcesses = [];
    private IReadOnlyList<ProcessRowViewModel> _processes = [];
    private ProcessSortColumn _sortColumn = ProcessSortColumn.WorkingSet;
    private bool _sortDescending = true;
    private string _processStatus = "Not loaded yet.";

    public PerformanceViewModel(ISystemMonitor monitor, IUiTimerFactory timers, TimeProvider time, ILogger<PerformanceViewModel> logger)
    {
        _monitor = monitor;
        _time = time;
        _logger = logger;
        RefreshProcessesCommand = new AsyncRelayCommand(RefreshProcessesAsync);
        SortCommand = new RelayCommand<ProcessSortColumn>(SortBy);

        _liveTimer = timers.Create(RefreshInterval);
        _liveTimer.Tick += (_, _) => RefreshLive();
        _processTimer = timers.Create(ProcessRefreshInterval);
        _processTimer.Tick += async (_, _) => await RefreshProcessesCommand.ExecuteAsync();
    }

    public override string Title => "Performance";

    public override string Subtitle => "A read-only view of memory, processor and disk usage.";

    public AsyncRelayCommand RefreshProcessesCommand { get; }

    public RelayCommand<ProcessSortColumn> SortCommand { get; }

    /// <summary>True while memory/processor/drives refresh themselves (only while visible).</summary>
    public bool IsRefreshing => _liveTimer.IsRunning;

    public bool IsAutoRefreshingProcesses => _processTimer.IsRunning;

    // ---- Memory and processor ---------------------------------------------------------------------

    public string MemoryValue => DisplayText.FormatPercent(_memory?.UsedPercent);

    public string MemoryDetail => _memory is null
        ? (_hasRefreshed ? "Not available" : "Measuring…")
        : $"{DisplayText.FormatBytes(_memory.UsedBytes)} of {DisplayText.FormatBytes(_memory.TotalBytes)} in use · {DisplayText.FormatBytes(_memory.AvailableBytes)} available";

    public string CpuValue => DisplayText.FormatPercent(_cpu?.Percent);

    public string CpuDetail => _cpu?.Percent is null ? "Measuring…" : "Across all processor cores (approximate)";

    /// <summary>Last <see cref="HistoryLength"/> processor readings (0–100), oldest first.</summary>
    public IReadOnlyList<double> CpuHistory => [.. _cpuHistory];

    /// <summary>Last <see cref="HistoryLength"/> memory readings (0–100), oldest first.</summary>
    public IReadOnlyList<double> MemoryHistory => [.. _memoryHistory];

    public string HistoryCaption => $"Last {HistoryLength * RefreshInterval.TotalSeconds / 60:0} minutes (while this page is open)";

    // ---- Drives -----------------------------------------------------------------------------------

    public IReadOnlyList<DriveRowViewModel> Drives
    {
        get => _drives;
        private set => SetProperty(ref _drives, value);
    }

    // ---- Programs ---------------------------------------------------------------------------------

    public IReadOnlyList<ProcessRowViewModel> Processes
    {
        get => _processes;
        private set => SetProperty(ref _processes, value);
    }

    public string ProcessStatus
    {
        get => _processStatus;
        private set => SetProperty(ref _processStatus, value);
    }

    public ProcessSortColumn SortColumn => _sortColumn;

    public bool SortDescending => _sortDescending;

    public string NameHeader => Header("Program", ProcessSortColumn.Name);

    public string IdHeader => Header("PID", ProcessSortColumn.Id);

    public string WorkingSetHeader => Header("In RAM", ProcessSortColumn.WorkingSet);

    public string PrivateHeader => Header("Private", ProcessSortColumn.PrivateBytes);

    /// <summary>Optional 5-second refresh of the program list (off by default; never runs while the page is hidden).</summary>
    public bool AutoRefreshProcesses
    {
        get => _autoRefreshProcesses;
        set
        {
            if (SetProperty(ref _autoRefreshProcesses, value))
            {
                UpdateProcessTimer();
            }
        }
    }

    public override void OnShown()
    {
        _isVisible = true;
        RefreshLive();
        _liveTimer.Start();
        UpdateProcessTimer();
        OnPropertyChanged(nameof(IsRefreshing));
        if (!_processesLoaded)
        {
            InitialProcessLoad = RefreshProcessesCommand.ExecuteAsync();
        }
    }

    /// <summary>The first program-list load started when the page was shown (tests wait for it).</summary>
    internal Task InitialProcessLoad { get; private set; } = Task.CompletedTask;

    public override void OnHidden()
    {
        _isVisible = false;
        _liveTimer.Stop();
        UpdateProcessTimer();
        OnPropertyChanged(nameof(IsRefreshing));
    }

    internal void RefreshLive()
    {
        _memory = _monitor.GetMemory();
        _cpu = _monitor.GetCpuUsage();
        Drives = _monitor.GetDrives().Select(d => new DriveRowViewModel(d)).ToList();
        _hasRefreshed = true;

        // Unknown readings are not added to the history: a gap is honest, a 0 would be invented.
        Push(_cpuHistory, _cpu.Percent);
        Push(_memoryHistory, _memory?.UsedPercent);

        OnPropertyChanged(nameof(MemoryValue));
        OnPropertyChanged(nameof(MemoryDetail));
        OnPropertyChanged(nameof(CpuValue));
        OnPropertyChanged(nameof(CpuDetail));
        OnPropertyChanged(nameof(CpuHistory));
        OnPropertyChanged(nameof(MemoryHistory));
    }

    private async Task RefreshProcessesAsync()
    {
        ProcessStatus = "Reading the program list…";
        try
        {
            _rawProcesses = await Task.Run(() => _monitor.GetTopProcesses(ProcessCount));
        }
        catch (Exception ex)
        {
            // Said on the page rather than in an error box, which auto-refresh would bring back every 5 seconds.
            _logger.LogWarning(ex, "The program list could not be read.");
            ProcessStatus = "The program list couldn't be read just now.";
            return;
        }

        _processesLoaded = true;
        ApplySort();
        ProcessStatus = $"Top {_rawProcesses.Count} programs by memory · updated {_time.GetLocalNow():T}";
    }

    private void SortBy(ProcessSortColumn column)
    {
        if (column == _sortColumn)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = column is ProcessSortColumn.WorkingSet or ProcessSortColumn.PrivateBytes;
        }

        ApplySort();
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(IdHeader));
        OnPropertyChanged(nameof(WorkingSetHeader));
        OnPropertyChanged(nameof(PrivateHeader));
    }

    private void ApplySort()
    {
        IOrderedEnumerable<ProcessMemoryInfo> sorted = _sortColumn switch
        {
            ProcessSortColumn.Name => _sortDescending
                ? _rawProcesses.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase)
                : _rawProcesses.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            ProcessSortColumn.Id => _sortDescending ? _rawProcesses.OrderByDescending(p => p.Id) : _rawProcesses.OrderBy(p => p.Id),
            ProcessSortColumn.PrivateBytes => _sortDescending
                ? _rawProcesses.OrderByDescending(p => p.PrivateBytes ?? -1)
                : _rawProcesses.OrderBy(p => p.PrivateBytes ?? long.MaxValue),
            _ => _sortDescending
                ? _rawProcesses.OrderByDescending(p => p.WorkingSetBytes ?? -1)
                : _rawProcesses.OrderBy(p => p.WorkingSetBytes ?? long.MaxValue),
        };

        Processes = sorted.ThenBy(p => p.Id).Select(p => new ProcessRowViewModel(p)).ToList();
    }

    private void UpdateProcessTimer()
    {
        if (_isVisible && _autoRefreshProcesses)
        {
            _processTimer.Start();
        }
        else
        {
            _processTimer.Stop();
        }

        OnPropertyChanged(nameof(IsAutoRefreshingProcesses));
    }

    private string Header(string text, ProcessSortColumn column) =>
        column == _sortColumn ? $"{text} {(_sortDescending ? "▼" : "▲")}" : text;

    private static void Push(Queue<double> history, double? value)
    {
        if (value is not { } reading)
        {
            return;
        }

        history.Enqueue(reading);
        while (history.Count > HistoryLength)
        {
            history.Dequeue();
        }
    }
}
