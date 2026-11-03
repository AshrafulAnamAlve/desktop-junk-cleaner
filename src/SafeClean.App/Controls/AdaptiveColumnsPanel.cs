using System.Windows;
using System.Windows.Controls;

namespace SafeClean.App.Controls;

/// <summary>
/// Lays cards out in equal columns that always fill the full width, so they line up with full-width cards
/// below them. Uses as many columns as fit (each at least <see cref="MinColumnWidth"/>), up to
/// <see cref="MaxColumns"/>; on narrow windows the cards wrap onto more rows.
/// </summary>
public sealed class AdaptiveColumnsPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(AdaptiveColumnsPanel),
        new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptiveColumnsPanel),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptiveColumnsPanel),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>How many columns fit: at least 1, at most <paramref name="maxColumns"/>.</summary>
    public static int ColumnsFor(double width, double minColumnWidth, double spacing, int maxColumns)
    {
        if (double.IsInfinity(width) || double.IsNaN(width) || minColumnWidth <= 0)
        {
            return Math.Max(1, maxColumns);
        }

        var fit = (int)Math.Floor((width + spacing) / (minColumnWidth + spacing));
        return Math.Clamp(fit, 1, Math.Max(1, maxColumns));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        var columns = ColumnsFor(availableSize.Width, MinColumnWidth, Spacing, Math.Min(MaxColumns, Math.Max(1, children.Count)));
        var columnWidth = double.IsInfinity(availableSize.Width)
            ? MinColumnWidth
            : Math.Max(0, (availableSize.Width - ((columns - 1) * Spacing)) / columns);

        double height = 0;
        foreach (var row in children.Chunk(columns))
        {
            double rowHeight = 0;
            foreach (var child in row)
            {
                child.Measure(new Size(columnWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }

            height += rowHeight + (height > 0 ? Spacing : 0);
        }

        var width = double.IsInfinity(availableSize.Width) ? (columns * columnWidth) + ((columns - 1) * Spacing) : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        var columns = ColumnsFor(finalSize.Width, MinColumnWidth, Spacing, Math.Min(MaxColumns, Math.Max(1, children.Count)));
        var columnWidth = Math.Max(0, (finalSize.Width - ((columns - 1) * Spacing)) / columns);

        double y = 0;
        foreach (var row in children.Chunk(columns))
        {
            var rowHeight = row.Max(c => c.DesiredSize.Height);
            for (var i = 0; i < row.Length; i++)
            {
                row[i].Arrange(new Rect(i * (columnWidth + Spacing), y, columnWidth, rowHeight));
            }

            y += rowHeight + Spacing;
        }

        return finalSize;
    }
}
