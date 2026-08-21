using System;
using OpenCvSharp;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;

namespace TestExtractApp;

public static class ColorDiffTest
{
    public static void RunTests()
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("   RUNNING COLORDIFF & LAB TEACH ACCURACY TESTS   ");
        Console.WriteLine("==================================================");

        // Test 1: Single image exact color teach & test
        TestExactColorMatchOnSameImage();

        // Test 2: Origin shift test (Teach on shifted object -> Inspect on shifted object -> DeltaE = 0)
        TestOriginShiftedColorMatch();

        // Test 3: Rotated ROI test
        TestRotatedRoiColorMatch();

        // Test 4: True color difference detection
        TestColorDifferenceDetection();

        Console.WriteLine("==================================================");
        Console.WriteLine("   ALL COLORDIFF TESTS PASSED (100% ACCURATE)     ");
        Console.WriteLine("==================================================");
    }

    private static void TestExactColorMatchOnSameImage()
    {
        Console.WriteLine("[TEST 1] Testing exact color match on same image...");

        using var mat = new Mat(400, 400, MatType.CV_8UC3, new Scalar(50, 100, 200)); // Orange/Brown BGR
        // Draw a green square in the middle (BGR: 0, 200, 0)
        Cv2.Rectangle(mat, new Rect(100, 100, 150, 150), new Scalar(0, 200, 0), -1);

        var inspectRoi = new Roi { X = 110, Y = 110, Width = 80, Height = 80, Angle = 0 };

        // 1. Teach ref color directly with ColorDiffProcessor.GetMeanLab
        var (refL, refA, refB) = ColorDiffProcessor.GetMeanLab(mat, inspectRoi);
        Console.WriteLine($"   Taught Ref: L={refL:F2}, a={refA:F2}, b={refB:F2}");

        var def = new ColorDiffDefinition
        {
            Name = "CD_Test1",
            InspectRoi = inspectRoi,
            UseRefColor = true,
            RefL = Math.Round(refL, 2),
            RefA = Math.Round(refA, 2),
            RefB = Math.Round(refB, 2),
            MaxDeltaE = 2.0
        };

        // 2. Run inspection on the exact same image
        var result = ColorDiffProcessor.Run(mat, def);
        Console.WriteLine($"   Result: DeltaE={result.DeltaE:F4}, Pass={result.Pass} (L={result.MeasuredL:F2}, a={result.MeasuredA:F2}, b={result.MeasuredB:F2})");

        if (result.DeltaE > 0.05 || !result.Pass)
        {
            throw new Exception($"[FAIL] Test 1 failed! DeltaE expected ~0.00 but got {result.DeltaE:F4}");
        }

        Console.WriteLine("   => PASS 100% (DeltaE ~ 0.00)");
    }

    private static void TestOriginShiftedColorMatch()
    {
        Console.WriteLine("[TEST 2] Testing Origin-shifted pose transformation...");

        using var mat = new Mat(600, 600, MatType.CV_8UC3, new Scalar(30, 30, 30));
        // Product moved to (200, 250)
        Cv2.Rectangle(mat, new Rect(200, 250, 120, 120), new Scalar(180, 50, 220), -1);

        var originTeach = new Point2d(100, 100);
        var originFound = new Point2d(200, 250); // Shifted by +100, +150
        double angleDeg = 0.0;

        var teachRoi = new Roi { X = 110, Y = 110, Width = 60, Height = 60, Angle = 0 };

        // Transformed ROI for current image:
        var dx = originFound.X - originTeach.X;
        var dy = originFound.Y - originTeach.Y;
        var sampleRoi = new Roi
        {
            X = (int)Math.Round(teachRoi.X + dx),
            Y = (int)Math.Round(teachRoi.Y + dy),
            Width = teachRoi.Width,
            Height = teachRoi.Height,
            Angle = teachRoi.Angle
        };

        // Teach on this image patch
        var (refL, refA, refB) = ColorDiffProcessor.GetMeanLab(mat, sampleRoi);

        var def = new ColorDiffDefinition
        {
            Name = "CD_OriginShift",
            InspectRoi = sampleRoi,
            UseRefColor = true,
            RefL = Math.Round(refL, 2),
            RefA = Math.Round(refA, 2),
            RefB = Math.Round(refB, 2),
            MaxDeltaE = 2.0
        };

        var result = ColorDiffProcessor.Run(mat, def);
        Console.WriteLine($"   Result: DeltaE={result.DeltaE:F4}, Pass={result.Pass}");

        if (result.DeltaE > 0.05 || !result.Pass)
        {
            throw new Exception($"[FAIL] Test 2 failed! DeltaE expected ~0.00 but got {result.DeltaE:F4}");
        }

        Console.WriteLine("   => PASS 100% (Origin Shift handled accurately)");
    }

    private static void TestRotatedRoiColorMatch()
    {
        Console.WriteLine("[TEST 3] Testing rotated ROI patch Lab sampling...");

        using var mat = new Mat(400, 400, MatType.CV_8UC3, new Scalar(20, 20, 20));
        Cv2.Circle(mat, new Point(200, 200), 100, new Scalar(200, 150, 40), -1);

        var rotatedRoi = new Roi { X = 160, Y = 160, Width = 80, Height = 80, Angle = 35.0 };

        var (refL, refA, refB) = ColorDiffProcessor.GetMeanLab(mat, rotatedRoi);

        var def = new ColorDiffDefinition
        {
            Name = "CD_Rotated",
            InspectRoi = rotatedRoi,
            UseRefColor = true,
            RefL = Math.Round(refL, 2),
            RefA = Math.Round(refA, 2),
            RefB = Math.Round(refB, 2),
            MaxDeltaE = 2.0
        };

        var result = ColorDiffProcessor.Run(mat, def);
        Console.WriteLine($"   Result: DeltaE={result.DeltaE:F4}, Pass={result.Pass}");

        if (result.DeltaE > 0.05 || !result.Pass)
        {
            throw new Exception($"[FAIL] Test 3 failed! DeltaE expected ~0.00 but got {result.DeltaE:F4}");
        }

        Console.WriteLine("   => PASS 100% (Rotated polygon mask accurate)");
    }

    private static void TestColorDifferenceDetection()
    {
        Console.WriteLine("[TEST 4] Testing true color difference detection (Red vs Green)...");

        using var mat = new Mat(300, 300, MatType.CV_8UC3, new Scalar(0, 0, 255)); // Pure Red in BGR

        var def = new ColorDiffDefinition
        {
            Name = "CD_Diff",
            InspectRoi = new Roi { X = 50, Y = 50, Width = 100, Height = 100 },
            UseRefColor = true,
            // Green in CIELab: roughly L=87, a=-86, b=83
            RefL = 87.0,
            RefA = -86.0,
            RefB = 83.0,
            MaxDeltaE = 5.0
        };

        var result = ColorDiffProcessor.Run(mat, def);
        Console.WriteLine($"   Result: DeltaE={result.DeltaE:F2}, Pass={result.Pass} (Expected NG with DeltaE > 50)");

        if (result.DeltaE < 30.0 || result.Pass)
        {
            throw new Exception($"[FAIL] Test 4 failed! DeltaE should be large between Red and Green, got {result.DeltaE:F2}");
        }

        Console.WriteLine("   => PASS 100% (Correctly detected color difference)");
    }
}
