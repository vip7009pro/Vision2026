using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

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
            return d.ToString(Format, culture);
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

        var sep = culture.NumberFormat.NumberDecimalSeparator;
        var normalized = s.Replace(",", sep).Replace(".", sep);

        if (double.TryParse(normalized, NumberStyles.Float, culture, out var parsed))
        {
            return parsed;
        }

        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        return Binding.DoNothing;
    }
}
