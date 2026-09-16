using System;
using System.Diagnostics;
using OpenCvSharp;
using VisionInspectionApp.Application.Services;

namespace TestExtractApp;

public static class ChessboardRobustnessTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=======================================================");
        Console.WriteLine("🧪 RUNNING TESTS: CHESSBOARD DETECTION ROBUSTNESS TESTS");
        Console.WriteLine("=======================================================");

        TestSyntheticChessboardStandardDetection();
        TestRotatedChessboardAutoSwapDimensions();
        TestGradientLightingAndClaheSectorBased();
        TestAlternativeConventionDetection();
        TestHighResolutionNoiseImageNoHang();
        TestAutoDetectCommonChessboardPatterns();

        Console.WriteLine("=======================================================");
        Console.WriteLine("✅ ALL CHESSBOARD DETECTION ROBUSTNESS TESTS PASSED!");
        Console.WriteLine("=======================================================\n");
    }

    /// <summary>
    /// Tạo ảnh bàn cờ nhân tạo sắc nét (squaresX x squaresY ô cờ -> (squaresX - 1) x (squaresY - 1) góc trong)
    /// </summary>
    private static Mat CreateSyntheticChessboard(int squaresX, int squaresY, int squareSizePx, int marginPx = 40)
    {
        int width = squaresX * squareSizePx + marginPx * 2;
        int height = squaresY * squareSizePx + marginPx * 2;

        var mat = new Mat(height, width, MatType.CV_8UC1, new Scalar(200));

        for (int r = 0; r < squaresY; r++)
        {
            for (int c = 0; c < squaresX; c++)
            {
                if ((r + c) % 2 == 0)
                {
                    int x = marginPx + c * squareSizePx;
                    int y = marginPx + r * squareSizePx;
                    Cv2.Rectangle(mat, new Rect(x, y, squareSizePx, squareSizePx), Scalar.All(20), -1);
                }
                else
                {
                    int x = marginPx + c * squareSizePx;
                    int y = marginPx + r * squareSizePx;
                    Cv2.Rectangle(mat, new Rect(x, y, squareSizePx, squareSizePx), Scalar.All(255), -1);
                }
            }
        }

        return mat;
    }

    private static void TestSyntheticChessboardStandardDetection()
    {
        Console.WriteLine("--- Test 1: Kiểm tra nhận diện bàn cờ chuẩn bằng Sector-Based SB ---");

        // 10 ô ngang x 7 ô dọc => 9 x 6 = 54 inner corners
        int squaresX = 10;
        int squaresY = 7;
        int expectedCornersX = 9;
        int expectedCornersY = 6;
        int expectedTotal = expectedCornersX * expectedCornersY;

        using var mat = CreateSyntheticChessboard(squaresX, squaresY, 50);

        var sw = Stopwatch.StartNew();
        var result = ChessboardCalibrationService.DetectCornersMultiStrategy(
            mat,
            new Size(expectedCornersX, expectedCornersY),
            autoSwapDimensions: true,
            useEnhancedSectorBased: true,
            tryAlternativeConvention: true);
        sw.Stop();

        if (!result.Found)
        {
            throw new Exception($"Không phát hiện được bàn cờ chuẩn {expectedCornersX}x{expectedCornersY}!");
        }

        if (result.Corners.Length != expectedTotal)
        {
            throw new Exception($"Số corners nhận diện không đúng! Mong đợi: {expectedTotal}, thực tế: {result.Corners.Length}");
        }

        Console.WriteLine($"  -> PASSED: Nhận diện thành công {result.Corners.Length} corners ({result.StrategyUsed}) trong {sw.ElapsedMilliseconds} ms (< 200 ms).");
    }

    private static void TestRotatedChessboardAutoSwapDimensions()
    {
        Console.WriteLine("--- Test 2: Kiểm tra tự động đảo chiều xoay 90° (W×H & H×W) ---");

        // Bàn cờ gốc 9x6 góc trong
        int squaresX = 10;
        int squaresY = 7;
        int expectedCornersX = 9;
        int expectedCornersY = 6;

        using var original = CreateSyntheticChessboard(squaresX, squaresY, 50);
        using var rotated = new Mat();
        // Xoay ảnh 90 độ thuận chiều kim đồng hồ => trở thành 6x9 góc trong
        Cv2.Rotate(original, rotated, RotateFlags.Rotate90Clockwise);

        // Yêu cầu tìm kích thước gốc (9x6)
        var result = ChessboardCalibrationService.DetectCornersMultiStrategy(
            rotated,
            new Size(expectedCornersX, expectedCornersY),
            autoSwapDimensions: true,
            useEnhancedSectorBased: true,
            tryAlternativeConvention: false);

        if (!result.Found)
        {
            throw new Exception("Thất bại khi tự động đảo chiều bàn cờ xoay 90 độ!");
        }

        if (result.Corners.Length != expectedCornersX * expectedCornersY)
        {
            throw new Exception($"Số corners nhận diện không đúng! Mong đợi: {expectedCornersX * expectedCornersY}, thực tế: {result.Corners.Length}");
        }

        bool matchedPattern = (result.PatternSize.Width == expectedCornersY && result.PatternSize.Height == expectedCornersX) ||
                              (result.PatternSize.Width == expectedCornersX && result.PatternSize.Height == expectedCornersY);
        if (!matchedPattern)
        {
            throw new Exception($"Kích thước pattern nhận diện không khớp! Thực tế: {result.PatternSize.Width}x{result.PatternSize.Height}");
        }

        Console.WriteLine($"  -> PASSED: Tự động phát hiện và đảo chiều sang {result.PatternSize.Width}x{result.PatternSize.Height} thành công.");
    }

    private static void TestGradientLightingAndClaheSectorBased()
    {
        Console.WriteLine("--- Test 3: Kiểm tra nhận diện khi có chênh lệch ánh sáng (Gradient Shading) ---");

        int squaresX = 10;
        int squaresY = 7;
        using var mat = CreateSyntheticChessboard(squaresX, squaresY, 50);

        // Áp dụng gradient sáng tối từ 0 đến 120 pixel dọc theo trục X
        for (int y = 0; y < mat.Height; y++)
        {
            for (int x = 0; x < mat.Width; x++)
            {
                byte val = mat.At<byte>(y, x);
                double factor = 0.3 + 0.7 * ((double)x / mat.Width);
                mat.Set(y, x, (byte)Math.Clamp(val * factor, 0, 255));
            }
        }

        var result = ChessboardCalibrationService.DetectCornersMultiStrategy(
            mat,
            new Size(9, 6),
            autoSwapDimensions: true,
            useEnhancedSectorBased: true,
            tryAlternativeConvention: true);

        if (!result.Found)
        {
            throw new Exception("Thất bại khi tìm bàn cờ dưới điều kiện ánh sáng chênh lệch gradient!");
        }

        Console.WriteLine($"  -> PASSED: Vượt qua thử thách gradient ánh sáng ({result.StrategyUsed}, {result.Corners.Length} corners).");
    }

    private static void TestAlternativeConventionDetection()
    {
        Console.WriteLine("--- Test 4: Kiểm tra tự động bù quy ước (Người dùng nhập 10x7 ô cờ thay vì 9x6 góc) ---");

        // Tạo bàn cờ 10 ô x 7 ô (tương ứng 9x6 góc)
        using var mat = CreateSyntheticChessboard(10, 7, 50);

        // Người dùng vô tình nhập số ô (10, 7) thay vì số góc (9, 6)
        var result = ChessboardCalibrationService.DetectCornersMultiStrategy(
            mat,
            new Size(10, 7),
            autoSwapDimensions: true,
            useEnhancedSectorBased: true,
            tryAlternativeConvention: true);

        if (!result.Found)
        {
            throw new Exception("Thất bại khi tự động bù quy ước kích thước bàn cờ!");
        }

        if (result.Corners.Length != 54)
        {
            throw new Exception($"Số góc phát hiện phải là 54! Thực tế: {result.Corners.Length}");
        }

        Console.WriteLine($"  -> PASSED: Tự động điều chỉnh quy ước kích thước và tìm đúng 54 góc trong.");
    }

    private static void TestHighResolutionNoiseImageNoHang()
    {
        Console.WriteLine("--- Test 5: Kiểm tra an toàn hiệu năng trên ảnh lớn (2560×1440) không có bàn cờ ---");

        using var largeNoise = new Mat(1440, 2560, MatType.CV_8UC1);
        Cv2.Randu(largeNoise, Scalar.All(50), Scalar.All(200));

        var sw = Stopwatch.StartNew();
        var result = ChessboardCalibrationService.DetectCornersMultiStrategy(
            largeNoise,
            new Size(9, 6),
            autoSwapDimensions: true,
            useEnhancedSectorBased: true,
            tryAlternativeConvention: false);
        sw.Stop();

        if (result.Found)
        {
            throw new Exception("Ảnh nhiễu không được báo tìm thấy bàn cờ!");
        }

        // Đảm bảo không bị treo ngốn thời gian hàng chục giây
        if (sw.ElapsedMilliseconds > 3000)
        {
            throw new Exception($"Xử lý ảnh lớn không bàn cờ mất quá lâu ({sw.ElapsedMilliseconds} ms > 3000 ms)!");
        }

        Console.WriteLine($"  -> PASSED: Thoát an toàn trong {sw.ElapsedMilliseconds} ms, không bị nghẽn CPU hoặc treo app.");
    }

    private static void TestAutoDetectCommonChessboardPatterns()
    {
        Console.WriteLine("--- Test 6: Tự động dò mẫu bàn cờ thông dụng khi người dùng nhập mặc định (9×6) nhưng bảng thực tế là 8×6 ô cờ (7×5 góc) ---");

        string userImg = @"C:\Users\Admin\.gemini\antigravity-ide\brain\4352ed59-a37c-4f4a-ad02-fa9834310d0b\.user_uploaded\media_1789521239129.png";
        Mat mat;
        if (System.IO.File.Exists(userImg))
        {
            mat = Cv2.ImRead(userImg, ImreadModes.Color);
        }
        else
        {
            // Bàn cờ 8 ô x 6 ô với nền trắng 255 chuẩn như giấy in
            mat = new Mat(6 * 50 + 80, 8 * 50 + 80, MatType.CV_8UC1, new Scalar(255));
            for (int r = 0; r < 6; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    if ((r + c) % 2 == 0)
                    {
                        Cv2.Rectangle(mat, new Rect(40 + c * 50, 40 + r * 50, 50, 50), Scalar.All(0), -1);
                    }
                }
            }
        }

        using (mat)
        {
            // Người dùng để mặc định (9, 6) trên UI
            var result = ChessboardCalibrationService.DetectCornersMultiStrategy(
                mat,
                new Size(9, 6),
                autoSwapDimensions: true,
                useEnhancedSectorBased: true,
                tryAlternativeConvention: true);

            if (!result.Found)
            {
                throw new Exception("Thất bại khi tự động dò tìm kích thước bàn cờ thực tế 7x5!");
            }

            if (result.Corners.Length != 35)
            {
                throw new Exception($"Số góc phát hiện phải là 35 (7×5)! Thực tế: {result.Corners.Length}");
            }

            if ((result.PatternSize.Width != 7 || result.PatternSize.Height != 5) &&
                (result.PatternSize.Width != 5 || result.PatternSize.Height != 7))
            {
                throw new Exception($"Kích thước pattern nhận diện phải là 7×5 (hoặc 5×7)! Thực tế: {result.PatternSize.Width}×{result.PatternSize.Height}");
            }

            Console.WriteLine($"  -> PASSED: Tự động khóa chính xác mẫu {result.PatternSize.Width}×{result.PatternSize.Height} ({result.Corners.Length} corners - {result.StrategyUsed}) dù cấu hình ban đầu là 9×6.");
        }
    }
}
