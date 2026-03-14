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

public sealed record MatchResult(Point2d Position, double Score, double AngleDeg);

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
        var disposeList = new List<Mat>();

        try
        {
            if (settings.UseGray && current.Channels() > 1)
            {
                var gray = new Mat();
                Cv2.CvtColor(current, gray, ColorConversionCodes.BGR2GRAY);
                disposeList.Add(gray);
                current = gray;
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
            }

            if (settings.UseThreshold)
            {
                if (current.Channels() > 1)
                {
                    var gray = new Mat();
                    Cv2.CvtColor(current, gray, ColorConversionCodes.BGR2GRAY);
                    disposeList.Add(gray);
                    current = gray;
                }

                var thr = new Mat();
                Cv2.Threshold(current, thr, settings.ThresholdValue, 255, ThresholdTypes.Binary);
                disposeList.Add(thr);
                current = thr;
            }

            if (settings.UseCanny)
            {
                if (current.Channels() > 1)
                {
                    var gray = new Mat();
                    Cv2.CvtColor(current, gray, ColorConversionCodes.BGR2GRAY);
                    disposeList.Add(gray);
                    current = gray;
                }

                var edges = new Mat();
                Cv2.Canny(current, edges, settings.Canny1, settings.Canny2);
                disposeList.Add(edges);
                current = edges;
            }

            if (settings.UseMorphology)
            {
                if (current.Channels() > 1)
                {
                    var gray = new Mat();
                    Cv2.CvtColor(current, gray, ColorConversionCodes.BGR2GRAY);
                    disposeList.Add(gray);
                    current = gray;
                }

                var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
                var mor = new Mat();
                Cv2.MorphologyEx(current, mor, MorphTypes.Close, kernel);
                disposeList.Add(mor);
                current = mor;
            }

            return current.Clone();
        }
        finally
        {
            foreach (var m in disposeList)
            {
                m.Dispose();
            }
        }
    }
}

public sealed class PatternMatcher
{
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

        using var roi = new Mat(image, roiRect);
        using var templ = Cv2.ImRead(definition.TemplateImageFile, ImreadModes.Grayscale);

        using var roiGray = EnsureGray(roi);
        using var templPrep = PreprocessTemplateForMatch(templ, preprocess);

        if (roiGray.Width < templPrep.Width || roiGray.Height < templPrep.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0);
        }

        using var result = new Mat();
        Cv2.MatchTemplate(roiGray, templPrep, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

        var centerInRoi = new Point2d(maxLoc.X + templPrep.Width / 2.0, maxLoc.Y + templPrep.Height / 2.0);
        var global = new Point2d(centerInRoi.X + roiRect.X, centerInRoi.Y + roiRect.Y);
        return new MatchResult(global, maxVal, 0.0);
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

        if (string.IsNullOrWhiteSpace(definition.TemplateImageFile) || !File.Exists(definition.TemplateImageFile))
        {
            throw new FileNotFoundException($"Template file not found for point '{definition.Name}'.", definition.TemplateImageFile);
        }

        using var roi = new Mat(image, roiRect);
        using var templ0 = Cv2.ImRead(definition.TemplateImageFile, ImreadModes.Grayscale);
        using var roiGray = EnsureGray(roi);
        using var templPrep0 = PreprocessTemplateForMatch(templ0, preprocess);

        using var templEdges0 = new Mat();
        Cv2.Canny(templPrep0, templEdges0, 50, 150);
        using var templEdgesRot = RotateWithPadding(templEdges0, angleDeg);
        var crop = ContentRectFromNonZero(templEdgesRot, pad: 2);
        if (crop.Width <= 0 || crop.Height <= 0)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, angleDeg);
        }

        using var templGrayRot = RotateWithPadding(templPrep0, angleDeg);
        if (crop.X < 0 || crop.Y < 0 || crop.X + crop.Width > templGrayRot.Width || crop.Y + crop.Height > templGrayRot.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, angleDeg);
        }

        using var templ = new Mat(templGrayRot, crop);

        if (roiGray.Width < templ.Width || roiGray.Height < templ.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, angleDeg);
        }

        using var result = new Mat();
        Cv2.MatchTemplate(roiGray, templ, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

        var centerInRoi = new Point2d(maxLoc.X + templ.Width / 2.0, maxLoc.Y + templ.Height / 2.0);
        var global = new Point2d(centerInRoi.X + roiRect.X, centerInRoi.Y + roiRect.Y);
        return new MatchResult(global, maxVal, angleDeg);
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

        using var roi = new Mat(image, roiRect);
        using var templ0 = Cv2.ImRead(definition.TemplateImageFile, ImreadModes.Grayscale);

        using var roiGray = EnsureGray(roi);
        using var templPrep0 = PreprocessTemplateForMatch(templ0, preprocess);

        using var roiEdges = new Mat();
        using var templEdges0 = new Mat();
        Cv2.Canny(roiGray, roiEdges, 50, 150);
        Cv2.Canny(templPrep0, templEdges0, 50, 150);

        if (roiGray.Width < templPrep0.Width || roiGray.Height < templPrep0.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, 0.0);
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

            using var result = new Mat();
            Cv2.MatchTemplate(roiEdges, templEdges, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

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
            return new MatchResult(centerFallback, 0.0, 0.0);
        }

        using var bestTemplGrayRot = RotateWithPadding(templPrep0, bestAngle);
        if (bestCrop.Width <= 0 || bestCrop.Height <= 0
            || bestCrop.X < 0 || bestCrop.Y < 0
            || bestCrop.X + bestCrop.Width > bestTemplGrayRot.Width
            || bestCrop.Y + bestCrop.Height > bestTemplGrayRot.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, bestAngle);
        }

        using var bestTemplGray = new Mat(bestTemplGrayRot, bestCrop);
        if (roiGray.Width < bestTemplGray.Width || roiGray.Height < bestTemplGray.Height)
        {
            var centerFallback = new Point2d(roiRect.X + roiRect.Width / 2.0, roiRect.Y + roiRect.Height / 2.0);
            return new MatchResult(centerFallback, 0.0, bestAngle);
        }

        using var resultGray = new Mat();
        Cv2.MatchTemplate(roiGray, bestTemplGray, resultGray, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(resultGray, out _, out var maxValGray, out _, out var maxLocGray);

        var centerInRoi = new Point2d(maxLocGray.X + bestTemplGray.Width / 2.0, maxLocGray.Y + bestTemplGray.Height / 2.0);
        var global = new Point2d(centerInRoi.X + roiRect.X, centerInRoi.Y + roiRect.Y);
        return new MatchResult(global, maxValGray, bestAngle);
    }

    private static Mat EnsureGray(Mat src)
    {
        if (src.Channels() == 1)
        {
            return src.Clone();
        }

        var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    private static Mat PreprocessTemplateForMatch(Mat templGrayOrBgr, PreprocessSettings? settings)
    {
        using var gray = EnsureGray(templGrayOrBgr);
        if (settings is null)
        {
            return gray.Clone();
        }

        var prep = new ImagePreprocessor();
        using var processed = prep.Run(gray, settings);

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
