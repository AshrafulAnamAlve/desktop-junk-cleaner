using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using static SafeClean.Infrastructure.Native.NativeMethods;

namespace SafeClean.Infrastructure.Monitoring;

/// <summary>
/// Reads memory, processor, drive and per-program memory figures from Windows. <b>Read-only</b>: it only
/// asks questions, never closes programs, trims memory, changes priorities or touches settings
/// (enforced by <c>ForbiddenApiTests</c>). Anything Windows won't answer is reported as unknown, not guessed.
/// </summary>
public sealed class SystemMonitor : ISystemMonitor
{
    private readonly Lock _cpuLock = new();
    private CpuTimes? _previousCpu;

    public MemorySnapshot? GetMemory()
    {
        var status = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            return null;
        }

        return new MemorySnapshot(ToLong(status.TotalPhys), ToLong(status.AvailPhys));
    }

    public CpuSnapshot GetCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user) || idle < 0 || kernel < 0 || user < 0)
        {
            return new CpuSnapshot(null);
        }

        var current = new CpuTimes((ulong)idle, (ulong)kernel, (ulong)user);
        lock (_cpuLock)
        {
            // The first reading has nothing to compare with, so it reports "measuring" rather than a number.
            var percent = _previousCpu is { } previous ? MonitoringMath.CpuPercent(previous, current) : null;
            _previousCpu = current;
            return new CpuSnapshot(percent);
        }
    }

    public IReadOnlyList<DriveSnapshot> GetDrives()
    {
        var drives = new List<DriveSnapshot>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                {
                    continue;
                }

                drives.Add(new DriveSnapshot(drive.Name, drive.VolumeLabel, drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A drive that can't be read right now is left out rather than shown with made-up figures.
            }
        }

        return drives;
    }

    public IReadOnlyList<ProcessMemoryInfo> GetTopProcesses(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var processes = Process.GetProcesses();
        var results = new List<ProcessMemoryInfo>(processes.Length);
        foreach (var process in processes)
        {
            using (process)
            {
                results.Add(new ProcessMemoryInfo(
                    TryRead(() => process.ProcessName),
                    process.Id,
                    TryRead<long?>(() => process.WorkingSet64),
                    TryRead<long?>(() => process.PrivateMemorySize64)));
            }
        }

        return results
            .OrderByDescending(p => p.WorkingSetBytes ?? -1)
            .ThenBy(p => p.Id)
            .Take(count)
            .ToList();
    }

    /// <summary>A program can exit or refuse access at any moment; that value then becomes "n/a".</summary>
    private static T? TryRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return default;
        }
    }

    private static long ToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;
}
