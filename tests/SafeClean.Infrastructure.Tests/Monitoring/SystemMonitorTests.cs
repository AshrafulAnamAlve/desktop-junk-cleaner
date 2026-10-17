using SafeClean.Infrastructure.Monitoring;

namespace SafeClean.Infrastructure.Tests.Monitoring;

/// <summary>Real, read-only readings from this PC. Nothing is changed.</summary>
public sealed class SystemMonitorTests
{
    private readonly SystemMonitor _monitor = new();

    [Fact]
    public void Memory_IsPlausible_AndMatchesWhatDotNetSees()
    {
        var memory = _monitor.GetMemory();

        Assert.NotNull(memory);
        Assert.True(memory.TotalBytes > 0);
        Assert.InRange(memory.AvailableBytes, 0, memory.TotalBytes);
        Assert.InRange(memory.UsedPercent!.Value, 0, 100);

        // Independent source: the .NET runtime's view of physical memory (within 2%).
        var dotNetTotal = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        Assert.InRange(memory.TotalBytes, dotNetTotal * 0.98, dotNetTotal * 1.02);
    }

    [Fact]
    public async Task Cpu_FirstReadingIsMeasuring_ThenAPercentage()
    {
        var first = _monitor.GetCpuUsage();
        await Task.Delay(500, TestContext.Current.CancellationToken);
        var second = _monitor.GetCpuUsage();

        Assert.True(first.IsMeasuring); // never a made-up first number
        Assert.NotNull(second.Percent);
        Assert.InRange(second.Percent.Value, 0, 100);
    }

    [Fact]
    public void Drives_IncludeTheSystemDrive_OnlyFixedOnes_WithSaneFigures()
    {
        var drives = _monitor.GetDrives();
        var systemDrive = Path.GetPathRoot(System.Environment.SystemDirectory)!;

        Assert.Contains(drives, d => string.Equals(d.Name, systemDrive, StringComparison.OrdinalIgnoreCase));
        Assert.All(drives, d =>
        {
            Assert.Equal(DriveType.Fixed, new DriveInfo(d.Name).DriveType);
            Assert.True(d.TotalBytes > 0);
            Assert.InRange(d.FreeBytes, 0, d.TotalBytes);
            Assert.False(string.IsNullOrEmpty(d.Format));
        });
    }

    [Fact]
    public void TopProcesses_AreSortedLargestFirst_AndLimited()
    {
        var top = _monitor.GetTopProcesses(10);

        Assert.InRange(top.Count, 1, 10);
        var sizes = top.Select(p => p.WorkingSetBytes ?? -1).ToList();
        Assert.Equal(sizes.OrderByDescending(s => s), sizes);
    }

    [Fact]
    public void AllProcesses_ListedWithoutErrors_IncludingThisOne()
    {
        // Some system processes refuse access; they must appear with "n/a" values, never break the list.
        var all = _monitor.GetTopProcesses(int.MaxValue);
        var self = Assert.Single(all, p => p.Id == System.Environment.ProcessId);

        Assert.True(all.Count > 10);
        Assert.True(self.WorkingSetBytes > 0);
        Assert.False(string.IsNullOrEmpty(self.Name));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void TopProcesses_ZeroOrNegativeCount_IsEmpty(int count) =>
        Assert.Empty(_monitor.GetTopProcesses(count));
}
