using System;
using System.Globalization;

namespace VisionInspectionApp.Application.Services;

public static class FlexibleNumberParser
{
    public static bool TryParseDouble(string? input, out double result, CultureInfo? culture = null)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = input.Trim();
        if (s is "-" or "." or "," or "-." or "-,")
        {
            return false;
        }

        // 1. Normalize both ',' and '.' to '.' and parse with InvariantCulture (Float style without thousands separator)
        var dotNormalized = s.Replace(',', '.');
        if (double.TryParse(dotNormalized, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        // 2. Fallback to specified or current culture
        if (culture != null && double.TryParse(s, NumberStyles.Float, culture, out result))
        {
            return true;
        }

        return false;
    }
}
