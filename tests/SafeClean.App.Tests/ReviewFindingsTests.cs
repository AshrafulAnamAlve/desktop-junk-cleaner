using System.ComponentModel;
using System.IO;
using SafeClean.App.ViewModels;
using SafeClean.Core.Models;

namespace SafeClean.App.Tests;

/// <summary>Phase 11: one test (or more) for each problem the security and code reviews found in the screens. Fakes only.</summary>
public sealed class ReviewFindingsTests
{
    private static async Task<JunkCleanerViewModel> ReadyToDeleteAsync(FakeSystemMonitor? monitor = null, FakeHistory? history = null, FakeCleanupService? cleanup = null)
    {
        var vm = Pages.JunkCleaner(cleanup: cleanup, confirmation: new FakeConfirmation(true), monitor: monitor, history: history);
        await vm.ScanCommand.ExecuteAsync();
        vm.AllItems.Single(i => i.Name == "a.tmp").IsSelected = true;
        vm.IsRealMode = true;
        return vm;
    }

    // ---- Settings can't quietly become less careful ----

    [Fact]
    public void ASavedValueThatIsntAPreset_StaysOnOffer_AndSavingDoesNotLowerIt()
    {
        var settings = new FakeSettingsService(new UserSettings(45, [], SettingsLogLevel.Information, 45));
        var page = Pages.Settings(settings);

        Assert.Equal(45, page.SelectedAge.Value);
        Assert.Equal(45, page.SelectedRetention.Value);

        page.NewExclusion = "*.bak";
        page.AddExclusionCommand.Execute(null);
        page.SaveCommand.Execute(null);

        Assert.Equal(45, settings.Current.MinimumFileAgeDays);
        Assert.Equal(45, settings.Current.LogRetentionDays);
        Assert.Equal(["*.bak"], settings.Current.Exclusions);
    }

    [Fact]
    public void ChangingAChoiceAfterSaving_NoLongerClaimsSaved()
    {
        var page = Pages.Settings();
        page.SaveCommand.Execute(null);
        Assert.StartsWith("Saved", page.SaveStatus, StringComparison.Ordinal);

        page.SelectedAge = page.AgeOptions.Single(o => o.Value == 60);
        Assert.Equal("Not saved yet.", page.SaveStatus);

        page.SaveCommand.Execute(null);
        page.SelectedRetention = page.RetentionOptions.Single(o => o.Value == 90);
        Assert.Equal("Not saved yet.", page.SaveStatus);

        page.SaveCommand.Execute(null);
        page.SelectedLogLevel = page.LogLevelOptions.Single(o => o.Value == SettingsLogLevel.Error);
        Assert.Equal("Not saved yet.", page.SaveStatus);

        page.RevertCommand.Execute(null);
        Assert.Equal(string.Empty, page.SaveStatus);
    }

    [Fact]
    public void UnreadableSettings_SavingAsksFirst_AndAnsweringNoKeepsCleanupPaused()
    {
        var settings = new FakeSettingsService(loadProblem: "Your saved settings couldn't be used (test).");
        var prompt = new FakePrompt(false);
        var page = Pages.Settings(settings, prompt: prompt);

        page.SaveCommand.Execute(null);

        Assert.Empty(settings.Saved);
        Assert.NotNull(settings.LoadProblem);
        Assert.Contains("paused", page.SaveStatus, StringComparison.Ordinal);
        Assert.Contains("exclusions", Assert.Single(prompt.Questions), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadableSettings_SaveWithoutAsking()
    {
        var settings = new FakeSettingsService();
        var prompt = new FakePrompt(false);
        var page = Pages.Settings(settings, prompt: prompt);

        page.SaveCommand.Execute(null);

        Assert.Empty(prompt.Questions);
        Assert.Single(settings.Saved);
    }

    [Fact]
    public async Task OldLogs_AnUnexpectedError_IsSaidOnThePage_AndDeletesNothing()
    {
        var logs = new FakeLogMaintenance(oldFiles: 3) { FindError = new IOException("test") };
        var page = Pages.Settings(logs: logs, prompt: new FakePrompt(true));

        await page.DeleteOldLogsCommand.ExecuteAsync();

        Assert.Equal(0, logs.DeleteCalls);
        Assert.Contains("unexpected error", page.LogStatus, StringComparison.Ordinal);
    }

    // ---- Nothing is written while running as administrator ----

    [Fact]
    public void WhenElevated_SettingsCantBeSaved_AndOldLogsCantBeDeleted()
    {
        var settings = new FakeSettingsService();
        var logs = new FakeLogMaintenance(oldFiles: 3);
        var prompt = new FakePrompt(true);
        var page = Pages.Settings(settings, logs: logs, prompt: prompt, elevated: true);

        Assert.False(page.SaveCommand.CanExecute(null));
        Assert.False(page.DeleteOldLogsCommand.CanExecute(null));

        page.SaveCommand.Execute(null);

        Assert.Empty(settings.Saved);
        Assert.Empty(prompt.Questions);
    }

    [Fact]
    public void NormallySettingsCanBeSaved_AndOldLogsCanBeDeleted()
    {
        var page = Pages.Settings();

        Assert.True(page.SaveCommand.CanExecute(null));
        Assert.True(page.DeleteOldLogsCommand.CanExecute(null));
    }

    [Fact]
    public void WhenElevated_HistoryCantBeCleared()
    {
        var history = new FakeHistory();
        history.Append(new CleanupHistoryEntry(TestScans.Now, CleanupHistoryEntry.TempFolder, 1, 0, 0, 10, 1, null));
        var prompt = new FakePrompt(true);
        var page = Pages.History(history, prompt, elevated: true);
        page.Reload();

        Assert.NotEmpty(page.Entries); // still shown: reading is fine
        Assert.False(page.ClearCommand.CanExecute(null));

        page.ClearCommand.Execute(null);

        Assert.Equal(0, history.ClearCalls);
        Assert.Empty(prompt.Questions);
    }

    [Fact]
    public void WhenElevated_TheWindowSaysWhyNothingIsWritten()
    {
        var logStatus = new FakeLogStatus();
        logStatus.TurnOff();
        var monitor = new FakeSystemMonitor();
        var timers = new ManualTimerFactory();
        var junk = Pages.JunkCleaner(elevated: true);

        var shell = Pages.Shell(Pages.Dashboard(monitor, junk, timers, elevated: true), junk, Pages.Performance(monitor, timers), logStatus, elevated: true);

        Assert.True(shell.IsLoggingOff);
        Assert.Contains("administrator", shell.LoggingOffText, StringComparison.Ordinal);
        Assert.Contains("settings", shell.LoggingOffText, StringComparison.Ordinal);
    }

    // ---- The Junk Cleaner tells the truth about its results ----

    [Fact]
    public void AgeRule_ShowsTheAgeInForce_AndFollowsChanges()
    {
        var settings = new FakeSettingsService();
        var vm = Pages.JunkCleaner(settings: settings);
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Contains("more than 7 days", vm.AgeRuleText, StringComparison.Ordinal);

        settings.Save(UserSettings.Default with { MinimumFileAgeDays = 30 });

        Assert.Contains("more than 30 days", vm.AgeRuleText, StringComparison.Ordinal);
        Assert.Contains(nameof(vm.AgeRuleText), changed);
    }

    [Fact]
    public async Task AScanStoppedAtItsLimit_SaysSo()
    {
        var vm = Pages.JunkCleaner(FakeTempScanner.Returning(TestScans.CutShort()));

        await vm.ScanCommand.ExecuteAsync();

        Assert.True(vm.WasCutShort);
        Assert.Contains("250,000", vm.CutShortText, StringComparison.Ordinal);
        Assert.Contains("limit", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACompleteScan_DoesNotClaimToBeCutShort()
    {
        var vm = Pages.JunkCleaner();

        await vm.ScanCommand.ExecuteAsync();

        Assert.False(vm.WasCutShort);
        Assert.StartsWith("Scan complete", vm.StatusText, StringComparison.Ordinal);
    }

    // ---- Side jobs never cost the user the report of a real cleanup ----

    [Fact]
    public async Task FreeSpaceThatCantBeRead_KeepsTheRealReport_AndFinishesTheCleanup()
    {
        var monitor = new FakeSystemMonitor { DrivesError = new IOException("test") };
        var history = new FakeHistory();
        var vm = await ReadyToDeleteAsync(monitor, history);

        await vm.DeleteCommand.ExecuteAsync();

        Assert.Equal(ScanState.CleanedUp, vm.State);
        Assert.False(vm.IsRealMode);
        Assert.NotNull(vm.Report!.Report); // the service's own report, not "unexpected error"
        Assert.Equal(1, vm.Report.Report.DeletedCount);
        Assert.False(vm.Report.HasDriveChange);
        Assert.Single(history.Entries);
    }

    [Fact]
    public async Task HistoryThatCantBeWritten_KeepsTheRealReport_AndFinishesTheCleanup()
    {
        var history = new FakeHistory { AppendError = new InvalidOperationException("test") };
        var vm = await ReadyToDeleteAsync(history: history);

        await vm.DeleteCommand.ExecuteAsync();

        Assert.Equal(ScanState.CleanedUp, vm.State);
        Assert.False(vm.IsRealMode);
        Assert.Equal(1, vm.Report!.Report!.DeletedCount);
    }

    [Fact]
    public async Task AProgramListThatCantBeRead_IsSaidOnThePage_AndTheOldListStays()
    {
        var monitor = new FakeSystemMonitor();
        var page = Pages.Performance(monitor, new ManualTimerFactory());
        await page.RefreshProcessesCommand.ExecuteAsync();
        var shown = page.Processes;
        Assert.NotEmpty(shown);

        monitor.ProcessesError = new InvalidOperationException("test");
        await page.RefreshProcessesCommand.ExecuteAsync();

        Assert.Contains("couldn't be read", page.ProcessStatus, StringComparison.Ordinal);
        Assert.Same(shown, page.Processes);
    }

    // ---- The window ----

    private static MainViewModel Shell(JunkCleanerViewModel junk, FakeLogStatus? logStatus = null)
    {
        var monitor = new FakeSystemMonitor();
        var timers = new ManualTimerFactory();
        return Pages.Shell(Pages.Dashboard(monitor, junk, timers), junk, Pages.Performance(monitor, timers), logStatus);
    }

    [Fact]
    public async Task ClosingDuringACleanup_StopsItBeforeTheNextFile_AndKeepsTheWindowOpenForTheReport()
    {
        var release = new TaskCompletionSource();
        CancellationToken seen = default;
        var cleanup = new FakeCleanupService(async (session, ids, _, token) =>
        {
            seen = token;
            await release.Task;
            return FakeCleanupService.Report(session, ids, CleanupOutcome.SkippedCancelled, cancelled: token.IsCancellationRequested);
        });
        var vm = await ReadyToDeleteAsync(cleanup: cleanup);
        var shell = Shell(vm);
        var running = vm.DeleteCommand.ExecuteAsync();
        Assert.True(vm.IsCleaning);

        Assert.False(shell.CanCloseNow());
        Assert.True(seen.IsCancellationRequested);

        release.SetResult();
        await running;

        Assert.Contains(vm.Report!.Notes, n => n.Contains("cancelled", StringComparison.Ordinal));
        Assert.True(shell.CanCloseNow());
    }

    [Fact]
    public void ClosingAtAnyOtherTime_IsAllowed() => Assert.True(Shell(Pages.JunkCleaner()).CanCloseNow());

    [Fact]
    public void WhenLoggingTurnsOff_TheWindowSaysSo()
    {
        var logStatus = new FakeLogStatus();
        var shell = Shell(Pages.JunkCleaner(), logStatus);
        var changed = new List<string?>();
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(shell.IsLoggingOff);

        logStatus.TurnOff();

        Assert.True(shell.IsLoggingOff);
        Assert.Contains(nameof(shell.IsLoggingOff), changed);
        Assert.Contains("isn't being recorded", shell.LoggingOffText, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenLoggingWasAlreadyOffAtStart_TheWindowSaysSoFromTheBeginning()
    {
        var logStatus = new FakeLogStatus();
        logStatus.TurnOff();

        Assert.True(Shell(Pages.JunkCleaner(), logStatus).IsLoggingOff);
    }
}
