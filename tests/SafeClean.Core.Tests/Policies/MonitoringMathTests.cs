using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.Core.Tests.Policies;

/// <summary>Monitoring arithmetic with synthetic numbers. Odd input must give "unknown", never a wrong figure.</summary>
public sealed class MonitoringMathTests
{
    private const long GiB = 1L << 30;

    [Fact]
    public void CpuPercent_IsBusyTimeOverTotalTime_KernelIncludesIdle()
    {
        // Idle +50, kernel +100 (of which 50 idle), user +50 → total 150, busy 100.
        var percent = MonitoringMath.CpuPercent(new CpuTimes(100, 200, 100), new CpuTimes(150, 300, 150));

        Assert.NotNull(percent);
        Assert.Equal(66.667, percent.Value, precision: 3);
    }

    [Fact]
    public void CpuPercent_FullyIdle_IsZero_FullyBusy_IsHundred()
    {
        Assert.Equal(0, MonitoringMath.CpuPercent(new CpuTimes(0, 0, 0), new CpuTimes(100, 100, 0)));
        Assert.Equal(100, MonitoringMath.CpuPercent(new CpuTimes(0, 0, 0), new CpuTimes(0, 60, 40)));
    }

    [Fact]
    public void CpuPercent_NoTimePassed_IsUnknown()
    {
        var same = new CpuTimes(10, 20, 30);

        Assert.Null(MonitoringMath.CpuPercent(same, same));
    }

    [Theory]
    [InlineData(150UL, 300UL, 150UL, 100UL, 300UL, 150UL)] // idle went backwards
    [InlineData(100UL, 300UL, 150UL, 100UL, 200UL, 150UL)] // kernel went backwards
    [InlineData(100UL, 300UL, 150UL, 100UL, 300UL, 100UL)] // user went backwards
    public void CpuPercent_CounterGoingBackwards_IsUnknown(ulong pi, ulong pk, ulong pu, ulong ci, ulong ck, ulong cu) =>
        Assert.Null(MonitoringMath.CpuPercent(new CpuTimes(pi, pk, pu), new CpuTimes(ci, ck, cu)));

    [Fact]
    public void CpuPercent_ImpossibleIdleAboveTotal_IsClampedToZero()
    {
        // A bogus reading where idle grew more than kernel + user must not give a negative percentage.
        Assert.Equal(0, MonitoringMath.CpuPercent(new CpuTimes(0, 0, 0), new CpuTimes(500, 100, 100)));
    }

    [Fact]
    public void CpuPercent_HugeCounters_DoNotOverflow()
    {
        var start = new CpuTimes(ulong.MaxValue - 1000, ulong.MaxValue - 2000, ulong.MaxValue - 3000);
        var end = new CpuTimes(ulong.MaxValue - 900, ulong.MaxValue - 1800, ulong.MaxValue - 2900);

        Assert.Equal(66.667, MonitoringMath.CpuPercent(start, end)!.Value, precision: 3);
    }

    [Fact]
    public void Memory_UsedIsTotalMinusAvailable()
    {
        var memory = new MemorySnapshot(16 * GiB, 4 * GiB);

        Assert.Equal(12 * GiB, memory.UsedBytes);
        Assert.Equal(75, memory.UsedPercent);
    }

    [Fact]
    public void Memory_UnknownTotal_GivesUnknownPercent()
    {
        var memory = new MemorySnapshot(0, 0);

        Assert.Equal(0, memory.UsedBytes);
        Assert.Null(memory.UsedPercent);
    }

    [Fact]
    public void Memory_AvailableAboveTotal_IsClamped_NotNegative()
    {
        var memory = new MemorySnapshot(8 * GiB, 9 * GiB);

        Assert.Equal(0, memory.UsedBytes);
        Assert.Equal(0, memory.UsedPercent);
    }

    [Fact]
    public void Drive_UsedAndPercent_FromCapacityAndFreeSpace()
    {
        var drive = new DriveSnapshot(@"C:\", "System", "NTFS", 500 * GiB, 125 * GiB);

        Assert.Equal(375 * GiB, drive.UsedBytes);
        Assert.Equal(75, drive.UsedPercent);
    }

    [Theory]
    [InlineData(1, 3, 33.333)]
    [InlineData(2, 3, 66.667)]
    [InlineData(-5, 10, 0)]
    [InlineData(15, 10, 100)]
    public void PercentOf_IsClamped(long part, long total, double expected) =>
        Assert.Equal(expected, MonitoringMath.PercentOf(part, total)!.Value, precision: 3);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PercentOf_UnknownTotal_IsNull(long total) =>
        Assert.Null(MonitoringMath.PercentOf(5, total));

    [Fact]
    public void CpuSnapshot_WithoutPercent_IsMeasuring()
    {
        Assert.True(new CpuSnapshot(null).IsMeasuring);
        Assert.False(new CpuSnapshot(12.5).IsMeasuring);
    }
}
