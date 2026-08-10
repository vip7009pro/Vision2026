using System.Collections.Generic;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application;

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

public sealed record PointMatchResult(
    string Name, 
    Point2d Position, 
    Rect MatchRect, 
    double Score, 
    double Threshold, 
    bool Pass, 
    double AngleDeg, 
    List<Point2d>? FeaturePoints = null);

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
