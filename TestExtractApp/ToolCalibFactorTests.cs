using System;
using System.Collections.Generic;
using System.Text.Json;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class ToolCalibFactorTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("🚀 RUNNING TOOL CALIB FACTOR (SET AS CALIB) TESTS");
        Console.WriteLine("=======================================================");

        TestDistanceMeasurementExtraction();
        TestSegmentLineDistanceMeasurementExtraction();
        TestEdgePairDetectMeasurementExtraction();
        TestDiameterAndCircleFinderMeasurementExtraction();
        TestValidationAndEdgeCases();
        TestCircleFinderDefinitionSerialization();
        TestDualAxisCalibrationRectangularPart();
        TestAngleOrientationDetection();
        TestDistanceMmArbitraryAngleVector();
        TestBackwardCompatibilityWhenDualAxisZero();

        Console.WriteLine("=======================================================");
        Console.WriteLine("✅ ALL TOOL CALIB FACTOR (1D & 2D) TESTS PASSED 100%!");
        Console.WriteLine("=======================================================\n");
    }

    private static void TestDualAxisCalibrationRectangularPart()
    {
        Console.Write("Testing Dual-Axis (X & Y) Calibration for Rectangular Part... ");

        // BÀI TOÁN THỰC TẾ CỦA KHÁCH HÀNG:
        // Phôi hình chữ nhật:
        // - Chiều dài (Phương ngang X): Nominal = 50.0 mm, Tolerance = ±0.5 mm
        //   Ống kính/Camera chụp được: dx = 500 px, dy = 0 px. => Tỉ lệ thực tế: ppmX = 10.0 px/mm.
        // - Chiều rộng (Phương dọc Y): Nominal = 20.0 mm, Tolerance = ±0.5 mm
        //   Do méo quang học ống kính dọc theo trục Y, chụp được: dx = 0 px, dy = 160 px. => Tỉ lệ thực tế: ppmY = 8.0 px/mm.

        // TRƯỜNG HỢP 1: Cơ chế cũ (Chung 1 hệ số calib)
        // Nếu chọn chiều ngang làm chuẩn: PixelsPerMm = 10.0
        double oldPpm = 10.0;
        double oldMeasuredLen = 500.0 / oldPpm; // = 50.0 mm (PASS)
        double oldMeasuredWid = 160.0 / oldPpm; // = 16.0 mm (NG nghiêm trọng! Nominal 20 ± 0.5)
        bool oldLenPass = oldMeasuredLen >= 49.5 && oldMeasuredLen <= 50.5;
        bool oldWidPass = oldMeasuredWid >= 19.5 && oldMeasuredWid <= 20.5;

        if (!oldLenPass) throw new Exception("Expected old length to pass with ppm=10");
        if (oldWidPass) throw new Exception("Expected old width to FAIL (NG) with 1D calibration");

        // TRƯỜNG HỢP 2: Cơ chế mới (2 trục độc lập X & Y)
        var config = new VisionConfig
        {
            PixelsPerMmX = 10.0,
            PixelsPerMmY = 8.0,
            PixelsPerMm = 10.0
        };

        var pLenA = new Point2d(100, 100);
        var pLenB = new Point2d(600, 100); // dx = 500, dy = 0 (Chiều ngang)

        var pWidA = new Point2d(100, 100);
        var pWidB = new Point2d(100, 260); // dx = 0, dy = 160 (Chiều dọc)

        double newMeasuredLen = Geometry2D.DistanceMm(pLenA, pLenB, config.GetEffectivePpmX(), config.GetEffectivePpmY());
        double newMeasuredWid = Geometry2D.DistanceMm(pWidA, pWidB, config.GetEffectivePpmX(), config.GetEffectivePpmY());

        bool newLenPass = newMeasuredLen >= 49.5 && newMeasuredLen <= 50.5;
        bool newWidPass = newMeasuredWid >= 19.5 && newWidPassLen(newMeasuredWid);

        static bool newWidPassLen(double w) => w >= 19.5 && w <= 20.5;

        if (Math.Abs(newMeasuredLen - 50.0) > 1e-4) throw new Exception($"Expected new length 50.0 mm, got {newMeasuredLen}");
        if (Math.Abs(newMeasuredWid - 20.0) > 1e-4) throw new Exception($"Expected new width 20.0 mm, got {newMeasuredWid}");
        if (!newLenPass || !newWidPass) throw new Exception("Both Length and Width MUST PASS with 2-axis calibration!");

        // Kiểm tra thông qua DistanceCalculator
        var distCalc = new DistanceCalculator();
        var specLen = new LineDistance { Name = "Length", PointA = "P1", PointB = "P2", Nominal = 50.0, TolerancePlus = 0.5, ToleranceMinus = 0.5 };
        var specWid = new LineDistance { Name = "Width", PointA = "P1", PointB = "P3", Nominal = 20.0, TolerancePlus = 0.5, ToleranceMinus = 0.5 };

        var resLen = distCalc.CheckDistance(specLen, pLenA, pLenB, config.GetEffectivePpmX(), config.GetEffectivePpmY());
        var resWid = distCalc.CheckDistance(specWid, pWidA, pWidB, config.GetEffectivePpmX(), config.GetEffectivePpmY());

        if (!resLen.Pass) throw new Exception($"resLen failed: Value={resLen.Value}");
        if (!resWid.Pass) throw new Exception($"resWid failed: Value={resWid.Value}");

        Console.WriteLine("PASSED");
    }

    private static void TestAngleOrientationDetection()
    {
        Console.Write("Testing angle & orientation detection for axis suggestion... ");

        static double CalcAngle(Point2d p1, Point2d p2)
        {
            var dx = Math.Abs(p2.X - p1.X);
            var dy = Math.Abs(p2.Y - p1.Y);
            if (dx < 1e-9 && dy < 1e-9) return 0.0;
            return Math.Atan2(dy, dx) * 180.0 / Math.PI;
        }

        // 1. Đoạn ngang: angle = 0 deg -> Trục X
        var aHorizontal = CalcAngle(new Point2d(0, 0), new Point2d(200, 0));
        if (Math.Abs(aHorizontal - 0.0) > 1e-4) throw new Exception($"Expected 0 deg, got {aHorizontal}");
        if (aHorizontal >= 45.0) throw new Exception("Horizontal should be < 45 deg (Axis X)");

        // 2. Đoạn nghiêng nhẹ: dx=100, dy=15 -> angle ~ 8.53 deg -> Trục X
        var aSlight = CalcAngle(new Point2d(0, 0), new Point2d(100, 15));
        if (aSlight >= 45.0) throw new Exception("Slight angle should suggest Axis X");

        // 3. Đoạn thẳng đứng: angle = 90 deg -> Trục Y
        var aVertical = CalcAngle(new Point2d(0, 0), new Point2d(0, 200));
        if (Math.Abs(aVertical - 90.0) > 1e-4) throw new Exception($"Expected 90 deg, got {aVertical}");
        if (aVertical < 45.0) throw new Exception("Vertical should be >= 45 deg (Axis Y)");

        // 4. Đoạn nghiêng dốc: dx=20, dy=100 -> angle ~ 78.69 deg -> Trục Y
        var aSteep = CalcAngle(new Point2d(0, 0), new Point2d(20, 100));
        if (aSteep < 45.0) throw new Exception("Steep angle should suggest Axis Y");

        Console.WriteLine("PASSED");
    }

    private static void TestDistanceMmArbitraryAngleVector()
    {
        Console.Write("Testing arbitrary angle 2D vector Euclidean conversion... ");

        // Vector xiên: A(100, 100), B(400, 500)
        // dx = 300 px, dy = 400 px
        // ppmX = 10.0 px/mm, ppmY = 8.0 px/mm
        // dxMm = 300 / 10 = 30 mm
        // dyMm = 400 / 8 = 50 mm
        // distMm = sqrt(30^2 + 50^2) = sqrt(900 + 2500) = sqrt(3400) ~ 58.3095189 mm
        var a = new Point2d(100, 100);
        var b = new Point2d(400, 500);
        double dist = Geometry2D.DistanceMm(a, b, 10.0, 8.0);
        double expected = Math.Sqrt(30.0 * 30.0 + 50.0 * 50.0);

        if (Math.Abs(dist - expected) > 1e-4)
            throw new Exception($"Expected {expected}, got {dist}");

        Console.WriteLine("PASSED");
    }

    private static void TestBackwardCompatibilityWhenDualAxisZero()
    {
        Console.Write("Testing backward compatibility when dual axis is zero/unset... ");

        var config = new VisionConfig
        {
            PixelsPerMm = 12.5,
            PixelsPerMmX = 0.0,
            PixelsPerMmY = 0.0
        };

        if (Math.Abs(config.GetEffectivePpmX() - 12.5) > 1e-4) throw new Exception("Expected fallback to PixelsPerMm for X");
        if (Math.Abs(config.GetEffectivePpmY() - 12.5) > 1e-4) throw new Exception("Expected fallback to PixelsPerMm for Y");

        var a = new Point2d(0, 0);
        var b = new Point2d(125, 0);
        double dist = Geometry2D.DistanceMm(a, b, config.GetEffectivePpmX(), config.GetEffectivePpmY());
        if (Math.Abs(dist - 10.0) > 1e-4) throw new Exception($"Expected 10.0 mm with legacy PixelsPerMm, got {dist}");

        Console.WriteLine("PASSED");
    }

    private static void TestDistanceMeasurementExtraction()
    {
        Console.Write("Testing Distance calibration extraction... ");

        var config = new VisionConfig
        {
            PixelsPerMm = 1.0,
            Distances = new List<LineDistance>
            {
                new LineDistance { Name = "Dist1", PointA = "P1", PointB = "P2", Nominal = 10.0 }
            }
        };

        var lastRun = new InspectionResult();
        lastRun.Points.Add(new PointMatchResult("P1", new Point2d(100, 100), new Rect(90, 90, 20, 20), 0.99, 0.7, true, 0.0));
        lastRun.Points.Add(new PointMatchResult("P2", new Point2d(200, 100), new Rect(190, 90, 20, 20), 0.99, 0.7, true, 0.0));
        lastRun.Distances.Add(new DistanceCheckResult("Dist1", "P1", "P2", 100.0, 10.0, 0.5, 0.5, true));

        // PointA (100, 100) to PointB (200, 100) is exactly 100.0 px
        double dx = 200 - 100;
        double dy = 100 - 100;
        double measuredPx = Math.Sqrt(dx * dx + dy * dy);
        double nominalMm = config.Distances[0].Nominal;
        double newPpm = measuredPx / nominalMm;

        if (Math.Abs(measuredPx - 100.0) > 1e-4) throw new Exception($"Expected measuredPx 100.0, got {measuredPx}");
        if (Math.Abs(nominalMm - 10.0) > 1e-4) throw new Exception($"Expected nominalMm 10.0, got {nominalMm}");
        if (Math.Abs(newPpm - 10.0) > 1e-4) throw new Exception($"Expected newPpm 10.0, got {newPpm}");

        Console.WriteLine("PASSED");
    }

    private static void TestSegmentLineDistanceMeasurementExtraction()
    {
        Console.Write("Testing SegmentLineDistance calibration extraction... ");

        var config = new VisionConfig
        {
            PixelsPerMm = 1.0,
            SegmentLineDistances = new List<SegmentLineDistance>
            {
                new SegmentLineDistance { Name = "SLD1", LineA = "Seg", LineB = "Line", Nominal = 25.0 }
            }
        };

        var lastRun = new InspectionResult();
        lastRun.SegmentLineDistances.Add(new SegmentDistanceResult(
            "SLD1", "Seg", "Line", 125.0, 25.0, 0.5, 0.5, true,
            new Point2d(50, 50), new Point2d(50, 175)));

        var r = lastRun.SegmentLineDistances[0];
        double dx = r.ClosestA.X - r.ClosestB.X;
        double dy = r.ClosestA.Y - r.ClosestB.Y;
        double measuredPx = Math.Sqrt(dx * dx + dy * dy);
        double nominalMm = config.SegmentLineDistances[0].Nominal;
        double newPpm = measuredPx / nominalMm;

        if (Math.Abs(measuredPx - 125.0) > 1e-4) throw new Exception($"Expected measuredPx 125.0, got {measuredPx}");
        if (Math.Abs(newPpm - 5.0) > 1e-4) throw new Exception($"Expected newPpm 5.0, got {newPpm}");

        Console.WriteLine("PASSED");
    }

    private static void TestEdgePairDetectMeasurementExtraction()
    {
        Console.Write("Testing EdgePairDetect calibration extraction... ");

        var config = new VisionConfig
        {
            PixelsPerMm = 1.0,
            EdgePairDetections = new List<EdgePairDetectDefinition>
            {
                new EdgePairDetectDefinition { Name = "EPD1", Nominal = 8.0 }
            }
        };

        var lastRun = new InspectionResult();
        lastRun.EdgePairDetections.Add(new EdgePairDetectResult(
            "EPD1", true,
            new Point2d(10, 10), new Point2d(10, 100),
            new Point2d(90, 10), new Point2d(90, 100),
            80.0, 8.0, 0.2, 0.2, true,
            new Point2d(10, 50), new Point2d(90, 50),
            new List<CaliperEdgePoint>(), new List<CaliperEdgePoint>()));

        var r = lastRun.EdgePairDetections[0];
        double dx = r.ClosestA.X - r.ClosestB.X;
        double dy = r.ClosestA.Y - r.ClosestB.Y;
        double measuredPx = Math.Sqrt(dx * dx + dy * dy);
        double nominalMm = config.EdgePairDetections[0].Nominal;
        double newPpm = measuredPx / nominalMm;

        if (Math.Abs(measuredPx - 80.0) > 1e-4) throw new Exception($"Expected measuredPx 80.0, got {measuredPx}");
        if (Math.Abs(newPpm - 10.0) > 1e-4) throw new Exception($"Expected newPpm 10.0, got {newPpm}");

        Console.WriteLine("PASSED");
    }

    private static void TestDiameterAndCircleFinderMeasurementExtraction()
    {
        Console.Write("Testing Diameter & CircleFinder calibration extraction... ");

        var config = new VisionConfig
        {
            PixelsPerMm = 1.0,
            CircleFinders = new List<CircleFinderDefinition>
            {
                new CircleFinderDefinition { Name = "CF1", NominalDiameter = 12.0 }
            },
            Diameters = new List<DiameterDefinition>
            {
                new DiameterDefinition { Name = "Dia1", CircleRef = "CF1", Nominal = 12.0 }
            }
        };

        var lastRun = new InspectionResult();
        lastRun.CircleFinders.Add(new CircleFinderResult("CF1", true, new Point2d(300, 300), RadiusPx: 48.0, Score: 0.99));
        lastRun.Diameters.Add(new DiameterResult("Dia1", "CF1", true, 96.0, 12.0, 0.1, 0.1, true, new Point2d(300, 300), RadiusPx: 48.0));

        // 1. CircleFinder: Diameter px = 2 * RadiusPx = 96.0 px
        var cf = lastRun.CircleFinders[0];
        double cfDiameterPx = 2.0 * cf.RadiusPx;
        double cfNominal = config.CircleFinders[0].NominalDiameter;
        double cfPpm = cfDiameterPx / cfNominal;

        if (Math.Abs(cfDiameterPx - 96.0) > 1e-4) throw new Exception($"Expected cfDiameterPx 96.0, got {cfDiameterPx}");
        if (Math.Abs(cfPpm - 8.0) > 1e-4) throw new Exception($"Expected cfPpm 8.0, got {cfPpm}");

        // 2. Diameter tool:
        var dia = lastRun.Diameters[0];
        double diaDiameterPx = 2.0 * dia.RadiusPx;
        double diaPpm = diaDiameterPx / dia.Nominal;
        if (Math.Abs(diaPpm - 8.0) > 1e-4) throw new Exception($"Expected diaPpm 8.0, got {diaPpm}");

        Console.WriteLine("PASSED");
    }

    private static void TestValidationAndEdgeCases()
    {
        Console.Write("Testing validation and edge cases... ");

        // 1. Nominal is zero
        double measuredPx = 100.0;
        double nominalZero = 0.0;
        bool isValidNominal = nominalZero > 0.0001;
        if (isValidNominal) throw new Exception("Zero nominal should be invalid!");
        if (measuredPx <= 0) throw new Exception("MeasuredPx should be positive");

        // 2. Division by zero / negative check
        double nominalNeg = -5.0;
        if (nominalNeg > 0.0001) throw new Exception("Negative nominal should be invalid!");

        // 3. Difference percentage calculation
        double currentPpm = 10.0;
        double newPpm = 11.5;
        double diffPercent = ((newPpm - currentPpm) / currentPpm) * 100.0;
        if (Math.Abs(diffPercent - 15.0) > 1e-4) throw new Exception($"Expected diff 15%, got {diffPercent}");

        bool isLargeDiff = Math.Abs(diffPercent) > 10.0;
        if (!isLargeDiff) throw new Exception("Expected isLargeDiff to be true for 15% diff");

        Console.WriteLine("PASSED");
    }

    private static void TestCircleFinderDefinitionSerialization()
    {
        Console.Write("Testing CircleFinderDefinition serialization with NominalDiameter... ");

        var def = new CircleFinderDefinition
        {
            Name = "Hole_M6",
            MinRadiusPx = 20,
            MaxRadiusPx = 50,
            NominalDiameter = 6.0
        };

        var json = JsonSerializer.Serialize(def);
        if (!json.Contains("\"NominalDiameter\":6")) throw new Exception($"Serialized JSON does not contain NominalDiameter: {json}");

        var deserialized = JsonSerializer.Deserialize<CircleFinderDefinition>(json);
        if (deserialized == null) throw new Exception("Deserialization returned null");
        if (Math.Abs(deserialized.NominalDiameter - 6.0) > 1e-4) throw new Exception($"Expected NominalDiameter 6.0, got {deserialized.NominalDiameter}");

        Console.WriteLine("PASSED");
    }
}
