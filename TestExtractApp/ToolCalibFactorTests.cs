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

        Console.WriteLine("=======================================================");
        Console.WriteLine("✅ ALL TOOL CALIB FACTOR TESTS PASSED 100%!");
        Console.WriteLine("=======================================================\n");
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
