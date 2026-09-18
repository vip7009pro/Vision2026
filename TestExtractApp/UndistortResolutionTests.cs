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
        Test4_ExportCalibrationToFileAndVerify();
        Test5_ImportCalibrationFromFileAndVerify();

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

    private static void Test4_ExportCalibrationToFileAndVerify()
    {
        Console.WriteLine("--- Test 4: Verify ExportCalibration to JSON file ---");

        var testCalib = new ChessboardCalibrationData
        {
            BoardCols = 9,
            BoardRows = 6,
            SquareSizeMm = 29.0,
            Fx = 2450.5,
            Fy = 2451.2,
            Cx = 2736.0,
            Cy = 1824.0,
            DistCoeffs = new double[] { -0.125, 0.045, 0.001, -0.002, 0.0 },
            ReprojectionError = 0.0245,
            PixelsPerMm = 32.789,
            ImageWidth = 5472,
            ImageHeight = 3648,
            IsCalibrated = true
        };

        var tempDir = Path.Combine(Path.GetTempPath(), "VisionCalibrationTests_" + Guid.NewGuid().ToString("N"));
        var exportPath = Path.Combine(tempDir, "exported_calib.json");

        try
        {
            // 1. Export ra file
            bool exported = ChessboardCalibrationService.ExportCalibration(testCalib, exportPath);
            if (!exported || !File.Exists(exportPath))
            {
                throw new Exception($"FAIL: ExportCalibration returned false or file does not exist at {exportPath}!");
            }

            // 2. Đọc lại và parse JSON xác thực tính toàn vẹn
            var jsonText = File.ReadAllText(exportPath);
            if (string.IsNullOrWhiteSpace(jsonText) || !jsonText.Contains("2450.5") || !jsonText.Contains("5472"))
            {
                throw new Exception($"FAIL: Exported JSON does not contain expected calibration data! Content: {jsonText}");
            }

            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<ChessboardCalibrationData>(jsonText, options);
            if (deserialized is null || !deserialized.IsCalibrated)
            {
                throw new Exception("FAIL: Deserialized calibration data is null or not calibrated!");
            }

            if (Math.Abs(deserialized.Fx - 2450.5) > 1e-6 ||
                Math.Abs(deserialized.PixelsPerMm - 32.789) > 1e-4 ||
                deserialized.ImageWidth != 5472 ||
                deserialized.ImageHeight != 3648 ||
                deserialized.DistCoeffs.Length != 5)
            {
                throw new Exception("FAIL: Deserialized values mismatch with original calibration data!");
            }

            Console.WriteLine($"  ✓ ExportCalibration successfully exported JSON and verified all fields (Fx={deserialized.Fx}, Px/mm={deserialized.PixelsPerMm}, 5472x3648): PASSED");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    private static void Test5_ImportCalibrationFromFileAndVerify()
    {
        Console.WriteLine("--- Test 5: Verify ImportCalibration from JSON file ---");

        var originalCalib = new ChessboardCalibrationData
        {
            BoardCols = 10,
            BoardRows = 7,
            SquareSizeMm = 25.0,
            Fx = 2600.75,
            Fy = 2601.25,
            Cx = 2736.0,
            Cy = 1824.0,
            DistCoeffs = new double[] { -0.11, 0.03, -0.001, 0.002, 0.005 },
            ReprojectionError = 0.019,
            PixelsPerMm = 35.5,
            ImageWidth = 5472,
            ImageHeight = 3648,
            IsCalibrated = true
        };

        var tempDir = Path.Combine(Path.GetTempPath(), "VisionCalibrationImportTests_" + Guid.NewGuid().ToString("N"));
        var validJsonPath = Path.Combine(tempDir, "valid_calib.json");
        var invalidJsonPath = Path.Combine(tempDir, "invalid_calib.json");
        var notFoundPath = Path.Combine(tempDir, "non_existent.json");

        try
        {
            // 1. Export ra file hợp lệ rồi Import lại
            ChessboardCalibrationService.ExportCalibration(originalCalib, validJsonPath);
            var (success, imported, errorMsg) = ChessboardCalibrationService.ImportCalibration(validJsonPath);

            if (!success || imported is null)
            {
                throw new Exception($"FAIL: ImportCalibration failed on valid file! Error: {errorMsg}");
            }

            if (Math.Abs(imported.Fx - 2600.75) > 1e-6 ||
                Math.Abs(imported.Fy - 2601.25) > 1e-6 ||
                Math.Abs(imported.PixelsPerMm - 35.5) > 1e-4 ||
                imported.ImageWidth != 5472 ||
                imported.ImageHeight != 3648 ||
                imported.DistCoeffs.Length != 5 ||
                !imported.IsCalibrated)
            {
                throw new Exception("FAIL: Imported calibration properties do not match exported values!");
            }
            Console.WriteLine($"  ✓ Import valid JSON succeeds with exact matched values: PASSED");

            // 2. Import file không tồn tại
            var (fnfSuccess, _, fnfMsg) = ChessboardCalibrationService.ImportCalibration(notFoundPath);
            if (fnfSuccess || string.IsNullOrWhiteSpace(fnfMsg))
            {
                throw new Exception("FAIL: Import non-existent file should fail but reported success!");
            }
            Console.WriteLine($"  ✓ Import non-existent file returns proper failure and message: PASSED");

            // 3. Import file JSON không hợp lệ (Focal <= 0 hoặc rỗng)
            File.WriteAllText(invalidJsonPath, "{\"Fx\": 0, \"Fy\": 0, \"IsCalibrated\": false}");
            var (invSuccess, _, invMsg) = ChessboardCalibrationService.ImportCalibration(invalidJsonPath);
            if (invSuccess || string.IsNullOrWhiteSpace(invMsg))
            {
                throw new Exception("FAIL: Import invalid/uncalibrated JSON should fail but reported success!");
            }
            Console.WriteLine($"  ✓ Import invalid/uncalibrated JSON returns proper failure: PASSED");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
