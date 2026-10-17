using SafeClean.Infrastructure.Monitoring;

namespace SafeClean.Infrastructure.Tests.Monitoring;

/// <summary>
/// Prints this PC's readings so they can be compared with Task Manager. Explicit: runs only when asked.
/// Read-only.
/// </summary>
public sealed class RealReadingsForComparison(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    public async Task PrintReadings()
    {
        var monitor = new SystemMonitor();
        const double GiB = 1L << 30;

        var memory = monitor.GetMemory()!;
        output.WriteLine($"Memory: total {memory.TotalBytes / GiB:F2} GiB, available {memory.AvailableBytes / GiB:F2} GiB, in use {memory.UsedBytes / GiB:F2} GiB ({memory.UsedPercent:F1}%)");

        // Several 1-second samples with timestamps, so they can be lined up with Windows' own counter.
        monitor.GetCpuUsage();
        for (var i = 0; i < 8; i++)
        {
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            output.WriteLine($"CPU sample ending {DateTime.Now:HH:mm:ss}: {monitor.GetCpuUsage().Percent:F1}%");
        }

        foreach (var drive in monitor.GetDrives())
        {
            output.WriteLine($"Drive {drive.Name} ({drive.Format}, \"{drive.Label}\"): total {drive.TotalBytes / GiB:F2} GiB, free {drive.FreeBytes / GiB:F2} GiB ({drive.UsedPercent:F1}% used)");
        }

        foreach (var process in monitor.GetTopProcesses(5))
        {
            output.WriteLine($"Process {process.Name ?? "n/a"} ({process.Id}): working set {process.WorkingSetBytes / (1024.0 * 1024):F1} MiB, private {process.PrivateBytes / (1024.0 * 1024):F1} MiB");
        }
    }
}
