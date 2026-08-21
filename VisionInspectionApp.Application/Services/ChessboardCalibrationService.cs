using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

public static class ChessboardCalibrationService
{
    /// <summary>
    /// Detect inner corners of chessboard pattern.
    /// patternSize = (innerCornersPerRow, innerCornersPerCol) = (boardCols-1, boardRows-1)
    /// </summary>
    public static (bool Found, Point2f[] Corners) DetectCorners(Mat image, Size patternSize)
    {
        if (image is null || image.IsDisposed || image.Empty())
            return (false, Array.Empty<Point2f>());

        using var gray = new Mat();
        if (image.Channels() > 1)
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        else
            image.CopyTo(gray);

        var corners = new Point2f[0];
        var flags = ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage | ChessboardFlags.FastCheck;
        bool found = Cv2.FindChessboardCorners(gray, patternSize, out corners, flags);

        if (found && corners.Length > 0)
        {
            // Sub-pixel refinement for higher accuracy
            var criteria = new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.001);
            Cv2.CornerSubPix(gray, corners, new Size(11, 11), new Size(-1, -1), criteria);
        }

        return (found, corners);
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

    private static readonly object _fileLock = new();

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
    /// Tự động áp dụng Global calibration cho VisionConfig nếu Job chưa có cấu hình riêng.
    /// </summary>
    public static bool EnsureCalibration(VisionConfig config)
    {
        if (config is null) return false;
        if (config.ChessboardCalibration is not null && config.ChessboardCalibration.IsCalibrated)
        {
            return true; // Đã có cấu hình riêng của Job
        }

        var globalCal = GetGlobalCalibration();
        if (globalCal is not null && globalCal.IsCalibrated)
        {
            config.ChessboardCalibration = globalCal.Clone();
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
