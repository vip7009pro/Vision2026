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
    double Score,
    List<Point2d>? EdgePoints = null,
    List<bool>? InlierFlags = null);

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

public sealed record PointMatchResult(string Name, Point2d Position, Rect MatchRect, double Score, double Threshold, bool Pass, double AngleDeg, System.Collections.Generic.List<Point2d>? FeaturePoints = null);

public sealed class InspectionTimings
{
      public System.Collections.Concurrent.ConcurrentDictionary<string, int> NodeTimings { get; } = new(System.StringComparer.OrdinalIgnoreCase);
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

    public List<SegmentDistanceResult> SegmentLineDistances { get; } = new();

    public List<AngleResult> Angles { get; } = new();

    public List<ConditionResult> Conditions { get; } = new();

    public List<BlobDetectionResult> BlobDetections { get; } = new();

    public List<SurfaceCompareResult> SurfaceCompares { get; } = new();

    public List<ContourCompareResult> ContourCompares { get; } = new();

    public List<LinePairDetectionResult> LinePairDetections { get; } = new();

    public List<EdgePairResult> EdgePairs { get; } = new();

    public List<EdgePairDetectResult> EdgePairDetections { get; } = new();

    public List<CircleFinderResult> CircleFinders { get; } = new();

    public List<DiameterResult> Diameters { get; } = new();

    public List<CaliperResult> Calipers { get; } = new();

    public List<CodeDetectionResult> CodeDetections { get; } = new();

    public List<ImageOutputResult> ImageOutputs { get; } = new();

    public List<PlcReadResult> PlcReads { get; } = new();

    public List<PlcWriteResult> PlcWrites { get; } = new();

    public List<PlcWaitResult> PlcWaits { get; } = new();

    public List<PlcTriggerResult> PlcTriggers { get; } = new();

    public List<PlcBatchReadResult> PlcBatchReads { get; } = new();

    public List<PlcBatchWriteResult> PlcBatchWrites { get; } = new();

    public DefectDetectionResult? Defects { get; set; }
}

public sealed record PlcReadResult(string Name, string PlcId, string TagName, object? Value, bool Found);

public sealed record PlcWriteResult(string Name, string PlcId, string TagName, object? WrittenValue, bool Success);

public sealed record PlcWaitResult(string Name, string PlcId, string TagName, PlcCompareOperator Operator, string TargetValue, bool Success, double ElapsedMs);

public sealed record PlcTriggerResult(string Name, string PlcId, string TagName, PlcTriggerEdge EdgeMode, bool Triggered);

public sealed record PlcBatchReadResult(string Name, string PlcId, Dictionary<string, object?> TagValues);

public sealed record PlcBatchWriteResult(string Name, string PlcId, bool Success);

public sealed record ImageOutputResult(
    string Name,
    bool Saved,
    string SavedFilePath,
    string Error = ""
);

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

public sealed record SurfaceCompareDefect(Rect BoundingBox, double Angle, Point2d Centroid, double Area);

public sealed record SurfaceCompareResult(
    string Name, 
    int Count, 
    double MaxArea, 
    List<SurfaceCompareDefect> Defects, 
    bool Pass,
    byte[]? TemplateImage = null,
    byte[]? CurrentImage = null,
    byte[]? BinaryImage = null,
    byte[]? DiffImage = null);

public sealed record ContourSegment(List<Point2d> Points, bool IsClosed);

public sealed record ContourCompareResult(
    string Name,
    bool Found,
    bool Pass,
    double MatchScore,
    double MaxDistancePx,
    double AreaDiffPercent,
    double PerimeterDiffPercent,
    List<Point2d>? TemplateContour = null,
    List<Point2d>? TestContour = null,
    List<List<Point2d>>? TemplateContours = null,
    List<List<Point2d>>? TestContours = null,
    List<List<Point2d>>? PassContours = null,
    List<List<Point2d>>? FailContours = null,
    List<ContourSegment>? PassSegments = null,
    List<ContourSegment>? FailSegments = null);

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
    void ResetTracking(string? productCode = null);
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

    private readonly PLC.Services.IPlcManagerService? _plcManager;

    public InspectionService(
        ImagePreprocessor preprocessor,
        PatternMatcher matcher,
        DistanceCalculator distanceCalculator,
        LineDetector lineDetector,
        IDefectDetector defectDetector,
        PLC.Services.IPlcManagerService? plcManager = null)
    {
        _preprocessor = preprocessor;
        _matcher = matcher;
        _distanceCalculator = distanceCalculator;
        _lineDetector = lineDetector;
        _defectDetector = defectDetector;
        _plcManager = plcManager;
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

            var imageSourcesByName = (config.ImageSources ?? new List<ImageSourceDefinition>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

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

                    // Preprocess node input: either raw image or another preprocess output connected to "In" or "Image".
                    var inEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, id, StringComparison.OrdinalIgnoreCase)
                                                          && (string.Equals(e.ToPort, "In", StringComparison.OrdinalIgnoreCase) || string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase)));
                    Mat baseMat = image;
                    if (inEdge is not null && nodesById.TryGetValue(inEdge.FromNodeId, out var fromNode))
                    {
                        if (string.Equals(fromNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                        {
                            baseMat = GetPreprocessNodeOutput(fromNode.Id);
                        }
                        else if (string.Equals(fromNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
                        {
                            baseMat = image;
                        }
                    }

                    var __sw = Stopwatch.StartNew();
                    var m = _preprocessor.Run(baseMat, settings);
                    __sw.Stop();
                    if (!string.IsNullOrWhiteSpace(node.RefName))
                    {
                        result.Timings.NodeTimings[node.RefName] = (int)__sw.ElapsedMilliseconds;
                    }
                    lock (matsLock) matsToDispose.Add(m);
                    return m;
                });
            }

            (Mat ImageMat, PreprocessSettings Settings) ResolveToolPreprocess(string toolType, string toolRefName)
            {
                var defaultSettings = config.Preprocess;

                var toolNode = nodesById.Values.FirstOrDefault(n => string.Equals(n.Type, toolType, StringComparison.OrdinalIgnoreCase)
                                                                    && string.Equals(n.RefName, toolRefName, StringComparison.OrdinalIgnoreCase));
                if (toolNode is null)
                {
                    return (GetProcessedDefault(), defaultSettings);
                }

                var imageEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, toolNode.Id, StringComparison.OrdinalIgnoreCase)
                                                       && string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase));
                
                if (imageEdge is null || !nodesById.TryGetValue(imageEdge.FromNodeId, out var fromNode))
                {
                    return (GetProcessedDefault(), defaultSettings);
                }

                if (string.Equals(fromNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                {
                    preprocessSettingsByName.TryGetValue(fromNode.RefName ?? string.Empty, out var ppSettings);
                    ppSettings ??= new PreprocessSettings();
                    var ppMat = GetPreprocessNodeOutput(fromNode.Id);
                    return (ppMat, ppSettings);
                }
                else if (string.Equals(fromNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
                {
                    var preprocessedMat = _preprocessor.Run(image, defaultSettings);
                    lock (matsLock) matsToDispose.Add(preprocessedMat);
                    return (preprocessedMat, defaultSettings);
                }

                return (GetProcessedDefault(), defaultSettings);
            }

            Mat ResolveToolImage(string toolType, string toolRefName)
            {
                // Default to the input image
                var mat = image;

                var toolNode = nodesById.Values.FirstOrDefault(n => string.Equals(n.Type, toolType, StringComparison.OrdinalIgnoreCase)
                                                                    && string.Equals(n.RefName, toolRefName, StringComparison.OrdinalIgnoreCase));
                if (toolNode is null)
                {
                    return mat;
                }

                var imageEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, toolNode.Id, StringComparison.OrdinalIgnoreCase)
                                                       && string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase));
                if (imageEdge is null)
                {
                    return mat;
                }

                if (!nodesById.TryGetValue(imageEdge.FromNodeId, out var fromNode))
                {
                    return mat;
                }

                // If connected to ImageSource, use the input image directly (do not load from disk during run)
                if (string.Equals(fromNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
                {
                    var preprocessedMat = _preprocessor.Run(image, config.Preprocess);
                    lock (matsLock) matsToDispose.Add(preprocessedMat);
                    return preprocessedMat;
                }
                // If connected to Preprocess, get preprocessed image
                else if (string.Equals(fromNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                {
                    return GetPreprocessNodeOutput(fromNode.Id);
                }

                return mat;
            }

            Mat? LoadImageFromSource(ImageSourceDefinition source)
            {
                try
                {
                    if (source.SourceType == ImageSourceType.File)
                    {
                        if (!string.IsNullOrWhiteSpace(source.FilePath) && File.Exists(source.FilePath))
                        {
                            var mat = Cv2.ImRead(source.FilePath);
                            if (mat is not null && !mat.Empty())
                            {
                                return mat;
                            }
                        }
                    }
                    else if (source.SourceType == ImageSourceType.Folder)
                    {
                        if (!string.IsNullOrWhiteSpace(source.FolderPath) && Directory.Exists(source.FolderPath))
                        {
                            var files = Directory.GetFiles(source.FolderPath, "*.*", SearchOption.TopDirectoryOnly)
                                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                           f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                           f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                           f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                                           f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
                                           f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
                                .OrderBy(f => f)
                                .FirstOrDefault();

                            if (!string.IsNullOrWhiteSpace(files))
                            {
                                var mat = Cv2.ImRead(files);
                                if (mat is not null && !mat.Empty())
                                {
                                    return mat;
                                }
                            }
                        }
                    }
                    // Camera source would need CameraService integration - for now return null
                    // This would be implemented when running in live mode with camera service
                }
                catch
                {
                    // Return null on any error
                }
                return null;
            }

            static List<BlobInfo> DetectBlobsInCrop(Mat crop, Roi inspectRoi, List<BlobRoiDefinition>? rois, BlobPolarity polarity, int threshold, int minArea, int maxArea, Point2d centerFound, double totalAngle)
            {
                var blobs = new List<BlobInfo>();
                if (crop is null || crop.Empty())
                {
                    return blobs;
                }

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

                var hasMulti = rois is not null && rois.Count > 0;
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

                        var rx = rr.Roi.X - inspectRoi.X;
                        var ry = rr.Roi.Y - inspectRoi.Y;
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

                        var rx = rr.Roi.X - inspectRoi.X;
                        var ry = rr.Roi.Y - inspectRoi.Y;
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

                    var globalCentroid = MapToGlobal(new Point2d(cx, cy), inspectRoi.Width, inspectRoi.Height, centerFound, totalAngle);
                    var bboxCenterLocal = new Point2d(left + width / 2.0, top + height / 2.0);
                    var bboxCenterGlobal = MapToGlobal(bboxCenterLocal, inspectRoi.Width, inspectRoi.Height, centerFound, totalAngle);
                    var fullRect = new Rect((int)Math.Round(bboxCenterGlobal.X - width / 2.0), (int)Math.Round(bboxCenterGlobal.Y - height / 2.0), width, height);

                    blobs.Add(new BlobInfo(fullRect, globalCentroid, areaPx));
                }
                return blobs;
            }

            static SurfaceCompareResult RunSurfaceCompare(
                Mat matBgrOrGray, 
                Point2d originTeach, 
                Point2d originFound, 
                double angleDeg, 
                SurfaceCompareDefinition def,
                ImagePreprocessor preprocessor,
                PreprocessSettings settings)
            {
                if (matBgrOrGray is null)
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>(), false);
                }

                if (def is null || string.IsNullOrWhiteSpace(def.Name))
                {
                    return new SurfaceCompareResult(string.Empty, 0, 0.0, new List<SurfaceCompareDefect>(), false);
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
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>(), false);
                }

                if (string.IsNullOrWhiteSpace(def.TemplateImageFile) || !File.Exists(def.TemplateImageFile))
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>(), false);
                }

                // Convert input to grayscale.
                Mat testGray = matBgrOrGray;
                using var testGrayOwned = matBgrOrGray.Channels() == 1 ? null : matBgrOrGray.CvtColor(ColorConversionCodes.BGR2GRAY);
                if (testGrayOwned is not null)
                {
                    testGray = testGrayOwned;
                }

                // Load and Preprocess template exactly like the current image.
                using var templRaw = Cv2.ImRead(def.TemplateImageFile, ImreadModes.Grayscale);
                if (templRaw.Empty())
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>(), false);
                }

                // Apply the same preprocessing steps to the template crop.
                using var templCrop0 = preprocessor.Run(templRaw, settings);
                // We need to un-rotate the target image to match the teach template orientation.
                var trTeach = new Roi { X = templateRoiTeach.X, Y = templateRoiTeach.Y, Width = templateRoiTeach.Width, Height = templateRoiTeach.Height, Angle = templateRoiTeach.Angle };
                using var testCropRaw = ExtractStraightRoi(testGray, trTeach, originTeach, originFound, angleDeg, out var centerFoundTpl);
                if (testCropRaw.Width <= 0 || testCropRaw.Height <= 0)
                {
                    return new SurfaceCompareResult(def.Name, 0, 0.0, new List<SurfaceCompareDefect>(), false);
                }

                // Apply the same preprocessing steps to the target crop.
                using var testCrop = preprocessor.Run(testCropRaw, settings);

                // Make sure template crop has the exact size
                using var tplCrop = new Mat();
                if (templCrop0.Width != templateRoiTeach.Width || templCrop0.Height != templateRoiTeach.Height)
                {
                    Cv2.Resize(templCrop0, tplCrop, new Size(templateRoiTeach.Width, templateRoiTeach.Height), 0, 0, InterpolationFlags.Area);
                }
                else
                {
                    templCrop0.CopyTo(tplCrop);
                }

                if (def.AutoAlign && def.AutoAlignMaxShiftPx > 0 && testCrop.Width > def.AutoAlignMaxShiftPx * 2 && testCrop.Height > def.AutoAlignMaxShiftPx * 2)
                {
                    var shift = Math.Clamp(def.AutoAlignMaxShiftPx, 1, 30);
                    var innerRect = new Rect(shift, shift, tplCrop.Width - shift * 2, tplCrop.Height - shift * 2);
                    using var tplInner = new Mat(tplCrop, innerRect);
                    using var matchRes = new Mat();
                    Cv2.MatchTemplate(testCrop, tplInner, matchRes, TemplateMatchModes.SqDiffNormed);
                    Cv2.MinMaxLoc(matchRes, out double minVal, out _, out Point minLoc, out _);

                    int dx = minLoc.X - shift;
                    int dy = minLoc.Y - shift;
                    if (dx != 0 || dy != 0)
                    {
                        using var M = new Mat(2, 3, MatType.CV_32FC1);
                        M.Set(0, 0, 1.0f); M.Set(0, 1, 0.0f); M.Set(0, 2, (float)-dx);
                        M.Set(1, 0, 0.0f); M.Set(1, 1, 1.0f); M.Set(1, 2, (float)-dy);
                        using var alignedTest = new Mat();
                        Cv2.WarpAffine(testCrop, alignedTest, M, testCrop.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);
                        alignedTest.CopyTo(testCrop);
                    }
                }

                using var bw = new Mat();
                var thr = Math.Clamp(def.DiffThreshold, 0, 255);

                if (def.Algorithm == SurfaceCompareAlgorithm.SSIM)
                {
                    var winSize = Math.Clamp(def.SsimWindowSize > 0 ? def.SsimWindowSize : 7, 3, 21);
                    if (winSize % 2 == 0) winSize += 1;

                    using var img1Float = new Mat();
                    using var img2Float = new Mat();
                    testCrop.ConvertTo(img1Float, MatType.CV_32FC1);
                    tplCrop.ConvertTo(img2Float, MatType.CV_32FC1);

                    using var mu1 = new Mat();
                    using var mu2 = new Mat();
                    var kSize = new Size(winSize, winSize);
                    Cv2.GaussianBlur(img1Float, mu1, kSize, 1.5);
                    Cv2.GaussianBlur(img2Float, mu2, kSize, 1.5);

                    using var mu1Sq = new Mat();
                    using var mu2Sq = new Mat();
                    using var mu1Mu2 = new Mat();
                    Cv2.Multiply(mu1, mu1, mu1Sq);
                    Cv2.Multiply(mu2, mu2, mu2Sq);
                    Cv2.Multiply(mu1, mu2, mu1Mu2);

                    using var sigma1Sq = new Mat();
                    using var sigma2Sq = new Mat();
                    using var sigma12 = new Mat();

                    using var t1 = new Mat();
                    using var t2 = new Mat();
                    using var t3 = new Mat();
                    Cv2.Multiply(img1Float, img1Float, t1);
                    Cv2.GaussianBlur(t1, t1, kSize, 1.5);
                    Cv2.Subtract(t1, mu1Sq, sigma1Sq);

                    Cv2.Multiply(img2Float, img2Float, t2);
                    Cv2.GaussianBlur(t2, t2, kSize, 1.5);
                    Cv2.Subtract(t2, mu2Sq, sigma2Sq);

                    Cv2.Multiply(img1Float, img2Float, t3);
                    Cv2.GaussianBlur(t3, t3, kSize, 1.5);
                    Cv2.Subtract(t3, mu1Mu2, sigma12);

                    const double C1 = 6.5025, C2 = 58.5225;
                    using var num1 = new Mat();
                    using var num2 = new Mat();
                    using var den1 = new Mat();
                    using var den2 = new Mat();

                    Cv2.AddWeighted(mu1Mu2, 2.0, mu1Mu2, 0, C1, num1);
                    Cv2.AddWeighted(sigma12, 2.0, sigma12, 0, C2, num2);

                    Cv2.AddWeighted(mu1Sq, 1.0, mu2Sq, 1.0, C1, den1);
                    Cv2.AddWeighted(sigma1Sq, 1.0, sigma2Sq, 1.0, C2, den2);

                    using var num = new Mat();
                    using var den = new Mat();
                    Cv2.Multiply(num1, num2, num);
                    Cv2.Multiply(den1, den2, den);

                    using var ssimMap = new Mat();
                    Cv2.Divide(num, den, ssimMap);

                    using var dissim = new Mat();
                    using var ones = new Mat(ssimMap.Rows, ssimMap.Cols, MatType.CV_32FC1, Scalar.All(1.0));
                    Cv2.Subtract(ones, ssimMap, dissim);
                    Cv2.Multiply(dissim, Scalar.All(255.0), dissim);

                    using var dissim8u = new Mat();
                    dissim.ConvertTo(dissim8u, MatType.CV_8UC1);

                    var ssimThr = Math.Clamp(def.SsimThreshold > 0 ? (1.0 - def.SsimThreshold) * 255.0 : 38.0, 5.0, 250.0);
                    if (def.DiffThreshold > 0 && def.DiffThreshold != 25)
                    {
                        ssimThr = (1.0 - Math.Clamp(def.DiffThreshold / 100.0, 0.05, 0.95)) * 255.0;
                    }
                    Cv2.Threshold(dissim8u, bw, ssimThr, 255, ThresholdTypes.Binary);
                }
                else if (def.Algorithm == SurfaceCompareAlgorithm.GradientAdaptive)
                {
                    using var grad1X = new Mat();
                    using var grad1Y = new Mat();
                    using var grad2X = new Mat();
                    using var grad2Y = new Mat();
                    Cv2.Sobel(testCrop, grad1X, MatType.CV_16S, 1, 0);
                    Cv2.Sobel(testCrop, grad1Y, MatType.CV_16S, 0, 1);
                    Cv2.Sobel(tplCrop, grad2X, MatType.CV_16S, 1, 0);
                    Cv2.Sobel(tplCrop, grad2Y, MatType.CV_16S, 0, 1);

                    using var absGrad1X = new Mat();
                    using var absGrad1Y = new Mat();
                    using var absGrad2X = new Mat();
                    using var absGrad2Y = new Mat();
                    Cv2.ConvertScaleAbs(grad1X, absGrad1X);
                    Cv2.ConvertScaleAbs(grad1Y, absGrad1Y);
                    Cv2.ConvertScaleAbs(grad2X, absGrad2X);
                    Cv2.ConvertScaleAbs(grad2Y, absGrad2Y);

                    using var mag1 = new Mat();
                    using var mag2 = new Mat();
                    Cv2.AddWeighted(absGrad1X, 0.5, absGrad1Y, 0.5, 0, mag1);
                    Cv2.AddWeighted(absGrad2X, 0.5, absGrad2Y, 0.5, 0, mag2);

                    using var gradDiff = new Mat();
                    Cv2.Absdiff(mag1, mag2, gradDiff);

                    using var grayDiff = new Mat();
                    Cv2.Absdiff(testCrop, tplCrop, grayDiff);

                    using var combinedDiff = new Mat();
                    var wGrad = Math.Clamp(def.GradientWeight > 0 ? def.GradientWeight : 0.5, 0.0, 1.0);
                    Cv2.AddWeighted(grayDiff, 1.0 - wGrad, gradDiff, wGrad, 0, combinedDiff);

                    Cv2.Threshold(combinedDiff, bw, thr, 255, ThresholdTypes.Binary);
                }
                else
                {
                    var edgeTol = Math.Max(0, def.EdgeTolerancePx);
                    if (edgeTol > 0)
                    {
                        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(edgeTol * 2 + 1, edgeTol * 2 + 1));
                        using var maxImg = new Mat();
                        using var minImg = new Mat();
                        Cv2.MorphologyEx(tplCrop, maxImg, MorphTypes.Dilate, kernel);
                        Cv2.MorphologyEx(tplCrop, minImg, MorphTypes.Erode, kernel);

                        using var diffHigh = new Mat();
                        using var diffLow = new Mat();
                        Cv2.Subtract(testCrop, maxImg, diffHigh);
                        Cv2.Subtract(minImg, testCrop, diffLow);

                        using var defectRaw = new Mat();
                        Cv2.BitwiseOr(diffHigh, diffLow, defectRaw);
                        Cv2.Threshold(defectRaw, bw, thr, 255, ThresholdTypes.Binary);
                    }
                    else
                    {
                        using var diffGray = new Mat();
                        Cv2.Absdiff(testCrop, tplCrop, diffGray);
                        Cv2.Threshold(diffGray, bw, thr, 255, ThresholdTypes.Binary);
                    }
                }

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

                        var ptsI = new Point[4];
                        ptsI[0] = new Point(rr0.Roi.X - templateRoiTeach.X, rr0.Roi.Y - templateRoiTeach.Y);
                        ptsI[1] = new Point(rr0.Roi.X + rr0.Roi.Width - templateRoiTeach.X, rr0.Roi.Y - templateRoiTeach.Y);
                        ptsI[2] = new Point(rr0.Roi.X + rr0.Roi.Width - templateRoiTeach.X, rr0.Roi.Y + rr0.Roi.Height - templateRoiTeach.Y);
                        ptsI[3] = new Point(rr0.Roi.X - templateRoiTeach.X, rr0.Roi.Y + rr0.Roi.Height - templateRoiTeach.Y);

                        if (rr0.Mode == BlobRoiMode.Include)
                        {
                            anyInclude = true;
                            Cv2.FillPoly(mask, new[] { ptsI }, Scalar.White);
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

                        var ptsI = new Point[4];
                        ptsI[0] = new Point(rr0.Roi.X - templateRoiTeach.X, rr0.Roi.Y - templateRoiTeach.Y);
                        ptsI[1] = new Point(rr0.Roi.X + rr0.Roi.Width - templateRoiTeach.X, rr0.Roi.Y - templateRoiTeach.Y);
                        ptsI[2] = new Point(rr0.Roi.X + rr0.Roi.Width - templateRoiTeach.X, rr0.Roi.Y + rr0.Roi.Height - templateRoiTeach.Y);
                        ptsI[3] = new Point(rr0.Roi.X - templateRoiTeach.X, rr0.Roi.Y + rr0.Roi.Height - templateRoiTeach.Y);

                        Cv2.FillPoly(mask, new[] { ptsI }, Scalar.Black);
                    }

                    Cv2.BitwiseAnd(bw, mask, bw);
                }
                else
                {
                    // If no sub-ROIs are defined, default to masking with the TemplateRoi transformed.
                    using var mask = new Mat(bw.Rows, bw.Cols, MatType.CV_8UC1, Scalar.Black);
                    var tr0 = def.TemplateRoi;
                    if (tr0.Width <= 0 || tr0.Height <= 0) tr0 = def.InspectRoi;

                    var ptsI = new Point[4];
                    ptsI[0] = new Point(tr0.X - templateRoiTeach.X, tr0.Y - templateRoiTeach.Y);
                    ptsI[1] = new Point(tr0.X + tr0.Width - templateRoiTeach.X, tr0.Y - templateRoiTeach.Y);
                    ptsI[2] = new Point(tr0.X + tr0.Width - templateRoiTeach.X, tr0.Y + tr0.Height - templateRoiTeach.Y);
                    ptsI[3] = new Point(tr0.X - templateRoiTeach.X, tr0.Y + tr0.Height - templateRoiTeach.Y);
                    
                    Cv2.FillPoly(mask, new[] { ptsI }, Scalar.White);
                    Cv2.BitwiseAnd(bw, mask, bw);
                }

                // Capture debug previews (Raw Diff)
                byte[]? imgBin = null;
                try { Cv2.ImEncode(".png", bw, out imgBin); } catch { }

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

                    // Track max area found before filtering (for debug/tuning)
                    if (areaPx > maxFoundArea) maxFoundArea = areaPx;

                    if (areaPx < minArea || areaPx > maxArea)
                    {
                        continue;
                    }

                    var cx = centroids.Get<double>(i, 0);
                    var cy = centroids.Get<double>(i, 1);

                    // Map local coordinates from straight patch back to global found image
                    var localCenterX = left + width / 2.0;
                    var localCenterY = top + height / 2.0;
                    var globalCenterForRect = MapToGlobal(new Point2d(localCenterX, localCenterY), templateRoiTeach.Width, templateRoiTeach.Height, centerFoundTpl, angleDeg);
                    
                    var fullRect = new Rect((int)Math.Round(globalCenterForRect.X - width / 2.0), (int)Math.Round(globalCenterForRect.Y - height / 2.0), width, height);
                    var centroid = MapToGlobal(new Point2d(cx, cy), templateRoiTeach.Width, templateRoiTeach.Height, centerFoundTpl, angleDeg);

                    defects.Add(new SurfaceCompareDefect(fullRect, angleDeg, centroid, areaPx));
                }

                var pass = defects.Count >= def.MinCount && defects.Count <= def.MaxCount;

                // Capture debug previews if needed (for troubleshooting)
                byte[]? imgTpl = null;
                byte[]? imgCur = null;
                byte[]? imgDif = null;
                try
                {
                    Cv2.ImEncode(".png", tplCrop, out imgTpl);
                    Cv2.ImEncode(".png", testCrop, out imgCur);
                    Cv2.ImEncode(".png", bw, out imgDif);      // Resulting diff after morphology
                }
                catch { /* Ignore encoding errors */ }

                return new SurfaceCompareResult(def.Name, defects.Count, maxFoundArea, defects, pass, imgTpl, imgCur, imgBin, imgDif);
            }

            static ContourCompareResult RunContourCompare(
                Mat matBgrOrGray,
                Point2d originTeach,
                Point2d originFound,
                double angleDeg,
                ContourCompareDefinition def,
                ImagePreprocessor preprocessor,
                PreprocessSettings settings)
            {
                if (matBgrOrGray is null || def is null || string.IsNullOrWhiteSpace(def.Name))
                {
                    return new ContourCompareResult(def?.Name ?? string.Empty, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                var templateRoiTeach = def.TemplateRoi;
                var inspectRoiTeach = def.InspectRoi;
                if (templateRoiTeach.Width <= 0 || templateRoiTeach.Height <= 0) templateRoiTeach = inspectRoiTeach;
                if (inspectRoiTeach.Width <= 0 || inspectRoiTeach.Height <= 0) inspectRoiTeach = templateRoiTeach;

                if (templateRoiTeach.Width <= 0 || templateRoiTeach.Height <= 0 || inspectRoiTeach.Width <= 0 || inspectRoiTeach.Height <= 0)
                {
                    return new ContourCompareResult(def.Name, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                if (string.IsNullOrWhiteSpace(def.TemplateImageFile) || !File.Exists(def.TemplateImageFile))
                {
                    return new ContourCompareResult(def.Name, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                using var templRaw = Cv2.ImRead(def.TemplateImageFile, ImreadModes.Grayscale);
                if (templRaw.Empty())
                {
                    return new ContourCompareResult(def.Name, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                Mat testGray = matBgrOrGray;
                using var testGrayOwned = matBgrOrGray.Channels() == 1 ? null : matBgrOrGray.CvtColor(ColorConversionCodes.BGR2GRAY);
                if (testGrayOwned is not null) testGray = testGrayOwned;

                // Calculate global center of Template ROI when pose transform is applied
                var centerTemplateTeach = new Point2d(templateRoiTeach.X + templateRoiTeach.Width / 2.0, templateRoiTeach.Y + templateRoiTeach.Height / 2.0);
                var centerTemplateRot = Rotate(centerTemplateTeach, originTeach, angleDeg);
                var deltaX = originFound.X - originTeach.X;
                var deltaY = originFound.Y - originTeach.Y;
                var centerFoundTemplate = new Point2d(centerTemplateRot.X + deltaX, centerTemplateRot.Y + deltaY);

                // Expand Template ROI by padding to search for sub-pixel/local shift
                var pad = 20;
                var trPadTeach = new Roi
                {
                    X = templateRoiTeach.X - pad,
                    Y = templateRoiTeach.Y - pad,
                    Width = templateRoiTeach.Width + pad * 2,
                    Height = templateRoiTeach.Height + pad * 2,
                    Angle = templateRoiTeach.Angle
                };

                using var searchCropPadRaw = ExtractStraightRoi(testGray, trPadTeach, originTeach, originFound, angleDeg, out _);
                if (searchCropPadRaw.Width <= 0 || searchCropPadRaw.Height <= 0)
                {
                    return new ContourCompareResult(def.Name, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                using var searchCropPad = preprocessor.Run(searchCropPadRaw, settings);
                using var templCrop = preprocessor.Run(templRaw, settings);

                var testRect = new Rect(pad, pad, templCrop.Width, templCrop.Height).Intersect(new Rect(0, 0, searchCropPad.Width, searchCropPad.Height));
                if (testRect.Width <= 0 || testRect.Height <= 0)
                {
                    return new ContourCompareResult(def.Name, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                using var testCrop = new Mat(searchCropPad, testRect).Clone();

                static List<Point[]> FindAllContours(Mat img, double c1, double c2, int minArea)
                {
                    using var canny = new Mat();
                    Cv2.Canny(img, canny, c1, c2);
                    Cv2.FindContours(canny, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                    if (contours is null || contours.Length == 0) return new List<Point[]>();
                    return contours.Where(c => c.Length >= 4 && (Cv2.ContourArea(c) >= minArea || Cv2.ArcLength(c, true) >= minArea / 2.0)).OrderByDescending(c => Cv2.ArcLength(c, true)).ToList();
                }

                var c1 = def.CannyThreshold1 > 0 ? def.CannyThreshold1 : 50;
                var c2 = def.CannyThreshold2 > 0 ? def.CannyThreshold2 : 150;
                var minArea = Math.Max(1, def.MinContourArea);

                var tplContours = FindAllContours(templCrop, c1, c2, minArea);
                var testContours = FindAllContours(testCrop, c1, c2, minArea);

                if (tplContours.Count == 0 || testContours.Count == 0)
                {
                    return new ContourCompareResult(def.Name, false, false, 999.0, 999.0, 999.0, 999.0);
                }

                var allTplLocalPoints = tplContours.SelectMany(c => c).Select(p => new Point2d(p.X, p.Y)).ToList();
                var allTestLocalPoints = testContours.SelectMany(c => c).Select(p => new Point2d(p.X, p.Y)).ToList();

                double maxAllowedDist = def.MaxHausdorffDistPx > 0 ? def.MaxHausdorffDistPx : 4.0;
                double maxAllowedAreaDiff = def.MaxAreaDiffPercent > 0 ? def.MaxAreaDiffPercent : 5.0;
                double maxAllowedShapeScore = def.MaxShapeMatchScore > 0 ? def.MaxShapeMatchScore : 0.10;

                // 1. Robust ICP Alignment: find optimal translation (alignDx, alignDy) in range [-15, 15]
                double alignDx = 0.0, alignDy = 0.0;
                double minError = double.MaxValue;
                double clipThreshSq = maxAllowedDist * maxAllowedDist * 2.0;

                for (double tX = -15; tX <= 15; tX += 1.0)
                {
                    for (double tY = -15; tY <= 15; tY += 1.0)
                    {
                        double errSum = 0.0;
                        int sampleCount = 0;
                        int step = Math.Max(1, allTestLocalPoints.Count / 100);

                        for (int k = 0; k < allTestLocalPoints.Count; k += step)
                        {
                            var p = allTestLocalPoints[k];
                            double px = p.X + tX;
                            double py = p.Y + tY;
                            double dMinSq = double.MaxValue;

                            foreach (var q in allTplLocalPoints)
                            {
                                double dX = px - q.X;
                                double dY = py - q.Y;
                                double dSq = dX * dX + dY * dY;
                                if (dSq < dMinSq) dMinSq = dSq;
                            }

                            errSum += Math.Min(clipThreshSq, dMinSq);
                            sampleCount++;
                        }

                        if (sampleCount > 0 && errSum < minError)
                        {
                            minError = errSum;
                            alignDx = tX;
                            alignDy = tY;
                        }
                    }
                }

                var shiftRot = Rotate(new Point2d(alignDx, alignDy), new Point2d(0, 0), angleDeg);
                var centerFoundTestCrop = new Point2d(centerFoundTemplate.X + shiftRot.X, centerFoundTemplate.Y + shiftRot.Y);

                // Convert contours to global space for UI canvas overlay drawing
                var tplPointsGlobalList = tplContours.Select(c => c.Select(p => MapToGlobal(new Point2d(p.X, p.Y), templCrop.Width, templCrop.Height, centerFoundTemplate, angleDeg)).ToList()).ToList();

                var passSegmentsList = new List<ContourSegment>();
                var failSegmentsList = new List<ContourSegment>();
                var passContoursGlobalList = new List<List<Point2d>>();
                var failContoursGlobalList = new List<List<Point2d>>();
                double maxDistPx = 0.0;

                static void ClassifyAndAddSubSegments(
                    List<Point2d> localPoints,
                    double[] distances,
                    double maxAllowedDist,
                    Func<Point2d, Point2d> mapToGlobal,
                    List<ContourSegment>? passSegDst,
                    List<ContourSegment> failSegDst,
                    List<List<Point2d>>? passDst,
                    List<List<Point2d>> failDst)
                {
                    if (localPoints.Count < 2) return;

                    List<Point2d>? currentSeg = null;
                    bool? currentSegOk = null;

                    for (int i = 0; i < localPoints.Count; i++)
                    {
                        bool ok = distances[i] <= maxAllowedDist;
                        var ptGlobal = mapToGlobal(localPoints[i]);

                        if (currentSegOk is null || currentSegOk != ok)
                        {
                            if (currentSeg is not null && currentSeg.Count > 1)
                            {
                                var seg = new ContourSegment(currentSeg, IsClosed: false);
                                if (currentSegOk == true)
                                {
                                    passSegDst?.Add(seg);
                                    passDst?.Add(currentSeg);
                                }
                                else
                                {
                                    failSegDst.Add(seg);
                                    failDst.Add(currentSeg);
                                }
                            }

                            currentSeg = new List<Point2d>();
                            currentSegOk = ok;

                            if (i > 0)
                            {
                                currentSeg.Add(mapToGlobal(localPoints[i - 1]));
                            }
                        }

                        currentSeg!.Add(ptGlobal);
                    }

                    if (currentSeg is not null && currentSeg.Count > 1)
                    {
                        var seg = new ContourSegment(currentSeg, IsClosed: false);
                        if (currentSegOk == true)
                        {
                            passSegDst?.Add(seg);
                            passDst?.Add(currentSeg);
                        }
                        else
                        {
                            failSegDst.Add(seg);
                            failDst.Add(currentSeg);
                        }
                    }
                }

                // 2. Classify Test Contours (Intact closed contours vs partial extra/deformed strokes)
                foreach (var cTest in testContours)
                {
                    var localPts = cTest.Select(p => new Point2d(p.X + alignDx, p.Y + alignDy)).ToList();
                    var globalPts = localPts.Select(pt => MapToGlobal(pt, templCrop.Width, templCrop.Height, centerFoundTemplate, angleDeg)).ToList();

                    int okCount = 0;
                    var ptDistances = new double[localPts.Count];
                    for (int k = 0; k < localPts.Count; k++)
                    {
                        var pt = localPts[k];
                        double minDist = double.MaxValue;
                        foreach (var q in allTplLocalPoints)
                        {
                            double dX = pt.X - q.X;
                            double dY = pt.Y - q.Y;
                            double d = Math.Sqrt(dX * dX + dY * dY);
                            if (d < minDist) minDist = d;
                        }
                        ptDistances[k] = minDist;
                        if (minDist > maxDistPx) maxDistPx = minDist;
                        if (minDist <= maxAllowedDist) okCount++;
                    }

                    double okRatio = (double)okCount / localPts.Count;
                    if (okRatio >= 0.80)
                    {
                        var seg = new ContourSegment(globalPts, IsClosed: true);
                        passSegmentsList.Add(seg);
                        passContoursGlobalList.Add(globalPts);
                    }
                    else
                    {
                        ClassifyAndAddSubSegments(
                            localPts,
                            ptDistances,
                            maxAllowedDist,
                            pt => MapToGlobal(pt, templCrop.Width, templCrop.Height, centerFoundTemplate, angleDeg),
                            passSegmentsList,
                            failSegmentsList,
                            passContoursGlobalList,
                            failContoursGlobalList);
                    }
                }

                // 3. Classify Template Contours (Missing strokes)
                foreach (var cTpl in tplContours)
                {
                    var localPts = cTpl.Select(p => new Point2d(p.X, p.Y)).ToList();
                    var ptDistances = new double[localPts.Count];

                    int okCount = 0;
                    for (int k = 0; k < localPts.Count; k++)
                    {
                        var pt = localPts[k];
                        double minDist = double.MaxValue;
                        foreach (var pTest in allTestLocalPoints)
                        {
                            double dX = pt.X - (pTest.X + alignDx);
                            double dY = pt.Y - (pTest.Y + alignDy);
                            double d = Math.Sqrt(dX * dX + dY * dY);
                            if (d < minDist) minDist = d;
                        }
                        ptDistances[k] = minDist;
                        if (minDist > maxDistPx) maxDistPx = minDist;
                        if (minDist <= maxAllowedDist) okCount++;
                    }

                    double okRatio = (double)okCount / localPts.Count;
                    if (okRatio < 0.85)
                    {
                        ClassifyAndAddSubSegments(
                            localPts,
                            ptDistances,
                            maxAllowedDist,
                            pt => MapToGlobal(pt, templCrop.Width, templCrop.Height, centerFoundTemplate, angleDeg),
                            passSegDst: null,
                            failSegmentsList,
                            passDst: null,
                            failContoursGlobalList);
                    }
                }

                var tplCombined = tplContours.SelectMany(c => c).ToArray();
                var testCombined = testContours.SelectMany(c => c).ToArray();
                double matchScore = (tplCombined.Length > 0 && testCombined.Length > 0) ? Cv2.MatchShapes(tplCombined, testCombined, ShapeMatchModes.I1) : 999.0;

                double areaTpl = Math.Max(1.0, tplContours.Sum(c => Cv2.ContourArea(c)));
                double areaTest = testContours.Sum(c => Cv2.ContourArea(c));
                double areaDiffPercent = Math.Abs(areaTest - areaTpl) / areaTpl * 100.0;

                double perimTpl = Math.Max(1.0, tplContours.Sum(c => Cv2.ArcLength(c, true)));
                double perimTest = testContours.Sum(c => Cv2.ArcLength(c, true));
                double perimDiffPercent = Math.Abs(perimTest - perimTpl) / perimTpl * 100.0;

                bool pass = def.MatchMethod switch
                {
                    ContourMatchMethod.HausdorffDistance => failSegmentsList.Count == 0 && maxDistPx <= maxAllowedDist,
                    ContourMatchMethod.AreaPerimeterDiff => areaDiffPercent <= maxAllowedAreaDiff,
                    _ => failSegmentsList.Count == 0 || matchScore <= maxAllowedShapeScore
                };

                var testPointsGlobalList = testContours.Select(c => c.Select(p => MapToGlobal(new Point2d(p.X, p.Y), templCrop.Width, templCrop.Height, centerFoundTestCrop, angleDeg)).ToList()).ToList();

                return new ContourCompareResult(
                    def.Name,
                    Found: true,
                    Pass: pass,
                    MatchScore: matchScore,
                    MaxDistancePx: maxDistPx,
                    AreaDiffPercent: areaDiffPercent,
                    PerimeterDiffPercent: perimDiffPercent,
                    TemplateContour: tplPointsGlobalList.FirstOrDefault(),
                    TestContour: testPointsGlobalList.FirstOrDefault(),
                    TemplateContours: tplPointsGlobalList,
                    TestContours: testPointsGlobalList,
                    PassContours: passContoursGlobalList,
                    FailContours: failContoursGlobalList,
                    PassSegments: passSegmentsList,
                    FailSegments: failSegmentsList);
            }

            static CaliperResult DetectCaliper(Mat matBgrOrGray, Roi roiTeach, CaliperDefinition def, Point2d originTeach, Point2d originFound, double angleDeg)
            {
                if (matBgrOrGray is null || roiTeach.Width <= 0 || roiTeach.Height <= 0)
                {
                    return new CaliperResult(def.Name, Found: false, new List<CaliperEdgePoint>(), default, default, 0.0);
                }

                using var patch = ExtractStraightRoi(matBgrOrGray, roiTeach, originTeach, originFound, angleDeg, out var centerFound);
                if (patch.Empty())
                {
                    return new CaliperResult(def.Name, Found: false, new List<CaliperEdgePoint>(), default, default, 0.0);
                }

                using var patchGrayOwned = patch.Channels() == 1 ? null : patch.CvtColor(ColorConversionCodes.BGR2GRAY);
                Mat gray = patchGrayOwned ?? patch;

                var rect = new Rect(0, 0, patch.Width, patch.Height);

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
                        var xLocal = rect.X + sr.X + sr.Width / 2.0;
                        var yLocal = rect.Y + sr.Y + ySub;
                        var ptGlobal = MapToGlobal(new Point2d(xLocal, yLocal), patch.Width, patch.Height, centerFound, angleDeg);
                        points.Add(new CaliperEdgePoint(ptGlobal.X, ptGlobal.Y, bestVal));
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
                        var xLocal = rect.X + sr.X + xSub;
                        var yLocal = rect.Y + sr.Y + sr.Height / 2.0;
                        var ptGlobal = MapToGlobal(new Point2d(xLocal, yLocal), patch.Width, patch.Height, centerFound, angleDeg);
                        points.Add(new CaliperEdgePoint(ptGlobal.X, ptGlobal.Y, bestVal));
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
            // Origin template (origin.png) is saved from Image 1 (Global Preprocess only).
            // At runtime, both runtime image and template must go through the SAME full pipeline:
            //   Runtime image: Raw → Global Preprocess → Local Preprocess (node) = Image 2
            //   Template:      Image 1 (origin.png) → PreprocessTemplateForMatch(originPre) = Image 2
            // This ensures symmetric comparison in the same "space".
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
                        OriginAlgorithm = originDefBase.OriginAlgorithm,
                        MinAngle = originDefBase.MinAngle,
                        MaxAngle = originDefBase.MaxAngle,
                        AngleStep = originDefBase.AngleStep,
                        EdgePoint = originDefBase.EdgePoint,
                        ShapeModel = originDefBase.ShapeModel,
                        EdgeThresholdMin = originDefBase.EdgeThresholdMin,
                        EdgeThresholdMax = originDefBase.EdgeThresholdMax
                    };
                }
            }

            var stepDeg = originDef.AngleStep > 0 ? originDef.AngleStep : 1.0;
            var originMatch = _matcher.MatchWithRotation(originMat, originDef, originTempl, originPre, originDef.MinAngle, originDef.MaxAngle, stepDeg);
            if (usedGuidedOrigin && originMatch.Score < originDefBase.MatchScoreThreshold)
            {
                var stepDegBase = originDefBase.AngleStep > 0 ? originDefBase.AngleStep : 1.0;
                var retry = _matcher.MatchWithRotation(originMat, originDefBase, originTempl, originPre, originDefBase.MinAngle, originDefBase.MaxAngle, stepDegBase);

                if (retry.Score > originMatch.Score)
                {
                    originMatch = retry;
                }
            }
            var templateAngleDeg = originMatch.AngleDeg;
            var poseAngleDeg = templateAngleDeg - config.Origin.TemplateRoi.Angle;
            var originPass = originMatch.Score >= config.Origin.MatchScoreThreshold;
            result.Origin = new PointMatchResult(
                config.Origin.Name,
                originMatch.Position,
                originMatch.MatchRect,
                originMatch.Score,
                config.Origin.MatchScoreThreshold,
                originPass,
                poseAngleDeg,
                originMatch.FeaturePoints);
            result.Timings.OriginMs = (int)Math.Max(0, swTotal.ElapsedMilliseconds - tOrigin0);
            result.Timings.NodeTimings[config.Origin.Name ?? "Origin"] = result.Timings.OriginMs;

            var originTeach = new Point2d(config.Origin.WorldPosition.X, config.Origin.WorldPosition.Y);
            var originFound = originMatch.Position;
            var angleDeg = poseAngleDeg;

            var tTools0 = swTotal.ElapsedMilliseconds;
            var pointTasks = (config.Points ?? new List<PointDefinition>())
                .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => Task.Run(() =>
                {
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
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
                            OriginAlgorithm = def.OriginAlgorithm,
                            MinAngle = def.MinAngle,
                            MaxAngle = def.MaxAngle,
                            AngleStep = def.AngleStep,
                            EdgePoint = def.EdgePoint,

                            ShapeModel = def.ShapeModel,
                            EdgeThresholdMin = def.EdgeThresholdMin,
                            EdgeThresholdMax = def.EdgeThresholdMax
                        };
                    }

                    static (bool Found, Point2d Position, double Score, Rect MatchRect) FindPointByEdge(Mat matBgrOrGray, Roi roiTeach, EdgePointSettings ep, Point2d originTeach, Point2d originFound, double angleDeg)
                    {
                        if (matBgrOrGray is null || roiTeach.Width <= 0 || roiTeach.Height <= 0)
                        {
                            return (false, default, 0.0, default);
                        }

                        using var patch = ExtractStraightRoi(matBgrOrGray, roiTeach, originTeach, originFound, angleDeg, out var centerFound);
                        if (patch.Empty())
                        {
                            return (false, default, 0.0, default);
                        }

                        using var patchGrayOwned = patch.Channels() == 1 ? null : patch.CvtColor(ColorConversionCodes.BGR2GRAY);
                        Mat gray = patchGrayOwned ?? patch;

                        var rect = new Rect(0, 0, patch.Width, patch.Height);

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
                                var xLocal = sr.X + sr.Width / 2.0;
                                var yLocal = sr.Y + ySub;
                                var ptGlobal = MapToGlobal(new Point2d(xLocal, yLocal), patch.Width, patch.Height, centerFound, angleDeg);

                                sumX += ptGlobal.X;
                                sumY += ptGlobal.Y;
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
                                var xLocal = sr.X + xSub;
                                var yLocal = sr.Y + sr.Height / 2.0;
                                var ptGlobal = MapToGlobal(new Point2d(xLocal, yLocal), patch.Width, patch.Height, centerFound, angleDeg);

                                sumX += ptGlobal.X;
                                sumY += ptGlobal.Y;
                                sumG += bestVal;
                                foundN++;
                            }
                        }

                        if (foundN <= 0)
                        {
                            return (false, default, foundN == 0 ? 0.0 : sumG / foundN, default);
                        }

                        // An EdgePoint verifies the edge around the taught template centre.  Its
                        // result must remain the template crosshair, just like TemplateMatch;
                        // otherwise a stronger, unrelated edge can make downstream dimensions
                        // jump away from the point that was taught.
                        var pos = centerFound;
                        var score = sumG / foundN;
                        var matchRect = new Rect(
                            (int)Math.Round(centerFound.X - roiTeach.Width / 2.0),
                            (int)Math.Round(centerFound.Y - roiTeach.Height / 2.0),
                            roiTeach.Width,
                            roiTeach.Height);
                        return (true, pos, score, matchRect);
                    }

                    Point2d basePos;
                    Rect matchRect;
                    double score;
                    double thr;
                    bool pass;
                    List<Point2d>? featurePoints = null;

                    double detectedAngleDeg = templateAngleDeg;

                    if (p.Algorithm == PointFindAlgorithm.EdgePoint)
                    {
                        var r = FindPointByEdge(matForPoint, p.TemplateRoi, p.EdgePoint, originTeach, originFound, angleDeg);
                        basePos = r.Position;
                        matchRect = r.MatchRect;
                        score = r.Score;
                        thr = p.EdgePoint.MinEdgeStrength;
                        pass = r.Found;
                    }
                    else
                    {
                        var templ = GetTemplateGray(def.TemplateImageFile);
                        MatchResult m;
                        if (p.Algorithm == PointFindAlgorithm.MvpShapeMatch
                            || p.Algorithm == PointFindAlgorithm.MvpShapePyramid
                            || p.Algorithm == PointFindAlgorithm.ShapePyramid
                            || p.Algorithm == PointFindAlgorithm.ShapeBased)
                        {
                            var minA = p.MinAngle != 0 ? p.MinAngle : -15.0;
                            var maxA = p.MaxAngle != 0 ? p.MaxAngle : 15.0;
                            var stepA = p.AngleStep > 0 ? p.AngleStep : 1.0;
                            m = _matcher.MatchWithRotation(matForPoint, def, templ, preForPoint, minA, maxA, stepA);
                        }
                        else
                        {
                            m = _matcher.MatchWithFixedRotation(matForPoint, def, templ, templateAngleDeg, preForPoint);
                            if (!ReferenceEquals(def, defBase) && m.Score < defBase.MatchScoreThreshold)
                            {
                                var templ2 = GetTemplateGray(defBase.TemplateImageFile);
                                var retry = _matcher.MatchWithFixedRotation(matForPoint, defBase, templ2, templateAngleDeg, preForPoint);
                                if (retry.Score > m.Score)
                                {
                                    m = retry;
                                }
                            }
                        }

                        basePos = m.Position;
                        matchRect = m.MatchRect;
                        score = m.Score;
                        thr = p.MatchScoreThreshold;
                        pass = score >= thr;
                        featurePoints = m.FeaturePoints;
                        if (m.AngleDeg != 0) detectedAngleDeg = m.AngleDeg;
                    }

                    var off = new Point2d(p.OffsetPx.X, p.OffsetPx.Y);
                    var offRot = Rotate(off, new Point2d(0, 0), detectedAngleDeg);
                    var pos = new Point2d(basePos.X + offRot.X, basePos.Y + offRot.Y);
                    __sw.Stop();
                    result.Timings.NodeTimings[p.Name] = (int)__sw.ElapsedMilliseconds;
                    return new PointMatchResult(p.Name, pos, matchRect, score, thr, pass, detectedAngleDeg, featurePoints);
                }))
                .ToArray();

            var tPointsQueued = swTotal.ElapsedMilliseconds;

            var lineTasks = (config.Lines ?? new List<LineToolDefinition>())
                .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.Name) && l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
                .Select(l => Task.Run(() =>
                {
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var roi = TransformRoiKeepSize(l.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForLine, _) = ResolveToolPreprocess("Line", l.Name);
                    var det = _lineDetector.DetectLongestLine(matForLine, roi, l.Canny1, l.Canny2, l.HoughThreshold, l.MinLineLength, l.MaxLineGap);
                    __sw.Stop(); result.Timings.NodeTimings[l.Name] = (int)__sw.ElapsedMilliseconds; return det with { Name = l.Name };
                }))
                .ToArray();

            var tLinesQueued = swTotal.ElapsedMilliseconds;

            var blobTasks = (config.BlobDetections ?? new List<BlobDetectionDefinition>())
                .Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Name) && b.InspectRoi.Width > 0 && b.InspectRoi.Height > 0)
                .Select(b => Task.Run(() =>
                {
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var (matForBlob, _) = ResolveToolPreprocess("BlobDetection", b.Name);
                    using var crop = ExtractStraightRoi(matForBlob, b.InspectRoi, originTeach, originFound, angleDeg, out var centerFound);
                    var totalAngle = angleDeg + b.InspectRoi.Angle;
                    var blobs = DetectBlobsInCrop(crop, b.InspectRoi, b.Rois, b.Polarity, b.Threshold, b.MinBlobArea, b.MaxBlobArea, centerFound, totalAngle);
                    __sw.Stop();
                    result.Timings.NodeTimings[b.Name] = (int)__sw.ElapsedMilliseconds;
                    return new BlobDetectionResult(b.Name, blobs.Count, blobs);
                }))
                .ToArray();

            var tBlobsQueued = swTotal.ElapsedMilliseconds;

                var surfaceCompareTasks = (config.SurfaceCompares ?? new List<SurfaceCompareDefinition>())
                    .Where(sc => sc is not null && !string.IsNullOrWhiteSpace(sc.Name) && sc.InspectRoi.Width > 0 && sc.InspectRoi.Height > 0)
                    .Select(sc => Task.Run(() =>
                    {
                        var __sw = System.Diagnostics.Stopwatch.StartNew();
                        var (_, scSettings) = ResolveToolPreprocess("SurfaceCompare", sc.Name);
                        var res = RunSurfaceCompare(image, originTeach, originFound, angleDeg, sc, _preprocessor, scSettings);
                        __sw.Stop(); result.Timings.NodeTimings[sc.Name] = (int)__sw.ElapsedMilliseconds; return res;
                    }))
                    .ToArray();

                var contourCompareTasks = (config.ContourCompares ?? new List<ContourCompareDefinition>())
                    .Where(cc => cc is not null && !string.IsNullOrWhiteSpace(cc.Name) && cc.InspectRoi.Width > 0 && cc.InspectRoi.Height > 0)
                    .Select(cc => Task.Run(() =>
                    {
                        var __sw = System.Diagnostics.Stopwatch.StartNew();
                        var (_, ccSettings) = ResolveToolPreprocess("ContourCompare", cc.Name);
                        var res = RunContourCompare(image, originTeach, originFound, angleDeg, cc, _preprocessor, ccSettings);
                        __sw.Stop(); result.Timings.NodeTimings[cc.Name] = (int)__sw.ElapsedMilliseconds; return res;
                    }))
                    .ToArray();

            var tScQueued = swTotal.ElapsedMilliseconds;

            var lpdTasks = (config.LinePairDetections ?? new List<LinePairDetectionDefinition>())
                .Where(lpd => lpd is not null && !string.IsNullOrWhiteSpace(lpd.Name) && lpd.SearchRoi.Width > 0 && lpd.SearchRoi.Height > 0)
                .Select(lpd => Task.Run(() =>
                {
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var roi = TransformRoiKeepSize(lpd.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForLpd, _) = ResolveToolPreprocess("LinePairDetection", lpd.Name);
                    var top = _lineDetector.DetectTopLines(matForLpd, roi, lpd.Canny1, lpd.Canny2, lpd.HoughThreshold, lpd.MinLineLength, lpd.MaxLineGap, topN: 2);
                    if (top.Count < 2)
                    {
                        __sw.Stop(); result.Timings.NodeTimings[lpd.Name] = (int)__sw.ElapsedMilliseconds; return new LinePairDetectionResult(
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

                    __sw.Stop(); result.Timings.NodeTimings[lpd.Name] = (int)__sw.ElapsedMilliseconds; return new LinePairDetectionResult(
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
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var roi = TransformRoiKeepSize(c.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForCal, _) = ResolveToolPreprocess("Caliper", c.Name);
                    var res = DetectCaliper(matForCal, c.SearchRoi, c, originTeach, originFound, angleDeg);
                    __sw.Stop(); result.Timings.NodeTimings[c.Name] = (int)__sw.ElapsedMilliseconds; return res;
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
            Task.WaitAll(contourCompareTasks);
            var tScDone = swTotal.ElapsedMilliseconds;

            Task.WaitAll(lpdTasks);
            var tLpdDone = swTotal.ElapsedMilliseconds;

            Task.WaitAll(caliperTasks);
            var tCalDone = swTotal.ElapsedMilliseconds;

            var tEpdQueued = swTotal.ElapsedMilliseconds;

            static EdgePairDetectResult DetectEdgePair(Mat matBgrOrGray, Roi roiTeach, EdgePairDetectDefinition def, double pixelsPerMm, Point2d originTeach, Point2d originFound, double angleDeg)
            {
                if (matBgrOrGray is null || roiTeach.Width <= 0 || roiTeach.Height <= 0)
                {
                    return new EdgePairDetectResult(def.Name, Found: false, default, default, default, default, double.NaN, def.Nominal, def.TolerancePlus, def.ToleranceMinus, Pass: false, default, default,
                        new List<CaliperEdgePoint>(), new List<CaliperEdgePoint>());
                }

                using var patch = ExtractStraightRoi(matBgrOrGray, roiTeach, originTeach, originFound, angleDeg, out var centerFound);
                if (patch.Empty())
                {
                    return new EdgePairDetectResult(def.Name, Found: false, default, default, default, default, double.NaN, def.Nominal, def.TolerancePlus, def.ToleranceMinus, Pass: false, default, default,
                        new List<CaliperEdgePoint>(), new List<CaliperEdgePoint>());
                }

                using var patchGrayOwned = patch.Channels() == 1 ? null : patch.CvtColor(ColorConversionCodes.BGR2GRAY);
                Mat gray = patchGrayOwned ?? patch;

                var rect = new Rect(0, 0, patch.Width, patch.Height);

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
                            var y0 = Math.Max(0, y - 1);
                            var y1 = Math.Max(0, Math.Min(n - 1, y));
                            var y2 = Math.Min(n - 1, y + 1);
                            return (prof.Get<double>(y0, 0) + prof.Get<double>(y1, 0) + prof.Get<double>(y2, 0)) / 3.0;
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
                        var xLocal = rect.X + sr.X + sr.Width / 2.0;
                        var ptA = MapToGlobal(new Point2d(xLocal, rect.Y + sr.Y + ySubA), patch.Width, patch.Height, centerFound, angleDeg);
                        var ptB = MapToGlobal(new Point2d(xLocal, rect.Y + sr.Y + ySubB), patch.Width, patch.Height, centerFound, angleDeg);
                        e1.Add(new CaliperEdgePoint(ptA.X, ptA.Y, valA));
                        e2.Add(new CaliperEdgePoint(ptB.X, ptB.Y, valB));
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
                            var x0 = Math.Max(0, x - 1);
                            var x1 = Math.Max(0, Math.Min(n - 1, x));
                            var x2 = Math.Min(n - 1, x + 1);
                            return (prof.Get<double>(0, x0) + prof.Get<double>(0, x1) + prof.Get<double>(0, x2)) / 3.0;
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
                        var yLocal = rect.Y + sr.Y + sr.Height / 2.0;
                        var ptA = MapToGlobal(new Point2d(rect.X + sr.X + xSubA, yLocal), patch.Width, patch.Height, centerFound, angleDeg);
                        var ptB = MapToGlobal(new Point2d(rect.X + sr.X + xSubB, yLocal), patch.Width, patch.Height, centerFound, angleDeg);
                        e1.Add(new CaliperEdgePoint(ptA.X, ptA.Y, valA));
                        e2.Add(new CaliperEdgePoint(ptB.X, ptB.Y, valB));
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
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var (matForEpd, _) = ResolveToolPreprocess("EdgePairDetect", epd.Name);
                    var res = DetectEdgePair(matForEpd, epd.SearchRoi, epd, config.PixelsPerMm, originTeach, originFound, angleDeg);
                    __sw.Stop(); result.Timings.NodeTimings[epd.Name] = (int)__sw.ElapsedMilliseconds; return res;
                }))
                .ToArray();

            static CircleFinderResult DetectCircle(Mat matBgrOrGray, Roi roi, CircleFinderDefinition def)
            {
                var name = def.Name ?? string.Empty;
                if (matBgrOrGray is null || matBgrOrGray.Empty() || roi.Width <= 0 || roi.Height <= 0)
                {
                    return new CircleFinderResult(name, Found: false, default, 0.0, 0.0);
                }

                var imgW = matBgrOrGray.Width;
                var imgH = matBgrOrGray.Height;

                if (def.Algorithm == CircleFindAlgorithm.RadialCaliper)
                {
                    using var grayOwnedR = matBgrOrGray.Channels() == 1 ? null : matBgrOrGray.CvtColor(ColorConversionCodes.BGR2GRAY);
                    var grayMat = grayOwnedR ?? matBgrOrGray;

                    var centerEst = new Point2d(roi.X + roi.Width / 2.0, roi.Y + roi.Height / 2.0);
                    var nominalR = Math.Min(roi.Width, roi.Height) / 2.0;
                    if (nominalR < 3.0) nominalR = 20.0;

                    var stripCount = Math.Clamp(def.StripCount > 0 ? def.StripCount : 32, 4, 360);
                    var stripLength = Math.Max(5, def.StripLength > 0 ? def.StripLength : 40);
                    var stripWidth = Math.Max(1, def.StripWidth > 0 ? def.StripWidth : 10);
                    var minEdgeStrength = Math.Max(1, def.MinEdgeStrength);

                    var startAngleRad = def.MinAngleDeg * Math.PI / 180.0;
                    var endAngleRad = def.MaxAngleDeg * Math.PI / 180.0;
                    if (Math.Abs(endAngleRad - startAngleRad) < 1e-4)
                    {
                        endAngleRad = startAngleRad + 2.0 * Math.PI;
                    }

                    var angleStep = (endAngleRad - startAngleRad) / stripCount;
                    var halfL = stripLength / 2.0;
                    var halfW = stripWidth / 2.0;

                    var detectedPoints = new List<Point2d>();

                    byte SampleBilinear(double x, double y)
                    {
                        var ix = (int)Math.Floor(x);
                        var iy = (int)Math.Floor(y);
                        if (ix < 0 || ix >= imgW - 1 || iy < 0 || iy >= imgH - 1)
                        {
                            if (ix >= 0 && ix < imgW && iy >= 0 && iy < imgH)
                                return grayMat.At<byte>(iy, ix);
                            return 0;
                        }

                        var fx = x - ix;
                        var fy = y - iy;
                        var v00 = grayMat.At<byte>(iy, ix);
                        var v10 = grayMat.At<byte>(iy, ix + 1);
                        var v01 = grayMat.At<byte>(iy + 1, ix);
                        var v11 = grayMat.At<byte>(iy + 1, ix + 1);

                        var top = v00 + fx * (v10 - v00);
                        var bottom = v01 + fx * (v11 - v01);
                        return (byte)Math.Clamp(top + fy * (bottom - top), 0, 255);
                    }

                    var roiAngleRad = roi.Angle * Math.PI / 180.0;
                    for (var i = 0; i < stripCount; i++)
                    {
                        var angle = roiAngleRad + startAngleRad + (i + 0.5) * angleStep;
                        var ux = Math.Cos(angle);
                        var uy = Math.Sin(angle);
                        var vx = -uy;
                        var vy = ux;

                        var radialSamples = stripLength + 1;
                        var profile = new double[radialSamples];
                        var widthStepCount = Math.Max(1, stripWidth);

                        for (var rIdx = 0; rIdx < radialSamples; rIdx++)
                        {
                            var rOffset = -halfL + (rIdx * stripLength / Math.Max(1, radialSamples - 1));
                            var sampleCenterPt = new Point2d(
                                centerEst.X + (nominalR + rOffset) * ux,
                                centerEst.Y + (nominalR + rOffset) * uy);

                            double sumVal = 0;
                            for (var wIdx = 0; wIdx < widthStepCount; wIdx++)
                            {
                                var wOffset = -halfW + (wIdx * stripWidth / Math.Max(1, widthStepCount - 1));
                                var px = sampleCenterPt.X + wOffset * vx;
                                var py = sampleCenterPt.Y + wOffset * vy;
                                sumVal += SampleBilinear(px, py);
                            }
                            profile[rIdx] = sumVal / widthStepCount;
                        }

                        var deriv = new double[radialSamples];
                        for (var rIdx = 1; rIdx < radialSamples - 1; rIdx++)
                        {
                            deriv[rIdx] = (profile[rIdx + 1] - profile[rIdx - 1]) / 2.0;
                        }

                        int bestIdx = -1;
                        double bestVal = 0;

                        for (var rIdx = 1; rIdx < radialSamples - 1; rIdx++)
                        {
                            var dVal = deriv[rIdx];
                            bool isCandidate = def.Polarity switch
                            {
                                EdgePolarity.LightToDark => dVal < -minEdgeStrength,
                                EdgePolarity.DarkToLight => dVal > minEdgeStrength,
                                _ => Math.Abs(dVal) > minEdgeStrength
                            };

                            if (!isCandidate) continue;

                            if (def.EdgeSelection == EdgeSelection.First)
                            {
                                bestIdx = rIdx;
                                bestVal = dVal;
                                break;
                            }

                            if (def.EdgeSelection == EdgeSelection.Last)
                            {
                                bestIdx = rIdx;
                                bestVal = dVal;
                            }
                            else // MaxStrength
                            {
                                if (Math.Abs(dVal) > Math.Abs(bestVal))
                                {
                                    bestIdx = rIdx;
                                    bestVal = dVal;
                                }
                            }
                        }

                        if (bestIdx > 0 && bestIdx < radialSamples - 1)
                        {
                            var y1 = deriv[bestIdx - 1];
                            var y2 = deriv[bestIdx];
                            var y3 = deriv[bestIdx + 1];
                            var denom = (y1 - 2.0 * y2 + y3);
                            var subOffset = 0.0;
                            if (Math.Abs(denom) > 1e-6)
                            {
                                subOffset = (y1 - y3) / (2.0 * denom);
                                subOffset = Math.Clamp(subOffset, -0.9, 0.9);
                            }

                            var subRIdx = bestIdx + subOffset;
                            var finalR = -halfL + (subRIdx * stripLength / Math.Max(1, radialSamples - 1));
                            var edgePt = new Point2d(
                                centerEst.X + (nominalR + finalR) * ux,
                                centerEst.Y + (nominalR + finalR) * uy);
                            detectedPoints.Add(edgePt);
                        }
                    }

                    if (detectedPoints.Count < 3)
                    {
                        return new CircleFinderResult(name, Found: false, centerEst, nominalR, 0.0, detectedPoints, new List<bool>());
                    }

                    var rnd = new Random(name.GetHashCode());
                    var bestInlierCount = -1;
                    Point2d bestCenter = centerEst;
                    double bestRadius = nominalR;
                    var inlierTol = 3.0;

                    static bool TryCircleFrom3Points(Point2d a, Point2d b, Point2d c, out Point2d center, out double r)
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

                    for (var iter = 0; iter < 100; iter++)
                    {
                        var ia = rnd.Next(detectedPoints.Count);
                        var ib = rnd.Next(detectedPoints.Count);
                        var ic = rnd.Next(detectedPoints.Count);
                        if (ia == ib || ia == ic || ib == ic) continue;

                        if (!TryCircleFrom3Points(detectedPoints[ia], detectedPoints[ib], detectedPoints[ic], out var c0, out var r0))
                            continue;

                        if (def.MinRadiusPx > 0 && r0 < def.MinRadiusPx) continue;
                        if (def.MaxRadiusPx > 0 && r0 > def.MaxRadiusPx) continue;

                        var inlCount = 0;
                        for (var pIdx = 0; pIdx < detectedPoints.Count; pIdx++)
                        {
                            var p = detectedPoints[pIdx];
                            var distToCenter = Math.Sqrt((p.X - c0.X) * (p.X - c0.X) + (p.Y - c0.Y) * (p.Y - c0.Y));
                            if (Math.Abs(distToCenter - r0) <= inlierTol)
                            {
                                inlCount++;
                            }
                        }

                        if (inlCount > bestInlierCount)
                        {
                            bestInlierCount = inlCount;
                            bestCenter = c0;
                            bestRadius = r0;
                        }
                    }

                    var inliers = new List<Point2d>();
                    var inlierFlags = new List<bool>();
                    for (var pIdx = 0; pIdx < detectedPoints.Count; pIdx++)
                    {
                        var p = detectedPoints[pIdx];
                        var distToCenter = Math.Sqrt((p.X - bestCenter.X) * (p.X - bestCenter.X) + (p.Y - bestCenter.Y) * (p.Y - bestCenter.Y));
                        var isInlier = Math.Abs(distToCenter - bestRadius) <= inlierTol;
                        inlierFlags.Add(isInlier);
                        if (isInlier)
                        {
                            inliers.Add(p);
                        }
                    }

                    if (inliers.Count >= 3)
                    {
                        double sumX = 0, sumY = 0, sumX2 = 0, sumY2 = 0, sumXY = 0;
                        double sumR = 0, sumXR = 0, sumYR = 0;
                        var M = inliers.Count;

                        for (var k = 0; k < M; k++)
                        {
                            var px = inliers[k].X;
                            var py = inliers[k].Y;
                            var r2 = px * px + py * py;
                            sumX += px;
                            sumY += py;
                            sumX2 += px * px;
                            sumY2 += py * py;
                            sumXY += px * py;
                            sumR += r2;
                            sumXR += px * r2;
                            sumYR += py * r2;
                        }

                        var A1 = M * sumX2 - sumX * sumX;
                        var B1 = M * sumXY - sumX * sumY;
                        var C1 = 0.5 * (M * sumXR - sumX * sumR);

                        var A2 = M * sumXY - sumX * sumY;
                        var B2 = M * sumY2 - sumY * sumY;
                        var C2 = 0.5 * (M * sumYR - sumY * sumR);

                        var det = A1 * B2 - A2 * B1;
                        if (Math.Abs(det) > 1e-6)
                        {
                            var cx = (C1 * B2 - C2 * B1) / det;
                            var cy = (A1 * C2 - A2 * C1) / det;
                            var meanR2 = (sumR - 2.0 * cx * sumX - 2.0 * cy * sumY) / M + cx * cx + cy * cy;
                            if (meanR2 > 0)
                            {
                                bestCenter = new Point2d(cx, cy);
                                bestRadius = Math.Sqrt(meanR2);
                            }
                        }
                    }

                    var minPassInliers = Math.Max(3, stripCount / 4);
                    var isFound = inliers.Count >= minPassInliers && bestRadius > 0;
                    if (def.MinRadiusPx > 0 && bestRadius < def.MinRadiusPx) isFound = false;
                    if (def.MaxRadiusPx > 0 && bestRadius > def.MaxRadiusPx) isFound = false;

                    var score = inliers.Count / (double)stripCount;

                    return new CircleFinderResult(name, isFound, bestCenter, bestRadius, score, detectedPoints, inlierFlags);
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
                    var __sw = System.Diagnostics.Stopwatch.StartNew();
                    var roi = TransformRoiKeepSize(c.SearchRoi, originTeach, originFound, angleDeg);
                    var (matForCircle, _) = ResolveToolPreprocess("CircleFinder", c.Name);
                    var res = DetectCircle(matForCircle, roi, c);
                    __sw.Stop(); result.Timings.NodeTimings[c.Name] = (int)__sw.ElapsedMilliseconds; return res;
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

            foreach (var t in contourCompareTasks)
            {
                result.ContourCompares.Add(t.Result);
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
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (d is null || string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.CircleRef))
                {
                    continue;
                }

                if (!foundCircles.TryGetValue(d.CircleRef, out var c) || !c.Found)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[d.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.Diameters.Add(new DiameterResult(d.Name, d.CircleRef, Found: false, double.NaN, d.Nominal, d.TolerancePlus, d.ToleranceMinus, Pass: false, default, 0.0));
                    continue;
                }

                var diameterPx = 2.0 * c.RadiusPx;
                var value = config.PixelsPerMm > 0 ? diameterPx / config.PixelsPerMm : diameterPx;
                var pass = value >= (d.Nominal - d.ToleranceMinus) && value <= (d.Nominal + d.TolerancePlus);
                __swNode.Stop();
                result.Timings.NodeTimings[d.Name] = (int)__swNode.ElapsedMilliseconds;
                result.Diameters.Add(new DiameterResult(d.Name, d.CircleRef, Found: true, value, d.Nominal, d.TolerancePlus, d.ToleranceMinus, pass, c.Center, c.RadiusPx));
            }

            var tEdgePairs0 = swTotal.ElapsedMilliseconds;
            foreach (var ep in config.EdgePairs)
            {
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (string.IsNullOrWhiteSpace(ep.Name) || string.IsNullOrWhiteSpace(ep.RefA) || string.IsNullOrWhiteSpace(ep.RefB))
                {
                    continue;
                }

                if (!foundLines.TryGetValue(ep.RefA, out var la) || !foundLines.TryGetValue(ep.RefB, out var lb) || !la.Found || !lb.Found)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[ep.Name] = (int)__swNode.ElapsedMilliseconds;
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

                __swNode.Stop();
                result.Timings.NodeTimings[ep.Name] = (int)__swNode.ElapsedMilliseconds;
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
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.LineA) || string.IsNullOrWhiteSpace(a.LineB))
                {
                    continue;
                }

                if (!foundLines.TryGetValue(a.LineA, out var la) || !foundLines.TryGetValue(a.LineB, out var lb) || !la.Found || !lb.Found)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[a.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.Angles.Add(new AngleResult(a.Name, a.LineA, a.LineB, double.NaN, a.Nominal, a.TolerancePlus, a.ToleranceMinus, Pass: false, Found: false, default, default, default));
                    continue;
                }

                var v1 = new Point2d(la.P2.X - la.P1.X, la.P2.Y - la.P1.Y);
                var v2 = new Point2d(lb.P2.X - lb.P1.X, lb.P2.Y - lb.P1.Y);
                var n1 = Math.Sqrt(v1.X * v1.X + v1.Y * v1.Y);
                var n2 = Math.Sqrt(v2.X * v2.X + v2.Y * v2.Y);
                if (n1 < 1e-9 || n2 < 1e-9)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[a.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.Angles.Add(new AngleResult(a.Name, a.LineA, a.LineB, double.NaN, a.Nominal, a.TolerancePlus, a.ToleranceMinus, Pass: false, Found: false, default, default, default));
                    continue;
                }

                var dot = (v1.X * v2.X + v1.Y * v2.Y) / (n1 * n2);
                dot = Math.Clamp(dot, -1.0, 1.0);
                var angle = Math.Acos(dot) * 180.0 / Math.PI;

                var pass = angle >= (a.Nominal - a.ToleranceMinus) && angle <= (a.Nominal + a.TolerancePlus);
                var found = TryIntersectInfiniteLines(la, lb, out var inter);
                __swNode.Stop();
                result.Timings.NodeTimings[a.Name] = (int)__swNode.ElapsedMilliseconds;
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
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (!distanceAnchors.TryGetValue(d.PointA, out var a) || !distanceAnchors.TryGetValue(d.PointB, out var b))
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[d.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.Distances.Add(new DistanceCheckResult(d.Name, d.PointA, d.PointB, double.NaN, d.Nominal, d.TolerancePlus, d.ToleranceMinus, false));
                    continue;
                }

                var checkRes = _distanceCalculator.CheckDistance(d, a, b, config.PixelsPerMm);
                __swNode.Stop();
                result.Timings.NodeTimings[d.Name] = (int)__swNode.ElapsedMilliseconds;
                result.Distances.Add(checkRes);
            }

            foreach (var dd in config.LineToLineDistances)
            {
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (!foundLines.TryGetValue(dd.LineA, out var la) || !foundLines.TryGetValue(dd.LineB, out var lb) || !la.Found || !lb.Found)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[dd.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.LineToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.LineA, dd.LineB, double.NaN, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, false, default, default));
                    continue;
                }

                var (distPx, ca, cb) = CalculateLineLineDistance(la, lb, dd.Mode);
                var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
                var pass = value >= (dd.Nominal - dd.ToleranceMinus) && value <= (dd.Nominal + dd.TolerancePlus);
                __swNode.Stop();
                result.Timings.NodeTimings[dd.Name] = (int)__swNode.ElapsedMilliseconds;
                result.LineToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.LineA, dd.LineB, value, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, pass, ca, cb));
            }

            foreach (var dd in config.PointToLineDistances)
            {
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (!foundPoints.TryGetValue(dd.Point, out var p) || !foundLines.TryGetValue(dd.Line, out var l) || !l.Found)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[dd.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.PointToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.Point, dd.Line, double.NaN, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, false, default, default));
                    continue;
                }

                var (distPx, closest) = CalculatePointLineDistance(p, l, dd.Mode);
                var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
                var pass = value >= (dd.Nominal - dd.ToleranceMinus) && value <= (dd.Nominal + dd.TolerancePlus);
                __swNode.Stop();
                result.Timings.NodeTimings[dd.Name] = (int)__swNode.ElapsedMilliseconds;
                result.PointToLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.Point, dd.Line, value, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, pass, p, closest));
            }
            foreach (var dd in config.SegmentLineDistances)
            {
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
                if (!foundLines.TryGetValue(dd.LineA, out var la) || !foundLines.TryGetValue(dd.LineB, out var lb) || !la.Found || !lb.Found)
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[dd.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.SegmentLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.LineA, dd.LineB, double.NaN, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, false, default, default));
                    continue;
                }

                Roi? searchRoiA = null;
                var lineADef = config.Lines?.FirstOrDefault(x => string.Equals(x.Name, dd.LineA, StringComparison.OrdinalIgnoreCase));
                if (lineADef is not null) searchRoiA = lineADef.SearchRoi;
                else
                {
                    var calADef = config.Calipers?.FirstOrDefault(x => string.Equals(x.Name, dd.LineA, StringComparison.OrdinalIgnoreCase));
                    if (calADef is not null) searchRoiA = calADef.SearchRoi;
                    else
                    {
                        var lpdADef = config.LinePairDetections?.FirstOrDefault(x => string.Equals(x.Name, dd.LineA, StringComparison.OrdinalIgnoreCase));
                        if (lpdADef is not null) searchRoiA = lpdADef.SearchRoi;
                        else
                        {
                            var epdADef = config.EdgePairDetections?.FirstOrDefault(x => string.Equals(x.Name, dd.LineA, StringComparison.OrdinalIgnoreCase));
                            if (epdADef is not null) searchRoiA = epdADef.SearchRoi;
                        }
                    }
                }

                var (distPx, ca, cb) = CalculateSegmentLineDistance(la, lb, dd.Mode, dd.ExtensionMode, searchRoiA, originTeach, originFound, angleDeg);
                var value = config.PixelsPerMm > 0 ? distPx / config.PixelsPerMm : distPx;
                var pass = value >= (dd.Nominal - dd.ToleranceMinus) && value <= (dd.Nominal + dd.TolerancePlus);
                __swNode.Stop();
                result.Timings.NodeTimings[dd.Name] = (int)__swNode.ElapsedMilliseconds;
                result.SegmentLineDistances.Add(new SegmentDistanceResult(dd.Name, dd.LineA, dd.LineB, value, dd.Nominal, dd.TolerancePlus, dd.ToleranceMinus, pass, ca, cb));
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
                var __swNode = System.Diagnostics.Stopwatch.StartNew();
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
                    __swNode.Stop();
                    result.Timings.NodeTimings[cdt.Name] = (int)__swNode.ElapsedMilliseconds;
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
                var anglesToTry = new List<double> { 0 };
                if (Math.Abs(angleDeg) > 0.1)
                {
                    anglesToTry.Add(-angleDeg); // Try to straighten based on Origin
                }
                
                if (cdt.TryHarder)
                {
                    double[] sweep = { 45, -45, 15, -15, 30, -30, 60, -60, 75, -75 };
                    foreach (var a in sweep)
                    {
                        if (!anglesToTry.Contains(a)) anglesToTry.Add(a);
                    }
                }

                ZXing.Result? decoded = null;
                double successfulAngle = 0;
                int rotatedW = gray.Cols;
                int rotatedH = gray.Rows;

                foreach (var a in anglesToTry)
                {
                    using var rotatedGray = new Mat();
                    if (Math.Abs(a) < 0.1)
                    {
                        gray.CopyTo(rotatedGray);
                        rotatedW = gray.Cols;
                        rotatedH = gray.Rows;
                    }
                    else
                    {
                        var center = new Point2f(gray.Cols / 2.0f, gray.Rows / 2.0f);
                        using var rot = Cv2.GetRotationMatrix2D(center, a, 1.0);
                        var absCos = Math.Abs(rot.At<double>(0, 0));
                        var absSin = Math.Abs(rot.At<double>(0, 1));
                        rotatedW = (int)(gray.Rows * absSin + gray.Cols * absCos);
                        rotatedH = (int)(gray.Rows * absCos + gray.Cols * absSin);
                        
                        rot.Set<double>(0, 2, rot.At<double>(0, 2) + rotatedW / 2.0 - center.X);
                        rot.Set<double>(1, 2, rot.At<double>(1, 2) + rotatedH / 2.0 - center.Y);
                        
                        Cv2.WarpAffine(gray, rotatedGray, rot, new Size(rotatedW, rotatedH), InterpolationFlags.Linear, BorderTypes.Replicate);
                    }

                    var bufRot = new byte[rotatedW * rotatedH];
                    Marshal.Copy(rotatedGray.Data, bufRot, 0, bufRot.Length);
                    var srcRot = new RGBLuminanceSource(bufRot, rotatedW, rotatedH, RGBLuminanceSource.BitmapFormat.Gray8);

                    decoded = reader.Decode(srcRot);
                    if (decoded is not null && !string.IsNullOrWhiteSpace(decoded.Text))
                    {
                        successfulAngle = a;
                        break;
                    }
                }

                if (decoded is null || string.IsNullOrWhiteSpace(decoded.Text))
                {
                    __swNode.Stop();
                    result.Timings.NodeTimings[cdt.Name] = (int)__swNode.ElapsedMilliseconds;
                    result.CodeDetections.Add(new CodeDetectionResult(cdt.Name, Found: false, Text: string.Empty, BoundingBox: default));
                    continue;
                }

                // Bounding box: start from decoded points, then pad slightly so the overlay covers the full symbol.
                var bb = rect;
                if (decoded.ResultPoints is not null && decoded.ResultPoints.Length > 0)
                {
                    var xs = new List<double>();
                    var ys = new List<double>();

                    if (Math.Abs(successfulAngle) > 0.1)
                    {
                        var center = new Point2f(gray.Cols / 2.0f, gray.Rows / 2.0f);
                        using var invRot = Cv2.GetRotationMatrix2D(new Point2f(rotatedW / 2.0f, rotatedH / 2.0f), -successfulAngle, 1.0);
                        invRot.Set<double>(0, 2, invRot.At<double>(0, 2) + center.X - rotatedW / 2.0f);
                        invRot.Set<double>(1, 2, invRot.At<double>(1, 2) + center.Y - rotatedH / 2.0f);

                        foreach (var p in decoded.ResultPoints)
                        {
                            var px = p.X * invRot.At<double>(0, 0) + p.Y * invRot.At<double>(0, 1) + invRot.At<double>(0, 2);
                            var py = p.X * invRot.At<double>(1, 0) + p.Y * invRot.At<double>(1, 1) + invRot.At<double>(1, 2);
                            xs.Add(px);
                            ys.Add(py);
                        }
                    }
                    else
                    {
                        xs.AddRange(decoded.ResultPoints.Select(p => (double)p.X));
                        ys.AddRange(decoded.ResultPoints.Select(p => (double)p.Y));
                    }

                    var minX = xs.Min();
                    var maxX = xs.Max();
                    var minY = ys.Min();
                    var maxY = ys.Max();
                    var baseW = Math.Max(1.0, maxX - minX);
                    var baseH = Math.Max(1.0, maxY - minY);
                    var padX = Math.Max(2.0, baseW * 0.12);
                    var padY = Math.Max(2.0, baseH * 0.12);

                    var x = rect.X + (int)Math.Floor(minX - padX);
                    var y = rect.Y + (int)Math.Floor(minY - padY);
                    var w = (int)Math.Ceiling(baseW + padX * 2);
                    var h = (int)Math.Ceiling(baseH + padY * 2);

                    bb = new Rect(x, y, w, h).Intersect(rect);
                }

                __swNode.Stop();
                result.Timings.NodeTimings[cdt.Name] = (int)__swNode.ElapsedMilliseconds;
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
            && result.SegmentLineDistances.All(x => x.Pass)
            && result.Angles.All(x => x.Pass)
            && result.EdgePairs.All(x => x.Pass)
            && result.EdgePairDetections.All(x => x.Pass)
            && result.LinePairDetections.All(x => x.Pass)
            && result.Diameters.All(x => x.Pass)
            && result.SurfaceCompares.All(x => x.Pass)
            && result.CodeDetections.All(x => x.Found)
            && result.Conditions.All(x => x.Pass)
            && (result.Defects?.Defects?.Count ?? 0) == 0;

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

            ExecutePlcNodes(config, result, _plcManager);

            ExecuteImageOutputs(config, result, image, GetPreprocessNodeOutput, nodesById, edges);

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

    private static void ExecutePlcNodes(VisionConfig config, InspectionResult result, PLC.Services.IPlcManagerService? plcManager)
    {
        if (config is null || result is null) return;

        // 1. PlcReads
        if (config.PlcReads != null)
        {
            foreach (var r in config.PlcReads)
            {
                var __swNode = Stopwatch.StartNew();
                var val = plcManager?.GetTagValue(r.PlcId, r.TagName);
                __swNode.Stop();
                result.Timings.NodeTimings[r.Name] = (int)__swNode.ElapsedMilliseconds;
                result.PlcReads.Add(new PlcReadResult(r.Name, r.PlcId, r.TagName, val?.CurrentValue, val != null));
            }
        }

        // 2. PlcWrites
        if (config.PlcWrites != null)
        {
            foreach (var w in config.PlcWrites)
            {
                var __swNode = Stopwatch.StartNew();
                bool ok = false;
                if (plcManager != null && !string.IsNullOrWhiteSpace(w.PlcId) && !string.IsNullOrWhiteSpace(w.TagName))
                {
                    ok = plcManager.WriteTagValueAsync(w.PlcId, w.TagName, w.WriteValue).GetAwaiter().GetResult();
                }
                __swNode.Stop();
                result.Timings.NodeTimings[w.Name] = (int)__swNode.ElapsedMilliseconds;
                result.PlcWrites.Add(new PlcWriteResult(w.Name, w.PlcId, w.TagName, w.WriteValue, ok));
            }
        }

        // 3. PlcWaits
        if (config.PlcWaits != null)
        {
            foreach (var wt in config.PlcWaits)
            {
                var __swNode = Stopwatch.StartNew();
                bool pass = false;
                int timeoutMs = Math.Max(10, wt.TimeoutMs);
                while (__swNode.ElapsedMilliseconds <= timeoutMs)
                {
                    var val = plcManager?.GetTagValue(wt.PlcId, wt.TagName);
                    if (val != null && CompareValues(val.CurrentValue, wt.Operator, wt.TargetValue))
                    {
                        pass = true;
                        break;
                    }
                    if (timeoutMs > 50) System.Threading.Thread.Sleep(10);
                }
                __swNode.Stop();
                result.Timings.NodeTimings[wt.Name] = (int)__swNode.ElapsedMilliseconds;
                result.PlcWaits.Add(new PlcWaitResult(wt.Name, wt.PlcId, wt.TagName, wt.Operator, wt.TargetValue, pass, __swNode.ElapsedMilliseconds));
            }
        }

        // 4. PlcTriggers
        if (config.PlcTriggers != null)
        {
            foreach (var tr in config.PlcTriggers)
            {
                var __swNode = Stopwatch.StartNew();
                var val = plcManager?.GetTagValue(tr.PlcId, tr.TagName);
                bool triggered = false;
                if (val != null)
                {
                    bool cur = ConvertToBool(val.CurrentValue);
                    bool prev = ConvertToBool(val.PreviousValue);
                    triggered = tr.EdgeMode switch
                    {
                        PlcTriggerEdge.RisingEdge => !prev && cur,
                        PlcTriggerEdge.FallingEdge => prev && !cur,
                        PlcTriggerEdge.Changed => prev != cur,
                        _ => false
                    };
                }
                __swNode.Stop();
                result.Timings.NodeTimings[tr.Name] = (int)__swNode.ElapsedMilliseconds;
                result.PlcTriggers.Add(new PlcTriggerResult(tr.Name, tr.PlcId, tr.TagName, tr.EdgeMode, triggered));
            }
        }
    }

    private static bool ConvertToBool(object? obj)
    {
        if (obj is bool b) return b;
        if (obj is int i) return i != 0;
        if (obj is double d) return d != 0;
        if (obj != null && bool.TryParse(obj.ToString(), out bool bRes)) return bRes;
        return false;
    }

    private static bool CompareValues(object? curVal, PlcCompareOperator op, string targetStr)
    {
        if (curVal == null) return false;

        if (double.TryParse(curVal.ToString(), out double curD) && double.TryParse(targetStr, out double tgtD))
        {
            return op switch
            {
                PlcCompareOperator.Equal => Math.Abs(curD - tgtD) < 1e-6,
                PlcCompareOperator.NotEqual => Math.Abs(curD - tgtD) >= 1e-6,
                PlcCompareOperator.GreaterThan => curD > tgtD,
                PlcCompareOperator.LessThan => curD < tgtD,
                PlcCompareOperator.GreaterOrEqual => curD >= tgtD,
                PlcCompareOperator.LessOrEqual => curD <= tgtD,
                _ => false
            };
        }

        string curStr = curVal.ToString() ?? string.Empty;
        int comp = string.Compare(curStr, targetStr, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            PlcCompareOperator.Equal => comp == 0,
            PlcCompareOperator.NotEqual => comp != 0,
            PlcCompareOperator.GreaterThan => comp > 0,
            PlcCompareOperator.LessThan => comp < 0,
            PlcCompareOperator.GreaterOrEqual => comp >= 0,
            PlcCompareOperator.LessOrEqual => comp <= 0,
            _ => false
        };
    }

    private static void ExecuteImageOutputs(VisionConfig config, InspectionResult result, Mat rawInputImage, Func<string, Mat> getPreprocessNodeOutput, Dictionary<string, ToolGraphNode> nodesById, List<ToolGraphEdge> edges)
    {
        if (config.ImageOutputs is null || config.ImageOutputs.Count == 0 || rawInputImage is null || rawInputImage.Empty())
        {
            return;
        }

        foreach (var io in config.ImageOutputs)
        {
            if (string.IsNullOrWhiteSpace(io.Name))
            {
                continue;
            }

            if (!io.EnableOutput)
            {
                continue;
            }

            if (io.SaveCondition == ImageOutputCondition.OnPass && !result.Pass)
            {
                continue;
            }
            if (io.SaveCondition == ImageOutputCondition.OnFail && result.Pass)
            {
                continue;
            }

            try
            {
                Mat sourceMat = rawInputImage;
                var ioNode = nodesById.Values.FirstOrDefault(n => (string.Equals(n.Type, "ImageOutput", StringComparison.OrdinalIgnoreCase) || string.Equals(n.Type, "OutputImage", StringComparison.OrdinalIgnoreCase)) && string.Equals(n.RefName, io.Name, StringComparison.OrdinalIgnoreCase));

                string? inputName = io.InputNodeName;
                if (string.IsNullOrWhiteSpace(inputName) && ioNode is not null)
                {
                    var edge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, ioNode.Id, StringComparison.OrdinalIgnoreCase));
                    if (edge is not null && nodesById.TryGetValue(edge.FromNodeId, out var fromNode))
                    {
                        inputName = fromNode.RefName;
                    }
                }

                if (!string.IsNullOrWhiteSpace(inputName))
                {
                    var srcNode = nodesById.Values.FirstOrDefault(n => string.Equals(n.RefName, inputName, StringComparison.OrdinalIgnoreCase));
                    if (srcNode is not null)
                    {
                        if (string.Equals(srcNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                        {
                            sourceMat = getPreprocessNodeOutput(srcNode.Id);
                        }
                        else
                        {
                            var inEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, srcNode.Id, StringComparison.OrdinalIgnoreCase));
                            if (inEdge is not null && nodesById.TryGetValue(inEdge.FromNodeId, out var fromNode))
                            {
                                if (string.Equals(fromNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                                {
                                    sourceMat = getPreprocessNodeOutput(fromNode.Id);
                                }
                            }
                        }
                    }
                }

                var folder = string.IsNullOrWhiteSpace(io.SaveFolderPath) ? @"C:\VisionOutput" : io.SaveFolderPath;
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var now = DateTime.Now;
                var fileName = string.IsNullOrWhiteSpace(io.FileNameFormat) ? "IMG_{YYYY}{MM}{DD}_{HH}{mm}{ss}" : io.FileNameFormat;
                fileName = fileName.Replace("{YYYY}", now.ToString("yyyy"))
                                   .Replace("{MM}", now.ToString("MM"))
                                   .Replace("{DD}", now.ToString("dd"))
                                   .Replace("{HH}", now.ToString("HH"))
                                   .Replace("{mm}", now.ToString("mm"))
                                   .Replace("{ss}", now.ToString("ss"))
                                   .Replace("{Count}", now.Ticks.ToString()[^6..])
                                   .Replace("{ProductCode}", config.ProductCode ?? "")
                                   .Replace("{Status}", result.Pass ? "PASS" : "FAIL");

                var ext = io.Format switch
                {
                    ImageOutputFormat.JPG => ".jpg",
                    ImageOutputFormat.BMP => ".bmp",
                    _ => ".png"
                };

                if (!fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    fileName += ext;
                }

                var fullPath = Path.Combine(folder, fileName);

                Mat saveMat;
                if (sourceMat.Channels() == 1)
                {
                    saveMat = new Mat();
                    Cv2.CvtColor(sourceMat, saveMat, ColorConversionCodes.GRAY2BGR);
                }
                else
                {
                    saveMat = sourceMat.Clone();
                }

                using (saveMat)
                {
                    if (io.IncludeOverlay)
                    {
                        BurnOverlaysToMat(saveMat, config, result, io, inputName);
                    }

                    var ok = Cv2.ImWrite(fullPath, saveMat);
                    result.ImageOutputs.Add(new ImageOutputResult(io.Name, ok, ok ? fullPath : "", ok ? "" : "Failed to write image file"));
                }
            }
            catch (Exception ex)
            {
                result.ImageOutputs.Add(new ImageOutputResult(io.Name, false, "", ex.Message));
            }
        }
    }

    private static void BurnOverlaysToMat(Mat mat, VisionConfig config, InspectionResult result, ImageOutputDefinition? io = null, string? resolvedInputNodeName = null)
    {
        if (mat is null || mat.Empty())
        {
            return;
        }

        var targetNodeName = !string.IsNullOrWhiteSpace(resolvedInputNodeName) ? resolvedInputNodeName : io?.InputNodeName;
        var renderAll = string.IsNullOrWhiteSpace(targetNodeName) ||
                         string.Equals(targetNodeName, "Default (Current Image)", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(targetNodeName, "ResultView", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(targetNodeName, "Preprocess", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(targetNodeName, "ImageSource", StringComparison.OrdinalIgnoreCase) ||
                         targetNodeName.StartsWith("ResultView", StringComparison.OrdinalIgnoreCase) ||
                         targetNodeName.StartsWith("Preprocess", StringComparison.OrdinalIgnoreCase) ||
                         targetNodeName.StartsWith("ImageSource", StringComparison.OrdinalIgnoreCase);

        var allowedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!renderAll && !string.IsNullOrWhiteSpace(targetNodeName))
        {
            allowedNodes.Add(targetNodeName);

            var distDef = config.Distances?.FirstOrDefault(x => string.Equals(x.Name, targetNodeName, StringComparison.OrdinalIgnoreCase));
            if (distDef is not null)
            {
                if (!string.IsNullOrWhiteSpace(distDef.PointA)) allowedNodes.Add(distDef.PointA);
                if (!string.IsNullOrWhiteSpace(distDef.PointB)) allowedNodes.Add(distDef.PointB);
            }

            var lldDef = config.LineToLineDistances?.FirstOrDefault(x => string.Equals(x.Name, targetNodeName, StringComparison.OrdinalIgnoreCase));
            if (lldDef is not null)
            {
                if (!string.IsNullOrWhiteSpace(lldDef.LineA)) allowedNodes.Add(lldDef.LineA);
                if (!string.IsNullOrWhiteSpace(lldDef.LineB)) allowedNodes.Add(lldDef.LineB);
            }

            var pldDef = config.PointToLineDistances?.FirstOrDefault(x => string.Equals(x.Name, targetNodeName, StringComparison.OrdinalIgnoreCase));
            if (pldDef is not null)
            {
                if (!string.IsNullOrWhiteSpace(pldDef.Point)) allowedNodes.Add(pldDef.Point);
                if (!string.IsNullOrWhiteSpace(pldDef.Line)) allowedNodes.Add(pldDef.Line);
            }

            var sldDef = config.SegmentLineDistances?.FirstOrDefault(x => string.Equals(x.Name, targetNodeName, StringComparison.OrdinalIgnoreCase));
            if (sldDef is not null)
            {
                if (!string.IsNullOrWhiteSpace(sldDef.LineA)) allowedNodes.Add(sldDef.LineA);
                if (!string.IsNullOrWhiteSpace(sldDef.LineB)) allowedNodes.Add(sldDef.LineB);
            }

            var angleDef = config.Angles?.FirstOrDefault(x => string.Equals(x.Name, targetNodeName, StringComparison.OrdinalIgnoreCase));
            if (angleDef is not null)
            {
                if (!string.IsNullOrWhiteSpace(angleDef.LineA)) allowedNodes.Add(angleDef.LineA);
                if (!string.IsNullOrWhiteSpace(angleDef.LineB)) allowedNodes.Add(angleDef.LineB);
            }
        }

        bool ShouldRender(string nodeName) => renderAll || allowedNodes.Contains(nodeName);

        var isCalibrated = config.PixelsPerMm > 0 && Math.Abs(config.PixelsPerMm - 1.0) > 1e-6;
        var scale = config.PixelsPerMm;
        string UnitStr(double val) => isCalibrated ? $"{val:0.##} mm" : $"{val:0.##} px";

        var green = new Scalar(0, 255, 0);       // Lime / Pass
        var red = new Scalar(0, 0, 255);         // Red / Fail / Outlier
        var cyan = new Scalar(255, 255, 0);       // Cyan / Search ROI
        var yellow = new Scalar(0, 255, 255);     // Yellow
        var white = new Scalar(255, 255, 255);   // White
        var showRoiBoxes = io is null || io.ShowRoi;

        void DrawRotatedRoi(Mat targetMat, Roi teachRoi, Scalar color, int thickness = 1)
        {
            if (!showRoiBoxes) return;
            if (targetMat is null || targetMat.Empty() || teachRoi is null || teachRoi.Width <= 0 || teachRoi.Height <= 0) return;

            Point2d originTeach;
            if (config.Origin is not null && (config.Origin.WorldPosition.X != 0 || config.Origin.WorldPosition.Y != 0))
            {
                originTeach = new Point2d(config.Origin.WorldPosition.X, config.Origin.WorldPosition.Y);
            }
            else if (config.Origin?.TemplateRoi.Width > 0)
            {
                originTeach = new Point2d(config.Origin.TemplateRoi.X + config.Origin.TemplateRoi.Width / 2.0, config.Origin.TemplateRoi.Y + config.Origin.TemplateRoi.Height / 2.0);
            }
            else if (config.Origin?.SearchRoi.Width > 0)
            {
                originTeach = new Point2d(config.Origin.SearchRoi.X + config.Origin.SearchRoi.Width / 2.0, config.Origin.SearchRoi.Y + config.Origin.SearchRoi.Height / 2.0);
            }
            else
            {
                originTeach = new Point2d(0, 0);
            }

            Point2d originFound;
            double angleDeg = 0;
            bool hasOriginPose = false;

            if (result.Origin is not null && (result.Origin.MatchRect.Width > 0 || result.Origin.Position.X != 0 || result.Origin.Position.Y != 0))
            {
                var mr = result.Origin.MatchRect;
                originFound = (mr.Width > 0 && mr.Height > 0)
                    ? new Point2d(mr.X + mr.Width / 2.0, mr.Y + mr.Height / 2.0)
                    : new Point2d(result.Origin.Position.X, result.Origin.Position.Y);
                angleDeg = result.Origin.AngleDeg;
                hasOriginPose = true;
            }
            else
            {
                originFound = originTeach;
            }

            Point2d centerFound;
            double totalAngle;

            if (hasOriginPose)
            {
                var centerTeach = new Point2d(teachRoi.X + teachRoi.Width / 2.0, teachRoi.Y + teachRoi.Height / 2.0);
                var centerRot = Rotate(centerTeach, originTeach, angleDeg);
                var dx = originFound.X - originTeach.X;
                var dy = originFound.Y - originTeach.Y;
                centerFound = new Point2d(centerRot.X + dx, centerRot.Y + dy);
                totalAngle = angleDeg + teachRoi.Angle;
            }
            else
            {
                centerFound = new Point2d(teachRoi.X + teachRoi.Width / 2.0, teachRoi.Y + teachRoi.Height / 2.0);
                totalAngle = teachRoi.Angle;
            }

            var halfW = teachRoi.Width / 2.0;
            var halfH = teachRoi.Height / 2.0;

            var p1 = Rotate(new Point2d(-halfW, -halfH), new Point2d(0, 0), totalAngle) + centerFound;
            var p2 = Rotate(new Point2d(halfW, -halfH), new Point2d(0, 0), totalAngle) + centerFound;
            var p3 = Rotate(new Point2d(halfW, halfH), new Point2d(0, 0), totalAngle) + centerFound;
            var p4 = Rotate(new Point2d(-halfW, halfH), new Point2d(0, 0), totalAngle) + centerFound;

            Cv2.Line(targetMat, new Point((int)Math.Round(p1.X), (int)Math.Round(p1.Y)), new Point((int)Math.Round(p2.X), (int)Math.Round(p2.Y)), color, thickness, LineTypes.AntiAlias);
            Cv2.Line(targetMat, new Point((int)Math.Round(p2.X), (int)Math.Round(p2.Y)), new Point((int)Math.Round(p3.X), (int)Math.Round(p3.Y)), color, thickness, LineTypes.AntiAlias);
            Cv2.Line(targetMat, new Point((int)Math.Round(p3.X), (int)Math.Round(p3.Y)), new Point((int)Math.Round(p4.X), (int)Math.Round(p4.Y)), color, thickness, LineTypes.AntiAlias);
            Cv2.Line(targetMat, new Point((int)Math.Round(p4.X), (int)Math.Round(p4.Y)), new Point((int)Math.Round(p1.X), (int)Math.Round(p1.Y)), color, thickness, LineTypes.AntiAlias);
        }

        // 1. Origin
        if (result.Origin is not null)
        {
            if (config.Origin?.TemplateRoi.Width > 0)
            {
                DrawRotatedRoi(mat, config.Origin.TemplateRoi, green, 2);
            }
            else if (config.Origin?.SearchRoi.Width > 0)
            {
                DrawRotatedRoi(mat, config.Origin.SearchRoi, green, 2);
            }

            var mr = result.Origin.MatchRect;
            var cx = (int)Math.Round(mr.Width > 0 && mr.Height > 0 ? mr.X + mr.Width / 2.0 : result.Origin.Position.X);
            var cyPos = (int)Math.Round(mr.Width > 0 && mr.Height > 0 ? mr.Y + mr.Height / 2.0 : result.Origin.Position.Y);
            Cv2.Line(mat, new Point(cx - 15, cyPos), new Point(cx + 15, cyPos), red, 2, LineTypes.AntiAlias);
            Cv2.Line(mat, new Point(cx, cyPos - 15), new Point(cx, cyPos + 15), green, 2, LineTypes.AntiAlias);
            Cv2.PutText(mat, $"Origin: {result.Origin.Score:0.00}", new Point(cx + 18, cyPos + 5), HersheyFonts.HersheySimplex, 0.5, green, 1, LineTypes.AntiAlias);
        }

        // Map point positions for Distance lookups
        var pointPosMap = new Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);
        foreach (var pRes in result.Points)
        {
            pointPosMap[pRes.Name] = pRes.Position;
        }

        // 2. Points
        foreach (var pRes in result.Points)
        {
            if (!ShouldRender(pRes.Name)) continue;
            var pDef = config.Points.FirstOrDefault(x => string.Equals(x.Name, pRes.Name, StringComparison.OrdinalIgnoreCase));
            if (pDef is not null && pDef.SearchRoi.Width > 0 && pDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, pDef.SearchRoi, cyan, 1);
            }

            var color = pRes.Pass ? green : red;
            var px = (int)Math.Round(pRes.Position.X);
            var py = (int)Math.Round(pRes.Position.Y);
            Cv2.Line(mat, new Point(px - 10, py), new Point(px + 10, py), color, 2, LineTypes.AntiAlias);
            Cv2.Line(mat, new Point(px, py - 10), new Point(px, py + 10), color, 2, LineTypes.AntiAlias);
            Cv2.PutText(mat, pRes.Name, new Point(px + 12, py - 6), HersheyFonts.HersheySimplex, 0.5, color, 1, LineTypes.AntiAlias);
        }

        // 3. Lines
        foreach (var lRes in result.Lines)
        {
            if (!ShouldRender(lRes.Name)) continue;
            var lDef = config.Lines.FirstOrDefault(x => string.Equals(x.Name, lRes.Name, StringComparison.OrdinalIgnoreCase));
            if (lDef is not null && lDef.SearchRoi.Width > 0 && lDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, lDef.SearchRoi, cyan, 1);
            }

            if (lRes.Found)
            {
                var p1 = new Point((int)lRes.P1.X, (int)lRes.P1.Y);
                var p2 = new Point((int)lRes.P2.X, (int)lRes.P2.Y);
                Cv2.Line(mat, p1, p2, green, 2, LineTypes.AntiAlias);
                Cv2.PutText(mat, lRes.Name, new Point((p1.X + p2.X) / 2 + 5, (p1.Y + p2.Y) / 2 - 5), HersheyFonts.HersheySimplex, 0.5, green, 1, LineTypes.AntiAlias);
            }
        }

        // 4. Calipers
        foreach (var cRes in result.Calipers)
        {
            if (!ShouldRender(cRes.Name)) continue;
            var cDef = config.Calipers.FirstOrDefault(x => string.Equals(x.Name, cRes.Name, StringComparison.OrdinalIgnoreCase));
            if (cDef is not null && cDef.SearchRoi.Width > 0 && cDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, cDef.SearchRoi, green, 1);
            }

            if (cRes.Found)
            {
                var p1 = new Point((int)cRes.LineP1.X, (int)cRes.LineP1.Y);
                var p2 = new Point((int)cRes.LineP2.X, (int)cRes.LineP2.Y);
                Cv2.Line(mat, p1, p2, green, 2, LineTypes.AntiAlias);
                Cv2.PutText(mat, cRes.Name, new Point((p1.X + p2.X) / 2 + 5, (p1.Y + p2.Y) / 2 - 5), HersheyFonts.HersheySimplex, 0.5, green, 1, LineTypes.AntiAlias);
            }
        }

        // 5. CircleFinders
        foreach (var cfRes in result.CircleFinders)
        {
            if (!ShouldRender(cfRes.Name)) continue;
            var cfDef = config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, cfRes.Name, StringComparison.OrdinalIgnoreCase));
            if (cfDef is not null && cfDef.SearchRoi.Width > 0 && cfDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, cfDef.SearchRoi, cyan, 1);
            }

            if (cfRes.Found)
            {
                var center = new Point((int)Math.Round(cfRes.Center.X), (int)Math.Round(cfRes.Center.Y));
                var radius = (int)Math.Round(cfRes.RadiusPx);
                Cv2.Circle(mat, center, radius, green, 2, LineTypes.AntiAlias);
                Cv2.Line(mat, new Point(center.X - 8, center.Y), new Point(center.X + 8, center.Y), green, 2, LineTypes.AntiAlias);
                Cv2.Line(mat, new Point(center.X, center.Y - 8), new Point(center.X, center.Y + 8), green, 2, LineTypes.AntiAlias);
                var rVal = isCalibrated ? cfRes.RadiusPx / scale : cfRes.RadiusPx;
                var lbl = $"{cfRes.Name}: R={rVal:0.##}{(isCalibrated ? "mm" : "px")}";
                Cv2.PutText(mat, lbl, new Point(center.X + 10, center.Y - 10), HersheyFonts.HersheySimplex, 0.5, green, 1, LineTypes.AntiAlias);
            }
        }

        // 6. Diameters
        foreach (var dRes in result.Diameters)
        {
            if (!ShouldRender(dRes.Name)) continue;
            if (dRes.Found)
            {
                var center = new Point((int)Math.Round(dRes.Center.X), (int)Math.Round(dRes.Center.Y));
                var radius = (int)Math.Round(dRes.RadiusPx);
                Cv2.Circle(mat, center, radius, green, 2, LineTypes.AntiAlias);
                var lbl = $"{dRes.Name}: D={UnitStr(dRes.Value)}";
                Cv2.PutText(mat, lbl, new Point(center.X + 10, center.Y - 10), HersheyFonts.HersheySimplex, 0.5, green, 1, LineTypes.AntiAlias);
            }
        }

        // 7. Distances
        foreach (var dRes in result.Distances)
        {
            if (!ShouldRender(dRes.Name)) continue;
            if ((dRes.Pass || dRes.Value > 0) && pointPosMap.TryGetValue(dRes.PointA, out var pa) && pointPosMap.TryGetValue(dRes.PointB, out var pb))
            {
                var p1 = new Point((int)pa.X, (int)pa.Y);
                var p2 = new Point((int)pb.X, (int)pb.Y);
                var col = dRes.Pass ? green : red;
                Cv2.Line(mat, p1, p2, col, 2, LineTypes.AntiAlias);
                var mx = (p1.X + p2.X) / 2;
                var my = (p1.Y + p2.Y) / 2;
                Cv2.PutText(mat, $"{dRes.Name}={UnitStr(dRes.Value)}", new Point(mx + 5, my - 5), HersheyFonts.HersheySimplex, 0.5, col, 1, LineTypes.AntiAlias);
            }
        }

        // 8. LineToLineDistances
        foreach (var lld in result.LineToLineDistances)
        {
            if (!ShouldRender(lld.Name)) continue;
            var p1 = new Point((int)lld.ClosestA.X, (int)lld.ClosestA.Y);
            var p2 = new Point((int)lld.ClosestB.X, (int)lld.ClosestB.Y);
            var col = lld.Pass ? green : red;
            Cv2.Line(mat, p1, p2, col, 2, LineTypes.AntiAlias);
            var mx = (p1.X + p2.X) / 2;
            var my = (p1.Y + p2.Y) / 2;
            Cv2.PutText(mat, $"{lld.Name}={UnitStr(lld.Value)}", new Point(mx + 5, my - 5), HersheyFonts.HersheySimplex, 0.5, col, 1, LineTypes.AntiAlias);
        }

        // 9. PointToLineDistances
        foreach (var pld in result.PointToLineDistances)
        {
            if (!ShouldRender(pld.Name)) continue;
            var p1 = new Point((int)pld.ClosestA.X, (int)pld.ClosestA.Y);
            var p2 = new Point((int)pld.ClosestB.X, (int)pld.ClosestB.Y);
            var col = pld.Pass ? green : red;
            Cv2.Line(mat, p1, p2, col, 2, LineTypes.AntiAlias);
            var mx = (p1.X + p2.X) / 2;
            var my = (p1.Y + p2.Y) / 2;
            Cv2.PutText(mat, $"{pld.Name}={UnitStr(pld.Value)}", new Point(mx + 5, my - 5), HersheyFonts.HersheySimplex, 0.5, col, 1, LineTypes.AntiAlias);
        }

        // 10. SegmentLineDistances
        foreach (var sld in result.SegmentLineDistances)
        {
            if (!ShouldRender(sld.Name)) continue;
            var p1 = new Point((int)sld.ClosestA.X, (int)sld.ClosestA.Y);
            var p2 = new Point((int)sld.ClosestB.X, (int)sld.ClosestB.Y);
            var col = sld.Pass ? green : red;
            Cv2.Line(mat, p1, p2, col, 2, LineTypes.AntiAlias);
            var mx = (p1.X + p2.X) / 2;
            var my = (p1.Y + p2.Y) / 2;
            Cv2.PutText(mat, $"{sld.Name}={UnitStr(sld.Value)}", new Point(mx + 5, my - 5), HersheyFonts.HersheySimplex, 0.5, col, 1, LineTypes.AntiAlias);
        }

        // 11. Angles
        foreach (var a in result.Angles)
        {
            if (!ShouldRender(a.Name)) continue;
            var col = a.Pass ? green : red;
            var vertex = new Point((int)a.Intersection.X, (int)a.Intersection.Y);
            Cv2.Circle(mat, vertex, 5, col, 2, LineTypes.AntiAlias);
            Cv2.PutText(mat, $"{a.Name}={a.ValueDeg:0.##} deg", new Point(vertex.X + 10, vertex.Y - 10), HersheyFonts.HersheySimplex, 0.5, col, 1, LineTypes.AntiAlias);
        }

        // 12. EdgePairs
        foreach (var ep in result.EdgePairs)
        {
            if (!ShouldRender(ep.Name)) continue;
            if (ep.Found)
            {
                var p1 = new Point((int)ep.ClosestA.X, (int)ep.ClosestA.Y);
                var p2 = new Point((int)ep.ClosestB.X, (int)ep.ClosestB.Y);
                var col = ep.Pass ? green : red;
                Cv2.Line(mat, p1, p2, col, 2, LineTypes.AntiAlias);
                Cv2.PutText(mat, $"{ep.Name}={UnitStr(ep.Value)}", new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2 - 5), HersheyFonts.HersheySimplex, 0.5, col, 1, LineTypes.AntiAlias);
            }
        }

        // 12b. EdgePairDetections
        foreach (var epdRes in result.EdgePairDetections)
        {
            if (!ShouldRender(epdRes.Name)) continue;
            var epdDef = config.EdgePairDetections?.FirstOrDefault(x => string.Equals(x.Name, epdRes.Name, StringComparison.OrdinalIgnoreCase));
            if (showRoiBoxes && epdDef is not null && epdDef.SearchRoi.Width > 0 && epdDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, epdDef.SearchRoi, cyan, 1);
            }

            if (epdRes.Found)
            {
                var l1p1 = new Point((int)epdRes.L1P1.X, (int)epdRes.L1P1.Y);
                var l1p2 = new Point((int)epdRes.L1P2.X, (int)epdRes.L1P2.Y);
                var l2p1 = new Point((int)epdRes.L2P1.X, (int)epdRes.L2P1.Y);
                var l2p2 = new Point((int)epdRes.L2P2.X, (int)epdRes.L2P2.Y);
                var col = epdRes.Pass ? green : red;
                Cv2.Line(mat, l1p1, l1p2, cyan, 2, LineTypes.AntiAlias);
                Cv2.Line(mat, l2p1, l2p2, cyan, 2, LineTypes.AntiAlias);
                var ca = new Point((int)epdRes.ClosestA.X, (int)epdRes.ClosestA.Y);
                var cb = new Point((int)epdRes.ClosestB.X, (int)epdRes.ClosestB.Y);
                Cv2.Line(mat, ca, cb, col, 2, LineTypes.AntiAlias);
                Cv2.PutText(mat, $"{epdRes.Name}={UnitStr(epdRes.Value)}", new Point((ca.X + cb.X) / 2, (ca.Y + cb.Y) / 2 - 5), HersheyFonts.HersheySimplex, 0.5, col, 1, LineTypes.AntiAlias);
            }
        }

        // 13. BlobDetections
        foreach (var bRes in result.BlobDetections)
        {
            if (!ShouldRender(bRes.Name)) continue;
            var bDef = config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, bRes.Name, StringComparison.OrdinalIgnoreCase));
            if (bDef is not null && bDef.InspectRoi.Width > 0 && bDef.InspectRoi.Height > 0)
            {
                DrawRotatedRoi(mat, bDef.InspectRoi, cyan, 1);
                if (bDef.Rois is not null)
                {
                    foreach (var rr in bDef.Rois)
                    {
                        if (rr?.Roi is not null && rr.Roi.Width > 0 && rr.Roi.Height > 0)
                        {
                            DrawRotatedRoi(mat, rr.Roi, rr.Mode == BlobRoiMode.Exclude ? red : yellow, 1);
                        }
                    }
                }
            }

            foreach (var blob in bRes.Blobs)
            {
                var r = new Rect(blob.BoundingBox.X, blob.BoundingBox.Y, blob.BoundingBox.Width, blob.BoundingBox.Height);
                Cv2.Rectangle(mat, r, yellow, 1, LineTypes.AntiAlias);
                var pt = new Point((int)blob.Centroid.X, (int)blob.Centroid.Y);
                Cv2.Circle(mat, pt, 3, yellow, -1, LineTypes.AntiAlias);
            }
        }

        // 14. SurfaceCompares
        foreach (var scRes in result.SurfaceCompares)
        {
            if (!ShouldRender(scRes.Name)) continue;
            var scDef = config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, scRes.Name, StringComparison.OrdinalIgnoreCase));
            if (scDef is not null && scDef.InspectRoi.Width > 0 && scDef.InspectRoi.Height > 0)
            {
                DrawRotatedRoi(mat, scDef.InspectRoi, cyan, 1);
            }

            foreach (var defect in scRes.Defects)
            {
                var r = new Rect(defect.BoundingBox.X, defect.BoundingBox.Y, defect.BoundingBox.Width, defect.BoundingBox.Height);
                Cv2.Rectangle(mat, r, red, 2, LineTypes.AntiAlias);
            }
        }

        // 14b. ContourCompares
        foreach (var ccRes in result.ContourCompares)
        {
            if (!ShouldRender(ccRes.Name)) continue;
            var ccDef = config.ContourCompares?.FirstOrDefault(x => string.Equals(x.Name, ccRes.Name, StringComparison.OrdinalIgnoreCase));
            if (showRoiBoxes && ccDef is not null && ccDef.InspectRoi.Width > 0 && ccDef.InspectRoi.Height > 0)
            {
                DrawRotatedRoi(mat, ccDef.InspectRoi, cyan, 1);
            }

            var tplList = ccRes.TemplateContours ?? (ccRes.TemplateContour is not null ? new List<List<Point2d>> { ccRes.TemplateContour } : null);
            if (tplList is not null)
            {
                foreach (var c in tplList)
                {
                    if (c.Count > 1)
                    {
                        var ptsTpl = c.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                        Cv2.Polylines(mat, new[] { ptsTpl }, true, yellow, 1, LineTypes.AntiAlias);
                    }
                }
            }

            if (ccRes.PassSegments is not null)
            {
                foreach (var seg in ccRes.PassSegments)
                {
                    if (seg.Points.Count > 1)
                    {
                        var ptsPass = seg.Points.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                        Cv2.Polylines(mat, new[] { ptsPass }, seg.IsClosed, green, 2, LineTypes.AntiAlias);
                    }
                }
            }
            else if (ccRes.PassContours is not null)
            {
                foreach (var c in ccRes.PassContours)
                {
                    if (c.Count > 1)
                    {
                        var ptsPass = c.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                        Cv2.Polylines(mat, new[] { ptsPass }, true, green, 2, LineTypes.AntiAlias);
                    }
                }
            }

            if (ccRes.FailSegments is not null)
            {
                foreach (var seg in ccRes.FailSegments)
                {
                    if (seg.Points.Count > 1)
                    {
                        var ptsFail = seg.Points.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                        Cv2.Polylines(mat, new[] { ptsFail }, seg.IsClosed, red, 2, LineTypes.AntiAlias);
                    }
                }
            }
            else if (ccRes.FailContours is not null)
            {
                foreach (var c in ccRes.FailContours)
                {
                    if (c.Count > 1)
                    {
                        var ptsFail = c.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();
                        Cv2.Polylines(mat, new[] { ptsFail }, false, red, 2, LineTypes.AntiAlias);
                    }
                }
            }
        }

        // 15. CodeDetections
        foreach (var cdt in result.CodeDetections)
        {
            if (!ShouldRender(cdt.Name)) continue;
            var cdtDef = config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, cdt.Name, StringComparison.OrdinalIgnoreCase));
            if (cdtDef is not null && cdtDef.SearchRoi.Width > 0 && cdtDef.SearchRoi.Height > 0)
            {
                DrawRotatedRoi(mat, cdtDef.SearchRoi, cyan, 1);
            }

            if (cdt.Found)
            {
                var col = green;
                if (cdt.BoundingBox.Width > 0 && cdt.BoundingBox.Height > 0)
                {
                    Cv2.Rectangle(mat, new Rect(cdt.BoundingBox.X, cdt.BoundingBox.Y, cdt.BoundingBox.Width, cdt.BoundingBox.Height), col, 2, LineTypes.AntiAlias);
                }

                Cv2.PutText(mat, $"{cdt.Name}: {cdt.Text}", new Point(cdt.BoundingBox.X, Math.Max(15, cdt.BoundingBox.Y - 5)), HersheyFonts.HersheySimplex, 0.6, col, 2, LineTypes.AntiAlias);
            }
        }

        // 16. TextNodes
        if (config.TextNodes is not null)
        {
            Dictionary<string, ConditionEvaluator.Variable>? vars = null;
            try { vars = ConditionEvaluator.BuildVariableMap(result); } catch { }

            double fontScale = (io is not null && io.TextFontSize > 0) ? (io.TextFontSize / 24.0) : 0.7;

            foreach (var t in config.TextNodes)
            {
                if (string.IsNullOrWhiteSpace(t.Name)) continue;
                var text = ConditionEvaluator.EvaluateTextTemplate(t.Text ?? string.Empty, vars);
                var col = white;
                if (vars is not null && t.Conditions is not null)
                {
                    foreach (var c in t.Conditions)
                    {
                        if (string.IsNullOrWhiteSpace(c.Expression)) continue;
                        try
                        {
                            if (ConditionEvaluator.Evaluate(c.Expression, vars))
                            {
                                col = ParseHexColorToScalar(c.Color) ?? col;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                RenderTextWithNewlines(mat, text, new Point(t.X, t.Y), HersheyFonts.HersheySimplex, fontScale, col, 2);
            }
        }
    }

    private static void RenderTextWithNewlines(Mat mat, string text, Point basePt, HersheyFonts fontFace, double fontScale, Scalar color, int thickness)
    {
        if (string.IsNullOrEmpty(text)) return;
        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        int fontHeight = Math.Max(15, (int)(32 * fontScale));
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i])) continue;
            var linePt = new Point(basePt.X, basePt.Y + i * fontHeight);
            Cv2.PutText(mat, lines[i], linePt, fontFace, fontScale, color, thickness, LineTypes.AntiAlias);
        }
    }

    private static Scalar? ParseHexColorToScalar(string hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            hex = hex.TrimStart('#');
            if (hex.Length == 8) hex = hex.Substring(2);
            if (hex.Length == 6)
            {
                var r = Convert.ToByte(hex.Substring(0, 2), 16);
                var g = Convert.ToByte(hex.Substring(2, 2), 16);
                var b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new Scalar(b, g, r);
            }
        }
        catch { }
        return null;
    }

    public void ResetTracking(string? productCode = null)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            _trackByProductCode.Clear();
            return;
        }

        _trackByProductCode.TryRemove(productCode, out _);
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

    private static (double DistPx, Point2d SegmentPt, Point2d LinePt) CalculateSegmentLineDistance(
        LineDetectResult la,
        LineDetectResult lb,
        SegmentLineDistanceMode mode,
        SegmentLineExtensionMode extensionMode,
        Roi? searchRoiA,
        Point2d originTeach,
        Point2d originFound,
        double angleDeg)
    {
        var segP1 = la.P1;
        var segP2 = la.P2;

        if (extensionMode == SegmentLineExtensionMode.ExtendToSearchRoiBounds && searchRoiA is not null && searchRoiA.Width > 0 && searchRoiA.Height > 0)
        {
            var dir = segP2 - segP1;
            if (dir.X * dir.X + dir.Y * dir.Y > 1e-9)
            {
                var roiCenter = new Point2d(searchRoiA.X + searchRoiA.Width / 2.0, searchRoiA.Y + searchRoiA.Height / 2.0);
                if (originFound.X != 0 || originFound.Y != 0)
                {
                    var rotC = Rotate(roiCenter, originTeach, angleDeg);
                    roiCenter = new Point2d(rotC.X + (originFound.X - originTeach.X), rotC.Y + (originFound.Y - originTeach.Y));
                }
                var totalAngle = angleDeg + searchRoiA.Angle;
                var halfW = searchRoiA.Width / 2.0;
                var halfH = searchRoiA.Height / 2.0;

                var rad = totalAngle * Math.PI / 180.0;
                var cos = Math.Cos(rad);
                var sin = Math.Sin(rad);

                Point2d ToLocal(Point2d g)
                {
                    var dx = g.X - roiCenter.X;
                    var dy = g.Y - roiCenter.Y;
                    return new Point2d(dx * cos + dy * sin, -dx * sin + dy * cos);
                }

                Point2d ToGlobal(Point2d loc)
                {
                    var gx = roiCenter.X + loc.X * cos - loc.Y * sin;
                    var gy = roiCenter.Y + loc.X * sin + loc.Y * cos;
                    return new Point2d(gx, gy);
                }

                var locP1 = ToLocal(segP1);
                var locP2 = ToLocal(segP2);
                var locDir = locP2 - locP1;

                if (Math.Abs(locDir.X) > 1e-9 || Math.Abs(locDir.Y) > 1e-9)
                {
                    var ts = new List<double>();
                    if (Math.Abs(locDir.X) > 1e-9)
                    {
                        var tLeft = (-halfW - locP1.X) / locDir.X;
                        var yLeft = locP1.Y + tLeft * locDir.Y;
                        if (yLeft >= -halfH - 1e-3 && yLeft <= halfH + 1e-3) ts.Add(tLeft);

                        var tRight = (halfW - locP1.X) / locDir.X;
                        var yRight = locP1.Y + tRight * locDir.Y;
                        if (yRight >= -halfH - 1e-3 && yRight <= halfH + 1e-3) ts.Add(tRight);
                    }

                    if (Math.Abs(locDir.Y) > 1e-9)
                    {
                        var tTop = (-halfH - locP1.Y) / locDir.Y;
                        var xTop = locP1.X + tTop * locDir.X;
                        if (xTop >= -halfW - 1e-3 && xTop <= halfW + 1e-3) ts.Add(tTop);

                        var tBottom = (halfH - locP1.Y) / locDir.Y;
                        var xBottom = locP1.X + tBottom * locDir.X;
                        if (xBottom >= -halfW - 1e-3 && xBottom <= halfW + 1e-3) ts.Add(tBottom);
                    }

                    if (ts.Count >= 2)
                    {
                        var minT = ts.Min();
                        var maxT = ts.Max();
                        var extLoc1 = new Point2d(locP1.X + minT * locDir.X, locP1.Y + minT * locDir.Y);
                        var extLoc2 = new Point2d(locP1.X + maxT * locDir.X, locP1.Y + maxT * locDir.Y);
                        segP1 = ToGlobal(extLoc1);
                        segP2 = ToGlobal(extLoc2);
                    }
                }
            }
        }

        static Point2d ClosestPointOnInfiniteLine(Point2d p, Point2d q1, Point2d q2)
        {
            var v = q2 - q1;
            var len2 = v.X * v.X + v.Y * v.Y;
            if (len2 <= 1e-12) return q1;
            var t = ((p.X - q1.X) * v.X + (p.Y - q1.Y) * v.Y) / len2;
            return new Point2d(q1.X + t * v.X, q1.Y + t * v.Y);
        }

        if (mode == SegmentLineDistanceMode.MidpointToInfiniteLine)
        {
            var mid = new Point2d((segP1.X + segP2.X) * 0.5, (segP1.Y + segP2.Y) * 0.5);
            var proj = ClosestPointOnInfiniteLine(mid, lb.P1, lb.P2);
            return (Geometry2D.Distance(mid, proj), mid, proj);
        }

        var c1 = ClosestPointOnInfiniteLine(segP1, lb.P1, lb.P2);
        var c2 = ClosestPointOnInfiniteLine(segP2, lb.P1, lb.P2);
        var d1 = Geometry2D.Distance(segP1, c1);
        var d2 = Geometry2D.Distance(segP2, c2);

        if (mode == SegmentLineDistanceMode.ClosestPointOnSegmentToInfiniteLine)
        {
            var vLine = lb.P2 - lb.P1;
            var cross1 = (lb.P2.X - lb.P1.X) * (segP1.Y - lb.P1.Y) - (lb.P2.Y - lb.P1.Y) * (segP1.X - lb.P1.X);
            var cross2 = (lb.P2.X - lb.P1.X) * (segP2.Y - lb.P1.Y) - (lb.P2.Y - lb.P1.Y) * (segP2.X - lb.P1.X);
            if (cross1 * cross2 <= 0 && (Math.Abs(cross1) > 1e-9 || Math.Abs(cross2) > 1e-9))
            {
                var denom = (segP2.X - segP1.X) * vLine.Y - (segP2.Y - segP1.Y) * vLine.X;
                if (Math.Abs(denom) > 1e-9)
                {
                    var tSeg = ((lb.P1.X - segP1.X) * vLine.Y - (lb.P1.Y - segP1.Y) * vLine.X) / denom;
                    var inter = new Point2d(segP1.X + tSeg * (segP2.X - segP1.X), segP1.Y + tSeg * (segP2.Y - segP1.Y));
                    return (0.0, inter, inter);
                }
            }

            return d1 <= d2 ? (d1, segP1, c1) : (d2, segP2, c2);
        }

        return d1 >= d2 ? (d1, segP1, c1) : (d2, segP2, c2);
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

    private static Mat ExtractStraightRoi(Mat source, Roi roiTeach, Point2d originTeach, Point2d originFound, double angleDeg, out Point2d centerFound)
    {
        var centerTeach = new Point2d(roiTeach.X + roiTeach.Width / 2.0, roiTeach.Y + roiTeach.Height / 2.0);
        var centerRot = Rotate(centerTeach, originTeach, angleDeg);
        var dx = originFound.X - originTeach.X;
        var dy = originFound.Y - originTeach.Y;
        centerFound = new Point2d(centerRot.X + dx, centerRot.Y + dy);

        double totalAngleDeg = angleDeg + roiTeach.Angle;

        if (Math.Abs(totalAngleDeg) < 0.001)
        {
            var rx = (int)Math.Round(centerFound.X - roiTeach.Width / 2.0);
            var ry = (int)Math.Round(centerFound.Y - roiTeach.Height / 2.0);
            var rect = new Rect(rx, ry, roiTeach.Width, roiTeach.Height).Intersect(new Rect(0, 0, source.Width, source.Height));
            if (rect.Width <= 0 || rect.Height <= 0) return new Mat();
            return new Mat(source, rect).Clone();
        }

        int diag = (int)Math.Ceiling(Math.Sqrt(roiTeach.Width * roiTeach.Width + roiTeach.Height * roiTeach.Height));
        var bbox = new Rect((int)(centerFound.X - diag / 2.0), (int)(centerFound.Y - diag / 2.0), diag, diag);
        var safeBbox = bbox.Intersect(new Rect(0, 0, source.Width, source.Height));
        if (safeBbox.Width <= 0 || safeBbox.Height <= 0) return new Mat();

        using var subSource = new Mat(source, safeBbox);
        var centerInBbox = new Point2f((float)(centerFound.X - safeBbox.X), (float)(centerFound.Y - safeBbox.Y));
        
        using var M = Cv2.GetRotationMatrix2D(centerInBbox, totalAngleDeg, 1.0);
        var tx = diag / 2.0 - centerInBbox.X;
        var ty = diag / 2.0 - centerInBbox.Y;
        M.Set(0, 2, M.Get<double>(0, 2) + tx);
        M.Set(1, 2, M.Get<double>(1, 2) + ty);
        using var rotatedBbox = new Mat();
        Cv2.WarpAffine(subSource, rotatedBbox, M, new Size(diag, diag), InterpolationFlags.Linear, BorderTypes.Replicate);

        var patch = new Mat();
        var centerInDst = new Point2f((float)(diag / 2.0), (float)(diag / 2.0));
        Cv2.GetRectSubPix(rotatedBbox, new Size(roiTeach.Width, roiTeach.Height), centerInDst, patch);
        return patch;
    }

    private static Point2d MapToGlobal(Point2d ptLocal, double w, double h, Point2d centerFound, double angleDeg)
    {
        var ptCenter = new Point2d(ptLocal.X - w / 2.0, ptLocal.Y - h / 2.0);
        var ptRot = Rotate(ptCenter, new Point2d(0, 0), angleDeg);
        return new Point2d(ptRot.X + centerFound.X, ptRot.Y + centerFound.Y);
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
            Height = (int)Math.Round(maxY - minY),
            Angle = Math.Round(roi.Angle + angleDeg, 1)
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
            Height = roi.Height,
            Angle = Math.Round(roi.Angle + angleDeg, 1)
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
            OffsetPx = p.OffsetPx,
            Algorithm = p.Algorithm,
            OriginAlgorithm = p.Algorithm switch
            {
                PointFindAlgorithm.TemplateMatch => OriginAlgorithm.TemplateMatch,
                PointFindAlgorithm.FeatureBased => OriginAlgorithm.FeatureBased,
                _ => p.OriginAlgorithm
            },
            MinAngle = p.MinAngle,
            MaxAngle = p.MaxAngle,
            AngleStep = p.AngleStep,
            EdgePoint = p.EdgePoint,

            ShapeModel = p.ShapeModel,
            EdgeThresholdMin = p.EdgeThresholdMin,
            EdgeThresholdMax = p.EdgeThresholdMax
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

public static class ConditionEvaluator
{
    internal readonly record struct ConditionValue(bool IsBool, bool Bool, double Number, string? Text)
    {
        public static ConditionValue FromBool(bool v) => new(true, v, 0.0, null);
        public static ConditionValue FromNumber(double v) => new(false, false, v, null);
        public static ConditionValue FromString(string v) => new(false, false, 0.0, v);
    }

    public sealed class Variable
    {
        public Variable(bool pass, double? value = null, double? score = null, bool? found = null, string? text = null)
        {
            Pass = pass;
            Value = value;
            Score = score;
            Found = found;
            Text = text;
        }

        public bool Pass { get; }
        public double? Value { get; }
        public double? Score { get; }
        public bool? Found { get; }
        public string? Text { get; }
    }

    public static Dictionary<string, Variable> BuildVariableMap(InspectionResult result)
    {
        var vars = new Dictionary<string, Variable>(StringComparer.OrdinalIgnoreCase);

        if (result.Origin is not null && !string.IsNullOrWhiteSpace(result.Origin.Name))
        {
            vars[result.Origin.Name] = new Variable(result.Origin.Pass, score: result.Origin.Score);
            vars[$"{result.Origin.Name}.X"] = new Variable(result.Origin.Pass, value: result.Origin.Position.X);
            vars[$"{result.Origin.Name}.Y"] = new Variable(result.Origin.Pass, value: result.Origin.Position.Y);
            vars[$"{result.Origin.Name}.Score"] = new Variable(result.Origin.Pass, value: result.Origin.Score);
            vars[$"{result.Origin.Name}.Pass"] = new Variable(result.Origin.Pass);
            vars[$"{result.Origin.Name}.Angle"] = new Variable(result.Origin.Pass, value: result.Origin.AngleDeg);
        }

        foreach (var p in result.Points)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            vars[p.Name] = new Variable(p.Pass, score: p.Score);
            vars[$"{p.Name}.X"] = new Variable(p.Pass, value: p.Position.X);
            vars[$"{p.Name}.Y"] = new Variable(p.Pass, value: p.Position.Y);
            vars[$"{p.Name}.Score"] = new Variable(p.Pass, value: p.Score);
            vars[$"{p.Name}.Pass"] = new Variable(p.Pass);
        }

        foreach (var l in result.Lines)
        {
            if (string.IsNullOrWhiteSpace(l.Name)) continue;
            vars[l.Name] = new Variable(l.Found, found: l.Found);
            vars[$"{l.Name}.Found"] = new Variable(l.Found, found: l.Found);
            vars[$"{l.Name}.Pass"] = new Variable(l.Found);
            vars[$"{l.Name}.Length"] = new Variable(l.Found, value: l.LengthPx);
        }

        foreach (var d in result.Distances)
        {
            if (string.IsNullOrWhiteSpace(d.Name)) continue;
            vars[d.Name] = new Variable(d.Pass, value: d.Value);
            vars[$"{d.Name}.Value"] = new Variable(d.Pass, value: d.Value);
            vars[$"{d.Name}.Pass"] = new Variable(d.Pass);
        }

        foreach (var dd in result.LineToLineDistances)
        {
            if (string.IsNullOrWhiteSpace(dd.Name)) continue;
            vars[dd.Name] = new Variable(dd.Pass, value: dd.Value);
            vars[$"{dd.Name}.Value"] = new Variable(dd.Pass, value: dd.Value);
            vars[$"{dd.Name}.Pass"] = new Variable(dd.Pass);
        }

        foreach (var dd in result.PointToLineDistances)
        {
            if (string.IsNullOrWhiteSpace(dd.Name)) continue;
            vars[dd.Name] = new Variable(dd.Pass, value: dd.Value);
            vars[$"{dd.Name}.Value"] = new Variable(dd.Pass, value: dd.Value);
            vars[$"{dd.Name}.Pass"] = new Variable(dd.Pass);
        }

        foreach (var sld in result.SegmentLineDistances)
        {
            if (string.IsNullOrWhiteSpace(sld.Name)) continue;
            vars[sld.Name] = new Variable(sld.Pass, value: sld.Value);
            vars[$"{sld.Name}.Value"] = new Variable(sld.Pass, value: sld.Value);
            vars[$"{sld.Name}.Pass"] = new Variable(sld.Pass);
        }

        foreach (var lpd in result.LinePairDetections)
        {
            if (string.IsNullOrWhiteSpace(lpd.Name)) continue;
            vars[lpd.Name] = new Variable(lpd.Pass, value: lpd.Value, found: lpd.Found);
            vars[$"{lpd.Name}.Value"] = new Variable(lpd.Pass, value: lpd.Value);
            vars[$"{lpd.Name}.Pass"] = new Variable(lpd.Pass);
            vars[$"{lpd.Name}.Found"] = new Variable(lpd.Pass, found: lpd.Found);
            vars[$"LPD.{lpd.Name}"] = new Variable(lpd.Pass, value: lpd.Value, found: lpd.Found);
        }

        foreach (var cf in result.CircleFinders)
        {
            if (string.IsNullOrWhiteSpace(cf.Name)) continue;
            vars[cf.Name] = new Variable(cf.Found, value: cf.RadiusPx, found: cf.Found, score: cf.Score);
            vars[$"{cf.Name}.Value"] = new Variable(cf.Found, value: cf.RadiusPx);
            vars[$"{cf.Name}.RadiusPx"] = new Variable(cf.Found, value: cf.RadiusPx);
            vars[$"{cf.Name}.CenterX"] = new Variable(cf.Found, value: cf.Center.X);
            vars[$"{cf.Name}.CenterY"] = new Variable(cf.Found, value: cf.Center.Y);
            vars[$"{cf.Name}.Found"] = new Variable(cf.Found, found: cf.Found);
            vars[$"{cf.Name}.Pass"] = new Variable(cf.Found);
            vars[$"{cf.Name}.Score"] = new Variable(cf.Found, value: cf.Score);
            vars[$"CIR.{cf.Name}"] = new Variable(cf.Found, value: cf.RadiusPx, found: cf.Found, score: cf.Score);
        }

        foreach (var a in result.Angles)
        {
            if (string.IsNullOrWhiteSpace(a.Name)) continue;
            vars[a.Name] = new Variable(a.Pass, value: a.ValueDeg);
            vars[$"{a.Name}.Value"] = new Variable(a.Pass, value: a.ValueDeg);
            vars[$"{a.Name}.Pass"] = new Variable(a.Pass);
        }

        foreach (var ep in result.EdgePairs)
        {
            if (string.IsNullOrWhiteSpace(ep.Name)) continue;
            vars[ep.Name] = new Variable(ep.Pass, value: ep.Value, found: ep.Found);
            vars[$"{ep.Name}.Value"] = new Variable(ep.Pass, value: ep.Value);
            vars[$"{ep.Name}.Pass"] = new Variable(ep.Pass);
            vars[$"{ep.Name}.Found"] = new Variable(ep.Pass, found: ep.Found);
            vars[$"EP.{ep.Name}"] = new Variable(ep.Pass, value: ep.Value, found: ep.Found);
            vars[$"EdgePair.{ep.Name}"] = new Variable(ep.Pass, value: ep.Value, found: ep.Found);
        }

        foreach (var epd in result.EdgePairDetections)
        {
            if (string.IsNullOrWhiteSpace(epd.Name)) continue;
            vars[epd.Name] = new Variable(epd.Pass, value: epd.Value, found: epd.Found);
            vars[$"{epd.Name}.Value"] = new Variable(epd.Pass, value: epd.Value);
            vars[$"{epd.Name}.Pass"] = new Variable(epd.Pass);
            vars[$"{epd.Name}.Found"] = new Variable(epd.Pass, found: epd.Found);
            vars[$"EPD.{epd.Name}"] = new Variable(epd.Pass, value: epd.Value, found: epd.Found);
            vars[$"EdgePairDetect.{epd.Name}"] = new Variable(epd.Pass, value: epd.Value, found: epd.Found);
        }

        foreach (var c in result.Conditions)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) continue;
            vars[c.Name] = new Variable(c.Pass);
            vars[$"{c.Name}.Pass"] = new Variable(c.Pass);
        }

        foreach (var b in result.BlobDetections)
        {
            if (string.IsNullOrWhiteSpace(b.Name)) continue;
            vars[b.Name] = new Variable(true, value: b.Count);
            vars[$"{b.Name}.Count"] = new Variable(true, value: b.Count);
            vars[$"{b.Name}.Value"] = new Variable(true, value: b.Count);
        }

        foreach (var sc in result.SurfaceCompares)
        {
            if (string.IsNullOrWhiteSpace(sc.Name)) continue;
            vars[sc.Name] = new Variable(sc.Pass, value: sc.Count, score: sc.MaxArea);
            vars[$"{sc.Name}.Count"] = new Variable(sc.Pass, value: sc.Count);
            vars[$"{sc.Name}.MaxArea"] = new Variable(sc.Pass, value: sc.MaxArea);
            vars[$"{sc.Name}.Pass"] = new Variable(sc.Pass);
            vars[$"SC.{sc.Name}"] = new Variable(sc.Pass, value: sc.Count, score: sc.MaxArea);
            vars[$"SurfaceCompare.{sc.Name}"] = new Variable(sc.Pass, value: sc.Count, score: sc.MaxArea);
            vars[$"SC.{sc.Name}.MaxArea"] = new Variable(sc.Pass, value: sc.MaxArea);
            vars[$"SurfaceCompare.{sc.Name}.MaxArea"] = new Variable(sc.Pass, value: sc.MaxArea);
        }

        foreach (var c in result.Calipers)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) continue;
            vars[c.Name] = new Variable(c.Found, value: c.AvgStrength, found: c.Found);
            vars[$"{c.Name}.Value"] = new Variable(c.Found, value: c.AvgStrength);
            vars[$"{c.Name}.Found"] = new Variable(c.Found, found: c.Found);
            vars[$"{c.Name}.Pass"] = new Variable(c.Found);
            vars[$"CAL.{c.Name}"] = new Variable(c.Found, value: c.AvgStrength, found: c.Found);
            vars[$"Caliper.{c.Name}"] = new Variable(c.Found, value: c.AvgStrength, found: c.Found);
        }

        foreach (var cdt in result.CodeDetections)
        {
            if (string.IsNullOrWhiteSpace(cdt.Name)) continue;
            vars[cdt.Name] = new Variable(cdt.Found, found: cdt.Found, text: cdt.Text);
            vars[$"{cdt.Name}.Text"] = new Variable(cdt.Found, text: cdt.Text);
            vars[$"{cdt.Name}.Found"] = new Variable(cdt.Found, found: cdt.Found);
            vars[$"{cdt.Name}.Pass"] = new Variable(cdt.Found);
        }

        foreach (var d in result.Diameters)
        {
            if (string.IsNullOrWhiteSpace(d.Name)) continue;
            vars[d.Name] = new Variable(d.Pass, value: d.Value, found: d.Found);
            vars[$"{d.Name}.Value"] = new Variable(d.Pass, value: d.Value);
            vars[$"{d.Name}.Pass"] = new Variable(d.Pass);
            vars[$"{d.Name}.Found"] = new Variable(d.Pass, found: d.Found);
            vars[$"CIR.{d.Name}"] = new Variable(d.Pass, value: d.Value, found: d.Found);
            vars[$"Diameter.{d.Name}"] = new Variable(d.Pass, value: d.Value, found: d.Found);
        }

        foreach (var io in result.ImageOutputs)
        {
            if (string.IsNullOrWhiteSpace(io.Name)) continue;
            vars[io.Name] = new Variable(io.Saved, found: io.Saved, text: io.SavedFilePath);
            vars[$"{io.Name}.Saved"] = new Variable(io.Saved, found: io.Saved);
            vars[$"{io.Name}.SavedFilePath"] = new Variable(io.Saved, text: io.SavedFilePath);
            vars[$"Saved.{io.Name}"] = new Variable(io.Saved, found: io.Saved, text: io.SavedFilePath);
        }

        return vars;
    }

    public static string EvaluateTextTemplate(string text, Dictionary<string, Variable>? vars)
    {
        if (string.IsNullOrEmpty(text) || vars is null || vars.Count == 0)
        {
            return text ?? string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(text, @"\{([^}]+)\}", m =>
        {
            var inner = m.Groups[1].Value?.Trim() ?? string.Empty;
            if (inner.Length == 0)
                return string.Empty;

            var fmt = string.Empty;
            var colonIdx = inner.IndexOf(':');
            if (colonIdx >= 0)
            {
                fmt = inner[(colonIdx + 1)..].Trim();
                inner = inner[..colonIdx].Trim();
            }

            if (vars.TryGetValue(inner, out var vDirect) && vDirect is not null)
            {
                object? directVal = vDirect.Text ?? (object?)vDirect.Value ?? vDirect.Found ?? vDirect.Pass;
                if (directVal is double dD)
                {
                    return string.IsNullOrWhiteSpace(fmt) ? dD.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : dD.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
                }
                if (directVal is bool bD)
                {
                    return bD ? "True" : "False";
                }
                return directVal?.ToString() ?? string.Empty;
            }

            var varName = inner;
            var prop = string.Empty;
            var dotIdx = inner.IndexOf('.');
            if (dotIdx >= 0)
            {
                varName = inner[..dotIdx].Trim();
                prop = inner[(dotIdx + 1)..].Trim();
            }

            if (string.IsNullOrWhiteSpace(varName) || !vars.TryGetValue(varName, out var v) || v is null)
            {
                return m.Value;
            }

            object? valueObj = null;
            if (string.IsNullOrWhiteSpace(prop))
            {
                valueObj = v.Text ?? (object?)v.Value ?? v.Found ?? v.Pass;
            }
            else if (string.Equals(prop, "Pass", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Pass;
            }
            else if (string.Equals(prop, "Value", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Value ?? (object?)v.Score ?? v.Found ?? v.Pass;
            }
            else if (string.Equals(prop, "Score", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Score ?? v.Value;
            }
            else if (string.Equals(prop, "Found", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Found ?? v.Pass;
            }
            else if (string.Equals(prop, "Text", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Text ?? v.Value?.ToString() ?? v.Pass.ToString();
            }
            else if (string.Equals(prop, "Count", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Value;
            }
            else if (string.Equals(prop, "MaxArea", StringComparison.OrdinalIgnoreCase) || string.Equals(prop, "Area", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Score;
            }

            if (valueObj is null)
            {
                return string.Empty;
            }

            if (valueObj is double d)
            {
                return string.IsNullOrWhiteSpace(fmt) ? d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : d.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            }

            if (valueObj is bool b)
            {
                return b ? "True" : "False";
            }

            return valueObj.ToString() ?? string.Empty;
        });
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
        String,
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

            if (ch == '"')
            {
                _i++;
                var start = _i;
                var sb = new System.Text.StringBuilder();
                while (_i < _s.Length)
                {
                    var c = _s[_i++];
                    if (c == '"')
                    {
                        return new Token(TokenKind.String, sb.Length == 0 ? _s.Substring(start, (_i - 1) - start) : sb.ToString(), 0);
                    }

                    if (c == '\\' && _i < _s.Length)
                    {
                        var next = _s[_i++];
                        sb.Append(next switch
                        {
                            '"' => '"',
                            '\\' => '\\',
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            _ => next
                        });
                        continue;
                    }

                    sb.Append(c);
                }

                throw new InvalidOperationException("Unterminated string literal");
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

            if (a.Text is not null || b.Text is not null)
            {
                var sa = a.Text ?? throw new InvalidOperationException("Left side is not a string");
                var sb = b.Text ?? throw new InvalidOperationException("Right side is not a string");
                return op switch
                {
                    TokenKind.Eq => string.Equals(sa, sb, StringComparison.Ordinal),
                    TokenKind.Ne => !string.Equals(sa, sb, StringComparison.Ordinal),
                    _ => throw new InvalidOperationException("Only == and != are allowed for strings")
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

            if (_t.Kind == TokenKind.String)
            {
                var text = _t.Text;
                Expect(TokenKind.String);
                return ConditionValue.FromString(text);
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
            if (string.Equals(member, "VALUE", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(member, "COUNT", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Value is null) throw new InvalidOperationException($"{name}.Value is not available");
                return ConditionValue.FromNumber(v.Value.Value);
            }
            if (string.Equals(member, "SCORE", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(member, "MAXAREA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member, "AREA", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Score is null) throw new InvalidOperationException($"{name}.Score is not available");
                return ConditionValue.FromNumber(v.Score.Value);
            }
            if (string.Equals(member, "FOUND", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Found is null) throw new InvalidOperationException($"{name}.Found is not available");
                return ConditionValue.FromBool(v.Found.Value);
            }

            if (string.Equals(member, "TEXT", StringComparison.OrdinalIgnoreCase))
            {
                if (v.Text is null) throw new InvalidOperationException($"{name}.Text is not available");
                return ConditionValue.FromString(v.Text);
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
