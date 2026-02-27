using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SNMPSimMgr.Converters;

public class BoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return b ? Visibility.Visible : Visibility.Collapsed;
        if (value is int i) return i != 0 ? Visibility.Visible : Visibility.Collapsed;
        if (value is string s) return !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}
