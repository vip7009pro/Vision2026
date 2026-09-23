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

            // Bổ sung các kích thước bàn cờ công nghiệp thông dụng làm fallback
            // Giúp tự động nhận diện thành công ngay cả khi người dùng để mặc định (9×6) hoặc đếm lệch ô/góc
            var commonFallbackSizes = new[]
            {
                new Size(7, 5), new Size(5, 7),
                new Size(8, 6), new Size(6, 8),
                new Size(8, 5), new Size(5, 8),
                new Size(7, 6), new Size(6, 7),
                new Size(6, 4), new Size(4, 6),
                new Size(7, 4), new Size(4, 7),
                new Size(5, 4), new Size(4, 5)
            };
            foreach (var cs in commonFallbackSizes)
            {
                if (!candidateSizes.Any(s => s.Width == cs.Width && s.Height == cs.Height))
                {
                    candidateSizes.Add(cs);
                }
            }
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
    /// Suy luận kích thước lưới bàn cờ (Width, Height) dựa trên số lượng góc thực tế và gợi ý ban đầu.
    /// Giúp giải quyết các trường hợp người dùng nhập số ô vuông thay vì số góc trong (ví dụ 8x6 ô cờ -> 7x5 góc),
    /// hoặc ảnh bị xoay 90 độ (Width và Height bị đảo chiều).
    /// </summary>
    public static Size InferPatternSize(int cornerCount, Size hintPatternSize)
    {
        if (cornerCount <= 0)
            return new Size(0, 0);

        int hw = Math.Max(1, hintPatternSize.Width);
        int hh = Math.Max(1, hintPatternSize.Height);

        // 1. Khớp chính xác với hintPatternSize
        if (hw * hh == cornerCount)
            return hintPatternSize;

        // 2. Khớp với chiều xoay đảo ngược (h x w)
        if (hh * hw == cornerCount)
            return new Size(hh, hw);

        // 3. Khớp quy ước số ô vuông trừ 1: (hw - 1) x (hh - 1) (ví dụ: 8x6 ô cờ => 7x5 = 35 góc)
        if (hw > 2 && hh > 2)
        {
            if ((hw - 1) * (hh - 1) == cornerCount)
                return new Size(hw - 1, hh - 1);
            if ((hh - 1) * (hw - 1) == cornerCount)
                return new Size(hh - 1, hw - 1);
        }

        // 4. Khớp quy ước số góc cộng 1: (hw + 1) x (hh + 1)
        if ((hw + 1) * (hh + 1) == cornerCount)
            return new Size(hw + 1, hh + 1);
        if ((hh + 1) * (hw + 1) == cornerCount)
            return new Size(hh + 1, hw + 1);

        // 5. Kiểm tra nếu chia hết cho (hw - 1) hoặc (hh - 1)
        if (hw > 2 && cornerCount % (hw - 1) == 0 && (cornerCount / (hw - 1)) >= 2)
            return new Size(hw - 1, cornerCount / (hw - 1));
        if (hh > 2 && cornerCount % (hh - 1) == 0 && (cornerCount / (hh - 1)) >= 2)
            return new Size(cornerCount / (hh - 1), hh - 1);

        // 6. Kiểm tra nếu chia hết cho hw hoặc hh
        if (hw >= 2 && cornerCount % hw == 0 && (cornerCount / hw) >= 2)
            return new Size(hw, cornerCount / hw);
        if (hh >= 2 && cornerCount % hh == 0 && (cornerCount / hh) >= 2)
            return new Size(cornerCount / hh, hh);

        // 7. Tìm cặp thừa số (w, h) của cornerCount gần nhất với tỉ lệ hw/hh
        var factorPairs = new List<Size>();
        for (int w = 2; w * w <= cornerCount; w++)
        {
            if (cornerCount % w == 0)
            {
                int h = cornerCount / w;
                factorPairs.Add(new Size(w, h));
                factorPairs.Add(new Size(h, w));
            }
        }

        if (factorPairs.Count > 0)
        {
            return factorPairs
                .OrderBy(p => Math.Abs(p.Width - hw) + Math.Abs(p.Height - hh))
                .First();
        }

        return new Size(cornerCount, 1);
    }

    /// <summary>
    /// Calibrate camera using multiple chessboard images.
    /// Returns (success, cameraMatrix, distCoeffs, reprojectionError, pixelsPerMm, errorMessage).
    /// Requires at least 3 images with successfully detected corners.
    /// Tự động đồng bộ số điểm object points và image points cho từng ảnh, chống lỗi OpenCVException.
    /// </summary>
    public static ChessboardCalibrationResult Calibrate(
        List<Point2f[]> allCorners,
        Size imageSize,
        Size patternSize,
        double squareSizeMm,
        IReadOnlyList<Size>? perViewPatternSizes = null)
    {
        if (allCorners is null || allCorners.Count < 3)
        {
            return new ChessboardCalibrationResult(false, null, null, double.MaxValue, 0,
                "Cần ít nhất 3 ảnh có góc bàn cờ hợp lệ.");
        }

        if (imageSize.Width <= 0 || imageSize.Height <= 0)
        {
            return new ChessboardCalibrationResult(false, null, null, double.MaxValue, 0,
                "Kích thước ảnh không hợp lệ.");
        }

        if (squareSizeMm <= 0)
        {
            return new ChessboardCalibrationResult(false, null, null, double.MaxValue, 0,
                "Kích thước ô cờ (SquareSizeMm) phải lớn hơn 0.");
        }

        // 1. Phân tích và ghép nối từng ảnh với pattern size tương ứng
        var candidateViews = new List<(Point2f[] Corners, Size ViewPatternSize)>();
        for (int i = 0; i < allCorners.Count; i++)
        {
            var corners = allCorners[i];
            if (corners is null || corners.Length < 4)
                continue;

            Size viewSize;
            if (perViewPatternSizes is not null && i < perViewPatternSizes.Count &&
                perViewPatternSizes[i].Width * perViewPatternSizes[i].Height == corners.Length)
            {
                viewSize = perViewPatternSizes[i];
            }
            else if (patternSize.Width * patternSize.Height == corners.Length)
            {
                viewSize = patternSize;
            }
            else if (patternSize.Height * patternSize.Width == corners.Length)
            {
                viewSize = new Size(patternSize.Height, patternSize.Width);
            }
            else
            {
                viewSize = InferPatternSize(corners.Length, patternSize);
            }

            candidateViews.Add((corners, viewSize));
        }

        if (candidateViews.Count < 3)
        {
            return new ChessboardCalibrationResult(false, null, null, double.MaxValue, 0,
                $"Cần ít nhất 3 ảnh hợp lệ (hiện có {candidateViews.Count}).");
        }

        // 2. Nhóm theo số lượng góc để chọn nhóm đồng nhất chiếm đa số (dominant group)
        var groups = candidateViews
            .GroupBy(v => v.Corners.Length)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (groups.Count == 0 || groups[0].Count() < 3)
        {
            var summary = string.Join(", ", groups.Select(g => $"{g.Count()} ảnh có {g.Key} góc"));
            return new ChessboardCalibrationResult(false, null, null, double.MaxValue, 0,
                $"Số lượng góc bàn cờ không đồng nhất giữa các ảnh ({summary}). Cần ít nhất 3 ảnh có cùng kích thước góc.");
        }

        var dominantViews = groups[0].ToList();
        var basePatternSize = dominantViews[0].ViewPatternSize;

        // 3. Xây dựng danh sách object points và image points tương ứng chính xác 100%
        var objectPointsList = new List<IEnumerable<Point3f>>();
        var imagePointsList = new List<IEnumerable<Point2f>>();
        var validCornersList = new List<Point2f[]>();

        foreach (var view in dominantViews)
        {
            var pSize = view.ViewPatternSize;
            // Đảm bảo tuyệt đối pSize khớp với corners.Length
            if (pSize.Width * pSize.Height != view.Corners.Length)
            {
                pSize = InferPatternSize(view.Corners.Length, patternSize);
            }

            var objPts = GenerateObjectPoints(pSize, squareSizeMm);
            if (objPts.Count != view.Corners.Length)
            {
                // Phòng ngừa tuyệt đối sai lệch điểm
                continue;
            }

            objectPointsList.Add(objPts);
            imagePointsList.Add(view.Corners);
            validCornersList.Add(view.Corners);
        }

        if (objectPointsList.Count < 3)
        {
            return new ChessboardCalibrationResult(false, null, null, double.MaxValue, 0,
                "Không đủ ảnh hợp lệ sau khi đồng bộ kích thước lưới bàn cờ.");
        }

        var cameraMatrix = new double[3, 3];
        var distCoeffs = new double[5];

        try
        {
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
            double pxPerMm = ComputePixelsPerMm(validCornersList, basePatternSize, squareSizeMm);

            return new ChessboardCalibrationResult(true, cameraMatrix, distCoeffs, rpe, pxPerMm);
        }
        catch (OpenCvSharp.OpenCVException cvEx)
        {
            return new ChessboardCalibrationResult(false, null, null, double.MaxValue, 0,
                $"Lỗi OpenCV CalibrateCamera: {cvEx.Message}");
        }
        catch (Exception ex)
        {
            return new ChessboardCalibrationResult(false, null, null, double.MaxValue, 0,
                $"Lỗi CalibrateCamera: {ex.Message}");
        }
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

        double fx = calibData.Fx;
        double fy = calibData.Fy;
        double cx = calibData.Cx;
        double cy = calibData.Cy;

        // Tự động điều chỉnh ma trận camera K theo tỉ lệ độ phân giải của ảnh nguồn src
        // Giúp bảo toàn 100% kích thước và quang tâm khi calibrate ở một độ phân giải nhưng áp dụng cho ảnh 20MP (5472x3648)
        if (calibData.ImageWidth > 0 && calibData.ImageHeight > 0 &&
            (src.Width != calibData.ImageWidth || src.Height != calibData.ImageHeight))
        {
            double scaleX = (double)src.Width / calibData.ImageWidth;
            double scaleY = (double)src.Height / calibData.ImageHeight;
            fx *= scaleX;
            fy *= scaleY;
            cx *= scaleX;
            cy *= scaleY;
        }
        else if ((calibData.ImageWidth <= 0 || calibData.ImageHeight <= 0) &&
                 (cx < src.Width * 0.25 || cy < src.Height * 0.25))
        {
            // Dự phòng thông minh cho dữ liệu calib cũ chưa lưu ImageWidth/Height (ví dụ cx=320, cy=240 khi nắn ảnh 20MP)
            double approxCalibW = cx * 2.0;
            double approxCalibH = cy * 2.0;
            if (approxCalibW > 100 && approxCalibH > 100)
            {
                double scaleX = (double)src.Width / approxCalibW;
                double scaleY = (double)src.Height / approxCalibH;
                fx *= scaleX;
                fy *= scaleY;
                cx *= scaleX;
                cy *= scaleY;
            }
        }

        var cameraMatrix = new double[3, 3];
        cameraMatrix[0, 0] = fx;
        cameraMatrix[1, 1] = fy;
        cameraMatrix[0, 2] = cx;
        cameraMatrix[1, 2] = cy;
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
            // Bảo toàn ma trận camera gốc K: giữ nguyên 100% quang tâm (Cx, Cy), tiêu cự (Fx, Fy)
            // và hệ số PixelsPerMm vật lý, tránh hiện tượng phóng to (zoom in) / dịch chuyển pixel làm trôi dạt ROI của các Tool
            using var map1 = new Mat();
            using var map2 = new Mat();
            Cv2.InitUndistortRectifyMap(camMat, distMat, new Mat(), camMat, src.Size(), MatType.CV_32FC1, map1, map2);

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

    /// <summary>
    /// Xuất thông số hiệu chuẩn camera ra tệp JSON bất kỳ để lưu trữ hoặc chia sẻ.
    /// </summary>
    public static bool ExportCalibration(ChessboardCalibrationData data, string filePath)
    {
        if (data is null || string.IsNullOrWhiteSpace(filePath)) return false;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };
            var json = System.Text.Json.JsonSerializer.Serialize(data, options);
            System.IO.File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChessboardCalibrationService] Error exporting calibration to '{filePath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Nhập thông số hiệu chuẩn camera từ tệp JSON.
    /// </summary>
    public static (bool Success, ChessboardCalibrationData? Data, string ErrorMessage) ImportCalibration(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return (false, null, "Đường dẫn tệp không hợp lệ.");

        if (!System.IO.File.Exists(filePath))
            return (false, null, $"Tệp không tồn tại: {filePath}");

        try
        {
            var json = System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return (false, null, "Nội dung tệp rỗng.");

            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var data = System.Text.Json.JsonSerializer.Deserialize<ChessboardCalibrationData>(json, options);
            if (data is null)
                return (false, null, "Không thể giải mã dữ liệu hiệu chuẩn từ JSON.");

            if (!data.IsCalibrated || data.Fx <= 0 || data.Fy <= 0)
            {
                return (false, null, "Dữ liệu trong tệp không hợp lệ hoặc chưa được hiệu chuẩn (Focal fx/fy <= 0).");
            }

            data.DistCoeffs ??= Array.Empty<double>();
            return (true, data, string.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChessboardCalibrationService] Error importing calibration from '{filePath}': {ex.Message}");
            return (false, null, $"Lỗi định dạng tệp: {ex.Message}");
        }
    }
}

public sealed record ChessboardCalibrationResult(
    bool Success,
    double[,]? CameraMatrix,
    double[]? DistCoeffs,
    double ReprojectionError,
    double PixelsPerMm,
    string? ErrorMessage = null);

