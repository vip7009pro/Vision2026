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
        Console.WriteLine("BENCHMARK EXACT USER ROI GEOMETRY ON 20MP IMAGE (5120x3840)");
        Console.WriteLine("Search ROI:   (2377, 1398) to (3772, 2423) -> 1395 x 1025 px");
        Console.WriteLine("Template ROI: (2761, 1791) to (3215, 1944) ->  454 x  153 px");
        Console.WriteLine("================================================================================");

        int imgW = 5120;
        int imgH = 3840;

        using var fullBgr = new Mat(new Size(imgW, imgH), MatType.CV_8UC3, new Scalar(210, 210, 210));
        
        // Exact user ROI geometry
        var searchRoi = new Roi { X = 2377, Y = 1398, Width = 1395, Height = 1025, Angle = 0.0 };
        var templateRoi = new Roi { X = 2761, Y = 1791, Width = 454, Height = 153, Angle = 0.0 };

        int cx = templateRoi.X + templateRoi.Width / 2;
        int cy = templateRoi.Y + templateRoi.Height / 2;

        // Draw industrial label features in user ROI region
        // Barcode lines
        for (int x = searchRoi.X + 50; x <= searchRoi.X + searchRoi.Width - 50; x += 15)
        {
            Cv2.Line(fullBgr, new Point(x, searchRoi.Y + 50), new Point(x, searchRoi.Y + 180), new Scalar(20, 20, 20), (x % 30 == 0) ? 6 : 3);
        }
        // Text block
        Cv2.PutText(fullBgr, "GH63-22334A_A_SM-A266B", new Point(searchRoi.X + 100, searchRoi.Y + 280), HersheyFonts.HersheyComplex, 1.2, new Scalar(20, 20, 20), 3);
        
        // MK01 Black Box Target inside Template ROI
        Cv2.Rectangle(fullBgr, new Rect(templateRoi.X, templateRoi.Y, templateRoi.Width, templateRoi.Height), new Scalar(20, 20, 20), -1);
        Cv2.PutText(fullBgr, "MK01", new Point(templateRoi.X + 60, templateRoi.Y + 100), HersheyFonts.HersheyComplex, 2.5, new Scalar(240, 240, 240), 6);
        
        // Other text & QR matrix
        Cv2.PutText(fullBgr, "RoHS HF W33", new Point(searchRoi.X + 150, searchRoi.Y + 600), HersheyFonts.HersheyComplex, 1.2, new Scalar(20, 20, 20), 2);
        Cv2.Rectangle(fullBgr, new Rect(searchRoi.X + 600, searchRoi.Y + 500, 200, 200), new Scalar(30, 30, 30), 4);

        using var templateBgr = new Mat(fullBgr, new Rect(templateRoi.X, templateRoi.Y, templateRoi.Width, templateRoi.Height));
        using var templateGray = templateBgr.CvtColor(ColorConversionCodes.BGR2GRAY);

        var preSettings = new PreprocessSettings
        {
            UseGaussianBlur = true,
            BlurKernel = 3,
            UseThreshold = true,
            ThresholdValue = 128
        };

        var def = new PointDefinition
        {
            Name = "Origin",
            OriginAlgorithm = OriginAlgorithm.MvpShapeMatch2,
            SearchRoi = searchRoi,
            TemplateRoi = templateRoi,
            MinAngle = -10.0,
            MaxAngle = 10.0,
            AngleStep = 1.0,
            MatchScoreThreshold = 0.80
        };

        var matcher = new OriginMatcher();

        // Warmup
        for (int w = 0; w < 3; w++)
        {
            _ = matcher.MatchWithRotation(fullBgr, def, templateGray, preSettings, -10.0, 10.0, 1.0);
        }

        Console.WriteLine("\n[Profiling Current Algorithm Stages on User 1395x1025 ROI]:");
        
        int runs = 10;
        var totalTimes = new List<double>();
        MatchResult lastRes = null!;

        for (int r = 0; r < runs; r++)
        {
            var sw = Stopwatch.StartNew();
            lastRes = matcher.MatchWithRotation(fullBgr, def, templateGray, preSettings, -10.0, 10.0, 1.0);
            sw.Stop();
            totalTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        Console.WriteLine($"-> Current Runtime on User ROI: Avg = {totalTimes.Average():F2} ms (Min = {totalTimes.Min():F2} ms, Max = {totalTimes.Max():F2} ms)");
        Console.WriteLine($"-> Detected: Pos=({lastRes.Position.X:F1}, {lastRes.Position.Y:F1}), Angle={lastRes.AngleDeg:F2}°, Score={lastRes.Score:F4}");
    }
}
