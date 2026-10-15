using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using static SafeClean.Core.Tests.Policies.TestData;

namespace SafeClean.Core.Tests.Policies;

public sealed class CandidatePolicyTests
{
    [Fact]
    public void OrdinaryOldTempFileIsEligible()
    {
        var decision = Evaluate(OldFile(@"sub\setup-cache.tmp"));

        Assert.Equal(CandidateStatus.Eligible, decision.Status);
        Assert.Empty(decision.Reasons);
        Assert.True(decision.IsSelectable);
        Assert.Equal(@"sub\setup-cache.tmp", decision.RelativePath);
    }

    [Theory]
    [InlineData(FileAttributes.Normal)]
    [InlineData(FileAttributes.Archive)]
    [InlineData(FileAttributes.Archive | FileAttributes.Temporary)]
    [InlineData(FileAttributes.Archive | FileAttributes.NotContentIndexed | FileAttributes.Compressed)]
    public void AllowedAttributesAreAccepted(FileAttributes attributes)
    {
        var decision = Evaluate(OldFile("a.tmp") with { Attributes = attributes });

        Assert.Equal(CandidateStatus.Eligible, decision.Status);
    }

    [Theory]
    [InlineData(FileAttributes.ReadOnly)]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.System)]
    [InlineData(FileAttributes.Device)]
    [InlineData(FileAttributes.SparseFile)]
    [InlineData(FileAttributes.Offline)]
    [InlineData(FileAttributes.Encrypted)]
    [InlineData(FileAttributes.IntegrityStream)]
    [InlineData(FileAttributes.NoScrubData)]
    [InlineData((FileAttributes)0x40000)]  // FILE_ATTRIBUTE_RECALL_ON_OPEN (cloud file)
    [InlineData((FileAttributes)0x80000)]  // FILE_ATTRIBUTE_PINNED
    [InlineData((FileAttributes)0x100000)] // FILE_ATTRIBUTE_UNPINNED
    [InlineData((FileAttributes)0x400000)] // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
    public void EachDisallowedAttributeCausesSkip(FileAttributes extra)
    {
        var decision = Evaluate(OldFile("a.tmp") with { Attributes = FileAttributes.Archive | extra });

        Assert.Equal(CandidateStatus.Skipped, decision.Status);
        Assert.Equal([SkipReason.DisallowedAttributes], decision.Reasons);
    }

    [Fact]
    public void FolderIsNeverACandidate()
    {
        var decision = Evaluate(OldFile("folder") with { Attributes = FileAttributes.Directory });

        Assert.Equal([SkipReason.IsDirectory], decision.Reasons);
    }

    [Fact]
    public void ReparsePointIsNeverACandidate()
    {
        var decision = Evaluate(OldFile("link.tmp") with { Attributes = FileAttributes.Archive | FileAttributes.ReparsePoint });

        Assert.Equal([SkipReason.ReparsePoint], decision.Reasons);
    }

    [Fact]
    public void JunctionFolderReportsBothReasons()
    {
        var decision = Evaluate(OldFile("junction") with { Attributes = FileAttributes.Directory | FileAttributes.ReparsePoint });

        Assert.Equal([SkipReason.IsDirectory, SkipReason.ReparsePoint], decision.Reasons);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    [InlineData(5u)]
    public void HardLinkedOrDeletePendingFileIsSkipped(uint linkCount)
    {
        var decision = Evaluate(OldFile("a.tmp") with { LinkCount = linkCount });

        Assert.Equal([SkipReason.UnexpectedLinkCount], decision.Reasons);
    }

    [Fact]
    public void RecentlyCreatedFileWithOldModifiedDateIsTooRecent()
    {
        // Typical for files just extracted from an archive: old "modified" date, fresh "created" date.
        var decision = Evaluate(OldFile("extracted.dll") with { CreationTimeUtc = Now.AddDays(-1) });

        Assert.Equal([SkipReason.TooRecent], decision.Reasons);
    }

    [Fact]
    public void RecentlyModifiedFileIsTooRecent()
    {
        var decision = Evaluate(OldFile("a.tmp") with { LastWriteTimeUtc = Now.AddDays(-1) });

        Assert.Equal([SkipReason.TooRecent], decision.Reasons);
    }

    [Fact]
    public void TmpExtensionAloneNeverMakesAFileEligible()
    {
        var decision = Evaluate(OldFile("brand-new.tmp") with { CreationTimeUtc = Now.AddHours(-1), LastWriteTimeUtc = Now.AddHours(-1) });

        Assert.Equal(CandidateStatus.Skipped, decision.Status);
        Assert.Equal([SkipReason.TooRecent], decision.Reasons);
    }

    [Fact]
    public void FileExactlyAtMinimumAgeIsEligible_OneSecondYoungerIsNot()
    {
        var atLimit = Now - SafetyDefaults.MinimumFileAge;
        var exactly = Evaluate(OldFile("a.tmp") with { CreationTimeUtc = atLimit, LastWriteTimeUtc = atLimit });
        var younger = Evaluate(OldFile("a.tmp") with { CreationTimeUtc = atLimit.AddSeconds(1), LastWriteTimeUtc = atLimit.AddSeconds(1) });

        Assert.Equal(CandidateStatus.Eligible, exactly.Status);
        Assert.Equal([SkipReason.TooRecent], younger.Reasons);
    }

    [Fact]
    public void FutureTimestampIsUntrustworthy()
    {
        var decision = Evaluate(OldFile("a.tmp") with { LastWriteTimeUtc = Now.AddDays(3) });

        Assert.Equal([SkipReason.UntrustworthyTimestamp], decision.Reasons);
    }

    [Fact]
    public void ZeroedTimestampIsUntrustworthy_NotOld()
    {
        // A FILETIME of zero means 1601-01-01; it must not make a file look ancient and eligible.
        var zero = new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var decision = Evaluate(OldFile("a.tmp") with { CreationTimeUtc = zero, LastWriteTimeUtc = zero });

        Assert.Equal([SkipReason.UntrustworthyTimestamp], decision.Reasons);
    }

    [Fact]
    public void MinimumAgeCannotBeLoosenedBelowTheDefault()
    {
        var context = Context(minimumAge: TimeSpan.FromDays(1));
        var threeDaysOld = OldFile("a.tmp") with { CreationTimeUtc = Now.AddDays(-3), LastWriteTimeUtc = Now.AddDays(-3) };

        Assert.Equal(SafetyDefaults.MinimumFileAge, context.MinimumAge);
        Assert.Equal([SkipReason.TooRecent], Evaluate(threeDaysOld, context).Reasons);
    }

    [Fact]
    public void StricterMinimumAgeIsHonoured()
    {
        var context = Context(minimumAge: TimeSpan.FromDays(60));

        Assert.Equal([SkipReason.TooRecent], Evaluate(OldFile("a.tmp"), context).Reasons);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("SETUP.EXE")]
    [InlineData("helper.dll")]
    [InlineData("installer.msi")]
    [InlineData("script.ps1")]
    [InlineData("run.bat")]
    [InlineData("shortcut.lnk")]
    public void ProgramsAndScriptsNeedReview(string name)
    {
        var decision = Evaluate(OldFile(name));

        Assert.Equal(CandidateStatus.NeedsReview, decision.Status);
        Assert.Empty(decision.Reasons);
        Assert.True(decision.IsSelectable);
    }

    [Fact]
    public void RecentExecutableIsSkipped_NotMerelyNeedsReview()
    {
        var decision = Evaluate(OldFile("setup.exe") with { LastWriteTimeUtc = Now.AddDays(-1) });

        Assert.Equal(CandidateStatus.Skipped, decision.Status);
        Assert.False(decision.IsSelectable);
    }

    [Fact]
    public void FileOutsideRootIsSkipped()
    {
        var path = @"C:\Users\Example\Documents\thesis.docx";
        var decision = Evaluate(OldFile("x") with { Path = path, FinalPath = path });

        Assert.Contains(SkipReason.OutsideRoot, decision.Reasons);
        Assert.Contains(SkipReason.Protected, decision.Reasons);
        Assert.Null(decision.RelativePath);
    }

    [Fact]
    public void SiblingFolderWithSamePrefixIsOutsideRoot()
    {
        var path = TempRoot + @"2\a.tmp";
        var decision = Evaluate(OldFile("x") with { Path = path, FinalPath = path });

        Assert.Equal([SkipReason.OutsideRoot], decision.Reasons);
    }

    [Fact]
    public void RedirectedFile_FinalPathElsewhere_IsSkipped()
    {
        // Found at Temp\sub\a.tmp, but Windows says the opened file is really in Documents (a swapped-in junction).
        var facts = OldFile(@"sub\a.tmp") with { FinalPath = @"C:\Users\Example\Documents\sub\a.tmp" };

        var decision = Evaluate(facts);

        Assert.Equal(CandidateStatus.Skipped, decision.Status);
        Assert.Contains(SkipReason.OutsideRoot, decision.Reasons);
        Assert.Contains(SkipReason.PathMismatch, decision.Reasons);
        Assert.Contains(SkipReason.Protected, decision.Reasons);
    }

    [Fact]
    public void RedirectionWithinRootIsStillAMismatch()
    {
        var facts = OldFile(@"a\file.tmp") with { FinalPath = TempRoot + @"\b\file.tmp" };

        Assert.Equal([SkipReason.PathMismatch], Evaluate(facts).Reasons);
    }

    [Fact]
    public void CaseOnlyDifferenceIsTreatedAsMismatch()
    {
        var facts = OldFile("File.tmp") with { FinalPath = TempRoot + @"\file.tmp" };

        Assert.Equal([SkipReason.PathMismatch], Evaluate(facts).Reasons);
    }

    [Fact]
    public void DifferentCasingOfTheRootPrefixIsOutsideRoot()
    {
        // OS-reported paths share exact casing with the root; any difference errs toward skipping.
        var path = @"C:\USERS\EXAMPLE\AppData\Local\Temp\a.tmp";
        var decision = Evaluate(OldFile("x") with { Path = path, FinalPath = path });

        Assert.Equal([SkipReason.OutsideRoot], decision.Reasons);
    }

    [Theory]
    [InlineData(@"C:\Users\Example\AppData\Local\Temp\..\..\Roaming\a.tmp")]
    [InlineData(@"C:\Users\Example\AppData\Local\Temp\a.tmp:hidden")]
    [InlineData(@"\\?\C:\Users\Example\AppData\Local\Temp\a.tmp")]
    [InlineData(@"C:\Users\Example\AppData\Local\Temp\nul.tmp")]
    public void InvalidPathsAreSkipped(string path)
    {
        var decision = Evaluate(OldFile("x") with { Path = path, FinalPath = path });

        Assert.Contains(SkipReason.InvalidPath, decision.Reasons);
        Assert.Equal(CandidateStatus.Skipped, decision.Status);
    }

    [Fact]
    public void ProtectedLocationNestedInsideRootIsRespected()
    {
        var nestedProtection = new ProtectedLocations([TempRoot + @"\Keep"], []);
        var decision = Evaluate(OldFile(@"Keep\important.tmp"), Context(protectedLocations: nestedProtection));

        Assert.Equal([SkipReason.Protected], decision.Reasons);
    }

    [Fact]
    public void ExcludedFolderIsSkipped_CaseInsensitive()
    {
        var context = Context(exclusions: [TempRoot.ToUpperInvariant() + @"\MYAPP"]);

        Assert.Equal([SkipReason.Excluded], Evaluate(OldFile(@"MyApp\cache.tmp"), context).Reasons);
        Assert.Empty(Evaluate(OldFile(@"Other\cache.tmp"), context).Reasons);
    }

    [Fact]
    public void InvalidExclusionIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Context(exclusions: [@"..\anything"]));
    }

    [Fact]
    public void DepthLimitIsEnforced()
    {
        var atLimit = string.Concat(Enumerable.Repeat(@"d\", SafetyDefaults.MaxScanDepth)) + "a.tmp";
        var tooDeep = string.Concat(Enumerable.Repeat(@"d\", SafetyDefaults.MaxScanDepth + 1)) + "a.tmp";

        Assert.Empty(Evaluate(OldFile(atLimit)).Reasons);
        Assert.Equal([SkipReason.TooDeep], Evaluate(OldFile(tooDeep)).Reasons);
    }

    [Fact]
    public void NegativeSizeIsSkipped()
    {
        Assert.Equal([SkipReason.UnexpectedSize], Evaluate(OldFile("a.tmp") with { Size = -1 }).Reasons);
    }

    [Fact]
    public void ZeroByteAndVeryLargeFilesAreAllowed()
    {
        Assert.Empty(Evaluate(OldFile("empty.tmp") with { Size = 0 }).Reasons);
        Assert.Empty(Evaluate(OldFile("huge.tmp") with { Size = 50L * 1024 * 1024 * 1024 }).Reasons);
    }

    [Fact]
    public void EveryFailedRuleIsReported()
    {
        var facts = OldFile("a.tmp") with
        {
            Attributes = FileAttributes.Archive | FileAttributes.Hidden,
            LastWriteTimeUtc = Now.AddHours(-2),
            LinkCount = 2,
        };

        var decision = Evaluate(facts);

        Assert.Equal(CandidateStatus.Skipped, decision.Status);
        Assert.Equal([SkipReason.DisallowedAttributes, SkipReason.UnexpectedLinkCount, SkipReason.TooRecent], decision.Reasons);
    }

    [Theory]
    [InlineData("report 😀.tmp")]
    [InlineData("تقرير.tmp")]
    [InlineData("cafe\u0301.tmp")]
    [InlineData(".leading-dot.tmp")]
    [InlineData("name with  spaces.tmp")]
    public void UnicodeAndUnusualNamesAreHandled(string name)
    {
        var decision = Evaluate(OldFile(name));

        Assert.Equal(CandidateStatus.Eligible, decision.Status);
        Assert.Equal(name, decision.RelativePath);
    }

    [Fact]
    public void RightToLeftOverrideTrickStillDetectsExecutable()
    {
        // "\u202Egpj.exe" displays like "exe.jpg" in some places, but the real extension is .exe.
        var decision = Evaluate(OldFile("\u202Egpj.exe"));

        Assert.Equal(CandidateStatus.NeedsReview, decision.Status);
    }

    [Fact]
    public void LongPathIsHandled()
    {
        var relative = string.Join('\\', Enumerable.Repeat(new string('d', 60), 5)) + @"\file.tmp";
        var decision = Evaluate(OldFile(relative));

        Assert.True((TempRoot + @"\" + relative).Length > 260);
        Assert.Equal(CandidateStatus.Eligible, decision.Status);
        Assert.Equal(relative, decision.RelativePath);
    }

    [Theory]
    [InlineData("a.tmp", CandidateCategory.TemporaryFiles)]
    [InlineData("A.TEMP", CandidateCategory.TemporaryFiles)]
    [InlineData("~DF1234", CandidateCategory.TemporaryFiles)]
    [InlineData("setup.log", CandidateCategory.LogFiles)]
    [InlineData("trace.etl", CandidateCategory.LogFiles)]
    [InlineData("crash.dmp", CandidateCategory.LogFiles)]
    [InlineData("package.msi", CandidateCategory.InstallerLeftovers)]
    [InlineData("data.cab", CandidateCategory.InstallerLeftovers)]
    [InlineData("setup.exe", CandidateCategory.InstallerLeftovers)]
    [InlineData("image.png", CandidateCategory.Other)]
    [InlineData("noextension", CandidateCategory.Other)]
    public void CategoriesComeFromNameForDisplayOnly(string name, CandidateCategory expected)
    {
        Assert.Equal(expected, Evaluate(OldFile(name)).Category);
    }
}
