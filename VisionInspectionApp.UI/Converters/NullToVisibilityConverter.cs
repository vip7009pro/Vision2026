using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VisionInspectionApp.UI.Converters;

/// <summary>
/// Trả về Visible nếu đối tượng là null (để hiển thị placeholder offline), ngược lại trả về Collapsed.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
