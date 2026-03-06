using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SNMPSimMgr.Converters
{
    public class BoolToVisConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool result;
            if (value is bool b) result = b;
            else if (value is int i) result = i != 0;
            else if (value is string s) result = !string.IsNullOrEmpty(s);
            else result = value != null;

            if (parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase))
                result = !result;

            return result ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility.Visible;
    }
}
