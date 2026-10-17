using SafeClean.Core.Models;

namespace SafeClean.Core.Interfaces;

/// <summary>
/// Read-only measurements of memory, processor, drives and per-program memory (docs/SECURITY.md §8).
/// Nothing here can change system state: no closing programs, no "freeing" memory, no settings.
/// </summary>
public interface ISystemMonitor
{
    /// <summary>Current memory use, or null if Windows didn't answer.</summary>
    MemorySnapshot? GetMemory();

    /// <summary>Processor use since the previous call. The first call returns "measuring" (null percent).</summary>
    CpuSnapshot GetCpuUsage();

    /// <summary>Fixed drives that are ready; drives that can't be read are left out.</summary>
    IReadOnlyList<DriveSnapshot> GetDrives();

    /// <summary>The <paramref name="count"/> programs using the most memory, largest first.</summary>
    IReadOnlyList<ProcessMemoryInfo> GetTopProcesses(int count);
}
