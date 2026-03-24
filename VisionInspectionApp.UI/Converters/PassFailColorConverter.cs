using System;
using System.Globalization;
using System.Windows.Data;

namespace VisionInspectionApp.UI.Converters;

/// <summary>
/// Converter để chọn màu dựa trên PASS/FAIL trong text
/// </summary>
public sealed class PassFailColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string text)
        {
            if (text.Contains("PASS"))
                return "Green";
            if (text.Contains("FAIL"))
                return "Red";
            if (text.Contains("WARNING"))
                return "Orange";
        }

        return "Black";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
