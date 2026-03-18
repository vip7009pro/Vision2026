using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.IO;
using ZXing;
using ZXing.Common;

namespace VisionInspectionApp.Application;

public interface IConfigService
{
    VisionConfig LoadConfig(string productCode);

    void SaveConfig(VisionConfig config);
}

public sealed record EdgePairDetectResult(
    string Name,
    bool Found,
    Point2d L1P1,
    Point2d L1P2,
    Point2d L2P1,
    Point2d L2P2,
    double Value,
    double Nominal,
    double TolPlus,
    double TolMinus,
    bool Pass,
    Point2d ClosestA,
    Point2d ClosestB,
    List<CaliperEdgePoint> Edge1Points,
    List<CaliperEdgePoint> Edge2Points);

public sealed record CircleFinderResult(
    string Name,
    bool Found,
    Point2d Center,
    double RadiusPx,
    double Score);

public sealed record DiameterResult(
    string Name,
    string CircleRef,
    bool Found,
    double Value,
    double Nominal,
    double TolPlus,
    double TolMinus,
    bool Pass,
    Point2d Center,
    double RadiusPx);

public sealed class ConfigStoreOptions
{
    public string ConfigRootDirectory { get; set; } = "configs";
}

public sealed record PointMatchResult(string Name, Point2d Position, Rect MatchRect, double Score, double Threshold, bool Pass, double AngleDeg);

public sealed class InspectionTimings
{
    public int TotalMs { get; set; }
    public int OriginMs { get; set; }
    public int PointsMs { get; set; }
    public int LinesMs { get; set; }
    public int BlobsMs { get; set; }
    public int SurfaceCompareMs { get; set; }
    public int LpdMs { get; set; }
    public int CalipersMs { get; set; }
    public int EdgePairDetectMs { get; set; }
    public int AnglesMs { get; set; }
    public int DistancesMs { get; set; }
    public int EdgePairsMs { get; set; }
    public int ConditionsMs { get; set; }
    public int DefectsMs { get; set; }
    public int CdtMs { get; set; }
}

public sealed class InspectionResult
{
    public bool Pass { get; set; }

    public InspectionTimings Timings { get; } = new();

    public PointMatchResult? Origin { get; set; }

    public List<PointMatchResult> Points { get; } = new();

    public List<LineDetectResult> Lines { get; } = new();

    public List<DistanceCheckResult> Distances { get; } = new();

    public List<SegmentDistanceResult> LineToLineDistances { get; } = new();

    public List<SegmentDistanceResult> PointToLineDistances { get; } = new();

    public List<AngleResult> Angles { get; } = new();

    public List<ConditionResult> Conditions { get; } = new();

    public List<BlobDetectionResult> BlobDetections { get; } = new();

    public List<SurfaceCompareResult> SurfaceCompares { get; } = new();

    public List<LinePairDetectionResult> LinePairDetections { get; } = new();

    public List<EdgePairResult> EdgePairs { get; } = new();

    public List<EdgePairDetectResult> EdgePairDetections { get; } = new();

    public List<CircleFinderResult> CircleFinders { get; } = new();

    public List<DiameterResult> Diameters { get; } = new();

    public List<CaliperResult> Calipers { get; } = new();

    public List<CodeDetectionResult> CodeDetections { get; } = new();

    public DefectDetectionResult? Defects { get; set; }
}

public sealed record EdgePairResult(
    string Name,
    string RefA,
    string RefB,
    bool Found,
    Point2d L1P1,
    Point2d L1P2,
    Point2d L2P1,
    Point2d L2P2,
    double Value,
    double Nominal,
    double TolPlus,
    double TolMinus,
    bool Pass,
    Point2d ClosestA,
    Point2d ClosestB);

public sealed record ConditionResult(string Name, string Expression, bool Pass, string? Error);

public sealed record BlobInfo(Rect BoundingBox, Point2d Centroid, double Area);

public sealed record BlobDetectionResult(string Name, int Count, List<BlobInfo> Blobs);

public sealed record SurfaceCompareDefect(Rect BoundingBox, Point2d Centroid, double Area);

public sealed record SurfaceCompareResult(string Name, int Count, double MaxArea, List<SurfaceCompareDefect> Defects);

public sealed record LinePairDetectionResult(
    string Name,
    bool Found,
    Point2d L1P1,
    Point2d L1P2,
    Point2d L2P1,
    Point2d L2P2,
    double Value,
    double Nominal,
    double TolPlus,
    double TolMinus,
    bool Pass,
    Point2d ClosestA,
    Point2d ClosestB);

public sealed record CaliperEdgePoint(double X, double Y, double Strength);

public sealed record CaliperResult(
    string Name,
    bool Found,
    List<CaliperEdgePoint> Points,
    Point2d LineP1,
    Point2d LineP2,
    double AvgStrength);

public sealed record AngleResult(
    string Name,
    string LineA,
    string LineB,
    double ValueDeg,
    double Nominal,
    double TolPlus,
    double TolMinus,
    bool Pass,
    bool Found,
    Point2d Intersection,
    Point2d ADir,
    Point2d BDir);

public sealed record CodeDetectionResult(string Name, bool Found, string Text, Rect BoundingBox);

public interface IInspectionService
{
    InspectionResult Inspect(Mat image, VisionConfig config);
}

public sealed class InspectionService : IInspectionService
{
    private readonly ImagePreprocessor _preprocessor;
    private readonly PatternMatcher _matcher;
    private readonly DistanceCalculator _distanceCalculator;
    private readonly LineDetector _lineDetector;
    private readonly IDefectDetector _defectDetector;

    private sealed class TrackState
    {
        public Point2d? LastOriginPos { get; set; }
        public double LastAngleDeg { get; set; }
        public ConcurrentDictionary<string, Point2d> LastPointPos { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly ConcurrentDictionary<string, TrackState> _trackByProductCode = new(StringComparer.OrdinalIgnoreCase);

    public InspectionService(
        ImagePreprocessor preprocessor,
        PatternMatcher matcher,
        DistanceCalculator distanceCalculator,
        LineDetector lineDetector,
        IDefectDetector defectDetector)
    {
        _preprocessor = preprocessor;
        _matcher = matcher;
        _distanceCalculator = distanceCalculator;
        _lineDetector = lineDetector;
        _defectDetector = defectDetector;
    }

    public InspectionResult Inspect(Mat image, VisionConfig config)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var result = new InspectionResult();

        var swTotal = Stopwatch.StartNew();

        var matsToDispose = new List<Mat>();
        var matsLock = new object();
        try
        {
            const int guidedRadiusPx = 50;
            var track = _trackByProductCode.GetOrAdd(config.ProductCode ?? string.Empty, _ => new TrackState());

            static Roi IntersectRoi(Roi a, Roi b)
            {
                if (a.Width <= 0 || a.Height <= 0) return new Roi();
                if (b.Width <= 0 || b.Height <= 0) return new Roi();

                var ax2 = a.X + a.Width;
                var ay2 = a.Y + a.Height;
                var bx2 = b.X + b.Width;
                var by2 = b.Y + b.Height;

                var x1 = Math.Max(a.X, b.X);
                var y1 = Math.Max(a.Y, b.Y);
                var x2 = Math.Min(ax2, bx2);
                var y2 = Math.Min(ay2, by2);

                var w = x2 - x1;
                var h = y2 - y1;
                if (w <= 0 || h <= 0) return new Roi();
                return new Roi { X = x1, Y = y1, Width = w, Height = h };
            }

            static Roi ClampRoiToImage(Roi roi, Mat img)
            {
                if (roi.Width <= 0 || roi.Height <= 0) return new Roi();

                var x1 = Math.Clamp(roi.X, 0, Math.Max(0, img.Width - 1));
                var y1 = Math.Clamp(roi.Y, 0, Math.Max(0, img.Height - 1));
                var x2 = Math.Clamp(roi.X + roi.Width, 0, img.Width);
                var y2 = Math.Clamp(roi.Y + roi.Height, 0, img.Height);

                var w = x2 - x1;
                var h = y2 - y1;
                if (w <= 0 || h <= 0) return new Roi();
                return new Roi { X = x1, Y = y1, Width = w, Height = h };
            }

            static Roi WindowRoi(Point2d center, int radius)
            {
                var x = (int)Math.Round(center.X - radius);
                var y = (int)Math.Round(center.Y - radius);
                var s = radius * 2;
                return new Roi { X = x, Y = y, Width = s, Height = s };
            }
            var nodesById = (config.ToolGraph?.Nodes ?? new List<ToolGraphNode>())
                .Where(n => !string.IsNullOrWhiteSpace(n.Id))
                .ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

            var preprocessSettingsByName = (config.PreprocessNodes ?? new List<PreprocessNodeDefinition>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .ToDictionary(p => p.Name, p => p.Settings ?? new PreprocessSettings(), StringComparer.OrdinalIgnoreCase);

            var edges = config.ToolGraph?.Edges ?? new List<ToolGraphEdge>();

            // Default (backward-compatible) processing path (lazy + thread-safe).
            var processedDefault = new Lazy<Mat>(() =>
            {
                var m = _preprocessor.Run(image, config.Preprocess);
                lock (matsLock) matsToDispose.Add(m);
                return m;
            });

            Mat GetProcessedDefault() => processedDefault.Value;

            var preprocessMatCache = new ConcurrentDictionary<string, Mat>(StringComparer.OrdinalIgnoreCase);

            var templateCache = new ConcurrentDictionary<string, Mat>(StringComparer.OrdinalIgnoreCase);

            Mat GetTemplateGray(string? templatePath)
            {
                if (string.IsNullOrWhiteSpace(templatePath))
                {
                    return new Mat();
                }

                return templateCache.GetOrAdd(templatePath, p =>
                {
                    var t = Cv2.ImRead(p, ImreadModes.Grayscale);
                    lock (matsLock) matsToDispose.Add(t);
                    return t;
                });
            }

            Mat GetPreprocessNodeOutput(string preprocessNodeId)
            {
                return preprocessMatCache.GetOrAdd(preprocessNodeId, id =>
                {
                    if (!nodesById.TryGetValue(id, out var node)
                        || !string.Equals(node.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                    {
                        return image;
                    }

                    preprocessSettingsByName.TryGetValue(node.RefName ?? string.Empty, out var settings);
                    settings ??= new PreprocessSettings();

                    // Preprocess node input: either raw image or another preprocess output connected to "In".
                    var inEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, id, StringComparison.OrdinalIgnoreCase)
                                                          && string.Equals(e.ToPort, "In", StringComparison.OrdinalIgnoreCase));
                    Mat baseMat = image;
                    if (inEdge is not null
                        && nodesById.TryGetValue(inEdge.FromNodeId, out var fromNode)
                        && string.Equals(fromNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                    {
                        baseMat = GetPreprocessNodeOutput(fromNode.Id);
                    }

                    var m = _preprocessor.Run(baseMat, settings);
                    lock (matsLock) matsToDispose.Add(m);
                    return m;
                });
            }

            (Mat ImageMat, PreprocessSettings Settings) ResolveToolPreprocess(string toolType, string toolRefName)
            {
                // Default.
                var settings = config.Preprocess;
                var mat = GetProcessedDefault();

                var toolNode = nodesById.Values.FirstOrDefault(n => string.Equals(n.Type, toolType, StringComparison.OrdinalIgnoreCase)
                                                                    && string.Equals(n.RefName, toolRefName, StringComparison.OrdinalIgnoreCase));
                if (toolNode is null)
                {
                    return (mat, settings);
                }

                var preEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, toolNode.Id, StringComparison.OrdinalIgnoreCase)
                                                       && string.Equals(e.ToPort, "Pre", StringComparison.OrdinalIgnoreCase));
                if (preEdge is null)
                {
                    return (mat, settings);
                }

                if (!nodesById.TryGetValue(preEdge.FromNodeId, out var ppNode)
                    || !string.Equals(ppNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                {
                    return (mat, settings);
                }

                preprocessSettingsByName.TryGetValue(ppNode.RefName ?? string.Empty, out var ppSettings);
                ppSettings ??= new PreprocessSettings();
                var ppMat = GetPreprocessNodeOutput(ppNode.Id);
                return (ppMat, ppSettings);
            }

            static List<BlobInfo> DetectBlobs(Mat matBgrOrGray, Roi roi, List<BlobRoiDefinition>? rois, BlobPolarity polarity, int threshold, int minArea, int maxArea)
            {
                var blobs = new List<BlobInfo>();

                if (matBgrOrGray is null || roi.Width <= 0 || roi.Height <= 0)
                {
                    return blobs;
                }

                // If multi-ROIs are provided, compute a working bounding box from INCLUDE ROIs.
                // This keeps performance reasonable while allowing masking for include/exclude.
                var hasMulti = rois is not null && rois.Count > 0;
                if (hasMulti)
                {
                    var inc = rois!.Where(x => x is not null && x.Mode == BlobRoiMode.Include && x.Roi.Width > 0 && x.Roi.Height > 0)
                        .Select(x => x.Roi)
                        .ToList();

                    if (inc.Count > 0)
                    {
                        var minX = inc.Min(x => x.X);
                        var minY = inc.Min(x => x.Y);
                        var maxX = inc.Max(x => x.X + x.Width);
                        var maxY = inc.Max(x => x.Y + x.Height);
                        roi = new Roi { X = minX, Y = minY, Width = Math.Max(1, maxX - minX), Height = Math.Max(1, maxY - minY) };
                    }
                }

                var rect = new Rect(roi.X, roi.Y, roi.Width, roi.Height);
                rect = rect.Intersect(new Rect(0, 0, matBgrOrGray.Width, matBgrOrGray.Height));
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    return blobs;
                }

                using var crop = new Mat(matBgrOrGray, rect);
                Mat gray = crop;
                using var grayOwned = crop.Channels() == 1 ? null : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
                if (grayOwned is not null)
                {
                    gray = grayOwned;
                }

                threshold = Math.Clamp(threshold, 0, 255);
                using var bw = new Mat();
                var thrType = polarity == BlobPolarity.DarkOnLight ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
                Cv2.Threshold(gray, bw, threshold, 255, thrType);

                if (hasMulti)
                {
                    using var mask = new Mat(bw.Rows, bw.Cols, MatType.CV_8UC1, Scalar.Black);

                    var anyInclude = false;
                    foreach (var rr in rois!)
                    {
                        if (rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                        {
                            continue;
                        }

                        var rx = rr.Roi.X - rect.X;
                        var ry = rr.Roi.Y - rect.Y;
                        var r = new Rect(rx, ry, rr.Roi.Width, rr.Roi.Height);
                        r = r.Intersect(new Rect(0, 0, bw.Cols, bw.Rows));
                        if (r.Width <= 0 || r.Height <= 0)
                        {
                            continue;
                        }

                        if (rr.Mode == BlobRoiMode.Include)
                        {
                            anyInclude = true;
                            using var sub = new Mat(mask, r);
                            sub.SetTo(Scalar.White);
                        }
                    }

                    if (!anyInclude)
                    {
                        mask.SetTo(Scalar.White);
                    }

                    foreach (var rr in rois!)
                    {
                        if (rr.Mode != BlobRoiMode.Exclude || rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                        {
                            continue;
                        }

                        var rx = rr.Roi.X - rect.X;
                        var ry = rr.Roi.Y - rect.Y;
                        var r = new Rect(rx, ry, rr.Roi.Width, rr.Roi.Height);
                        r = r.Intersect(new Rect(0, 0, bw.Cols, bw.Rows));
                        if (r.Width <= 0 || r.Height <= 0)
                        {
                            continue;
                        }

                        using var sub = new Mat(mask, r);
                        sub.SetTo(Scalar.Black);
                    }

                    Cv2.BitwiseAnd(bw, mask, bw);
                }

                minArea = Math.Max(0, minArea);
                maxArea = Math.Max(minArea, maxArea);

                // Use connected components so very small dots (even 1 pixel) have stable pixel area.
                // stats: [label, CC_STAT_LEFT, TOP, WIDTH, HEIGHT, AREA]
                using var labels = new Mat();
                using var stats = new Mat();
                using var centroids = new Mat();
                var nLabels = Cv2.ConnectedComponentsWithStats(
                    bw,
                    labels,
                    stats,
                    centroids,
                    PixelConnectivity.Connectivity8,
                    MatType.CV_32S);

                for (var i = 1; i < nLabels; i++) // 0 is background
                {
                    var left = stats.Get<int>(i, (int)ConnectedComponentsTypes.Left);
                    var top = stats.Get<int>(i, (int)ConnectedComponentsTypes.Top);
                    var width = stats.Get<int>(i, (int)ConnectedComponentsTypes.Width);
                    var height = stats.Get<int>(i, (int)ConnectedComponentsTypes.Height);
                    var areaPx = stats.Get<int>(i, (int)ConnectedComponentsTypes.Area);

                    if (areaPx < minArea || areaPx > maxArea)
                    {
                        continue;
                    }

                    var cx = centroids.Get<double>(i, 0);
                    var cy = centroids.Get<double>(i, 1);

                    // Convert from crop coordinates to full image coordinates
                    var fullRect = new Rect(left + rect.X, top + rect.Y, width, height);
                    var centroid = new Point2d(cx + rect.X, cy + rect.Y);
                    blobs.Add(new BlobInfo(fullRect, centroid, areaPx));
                }
                return blobs;
            }

            static SurfaceCompareResult RunSurfaceCompare(Mat matBgrOrGray, Point2d originTeach, Point2d originFound, double angleDeg, SurfaceCompareDefinition def)
            {
                if (matBgrOrGray is null)
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>());
                }

                if (def is null || string.IsNullOrWhiteSpace(def.Name))
                {
                    return new SurfaceCompareResult(string.Empty, 0, 0.0, new List<SurfaceCompareDefect>());
                }

                var templateRoiTeach = def.TemplateRoi;
                var inspectRoiTeach = def.InspectRoi;

                if (templateRoiTeach.Width <= 0 || templateRoiTeach.Height <= 0)
                {
                    templateRoiTeach = inspectRoiTeach;
                }

                if (inspectRoiTeach.Width <= 0 || inspectRoiTeach.Height <= 0)
                {
                    inspectRoiTeach = templateRoiTeach;
                }

                if (templateRoiTeach.Width <= 0 || templateRoiTeach.Height <= 0 || inspectRoiTeach.Width <= 0 || inspectRoiTeach.Height <= 0)
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>());
                }

                if (string.IsNullOrWhiteSpace(def.TemplateImageFile) || !File.Exists(def.TemplateImageFile))
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>());
                }

                // Convert input to grayscale.
                Mat testGray = matBgrOrGray;
                using var testGrayOwned = matBgrOrGray.Channels() == 1 ? null : matBgrOrGray.CvtColor(ColorConversionCodes.BGR2GRAY);
                if (testGrayOwned is not null)
                {
                    testGray = testGrayOwned;
                }

                // Build a full-size template canvas by placing the stored template crop back into TemplateRoi.
                using var templCrop0 = Cv2.ImRead(def.TemplateImageFile, ImreadModes.Grayscale);
                if (templCrop0.Empty())
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>());
                }

                using var templateCanvas = new Mat(testGray.Rows, testGray.Cols, MatType.CV_8UC1, Scalar.Black);
                var tplRect = new Rect(templateRoiTeach.X, templateRoiTeach.Y, templateRoiTeach.Width, templateRoiTeach.Height)
                    .Intersect(new Rect(0, 0, templateCanvas.Cols, templateCanvas.Rows));
                if (tplRect.Width <= 0 || tplRect.Height <= 0)
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>());
                }

                using (var dst = new Mat(templateCanvas, tplRect))
                {
                    // If stored crop size doesn't exactly match (older data), resize to fit.
                    if (templCrop0.Width != tplRect.Width || templCrop0.Height != tplRect.Height)
                    {
                        using var resized = new Mat();
                        Cv2.Resize(templCrop0, resized, new Size(tplRect.Width, tplRect.Height), 0, 0, InterpolationFlags.Area);
                        resized.CopyTo(dst);
                    }
                    else
                    {
                        templCrop0.CopyTo(dst);
                    }
                }

                // Warp template canvas from teach pose to found pose.
                var dx = originFound.X - originTeach.X;
                var dy = originFound.Y - originTeach.Y;
                using var m = Cv2.GetRotationMatrix2D(new Point2f((float)originTeach.X, (float)originTeach.Y), angleDeg, 1.0);
                m.Set(0, 2, m.Get<double>(0, 2) + dx);
                m.Set(1, 2, m.Get<double>(1, 2) + dy);

                using var templWarp = new Mat(testGray.Rows, testGray.Cols, MatType.CV_8UC1, Scalar.Black);
                Cv2.WarpAffine(templateCanvas, templWarp, m, new Size(testGray.Cols, testGray.Rows), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);

                // Inspect ROI is defined in teach space; transform to current pose (keep size).
                var inspectRoi = TransformRoiKeepSize(inspectRoiTeach, originTeach, originFound, angleDeg);
                if (inspectRoi.Width <= 0 || inspectRoi.Height <= 0)
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>());
                }

                var rect = new Rect(inspectRoi.X, inspectRoi.Y, inspectRoi.Width, inspectRoi.Height)
                    .Intersect(new Rect(0, 0, testGray.Cols, testGray.Rows));
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>());
                }

                // Compute diff in ROI.
                using var testCrop = new Mat(testGray, rect);
                using var tplCrop = new Mat(templWarp, rect);
                using var diff = new Mat();
                Cv2.Absdiff(testCrop, tplCrop, diff);

                var thr = Math.Clamp(def.DiffThreshold, 0, 255);
                using var bw = new Mat();
                Cv2.Threshold(diff, bw, thr, 255, ThresholdTypes.Binary);

                // Apply include/exclude multi-ROI mask (definitions are in teach space).
                var rois = def.Rois;
                if (rois is not null && rois.Count > 0)
                {
                    using var mask = new Mat(bw.Rows, bw.Cols, MatType.CV_8UC1, Scalar.Black);

                    var anyInclude = false;
                    foreach (var rr0 in rois)
                    {
                        if (rr0 is null || rr0.Roi.Width <= 0 || rr0.Roi.Height <= 0)
                        {
                            continue;
                        }

                        var rr = TransformRoiKeepSize(rr0.Roi, originTeach, originFound, angleDeg);
                        if (rr.Width <= 0 || rr.Height <= 0)
                        {
                            continue;
                        }

                        var rx = rr.X - rect.X;
                        var ry = rr.Y - rect.Y;
                        var r = new Rect(rx, ry, rr.Width, rr.Height)
                            .Intersect(new Rect(0, 0, bw.Cols, bw.Rows));
                        if (r.Width <= 0 || r.Height <= 0)
                        {
                            continue;
                        }

                        if (rr0.Mode == BlobRoiMode.Include)
                        {
                            anyInclude = true;
                            using var sub = new Mat(mask, r);
                            sub.SetTo(Scalar.White);
                        }
                    }

                    if (!anyInclude)
                    {
                        mask.SetTo(Scalar.White);
                    }

                    foreach (var rr0 in rois)
                    {
                        if (rr0 is null || rr0.Mode != BlobRoiMode.Exclude || rr0.Roi.Width <= 0 || rr0.Roi.Height <= 0)
                        {
                            continue;
                        }

                        var rr = TransformRoiKeepSize(rr0.Roi, originTeach, originFound, angleDeg);
                        if (rr.Width <= 0 || rr.Height <= 0)
                        {
                            continue;
                        }

                        var rx = rr.X - rect.X;
                        var ry = rr.Y - rect.Y;
                        var r = new Rect(rx, ry, rr.Width, rr.Height)
                            .Intersect(new Rect(0, 0, bw.Cols, bw.Rows));
                        if (r.Width <= 0 || r.Height <= 0)
                        {
                            continue;
                        }

                        using var sub = new Mat(mask, r);
                        sub.SetTo(Scalar.Black);
                    }

                    Cv2.BitwiseAnd(bw, mask, bw);
                }

                var k = Math.Max(1, def.MorphKernel);
                if (k % 2 == 0) k += 1;
                if (k >= 3)
                {
                    using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(k, k));
                    Cv2.MorphologyEx(bw, bw, MorphTypes.Close, kernel);
                    Cv2.MorphologyEx(bw, bw, MorphTypes.Open, kernel);
                }

                var minArea = Math.Max(0, def.MinBlobArea);
                var maxArea = Math.Max(minArea, def.MaxBlobArea);

                var defects = new List<SurfaceCompareDefect>();
                double maxFoundArea = 0.0;

                using var labels = new Mat();
                using var stats = new Mat();
                using var centroids = new Mat();
                var nLabels = Cv2.ConnectedComponentsWithStats(
                    bw,
                    labels,
                    stats,
                    centroids,
                    PixelConnectivity.Connectivity8,
                    MatType.CV_32S);

                for (var i = 1; i < nLabels; i++)
                {
                    var left = stats.Get<int>(i, (int)ConnectedComponentsTypes.Left);
                    var top = stats.Get<int>(i, (int)ConnectedComponentsTypes.Top);
                    var width = stats.Get<int>(i, (int)ConnectedComponentsTypes.Width);
                    var height = stats.Get<int>(i, (int)ConnectedComponentsTypes.Height);
                    var areaPx = stats.Get<int>(i, (int)ConnectedComponentsTypes.Area);

                    if (areaPx < minArea || areaPx > maxArea)
                    {
                        continue;
                    }

                    var cx = centroids.Get<double>(i, 0);
                    var cy = centroids.Get<double>(i, 1);

                    var fullRect = new Rect(left + rect.X, top + rect.Y, width, height);
                    var centroid = new Point2d(cx + rect.X, cy + rect.Y);
                    defects.Add(new SurfaceCompareDefect(fullRect, centroid, areaPx));
                    if (areaPx > maxFoundArea) maxFoundArea = areaPx;
                }

                return new SurfaceCompareResult(def.Name, defects.Count, maxFoundArea, defects);
            }

            static CaliperResult DetectCaliper(Mat matBgrOrGray, Roi roi, CaliperDefinition def)
            {
                if (matBgrOrGray is null || roi.Width <= 0 || roi.Height <= 0)
                {
                    return new CaliperResult(def.Name, Found: false, new List<CaliperEdgePoint>(), default, default, 0.0);
                }

                var rect = new Rect(roi.X, roi.Y, roi.Width, roi.Height)
                    .Intersect(new Rect(0, 0, matBgrOrGray.Width, matBgrOrGray.Height));
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    return new CaliperResult(def.Name, Found: false, new List<CaliperEdgePoint>(), default, default, 0.0);
                }

                using var crop = new Mat(matBgrOrGray, rect);
                Mat gray = crop;
                using var grayOwned = crop.Channels() == 1 ? null : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
                if (grayOwned is not null)
                {
                    gray = grayOwned;
                }

                var stripCount = Math.Clamp(def.StripCount, 1, 200);
                var stripWidth = Math.Clamp(def.StripWidth, 1, Math.Max(1, Math.Min(rect.Width, rect.Height)));
                var stripLength = Math.Clamp(def.StripLength, 3, Math.Max(3, Math.Max(rect.Width, rect.Height)));

                var points = new List<CaliperEdgePoint>(stripCount);
                var strengths = new List<double>(stripCount);

                static double InterpPeak(double a, double b, double c)
                {
                    var denom = (a - 2 * b + c);
                    if (Math.Abs(denom) < 1e-12) return 0.0;
                    return 0.5 * (a - c) / denom;
                }

                for (var i = 0; i < stripCount; i++)
                {
                    if (def.Orientation == CaliperOrientation.Vertical)
                    {
                        var xCenter = (i + 0.5) * rect.Width / stripCount;
                        var x0 = (int)Math.Round(xCenter - stripWidth / 2.0);
                        var y0 = (int)Math.Round((rect.Height - stripLength) / 2.0);
                        var sr = new Rect(x0, y0, stripWidth, stripLength)
                            .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                        if (sr.Width <= 0 || sr.Height <= 2) continue;

                        using var s = new Mat(gray, sr);
                        using var prof = new Mat();
                        Cv2.Reduce(s, prof, dim: ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_64F);

                        var n = prof.Rows;
                        if (n < 3) continue;

                        var bestIdx = -1;
                        var bestVal = 0.0;
                        for (var y = 1; y < n - 1; y++)
                        {
                            var v0 = prof.Get<double>(y - 1, 0);
                            var v1 = prof.Get<double>(y, 0);
                            var v2 = prof.Get<double>(y + 1, 0);
                            var g = (v2 - v0) * 0.5;
                            if (def.Polarity == EdgePolarity.DarkToLight) { if (g <= 0) continue; }
                            else if (def.Polarity == EdgePolarity.LightToDark) { if (g >= 0) continue; g = -g; }
                            else { g = Math.Abs(g); }

                            if (g > bestVal)
                            {
                                bestVal = g;
                                bestIdx = y;
                            }
                        }

                        if (bestIdx < 1 || bestIdx >= n - 1) continue;
                        if (bestVal < def.MinEdgeStrength) continue;

                        var gL = Math.Abs(prof.Get<double>(bestIdx, 0) - prof.Get<double>(bestIdx - 1, 0));
                        var gC = Math.Abs(prof.Get<double>(bestIdx + 1, 0) - prof.Get<double>(bestIdx - 1, 0)) * 0.5;
                        var gR = Math.Abs(prof.Get<double>(bestIdx + 1, 0) - prof.Get<double>(bestIdx, 0));
                        var sub = InterpPeak(gL, gC, gR);

                        var ySub = bestIdx + sub;
                        var xGlobal = rect.X + sr.X + sr.Width / 2.0;
                        var yGlobal = rect.Y + sr.Y + ySub;
                        points.Add(new CaliperEdgePoint(xGlobal, yGlobal, bestVal));
                        strengths.Add(bestVal);
                    }
                    else
                    {
                        var yCenter = (i + 0.5) * rect.Height / stripCount;
                        var y0 = (int)Math.Round(yCenter - stripWidth / 2.0);
                        var x0 = (int)Math.Round((rect.Width - stripLength) / 2.0);
                        var sr = new Rect(x0, y0, stripLength, stripWidth)
                            .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                        if (sr.Width <= 2 || sr.Height <= 0) continue;

                        using var s = new Mat(gray, sr);
                        using var prof = new Mat();
                        Cv2.Reduce(s, prof, dim: ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_64F);

                        var n = prof.Cols;
                        if (n < 3) continue;

                        var bestIdx = -1;
                        var bestVal = 0.0;
                        for (var x = 1; x < n - 1; x++)
                        {
                            var v0 = prof.Get<double>(0, x - 1);
                            var v1 = prof.Get<double>(0, x);
                            var v2 = prof.Get<double>(0, x + 1);
                            var g = (v2 - v0) * 0.5;
                            if (def.Polarity == EdgePolarity.DarkToLight) { if (g <= 0) continue; }
                            else if (def.Polarity == EdgePolarity.LightToDark) { if (g >= 0) continue; g = -g; }
                            else { g = Math.Abs(g); }

                            if (g > bestVal)
                            {
                                bestVal = g;
                                bestIdx = x;
                            }
                        }

                        if (bestIdx < 1 || bestIdx >= n - 1) continue;
                        if (bestVal < def.MinEdgeStrength) continue;

                        var gL = Math.Abs(prof.Get<double>(0, bestIdx) - prof.Get<double>(0, bestIdx - 1));
                        var gC = Math.Abs(prof.Get<double>(0, bestIdx + 1) - prof.Get<double>(0, bestIdx - 1)) * 0.5;
                        var gR = Math.Abs(prof.Get<double>(0, bestIdx + 1) - prof.Get<double>(0, bestIdx));
                        var sub = InterpPeak(gL, gC, gR);

                        var xSub = bestIdx + sub;
                        var xGlobal = rect.X + sr.X + xSub;
                        var yGlobal = rect.Y + sr.Y + sr.Height / 2.0;
                        points.Add(new CaliperEdgePoint(xGlobal, yGlobal, bestVal));
                        strengths.Add(bestVal);
                    }
                }

                if (points.Count < 2)
                {
                    var avg0 = strengths.Count == 0 ? 0.0 : strengths.Average();
                    return new CaliperResult(def.Name, Found: false, points, default, default, avg0);
                }

                var meanX = points.Average(p => p.X);
                var meanY = points.Average(p => p.Y);

                var sxx = 0.0;
                var syy = 0.0;
                var sxy = 0.0;
                foreach (var p in points)
                {
                    var dx = p.X - meanX;
                    var dy = p.Y - meanY;
                    sxx += dx * dx;
                    syy += dy * dy;
                    sxy += dx * dy;
                }

                var theta = 0.5 * Math.Atan2(2 * sxy, (sxx - syy));
                var dir = new Point2d(Math.Cos(theta), Math.Sin(theta));

                var minT = double.PositiveInfinity;
                var maxT = double.NegativeInfinity;
                foreach (var p in points)
                {
                    var t = (p.X - meanX) * dir.X + (p.Y - meanY) * dir.Y;
                    if (t < minT) minT = t;
                    if (t > maxT) maxT = t;
                }

                if (!double.IsFinite(minT) || !double.IsFinite(maxT))
                {
                    var avg0 = strengths.Average();
                    return new CaliperResult(def.Name, Found: false, points, default, default, avg0);
                }

                var p1 = new Point2d(meanX + minT * dir.X, meanY + minT * dir.Y);
                var p2 = new Point2d(meanX + maxT * dir.X, meanY + maxT * dir.Y);
                var avg = strengths.Average();
                return new CaliperResult(def.Name, Found: true, points, p1, p2, avg);
            }

            // Origin
            var tOrigin0 = swTotal.ElapsedMilliseconds;
            var (originMat, originPre) = ResolveToolPreprocess("Origin", config.Origin.Name);
            var originTempl = GetTemplateGray(config.Origin.TemplateImageFile);

            var originDefBase = config.Origin;
            var originDef = originDefBase;
            var usedGuidedOrigin = false;
            if (track.LastOriginPos is not null)
            {
                var guide = ClampRoiToImage(WindowRoi(track.LastOriginPos.Value, guidedRadiusPx), originMat);
                var shrunk = IntersectRoi(originDefBase.SearchRoi, guide);
                if (shrunk.Width > 0 && shrunk.Height > 0)
                {
                    usedGuidedOrigin = true;
                    originDef = new PointDefinition
                    {
                        Name = originDefBase.Name,
                        MatchScoreThreshold = originDefBase.MatchScoreThreshold,
                        TemplateImageFile = originDefBase.TemplateImageFile,
                        TemplateRoi = originDefBase.TemplateRoi,
                        SearchRoi = shrunk,
                        WorldPosition = originDefBase.WorldPosition,
                        OffsetPx = originDefBase.OffsetPx,
                        Algorithm = originDefBase.Algorithm,
                        EdgePoint = originDefBase.EdgePoint,
                        ShapeModel = originDefBase.ShapeModel
                    };
                }
            }

            var originMatch = _matcher.MatchWithRotation(originMat, originDef, originTempl, originPre, -60.0, 60.0, 2.0);
            if (usedGuidedOrigin && originMatch.Score < originDefBase.MatchScoreThreshold)
            {
                var retry = _matcher.MatchWithRotation(originMat, originDefBase, originTempl, originPre, -60.0, 60.0, 2.0);
                if (retry.Score > originMatch.Score)
                {
                    originMatch = retry;
                }
            }
            var templateAngleDeg = originMatch.AngleDeg;
            var poseAngleDeg = templateAngleDeg;
            var originPass = originMatch.Score >= config.Origin.MatchScoreThreshold;
            result.Origin = new PointMatchResult(
                config.Origin.Name,
                originMatch.Position,
                originMatch.MatchRect,
                originMatch.Score,
                config.Origin.MatchScoreThreshold,
                originPass,
                poseAngleDeg);
            result.Timings.OriginMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tOrigin0);

            var originTeach = new Point2d(config.Origin.WorldPosition.X, config.Origin.WorldPosition.Y);
            var originFound = originMatch.Position;
            var angleDeg = poseAngleDeg;

            var tTools0 = swTotal.ElapsedMilliseconds;
            var pointTasks = (config.Points ?? new List<PointDefinition>())
                .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => Task.Run(() =>
                {
                    var defBase = TransformPointDefinition(p, originTeach, originFound, angleDeg);
                    var def = defBase;

                    var (matForPoint, preForPoint) = ResolveToolPreprocess("Point", p.Name);

                    // Guided ROI (B): prioritize last known point position; otherwise use expected center from transformed SearchRoi.
                    Point2d center;
                    if (track.LastPointPos.TryGetValue(p.Name, out var lastP))
                    {
                        center = lastP;
                    }
                    else
                    {
                        center = new Point2d(def.SearchRoi.X + def.SearchRoi.Width / 2.0, def.SearchRoi.Y + def.SearchRoi.Height / 2.0);
                    }

                    var guide = ClampRoiToImage(WindowRoi(center, guidedRadiusPx), matForPoint);
                    var shrunk = IntersectRoi(def.SearchRoi, guide);
                    if (shrunk.Width > 0 && shrunk.Height > 0)
                    {
                        def = new PointDefinition
                        {
                            Name = def.Name,
                            MatchScoreThreshold = def.MatchScoreThreshold,
                            TemplateImageFile = def.TemplateImageFile,
                            TemplateRoi = def.TemplateRoi,
                            SearchRoi = shrunk,
                            WorldPosition = def.WorldPosition,
                            OffsetPx = def.OffsetPx,
                            Algorithm = def.Algorithm,
                            EdgePoint = def.EdgePoint,
                            ShapeModel = def.ShapeModel
                        };
                    }

                    static (bool Found, Point2d Position, double Score, Rect MatchRect) FindPointByEdge(Mat matBgrOrGray, Roi roi, EdgePointSettings ep)
                    {
                        if (matBgrOrGray is null || roi.Width <= 0 || roi.Height <= 0)
                        {
                            return (false, default, 0.0, default);
                        }

                        var rect = new Rect(roi.X, roi.Y, roi.Width, roi.Height)
                            .Intersect(new Rect(0, 0, matBgrOrGray.Width, matBgrOrGray.Height));
                        if (rect.Width <= 0 || rect.Height <= 0)
                        {
                            return (false, default, 0.0, default);
                        }

                        using var crop = new Mat(matBgrOrGray, rect);
                        Mat gray = crop;
                        using var grayOwned = crop.Channels() == 1 ? null : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
                        if (grayOwned is not null)
                        {
                            gray = grayOwned;
                        }

                        var stripCount = Math.Clamp(ep.StripCount, 1, 200);
                        var stripWidth = Math.Clamp(ep.StripWidth, 1, Math.Max(1, Math.Min(rect.Width, rect.Height)));
                        var stripLength = Math.Clamp(ep.StripLength, 3, Math.Max(3, Math.Max(rect.Width, rect.Height)));

                        var sumX = 0.0;
                        var sumY = 0.0;
                        var sumG = 0.0;
                        var foundN = 0;

                        static double InterpPeak(double a, double b, double c)
                        {
                            var denom = (a - 2 * b + c);
                            if (Math.Abs(denom) < 1e-12) return 0.0;
                            return 0.5 * (a - c) / denom;
                        }

                        for (var i = 0; i < stripCount; i++)
                        {
                            if (ep.Orientation == CaliperOrientation.Vertical)
                            {
                                var xCenter = (i + 0.5) * rect.Width / stripCount;
                                var x0 = (int)Math.Round(xCenter - stripWidth / 2.0);
                                var y0 = (int)Math.Round((rect.Height - stripLength) / 2.0);
                                var sr = new Rect(x0, y0, stripWidth, stripLength)
                                    .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                                if (sr.Width <= 0 || sr.Height <= 2) continue;

                                using var s = new Mat(gray, sr);
                                using var prof = new Mat();
                                Cv2.Reduce(s, prof, dim: ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_64F);

                                var n = prof.Rows;
                                if (n < 3) continue;

                                var bestIdx = -1;
                                var bestVal = 0.0;
                                for (var y = 1; y < n - 1; y++)
                                {
                                    var v0 = prof.Get<double>(y - 1, 0);
                                    var v1 = prof.Get<double>(y, 0);
                                    var v2 = prof.Get<double>(y + 1, 0);
                                    var g = (v2 - v0) * 0.5;
                                    if (ep.Polarity == EdgePolarity.DarkToLight) { if (g <= 0) continue; }
                                    else if (ep.Polarity == EdgePolarity.LightToDark) { if (g >= 0) continue; g = -g; }
                                    else { g = Math.Abs(g); }

                                    if (g > bestVal)
                                    {
                                        bestVal = g;
                                        bestIdx = y;
                                    }
                                }

                                if (bestIdx < 1 || bestIdx >= n - 1) continue;
                                if (bestVal < ep.MinEdgeStrength) continue;

                                var gL = Math.Abs(prof.Get<double>(bestIdx, 0) - prof.Get<double>(bestIdx - 1, 0));
                                var gC = Math.Abs(prof.Get<double>(bestIdx + 1, 0) - prof.Get<double>(bestIdx - 1, 0)) * 0.5;
                                var gR = Math.Abs(prof.Get<double>(bestIdx + 1, 0) - prof.Get<double>(bestIdx, 0));
                                var sub = InterpPeak(gL, gC, gR);

                                var ySub = bestIdx + sub;
                                var xGlobal = rect.X + sr.X + sr.Width / 2.0;
                                var yGlobal = rect.Y + sr.Y + ySub;

                                sumX += xGlobal;
                                sumY += yGlobal;
                                sumG += bestVal;
                                foundN++;
                            }
                            else
                            {
                                var yCenter = (i + 0.5) * rect.Height / stripCount;
                                var y0 = (int)Math.Round(yCenter - stripWidth / 2.0);
                                var x0 = (int)Math.Round((rect.Width - stripLength) / 2.0);
                                var sr = new Rect(x0, y0, stripLength, stripWidth)
                                    .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                                if (sr.Width <= 2 || sr.Height <= 0) continue;

                                using var s = new Mat(gray, sr);
                                using var prof = new Mat();
                                Cv2.Reduce(s, prof, dim: ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_64F);

                                var n = prof.Cols;
                                if (n < 3) continue;

                                var bestIdx = -1;
                                var bestVal = 0.0;
                                for (var x = 1; x < n - 1; x++)
                                {
                                    var v0 = prof.Get<double>(0, x - 1);
                                    var v1 = prof.Get<double>(0, x);
                                    var v2 = prof.Get<double>(0, x + 1);
                                    var g = (v2 - v0) * 0.5;
                                    if (ep.Polarity == EdgePolarity.DarkToLight) { if (g <= 0) continue; }
                                    else if (ep.Polarity == EdgePolarity.LightToDark) { if (g >= 0) continue; g = -g; }
                                    else { g = Math.Abs(g); }

                                    if (g > bestVal)
                                    {
                                        bestVal = g;
                                        bestIdx = x;
                                    }
                                }

                                if (bestIdx < 1 || bestIdx >= n - 1) continue;
                                if (bestVal < ep.MinEdgeStrength) continue;

                                var gL = Math.Abs(prof.Get<double>(0, bestIdx) - prof.Get<double>(0, bestIdx - 1));
                                var gC = Math.Abs(prof.Get<double>(0, bestIdx + 1) - prof.Get<double>(0, bestIdx - 1)) * 0.5;
                                var gR = Math.Abs(prof.Get<double>(0, bestIdx + 1) - prof.Get<double>(0, bestIdx));
                                var sub = InterpPeak(gL, gC, gR);

                                var xSub = bestIdx + sub;
                                var xGlobal = rect.X + sr.X + xSub;
                                var yGlobal = rect.Y + sr.Y + sr.Height / 2.0;

                                sumX += xGlobal;
                                sumY += yGlobal;
                                sumG += bestVal;
                                foundN++;
                            }
                        }

                        if (foundN <= 0)
                        {
                            return (false, default, foundN == 0 ? 0.0 : sumG / foundN, rect);
                        }

                        var pos = new Point2d(sumX / foundN, sumY / foundN);
                        var score = sumG / foundN;
                        return (true, pos, score, rect);
                    }

                    Point2d basePos;
                    Rect matchRect;
                    double score;
                    double thr;
                    bool pass;

                    if (p.Algorithm == PointFindAlgorithm.EdgePoint)
                    {
                        var r = FindPointByEdge(matForPoint, def.SearchRoi, p.EdgePoint);
                        basePos = r.Position;
                        matchRect = r.MatchRect;
                        score = r.Score;
                        thr = p.EdgePoint.MinEdgeStrength;
                        pass = r.Found;
                    }
                    else
                    {
                        var templ = GetTemplateGray(def.TemplateImageFile);
                        var m = _matcher.MatchWithFixedRotation(matForPoint, def, templ, templateAngleDeg, preForPoint);
                        if (!ReferenceEquals(def, defBase) && m.Score < defBase.MatchScoreThreshold)
                        {
                            var templ2 = GetTemplateGray(defBase.TemplateImageFile);
                            var retry = _matcher.MatchWithFixedRotation(matForPoint, defBase, templ2, templateAngleDeg, preForPoint);
                            if (retry.Score > m.Score)
                            {
                                m = retry;
                            }
                        }

                        basePos = m.Position;
                        matchRect = m.MatchRect;
                        score = m.Score;
                        thr = p.MatchScoreThreshold;
                        pass = score >= thr;
                    }

                    var off = new Point2d(p.OffsetPx.X, p.OffsetPx.Y);
                    var offRot = Rotate(off, new Point2d(0, 0), templateAngleDeg);
                    var pos = new Point2d(basePos.X + offRot.X, basePos.Y + offRot.Y);
                    return new PointMatchResult(p.Name, pos, matchRect, score, thr, pass, 0.0);
                }))
                .ToArray();

            var tPointsQueued = swTotal.ElapsedMilliseconds;

            var lineTasks = (config.Lines ?? new List<LineToolDefinition>())
                .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.Name) && l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
                .Select(l => Task.Run(() =>
                {
                    var roi = TransformRoiKeepSize(l.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForLine, _) = ResolveToolPreprocess("Line", l.Name);
                    var det = _lineDetector.DetectLongestLine(matForLine, roi, l.Canny1, l.Canny2, l.HoughThreshold, l.MinLineLength, l.MaxLineGap);
                    return det with { Name = l.Name };
                }))
                .ToArray();

            var tLinesQueued = swTotal.ElapsedMilliseconds;

            var blobTasks = (config.BlobDetections ?? new List<BlobDetectionDefinition>())
                .Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Name) && b.InspectRoi.Width > 0 && b.InspectRoi.Height > 0)
                .Select(b => Task.Run(() =>
                {
                    var roi = TransformRoiKeepSize(b.InspectRoi, originTeach, originFound, angleDeg);
                    var (matForBlob, _) = ResolveToolPreprocess("BlobDetection", b.Name);
                    var rois = b.Rois;
                    if (rois is not null && rois.Count > 0)
                    {
                        rois = rois
                            .Select(x => new BlobRoiDefinition { Mode = x.Mode, Roi = TransformRoiKeepSize(x.Roi, originTeach, originFound, angleDeg) })
                            .ToList();
                    }

                    var blobs = DetectBlobs(matForBlob, roi, rois, b.Polarity, b.Threshold, b.MinBlobArea, b.MaxBlobArea);
                    return new BlobDetectionResult(b.Name, blobs.Count, blobs);
                }))
                .ToArray();

            var tBlobsQueued = swTotal.ElapsedMilliseconds;

            var surfaceCompareTasks = (config.SurfaceCompares ?? new List<SurfaceCompareDefinition>())
                .Where(sc => sc is not null && !string.IsNullOrWhiteSpace(sc.Name) && sc.InspectRoi.Width > 0 && sc.InspectRoi.Height > 0)
                .Select(sc => Task.Run(() =>
                {
                    var (matForSc, _) = ResolveToolPreprocess("SurfaceCompare", sc.Name);
                    return RunSurfaceCompare(matForSc, originTeach, originFound, angleDeg, sc);
                }))
                .ToArray();

            var tScQueued = swTotal.ElapsedMilliseconds;

            var lpdTasks = (config.LinePairDetections ?? new List<LinePairDetectionDefinition>())
                .Where(lpd => lpd is not null && !string.IsNullOrWhiteSpace(lpd.Name) && lpd.SearchRoi.Width > 0 && lpd.SearchRoi.Height > 0)
                .Select(lpd => Task.Run(() =>
                {
                    var roi = TransformRoiKeepSize(lpd.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForLpd, _) = ResolveToolPreprocess("LinePairDetection", lpd.Name);
                    var top = _lineDetector.DetectTopLines(matForLpd, roi, lpd.Canny1, lpd.Canny2, lpd.HoughThreshold, lpd.MinLineLength, lpd.MaxLineGap, topN: 2);
                    if (top.Count < 2)
                    {
                        return new LinePairDetectionResult(
                            lpd.Name,
                            Found: false,
                            default, default, default, default,
                            double.NaN,
                            lpd.Nominal,
                            lpd.TolerancePlus,
                            lpd.ToleranceMinus,
                            Pass: false,
                            default,
                            default);
                    }

                    var l1 = top[0];
                    var l2 = top[1];
                    var (distPx, ca, cb) = Geometry2D.SegmentToSegmentDistance(l1.P1, l1.P2, l2.P1, l2.P2);
                    var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
                    var pass = value >= (lpd.Nominal - lpd.ToleranceMinus) && value <= (lpd.Nominal + lpd.TolerancePlus);

                    return new LinePairDetectionResult(
                        lpd.Name,
                        Found: true,
                        l1.P1, l1.P2,
                        l2.P1, l2.P2,
                        value,
                        lpd.Nominal,
                        lpd.TolerancePlus,
                        lpd.ToleranceMinus,
                        pass,
                        ca,
                        cb);
                }))
                .ToArray();

            var tLpdQueued = swTotal.ElapsedMilliseconds;

            var caliperTasks = (config.Calipers ?? new List<CaliperDefinition>())
                .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Name) && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
                .Select(c => Task.Run(() =>
                {
                    var roi = TransformRoiKeepSize(c.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForCal, _) = ResolveToolPreprocess("Caliper", c.Name);
                    return DetectCaliper(matForCal, roi, c);
                }))
                .ToArray();

            var tCalQueued = swTotal.ElapsedMilliseconds;

            Task.WaitAll(pointTasks);
            var tPointsDone = swTotal.ElapsedMilliseconds;

            Task.WaitAll(lineTasks);
            var tLinesDone = swTotal.ElapsedMilliseconds;

            Task.WaitAll(blobTasks);
            var tBlobsDone = swTotal.ElapsedMilliseconds;

            Task.WaitAll(surfaceCompareTasks);
            var tScDone = swTotal.ElapsedMilliseconds;

            Task.WaitAll(lpdTasks);
            var tLpdDone = swTotal.ElapsedMilliseconds;

            Task.WaitAll(caliperTasks);
            var tCalDone = swTotal.ElapsedMilliseconds;

            var tEpdQueued = swTotal.ElapsedMilliseconds;

            static EdgePairDetectResult DetectEdgePair(Mat matBgrOrGray, Roi roi, EdgePairDetectDefinition def, double pixelsPerMm)
            {
                if (matBgrOrGray is null || roi.Width <= 0 || roi.Height <= 0)
                {
                    return new EdgePairDetectResult(def.Name, Found: false, default, default, default, default, double.NaN, def.Nominal, def.TolerancePlus, def.ToleranceMinus, Pass: false, default, default,
                        new List<CaliperEdgePoint>(), new List<CaliperEdgePoint>());
                }

                var rect = new Rect(roi.X, roi.Y, roi.Width, roi.Height)
                    .Intersect(new Rect(0, 0, matBgrOrGray.Width, matBgrOrGray.Height));
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    return new EdgePairDetectResult(def.Name, Found: false, default, default, default, default, double.NaN, def.Nominal, def.TolerancePlus, def.ToleranceMinus, Pass: false, default, default,
                        new List<CaliperEdgePoint>(), new List<CaliperEdgePoint>());
                }

                using var crop = new Mat(matBgrOrGray, rect);
                Mat gray = crop;
                using var grayOwned = crop.Channels() == 1 ? null : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
                if (grayOwned is not null) gray = grayOwned;

                var stripCount = Math.Clamp(def.StripCount, 1, 200);
                var stripWidth = Math.Clamp(def.StripWidth, 1, Math.Max(1, Math.Min(rect.Width, rect.Height)));
                var stripLength = Math.Clamp(def.StripLength, 3, Math.Max(3, Math.Max(rect.Width, rect.Height)));
                var minSep = Math.Clamp(def.MinEdgeSeparationPx, 1, Math.Max(1, Math.Max(rect.Width, rect.Height)));

                var e1 = new List<CaliperEdgePoint>(stripCount);
                var e2 = new List<CaliperEdgePoint>(stripCount);

                static double InterpPeak(double a, double b, double c)
                {
                    var denom = (a - 2 * b + c);
                    if (Math.Abs(denom) < 1e-12) return 0.0;
                    return 0.5 * (a - c) / denom;
                }

                for (var i = 0; i < stripCount; i++)
                {
                    if (def.Orientation == CaliperOrientation.Vertical)
                    {
                        var xCenter = (i + 0.5) * rect.Width / stripCount;
                        var x0 = (int)Math.Round(xCenter - stripWidth / 2.0);
                        var y0 = (int)Math.Round((rect.Height - stripLength) / 2.0);
                        var sr = new Rect(x0, y0, stripWidth, stripLength)
                            .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                        if (sr.Width <= 0 || sr.Height <= 2) continue;

                        using var s = new Mat(gray, sr);
                        using var prof = new Mat();
                        Cv2.Reduce(s, prof, dim: ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_64F);
                        var n = prof.Rows;
                        if (n < 3) continue;

                        double Sm(int y)
                        {
                            if (y <= 0) return prof.Get<double>(0, 0);
                            if (y >= n - 1) return prof.Get<double>(n - 1, 0);
                            return (prof.Get<double>(y - 1, 0) + prof.Get<double>(y, 0) + prof.Get<double>(y + 1, 0)) / 3.0;
                        }

                        var candidates = new List<(int idx, double g)>(n);
                        var maxG = 0.0;
                        for (var y = 1; y < n - 1; y++)
                        {
                            var v0 = Sm(y - 1);
                            var v2 = Sm(y + 1);
                            var g = (v2 - v0) * 0.5;
                            if (def.Polarity == EdgePolarity.DarkToLight) { if (g <= 0) continue; }
                            else if (def.Polarity == EdgePolarity.LightToDark) { if (g >= 0) continue; g = -g; }
                            else { g = Math.Abs(g); }

                            if (g > maxG) maxG = g;
                            candidates.Add((y, g));
                        }

                        if (candidates.Count < 2) continue;

                        var effMin = Math.Max(0.0, Math.Min(def.MinEdgeStrength, maxG * 0.5));
                        candidates.Sort((a, b) => b.g.CompareTo(a.g));
                        if (candidates.Count > 40) candidates.RemoveRange(40, candidates.Count - 40);

                        var bestA = (-1, 0.0);
                        var bestB = (-1, 0.0);
                        var bestScore = double.NegativeInfinity;
                        for (var a = 0; a < candidates.Count; a++)
                        {
                            for (var b = a + 1; b < candidates.Count; b++)
                            {
                                var candA = candidates[a];
                                var candB = candidates[b];
                                if (Math.Abs(candA.idx - candB.idx) < minSep) continue;
                                var score = candA.g + candB.g;
                                if (score > bestScore)
                                {
                                    bestScore = score;
                                    bestA = candA;
                                    bestB = candB;
                                }
                            }
                        }

                        if (bestA.Item1 < 1 || bestB.Item1 < 1) continue;
                        if (bestA.Item2 < effMin || bestB.Item2 < effMin) continue;

                        // order by coordinate: edge1 smaller y, edge2 larger y
                        var idxA = bestA.Item1;
                        var idxB = bestB.Item1;
                        var valA = bestA.Item2;
                        var valB = bestB.Item2;
                        if (idxA > idxB)
                        {
                            (idxA, idxB) = (idxB, idxA);
                            (valA, valB) = (valB, valA);
                        }

                        double SubAt(int idx)
                        {
                            var gL = Math.Abs(prof.Get<double>(idx, 0) - prof.Get<double>(idx - 1, 0));
                            var gC = Math.Abs(prof.Get<double>(idx + 1, 0) - prof.Get<double>(idx - 1, 0)) * 0.5;
                            var gR = Math.Abs(prof.Get<double>(idx + 1, 0) - prof.Get<double>(idx, 0));
                            return InterpPeak(gL, gC, gR);
                        }

                        var ySubA = idxA + SubAt(idxA);
                        var ySubB = idxB + SubAt(idxB);
                        var xGlobal = rect.X + sr.X + sr.Width / 2.0;
                        e1.Add(new CaliperEdgePoint(xGlobal, rect.Y + sr.Y + ySubA, valA));
                        e2.Add(new CaliperEdgePoint(xGlobal, rect.Y + sr.Y + ySubB, valB));
                    }
                    else
                    {
                        var yCenter = (i + 0.5) * rect.Height / stripCount;
                        var y0 = (int)Math.Round(yCenter - stripWidth / 2.0);
                        var x0 = (int)Math.Round((rect.Width - stripLength) / 2.0);
                        var sr = new Rect(x0, y0, stripLength, stripWidth)
                            .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                        if (sr.Width <= 2 || sr.Height <= 0) continue;

                        using var s = new Mat(gray, sr);
                        using var prof = new Mat();
                        Cv2.Reduce(s, prof, dim: ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_64F);
                        var n = prof.Cols;
                        if (n < 3) continue;

                        double Sm(int x)
                        {
                            if (x <= 0) return prof.Get<double>(0, 0);
                            if (x >= n - 1) return prof.Get<double>(0, n - 1);
                            return (prof.Get<double>(0, x - 1) + prof.Get<double>(0, x) + prof.Get<double>(0, x + 1)) / 3.0;
                        }

                        var candidates = new List<(int idx, double g)>(n);
                        var maxG = 0.0;
                        for (var x = 1; x < n - 1; x++)
                        {
                            var v0 = Sm(x - 1);
                            var v2 = Sm(x + 1);
                            var g = (v2 - v0) * 0.5;
                            if (def.Polarity == EdgePolarity.DarkToLight) { if (g <= 0) continue; }
                            else if (def.Polarity == EdgePolarity.LightToDark) { if (g >= 0) continue; g = -g; }
                            else { g = Math.Abs(g); }

                            if (g > maxG) maxG = g;
                            candidates.Add((x, g));
                        }

                        if (candidates.Count < 2) continue;

                        var effMin = Math.Max(0.0, Math.Min(def.MinEdgeStrength, maxG * 0.5));
                        candidates.Sort((a, b) => b.g.CompareTo(a.g));
                        if (candidates.Count > 40) candidates.RemoveRange(40, candidates.Count - 40);

                        var bestA = (-1, 0.0);
                        var bestB = (-1, 0.0);
                        var bestScore = double.NegativeInfinity;
                        for (var a = 0; a < candidates.Count; a++)
                        {
                            for (var b = a + 1; b < candidates.Count; b++)
                            {
                                var candA = candidates[a];
                                var candB = candidates[b];
                                if (Math.Abs(candA.idx - candB.idx) < minSep) continue;
                                var score = candA.g + candB.g;
                                if (score > bestScore)
                                {
                                    bestScore = score;
                                    bestA = candA;
                                    bestB = candB;
                                }
                            }
                        }

                        if (bestA.Item1 < 1 || bestB.Item1 < 1) continue;
                        if (bestA.Item2 < effMin || bestB.Item2 < effMin) continue;

                        var idxA = bestA.Item1;
                        var idxB = bestB.Item1;
                        var valA = bestA.Item2;
                        var valB = bestB.Item2;
                        if (idxA > idxB)
                        {
                            (idxA, idxB) = (idxB, idxA);
                            (valA, valB) = (valB, valA);
                        }

                        double SubAt(int idx)
                        {
                            var gL = Math.Abs(prof.Get<double>(0, idx) - prof.Get<double>(0, idx - 1));
                            var gC = Math.Abs(prof.Get<double>(0, idx + 1) - prof.Get<double>(0, idx - 1)) * 0.5;
                            var gR = Math.Abs(prof.Get<double>(0, idx + 1) - prof.Get<double>(0, idx));
                            return InterpPeak(gL, gC, gR);
                        }

                        var xSubA = idxA + SubAt(idxA);
                        var xSubB = idxB + SubAt(idxB);
                        var yGlobal = rect.Y + sr.Y + sr.Height / 2.0;
                        e1.Add(new CaliperEdgePoint(rect.X + sr.X + xSubA, yGlobal, valA));
                        e2.Add(new CaliperEdgePoint(rect.X + sr.X + xSubB, yGlobal, valB));
                    }
                }

                if (e1.Count < 2 || e2.Count < 2)
                {
                    return new EdgePairDetectResult(def.Name, Found: false, default, default, default, default, double.NaN, def.Nominal, def.TolerancePlus, def.ToleranceMinus, Pass: false, default, default, e1, e2);
                }

                static (Point2d p1, Point2d p2) FitLineFromPoints(List<CaliperEdgePoint> pts)
                {
                    var meanX = pts.Average(p => p.X);
                    var meanY = pts.Average(p => p.Y);
                    var sxx = 0.0;
                    var syy = 0.0;
                    var sxy = 0.0;
                    foreach (var p in pts)
                    {
                        var dx = p.X - meanX;
                        var dy = p.Y - meanY;
                        sxx += dx * dx;
                        syy += dy * dy;
                        sxy += dx * dy;
                    }
                    var theta = 0.5 * Math.Atan2(2 * sxy, (sxx - syy));
                    var dir = new Point2d(Math.Cos(theta), Math.Sin(theta));
                    var minT = double.PositiveInfinity;
                    var maxT = double.NegativeInfinity;
                    foreach (var p in pts)
                    {
                        var t = (p.X - meanX) * dir.X + (p.Y - meanY) * dir.Y;
                        if (t < minT) minT = t;
                        if (t > maxT) maxT = t;
                    }
                    return (new Point2d(meanX + minT * dir.X, meanY + minT * dir.Y), new Point2d(meanX + maxT * dir.X, meanY + maxT * dir.Y));
                }

                var (l1p1, l1p2) = FitLineFromPoints(e1);
                var (l2p1, l2p2) = FitLineFromPoints(e2);
                var (distPx, ca, cb) = Geometry2D.SegmentToSegmentDistance(l1p1, l1p2, l2p1, l2p2);
                var value = pixelsPerMm > 0 ? distPx / pixelsPerMm : distPx;
                var pass = value >= (def.Nominal - def.ToleranceMinus) && value <= (def.Nominal + def.TolerancePlus);

                return new EdgePairDetectResult(def.Name, Found: true, l1p1, l1p2, l2p1, l2p2, value, def.Nominal, def.TolerancePlus, def.ToleranceMinus, pass, ca, cb, e1, e2);
            }

            var epdTasks = (config.EdgePairDetections ?? new List<EdgePairDetectDefinition>())
                .Where(epd => epd is not null && !string.IsNullOrWhiteSpace(epd.Name) && epd.SearchRoi.Width > 0 && epd.SearchRoi.Height > 0)
                .Select(epd => Task.Run(() =>
                {
                    var roi = TransformRoiKeepSize(epd.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForEpd, _) = ResolveToolPreprocess("EdgePairDetect", epd.Name);
                    return DetectEdgePair(matForEpd, roi, epd, config.PixelsPerMm);
                }))
                .ToArray();

            static CircleFinderResult DetectCircle(Mat matBgrOrGray, Roi roi, CircleFinderDefinition def)
            {
                var name = def.Name ?? string.Empty;
                if (matBgrOrGray is null || roi.Width <= 0 || roi.Height <= 0)
                {
                    return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                }

                var rect = new Rect(roi.X, roi.Y, roi.Width, roi.Height)
                    .Intersect(new Rect(0, 0, matBgrOrGray.Width, matBgrOrGray.Height));
                if (rect.Width <= 2 || rect.Height <= 2)
                {
                    return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                }

                using var crop = new Mat(matBgrOrGray, rect);
                Mat gray = crop;
                using var grayOwned = crop.Channels() == 1 ? null : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
                if (grayOwned is not null) gray = grayOwned;

                var minR = Math.Max(0, def.MinRadiusPx);
                var maxR = Math.Max(0, def.MaxRadiusPx);

                if (def.Algorithm == CircleFindAlgorithm.HoughCircles)
                {
                    using var blur = new Mat();
                    Cv2.GaussianBlur(gray, blur, new Size(0, 0), 1.2);
                    var dp = Math.Max(1.0, def.HoughDp);
                    var minDist = Math.Max(1.0, def.HoughMinDistPx);
                    var p1 = Math.Max(1.0, def.HoughParam1);
                    var p2 = Math.Max(1.0, def.HoughParam2);
                    var circles = Cv2.HoughCircles(blur, HoughModes.Gradient, dp, minDist, p1, p2, minR, maxR);
                    if (circles is null || circles.Length == 0)
                    {
                        return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                    }

                    var best = circles.OrderByDescending(c => c.Radius).First();
                    var center = new Point2d(rect.X + best.Center.X, rect.Y + best.Center.Y);
                    return new CircleFinderResult(name, Found: true, center, best.Radius, Score: 1.0);
                }

                if (def.Algorithm == CircleFindAlgorithm.ContourFit)
                {
                    using var edges = new Mat();
                    Cv2.Canny(gray, edges, def.Canny1, def.Canny2);
                    Cv2.FindContours(edges, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                    var bestScore = double.NegativeInfinity;
                    var bestCenter = new Point2d();
                    var bestR = 0.0;

                    foreach (var cnt in contours)
                    {
                        if (cnt is null || cnt.Length < 20) continue;
                        var area = Math.Abs(Cv2.ContourArea(cnt));
                        if (area <= 1.0) continue;

                        var peri = Cv2.ArcLength(cnt, closed: true);
                        if (peri <= 1e-9) continue;
                        var circ = 4.0 * Math.PI * area / (peri * peri);
                        if (circ < def.MinCircularity) continue;

                        Cv2.MinEnclosingCircle(cnt, out var c, out var r);
                        if (minR > 0 && r < minR) continue;
                        if (maxR > 0 && r > maxR) continue;

                        var score = circ * Math.Sqrt(area);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestCenter = new Point2d(rect.X + c.X, rect.Y + c.Y);
                            bestR = r;
                        }
                    }

                    if (bestScore <= double.NegativeInfinity)
                    {
                        return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                    }

                    return new CircleFinderResult(name, Found: true, bestCenter, bestR, Score: bestScore);
                }

                // RANSAC (simple): reuse contour edges as points (if any). If none, fail.
                {
                    using var edges = new Mat();
                    Cv2.Canny(gray, edges, def.Canny1, def.Canny2);
                    var pts = new List<Point2f>();
                    for (var y = 0; y < edges.Rows; y++)
                    {
                        for (var x = 0; x < edges.Cols; x++)
                        {
                            if (edges.Get<byte>(y, x) != 0) pts.Add(new Point2f(x, y));
                        }
                    }

                    if (pts.Count < 50)
                    {
                        return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                    }

                    var rnd = new Random(def.Name?.GetHashCode() ?? 0);
                    var bestInliers = -1;
                    var bestCenter = new Point2d();
                    var bestR = 0.0;
                    var thresh = 2.5;
                    var iters = 80;

                    static bool TryCircleFrom3(Point2f a, Point2f b, Point2f c, out Point2d center, out double r)
                    {
                        center = default;
                        r = 0;
                        var ax = a.X; var ay = a.Y;
                        var bx = b.X; var by = b.Y;
                        var cx = c.X; var cy = c.Y;
                        var d = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
                        if (Math.Abs(d) < 1e-9) return false;
                        var ax2ay2 = ax * ax + ay * ay;
                        var bx2by2 = bx * bx + by * by;
                        var cx2cy2 = cx * cx + cy * cy;
                        var ux = (ax2ay2 * (by - cy) + bx2by2 * (cy - ay) + cx2cy2 * (ay - by)) / d;
                        var uy = (ax2ay2 * (cx - bx) + bx2by2 * (ax - cx) + cx2cy2 * (bx - ax)) / d;
                        center = new Point2d(ux, uy);
                        r = Math.Sqrt((ux - ax) * (ux - ax) + (uy - ay) * (uy - ay));
                        return double.IsFinite(r) && r > 0;
                    }

                    for (var k = 0; k < iters; k++)
                    {
                        var ia = rnd.Next(pts.Count);
                        var ib = rnd.Next(pts.Count);
                        var ic = rnd.Next(pts.Count);
                        if (ia == ib || ia == ic || ib == ic) continue;

                        if (!TryCircleFrom3(pts[ia], pts[ib], pts[ic], out var c0, out var r0)) continue;
                        if (minR > 0 && r0 < minR) continue;
                        if (maxR > 0 && r0 > maxR) continue;

                        var inl = 0;
                        for (var i = 0; i < pts.Count; i += 2)
                        {
                            var p = pts[i];
                            var dx = p.X - c0.X;
                            var dy = p.Y - c0.Y;
                            var d0 = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - r0);
                            if (d0 <= thresh) inl++;
                        }

                        if (inl > bestInliers)
                        {
                            bestInliers = inl;
                            bestCenter = c0;
                            bestR = r0;
                        }
                    }

                    if (bestInliers < 0)
                    {
                        return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                    }

                    var center = new Point2d(rect.X + bestCenter.X, rect.Y + bestCenter.Y);
                    return new CircleFinderResult(name, Found: true, center, bestR, Score: bestInliers);
                }
            }

            var circleTasks = (config.CircleFinders ?? new List<CircleFinderDefinition>())
                .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Name) && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
                .Select(c => Task.Run(() =>
                {
                    var roi = TransformRoiKeepSize(c.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForCircle, _) = ResolveToolPreprocess("CircleFinder", c.Name);
                    return DetectCircle(matForCircle, roi, c);
                }))
                .ToArray();

            var tEpdDone = swTotal.ElapsedMilliseconds;

            result.Timings.PointsMs = (int)Math.Max(0, tPointsDone - tPointsQueued);
            result.Timings.LinesMs = (int)Math.Max(0, tLinesDone - tLinesQueued);
            result.Timings.BlobsMs = (int)Math.Max(0, tBlobsDone - tBlobsQueued);
            result.Timings.SurfaceCompareMs = (int)Math.Max(0, tScDone - tScQueued);
            result.Timings.LpdMs = (int)Math.Max(0, tLpdDone - tLpdQueued);
            result.Timings.CalipersMs = (int)Math.Max(0, tCalDone - tCalQueued);

            var foundPoints = new Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in pointTasks)
            {
                var pr = t.Result;
                result.Points.Add(pr);
                foundPoints[pr.Name] = pr.Position;
            }

            var foundLines = new Dictionary<string, LineDetectResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in lineTasks)
            {
                var lr = t.Result;
                result.Lines.Add(lr);
                foundLines[lr.Name] = lr;
            }

            foreach (var t in blobTasks)
            {
                result.BlobDetections.Add(t.Result);
            }

            foreach (var t in surfaceCompareTasks)
            {
                result.SurfaceCompares.Add(t.Result);
            }

            foreach (var t in lpdTasks)
            {
                result.LinePairDetections.Add(t.Result);
            }

            foreach (var t in caliperTasks)
            {
                result.Calipers.Add(t.Result);
            }

            Task.WaitAll(epdTasks);
            foreach (var t in epdTasks)
            {
                result.EdgePairDetections.Add(t.Result);
            }

            Task.WaitAll(circleTasks);
            foreach (var t in circleTasks)
            {
                result.CircleFinders.Add(t.Result);
            }

            result.Timings.EdgePairDetectMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tEpdQueued);

            foreach (var cal in result.Calipers)
            {
                if (!cal.Found)
                {
                    continue;
                }

                var dx = cal.LineP2.X - cal.LineP1.X;
                var dy = cal.LineP2.Y - cal.LineP1.Y;
                var len = Math.Sqrt(dx * dx + dy * dy);
                foundLines[cal.Name] = new LineDetectResult(cal.Name, cal.LineP1, cal.LineP2, len, Found: true);
            }

            var foundCircles = new Dictionary<string, CircleFinderResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in result.CircleFinders)
            {
                foundCircles[c.Name] = c;
            }

            foreach (var d in (config.Diameters ?? new List<DiameterDefinition>()))
            {
                if (d is null || string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.CircleRef))
                {
                    continue;
                }

                if (!foundCircles.TryGetValue(d.CircleRef, out var c) || !c.Found)
                {
                    result.Diameters.Add(new DiameterResult(d.Name, d.CircleRef, Found: false, double.NaN, d.Nominal, d.TolerancePlus, d.ToleranceMinus, Pass: false, default, 0.0));
                    continue;
                }

                var diameterPx = 2.0 * c.RadiusPx;
                var value = config.PixelsPerMm > 0 ? diameterPx / config.PixelsPerMm : diameterPx;
                var pass = value >= (d.Nominal - d.ToleranceMinus) && value <= (d.Nominal + d.TolerancePlus);
                result.Diameters.Add(new DiameterResult(d.Name, d.CircleRef, Found: true, value, d.Nominal, d.TolerancePlus, d.ToleranceMinus, pass, c.Center, c.RadiusPx));
            }

            var tEdgePairs0 = swTotal.ElapsedMilliseconds;
            foreach (var ep in config.EdgePairs)
            {
                if (string.IsNullOrWhiteSpace(ep.Name) || string.IsNullOrWhiteSpace(ep.RefA) || string.IsNullOrWhiteSpace(ep.RefB))
                {
                    continue;
                }

                if (!foundLines.TryGetValue(ep.RefA, out var la) || !foundLines.TryGetValue(ep.RefB, out var lb) || !la.Found || !lb.Found)
                {
                    result.EdgePairs.Add(new EdgePairResult(
                        ep.Name,
                        ep.RefA,
                        ep.RefB,
                        Found: false,
                        default, default, default, default,
                        double.NaN,
                        ep.Nominal,
                        ep.TolerancePlus,
                        ep.ToleranceMinus,
                        Pass: false,
                        default,
                        default));
                    continue;
                }

                var (distPx, ca, cb) = Geometry2D.SegmentToSegmentDistance(la.P1, la.P2, lb.P1, lb.P2);
                var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
                var pass = value >= (ep.Nominal - ep.ToleranceMinus) && value <= (ep.Nominal + ep.TolerancePlus);

                result.EdgePairs.Add(new EdgePairResult(
                    ep.Name,
                    ep.RefA,
                    ep.RefB,
                    Found: true,
                    la.P1, la.P2,
                    lb.P1, lb.P2,
                    value,
                    ep.Nominal,
                    ep.TolerancePlus,
                    ep.ToleranceMinus,
                    pass,
                    ca,
                    cb));
            }
            result.Timings.EdgePairsMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tEdgePairs0);

            static bool TryIntersectInfiniteLines(LineDetectResult a, LineDetectResult b, out Point2d inter)
            {
                var ax = a.P2.X - a.P1.X;
                var ay = a.P2.Y - a.P1.Y;
                var bx = b.P2.X - b.P1.X;
                var by = b.P2.Y - b.P1.Y;
                var denom = ax * by - ay * bx;
                if (Math.Abs(denom) < 1e-9)
                {
                    inter = default;
                    return false;
                }

                var cx = b.P1.X - a.P1.X;
                var cy = b.P1.Y - a.P1.Y;
                var t = (cx * by - cy * bx) / denom;
                inter = new Point2d(a.P1.X + t * ax, a.P1.Y + t * ay);
                return double.IsFinite(inter.X) && double.IsFinite(inter.Y);
            }

            var tAngles0 = swTotal.ElapsedMilliseconds;
            foreach (var a in config.Angles)
            {
                if (string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.LineA) || string.IsNullOrWhiteSpace(a.LineB))
                {
                    continue;
                }

                if (!foundLines.TryGetValue(a.LineA, out var la) || !foundLines.TryGetValue(a.LineB, out var lb) || !la.Found || !lb.Found)
                {
                    result.Angles.Add(new AngleResult(a.Name, a.LineA, a.LineB, double.NaN, a.Nominal, a.TolerancePlus, a.ToleranceMinus, Pass: false, Found: false, default, default, default));
                    continue;
                }

                var v1 = new Point2d(la.P2.X - la.P1.X, la.P2.Y - la.P1.Y);
                var v2 = new Point2d(lb.P2.X - lb.P1.X, lb.P2.Y - lb.P1.Y);
                var n1 = Math.Sqrt(v1.X * v1.X + v1.Y * v1.Y);
                var n2 = Math.Sqrt(v2.X * v2.X + v2.Y * v2.Y);
                if (n1 < 1e-9 || n2 < 1e-9)
                {
                    result.Angles.Add(new AngleResult(a.Name, a.LineA, a.LineB, double.NaN, a.Nominal, a.TolerancePlus, a.ToleranceMinus, Pass: false, Found: false, default, default, default));
                    continue;
                }

                var dot = (v1.X * v2.X + v1.Y * v2.Y) / (n1 * n2);
                dot = Math.Clamp(dot, -1.0, 1.0);
                var angle = Math.Acos(dot) * 180.0 / Math.PI;

                var pass = angle >= (a.Nominal - a.ToleranceMinus) && angle <= (a.Nominal + a.TolerancePlus);
                var found = TryIntersectInfiniteLines(la, lb, out var inter);
                result.Angles.Add(new AngleResult(a.Name, a.LineA, a.LineB, angle, a.Nominal, a.TolerancePlus, a.ToleranceMinus, pass, found, inter, new Point2d(v1.X / n1, v1.Y / n1), new Point2d(v2.X / n2, v2.Y / n2)));
            }
            result.Timings.AnglesMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tAngles0);

            var tDistances0 = swTotal.ElapsedMilliseconds;
            var distanceAnchors = new Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in foundPoints)
            {
                distanceAnchors[kv.Key] = kv.Value;
            }

            foreach (var c in result.CircleFinders)
            {
                if (c is not null && c.Found)
                {
                    distanceAnchors[c.Name] = c.Center;
                }
            }

            foreach (var d in result.Diameters)
            {
                if (d is not null && d.Found)
                {
                    distanceAnchors[d.Name] = d.Center;
                }
            }

            foreach (var d in config.Distances)
            {
                if (!distanceAnchors.TryGetValue(d.PointA, out var a) || !distanceAnchors.TryGetValue(d.PointB, out var b))
                {
                    result.Distances.Add(new DistanceCheckResult(d.Name, d.PointA, d.PointB, double.NaN, d.Nominal, d.TolerancePlus, d.ToleranceMinus, false));
                    continue;
                }

                result.Distances.Add(_distanceCalculator.CheckDistance(d, a, b, config.PixelsPerMm));
            }

            foreach (var dd in config.LineToLineDistances)
            {
                if (!foundLines.TryGetValue(dd.LineA, out var la) || !foundLines.TryGetValue(dd.LineB, out var lb) || !la.Found || !lb.Found)
                {
                    result.LineToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.LineA, dd.LineB, double.NaN, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, false, default, default));
                    continue;
                }

                var (distPx, ca, cb) = CalculateLineLineDistance(la, lb, dd.Mode);
                var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
                var pass = value >= (dd.Nominal - dd.ToleranceMinus) && value <= (dd.Nominal + dd.TolerancePlus);
                result.LineToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.LineA, dd.LineB, value, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, pass, ca, cb));
            }

            foreach (var dd in config.PointToLineDistances)
            {
                if (!foundPoints.TryGetValue(dd.Point, out var p) || !foundLines.TryGetValue(dd.Line, out var l) || !l.Found)
                {
                    result.PointToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.Point, dd.Line, double.NaN, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, false, default, default));
                    continue;
                }

                var (distPx, closest) = CalculatePointLineDistance(p, l, dd.Mode);
                var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
                var pass = value >= (dd.Nominal - dd.ToleranceMinus) && value <= (dd.Nominal + dd.TolerancePlus);
                result.PointToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.Point, dd.Line, value, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, pass, p, closest));
            }
            result.Timings.DistancesMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tDistances0);

            var tCdt0 = swTotal.ElapsedMilliseconds;
            static BarcodeFormat[] ResolveFormats(List<CodeSymbology> sym)
            {
                if (sym is null || sym.Count == 0)
                {
                    return Array.Empty<BarcodeFormat>();
                }

                var fmts = new HashSet<BarcodeFormat>();
                foreach (var s in sym)
                {
                    switch (s)
                    {
                        case CodeSymbology.Qr:
                            fmts.Add(BarcodeFormat.QR_CODE);
                            break;
                        case CodeSymbology.DataMatrix:
                            fmts.Add(BarcodeFormat.DATA_MATRIX);
                            break;
                        case CodeSymbology.Pdf417:
                            fmts.Add(BarcodeFormat.PDF_417);
                            break;
                        case CodeSymbology.Aztec:
                            fmts.Add(BarcodeFormat.AZTEC);
                            break;
                        case CodeSymbology.Barcode1D:
                            fmts.Add(BarcodeFormat.CODE_128);
                            fmts.Add(BarcodeFormat.CODE_39);
                            fmts.Add(BarcodeFormat.CODE_93);
                            fmts.Add(BarcodeFormat.EAN_13);
                            fmts.Add(BarcodeFormat.EAN_8);
                            fmts.Add(BarcodeFormat.UPC_A);
                            fmts.Add(BarcodeFormat.UPC_E);
                            fmts.Add(BarcodeFormat.ITF);
                            fmts.Add(BarcodeFormat.CODABAR);
                            break;
                        default:
                            break;
                    }
                }

                return fmts.ToArray();
            }

            foreach (var cdt in config.CodeDetections)
            {
                if (string.IsNullOrWhiteSpace(cdt.Name) || cdt.SearchRoi.Width <= 0 || cdt.SearchRoi.Height <= 0)
                {
                    continue;
                }

                var roi = TransformRoiKeepSize(cdt.SearchRoi, originTeach, originFound, angleDeg);
                var (matForCode, _) = ResolveToolPreprocess("CodeDetection", cdt.Name);

                var rect = new Rect(roi.X, roi.Y, roi.Width, roi.Height)
                    .Intersect(new Rect(0, 0, matForCode.Width, matForCode.Height));

                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    result.CodeDetections.Add(new CodeDetectionResult(cdt.Name, Found: false, Text: string.Empty, BoundingBox: default));
                    continue;
                }

                using var crop = new Mat(matForCode, rect);
                Mat gray0;
                if (crop.Channels() == 1)
                {
                    gray0 = crop;
                }
                else
                {
                    gray0 = crop.CvtColor(ColorConversionCodes.BGR2GRAY);
                    matsToDispose.Add(gray0);
                }

                var options = new DecodingOptions
                {
                    TryHarder = cdt.TryHarder,
                    PossibleFormats = ResolveFormats(cdt.Symbologies).ToList()
                };

                var reader = new BarcodeReaderGeneric
                {
                    AutoRotate = true,
                    Options = options
                };

                options.TryInverted = true;

                // Convert gray Mat to byte[] and decode via LuminanceSource (no System.Drawing dependency).
                var gray = gray0.IsContinuous() ? gray0 : gray0.Clone();
                if (!ReferenceEquals(gray, gray0))
                {
                    matsToDispose.Add(gray);
                }

                var w0 = gray.Cols;
                var h0 = gray.Rows;
                var buf = new byte[w0 * h0];
                Marshal.Copy(gray.Data, buf, 0, buf.Length);
                var source = new RGBLuminanceSource(buf, w0, h0, RGBLuminanceSource.BitmapFormat.Gray8);

                var decoded = reader.Decode(source);
                if (decoded is null || string.IsNullOrWhiteSpace(decoded.Text))
                {
                    result.CodeDetections.Add(new CodeDetectionResult(cdt.Name, Found: false, Text: string.Empty, BoundingBox: default));
                    continue;
                }

                // Bounding box: ROI rect by default. If ZXing returns points, compute a tighter box.
                var bb = rect;
                if (decoded.ResultPoints is not null && decoded.ResultPoints.Length > 0)
                {
                    var xs = decoded.ResultPoints.Select(p => p.X).ToArray();
                    var ys = decoded.ResultPoints.Select(p => p.Y).ToArray();
                    var minX = xs.Min();
                    var maxX = xs.Max();
                    var minY = ys.Min();
                    var maxY = ys.Max();
                    var w = Math.Max(1, maxX - minX);
                    var h = Math.Max(1, maxY - minY);
                    bb = new Rect(
                        rect.X + (int)Math.Round(minX),
                        rect.Y + (int)Math.Round(minY),
                        (int)Math.Round(w),
                        (int)Math.Round(h));
                }

                result.CodeDetections.Add(new CodeDetectionResult(cdt.Name, Found: true, Text: decoded.Text, BoundingBox: bb));
            }
            result.Timings.CdtMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tCdt0);

            var tCond0 = swTotal.ElapsedMilliseconds;
            EvaluateConditions(config, result);
            result.Timings.ConditionsMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tCond0);

            var tDef0 = swTotal.ElapsedMilliseconds;
            var defectConfig = TransformDefectConfig(config.DefectConfig, originTeach, originFound, angleDeg);
            // Defects remain on default preprocess for now (backward compatible).
            result.Defects = _defectDetector.Detect(GetProcessedDefault(), defectConfig);
            result.Timings.DefectsMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tDef0);

            result.Pass = originPass
            && result.Points.All(x => x.Pass)
            && result.Distances.All(x => x.Pass)
            && result.LineToLineDistances.All(x => x.Pass)
            && result.PointToLineDistances.All(x => x.Pass)
            && result.Conditions.All(x => x.Pass)
            && (result.Defects.Defects.Count == 0);

            if (originPass)
            {
                track.LastOriginPos = originMatch.Position;
                track.LastAngleDeg = poseAngleDeg;

                foreach (var pr in result.Points)
                {
                    if (pr.Pass)
                    {
                        track.LastPointPos[pr.Name] = pr.Position;
                    }
                }
            }

            result.Timings.TotalMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds);

            return result;
        }
        finally
        {
            foreach (var m in matsToDispose)
            {
                m.Dispose();
            }
        }
    }

    private static void EvaluateConditions(VisionConfig config, InspectionResult result)
    {
        if (config.Conditions is null || config.Conditions.Count == 0)
        {
            return;
        }

        var vars = ConditionEvaluator.BuildVariableMap(result);
        foreach (var c in config.Conditions)
        {
            if (string.IsNullOrWhiteSpace(c.Name))
            {
                continue;
            }

            var expr = c.Expression ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expr))
            {
                result.Conditions.Add(new ConditionResult(c.Name, expr, false, "Empty expression"));
                continue;
            }

            try
            {
                var ok = ConditionEvaluator.Evaluate(expr, vars);
                result.Conditions.Add(new ConditionResult(c.Name, expr, ok, null));
            }
            catch (Exception ex)
            {
                result.Conditions.Add(new ConditionResult(c.Name, expr, false, ex.Message));
            }
        }
    }

    private static (double DistPx, Point2d A, Point2d B) CalculateLineLineDistance(LineDetectResult la, LineDetectResult lb, LineLineDistanceMode mode)
    {
        // Default / legacy
        if (mode == LineLineDistanceMode.ClosestPointsOnSegments)
        {
            return Geometry2D.SegmentToSegmentDistance(la.P1, la.P2, lb.P1, lb.P2);
        }

        if (mode == LineLineDistanceMode.ExtendToOtherEndpoints)
        {
            var (ea1, ea2) = ExtendSegmentToCoverOtherEndpoints(la.P1, la.P2, lb.P1, lb.P2);
            var (eb1, eb2) = ExtendSegmentToCoverOtherEndpoints(lb.P1, lb.P2, la.P1, la.P2);
            return Geometry2D.SegmentToSegmentDistance(ea1, ea2, eb1, eb2);
        }

        if (mode == LineLineDistanceMode.MidpointToMidpoint)
        {
            var ma = new Point2d((la.P1.X + la.P2.X) * 0.5, (la.P1.Y + la.P2.Y) * 0.5);
            var mb = new Point2d((lb.P1.X + lb.P2.X) * 0.5, (lb.P1.Y + lb.P2.Y) * 0.5);
            return (Geometry2D.Distance(ma, mb), ma, mb);
        }

        // Endpoints based
        var aEnds = new[] { la.P1, la.P2 };
        var bEnds = new[] { lb.P1, lb.P2 };

        var bestDist = mode == LineLineDistanceMode.FarthestEndpoints ? double.NegativeInfinity : double.PositiveInfinity;
        var bestA = la.P1;
        var bestB = lb.P1;

        foreach (var a in aEnds)
        {
            foreach (var b in bEnds)
            {
                var d = Geometry2D.Distance(a, b);
                if (mode == LineLineDistanceMode.NearestEndpoints)
                {
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestA = a;
                        bestB = b;
                    }
                }
                else if (mode == LineLineDistanceMode.FarthestEndpoints)
                {
                    if (d > bestDist)
                    {
                        bestDist = d;
                        bestA = a;
                        bestB = b;
                    }
                }
            }
        }

        return (bestDist, bestA, bestB);
    }

    private static (Point2d P1, Point2d P2) ExtendSegmentToCoverOtherEndpoints(Point2d s1, Point2d s2, Point2d o1, Point2d o2)
    {
        var d = s2 - s1;
        var len2 = d.X * d.X + d.Y * d.Y;
        if (len2 <= 1e-12)
        {
            return (s1, s2);
        }

        // Param along the segment's infinite line: p(t)=s1 + t*d, original endpoints are t=0 and t=1.
        var tO1 = ((o1.X - s1.X) * d.X + (o1.Y - s1.Y) * d.Y) / len2;
        var tO2 = ((o2.X - s1.X) * d.X + (o2.Y - s1.Y) * d.Y) / len2;

        var tMin = Math.Min(0.0, Math.Min(tO1, tO2));
        var tMax = Math.Max(1.0, Math.Max(tO1, tO2));

        var p1 = new Point2d(s1.X + tMin * d.X, s1.Y + tMin * d.Y);
        var p2 = new Point2d(s1.X + tMax * d.X, s1.Y + tMax * d.Y);
        return (p1, p2);
    }

    private static (double DistPx, Point2d Closest) CalculatePointLineDistance(Point2d p, LineDetectResult l, PointLineDistanceMode mode)
    {
        if (mode == PointLineDistanceMode.PointToInfiniteLine)
        {
            var a = l.P1;
            var b = l.P2;
            var ab = b - a;
            var ap = p - a;
            var ab2 = ab.X * ab.X + ab.Y * ab.Y;
            if (ab2 <= 1e-12)
            {
                return (Geometry2D.Distance(p, a), a);
            }

            var t = (ap.X * ab.X + ap.Y * ab.Y) / ab2;
            var proj = new Point2d(a.X + t * ab.X, a.Y + t * ab.Y);
            return (Geometry2D.Distance(p, proj), proj);
        }

        // Default / legacy
        return Geometry2D.PointToSegmentDistance(p, l.P1, l.P2);
    }

    private static Point2d Rotate(Point2d p, Point2d origin, double angleDeg)
    {
        if (Math.Abs(angleDeg) < 0.000001)
        {
            return p;
        }

        var a = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(a);
        var sin = Math.Sin(a);

        var dx = p.X - origin.X;
        var dy = p.Y - origin.Y;
        var x = dx * cos - dy * sin;
        var y = dx * sin + dy * cos;
        return new Point2d(x + origin.X, y + origin.Y);
    }

    private static Roi TransformRoi(Roi roi, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return new Roi();
        }

        var p1 = new Point2d(roi.X, roi.Y);
        var p2 = new Point2d(roi.X + roi.Width, roi.Y);
        var p3 = new Point2d(roi.X + roi.Width, roi.Y + roi.Height);
        var p4 = new Point2d(roi.X, roi.Y + roi.Height);

        p1 = Rotate(p1, originTeach, angleDeg);
        p2 = Rotate(p2, originTeach, angleDeg);
        p3 = Rotate(p3, originTeach, angleDeg);
        p4 = Rotate(p4, originTeach, angleDeg);

        var dx = originFound.X - originTeach.X;
        var dy = originFound.Y - originTeach.Y;

        p1 = new Point2d(p1.X + dx, p1.Y + dy);
        p2 = new Point2d(p2.X + dx, p2.Y + dy);
        p3 = new Point2d(p3.X + dx, p3.Y + dy);
        p4 = new Point2d(p4.X + dx, p4.Y + dy);

        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

        return new Roi
        {
            X = (int)Math.Round(minX),
            Y = (int)Math.Round(minY),
            Width = (int)Math.Round(maxX - minX),
            Height = (int)Math.Round(maxY - minY)
        };
    }

    private static Roi TransformRoiKeepSize(Roi roi, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return new Roi();
        }

        var centerTeach = new Point2d(roi.X + roi.Width / 2.0, roi.Y + roi.Height / 2.0);
        var centerRot = Rotate(centerTeach, originTeach, angleDeg);

        var dx = originFound.X - originTeach.X;
        var dy = originFound.Y - originTeach.Y;
        var centerFound = new Point2d(centerRot.X + dx, centerRot.Y + dy);

        return new Roi
        {
            X = (int)Math.Round(centerFound.X - roi.Width / 2.0),
            Y = (int)Math.Round(centerFound.Y - roi.Height / 2.0),
            Width = roi.Width,
            Height = roi.Height
        };
    }

    private static PointDefinition TransformPointDefinition(PointDefinition p, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        return new PointDefinition
        {
            Name = p.Name,
            MatchScoreThreshold = p.MatchScoreThreshold,
            TemplateImageFile = p.TemplateImageFile,
            TemplateRoi = p.TemplateRoi,
            SearchRoi = TransformRoiKeepSize(p.SearchRoi, originTeach, originFound, angleDeg),
            WorldPosition = p.WorldPosition,
            OffsetPx = p.OffsetPx
        };
    }

    private static DefectInspectionConfig TransformDefectConfig(DefectInspectionConfig cfg, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        return new DefectInspectionConfig
        {
            InspectRoi = TransformRoi(cfg.InspectRoi, originTeach, originFound, angleDeg),
            ThresholdWhite = cfg.ThresholdWhite,
            ThresholdBlack = cfg.ThresholdBlack,
            MinBlobSize = cfg.MinBlobSize,
            MaxBlobSize = cfg.MaxBlobSize
        };
    }
}

internal static class ConditionEvaluator
{
    internal readonly record struct ConditionValue(bool IsBool, bool Bool, double Number)
    {
        public static ConditionValue FromBool(bool v) => new(true, v, 0.0);
        public static ConditionValue FromNumber(double v) => new(false, false, v);
    }

    internal sealed class Variable
    {
        public Variable(bool pass, double? value = null, double? score = null, bool? found = null)
        {
            Pass = pass;
            Value = value;
            Score = score;
            Found = found;
        }

        public bool Pass { get; }
        public double? Value { get; }
        public double? Score { get; }
        public bool? Found { get; }
    }

    public static Dictionary<string, Variable> BuildVariableMap(InspectionResult result)
    {
        var vars = new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase);

        if (result.Origin is not null)
        {
            vars[result.Origin.Name] = new Variable(result.Origin.Pass, score: result.Origin.Score);
        }

        foreach (var p in result.Points)
        {
            vars[p.Name] = new Variable(p.Pass, score: p.Score);
        }

        foreach (var l in result.Lines)
        {
            vars[l.Name] = new Variable(l.Found, found: l.Found);
        }

        foreach (var d in result.Distances)
        {
            vars[d.Name] = new Variable(d.Pass, value: d.Value);
        }

        foreach (var dd in result.LineToLineDistances)
        {
            vars[dd.Name] = new Variable(dd.Pass, value: dd.Value);
        }

        foreach (var dd in result.PointToLineDistances)
        {
            vars[dd.Name] = new Variable(dd.Pass, value: dd.Value);
        }

        foreach (var a in result.Angles)
        {
            vars[a.Name] = new Variable(a.Pass, value: a.ValueDeg);
        }

        foreach (var ep in result.EdgePairs)
        {
            vars[$"EP.{ep.Name}"] = new Variable(ep.Pass, value: ep.Value, found: ep.Found);
            vars[$"EdgePair.{ep.Name}"] = new Variable(ep.Pass, value: ep.Value, found: ep.Found);
        }

        foreach (var epd in result.EdgePairDetections)
        {
            vars[$"EPD.{epd.Name}"] = new Variable(epd.Pass, value: epd.Value, found: epd.Found);
            vars[$"EdgePairDetect.{epd.Name}"] = new Variable(epd.Pass, value: epd.Value, found: epd.Found);
        }

        foreach (var c in result.Conditions)
        {
            vars[c.Name] = new Variable(c.Pass);
        }

        foreach (var b in result.BlobDetections)
        {
            vars[b.Name] = new Variable(true, value: b.Count);
        }

        foreach (var sc in result.SurfaceCompares)
        {
            vars[$"SC.{sc.Name}"] = new Variable(true, value: sc.Count);
            vars[$"SurfaceCompare.{sc.Name}"] = new Variable(true, value: sc.Count);
            vars[$"SC.{sc.Name}.MaxArea"] = new Variable(true, value: sc.MaxArea);
            vars[$"SurfaceCompare.{sc.Name}.MaxArea"] = new Variable(true, value: sc.MaxArea);
        }

        foreach (var c in result.Calipers)
        {
            vars[c.Name] = new Variable(c.Found, found: c.Found);
        }

        foreach (var d in result.Diameters)
        {
            vars[$"CIR.{d.Name}"] = new Variable(d.Pass, value: d.Value, found: d.Found);
            vars[$"Diameter.{d.Name}"] = new Variable(d.Pass, value: d.Value, found: d.Found);
        }

        return vars;
    }

    public static bool Evaluate(string expression, Dictionary<string, Variable> vars)
    {
        var p = new Parser(expression, vars);
        var v = p.ParseExpression();
        p.Expect(TokenKind.Eof);
        return ToBool(v);
    }

    private static bool ToBool(ConditionValue v)
    {
        if (v.IsBool) return v.Bool;
        throw new InvalidOperationException("Expression did not evaluate to boolean");
    }

    private enum TokenKind
    {
        Eof,
        Identifier,
        Number,
        LParen,
        RParen,
        Dot,
        And,
        Or,
        Not,
        Eq,
        Ne,
        Gt,
        Ge,
        Lt,
        Le
    }

    private readonly record struct Token(TokenKind Kind, string Text, double Number);

    private sealed class Lexer
    {
        private readonly string _s;
        private int _i;

        public Lexer(string s) => _s = s ?? string.Empty;

        public Token Next()
        {
            SkipWs();
            if (_i >= _s.Length) return new Token(TokenKind.Eof, string.Empty, 0);

            var ch = _s[_i];
            if (char.IsLetter(ch) || ch == '_')
            {
                var start = _i++;
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' || _s[_i] == '-')) _i++;
                var t = _s.Substring(start, _i - start);
                if (string.Equals(t, "AND", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.And, t, 0);
                if (string.Equals(t, "OR", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Or, t, 0);
                if (string.Equals(t, "NOT", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Not, t, 0);
                if (string.Equals(t, "TRUE", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Number, t, 1);
                if (string.Equals(t, "FALSE", StringComparison.OrdinalIgnoreCase)) return new Token(TokenKind.Number, t, 0);
                return new Token(TokenKind.Identifier, t, 0);
            }

            if (char.IsDigit(ch) || (ch == '.' && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1])))
            {
                var start = _i;
                var hasDot = false;
                if (ch == '.') { hasDot = true; _i++; }
                while (_i < _s.Length)
                {
                    var c = _s[_i];
                    if (char.IsDigit(c)) { _i++; continue; }
                    if (c == '.' && !hasDot) { hasDot = true; _i++; continue; }
                    break;
                }

                var t = _s.Substring(start, _i - start);
                if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
                {
                    throw new InvalidOperationException($"Invalid number '{t}'");
                }
                return new Token(TokenKind.Number, t, n);
            }

            _i++;
            return ch switch
            {
                '(' => new Token(TokenKind.LParen, "(", 0),
                ')' => new Token(TokenKind.RParen, ")", 0),
                '.' => new Token(TokenKind.Dot, ".", 0),
                '>' => Peek('=') ? new Token(TokenKind.Ge, ">=", 0) : new Token(TokenKind.Gt, ">", 0),
                '<' => Peek('=') ? new Token(TokenKind.Le, "<=", 0) : new Token(TokenKind.Lt, "<", 0),
                '=' => Peek('=') ? new Token(TokenKind.Eq, "==", 0) : throw new InvalidOperationException("Use '==' for equality"),
                '!' => Peek('=') ? new Token(TokenKind.Ne, "!=", 0) : throw new InvalidOperationException("Use '!=' for not-equal"),
                _ => throw new InvalidOperationException($"Unexpected character '{ch}'")
            };
        }

        private bool Peek(char expected)
        {
            if (_i < _s.Length && _s[_i] == expected)
            {
                _i++;
                return true;
            }
            return false;
        }

        private void SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }
    }

    private sealed class Parser
    {
        private readonly Lexer _lex;
        private readonly Dictionary<string, Variable> _vars;
        private Token _t;

        public Parser(string s, Dictionary<string, Variable> vars)
        {
            _lex = new Lexer(s);
            _vars = vars;
            _t = _lex.Next();
        }

        public ConditionValue ParseExpression() => ParseOr();

        public void Expect(TokenKind kind)
        {
            if (_t.Kind != kind)
            {
                throw new InvalidOperationException($"Expected {kind} but got '{_t.Text}'");
            }
            _t = _lex.Next();
        }

        private ConditionValue ParseOr()
        {
            var left = ParseAnd();
            while (_t.Kind == TokenKind.Or)
            {
                Expect(TokenKind.Or);
                var right = ParseAnd();
                left = ConditionValue.FromBool(ToBool(left) || ToBool(right));
            }
            return left;
        }

        private ConditionValue ParseAnd()
        {
            var left = ParseUnary();
            while (_t.Kind == TokenKind.And)
            {
                Expect(TokenKind.And);
                var right = ParseUnary();
                left = ConditionValue.FromBool(ToBool(left) && ToBool(right));
            }
            return left;
        }

        private ConditionValue ParseUnary()
        {
            if (_t.Kind == TokenKind.Not)
            {
                Expect(TokenKind.Not);
                var v = ParseUnary();
                return ConditionValue.FromBool(!ToBool(v));
            }
            return ParsePrimary();
        }

        private ConditionValue ParsePrimary()
        {
            if (_t.Kind == TokenKind.LParen)
            {
                Expect(TokenKind.LParen);
                var v = ParseExpression();
                Expect(TokenKind.RParen);
                return v;
            }

            var left = ParseValue();
            if (IsCompare(_t.Kind))
            {
                var op = _t.Kind;
                _t = _lex.Next();
                var right = ParseValue();
                return ConditionValue.FromBool(Compare(op, left, right));
            }

            return left;
        }

        private static bool IsCompare(TokenKind k) => k is TokenKind.Eq or TokenKind.Ne or TokenKind.Gt or TokenKind.Ge or TokenKind.Lt or TokenKind.Le;

        private static bool Compare(TokenKind op, ConditionValue a, ConditionValue b)
        {
            if (a.IsBool || b.IsBool)
            {
                var ba = a.IsBool ? a.Bool : throw new InvalidOperationException("Left side is not boolean");
                var bb = b.IsBool ? b.Bool : throw new InvalidOperationException("Right side is not boolean");
                return op switch
                {
                    TokenKind.Eq => ba == bb,
                    TokenKind.Ne => ba != bb,
                    _ => throw new InvalidOperationException("Only == and != are allowed for booleans")
                };
            }

            var na = a.Number;
            var nb = b.Number;
            return op switch
            {
                TokenKind.Eq => Math.Abs(na - nb) < 0.0000001,
                TokenKind.Ne => Math.Abs(na - nb) >= 0.0000001,
                TokenKind.Gt => na > nb,
                TokenKind.Ge => na >= nb,
                TokenKind.Lt => na < nb,
                TokenKind.Le => na <= nb,
                _ => false
            };
        }

        private ConditionValue ParseValue()
        {
            if (_t.Kind == TokenKind.Number)
            {
                var n = _t.Number;
                var txt = _t.Text;
                Expect(TokenKind.Number);

                if (string.Equals(txt, "TRUE", StringComparison.OrdinalIgnoreCase)) return ConditionValue.FromBool(true);
                if (string.Equals(txt, "FALSE", StringComparison.OrdinalIgnoreCase)) return ConditionValue.FromBool(false);
                return ConditionValue.FromNumber(n);
            }

            if (_t.Kind == TokenKind.Identifier)
            {
                var name = _t.Text;
                Expect(TokenKind.Identifier);

                string? member = null;
                if (_t.Kind == TokenKind.Dot)
                {
                    Expect(TokenKind.Dot);
                    if (_t.Kind != TokenKind.Identifier)
                    {
                        throw new InvalidOperationException("Expected member after '.'");
                    }
                    member = _t.Text;
                    Expect(TokenKind.Identifier);
                }

                return Resolve(name, member);
            }

            throw new InvalidOperationException($"Unexpected token '{_t.Text}'");
        }

        private ConditionValue Resolve(string name, string? member)
        {
            if (!_vars.TryGetValue(name, out var v))
            {
                throw new InvalidOperationException($"Unknown identifier '{name}'");
            }

            if (string.IsNullOrWhiteSpace(member))
            {
                return ConditionValue.FromBool(v.Pass);
            }

            if (string.Equals(member, "PASS", StringComparison.OrdinalIgnoreCase)) return ConditionValue.FromBool(v.Pass);
            if (string.Equals(member, "VALUE", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Value is null) throw new InvalidOperationException($"{name}.Value is not available");
                return ConditionValue.FromNumber(v.Value.Value);
            }
            if (string.Equals(member, "SCORE", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Score is null) throw new InvalidOperationException($"{name}.Score is not available");
                return ConditionValue.FromNumber(v.Score.Value);
            }
            if (string.Equals(member, "FOUND", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Found is null) throw new InvalidOperationException($"{name}.Found is not available");
                return ConditionValue.FromBool(v.Found.Value);
            }

            throw new InvalidOperationException($"Unknown member '{member}' on '{name}'");
        }

        private static bool ToBool(ConditionValue v)
        {
            if (v.IsBool) return v.Bool;
            throw new InvalidOperationException("Expected boolean");
        }
    }
}
