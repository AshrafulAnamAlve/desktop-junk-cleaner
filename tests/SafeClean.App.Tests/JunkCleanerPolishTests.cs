using SafeClean.App.ViewModels;
using SafeClean.Core.Models;

namespace SafeClean.App.Tests;

/// <summary>Phase 9 Junk Cleaner polish: closing details, Esc, folder labels, the report's drive-change line.</summary>
public sealed class JunkCleanerPolishTests
{
    [Fact]
    public async Task Details_CanBeClosed_ToGiveTheListItsRoomBack()
    {
        var vm = Pages.JunkCleaner();
        await vm.ScanCommand.ExecuteAsync();
        vm.FocusedItem = vm.VisibleItems[0];
        Assert.True(vm.CloseDetailsCommand.CanExecute(null));

        vm.CloseDetailsCommand.Execute(null);

        Assert.Null(vm.FocusedItem);
        Assert.False(vm.HasFocusedItem);
    }

    [Fact]
    public async Task Esc_ClosesThePreviewFirst_ThenTheDetails()
    {
        var vm = Pages.JunkCleaner();
        await vm.ScanCommand.ExecuteAsync();
        vm.FocusedItem = vm.VisibleItems[0];
        vm.AllItems.Single(i => i.Name == "a.tmp").IsSelected = true;
        vm.PreviewCommand.Execute(null);

        vm.EscapeCommand.Execute(null);
        Assert.False(vm.HasPreview);
        Assert.True(vm.HasFocusedItem);

        vm.EscapeCommand.Execute(null);
        Assert.False(vm.HasFocusedItem);
    }

    [Theory]
    [InlineData(null, "Temp folder")]
    [InlineData(@"F:\Sandbox", "Sandbox folder")]
    public async Task FilesDirectlyInTheScannedFolder_AreLabelledByMode(string? sandboxRoot, string expected)
    {
        var vm = Pages.JunkCleaner(sandboxRoot: sandboxRoot);
        await vm.ScanCommand.ExecuteAsync();

        Assert.Equal(expected, vm.AllItems.Single(i => i.Name == "a.tmp").Folder);
        Assert.Equal("logs", vm.AllItems.Single(i => i.Name == "setup.log").Folder);
    }

    [Fact]
    public async Task FileRows_AnnounceNameSizeAndStatus_ToScreenReaders()
    {
        // The file row style uses SpokenName as its accessible name, so status is heard, not only the file name.
        var vm = Pages.JunkCleaner();
        await vm.ScanCommand.ExecuteAsync();

        Assert.Equal("a.tmp, 100 bytes, Safe to remove", vm.AllItems.Single(i => i.Name == "a.tmp").SpokenName);
        Assert.StartsWith("All files: 4 safe · 1 review · 1 kept", vm.Categories[0].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(vm.Categories, c => c.ToString()!.Contains("ViewModel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Report_ShowsTheDriveChangeSeparately_FromSpaceFreed()
    {
        var monitor = new FakeSystemMonitor();
        var before = monitor.Drives[0];
        var cleanup = new FakeCleanupService((session, ids, _, _) =>
        {
            // Another program happens to free 1 KB on the drive while SafeClean deletes a 100-byte file.
            monitor.Drives = [before with { FreeBytes = before.FreeBytes + 1124 }];
            return Task.FromResult(FakeCleanupService.Report(session, ids, CleanupOutcome.Deleted));
        });
        var vm = Pages.JunkCleaner(cleanup: cleanup, confirmation: new FakeConfirmation(true), monitor: monitor);
        await vm.ScanCommand.ExecuteAsync();
        vm.AllItems.Single(i => i.Name == "a.tmp").IsSelected = true;
        vm.IsRealMode = true;

        await vm.DeleteCommand.ExecuteAsync();

        Assert.Equal(100, vm.Report!.Report!.BytesFreed);              // "space freed": SafeClean's own deletions only
        Assert.True(vm.Report.HasDriveChange);
        Assert.Contains("+1.1 KB", vm.Report.DriveChangeText, StringComparison.Ordinal); // the drive's own figure, labelled as such
        Assert.Contains("other programs", vm.Report.DriveChangeText, StringComparison.Ordinal);
    }
}
