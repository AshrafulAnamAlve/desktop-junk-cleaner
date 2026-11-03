using System.Reflection;
using SafeClean.App.ViewModels;
using SafeClean.Core.Models;

namespace SafeClean.App.Tests;

/// <summary>Phase 10 screens with fakes: stricter-only settings, exclusions, old-log deletion on request, history.</summary>
public sealed class SettingsAndHistoryViewModelTests
{
    [Fact]
    public void Settings_StartFromTheSavedValues_AndOfferNoAgeBelowSevenDays()
    {
        var saved = new UserSettings(30, ["Installers"], SettingsLogLevel.Warning, 14);
        var page = Pages.Settings(new FakeSettingsService(saved));

        Assert.Equal(30, page.SelectedAge.Value);
        Assert.Equal(SettingsLogLevel.Warning, page.SelectedLogLevel.Value);
        Assert.Equal(14, page.SelectedRetention.Value);
        Assert.Equal(["Installers"], page.Exclusions);
        Assert.All(page.AgeOptions, option => Assert.True(option.Value >= 7));
    }

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"..\..")]
    [InlineData(@"C:\Users\Example\Documents")]
    [InlineData(@"\Users")]
    [InlineData(@"logs\*.log")]
    public void Exclusions_OutsideTemp_OrMalformed_AreRefusedWithAReason(string input)
    {
        var page = Pages.Settings();
        page.NewExclusion = input;

        page.AddExclusionCommand.Execute(null);

        Assert.Empty(page.Exclusions);
        Assert.False(string.IsNullOrEmpty(page.ExclusionError));
    }

    [Fact]
    public void Exclusions_InsideTemp_AndPatterns_AreAdded_RemovedAndSaved()
    {
        var settings = new FakeSettingsService();
        var page = Pages.Settings(settings);

        page.NewExclusion = @"Installers\Old\";
        page.AddExclusionCommand.Execute(null);
        page.NewExclusion = "*.log";
        page.AddExclusionCommand.Execute(null);
        page.NewExclusion = "INSTALLERS\\old";
        page.AddExclusionCommand.Execute(null); // same as the first (case-insensitive)
        Assert.Contains("already", page.ExclusionError, StringComparison.Ordinal);

        page.SelectedAge = page.AgeOptions.Single(o => o.Value == 60);
        page.SaveCommand.Execute(null);

        var saved = Assert.Single(settings.Saved);
        Assert.Equal(60, saved.MinimumFileAgeDays);
        Assert.Equal([@"Installers\Old", "*.log"], saved.Exclusions);
        Assert.Equal("Saved. Changes apply from the next scan.", page.SaveStatus);

        page.RemoveExclusionCommand.Execute("*.log");
        page.SaveCommand.Execute(null);
        Assert.Equal([@"Installers\Old"], settings.Current.Exclusions);
    }

    [Fact]
    public void UnreadableSettings_AreShownOnTheSettingsPage_UntilSaved()
    {
        var settings = new FakeSettingsService(loadProblem: "Your saved settings couldn't be used (test).");
        var page = Pages.Settings(settings, prompt: new FakePrompt(true));
        Assert.Contains("couldn't be used", page.LoadProblem, StringComparison.Ordinal);

        page.SaveCommand.Execute(null);

        Assert.Null(page.LoadProblem);
    }

    [Fact]
    public void Settings_HaveNoWayToStartInRealMode()
    {
        // Real mode is never a setting: the settings record has no such field at all.
        var names = typeof(UserSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order().ToList();

        Assert.Equal(["Exclusions", "LogLevel", "LogRetentionDays", "MinimumFileAgeDays"], names);
        Assert.False(Pages.JunkCleaner(settings: new FakeSettingsService(new UserSettings(365, ["x"], SettingsLogLevel.Error, 90))).IsRealMode);
    }

    [Fact]
    public async Task OldLogs_NothingOld_NothingAsked()
    {
        var logs = new FakeLogMaintenance(oldFiles: 0);
        var prompt = new FakePrompt(true);
        var page = Pages.Settings(logs: logs, prompt: prompt);

        await page.DeleteOldLogsCommand.ExecuteAsync();

        Assert.Empty(prompt.Questions);
        Assert.Equal(0, logs.DeleteCalls);
        Assert.Contains("Nothing to delete", page.LogStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OldLogs_DeclinedQuestion_DeletesNothing()
    {
        var logs = new FakeLogMaintenance(oldFiles: 3);
        var prompt = new FakePrompt(false);
        var history = new FakeHistory();
        var page = Pages.Settings(logs: logs, prompt: prompt, history: history);

        await page.DeleteOldLogsCommand.ExecuteAsync();

        Assert.Single(prompt.Questions);
        Assert.Contains("safeclean-YYYYMMDD.log", prompt.Questions[0], StringComparison.Ordinal);
        Assert.Equal(0, logs.DeleteCalls);
        Assert.Empty(history.Entries);
        Assert.Equal("Nothing was deleted.", page.LogStatus);
    }

    [Fact]
    public async Task OldLogs_Confirmed_AreDeleted_WithTheSavedRetention_AndRecordedInHistory()
    {
        var settings = new FakeSettingsService(UserSettings.Default with { LogRetentionDays = 60 });
        var logs = new FakeLogMaintenance(oldFiles: 2);
        var history = new FakeHistory();
        var page = Pages.Settings(settings, logs, history, new FakePrompt(true));

        await page.DeleteOldLogsCommand.ExecuteAsync();

        Assert.Equal(60, logs.LastRetention);
        Assert.Equal(1, logs.DeleteCalls);
        var entry = Assert.Single(history.Entries);
        Assert.Equal(CleanupHistoryEntry.SafeCleanLogs, entry.What);
        Assert.Equal(2, entry.Deleted);
        Assert.StartsWith("Deleted 2 log files", page.LogStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OldLogs_FolderProblem_IsReported_NothingDeleted()
    {
        var logs = new FakeLogMaintenance(oldFiles: 0, problem: "This isn't SafeClean's own log folder.");
        var page = Pages.Settings(logs: logs, prompt: new FakePrompt(true));

        await page.DeleteOldLogsCommand.ExecuteAsync();

        Assert.Equal(0, logs.DeleteCalls);
        Assert.StartsWith("Nothing was deleted:", page.LogStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void History_ShowsNewestFirst_WithRefusalsAndUnreadableLines()
    {
        var history = new FakeHistory { SkippedLines = 2 };
        history.Append(new CleanupHistoryEntry(TestScans.Now.AddDays(-2), CleanupHistoryEntry.TempFolder, 5, 1, 0, 2048, 1.5, null));
        history.Append(new CleanupHistoryEntry(TestScans.Now, CleanupHistoryEntry.TempFolder, 0, 0, 0, 0, 0.1, CleanupRefusal.RootChanged));
        var page = new HistoryViewModel(history, new FakePrompt(false));

        page.OnShown();

        Assert.Equal(2, page.Entries.Count);
        Assert.True(page.Entries[0].WasRefused);              // newest first
        Assert.StartsWith("Refused — nothing deleted.", page.Entries[0].ResultText, StringComparison.Ordinal);
        Assert.Equal("5 deleted · 1 skipped · 0 failed", page.Entries[1].ResultText);
        Assert.Contains("2 lines", page.SkippedLinesText, StringComparison.Ordinal);
    }

    [Fact]
    public void History_IsClearedOnlyAfterConfirmation()
    {
        var history = new FakeHistory();
        history.Append(new CleanupHistoryEntry(TestScans.Now, CleanupHistoryEntry.TempFolder, 1, 0, 0, 10, 1, null));

        var declined = new HistoryViewModel(history, new FakePrompt(false));
        declined.OnShown();
        declined.ClearCommand.Execute(null);
        Assert.Equal(0, history.ClearCalls);

        var confirmed = new HistoryViewModel(history, new FakePrompt(true));
        confirmed.OnShown();
        confirmed.ClearCommand.Execute(null);
        Assert.Equal(1, history.ClearCalls);
        Assert.True(confirmed.IsEmpty);
    }

    [Fact]
    public void JunkCleaner_UnreadableSettings_PauseRealMode()
    {
        var settings = new FakeSettingsService(loadProblem: "Your saved settings couldn't be used (test).");
        var page = Pages.JunkCleaner(settings: settings);

        page.IsRealMode = true;

        Assert.False(page.IsRealMode);
        Assert.False(page.CanUseRealMode);
        Assert.Contains("paused", page.RealModeHint, StringComparison.Ordinal);

        settings.Save(UserSettings.Default); // saving valid settings lifts the pause
        Assert.True(page.CanUseRealMode);
    }

    [Fact]
    public async Task JunkCleaner_RecordsEachRealCleanupInHistory_WithoutFileNames()
    {
        var history = new FakeHistory();
        var page = Pages.JunkCleaner(cleanup: new FakeCleanupService(), confirmation: new FakeConfirmation(true), history: history);
        await page.ScanCommand.ExecuteAsync();
        page.AllItems.Single(i => i.Name == "a.tmp").IsSelected = true;
        page.IsRealMode = true;

        await page.DeleteCommand.ExecuteAsync();

        var entry = Assert.Single(history.Entries);
        Assert.Equal(CleanupHistoryEntry.TempFolder, entry.What);
        Assert.Equal(1, entry.Deleted);
        Assert.Equal(100, entry.BytesFreed);
        Assert.DoesNotContain(typeof(CleanupHistoryEntry).GetProperties(), p => p.Name.Contains("Path", StringComparison.Ordinal) || p.Name.Contains("File", StringComparison.Ordinal));
    }
}
