using SafeClean.App.Mvvm;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.App.ViewModels;

/// <summary>The main window: sidebar navigation, current page and global safety badges.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly JunkCleanerViewModel _junkCleaner;
    private NavigationItem _selectedItem;

    public MainViewModel(
        DashboardViewModel dashboard,
        JunkCleanerViewModel junkCleaner,
        PerformanceViewModel performance,
        HistoryViewModel history,
        SettingsViewModel settings,
        IElevationProbe elevation,
        AppInfo appInfo)
    {
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

            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(CurrentPage));
            }
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

    public string SandboxBannerText => $"SANDBOX MODE (Debug build) — working on {SandboxRoot}, not your Temp folder.";
}
