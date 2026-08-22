using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VisionInspectionApp.Application.Services;

namespace VisionInspectionApp.UI.Converters;

public sealed class FlexibleDoubleConverter : IValueConverter
{
    public string Format { get; set; } = "0.###";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is double d)
        {
            return d.ToString(Format, CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (value as string) ?? string.Empty;
        s = s.Trim();

        if (string.IsNullOrWhiteSpace(s))
        {
            return Binding.DoNothing;
        }

        if (s is "-" or "." or "," or "-." or "-,")
        {
            return Binding.DoNothing;
        }

        // While the user is actively typing a decimal separator or trailing decimals like "28." or "28.0"
        // return Binding.DoNothing so WPF doesn't immediately overwrite the TextBox with stripped format "28"
        if (s.EndsWith('.') || s.EndsWith(','))
        {
            return Binding.DoNothing;
        }

        if ((s.Contains('.') || s.Contains(',')) && s.EndsWith('0'))
        {
            return Binding.DoNothing;
        }

        if (FlexibleNumberParser.TryParseDouble(s, out var parsed, culture))
        {
            return parsed;
        }

        return Binding.DoNothing;
    }
}
