using System;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class MvpShapeMatch2PreprocessTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ❌ FAIL: {message}");
            Console.ResetColor();
            throw new Exception($"Assertion failed: {message}");
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✅ PASS: {message}");
        Console.ResetColor();
    }

    public static void RunAllTests()
    {
        Console.WriteLine("\n========================================================");
        Console.WriteLine("🎯 RUNNING MVPSHAPEMATCH2 PREPROCESS INTEGRATION TESTS");
        Console.WriteLine("========================================================");

        using var rawMat = new Mat(300, 400, MatType.CV_8UC1, new Scalar(50));
        Cv2.Rectangle(rawMat, new Rect(120, 80, 160, 140), new Scalar(200), -1);
        Cv2.Circle(rawMat, new Point(200, 150), 30, new Scalar(100), -1);

        var roiRect = new Rect(100, 60, 200, 180);

        // Test 1: Raw Grayscale
        RunSingleCase("Test 1: Ảnh gốc Grayscale", rawMat, rawMat, roiRect, null, 0.98);

        // Test 2: Binary Threshold
        using var threshMat = new Mat();
        Cv2.Threshold(rawMat, threshMat, 128, 255, ThresholdTypes.Binary);
        RunSingleCase("Test 2: Ảnh qua Binary Threshold (0 và 255)", threshMat, threshMat, roiRect, null, 0.98);

        // Test 3: Binary Inverted
        using var invThreshMat = new Mat();
        Cv2.Threshold(rawMat, invThreshMat, 128, 255, ThresholdTypes.BinaryInv);
        RunSingleCase("Test 3: Ảnh qua Binary Inverted", invThreshMat, invThreshMat, roiRect, null, 0.98);

        // Test 4: Otsu Threshold
        using var otsuMat = new Mat();
        Cv2.Threshold(rawMat, otsuMat, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);
        RunSingleCase("Test 4: Ảnh qua Otsu Threshold", otsuMat, otsuMat, roiRect, null, 0.98);

        // Test 5: Canny Edge Image
        using var cannyMat = new Mat();
        Cv2.Canny(rawMat, cannyMat, 50, 150);
        RunSingleCase("Test 5: Ảnh qua Canny Edge", cannyMat, cannyMat, roiRect, null, 0.98);

        // Test 6: Gaussian Blur
        using var blurMat = new Mat();
        Cv2.GaussianBlur(rawMat, blurMat, new Size(5, 5), 1.5);
        RunSingleCase("Test 6: Ảnh qua Gaussian Blur", blurMat, blurMat, roiRect, null, 0.98);

        // Test 7: Runtime PreprocessSettings (Search ROI & Template cùng được preprocess động trong OriginMatcher)
        using var templateRawCrop = new Mat(rawMat, roiRect).Clone();
        var preSettings = new PreprocessSettings
        {
            UseThreshold = true,
            ThresholdType = PreprocessThresholdType.Binary,
            ThresholdLow = 128
        };
        var matcher = new OriginMatcher();
        var def7 = new PointDefinition
        {
            Name = "Origin",
            OriginAlgorithm = OriginAlgorithm.MvpShapeMatch2,
            TemplateRoi = new Roi { X = roiRect.X, Y = roiRect.Y, Width = roiRect.Width, Height = roiRect.Height, Angle = 0 },
            SearchRoi = new Roi { X = 0, Y = 0, Width = rawMat.Width, Height = rawMat.Height, Angle = 0 },
            MinAngle = -10,
            MaxAngle = 10,
            AngleStep = 1.0,
            MinScore = 0.5,
            MvpAutoThresh = true
        };
        var res7 = matcher.MatchWithRotation(rawMat, def7, templateRawCrop, preSettings, def7.MinAngle, def7.MaxAngle, def7.AngleStep);
        Assert(res7.Score >= 0.98, $"Test 7: Runtime PreprocessSettings Threshold Score={res7.Score:F4} >= 0.98 (Pos={res7.Position.X:F1},{res7.Position.Y:F1}, Angle={res7.AngleDeg:F2}°)");

        // Test 8: Thay đổi Preprocess liên tiếp không bị dính cache cũ nhờ templHash
        var preSettings8 = new PreprocessSettings
        {
            UseGaussianBlur = true,
            BlurKernel = 5
        };
        var res8 = matcher.MatchWithRotation(rawMat, def7, templateRawCrop, preSettings8, def7.MinAngle, def7.MaxAngle, def7.AngleStep);
        Assert(res8.Score >= 0.98, $"Test 8: Runtime PreprocessSettings GaussianBlur Score={res8.Score:F4} >= 0.98 (Pos={res8.Position.X:F1},{res8.Position.Y:F1}, Angle={res8.AngleDeg:F2}°)");

        Console.WriteLine("\n✅ TOÀN BỘ 8/8 BÀI KIỂM THỬ MVPSHAPEMATCH2 PREPROCESS ĐÃ VƯỢT QUA 100%!\n");
    }

    private static void RunSingleCase(string name, Mat searchMat, Mat templateSourceMat, Rect roiRect, PreprocessSettings? pre, double minExpectedScore)
    {
        using var templateCrop = new Mat(templateSourceMat, roiRect).Clone();

        var def = new PointDefinition
        {
            Name = "Origin",
            OriginAlgorithm = OriginAlgorithm.MvpShapeMatch2,
            TemplateRoi = new Roi { X = roiRect.X, Y = roiRect.Y, Width = roiRect.Width, Height = roiRect.Height, Angle = 0 },
            SearchRoi = new Roi { X = 0, Y = 0, Width = searchMat.Width, Height = searchMat.Height, Angle = 0 },
            MinAngle = -10,
            MaxAngle = 10,
            AngleStep = 1.0,
            MinScore = 0.5,
            MvpAutoThresh = true
        };

        var matcher = new OriginMatcher();
        var result = matcher.MatchWithRotation(searchMat, def, templateCrop, pre, def.MinAngle, def.MaxAngle, def.AngleStep);

        double targetCenterX = roiRect.X + roiRect.Width / 2.0;
        double targetCenterY = roiRect.Y + roiRect.Height / 2.0;
        double dist = Math.Sqrt(Math.Pow(result.Position.X - targetCenterX, 2) + Math.Pow(result.Position.Y - targetCenterY, 2));

        Assert(result.Score >= minExpectedScore, $"{name} — Điểm số mong đợi >={minExpectedScore:F2}, thực tế Score={result.Score:F4}");
        Assert(dist <= 1.5, $"{name} — Độ lệch tâm mong đợi <= 1.5px, thực tế lệch {dist:F2}px (Pos={result.Position.X:F1}, {result.Position.Y:F1}, Target={targetCenterX:F1}, {targetCenterY:F1})");
        Assert(Math.Abs(result.AngleDeg) <= 0.5, $"{name} — Độ lệch góc mong đợi <= 0.5°, thực tế Angle={result.AngleDeg:F2}°");
    }
}
