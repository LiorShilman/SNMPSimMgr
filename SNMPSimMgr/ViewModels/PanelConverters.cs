using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.ViewModels
{
    /// <summary>Converts a MIB field name to a friendly display name.</summary>
    public class FriendlyNameConverter : IValueConverter
    {
        public static readonly FriendlyNameConverter  Instance = new FriendlyNameConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is string name ? MibBrowserViewModel.FriendlyName(name) : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Resolves enum label from a MibFieldSchema's CurrentValue + Options.</summary>
    public class EnumLabelConverter : IValueConverter
    {
        public static readonly EnumLabelConverter  Instance = new EnumLabelConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MibFieldSchema field && field.Options != null &&
                int.TryParse(field.CurrentValue, out var intVal))
            {
                var match = field.Options.FirstOrDefault(o => o.Value == intVal);
                if (match != null) return match.Label;
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Converts between int string "1"/"0" and bool for toggle fields.</summary>
    public class IntToBoolConverter : IValueConverter
    {
        public static readonly IntToBoolConverter  Instance = new IntToBoolConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is string s && (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? "1" : "0";
        }
    }
}
