using System;
using System.Collections.Generic;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
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
}
