using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using SafeClean.App.Mvvm;
using SafeClean.App.Presentation;
using SafeClean.App.Services;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.App.ViewModels;

/// <summary>
/// Settings page. Everything here can only make SafeClean <b>stricter</b> (longer minimum age, more exclusions);
/// there is no setting for Real mode, locations or protected folders. Changes apply from the next scan.
/// Also: SafeClean's own logs (level, copy folder path, delete old logs on request) and version information.
/// </summary>
public sealed class SettingsViewModel : PageViewModel
{
    private readonly ISettingsService _settings;
    private readonly ILogMaintenance _logs;
    private readonly ICleanupHistory _history;
    private readonly IUserPrompt _prompt;
    private readonly ILogger<SettingsViewModel> _logger;
    private Choice<int> _selectedAge;
    private Choice<SettingsLogLevel> _selectedLogLevel;
    private Choice<int> _selectedRetention;
    private string _newExclusion = string.Empty;
    private string _exclusionError = string.Empty;
    private string _saveStatus = string.Empty;
    private IReadOnlyList<string> _saveErrors = [];
    private string _logStatus = string.Empty;

    public SettingsViewModel(ISettingsService settings, ILogMaintenance logs, ICleanupHistory history, IUserPrompt prompt, IElevationProbe elevation, AppInfo info, ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _logs = logs;
        _history = history;
        _prompt = prompt;
        _logger = logger;
        Version = info.Version;
        LogDirectory = info.LogDirectory;

        AgeOptions = WithCurrent(SettingsPolicy.AgeChoices, settings.Current.MinimumFileAgeDays)
            .Select(d => new Choice<int>(d, d == SettingsPolicy.MinAgeDays ? $"{d} days (default)" : $"{d} days")).ToList();
        RetentionOptions = WithCurrent(SettingsPolicy.RetentionChoices, settings.Current.LogRetentionDays)
            .Select(d => new Choice<int>(d, $"{d} days")).ToList();
        LogLevelOptions =
        [
            new(SettingsLogLevel.Information, "Normal (default)"),
            new(SettingsLogLevel.Warning, "Warnings and errors"),
            new(SettingsLogLevel.Error, "Errors only"),
        ];
        _selectedAge = AgeOptions[0];
        _selectedRetention = RetentionOptions[0];
        _selectedLogLevel = LogLevelOptions[0];

        AddExclusionCommand = new RelayCommand(AddExclusion);
        RemoveExclusionCommand = new RelayCommand<string>(RemoveExclusion);

        // Nothing is written while running as administrator; the window says so.
        SaveCommand = new RelayCommand(Save, () => !elevation.IsElevated);
        RevertCommand = new RelayCommand(LoadFromCurrent);
        DeleteOldLogsCommand = new AsyncRelayCommand(DeleteOldLogsAsync, () => !elevation.IsElevated);

        LoadFromCurrent();
        _settings.Changed += (_, _) => OnPropertyChanged(nameof(LoadProblem));
    }

    public override string Title => "Settings";

    public override string Subtitle => "Make SafeClean stricter, and manage its own logs and history.";

    public string Version { get; }

    public string LogDirectory { get; }

    // ---- Cleanup rules (stricter only) ------------------------------------------------------------

    public IReadOnlyList<Choice<int>> AgeOptions { get; }

    public Choice<int> SelectedAge
    {
        get => _selectedAge;
        set
        {
            if (SetProperty(ref _selectedAge, value ?? AgeOptions[0]))
            {
                MarkNotSaved();
            }
        }
    }

    public ObservableCollection<string> Exclusions { get; } = [];

    public string NewExclusion
    {
        get => _newExclusion;
        set => SetProperty(ref _newExclusion, value ?? string.Empty);
    }

    public string ExclusionError
    {
        get => _exclusionError;
        private set => SetProperty(ref _exclusionError, value);
    }

    public RelayCommand AddExclusionCommand { get; }

    public RelayCommand<string> RemoveExclusionCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand RevertCommand { get; }

    public string SaveStatus
    {
        get => _saveStatus;
        private set => SetProperty(ref _saveStatus, value);
    }

    public IReadOnlyList<string> SaveErrors
    {
        get => _saveErrors;
        private set => SetProperty(ref _saveErrors, value);
    }

    /// <summary>Why the saved settings couldn't be used (cleanup is paused until they are saved again), or null.</summary>
    public string? LoadProblem => _settings.LoadProblem;

    // ---- Logs -------------------------------------------------------------------------------------

    public IReadOnlyList<Choice<SettingsLogLevel>> LogLevelOptions { get; }

    public Choice<SettingsLogLevel> SelectedLogLevel
    {
        get => _selectedLogLevel;
        set
        {
            if (SetProperty(ref _selectedLogLevel, value ?? LogLevelOptions[0]))
            {
                MarkNotSaved();
            }
        }
    }

    public IReadOnlyList<Choice<int>> RetentionOptions { get; }

    public Choice<int> SelectedRetention
    {
        get => _selectedRetention;
        set
        {
            if (SetProperty(ref _selectedRetention, value ?? RetentionOptions[0]))
            {
                MarkNotSaved();
            }
        }
    }

    public AsyncRelayCommand DeleteOldLogsCommand { get; }

    public string LogStatus
    {
        get => _logStatus;
        private set => SetProperty(ref _logStatus, value);
    }

    /// <summary>The settings as currently shown (not yet saved).</summary>
    internal UserSettings Edited => new(SelectedAge.Value, [.. Exclusions], SelectedLogLevel.Value, SelectedRetention.Value);

    /// <summary>
    /// A hand-edited file can hold a valid value that isn't a preset (say 45 days). It stays on offer, so saving
    /// something else can't quietly replace it with a lower preset and make SafeClean less careful.
    /// </summary>
    private static IEnumerable<int> WithCurrent(IEnumerable<int> choices, int current) => choices.Append(current).Distinct().Order();

    private void MarkNotSaved() => SaveStatus = "Not saved yet.";

    private void LoadFromCurrent()
    {
        var current = _settings.Current;
        SelectedAge = AgeOptions.FirstOrDefault(o => o.Value == current.MinimumFileAgeDays) ?? AgeOptions[0];
        SelectedLogLevel = LogLevelOptions.FirstOrDefault(o => o.Value == current.LogLevel) ?? LogLevelOptions[0];
        SelectedRetention = RetentionOptions.FirstOrDefault(o => o.Value == current.LogRetentionDays) ?? RetentionOptions[0];
        Exclusions.Clear();
        foreach (var exclusion in current.Exclusions)
        {
            Exclusions.Add(exclusion);
        }

        NewExclusion = string.Empty;
        ExclusionError = string.Empty;
        SaveErrors = [];
        SaveStatus = string.Empty;
    }

    private void AddExclusion()
    {
        var result = SettingsPolicy.ValidateExclusion(NewExclusion);
        if (result.Error is not null)
        {
            ExclusionError = result.Error;
            return;
        }

        if (Exclusions.Contains(result.Normalized!, StringComparer.OrdinalIgnoreCase))
        {
            ExclusionError = "That exclusion is already in the list.";
            return;
        }

        Exclusions.Add(result.Normalized!);
        NewExclusion = string.Empty;
        ExclusionError = string.Empty;
        MarkNotSaved();
    }

    private void RemoveExclusion(string exclusion)
    {
        Exclusions.Remove(exclusion);
        MarkNotSaved();
    }

    private void Save()
    {
        // The unreadable file may hold exclusions this page can't show, and saving lifts the cleanup pause. Ask first.
        if (_settings.LoadProblem is not null && !_prompt.Confirm(
                "Replace your saved settings?",
                "SafeClean couldn't read your saved settings, so any exclusions in them aren't shown on this page. Add them again before saving if you still need them.\n\n" +
                "Saving replaces that file (a copy is kept beside it, ending in .unreadable) and lets cleanup run again. Save now?"))
        {
            SaveStatus = "Not saved. Cleanup stays paused.";
            return;
        }

        var result = _settings.Save(Edited);
        SaveErrors = result.Errors;
        SaveStatus = result.IsValid ? "Saved. Changes apply from the next scan." : "Not saved — please fix the problems below.";
        if (result.IsValid)
        {
            LoadFromCurrent();
            SaveStatus = "Saved. Changes apply from the next scan.";
        }

        OnPropertyChanged(nameof(LoadProblem));
    }

    private async Task DeleteOldLogsAsync()
    {
        try
        {
            await FindAndDeleteOldLogsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deleting old logs stopped unexpectedly.");
            LogStatus = "This stopped because of an unexpected error. Details are in the log. Some old log files may already have been deleted.";
        }
    }

    private async Task FindAndDeleteOldLogsAsync()
    {
        var retention = _settings.Current.LogRetentionDays;
        LogStatus = "Looking for old SafeClean log files (read-only)…";
        var plan = await _logs.FindOldLogsAsync(retention, CancellationToken.None);

        if (plan.Problem is not null)
        {
            LogStatus = "Nothing was deleted: " + plan.Problem;
            return;
        }

        if (plan.FileCount == 0)
        {
            LogStatus = $"No SafeClean log files are older than {retention} days. Nothing to delete.";
            return;
        }

        var question =
            $"Permanently delete {DisplayText.Count(plan.FileCount, "of SafeClean's own log files", "of SafeClean's own log files")} " +
            $"older than {retention} days ({DisplayText.FormatBytes(plan.TotalBytes)})?\n\n" +
            "Only files named safeclean-YYYYMMDD.log in SafeClean's log folder can be affected. Nothing else is touched, " +
            "and each file is checked again before it is deleted.";
        if (!_prompt.Confirm("Delete old SafeClean logs?", question))
        {
            LogStatus = "Nothing was deleted.";
            return;
        }

        var report = await _logs.DeleteOldLogsAsync(plan, CancellationToken.None);
        _history.Append(CleanupHistoryEntry.From(report, CleanupHistoryEntry.SafeCleanLogs));
        LogStatus = report.WasRefused
            ? "Nothing was deleted: " + PolicyText.Describe(report.Refusal.Value)
            : $"Deleted {DisplayText.Count(report.DeletedCount, "log file", "log files")} ({DisplayText.FormatBytes(report.BytesFreed)}). " +
              $"{DisplayText.Count(report.SkippedCount + report.FailedCount, "file was", "files were")} kept.";
    }
}
