using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SafeClean.App.Converters;

/// <summary>
/// Shows an element only when a size (for example the page's height) is at least the given parameter.
/// Used to drop optional rows on small screens so the important list keeps its room.
/// </summary>
public sealed class AtLeastToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        IsAtLeast(value, parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static bool IsAtLeast(object value, object parameter) =>
        value is double size
        && double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)
        && size >= threshold;
}
