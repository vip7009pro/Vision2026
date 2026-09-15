using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VisionInspectionApp.Application.Services;

namespace TestExtractApp;

public static class ChessboardCalibrationMismatchTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=======================================================================");
        Console.WriteLine("🧪 RUNNING TESTS: CHESSBOARD CALIBRATION MISMATCH & ROBUSTNESS TESTS");
        Console.WriteLine("=======================================================================");

        TestInferPatternSize();
        TestCalibrateWithSquareCountConventionMismatch();
        TestCalibrateWithMixedCornerCountsDominantGroupSelection();
        TestCalibrateWithRotatedChessboardViews();
        TestCalibrateWithTooFewImagesOrInvalidInput();

        Console.WriteLine("=======================================================================");
        Console.WriteLine("✅ ALL CHESSBOARD CALIBRATION MISMATCH TESTS PASSED 100%!");
        Console.WriteLine("=======================================================================\n");
    }

    /// <summary>
    /// Test 1: Kiểm tra hàm InferPatternSize tự động suy luận lưới góc (w, h) chuẩn xác từ số góc thực tế.
    /// </summary>
    private static void TestInferPatternSize()
    {
        Console.WriteLine("--- Test 1: Kiểm tra hàm InferPatternSize ---");

        // Khi người dùng nhập 8x6 ô cờ nhưng thực tế có 35 góc (7x5)
        var size1 = ChessboardCalibrationService.InferPatternSize(35, new Size(8, 6));
        if (size1.Width != 7 || size1.Height != 5)
            throw new Exception($"Expected (7, 5), got ({size1.Width}, {size1.Height})");

        // Khi ảnh xoay dọc 6x8 ô cờ nhưng thực tế có 35 góc (5x7)
        var size2 = ChessboardCalibrationService.InferPatternSize(35, new Size(6, 8));
        if (size2.Width != 5 || size2.Height != 7)
            throw new Exception($"Expected (5, 7), got ({size2.Width}, {size2.Height})");

        // Khi số góc khớp chính xác 48 (8x6)
        var size3 = ChessboardCalibrationService.InferPatternSize(48, new Size(8, 6));
        if (size3.Width != 8 || size3.Height != 6)
            throw new Exception($"Expected (8, 6), got ({size3.Width}, {size3.Height})");

        // Khi nhập 10x7 ô cờ (54 góc = 9x6)
        var size4 = ChessboardCalibrationService.InferPatternSize(54, new Size(10, 7));
        if (size4.Width != 9 || size4.Height != 6)
            throw new Exception($"Expected (9, 6), got ({size4.Width}, {size4.Height})");

        Console.WriteLine("   => PASS: InferPatternSize suy luận chính xác các quy ước kích thước.");
    }

    /// <summary>
    /// Tạo danh sách các điểm 2D giả lập của bàn cờ khi chiếu lên camera (tương ứng với các góc xoay khác nhau).
    /// </summary>
    private static Point2f[] GenerateSimulatedCorners(Size patternSize, double squareSizePx, double offsetX, double offsetY, double angleRad = 0.0)
    {
        var list = new List<Point2f>();
        double cos = Math.Cos(angleRad);
        double sin = Math.Sin(angleRad);

        for (int r = 0; r < patternSize.Height; r++)
        {
            for (int c = 0; c < patternSize.Width; c++)
            {
                double px = c * squareSizePx;
                double py = r * squareSizePx;

                // Xoay nhẹ và dịch chuyển
                double rx = px * cos - py * sin + offsetX;
                double ry = px * sin + py * cos + offsetY;

                list.Add(new Point2f((float)rx, (float)ry));
            }
        }
        return list.ToArray();
    }

    /// <summary>
    /// Test 2: Khắc phục lỗi nguyên thủy của người dùng:
    /// patternSize cấu hình trên UI là (8, 6) = 48 điểm,
    /// nhưng ảnh thực tế phát hiện 35 góc (7x5 = 35 điểm).
    /// </summary>
    private static void TestCalibrateWithSquareCountConventionMismatch()
    {
        Console.WriteLine("--- Test 2: Calibrate khi UI cấu hình 48 góc (8x6) nhưng ảnh thực tế có 35 góc (7x5) ---");

        var actualPattern = new Size(7, 5);
        var uiPattern = new Size(8, 6); // Gây ra lỗi numberOfObjectPoints 48 != numberOfImagePoints 35 nếu chưa sửa
        var imageSize = new Size(1280, 960);
        double squareSizeMm = 25.0;

        // Tạo 3 ảnh chụp bàn cờ ở các góc xoay và vị trí khác nhau
        var allCorners = new List<Point2f[]>
        {
            GenerateSimulatedCorners(actualPattern, 40, 200, 200, 0.0),
            GenerateSimulatedCorners(actualPattern, 38, 300, 250, 0.1),
            GenerateSimulatedCorners(actualPattern, 42, 150, 300, -0.08)
        };

        // Calibrate với uiPattern (8, 6)
        var result = ChessboardCalibrationService.Calibrate(allCorners, imageSize, uiPattern, squareSizeMm);

        if (!result.Success)
        {
            throw new Exception($"Calibrate thất bại: {result.ErrorMessage}");
        }

        if (result.CameraMatrix == null || result.CameraMatrix[0, 0] <= 0)
        {
            throw new Exception("CameraMatrix không hợp lệ.");
        }

        Console.WriteLine($"   => PASS: Calibrate thành công mà không bị crash! FocalX={result.CameraMatrix[0, 0]:F1}, PxMm={result.PixelsPerMm:F2}, ReprojectionError={result.ReprojectionError:F4}");
    }

    /// <summary>
    /// Test 3: Xử lý tập ảnh hỗn hợp: Có 4 ảnh 35 góc (dominant) và 1 ảnh 48 góc (mismatch).
    /// </summary>
    private static void TestCalibrateWithMixedCornerCountsDominantGroupSelection()
    {
        Console.WriteLine("--- Test 3: Calibrate an toàn khi tập ảnh bị lẫn lộn góc (4 ảnh 35 góc + 1 ảnh 48 góc) ---");

        var p35 = new Size(7, 5);
        var p48 = new Size(8, 6);
        var imageSize = new Size(1280, 960);
        double squareSizeMm = 25.0;

        var allCorners = new List<Point2f[]>
        {
            GenerateSimulatedCorners(p35, 40, 200, 200, 0.0),
            GenerateSimulatedCorners(p48, 35, 100, 100, 0.0), // Mismatched image
            GenerateSimulatedCorners(p35, 38, 300, 250, 0.1),
            GenerateSimulatedCorners(p35, 42, 150, 300, -0.08),
            GenerateSimulatedCorners(p35, 39, 220, 210, 0.05)
        };

        var perViewSizes = new List<Size> { p35, p48, p35, p35, p35 };

        var result = ChessboardCalibrationService.Calibrate(allCorners, imageSize, p35, squareSizeMm, perViewSizes);

        if (!result.Success)
        {
            throw new Exception($"Calibrate thất bại khi có dominant group: {result.ErrorMessage}");
        }

        Console.WriteLine($"   => PASS: Tự động lọc ảnh lệch góc và calibrate thành công nhóm 4 ảnh hợp lệ! ReprojectionError={result.ReprojectionError:F4}");
    }

    /// <summary>
    /// Test 4: Hỗ trợ ảnh bàn cờ xoay 90 độ (7x5 và 5x7 đều có 35 góc nhưng chiều W x H đảo ngược).
    /// </summary>
    private static void TestCalibrateWithRotatedChessboardViews()
    {
        Console.WriteLine("--- Test 4: Calibrate với ảnh xoay 90 độ (7x5 và 5x7 cùng 35 góc) ---");

        var pHorizontal = new Size(7, 5);
        var pVertical = new Size(5, 7);
        var imageSize = new Size(1280, 960);
        double squareSizeMm = 25.0;

        var allCorners = new List<Point2f[]>
        {
            GenerateSimulatedCorners(pHorizontal, 40, 200, 200, 0.0),
            GenerateSimulatedCorners(pHorizontal, 38, 300, 250, 0.08),
            GenerateSimulatedCorners(pVertical, 40, 250, 200, 0.0),
            GenerateSimulatedCorners(pVertical, 38, 200, 300, -0.05)
        };

        var perViewSizes = new List<Size> { pHorizontal, pHorizontal, pVertical, pVertical };

        var result = ChessboardCalibrationService.Calibrate(allCorners, imageSize, pHorizontal, squareSizeMm, perViewSizes);

        if (!result.Success)
        {
            throw new Exception($"Calibrate ảnh xoay 90 độ thất bại: {result.ErrorMessage}");
        }

        Console.WriteLine($"   => PASS: Calibrate xoay 90 độ chính xác không lỗi! ReprojectionError={result.ReprojectionError:F4}");
    }

    /// <summary>
    /// Test 5: Không bao giờ crash khi dữ liệu đầu vào không hợp lệ hoặc thiếu ảnh.
    /// </summary>
    private static void TestCalibrateWithTooFewImagesOrInvalidInput()
    {
        Console.WriteLine("--- Test 5: Bắt ngoại lệ và trả kết quả an toàn khi thiếu ảnh hoặc dữ liệu lỗi ---");

        var imageSize = new Size(1280, 960);

        // Trường hợp chỉ có 2 ảnh (< 3)
        var twoCorners = new List<Point2f[]>
        {
            GenerateSimulatedCorners(new Size(7, 5), 40, 200, 200),
            GenerateSimulatedCorners(new Size(7, 5), 40, 200, 200)
        };
        var r1 = ChessboardCalibrationService.Calibrate(twoCorners, imageSize, new Size(7, 5), 25.0);
        if (r1.Success || string.IsNullOrEmpty(r1.ErrorMessage))
        {
            throw new Exception("Expected failure when < 3 images.");
        }

        // Trường hợp squareSizeMm <= 0
        var threeCorners = new List<Point2f[]>
        {
            GenerateSimulatedCorners(new Size(7, 5), 40, 200, 200),
            GenerateSimulatedCorners(new Size(7, 5), 40, 200, 200),
            GenerateSimulatedCorners(new Size(7, 5), 40, 200, 200)
        };
        var r2 = ChessboardCalibrationService.Calibrate(threeCorners, imageSize, new Size(7, 5), -5.0);
        if (r2.Success || string.IsNullOrEmpty(r2.ErrorMessage))
        {
            throw new Exception("Expected failure when squareSizeMm <= 0.");
        }

        // Trường hợp không có nhóm nào >= 3 ảnh
        var mixed = new List<Point2f[]>
        {
            GenerateSimulatedCorners(new Size(7, 5), 40, 200, 200), // 35
            GenerateSimulatedCorners(new Size(7, 5), 40, 200, 200), // 35
            GenerateSimulatedCorners(new Size(8, 6), 40, 200, 200), // 48
            GenerateSimulatedCorners(new Size(8, 6), 40, 200, 200)  // 48
        };
        var r3 = ChessboardCalibrationService.Calibrate(mixed, imageSize, new Size(7, 5), 25.0);
        if (r3.Success || string.IsNullOrEmpty(r3.ErrorMessage))
        {
            throw new Exception("Expected failure when no group has >= 3 images.");
        }

        Console.WriteLine("   => PASS: Toàn bộ các trường hợp lỗi dữ liệu đều được bắt và trả về thông báo lỗi chuẩn xác.");
    }
}
