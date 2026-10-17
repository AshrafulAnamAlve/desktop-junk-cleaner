using SafeClean.Core.Models;

namespace SafeClean.Core.Policies;

/// <summary>Arithmetic for the monitoring screens. Pure and defensive: odd inputs give "unknown", never a wrong number.</summary>
public static class MonitoringMath
{
    /// <summary>Total minus free, never below zero (free can briefly exceed total in some reports).</summary>
    public static long UsedBytes(long totalBytes, long freeBytes) =>
        totalBytes <= 0 ? 0 : Math.Clamp(totalBytes - Math.Max(freeBytes, 0), 0, totalBytes);

    /// <summary><paramref name="part"/> as a percentage of <paramref name="total"/>, clamped to 0–100; null if the total is unknown.</summary>
    public static double? PercentOf(long part, long total) =>
        total <= 0 ? null : Math.Clamp(part * 100.0 / total, 0, 100);

    /// <summary>
    /// Processor use between two readings of the cumulative system times. Busy time = (kernel + user) − idle,
    /// because Windows counts idle time inside kernel time. Returns null when no time has passed or a counter
    /// went backwards (for example after the PC resumed from sleep), so the screen shows "measuring" instead.
    /// </summary>
    public static double? CpuPercent(CpuTimes previous, CpuTimes current)
    {
        if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User)
        {
            return null;
        }

        var idle = (double)(current.Idle - previous.Idle);
        var total = (double)(current.Kernel - previous.Kernel) + (current.User - previous.User);
        if (total <= 0)
        {
            return null;
        }

        return Math.Clamp((total - idle) * 100.0 / total, 0, 100);
    }
}
