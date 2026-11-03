using SafeClean.App.Controls;
using SafeClean.App.Converters;
using SafeClean.App.Presentation;
using SafeClean.App.ViewModels;

namespace SafeClean.App.Tests;

/// <summary>Phase 9 helpers: percentages, line-chart points, card columns, window fitting, report wording.</summary>
public sealed class LayoutAndFormatTests
{
    [Theory]
    [InlineData(63.4, "63%")]
    [InlineData(99.5, "100%")]
    [InlineData(0.4, "0%")]
    [InlineData(-5.0, "0%")]
    [InlineData(150.0, "100%")]
    public void Percent_IsRoundedAndClamped(double value, string expected) =>
        Assert.Equal(expected, DisplayText.FormatPercent(value, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void UnknownPercent_IsADash_NotZero() =>
        Assert.Equal("—", DisplayText.FormatPercent(null));

    [Fact]
    public void Sparkline_NewestReadingAtTheRightEdge_HundredPercentAtTheTop()
    {
        var points = SparklineConverter.ToPoints([0, 50, 100], capacity: 5, width: 400, height: 100);

        Assert.Equal(3, points.Count);
        Assert.Equal(400, points[^1].X);   // newest at the right
        Assert.Equal(0, points[^1].Y);     // 100% at the top
        Assert.Equal(200, points[0].X);    // older readings step left, one slot each
        Assert.Equal(100, points[0].Y);    // 0% at the bottom
        Assert.Equal(50, points[1].Y);
    }

    [Fact]
    public void Sparkline_ShowsOnlyTheNewestReadings_AndNothingWithoutData()
    {
        Assert.Empty(SparklineConverter.ToPoints([], 60, 300, 60));

        var points = SparklineConverter.ToPoints(Enumerable.Range(0, 100).Select(i => (double)i).ToList(), 60, 300, 60);
        Assert.Equal(60, points.Count);
        Assert.Equal(0, points[0].X, precision: 6);
    }

    [Theory]
    [InlineData(900, 4)]  // default width: all four cards in a row
    [InlineData(600, 2)]  // narrower: two per row
    [InlineData(300, 1)]  // very narrow: one per row
    [InlineData(5000, 4)] // never more than the maximum
    public void Cards_UseAsManyColumnsAsFit(double width, int expected) =>
        Assert.Equal(expected, AdaptiveColumnsPanel.ColumnsFor(width, minColumnWidth: 190, spacing: 16, maxColumns: 4));

    [Fact]
    public void Cards_WithUnlimitedWidth_UseTheMaximum() =>
        Assert.Equal(4, AdaptiveColumnsPanel.ColumnsFor(double.PositiveInfinity, 190, 16, 4));

    [Theory]
    [InlineData(760, 600, 738, 738, 600)]   // this PC (1366×768): taller than the usable area → shrink
    [InlineData(760, 600, 1040, 760, 600)]  // a bigger screen: keep the preferred size
    [InlineData(760, 600, 512, 512, 512)]   // 150% on 1366×768: even the minimum must shrink
    [InlineData(760, 600, 0, 760, 600)]     // unknown screen: leave as is
    public void Window_FitsTheUsableScreenArea(double preferred, double minimum, double available, double expectedSize, double expectedMinimum)
    {
        var (size, min) = WindowFit.Fit(preferred, minimum, available);

        Assert.Equal(expectedSize, size);
        Assert.Equal(expectedMinimum, min);
    }

    [Theory]
    [InlineData(600.0, "560", true)]
    [InlineData(560.0, "560", true)]
    [InlineData(559.9, "560", false)]
    [InlineData("not a size", "560", false)]
    public void OptionalRows_ShowOnlyWhenThereIsRoom(object height, string threshold, bool expected) =>
        Assert.Equal(expected, AtLeastToVisibilityConverter.IsAtLeast(height, threshold));

    [Fact]
    public void DriveChange_IsWordedAsSeparateFromSpaceFreed()
    {
        var text = CleanupReportViewModel.DescribeDriveChange(@"C:\", 1000, 1100);

        Assert.StartsWith("Drive C: free space change: +100 bytes", text, StringComparison.Ordinal);
        Assert.Contains("may include other programs' activity", text, StringComparison.Ordinal);
        Assert.Contains("−", CleanupReportViewModel.DescribeDriveChange(@"C:\", 2048, 1024), StringComparison.Ordinal);
        Assert.Contains("no change", CleanupReportViewModel.DescribeDriveChange(@"C:\", 5, 5), StringComparison.Ordinal);
        Assert.Equal(string.Empty, CleanupReportViewModel.DescribeDriveChange(@"C:\", null, 5));
    }
}
