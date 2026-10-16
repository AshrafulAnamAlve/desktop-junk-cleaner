using Microsoft.Extensions.Logging;
using SafeClean.App.Mvvm;
using SafeClean.App.Presentation;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.App.ViewModels;

/// <summary>
/// The Junk Cleaner page: runs a read-only scan, shows the results, lets the user tick files, and shows a
/// Dry Run preview. This page contains no deletion code.
/// <list type="bullet">
/// <item>Nothing is selected after a scan; "select all safe files" never includes "needs review" files.</item>
/// <item>Skipped entries can't be selected by any means.</item>
/// <item>The selection is a set of candidate IDs from the current scan; a new scan clears it.</item>
/// </list>
/// </summary>
public sealed class JunkCleanerViewModel : PageViewModel
{
    private static readonly CandidateCategory[] CategoryOrder =
        [CandidateCategory.TemporaryFiles, CandidateCategory.LogFiles, CandidateCategory.InstallerLeftovers, CandidateCategory.Other];

    private readonly ITempScanner _scanner;
    private readonly ILogger<JunkCleanerViewModel> _logger;
    private CancellationTokenSource? _scanCancellation;
    private List<CandidateItemViewModel> _allItems = [];

    private ScanState _state;
    private string _statusText = "Not scanned yet.";
    private ScanSession? _session;
    private IReadOnlyList<string> _refusalReasons = [];
    private IReadOnlyList<CategoryViewModel> _categories = [];
    private CategoryViewModel? _selectedCategory;
    private ResultFilter _filter;
    private string _searchText = string.Empty;
    private IReadOnlyList<CandidateItemViewModel> _visibleItems = [];
    private CandidateItemViewModel? _focusedItem;
    private int _selectedCount;
    private long _selectedBytes;
    private DryRunPreview? _preview;

    public JunkCleanerViewModel(ITempScanner scanner, ILogger<JunkCleanerViewModel> logger)
    {
        _scanner = scanner;
        _logger = logger;

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning);
        CancelScanCommand = new RelayCommand(() => _scanCancellation?.Cancel(), () => IsScanning);
        SelectAllEligibleCommand = new RelayCommand(SelectAllEligible, () => HasResults);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => SelectedCount > 0);
        PreviewCommand = new RelayCommand(ShowPreview, () => SelectedCount > 0);
        DismissPreviewCommand = new RelayCommand(() => Preview = null, () => Preview is not null);
    }

    public override string Title => "Junk Cleaner";

    public override string Subtitle => "Find temporary files that are safe to remove.";

    public string AgeRuleText { get; } =
        $"Created and last changed more than {SafetyDefaults.MinimumFileAge.TotalDays:0} days ago.";

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand CancelScanCommand { get; }

    public RelayCommand SelectAllEligibleCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public RelayCommand PreviewCommand { get; }

    public RelayCommand DismissPreviewCommand { get; }

    // ---- Scan state -------------------------------------------------------------------------------

    public ScanState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsScanning));
                OnPropertyChanged(nameof(HasResults));
                OnPropertyChanged(nameof(IsEmptyResult));
                OnPropertyChanged(nameof(ShowIntro));
                OnPropertyChanged(nameof(ShowMessage));
                OnPropertyChanged(nameof(IsRefused));
                OnPropertyChanged(nameof(MessageTitle));
                OnPropertyChanged(nameof(MessageBody));
                NotifyCommands();
            }
        }
    }

    public bool IsScanning => State == ScanState.Scanning;

    public bool HasResults => State == ScanState.Completed && _allItems.Count > 0;

    public bool IsEmptyResult => State == ScanState.Completed && _allItems.Count == 0;

    public bool ShowIntro => State == ScanState.NotStarted;

    public bool ShowMessage => State is ScanState.Refused or ScanState.Cancelled or ScanState.Failed;

    public bool IsRefused => State == ScanState.Refused;

    public string MessageTitle => State switch
    {
        ScanState.Refused => "SafeClean can't scan your Temp folder",
        ScanState.Cancelled => "Scan cancelled",
        ScanState.Failed => "The scan stopped unexpectedly",
        _ => string.Empty,
    };

    public string MessageBody => State switch
    {
        ScanState.Refused => "To protect your files, SafeClean only scans your own Temp folder when it passes every safety check. These checks failed:",
        ScanState.Cancelled => "No results are shown because the scan didn't finish. Nothing was changed.",
        ScanState.Failed => "Nothing was changed. Details were written to the log file. You can try scanning again.",
        _ => string.Empty,
    };

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public IReadOnlyList<string> RefusalReasons
    {
        get => _refusalReasons;
        private set => SetProperty(ref _refusalReasons, value);
    }

    /// <summary>The scan the current results come from (null before a successful scan).</summary>
    public ScanSession? Session
    {
        get => _session;
        private set
        {
            if (SetProperty(ref _session, value))
            {
                OnPropertyChanged(nameof(EligibleCountText));
                OnPropertyChanged(nameof(EligibleSizeText));
                OnPropertyChanged(nameof(NeedsReviewCountText));
                OnPropertyChanged(nameof(NeedsReviewSizeText));
                OnPropertyChanged(nameof(SkippedCountText));
                OnPropertyChanged(nameof(ScanDetailsText));
                OnPropertyChanged(nameof(EstimatedRecoverableText));
            }
        }
    }

    // ---- Summary tiles ----------------------------------------------------------------------------

    public string EligibleCountText => DisplayText.Count(Session?.Summary.EligibleCount ?? 0, "file", "files");

    public string EligibleSizeText => DisplayText.FormatBytes(Session?.Summary.EligibleBytes ?? 0);

    public string NeedsReviewCountText => DisplayText.Count(Session?.Summary.NeedsReviewCount ?? 0, "file", "files");

    public string NeedsReviewSizeText => DisplayText.FormatBytes(Session?.Summary.NeedsReviewBytes ?? 0);

    public string SkippedCountText => DisplayText.Count(Session?.Summary.SkippedCount ?? 0, "entry", "entries");

    public string EstimatedRecoverableText => "Up to " + DisplayText.FormatBytes(Session?.Summary.EstimatedRecoverableBytes ?? 0) + " (estimate)";

    public string ScanDetailsText => Session is null
        ? string.Empty
        : $"{Session.Summary.TotalCount:N0} entries · {Session.FoldersScanned:N0} folders · {(Session.CompletedAt - Session.StartedAt).TotalSeconds:N1} s";

    // ---- Categories, filter, search ---------------------------------------------------------------

    public IReadOnlyList<CategoryViewModel> Categories
    {
        get => _categories;
        private set => SetProperty(ref _categories, value);
    }

    public CategoryViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                RefreshVisibleItems();
            }
        }
    }

    public ResultFilter Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                RefreshVisibleItems();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RefreshVisibleItems();
            }
        }
    }

    public IReadOnlyList<CandidateItemViewModel> VisibleItems
    {
        get => _visibleItems;
        private set
        {
            if (SetProperty(ref _visibleItems, value))
            {
                OnPropertyChanged(nameof(VisibleCountText));
                OnPropertyChanged(nameof(NoVisibleItems));
            }
        }
    }

    public string VisibleCountText => $"Showing {VisibleItems.Count:N0} of {_allItems.Count:N0}";

    public bool NoVisibleItems => HasResults && VisibleItems.Count == 0;

    /// <summary>The highlighted row, shown in the details panel (different from the checkbox selection).</summary>
    public CandidateItemViewModel? FocusedItem
    {
        get => _focusedItem;
        set
        {
            if (SetProperty(ref _focusedItem, value))
            {
                OnPropertyChanged(nameof(HasFocusedItem));
            }
        }
    }

    public bool HasFocusedItem => FocusedItem is not null;

    /// <summary>All rows from the current scan, largest first.</summary>
    internal IReadOnlyList<CandidateItemViewModel> AllItems => _allItems;

    // ---- Selection --------------------------------------------------------------------------------

    public int SelectedCount
    {
        get => _selectedCount;
        private set => SetProperty(ref _selectedCount, value);
    }

    public long SelectedBytes
    {
        get => _selectedBytes;
        private set => SetProperty(ref _selectedBytes, value);
    }

    public string SelectionText => SelectedCount == 0
        ? "Nothing selected. Tick files or a category to preview a cleanup."
        : $"{DisplayText.Count(SelectedCount, "file", "files")} selected · about {DisplayText.FormatBytes(SelectedBytes)} (estimate)";

    /// <summary>The IDs a future cleanup would receive — never paths.</summary>
    public IReadOnlyCollection<CandidateId> SelectedIds => _allItems.Where(i => i.IsSelected).Select(i => i.Id).ToList();

    // ---- Dry Run preview --------------------------------------------------------------------------

    public DryRunPreview? Preview
    {
        get => _preview;
        private set
        {
            if (SetProperty(ref _preview, value))
            {
                OnPropertyChanged(nameof(HasPreview));
                OnPropertyChanged(nameof(PreviewText));
                OnPropertyChanged(nameof(PreviewDetails));
                OnPropertyChanged(nameof(PreviewBreakdown));
                DismissPreviewCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasPreview => Preview is not null;

    public string PreviewText => Preview is null
        ? string.Empty
        : $"Dry Run: nothing was deleted. A real cleanup would try to delete {DisplayText.Count(Preview.FileCount, "file", "files")}, about {DisplayText.FormatBytes(Preview.EstimatedBytes)} (estimate).";

    /// <summary>"Installer leftovers: 27 files" — one line per category in the preview.</summary>
    public IReadOnlyList<string> PreviewBreakdown => Preview is null
        ? []
        : CategoryOrder
            .Where(c => Preview.FilesByCategory.ContainsKey(c))
            .Select(c => $"{PolicyText.Describe(c)}: {DisplayText.Count(Preview.FilesByCategory[c], "file", "files")}")
            .ToList();

    public string PreviewDetails
    {
        get
        {
            if (Preview is null)
            {
                return string.Empty;
            }

            var text = "Every file would be checked again right before deletion. Files that changed, are in use, or no longer pass the safety rules would be skipped, so less space may be freed.";
            if (Preview.NeedsReviewCount > 0)
            {
                text += $" {DisplayText.Count(Preview.NeedsReviewCount, "selected file needs", "selected files need")} review.";
            }

            if (Preview.IgnoredCount > 0)
            {
                text += $" {DisplayText.Count(Preview.IgnoredCount, "selection was", "selections were")} ignored because they can't be cleaned.";
            }

            return text;
        }
    }

    // ---- Actions ----------------------------------------------------------------------------------

    private async Task ScanAsync()
    {
        ClearResults();
        State = ScanState.Scanning;
        StatusText = "Starting scan…";

        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        var progress = new Progress<ScanProgress>(p =>
        {
            if (IsScanning)
            {
                StatusText = $"Examined {p.FilesExamined:N0} files in {p.FoldersScanned:N0} folders…";
            }
        });

        try
        {
            var result = await _scanner.ScanAsync(progress, cancellation.Token);
            if (!result.Succeeded)
            {
                RefusalReasons = result.RootDecision.Problems.Select(PolicyText.Describe).ToList();
                StatusText = "Scan refused by the safety checks.";
                State = ScanState.Refused;
                return;
            }

            LoadSession(result.Session);
            StatusText = $"Scan complete: {result.Session.Summary.TotalCount:N0} entries. Read-only — nothing was changed.";
            State = ScanState.Completed;
            OnPropertyChanged(nameof(VisibleCountText));
            OnPropertyChanged(nameof(NoVisibleItems));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
            State = ScanState.Cancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The scan failed.");
            StatusText = "The scan stopped because of an unexpected error.";
            State = ScanState.Failed;
        }
        finally
        {
            _scanCancellation = null;
        }
    }

    private void ClearResults()
    {
        Session = null;
        _allItems = [];
        Categories = [];
        SelectedCategory = null;
        VisibleItems = [];
        FocusedItem = null;
        RefusalReasons = [];
        Preview = null;
        RecalculateSelection();
    }

    private void LoadSession(ScanSession session)
    {
        _allItems = session.Candidates
            .Select(c => new CandidateItemViewModel(c, OnItemSelectionChanged))
            .OrderByDescending(i => i.Size)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var categories = new List<CategoryViewModel> { new(null, _allItems, SetCategorySelection) };
        foreach (var category in CategoryOrder)
        {
            var items = _allItems.Where(i => i.Category == category).ToList();
            if (items.Count > 0)
            {
                categories.Add(new CategoryViewModel(category, items, SetCategorySelection));
            }
        }

        Session = session;
        Categories = categories;
        _selectedCategory = categories[0];
        OnPropertyChanged(nameof(SelectedCategory));
        RefreshVisibleItems();
        RecalculateSelection();
    }

    private void RefreshVisibleItems()
    {
        IEnumerable<CandidateItemViewModel> query = _allItems;

        if (SelectedCategory is { Category: { } category })
        {
            query = query.Where(i => i.Category == category);
        }

        query = Filter switch
        {
            ResultFilter.Eligible => query.Where(i => i.Status == CandidateStatus.Eligible),
            ResultFilter.NeedsReview => query.Where(i => i.Status == CandidateStatus.NeedsReview),
            ResultFilter.Skipped => query.Where(i => i.Status == CandidateStatus.Skipped),
            _ => query,
        };

        var search = SearchText.Trim();
        if (search.Length > 0)
        {
            query = query.Where(i => i.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var visible = query.ToList();
        VisibleItems = visible;
        if (FocusedItem is not null && !visible.Contains(FocusedItem))
        {
            FocusedItem = null;
        }
    }

    private void SelectAllEligible()
    {
        foreach (var item in _allItems.Where(i => i.Status == CandidateStatus.Eligible))
        {
            item.SetSelectedQuietly(true);
        }

        RecalculateSelection();
    }

    private void ClearSelection()
    {
        foreach (var item in _allItems)
        {
            item.SetSelectedQuietly(false);
        }

        RecalculateSelection();
    }

    /// <summary>Ticking a category selects its safe files only; unticking clears every selection in it.</summary>
    private void SetCategorySelection(CategoryViewModel category, bool select)
    {
        foreach (var item in category.Items)
        {
            if (!select)
            {
                item.SetSelectedQuietly(false);
            }
            else if (item.Status == CandidateStatus.Eligible)
            {
                item.SetSelectedQuietly(true);
            }
        }

        RecalculateSelection();
    }

    private void OnItemSelectionChanged(CandidateItemViewModel item)
    {
        SelectedCount += item.IsSelected ? 1 : -1;
        SelectedBytes += item.IsSelected ? item.Size : -item.Size;
        foreach (var category in Categories.Where(c => c.IsAll || c.Category == item.Category))
        {
            category.RefreshCheckState();
        }

        OnSelectionChanged();
    }

    private void RecalculateSelection()
    {
        var count = 0;
        long bytes = 0;
        foreach (var item in _allItems.Where(i => i.IsSelected))
        {
            count++;
            bytes += item.Size;
        }

        SelectedCount = count;
        SelectedBytes = bytes;
        foreach (var category in Categories)
        {
            category.RefreshCheckState();
        }

        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        Preview = null; // a preview always describes the current selection
        OnPropertyChanged(nameof(SelectionText));
        NotifyCommands();
    }

    private void ShowPreview()
    {
        if (Session is not null)
        {
            Preview = DryRunPlanner.Create(Session, SelectedIds);
        }
    }

    private void NotifyCommands()
    {
        ScanCommand.NotifyCanExecuteChanged();
        CancelScanCommand.NotifyCanExecuteChanged();
        SelectAllEligibleCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
        DismissPreviewCommand.NotifyCanExecuteChanged();
    }
}
