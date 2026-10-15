using SafeClean.App.Mvvm;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.App.ViewModels;

/// <summary>The main window: sidebar navigation, current page and global safety badges.</summary>
public sealed class MainViewModel : ObservableObject
{
    private NavigationItem _selectedItem;

    public MainViewModel(
        DashboardViewModel dashboard,
        JunkCleanerViewModel junkCleaner,
        PerformanceViewModel performance,
        HistoryViewModel history,
        SettingsViewModel settings,
        IElevationProbe elevation)
    {
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

    /// <summary>Always Dry Run at startup (see docs/SECURITY.md §1).</summary>
    public CleanupMode Mode { get; } = SafetyDefaults.StartupMode;

    public bool IsElevated { get; }
}
