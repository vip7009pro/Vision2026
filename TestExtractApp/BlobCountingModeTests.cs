using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class BlobCountingModeTests
{
    private static IInspectionService CreateInspectionService()
    {
        var pre = new ImagePreprocessor();
        var matcher = new PatternMatcher();
        var dist = new DistanceCalculator();
        var line = new LineDetector();
        var defect = new DefectDetector();
        return new InspectionService(pre, matcher, dist, line, defect);
    }

    public static void RunTests()
    {
        Console.WriteLine("=== Running BlobCountingMode (Separate vs ExcludeContained) Tests ===");

        Test_SeparateMode_CountsBothOuterAndInnerBlob();
        Test_ExcludeContainedMode_EliminatesInnerBlob();
        Test_ThreeLevelNesting_KeepsOnlyOutermostBlob();
        Test_TwoSeparateBlobs_BothKeptInBothModes();
        Test_OuterBlobWithInnerBlob_AndOneIndependentBlob();
        Test_SerializationAndBackwardCompatibility();

        Console.WriteLine("=== All BlobCountingMode Tests Passed Successfully! ===");
    }

    private static void Test_SeparateMode_CountsBothOuterAndInnerBlob()
    {
        Console.WriteLine("[Test 1] Testing Separate Mode (counts both outer and inner blob)...");

        // Tạo ảnh nhị phân: Nền đen (0), vành tròn trắng bên ngoài và chấm tròn trắng bên trong
        using var img = new Mat(300, 300, MatType.CV_8UC3, Scalar.Black);
        // Vành ngoài: Bán kính 80, độ dày 20 (bán kính ngoài 90, bán kính trong 70)
        Cv2.Circle(img, new Point(150, 150), 80, Scalar.White, thickness: 20);
        // Chấm con trong: Bán kính 15, đặc
        Cv2.Circle(img, new Point(150, 150), 15, Scalar.White, thickness: -1);

        var config = new VisionConfig
        {
            Origin = new PointDefinition { Name = "Origin" },
            BlobDetections = new List<BlobDetectionDefinition>
            {
                new BlobDetectionDefinition
                {
                    Name = "Blob1",
                    InspectRoi = new Roi { X = 10, Y = 10, Width = 280, Height = 280 },
                    Polarity = BlobPolarity.LightOnDark,
                    Threshold = 100,
                    MinBlobArea = 50,
                    MaxBlobArea = 50000,
                    CountingMode = BlobCountingMode.Separate
                }
            }
        };

        var service = CreateInspectionService();
        var result = service.Inspect(img, config);

        var blobRes = result.BlobDetections.FirstOrDefault(b => b.Name == "Blob1");
        if (blobRes is null)
            throw new Exception("Blob1 result not found!");

        if (blobRes.Count != 2)
            throw new Exception($"Expected 2 blobs in Separate mode, but got {blobRes.Count}!");

        Console.WriteLine($" -> Separate mode successfully detected {blobRes.Count} separate blobs (Outer ring + Inner circle).");
    }

    private static void Test_ExcludeContainedMode_EliminatesInnerBlob()
    {
        Console.WriteLine("[Test 2] Testing ExcludeContained Mode (eliminates contained/nested blob)...");

        // Dùng cùng cấu trúc ảnh: Vành tròn trắng ngoài và chấm tròn trắng trong
        using var img = new Mat(300, 300, MatType.CV_8UC3, Scalar.Black);
        Cv2.Circle(img, new Point(150, 150), 80, Scalar.White, thickness: 20);
        Cv2.Circle(img, new Point(150, 150), 15, Scalar.White, thickness: -1);

        var config = new VisionConfig
        {
            Origin = new PointDefinition { Name = "Origin" },
            BlobDetections = new List<BlobDetectionDefinition>
            {
                new BlobDetectionDefinition
                {
                    Name = "Blob1",
                    InspectRoi = new Roi { X = 10, Y = 10, Width = 280, Height = 280 },
                    Polarity = BlobPolarity.LightOnDark,
                    Threshold = 100,
                    MinBlobArea = 50,
                    MaxBlobArea = 50000,
                    CountingMode = BlobCountingMode.ExcludeContained
                }
            }
        };

        var service = CreateInspectionService();
        var result = service.Inspect(img, config);

        var blobRes = result.BlobDetections.FirstOrDefault(b => b.Name == "Blob1");
        if (blobRes is null)
            throw new Exception("Blob1 result not found!");

        if (blobRes.Count != 1)
            throw new Exception($"Expected 1 blob in ExcludeContained mode, but got {blobRes.Count}!");

        var remainingBlob = blobRes.Blobs[0];
        // Bounding box của blob to phải bao trùm phạm vi bán kính 80 (khoảng 140-180 px width/height)
        if (remainingBlob.BoundingBox.Width < 100 || remainingBlob.BoundingBox.Height < 100)
            throw new Exception($"Remaining blob bounding box too small ({remainingBlob.BoundingBox.Width}x{remainingBlob.BoundingBox.Height}), expected outer ring!");

        Console.WriteLine($" -> ExcludeContained mode successfully eliminated inner blob, kept only 1 outer blob (Area={remainingBlob.Area}).");
    }

    private static void Test_ThreeLevelNesting_KeepsOnlyOutermostBlob()
    {
        Console.WriteLine("[Test 3] Testing 3-level nested blobs (A contains B, B contains C)...");

        using var img = new Mat(400, 400, MatType.CV_8UC3, Scalar.Black);
        // Vòng A ngoài cùng (bán kính 120, độ dày 15)
        Cv2.Circle(img, new Point(200, 200), 120, Scalar.White, thickness: 15);
        // Vòng B ở giữa (bán kính 60, độ dày 15)
        Cv2.Circle(img, new Point(200, 200), 60, Scalar.White, thickness: 15);
        // Chấm C trong cùng (bán kính 12, đặc)
        Cv2.Circle(img, new Point(200, 200), 12, Scalar.White, thickness: -1);

        var configSeparate = new VisionConfig
        {
            Origin = new PointDefinition { Name = "Origin" },
            BlobDetections = new List<BlobDetectionDefinition>
            {
                new BlobDetectionDefinition
                {
                    Name = "BlobSeparate",
                    InspectRoi = new Roi { X = 10, Y = 10, Width = 380, Height = 380 },
                    Polarity = BlobPolarity.LightOnDark,
                    Threshold = 100,
                    MinBlobArea = 30,
                    MaxBlobArea = 50000,
                    CountingMode = BlobCountingMode.Separate
                }
            }
        };

        var configExclude = new VisionConfig
        {
            Origin = new PointDefinition { Name = "Origin" },
            BlobDetections = new List<BlobDetectionDefinition>
            {
                new BlobDetectionDefinition
                {
                    Name = "BlobExclude",
                    InspectRoi = new Roi { X = 10, Y = 10, Width = 380, Height = 380 },
                    Polarity = BlobPolarity.LightOnDark,
                    Threshold = 100,
                    MinBlobArea = 30,
                    MaxBlobArea = 50000,
                    CountingMode = BlobCountingMode.ExcludeContained
                }
            }
        };

        var service = CreateInspectionService();
        var resSeparate = service.Inspect(img, configSeparate).BlobDetections.First();
        var resExclude = service.Inspect(img, configExclude).BlobDetections.First();

        if (resSeparate.Count != 3)
            throw new Exception($"Expected 3 blobs in Separate mode for 3-level nesting, got {resSeparate.Count}!");

        if (resExclude.Count != 1)
            throw new Exception($"Expected 1 blob in ExcludeContained mode for 3-level nesting, got {resExclude.Count}!");

        Console.WriteLine($" -> 3-level nesting verified: Separate=3 blobs, ExcludeContained=1 blob.");
    }

    private static void Test_TwoSeparateBlobs_BothKeptInBothModes()
    {
        Console.WriteLine("[Test 4] Testing 2 independent separate blobs (none contained)...");

        using var img = new Mat(300, 300, MatType.CV_8UC3, Scalar.Black);
        // Blob 1 bên trái
        Cv2.Rectangle(img, new Rect(30, 100, 60, 60), Scalar.White, thickness: -1);
        // Blob 2 bên phải độc lập
        Cv2.Rectangle(img, new Rect(180, 100, 50, 50), Scalar.White, thickness: -1);

        var configSeparate = new VisionConfig
        {
            Origin = new PointDefinition { Name = "Origin" },
            BlobDetections = new List<BlobDetectionDefinition>
            {
                new BlobDetectionDefinition
                {
                    Name = "BlobSep",
                    InspectRoi = new Roi { X = 10, Y = 10, Width = 280, Height = 280 },
                    Polarity = BlobPolarity.LightOnDark,
                    Threshold = 100,
                    MinBlobArea = 50,
                    MaxBlobArea = 50000,
                    CountingMode = BlobCountingMode.Separate
                }
            }
        };

        var configExclude = new VisionConfig
        {
            Origin = new PointDefinition { Name = "Origin" },
            BlobDetections = new List<BlobDetectionDefinition>
            {
                new BlobDetectionDefinition
                {
                    Name = "BlobEx",
                    InspectRoi = new Roi { X = 10, Y = 10, Width = 280, Height = 280 },
                    Polarity = BlobPolarity.LightOnDark,
                    Threshold = 100,
                    MinBlobArea = 50,
                    MaxBlobArea = 50000,
                    CountingMode = BlobCountingMode.ExcludeContained
                }
            }
        };

        var service = CreateInspectionService();
        var resSep = service.Inspect(img, configSeparate).BlobDetections.First();
        var resEx = service.Inspect(img, configExclude).BlobDetections.First();

        if (resSep.Count != 2)
            throw new Exception($"Expected 2 blobs in Separate mode, got {resSep.Count}!");

        if (resEx.Count != 2)
            throw new Exception($"Expected 2 blobs in ExcludeContained mode, got {resEx.Count}!");

        Console.WriteLine($" -> 2 independent blobs verified: both modes correctly keep 2 blobs.");
    }

    private static void Test_OuterBlobWithInnerBlob_AndOneIndependentBlob()
    {
        Console.WriteLine("[Test 5] Testing 1 outer+inner pair AND 1 independent outside blob...");

        using var img = new Mat(300, 400, MatType.CV_8UC3, Scalar.Black);
        // Cặp lồng nhau bên trái: Vành ngoài tâm (100, 150) bán kính 70, chấm trong tâm (100, 150) bán kính 15
        Cv2.Circle(img, new Point(100, 150), 70, Scalar.White, thickness: 15);
        Cv2.Circle(img, new Point(100, 150), 15, Scalar.White, thickness: -1);
        // Blob độc lập bên phải: hình vuông tại (260, 110, 60, 60)
        Cv2.Rectangle(img, new Rect(260, 110, 60, 60), Scalar.White, thickness: -1);

        var configSep = new VisionConfig
        {
            Origin = new PointDefinition { Name = "Origin" },
            BlobDetections = new List<BlobDetectionDefinition>
            {
                new BlobDetectionDefinition
                {
                    Name = "Blob1",
                    InspectRoi = new Roi { X = 10, Y = 10, Width = 380, Height = 280 },
                    Polarity = BlobPolarity.LightOnDark,
                    Threshold = 100,
                    MinBlobArea = 30,
                    MaxBlobArea = 50000,
                    CountingMode = BlobCountingMode.Separate
                }
            }
        };

        var configEx = new VisionConfig
        {
            Origin = new PointDefinition { Name = "Origin" },
            BlobDetections = new List<BlobDetectionDefinition>
            {
                new BlobDetectionDefinition
                {
                    Name = "Blob1",
                    InspectRoi = new Roi { X = 10, Y = 10, Width = 380, Height = 280 },
                    Polarity = BlobPolarity.LightOnDark,
                    Threshold = 100,
                    MinBlobArea = 30,
                    MaxBlobArea = 50000,
                    CountingMode = BlobCountingMode.ExcludeContained
                }
            }
        };

        var service = CreateInspectionService();
        var resSep = service.Inspect(img, configSep).BlobDetections.First();
        var resEx = service.Inspect(img, configEx).BlobDetections.First();

        if (resSep.Count != 3)
            throw new Exception($"Expected 3 blobs in Separate mode, got {resSep.Count}!");

        if (resEx.Count != 2)
            throw new Exception($"Expected 2 blobs in ExcludeContained mode (outer ring + independent blob), got {resEx.Count}!");

        Console.WriteLine($" -> Outer+inner + independent blob verified: Separate=3, ExcludeContained=2.");
    }

    private static void Test_SerializationAndBackwardCompatibility()
    {
        Console.WriteLine("[Test 6] Testing JSON serialization and backward compatibility...");

        // Case 1: Legacy JSON without CountingMode property
        var legacyJson = "{\"Name\":\"LegacyBlob\",\"InspectRoi\":{\"X\":10,\"Y\":10,\"Width\":100,\"Height\":100},\"Threshold\":128}";
        var defLegacy = JsonSerializer.Deserialize<BlobDetectionDefinition>(legacyJson);
        if (defLegacy is null)
            throw new Exception("Failed to deserialize legacy JSON!");

        if (defLegacy.CountingMode != BlobCountingMode.Separate || defLegacy.FilterContainedBlobs != false)
            throw new Exception($"Legacy default expected Separate (false), but got {defLegacy.CountingMode} ({defLegacy.FilterContainedBlobs})!");

        // Case 2: New config with ExcludeContained
        var defNew = new BlobDetectionDefinition
        {
            Name = "NewBlob",
            CountingMode = BlobCountingMode.ExcludeContained
        };
        var json = JsonSerializer.Serialize(defNew);
        var deserialized = JsonSerializer.Deserialize<BlobDetectionDefinition>(json);
        if (deserialized is null)
            throw new Exception("Failed to roundtrip serialize/deserialize!");

        if (deserialized.CountingMode != BlobCountingMode.ExcludeContained || !deserialized.FilterContainedBlobs)
            throw new Exception($"Roundtrip failed: expected ExcludeContained, got {deserialized.CountingMode}!");

        // Case 3: Toggle via FilterContainedBlobs boolean helper
        defNew.FilterContainedBlobs = false;
        if (defNew.CountingMode != BlobCountingMode.Separate)
            throw new Exception("Setting FilterContainedBlobs = false should set CountingMode = Separate!");

        defNew.FilterContainedBlobs = true;
        if (defNew.CountingMode != BlobCountingMode.ExcludeContained)
            throw new Exception("Setting FilterContainedBlobs = true should set CountingMode = ExcludeContained!");

        Console.WriteLine(" -> JSON Serialization & Backward Compatibility verified!");
    }
}
