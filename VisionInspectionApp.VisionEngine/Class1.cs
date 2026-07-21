using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.VisionEngine;

public interface IMeasurement
{
    string Name { get; }
}

public static class Geometry2D
{
    private static double Dot(Point2d a, Point2d b)
    {
        return a.X * b.X + a.Y * b.Y;
    }

    public static double Distance(Point2d a, Point2d b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static (double Dist, Point2d ClosestOnSegment) PointToSegmentDistance(Point2d p, Point2d a, Point2d b)
    {
        var ab = b - a;
        var ap = p - a;
        var ab2 = Dot(ab, ab);
        if (ab2 <= 1e-12)
        {
            return (Distance(p, a), a);
        }

        var t = Dot(ap, ab) / ab2;
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        var proj = new Point2d(a.X + t * ab.X, a.Y + t * ab.Y);
        return (Distance(p, proj), proj);
    }

    public static (double Dist, Point2d ClosestA, Point2d ClosestB) SegmentToSegmentDistance(Point2d a1, Point2d a2, Point2d b1, Point2d b2)
    {
        // Compute minimum of point-to-segment distances (good enough for 2D segments for our use case).
        var (d1, c1) = PointToSegmentDistance(a1, b1, b2);
        var (d2, c2) = PointToSegmentDistance(a2, b1, b2);
        var (d3, c3) = PointToSegmentDistance(b1, a1, a2);
        var (d4, c4) = PointToSegmentDistance(b2, a1, a2);

        var min = d1;
        var ca = a1;
        var cb = c1;

        if (d2 < min)
        {
            min = d2;
            ca = a2;
            cb = c2;
        }

        if (d3 < min)
        {
            min = d3;
            ca = c3;
            cb = b1;
        }

        if (d4 < min)
        {
            min = d4;
            ca = c4;
            cb = b2;
        }

        return (min, ca, cb);
    }
}

public sealed class LineDetector
{
    public LineDetectResult DetectLongestLine(Mat image, Roi searchRoi, int canny1, int canny2, int houghThreshold, int minLineLength, int maxLineGap)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        var roiRect = new Rect(searchRoi.X, searchRoi.Y, searchRoi.Width, searchRoi.Height)
            .Intersect(new Rect(0, 0, image.Width, image.Height));
        if (roiRect.Width <= 0 || roiRect.Height <= 0)
        {
            return new LineDetectResult(string.Empty, default, default, 0.0, false);
        }

        using var roi = new Mat(image, roiRect);
        using var gray = roi.Channels() == 1 ? roi.Clone() : roi.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var edges = new Mat();

        Cv2.Canny(gray, edges, canny1, canny2);

        var lines = Cv2.HoughLinesP(
            edges,
            1,
            Math.PI / 180.0,
            houghThreshold,
            minLineLength: minLineLength,
            maxLineGap: maxLineGap);

        if (lines is null || lines.Length == 0)
        {
            return new LineDetectResult(string.Empty, default, default, 0.0, false);
        }

        LineSegmentPoint best = lines[0];
        var bestLen = 0.0;
        foreach (var l in lines)
        {
            var p1 = new Point2d(l.P1.X, l.P1.Y);
            var p2 = new Point2d(l.P2.X, l.P2.Y);
            var len = Geometry2D.Distance(p1, p2);
            if (len > bestLen)
            {
                bestLen = len;
                best = l;
            }
        }

        var gp1 = new Point2d(best.P1.X + roiRect.X, best.P1.Y + roiRect.Y);
        var gp2 = new Point2d(best.P2.X + roiRect.X, best.P2.Y + roiRect.Y);
        var (cp1, cp2, clipped) = ClipInfiniteLineToRect(gp1, gp2, roiRect);
        if (clipped)
        {
            var len = Geometry2D.Distance(cp1, cp2);
            return new LineDetectResult(string.Empty, cp1, cp2, len, true);
        }

        return new LineDetectResult(string.Empty, gp1, gp2, bestLen, true);
    }

    public List<LineDetectResult> DetectTopLines(Mat image, Roi searchRoi, int canny1, int canny2, int houghThreshold, int minLineLength, int maxLineGap, int topN)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        topN = Math.Clamp(topN, 1, 20);

        var roiRect = new Rect(searchRoi.X, searchRoi.Y, searchRoi.Width, searchRoi.Height)
            .Intersect(new Rect(0, 0, image.Width, image.Height));
        if (roiRect.Width <= 0 || roiRect.Height <= 0)
        {
            return new List<LineDetectResult>();
        }

        using var roi = new Mat(image, roiRect);
        using var gray = roi.Channels() == 1 ? roi.Clone() : roi.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var edges = new Mat();
        Cv2.Canny(gray, edges, canny1, canny2);

        var lines = Cv2.HoughLinesP(
            edges,
            1,
            Math.PI / 180.0,
            houghThreshold,
            minLineLength: minLineLength,
            maxLineGap: maxLineGap);

        if (lines is null || lines.Length == 0)
        {
            return new List<LineDetectResult>();
        }

        var tmp = new List<LineDetectResult>(lines.Length);
        foreach (var l in lines)
        {
            var gp1 = new Point2d(l.P1.X + roiRect.X, l.P1.Y + roiRect.Y);
            var gp2 = new Point2d(l.P2.X + roiRect.X, l.P2.Y + roiRect.Y);
            var (cp1, cp2, clipped) = ClipInfiniteLineToRect(gp1, gp2, roiRect);
            var p1 = clipped ? cp1 : gp1;
            var p2 = clipped ? cp2 : gp2;
            var len = Geometry2D.Distance(p1, p2);
            tmp.Add(new LineDetectResult(string.Empty, p1, p2, len, true));
        }

        return tmp
            .OrderByDescending(x => x.LengthPx)
            .Take(topN)
            .ToList();
    }

    private static (Point2d P1, Point2d P2, bool Ok) ClipInfiniteLineToRect(Point2d p1, Point2d p2, Rect rect)
    {
        var xmin = rect.X;
        var xmax = rect.X + rect.Width;
        var ymin = rect.Y;
        var ymax = rect.Y + rect.Height;

        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        if (Math.Abs(dx) < 1e-12 && Math.Abs(dy) < 1e-12)
        {
            return (p1, p2, false);
        }

        var pts = new System.Collections.Generic.List<Point2d>(4);

        if (Math.Abs(dx) > 1e-12)
        {
            var t = (xmin - p1.X) / dx;
            var y = p1.Y + t * dy;
            if (y >= ymin && y <= ymax) pts.Add(new Point2d(xmin, y));

            t = (xmax - p1.X) / dx;
            y = p1.Y + t * dy;
            if (y >= ymin && y <= ymax) pts.Add(new Point2d(xmax, y));
        }

        if (Math.Abs(dy) > 1e-12)
        {
            var t = (ymin - p1.Y) / dy;
            var x = p1.X + t * dx;
            if (x >= xmin && x <= xmax) pts.Add(new Point2d(x, ymin));

            t = (ymax - p1.Y) / dy;
            x = p1.X + t * dx;
            if (x >= xmin && x <= xmax) pts.Add(new Point2d(x, ymax));
        }

        if (pts.Count < 2)
        {
            return (p1, p2, false);
        }

        var bestA = pts[0];
        var bestB = pts[1];
        var bestDist = Geometry2D.Distance(bestA, bestB);
        for (int i = 0; i < pts.Count; i++)
        {
            for (int j = i + 1; j < pts.Count; j++)
            {
                var d = Geometry2D.Distance(pts[i], pts[j]);
                if (d > bestDist)
                {
                    bestDist = d;
                    bestA = pts[i];
                    bestB = pts[j];
                }
            }
        }

        return (bestA, bestB, true);
    }
}

public interface IDefectDetector
{
    DefectDetectionResult Detect(Mat image, DefectInspectionConfig config);
}

public sealed record MatchResult(Point2d Position, double Score, double AngleDeg, Rect MatchRect, System.Collections.Generic.List<Point2d>? FeaturePoints = null);

public static class ShapeModelTrainer
{
    public static ShapeModelDefinition Train(Mat templateGray, int featureCount = 300, int binCount = 16)
    {
        if (templateGray is null) throw new ArgumentNullException(nameof(templateGray));
        if (templateGray.Empty()) return new ShapeModelDefinition();

        featureCount = Math.Clamp(featureCount, 50, 2000);
        binCount = Math.Clamp(binCount, 8, 64);

        using var gx = new Mat();
        using var gy = new Mat();
        Cv2.Sobel(templateGray, gx, MatType.CV_32F, 1, 0, ksize: 3);
        Cv2.Sobel(templateGray, gy, MatType.CV_32F, 0, 1, ksize: 3);

        var w = templateGray.Width;
        var h = templateGray.Height;
        if (w <= 0 || h <= 0) return new ShapeModelDefinition();

        var cx = w / 2;
        var cy = h / 2;

        var candidates = new List<(float Mag, int X, int Y, int Bin)>(w * h / 8);

        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                var dx = gx.At<float>(y, x);
                var dy = gy.At<float>(y, x);
                var mag = MathF.Sqrt(dx * dx + dy * dy);
                if (mag < 20.0f) continue;

                var a = MathF.Atan2(dy, dx);
                if (a < 0) a += MathF.Tau;
                var bin = (int)MathF.Floor(a * binCount / MathF.Tau);
                if (bin < 0) bin = 0;
                if (bin >= binCount) bin = binCount - 1;
                candidates.Add((mag, x, y, bin));
            }
        }

        candidates.Sort((a, b) => b.Mag.CompareTo(a.Mag));

        var model = new ShapeModelDefinition
        {
            TemplateWidth = w,
            TemplateHeight = h,
            BinCount = binCount
        };

        var minDist2 = 9;
        for (var i = 0; i < candidates.Count && model.Features.Count < featureCount; i++)
        {
            var c = candidates[i];
            var keep = true;
            for (var j = 0; j < model.Features.Count; j++)
            {
                var f = model.Features[j];
                var fx = f.Dx + cx;
                var fy = f.Dy + cy;
                var ddx = c.X - fx;
                var ddy = c.Y - fy;
                if (ddx * ddx + ddy * ddy < minDist2)
                {
                    keep = false;
                    break;
                }
            }

            if (!keep) continue;

            var dx0 = c.X - cx;
            var dy0 = c.Y - cy;
            var weight = (int)Math.Clamp(c.Mag, 1.0f, 255.0f);
            model.Features.Add(new ShapeFeatureDefinition
            {
                Dx = dx0,
                Dy = dy0,
                Bin = c.Bin,
                Weight = weight
            });
        }

        model.FeatureCount = model.Features.Count;
        return model;
    }
}

public sealed record DistanceCheckResult(string Name, string PointA, string PointB, double Value, double Nominal, double TolPlus, double TolMinus, bool Pass);

public sealed record LineDetectResult(string Name, Point2d P1, Point2d P2, double LengthPx, bool Found);

public sealed record SegmentDistanceResult(
    string Name,
    string RefA,
    string RefB,
    double Value,
    double Nominal,
    double TolPlus,
    double TolMinus,
    bool Pass,
    Point2d ClosestA,
    Point2d ClosestB);

public sealed record DefectBlob(Rect BoundingBox, double Area, string Type);

public sealed class DefectDetectionResult
{
    public List<DefectBlob> Defects { get; } = new();
}

public sealed class ImagePreprocessor
{
    private static readonly Mat MorphKernel3x3 = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));

    private static int MakeOddAtLeast3(int k)
    {
        if (k < 3) k = 3;
        if (k % 2 == 0) k += 1;
        return k;
    }

    public Mat Run(Mat inputBgrOrGray, PreprocessSettings settings)
    {
        if (inputBgrOrGray is null)
        {
            throw new ArgumentNullException(nameof(inputBgrOrGray));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (inputBgrOrGray.Empty())
        {
            return inputBgrOrGray.Clone();
        }

        var current = inputBgrOrGray;
        var anyOp = false;
        var disposeList = new List<Mat>();
        Mat? ret = null;

        try
        {
            // Illumination correction should run early (before threshold/canny) and can work on gray.
            if (settings.IlluminationCorrection != IlluminationCorrectionPreset.None)
            {
                if (current.Channels() > 1)
                {
                    var gray0 = new Mat();
                    Cv2.CvtColor(current, gray0, ColorConversionCodes.BGR2GRAY);
                    disposeList.Add(gray0);
                    current = gray0;
                    anyOp = true;
                }

                var k = MakeOddAtLeast3(settings.IlluminationKernel);

                if (settings.IlluminationCorrection == IlluminationCorrectionPreset.BackgroundSubtract)
                {
                    // Remove low-frequency background via strong blur and subtract.
                    var bg = new Mat();
                    Cv2.GaussianBlur(current, bg, new Size(k, k), 0);
                    disposeList.Add(bg);

                    var sub = new Mat();
                    Cv2.Subtract(current, bg, sub);
                    disposeList.Add(sub);

                    var norm = new Mat();
                    Cv2.Normalize(sub, norm, 0, 255, NormTypes.MinMax);
                    disposeList.Add(norm);

                    current = norm;
                    anyOp = true;
                }
                else if (settings.IlluminationCorrection == IlluminationCorrectionPreset.FlatFieldNormalize)
                {
                    // Approximate flat-field correction: divide by blurred background then normalize.
                    var bg = new Mat();
                    Cv2.GaussianBlur(current, bg, new Size(k, k), 0);
                    disposeList.Add(bg);

                    using var cur32 = new Mat();
                    using var bg32 = new Mat();
                    current.ConvertTo(cur32, MatType.CV_32F);
                    bg.ConvertTo(bg32, MatType.CV_32F);

                    // Avoid division by zero by adding epsilon.
                    var bgEps = new Mat();
                    Cv2.Add(bg32, Scalar.All(1.0), bgEps);
                    disposeList.Add(bgEps);

                    var div = new Mat();
                    Cv2.Divide(cur32, bgEps, div);
                    disposeList.Add(div);

                    var norm = new Mat();
                    Cv2.Normalize(div, norm, 0, 255, NormTypes.MinMax);
                    disposeList.Add(norm);

                    var u8 = new Mat();
                    norm.ConvertTo(u8, MatType.CV_8U);
                    disposeList.Add(u8);

                    current = u8;
                    anyOp = true;
                }
                else if (settings.IlluminationCorrection == IlluminationCorrectionPreset.Clahe)
                {
                    var clip = Math.Clamp(settings.ClaheClipLimit, 0.1, 40.0);
                    var grid = Math.Clamp(settings.ClaheTileGrid, 2, 32);
                    using var clahe = Cv2.CreateCLAHE(clipLimit: clip, tileGridSize: new Size(grid, grid));

                    var dstClahe = new Mat();
                    clahe.Apply(current, dstClahe);
                    disposeList.Add(dstClahe);

                    current = dstClahe;
                    anyOp = true;
                }
            }

            if (settings.UseGray && current.Channels() > 1)
            {
                var gray = new Mat();
                Cv2.CvtColor(current, gray, ColorConversionCodes.BGR2GRAY);
                disposeList.Add(gray);
                current = gray;
                anyOp = true;
            }

            if (settings.UseGaussianBlur)
            {
                var k = settings.BlurKernel;
                if (k < 1) k = 1;
                if (k % 2 == 0) k += 1;

                var blur = new Mat();
                Cv2.GaussianBlur(current, blur, new Size(k, k), 0);
                disposeList.Add(blur);
                current = blur;
                anyOp = true;
            }

            if (settings.UseThreshold)
            {
                if (current.Channels() > 1)
                {
                    var gray = new Mat();
                    Cv2.CvtColor(current, gray, ColorConversionCodes.BGR2GRAY);
                    disposeList.Add(gray);
                    current = gray;
                    anyOp = true;
                }

                var thr = new Mat();
                Cv2.Threshold(current, thr, settings.ThresholdValue, 255, ThresholdTypes.Binary);
                disposeList.Add(thr);
                current = thr;
                anyOp = true;
            }

            if (settings.UseCanny)
            {
                if (current.Channels() > 1)
                {
                    var gray = new Mat();
                    Cv2.CvtColor(current, gray, ColorConversionCodes.BGR2GRAY);
                    disposeList.Add(gray);
                    current = gray;
                    anyOp = true;
                }

                var edges = new Mat();
                Cv2.Canny(current, edges, settings.Canny1, settings.Canny2);
                disposeList.Add(edges);
                current = edges;
                anyOp = true;
            }

            if (settings.UseMorphology)
            {
                if (current.Channels() > 1)
                {
                    var gray = new Mat();
                    Cv2.CvtColor(current, gray, ColorConversionCodes.BGR2GRAY);
                    disposeList.Add(gray);
                    current = gray;
                    anyOp = true;
                }

                var mor = new Mat();
                Cv2.MorphologyEx(current, mor, MorphTypes.Close, MorphKernel3x3);
                disposeList.Add(mor);
                current = mor;
                anyOp = true;
            }

            if (!anyOp)
            {
                ret = inputBgrOrGray.Clone();
                return ret;
            }

            ret = current;
            return ret;
        }
        finally
        {
            if (ret is not null)
            {
                disposeList.Remove(ret);
            }
            foreach (var m in disposeList)
            {
                m.Dispose();
            }
        }
    }
}

public sealed class PatternMatcher
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

    public MatchResult Match(Mat image, PointDefinition definition, PreprocessSettings? preprocess)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var roiRect = ToRect(definition.SearchRoi, image.Width, image.Height);
        if (roiRect.Width <= 0 || roiRect.Height <= 0)
        {
            throw new ArgumentException($"Invalid SearchRoi for point '{definition.Name}'.");
        }

        if (string.IsNullOrWhiteSpace(definition.TemplateImageFile) || !File.Exists(definition.TemplateImageFile))
        {
            throw new FileNotFoundException($"Template file not found for point '{definition.Name}'.", definition.TemplateImageFile);
        }

        using var templ = Cv2.ImRead(definition.TemplateImageFile, ImreadModes.Grayscale);
        using var templGray = EnsureGrayBorrowed(templ);
        return Match(image, definition, templGray.Mat, preprocess);
    }

    public MatchResult Match(Mat image, PointDefinition definition, Mat templateGray, PreprocessSettings? preprocess)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (templateGray is null)
        {
            throw new ArgumentNullException(nameof(templateGray));
        }

        var roiRect = ToRect(definition.SearchRoi, image.Width, image.Height);
        if (roiRect.Width <= 0 || roiRect.Height <= 0)
        {
            throw new ArgumentException($"Invalid SearchRoi for point '{definition.Name}'.");
        }

        using var roi = new Mat(image, roiRect);

        using var roiGray = EnsureGrayBorrowed(roi);

        if (definition.OriginAlgorithm == OriginAlgorithm.FeatureBased)
        {
            return MatchByFeatureBased(roiGray.Mat, templateGray, definition, 0.0, preprocess, roiRect);
        }
        
        if (definition.OriginAlgorithm == OriginAlgorithm.TemplateMatch)
        {
            using var tPrep = PreprocessTemplateForMatch(templateGray, preprocess);
            var (maxV, maxL) = MatchTemplatePyramid(roiGray.Mat, tPrep, TemplateMatchModes.CCoeffNormed);
            var cInRoi = new Point2d(maxL.X + tPrep.Width / 2.0, maxL.Y + tPrep.Height / 2.0);
            var g = new Point2d(cInRoi.X + roiRect.X, cInRoi.Y + roiRect.Y);
            var mRect = new Rect(roiRect.X + maxL.X, roiRect.Y + maxL.Y, tPrep.Width, tPrep.Height);
            return new MatchResult(g, maxV, 0.0, mRect);
        }

        using var templPrep = PreprocessTemplateForMatch(templateGray, preprocess);

        if (roiGray.Mat.Width < templPrep.Width || roiGray.Mat.Height < templPrep.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        var (maxVal, maxLoc) = MatchTemplatePyramid(roiGray.Mat, templPrep, TemplateMatchModes.CCoeffNormed);

        var centerInRoi = new Point2d(maxLoc.X + templPrep.Width / 2.0, maxLoc.Y + templPrep.Height / 2.0);
        var global = new Point2d(centerInRoi.X + roiRect.X, centerInRoi.Y + roiRect.Y);
        var matchRect = new Rect(roiRect.X + maxLoc.X, roiRect.Y + maxLoc.Y, templPrep.Width, templPrep.Height);
        return new MatchResult(global, maxVal, 0.0, matchRect);
    }

    public MatchResult MatchWithFixedRotation(Mat image, PointDefinition definition, double angleDeg, PreprocessSettings? preprocess)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var roiRect = ToRect(definition.SearchRoi, image.Width, image.Height);
        if (roiRect.Width <= 0 || roiRect.Height <= 0)
        {
            throw new ArgumentException($"Invalid SearchRoi for point '{definition.Name}'.");
        }

        if (definition.ShapeModel is not null
            && definition.ShapeModel.TemplateWidth > 0
            && definition.ShapeModel.TemplateHeight > 0
            && definition.ShapeModel.Features is not null
            && definition.ShapeModel.Features.Count > 0)
        {
            using var dummyTemplate = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(0));
            return MatchWithFixedRotation(image, definition, dummyTemplate, angleDeg, preprocess);
        }

        if (string.IsNullOrWhiteSpace(definition.TemplateImageFile) || !File.Exists(definition.TemplateImageFile))
        {
            throw new FileNotFoundException($"Template file not found for point '{definition.Name}'.", definition.TemplateImageFile);
        }

        using var templ0 = Cv2.ImRead(definition.TemplateImageFile, ImreadModes.Grayscale);
        using var templGray0 = EnsureGrayBorrowed(templ0);
        return MatchWithFixedRotation(image, definition, templGray0.Mat, angleDeg, preprocess);
    }

    public MatchResult MatchWithFixedRotation(Mat image, PointDefinition definition, Mat templateGray, double angleDeg, PreprocessSettings? preprocess)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (templateGray is null)
        {
            throw new ArgumentNullException(nameof(templateGray));
        }

        var roiRect = ToRect(definition.SearchRoi, image.Width, image.Height);
        if (roiRect.Width <= 0 || roiRect.Height <= 0)
        {
            throw new ArgumentException($"Invalid SearchRoi for point '{definition.Name}'.");
        }

        using var roi = new Mat(image, roiRect);

        using var roiGray = EnsureGrayBorrowed(roi);

        if (definition.OriginAlgorithm == OriginAlgorithm.FeatureBased)
        {
            return MatchByFeatureBased(roiGray.Mat, templateGray, definition, angleDeg, preprocess, roiRect);
        }

        if (definition.OriginAlgorithm == OriginAlgorithm.TemplateMatch)
        {
            // Bypass ShapeModel
        }
        else if (definition.ShapeModel is not null
            && definition.ShapeModel.TemplateWidth > 0
            && definition.ShapeModel.TemplateHeight > 0
            && definition.ShapeModel.Features is not null
            && definition.ShapeModel.Features.Count > 0)
        {
            var m = MatchByShapeModel(roiGray.Mat, definition.ShapeModel, angleDeg);
            var globalPos = new Point2d(m.Position.X + roiRect.X, m.Position.Y + roiRect.Y);
            var mr = new Rect(
                roiRect.X + m.MatchRect.X,
                roiRect.Y + m.MatchRect.Y,
                m.MatchRect.Width,
                m.MatchRect.Height);
            return new MatchResult(globalPos, m.Score, angleDeg, mr);
        }

        using var templPrep = PreprocessTemplateForMatch(templateGray, preprocess);

        if (Math.Abs(angleDeg) < 0.1)
        {
            if (roiGray.Mat.Width < templPrep.Width || roiGray.Mat.Height < templPrep.Height)
            {
                var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
                return new MatchResult(centerFallback, 0.0, angleDeg, roiRect);
            }
            var (maxV, maxL) = MatchTemplatePyramid(roiGray.Mat, templPrep, TemplateMatchModes.CCoeffNormed);
            var centerInRoi = new Point2d(maxL.X + templPrep.Width / 2.0, maxL.Y + templPrep.Height / 2.0);
            var global = new Point2d(centerInRoi.X + roiRect.X, centerInRoi.Y + roiRect.Y);
            var matchRect = new Rect(roiRect.X + maxL.X, roiRect.Y + maxL.Y, templPrep.Width, templPrep.Height);
            return new MatchResult(global, maxV, angleDeg, matchRect);
        }

        // To avoid clipping during rotation, we extract a larger ROI from the original image.
        int diag = (int)Math.Ceiling(Math.Sqrt(roiRect.Width * roiRect.Width + roiRect.Height * roiRect.Height));
        int padX = (diag - roiRect.Width) / 2;
        int padY = (diag - roiRect.Height) / 2;

        var paddedRect = new Rect(roiRect.X - padX, roiRect.Y - padY, roiRect.Width + 2 * padX, roiRect.Height + 2 * padY);
        
        var imgRect = new Rect(0, 0, image.Width, image.Height);
        var safePaddedRect = paddedRect.Intersect(imgRect);
        
        using var paddedRoi = new Mat(image, safePaddedRect);
        using var paddedRoiGray = EnsureGrayBorrowed(paddedRoi);

        var centerInSafePadded = new Point2f(
            (float)(roiRect.X + roiRect.Width / 2.0 - safePaddedRect.X),
            (float)(roiRect.Y + roiRect.Height / 2.0 - safePaddedRect.Y)
        );

        using var M = Cv2.GetRotationMatrix2D(centerInSafePadded, angleDeg, 1.0);
        
        using var unrotatedPadded = new Mat();
        Cv2.WarpAffine(paddedRoiGray.Mat, unrotatedPadded, M, paddedRoiGray.Mat.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);

        var cropRectInPadded = new Rect(
            (int)(centerInSafePadded.X - roiRect.Width / 2.0),
            (int)(centerInSafePadded.Y - roiRect.Height / 2.0),
            roiRect.Width,
            roiRect.Height
        );

        var safeCropRect = cropRectInPadded.Intersect(new Rect(0, 0, unrotatedPadded.Width, unrotatedPadded.Height));

        using var unrotatedRoi = new Mat(unrotatedPadded, safeCropRect);

        if (unrotatedRoi.Width < templPrep.Width || unrotatedRoi.Height < templPrep.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, angleDeg, roiRect);
        }

        var (maxVal, maxLoc) = MatchTemplatePyramid(unrotatedRoi, templPrep, TemplateMatchModes.CCoeffNormed);
        
        var centerInCrop = new Point2d(maxLoc.X + templPrep.Width / 2.0, maxLoc.Y + templPrep.Height / 2.0);
        var unrotatedCenter = new Point2d(centerInCrop.X + safeCropRect.X, centerInCrop.Y + safeCropRect.Y);
        
        var rad = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        
        var dx = unrotatedCenter.X - centerInSafePadded.X;
        var dy = unrotatedCenter.Y - centerInSafePadded.Y;
        
        var rotatedCenterInPadded = new Point2d(
            centerInSafePadded.X + dx * cos - dy * sin,
            centerInSafePadded.Y + dx * sin + dy * cos
        );

        var globalCenter = new Point2d(rotatedCenterInPadded.X + safePaddedRect.X, rotatedCenterInPadded.Y + safePaddedRect.Y);
        var globalMatchRect = new Rect(
            (int)(globalCenter.X - templPrep.Width / 2.0),
            (int)(globalCenter.Y - templPrep.Height / 2.0),
            templPrep.Width,
            templPrep.Height
        );

        return new MatchResult(globalCenter, maxVal, angleDeg, globalMatchRect);
    }

    
    private MatchResult MatchByFeatureBased(Mat roiGray, Mat templateGray, PointDefinition definition, double angleDeg, PreprocessSettings? preprocess, Rect roiRect)
    {
        using var templPrep = PreprocessTemplateForMatch(templateGray, preprocess);
        
        using var detector = OpenCvSharp.Features2D.SIFT.Create();
        using var des1 = new Mat();
        using var des2 = new Mat();
        
        detector.DetectAndCompute(templPrep, null, out KeyPoint[] keypoints1, des1);
        detector.DetectAndCompute(roiGray, null, out KeyPoint[] keypoints2, des2);
        
        if (des1.Empty() || des2.Empty() || des1.Rows < 4 || des2.Rows < 4)
        {
            return FallbackToTemplateMatch(roiGray, templateGray, definition, angleDeg, preprocess, roiRect);
        }
        
        using var bf = new BFMatcher(NormTypes.L2, crossCheck: true);
        var matches = bf.Match(des1, des2);
        
        var goodMatches = matches.Where(m => m.Distance < 300).OrderBy(m => m.Distance).Take(50).ToArray();
        
        if (goodMatches.Length < 4)
        {
            return FallbackToTemplateMatch(roiGray, templateGray, definition, angleDeg, preprocess, roiRect);
        }
        
        var pts1 = goodMatches.Select(m => new Point2d(keypoints1[m.QueryIdx].Pt.X, keypoints1[m.QueryIdx].Pt.Y)).ToArray();
        var pts2 = goodMatches.Select(m => new Point2d(keypoints2[m.TrainIdx].Pt.X, keypoints2[m.TrainIdx].Pt.Y)).ToArray();
        
        using var inliers = new Mat();
        using var H = Cv2.FindHomography(InputArray.Create(pts1), InputArray.Create(pts2), HomographyMethods.LMedS, 3.0, inliers);
        
        if (H.Empty())
        {
            return FallbackToTemplateMatch(roiGray, templateGray, definition, angleDeg, preprocess, roiRect);
        }
        
        var h11 = H.At<double>(0, 0);
        var h21 = H.At<double>(1, 0);
        var actualAngleDeg = Math.Atan2(h21, h11) * 180.0 / Math.PI;

        var pad = 4;
        using var T_inv = Mat.Eye(3, 3, MatType.CV_64FC1).ToMat();
        T_inv.Set<double>(0, 2, -pad);
        T_inv.Set<double>(1, 2, -pad);
        
        using var H_warped = new Mat();
        Cv2.Gemm(H, T_inv, 1.0, new Mat(), 0.0, H_warped);

        using var warped = new Mat();
        Cv2.WarpPerspective(roiGray, warped, H_warped, new Size(templPrep.Width + 2 * pad, templPrep.Height + 2 * pad), InterpolationFlags.Linear | InterpolationFlags.WarpInverseMap);
        
        var maxVal = 0.0;
        using var res = new Mat();
        Cv2.MatchTemplate(warped, templPrep, res, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(res, out _, out maxVal, out _, out var maxLoc);

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
        
        var featurePoints = new System.Collections.Generic.List<Point2d>();
        for (int i = 0; i < pts2.Length; i++)
        {
            if (inliers.At<byte>(i, 0) != 0)
            {
                featurePoints.Add(new Point2d(pts2[i].X + roiRect.X, pts2[i].Y + roiRect.Y));
            }
        }

        return new MatchResult(global, maxVal, actualAngleDeg, matchRect, featurePoints);
    }

    private MatchResult FallbackToTemplateMatch(Mat roiGray, Mat templateGray, PointDefinition definition, double angleDeg, PreprocessSettings? preprocess, Rect roiRect)
    {
        using var tPrep = PreprocessTemplateForMatch(templateGray, preprocess);
        using var templGrayRot = RotateWithPadding(tPrep, angleDeg);
        var crop = ContentRectFromNonZero(templGrayRot, pad: 0);
        if (crop.Width <= 0 || crop.Height <= 0) {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, angleDeg, roiRect);
        }
        using var templCrop = new Mat(templGrayRot, crop);
        var cw = Math.Min(templCrop.Width, roiGray.Width);
        var ch = Math.Min(templCrop.Height, roiGray.Height);
        var cx = (templCrop.Width - cw) / 2;
        var cy = (templCrop.Height - ch) / 2;
        using var t2 = new Mat(templCrop, new Rect(cx, cy, cw, ch));
        var (maxV, maxL) = MatchTemplatePyramid(roiGray, t2, TemplateMatchModes.CCoeffNormed);
        var cInRoi = new Point2d(maxL.X + t2.Width / 2.0, maxL.Y + t2.Height / 2.0);
        var g = new Point2d(cInRoi.X + roiRect.X, cInRoi.Y + roiRect.Y);
        var mRect = new Rect(roiRect.X + maxL.X, roiRect.Y + maxL.Y, t2.Width, t2.Height);
        return new MatchResult(g, maxV, angleDeg, mRect);
    }

    private static MatchResult MatchByShapeModel(Mat roiGray, ShapeModelDefinition model, double angleDeg)
    {
        if (roiGray is null) throw new ArgumentNullException(nameof(roiGray));
        if (model is null) throw new ArgumentNullException(nameof(model));

        var tplW = model.TemplateWidth;
        var tplH = model.TemplateHeight;
        if (tplW <= 0 || tplH <= 0) return new MatchResult(new Point2d(roiGray.Width / 2.0, roiGray.Height / 2.0), 0.0, angleDeg, new Rect(0, 0, 0, 0));

        var maxX = roiGray.Width - tplW;
        var maxY = roiGray.Height - tplH;
        if (maxX < 0 || maxY < 0) return new MatchResult(new Point2d(roiGray.Width / 2.0, roiGray.Height / 2.0), 0.0, angleDeg, new Rect(0, 0, 0, 0));

        var binCount = Math.Clamp(model.BinCount, 8, 64);
        var binShift = (int)Math.Round(angleDeg / 360.0 * binCount);
        binShift %= binCount;
        if (binShift < 0) binShift += binCount;

        var a = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(a);
        var sin = Math.Sin(a);

        var rotated = new List<(int Dx, int Dy, int Bin, int Weight)>(model.Features.Count);
        var totalWeight = 0;
        foreach (var f in model.Features)
        {
            var rdx = (int)Math.Round(f.Dx * cos - f.Dy * sin);
            var rdy = (int)Math.Round(f.Dx * sin + f.Dy * cos);
            var b = (f.Bin + binShift) % binCount;
            var w = Math.Max(1, f.Weight);
            rotated.Add((rdx, rdy, b, w));
            totalWeight += w;
        }

        if (totalWeight <= 0) totalWeight = 1;

        using var gx = new Mat();
        using var gy = new Mat();
        Cv2.Sobel(roiGray, gx, MatType.CV_32F, 1, 0, ksize: 3);
        Cv2.Sobel(roiGray, gy, MatType.CV_32F, 0, 1, ksize: 3);

        var edgeByBin = new List<Point>[binCount];
        for (var i = 0; i < binCount; i++) edgeByBin[i] = new List<Point>(1024);

        for (var y = 1; y < roiGray.Height - 1; y++)
        {
            for (var x = 1; x < roiGray.Width - 1; x++)
            {
                var dx = gx.At<float>(y, x);
                var dy = gy.At<float>(y, x);
                var mag = MathF.Sqrt(dx * dx + dy * dy);
                if (mag < 20.0f) continue;

                var ang = MathF.Atan2(dy, dx);
                if (ang < 0) ang += MathF.Tau;
                var bin = (int)MathF.Floor(ang * binCount / MathF.Tau);
                if (bin < 0) bin = 0;
                if (bin >= binCount) bin = binCount - 1;
                edgeByBin[bin].Add(new Point(x, y));
            }
        }

        var accW = maxX + 1;
        var accH = maxY + 1;
        var acc = new int[accW * accH];

        var cx = tplW / 2;
        var cy = tplH / 2;

        for (var i = 0; i < rotated.Count; i++)
        {
            var rf = rotated[i];
            var tx = cx + rf.Dx;
            var ty = cy + rf.Dy;

            if (tx < 0 || ty < 0 || tx >= tplW || ty >= tplH) continue;

            var pts = edgeByBin[rf.Bin];
            for (var p = 0; p < pts.Count; p++)
            {
                var ip = pts[p];
                var ox = ip.X - tx;
                var oy = ip.Y - ty;
                if ((uint)ox > (uint)maxX || (uint)oy > (uint)maxY) continue;
                acc[oy * accW + ox] += rf.Weight;
            }
        }

        var best = -1;
        var bestIdx = 0;
        for (var i = 0; i < acc.Length; i++)
        {
            var v = acc[i];
            if (v > best)
            {
                best = v;
                bestIdx = i;
            }
        }

        var bestX = bestIdx % accW;
        var bestY = bestIdx / accW;
        var score = (double)best / totalWeight;
        var center = new Point2d(bestX + tplW / 2.0, bestY + tplH / 2.0);
        var rect = new Rect(bestX, bestY, tplW, tplH);

        var featurePoints = new System.Collections.Generic.List<Point2d>(rotated.Count);
        foreach (var rf in rotated)
        {
            featurePoints.Add(new Point2d(center.X + rf.Dx, center.Y + rf.Dy));
        }

        return new MatchResult(center, score, angleDeg, rect, featurePoints);
    }

    private static List<Point>[] BuildEdgeByBinFromSobel(Mat roiGray, int binCount, float magThreshold)
    {
        if (roiGray is null) throw new ArgumentNullException(nameof(roiGray));
        if (binCount < 1) throw new ArgumentOutOfRangeException(nameof(binCount));

        using var gx = new Mat();
        using var gy = new Mat();
        Cv2.Sobel(roiGray, gx, MatType.CV_32F, 1, 0, ksize: 3);
        Cv2.Sobel(roiGray, gy, MatType.CV_32F, 0, 1, ksize: 3);

        var edgeByBin = new List<Point>[binCount];
        for (var i = 0; i < binCount; i++) edgeByBin[i] = new List<Point>(1024);

        for (var y = 1; y < roiGray.Height - 1; y++)
        {
            for (var x = 1; x < roiGray.Width - 1; x++)
            {
                var dx = gx.At<float>(y, x);
                var dy = gy.At<float>(y, x);
                var mag = MathF.Sqrt(dx * dx + dy * dy);
                if (mag < magThreshold) continue;

                var ang = MathF.Atan2(dy, dx);
                if (ang < 0) ang += MathF.Tau;
                var bin = (int)MathF.Floor(ang * binCount / MathF.Tau);
                if (bin < 0) bin = 0;
                if (bin >= binCount) bin = binCount - 1;
                edgeByBin[bin].Add(new Point(x, y));
            }
        }

        return edgeByBin;
    }

    private static (double Score, Point2d Center, Rect MatchRect) ScoreByShapeModel(
        List<Point>[] edgeByBin,
        int roiWidth,
        int roiHeight,
        ShapeModelDefinition model,
        double angleDeg,
        int[] accScratch)
    {
        var tplW = model.TemplateWidth;
        var tplH = model.TemplateHeight;
        if (tplW <= 0 || tplH <= 0) return (0.0, new Point2d(roiWidth / 2.0, roiHeight / 2.0), new Rect(0, 0, 0, 0));

        var maxX = roiWidth - tplW;
        var maxY = roiHeight - tplH;
        if (maxX < 0 || maxY < 0) return (0.0, new Point2d(roiWidth / 2.0, roiHeight / 2.0), new Rect(0, 0, 0, 0));

        var binCount = Math.Clamp(model.BinCount, 8, 64);
        var binShift = (int)Math.Round(angleDeg / 360.0 * binCount);
        binShift %= binCount;
        if (binShift < 0) binShift += binCount;

        var a = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(a);
        var sin = Math.Sin(a);

        var accW = maxX + 1;
        var accH = maxY + 1;
        var accLen = accW * accH;
        if (accScratch.Length < accLen)
        {
            accScratch = new int[accLen];
        }
        else
        {
            Array.Clear(accScratch, 0, accLen);
        }

        var cx = tplW / 2;
        var cy = tplH / 2;

        var totalWeight = 0;
        foreach (var f in model.Features)
        {
            var w = Math.Max(1, f.Weight);
            totalWeight += w;
        }
        if (totalWeight <= 0) totalWeight = 1;

        // Group model features by their target bin after rotation
        // This makes the inner loop much faster
        var featuresByBin = new List<(int tx, int ty, int w)>[binCount];
        for (int i = 0; i < binCount; i++) featuresByBin[i] = new List<(int tx, int ty, int w)>();

        foreach (var f in model.Features)
        {
            var rdx = (int)Math.Round(f.Dx * cos - f.Dy * sin);
            var rdy = (int)Math.Round(f.Dx * sin + f.Dy * cos);
            var b = (f.Bin + binShift) % binCount;
            var w = Math.Max(1, f.Weight);

            var tx = cx + rdx;
            var ty = cy + rdy;
            if (tx < 0 || ty < 0 || tx >= tplW || ty >= tplH) continue;
            
            featuresByBin[b].Add((tx, ty, w));
        }

        // Optimized voting loop
        for (var b = 0; b < binCount; b++)
        {
            var feats = featuresByBin[b];
            if (feats.Count == 0) continue;
            
            var pts = edgeByBin[b];
            if (pts.Count == 0) continue;

            for (var p = 0; p < pts.Count; p++)
            {
                var ip = pts[p];
                for (var fi = 0; fi < feats.Count; fi++)
                {
                    var f = feats[fi];
                    var ox = ip.X - f.tx;
                    var oy = ip.Y - f.ty;
                    if ((uint)ox > (uint)maxX || (uint)oy > (uint)maxY) continue;
                    accScratch[oy * accW + ox] += f.w;
                }
            }
        }

        var best = -1;
        var bestIdx = 0;
        for (var i = 0; i < accLen; i++)
        {
            var v = accScratch[i];
            if (v > best)
            {
                best = v;
                bestIdx = i;
            }
        }

        var bestX = bestIdx % accW;
        var bestY = bestIdx / accW;
        
        // Sum a 3x3 neighborhood around the peak to account for rotation discretization
        var localSum = 0;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = bestX + dx;
                var ny = bestY + dy;
                if ((uint)nx < (uint)accW && (uint)ny < (uint)accH)
                {
                    localSum += accScratch[ny * accW + nx];
                }
            }
        }

        var score = Math.Min(1.0, (double)localSum / totalWeight);
        var center = new Point2d(bestX + tplW / 2.0, bestY + tplH / 2.0);
        var rect = new Rect(bestX, bestY, tplW, tplH);
        return (score, center, rect);
    }

    public MatchResult MatchWithRotation(Mat image, PointDefinition definition, PreprocessSettings? preprocess, double minAngleDeg = -10.0, double maxAngleDeg = 10.0, double stepDeg = 2.0)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var roiRect = ToRect(definition.SearchRoi, image.Width, image.Height);
        if (roiRect.Width <= 0 || roiRect.Height <= 0)
        {
            throw new ArgumentException($"Invalid SearchRoi for point '{definition.Name}'.");
        }

        if (string.IsNullOrWhiteSpace(definition.TemplateImageFile) || !File.Exists(definition.TemplateImageFile))
        {
            throw new FileNotFoundException($"Template file not found for point '{definition.Name}'.", definition.TemplateImageFile);
        }

        using var templ0 = Cv2.ImRead(definition.TemplateImageFile, ImreadModes.Grayscale);
        using var templGray0 = EnsureGrayBorrowed(templ0);
        return MatchWithRotation(image, definition, templGray0.Mat, preprocess, minAngleDeg, maxAngleDeg, stepDeg);
    }

    public MatchResult MatchWithRotation(Mat image, PointDefinition definition, Mat templateGray, PreprocessSettings? preprocess, double minAngleDeg = -10.0, double maxAngleDeg = 10.0, double stepDeg = 2.0)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (templateGray is null)
        {
            throw new ArgumentNullException(nameof(templateGray));
        }

        var roiRect = ToRect(definition.SearchRoi, image.Width, image.Height);
        if (roiRect.Width <= 0 || roiRect.Height <= 0 || templateGray.Empty())
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        using var roi = new Mat(image, roiRect);

        using var roiGray = EnsureGrayBorrowed(roi);

        if (definition.OriginAlgorithm == OriginAlgorithm.FeatureBased)
        {
            // For FeatureBased, angle is resolved by Homography. We pass 0.0 as angleDeg and let it find the real angle.
            return MatchByFeatureBased(roiGray.Mat, templateGray, definition, 0.0, preprocess, roiRect);
        }

        return MatchByPyramidFast(roiGray.Mat, templateGray, definition, preprocess, minAngleDeg, maxAngleDeg, stepDeg, roiRect);
    }

    private MatchResult MatchByPyramidFast(Mat roiGray, Mat templateGray, PointDefinition def, PreprocessSettings? preprocess, double minAngleDeg, double maxAngleDeg, double stepDeg, Rect roiRect)
    {
        if (roiGray.Empty() || templateGray.Empty())
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        using var templPrep = PreprocessTemplateForMatch(templateGray, preprocess);

        // Build feature images for matching:
        // For ShapePyramid / ShapeBased: Use Gaussian-blurred Sobel Magnitude (edge shape invariant, lighting invariant)
        // For TemplateMatch / TemplateMatchPyramid: Use Preprocessed Grayscale directly
        using var roiFeatureMat = new Mat();
        using var templFeatureMat = new Mat();

        if (def.OriginAlgorithm == OriginAlgorithm.ShapePyramid || def.OriginAlgorithm == OriginAlgorithm.ShapeBased)
        {
            using var gxR = new Mat(); using var gyR = new Mat();
            Cv2.Sobel(roiGray, gxR, MatType.CV_32F, 1, 0, 3);
            Cv2.Sobel(roiGray, gyR, MatType.CV_32F, 0, 1, 3);
            using var magR = new Mat();
            Cv2.Magnitude(gxR, gyR, magR);
            using var mag8R = new Mat();
            magR.ConvertTo(mag8R, MatType.CV_8U);
            Cv2.GaussianBlur(mag8R, roiFeatureMat, new Size(5, 5), 0);

            using var gxT = new Mat(); using var gyT = new Mat();
            Cv2.Sobel(templPrep, gxT, MatType.CV_32F, 1, 0, 3);
            Cv2.Sobel(templPrep, gyT, MatType.CV_32F, 0, 1, 3);
            using var magT = new Mat();
            Cv2.Magnitude(gxT, gyT, magT);
            using var mag8T = new Mat();
            magT.ConvertTo(mag8T, MatType.CV_8U);
            Cv2.GaussianBlur(mag8T, templFeatureMat, new Size(5, 5), 0);
        }
        else
        {
            roiGray.CopyTo(roiFeatureMat);
            templPrep.CopyTo(templFeatureMat);
        }

        // Determine coarse pyramid scale factor
        var scale = 0.25;
        if (templFeatureMat.Width < 60 || templFeatureMat.Height < 60) scale = 0.5;
        if (templFeatureMat.Width < 30 || templFeatureMat.Height < 30) scale = 1.0;

        var roiSmallSize = new Size(Math.Max(1, (int)Math.Round(roiFeatureMat.Width * scale)), Math.Max(1, (int)Math.Round(roiFeatureMat.Height * scale)));
        using var roiSmall = new Mat();
        Cv2.Resize(roiFeatureMat, roiSmall, roiSmallSize, 0, 0, InterpolationFlags.Area);

        var templSmallSize = new Size(Math.Max(1, (int)Math.Round(templFeatureMat.Width * scale)), Math.Max(1, (int)Math.Round(templFeatureMat.Height * scale)));
        using var templSmall = new Mat();
        Cv2.Resize(templFeatureMat, templSmall, templSmallSize, 0, 0, InterpolationFlags.Area);

        if (stepDeg <= 0.000001) stepDeg = 1.0;

        // Stage 1: Coarse angle sweep on downscaled pyramid (Fast: ~10ms)
        var bestCoarseScore = double.NegativeInfinity;
        var bestCoarseAngle = 0.0;
        Point bestCoarseLocSmall = new Point(0, 0);
        Rect bestCoarseCropSmall = new Rect();

        var angle = minAngleDeg;
        while (angle <= maxAngleDeg + 0.000001)
        {
            using var templSmallRot = RotateWithPadding(templSmall, angle);
            var cropSmall = ContentRectFromNonZero(templSmallRot, pad: 0);
            if (cropSmall.Width <= 0 || cropSmall.Height <= 0 || roiSmall.Width < cropSmall.Width || roiSmall.Height < cropSmall.Height)
            {
                angle += stepDeg;
                continue;
            }

            using var templSmallCrop = new Mat(templSmallRot, cropSmall);
            using var resMatSmall = new Mat();
            Cv2.MatchTemplate(roiSmall, templSmallCrop, resMatSmall, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(resMatSmall, out _, out var maxVal, out _, out var maxLoc);

            if (maxVal > bestCoarseScore)
            {
                bestCoarseScore = maxVal;
                bestCoarseAngle = angle;
                bestCoarseLocSmall = maxLoc;
                bestCoarseCropSmall = cropSmall;
            }

            angle += stepDeg;
        }

        if (double.IsNegativeInfinity(bestCoarseScore))
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        // Stage 2: Fine angle sweep on full resolution image, but restricted to a tiny ROI around coarse location!
        var coarseLocFullX = (int)Math.Round(bestCoarseLocSmall.X / scale);
        var coarseLocFullY = (int)Math.Round(bestCoarseLocSmall.Y / scale);

        var margin = (int)Math.Round(40 / scale);
        var fineX = Math.Clamp(coarseLocFullX - margin, 0, Math.Max(0, roiFeatureMat.Width - 1));
        var fineY = Math.Clamp(coarseLocFullY - margin, 0, Math.Max(0, roiFeatureMat.Height - 1));
        var fineW = Math.Min(roiFeatureMat.Width - fineX, (int)Math.Round(bestCoarseCropSmall.Width / scale) + 2 * margin);
        var fineH = Math.Min(roiFeatureMat.Height - fineY, (int)Math.Round(bestCoarseCropSmall.Height / scale) + 2 * margin);

        if (fineW <= 0 || fineH <= 0)
        {
            fineX = 0; fineY = 0; fineW = roiFeatureMat.Width; fineH = roiFeatureMat.Height;
        }

        using var roiFine = new Mat(roiFeatureMat, new Rect(fineX, fineY, fineW, fineH));

        var refineMin = Math.Max(minAngleDeg, bestCoarseAngle - Math.Max(stepDeg, 2.0));
        var refineMax = Math.Min(maxAngleDeg, bestCoarseAngle + Math.Max(stepDeg, 2.0));
        var refineStep = Math.Min(stepDeg, 0.5);

        var bestFineScore = double.NegativeInfinity;
        var bestFineAngle = bestCoarseAngle;
        Point bestFineLocInFine = new Point(0, 0);
        Rect bestFineCrop = new Rect();

        angle = refineMin;
        while (angle <= refineMax + 0.000001)
        {
            using var templRotFull = RotateWithPadding(templFeatureMat, angle);
            var cropFull = ContentRectFromNonZero(templRotFull, pad: 0);
            if (cropFull.Width <= 0 || cropFull.Height <= 0 || roiFine.Width < cropFull.Width || roiFine.Height < cropFull.Height)
            {
                angle += refineStep;
                continue;
            }

            using var templCropFull = new Mat(templRotFull, cropFull);
            using var resMatFine = new Mat();
            Cv2.MatchTemplate(roiFine, templCropFull, resMatFine, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(resMatFine, out _, out var maxVal, out _, out var maxLoc);

            if (maxVal > bestFineScore)
            {
                bestFineScore = maxVal;
                bestFineAngle = angle;
                bestFineLocInFine = maxLoc;
                bestFineCrop = cropFull;
            }

            angle += refineStep;
        }

        if (double.IsNegativeInfinity(bestFineScore))
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        // Sub-pixel 2D Parabolic Peak refinement
        Point2d subPixelLocInFine = new Point2d(bestFineLocInFine.X, bestFineLocInFine.Y);
        using var bestTemplRotFinal = RotateWithPadding(templFeatureMat, bestFineAngle);
        using var bestTemplCropFinal = new Mat(bestTemplRotFinal, bestFineCrop);
        using var resFinal = new Mat();
        Cv2.MatchTemplate(roiFine, bestTemplCropFinal, resFinal, TemplateMatchModes.CCoeffNormed);

        int fx = bestFineLocInFine.X;
        int fy = bestFineLocInFine.Y;
        if (fx > 0 && fx < resFinal.Width - 1 && fy > 0 && fy < resFinal.Height - 1)
        {
            float l = resFinal.At<float>(fy, fx - 1);
            float r = resFinal.At<float>(fy, fx + 1);
            float u = resFinal.At<float>(fy - 1, fx);
            float d = resFinal.At<float>(fy + 1, fx);
            float c = resFinal.At<float>(fy, fx);

            double denomX = l + r - 2.0 * c;
            double denomY = u + d - 2.0 * c;
            if (Math.Abs(denomX) > 1e-6)
            {
                double dx = (l - r) / (2.0 * denomX);
                if (Math.Abs(dx) < 1.0) subPixelLocInFine.X += dx;
            }
            if (Math.Abs(denomY) > 1e-6)
            {
                double dy = (u - d) / (2.0 * denomY);
                if (Math.Abs(dy) < 1.0) subPixelLocInFine.Y += dy;
            }
        }

        var w = templateGray.Width;
        var h = templateGray.Height;
        var diag = (int)Math.Ceiling(Math.Sqrt(w * w + h * h));
        diag = Math.Max(diag, Math.Max(w, h));
        var px = (diag - w) / 2;
        var py = (diag - h) / 2;
        double cxInCrop = (px + w / 2.0) - bestFineCrop.X;
        double cyInCrop = (py + h / 2.0) - bestFineCrop.Y;

        var matchLocInRoi = new Point2d(fineX + subPixelLocInFine.X, fineY + subPixelLocInFine.Y);
        var centerInRoi = new Point2d(matchLocInRoi.X + cxInCrop, matchLocInRoi.Y + cyInCrop);
        var globalPos = new Point2d(centerInRoi.X + roiRect.X, centerInRoi.Y + roiRect.Y);
        var matchRect = new Rect(roiRect.X + (int)Math.Round(matchLocInRoi.X), roiRect.Y + (int)Math.Round(matchLocInRoi.Y), bestFineCrop.Width, bestFineCrop.Height);

        return new MatchResult(globalPos, Math.Clamp(bestFineScore, 0.0, 1.0), bestFineAngle, matchRect);
    }

    private MatchResult MatchByTemplateSweep(Mat roiGray, Mat templateGray, PreprocessSettings? preprocess, double minAngleDeg, double maxAngleDeg, double stepDeg, Rect roiRect)
    {
        using var templPrep0 = PreprocessTemplateForMatch(templateGray, preprocess);

        using var roiEdges = new Mat();
        using var templEdges0 = new Mat();
        Cv2.Canny(roiGray, roiEdges, 50, 150);
        Cv2.Canny(templPrep0, templEdges0, 50, 150);

        if (roiGray.Width < templPrep0.Width || roiGray.Height < templPrep0.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        var bestAngleScore = double.NegativeInfinity;
        var bestAngle = 0.0;
        var bestCrop = new Rect(0, 0, templEdges0.Width, templEdges0.Height);

        var angle = minAngleDeg;
        if (stepDeg <= 0.000001)
        {
            stepDeg = 1.0;
        }

        while (angle <= maxAngleDeg + 0.000001)
        {
            using var templEdgesRot = RotateWithPadding(templEdges0, angle);
            var crop = ContentRectFromNonZero(templEdgesRot, pad: 2);
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                angle += stepDeg;
                continue;
            }

            using var templEdges = new Mat(templEdgesRot, crop);

            if (roiEdges.Width < templEdges.Width || roiEdges.Height < templEdges.Height)
            {
                angle += stepDeg;
                continue;
            }

            var (maxVal, _) = MatchTemplatePyramid(roiEdges, templEdges, TemplateMatchModes.CCoeffNormed);

            if (maxVal > bestAngleScore)
            {
                bestAngleScore = maxVal;
                bestAngle = angle;
                bestCrop = crop;
            }

            angle += stepDeg;
        }

        if (double.IsNegativeInfinity(bestAngleScore))
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
        }

        using var bestTemplGrayRot = RotateWithPadding(templPrep0, bestAngle);
        if (bestCrop.Width <= 0 || bestCrop.Height <= 0
            || bestCrop.X < 0 || bestCrop.Y < 0
            || bestCrop.X + bestCrop.Width > bestTemplGrayRot.Width
            || bestCrop.Y + bestCrop.Height > bestTemplGrayRot.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, bestAngle, roiRect);
        }

        using var bestTemplGray = new Mat(bestTemplGrayRot, bestCrop);
        if (roiGray.Width < bestTemplGray.Width || roiGray.Height < bestTemplGray.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, bestAngle, roiRect);
        }

        var (maxValGray, maxLocGray) = MatchTemplatePyramid(roiGray, bestTemplGray, TemplateMatchModes.CCoeffNormed);

        var w = templateGray.Width;
        var h = templateGray.Height;
        var diag = (int)Math.Ceiling(Math.Sqrt(w * w + h * h));
        diag = Math.Max(diag, Math.Max(w, h));
        var px = (diag - w) / 2;
        var py = (diag - h) / 2;
        double cxInCrop = (px + w / 2.0) - bestCrop.X;
        double cyInCrop = (py + h / 2.0) - bestCrop.Y;
        var centerInRoi = new Point2d(maxLocGray.X + cxInCrop, maxLocGray.Y + cyInCrop);
        
        var global = new Point2d(centerInRoi.X + roiRect.X, centerInRoi.Y + roiRect.Y);
        var matchRect = new Rect(roiRect.X + maxLocGray.X, roiRect.Y + maxLocGray.Y, bestTemplGray.Width, bestTemplGray.Height);
        return new MatchResult(global, maxValGray, bestAngle, matchRect);
    }


    private static (double MaxVal, Point MaxLoc) MatchTemplatePyramid(Mat imageGray, Mat templGray, TemplateMatchModes mode)
    {
        if (imageGray is null) throw new ArgumentNullException(nameof(imageGray));
        if (templGray is null) throw new ArgumentNullException(nameof(templGray));
        if (imageGray.Empty() || templGray.Empty()) return (0.0, new Point(0, 0));

        // Heuristic pyramid settings:
        // - 2 levels typically give large speedups while preserving accuracy.
        // - Refine windows are small to reduce total scanned pixels.
        const int levels = 2;
        const int refineRadius = 32;

        var pred = new Point(0, 0);

        for (var level = levels; level >= 0; level--)
        {
            var scale = 1.0 / (1 << level);

            using var imgL = new Mat();
            using var tplL = new Mat();
            var imgSize = new Size(Math.Max(1, (int)Math.Round(imageGray.Width * scale)), Math.Max(1, (int)Math.Round(imageGray.Height * scale)));
            var tplSize = new Size(Math.Max(1, (int)Math.Round(templGray.Width * scale)), Math.Max(1, (int)Math.Round(templGray.Height * scale)));
            Cv2.Resize(imageGray, imgL, imgSize, 0, 0, InterpolationFlags.Area);
            Cv2.Resize(templGray, tplL, tplSize, 0, 0, InterpolationFlags.Area);

            Mat actualTpl = tplL;
            Mat? tempTpl = null;

            if (imgL.Width < actualTpl.Width || imgL.Height < actualTpl.Height)
            {
                var cw = Math.Min(actualTpl.Width, imgL.Width);
                var ch = Math.Min(actualTpl.Height, imgL.Height);
                var cx = (actualTpl.Width - cw) / 2;
                var cy = (actualTpl.Height - ch) / 2;
                tempTpl = new Mat(actualTpl, new Rect(cx, cy, cw, ch));
                actualTpl = tempTpl;
            }

            Rect search;
            if (level == levels)
            {
                search = new Rect(0, 0, imgL.Width, imgL.Height);
            }
            else
            {
                var px = pred.X * 2;
                var py = pred.Y * 2;
                var r = refineRadius;

                var sx = Math.Clamp(px - r, 0, Math.Max(0, imgL.Width - 1));
                var sy = Math.Clamp(py - r, 0, Math.Max(0, imgL.Height - 1));

                // Ensure search window large enough to fit template.
                var sw = Math.Min(imgL.Width - sx, actualTpl.Width + 2 * r);
                var sh = Math.Min(imgL.Height - sy, actualTpl.Height + 2 * r);

                if (sw < actualTpl.Width) sw = actualTpl.Width;
                if (sh < actualTpl.Height) sh = actualTpl.Height;

                search = new Rect(sx, sy, sw, sh);
                search = search.Intersect(new Rect(0, 0, imgL.Width, imgL.Height));
            }

            using var searchMat = new Mat(imgL, search);
            using var res = new Mat();
            Cv2.MatchTemplate(searchMat, actualTpl, res, mode);
            Cv2.MinMaxLoc(res, out _, out var maxVal, out _, out var maxLoc);

            pred = new Point(maxLoc.X + search.X, maxLoc.Y + search.Y);
            
            tempTpl?.Dispose();

            if (level == 0)
            {
                return (maxVal, pred);
            }
        }

        return (0.0, new Point(0, 0));
    }

    private static GrayMat EnsureGrayBorrowed(Mat src)
    {
        if (src.Channels() == 1)
        {
            return new GrayMat(src, owned: null);
        }

        var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        return new GrayMat(gray, owned: gray);
    }

    private static Mat PreprocessTemplateForMatch(Mat templGrayOrBgr, PreprocessSettings? settings)
    {
        using var gray = EnsureGrayBorrowed(templGrayOrBgr);
        if (settings is null)
        {
            return gray.Mat.Clone();
        }

        var prep = new ImagePreprocessor();
        using var processed = prep.Run(gray.Mat, settings);

        if (processed.Channels() == 1)
        {
            return processed.Clone();
        }

        var processedGray = new Mat();
        Cv2.CvtColor(processed, processedGray, ColorConversionCodes.BGR2GRAY);
        return processedGray;
    }

    private static Mat RotateSameSize(Mat templGray, double angleDeg)
    {
        var center = new Point2f(templGray.Width / 2f, templGray.Height / 2f);
        using var m = Cv2.GetRotationMatrix2D(center, -angleDeg, 1.0);
        var dst = new Mat();
        Cv2.WarpAffine(templGray, dst, m, new Size(templGray.Width, templGray.Height), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        return dst;
    }

    private static Mat RotateWithPadding(Mat templGray, double angleDeg)
    {
        var w = templGray.Width;
        var h = templGray.Height;
        var diag = (int)Math.Ceiling(Math.Sqrt(w * w + h * h));
        diag = Math.Max(diag, Math.Max(w, h));

        var padded = new Mat(new Size(diag, diag), MatType.CV_8UC1, Scalar.Black);
        var x = (diag - w) / 2;
        var y = (diag - h) / 2;
        using (var roi = new Mat(padded, new Rect(x, y, w, h)))
        {
            templGray.CopyTo(roi);
        }

        if (Math.Abs(angleDeg) < 1e-6)
        {
            return padded;
        }

        var center = new Point2f(x + w / 2f, y + h / 2f);
        using var m = Cv2.GetRotationMatrix2D(center, -angleDeg, 1.0);
        var dst = new Mat();
        Cv2.WarpAffine(padded, dst, m, new Size(diag, diag), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);
        padded.Dispose();
        return dst;
    }

    private static Rect ContentRectFromNonZero(Mat srcGray, int pad)
    {
        if (srcGray.Empty())
        {
            return new Rect(0, 0, 0, 0);
        }

        using var nz = new Mat();
        Cv2.FindNonZero(srcGray, nz);
        if (nz.Empty())
        {
            return new Rect(0, 0, 0, 0);
        }

        var r = Cv2.BoundingRect(nz);
        var x = Math.Max(0, r.X - pad);
        var y = Math.Max(0, r.Y - pad);
        var right = Math.Min(srcGray.Width, r.X + r.Width + pad);
        var bottom = Math.Min(srcGray.Height, r.Y + r.Height + pad);
        var w = Math.Max(0, right - x);
        var h = Math.Max(0, bottom - y);
        return new Rect(x, y, w, h);
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

public sealed class CoordinateSystem
{
    public Point2d Offset { get; }

    public CoordinateSystem(Point2d offset)
    {
        Offset = offset;
    }

    public static CoordinateSystem FromOrigin(Point2d originFound, Point2d originTeach)
    {
        var dx = originFound.X - originTeach.X;
        var dy = originFound.Y - originTeach.Y;
        return new CoordinateSystem(new Point2d(dx, dy));
    }

    public Roi TransformRoi(Roi roi)
    {
        return new Roi
        {
            X = (int)Math.Round(roi.X + Offset.X),
            Y = (int)Math.Round(roi.Y + Offset.Y),
            Width = roi.Width,
            Height = roi.Height
        };
    }

    public Point2d TransformPoint(Point2d p)
    {
        return new Point2d(p.X + Offset.X, p.Y + Offset.Y);
    }
}

public sealed class DistanceCalculator
{
    public static double Distance(Point2d a, Point2d b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public DistanceCheckResult CheckDistance(LineDistance spec, Point2d a, Point2d b, double pixelsPerMm)
    {
        var distPx = Distance(a, b);
        var value = pixelsPerMm > 0 ? distPx / pixelsPerMm : distPx;
        var min = spec.Nominal - spec.ToleranceMinus;
        var max = spec.Nominal + spec.TolerancePlus;
        var pass = value >= min && value <= max;
        return new DistanceCheckResult(spec.Name, spec.PointA, spec.PointB, value, spec.Nominal, spec.TolerancePlus, spec.ToleranceMinus, pass);
    }
}

public sealed class DefectDetector : IDefectDetector
{
    public DefectDetectionResult Detect(Mat image, DefectInspectionConfig config)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var result = new DefectDetectionResult();

        var roiRect = new Rect(
            Math.Clamp(config.InspectRoi.X, 0, Math.Max(0, image.Width - 1)),
            Math.Clamp(config.InspectRoi.Y, 0, Math.Max(0, image.Height - 1)),
            Math.Clamp(config.InspectRoi.Width, 0, image.Width - Math.Clamp(config.InspectRoi.X, 0, Math.Max(0, image.Width - 1))),
            Math.Clamp(config.InspectRoi.Height, 0, image.Height - Math.Clamp(config.InspectRoi.Y, 0, Math.Max(0, image.Height - 1)))
        );

        if (roiRect.Width <= 0 || roiRect.Height <= 0)
        {
            return result;
        }

        using var roi = new Mat(image, roiRect);
        using var gray = roi.Channels() == 1 ? roi.Clone() : roi.CvtColor(ColorConversionCodes.BGR2GRAY);

        DetectWhite(gray, roiRect.Location, config, result);
        DetectBlack(gray, roiRect.Location, config, result);

        return result;
    }

    private static void DetectWhite(Mat gray, Point offset, DefectInspectionConfig config, DefectDetectionResult result)
    {
        using var mask = new Mat();
        Cv2.Threshold(gray, mask, config.ThresholdWhite, 255, ThresholdTypes.Binary);
        ExtractBlobs(mask, offset, config, result, "WHITE");
    }

    private static void DetectBlack(Mat gray, Point offset, DefectInspectionConfig config, DefectDetectionResult result)
    {
        using var mask = new Mat();
        Cv2.Threshold(gray, mask, config.ThresholdBlack, 255, ThresholdTypes.BinaryInv);
        ExtractBlobs(mask, offset, config, result, "BLACK");
    }

    private static void ExtractBlobs(Mat binaryMask, Point offset, DefectInspectionConfig config, DefectDetectionResult result, string type)
    {
        Cv2.FindContours(binaryMask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        foreach (var c in contours)
        {
            var area = Cv2.ContourArea(c);
            if (area < config.MinBlobSize || area > config.MaxBlobSize)
            {
                continue;
            }

            var rect = Cv2.BoundingRect(c);
            var global = new Rect(rect.X + offset.X, rect.Y + offset.Y, rect.Width, rect.Height);
            result.Defects.Add(new DefectBlob(global, area, type));
        }
    }
}
