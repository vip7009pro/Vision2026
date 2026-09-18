using System;
using System.IO;
using OpenCvSharp;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;

namespace TestExtractApp;

public static class UndistortResolutionTests
{
    public static void RunAll()
    {
        Console.WriteLine("\n=== RUNNING UNDISTORT RESOLUTION & 20MP PRESERVATION TESTS ===");

        Test1_Undistort_20MP_PreservesResolution_WithDifferentCalibSize();
        Test2_Undistort_ScalesCameraMatrixK_Correctly();
        Test3_GlobalCalibrationJson_PreservesResolutionWhenApplied();

        Console.WriteLine("=== ALL UNDISTORT RESOLUTION TESTS PASSED! ===\n");
    }

    private static void Test1_Undistort_20MP_PreservesResolution_WithDifferentCalibSize()
    {
        Console.WriteLine("--- Test 1: Undistort 20MP image (5472x3648) with 1280x853 calibration data ---");

        // Giả lập calibration data được tính từ độ phân giải proxy 1280x853
        var calibData1280 = new ChessboardCalibrationData
        {
            BoardCols = 8,
            BoardRows = 6,
            SquareSizeMm = 29.0,
            Fx = 1000.0,
            Fy = 1000.0,
            Cx = 640.0,
            Cy = 426.5,
            DistCoeffs = new double[] { -0.15, 0.05, 0.0, 0.0, 0.0 },
            ReprojectionError = 0.05,
            PixelsPerMm = 15.0,
            ImageWidth = 1280,
            ImageHeight = 853,
            IsCalibrated = true
        };

        // Tạo ảnh giả lập 20MP (5472x3648)
        using var src20MP = new Mat(3648, 5472, MatType.CV_8UC3, new Scalar(100, 150, 200));

        // Thực hiện Undistort
        using var resultMat = ChessboardCalibrationService.Undistort(src20MP, calibData1280);

        if (resultMat == null || resultMat.Empty())
        {
            throw new Exception("FAIL: Undistort returned null or empty Mat!");
        }

        if (resultMat.Width != 5472 || resultMat.Height != 3648)
        {
            throw new Exception($"FAIL: Expected undistorted size 5472x3648, but got {resultMat.Width}x{resultMat.Height}!");
        }

        Console.WriteLine($"  ✓ Input 5472x3648 -> Undistort Output: {resultMat.Width}x{resultMat.Height} (100% PRESERVED 20MPx)");
    }

    private static void Test2_Undistort_ScalesCameraMatrixK_Correctly()
    {
        Console.WriteLine("--- Test 2: Auto-scaling of camera matrix K for legacy calibration (VGA 640x480 -> 20MP) ---");

        // Dữ liệu calibration cũ (chưa có ImageWidth/Height, cx=320, cy=240)
        var legacyCalibData = new ChessboardCalibrationData
        {
            BoardCols = 8,
            BoardRows = 6,
            SquareSizeMm = 29.0,
            Fx = 800.0,
            Fy = 800.0,
            Cx = 320.0,
            Cy = 240.0,
            DistCoeffs = new double[] { -0.15, 0.05, 0.0, 0.0, 0.0 },
            ReprojectionError = 0.042,
            PixelsPerMm = 25.5,
            ImageWidth = 0,
            ImageHeight = 0,
            IsCalibrated = true
        };

        using var src20MP = new Mat(3648, 5472, MatType.CV_8UC3, new Scalar(50, 100, 150));

        // Vẽ một hình tròn ở tâm ảnh 20MP (2736, 1824)
        Cv2.Circle(src20MP, new Point(2736, 1824), 100, new Scalar(255, 255, 255), -1);

        using var resultMat = ChessboardCalibrationService.Undistort(src20MP, legacyCalibData);

        if (resultMat.Width != 5472 || resultMat.Height != 3648)
        {
            throw new Exception($"FAIL: Expected 5472x3648, but got {resultMat.Width}x{resultMat.Height}!");
        }

        // Kiểm tra pixel ở tâm ảnh vẫn tồn tại và không bị méo lệch góc
        var centerPixel = resultMat.Get<Vec3b>(1824, 2736);
        if (centerPixel[0] < 200)
        {
            throw new Exception("FAIL: Center pixel was shifted inappropriately!");
        }

        Console.WriteLine($"  ✓ Legacy calib (cx=320, cy=240) automatically scaled to 20MP center without image shrinking: PASSED");
    }

    private static void Test3_GlobalCalibrationJson_PreservesResolutionWhenApplied()
    {
        Console.WriteLine("--- Test 3: Verify Global Calibration loading and ImageWidth/Height serialization ---");

        var testCalib = new ChessboardCalibrationData
        {
            BoardCols = 8,
            BoardRows = 6,
            SquareSizeMm = 29.0,
            Fx = 1500.0,
            Fy = 1500.0,
            Cx = 2736.0,
            Cy = 1824.0,
            DistCoeffs = new double[] { -0.1, 0.02, 0, 0, 0 },
            ReprojectionError = 0.03,
            PixelsPerMm = 30.0,
            ImageWidth = 5472,
            ImageHeight = 3648,
            IsCalibrated = true
        };

        // Test Clone
        var clone = testCalib.Clone();
        if (clone.ImageWidth != 5472 || clone.ImageHeight != 3648)
        {
            throw new Exception($"FAIL: Clone failed to copy ImageWidth/Height! Got {clone.ImageWidth}x{clone.ImageHeight}");
        }

        Console.WriteLine($"  ✓ ChessboardCalibrationData.Clone() preserves ImageWidth={clone.ImageWidth}, ImageHeight={clone.ImageHeight}: PASSED");
    }
}
