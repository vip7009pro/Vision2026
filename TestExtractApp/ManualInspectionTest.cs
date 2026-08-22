using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;
using VisionInspectionApp.UI.Models.ManualInspection;
using VisionInspectionApp.UI.Services.ManualInspection;

namespace TestExtractApp;

public static class ManualInspectionTest
{
    public static void RunTests()
    {
        Console.WriteLine("\n========================================================");
        Console.WriteLine("🧪 RUNNING MANUAL INSPECTION / 2D VISION CMM UNIT TESTS");
        Console.WriteLine("========================================================");

        TestCircleFit();
        TestRotatedRectFit();
        TestAngle3Points();
        TestLineIntersection();
        TestLineLineDistance();
        TestSubpixelEdgeDetection();
        TestToleranceEvaluation();
        TestCsvExport();

        Console.WriteLine("✅ ALL MANUAL INSPECTION TESTS PASSED SUCCESSFULLY!\n");
    }

    private static void TestCircleFit()
    {
        Console.Write("  [Test 1] Fit Circle 3 Points... ");
        var p1 = new GeoPoint2D(0, 10);
        var p2 = new GeoPoint2D(10, 0);
        var p3 = new GeoPoint2D(0, -10);

        bool ok = ManualVisionMeasurementService.TryFitCircle3Points(p1, p2, p3, out var circle);
        if (!ok || Math.Abs(circle.Center.X) > 1e-4 || Math.Abs(circle.Center.Y) > 1e-4 || Math.Abs(circle.Radius - 10.0) > 1e-4)
        {
            throw new Exception($"Circle fit failed! Center=({circle.Center.X},{circle.Center.Y}), Radius={circle.Radius}");
        }
        Console.WriteLine($"PASSED (Center=({circle.Center.X:F2},{circle.Center.Y:F2}), R={circle.Radius:F2}, Ø={circle.Diameter:F2})");
    }

    private static void TestRotatedRectFit()
    {
        Console.Write("  [Test 2] Fit Rotated Rect 3 Points... ");
        var p1 = new GeoPoint2D(0, 0);
        var p2 = new GeoPoint2D(100, 0);
        var p3 = new GeoPoint2D(100, 50);

        bool ok = ManualVisionMeasurementService.TryFitRotatedRect3Points(p1, p2, p3, out var rRect);
        if (!ok || Math.Abs(rRect.Width - 100.0) > 1e-4 || Math.Abs(rRect.Height - 50.0) > 1e-4)
        {
            throw new Exception($"Rotated rect fit failed! W={rRect.Width}, H={rRect.Height}");
        }

        var corners = rRect.GetCorners();
        if (corners.Count != 4)
        {
            throw new Exception("Rotated rect corners count != 4");
        }
        Console.WriteLine($"PASSED (W={rRect.Width:F1}, H={rRect.Height:F1}, Angle={rRect.AngleDeg:F1}°)");
    }

    private static void TestAngle3Points()
    {
        Console.Write("  [Test 3] Calculate Angle 3 Points (Right angle)... ");
        var p1 = new GeoPoint2D(100, 0);
        var vertex = new GeoPoint2D(0, 0);
        var p2 = new GeoPoint2D(0, 100);

        double angle = ManualVisionMeasurementService.CalculateAngle3Points(p1, vertex, p2);
        if (Math.Abs(angle - 90.0) > 1e-4)
        {
            throw new Exception($"Angle 3P failed! Expected 90.0°, got {angle}°");
        }
        Console.WriteLine($"PASSED ({angle:F2}°)");
    }

    private static void TestLineIntersection()
    {
        Console.Write("  [Test 4] Line Intersection (X and Y axes)... ");
        var l1 = new GeoLine2D(new GeoPoint2D(-50, 0), new GeoPoint2D(50, 0));
        var l2 = new GeoLine2D(new GeoPoint2D(0, -50), new GeoPoint2D(0, 50));

        bool ok = ManualVisionMeasurementService.TryFindLineIntersection(l1, l2, out var inter);
        if (!ok || Math.Abs(inter.X) > 1e-4 || Math.Abs(inter.Y) > 1e-4)
        {
            throw new Exception($"Line intersection failed! Got ({inter.X},{inter.Y})");
        }
        Console.WriteLine($"PASSED (Intersection=({inter.X:F1},{inter.Y:F1}))");
    }

    private static void TestLineLineDistance()
    {
        Console.Write("  [Test 5] Line-to-Line Distance (Parallel lines)... ");
        var l1 = new GeoLine2D(new GeoPoint2D(0, 0), new GeoPoint2D(100, 0));
        var l2 = new GeoLine2D(new GeoPoint2D(0, 25), new GeoPoint2D(100, 25));

        double dist = ManualVisionMeasurementService.CalculateLineLineDistance(l1, l2);
        if (Math.Abs(dist - 25.0) > 1e-4)
        {
            throw new Exception($"Line distance failed! Expected 25.0, got {dist}");
        }
        Console.WriteLine($"PASSED (Dist={dist:F2} px)");
    }

    private static void TestSubpixelEdgeDetection()
    {
        Console.Write("  [Test 6] Sub-pixel Edge Point Detection (Sobel + Parabolic)... ");
        using var mat = new Mat(100, 100, MatType.CV_8UC1, Scalar.All(50));
        // Draw bright step edge at X >= 50
        mat.ColRange(50, 100).SetTo(Scalar.All(200));

        bool ok = ManualVisionMeasurementService.TryFindSubpixelEdgePoint(mat, new GeoPoint2D(48, 50), 15, out var edgePt);
        if (!ok || Math.Abs(edgePt.X - 50.0) > 1.5)
        {
            throw new Exception($"Sub-pixel edge detection failed! Found X={edgePt.X:F3}, Y={edgePt.Y:F3}");
        }
        Console.WriteLine($"PASSED (Found Sub-pixel Edge at X={edgePt.X:F3}, Y={edgePt.Y:F3})");
    }

    private static void TestToleranceEvaluation()
    {
        Console.Write("  [Test 7] Tolerance GD&T Pass/Fail Evaluation... ");
        var record1 = new ManualMeasurementRecord
        {
            ValueMm = 50.02,
            Nominal = 50.0,
            UpperTolerance = 0.05,
            LowerTolerance = 0.05
        };
        record1.EvaluateTolerance();
        if (record1.Status != ToleranceStatus.PASS)
        {
            throw new Exception($"Record1 should be PASS, got {record1.Status}");
        }

        var record2 = new ManualMeasurementRecord
        {
            ValueMm = 50.15,
            Nominal = 50.0,
            UpperTolerance = 0.05,
            LowerTolerance = 0.05
        };
        record2.EvaluateTolerance();
        if (record2.Status != ToleranceStatus.NG)
        {
            throw new Exception($"Record2 should be NG, got {record2.Status}");
        }
        Console.WriteLine("PASSED (PASS/NG logic verified)");
    }

    private static void TestCsvExport()
    {
        Console.Write("  [Test 8] Manual Measurement CSV Exporter... ");
        string tmpFile = Path.Combine(Path.GetTempPath(), $"TestManualExport_{Guid.NewGuid():N}.csv");
        try
        {
            var records = new List<ManualMeasurementRecord>
            {
                new() { Id = 1, ToolName = "Khoảng cách 2 điểm (2P)", ValueMm = 25.432, ValuePx = 254.3, Unit = "mm", Nominal = 25.4, UpperTolerance = 0.1, LowerTolerance = 0.1, Status = ToleranceStatus.PASS, Details = "P1(0,0) -> P2(254,0)" },
                new() { Id = 2, ToolName = "Góc 2 đường", ValueMm = 90.05, ValuePx = 90.05, Unit = "°", Nominal = 90.0, UpperTolerance = 0.1, LowerTolerance = 0.1, Status = ToleranceStatus.PASS, Details = "Góc 90.05°" }
            };

            ManualMeasurementExporter.ExportToCsv(tmpFile, records, 10.0);
            if (!File.Exists(tmpFile))
            {
                throw new Exception("CSV file not created!");
            }

            string content = File.ReadAllText(tmpFile);
            if (!content.Contains("Khoảng cách 2 điểm (2P)") || !content.Contains("PASS"))
            {
                throw new Exception("CSV content missing expected records!");
            }
            Console.WriteLine("PASSED (CSV exported & validated)");
        }
        finally
        {
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
        }
    }
}
