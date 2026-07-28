using System;
using System.Diagnostics;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== TESTING ORIGIN MATCHER ALGORITHMS ===");

        // Create synthetic image with geometric features (rectangle + cross + circle)
        int imgW = 1200;
        int imgH = 900;
        using var baseImg = new Mat(new Size(imgW, imgH), MatType.CV_8UC1, Scalar.All(200));

        // Draw shape at center (600, 450)
        Cv2.Rectangle(baseImg, new Rect(530, 380, 140, 140), Scalar.All(30), 4);
        Cv2.Circle(baseImg, new Point(600, 450), 40, Scalar.All(10), 3);
        Cv2.Line(baseImg, new Point(550, 450), new Point(650, 450), Scalar.All(10), 3);

        // Define Origin Template ROI at center
        var def = new PointDefinition
        {
            Name = "Origin",
            OriginAlgorithm = OriginAlgorithm.MvpShapeMatch,
            SearchRoi = new Roi { X = 50, Y = 50, Width = 1100, Height = 800 },
            TemplateRoi = new Roi { X = 520, Y = 370, Width = 160, Height = 160, Angle = 0.0 },
            MinAngle = -45.0,
            MaxAngle = 45.0,
            AngleStep = 1.0,
            MatchScoreThreshold = 0.7
        };

        // Extract template patch
        var templRoiRect = new Rect(520, 370, 160, 160);
        using var templateGray = new Mat(baseImg, templRoiRect);

        var matcher = new OriginMatcher();

        // 1. TEST IDENTICAL IMAGE MATCH
        var sw = Stopwatch.StartNew();
        var match0 = matcher.MatchWithRotation(baseImg, def, templateGray, null, def.MinAngle, def.MaxAngle, def.AngleStep);
        sw.Stop();

        Console.WriteLine($"\n--- Test 1: Identical Image Match (0 deg) ---");
        Console.WriteLine($"Score: {match0.Score:F4} (Expected >= 0.99)");
        Console.WriteLine($"Position: ({match0.Position.X:F2}, {match0.Position.Y:F2}) (Expected 600.00, 450.00)");
        Console.WriteLine($"Angle: {match0.AngleDeg:F2} deg (Expected 0.00)");
        Console.WriteLine($"Execution Time: {sw.ElapsedMilliseconds} ms");

        bool pass1 = match0.Score >= 0.99 && Math.Abs(match0.Position.X - 600.0) < 1.5 && Math.Abs(match0.Position.Y - 450.0) < 1.5;
        Console.WriteLine($"Test 1 Result: {(pass1 ? "PASS" : "FAIL")}");

        // 2. TEST ROTATED IMAGE MATCH (+25 degrees)
        double testAngle = 25.0;
        Point2f center = new Point2f(600f, 450f);
        using var rotMat = Cv2.GetRotationMatrix2D(center, -testAngle, 1.0);
        using var rotatedImg = new Mat();
        Cv2.WarpAffine(baseImg, rotatedImg, rotMat, baseImg.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(200));

        sw.Restart();
        var matchRot = matcher.MatchWithRotation(rotatedImg, def, templateGray, null, def.MinAngle, def.MaxAngle, def.AngleStep);
        sw.Stop();

        Console.WriteLine($"\n--- Test 2: Rotated Image Match (+{testAngle} deg) ---");
        Console.WriteLine($"Score: {matchRot.Score:F4} (Expected >= 0.90)");
        Console.WriteLine($"Position: ({matchRot.Position.X:F2}, {matchRot.Position.Y:F2}) (Expected 600.00, 450.00)");
        Console.WriteLine($"Angle: {matchRot.AngleDeg:F2} deg (Expected {testAngle:F2})");
        Console.WriteLine($"Execution Time: {sw.ElapsedMilliseconds} ms");

        bool pass2 = matchRot.Score >= 0.90 && Math.Abs(matchRot.AngleDeg - testAngle) < 1.5 && Math.Abs(matchRot.Position.X - 600.0) < 3.0;
        Console.WriteLine($"Test 2 Result: {(pass2 ? "PASS" : "FAIL")}");

        // 3. TEST SHAPE PYRAMID ALGORITHM
        def.OriginAlgorithm = OriginAlgorithm.ShapePyramid;
        sw.Restart();
        var matchPyr = matcher.MatchWithRotation(rotatedImg, def, templateGray, null, def.MinAngle, def.MaxAngle, def.AngleStep);
        sw.Stop();

        Console.WriteLine($"\n--- Test 3: ShapePyramid Algorithm (+{testAngle} deg) ---");
        Console.WriteLine($"Score: {matchPyr.Score:F4}");
        Console.WriteLine($"Position: ({matchPyr.Position.X:F2}, {matchPyr.Position.Y:F2})");
        Console.WriteLine($"Angle: {matchPyr.AngleDeg:F2} deg");
        Console.WriteLine($"Execution Time: {sw.ElapsedMilliseconds} ms");

        Console.WriteLine("\n=== ALL TESTS FINISHED ===");
    }
}
