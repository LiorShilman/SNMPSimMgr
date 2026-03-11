using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SNMPSimMgr.Converters
{
    public class TypeToColorConverter : IValueConverter
    {
        // Access-type colors
        private static readonly SolidColorBrush ReadWrite = new SolidColorBrush(Color.FromRgb(0x36, 0xB3, 0x7E));     // green
        private static readonly SolidColorBrush ReadOnly = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));      // blue
        private static readonly SolidColorBrush NotAccessible = new SolidColorBrush(Color.FromRgb(0x6B, 0x73, 0x94)); // dim gray
        private static readonly SolidColorBrush ReadCreate = new SolidColorBrush(Color.FromRgb(0xFF, 0xAB, 0x00));    // orange

        // SNMP value-type colors
        private static readonly SolidColorBrush StringColor = new SolidColorBrush(Color.FromRgb(0x36, 0xB3, 0x7E));   // green
        private static readonly SolidColorBrush IntColor = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));      // blue
        private static readonly SolidColorBrush CounterColor = new SolidColorBrush(Color.FromRgb(0xFF, 0xAB, 0x00));  // orange
        private static readonly SolidColorBrush OidColor = new SolidColorBrush(Color.FromRgb(0xBB, 0x86, 0xFC));      // purple
        private static readonly SolidColorBrush TimeColor = new SolidColorBrush(Color.FromRgb(0x03, 0xDA, 0xC6));     // teal
        private static readonly SolidColorBrush IpColor = new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x43));       // red-orange
        private static readonly SolidColorBrush DefaultColor = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xB2)); // gray

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var type = value as string;
            if (string.IsNullOrEmpty(type)) return DefaultColor;

            var lower = type.ToLowerInvariant().Trim();

            // Access types (from MIB MAX-ACCESS)
            if (lower == "read-write") return ReadWrite;
            if (lower == "read-only") return ReadOnly;
            if (lower == "read-create") return ReadCreate;
            if (lower.StartsWith("not-accessible") || lower == "accessible-for-notify") return NotAccessible;
            if (lower.Contains("write")) return ReadWrite;
            if (lower.Contains("create")) return ReadCreate;

            // SNMP value types
            if (lower.Contains("string") || lower.Contains("octet"))
                return StringColor;
            if (lower.Contains("integer") || lower.Contains("int") || lower.Contains("gauge"))
                return IntColor;
            if (lower.Contains("counter"))
                return CounterColor;
            if (lower.Contains("objectid") || lower.Contains("oid") || lower.Contains("object identifier"))
                return OidColor;
            if (lower.Contains("time") || lower.Contains("timetick"))
                return TimeColor;
            if (lower.Contains("ip") || lower.Contains("address") || lower.Contains("netaddr"))
                return IpColor;

            return DefaultColor;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
