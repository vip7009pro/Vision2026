using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestApp;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("STRESS TESTING 50 RANDOM SHIFTS & ROTATIONS ON MVPSHAPEMATCH2");
        Console.WriteLine("================================================================================");

        int imgW = 5120;
        int imgH = 3840;
        int cx = 2560;
        int cy = 1920;

        using var fullBgr = new Mat(new Size(imgW, imgH), MatType.CV_8UC3, new Scalar(210, 210, 210));
        
        // Draw distinct non-symmetric industrial product features
        Cv2.Rectangle(fullBgr, new Rect(cx - 300, cy - 250, 600, 500), new Scalar(40, 40, 40), 8);
        Cv2.Rectangle(fullBgr, new Rect(cx - 220, cy - 180, 440, 360), new Scalar(90, 90, 90), 4);
        
        // Circular hole & non-symmetric crosshair fiducial (L-shaped notches)
        Cv2.Circle(fullBgr, new Point(cx, cy), 100, new Scalar(30, 30, 30), 6);
        Cv2.Circle(fullBgr, new Point(cx, cy), 50, new Scalar(150, 50, 50), -1);
        Cv2.Line(fullBgr, new Point(cx - 150, cy), new Point(cx + 80, cy), new Scalar(20, 20, 20), 4);
        Cv2.Line(fullBgr, new Point(cx, cy - 120), new Point(cx, cy + 150), new Scalar(20, 20, 20), 4);
        
        // Non-symmetric corners
        Cv2.Circle(fullBgr, new Point(cx - 260, cy - 210), 20, new Scalar(30, 30, 30), -1);
        Cv2.Rectangle(fullBgr, new Rect(cx + 240, cy - 220, 30, 30), new Scalar(30, 30, 30), -1);
        Cv2.Circle(fullBgr, new Point(cx - 260, cy + 210), 10, new Scalar(30, 30, 30), -1);
        
        Cv2.PutText(fullBgr, "VISION 2026 PRODUCT", new Point(cx - 180, cy - 100), HersheyFonts.HersheyComplex, 1.0, new Scalar(20, 20, 20), 2);

        var searchRoi = new Roi { X = cx - 900, Y = cy - 700, Width = 1800, Height = 1400 };
        var templateRoi = new Roi { X = cx - 200, Y = cy - 160, Width = 400, Height = 320, Angle = 0.0 };

        using var templateBgr = new Mat(fullBgr, new Rect(templateRoi.X, templateRoi.Y, templateRoi.Width, templateRoi.Height));
        using var templateGray = templateBgr.CvtColor(ColorConversionCodes.BGR2GRAY);

        var matcher = new OriginMatcher();

        int totalTests = 50;
        var rng = new Random(100);
        int passed = 0;
        var times = new List<double>();

        Console.WriteLine($"\nRunning {totalTests} consecutive random test cases (Shift [-40..+40px], Angle [-12..+12 deg])...");

        for (int i = 0; i < totalTests; i++)
        {
            double dX = (rng.NextDouble() - 0.5) * 80.0;
            double dY = (rng.NextDouble() - 0.5) * 80.0;
            double angle = (rng.NextDouble() - 0.5) * 24.0;

            Point2f rotCenter = new Point2f(cx, cy);
            using var rotMat = Cv2.GetRotationMatrix2D(rotCenter, -angle, 1.0);
            rotMat.Set(0, 2, rotMat.Get<double>(0, 2) + dX);
            rotMat.Set(1, 2, rotMat.Get<double>(1, 2) + dY);

            using var warpedImg = new Mat();
            Cv2.WarpAffine(fullBgr, warpedImg, rotMat, fullBgr.Size(), InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(210, 210, 210));

            double trueX = cx + dX;
            double trueY = cy + dY;
            double trueAngle = angle;

            var defMvp2 = new PointDefinition
            {
                Name = "Origin",
                OriginAlgorithm = OriginAlgorithm.MvpShapeMatch2,
                SearchRoi = searchRoi,
                TemplateRoi = templateRoi,
                MinAngle = -15.0,
                MaxAngle = 15.0,
                AngleStep = 1.0,
                MatchScoreThreshold = 0.6
            };

            var sw = Stopwatch.StartNew();
            var res = matcher.MatchWithRotation(warpedImg, defMvp2, templateGray, null, -15.0, 15.0, 1.0);
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);

            double errX = res.Position.X - trueX;
            double errY = res.Position.Y - trueY;
            double errDist = Math.Sqrt(errX * errX + errY * errY);
            double errAngle = Math.Abs(res.AngleDeg - trueAngle);

            bool ok = res.Score >= 0.70 && errDist < 2.0 && errAngle < 1.0;
            if (ok) passed++;

            if (!ok || (i + 1) % 10 == 0)
            {
                Console.WriteLine($"Test {i + 1,2}/{totalTests}: Expected ({trueX,6:F1}, {trueY,6:F1}, {trueAngle,5:F1}°) -> Det ({res.Position.X,6:F1}, {res.Position.Y,6:F1}, {res.AngleDeg,5:F1}°) | Err: {errDist:F2}px, {errAngle:F2}° | Score: {res.Score:F4} | Time: {sw.Elapsed.TotalMilliseconds:F1}ms | {(ok ? "PASS" : "FAIL ⚠️")}");
            }
        }

        Console.WriteLine("----------------------------------------------------------------------------------------------------------------------------------");
        Console.WriteLine($"SUMMARY: {passed}/{totalTests} PASSED ({(double)passed / totalTests * 100.0:F1}%) | Avg Runtime: {times.Average():F2} ms (Min: {times.Min():F2} ms, Max: {times.Max():F2} ms)");
        Console.WriteLine("==================================================================================================================================");
    }
}
