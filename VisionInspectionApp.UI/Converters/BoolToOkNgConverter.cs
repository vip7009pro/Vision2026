using System;
using System.Globalization;
using System.Windows.Data;

namespace VisionInspectionApp.UI.Converters;

public sealed class BoolToOkNgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? "OK" : "NG";
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            if (string.Equals(s, "OK", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(s, "NG", StringComparison.OrdinalIgnoreCase)) return false;
        }

        return Binding.DoNothing;
    }
}
