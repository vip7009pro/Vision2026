using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.Application;

public interface IConfigService
{
    VisionConfig LoadConfig(string productCode);

    void SaveConfig(VisionConfig config);
}

public sealed class ConfigStoreOptions
{
    public string ConfigRootDirectory { get; set; } = "configs";
}

public sealed record PointMatchResult(string Name, Point2d Position, double Score, double Threshold, bool Pass, double AngleDeg);

public sealed class InspectionResult
{
    public bool Pass { get; set; }

    public PointMatchResult? Origin { get; set; }

    public List<PointMatchResult> Points { get; } = new();

    public List<DistanceCheckResult> Distances { get; } = new();

    public DefectDetectionResult? Defects { get; set; }
}

public interface IInspectionService
{
    InspectionResult Inspect(Mat image, VisionConfig config);
}

public sealed class InspectionService : IInspectionService
{
    private readonly ImagePreprocessor _preprocessor;
    private readonly PatternMatcher _matcher;
    private readonly DistanceCalculator _distanceCalculator;
    private readonly IDefectDetector _defectDetector;

    public InspectionService(
        ImagePreprocessor preprocessor,
        PatternMatcher matcher,
        DistanceCalculator distanceCalculator,
        IDefectDetector defectDetector)
    {
        _preprocessor = preprocessor;
        _matcher = matcher;
        _distanceCalculator = distanceCalculator;
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

        using var processed = _preprocessor.Run(image, config.Preprocess);

        var result = new InspectionResult();

        var originMatch = _matcher.MatchWithRotation(processed, config.Origin, config.Preprocess, -60.0, 60.0, 2.0);
        var templateAngleDeg = originMatch.AngleDeg;
        var poseAngleDeg = -templateAngleDeg;
        var originPass = originMatch.Score >= config.Origin.MatchScoreThreshold;
        result.Origin = new PointMatchResult(
            config.Origin.Name,
            originMatch.Position,
            originMatch.Score,
            config.Origin.MatchScoreThreshold,
            originPass,
            poseAngleDeg);

        var originTeach = new Point2d(config.Origin.WorldPosition.X, config.Origin.WorldPosition.Y);
        var originFound = originMatch.Position;
        var angleDeg = poseAngleDeg;

        var foundPoints = new Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in config.Points)
        {
            var def = TransformPointDefinition(p, originTeach, originFound, angleDeg);
            var m = _matcher.MatchWithFixedRotation(processed, def, templateAngleDeg, config.Preprocess);
            var pass = m.Score >= p.MatchScoreThreshold;

            result.Points.Add(new PointMatchResult(p.Name, m.Position, m.Score, p.MatchScoreThreshold, pass, 0.0));
            foundPoints[p.Name] = m.Position;
        }

        foreach (var d in config.Distances)
        {
            if (!foundPoints.TryGetValue(d.PointA, out var a) || !foundPoints.TryGetValue(d.PointB, out var b))
            {
                result.Distances.Add(new DistanceCheckResult(d.Name, d.PointA, d.PointB, double.NaN, d.Nominal, d.TolerancePlus, d.ToleranceMinus, false));
                continue;
            }

            result.Distances.Add(_distanceCalculator.CheckDistance(d, a, b));
        }

        var defectConfig = TransformDefectConfig(config.DefectConfig, originTeach, originFound, angleDeg);
        result.Defects = _defectDetector.Detect(processed, defectConfig);

        result.Pass = originPass
            && result.Points.All(x => x.Pass)
            && result.Distances.All(x => x.Pass)
            && (result.Defects.Defects.Count == 0);

        return result;
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
            WorldPosition = p.WorldPosition
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
