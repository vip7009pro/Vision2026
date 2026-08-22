using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VisionInspectionApp.UI.Converters;

public sealed class EqualityToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return Equals(value, parameter);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is not null)
        {
            return parameter;
        }
        return Binding.DoNothing;
    }
}

public sealed class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return System.Windows.Visibility.Collapsed;
        return Equals(value, parameter) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ToleranceStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PassBrush = new(Color.FromRgb(34, 197, 94)); // Green
    private static readonly SolidColorBrush FailBrush = new(Color.FromRgb(239, 68, 68)); // Red
    private static readonly SolidColorBrush NoneBrush = new(Color.FromRgb(148, 163, 184)); // Gray

    static ToleranceStatusToBrushConverter()
    {
        PassBrush.Freeze();
        FailBrush.Freeze();
        NoneBrush.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Models.ManualInspection.ToleranceStatus status)
        {
            return status switch
            {
                Models.ManualInspection.ToleranceStatus.PASS => PassBrush,
                Models.ManualInspection.ToleranceStatus.NG => FailBrush,
                _ => NoneBrush
            };
        }
        return NoneBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
