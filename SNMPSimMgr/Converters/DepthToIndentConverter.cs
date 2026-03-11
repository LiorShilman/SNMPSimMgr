using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SNMPSimMgr.Converters
{
    public class DepthToIndentConverter : IValueConverter
    {
        private const double IndentPerLevel = 18.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int depth = value is int d ? d : 0;
            double indent = depth * IndentPerLevel;

            if (targetType == typeof(double))
                return indent;

            return indent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
