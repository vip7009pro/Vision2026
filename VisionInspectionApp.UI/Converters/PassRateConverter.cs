using System;
using System.Globalization;
using System.Windows.Data;

namespace VisionInspectionApp.UI.Converters;

/// <summary>
/// Converter để tính tỷ lệ pass (multivalue converter)
/// </summary>
public sealed class PassRateConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && int.TryParse(values[0].ToString(), out int pass) && int.TryParse(values[1].ToString(), out int total))
        {
            if (total == 0)
                return "0%";

            double rate = (pass * 100.0) / total;
            return $"{rate:F1}%";
        }

        return "N/A";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return new[] { Binding.DoNothing, Binding.DoNothing };
    }
}
