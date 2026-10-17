using SafeClean.Core.Policies;

namespace SafeClean.Core.Models;

/// <summary>Physical memory (RAM) as Windows reports it. High use is normal: Windows uses spare RAM as a cache.</summary>
/// <param name="TotalBytes">Installed memory usable by Windows.</param>
/// <param name="AvailableBytes">Memory that programs can get immediately (includes the reusable cache).</param>
public sealed record MemorySnapshot(long TotalBytes, long AvailableBytes)
{
    public long UsedBytes => MonitoringMath.UsedBytes(TotalBytes, AvailableBytes);

    /// <summary>0–100, or null if the total is unknown.</summary>
    public double? UsedPercent => MonitoringMath.PercentOf(UsedBytes, TotalBytes);
}

/// <summary>Processor use across all cores since the previous reading.</summary>
/// <param name="Percent">0–100, or null while the first measurement is still being taken (never a made-up number).</param>
public sealed record CpuSnapshot(double? Percent)
{
    public bool IsMeasuring => Percent is null;
}

/// <summary>One fixed, ready drive.</summary>
/// <param name="Name">For example <c>C:\</c>.</param>
/// <param name="Label">Volume label (may be empty).</param>
/// <param name="Format">File system, for example NTFS.</param>
/// <param name="TotalBytes">Capacity.</param>
/// <param name="FreeBytes">Space this user can still use.</param>
public sealed record DriveSnapshot(string Name, string Label, string Format, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => MonitoringMath.UsedBytes(TotalBytes, FreeBytes);

    /// <summary>0–100, or null if the capacity is unknown.</summary>
    public double? UsedPercent => MonitoringMath.PercentOf(UsedBytes, TotalBytes);
}

/// <summary>How much memory one running program reports. Values Windows won't give are null ("n/a"), never guessed.</summary>
/// <param name="Name">Program name, or null if it couldn't be read.</param>
/// <param name="Id">Process ID.</param>
/// <param name="WorkingSetBytes">Memory currently in RAM for this program.</param>
/// <param name="PrivateBytes">Memory reserved for this program alone.</param>
public sealed record ProcessMemoryInfo(string? Name, int Id, long? WorkingSetBytes, long? PrivateBytes);

/// <summary>Cumulative processor times from Windows, in 100-nanosecond units. Kernel time includes idle time.</summary>
public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);
