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

namespace VisionInspectionApp.VisionEngine
{
    public struct VectorFeaturePoint
    {
        public float X;
        public float Y;
        public float Gx;
        public float Gy;
    }

    public sealed class Mvp2TemplateModel
    {
        public VectorFeaturePoint[] Features { get; set; } = Array.Empty<VectorFeaturePoint>();
        public Point2f Center { get; set; }
        public Size Size { get; set; }
    }

    public static class MvpShapeMatch2Engine
    {
        private static readonly ConcurrentDictionary<string, Mvp2TemplateModel[]> _templateModelCache = new(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache()
        {
            _templateModelCache.Clear();
        }

        public static Mvp2TemplateModel ExtractTemplateModel(
            Mat templateGray,
            int edgeThresh = 25,
            int lengthThresh = 8,
            bool autoThresh = true,
            Mat? eraserMask = null)
        {
            if (templateGray == null || templateGray.Empty())
            {
                return new Mvp2TemplateModel();
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
                return new Mvp2TemplateModel
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

            int targetN = Math.Clamp(chosenPoints.Count, 80, 140);
            if (chosenPoints.Count > targetN)
            {
                chosenPoints = chosenPoints.OrderByDescending(p => p.Mag).Take(targetN).ToList();
            }

            var features = new List<VectorFeaturePoint>(chosenPoints.Count);
            foreach (var item in chosenPoints)
            {
                features.Add(new VectorFeaturePoint
                {
                    X = item.Pt.X - centerX,
                    Y = item.Pt.Y - centerY,
                    Gx = item.Gx,
                    Gy = item.Gy
                });
            }

            return new Mvp2TemplateModel
            {
                Features = features.ToArray(),
                Center = new Point2f(centerX, centerY),
                Size = new Size(gray.Width, gray.Height)
            };
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

            // Adaptive Pyramid Level based on Search ROI Dimensions
            int maxPyramidLevel = 2;
            if (roiInput.Width >= 1000 && roiInput.Height >= 800) maxPyramidLevel = 3;
            else if (roiInput.Width >= 400 && roiInput.Height >= 300) maxPyramidLevel = 2;
            else if (roiInput.Width >= 150 && roiInput.Height >= 100) maxPyramidLevel = 1;
            else maxPyramidLevel = 0;

            if (def.MvpMaxPyramidLayers > 0)
            {
                maxPyramidLevel = Math.Min(maxPyramidLevel, Math.Clamp(def.MvpMaxPyramidLayers - 1, 0, 3));
            }
            while (maxPyramidLevel > 0 && (templInput.Width / (1 << maxPyramidLevel) < 16 || templInput.Height / (1 << maxPyramidLevel) < 16))
            {
                maxPyramidLevel--;
            }

            Mat[] pyrRoi = new Mat[maxPyramidLevel + 1];
            pyrRoi[0] = roiInput.Clone();
            for (int l = 1; l <= maxPyramidLevel; l++)
            {
                pyrRoi[l] = new Mat();
                Cv2.PyrDown(pyrRoi[l - 1], pyrRoi[l]);
            }

            string cacheKey = $"{def.Name}_{edgeThresh}_{lengthThresh}_{autoThresh}_{templInput.Width}x{templInput.Height}_{def.MvpEraserMask?.Length ?? 0}_{maxPyramidLevel}";
            
            Mvp2TemplateModel[] pyrModels = _templateModelCache.GetOrAdd(cacheKey, _ =>
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

                var models = new Mvp2TemplateModel[maxPyramidLevel + 1];
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

            Mat?[] pyrNx = new Mat?[maxPyramidLevel + 1];
            Mat?[] pyrNy = new Mat?[maxPyramidLevel + 1];

            double targetMinScore = def.MinScore > 0 ? def.MinScore : (def.MatchScoreThreshold > 0 ? def.MatchScoreThreshold : 0.6);

            double coarseAngleStep = Math.Clamp(stepDeg * (1 << maxPyramidLevel), 1.0, 3.0);
            int coarseLvl = maxPyramidLevel;
            GetOrComputeGradientGrid(pyrRoi[coarseLvl], ref pyrNx[coarseLvl], ref pyrNy[coarseLvl]);

            var candidates = CoarseSearchParallel(pyrNx[coarseLvl]!, pyrNy[coarseLvl]!, pyrModels[coarseLvl].Features, minAngleDeg, maxAngleDeg, coarseAngleStep, targetMinScore * 0.35);

            if (candidates.Count == 0)
            {
                candidates = CoarseSearchParallel(pyrNx[coarseLvl]!, pyrNy[coarseLvl]!, pyrModels[coarseLvl].Features, minAngleDeg, maxAngleDeg, coarseAngleStep, 0.10);
            }

            if (candidates.Count == 0)
            {
                for (int l = 0; l <= maxPyramidLevel; l++) { pyrRoi[l].Dispose(); pyrNx[l]?.Dispose(); pyrNy[l]?.Dispose(); }
                var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
                return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
            }

            // Top candidates for pyramid refinement (Max 4 candidates)
            var topCandidates = candidates.OrderByDescending(c => c.Score).Take(4).ToList();
            var zeroCand = candidates.OrderBy(c => Math.Abs(c.Angle)).FirstOrDefault();
            if (!topCandidates.Any(c => Math.Abs(c.Angle - zeroCand.Angle) < 1e-4))
            {
                topCandidates.Add(zeroCand);
            }

            double bestScore = 0;
            double bestAngle = 0;
            Point2d bestCenterLvl0 = new Point2d();

            // Refine intermediate pyramid layers (Level > 0)
            var refinedCandidates = new List<(double X, double Y, double Angle, double Score)>();

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

                    GetOrComputeGradientGrid(pyrRoi[lvl], ref pyrNx[lvl], ref pyrNy[lvl]);

                    RefineSearchFast(
                        pyrNx[lvl]!, pyrNy[lvl]!, pyrModels[lvl].Features,
                        curScaleX, curScaleY, curAngle,
                        searchRadius: 3, angleRange: deltaA, angleStep: Math.Clamp(stepDeg * (1 << lvl), 0.5, 2.0),
                        out double refX, out double refY, out double refAngle, out double refScore);

                    curX = refX / lvlScale;
                    curY = refY / lvlScale;
                    curAngle = refAngle;
                    lastScore = refScore;
                }

                refinedCandidates.Add((curX, curY, curAngle, lastScore));
            }

            // Select only the single BEST candidate for Level 0 refinement
            var bestCand = refinedCandidates.OrderByDescending(c => c.Score).First();
            double finalX = bestCand.X;
            double finalY = bestCand.Y;
            double finalAngle = bestCand.Angle;

            GetOrComputeGradientGrid(pyrRoi[0], ref pyrNx[0], ref pyrNy[0]);

            RefineSearchLevel0(
                pyrNx[0]!, pyrNy[0]!, pyrModels[0].Features,
                finalX, finalY, finalAngle,
                searchRadius: 2, angleRange: 0.8, angleStep: Math.Clamp(stepDeg, 0.1, 0.5),
                out double refLvl0X, out double refLvl0Y, out double refLvl0Angle, out double refLvl0Score);

            bestCenterLvl0 = new Point2d(refLvl0X, refLvl0Y);
            bestAngle = refLvl0Angle;
            bestScore = refLvl0Score;

            // Sub-pixel parabolic peak refinement at Level 0
            SubPixelRefine(pyrNx[0]!, pyrNy[0]!, pyrModels[0].Features, bestCenterLvl0.X, bestCenterLvl0.Y, bestAngle, stepDeg, out Point2d subPixelCenter, out double subPixelAngle, out double finalScore);

            for (int l = 0; l <= maxPyramidLevel; l++) { pyrRoi[l].Dispose(); pyrNx[l]?.Dispose(); pyrNy[l]?.Dispose(); }

            if (finalScore > 0.95) finalScore = 1.0;

            Point2d finalWorldCenter = new Point2d(roiRect.X + subPixelCenter.X, roiRect.Y + subPixelCenter.Y);

            Rect matchRect = new Rect(
                (int)Math.Round(finalWorldCenter.X - templInput.Width / 2.0),
                (int)Math.Round(finalWorldCenter.Y - templInput.Height / 2.0),
                templInput.Width,
                templInput.Height);

            return new MatchResult(finalWorldCenter, Math.Clamp(finalScore, 0.0, 1.0), subPixelAngle, matchRect);
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
        private static List<(double Score, double Angle, double X, double Y)> CoarseSearchParallel(
            Mat nx, Mat ny, VectorFeaturePoint[] features,
            double minAngle, double maxAngle, double angleStep, double minScore)
        {
            var results = new ConcurrentBag<(double Score, double Angle, double X, double Y)>();
            int w = nx.Width;
            int h = nx.Height;
            int N = features.Length;
            if (N == 0 || w < 2 || h < 2) return results.ToList();

            int gridStep = (w >= 100 && h >= 100) ? 2 : 1;

            var angleList = new List<double>();
            for (double a = minAngle; a <= maxAngle + 1e-5; a += angleStep)
            {
                angleList.Add(a);
            }

            unsafe
            {
                float* pNx = (float*)nx.Data;
                float* pNy = (float*)ny.Data;
                int stepN = (int)(nx.Step() / sizeof(float));

                Parallel.ForEach(angleList, angle =>
                {
                    double rad = angle * Math.PI / 180.0;
                    float cosA = (float)Math.Cos(rad);
                    float sinA = (float)Math.Sin(rad);

                    var rotFeat = new (int Dx, int Dy, float Gx, float Gy)[N];
                    for (int i = 0; i < N; i++)
                    {
                        float rx = features[i].X * cosA - features[i].Y * sinA;
                        float ry = features[i].X * sinA + features[i].Y * cosA;
                        float rGx = features[i].Gx * cosA - features[i].Gy * sinA;
                        float rGy = features[i].Gx * sinA + features[i].Gy * cosA;
                        rotFeat[i] = ((int)Math.Round(rx), (int)Math.Round(ry), rGx, rGy);
                    }

                    int startX = 0;
                    int endX = w;
                    int startY = 0;
                    int endY = h;

                    for (int cy = startY; cy < endY; cy += gridStep)
                    {
                        for (int cx = startX; cx < endX; cx += gridStep)
                        {
                            float scoreSum = 0;
                            float maxRemaining = N;

                            int i = 0;
                            for (; i <= N - 4; i += 4)
                            {
                                int px0 = cx + rotFeat[i].Dx; int py0 = cy + rotFeat[i].Dy;
                                if (px0 >= 0 && px0 < w && py0 >= 0 && py0 < h)
                                {
                                    int idx0 = py0 * stepN + px0;
                                    float dot0 = pNx[idx0] * rotFeat[i].Gx + pNy[idx0] * rotFeat[i].Gy;
                                    if (dot0 > 0) scoreSum += dot0;
                                }

                                int px1 = cx + rotFeat[i + 1].Dx; int py1 = cy + rotFeat[i + 1].Dy;
                                if (px1 >= 0 && px1 < w && py1 >= 0 && py1 < h)
                                {
                                    int idx1 = py1 * stepN + px1;
                                    float dot1 = pNx[idx1] * rotFeat[i + 1].Gx + pNy[idx1] * rotFeat[i + 1].Gy;
                                    if (dot1 > 0) scoreSum += dot1;
                                }

                                int px2 = cx + rotFeat[i + 2].Dx; int py2 = cy + rotFeat[i + 2].Dy;
                                if (px2 >= 0 && px2 < w && py2 >= 0 && py2 < h)
                                {
                                    int idx2 = py2 * stepN + px2;
                                    float dot2 = pNx[idx2] * rotFeat[i + 2].Gx + pNy[idx2] * rotFeat[i + 2].Gy;
                                    if (dot2 > 0) scoreSum += dot2;
                                }

                                int px3 = cx + rotFeat[i + 3].Dx; int py3 = cy + rotFeat[i + 3].Dy;
                                if (px3 >= 0 && px3 < w && py3 >= 0 && py3 < h)
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

                                if (px >= 0 && px < w && py >= 0 && py < h)
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
        private static void RefineSearchFast(
            Mat nx, Mat ny, VectorFeaturePoint[] features,
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
            int N = features.Length;
            if (N == 0) return;

            unsafe
            {
                float* pNx = (float*)nx.Data;
                float* pNy = (float*)ny.Data;
                int stepN = (int)(nx.Step() / sizeof(float));

                for (double angle = startAngle - angleRange; angle <= startAngle + angleRange + 1e-5; angle += angleStep)
                {
                    double rad = angle * Math.PI / 180.0;
                    float cosA = (float)Math.Cos(rad);
                    float sinA = (float)Math.Sin(rad);

                    var rotFeat = new (int Dx, int Dy, float Gx, float Gy)[N];
                    for (int i = 0; i < N; i++)
                    {
                        float rx = features[i].X * cosA - features[i].Y * sinA;
                        float ry = features[i].X * sinA + features[i].Y * cosA;
                        float rGx = features[i].Gx * cosA - features[i].Gy * sinA;
                        float rGy = features[i].Gx * sinA + features[i].Gy * cosA;
                        rotFeat[i] = ((int)Math.Round(rx), (int)Math.Round(ry), rGx, rGy);
                    }

                    int cyCenter = (int)Math.Round(startY);
                    int cxCenter = (int)Math.Round(startX);

                    for (int cy = cyCenter - searchRadius; cy <= cyCenter + searchRadius; cy++)
                    {
                        if (cy < 0 || cy >= h) continue;
                        for (int cx = cxCenter - searchRadius; cx <= cxCenter + searchRadius; cx++)
                        {
                            if (cx < 0 || cx >= w) continue;

                            float scoreSum = 0;
                            for (int i = 0; i < N; i++)
                            {
                                int px = cx + rotFeat[i].Dx;
                                int py = cy + rotFeat[i].Dy;

                                if (px >= 0 && px < w && py >= 0 && py < h)
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void RefineSearchLevel0(
            Mat nx, Mat ny, VectorFeaturePoint[] features,
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
            int N = features.Length;
            if (N == 0) return;

            unsafe
            {
                float* pNx = (float*)nx.Data;
                float* pNy = (float*)ny.Data;
                int stepN = (int)(nx.Step() / sizeof(float));

                for (double angle = startAngle - angleRange; angle <= startAngle + angleRange + 1e-5; angle += angleStep)
                {
                    double rad = angle * Math.PI / 180.0;
                    float cosA = (float)Math.Cos(rad);
                    float sinA = (float)Math.Sin(rad);

                    var rotFeat = new (int Dx, int Dy, float Gx, float Gy)[N];
                    for (int i = 0; i < N; i++)
                    {
                        float rx = features[i].X * cosA - features[i].Y * sinA;
                        float ry = features[i].X * sinA + features[i].Y * cosA;
                        float rGx = features[i].Gx * cosA - features[i].Gy * sinA;
                        float rGy = features[i].Gx * sinA + features[i].Gy * cosA;
                        rotFeat[i] = ((int)Math.Round(rx), (int)Math.Round(ry), rGx, rGy);
                    }

                    int cyCenter = (int)Math.Round(startY);
                    int cxCenter = (int)Math.Round(startX);

                    for (int cy = cyCenter - searchRadius; cy <= cyCenter + searchRadius; cy++)
                    {
                        if (cy < 0 || cy >= h) continue;
                        for (int cx = cxCenter - searchRadius; cx <= cxCenter + searchRadius; cx++)
                        {
                            if (cx < 0 || cx >= w) continue;

                            float scoreSum = 0;
                            for (int i = 0; i < N; i++)
                            {
                                int px = cx + rotFeat[i].Dx;
                                int py = cy + rotFeat[i].Dy;

                                float maxDot = 0;
                                for (int vy = -1; vy <= 1; vy++)
                                {
                                    int npy = py + vy;
                                    if (npy < 0 || npy >= h) continue;
                                    int rowIdx = npy * stepN;
                                    for (int vx = -1; vx <= 1; vx++)
                                    {
                                        int npx = px + vx;
                                        if (npx < 0 || npx >= w) continue;
                                        int idx = rowIdx + npx;
                                        float dot = pNx[idx] * rotFeat[i].Gx + pNy[idx] * rotFeat[i].Gy;
                                        if (dot > maxDot) maxDot = dot;
                                    }
                                }
                                scoreSum += maxDot;
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

        private static void SubPixelRefine(
            Mat nx, Mat ny, VectorFeaturePoint[] features,
            double x, double y, double angle, double stepDeg,
            out Point2d subPixelCenter, out double subPixelAngle, out double finalScore)
        {
            double[,] grid = new double[3, 3];
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    RefineSearchLevel0(nx, ny, features, x + dx, y + dy, angle, searchRadius: 0, angleRange: 0, angleStep: 1, out _, out _, out _, out double score);
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
            RefineSearchLevel0(nx, ny, features, x, y, angle - aStep, searchRadius: 0, angleRange: 0, angleStep: 1, out _, out _, out _, out double scoreMinusA);
            RefineSearchLevel0(nx, ny, features, x, y, angle + aStep, searchRadius: 0, angleRange: 0, angleStep: 1, out _, out _, out _, out double scorePlusA);

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
}
