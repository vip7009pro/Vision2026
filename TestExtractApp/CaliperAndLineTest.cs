using System;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class CaliperAndLineTest
{
    public static void RunTests()
    {
        Console.WriteLine("=== Testing Caliper and Line Detection ===");

        // Create an image with a clear horizontal edge / line at Y = 100
        using var img = new Mat(300, 400, MatType.CV_8UC1, Scalar.All(50));
        // Fill lower half with 200 (edge at Y = 100)
        img[new Rect(0, 100, 400, 200)].SetTo(new Scalar(200));

        // Test 1: Caliper on horizontal edge (Orientation = Vertical because strips cross the edge vertically)
        var calDef = new CaliperDefinition
        {
            Name = "Cal1",
            SearchRoi = new Roi { X = 50, Y = 50, Width = 300, Height = 100, Angle = 0 },
            Orientation = CaliperOrientation.Vertical,
            Polarity = EdgePolarity.DarkToLight,
            StripCount = 10,
            StripWidth = 7,
            StripLength = 60,
            MinEdgeStrength = 10.0
        };

        var calRes = CaliperDetector.Detect(img, calDef);
        Console.WriteLine($"[Caliper Test 1 (Vertical strips on horizontal edge)] Found={calRes.Found}, Points={calRes.Points.Count}, AvgStrength={calRes.AvgStrength:F2}, P1=({calRes.LineP1.X:F1}, {calRes.LineP1.Y:F1}), P2=({calRes.LineP2.X:F1}, {calRes.LineP2.Y:F1})");
        foreach (var p in calRes.Points)
        {
            Console.WriteLine($"   Pt: ({p.X:F2}, {p.Y:F2}) Str={p.Strength:F2}");
        }

        // Test 2: Caliper with Polarity = Any
        calDef.Polarity = EdgePolarity.Any;
        var calRes2 = CaliperDetector.Detect(img, calDef);
        Console.WriteLine($"[Caliper Test 2 (Polarity Any)] Found={calRes2.Found}, Points={calRes2.Points.Count}");

        // Test 3: Caliper with Orientation = Horizontal (strips running horizontally across vertical edge)
        using var imgVert = new Mat(300, 400, MatType.CV_8UC1, Scalar.All(50));
        imgVert[new Rect(200, 0, 200, 300)].SetTo(new Scalar(200)); // edge at X = 200

        var calDefHoriz = new CaliperDefinition
        {
            Name = "Cal2",
            SearchRoi = new Roi { X = 150, Y = 50, Width = 100, Height = 200, Angle = 0 },
            Orientation = CaliperOrientation.Horizontal,
            Polarity = EdgePolarity.DarkToLight,
            StripCount = 10,
            StripWidth = 7,
            StripLength = 60,
            MinEdgeStrength = 10.0
        };
        var calRes3 = CaliperDetector.Detect(imgVert, calDefHoriz);
        Console.WriteLine($"[Caliper Test 3 (Horizontal strips on vertical edge)] Found={calRes3.Found}, Points={calRes3.Points.Count}, P1=({calRes3.LineP1.X:F1}, {calRes3.LineP1.Y:F1}), P2=({calRes3.LineP2.X:F1}, {calRes3.LineP2.Y:F1})");

        // Test 4: Line Detector on the same image
        var lineDet = new LineDetector();
        var lineRes = lineDet.DetectLongestLine(img, new Roi { X = 50, Y = 50, Width = 300, Height = 100, Angle = 0 }, canny1: 50, canny2: 150, houghThreshold: 50, minLineLength: 30, maxLineGap: 10);
        Console.WriteLine($"[LineDetector Test 1] Found={lineRes.Found}, P1=({lineRes.P1.X:F1}, {lineRes.P1.Y:F1}), P2=({lineRes.P2.X:F1}, {lineRes.P2.Y:F1}), Length={lineRes.LengthPx:F1}");

        // Test 5: Rotated Caliper ROI (Angle = 30 deg) on an image rotated by 30 deg
        using var imgRot = new Mat(400, 400, MatType.CV_8UC1, Scalar.All(50));
        // Draw a line passing through (200, 200) rotated by 30 deg clockwise
        for (int y = 0; y < 400; y++)
        {
            for (int x = 0; x < 400; x++)
            {
                var rad = 30.0 * Math.PI / 180.0;
                // rotated Y coordinate relative to center (200, 200)
                var yr = -(x - 200) * Math.Sin(rad) + (y - 200) * Math.Cos(rad);
                if (yr > 0) imgRot.Set(y, x, (byte)200);
            }
        }
        var calDefRot = new CaliperDefinition
        {
            Name = "CalRot",
            SearchRoi = new Roi { X = 100, Y = 150, Width = 200, Height = 100, Angle = 30 },
            Orientation = CaliperOrientation.Vertical,
            Polarity = EdgePolarity.DarkToLight,
            StripCount = 10,
            StripWidth = 7,
            StripLength = 60,
            MinEdgeStrength = 10.0
        };
        var calResRot = CaliperDetector.Detect(imgRot, calDefRot);
        Console.WriteLine($"[Caliper Test 5 (Rotated ROI 30 deg)] Found={calResRot.Found}, Points={calResRot.Points.Count}, P1=({calResRot.LineP1.X:F1}, {calResRot.LineP1.Y:F1}), P2=({calResRot.LineP2.X:F1}, {calResRot.LineP2.Y:F1})");
        // Test 6: HORIZONTAL straight edge at Y = 150 on original image, with ROTATED ROI (Angle = -5 deg)
        using var imgHorizEdge = new Mat(300, 400, MatType.CV_8UC1, Scalar.All(50));
        imgHorizEdge[new Rect(0, 150, 400, 150)].SetTo(new Scalar(200)); // Perfect horizontal edge at Y = 150

        var calDefTilted = new CaliperDefinition
        {
            Name = "CalTilted",
            SearchRoi = new Roi { X = 100, Y = 100, Width = 200, Height = 100, Angle = -5.0 },
            Orientation = CaliperOrientation.Vertical,
            Polarity = EdgePolarity.DarkToLight,
            StripCount = 10,
            StripWidth = 7,
            StripLength = 60,
            MinEdgeStrength = 10.0
        };

        var resTilted = CaliperDetector.Detect(imgHorizEdge, calDefTilted);
        Console.WriteLine($"[Caliper Test 6 (Horizontal Edge Y=150, Tilted ROI -5 deg)] Found={resTilted.Found}, Points={resTilted.Points.Count}, P1=({resTilted.LineP1.X:F1}, {resTilted.LineP1.Y:F1}), P2=({resTilted.LineP2.X:F1}, {resTilted.LineP2.Y:F1})");
        foreach (var p in resTilted.Points)
        {
            Console.WriteLine($"   Pt: ({p.X:F2}, {p.Y:F2}) -> Error from Y=150: {p.Y - 150.0:F3}px");
        }

        // Test 7: Line Detector on Horizontal Edge with Tilted ROI (-5 deg)
        var lineResTilted = lineDet.DetectLongestLine(imgHorizEdge, calDefTilted.SearchRoi, canny1: 50, canny2: 150, houghThreshold: 30, minLineLength: 20, maxLineGap: 10);
        Console.WriteLine($"[LineDetector Test 7 (Horizontal Edge Y=150, Tilted ROI -5 deg)] Found={lineResTilted.Found}, P1=({lineResTilted.P1.X:F1}, {lineResTilted.P1.Y:F1}), P2=({lineResTilted.P2.X:F1}, {lineResTilted.P2.Y:F1})");

        // Test 8: SegmentLineDistance calculation
        var la = new LineDetectResult("LineA", new Point2d(50, 100), new Point2d(150, 100), 100.0, Found: true);
        var lb = new LineDetectResult("LineB", new Point2d(200, 250), new Point2d(350, 250), 150.0, Found: true);
        var (dist, ca, cb) = Geometry2D.CalculateSegmentLineDistance(la, lb, SegmentLineDistanceMode.ClosestPointOnSegmentToInfiniteLine, SegmentLineExtensionMode.ActualDetectedSegment, null, default, default, 0.0);
        Console.WriteLine($"[SegmentLineDistance Test 8] Dist={dist:F2}px (Expected: 150.00px), ClosestA=({ca.X:F1}, {ca.Y:F1}), ClosestB=({cb.X:F1}, {cb.Y:F1})");
        if (Math.Abs(dist - 150.0) < 1e-3)
        {
            Console.WriteLine("[PASS] SegmentLineDistance accuracy verified.");
        }
        else
        {
            throw new Exception($"SegmentLineDistance accuracy failed: expected 150.0, got {dist}");
        }
    }
}
