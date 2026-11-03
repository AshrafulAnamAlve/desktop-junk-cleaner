using System.ComponentModel;
using System.IO;
using SafeClean.App.Mvvm;
using SafeClean.App.Presentation;
using SafeClean.App.Services;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;

namespace SafeClean.App.ViewModels;

/// <summary>
/// Overview page with live, read-only figures.
/// <list type="bullet">
/// <item>Anything not measured yet shows "—" or "Measuring…", never a made-up 0.</item>
/// <item>The junk card shows only real scan results, or the space freed according to a real cleanup report.</item>
/// <item>Refreshes every 2 seconds, and only while the page is visible.</item>
/// <item>No cleanup from here: "Scan Temp folder" opens the Junk Cleaner and starts a read-only scan.</item>
/// </list>
/// </summary>
public sealed class DashboardViewModel : PageViewModel
{
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly ISystemMonitor _monitor;
    private readonly JunkCleanerViewModel _junkCleaner;
    private readonly IUiTimer _timer;
    private MemorySnapshot? _memory;
    private CpuSnapshot? _cpu;
    private DriveSnapshot? _drive;
    private bool _hasRefreshed;

    public DashboardViewModel(ISystemMonitor monitor, IElevationProbe elevation, JunkCleanerViewModel junkCleaner, IUiTimerFactory timers)
    {
        _monitor = monitor;
        _junkCleaner = junkCleaner;
        IsElevated = elevation.IsElevated;
        SystemDrive = Path.GetPathRoot(System.Environment.SystemDirectory) ?? @"C:\";

        _timer = timers.Create(RefreshInterval);
        _timer.Tick += (_, _) => Refresh();
        _junkCleaner.PropertyChanged += OnJunkCleanerChanged;
        ScanCommand = new RelayCommand(() => ScanRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>The main window answers this by opening the Junk Cleaner and starting a read-only scan.</summary>
    public event EventHandler? ScanRequested;

    public override string Title => "Dashboard";

    public override string Subtitle => "An overview of your PC's storage, memory and safety status.";

    public RelayCommand ScanCommand { get; }

    /// <summary>True while the page refreshes itself (only while visible).</summary>
    public bool IsRefreshing => _timer.IsRunning;

    // ---- Memory -----------------------------------------------------------------------------------

    public string MemoryValue => DisplayText.FormatPercent(_memory?.UsedPercent);

    public string MemoryDetail => _memory is null
        ? NotYet
        : $"{DisplayText.FormatBytes(_memory.UsedBytes)} of {DisplayText.FormatBytes(_memory.TotalBytes)} in use";

    public double MemoryPercent => _memory?.UsedPercent ?? 0;

    public bool HasMemory => _memory?.UsedPercent is not null;

    // ---- Processor --------------------------------------------------------------------------------

    public string CpuValue => DisplayText.FormatPercent(_cpu?.Percent);

    public string CpuDetail => _cpu?.Percent is null ? "Measuring…" : "Across all processor cores";

    public double CpuPercent => _cpu?.Percent ?? 0;

    public bool HasCpu => _cpu?.Percent is not null;

    // ---- System drive -----------------------------------------------------------------------------

    public string SystemDrive { get; }

    public string DriveTitle => $"System drive ({SystemDrive.TrimEnd('\\')})";

    public string DriveValue => DisplayText.FormatPercent(_drive?.UsedPercent);

    public string DriveDetail => _drive is null
        ? NotYet
        : $"{DisplayText.FormatBytes(_drive.FreeBytes)} free of {DisplayText.FormatBytes(_drive.TotalBytes)}";

    public double DrivePercent => _drive?.UsedPercent ?? 0;

    public bool HasDrive => _drive?.UsedPercent is not null;

    // ---- Junk files (real scan or real cleanup report only) ---------------------------------------

    public string JunkValue
    {
        get
        {
            if (_junkCleaner.Report?.Report is { WasRefused: false } report)
            {
                return DisplayText.FormatBytes(report.BytesFreed);
            }

            return _junkCleaner.Session is { } session
                ? DisplayText.FormatBytes(session.Summary.EstimatedRecoverableBytes)
                : "—";
        }
    }

    public string JunkDetail
    {
        get
        {
            if (_junkCleaner.Report is { } report)
            {
                return report.Report switch
                {
                    { WasRefused: false } done => $"Freed by the last cleanup ({DisplayText.Count(done.DeletedCount, "file", "files")} deleted)",
                    { WasRefused: true } => "The last cleanup was refused, so nothing was deleted.",
                    null => "The last cleanup didn't finish normally. Scan again to see what remains.",
                };
            }

            return _junkCleaner.Session is { } session
                ? $"Up to this much (estimate): {DisplayText.Count(session.Summary.EligibleCount, "safe file", "safe files")}, {DisplayText.Count(session.Summary.NeedsReviewCount, "needs", "need")} review"
                : "No scan yet. A scan only reads your Temp folder.";
        }
    }

    // ---- Safety -----------------------------------------------------------------------------------

    public bool IsRealMode => _junkCleaner.IsRealMode;

    public string ModeText => IsRealMode
        ? "Real mode is on: selected files can be deleted after you confirm."
        : "Dry Run: preview only, nothing is deleted.";

    public bool IsElevated { get; }

    public string RightsText => IsElevated
        ? "Running as administrator: cleanup is disabled. Restart SafeClean normally."
        : "Running with normal user rights, as intended.";

    public override void OnShown()
    {
        Refresh();
        _timer.Start();
        OnPropertyChanged(nameof(IsRefreshing));
    }

    public override void OnHidden()
    {
        _timer.Stop();
        OnPropertyChanged(nameof(IsRefreshing));
    }

    internal void Refresh()
    {
        _memory = _monitor.GetMemory();
        _cpu = _monitor.GetCpuUsage();
        _drive = _monitor.GetDrives().FirstOrDefault(d => string.Equals(d.Name, SystemDrive, StringComparison.OrdinalIgnoreCase));
        _hasRefreshed = true;

        OnPropertyChanged(nameof(MemoryValue));
        OnPropertyChanged(nameof(MemoryDetail));
        OnPropertyChanged(nameof(MemoryPercent));
        OnPropertyChanged(nameof(HasMemory));
        OnPropertyChanged(nameof(CpuValue));
        OnPropertyChanged(nameof(CpuDetail));
        OnPropertyChanged(nameof(CpuPercent));
        OnPropertyChanged(nameof(HasCpu));
        OnPropertyChanged(nameof(DriveValue));
        OnPropertyChanged(nameof(DriveDetail));
        OnPropertyChanged(nameof(DrivePercent));
        OnPropertyChanged(nameof(HasDrive));
    }

    private string NotYet => _hasRefreshed ? "Not available" : "Measuring…";

    private void OnJunkCleanerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(JunkCleanerViewModel.Session) or nameof(JunkCleanerViewModel.Report))
        {
            OnPropertyChanged(nameof(JunkValue));
            OnPropertyChanged(nameof(JunkDetail));
        }
        else if (e.PropertyName == nameof(JunkCleanerViewModel.IsRealMode))
        {
            OnPropertyChanged(nameof(IsRealMode));
            OnPropertyChanged(nameof(ModeText));
        }
    }
}
