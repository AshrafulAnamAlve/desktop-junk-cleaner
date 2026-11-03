using SafeClean.App.Mvvm;
using SafeClean.App.Presentation;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.App.ViewModels;

/// <summary>One row in the Junk Cleaner list. Skipped entries can never become selected.</summary>
public sealed class CandidateItemViewModel : ObservableObject
{
    private readonly Action<CandidateItemViewModel> _selectionChanged;
    private bool _isSelected;

    /// <param name="candidate">The scan entry.</param>
    /// <param name="selectionChanged">Called when the checkbox changes.</param>
    /// <param name="rootLabel">How the scanned folder itself is named in the list ("Temp folder", or "Sandbox folder" in Debug sandbox mode).</param>
    public CandidateItemViewModel(ScanCandidate candidate, Action<CandidateItemViewModel> selectionChanged, string rootLabel = "Temp folder")
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate = candidate;
        _selectionChanged = selectionChanged;

        var relative = candidate.Decision.RelativePath;
        var separator = relative?.LastIndexOf('\\') ?? -1;
        Name = DisplayText.Reveal(WindowsPath.GetName(candidate.Path));
        Folder = relative is null ? string.Empty : separator < 0 ? rootLabel : DisplayText.Reveal(relative[..separator]);
        FullPath = DisplayText.Reveal(candidate.Path);

        var facts = candidate.Facts;
        SizeText = facts is null ? "—" : DisplayText.FormatBytes(candidate.Size);
        ExactSizeText = facts is null ? "Unknown" : $"{candidate.Size:N0} bytes ({DisplayText.FormatBytes(candidate.Size)})";
        CreatedText = facts is null ? "Unknown" : DisplayText.FormatDate(facts.CreationTimeUtc);
        ModifiedText = facts is null ? "Unknown" : DisplayText.FormatDate(facts.LastWriteTimeUtc);
        StatusText = PolicyText.Describe(candidate.Decision.Status);
        CategoryText = PolicyText.Describe(candidate.Decision.Category);
        Reasons = candidate.Decision.Reasons.Select(PolicyText.Describe).ToList();
    }

    public ScanCandidate Candidate { get; }

    public CandidateId Id => Candidate.Id;

    public CandidateStatus Status => Candidate.Decision.Status;

    public CandidateCategory Category => Candidate.Decision.Category;

    public bool IsSelectable => Candidate.Decision.IsSelectable;

    public long Size => Candidate.Size;

    public string Name { get; }

    /// <summary>The folder below Temp that contains the entry.</summary>
    public string Folder { get; }

    public string FullPath { get; }

    public string SizeText { get; }

    public string ExactSizeText { get; }

    public string CreatedText { get; }

    public string ModifiedText { get; }

    public string StatusText { get; }

    public string CategoryText { get; }

    public IReadOnlyList<string> Reasons { get; }

    public bool HasReasons => Reasons.Count > 0;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !IsSelectable)
            {
                OnPropertyChanged(); // tell the checkbox to snap back
                return;
            }

            if (SetProperty(ref _isSelected, value))
            {
                _selectionChanged(this);
            }
        }
    }

    /// <summary>What screen readers announce for the row: "a.tmp, 100 bytes, Safe to remove".</summary>
    public string SpokenName => $"{Name}, {SizeText}, {StatusText}";

    public override string ToString() => SpokenName;

    /// <summary>Changes the selection without notifying the page (used for bulk changes, which recount once).</summary>
    internal void SetSelectedQuietly(bool value)
    {
        if ((value && !IsSelectable) || _isSelected == value)
        {
            return;
        }

        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
    }
}
