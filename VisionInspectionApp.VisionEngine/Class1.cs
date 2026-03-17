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

public sealed record MatchResult(Point2d Position, double Score, double AngleDeg, Rect MatchRect);

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

        var cx = (w - 1) / 2.0;
        var cy = (h - 1) / 2.0;

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
                var fx = f.Dx + (int)Math.Round(cx);
                var fy = f.Dy + (int)Math.Round(cy);
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
                Dx = (int)Math.Round(dx0),
                Dy = (int)Math.Round(dy0),
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

        if (definition.ShapeModel is not null
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

        using var templPrep0 = PreprocessTemplateForMatch(templateGray, preprocess);

        using var templEdges0 = new Mat();
        Cv2.Canny(templPrep0, templEdges0, 50, 150);
        using var templEdgesRot = RotateWithPadding(templEdges0, angleDeg);
        var crop = ContentRectFromNonZero(templEdgesRot, pad: 2);
        if (crop.Width <= 0 || crop.Height <= 0)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, angleDeg, roiRect);
        }

        using var templGrayRot = RotateWithPadding(templPrep0, angleDeg);
        if (crop.X < 0 || crop.Y < 0 || crop.X + crop.Width > templGrayRot.Width || crop.Y + crop.Height > templGrayRot.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, angleDeg, roiRect);
        }

        using var templ = new Mat(templGrayRot, crop);

        if (roiGray.Mat.Width < templ.Width || roiGray.Mat.Height < templ.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, angleDeg, roiRect);
        }

        var (maxVal, maxLoc) = MatchTemplatePyramid(roiGray.Mat, templ, TemplateMatchModes.CCoeffNormed);

        var centerInRoi = new Point2d(maxLoc.X + templ.Width / 2.0, maxLoc.Y + templ.Height / 2.0);
        var global = new Point2d(centerInRoi.X + roiRect.X, centerInRoi.Y + roiRect.Y);
        var matchRect = new Rect(roiRect.X + maxLoc.X, roiRect.Y + maxLoc.Y, templ.Width, templ.Height);
        return new MatchResult(global, maxVal, angleDeg, matchRect);
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
        return new MatchResult(center, score, angleDeg, rect);
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

        foreach (var f in model.Features)
        {
            var rdx = (int)Math.Round(f.Dx * cos - f.Dy * sin);
            var rdy = (int)Math.Round(f.Dx * sin + f.Dy * cos);
            var b = (f.Bin + binShift) % binCount;
            var w = Math.Max(1, f.Weight);

            var tx = cx + rdx;
            var ty = cy + rdy;
            if (tx < 0 || ty < 0 || tx >= tplW || ty >= tplH) continue;

            var pts = edgeByBin[b];
            for (var p = 0; p < pts.Count; p++)
            {
                var ip = pts[p];
                var ox = ip.X - tx;
                var oy = ip.Y - ty;
                if ((uint)ox > (uint)maxX || (uint)oy > (uint)maxY) continue;
                accScratch[oy * accW + ox] += w;
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
        var score = (double)best / totalWeight;
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
        if (roiRect.Width <= 0 || roiRect.Height <= 0)
        {
            throw new ArgumentException($"Invalid SearchRoi for point '{definition.Name}'.");
        }

        using var roi = new Mat(image, roiRect);

        using var roiGray = EnsureGrayBorrowed(roi);

        if (definition.ShapeModel is not null
            && definition.ShapeModel.TemplateWidth > 0
            && definition.ShapeModel.TemplateHeight > 0
            && definition.ShapeModel.Features is not null
            && definition.ShapeModel.Features.Count > 0)
        {
            var model = definition.ShapeModel;
            var binCount = Math.Clamp(model.BinCount, 8, 64);
            var edgeByBin = BuildEdgeByBinFromSobel(roiGray.Mat, binCount, magThreshold: 20.0f);

            var bestScoreSm = double.NegativeInfinity;
            var bestAngleSm = 0.0;
            var bestCenter = new Point2d(roiRect.Width / 2.0, roiRect.Height / 2.0);
            var bestRect = new Rect(0, 0, model.TemplateWidth, model.TemplateHeight);

            if (stepDeg <= 0.000001)
            {
                stepDeg = 1.0;
            }

            var maxX = roiRect.Width - model.TemplateWidth;
            var maxY = roiRect.Height - model.TemplateHeight;
            if (maxX < 0 || maxY < 0)
            {
                var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
                return new MatchResult(centerFallback, 0.0, 0.0, roiRect);
            }

            var accScratch = new int[(maxX + 1) * (maxY + 1)];

            const double earlyExitScore = 0.97;
            var searchMin = minAngleDeg;
            var searchMax = maxAngleDeg;
            if (searchMax < searchMin)
            {
                (searchMin, searchMax) = (searchMax, searchMin);
            }

            var coarseStepDeg = Math.Max(stepDeg * 3.0, 4.0);
            if ((searchMax - searchMin) < coarseStepDeg * 1.5)
            {
                coarseStepDeg = stepDeg;
            }

            var bestAngleCoarse = 0.0;
            var ang = searchMin;
            while (ang <= searchMax + 0.000001)
            {
                var (score, center, rect) = ScoreByShapeModel(edgeByBin, roiRect.Width, roiRect.Height, model, ang, accScratch);
                if (score > bestScoreSm)
                {
                    bestScoreSm = score;
                    bestAngleSm = ang;
                    bestAngleCoarse = ang;
                    bestCenter = center;
                    bestRect = rect;
                    if (bestScoreSm >= earlyExitScore)
                    {
                        break;
                    }
                }

                ang += coarseStepDeg;
            }

            if (bestScoreSm < earlyExitScore && coarseStepDeg > stepDeg + 0.000001)
            {
                var refineMin = Math.Max(searchMin, bestAngleCoarse - coarseStepDeg);
                var refineMax = Math.Min(searchMax, bestAngleCoarse + coarseStepDeg);

                ang = refineMin;
                while (ang <= refineMax + 0.000001)
                {
                    var (score, center, rect) = ScoreByShapeModel(edgeByBin, roiRect.Width, roiRect.Height, model, ang, accScratch);
                    if (score > bestScoreSm)
                    {
                        bestScoreSm = score;
                        bestAngleSm = ang;
                        bestCenter = center;
                        bestRect = rect;
                        if (bestScoreSm >= earlyExitScore)
                        {
                            break;
                        }
                    }

                    ang += stepDeg;
                }
            }

            var globalPos = new Point2d(bestCenter.X + roiRect.X, bestCenter.Y + roiRect.Y);
            var mr = new Rect(roiRect.X + bestRect.X, roiRect.Y + bestRect.Y, bestRect.Width, bestRect.Height);
            return new MatchResult(globalPos, bestScoreSm, bestAngleSm, mr);
        }

        using var templPrep0 = PreprocessTemplateForMatch(templateGray, preprocess);

        using var roiEdges = new Mat();
        using var templEdges0 = new Mat();
        Cv2.Canny(roiGray.Mat, roiEdges, 50, 150);
        Cv2.Canny(templPrep0, templEdges0, 50, 150);

        if (roiGray.Mat.Width < templPrep0.Width || roiGray.Mat.Height < templPrep0.Height)
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
        if (roiGray.Mat.Width < bestTemplGray.Width || roiGray.Mat.Height < bestTemplGray.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, bestAngle, roiRect);
        }

        var (maxValGray, maxLocGray) = MatchTemplatePyramid(roiGray.Mat, bestTemplGray, TemplateMatchModes.CCoeffNormed);

        var centerInRoi = new Point2d(maxLocGray.X + bestTemplGray.Width / 2.0, maxLocGray.Y + bestTemplGray.Height / 2.0);
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

            if (imgL.Width < tplL.Width || imgL.Height < tplL.Height)
            {
                return (0.0, new Point(0, 0));
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
                var sw = Math.Min(imgL.Width - sx, tplL.Width + 2 * r);
                var sh = Math.Min(imgL.Height - sy, tplL.Height + 2 * r);

                if (sw < tplL.Width) sw = tplL.Width;
                if (sh < tplL.Height) sh = tplL.Height;

                search = new Rect(sx, sy, sw, sh);
                search = search.Intersect(new Rect(0, 0, imgL.Width, imgL.Height));
            }

            using var searchMat = new Mat(imgL, search);
            using var res = new Mat();
            Cv2.MatchTemplate(searchMat, tplL, res, mode);
            Cv2.MinMaxLoc(res, out _, out var maxVal, out _, out var maxLoc);

            pred = new Point(maxLoc.X + search.X, maxLoc.Y + search.Y);

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
        using var m = Cv2.GetRotationMatrix2D(center, angleDeg, 1.0);
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

        var center = new Point2f(diag / 2f, diag / 2f);
        using var m = Cv2.GetRotationMatrix2D(center, angleDeg, 1.0);
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
