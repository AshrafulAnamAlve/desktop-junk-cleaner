using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SafeClean.App.Converters;

/// <summary>Hides an element when the bound value is null or an empty string (for example "no problem to report").</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || (value is string text && text.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
