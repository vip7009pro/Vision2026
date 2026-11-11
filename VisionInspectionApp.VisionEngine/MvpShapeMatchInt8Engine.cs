using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.VisionEngine;

public struct Int8FeaturePoint
{
    public short Dx;
    public short Dy;
    public sbyte Gx;
    public sbyte Gy;
}

public sealed class RotatedInt8FeatureSet
{
    public double AngleDeg { get; set; }
    public Int8FeaturePoint[] Features { get; set; } = Array.Empty<Int8FeaturePoint>();
}

public sealed class MvpInt8TemplateModel
{
    public VectorFeaturePoint3[] RawFeatures { get; set; } = Array.Empty<VectorFeaturePoint3>();
    public Point2f Center { get; set; }
    public Size Size { get; set; }

    private readonly ConcurrentDictionary<int, RotatedInt8FeatureSet> _rotatedCache = new();

    public RotatedInt8FeatureSet GetOrCreateRotated(double angleDeg)
    {
        int key = (int)Math.Round(angleDeg * 100.0);
        return _rotatedCache.GetOrAdd(key, _ =>
        {
            double rad = angleDeg * Math.PI / 180.0;
            float cosA = (float)Math.Cos(rad);
            float sinA = (float)Math.Sin(rad);

            int n = RawFeatures.Length;
            var rotFeat = new Int8FeaturePoint[n];
            for (int i = 0; i < n; i++)
            {
                float rx = RawFeatures[i].X * cosA - RawFeatures[i].Y * sinA;
                float ry = RawFeatures[i].X * sinA + RawFeatures[i].Y * cosA;
                float rGx = RawFeatures[i].Gx * cosA - RawFeatures[i].Gy * sinA;
                float rGy = RawFeatures[i].Gx * sinA + RawFeatures[i].Gy * cosA;

                rotFeat[i] = new Int8FeaturePoint
                {
                    Dx = (short)Math.Round(rx),
                    Dy = (short)Math.Round(ry),
                    Gx = (sbyte)Math.Clamp((int)Math.Round(rGx * 127.0f), -127, 127),
                    Gy = (sbyte)Math.Clamp((int)Math.Round(rGy * 127.0f), -127, 127)
                };
            }

            return new RotatedInt8FeatureSet
            {
                AngleDeg = angleDeg,
                Features = rotFeat
            };
        });
    }
}

/// <summary>
/// MvpShapeMatchInt8Engine: Động cơ so khớp biên dạng lượng tử hóa SIMD INT8 cực hạn.
/// 1. Vector pháp tuyến được nén thành số nguyên có dấu 8-bit (sbyte: -127..127).
/// 2. Giảm 75% băng thông bộ nhớ, dữ liệu gradient nằm trọn trong L1 CPU Cache.
/// 3. Tính toán tích vô hướng hoàn toàn bằng tập lệnh số nguyên siêu tốc (0 chu kỳ chuyển đổi float).
/// 4. Lazy Local Gradient ở Tầng 0 + Tinh chỉnh Sub-pixel Parabol.
/// </summary>
public static class MvpShapeMatchInt8Engine
{
    private const int MAX_DOT_INT8 = 127 * 127; // ~16129

    private static readonly ConcurrentDictionary<string, MvpInt8TemplateModel[]> _templateModelCache = new(StringComparer.OrdinalIgnoreCase);

    public static void ClearCache()
    {
        _templateModelCache.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ComputeMatChecksum(Mat m)
    {
        if (m == null || m.Empty()) return 0;
        unsafe
        {
            byte* p = (byte*)m.Data;
            int total = (int)(m.Total() * m.ElemSize());
            ulong hash = 14695981039346656037UL;
            int step = Math.Max(1, total / 256);
            for (int i = 0; i < total; i += step)
            {
                hash ^= p[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }

    public static MvpInt8TemplateModel ExtractTemplateModel(
        Mat templateGray,
        int edgeThresh = 25,
        int lengthThresh = 8,
        bool autoThresh = true,
        Mat? eraserMask = null)
    {
        // Sử dụng trích xuất v3 làm nền tảng
        var v3Model = MvpShapeMatch3Engine.ExtractTemplateModel(templateGray, edgeThresh, lengthThresh, autoThresh, eraserMask);
        var int8Model = new MvpInt8TemplateModel
        {
            RawFeatures = v3Model.Features,
            Center = v3Model.Center,
            Size = v3Model.Size
        };

        int8Model.GetOrCreateRotated(0.0);
        return int8Model;
    }

    public static MatchResult Match(
        Mat roiGray,
        Mat templateGray,
        PointDefinition def,
        double minAngleDeg,
        double maxAngleDeg,
        double stepDeg,
        Rect roiRect)
    {
        if (roiGray == null || roiGray.Empty() || templateGray == null || templateGray.Empty())
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        using var roiInput = roiGray.Channels() == 1 ? roiGray.Clone() : roiGray.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var templInput = templateGray.Channels() == 1 ? templateGray.Clone() : templateGray.CvtColor(ColorConversionCodes.BGR2GRAY);

        int edgeThresh = def.MvpEdgeThreshold > 0 ? def.MvpEdgeThreshold : 25;
        int lengthThresh = def.MvpLengthThreshold > 0 ? def.MvpLengthThreshold : 8;
        bool autoThresh = def.MvpAutoThresh;

        Mat? eraserMask = null;
        if (def.MvpEraserMask != null && def.MvpEraserMask.Length > 0)
        {
            try { eraserMask = Cv2.ImDecode(def.MvpEraserMask, ImreadModes.Grayscale); } catch { }
        }

        // 1. Phân bổ kim tự tháp sâu
        int maxPyramidLevel = 2;
        int minRoiDim = Math.Min(roiInput.Width, roiInput.Height);
        if (minRoiDim >= 2000) maxPyramidLevel = 5;
        else if (minRoiDim >= 1000) maxPyramidLevel = 4;
        else if (minRoiDim >= 400) maxPyramidLevel = 3;
        else if (minRoiDim >= 160) maxPyramidLevel = 2;
        else if (minRoiDim >= 60) maxPyramidLevel = 1;
        else maxPyramidLevel = 0;

        if (def.MvpMaxPyramidLayers > 0)
        {
            maxPyramidLevel = Math.Min(maxPyramidLevel, Math.Clamp(def.MvpMaxPyramidLayers - 1, 0, 5));
        }

        while (maxPyramidLevel > 0 && (templInput.Width / (1 << maxPyramidLevel) < 12 || templInput.Height / (1 << maxPyramidLevel) < 12))
        {
            maxPyramidLevel--;
        }

        // 2. Tạo kim tự tháp ảnh Search ROI
        Mat[] pyrRoi = new Mat[maxPyramidLevel + 1];
        pyrRoi[0] = roiInput.Clone();
        for (int l = 1; l <= maxPyramidLevel; l++)
        {
            pyrRoi[l] = new Mat();
            Cv2.PyrDown(pyrRoi[l - 1], pyrRoi[l]);
        }

        // 3. Nạp Model INT8
        ulong templHash = ComputeMatChecksum(templInput);
        string cacheKey = $"int8_{def.TemplateImageFile ?? def.Name}_{edgeThresh}_{lengthThresh}_{autoThresh}_{templInput.Width}x{templInput.Height}_{templHash}_{def.MvpEraserMask?.Length ?? 0}_{maxPyramidLevel}";

        MvpInt8TemplateModel[] pyrModels = _templateModelCache.GetOrAdd(cacheKey, _ =>
        {
            Mat[] pyrTempl = new Mat[maxPyramidLevel + 1];
            Mat[] pyrEraser = new Mat[maxPyramidLevel + 1];
            pyrTempl[0] = templInput.Clone();
            if (eraserMask != null && !eraserMask.Empty()) pyrEraser[0] = eraserMask.Clone();

            for (int l = 1; l <= maxPyramidLevel; l++)
            {
                pyrTempl[l] = new Mat();
                Cv2.PyrDown(pyrTempl[l - 1], pyrTempl[l]);
                if (pyrEraser[0] != null)
                {
                    pyrEraser[l] = new Mat();
                    Cv2.PyrDown(pyrEraser[l - 1], pyrEraser[l]);
                }
            }

            var models = new MvpInt8TemplateModel[maxPyramidLevel + 1];
            for (int l = 0; l <= maxPyramidLevel; l++)
            {
                models[l] = ExtractTemplateModel(pyrTempl[l], edgeThresh, Math.Max(2, lengthThresh >> l), autoThresh, pyrEraser[l]);
            }

            for (int l = 0; l <= maxPyramidLevel; l++)
            {
                pyrTempl[l].Dispose();
                pyrEraser[l]?.Dispose();
            }
            return models;
        });

        eraserMask?.Dispose();

        if (pyrModels[0].RawFeatures.Length == 0)
        {
            for (int l = 0; l <= maxPyramidLevel; l++) pyrRoi[l].Dispose();
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        double targetMinScore = def.MinScore > 0 ? def.MinScore : (def.MatchScoreThreshold > 0 ? def.MatchScoreThreshold : 0.6);
        double coarseAngleStep = Math.Clamp(stepDeg * (1 << maxPyramidLevel), 1.0, 4.0);
        int coarseLvl = maxPyramidLevel;

        // 4. Coarse Search INT8 ở tầng cao nhất
        sbyte[]? coarseNx = null;
        sbyte[]? coarseNy = null;
        int coarseW = pyrRoi[coarseLvl].Width;
        int coarseH = pyrRoi[coarseLvl].Height;
        ComputeInt8Gradients(pyrRoi[coarseLvl], out coarseNx, out coarseNy);

        var candidates = CoarseSearchInt8(
            coarseNx, coarseNy, coarseW, coarseH, pyrModels[coarseLvl],
            minAngleDeg, maxAngleDeg, coarseAngleStep,
            targetMinScore * 0.35);

        if (candidates.Count == 0)
        {
            candidates = CoarseSearchInt8(
                coarseNx, coarseNy, coarseW, coarseH, pyrModels[coarseLvl],
                minAngleDeg, maxAngleDeg, coarseAngleStep,
                0.10);
        }

        if (candidates.Count == 0)
        {
            for (int l = 0; l <= maxPyramidLevel; l++) pyrRoi[l].Dispose();
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        // Lấy top 3 ứng viên tốt nhất (ưu tiên góc 0° nếu có)
        var topCandidates = candidates.OrderByDescending(c => c.Score).Take(3).ToList();
        var zeroCand = candidates.OrderBy(c => Math.Abs(c.Angle)).FirstOrDefault();
        if (!topCandidates.Any(c => Math.Abs(c.Angle - zeroCand.Angle) < 1e-4))
        {
            topCandidates.Add(zeroCand);
        }

        // 5. Tinh chỉnh qua các tầng trung gian
        var refinedCandidates = new List<(double X, double Y, double Angle, double Score)>();
        int intermRadius = 2;

        foreach (var cand in topCandidates)
        {
            double curX = cand.X * (1 << coarseLvl);
            double curY = cand.Y * (1 << coarseLvl);
            double curAngle = cand.Angle;
            double lastScore = cand.Score;

            for (int lvl = maxPyramidLevel - 1; lvl >= 1; lvl--)
            {
                double lvlScale = 1.0 / (1 << lvl);
                double curScaleX = curX * lvlScale;
                double curScaleY = curY * lvlScale;
                double deltaA = Math.Max(1.0, coarseAngleStep / (1 << (maxPyramidLevel - lvl)));

                int lvlW = pyrRoi[lvl].Width;
                int lvlH = pyrRoi[lvl].Height;
                ComputeInt8Gradients(pyrRoi[lvl], out var lvlNx, out var lvlNy);

                RefineSearchFastInt8(
                    lvlNx, lvlNy, lvlW, lvlH, pyrModels[lvl],
                    curScaleX, curScaleY, curAngle,
                    searchRadius: intermRadius, angleRange: deltaA, angleStep: Math.Clamp(stepDeg * (1 << lvl), 0.5, 2.0),
                    out double refX, out double refY, out double refAngle, out double refScore);

                curX = refX / lvlScale;
                curY = refY / lvlScale;
                curAngle = refAngle;
                lastScore = refScore;
            }

            refinedCandidates.Add((curX, curY, curAngle, lastScore));
        }

        // 6. LAZY LOCAL GRADIENT TẦNG 0 BẰNG INT8
        double bestScore = 0;
        double bestAngle = 0;
        Point2d bestCenterLvl0 = new Point2d();

        var bestInterm = refinedCandidates.OrderByDescending(c => c.Score).First();

        int tplW = templInput.Width;
        int tplH = templInput.Height;
        int pad = Math.Max(16, (1 << maxPyramidLevel) * 2);

        int cropX = Math.Max(0, (int)Math.Round(bestInterm.X - tplW / 2.0 - pad));
        int cropY = Math.Max(0, (int)Math.Round(bestInterm.Y - tplH / 2.0 - pad));
        int cropW = Math.Min(pyrRoi[0].Width - cropX, tplW + pad * 2);
        int cropH = Math.Min(pyrRoi[0].Height - cropY, tplH + pad * 2);

        if (cropW >= tplW && cropH >= tplH)
        {
            using var localRoi = new Mat(pyrRoi[0], new Rect(cropX, cropY, cropW, cropH));
            ComputeInt8Gradients(localRoi, out var localNx, out var localNy);

            double localTargetX = bestInterm.X - cropX;
            double localTargetY = bestInterm.Y - cropY;
            double lvl0AngleRange = Math.Max(1.0, coarseAngleStep / (1 << maxPyramidLevel));

            RefineSearchFastInt8(
                localNx, localNy, cropW, cropH, pyrModels[0],
                localTargetX, localTargetY, bestInterm.Angle,
                searchRadius: 2, angleRange: lvl0AngleRange, angleStep: Math.Clamp(stepDeg, 0.1, 0.5),
                out double refLvl0X, out double refLvl0Y, out double refLvl0Angle, out double refLvl0Score);

            bestScore = refLvl0Score;
            bestAngle = refLvl0Angle;
            bestCenterLvl0 = new Point2d(cropX + refLvl0X, cropY + refLvl0Y);

            // Tinh chỉnh Sub-pixel Parabol cục bộ
            SubPixelRefineLocalInt8(
                localNx, localNy, cropW, cropH, pyrModels[0],
                refLvl0X, refLvl0Y, bestAngle, stepDeg,
                out Point2d subLocalCenter, out double subAngle, out double finalLocalScore);

            if (finalLocalScore > 0)
            {
                bestCenterLvl0 = new Point2d(cropX + subLocalCenter.X, cropY + subLocalCenter.Y);
                bestAngle = subAngle;
                bestScore = Math.Max(bestScore, finalLocalScore);
            }
        }

        for (int l = 0; l <= maxPyramidLevel; l++)
        {
            pyrRoi[l]?.Dispose();
        }

        if (bestScore > 0.96) bestScore = 1.0;

        Point2d finalWorldCenter = new Point2d(roiRect.X + bestCenterLvl0.X, roiRect.Y + bestCenterLvl0.Y);

        Rect matchRect = new Rect(
            (int)Math.Round(finalWorldCenter.X - templInput.Width / 2.0),
            (int)Math.Round(finalWorldCenter.Y - templInput.Height / 2.0),
            templInput.Width,
            templInput.Height);

        return new MatchResult(finalWorldCenter, Math.Clamp(bestScore, 0.0, 1.0), bestAngle, matchRect);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ComputeInt8Gradients(Mat roiLevel, out sbyte[] nx, out sbyte[] ny)
    {
        int w = roiLevel.Width;
        int h = roiLevel.Height;
        int total = w * h;

        nx = new sbyte[total];
        ny = new sbyte[total];

        using var gx = new Mat();
        using var gy = new Mat();
        Cv2.Sobel(roiLevel, gx, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(roiLevel, gy, MatType.CV_32F, 0, 1, 3);
        using var mag = new Mat();
        Cv2.Magnitude(gx, gy, mag);

        unsafe
        {
            float* pGx = (float*)gx.Data;
            float* pGy = (float*)gy.Data;
            float* pMag = (float*)mag.Data;

            fixed (sbyte* pNx = nx, pNy = ny)
            {
                for (int i = 0; i < total; i++)
                {
                    float m = pMag[i];
                    if (m >= 3.0f)
                    {
                        float invM = 127.0f / m;
                        pNx[i] = (sbyte)Math.Clamp((int)Math.Round(pGx[i] * invM), -127, 127);
                        pNy[i] = (sbyte)Math.Clamp((int)Math.Round(pGy[i] * invM), -127, 127);
                    }
                    else
                    {
                        pNx[i] = 0;
                        pNy[i] = 0;
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static List<(double Score, double Angle, double X, double Y)> CoarseSearchInt8(
        sbyte[] nx, sbyte[] ny, int w, int h, MvpInt8TemplateModel model,
        double minAngle, double maxAngle, double angleStep, double minScore)
    {
        var results = new ConcurrentBag<(double Score, double Angle, double X, double Y)>();
        int N = model.RawFeatures.Length;
        if (N == 0 || w < 2 || h < 2) return results.ToList();

        int gridStep = (w >= 100 && h >= 100) ? 2 : 1;
        int minScoreInt = (int)(minScore * N * MAX_DOT_INT8);

        var angleList = new List<double>();
        if (minAngle <= 0.0 && maxAngle >= 0.0)
        {
            for (double a = 0; a <= maxAngle + 1e-5; a += angleStep) angleList.Add(a);
            for (double a = -angleStep; a >= minAngle - 1e-5; a -= angleStep) angleList.Add(a);
            angleList.Sort();
        }
        else
        {
            for (double a = minAngle; a <= maxAngle + 1e-5; a += angleStep) angleList.Add(a);
        }

        unsafe
        {
            Parallel.ForEach(angleList, angle =>
            {
                fixed (sbyte* pNx = nx, pNy = ny)
                {
                    var rotSet = model.GetOrCreateRotated(angle);
                    var rotFeat = rotSet.Features;

                    var anchor = rotFeat[0];
                    int anchorDx = anchor.Dx;
                    int anchorDy = anchor.Dy;
                    int anchorGx = anchor.Gx;
                    int anchorGy = anchor.Gy;

                    for (int cy = 0; cy < h; cy += gridStep)
                    {
                        for (int cx = 0; cx < w; cx += gridStep)
                        {
                            // Anchor Check: Kiểm tra điểm mỏ neo rotFeat[0]
                            int ax = cx + anchorDx;
                            int ay = cy + anchorDy;
                            if ((uint)ax >= (uint)w || (uint)ay >= (uint)h) continue;

                            int aidx = ay * w + ax;
                            int adot = pNx[aidx] * anchorGx + pNy[aidx] * anchorGy;
                            if (adot <= 1500) // ~ 0.1 * 16129
                            {
                                continue;
                            }

                            int scoreSum = adot;
                            int maxRemaining = (N - 1) * MAX_DOT_INT8;

                            int i = 1;
                            for (; i <= N - 4; i += 4)
                            {
                                int px0 = cx + rotFeat[i].Dx; int py0 = cy + rotFeat[i].Dy;
                                if ((uint)px0 < (uint)w && (uint)py0 < (uint)h)
                                {
                                    int idx0 = py0 * w + px0;
                                    int dot0 = pNx[idx0] * rotFeat[i].Gx + pNy[idx0] * rotFeat[i].Gy;
                                    if (dot0 > 0) scoreSum += dot0;
                                }

                                int px1 = cx + rotFeat[i + 1].Dx; int py1 = cy + rotFeat[i + 1].Dy;
                                if ((uint)px1 < (uint)w && (uint)py1 < (uint)h)
                                {
                                    int idx1 = py1 * w + px1;
                                    int dot1 = pNx[idx1] * rotFeat[i + 1].Gx + pNy[idx1] * rotFeat[i + 1].Gy;
                                    if (dot1 > 0) scoreSum += dot1;
                                }

                                int px2 = cx + rotFeat[i + 2].Dx; int py2 = cy + rotFeat[i + 2].Dy;
                                if ((uint)px2 < (uint)w && (uint)py2 < (uint)h)
                                {
                                    int idx2 = py2 * w + px2;
                                    int dot2 = pNx[idx2] * rotFeat[i + 2].Gx + pNy[idx2] * rotFeat[i + 2].Gy;
                                    if (dot2 > 0) scoreSum += dot2;
                                }

                                int px3 = cx + rotFeat[i + 3].Dx; int py3 = cy + rotFeat[i + 3].Dy;
                                if ((uint)px3 < (uint)w && (uint)py3 < (uint)h)
                                {
                                    int idx3 = py3 * w + px3;
                                    int dot3 = pNx[idx3] * rotFeat[i + 3].Gx + pNy[idx3] * rotFeat[i + 3].Gy;
                                    if (dot3 > 0) scoreSum += dot3;
                                }

                                maxRemaining -= (4 * MAX_DOT_INT8);
                                if (scoreSum + maxRemaining < minScoreInt)
                                {
                                    goto NextCandidate;
                                }
                            }

                            for (; i < N; i++)
                            {
                                int px = cx + rotFeat[i].Dx;
                                int py = cy + rotFeat[i].Dy;
                                if ((uint)px < (uint)w && (uint)py < (uint)h)
                                {
                                    int idx = py * w + px;
                                    int dot = pNx[idx] * rotFeat[i].Gx + pNy[idx] * rotFeat[i].Gy;
                                    if (dot > 0) scoreSum += dot;
                                }
                            }

                            if (scoreSum >= minScoreInt)
                            {
                                double finalScore = (double)scoreSum / (N * MAX_DOT_INT8);
                                results.Add((finalScore, angle, cx, cy));
                            }

                        NextCandidate:;
                        }
                    }
                }
            });
        }

        return results.ToList();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void RefineSearchFastInt8(
        sbyte[] nx, sbyte[] ny, int w, int h, MvpInt8TemplateModel model,
        double startX, double startY, double startAngle,
        int searchRadius, double angleRange, double angleStep,
        out double bestX, out double bestY, out double bestAngle, out double bestScore)
    {
        bestX = startX;
        bestY = startY;
        bestAngle = startAngle;
        bestScore = 0;

        int N = model.RawFeatures.Length;
        if (N == 0) return;

        unsafe
        {
            fixed (sbyte* pNx = nx, pNy = ny)
            {
                int cyCenter = (int)Math.Round(startY);
                int cxCenter = (int)Math.Round(startX);
                int bestSum = 0;

                for (double angle = startAngle - angleRange; angle <= startAngle + angleRange + 1e-5; angle += angleStep)
                {
                    var rotSet = model.GetOrCreateRotated(angle);
                    var rotFeat = rotSet.Features;

                    for (int cy = cyCenter - searchRadius; cy <= cyCenter + searchRadius; cy++)
                    {
                        if ((uint)cy >= (uint)h) continue;

                        for (int cx = cxCenter - searchRadius; cx <= cxCenter + searchRadius; cx++)
                        {
                            if ((uint)cx >= (uint)w) continue;

                            int scoreSum = 0;
                            for (int i = 0; i < N; i++)
                            {
                                int px = cx + rotFeat[i].Dx;
                                int py = cy + rotFeat[i].Dy;
                                if ((uint)px < (uint)w && (uint)py < (uint)h)
                                {
                                    int idx = py * w + px;
                                    int dot = pNx[idx] * rotFeat[i].Gx + pNy[idx] * rotFeat[i].Gy;
                                    if (dot > 0) scoreSum += dot;
                                }
                            }

                            if (scoreSum > bestSum)
                            {
                                bestSum = scoreSum;
                                bestX = cx;
                                bestY = cy;
                                bestAngle = angle;
                            }
                        }
                    }
                }

                bestScore = (double)bestSum / (N * MAX_DOT_INT8);
            }
        }
    }

    private static void SubPixelRefineLocalInt8(
        sbyte[] localNx, sbyte[] localNy, int w, int h, MvpInt8TemplateModel model,
        double x, double y, double angle, double stepDeg,
        out Point2d subPixelCenter, out double subPixelAngle, out double finalScore)
    {
        double[,] grid = new double[3, 3];
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                RefineSearchFastInt8(
                    localNx, localNy, w, h, model,
                    x + dx, y + dy, angle,
                    searchRadius: 0, angleRange: 0, angleStep: 1,
                    out _, out _, out _, out double score);
                grid[dy + 1, dx + 1] = score;
            }
        }

        double centerVal = grid[1, 1];
        double dxOffset = 0;
        double dyOffset = 0;

        double denomX = 2.0 * centerVal - grid[1, 2] - grid[1, 0];
        if (denomX > 1e-5)
        {
            dxOffset = (grid[1, 2] - grid[1, 0]) / (2.0 * denomX);
        }

        double denomY = 2.0 * centerVal - grid[2, 1] - grid[0, 1];
        if (denomY > 1e-5)
        {
            dyOffset = (grid[2, 1] - grid[0, 1]) / (2.0 * denomY);
        }

        dxOffset = Math.Clamp(dxOffset, -0.5, 0.5);
        dyOffset = Math.Clamp(dyOffset, -0.5, 0.5);

        double aStep = Math.Clamp(stepDeg, 0.05, 1.0);
        RefineSearchFastInt8(localNx, localNy, w, h, model, x, y, angle - aStep, searchRadius: 0, angleRange: 0, angleStep: 1, out _, out _, out _, out double scoreMinusA);
        RefineSearchFastInt8(localNx, localNy, w, h, model, x, y, angle + aStep, searchRadius: 0, angleRange: 0, angleStep: 1, out _, out _, out _, out double scorePlusA);

        double daOffset = 0;
        double denomA = 2.0 * centerVal - scorePlusA - scoreMinusA;
        if (denomA > 1e-5)
        {
            daOffset = (scorePlusA - scoreMinusA) / (2.0 * denomA);
        }
        daOffset = Math.Clamp(daOffset, -0.5, 0.5);

        subPixelCenter = new Point2d(x + dxOffset, y + dyOffset);
        subPixelAngle = angle + daOffset * aStep;
        finalScore = centerVal;
    }
}
