using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class ContourCompareTests
{
    private static InspectionService CreateService()
    {
        var pre = new ImagePreprocessor();
        var matcher = new PatternMatcher();
        var dist = new DistanceCalculator();
        var line = new LineDetector();
        var defect = new DefectDetector();
        return new InspectionService(pre, matcher, dist, line, defect);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ❌ FAIL: {message}");
            Console.ResetColor();
            throw new Exception($"Assertion failed: {message}");
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✅ PASS: {message}");
        Console.ResetColor();
    }

    public static void RunAllTests()
    {
        Console.WriteLine("\n========================================================");
        Console.WriteLine("🎯 RUNNING CONTOURCOMPARE INTEGRATION TESTS");
        Console.WriteLine("========================================================");

        Test_ContourCompare_WithPreprocessNode_IdenticalImage();
        Test_ContourCompare_AllThreeMatchMethods();
        Test_ContourCompare_SearchRoiLargerThanTemplateRoi();
        Test_ContourCompare_DefectiveContour_DetectsNG();
        Test_ContourCompare_IdenticalImage_MustOverlapExactly_ZeroDist();
        Test_ContourCompare_LongStraightEdge_WithSlot_IdenticalImage_MustBeZeroDist_NoRedSegments();
        Test_ContourCompare_WhenNG_WholeJobResultPassMustBeFalse();
        Test_ContourCompare_ExtractMeasurementDetails();
    }

    private static void Test_ContourCompare_WithPreprocessNode_IdenticalImage()
    {
        Console.WriteLine("\n[Test 1] ContourCompare với Preprocess Node (Nhị phân 4) chạy trên cùng ảnh...");

        // 1. Tạo ảnh test
        using var rawMat = new Mat(400, 500, MatType.CV_8UC3, new Scalar(40, 40, 40));
        // Vẽ hình chữ nhật sáng và vòng tròn
        Cv2.Rectangle(rawMat, new Rect(150, 100, 180, 120), new Scalar(220, 220, 220), -1);
        Cv2.Circle(rawMat, new Point(240, 160), 35, new Scalar(40, 40, 40), -1);

        // 2. Thiết lập Config Job
        var config = new VisionConfig
        {
            ProductCode = "CC_TEST_PROD",
            Origin = new PointDefinition
            {
                Name = "Origin",
                SearchRoi = new Roi { X = 50, Y = 50, Width = 380, Height = 250 },
                TemplateRoi = new Roi { X = 140, Y = 90, Width = 200, Height = 140 },
                WorldPosition = new Point2dModel { X = 240, Y = 160 }
            }
        };

        // Node Preprocess "Nhi phan 4"
        var preDef = new PreprocessNodeDefinition
        {
            Name = "Nhi phan 4",
            Settings = new PreprocessSettings
            {
                UseGray = true,
                UseThreshold = true,
                ThresholdType = PreprocessThresholdType.Binary,
                ThresholdLow = 120
            }
        };
        config.PreprocessNodes.Add(preDef);

        // Node ContourCompare CC1
        var tempDir = Path.Combine(Path.GetTempPath(), "Vision_CC_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var templPath = Path.Combine(tempDir, "cc1_contour.png");

        var ccDef = new ContourCompareDefinition
        {
            Name = "CC1",
            TemplateRoi = new Roi { X = 140, Y = 90, Width = 200, Height = 140 },
            InspectRoi = new Roi { X = 140, Y = 90, Width = 200, Height = 140 },
            TemplateImageFile = templPath,
            MatchMethod = ContourMatchMethod.AreaPerimeterDiff,
            CannyThreshold1 = 50,
            CannyThreshold2 = 150,
            MinContourArea = 50,
            MaxShapeMatchScore = 0.10,
            MaxHausdorffDistPx = 5.0,
            MaxAreaDiffPercent = 5.0
        };
        config.ContourCompares.Add(ccDef);

        // ToolGraph: ImageSource -> Preprocess -> ContourCompare
        config.ToolGraph.Nodes.Add(new ToolGraphNode { Id = "src", Type = "ImageSource", RefName = "ImageSource" });
        config.ToolGraph.Nodes.Add(new ToolGraphNode { Id = "pre", Type = "Preprocess", RefName = "Nhi phan 4" });
        config.ToolGraph.Nodes.Add(new ToolGraphNode { Id = "cc", Type = "ContourCompare", RefName = "CC1" });

        config.ToolGraph.Edges.Add(new ToolGraphEdge { FromNodeId = "src", ToNodeId = "pre", FromPort = "Image", ToPort = "In" });
        config.ToolGraph.Edges.Add(new ToolGraphEdge { FromNodeId = "pre", ToNodeId = "cc", FromPort = "Out", ToPort = "In" });

        // Tạo template file đúng chuẩn (qua preprocess)
        var preprocessor = new ImagePreprocessor();
        using var preprocessed = preprocessor.Run(rawMat, preDef.Settings);
        using var templCrop = new Mat(preprocessed, new Rect(140, 90, 200, 140));
        using var templGray = templCrop.Channels() == 1 ? templCrop.Clone() : templCrop.CvtColor(ColorConversionCodes.BGR2GRAY);
        Cv2.ImWrite(templPath, templGray);

        // 3. Thực thi Inspect
        var inspectionService = CreateService();
        var result = inspectionService.Inspect(rawMat, config);

        Assert(result.ContourCompares.Count == 1, "Kết quả trả về 1 bản ghi ContourCompare");
        var r = result.ContourCompares[0];
        Assert(r.Found, "ContourCompare phải tìm thấy viền (Found = true)");
        Assert(r.Pass, $"ContourCompare phải PASS trên cùng 1 ảnh (thực tế: Pass={r.Pass})");
        Assert(r.MatchScore < 0.05, $"MatchScore Hu Moments phải gần 0 (thực tế: {r.MatchScore:F4})");
        Assert(r.MaxDistancePx < 1.0, $"MaxDistancePx phải < 1.0px (thực tế: {r.MaxDistancePx:F2}px)");
        Assert(r.AreaDiffPercent < 1.0, $"AreaDiffPercent phải < 1.0% (thực tế: {r.AreaDiffPercent:F2}%)");

        // Dọn dẹp
        try { Directory.Delete(tempDir, true); } catch { }
    }

    private static void Test_ContourCompare_AllThreeMatchMethods()
    {
        Console.WriteLine("\n[Test 2] Kiểm tra cả 3 thuật toán Match Method (HuMoments, HausdorffDistance, AreaPerimeterDiff)...");

        using var rawMat = new Mat(300, 300, MatType.CV_8UC1, new Scalar(0));
        Cv2.Circle(rawMat, new Point(150, 150), 50, new Scalar(255), -1);

        var tempDir = Path.Combine(Path.GetTempPath(), "Vision_CC_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var templPath = Path.Combine(tempDir, "circle_contour.png");
        using var tpl = new Mat(rawMat, new Rect(80, 80, 140, 140));
        Cv2.ImWrite(templPath, tpl);

        var methods = new[] { ContourMatchMethod.HuMoments, ContourMatchMethod.HausdorffDistance, ContourMatchMethod.AreaPerimeterDiff };

        foreach (var method in methods)
        {
            var config = new VisionConfig
            {
                ProductCode = "METHOD_TEST"
            };
            var ccDef = new ContourCompareDefinition
            {
                Name = "CC_Method",
                TemplateRoi = new Roi { X = 80, Y = 80, Width = 140, Height = 140 },
                InspectRoi = new Roi { X = 80, Y = 80, Width = 140, Height = 140 },
                TemplateImageFile = templPath,
                MatchMethod = method,
                CannyThreshold1 = 50,
                CannyThreshold2 = 150,
                MinContourArea = 30,
                MaxShapeMatchScore = 0.10,
                MaxHausdorffDistPx = 5.0,
                MaxAreaDiffPercent = 5.0
            };
            config.ContourCompares.Add(ccDef);

            var inspectionService = CreateService();
            var res = inspectionService.Inspect(rawMat, config);

            Assert(res.ContourCompares.Count == 1, $"Kiểm tra method {method}: có kết quả");
            Assert(res.ContourCompares[0].Pass, $"Kiểm tra method {method}: PASS trên ảnh mẫu gốc");
        }

        try { Directory.Delete(tempDir, true); } catch { }
    }

    private static void Test_ContourCompare_SearchRoiLargerThanTemplateRoi()
    {
        Console.WriteLine("\n[Test 3] Kiểm tra Search ROI lớn hơn Template ROI (MatchTemplate định vị trong Search ROI)...");

        using var rawMat = new Mat(400, 400, MatType.CV_8UC1, new Scalar(30));
        // Vẽ chi tiết nằm ở (180, 180) kích thước (60, 40) lọt trọn vẹn trong Template ROI (170, 170, 80, 60)
        Cv2.Rectangle(rawMat, new Rect(180, 180, 60, 40), new Scalar(240), -1);

        var tempDir = Path.Combine(Path.GetTempPath(), "Vision_CC_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var templPath = Path.Combine(tempDir, "rect_contour.png");
        using var tpl = new Mat(rawMat, new Rect(170, 170, 80, 60));
        Cv2.ImWrite(templPath, tpl);

        var config = new VisionConfig
        {
            ProductCode = "SEARCH_ROI_TEST"
        };
        var ccDef = new ContourCompareDefinition
        {
            Name = "CC_SearchRoi",
            // Template ROI đúng kích thước chi tiết (80x60)
            TemplateRoi = new Roi { X = 170, Y = 170, Width = 80, Height = 60 },
            // Search ROI rộng hơn (200x180)
            InspectRoi = new Roi { X = 110, Y = 110, Width = 200, Height = 180 },
            TemplateImageFile = templPath,
            MatchMethod = ContourMatchMethod.AreaPerimeterDiff,
            CannyThreshold1 = 50,
            CannyThreshold2 = 150,
            MinContourArea = 30,
            MaxAreaDiffPercent = 5.0
        };
        config.ContourCompares.Add(ccDef);

        var inspectionService = CreateService();
        var res = inspectionService.Inspect(rawMat, config);

        Assert(res.ContourCompares.Count == 1, "Trả về kết quả CC_SearchRoi");
        var r = res.ContourCompares[0];
        Assert(r.Found && r.Pass, $"Search ROI rộng hơn Template ROI: PASS thành công (Pass={r.Pass})");
        Assert(r.MaxDistancePx < 1.0, $"Lệch vị trí cực tiểu sau MatchTemplate + ICP ({r.MaxDistancePx:F2}px)");

        try { Directory.Delete(tempDir, true); } catch { }
    }

    private static void Test_ContourCompare_DefectiveContour_DetectsNG()
    {
        Console.WriteLine("\n[Test 4] Kiểm tra phôi bị biến dạng / khuyết viền: Báo NG chính xác...");

        using var goodMat = new Mat(300, 300, MatType.CV_8UC1, new Scalar(0));
        Cv2.Rectangle(goodMat, new Rect(100, 100, 100, 100), new Scalar(255), -1);

        var tempDir = Path.Combine(Path.GetTempPath(), "Vision_CC_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var templPath = Path.Combine(tempDir, "square_contour.png");
        using var tpl = new Mat(goodMat, new Rect(80, 80, 140, 140));
        Cv2.ImWrite(templPath, tpl);

        // Tạo ảnh lỗi: góc hình chữ nhật bị mẻ một miếng to (khuyết tật)
        using var badMat = goodMat.Clone();
        Cv2.Circle(badMat, new Point(190, 110), 25, new Scalar(0), -1); // Khuyết góc trên phải

        var config = new VisionConfig
        {
            ProductCode = "DEFECT_TEST"
        };
        var ccDef = new ContourCompareDefinition
        {
            Name = "CC_Defect",
            TemplateRoi = new Roi { X = 80, Y = 80, Width = 140, Height = 140 },
            InspectRoi = new Roi { X = 80, Y = 80, Width = 140, Height = 140 },
            TemplateImageFile = templPath,
            MatchMethod = ContourMatchMethod.HausdorffDistance,
            CannyThreshold1 = 50,
            CannyThreshold2 = 150,
            MinContourArea = 30,
            MaxHausdorffDistPx = 4.0
        };
        config.ContourCompares.Add(ccDef);

        var inspectionService = CreateService();
        var res = inspectionService.Inspect(badMat, config);

        Assert(res.ContourCompares.Count == 1, "Trả về kết quả kiểm tra phôi lỗi");
        var r = res.ContourCompares[0];
        Assert(!r.Pass, $"Phôi mẻ viền phải báo NG (thực tế: Pass={r.Pass})");
        Assert(r.MaxDistancePx > 4.0, $"Max distance phải vượt ngưỡng lỗi (thực tế: {r.MaxDistancePx:F2}px > 4.0px)");
        Assert(r.FailSegments != null && r.FailSegments.Count > 0, $"Phôi mẻ viền phải trích xuất các đoạn lỗi tô đỏ FailSegments (thực tế: {r.FailSegments?.Count ?? 0} đoạn)");

        try { Directory.Delete(tempDir, true); } catch { }
    }

    private static void Test_ContourCompare_IdenticalImage_MustOverlapExactly_ZeroDist()
    {
        Console.WriteLine("\n[Test 5] Cùng 1 mẫu 1 ảnh: Viền phải khớp chằn chặn 100% (MaxDist = 0.00px, 0 đoạn lỗi)...");

        using var goodMat = new Mat(300, 300, MatType.CV_8UC1, new Scalar(0));
        // Vẽ mũi tên hoặc hình phức tạp
        Cv2.Rectangle(goodMat, new Rect(80, 130, 100, 40), new Scalar(255), -1);
        Cv2.Circle(goodMat, new Point(180, 150), 30, new Scalar(255), -1);

        var tempDir = Path.Combine(Path.GetTempPath(), "Vision_CC_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var templPath = Path.Combine(tempDir, "arrow_contour.png");
        using var tpl = new Mat(goodMat, new Rect(70, 110, 160, 80));
        Cv2.ImWrite(templPath, tpl);

        var config = new VisionConfig
        {
            ProductCode = "IDENTICAL_TEST"
        };
        var ccDef = new ContourCompareDefinition
        {
            Name = "CC_Identical",
            TemplateRoi = new Roi { X = 70, Y = 110, Width = 160, Height = 80 },
            InspectRoi = new Roi { X = 70, Y = 110, Width = 160, Height = 80 },
            TemplateImageFile = templPath,
            MatchMethod = ContourMatchMethod.HausdorffDistance,
            CannyThreshold1 = 50,
            CannyThreshold2 = 150,
            MinContourArea = 30,
            MaxHausdorffDistPx = 4.0
        };
        config.ContourCompares.Add(ccDef);

        var inspectionService = CreateService();
        var res = inspectionService.Inspect(goodMat, config);

        Assert(res.ContourCompares.Count == 1, "Trả về kết quả CC_Identical");
        var r = res.ContourCompares[0];
        Assert(r.Pass, $"Cùng 1 ảnh phải PASS tuyệt đối (Pass={r.Pass})");
        Assert(r.MaxDistancePx < 0.001, $"Khoảng cách lệch cực đại phải bằng 0.00px (thực tế: {r.MaxDistancePx:F4}px)");
        Assert(r.FailSegments == null || r.FailSegments.Count == 0, $"Không được có bất kỳ đoạn viền lỗi tô đỏ nào (thực tế: {r.FailSegments?.Count ?? 0})");
        Assert(r.PassSegments != null && r.PassSegments.Count > 0, $"Phải có đoạn viền xanh lá PassSegments khớp chuẩn (thực tế: {r.PassSegments?.Count ?? 0})");

        try { Directory.Delete(tempDir, true); } catch { }
    }

    private static void Test_ContourCompare_LongStraightEdge_WithSlot_IdenticalImage_MustBeZeroDist_NoRedSegments()
    {
        Console.WriteLine("\n[Test 6] Phôi lớn có cạnh thẳng dài và rãnh chữ U (giống ảnh thực tế người dùng)...");

        // Vẽ một khối chữ nhật lớn màu trắng trên nền đen: 400x160 với rãnh chữ U ở cạnh trái
        using var rawMat = new Mat(300, 600, MatType.CV_8UC1, new Scalar(0));
        // Thân chính
        Cv2.Rectangle(rawMat, new Rect(50, 60, 500, 180), new Scalar(255), -1);
        // Khoét rãnh chữ U ở mép trái (x=50..90, y=130..170)
        Cv2.Rectangle(rawMat, new Rect(50, 130, 40, 40), new Scalar(0), -1);

        var tempDir = Path.Combine(Path.GetTempPath(), "Vision_CC_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var templPath = Path.Combine(tempDir, "slot_contour.png");
        using var tpl = new Mat(rawMat, new Rect(40, 50, 520, 200));
        Cv2.ImWrite(templPath, tpl);

        var config = new VisionConfig
        {
            ProductCode = "LONG_SLOT_TEST"
        };
        var ccDef = new ContourCompareDefinition
        {
            Name = "CC2",
            TemplateRoi = new Roi { X = 40, Y = 50, Width = 520, Height = 200 },
            InspectRoi = new Roi { X = 40, Y = 50, Width = 520, Height = 200 },
            TemplateImageFile = templPath,
            MatchMethod = ContourMatchMethod.HausdorffDistance,
            CannyThreshold1 = 50,
            CannyThreshold2 = 150,
            MinContourArea = 50,
            MaxHausdorffDistPx = 4.0
        };
        config.ContourCompares.Add(ccDef);

        var inspectionService = CreateService();
        var res = inspectionService.Inspect(rawMat, config);

        Assert(res.ContourCompares.Count == 1, "Trả về kết quả CC2");
        var r = res.ContourCompares[0];
        Assert(r.Pass, $"Cùng 1 ảnh chi tiết cạnh dài + rãnh U: PASS tuyệt đối (Pass={r.Pass})");
        Assert(r.MaxDistancePx < 0.001, $"Khoảng cách cực đại phải bằng 0.00px (thực tế: {r.MaxDistancePx:F4}px)");
        Assert(r.FailSegments == null || r.FailSegments.Count == 0, $"Không được có bất kỳ đoạn viền đỏ nào dọc cạnh thẳng hay rãnh U (thực tế: {r.FailSegments?.Count ?? 0} đoạn lỗi)");
        Assert(res.Pass, $"Toàn bộ Job phải có kết quả Pass = true (thực tế: {res.Pass})");

        try { Directory.Delete(tempDir, true); } catch { }
    }

    private static void Test_ContourCompare_WhenNG_WholeJobResultPassMustBeFalse()
    {
        Console.WriteLine("\n[Test 7] Kiểm tra khi ContourCompare bị NG thì toàn bộ JOB phải ra NG (result.Pass = false)...");

        using var goodMat = new Mat(300, 300, MatType.CV_8UC1, new Scalar(0));
        Cv2.Rectangle(goodMat, new Rect(80, 80, 140, 140), new Scalar(255), -1);

        var tempDir = Path.Combine(Path.GetTempPath(), "Vision_CC_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var templPath = Path.Combine(tempDir, "sq_contour.png");
        using var tpl = new Mat(goodMat, new Rect(70, 70, 160, 160));
        Cv2.ImWrite(templPath, tpl);

        // Tạo ảnh lỗi: phôi bị biến dạng nặng
        using var badMat = new Mat(300, 300, MatType.CV_8UC1, new Scalar(0));
        Cv2.Circle(badMat, new Point(150, 150), 70, new Scalar(255), -1); // Tròn thay vì vuông

        var config = new VisionConfig
        {
            ProductCode = "WHOLE_JOB_NG_TEST"
        };
        var ccDef = new ContourCompareDefinition
        {
            Name = "CC_MustFail",
            TemplateRoi = new Roi { X = 70, Y = 70, Width = 160, Height = 160 },
            InspectRoi = new Roi { X = 70, Y = 70, Width = 160, Height = 160 },
            TemplateImageFile = templPath,
            MatchMethod = ContourMatchMethod.HausdorffDistance,
            CannyThreshold1 = 50,
            CannyThreshold2 = 150,
            MinContourArea = 50,
            MaxHausdorffDistPx = 3.0
        };
        config.ContourCompares.Add(ccDef);

        var inspectionService = CreateService();
        var res = inspectionService.Inspect(badMat, config);

        Assert(res.ContourCompares.Count == 1, "Có kết quả CC_MustFail");
        Assert(!res.ContourCompares[0].Pass, "ContourCompare phải NG");
        Assert(!res.Pass, $"Toàn bộ Job phải ra NG khi ContourCompare NG (thực tế: res.Pass={res.Pass})");

        try { Directory.Delete(tempDir, true); } catch { }
    }

    private static void Test_ContourCompare_ExtractMeasurementDetails()
    {
        Console.WriteLine("\n[Test 8] Kiểm tra kết quả ContourCompare được đưa vào Bảng kết quả đo đạc (ExtractMeasurementDetails)...");

        using var goodMat = new Mat(200, 200, MatType.CV_8UC1, new Scalar(0));
        Cv2.Rectangle(goodMat, new Rect(50, 50, 100, 100), new Scalar(255), -1);

        var tempDir = Path.Combine(Path.GetTempPath(), "Vision_CC_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var templPath = Path.Combine(tempDir, "sq_details.png");
        using var tpl = new Mat(goodMat, new Rect(40, 40, 120, 120));
        Cv2.ImWrite(templPath, tpl);

        var config = new VisionConfig
        {
            ProductCode = "DETAILS_TEST"
        };
        var ccDef = new ContourCompareDefinition
        {
            Name = "CC_Details",
            TemplateRoi = new Roi { X = 40, Y = 40, Width = 120, Height = 120 },
            InspectRoi = new Roi { X = 40, Y = 40, Width = 120, Height = 120 },
            TemplateImageFile = templPath,
            MatchMethod = ContourMatchMethod.HausdorffDistance,
            CannyThreshold1 = 50,
            CannyThreshold2 = 150,
            MinContourArea = 50,
            MaxHausdorffDistPx = 4.0
        };
        config.ContourCompares.Add(ccDef);

        var inspectionService = CreateService();
        var res = inspectionService.Inspect(goodMat, config);

        var oqcService = new VisionInspectionApp.Application.OQC.OqcScannerService();
        var details = oqcService.ExtractMeasurementDetails(res, config);

        Assert(details != null && details.Count > 0, "ExtractMeasurementDetails phải trả về danh sách phép đo");
        var ccDetail = details.FirstOrDefault(d => d.ToolName == "CC_Details");
        Assert(ccDetail != null, "Phải tìm thấy dòng ContourCompare 'CC_Details' trong bảng đo");
        Assert(ccDetail.Pass, "Kết quả đo của ContourCompare phải PASS");
        Assert(ccDetail.Unit == "px", $"Đơn vị đo của Hausdorff là 'px' (thực tế: '{ccDetail.Unit}')");
        Assert(ccDetail.CustomSpecText.Contains("4.00 px"), $"Hiển thị ngưỡng spec chuẩn xác (thực tế: '{ccDetail.CustomSpecText}')");

        try { Directory.Delete(tempDir, true); } catch { }
    }
}
