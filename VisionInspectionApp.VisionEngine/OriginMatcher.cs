using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.VisionEngine;

public sealed class OriginMatcher
{
    private readonly struct GrayMat : IDisposable
    {
        public GrayMat(Mat mat, Mat? owned)
        {
            Mat = mat;
            _owned = owned;
        }

        public Mat Mat { get; }
        private readonly Mat? _owned;

        public void Dispose()
        {
            _owned?.Dispose();
        }
    }

    public MatchResult MatchWithRotation(
        Mat image,
        PointDefinition definition,
        Mat templateGray,
        PreprocessSettings? preprocess,
        double minAngleDeg = -10.0,
        double maxAngleDeg = 10.0,
        double stepDeg = 1.0)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (templateGray is null) throw new ArgumentNullException(nameof(templateGray));

        var roiRect = ToRect(definition.SearchRoi, image.Width, image.Height);
        if (roiRect.Width <= 0 || roiRect.Height <= 0 || templateGray.Empty())
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        using var roi = new Mat(image, roiRect);
        using var roiGray = EnsureGrayBorrowed(roi);

        double effectiveStep = stepDeg > 0.000001 ? stepDeg : (definition.AngleStep > 0 ? definition.AngleStep : 1.0);

        if (definition.OriginAlgorithm == OriginAlgorithm.MvpShapeMatch2)
        {
            var baseAngle2 = definition.TemplateRoi.Angle;
            double searchMin2 = baseAngle2 + minAngleDeg;
            double searchMax2 = baseAngle2 + maxAngleDeg;
            return MvpShapeMatch2Engine.Match(roiGray.Mat, templateGray, definition, searchMin2, searchMax2, effectiveStep, roiRect);
        }

        if (definition.OriginAlgorithm == OriginAlgorithm.FeatureBased)
        {
            try
            {
                return MatchByFeatureBased(roiGray.Mat, templateGray, definition, preprocess, roiRect);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OriginMatcher] FeatureBased match error: {ex.Message}, falling back to template match.");
                return FallbackToTemplateMatch(roiGray.Mat, templateGray, definition, 0.0, preprocess, roiRect);
            }
        }

        var baseAngle = definition.TemplateRoi.Angle;
        double searchMin = baseAngle + minAngleDeg;
        double searchMax = baseAngle + maxAngleDeg;

        return MatchByPyramid(roiGray.Mat, templateGray, definition, preprocess, searchMin, searchMax, effectiveStep, roiRect);
    }

    public static Mat RotateTemplateCentered(Mat src, double angleDeg)
    {
        if (Math.Abs(angleDeg) < 1e-6)
        {
            return src.Clone();
        }

        double rad = angleDeg * Math.PI / 180.0;
        double cos = Math.Abs(Math.Cos(rad));
        double sin = Math.Abs(Math.Sin(rad));

        int origW = src.Width;
        int origH = src.Height;

        int newW = (int)Math.Ceiling(origW * cos + origH * sin);
        int newH = (int)Math.Ceiling(origW * sin + origH * cos);

        Point2f center = new Point2f(origW / 2.0f, origH / 2.0f);
        using var rotMat = Cv2.GetRotationMatrix2D(center, -angleDeg, 1.0);

        rotMat.Set(0, 2, rotMat.At<double>(0, 2) + (newW - origW) / 2.0);
        rotMat.Set(1, 2, rotMat.At<double>(1, 2) + (newH - origH) / 2.0);

        var dst = new Mat(new Size(newW, newH), src.Type(), Scalar.Black);
        Cv2.WarpAffine(src, dst, rotMat, new Size(newW, newH), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        return dst;
    }

    private MatchResult MatchByPyramid(
        Mat roiGray,
        Mat templateGray,
        PointDefinition def,
        PreprocessSettings? preprocess,
        double minAngleDeg,
        double maxAngleDeg,
        double stepDeg,
        Rect roiRect)
    {
        if (roiGray.Empty() || templateGray.Empty())
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        using var templPrep = PreprocessTemplateForMatch(templateGray, preprocess);
        if (templPrep.Empty() || templPrep.Width <= 0 || templPrep.Height <= 0)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        // Determine maximum pyramid level
        int maxPyramidLevel = 2;
        if (def.OriginAlgorithm == OriginAlgorithm.MvpShapeMatch && def.MvpMaxPyramidLayers > 0)
        {
            maxPyramidLevel = Math.Clamp(def.MvpMaxPyramidLayers - 1, 0, 3);
        }

        while (maxPyramidLevel > 0 && (templPrep.Width / (1 << maxPyramidLevel) < 16 || templPrep.Height / (1 << maxPyramidLevel) < 16))
        {
            maxPyramidLevel--;
        }

        // BUILD GRAYSCALE PYRAMID FIRST (To preserve sharp edge gradients at high pyramid levels)
        Mat[] pyrRoiGray = new Mat[maxPyramidLevel + 1];
        Mat[] pyrTemplGray = new Mat[maxPyramidLevel + 1];

        pyrRoiGray[0] = roiGray.Clone();
        pyrTemplGray[0] = templPrep.Clone();

        for (int l = 1; l <= maxPyramidLevel; l++)
        {
            pyrRoiGray[l] = new Mat();
            pyrTemplGray[l] = new Mat();
            Cv2.PyrDown(pyrRoiGray[l - 1], pyrRoiGray[l]);
            Cv2.PyrDown(pyrTemplGray[l - 1], pyrTemplGray[l]);
        }

        // BUILD FEATURE MAPS AT EACH LEVEL FROM GRAYSCALE PYRAMIDS
        Mat[] pyrRoiFeature = new Mat[maxPyramidLevel + 1];
        Mat[] pyrTemplFeature = new Mat[maxPyramidLevel + 1];

        bool isGeometric = def.OriginAlgorithm == OriginAlgorithm.MvpShapeMatch
                        || def.OriginAlgorithm == OriginAlgorithm.ShapeBased
                        || def.OriginAlgorithm == OriginAlgorithm.ShapePyramid;

        int edgeThresh = def.MvpEdgeThreshold > 0 ? def.MvpEdgeThreshold : (def.EdgeThresholdMin > 0 ? def.EdgeThresholdMin : 25);

        for (int l = 0; l <= maxPyramidLevel; l++)
        {
            pyrRoiFeature[l] = new Mat();
            pyrTemplFeature[l] = new Mat();

            if (isGeometric)
            {
                // Sobel magnitude creates continuous, robust edge gradient maps across all pyramid levels
                using var gxR = new Mat(); using var gyR = new Mat();
                Cv2.Sobel(pyrRoiGray[l], gxR, MatType.CV_32F, 1, 0, 3);
                Cv2.Sobel(pyrRoiGray[l], gyR, MatType.CV_32F, 0, 1, 3);
                using var magR = new Mat();
                Cv2.Magnitude(gxR, gyR, magR);
                using var mag8R = new Mat();
                magR.ConvertTo(mag8R, MatType.CV_8U);
                Cv2.GaussianBlur(mag8R, pyrRoiFeature[l], new Size(3, 3), 1.0);

                using var gxT = new Mat(); using var gyT = new Mat();
                Cv2.Sobel(pyrTemplGray[l], gxT, MatType.CV_32F, 1, 0, 3);
                Cv2.Sobel(pyrTemplGray[l], gyT, MatType.CV_32F, 0, 1, 3);
                using var magT = new Mat();
                Cv2.Magnitude(gxT, gyT, magT);
                using var mag8T = new Mat();
                magT.ConvertTo(mag8T, MatType.CV_8U);

                if (l == 0 && def.MvpEraserMask != null && def.MvpEraserMask.Length > 0)
                {
                    try
                    {
                        using var decodedMask = Cv2.ImDecode(def.MvpEraserMask, ImreadModes.Grayscale);
                        if (decodedMask != null && !decodedMask.Empty() && decodedMask.Width == mag8T.Width && decodedMask.Height == mag8T.Height)
                        {
                            Cv2.BitwiseAnd(mag8T, decodedMask, mag8T);
                        }
                    }
                    catch { }
                }

                Cv2.GaussianBlur(mag8T, pyrTemplFeature[l], new Size(3, 3), 1.0);
            }
            else
            {
                pyrRoiGray[l].CopyTo(pyrRoiFeature[l]);
                pyrTemplGray[l].CopyTo(pyrTemplFeature[l]);
            }
        }

        try
        {
            // Level maxPyramidLevel: Coarse angle & position sweep
            int coarseLvl = maxPyramidLevel;
            double coarseScale = 1.0 / (1 << coarseLvl);
            double coarseStep = Math.Max(stepDeg * 2.0, 2.0);

            var coarseAngleCandidates = new List<(double Score, double Angle, Point2d CenterInLevel0)>();

            // Align coarse angle loop grid to 0.0° so 0.0° is explicitly tested!
            double startCoarseAngle = Math.Floor(minAngleDeg / coarseStep) * coarseStep;
            double angle = startCoarseAngle;
            while (angle <= maxAngleDeg + 0.000001)
            {
                if (angle >= minAngleDeg - 0.000001)
                {
                    using var templRot = RotateTemplateCentered(pyrTemplFeature[coarseLvl], angle);
                    var crop = ContentRectFromNonZero(templRot, pad: 2);
                    if (crop.Width > 0 && crop.Height > 0 && pyrRoiFeature[coarseLvl].Width >= crop.Width && pyrRoiFeature[coarseLvl].Height >= crop.Height)
                    {
                        using var templCropped = new Mat(templRot, crop);
                        using var resMat = new Mat();
                        Cv2.MatchTemplate(pyrRoiFeature[coarseLvl], templCropped, resMat, TemplateMatchModes.CCoeffNormed);
                        Cv2.MinMaxLoc(resMat, out _, out var maxVal, out _, out var maxLoc);

                        var centerInCoarse = new Point2d(maxLoc.X + (templRot.Width / 2.0 - crop.X), maxLoc.Y + (templRot.Height / 2.0 - crop.Y));
                        var centerInLevel0 = new Point2d(centerInCoarse.X / coarseScale, centerInCoarse.Y / coarseScale);
                        coarseAngleCandidates.Add((maxVal, angle, centerInLevel0));
                    }
                }
                angle += coarseStep;
            }

            if (coarseAngleCandidates.Count == 0)
            {
                var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
                return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
            }

            // Keep top 5 candidates, AND ALWAYS preserve candidate closest to 0.0° as anchor
            var currentCandidates = coarseAngleCandidates.OrderByDescending(c => c.Score).Take(5).ToList();
            var zeroCand = coarseAngleCandidates.OrderBy(c => Math.Abs(c.Angle)).FirstOrDefault();
            if (!currentCandidates.Any(c => Math.Abs(c.Angle - zeroCand.Angle) < 1e-4))
            {
                currentCandidates.Add(zeroCand);
            }

            // Refine through intermediate & fine levels
            for (int lvl = maxPyramidLevel - 1; lvl >= 0; lvl--)
            {
                double curLvlScale = 1.0 / (1 << lvl);
                double lvlStep = (lvl == 0) ? stepDeg : Math.Max(stepDeg, 1.0);
                int searchRadiusPx = (lvl == 0) ? 16 : 24;

                var nextCandidates = new List<(double Score, double Angle, Point2d CenterInLevel0)>();

                foreach (var cand in currentCandidates)
                {
                    Point2d expectedCenterInLvl = new Point2d(cand.CenterInLevel0.X * curLvlScale, cand.CenterInLevel0.Y * curLvlScale);
                    double candAngle = cand.Angle;

                    double angleStart = Math.Max(minAngleDeg, candAngle - coarseStep);
                    double angleEnd = Math.Min(maxAngleDeg, candAngle + coarseStep);

                    angle = angleStart;
                    while (angle <= angleEnd + 0.000001)
                    {
                        using var templRot = RotateTemplateCentered(pyrTemplFeature[lvl], angle);
                        var crop = ContentRectFromNonZero(templRot, pad: 2);
                        if (crop.Width > 0 && crop.Height > 0 && pyrRoiFeature[lvl].Width >= crop.Width && pyrRoiFeature[lvl].Height >= crop.Height)
                        {
                            using var templCropped = new Mat(templRot, crop);

                            int expectedTopLeftX = (int)Math.Round(expectedCenterInLvl.X - (templRot.Width / 2.0 - crop.X));
                            int expectedTopLeftY = (int)Math.Round(expectedCenterInLvl.Y - (templRot.Height / 2.0 - crop.Y));

                            int subX = Math.Max(0, expectedTopLeftX - searchRadiusPx);
                            int subY = Math.Max(0, expectedTopLeftY - searchRadiusPx);
                            int subW = Math.Min(pyrRoiFeature[lvl].Width - subX, templCropped.Width + searchRadiusPx * 2);
                            int subH = Math.Min(pyrRoiFeature[lvl].Height - subY, templCropped.Height + searchRadiusPx * 2);

                            if (subW >= templCropped.Width && subH >= templCropped.Height)
                            {
                                using var subRoi = new Mat(pyrRoiFeature[lvl], new Rect(subX, subY, subW, subH));
                                using var resMat = new Mat();
                                Cv2.MatchTemplate(subRoi, templCropped, resMat, TemplateMatchModes.CCoeffNormed);
                                Cv2.MinMaxLoc(resMat, out _, out var maxVal, out _, out var maxLoc);

                                var centerInLvl = new Point2d(subX + maxLoc.X + (templRot.Width / 2.0 - crop.X), subY + maxLoc.Y + (templRot.Height / 2.0 - crop.Y));
                                var centerInLevel0 = new Point2d(centerInLvl.X / curLvlScale, centerInLvl.Y / curLvlScale);

                                double finalScore = maxVal;
                                if (lvl == 0 && isGeometric)
                                {
                                    double geomScore = ComputeGeometricEdgeScore(
                                        pyrRoiFeature[0],
                                        pyrTemplFeature[0],
                                        centerInLevel0,
                                        angle,
                                        edgeThresh);
                                    if (geomScore > 0)
                                    {
                                        finalScore = geomScore;
                                    }
                                }

                                nextCandidates.Add((finalScore, angle, centerInLevel0));
                            }
                        }
                        angle += lvlStep;
                    }
                }

                if (nextCandidates.Count > 0)
                {
                    currentCandidates = nextCandidates.OrderByDescending(c => c.Score).Take(5).ToList();
                    var zeroCandNext = nextCandidates.OrderBy(c => Math.Abs(c.Angle)).FirstOrDefault();
                    if (!currentCandidates.Any(c => Math.Abs(c.Angle - zeroCandNext.Angle) < 1e-4))
                    {
                        currentCandidates.Add(zeroCandNext);
                    }
                }
            }

            var bestCand = currentCandidates.OrderByDescending(c => c.Score).First();
            double bestScore = bestCand.Score;
            double bestAngle = bestCand.Angle;
            Point2d bestCenterInRoi = bestCand.CenterInLevel0;

            // Adjust score to guarantee 1.0000 on near-perfect matches (> 0.985)
            if (bestScore > 0.985)
            {
                bestScore = 1.0;
            }

            var globalPos = new Point2d(bestCenterInRoi.X + roiRect.X, bestCenterInRoi.Y + roiRect.Y);
            var matchRect = new Rect(
                (int)Math.Round(globalPos.X - templateGray.Width / 2.0),
                (int)Math.Round(globalPos.Y - templateGray.Height / 2.0),
                templateGray.Width,
                templateGray.Height);

            return new MatchResult(globalPos, Math.Clamp(bestScore, 0.0, 1.0), bestAngle, matchRect);
        }
        finally
        {
            for (int l = 0; l <= maxPyramidLevel; l++)
            {
                pyrRoiGray[l]?.Dispose();
                pyrTemplGray[l]?.Dispose();
                pyrRoiFeature[l]?.Dispose();
                pyrTemplFeature[l]?.Dispose();
            }
        }
    }

    private static double ComputeGeometricEdgeScore(Mat roiEdgeMat, Mat templEdgeMat, Point2d centerInRoi, double angleDeg, int minGradientThresh)
    {
        if (roiEdgeMat is null || templEdgeMat is null || roiEdgeMat.Empty() || templEdgeMat.Empty())
        {
            return 0.0;
        }

        using var nzMat = new Mat();
        // Threshold template edges to extract strong contour points
        int thresh = Math.Max(15, minGradientThresh);
        using var binTempl = new Mat();
        Cv2.Threshold(templEdgeMat, binTempl, thresh, 255, ThresholdTypes.Binary);
        Cv2.FindNonZero(binTempl, nzMat);

        if (nzMat.Empty() || nzMat.Rows == 0)
        {
            return 0.0;
        }

        int nPoints = nzMat.Rows;
        int step = Math.Max(1, nPoints / 600); // Subsample up to 600 edge points for ultra-fast calculation

        double tplCenterX = templEdgeMat.Width / 2.0;
        double tplCenterY = templEdgeMat.Height / 2.0;

        double rad = angleDeg * Math.PI / 180.0;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);

        double totalScore = 0.0;
        int evaluatedCount = 0;

        int roiW = roiEdgeMat.Width;
        int roiH = roiEdgeMat.Height;

        for (int i = 0; i < nPoints; i += step)
        {
            Point pt = nzMat.At<Point>(i);
            byte tplVal = templEdgeMat.At<byte>(pt.Y, pt.X);
            if (tplVal < thresh) continue;

            double dx = pt.X - tplCenterX;
            double dy = pt.Y - tplCenterY;

            // Rotate offset by angle (clockwise)
            double rdx = dx * cos - dy * sin;
            double rdy = dx * sin + dy * cos;

            int targetX = (int)Math.Round(centerInRoi.X + rdx);
            int targetY = (int)Math.Round(centerInRoi.Y + rdy);

            double bestMatch = 0.0;
            for (int ny = -1; ny <= 1; ny++)
            {
                for (int nx = -1; nx <= 1; nx++)
                {
                    int qx = targetX + nx;
                    int qy = targetY + ny;
                    if (qx >= 0 && qx < roiW && qy >= 0 && qy < roiH)
                    {
                        byte v = roiEdgeMat.At<byte>(qy, qx);
                        if (v >= thresh)
                        {
                            double match = Math.Min(1.0, (double)v / Math.Max(1.0, (double)tplVal));
                            if (match > 0.8) match = 1.0;
                            if (match > bestMatch) bestMatch = match;
                        }
                    }
                }
            }
            totalScore += bestMatch;
            evaluatedCount++;
        }

        if (evaluatedCount <= 0) return 0.0;

        double rawRatio = totalScore / evaluatedCount;
        return rawRatio;
    }

    private MatchResult MatchByFeatureBased(Mat roiGray, Mat templateGray, PointDefinition definition, PreprocessSettings? preprocess, Rect roiRect)
    {
        using var templPrep = PreprocessTemplateForMatch(templateGray, preprocess);

        // Apply CLAHE (Contrast Limited Adaptive Histogram Equalization) to make SIFT invariant to camera gain and lighting shifts
        using var clahe = Cv2.CreateCLAHE(clipLimit: 3.0, tileGridSize: new Size(8, 8));
        using var roiClahe = new Mat();
        using var templClahe = new Mat();
        clahe.Apply(roiGray, roiClahe);
        clahe.Apply(templPrep, templClahe);

        using var detector = OpenCvSharp.Features2D.SIFT.Create();
        using var des1 = new Mat();
        using var des2 = new Mat();

        detector.DetectAndCompute(templClahe, null, out KeyPoint[] keypoints1, des1);
        detector.DetectAndCompute(roiClahe, null, out KeyPoint[] keypoints2, des2);

        if (des1.Empty() || des2.Empty() || des1.Rows < 4 || des2.Rows < 4)
        {
            return FallbackToTemplateMatch(roiGray, templateGray, definition, 0.0, preprocess, roiRect);
        }

        using var bf = new BFMatcher(NormTypes.L2, crossCheck: false);
        var knnMatches = bf.KnnMatch(des1, des2, k: 2);

        var goodMatchesList = new List<DMatch>();
        foreach (var m in knnMatches)
        {
            if (m.Length >= 2 && m[0].Distance < 0.75f * m[1].Distance)
            {
                goodMatchesList.Add(m[0]);
            }
        }

        var goodMatches = goodMatchesList.OrderBy(m => m.Distance).Take(60).ToArray();

        if (goodMatches.Length < 4)
        {
            return FallbackToTemplateMatch(roiGray, templateGray, definition, 0.0, preprocess, roiRect);
        }

        var pts1 = goodMatches.Select(m => new Point2d(keypoints1[m.QueryIdx].Pt.X, keypoints1[m.QueryIdx].Pt.Y)).ToArray();
        var pts2 = goodMatches.Select(m => new Point2d(keypoints2[m.TrainIdx].Pt.X, keypoints2[m.TrainIdx].Pt.Y)).ToArray();

        using var inliers = new Mat();
        using var M = Cv2.EstimateAffinePartial2D(InputArray.Create(pts1), InputArray.Create(pts2), inliers);

        if (M.Empty())
        {
            return FallbackToTemplateMatch(roiGray, templateGray, definition, 0.0, preprocess, roiRect);
        }

        int inlierCount = Cv2.CountNonZero(inliers);
        if (inlierCount < 4)
        {
            return FallbackToTemplateMatch(roiGray, templateGray, definition, 0.0, preprocess, roiRect);
        }

        // Convert 2x3 Affine Matrix to 3x3 Homography Matrix for perspective operations
        using var H = Mat.Eye(3, 3, MatType.CV_64FC1).ToMat();
        H.Set<double>(0, 0, M.At<double>(0, 0));
        H.Set<double>(0, 1, M.At<double>(0, 1));
        H.Set<double>(0, 2, M.At<double>(0, 2));
        H.Set<double>(1, 0, M.At<double>(1, 0));
        H.Set<double>(1, 1, M.At<double>(1, 1));
        H.Set<double>(1, 2, M.At<double>(1, 2));

        var m00 = M.At<double>(0, 0);
        var m01 = M.At<double>(0, 1);
        var m10 = M.At<double>(1, 0);
        var m11 = M.At<double>(1, 1);

        double det = m00 * m11 - m01 * m10;
        if (det <= 0.1 || det >= 10.0)
        {
            return FallbackToTemplateMatch(roiGray, templateGray, definition, 0.0, preprocess, roiRect);
        }

        // Calculate true 2D rigid rotation angle from Affine matrix
        var actualAngleDeg = Math.Atan2(m10, m00) * 180.0 / Math.PI;

        while (actualAngleDeg > 180.0) actualAngleDeg -= 360.0;
        while (actualAngleDeg < -180.0) actualAngleDeg += 360.0;

        double baseAngle = definition.TemplateRoi.Angle;
        double angleDiff = actualAngleDeg - baseAngle;
        while (angleDiff > 180.0) angleDiff -= 360.0;
        while (angleDiff < -180.0) angleDiff += 360.0;

        if (definition.MinAngle != 0 || definition.MaxAngle != 0)
        {
            if (angleDiff < definition.MinAngle - 10.0 || angleDiff > definition.MaxAngle + 10.0)
            {
                return FallbackToTemplateMatch(roiGray, templateGray, definition, 0.0, preprocess, roiRect);
            }
        }

        var pad = 4;
        using var H_warped = Mat.Eye(3, 3, MatType.CV_64FC1).ToMat();
        var h00 = H.At<double>(0, 0);
        var h01 = H.At<double>(0, 1);
        var h02 = H.At<double>(0, 2);
        var h10 = H.At<double>(1, 0);
        var h11 = H.At<double>(1, 1);
        var h12 = H.At<double>(1, 2);

        H_warped.Set<double>(0, 0, h00);
        H_warped.Set<double>(0, 1, h01);
        H_warped.Set<double>(0, 2, h02 - pad * (h00 + h01));
        H_warped.Set<double>(1, 0, h10);
        H_warped.Set<double>(1, 1, h11);
        H_warped.Set<double>(1, 2, h12 - pad * (h10 + h11));
        H_warped.Set<double>(2, 0, 0.0);
        H_warped.Set<double>(2, 1, 0.0);
        H_warped.Set<double>(2, 2, 1.0);

        using var warped = new Mat();
        Cv2.WarpPerspective(roiGray, warped, H_warped, new Size(templPrep.Width + 2 * pad, templPrep.Height + 2 * pad), InterpolationFlags.Linear | InterpolationFlags.WarpInverseMap);

        var maxVal = 0.0;
        using var res = new Mat();
        Cv2.MatchTemplate(warped, templPrep, res, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(res, out _, out maxVal, out _, out var maxLoc);

        double minScoreTarget = definition.MinScore > 0 ? definition.MinScore : 0.6;
        if (maxVal < minScoreTarget * 0.65)
        {
            return FallbackToTemplateMatch(roiGray, templateGray, definition, 0.0, preprocess, roiRect);
        }

        var offsetX = maxLoc.X - pad;
        var offsetY = maxLoc.Y - pad;

        var objCenter = new Point2d[] { new Point2d(templPrep.Width / 2.0 + offsetX, templPrep.Height / 2.0 + offsetY) };
        var sceneCenter = Cv2.PerspectiveTransform(objCenter, H);

        var centerInRoi = sceneCenter[0];
        var global = new Point2d(centerInRoi.X + roiRect.X, centerInRoi.Y + roiRect.Y);

        var objCorners = new Point2d[] {
            new Point2d(0, 0),
            new Point2d(templPrep.Width, 0),
            new Point2d(templPrep.Width, templPrep.Height),
            new Point2d(0, templPrep.Height)
        };
        var sceneCorners = Cv2.PerspectiveTransform(objCorners, H);
        var minX = sceneCorners.Min(p => p.X);
        var maxX = sceneCorners.Max(p => p.X);
        var minY = sceneCorners.Min(p => p.Y);
        var maxY = sceneCorners.Max(p => p.Y);

        var matchRect = new Rect((int)(roiRect.X + minX), (int)(roiRect.Y + minY), (int)(maxX - minX), (int)(maxY - minY));

        var featurePoints = new List<Point2d>();
        for (int i = 0; i < pts2.Length; i++)
        {
            byte isInlierVal = 0;
            if (inliers.Rows == 1 && i < inliers.Cols)
            {
                isInlierVal = inliers.At<byte>(0, i);
            }
            else if (inliers.Cols == 1 && i < inliers.Rows)
            {
                isInlierVal = inliers.At<byte>(i, 0);
            }
            else if (i < inliers.Total())
            {
                isInlierVal = inliers.Get<byte>(i);
            }

            if (isInlierVal != 0)
            {
                featurePoints.Add(new Point2d(pts2[i].X + roiRect.X, pts2[i].Y + roiRect.Y));
            }
        }

        return new MatchResult(global, Math.Clamp(maxVal, 0.0, 1.0), actualAngleDeg, matchRect, featurePoints);
    }

    private MatchResult FallbackToTemplateMatch(Mat roiGray, Mat templateGray, PointDefinition definition, double angleDeg, PreprocessSettings? preprocess, Rect roiRect)
    {
        using var tPrep = PreprocessTemplateForMatch(templateGray, preprocess);
        using var templGrayRot = RotateTemplateCentered(tPrep, angleDeg);
        var crop = ContentRectFromNonZero(templGrayRot, pad: 0);
        if (crop.Width <= 0 || crop.Height <= 0)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, angleDeg, roiRect);
        }
        using var templCrop = new Mat(templGrayRot, crop);
        var cw = Math.Min(templCrop.Width, roiGray.Width);
        var ch = Math.Min(templCrop.Height, roiGray.Height);
        var cx = (templCrop.Width - cw) / 2;
        var cy = (templCrop.Height - ch) / 2;
        using var t2 = new Mat(templCrop, new Rect(cx, cy, cw, ch));

        using var res = new Mat();
        Cv2.MatchTemplate(roiGray, t2, res, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(res, out _, out var maxV, out _, out var maxL);

        var cInRoi = new Point2d(maxL.X + t2.Width / 2.0, maxL.Y + t2.Height / 2.0);
        var g = new Point2d(cInRoi.X + roiRect.X, cInRoi.Y + roiRect.Y);
        var mRect = new Rect(roiRect.X + maxL.X, roiRect.Y + maxL.Y, t2.Width, t2.Height);
        return new MatchResult(g, Math.Clamp(maxV, 0.0, 1.0), angleDeg, mRect);
    }

    private static Rect ContentRectFromNonZero(Mat srcGray, int pad)
    {
        if (srcGray.Empty()) return new Rect(0, 0, 0, 0);

        using var nz = new Mat();
        Cv2.FindNonZero(srcGray, nz);
        if (nz.Empty() || nz.Rows == 0) return new Rect(0, 0, 0, 0);

        var r = Cv2.BoundingRect(nz);
        var x = Math.Max(0, r.X - pad);
        var y = Math.Max(0, r.Y - pad);
        var right = Math.Min(srcGray.Width, r.X + r.Width + pad);
        var bottom = Math.Min(srcGray.Height, r.Y + r.Height + pad);
        var w = Math.Max(0, right - x);
        var h = Math.Max(0, bottom - y);
        return new Rect(x, y, w, h);
    }

    private static GrayMat EnsureGrayBorrowed(Mat src)
    {
        if (src.Channels() == 1) return new GrayMat(src, owned: null);
        var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        return new GrayMat(gray, owned: gray);
    }

    private static Mat PreprocessTemplateForMatch(Mat templGrayOrBgr, PreprocessSettings? settings)
    {
        using var gray = EnsureGrayBorrowed(templGrayOrBgr);
        if (settings is null) return gray.Mat.Clone();

        var prep = new ImagePreprocessor();
        using var processed = prep.Run(gray.Mat, settings);

        if (processed.Channels() == 1) return processed.Clone();

        var processedGray = new Mat();
        Cv2.CvtColor(processed, processedGray, ColorConversionCodes.BGR2GRAY);
        return processedGray;
    }

    private static Rect ToRect(Roi roi, int imgW, int imgH)
    {
        var x = Math.Clamp(roi.X, 0, Math.Max(0, imgW - 1));
        var y = Math.Clamp(roi.Y, 0, Math.Max(0, imgH - 1));
        var w = Math.Clamp(roi.Width, 0, imgW - x);
        var h = Math.Clamp(roi.Height, 0, imgH - y);
        return new Rect(x, y, w, h);
    }
}
