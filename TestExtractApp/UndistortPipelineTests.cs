using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class UndistortPipelineTests
{
    private static InspectionService CreateInspectionService()
    {
        var pre = new ImagePreprocessor();
        var matcher = new PatternMatcher();
        var dist = new DistanceCalculator();
        var line = new LineDetector();
        var defect = new DefectDetector();
        return new InspectionService(pre, matcher, dist, line, defect);
    }
    public static void RunAllTests()
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("    RUNNING UNDISTORT & CALIPER PIPELINE TESTS    ");
        Console.WriteLine("==================================================");

        TestUndistortPreservesCenterAndIntrinsicMatrix();
        TestCaliperDetectionUnderUndistortPipeline();
        TestOriginTeachAndInspectSameImageWithUndistort_ScoreMustBeNearOne();
        TestSetSharedImageForDisplay_ConsistentWithUndistortAndToggle();

        Console.WriteLine("==================================================");
        Console.WriteLine("    ALL UNDISTORT PIPELINE TESTS PASSED 100%      ");
        Console.WriteLine("==================================================");
    }

    private static void TestUndistortPreservesCenterAndIntrinsicMatrix()
    {
        Console.Write("  [Test 1] Undistort preserves optical center and avoids artificial zoom... ");

        int width = 800;
        int height = 600;
        double cx = 400.0;
        double cy = 300.0;
        double fx = 800.0;
        double fy = 800.0;

        // Tạo ảnh synthetic có một điểm tròn ở đúng tâm quang học (400, 300)
        using var testImg = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);
        Cv2.Circle(testImg, new Point((int)cx, (int)cy), 10, Scalar.White, -1);

        var calib = new ChessboardCalibrationData
        {
            IsCalibrated = true,
            Fx = fx,
            Fy = fy,
            Cx = cx,
            Cy = cy,
            DistCoeffs = new double[] { -0.15, 0.05, 0.0, 0.0, 0.0 }, // Méo thùng nhẹ (barrel)
            PixelsPerMm = 10.0
        };

        using var undistorted = ChessboardCalibrationService.Undistort(testImg, calib);

        if (undistorted.Width != width || undistorted.Height != height)
        {
            throw new Exception($"Undistorted size mismatch: got {undistorted.Width}x{undistorted.Height}, expected {width}x{height}");
        }

        // Tìm điểm tâm của hình tròn trắng trên ảnh đã khử méo bằng Moments (trọng tâm hình học)
        using var gray = undistorted.CvtColor(ColorConversionCodes.BGR2GRAY);
        var moments = Cv2.Moments(gray);
        if (moments.M00 <= 0)
        {
            throw new Exception("Circle not found in undistorted image!");
        }

        double foundCx = moments.M10 / moments.M00;
        double foundCy = moments.M01 / moments.M00;

        // Vì r = 0 tại quang tâm (Cx, Cy), méo thấu kính r^2 = 0, nên tâm quang học PHẢI được bảo toàn tuyệt đối!
        double distCenter = Math.Sqrt(Math.Pow(foundCx - cx, 2) + Math.Pow(foundCy - cy, 2));
        if (distCenter > 1.5)
        {
            throw new Exception($"Optical center was shifted after undistort! Distance = {distCenter:F2}px (Expected <= 1.5px)");
        }

        Console.WriteLine($"PASSED (Center preserved, shift={distCenter:F2}px)");
    }

    private static void TestCaliperDetectionUnderUndistortPipeline()
    {
        Console.Write("  [Test 2] Caliper edge detection accuracy with EnableUndistort... ");

        int width = 640;
        int height = 480;
        using var img = new Mat(height, width, MatType.CV_8UC3, Scalar.Black);

        // Vẽ một mép tương phản từ Đen sang Trắng ở X = 200 (kéo dài theo chiều dọc)
        Cv2.Rectangle(img, new Rect(200, 0, width - 200, height), Scalar.White, -1);

        var calib = new ChessboardCalibrationData
        {
            IsCalibrated = true,
            Fx = 600.0,
            Fy = 600.0,
            Cx = 320.0,
            Cy = 240.0,
            DistCoeffs = new double[] { -0.05, 0.01, 0.0, 0.0, 0.0 },
            PixelsPerMm = 10.0
        };

        var config = new VisionConfig
        {
            ProductCode = "TEST_UNDISTORT_CALIPER",
            ChessboardCalibration = calib,
            ImageSources = new List<ImageSourceDefinition>
            {
                new ImageSourceDefinition
                {
                    Name = "ImageSource1",
                    EnableUndistort = true
                }
            },
            Calipers = new List<CaliperDefinition>
            {
                new CaliperDefinition
                {
                    Name = "Caliper1",
                    SearchRoi = new Roi { X = 170, Y = 100, Width = 60, Height = 200 },
                    Orientation = CaliperOrientation.Horizontal,
                    Polarity = EdgePolarity.DarkToLight,
                    MinEdgeStrength = 10.0,
                    StripCount = 10,
                    StripWidth = 5,
                    StripLength = 40
                }
            }
        };

        var inspectionService = CreateInspectionService();
        var result = inspectionService.Inspect(img, config);

        if (result.Calipers.Count == 0)
        {
            throw new Exception("No caliper result returned from pipeline!");
        }

        var calRes = result.Calipers[0];
        if (!calRes.Found)
        {
            throw new Exception("Caliper failed to find the edge under undistort pipeline!");
        }

        // Tọa độ mép X tìm thấy phải nằm sát 200 (dung sai < 3px cho méo nhẹ tại X=200 cách tâm Cx=320)
        double avgDetectedX = 0;
        foreach (var pt in calRes.Points)
        {
            avgDetectedX += pt.X;
        }
        avgDetectedX /= calRes.Points.Count;

        double edgeOffset = Math.Abs(avgDetectedX - 200.0);
        if (edgeOffset > 3.0)
        {
            throw new Exception($"Caliper edge was shifted too much! Detected X={avgDetectedX:F2}, Expected ~200.0, Offset={edgeOffset:F2}px");
        }

        Console.WriteLine($"PASSED (Detected X={avgDetectedX:F2}, Offset={edgeOffset:F2}px <= 3px)");
    }

    /// <summary>
    /// Tạo ảnh RAW có họa tiết bất đối xứng rõ nét nằm lệch góc (vùng chịu méo xuyên tâm mạnh nhất)
    /// để kiểm chứng: nếu ảnh mẫu bị Undistort 2 lần trong khi ảnh current Undistort 1 lần thì điểm tụt mạnh (~0.5-0.6),
    /// còn khi cùng pipeline 1x Undistort thì chạy trên chính ảnh đó phải đạt điểm ~1.0 (>= 0.99).
    /// </summary>
    private static Mat CreateOffCenterPatternImage(int width = 800, int height = 600)
    {
        var img = new Mat(height, width, MatType.CV_8UC3, new Scalar(30, 30, 30));

        // Họa tiết sắc nét ở góc trên-trái (x=70..230, y=60..220), cách xa quang tâm (400, 300)
        Cv2.Rectangle(img, new Rect(80, 70, 140, 140), new Scalar(235, 235, 235), -1);
        Cv2.Rectangle(img, new Rect(95, 85, 50, 90), new Scalar(25, 25, 25), -1);
        Cv2.Circle(img, new Point(185, 115), 22, new Scalar(20, 20, 20), -1);
        Cv2.Line(img, new Point(155, 155), new Point(210, 195), new Scalar(15, 15, 15), 6);
        Cv2.Polylines(img, new[]
        {
            new[] { new Point(90, 195), new Point(135, 180), new Point(165, 205) }
        }, false, new Scalar(20, 20, 20), 5);

        return img;
    }

    private static ChessboardCalibrationData CreateStrongDistortionCalib(int width = 800, int height = 600)
    {
        return new ChessboardCalibrationData
        {
            IsCalibrated = true,
            ImageWidth = width,
            ImageHeight = height,
            Fx = 650.0,
            Fy = 650.0,
            Cx = width / 2.0,
            Cy = height / 2.0,
            // Méo thấu kính mạnh để bất kỳ sai lệch 0x vs 1x vs 2x undistort nào cũng lộ rõ ngay
            DistCoeffs = new double[] { -0.28, 0.12, 0.002, -0.002, 0.0 },
            PixelsPerMm = 10.0
        };
    }

    private static (ToolEditorViewModel Vm, VisionConfig Config, ImageSourceDefinition ImgSrcDef, ToolGraphNodeViewModel OriginNode)
        CreateToolEditorWithOriginAndUndistort(string imagePath, ChessboardCalibrationData calib, bool enableUndistort)
    {
        var imgSrcDef = new ImageSourceDefinition
        {
            Name = "ImageSource1",
            SourceType = ImageSourceType.File,
            FilePath = imagePath,
            EnableUndistort = enableUndistort
        };

        var originDef = new PointDefinition
        {
            Name = "Origin",
            OriginAlgorithm = OriginAlgorithm.MvpShapeMatch2,
            MinScore = 0.6,
            MatchScoreThreshold = 0.6,
            MinAngle = -15,
            MaxAngle = 15,
            AngleStep = 1.0,
            TemplateRoi = new Roi { X = 60, Y = 50, Width = 180, Height = 180, Angle = 0 },
            SearchRoi = new Roi { X = 0, Y = 0, Width = 800, Height = 600, Angle = 0 }
        };

        var config = new VisionConfig
        {
            ProductCode = "TEST_ORIGIN_UNDISTORT_" + Guid.NewGuid().ToString("N")[..8],
            ChessboardCalibration = calib,
            ImageSources = new List<ImageSourceDefinition> { imgSrcDef },
            Origin = originDef,
            ToolGraph = new ToolGraph
            {
                Nodes = new List<ToolGraphNode>
                {
                    new ToolGraphNode { Id = "src1", Type = "ImageSource", RefName = "ImageSource1", X = 50, Y = 100 },
                    new ToolGraphNode { Id = "ori1", Type = "Origin", RefName = "Origin", X = 300, Y = 100 }
                },
                Edges = new List<ToolGraphEdge>
                {
                    new ToolGraphEdge { FromNodeId = "src1", FromPort = "Image", ToNodeId = "ori1", ToPort = "Image" }
                }
            }
        };

        var vm = new ToolEditorViewModel();
        typeof(ToolEditorViewModel)
            .GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, config);
        vm.ProductCode = config.ProductCode;

        var srcNode = new ToolGraphNodeViewModel { Id = "src1", Type = "ImageSource", RefName = "ImageSource1" };
        var originNode = new ToolGraphNodeViewModel { Id = "ori1", Type = "Origin", RefName = "Origin" };
        vm.Nodes.Clear();
        vm.Nodes.Add(srcNode);
        vm.Nodes.Add(originNode);

        return (vm, config, imgSrcDef, originNode);
    }

    private static void TestOriginTeachAndInspectSameImageWithUndistort_ScoreMustBeNearOne()
    {
        Console.Write("  [Test 3] Origin Teach + Inspect same image with EnableUndistort=true (no double undistort)... ");

        var tempDir = Path.Combine(Path.GetTempPath(), "Vision2026_OriginUndistort_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        var rawImgPath = Path.Combine(tempDir, "raw_sample.png");

        try
        {
            using var rawImg = CreateOffCenterPatternImage();
            Cv2.ImWrite(rawImgPath, rawImg);
            var calib = CreateStrongDistortionCalib(rawImg.Width, rawImg.Height);

            var (vm, config, imgSrcDef, originNode) = CreateToolEditorWithOriginAndUndistort(rawImgPath, calib, enableUndistort: true);

            // 1. Nạp ảnh vào SharedImage giống hệt khi chọn file / bật Undistort trong Tool Editor
            vm.UpdateSharedImageForImageSource(imgSrcDef);

            // 2. Xác nhận ResolveToolImageForPreview(sharedSnap, originNode) trả về ảnh 1x Undistort (KHÔNG undistort lần 2)
            var getSnapMethod = typeof(ToolEditorViewModel).GetMethod("GetCurrentWorkingImageSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var resolveMethod = typeof(ToolEditorViewModel).GetMethod("ResolveToolImageForPreview", BindingFlags.NonPublic | BindingFlags.Instance)!;

            using var sharedSnap = (Mat)getSnapMethod.Invoke(vm, null)!;
            using var resolvedForOrigin = (Mat)resolveMethod.Invoke(vm, new object[] { sharedSnap, originNode })!;
            using var expected1xUndistort = ChessboardCalibrationService.Undistort(rawImg, calib);

            using var diffMat = new Mat();
            Cv2.Absdiff(resolvedForOrigin, expected1xUndistort, diffMat);
            var maxDiff = Cv2.Mean(diffMat);
            double totalMeanDiff = maxDiff.Val0 + maxDiff.Val1 + maxDiff.Val2;
            if (totalMeanDiff > 0.01)
            {
                throw new Exception($"ResolveToolImageForPreview did NOT match 1x Undistort! Mean abs diff = {totalMeanDiff:F4} (double undistort regression!)");
            }

            // 3. Lấy mẫu Origin giống hệt nút Teach Template trên UI
            vm.Origin_TeachTemplate();
            if (string.IsNullOrWhiteSpace(config.Origin.TemplateImageFile) || !File.Exists(config.Origin.TemplateImageFile))
            {
                throw new Exception("Origin_TeachTemplate failed to save origin.png template!");
            }

            // 4. Chạy InspectionService.Inspect trên CHÍNH ẢNH RAW đó với mọi thuật toán Origin chính
            var inspectionService = CreateInspectionService();
            var algorithms = new[]
            {
                OriginAlgorithm.MvpShapeMatch2,
                OriginAlgorithm.MvpShapeMatch3,
                OriginAlgorithm.MvpShapeMatchInt8,
                OriginAlgorithm.MvpShapeMatch,
                OriginAlgorithm.ShapePyramid
            };

            var scoreSummary = new List<string>();
            foreach (var alg in algorithms)
            {
                config.Origin.OriginAlgorithm = alg;
                OriginMatcher.ClearCache();

                var res = inspectionService.Inspect(rawImg, config);
                if (res.Origin == null)
                {
                    throw new Exception($"[{alg}] Result.Origin is null!");
                }
                if (!res.Origin.Pass || res.Origin.Score < 0.98)
                {
                    throw new Exception($"[{alg}] Origin score too low on SAME image with EnableUndistort=true! Score={res.Origin.Score:F4} (expected >= 0.98), Pass={res.Origin.Pass}");
                }

                double expectedCx = config.Origin.TemplateRoi.X + config.Origin.TemplateRoi.Width / 2.0;
                double expectedCy = config.Origin.TemplateRoi.Y + config.Origin.TemplateRoi.Height / 2.0;
                double posErr = Math.Sqrt(Math.Pow(res.Origin.Position.X - expectedCx, 2) + Math.Pow(res.Origin.Position.Y - expectedCy, 2));
                if (posErr > 2.0)
                {
                    throw new Exception($"[{alg}] Origin position shifted on SAME image! Err={posErr:F2}px (found=({res.Origin.Position.X:F2},{res.Origin.Position.Y:F2}), expected=({expectedCx:F2},{expectedCy:F2}))");
                }

                scoreSummary.Add($"{alg}={res.Origin.Score:F4}");
            }

            Console.WriteLine($"PASSED ({string.Join(", ", scoreSummary)})");
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static void TestSetSharedImageForDisplay_ConsistentWithUndistortAndToggle()
    {
        Console.Write("  [Test 4] SetSharedImageForDisplay (OpenJob/Capture path) + toggle EnableUndistort ON/OFF... ");

        var tempDir = Path.Combine(Path.GetTempPath(), "Vision2026_OriginUndistortToggle_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        var rawImgPath = Path.Combine(tempDir, "raw_sample.png");

        try
        {
            using var rawImg = CreateOffCenterPatternImage();
            Cv2.ImWrite(rawImgPath, rawImg);
            var calib = CreateStrongDistortionCalib(rawImg.Width, rawImg.Height);

            var (vm, config, imgSrcDef, _) = CreateToolEditorWithOriginAndUndistort(rawImgPath, calib, enableUndistort: true);
            var inspectionService = CreateInspectionService();

            // Giả lập đường Open Job / Capture Camera gọi SetSharedImageForDisplay(rawImg, imgSrcDef) rồi Teach ngay
            vm.SetSharedImageForDisplay(rawImg, imgSrcDef);
            vm.Origin_TeachTemplate();

            OriginMatcher.ClearCache();
            var resOn = inspectionService.Inspect(rawImg, config);
            if (resOn.Origin == null || !resOn.Origin.Pass || resOn.Origin.Score < 0.98)
            {
                throw new Exception($"[EnableUndistort=true via SetSharedImageForDisplay] Score={resOn.Origin?.Score:F4} (expected >= 0.98)");
            }

            // Tắt EnableUndistort -> cập nhật lại SharedImage -> Teach lại -> Inspect phải vẫn đạt ~1.0
            imgSrcDef.EnableUndistort = false;
            vm.UpdateSharedImageForImageSource(imgSrcDef);
            vm.Origin_TeachTemplate();

            OriginMatcher.ClearCache();
            var resOff = inspectionService.Inspect(rawImg, config);
            if (resOff.Origin == null || !resOff.Origin.Pass || resOff.Origin.Score < 0.98)
            {
                throw new Exception($"[EnableUndistort=false] Score={resOff.Origin?.Score:F4} (expected >= 0.98)");
            }

            Console.WriteLine($"PASSED (Undistort ON={resOn.Origin.Score:F4}, OFF={resOff.Origin.Score:F4})");
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }
}
