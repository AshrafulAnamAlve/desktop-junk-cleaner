using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SafeClean.App.Converters;

/// <summary>Shows an element when the value is false (the opposite of WPF's BooleanToVisibilityConverter).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
