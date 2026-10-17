using SafeClean.App.ViewModels;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.App.Tests;

/// <summary>
/// Phase 7: the steps in front of a real cleanup, and the report after it. Everything uses fakes — the
/// cleanup service here only records calls, so these tests can't delete anything.
/// </summary>
public sealed class JunkCleanerCleanupTests
{
    private static async Task<JunkCleanerViewModel> ScannedAsync(
        FakeCleanupService? cleanup = null,
        FakeConfirmation? confirmation = null,
        bool elevated = false,
        TimeProvider? clock = null,
        string? sandboxRoot = null)
    {
        var vm = Pages.JunkCleaner(cleanup: cleanup, confirmation: confirmation, elevated: elevated, clock: clock, sandboxRoot: sandboxRoot);
        await vm.ScanCommand.ExecuteAsync();
        return vm;
    }

    private static CandidateItemViewModel Item(JunkCleanerViewModel vm, string name) => vm.AllItems.Single(i => i.Name == name);

    private static void Select(JunkCleanerViewModel vm, params string[] names)
    {
        foreach (var name in names)
        {
            Item(vm, name).IsSelected = true;
        }
    }

    [Fact]
    public async Task RealModeIsOffAtStart_SoDeletingIsImpossible()
    {
        var cleanup = new FakeCleanupService();
        var confirmation = new FakeConfirmation(true);
        var vm = await ScannedAsync(cleanup, confirmation);
        Select(vm, "a.tmp");

        Assert.False(vm.IsRealMode);
        Assert.False(vm.DeleteCommand.CanExecute(null));
        await vm.DeleteCommand.ExecuteAsync();

        Assert.Empty(confirmation.Requests);
        Assert.Empty(cleanup.Calls);
    }

    [Fact]
    public void ANewWindow_StartsInDryRunAgain()
    {
        // Real mode is never saved: every new start (a new view model) is back in Dry Run.
        var first = Pages.JunkCleaner();
        first.IsRealMode = true;

        Assert.True(first.IsRealMode);
        Assert.False(Pages.JunkCleaner().IsRealMode);
    }

    [Fact]
    public async Task RealModeWithASelection_EnablesTheDeleteButton()
    {
        var vm = await ScannedAsync();
        Assert.StartsWith("Off: preview only", vm.RealModeHint, StringComparison.Ordinal);

        vm.IsRealMode = true;
        Assert.Contains("permanently deleted", vm.RealModeHint, StringComparison.Ordinal);
        Assert.Contains("Recycle Bin", vm.RealModeHint, StringComparison.Ordinal);
        Assert.False(vm.DeleteCommand.CanExecute(null)); // nothing selected yet
        Assert.Equal("Delete selected files…", vm.DeleteButtonText);

        Select(vm, "a.tmp", "b.tmp");
        Assert.True(vm.DeleteCommand.CanExecute(null));
        Assert.Equal("Delete 2 files…", vm.DeleteButtonText);
        Assert.Equal("Real mode · deletes files", vm.ModeBadgeText);
    }

    [Fact]
    public async Task WhenElevated_RealModeCannotBeSwitchedOn()
    {
        var cleanup = new FakeCleanupService();
        var vm = await ScannedAsync(cleanup, new FakeConfirmation(true), elevated: true);
        Select(vm, "a.tmp");

        vm.IsRealMode = true;

        Assert.False(vm.IsRealMode);
        Assert.False(vm.CanUseRealMode);
        Assert.Contains("administrator", vm.RealModeHint, StringComparison.Ordinal);
        Assert.False(vm.DeleteCommand.CanExecute(null));
        await vm.DeleteCommand.ExecuteAsync();
        Assert.Empty(cleanup.Calls);
    }

    [Fact]
    public async Task CancellingTheDialog_NeverCallsTheCleanupService()
    {
        var cleanup = new FakeCleanupService();
        var confirmation = new FakeConfirmation(false);
        var vm = await ScannedAsync(cleanup, confirmation);
        Select(vm, "a.tmp");
        vm.IsRealMode = true;

        await vm.DeleteCommand.ExecuteAsync();

        Assert.Single(confirmation.Requests);
        Assert.Empty(cleanup.Calls);
        Assert.Equal(ScanState.Completed, vm.State);
        Assert.Equal(1, vm.SelectedCount); // nothing changed
        Assert.Contains("Nothing was deleted", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Confirming_CallsTheServiceOnce_WithExactlyTheSelectedIds_InRealMode()
    {
        var cleanup = new FakeCleanupService();
        var vm = await ScannedAsync(cleanup, new FakeConfirmation(true));
        var session = vm.Session;
        Select(vm, "a.tmp", "installer.exe"); // a review file ticked by hand is included; nothing else is
        var expected = new[] { Item(vm, "a.tmp").Id, Item(vm, "installer.exe").Id };
        vm.IsRealMode = true;

        await vm.DeleteCommand.ExecuteAsync();

        var call = Assert.Single(cleanup.Calls);
        Assert.Same(session, call.Session);
        Assert.Equal(CleanupMode.Real, call.Mode);
        Assert.Equal(expected.Select(id => id.Value).Order(), call.Ids.Select(id => id.Value).Order());
    }

    [Fact]
    public async Task TheDialog_ShowsCountSizeCategoriesAndReviewWarning()
    {
        var confirmation = new FakeConfirmation(false);
        var vm = await ScannedAsync(confirmation: confirmation);
        Select(vm, "a.tmp", "installer.exe");
        vm.IsRealMode = true;

        await vm.DeleteCommand.ExecuteAsync();

        var request = Assert.Single(confirmation.Requests);
        Assert.Equal(2, request.FileCount);
        Assert.Equal(1100, request.EstimatedBytes);
        Assert.Equal("Permanently delete 2 files?", request.Title);
        Assert.Equal("Delete 2 files", request.DeleteButtonText);
        Assert.True(request.HasReviewWarning);
        Assert.Equal(["Temporary files: 1 file", "Installer leftovers: 1 file"], request.Breakdown);
        Assert.Contains("not moved to the Recycle Bin", request.PermanentText, StringComparison.Ordinal);
        Assert.False(request.IsSandbox);
    }

    [Fact]
    public async Task InSandboxMode_TheDialogNamesTheSandboxFolder()
    {
        var confirmation = new FakeConfirmation(false);
        var vm = await ScannedAsync(confirmation: confirmation, sandboxRoot: @"F:\Sandbox");
        Select(vm, "a.tmp");
        vm.IsRealMode = true;

        await vm.DeleteCommand.ExecuteAsync();

        var request = Assert.Single(confirmation.Requests);
        Assert.True(request.IsSandbox);
        Assert.Contains(@"F:\Sandbox", request.SandboxText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResultsOlderThanThirtyMinutes_AreNotCleaned_AndNoDialogOpens()
    {
        var cleanup = new FakeCleanupService();
        var confirmation = new FakeConfirmation(true);
        var tooLate = new FixedClock(TestScans.Now + SafetyDefaults.MaxScanSessionAge + TimeSpan.FromMinutes(1));
        var vm = await ScannedAsync(cleanup, confirmation, clock: tooLate);
        Select(vm, "a.tmp");
        vm.IsRealMode = true;

        await vm.DeleteCommand.ExecuteAsync();

        Assert.Empty(confirmation.Requests);
        Assert.Empty(cleanup.Calls);
        Assert.Contains("30 minutes", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReport_ShowsExactlyWhatTheServiceReturned()
    {
        var cleanup = new FakeCleanupService((session, ids, _, _) =>
        {
            var list = ids.ToList();
            var entries = new List<CleanupEntry>
            {
                Entry(session, list[0], CleanupOutcome.Deleted),
                Entry(session, list[1], CleanupOutcome.SkippedInUse),
                Entry(session, list[2], CleanupOutcome.SkippedInUse),
                Entry(session, list[3], CleanupOutcome.Failed),
            };
            return Task.FromResult(CleanupReport.Completed(CleanupMode.Real, TestScans.Now, TestScans.Now, entries, false, false));
        });
        var vm = await ScannedAsync(cleanup, new FakeConfirmation(true));
        Select(vm, "b.tmp", "a.tmp", "setup.log", "c.dat");
        vm.IsRealMode = true;

        await vm.DeleteCommand.ExecuteAsync();

        var report = vm.Report!;
        Assert.True(vm.HasReport);
        Assert.Equal("Deleted 1 file", report.Headline);
        Assert.Equal("1 file deleted", report.DeletedText);
        Assert.Equal("2 files skipped", report.SkippedText);
        Assert.Equal("1 file failed", report.FailedText);
        Assert.Equal([$"2 files — {PolicyText.Describe(CleanupOutcome.SkippedInUse)}"], report.SkippedGroups);
        Assert.Single(report.FailedLines);
        Assert.Contains("Windows error 5", report.FailedLines[0], StringComparison.Ordinal);
        Assert.Equal(cleanup.Calls[0].Session.Candidates.Single(c => c.Id == cleanup.Calls[0].Ids[0]).Size, report.Report!.BytesFreed);
    }

    private static CleanupEntry Entry(ScanSession session, CandidateId id, CleanupOutcome outcome)
    {
        var candidate = session.Candidates.Single(c => c.Id == id);
        return new CleanupEntry(id, candidate.Path, candidate.Decision.RelativePath, outcome,
            outcome == CleanupOutcome.Deleted ? candidate.Size : 0, outcome == CleanupOutcome.Failed ? 5 : 0);
    }

    [Fact]
    public async Task SpaceFreed_ComesOnlyFromTheReport_NotFromTheSelection()
    {
        // Two files selected (300 bytes), but the service deleted only one (100 bytes).
        var cleanup = new FakeCleanupService((session, ids, _, _) =>
        {
            var list = ids.ToList();
            return Task.FromResult(CleanupReport.Completed(CleanupMode.Real, TestScans.Now, TestScans.Now,
                [Entry(session, list[0], CleanupOutcome.Deleted), Entry(session, list[1], CleanupOutcome.SkippedChanged)], false, false));
        });
        var vm = await ScannedAsync(cleanup, new FakeConfirmation(true));
        Select(vm, "a.tmp", "b.tmp");
        vm.IsRealMode = true;

        await vm.DeleteCommand.ExecuteAsync();

        var freed = vm.Report!.Report!.BytesFreed;
        Assert.True(freed is 100 or 200);
        Assert.Contains(freed == 100 ? "100 bytes" : "200 bytes", vm.Report.SpaceFreedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AfterCleanup_ResultsAreClosed_RealModeIsOff_AndANewScanIsNeeded()
    {
        var vm = await ScannedAsync(new FakeCleanupService(), new FakeConfirmation(true));
        Select(vm, "a.tmp");
        vm.IsRealMode = true;

        await vm.DeleteCommand.ExecuteAsync();

        Assert.Equal(ScanState.CleanedUp, vm.State);
        Assert.Null(vm.Session);
        Assert.False(vm.HasResults);
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.IsRealMode);
        Assert.False(vm.DeleteCommand.CanExecute(null));
        Assert.True(vm.ScanCommand.CanExecute(null));

        await vm.ScanCommand.ExecuteAsync(); // scanning again clears the report
        Assert.Null(vm.Report);
        Assert.Equal(ScanState.Completed, vm.State);
    }

    [Fact]
    public async Task ARefusedCleanup_SaysNothingWasDeleted_AndWhy()
    {
        var cleanup = new FakeCleanupService((_, _, _, _) =>
            Task.FromResult(CleanupReport.Refused(CleanupMode.Real, TestScans.Now, CleanupRefusal.RootChanged)));
        var vm = await ScannedAsync(cleanup, new FakeConfirmation(true));
        Select(vm, "a.tmp");
        vm.IsRealMode = true;

        await vm.DeleteCommand.ExecuteAsync();

        Assert.Equal("Nothing was deleted", vm.Report!.Headline);
        Assert.Contains(PolicyText.Describe(CleanupRefusal.RootChanged), vm.Report.Notes);
        Assert.Equal(string.Empty, vm.Report.SpaceFreedText);
    }

    [Fact]
    public async Task IfTheServiceFails_TheReportDoesNotClaimAnything()
    {
        var cleanup = new FakeCleanupService((_, _, _, _) => throw new InvalidOperationException("boom"));
        var vm = await ScannedAsync(cleanup, new FakeConfirmation(true));
        Select(vm, "a.tmp");
        vm.IsRealMode = true;

        await vm.DeleteCommand.ExecuteAsync();

        Assert.Equal(ScanState.CleanedUp, vm.State);
        Assert.Null(vm.Report!.Report);
        Assert.Contains("unexpected error", vm.Report.Headline, StringComparison.Ordinal);
        Assert.Contains(vm.Report.Notes, n => n.Contains("may already have been deleted", StringComparison.Ordinal));
        Assert.False(vm.IsRealMode);
    }

    [Fact]
    public async Task WhileCleaning_ScanAndRealModeAreLocked_AndCancelReachesTheService()
    {
        var release = new TaskCompletionSource();
        CancellationToken seen = default;
        var cleanup = new FakeCleanupService(async (session, ids, _, token) =>
        {
            seen = token;
            await release.Task;
            return FakeCleanupService.Report(session, ids, CleanupOutcome.SkippedCancelled, cancelled: token.IsCancellationRequested);
        });
        var vm = await ScannedAsync(cleanup, new FakeConfirmation(true));
        Select(vm, "a.tmp");
        vm.IsRealMode = true;

        var running = vm.DeleteCommand.ExecuteAsync();

        Assert.True(vm.IsCleaning);
        Assert.False(vm.ScanCommand.CanExecute(null));
        Assert.False(vm.CanUseRealMode);
        Assert.False(vm.DeleteCommand.CanExecute(null));
        Assert.True(vm.CancelCleanupCommand.CanExecute(null));

        vm.CancelCleanupCommand.Execute(null);
        Assert.True(seen.IsCancellationRequested);
        release.SetResult();
        await running;

        Assert.False(vm.IsCleaning);
        Assert.Contains(vm.Report!.Notes, n => n.Contains("cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SwitchingRealModeOn_ClosesAnOpenPreview()
    {
        var vm = await ScannedAsync();
        Select(vm, "a.tmp");
        vm.PreviewCommand.Execute(null);
        Assert.True(vm.HasPreview);

        vm.IsRealMode = true;

        Assert.False(vm.HasPreview);
    }

    [Fact]
    public void TheMainWindowBadge_FollowsTheSwitch()
    {
        var junk = Pages.JunkCleaner();
        var main = new MainViewModel(
            new DashboardViewModel(new FakeElevation(false)),
            junk,
            new PerformanceViewModel(),
            new HistoryViewModel(),
            new SettingsViewModel(new AppInfo("test", "logs")),
            new FakeElevation(false),
            new AppInfo("test", "logs"));
        var changed = new List<string?>();
        main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal(CleanupMode.DryRun, main.Mode);
        junk.IsRealMode = true;
        Assert.Equal(CleanupMode.Real, main.Mode);
        Assert.Contains(nameof(MainViewModel.IsRealMode), changed);
        junk.IsRealMode = false;
        Assert.False(main.IsRealMode);
        Assert.False(main.IsSandbox);
    }
}
