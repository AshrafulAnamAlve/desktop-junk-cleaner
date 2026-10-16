using SafeClean.App.Mvvm;
using SafeClean.App.Presentation;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.App.ViewModels;

/// <summary>
/// A category in the side list (or "All files"). Its checkbox selects only the <b>safe</b> files in it;
/// "needs review" files are never selected in bulk. It shows a dash when only some are selected.
/// </summary>
public sealed class CategoryViewModel : ObservableObject
{
    private readonly Action<CategoryViewModel, bool> _setSelection;
    private bool? _isChecked = false;

    public CategoryViewModel(CandidateCategory? category, IReadOnlyList<CandidateItemViewModel> items, Action<CategoryViewModel, bool> setSelection)
    {
        ArgumentNullException.ThrowIfNull(items);
        Category = category;
        Items = items;
        _setSelection = setSelection;

        Title = category is { } c ? PolicyText.Describe(c) : "All files";
        EligibleCount = items.Count(i => i.Status == CandidateStatus.Eligible);
        NeedsReviewCount = items.Count(i => i.Status == CandidateStatus.NeedsReview);
        SkippedCount = items.Count - EligibleCount - NeedsReviewCount;
        EligibleBytes = items.Where(i => i.Status == CandidateStatus.Eligible).Sum(i => i.Size);
    }

    /// <summary>Null for the "All files" entry.</summary>
    public CandidateCategory? Category { get; }

    public bool IsAll => Category is null;

    public string Title { get; }

    public int EligibleCount { get; }

    public int NeedsReviewCount { get; }

    public int SkippedCount { get; }

    public long EligibleBytes { get; }

    public string CountsText => $"{EligibleCount:N0} safe · {NeedsReviewCount:N0} review · {SkippedCount:N0} kept";

    public string SizeText => DisplayText.FormatBytes(EligibleBytes) + " safe";

    /// <summary>The checkbox is only enabled when there is at least one safe file to select.</summary>
    public bool CanSelect => EligibleCount > 0;

    internal IReadOnlyList<CandidateItemViewModel> Items { get; }

    public bool? IsChecked
    {
        get => _isChecked;
        set => _setSelection(this, value == true);
    }

    /// <summary>Checked when every safe file is selected; dash when some (or only review files) are; empty otherwise.</summary>
    internal void RefreshCheckState()
    {
        var eligibleSelected = 0;
        var anySelected = false;
        foreach (var item in Items)
        {
            if (!item.IsSelected)
            {
                continue;
            }

            anySelected = true;
            if (item.Status == CandidateStatus.Eligible)
            {
                eligibleSelected++;
            }
        }

        bool? state = EligibleCount > 0 && eligibleSelected == EligibleCount ? true : anySelected ? null : false;
        SetProperty(ref _isChecked, state, nameof(IsChecked));
    }
}
