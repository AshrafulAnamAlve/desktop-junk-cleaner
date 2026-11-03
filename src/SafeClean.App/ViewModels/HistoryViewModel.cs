using System.Globalization;
using SafeClean.App.Mvvm;
using SafeClean.App.Presentation;
using SafeClean.App.Services;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.App.ViewModels;

/// <summary>Past cleanups, newest first: counts and sizes only (no file names are ever stored).</summary>
public sealed class HistoryViewModel : PageViewModel
{
    private readonly ICleanupHistory _history;
    private readonly IUserPrompt _prompt;
    private IReadOnlyList<HistoryRowViewModel> _entries = [];
    private int _skippedLines;

    public HistoryViewModel(ICleanupHistory history, IUserPrompt prompt)
    {
        _history = history;
        _prompt = prompt;
        ClearCommand = new RelayCommand(Clear, () => Entries.Count > 0);
    }

    public override string Title => "Cleanup History";

    public override string Subtitle => "A record of past cleanups on this PC (no file names are stored).";

    public RelayCommand ClearCommand { get; }

    public IReadOnlyList<HistoryRowViewModel> Entries
    {
        get => _entries;
        private set
        {
            if (SetProperty(ref _entries, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(HasEntries));
                ClearCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsEmpty => Entries.Count == 0;

    public bool HasEntries => Entries.Count > 0;

    public string SkippedLinesText => _skippedLines == 0
        ? string.Empty
        : $"{DisplayText.Count(_skippedLines, "line", "lines")} of the history file couldn't be read and were skipped.";

    public override void OnShown() => Reload();

    internal void Reload()
    {
        var result = _history.Load();
        _skippedLines = result.SkippedLines;
        Entries = result.Entries.Select(e => new HistoryRowViewModel(e)).ToList();
        OnPropertyChanged(nameof(SkippedLinesText));
    }

    private void Clear()
    {
        if (_prompt.Confirm("Clear cleanup history?", "Remove every entry from the cleanup history list? This only affects the list, not any files on your PC."))
        {
            _history.Clear();
            Reload();
        }
    }
}

/// <summary>One past cleanup.</summary>
public sealed class HistoryRowViewModel(CleanupHistoryEntry entry)
{
    public string DateText { get; } = DisplayText.FormatDate(entry.StartedAt);

    public string What { get; } = entry.What;

    public string ResultText { get; } = entry.Refusal is { } refusal
        ? "Refused — nothing deleted. " + PolicyText.Describe(refusal)
        : $"{entry.Deleted:N0} deleted · {entry.Skipped:N0} skipped · {entry.Failed:N0} failed";

    public string FreedText { get; } = entry.Refusal is null ? DisplayText.FormatBytes(entry.BytesFreed) + " freed" : "—";

    public string DurationText { get; } = string.Format(CultureInfo.CurrentCulture, "{0:N1} s", entry.DurationSeconds);

    public bool WasRefused { get; } = entry.Refusal is not null;

    public override string ToString() => $"{DateText}, {What}: {ResultText}, {FreedText}";
}
