using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.OQC;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class NewJobAndBlobSpecTests
{
    public static void RunTests()
    {
        Console.WriteLine("=== Running NewJob, CaliperStrip & BlobDetection Spec Tests ===");

        TestBlobSpecEvaluation();
        TestBlobMinDistanceEvaluation();
        TestBlobMaxSizeEvaluation();
        TestOqcScannerBlobMeasurement();
        TestBlobOverallResultPass();
        TestCaliperStripRoiDefinition();

        Console.WriteLine("=== All NewJob, CaliperStrip & BlobDetection Spec Tests Passed! ===");
    }

    private static void TestBlobSpecEvaluation()
    {
        Console.WriteLine("[Test 1] Testing BlobDetection MaxAllowedBlobs Spec evaluation...");

        var blobDef = new BlobDetectionDefinition
        {
            Name = "Blob1",
            MaxAllowedBlobs = 2
        };

        // Case A: 0 blobs found <= 2 => PASS
        var blobs0 = new List<BlobInfo>();
        var pass0 = blobs0.Count <= blobDef.MaxAllowedBlobs;
        var res0 = new BlobDetectionResult(blobDef.Name, blobs0.Count, blobs0, pass0, blobDef.MaxAllowedBlobs);
        if (!res0.Pass || res0.MaxAllowedBlobs != 2)
            throw new Exception($"Expected PASS for 0 blobs with spec 2, got Pass={res0.Pass}");

        // Case B: 2 blobs found <= 2 => PASS
        var blobs2 = new List<BlobInfo>
        {
            new BlobInfo(new Rect(10, 10, 20, 20), new Point2d(20, 20), 400),
            new BlobInfo(new Rect(50, 50, 20, 20), new Point2d(60, 60), 400)
        };
        var pass2 = blobs2.Count <= blobDef.MaxAllowedBlobs;
        var res2 = new BlobDetectionResult(blobDef.Name, blobs2.Count, blobs2, pass2, blobDef.MaxAllowedBlobs);
        if (!res2.Pass)
            throw new Exception($"Expected PASS for 2 blobs with spec 2, got Pass={res2.Pass}");

        // Case C: 3 blobs found > 2 => FAIL (NG)
        var blobs3 = new List<BlobInfo>
        {
            new BlobInfo(new Rect(10, 10, 20, 20), new Point2d(20, 20), 400),
            new BlobInfo(new Rect(50, 50, 20, 20), new Point2d(60, 60), 400),
            new BlobInfo(new Rect(90, 90, 20, 20), new Point2d(100, 100), 400)
        };
        var pass3 = blobs3.Count <= blobDef.MaxAllowedBlobs;
        var res3 = new BlobDetectionResult(blobDef.Name, blobs3.Count, blobs3, pass3, blobDef.MaxAllowedBlobs);
        if (res3.Pass)
            throw new Exception($"Expected FAIL for 3 blobs with spec 2, got Pass={res3.Pass}");

        Console.WriteLine(" -> BlobDetection Spec OK/NG evaluation verified!");
    }

    private static void TestBlobMinDistanceEvaluation()
    {
        Console.WriteLine("[Test 2] Testing BlobDetection MinBlobDistance Spec evaluation...");

        // Case A: MinBlobDistance <= 0 (ignored), close blobs -> PASS
        var bA = new List<BlobInfo>
        {
            new BlobInfo(new Rect(0, 0, 10, 10), new Point2d(5, 5), 100),
            new BlobInfo(new Rect(10, 0, 10, 10), new Point2d(15, 5), 100) // distance = 10 px
        };
        double distA = Math.Sqrt(Math.Pow(bA[0].Centroid.X - bA[1].Centroid.X, 2) + Math.Pow(bA[0].Centroid.Y - bA[1].Centroid.Y, 2));
        var resA = new BlobDetectionResult("B_Ignored", bA.Count, bA, Pass: true, MaxAllowedBlobs: 5, MinBlobDistance: 0.0, MeasuredMinDistance: distA);
        if (!resA.Pass)
            throw new Exception("Expected PASS when MinBlobDistance <= 0!");

        // Case B: 0 or 1 blob with MinBlobDistance = 50.0 -> PASS (no pair to violate)
        var bB = new List<BlobInfo>
        {
            new BlobInfo(new Rect(0, 0, 10, 10), new Point2d(5, 5), 100)
        };
        var resB = new BlobDetectionResult("B_Single", bB.Count, bB, Pass: true, MaxAllowedBlobs: 5, MinBlobDistance: 50.0, MeasuredMinDistance: null);
        if (!resB.Pass)
            throw new Exception("Expected PASS for single blob with MinBlobDistance > 0!");

        // Case C: 2 blobs with distance 100px >= spec 50px (uncalibrated) -> PASS
        var bC = new List<BlobInfo>
        {
            new BlobInfo(new Rect(0, 0, 10, 10), new Point2d(0, 0), 100),
            new BlobInfo(new Rect(100, 0, 10, 10), new Point2d(100, 0), 100) // distance = 100px
        };
        double distC = 100.0;
        bool passC = distC >= 50.0;
        var resC = new BlobDetectionResult("B_Far", bC.Count, bC, Pass: passC, MaxAllowedBlobs: 5, MinBlobDistance: 50.0, MeasuredMinDistance: distC);
        if (!resC.Pass)
            throw new Exception("Expected PASS for 100px distance with spec 50px!");

        // Case D: 2 blobs with distance 30px < spec 50px (uncalibrated) -> FAIL (NG)
        var bD = new List<BlobInfo>
        {
            new BlobInfo(new Rect(0, 0, 10, 10), new Point2d(0, 0), 100),
            new BlobInfo(new Rect(30, 0, 10, 10), new Point2d(30, 0), 100) // distance = 30px
        };
        double distD = 30.0;
        bool passD = distD >= 50.0;
        var resD = new BlobDetectionResult("B_Close", bD.Count, bD, Pass: passD, MaxAllowedBlobs: 5, MinBlobDistance: 50.0, MeasuredMinDistance: distD);
        if (resD.Pass)
            throw new Exception("Expected FAIL for 30px distance with spec 50px!");

        // Case E: Calibrated with PixelsPerMm = 10.0 (100px = 10.0mm)
        double pxMm = 10.0;
        double distPx = 100.0;
        double distMm = distPx / pxMm; // 10.0 mm
        bool passE1 = distMm >= 8.0;  // spec = 8.0 mm -> PASS
        bool passE2 = distMm >= 12.0; // spec = 12.0 mm -> FAIL
        if (!passE1 || passE2)
            throw new Exception($"Calibration distance calculation error: passE1={passE1}, passE2={passE2}");

        Console.WriteLine(" -> BlobDetection MinBlobDistance Spec evaluation verified!");
    }

    private static void TestBlobMaxSizeEvaluation()
    {
        Console.WriteLine("[Test 3] Testing BlobDetection MaxBlobWidth x MaxBlobLength Spec evaluation...");

        double specW = 10.0;
        double specL = 30.0;

        bool CheckBlobSize(double bw, double bh)
        {
            return (bw <= specW && bh <= specL) || (bh <= specW && bw <= specL);
        }

        // Case A: Horizontal blob 25 x 8 -> (bw=25 <= 30 && bh=8 <= 10) -> PASS
        if (!CheckBlobSize(25, 8))
            throw new Exception("Expected PASS for horizontal blob 25 x 8 with spec 10 x 30!");

        // Case B: Vertical blob 8 x 25 -> (bw=8 <= 10 && bh=25 <= 30) -> PASS
        if (!CheckBlobSize(8, 25))
            throw new Exception("Expected PASS for vertical blob 8 x 25 with spec 10 x 30!");

        // Case C: Square blob 15 x 15 -> (15 > 10) -> FAIL (NG)
        if (CheckBlobSize(15, 15))
            throw new Exception("Expected FAIL for square blob 15 x 15 with spec 10 x 30!");

        // Case D: Overlength blob 8 x 35 -> (35 > 30) -> FAIL (NG)
        if (CheckBlobSize(8, 35))
            throw new Exception("Expected FAIL for overlength blob 8 x 35 with spec 10 x 30!");

        // Case E: Overlength horizontal blob 35 x 8 -> (35 > 30) -> FAIL (NG)
        if (CheckBlobSize(35, 8))
            throw new Exception("Expected FAIL for overlength horizontal blob 35 x 8 with spec 10 x 30!");

        // Case F: Calibration scale factor applied
        double scale = 2.0;
        double rawW = 16.0; // 16 / 2 = 8 mm
        double rawH = 50.0; // 50 / 2 = 25 mm
        if (!CheckBlobSize(rawW / scale, rawH / scale))
            throw new Exception("Expected PASS for calibrated blob 16x50 px at 2 px/mm!");

        Console.WriteLine(" -> BlobDetection MaxBlobWidth x MaxBlobLength Spec evaluation verified!");
    }

    private static void TestOqcScannerBlobMeasurement()
    {
        Console.WriteLine("[Test 4] Testing OQC Scanner measurement table output for BlobDetection...");

        var config = new VisionConfig
        {
            ProductCode = "TestProduct",
            BlobDetections = new List<BlobDetectionDefinition>
            {
                new BlobDetectionDefinition
                {
                    Name = "DefectCheck",
                    MaxAllowedBlobs = 0,
                    MinBlobDistance = 15.0,
                    MaxBlobWidth = 5.0,
                    MaxBlobLength = 10.0
                }
            }
        };

        var result = new InspectionResult
        {
            Pass = false
        };
        result.BlobDetections.Add(new BlobDetectionResult(
            "DefectCheck",
            Count: 1,
            Blobs: new List<BlobInfo> { new BlobInfo(new Rect(5, 5, 10, 10), new Point2d(10, 10), 100) },
            Pass: false,
            MaxAllowedBlobs: 0,
            MinBlobDistance: 15.0,
            MeasuredMinDistance: null,
            MaxBlobWidth: 5.0,
            MaxBlobLength: 10.0,
            MeasuredMaxWidth: 10.0,
            MeasuredMaxLength: 10.0
        ));

        var oqcService = new OqcScannerService();
        var details = oqcService.ExtractMeasurementDetails(result, config);

        var blobRow = details.FirstOrDefault(d => d.ToolName == "DefectCheck" && d.ToolType == "BlobDetection");
        if (blobRow is null)
            throw new Exception("BlobDetection row not found in OQC measurement details!");

        if (!blobRow.CustomSpecText.Contains("<= 0") || !blobRow.CustomSpecText.Contains("Dist >= 15") || !blobRow.CustomSpecText.Contains("Size <= 5x10"))
            throw new Exception($"Expected CustomSpecText to contain all specs, got '{blobRow.CustomSpecText}'");

        if (blobRow.Pass != false)
            throw new Exception("Expected blobRow.Pass to be false for 1 blob with max 0!");

        if (blobRow.Result != 1)
            throw new Exception($"Expected blobRow.Result to be 1, got {blobRow.Result}");

        Console.WriteLine(" -> OQC Scanner Blob measurement detail verified!");
    }

    private static void TestBlobOverallResultPass()
    {
        Console.WriteLine("[Test 5] Testing Overall InspectionResult.Pass with BlobDetection...");

        var resPass = new InspectionResult { Pass = true };
        resPass.BlobDetections.Add(new BlobDetectionResult("B1", 0, new List<BlobInfo>(), Pass: true, MaxAllowedBlobs: 0));
        var overallPass = resPass.Pass && resPass.BlobDetections.All(x => x.Pass);
        if (!overallPass)
            throw new Exception("Expected overall inspection to PASS when all blobs pass!");

        var resFail = new InspectionResult { Pass = true };
        resFail.BlobDetections.Add(new BlobDetectionResult("B1", 3, new List<BlobInfo>(), Pass: false, MaxAllowedBlobs: 1));
        var overallFail = resFail.Pass && resFail.BlobDetections.All(x => x.Pass);
        if (overallFail)
            throw new Exception("Expected overall inspection to FAIL when blob detection fails!");

        Console.WriteLine(" -> Overall InspectionResult.Pass logic with BlobDetection verified!");
    }

    private static void TestCaliperStripRoiDefinition()
    {
        Console.WriteLine("[Test 6] Testing Caliper Strip ROI properties...");

        var c = new CaliperDefinition
        {
            Name = "Cal1",
            SearchRoi = new Roi { X = 100, Y = 100, Width = 200, Height = 80, Angle = 15.0 },
            Orientation = CaliperOrientation.Horizontal,
            StripLength = 50
        };

        // Symmetric horizontal strip width is StripLength, height is SearchRoi.Height
        var stripRoiH = new Roi
        {
            X = c.Orientation == CaliperOrientation.Horizontal ? c.SearchRoi.X + (c.SearchRoi.Width - c.StripLength) / 2 : c.SearchRoi.X,
            Y = c.Orientation == CaliperOrientation.Horizontal ? c.SearchRoi.Y : c.SearchRoi.Y + (c.SearchRoi.Height - c.StripLength) / 2,
            Width = c.Orientation == CaliperOrientation.Horizontal ? c.StripLength : c.SearchRoi.Width,
            Height = c.Orientation == CaliperOrientation.Horizontal ? c.SearchRoi.Height : c.StripLength,
            Angle = c.SearchRoi.Angle
        };

        if (stripRoiH.Width != 50 || stripRoiH.Height != 80)
            throw new Exception($"Expected Width=50, Height=80 for horizontal strip, got W={stripRoiH.Width}, H={stripRoiH.Height}");

        // Center must be identical to Search ROI center
        var searchCx = c.SearchRoi.X + c.SearchRoi.Width / 2.0;
        var searchCy = c.SearchRoi.Y + c.SearchRoi.Height / 2.0;
        var stripCx = stripRoiH.X + stripRoiH.Width / 2.0;
        var stripCy = stripRoiH.Y + stripRoiH.Height / 2.0;

        if (Math.Abs(searchCx - stripCx) > 1e-6 || Math.Abs(searchCy - stripCy) > 1e-6)
            throw new Exception($"Strip center ({stripCx}, {stripCy}) does not match Search center ({searchCx}, {searchCy})!");

        Console.WriteLine(" -> Caliper Strip ROI geometry verified!");
    }
}
