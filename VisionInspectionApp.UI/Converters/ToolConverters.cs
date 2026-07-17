using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VisionInspectionApp.UI.Converters
{
    public class ToolToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string toolName)
            {
                // Using Segoe MDL2 Assets or Segoe Fluent Icons unicode characters
                return toolName switch
                {
                    "Preprocess" => "\uE71C", // Filter
                    "Origin" => "\uE81E", // MapPin
                    "Point" => "\uE71A", // Location
                    "Line" => "\uE74C", // Edit / Line
                    "Caliper" => "\uE928", // Ruler
                    "LinePairDetection" => "\uE8A5", // Parallel lines
                    "EdgePairDetect" => "\uE8A5",
                    "CircleFinder" => "\uEA3A", // Circle
                    "Diameter" => "\uE9CE", // Diameter
                    "Distance" => "\uE8CB", // Distance
                    "LineLineDistance" => "\uE8CB",
                    "PointLineDistance" => "\uE8CB",
                    "Angle" => "\uE995", // Angle
                    "EdgePair" => "\uE8A5",
                    "Condition" => "\uE81C", // Logic/Function
                    "Text" => "\uE8D2", // Text
                    "BlobDetection" => "\uE9A3", // Drops / Blob
                    "SurfaceCompare" => "\uE7B6", // Compare
                    "CodeDetection" => "\uED14", // QRCode
                    _ => "\uE734" // Default (Star or general)
                };
            }
            return "\uE734";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ToolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string toolName)
            {
                var hex = toolName switch
                {
                    "Preprocess" => "#0078D7",
                    "Origin" => "#D13438",
                    "Point" => "#107C10",
                    "Line" => "#00B7C3",
                    "Caliper" => "#8764B8",
                    "CircleFinder" => "#00B7C3",
                    "Condition" => "#D83B01",
                    "Text" => "#5C2D91",
                    "CodeDetection" => "#038387",
                    "BlobDetection" => "#C239B3",
                    "SurfaceCompare" => "#0078D7",
                    _ => "#0078D7"
                };
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            return new SolidColorBrush(Colors.Black);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
