using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.VisionEngine
{
    public struct DetectedObjectCandidate
    {
        public double X;
        public double Y;
        public double AngleDeg;
        public double Score;
        public double Width;
        public double Height;
    }

    public static class ObjectCountingEngine
    {
        private static readonly ConcurrentDictionary<string, Mat> _templateCache = new(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache()
        {
            foreach (var kvp in _templateCache)
            {
                kvp.Value?.Dispose();
            }
            _templateCache.Clear();
        }

        /// <summary>
        /// Thực hiện phát hiện và đếm tất cả các vật thể tương đồng với template trong vùng Search ROI,
        /// bất kể vật thể nằm ở vị trí nào và xoay theo phương hướng nào (từ MinAngle đến MaxAngle).
        /// </summary>
        public static (int FoundCount, bool Pass, List<DetectedObjectCandidate> Objects) CountObjects(
            Mat searchMat,
            Mat templateMat,
            ObjectCountingDefinition def,
            Rect searchRoiRect)
        {
            if (searchMat == null || searchMat.Empty() || templateMat == null || templateMat.Empty())
            {
                int zeroCount = 0;
                bool zeroPass = zeroCount >= def.MinCount && zeroCount <= def.MaxCount;
                return (zeroCount, zeroPass, new List<DetectedObjectCandidate>());
            }

            // Đảm bảo ROI nằm trọn trong ảnh tìm kiếm
            var boundedRoi = new Rect(
                Math.Clamp(searchRoiRect.X, 0, searchMat.Width - 1),
                Math.Clamp(searchRoiRect.Y, 0, searchMat.Height - 1),
                Math.Clamp(searchRoiRect.Width, 1, searchMat.Width - Math.Clamp(searchRoiRect.X, 0, searchMat.Width - 1)),
                Math.Clamp(searchRoiRect.Height, 1, searchMat.Height - Math.Clamp(searchRoiRect.Y, 0, searchMat.Height - 1))
            );

            if (boundedRoi.Width < 4 || boundedRoi.Height < 4)
            {
                int zeroCount = 0;
                bool zeroPass = zeroCount >= def.MinCount && zeroCount <= def.MaxCount;
                return (zeroCount, zeroPass, new List<DetectedObjectCandidate>());
            }

            using var searchCrop = new Mat(searchMat, boundedRoi);
            using var searchGray = searchCrop.Channels() == 1 ? searchCrop.Clone() : searchCrop.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var templateGray = templateMat.Channels() == 1 ? templateMat.Clone() : templateMat.CvtColor(ColorConversionCodes.BGR2GRAY);

            if (searchGray.Width < templateGray.Width || searchGray.Height < templateGray.Height)
            {
                int zeroCount = 0;
                bool zeroPass = zeroCount >= def.MinCount && zeroCount <= def.MaxCount;
                return (zeroCount, zeroPass, new List<DetectedObjectCandidate>());
            }

            double minScore = Math.Clamp(def.MinScore, 0.1, 1.0);
            double minAngle = Math.Min(def.MinAngle, def.MaxAngle);
            double maxAngle = Math.Max(def.MinAngle, def.MaxAngle);
            double step = Math.Clamp(def.AngleStep, 0.5, 30.0);
            double maxOverlap = Math.Clamp(def.MaxOverlap, 0.05, 0.95);
            int maxLimit = Math.Max(1, def.MaxFoundLimit);

            // Tự động áp dụng cửa sổ suy giảm biên (Apodization Window) để triệt tiêu nhiễu vân nền ở 4 góc template
            using var templProcessed = ApplyCircularWindow(templateGray);

            List<DetectedObjectCandidate> candidates;
            if (def.Algorithm == ObjectCountingAlgorithm.ShapeMatching)
            {
                candidates = DetectByShapeMatching(searchGray, templProcessed, minScore, minAngle, maxAngle, step, maxLimit);
            }
            else
            {
                candidates = DetectByNormalizedCorrelation(searchGray, templProcessed, minScore, minAngle, maxAngle, step, maxLimit);
            }

            // Chuyển tọa độ tương đối từ Search ROI về tọa độ thế giới (Toàn ảnh gốc)
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                c.X += boundedRoi.X;
                c.Y += boundedRoi.Y;
                candidates[i] = c;
            }

            // Áp dụng Non-Maximum Suppression (NMS) đa hướng xoay để đảm bảo mỗi vật thể chỉ đếm đúng 1 lần
            var finalObjects = ApplyRotatedNms(candidates, templateGray.Width, templateGray.Height, maxOverlap, maxLimit);

            int foundCount = finalObjects.Count;
            bool pass = foundCount >= def.MinCount && foundCount <= def.MaxCount;

            return (foundCount, pass, finalObjects);
        }

        #region Thuật toán 1: Multi-Angle Normalized Cross-Correlation (Pyramid Coarse-to-Fine)

        private static List<DetectedObjectCandidate> DetectByNormalizedCorrelation(
            Mat searchGray,
            Mat templateGray,
            double minScore,
            double minAngle,
            double maxAngle,
            double angleStep,
            int maxCandidates)
        {
            var rawCandidates = new ConcurrentBag<DetectedObjectCandidate>();

            // Tạo danh sách góc xoay cần quét
            var angles = new List<double>();
            for (double a = minAngle; a <= maxAngle + 1e-4; a += angleStep)
            {
                angles.Add(a);
            }
            if (!angles.Any(a => Math.Abs(a) < 1e-4) && minAngle <= 0 && maxAngle >= 0)
            {
                angles.Add(0.0);
                angles.Sort();
            }

            bool usePyramid = searchGray.Width >= 160 && searchGray.Height >= 160 &&
                              templateGray.Width >= 30 && templateGray.Height >= 30;

            if (usePyramid)
            {
                // Kim tự tháp Coarse-to-Fine: Level 1 (Downsampled 2x)
                using var searchL1 = new Mat();
                using var templL1 = new Mat();
                Cv2.PyrDown(searchGray, searchL1);
                Cv2.PyrDown(templateGray, templL1);

                double coarseScoreThresh = Math.Max(0.35, minScore * 0.75);
                double coarseAngleStep = Math.Clamp(angleStep * 1.5, 2.0, 10.0);

                var coarseAngles = new List<double>();
                for (double a = minAngle; a <= maxAngle + 1e-4; a += coarseAngleStep)
                {
                    coarseAngles.Add(a);
                }
                if (!coarseAngles.Any(a => Math.Abs(a) < 1e-4) && minAngle <= 0 && maxAngle >= 0)
                {
                    coarseAngles.Add(0.0);
                    coarseAngles.Sort();
                }

                var coarseHits = new ConcurrentBag<(double X, double Y, double Angle, double Score)>();

                Parallel.ForEach(coarseAngles, angle =>
                {
                    using var rotTempl = RotateImage(templL1, angle);
                    if (searchL1.Width < rotTempl.Width || searchL1.Height < rotTempl.Height) return;

                    using var res = new Mat();
                    Cv2.MatchTemplate(searchL1, rotTempl, res, TemplateMatchModes.CCoeffNormed);

                    // Tìm các đỉnh cực đại địa phương (Local Maxima)
                    FindLocalPeaks(res, coarseScoreThresh, (px, py, score) =>
                    {
                        double centerX = px + rotTempl.Width / 2.0;
                        double centerY = py + rotTempl.Height / 2.0;
                        coarseHits.Add((centerX, centerY, angle, score));
                    });
                });

                // Tinh chỉnh Level 0 quanh các ứng viên từ Level 1
                var topCoarse = coarseHits
                    .OrderByDescending(h => h.Score)
                    .Take(maxCandidates * 4)
                    .ToList();

                int searchPad = Math.Max(12, (int)(templateGray.Width * 0.25));

                Parallel.ForEach(topCoarse, hit =>
                {
                    double l0CenterX = hit.X * 2.0;
                    double l0CenterY = hit.Y * 2.0;

                    int subX = (int)Math.Max(0, l0CenterX - templateGray.Width / 2.0 - searchPad);
                    int subY = (int)Math.Max(0, l0CenterY - templateGray.Height / 2.0 - searchPad);
                    int subW = (int)Math.Min(searchGray.Width - subX, templateGray.Width + searchPad * 2);
                    int subH = (int)Math.Min(searchGray.Height - subY, templateGray.Height + searchPad * 2);

                    if (subW < templateGray.Width || subH < templateGray.Height) return;

                    using var subSearch = new Mat(searchGray, new Rect(subX, subY, subW, subH));

                    double bestLocalScore = 0;
                    double bestLocalAngle = hit.Angle;
                    double bestLocalX = l0CenterX;
                    double bestLocalY = l0CenterY;

                    // Tinh chỉnh góc với bước 1° quanh hit.Angle
                    for (double da = -coarseAngleStep; da <= coarseAngleStep + 1e-4; da += Math.Clamp(angleStep, 0.5, 2.0))
                    {
                        double curAngle = hit.Angle + da;
                        if (curAngle < minAngle || curAngle > maxAngle) continue;

                        using var rotTemplL0 = RotateImage(templateGray, curAngle);
                        if (subSearch.Width < rotTemplL0.Width || subSearch.Height < rotTemplL0.Height) continue;

                        using var subRes = new Mat();
                        Cv2.MatchTemplate(subSearch, rotTemplL0, subRes, TemplateMatchModes.CCoeffNormed);

                        Cv2.MinMaxLoc(subRes, out _, out double maxVal, out _, out Point maxLoc);
                        if (maxVal > bestLocalScore)
                        {
                            bestLocalScore = maxVal;
                            bestLocalAngle = curAngle;
                            bestLocalX = subX + maxLoc.X + rotTemplL0.Width / 2.0;
                            bestLocalY = subY + maxLoc.Y + rotTemplL0.Height / 2.0;
                        }
                    }

                    if (bestLocalScore >= minScore)
                    {
                        rawCandidates.Add(new DetectedObjectCandidate
                        {
                            X = bestLocalX,
                            Y = bestLocalY,
                            AngleDeg = bestLocalAngle,
                            Score = bestLocalScore,
                            Width = templateGray.Width,
                            Height = templateGray.Height
                        });
                    }
                });
            }
            else
            {
                // Đối với ảnh nhỏ: Chạy trực tiếp Level 0
                Parallel.ForEach(angles, angle =>
                {
                    using var rotTempl = RotateImage(templateGray, angle);
                    if (searchGray.Width < rotTempl.Width || searchGray.Height < rotTempl.Height) return;

                    using var res = new Mat();
                    Cv2.MatchTemplate(searchGray, rotTempl, res, TemplateMatchModes.CCoeffNormed);

                    FindLocalPeaks(res, minScore, (px, py, score) =>
                    {
                        double centerX = px + rotTempl.Width / 2.0;
                        double centerY = py + rotTempl.Height / 2.0;

                        rawCandidates.Add(new DetectedObjectCandidate
                        {
                            X = centerX,
                            Y = centerY,
                            AngleDeg = angle,
                            Score = score,
                            Width = templateGray.Width,
                            Height = templateGray.Height
                        });
                    });
                });
            }

            return rawCandidates.ToList();
        }

        #endregion

        #region Thuật toán 2: Multi-Angle Shape & Edge Gradient Matching

        private static List<DetectedObjectCandidate> DetectByShapeMatching(
            Mat searchGray,
            Mat templateGray,
            double minScore,
            double minAngle,
            double maxAngle,
            double angleStep,
            int maxCandidates)
        {
            // Trích xuất biên Canny hoặc Sobel gradient trên template để làm mô hình biên dạng
            using var templateEdges = new Mat();
            Cv2.Canny(templateGray, templateEdges, 50, 150);

            Cv2.FindContours(templateEdges, out Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxNone);

            var edgePoints = new List<Point>();
            foreach (var contour in contours)
            {
                if (contour.Length < 5) continue;
                // Lấy mẫu các điểm biên đều nhau để tăng tốc
                int stride = Math.Max(1, contour.Length / 40);
                for (int i = 0; i < contour.Length; i += stride)
                {
                    edgePoints.Add(contour[i]);
                }
            }

            if (edgePoints.Count < 8)
            {
                // Fallback nếu không trích xuất đủ biên: sử dụng correlation
                return DetectByNormalizedCorrelation(searchGray, templateGray, minScore, minAngle, maxAngle, angleStep, maxCandidates);
            }

            // Điểm biên tương đối so với tâm template
            float centerX = templateGray.Width / 2.0f;
            float centerY = templateGray.Height / 2.0f;
            var relPoints = edgePoints.Select(p => new Point2f(p.X - centerX, p.Y - centerY)).ToArray();

            // Tính ảnh biên của Search ROI
            using var searchEdges = new Mat();
            Cv2.Canny(searchGray, searchEdges, 50, 150);

            // Distance Transform trên ảnh đảo của biên để tính khoảng cách Hausdorff / Chamfer siêu nhanh
            using var invSearchEdges = new Mat();
            Cv2.BitwiseNot(searchEdges, invSearchEdges);
            using var distMap = new Mat();
            Cv2.DistanceTransform(invSearchEdges, distMap, DistanceTypes.L2, DistanceTransformMasks.Mask3);

            var candidates = new ConcurrentBag<DetectedObjectCandidate>();

            var angles = new List<double>();
            for (double a = minAngle; a <= maxAngle + 1e-4; a += angleStep)
            {
                angles.Add(a);
            }
            if (!angles.Any(a => Math.Abs(a) < 1e-4) && minAngle <= 0 && maxAngle >= 0)
            {
                angles.Add(0.0);
                angles.Sort();
            }

            int w = distMap.Width;
            int h = distMap.Height;
            int N = relPoints.Length;
            int gridStep = (w >= 300 && h >= 300) ? 2 : 1;
            float maxAcceptDist = Math.Max(2.5f, (float)(Math.Min(templateGray.Width, templateGray.Height) * 0.08));

            unsafe
            {
                float* pDist = (float*)distMap.Data;
                int stepD = (int)(distMap.Step() / sizeof(float));

                Parallel.ForEach(angles, angle =>
                {
                    double rad = angle * Math.PI / 180.0;
                    float cosA = (float)Math.Cos(rad);
                    float sinA = (float)Math.Sin(rad);

                    var rotPts = new (int Dx, int Dy)[N];
                    for (int i = 0; i < N; i++)
                    {
                        float rx = relPoints[i].X * cosA - relPoints[i].Y * sinA;
                        float ry = relPoints[i].X * sinA + relPoints[i].Y * cosA;
                        rotPts[i] = ((int)Math.Round(rx), (int)Math.Round(ry));
                    }

                    int marginX = (int)Math.Ceiling(templateGray.Width * 0.55);
                    int marginY = (int)Math.Ceiling(templateGray.Height * 0.55);

                    int rows = (h - 2 * marginY) / gridStep;
                    int cols = (w - 2 * marginX) / gridStep;
                    if (rows < 3 || cols < 3) return;

                    float[,] localScore = new float[rows, cols];

                    for (int ry = 0; ry < rows; ry++)
                    {
                        int cy = marginY + ry * gridStep;
                        for (int rx = 0; rx < cols; rx++)
                        {
                            int cx = marginX + rx * gridStep;
                            int inliers = 0;
                            float totalDist = 0;

                            for (int i = 0; i < N; i++)
                            {
                                int px = cx + rotPts[i].Dx;
                                int py = cy + rotPts[i].Dy;

                                if (px >= 0 && px < w && py >= 0 && py < h)
                                {
                                    float d = pDist[py * stepD + px];
                                    if (d <= maxAcceptDist)
                                    {
                                        inliers++;
                                        totalDist += d;
                                    }
                                }
                            }

                            double inlierRatio = (double)inliers / N;
                            if (inlierRatio >= minScore)
                            {
                                double avgDist = inliers > 0 ? totalDist / inliers : maxAcceptDist;
                                double score = inlierRatio * (1.0 - (avgDist / (maxAcceptDist * 2.0)));
                                localScore[ry, rx] = (float)Math.Clamp(score, 0.0, 1.0);
                            }
                        }
                    }

                    // Trích xuất Local Maxima trên lưới để tránh bùng nổ candidate lân cận
                    for (int ry = 1; ry < rows - 1; ry++)
                    {
                        for (int rx = 1; rx < cols - 1; rx++)
                        {
                            float v = localScore[ry, rx];
                            if (v >= minScore &&
                                v >= localScore[ry - 1, rx - 1] &&
                                v >= localScore[ry - 1, rx] &&
                                v >= localScore[ry - 1, rx + 1] &&
                                v >= localScore[ry, rx - 1] &&
                                v >  localScore[ry, rx + 1] &&
                                v >= localScore[ry + 1, rx - 1] &&
                                v >  localScore[ry + 1, rx] &&
                                v >  localScore[ry + 1, rx + 1])
                            {
                                int cx = marginX + rx * gridStep;
                                int cy = marginY + ry * gridStep;
                                candidates.Add(new DetectedObjectCandidate
                                {
                                    X = cx,
                                    Y = cy,
                                    AngleDeg = angle,
                                    Score = v,
                                    Width = templateGray.Width,
                                    Height = templateGray.Height
                                });
                            }
                        }
                    }
                });
            }

            return candidates.ToList();
        }

        #endregion

        #region Non-Maximum Suppression (Rotated NMS) & Tiện ích xử lý ảnh

        /// <summary>
        /// Lọc bỏ các phát hiện trùng lặp dựa trên hộp chữ nhật xoay (Rotated IoU) và khoảng cách tâm.
        /// </summary>
        public static List<DetectedObjectCandidate> ApplyRotatedNms(
            List<DetectedObjectCandidate> candidates,
            double templW,
            double templH,
            double maxOverlapIoU,
            int maxResults)
        {
            if (candidates == null || candidates.Count == 0) return new List<DetectedObjectCandidate>();

            // Sắp xếp các ứng viên giảm dần theo điểm số (ưu tiên giữ lại phát hiện có độ khớp cao nhất)
            var sorted = candidates.OrderByDescending(c => c.Score).ToList();
            var kept = new List<DetectedObjectCandidate>();

            // Khoảng cách tâm tối thiểu để hai vật thể độc lập được công nhận (dựa trên maxOverlap)
            double centerDistRatio = Math.Clamp(1.0 - maxOverlapIoU, 0.55, 0.85);
            double centerDistThreshold = Math.Min(templW, templH) * centerDistRatio;
            double centerDistSqThresh = centerDistThreshold * centerDistThreshold;

            foreach (var cand in sorted)
            {
                if (kept.Count >= maxResults) break;

                bool isDuplicate = false;
                var rrCand = new RotatedRect(
                    new Point2f((float)cand.X, (float)cand.Y),
                    new Size2f((float)templW, (float)templH),
                    (float)cand.AngleDeg);

                foreach (var existing in kept)
                {
                    // 1. Kiểm tra khoảng cách tâm
                    double dx = cand.X - existing.X;
                    double dy = cand.Y - existing.Y;
                    double distSq = dx * dx + dy * dy;

                    if (distSq < centerDistSqThresh)
                    {
                        isDuplicate = true;
                        break;
                    }

                    // 2. Kiểm tra tỷ lệ giao nhau trên diện tích vật thể (Overlap on Self)
                    var rrExisting = new RotatedRect(
                        new Point2f((float)existing.X, (float)existing.Y),
                        new Size2f((float)templW, (float)templH),
                        (float)existing.AngleDeg);

                    Cv2.RotatedRectangleIntersection(rrCand, rrExisting, out Point2f[] intersectPts);
                    if (intersectPts != null && intersectPts.Length >= 3)
                    {
                        double interArea = Cv2.ContourArea(intersectPts);
                        double minArea = templW * templH;
                        double overlapRatio = interArea / minArea;

                        if (overlapRatio > maxOverlapIoU)
                        {
                            isDuplicate = true;
                            break;
                        }
                    }
                }

                if (!isDuplicate)
                {
                    kept.Add(cand);
                }
            }

            return kept;
        }

        /// <summary>
        /// Tự động áp dụng cửa sổ suy giảm biên mềm (Apodization Window) cho ảnh template
        /// để làm mờ 4 góc vuông (chứa vân nền nhôm/mặt băng chuyền xung quanh chi tiết).
        /// </summary>
        public static Mat ApplyCircularWindow(Mat src)
        {
            if (src == null || src.Empty()) return new Mat();
            if (src.Width < 16 || src.Height < 16) return src.Clone();

            var dst = src.Clone();
            int w = src.Width;
            int h = src.Height;
            double cx = w / 2.0;
            double cy = h / 2.0;
            double rx = w * 0.46;
            double ry = h * 0.46;

            double meanBg = 0;
            int bgCount = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double nx = (x - cx) / rx;
                    double ny = (y - cy) / ry;
                    double d = Math.Sqrt(nx * nx + ny * ny);
                    if (d > 0.80)
                    {
                        meanBg += src.At<byte>(y, x);
                        bgCount++;
                    }
                }
            }
            meanBg = bgCount > 0 ? meanBg / bgCount : 128.0;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double nx = (x - cx) / rx;
                    double ny = (y - cy) / ry;
                    double d = Math.Sqrt(nx * nx + ny * ny);

                    byte val = src.At<byte>(y, x);
                    if (d <= 0.70)
                    {
                        dst.Set(y, x, val);
                    }
                    else if (d <= 1.0)
                    {
                        double wgt = 0.5 * (1.0 + Math.Cos(Math.PI * (d - 0.70) / 0.30));
                        double blended = val * wgt + meanBg * (1.0 - wgt);
                        dst.Set(y, x, (byte)Math.Clamp(Math.Round(blended), 0, 255));
                    }
                    else
                    {
                        dst.Set(y, x, (byte)Math.Clamp(Math.Round(meanBg), 0, 255));
                    }
                }
            }

            return dst;
        }

        private static Mat RotateImage(Mat src, double angle)
        {
            if (Math.Abs(angle) < 1e-4)
            {
                return src.Clone();
            }

            var center = new Point2f(src.Width / 2.0f, src.Height / 2.0f);
            using var rotMat = Cv2.GetRotationMatrix2D(center, angle, 1.0);

            // Tính kích thước bounding box mới để không bị cắt xén biên khi xoay
            double rad = Math.Abs(angle * Math.PI / 180.0);
            double cos = Math.Abs(Math.Cos(rad));
            double sin = Math.Abs(Math.Sin(rad));

            int newW = (int)Math.Ceiling(src.Width * cos + src.Height * sin);
            int newH = (int)Math.Ceiling(src.Width * sin + src.Height * cos);

            // Điều chỉnh ma trận biến đổi affine về tâm mới
            rotMat.Set(0, 2, rotMat.At<double>(0, 2) + (newW / 2.0 - center.X));
            rotMat.Set(1, 2, rotMat.At<double>(1, 2) + (newH / 2.0 - center.Y));

            var dst = new Mat();
            Cv2.WarpAffine(src, dst, rotMat, new Size(newW, newH), InterpolationFlags.Linear, BorderTypes.Replicate);
            return dst;
        }

        private static void FindLocalPeaks(Mat scoreMap, double threshold, Action<int, int, double> onPeak)
        {
            int w = scoreMap.Width;
            int h = scoreMap.Height;
            if (w < 3 || h < 3) return;

            unsafe
            {
                float* pData = (float*)scoreMap.Data;
                int step = (int)(scoreMap.Step() / sizeof(float));

                for (int y = 1; y < h - 1; y++)
                {
                    int rowIdx = y * step;
                    for (int x = 1; x < w - 1; x++)
                    {
                        float v = pData[rowIdx + x];
                        if (v >= threshold && !float.IsNaN(v) && !float.IsInfinity(v))
                        {
                            // Kiểm tra là cực đại địa phương (lớn hơn 8 điểm lân cận)
                            if (v >= pData[rowIdx - step + x - 1] &&
                                v >= pData[rowIdx - step + x] &&
                                v >= pData[rowIdx - step + x + 1] &&
                                v >= pData[rowIdx + x - 1] &&
                                v >  pData[rowIdx + x + 1] &&
                                v >= pData[rowIdx + step + x - 1] &&
                                v >  pData[rowIdx + step + x] &&
                                v >  pData[rowIdx + step + x + 1])
                            {
                                onPeak(x, y, v);
                            }
                        }
                    }
                }
            }
        }

        #endregion
    }
}
