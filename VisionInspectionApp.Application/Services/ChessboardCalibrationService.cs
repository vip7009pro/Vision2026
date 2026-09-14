using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

public static class ChessboardCalibrationService
{
    /// <summary>
    /// Kết quả phát hiện góc bàn cờ kèm thông tin kích thước và chiến lược phát hiện thành công.
    /// </summary>
    public sealed record ChessboardDetectionResult(
        bool Found,
        Point2f[] Corners,
        Size PatternSize,
        string StrategyUsed);

    /// <summary>
    /// Detect inner corners of chessboard pattern sử dụng cơ chế đa chiến lược (Multi-Strategy):
    /// 1. Sector-Based SB (thuật toán Duda & Frese OpenCV 4 siêu nhạy, miễn nhiễm méo và chênh lệch sáng)
    /// 2. Tự động thử đảo chiều xoay 90 độ (W×H & H×W)
    /// 3. Thử nghiệm quy ước kích thước thay thế (Inner Corners vs Square count)
    /// 4. Tiền xử lý tăng cường tương phản CLAHE
    /// 5. Pyramid Downscale 0.5x cho ảnh phân giải cao
    /// 6. Fallback cổ điển AdaptiveThresh + NormalizeImage
    /// </summary>
    public static ChessboardDetectionResult DetectCornersMultiStrategy(
        Mat image,
        Size requestedPatternSize,
        bool autoSwapDimensions = true,
        bool useEnhancedSectorBased = true,
        bool tryAlternativeConvention = true)
    {
        if (image is null || image.IsDisposed || image.Empty() || requestedPatternSize.Width < 2 || requestedPatternSize.Height < 2)
        {
            return new ChessboardDetectionResult(false, Array.Empty<Point2f>(), requestedPatternSize, "Invalid Input");
        }

        using var gray = new Mat();
        if (image.Channels() > 1)
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        else
            image.CopyTo(gray);

        // Xây dựng danh sách các kích thước pattern ứng viên
        var candidateSizes = new List<Size> { requestedPatternSize };

        if (autoSwapDimensions && requestedPatternSize.Width != requestedPatternSize.Height)
        {
            candidateSizes.Add(new Size(requestedPatternSize.Height, requestedPatternSize.Width));
        }

        if (tryAlternativeConvention)
        {
            int w = requestedPatternSize.Width;
            int h = requestedPatternSize.Height;

            // Nếu người dùng nhập số ô cờ (Cols, Rows) thì góc trong là (w-1, h-1)
            if (w > 2 && h > 2)
            {
                candidateSizes.Add(new Size(w - 1, h - 1));
                if (autoSwapDimensions && w != h)
                    candidateSizes.Add(new Size(h - 1, w - 1));
            }

            // Nếu người dùng nhập số góc trong nhưng code trước đó trừ 1 thì bù lại (w+1, h+1)
            candidateSizes.Add(new Size(w + 1, h + 1));
            if (autoSwapDimensions && w != h)
                candidateSizes.Add(new Size(h + 1, w + 1));
        }

        // Lọc bỏ trùng lặp và kích thước không hợp lệ
        var uniqueSizes = candidateSizes
            .Where(s => s.Width >= 2 && s.Height >= 2)
            .GroupBy(s => (s.Width, s.Height))
            .Select(g => g.First())
            .ToList();

        var subPixCriteria = new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.001);
        var totalWatch = Stopwatch.StartNew();
        const int MaxTimeBudgetMs = 2500; // Ngân sách thời gian tối đa để đảm bảo UI mượt mà, không bao giờ treo

        // 1. Thử Sector-Based SB (Thuật toán OpenCV 4 hiện đại nhất - cực nhạy, tự động sub-pixel)
        if (useEnhancedSectorBased)
        {
            foreach (var pSize in uniqueSizes)
            {
                if (totalWatch.ElapsedMilliseconds > MaxTimeBudgetMs) break;

                var sbCorners = new Point2f[0];
                try
                {
                    if (Cv2.FindChessboardCornersSB(gray, pSize, out sbCorners, ChessboardFlags.None) &&
                        sbCorners.Length == pSize.Width * pSize.Height)
                    {
                        return new ChessboardDetectionResult(true, sbCorners, pSize, $"Sector-Based SB ({pSize.Width}×{pSize.Height})");
                    }
                }
                catch { }
            }

            // 2. Thử Pyramid Downscale 0.5x cho ảnh lớn (>1600px)
            if ((gray.Width > 1600 || gray.Height > 1200) && totalWatch.ElapsedMilliseconds <= MaxTimeBudgetMs)
            {
                using var smallGray = new Mat();
                double scale = 0.5;
                Cv2.Resize(gray, smallGray, new Size(gray.Width * scale, gray.Height * scale), 0, 0, InterpolationFlags.Area);

                foreach (var pSize in uniqueSizes)
                {
                    if (totalWatch.ElapsedMilliseconds > MaxTimeBudgetMs) break;

                    var smallCorners = new Point2f[0];
                    try
                    {
                        if (Cv2.FindChessboardCornersSB(smallGray, pSize, out smallCorners, ChessboardFlags.None) &&
                            smallCorners.Length == pSize.Width * pSize.Height)
                        {
                            var scaledCorners = smallCorners.Select(p => new Point2f((float)(p.X / scale), (float)(p.Y / scale))).ToArray();
                            Cv2.CornerSubPix(gray, scaledCorners, new Size(11, 11), new Size(-1, -1), subPixCriteria);
                            return new ChessboardDetectionResult(true, scaledCorners, pSize, $"Sector-Based SB Pyramid ({pSize.Width}×{pSize.Height})");
                        }
                    }
                    catch { }
                }
            }

            // 3. Thử với CLAHE (Tăng tương phản cục bộ chống ánh sáng chói / bóng mờ)
            if (totalWatch.ElapsedMilliseconds <= MaxTimeBudgetMs)
            {
                try
                {
                    using var clahe = Cv2.CreateCLAHE(clipLimit: 3.0, tileGridSize: new Size(8, 8));
                    using var claheGray = new Mat();
                    clahe.Apply(gray, claheGray);

                    foreach (var pSize in uniqueSizes)
                    {
                        if (totalWatch.ElapsedMilliseconds > MaxTimeBudgetMs) break;

                        var claheCorners = new Point2f[0];
                        if (Cv2.FindChessboardCornersSB(claheGray, pSize, out claheCorners, ChessboardFlags.None) &&
                            claheCorners.Length == pSize.Width * pSize.Height)
                        {
                            Cv2.CornerSubPix(gray, claheCorners, new Size(11, 11), new Size(-1, -1), subPixCriteria);
                            return new ChessboardDetectionResult(true, claheCorners, pSize, $"Sector-Based SB + CLAHE ({pSize.Width}×{pSize.Height})");
                        }
                    }
                }
                catch { }
            }
        }

        // 4. Fallback cổ điển: Cv2.FindChessboardCorners
        // Bắt buộc kèm FastCheck để kiểm tra nhanh trong <1ms, triệt tiêu nguy cơ bùng nổ tổ hợp làm treo máy khi không có chessboard
        if (totalWatch.ElapsedMilliseconds <= MaxTimeBudgetMs)
        {
            var classicFlags = ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage | ChessboardFlags.FastCheck;

            // Nếu ảnh lớn (>1280px), kiểm tra trên ảnh downscale trước để tiết kiệm CPU
            if (gray.Width > 1280 || gray.Height > 960)
            {
                double scale = 0.5;
                using var smallGray = new Mat();
                Cv2.Resize(gray, smallGray, new Size(gray.Width * scale, gray.Height * scale), 0, 0, InterpolationFlags.Area);

                foreach (var pSize in uniqueSizes)
                {
                    if (totalWatch.ElapsedMilliseconds > MaxTimeBudgetMs) break;

                    var smallCorners = new Point2f[0];
                    try
                    {
                        if (Cv2.FindChessboardCorners(smallGray, pSize, out smallCorners, classicFlags) &&
                            smallCorners.Length == pSize.Width * pSize.Height)
                        {
                            var scaledCorners = smallCorners.Select(p => new Point2f((float)(p.X / scale), (float)(p.Y / scale))).ToArray();
                            Cv2.CornerSubPix(gray, scaledCorners, new Size(11, 11), new Size(-1, -1), subPixCriteria);
                            return new ChessboardDetectionResult(true, scaledCorners, pSize, $"Classic AdaptiveThresh Pyramid ({pSize.Width}×{pSize.Height})");
                        }
                    }
                    catch { }
                }
            }
            else
            {
                foreach (var pSize in uniqueSizes)
                {
                    if (totalWatch.ElapsedMilliseconds > MaxTimeBudgetMs) break;

                    var classicCorners = new Point2f[0];
                    try
                    {
                        if (Cv2.FindChessboardCorners(gray, pSize, out classicCorners, classicFlags) &&
                            classicCorners.Length == pSize.Width * pSize.Height)
                        {
                            Cv2.CornerSubPix(gray, classicCorners, new Size(11, 11), new Size(-1, -1), subPixCriteria);
                            return new ChessboardDetectionResult(true, classicCorners, pSize, $"Classic AdaptiveThresh ({pSize.Width}×{pSize.Height})");
                        }
                    }
                    catch { }
                }
            }
        }

        return new ChessboardDetectionResult(false, Array.Empty<Point2f>(), requestedPatternSize, "Not Found");
    }

    /// <summary>
    /// Detect inner corners of chessboard pattern (Tương thích ngược, tự động dùng Multi-Strategy).
    /// </summary>
    public static (bool Found, Point2f[] Corners) DetectCorners(Mat image, Size patternSize)
    {
        var result = DetectCornersMultiStrategy(image, patternSize, autoSwapDimensions: true, useEnhancedSectorBased: true, tryAlternativeConvention: true);
        return (result.Found, result.Corners);
    }

    /// <summary>
    /// Draw detected chessboard corners on a clone of the image.
    /// Returns a new Mat with corners drawn.
    /// </summary>
    public static Mat DrawCorners(Mat image, Size patternSize, Point2f[] corners, bool found)
    {
        var output = image.Clone();
        Cv2.DrawChessboardCorners(output, patternSize, corners, found);
        return output;
    }

    /// <summary>
    /// Generate 3D object points for the chessboard.
    /// Each corner maps to (col * squareSize, row * squareSize, 0).
    /// </summary>
    public static List<Point3f> GenerateObjectPoints(Size patternSize, double squareSizeMm)
    {
        var objPts = new List<Point3f>();
        for (int row = 0; row < patternSize.Height; row++)
        {
            for (int col = 0; col < patternSize.Width; col++)
            {
                objPts.Add(new Point3f((float)(col * squareSizeMm), (float)(row * squareSizeMm), 0f));
            }
        }
        return objPts;
    }

    /// <summary>
    /// Calibrate camera using multiple chessboard images.
    /// Returns (success, cameraMatrix, distCoeffs, reprojectionError, rvecs, tvecs).
    /// Requires at least 3 images with successfully detected corners.
    /// </summary>
    public static ChessboardCalibrationResult Calibrate(
        List<Point2f[]> allCorners,
        Size imageSize,
        Size patternSize,
        double squareSizeMm)
    {
        if (allCorners is null || allCorners.Count < 3)
        {
            return new ChessboardCalibrationResult(false, null, null, double.MaxValue, 0);
        }

        var objPointsTemplate = GenerateObjectPoints(patternSize, squareSizeMm);

        // Build list of object points and image points
        var objectPointsList = new List<IEnumerable<Point3f>>();
        var imagePointsList = new List<IEnumerable<Point2f>>();

        foreach (var corners in allCorners)
        {
            objectPointsList.Add(objPointsTemplate);
            imagePointsList.Add(corners);
        }

        var cameraMatrix = new double[3, 3];
        var distCoeffs = new double[5];

        double rpe = Cv2.CalibrateCamera(
            objectPointsList,
            imagePointsList,
            imageSize,
            cameraMatrix,
            distCoeffs,
            out var rvecs,
            out var tvecs,
            CalibrationFlags.None);

        // Compute pixels per mm from focal length and square size
        double fx = cameraMatrix[0, 0];
        double fy = cameraMatrix[1, 1];

        // Compute average distance between adjacent corners in pixels (more robust px/mm estimate)
        double pxPerMm = ComputePixelsPerMm(allCorners, patternSize, squareSizeMm);

        return new ChessboardCalibrationResult(true, cameraMatrix, distCoeffs, rpe, pxPerMm);
    }

    /// <summary>
    /// Compute pixels per mm by averaging distance between adjacent corners across all images.
    /// </summary>
    public static double ComputePixelsPerMm(List<Point2f[]> allCorners, Size patternSize, double squareSizeMm)
    {
        if (allCorners is null || allCorners.Count == 0 || squareSizeMm <= 0)
            return 0;

        var distances = new List<double>();

        foreach (var corners in allCorners)
        {
            int cols = patternSize.Width;
            int rows = patternSize.Height;

            // Horizontal distances
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    int idx1 = r * cols + c;
                    int idx2 = r * cols + c + 1;
                    if (idx1 < corners.Length && idx2 < corners.Length)
                    {
                        double dx = corners[idx2].X - corners[idx1].X;
                        double dy = corners[idx2].Y - corners[idx1].Y;
                        distances.Add(Math.Sqrt(dx * dx + dy * dy));
                    }
                }
            }

            // Vertical distances
            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int idx1 = r * cols + c;
                    int idx2 = (r + 1) * cols + c;
                    if (idx1 < corners.Length && idx2 < corners.Length)
                    {
                        double dx = corners[idx2].X - corners[idx1].X;
                        double dy = corners[idx2].Y - corners[idx1].Y;
                        distances.Add(Math.Sqrt(dx * dx + dy * dy));
                    }
                }
            }
        }

        if (distances.Count == 0) return 0;
        double avgPixelDist = distances.Average();
        return avgPixelDist / squareSizeMm;
    }

    /// <summary>
    /// Undistort an image using calibration data.
    /// Uses GetOptimalNewCameraMatrix and InitUndistortRectifyMap to prevent boundary foldovers.
    /// </summary>
    public static Mat Undistort(Mat src, ChessboardCalibrationData calibData)
    {
        if (src is null || src.IsDisposed || src.Empty() || calibData is null || !calibData.IsCalibrated)
            return src?.Clone() ?? new Mat();

        if (calibData.Fx <= 10 || calibData.Fy <= 10 || calibData.Cx <= 0 || calibData.Cy <= 0)
            return src.Clone();

        var cameraMatrix = new double[3, 3];
        cameraMatrix[0, 0] = calibData.Fx;
        cameraMatrix[1, 1] = calibData.Fy;
        cameraMatrix[0, 2] = calibData.Cx;
        cameraMatrix[1, 2] = calibData.Cy;
        cameraMatrix[2, 2] = 1.0;

        var distCoeffs = calibData.DistCoeffs ?? Array.Empty<double>();

        // Sanitize distortion coefficients to prevent extreme mathematical divergence
        if (distCoeffs.Length > 0 && distCoeffs.Any(c => double.IsNaN(c) || double.IsInfinity(c) || Math.Abs(c) > 20.0))
        {
            return src.Clone();
        }

        using var camMat = Mat.FromArray(cameraMatrix);
        using var distMat = Mat.FromArray(distCoeffs);

        try
        {
            using var newCamMat = Cv2.GetOptimalNewCameraMatrix(camMat, distMat, src.Size(), 0.0, src.Size(), out var validRoi);
            using var map1 = new Mat();
            using var map2 = new Mat();
            Cv2.InitUndistortRectifyMap(camMat, distMat, new Mat(), newCamMat, src.Size(), MatType.CV_32FC1, map1, map2);

            var dst = new Mat();
            Cv2.Remap(src, dst, map1, map2, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
            return dst;
        }
        catch
        {
            var fallbackDst = new Mat();
            Cv2.Undistort(src, fallbackDst, camMat, distMat);
            return fallbackDst;
        }
    }

    // ==========================================
    // GLOBAL CALIBRATION MANAGEMENT
    // ==========================================

    private static readonly string GlobalCalibrationDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vision2026");

    private static readonly string GlobalCalibrationFilePath = System.IO.Path.Combine(
        GlobalCalibrationDir, "global_chessboard_calibration.json");

    private static readonly string GlobalCalibrationSettingsFilePath = System.IO.Path.Combine(
        GlobalCalibrationDir, "global_chessboard_settings.json");

    private static readonly object _fileLock = new();

    /// <summary>
    /// Cờ cưỡng chế áp dụng Global Calibration cho mọi Job (kể cả khi Job đã có Calib riêng).
    /// </summary>
    public static bool IsForceApplyGlobalCalibration { get; set; }

    static ChessboardCalibrationService()
    {
        IsForceApplyGlobalCalibration = LoadForceApplyGlobalCalibrationSetting();
    }

    private static bool LoadForceApplyGlobalCalibrationSetting()
    {
        try
        {
            lock (_fileLock)
            {
                if (!System.IO.File.Exists(GlobalCalibrationSettingsFilePath))
                    return false;

                var json = System.IO.File.ReadAllText(GlobalCalibrationSettingsFilePath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("forceApplyGlobalCalibration", out var prop))
                {
                    return prop.GetBoolean();
                }
                if (doc.RootElement.TryGetProperty("ForceApplyGlobalCalibration", out var propPascal))
                {
                    return propPascal.GetBoolean();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChessboardCalibrationService] Error reading global calibration settings: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Lưu cờ cưỡng chế áp dụng Global Calibration vào file cấu hình toàn cục.
    /// </summary>
    public static bool SaveForceApplyGlobalCalibration(bool enable)
    {
        IsForceApplyGlobalCalibration = enable;
        try
        {
            lock (_fileLock)
            {
                if (!System.IO.Directory.Exists(GlobalCalibrationDir))
                {
                    System.IO.Directory.CreateDirectory(GlobalCalibrationDir);
                }

                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                };
                var payload = new { ForceApplyGlobalCalibration = enable };
                var json = System.Text.Json.JsonSerializer.Serialize(payload, options);
                System.IO.File.WriteAllText(GlobalCalibrationSettingsFilePath, json);
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChessboardCalibrationService] Error saving global calibration settings: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Lưu cấu hình calibration làm Global mặc định cho toàn bộ ứng dụng.
    /// </summary>
    public static bool SaveGlobalCalibration(ChessboardCalibrationData data)
    {
        if (data is null) return false;
        try
        {
            lock (_fileLock)
            {
                if (!System.IO.Directory.Exists(GlobalCalibrationDir))
                {
                    System.IO.Directory.CreateDirectory(GlobalCalibrationDir);
                }

                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                };
                var json = System.Text.Json.JsonSerializer.Serialize(data, options);
                System.IO.File.WriteAllText(GlobalCalibrationFilePath, json);
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChessboardCalibrationService] Error saving global calibration: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Đọc cấu hình Global calibration nếu có.
    /// </summary>
    public static ChessboardCalibrationData? GetGlobalCalibration()
    {
        try
        {
            lock (_fileLock)
            {
                if (!System.IO.File.Exists(GlobalCalibrationFilePath))
                {
                    return null;
                }

                var json = System.IO.File.ReadAllText(GlobalCalibrationFilePath);
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var data = System.Text.Json.JsonSerializer.Deserialize<ChessboardCalibrationData>(json, options);
                if (data is not null && data.IsCalibrated)
                {
                    return data;
                }
                return null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChessboardCalibrationService] Error reading global calibration: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Kiểm tra hệ thống đã có Global calibration hợp lệ hay chưa.
    /// </summary>
    public static bool HasGlobalCalibration()
    {
        var cal = GetGlobalCalibration();
        return cal is not null && cal.IsCalibrated;
    }

    /// <summary>
    /// Lấy cấu hình Calibration có hiệu lực cho VisionConfig hiện tại (ưu tiên Global Calib nếu bật cờ cưỡng chế).
    /// </summary>
    public static ChessboardCalibrationData? GetEffectiveCalibration(VisionConfig? config)
    {
        var globalCal = GetGlobalCalibration();
        bool hasGlobal = globalCal is not null && globalCal.IsCalibrated;

        // Nếu bật cưỡng chế và đã có Global Calib -> luôn trả về Global Calib
        if (IsForceApplyGlobalCalibration && hasGlobal)
        {
            return globalCal;
        }

        // Nếu Job đã có calib riêng
        if (config?.ChessboardCalibration is not null && config.ChessboardCalibration.IsCalibrated)
        {
            return config.ChessboardCalibration;
        }

        // Nếu Job chưa có calib riêng nhưng hệ thống có Global Calib
        return hasGlobal ? globalCal : null;
    }

    /// <summary>
    /// Tự động áp dụng Global calibration cho VisionConfig.
    /// Nếu bật cờ cưỡng chế (IsForceApplyGlobalCalibration) và có Global Calib, sẽ ghi đè lên calib riêng của Job.
    /// Nếu không bật cưỡng chế, chỉ áp dụng nếu Job chưa có cấu hình riêng.
    /// </summary>
    public static bool EnsureCalibration(VisionConfig config)
    {
        if (config is null) return false;

        var globalCal = GetGlobalCalibration();
        bool hasGlobal = globalCal is not null && globalCal.IsCalibrated;

        // 1. Nếu đang bật cưỡng chế áp dụng Global Calib VÀ hệ thống đã có Global Calib
        if (IsForceApplyGlobalCalibration && hasGlobal)
        {
            config.ChessboardCalibration = globalCal!.Clone();
            config.PixelsPerMm = globalCal.PixelsPerMm;
            return true;
        }

        // 2. Nếu Job đã có cấu hình riêng hợp lệ
        if (config.ChessboardCalibration is not null && config.ChessboardCalibration.IsCalibrated)
        {
            return true; // Giữ nguyên cấu hình riêng của Job
        }

        // 3. Nếu Job chưa có cấu hình riêng nhưng hệ thống có Global Calib
        if (hasGlobal)
        {
            config.ChessboardCalibration = globalCal!.Clone();
            if (config.PixelsPerMm <= 0 || Math.Abs(config.PixelsPerMm - 1.0) < 1e-6)
            {
                config.PixelsPerMm = globalCal.PixelsPerMm;
            }
            return true;
        }

        return false;
    }
}

public sealed record ChessboardCalibrationResult(
    bool Success,
    double[,]? CameraMatrix,
    double[]? DistCoeffs,
    double ReprojectionError,
    double PixelsPerMm);
