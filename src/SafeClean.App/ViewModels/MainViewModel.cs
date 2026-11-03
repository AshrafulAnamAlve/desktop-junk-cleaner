using SafeClean.App.Mvvm;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.App.ViewModels;

/// <summary>The main window: sidebar navigation, current page and global safety badges.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly JunkCleanerViewModel _junkCleaner;
    private readonly ILogStatus _logStatus;
    private NavigationItem _selectedItem;
    private bool _isWindowVisible;

    public MainViewModel(
        DashboardViewModel dashboard,
        JunkCleanerViewModel junkCleaner,
        PerformanceViewModel performance,
        HistoryViewModel history,
        SettingsViewModel settings,
        IElevationProbe elevation,
        ILogStatus logStatus,
        AppInfo appInfo)
    {
        _logStatus = logStatus;
        _logStatus.Disabled += (_, _) => OnPropertyChanged(nameof(IsLoggingOff));
        _junkCleaner = junkCleaner;
        _junkCleaner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(JunkCleanerViewModel.IsRealMode))
            {
                OnPropertyChanged(nameof(Mode));
                OnPropertyChanged(nameof(IsRealMode));
            }
        };
        SandboxRoot = appInfo.SandboxRoot;

        NavigationItems =
        [
            new NavigationItem("Dashboard", Glyphs.Dashboard, dashboard),
            new NavigationItem("Junk Cleaner", Glyphs.JunkCleaner, junkCleaner),
            new NavigationItem("Performance", Glyphs.Performance, performance),
            new NavigationItem("History", Glyphs.History, history),
            new NavigationItem("Settings", Glyphs.Settings, settings),
        ];
        _selectedItem = NavigationItems[0];
        IsElevated = elevation.IsElevated;

        // The dashboard's "Scan Temp folder" opens the Junk Cleaner and starts a read-only scan (never a cleanup).
        dashboard.ScanRequested += async (_, _) =>
        {
            SelectedItem = NavigationItems[1];
            await junkCleaner.ScanCommand.ExecuteAsync();
        };
    }

    public IReadOnlyList<NavigationItem> NavigationItems { get; }

    public NavigationItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            // Ctrl+Click can deselect a list item; keep showing the current page rather than a blank one.
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }

            var previous = _selectedItem.Page;
            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(CurrentPage));
                if (_isWindowVisible)
                {
                    previous.OnHidden();
                    value.Page.OnShown();
                }
            }
        }
    }

    /// <summary>
    /// Called by the window when it is shown, minimised or restored. Only the visible page refreshes,
    /// and nothing refreshes while the window is minimised.
    /// </summary>
    public void SetWindowVisible(bool visible)
    {
        if (visible == _isWindowVisible)
        {
            return;
        }

        _isWindowVisible = visible;
        if (visible)
        {
            CurrentPage.OnShown();
        }
        else
        {
            CurrentPage.OnHidden();
        }
    }

    public PageViewModel CurrentPage => _selectedItem.Page;

    /// <summary>Dry Run at every start (docs/SECURITY.md §1); Real only while the Junk Cleaner's switch is on.</summary>
    public CleanupMode Mode => _junkCleaner.IsRealMode ? CleanupMode.Real : SafetyDefaults.StartupMode;

    public bool IsRealMode => Mode == CleanupMode.Real;

    public bool IsElevated { get; }

    /// <summary>Debug builds only: the test folder used instead of the real Temp folder.</summary>
    public string? SandboxRoot { get; }

    public bool IsSandbox => SandboxRoot is not null;

    public string SandboxBannerText => $"SANDBOX MODE (Debug build) — working on {SandboxRoot}, not your Temp folder. Settings, history and logs are kept beside it, not in your real ones.";

    /// <summary>The log file can't be written, so the user is told this session isn't being recorded.</summary>
    public bool IsLoggingOff => _logStatus.IsDisabled;

    public string LoggingOffText => "SafeClean can't write its log file, so this session isn't being recorded. Scanning and cleanup still apply every safety check, but the on-screen report is the only record of a cleanup.";

    /// <summary>
    /// Asked when the window is about to close. During a cleanup the answer is no: the cleanup stops before the
    /// next file and shows its report, so it is never cut off without a record. Closing again then works.
    /// </summary>
    public bool CanCloseNow()
    {
        if (!_junkCleaner.IsCleaning)
        {
            return true;
        }

        _junkCleaner.CancelCleanupCommand.Execute(null);
        return false;
    }
}
