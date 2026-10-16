using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.App.ViewModels;
using SafeClean.Core.Models;

namespace SafeClean.App.Tests;

public sealed class JunkCleanerViewModelTests
{
    private static JunkCleanerViewModel Create(FakeTempScanner scanner) => new(scanner, NullLogger<JunkCleanerViewModel>.Instance);

    private static async Task<JunkCleanerViewModel> ScannedAsync(ScanResult? result = null)
    {
        var vm = Create(FakeTempScanner.Returning(result ?? TestScans.Standard()));
        await vm.ScanCommand.ExecuteAsync();
        return vm;
    }

    private static CandidateItemViewModel Item(JunkCleanerViewModel vm, string name) => vm.AllItems.Single(i => i.Name == name);

    private static CategoryViewModel Category(JunkCleanerViewModel vm, CandidateCategory category) =>
        vm.Categories.Single(c => c.Category == category);

    [Fact]
    public void BeforeScanning_ShowsIntroAndOffersNothingToSelect()
    {
        var vm = Create(FakeTempScanner.Returning(TestScans.Standard()));

        Assert.Equal(ScanState.NotStarted, vm.State);
        Assert.True(vm.ShowIntro);
        Assert.True(vm.ScanCommand.CanExecute(null));
        Assert.False(vm.SelectAllEligibleCommand.CanExecute(null));
        Assert.False(vm.PreviewCommand.CanExecute(null));
    }

    [Fact]
    public async Task AfterScan_ResultsShown_NothingSelected()
    {
        var vm = await ScannedAsync();

        Assert.Equal(ScanState.Completed, vm.State);
        Assert.True(vm.HasResults);
        Assert.Equal(6, vm.AllItems.Count);
        Assert.Equal(0, vm.SelectedCount);
        Assert.All(vm.AllItems, i => Assert.False(i.IsSelected));
        Assert.Equal("4 files", vm.EligibleCountText);
        Assert.Equal("1 file", vm.NeedsReviewCountText);
        Assert.Equal("1 entry", vm.SkippedCountText);
    }

    [Fact]
    public async Task ResultsAreSortedLargestFirst()
    {
        var vm = await ScannedAsync();

        Assert.Equal(["installer.exe", "b.tmp", "a.tmp", "fresh.tmp", "setup.log", "c.dat"], vm.VisibleItems.Select(i => i.Name));
    }

    [Fact]
    public async Task SelectAllSafe_SelectsEligibleOnly_NeverReviewOrSkipped()
    {
        var vm = await ScannedAsync();

        vm.SelectAllEligibleCommand.Execute(null);

        Assert.Equal(4, vm.SelectedCount);
        Assert.Equal(380, vm.SelectedBytes);
        Assert.False(Item(vm, "installer.exe").IsSelected);
        Assert.False(Item(vm, "fresh.tmp").IsSelected);
    }

    [Fact]
    public async Task SkippedEntryCanNeverBeSelected()
    {
        var vm = await ScannedAsync();
        var skipped = Item(vm, "fresh.tmp");

        skipped.IsSelected = true;
        Category(vm, CandidateCategory.TemporaryFiles).IsChecked = true;
        vm.SelectAllEligibleCommand.Execute(null);

        Assert.False(skipped.IsSelected);
        Assert.DoesNotContain(skipped.Id, vm.SelectedIds);
    }

    [Fact]
    public async Task ReviewFileCanBeSelectedByHand()
    {
        var vm = await ScannedAsync();

        Item(vm, "installer.exe").IsSelected = true;

        Assert.Equal(1, vm.SelectedCount);
        Assert.Equal(1000, vm.SelectedBytes);
    }

    [Fact]
    public async Task CategoryCheckbox_SelectsOnlySafeFilesInThatCategory()
    {
        var vm = await ScannedAsync();
        var temporary = Category(vm, CandidateCategory.TemporaryFiles);

        temporary.IsChecked = true;

        Assert.True(Item(vm, "a.tmp").IsSelected);
        Assert.True(Item(vm, "b.tmp").IsSelected);
        Assert.False(Item(vm, "fresh.tmp").IsSelected); // skipped, same category
        Assert.False(Item(vm, "setup.log").IsSelected); // other category
        Assert.Equal(2, vm.SelectedCount);
        Assert.Equal(300, vm.SelectedBytes);
        Assert.True(temporary.IsChecked);
    }

    [Fact]
    public async Task CategoryShowsDash_WhenOnlySomeSafeFilesSelected()
    {
        var vm = await ScannedAsync();
        var temporary = Category(vm, CandidateCategory.TemporaryFiles);

        Item(vm, "a.tmp").IsSelected = true;
        Assert.Null(temporary.IsChecked);

        Item(vm, "b.tmp").IsSelected = true;
        Assert.True(temporary.IsChecked);

        temporary.IsChecked = false;
        Assert.False(temporary.IsChecked);
        Assert.Equal(0, vm.SelectedCount);
    }

    [Fact]
    public async Task CategoryWithOnlyReviewFiles_CannotBeSelectedInBulk()
    {
        var vm = await ScannedAsync();
        var installers = Category(vm, CandidateCategory.InstallerLeftovers);

        Assert.False(installers.CanSelect);
        installers.IsChecked = true;
        Assert.Equal(0, vm.SelectedCount);

        Item(vm, "installer.exe").IsSelected = true;
        Assert.Null(installers.IsChecked);
    }

    [Fact]
    public async Task AllFilesEntry_TracksOverallSelection()
    {
        var vm = await ScannedAsync();
        var all = vm.Categories[0];

        Assert.True(all.IsAll);
        all.IsChecked = true;

        Assert.Equal(4, vm.SelectedCount);
        Assert.True(all.IsChecked);
    }

    [Fact]
    public async Task Preview_DescribesSelectionWithoutDeleting()
    {
        var vm = await ScannedAsync();
        vm.SelectAllEligibleCommand.Execute(null);
        Item(vm, "installer.exe").IsSelected = true;

        vm.PreviewCommand.Execute(null);

        Assert.NotNull(vm.Preview);
        Assert.Equal(5, vm.Preview.FileCount);
        Assert.Equal(1380, vm.Preview.EstimatedBytes);
        Assert.Equal(1, vm.Preview.NeedsReviewCount);
        Assert.StartsWith("Dry Run: nothing was deleted.", vm.PreviewText, StringComparison.Ordinal);
        Assert.Contains("1 selected file needs review", vm.PreviewDetails, StringComparison.Ordinal);
        Assert.Equal(
            ["Temporary files: 2 files", "Logs and diagnostic files: 1 file", "Installer leftovers: 1 file", "Other files: 1 file"],
            vm.PreviewBreakdown);

        vm.DismissPreviewCommand.Execute(null);
        Assert.Null(vm.Preview);
    }

    [Fact]
    public async Task ChangingTheSelection_ClearsAnOldPreview()
    {
        var vm = await ScannedAsync();
        Item(vm, "a.tmp").IsSelected = true;
        vm.PreviewCommand.Execute(null);

        Item(vm, "b.tmp").IsSelected = true;

        Assert.Null(vm.Preview);
    }

    [Fact]
    public async Task PreviewNeedsASelection()
    {
        var vm = await ScannedAsync();

        Assert.False(vm.PreviewCommand.CanExecute(null));
        vm.PreviewCommand.Execute(null);
        Assert.Null(vm.Preview);
    }

    [Fact]
    public async Task NewScan_ClearsOldSelectionAndPreview()
    {
        var vm = Create(new FakeTempScanner((_, _) => Task.FromResult(TestScans.Standard()))); // a fresh session per scan
        await vm.ScanCommand.ExecuteAsync();
        vm.SelectAllEligibleCommand.Execute(null);
        vm.PreviewCommand.Execute(null);
        var oldSession = vm.Session;

        await vm.ScanCommand.ExecuteAsync();

        Assert.NotSame(oldSession, vm.Session);
        Assert.Equal(0, vm.SelectedCount);
        Assert.Empty(vm.SelectedIds);
        Assert.Null(vm.Preview);
    }

    [Fact]
    public async Task SelectedIdsBelongToTheCurrentSession()
    {
        var vm = await ScannedAsync();
        vm.SelectAllEligibleCommand.Execute(null);

        var sessionIds = vm.Session!.Candidates.Select(c => c.Id).ToHashSet();
        Assert.All(vm.SelectedIds, id => Assert.Contains(id, sessionIds));
    }

    [Theory]
    [InlineData(ResultFilter.Eligible, new[] { "b.tmp", "a.tmp", "setup.log", "c.dat" })]
    [InlineData(ResultFilter.NeedsReview, new[] { "installer.exe" })]
    [InlineData(ResultFilter.Skipped, new[] { "fresh.tmp" })]
    public async Task StatusFilterShowsMatchingFiles(ResultFilter filter, string[] expected)
    {
        var vm = await ScannedAsync();

        vm.Filter = filter;

        Assert.Equal(expected, vm.VisibleItems.Select(i => i.Name));
    }

    [Fact]
    public async Task CategoryAndSearchFiltersCombine()
    {
        var vm = await ScannedAsync();

        vm.SelectedCategory = Category(vm, CandidateCategory.TemporaryFiles);
        Assert.Equal(["b.tmp", "a.tmp", "fresh.tmp"], vm.VisibleItems.Select(i => i.Name));

        vm.SearchText = "FRESH";
        Assert.Equal(["fresh.tmp"], vm.VisibleItems.Select(i => i.Name));

        vm.SearchText = "nothing-matches";
        Assert.True(vm.NoVisibleItems);
    }

    [Fact]
    public async Task FilteringDoesNotChangeTheSelection()
    {
        var vm = await ScannedAsync();
        vm.SelectAllEligibleCommand.Execute(null);

        vm.Filter = ResultFilter.Skipped;

        Assert.Equal(4, vm.SelectedCount);
    }

    [Fact]
    public async Task DetailsShowEveryReason()
    {
        var vm = await ScannedAsync();

        vm.FocusedItem = Item(vm, "fresh.tmp");

        Assert.True(vm.HasFocusedItem);
        Assert.Equal(["It was created or changed recently and may still be in use."], vm.FocusedItem.Reasons);
        Assert.Equal(@"C:\Users\Example\AppData\Local\Temp\fresh.tmp", vm.FocusedItem.FullPath);
        Assert.Equal("70 bytes (70 bytes)", vm.FocusedItem.ExactSizeText);
    }

    [Fact]
    public async Task RefusedRoot_ShowsReasons_AndOffersNoSelection()
    {
        var vm = await ScannedAsync(TestScans.Refused());

        Assert.Equal(ScanState.Refused, vm.State);
        Assert.False(vm.HasResults);
        Assert.Contains("The Temp folder isn't on an internal drive.", vm.RefusalReasons);
        Assert.Contains("The Temp folder's drive isn't formatted with NTFS.", vm.RefusalReasons);
        Assert.False(vm.SelectAllEligibleCommand.CanExecute(null));
        Assert.False(vm.PreviewCommand.CanExecute(null));
        Assert.True(vm.ScanCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelling_ReturnsToIdle_WithoutPartialResults()
    {
        var started = new TaskCompletionSource();
        var scanner = new FakeTempScanner(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        });
        var vm = Create(scanner);

        var scan = vm.ScanCommand.ExecuteAsync();
        await started.Task;
        Assert.True(vm.IsScanning);
        Assert.True(vm.CancelScanCommand.CanExecute(null));

        vm.CancelScanCommand.Execute(null);
        await scan;

        Assert.Equal(ScanState.Cancelled, vm.State);
        Assert.False(vm.HasResults);
        Assert.Empty(vm.AllItems);
        Assert.Null(vm.Session);
    }

    [Fact]
    public async Task ScanFailure_IsShownAndLogged_NotThrown()
    {
        var vm = Create(new FakeTempScanner((_, _) => throw new IOException("disk went away")));

        await vm.ScanCommand.ExecuteAsync();

        Assert.Equal(ScanState.Failed, vm.State);
        Assert.False(vm.HasResults);
        Assert.True(vm.ScanCommand.CanExecute(null));
    }

    [Fact]
    public async Task ScanCannotBeStartedTwiceAtOnce()
    {
        var release = new TaskCompletionSource<ScanResult>();
        var scanner = new FakeTempScanner((_, _) => release.Task);
        var vm = Create(scanner);

        var first = vm.ScanCommand.ExecuteAsync();
        Assert.False(vm.ScanCommand.CanExecute(null));
        await vm.ScanCommand.ExecuteAsync(); // ignored while the first is running
        release.SetResult(TestScans.Standard());
        await first;

        Assert.Equal(1, scanner.Calls);
    }

    [Fact]
    public async Task ProgressUpdatesTheStatusWhileScanning()
    {
        var reported = new TaskCompletionSource();
        var release = new TaskCompletionSource<ScanResult>();
        var scanner = new FakeTempScanner((progress, _) =>
        {
            progress!.Report(new ScanProgress(1234, 56));
            return release.Task;
        });
        var vm = Create(scanner);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.StatusText) && vm.StatusText.StartsWith("Examined", StringComparison.Ordinal))
            {
                reported.TrySetResult();
            }
        };

        var scan = vm.ScanCommand.ExecuteAsync();
        await reported.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Contains("1,234 files in 56 folders", vm.StatusText, StringComparison.Ordinal);

        release.SetResult(TestScans.Standard());
        await scan;
    }

    [Fact]
    public async Task EmptyTempFolder_ShowsEmptyState()
    {
        var vm = await ScannedAsync(TestScans.Completed());

        Assert.True(vm.IsEmptyResult);
        Assert.False(vm.HasResults);
    }

    [Fact]
    public async Task DisguisedFileNameIsRevealed()
    {
        var vm = await ScannedAsync(TestScans.Completed(("\u202Egpj.exe", 10, 30)));

        var item = Assert.Single(vm.AllItems);
        Assert.Equal("⟦U+202E⟧gpj.exe", item.Name);
        Assert.Equal(CandidateStatus.NeedsReview, item.Status);
    }
}
