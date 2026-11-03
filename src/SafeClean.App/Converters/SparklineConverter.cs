using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SafeClean.App.Converters;

/// <summary>
/// Turns percentage readings (0–100, oldest first) into points for a small line chart drawn in a fixed
/// <see cref="Width"/> × <see cref="Height"/> box (the view scales the box). No chart library needed.
/// </summary>
public sealed class SparklineConverter : IValueConverter
{
    public const double Width = 300;
    public const double Height = 60;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new PointCollection(ToPoints(value as IReadOnlyList<double> ?? [], 60, Width, Height));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>
    /// The newest reading sits at the right edge; older ones step left, one slot per reading, so the line
    /// grows from the right as readings arrive. 100% is the top of the box, 0% the bottom.
    /// </summary>
    public static IReadOnlyList<Point> ToPoints(IReadOnlyList<double> values, int capacity, double width, double height)
    {
        if (values.Count == 0 || capacity < 2)
        {
            return [];
        }

        var step = width / (capacity - 1);
        var shown = values.Skip(Math.Max(0, values.Count - capacity)).ToList();
        var firstX = width - ((shown.Count - 1) * step);
        return shown
            .Select((v, i) => new Point(firstX + (i * step), height - (Math.Clamp(v, 0, 100) / 100 * height)))
            .ToList();
    }
}
