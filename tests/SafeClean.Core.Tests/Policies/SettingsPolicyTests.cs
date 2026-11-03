using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using static SafeClean.Core.Tests.Policies.TestData;

namespace SafeClean.Core.Tests.Policies;

/// <summary>Settings can make SafeClean stricter, never looser (Phase 10). Plain data only.</summary>
public sealed class SettingsPolicyTests
{
    private static UserSettings With(int age = 7, int retention = 30, params string[] exclusions) =>
        new(age, exclusions, SettingsLogLevel.Information, retention);

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(0)]
    [InlineData(-10)]
    public void AgeBelowSevenDays_IsRejected(int days)
    {
        var result = SettingsPolicy.Validate(With(age: days));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("can't be lower than 7 days", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(365)]
    public void AgeWithinRange_IsAccepted(int days) => Assert.True(SettingsPolicy.Validate(With(age: days)).IsValid);

    [Fact]
    public void AgeAboveAYear_IsRejected() => Assert.False(SettingsPolicy.Validate(With(age: 366)).IsValid);

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(90, true)]
    [InlineData(91, false)]
    public void LogRetention_MustBeSevenToNinetyDays(int days, bool valid) =>
        Assert.Equal(valid, SettingsPolicy.Validate(With(retention: days)).IsValid);

    [Fact]
    public void UnknownLogLevel_IsRejected() =>
        Assert.False(SettingsPolicy.Validate(UserSettings.Default with { LogLevel = (SettingsLogLevel)42 }).IsValid);

    [Theory]
    [InlineData(@"C:\Windows")]                    // full path
    [InlineData(@"C:\Users\Example\Documents")]    // personal folder, full path
    [InlineData(@"..\..")]                         // escapes Temp
    [InlineData(@"cache\..\..\Documents")]         // escapes Temp in the middle
    [InlineData(@".\cache")]
    [InlineData(@"\Users")]                        // rooted
    [InlineData(@"\\server\share")]                // network
    [InlineData(@"cache/old")]                     // forward slash
    [InlineData(@"file.tmp:stream")]               // alternate data stream
    [InlineData("CON")]                            // device name
    [InlineData("name.")]                          // trailing dot
    [InlineData(@"logs\*.log")]                    // a pattern can't contain folders
    [InlineData("*.lo|g")]                         // invalid character in a pattern
    [InlineData("   ")]
    public void ExclusionsOutsideTemp_OrMalformed_AreRejected(string exclusion)
    {
        var result = SettingsPolicy.ValidateExclusion(exclusion);

        Assert.Null(result.Normalized);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Users\Example\Documents")]
    [InlineData(@"\Users")]
    [InlineData(@"\\server\share")]
    [InlineData("/tmp")]
    public void FullPaths_AreRefused_WithAHelpfulReason(string exclusion)
    {
        var result = SettingsPolicy.ValidateExclusion(exclusion);

        Assert.Null(result.Normalized);
        Assert.Contains("relative to your Temp folder", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Documents", "Documents")]               // a folder named Documents *inside Temp* is fine
    [InlineData(@"Installers\Old\", @"Installers\Old")]
    [InlineData("  setup.log  ", "setup.log")]
    [InlineData("*.log", "*.log")]
    [InlineData("~*.tmp", "~*.tmp")]
    [InlineData("report-????.tmp", "report-????.tmp")]
    public void ExclusionsInsideTemp_AndPatterns_AreAccepted(string exclusion, string expected) =>
        Assert.Equal(expected, SettingsPolicy.ValidateExclusion(exclusion).Normalized);

    [Fact]
    public void DuplicateExclusions_AreRemoved_IgnoringCase()
    {
        var result = SettingsPolicy.Validate(With(exclusions: [@"Cache\Old", @"cache\old\", "*.LOG", "*.log"]));

        Assert.Equal([@"Cache\Old", "*.LOG"], result.Settings!.Exclusions);
    }

    [Fact]
    public void OneBadExclusion_RejectsTheWholeSave()
    {
        var result = SettingsPolicy.Validate(With(exclusions: ["fine", @"C:\Windows"]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains(@"C:\Windows", StringComparison.Ordinal));
    }

    [Fact]
    public void TooManyExclusions_AreRejected() =>
        Assert.False(SettingsPolicy.Validate(With(exclusions: Enumerable.Range(0, 101).Select(i => "f" + i).ToArray())).IsValid);

    [Theory]
    [InlineData("setup.log", "*.log", true)]
    [InlineData("SETUP.LOG", "*.log", true)]
    [InlineData("setup.log.bak", "*.log", false)]
    [InlineData("~temp1.tmp", "~*.tmp", true)]
    [InlineData("report-2026.tmp", "report-????.tmp", true)]
    [InlineData("report-26.tmp", "report-????.tmp", false)]
    [InlineData("anything", "*", true)]
    [InlineData("a.b.c", "*.*", true)]
    [InlineData("abc", "a*b*c", true)]
    [InlineData("abd", "a*b*c", false)]
    [InlineData("", "*", true)]
    [InlineData("x", "", false)]
    public void Wildcards_MatchFileNames(string name, string pattern, bool expected) =>
        Assert.Equal(expected, WildcardPattern.IsMatch(name, pattern));

    // ---- Settings turned into rules (scanning and cleanup use the same method) ----

    private static CandidatePolicyContext RulesFor(UserSettings settings) =>
        CandidatePolicyContext.ForSettings(ApprovedRoot(), Protected, Now, settings);

    [Fact]
    public void ExcludedFolder_SkipsEverythingInside_AsExcluded()
    {
        var rules = RulesFor(With(exclusions: [@"Installers\Keep"]));

        Assert.Equal([SkipReason.Excluded], Evaluate(OldFile(@"Installers\Keep\setup.tmp"), rules).Reasons);
        Assert.Equal([SkipReason.Excluded], Evaluate(OldFile(@"installers\keep\deep\x.tmp"), rules).Reasons);
        Assert.Equal(CandidateStatus.Eligible, Evaluate(OldFile(@"Installers\Other\setup.tmp"), rules).Status);
        Assert.Equal(CandidateStatus.Eligible, Evaluate(OldFile(@"Installers\KeepNot\setup.tmp"), rules).Status); // no prefix trap
    }

    [Fact]
    public void ExcludedPattern_SkipsMatchingNamesAnywhere()
    {
        var rules = RulesFor(With(exclusions: ["*.log"]));

        Assert.Equal([SkipReason.Excluded], Evaluate(OldFile(@"deep\folder\setup.log"), rules).Reasons);
        Assert.Equal(CandidateStatus.Eligible, Evaluate(OldFile("setup.log.old"), rules).Status);
    }

    [Fact]
    public void LongerMinimumAge_KeepsYoungerFiles()
    {
        var rules = RulesFor(With(age: 60));

        Assert.Equal(TimeSpan.FromDays(60), rules.MinimumAge);
        Assert.Equal([SkipReason.TooRecent], Evaluate(OldFile("a.tmp"), rules).Reasons); // 30 days old
    }

    [Fact]
    public void NoSettings_MeansTheSafeDefaults()
    {
        var rules = CandidatePolicyContext.ForSettings(ApprovedRoot(), Protected, Now, null);

        Assert.Equal(SafetyDefaults.MinimumFileAge, rules.MinimumAge);
        Assert.Empty(rules.ExcludedSubtrees);
        Assert.Empty(rules.ExcludedNamePatterns);
    }

    [Fact]
    public void UnreadableSettings_RefuseTheCleanup()
    {
        var session = new ScanSession(Guid.NewGuid(), ApprovedRoot(), Now, Now, [], 1, 0, false);

        Assert.Equal(CleanupRefusal.SettingsUnreadable, CleanupPolicy.CheckRequest(CleanupMode.Real, false, session, Now, settingsReadable: false));
        Assert.Equal(CleanupRefusal.SettingsUnreadable, CleanupPolicy.CheckRequest(CleanupMode.DryRun, false, session, Now, settingsReadable: false));
        Assert.Null(CleanupPolicy.CheckRequest(CleanupMode.Real, false, session, Now, settingsReadable: true));
    }

    [Fact]
    public void HistoryEntry_HoldsCountsAndSizes_FromTheReport()
    {
        var report = CleanupReport.Completed(
            CleanupMode.Real,
            Now,
            Now.AddSeconds(2),
            [
                new CleanupEntry(new CandidateId(1), TempRoot + @"\a.tmp", "a.tmp", CleanupOutcome.Deleted, 100, 0),
                new CleanupEntry(new CandidateId(2), TempRoot + @"\b.tmp", "b.tmp", CleanupOutcome.SkippedInUse, 0, 32),
            ],
            false,
            false);

        var entry = CleanupHistoryEntry.From(report, CleanupHistoryEntry.TempFolder);

        Assert.Equal((1, 1, 0, 100L, 2.0), (entry.Deleted, entry.Skipped, entry.Failed, entry.BytesFreed, entry.DurationSeconds));
        Assert.Null(entry.Refusal);
    }
}
