using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.Core.Models;
using SafeClean.Infrastructure.Logging;
using SafeClean.Infrastructure.Settings;
using SafeClean.Infrastructure.Tests.Sandbox;

namespace SafeClean.Infrastructure.Tests.Settings;

/// <summary>Settings and history files, in a test sandbox only (never the real %LOCALAPPDATA%\SafeClean).</summary>
public sealed class SettingsAndHistoryStoreTests : IDisposable
{
    private readonly TestSandbox _sandbox = TestSandbox.Create();

    public void Dispose() => _sandbox.Dispose();

    private string SettingsPath => _sandbox.PathFor(@"SafeClean\settings.json");

    private UserSettingsStore Store() => new(SettingsPath, NullLogger<UserSettingsStore>.Instance);

    [Fact]
    public void NoFile_MeansSafeDefaults_AndNoProblem()
    {
        var store = Store();

        Assert.Equal(UserSettings.Default.MinimumFileAgeDays, store.Current.MinimumFileAgeDays);
        Assert.Empty(store.Current.Exclusions);
        Assert.Null(store.LoadProblem);
        Assert.False(File.Exists(SettingsPath)); // reading never creates anything
    }

    [Fact]
    public void Saved_Settings_AreReadBackExactly_AndTheChangeIsAnnounced()
    {
        var store = Store();
        var changed = 0;
        store.Changed += (_, _) => changed++;

        var result = store.Save(new UserSettings(30, [@"Installers\Old", "*.log"], SettingsLogLevel.Warning, 14));
        var reloaded = Store();

        Assert.True(result.IsValid);
        Assert.Equal(1, changed);
        Assert.Equal(30, reloaded.Current.MinimumFileAgeDays);
        Assert.Equal([@"Installers\Old", "*.log"], reloaded.Current.Exclusions);
        Assert.Equal(SettingsLogLevel.Warning, reloaded.Current.LogLevel);
        Assert.Equal(14, reloaded.Current.LogRetentionDays);
        Assert.Null(reloaded.LoadProblem);
        Assert.False(File.Exists(SettingsPath + ".tmp")); // the temporary copy was swapped in, not left behind
        Assert.Contains("\"logLevel\": \"Warning\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidSettings_AreNeverWritten()
    {
        var store = Store();
        store.Save(new UserSettings(14, [], SettingsLogLevel.Information, 30));
        var before = File.ReadAllText(SettingsPath);

        var result = store.Save(new UserSettings(3, [@"C:\Windows"], SettingsLogLevel.Information, 30));

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(before, File.ReadAllText(SettingsPath));
        Assert.Equal(14, store.Current.MinimumFileAgeDays);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{ \"minimumFileAgeDays\": 3 }")]                         // hand-edited below the safe minimum
    [InlineData("{ \"exclusions\": [\"C:\\\\Windows\"] }")]               // hand-edited full path
    [InlineData("{ \"minimumFileAgeDays\": \"seven\" }")]                 // wrong type
    public void UnreadableOrInvalidFile_GivesDefaults_AndAProblemThatPausesCleanup(string content)
    {
        File.WriteAllText(_sandbox.CreateDirectory("SafeClean") + @"\settings.json", content, Encoding.UTF8);

        var store = Store();

        Assert.Equal(7, store.Current.MinimumFileAgeDays);
        Assert.Empty(store.Current.Exclusions);
        Assert.NotNull(store.LoadProblem);
        Assert.Contains("cleanup is paused", store.LoadProblem, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingValidSettings_ClearsAnEarlierProblem()
    {
        File.WriteAllText(_sandbox.CreateDirectory("SafeClean") + @"\settings.json", "garbage");
        var store = Store();
        Assert.NotNull(store.LoadProblem);

        store.Save(UserSettings.Default);

        Assert.Null(store.LoadProblem);
        Assert.Null(Store().LoadProblem);
    }

    [Theory]
    [InlineData("{ \"Exclusions\": [\"keep\"] }")]                  // wrong capital letter
    [InlineData("{ \"exclusion\": [\"keep\"] }")]                   // mistyped name
    [InlineData("{ \"minimumFileAgeDays\": 30, \"extra\": 1 }")]    // a setting SafeClean doesn't know
    public void UnknownNames_PauseCleanup_InsteadOfSilentlyDroppingThem(string content)
    {
        File.WriteAllText(_sandbox.CreateDirectory("SafeClean") + @"\settings.json", content, Encoding.UTF8);

        var store = Store();

        Assert.NotNull(store.LoadProblem);
        Assert.Contains("cleanup is paused", store.LoadProblem, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingOverAnUnusableFile_KeepsACopyOfIt()
    {
        const string Unusable = "{ \"exclusions\": [\"my-important-folder\"], \"mistake\": ";
        File.WriteAllText(_sandbox.CreateDirectory("SafeClean") + @"\settings.json", Unusable, Encoding.UTF8);
        var store = Store();

        store.Save(UserSettings.Default);

        Assert.Equal(Unusable, File.ReadAllText(SettingsPath + UserSettingsStore.UnreadableCopySuffix));
        Assert.Null(Store().LoadProblem);
    }

    [Fact]
    public void SavingNormally_MakesNoCopy()
    {
        var store = Store();
        store.Save(UserSettings.Default);
        store.Save(UserSettings.Default with { MinimumFileAgeDays = 14 });

        Assert.False(File.Exists(SettingsPath + UserSettingsStore.UnreadableCopySuffix));
    }

    [Fact]
    public void WhenWritingIsNotAllowed_SavingIsRefused_AndNothingIsWritten()
    {
        var store = new UserSettingsStore(SettingsPath, NullLogger<UserSettingsStore>.Instance, writingAllowed: false);
        var changed = 0;
        store.Changed += (_, _) => changed++;

        var result = store.Save(UserSettings.Default with { MinimumFileAgeDays = 30 });

        Assert.False(result.IsValid);
        Assert.Contains("administrator", Assert.Single(result.Errors), StringComparison.Ordinal);
        Assert.Equal(7, store.Current.MinimumFileAgeDays);
        Assert.Equal(0, changed);
        Assert.False(Directory.Exists(Path.GetDirectoryName(SettingsPath)));
    }

    [Fact]
    public void WhenWritingIsNotAllowed_AnUnusableFileStaysExactlyAsItWas_AndCleanupStaysPaused()
    {
        const string Unusable = "garbage";
        File.WriteAllText(_sandbox.CreateDirectory("SafeClean") + @"\settings.json", Unusable, Encoding.UTF8);
        var store = new UserSettingsStore(SettingsPath, NullLogger<UserSettingsStore>.Instance, writingAllowed: false);

        store.Save(UserSettings.Default);

        Assert.Equal(Unusable, File.ReadAllText(SettingsPath));
        Assert.False(File.Exists(SettingsPath + UserSettingsStore.UnreadableCopySuffix));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
        Assert.NotNull(store.LoadProblem);
    }

    // ---- History ----

    private JsonLinesCleanupHistory History() => new(_sandbox.PathFor(@"SafeClean\history.jsonl"), NullLogger<JsonLinesCleanupHistory>.Instance);

    [Fact]
    public void History_RoundTrips_NewestFirst_AndStoresNoFileNames()
    {
        var history = History();
        var older = new CleanupHistoryEntry(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), CleanupHistoryEntry.TempFolder, 3, 1, 0, 4096, 1.5, null);
        var newer = new CleanupHistoryEntry(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero), CleanupHistoryEntry.TempFolder, 0, 0, 0, 0, 0.1, CleanupRefusal.Elevated);

        history.Append(older);
        history.Append(newer);
        var loaded = History().Load();

        Assert.Equal([newer, older], loaded.Entries);
        Assert.Equal(0, loaded.SkippedLines);
        var text = File.ReadAllText(_sandbox.PathFor(@"SafeClean\history.jsonl"));
        Assert.DoesNotContain(@":\", text, StringComparison.Ordinal);         // no paths
        Assert.DoesNotContain(".tmp", text, StringComparison.OrdinalIgnoreCase); // no file names
        Assert.Contains("\"refusal\":\"Elevated\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void History_CorruptLines_AreSkippedAndCounted_NeverFatal()
    {
        var history = History();
        history.Append(new CleanupHistoryEntry(DateTimeOffset.UtcNow, CleanupHistoryEntry.TempFolder, 1, 0, 0, 10, 1, null));
        File.AppendAllText(_sandbox.PathFor(@"SafeClean\history.jsonl"), "not json\n{\"deleted\":-5,\"what\":\"x\"}\n");

        var loaded = history.Load();

        Assert.Single(loaded.Entries);
        Assert.Equal(2, loaded.SkippedLines);
    }

    [Fact]
    public void History_UnknownRefusalValue_IsSkipped_SoTheHistoryPageCanStillShowTheRest()
    {
        var history = History();
        history.Append(new CleanupHistoryEntry(DateTimeOffset.UtcNow, CleanupHistoryEntry.TempFolder, 1, 0, 0, 10, 1, null));
        File.AppendAllText(_sandbox.PathFor(@"SafeClean\history.jsonl"), "{\"what\":\"Temp folder\",\"refusal\":99}\n");

        var loaded = history.Load();

        Assert.Single(loaded.Entries);
        Assert.Equal(1, loaded.SkippedLines);
    }

    [Fact]
    public void History_WhenWritingIsNotAllowed_AddingAndClearingChangeNothing_ButReadingStillWorks()
    {
        var entry = new CleanupHistoryEntry(DateTimeOffset.UtcNow, CleanupHistoryEntry.TempFolder, 1, 0, 0, 10, 1, null);
        History().Append(entry);
        var path = _sandbox.PathFor(@"SafeClean\history.jsonl");
        var before = File.ReadAllText(path);
        var history = new JsonLinesCleanupHistory(path, NullLogger<JsonLinesCleanupHistory>.Instance, writingAllowed: false);

        history.Append(entry);
        history.Clear();

        Assert.Equal(before, File.ReadAllText(path));
        Assert.Single(history.Load().Entries);
    }

    [Fact]
    public void History_Clear_EmptiesTheFile_WithoutDeletingIt()
    {
        var history = History();
        history.Append(new CleanupHistoryEntry(DateTimeOffset.UtcNow, CleanupHistoryEntry.TempFolder, 1, 0, 0, 10, 1, null));

        history.Clear();

        var path = _sandbox.PathFor(@"SafeClean\history.jsonl");
        Assert.True(File.Exists(path));
        Assert.Equal(0, new FileInfo(path).Length);
        Assert.Empty(history.Load().Entries);
    }

    [Fact]
    public void MissingHistory_IsEmpty() => Assert.Empty(History().Load().Entries);

    // ---- Log level ----

    [Fact]
    public void HigherLogLevel_HidesInformation_ButNeverTheCleanupAuditTrail()
    {
        var directory = _sandbox.PathFor("logs");
        var day = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
        using (var provider = new FileLoggerProvider(directory, () => day, @"C:\Users\Nobody") { MinimumLevel = LogLevel.Warning })
        {
            provider.CreateLogger("SafeClean.Infrastructure.Scanning.TempScanner").LogInformation("scan details");
            provider.CreateLogger("SafeClean.Infrastructure.Scanning.TempScanner").LogWarning("scan warning");
            provider.CreateLogger("SafeClean.Infrastructure.Cleanup.CleanupService").LogInformation("Cleanup (Real) Deleted: a.tmp");
        }

        var text = File.ReadAllText(Path.Combine(directory, "safeclean-20261028.log"));
        Assert.DoesNotContain("scan details", text, StringComparison.Ordinal);
        Assert.Contains("scan warning", text, StringComparison.Ordinal);
        Assert.Contains("Cleanup (Real) Deleted: a.tmp", text, StringComparison.Ordinal);
    }
}
