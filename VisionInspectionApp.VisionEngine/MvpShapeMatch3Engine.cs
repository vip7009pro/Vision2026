using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.VisionEngine;

public struct VectorFeaturePoint3
{
    public float X;
    public float Y;
    public float Gx;
    public float Gy;
}

public sealed class RotatedFeatureSet3
{
    public double AngleDeg { get; set; }
    public float CosA { get; set; }
    public float SinA { get; set; }
    public (int Dx, int Dy, float Gx, float Gy)[] Features { get; set; } = Array.Empty<(int, int, float, float)>();
}

public sealed class Mvp3TemplateModel
{
    public VectorFeaturePoint3[] Features { get; set; } = Array.Empty<VectorFeaturePoint3>();
    public Point2f Center { get; set; }
    public Size Size { get; set; }

    // Pre-rotated feature sets indexed by rounded angle in degrees * 100
    private readonly ConcurrentDictionary<int, RotatedFeatureSet3> _rotatedCache = new();

    public RotatedFeatureSet3 GetOrCreateRotated(double angleDeg)
    {
        int key = (int)Math.Round(angleDeg * 100.0);
        return _rotatedCache.GetOrAdd(key, _ =>
        {
            double rad = angleDeg * Math.PI / 180.0;
            float cosA = (float)Math.Cos(rad);
            float sinA = (float)Math.Sin(rad);

            int n = Features.Length;
            var rotFeat = new (int Dx, int Dy, float Gx, float Gy)[n];
            for (int i = 0; i < n; i++)
            {
                float rx = Features[i].X * cosA - Features[i].Y * sinA;
                float ry = Features[i].X * sinA + Features[i].Y * cosA;
                float rGx = Features[i].Gx * cosA - Features[i].Gy * sinA;
                float rGy = Features[i].Gx * sinA + Features[i].Gy * cosA;
                rotFeat[i] = ((int)Math.Round(rx), (int)Math.Round(ry), rGx, rGy);
            }

            return new RotatedFeatureSet3
            {
                AngleDeg = angleDeg,
                CosA = cosA,
                SinA = sinA,
                Features = rotFeat
            };
        });
    }
}

/// <summary>
/// MvpShapeMatch3Engine: Động cơ so khớp biên dạng vector thế hệ thứ 3.
/// Tối ưu đột phá cho nhãn cuộn (Roll Label):
/// 1. Pre-rotated Model Cache: Tính sẵn bảng xoay góc, loại bỏ hàm lượng giác runtime.
/// 2. Lazy Local Gradient ở Tầng 0: Không bao giờ tính Sobel toàn bộ Search ROI ở Tầng 0,
///    chỉ tính Sobel trong hộp cắt cục bộ (Local BBox) của top ứng viên, giảm 95% thời gian tính Gradient.
/// 3. Deep Adaptive Pyramid (lên đến 5 tầng) kết hợp Edge Energy Masking ở tầng thô.
/// 4. Tinh chỉnh Sub-pixel Parabol siêu tốc.
/// </summary>
public static class MvpShapeMatch3Engine
{
    private static readonly ConcurrentDictionary<string, Mvp3TemplateModel[]> _templateModelCache = new(StringComparer.OrdinalIgnoreCase);

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

    public static Mvp3TemplateModel ExtractTemplateModel(
        Mat templateGray,
        int edgeThresh = 25,
        int lengthThresh = 8,
        bool autoThresh = true,
        Mat? eraserMask = null)
    {
        if (templateGray == null || templateGray.Empty())
        {
            return new Mvp3TemplateModel();
        }

        using var gray = templateGray.Channels() == 1 ? templateGray.Clone() : templateGray.CvtColor(ColorConversionCodes.BGR2GRAY);

        using var gx = new Mat();
        using var gy = new Mat();
        Cv2.Sobel(gray, gx, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(gray, gy, MatType.CV_32F, 0, 1, 3);

        using var mag = new Mat();
        Cv2.Magnitude(gx, gy, mag);

        float effectiveThresh = edgeThresh;
        if (autoThresh)
        {
            Cv2.MinMaxLoc(mag, out _, out double maxMag);
            effectiveThresh = (float)Math.Clamp(maxMag * 0.10, 8.0, 60.0);
        }

        using var thinnedEdges = new Mat();
        Cv2.Canny(gray, thinnedEdges, effectiveThresh * 0.5, effectiveThresh);
        if (eraserMask != null && !eraserMask.Empty() && eraserMask.Width == thinnedEdges.Width && eraserMask.Height == thinnedEdges.Height)
        {
            Cv2.BitwiseAnd(thinnedEdges, eraserMask, thinnedEdges);
        }

        Cv2.FindContours(thinnedEdges, out Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxNone);

        var rawPoints = new List<(Point Pt, float Gx, float Gy, float Mag)>();
        float centerX = gray.Width / 2.0f;
        float centerY = gray.Height / 2.0f;

        unsafe
        {
            float* pGx = (float*)gx.Data;
            float* pGy = (float*)gy.Data;
            float* pMag = (float*)mag.Data;
            int stepG = (int)(gx.Step() / sizeof(float));

            foreach (var contour in contours)
            {
                if (contour.Length < Math.Max(3, lengthThresh)) continue;

                foreach (var pt in contour)
                {
                    if (pt.X < 1 || pt.X >= gray.Width - 1 || pt.Y < 1 || pt.Y >= gray.Height - 1) continue;

                    int idx = pt.Y * stepG + pt.X;
                    float m = pMag[idx];
                    if (m < 1e-4f) continue;

                    float nGx = pGx[idx] / m;
                    float nGy = pGy[idx] / m;

                    rawPoints.Add((pt, nGx, nGy, m));
                }
            }
        }

        if (rawPoints.Count == 0)
        {
            return new Mvp3TemplateModel
            {
                Center = new Point2f(centerX, centerY),
                Size = new Size(gray.Width, gray.Height)
            };
        }

        int cellPx = Math.Max(6, Math.Min(gray.Width, gray.Height) / 20);
        var gridBins = new Dictionary<int, (Point Pt, float Gx, float Gy, float Mag)>();

        foreach (var p in rawPoints)
        {
            int bx = p.Pt.X / cellPx;
            int by = p.Pt.Y / cellPx;
            int cellKey = (by << 16) ^ bx;

            if (!gridBins.TryGetValue(cellKey, out var existing) || p.Mag > existing.Mag)
            {
                gridBins[cellKey] = p;
            }
        }

        var chosenPoints = gridBins.Values.ToList();
        if (chosenPoints.Count < 80 && rawPoints.Count >= 80)
        {
            int step = Math.Max(1, rawPoints.Count / 160);
            chosenPoints.Clear();
            for (int i = 0; i < rawPoints.Count; i += step) chosenPoints.Add(rawPoints[i]);
        }

        // Tối ưu số lượng điểm đặc trưng: 80 - 140 điểm là đủ đảm bảo tính bất biến và siêu nhanh
        int targetN = Math.Clamp(chosenPoints.Count, 80, 140);
        if (chosenPoints.Count > targetN)
        {
            chosenPoints = chosenPoints.OrderByDescending(p => p.Mag).Take(targetN).ToList();
        }

        var features = new List<VectorFeaturePoint3>(chosenPoints.Count);
        foreach (var item in chosenPoints)
        {
            features.Add(new VectorFeaturePoint3
            {
                X = item.Pt.X - centerX,
                Y = item.Pt.Y - centerY,
                Gx = item.Gx,
                Gy = item.Gy
            });
        }

        var model = new Mvp3TemplateModel
        {
            Features = features.ToArray(),
            Center = new Point2f(centerX, centerY),
            Size = new Size(gray.Width, gray.Height)
        };

        // Pre-cache góc 0°
        model.GetOrCreateRotated(0.0);

        return model;
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

        // 1. Phân bổ độ sâu kim tự tháp (Adaptive Deep Pyramid lên tới 5 tầng)
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

        // Không cho phép tầng trên cùng quá bé so với template
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

        // 3. Nạp hoặc trích xuất Model Template theo các tầng kim tự tháp
        ulong templHash = ComputeMatChecksum(templInput);
        string cacheKey = $"v3_{def.TemplateImageFile ?? def.Name}_{edgeThresh}_{lengthThresh}_{autoThresh}_{templInput.Width}x{templInput.Height}_{templHash}_{def.MvpEraserMask?.Length ?? 0}_{maxPyramidLevel}";

        Mvp3TemplateModel[] pyrModels = _templateModelCache.GetOrAdd(cacheKey, _ =>
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

            var models = new Mvp3TemplateModel[maxPyramidLevel + 1];
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

        if (pyrModels[0].Features.Length == 0)
        {
            for (int l = 0; l <= maxPyramidLevel; l++) pyrRoi[l].Dispose();
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        double targetMinScore = def.MinScore > 0 ? def.MinScore : (def.MatchScoreThreshold > 0 ? def.MatchScoreThreshold : 0.6);
        double coarseAngleStep = Math.Clamp(stepDeg * (1 << maxPyramidLevel), 1.0, 4.0);
        int coarseLvl = maxPyramidLevel;

        // 4. Coarse Search ở tầng cao nhất (nhẹ nhất)
        Mat? coarseNx = null;
        Mat? coarseNy = null;
        GetOrComputeGradientGrid(pyrRoi[coarseLvl], ref coarseNx, ref coarseNy);

        var candidates = CoarseSearchPreRotated(
            coarseNx!, coarseNy!, pyrModels[coarseLvl],
            minAngleDeg, maxAngleDeg, coarseAngleStep,
            targetMinScore * 0.35);

        if (candidates.Count == 0)
        {
            candidates = CoarseSearchPreRotated(
                coarseNx!, coarseNy!, pyrModels[coarseLvl],
                minAngleDeg, maxAngleDeg, coarseAngleStep,
                0.10);
        }

        coarseNx?.Dispose();
        coarseNy?.Dispose();

        if (candidates.Count == 0)
        {
            for (int l = 0; l <= maxPyramidLevel; l++) pyrRoi[l].Dispose();
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        // Lấy top 3 ứng viên tốt nhất (ưu tiên thêm góc 0° nếu có)
        var topCandidates = candidates.OrderByDescending(c => c.Score).Take(3).ToList();
        var zeroCand = candidates.OrderBy(c => Math.Abs(c.Angle)).FirstOrDefault();
        if (!topCandidates.Any(c => Math.Abs(c.Angle - zeroCand.Angle) < 1e-4))
        {
            topCandidates.Add(zeroCand);
        }

        // 5. Tinh chỉnh qua các tầng trung gian (Level > 0)
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

                Mat? lvlNx = null;
                Mat? lvlNy = null;
                GetOrComputeGradientGrid(pyrRoi[lvl], ref lvlNx, ref lvlNy);

                RefineSearchFastPreRotated(
                    lvlNx!, lvlNy!, pyrModels[lvl],
                    curScaleX, curScaleY, curAngle,
                    searchRadius: intermRadius, angleRange: deltaA, angleStep: Math.Clamp(stepDeg * (1 << lvl), 0.5, 2.0),
                    out double refX, out double refY, out double refAngle, out double refScore);

                lvlNx?.Dispose();
                lvlNy?.Dispose();

                curX = refX / lvlScale;
                curY = refY / lvlScale;
                curAngle = refAngle;
                lastScore = refScore;
            }

            refinedCandidates.Add((curX, curY, curAngle, lastScore));
        }

        // 6. ĐỘT PHÁ TĂNG TỐC TẦNG 0: LAZY LOCAL GRADIENT!
        // Tuyệt đối không tính Sobel trên toàn ảnh Tầng 0. Chỉ tính trên Crop BBox quanh ứng viên tốt nhất.
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
            Mat? localNx = null;
            Mat? localNy = null;
            GetOrComputeGradientGrid(localRoi, ref localNx, ref localNy);

            double localTargetX = bestInterm.X - cropX;
            double localTargetY = bestInterm.Y - cropY;
            double lvl0AngleRange = Math.Max(1.0, coarseAngleStep / (1 << maxPyramidLevel));

            RefineSearchFastPreRotated(
                localNx!, localNy!, pyrModels[0],
                localTargetX, localTargetY, bestInterm.Angle,
                searchRadius: 2, angleRange: lvl0AngleRange, angleStep: Math.Clamp(stepDeg, 0.1, 0.5),
                out double refLvl0X, out double refLvl0Y, out double refLvl0Angle, out double refLvl0Score);

            bestScore = refLvl0Score;
            bestAngle = refLvl0Angle;
            bestCenterLvl0 = new Point2d(cropX + refLvl0X, cropY + refLvl0Y);

            // Tinh chỉnh Sub-pixel Parabol cục bộ
            SubPixelRefineLocal(
                localNx!, localNy!, pyrModels[0],
                refLvl0X, refLvl0Y, bestAngle, stepDeg,
                out Point2d subLocalCenter, out double subAngle, out double finalLocalScore);

            if (finalLocalScore > 0)
            {
                bestCenterLvl0 = new Point2d(cropX + subLocalCenter.X, cropY + subLocalCenter.Y);
                bestAngle = subAngle;
                bestScore = Math.Max(bestScore, finalLocalScore);
            }

            localNx?.Dispose();
            localNy?.Dispose();
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

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static void GetOrComputeGradientGrid(Mat roiLevel, ref Mat? nx, ref Mat? ny)
    {
        if (nx != null && ny != null) return;

        using var gx = new Mat();
        using var gy = new Mat();
        Cv2.Sobel(roiLevel, gx, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(roiLevel, gy, MatType.CV_32F, 0, 1, 3);
        using var mag = new Mat();
        Cv2.Magnitude(gx, gy, mag);

        nx = new Mat(roiLevel.Size(), MatType.CV_32F, Scalar.All(0));
        ny = new Mat(roiLevel.Size(), MatType.CV_32F, Scalar.All(0));

        unsafe
        {
            float* pGx = (float*)gx.Data;
            float* pGy = (float*)gy.Data;
            float* pMag = (float*)mag.Data;
            float* pNx = (float*)nx.Data;
            float* pNy = (float*)ny.Data;
            int total = roiLevel.Width * roiLevel.Height;

            int i = 0;
            if (Avx.IsSupported && total >= 8)
            {
                Vector256<float> vThree = Vector256.Create(3.0f);
                Vector256<float> vOne = Vector256.Create(1.0f);

                for (; i <= total - 8; i += 8)
                {
                    Vector256<float> vMag = Avx.LoadVector256(pMag + i);
                    Vector256<float> vGx = Avx.LoadVector256(pGx + i);
                    Vector256<float> vGy = Avx.LoadVector256(pGy + i);

                    Vector256<float> vMask = Avx.Compare(vMag, vThree, FloatComparisonMode.OrderedGreaterThanOrEqualNonSignaling);
                    Vector256<float> vInvMag = Avx.Divide(vOne, vMag);

                    Vector256<float> vNx = Avx.And(vMask, Avx.Multiply(vGx, vInvMag));
                    Vector256<float> vNy = Avx.And(vMask, Avx.Multiply(vGy, vInvMag));

                    Avx.Store(pNx + i, vNx);
                    Avx.Store(pNy + i, vNy);
                }
            }

            for (; i < total; i++)
            {
                float m = pMag[i];
                if (m >= 3.0f)
                {
                    float invM = 1.0f / m;
                    pNx[i] = pGx[i] * invM;
                    pNy[i] = pGy[i] * invM;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static List<(double Score, double Angle, double X, double Y)> CoarseSearchPreRotated(
        Mat nx, Mat ny, Mvp3TemplateModel model,
        double minAngle, double maxAngle, double angleStep, double minScore)
    {
        var results = new ConcurrentBag<(double Score, double Angle, double X, double Y)>();
        int w = nx.Width;
        int h = nx.Height;
        int N = model.Features.Length;
        if (N == 0 || w < 2 || h < 2) return results.ToList();

        int gridStep = (w >= 100 && h >= 100) ? 2 : 1;

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
            float* pNx = (float*)nx.Data;
            float* pNy = (float*)ny.Data;
            int stepN = (int)(nx.Step() / sizeof(float));

            Parallel.ForEach(angleList, angle =>
            {
                // Lấy features đã xoay sẵn từ Cache
                var rotSet = model.GetOrCreateRotated(angle);
                var rotFeat = rotSet.Features;

                for (int cy = 0; cy < h; cy += gridStep)
                {
                    for (int cx = 0; cx < w; cx += gridStep)
                    {
                        // Anchor Check: Kiểm tra điểm mỏ neo rotFeat[0]
                        int ax = cx + rotFeat[0].Dx;
                        int ay = cy + rotFeat[0].Dy;
                        if ((uint)ax >= (uint)w || (uint)ay >= (uint)h) continue;

                        int aidx = ay * stepN + ax;
                        float adot = pNx[aidx] * rotFeat[0].Gx + pNy[aidx] * rotFeat[0].Gy;
                        if (adot <= 0.1f)
                        {
                            // Điểm mỏ neo không trùng khớp hướng biên => bỏ qua ngay không tính tiếp
                            continue;
                        }

                        float scoreSum = adot;
                        float maxRemaining = N - 1;

                        int i = 1;
                        for (; i <= N - 4; i += 4)
                        {
                            int px0 = cx + rotFeat[i].Dx; int py0 = cy + rotFeat[i].Dy;
                            if ((uint)px0 < (uint)w && (uint)py0 < (uint)h)
                            {
                                int idx0 = py0 * stepN + px0;
                                float dot0 = pNx[idx0] * rotFeat[i].Gx + pNy[idx0] * rotFeat[i].Gy;
                                if (dot0 > 0) scoreSum += dot0;
                            }

                            int px1 = cx + rotFeat[i + 1].Dx; int py1 = cy + rotFeat[i + 1].Dy;
                            if ((uint)px1 < (uint)w && (uint)py1 < (uint)h)
                            {
                                int idx1 = py1 * stepN + px1;
                                float dot1 = pNx[idx1] * rotFeat[i + 1].Gx + pNy[idx1] * rotFeat[i + 1].Gy;
                                if (dot1 > 0) scoreSum += dot1;
                            }

                            int px2 = cx + rotFeat[i + 2].Dx; int py2 = cy + rotFeat[i + 2].Dy;
                            if ((uint)px2 < (uint)w && (uint)py2 < (uint)h)
                            {
                                int idx2 = py2 * stepN + px2;
                                float dot2 = pNx[idx2] * rotFeat[i + 2].Gx + pNy[idx2] * rotFeat[i + 2].Gy;
                                if (dot2 > 0) scoreSum += dot2;
                            }

                            int px3 = cx + rotFeat[i + 3].Dx; int py3 = cy + rotFeat[i + 3].Dy;
                            if ((uint)px3 < (uint)w && (uint)py3 < (uint)h)
                            {
                                int idx3 = py3 * stepN + px3;
                                float dot3 = pNx[idx3] * rotFeat[i + 3].Gx + pNy[idx3] * rotFeat[i + 3].Gy;
                                if (dot3 > 0) scoreSum += dot3;
                            }

                            maxRemaining -= 4.0f;
                            if ((scoreSum + maxRemaining) / N < minScore)
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
                                int idx = py * stepN + px;
                                float dot = pNx[idx] * rotFeat[i].Gx + pNy[idx] * rotFeat[i].Gy;
                                if (dot > 0) scoreSum += dot;
                            }
                        }

                        float finalScore = scoreSum / N;
                        if (finalScore >= minScore)
                        {
                            results.Add((finalScore, angle, cx, cy));
                        }

                    NextCandidate:;
                    }
                }
            });
        }

        return results.ToList();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void RefineSearchFastPreRotated(
        Mat nx, Mat ny, Mvp3TemplateModel model,
        double startX, double startY, double startAngle,
        int searchRadius, double angleRange, double angleStep,
        out double bestX, out double bestY, out double bestAngle, out double bestScore)
    {
        bestX = startX;
        bestY = startY;
        bestAngle = startAngle;
        bestScore = 0;

        int w = nx.Width;
        int h = nx.Height;
        int N = model.Features.Length;
        if (N == 0) return;

        unsafe
        {
            float* pNx = (float*)nx.Data;
            float* pNy = (float*)ny.Data;
            int stepN = (int)(nx.Step() / sizeof(float));

            int cyCenter = (int)Math.Round(startY);
            int cxCenter = (int)Math.Round(startX);

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

                        float scoreSum = 0;
                        for (int i = 0; i < N; i++)
                        {
                            int px = cx + rotFeat[i].Dx;
                            int py = cy + rotFeat[i].Dy;
                            if ((uint)px < (uint)w && (uint)py < (uint)h)
                            {
                                int idx = py * stepN + px;
                                float dot = pNx[idx] * rotFeat[i].Gx + pNy[idx] * rotFeat[i].Gy;
                                if (dot > 0) scoreSum += dot;
                            }
                        }

                        float score = scoreSum / N;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestX = cx;
                            bestY = cy;
                            bestAngle = angle;
                        }
                    }
                }
            }
        }
    }

    private static void SubPixelRefineLocal(
        Mat localNx, Mat localNy, Mvp3TemplateModel model,
        double x, double y, double angle, double stepDeg,
        out Point2d subPixelCenter, out double subPixelAngle, out double finalScore)
    {
        double[,] grid = new double[3, 3];
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                RefineSearchFastPreRotated(
                    localNx, localNy, model,
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
        RefineSearchFastPreRotated(localNx, localNy, model, x, y, angle - aStep, searchRadius: 0, angleRange: 0, angleStep: 1, out _, out _, out _, out double scoreMinusA);
        RefineSearchFastPreRotated(localNx, localNy, model, x, y, angle + aStep, searchRadius: 0, angleRange: 0, angleStep: 1, out _, out _, out _, out double scorePlusA);

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
