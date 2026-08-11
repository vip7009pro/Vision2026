namespace VisionInspectionApp.Models;

public enum LineLineDistanceMode
{
    ClosestPointsOnSegments = 0,
    MidpointToMidpoint = 1,
    NearestEndpoints = 2,
    FarthestEndpoints = 3,
    ExtendToOtherEndpoints = 4
}

public sealed class EdgePairDetectDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi SearchRoi { get; set; } = new();

    public CaliperOrientation Orientation { get; set; } = CaliperOrientation.Vertical;

    public EdgePolarity Polarity { get; set; } = EdgePolarity.Any;

    public int StripCount { get; set; } = 10;

    public int StripWidth { get; set; } = 7;

    public int StripLength { get; set; } = 60;

    public double MinEdgeStrength { get; set; } = 10.0;

    public int MinEdgeSeparationPx { get; set; } = 10;

    public double Nominal { get; set; }

    public double TolerancePlus { get; set; }

    public double ToleranceMinus { get; set; }
}

public enum CircleFindAlgorithm
{
    RadialCaliper = 0,
    ContourFit = 1,
    HoughCircles = 2,
    Ransac = 3
}

public sealed class CircleFinderDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi SearchRoi { get; set; } = new();

    public CircleFindAlgorithm Algorithm { get; set; } = CircleFindAlgorithm.RadialCaliper;

    // RadialCaliper params
    public int StripCount { get; set; } = 32;

    public int StripWidth { get; set; } = 10;

    public int StripLength { get; set; } = 40;

    public EdgePolarity Polarity { get; set; } = EdgePolarity.Any;

    public EdgeSelection EdgeSelection { get; set; } = EdgeSelection.MaxStrength;

    public int MinEdgeStrength { get; set; } = 15;

    public double MinAngleDeg { get; set; } = 0.0;

    public double MaxAngleDeg { get; set; } = 360.0;

    // Common constraints
    public int MinRadiusPx { get; set; } = 0;

    public int MaxRadiusPx { get; set; } = 0;

    // HoughCircles params
    public double HoughDp { get; set; } = 1.2;

    public double HoughMinDistPx { get; set; } = 20;

    public double HoughParam1 { get; set; } = 120;

    public double HoughParam2 { get; set; } = 30;

    // ContourFit params
    public int Canny1 { get; set; } = 80;

    public int Canny2 { get; set; } = 200;

    public double MinCircularity { get; set; } = 0.6;
}

public sealed class DiameterDefinition
{
    public string Name { get; set; } = string.Empty;

    public string CircleRef { get; set; } = string.Empty;

    public double Nominal { get; set; }

    public double TolerancePlus { get; set; }

    public double ToleranceMinus { get; set; }
}

public enum PointLineDistanceMode
{
    PointToSegment = 0,
    PointToInfiniteLine = 1
}

public enum BlobPolarity
{
    DarkOnLight = 0,
    LightOnDark = 1
}

public enum BlobRoiMode
{
    Include = 0,
    Exclude = 1
}

public enum CodeSymbology
{
    Qr = 0,
    Barcode1D = 1,
    DataMatrix = 2,
    Pdf417 = 3,
    Aztec = 4
}

public enum ImageSourceType
{
    File = 0,
    Folder = 1,
    Camera = 2
}

public enum ImageSourceTriggerMode
{
    SoftTrigger = 0,
    LineTrigger = 1,
    PlcTrigger = 2
}

public sealed class ImageSourceDefinition
{
    public string Name { get; set; } = string.Empty;

    public ImageSourceType SourceType { get; set; } = ImageSourceType.File;

    public ImageSourceTriggerMode TriggerMode { get; set; } = ImageSourceTriggerMode.SoftTrigger;

    public string FilePath { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;

    public int CameraIndex { get; set; } = 0;

    public string RtspUrl { get; set; } = string.Empty;

    public bool LoopFolder { get; set; } = true;

    public int FolderIntervalMs { get; set; } = 1000;

    // Line Trigger (Hardware Sensor Signal)
    public string LineTriggerName { get; set; } = "Line1";

    // PLC Trigger (PLC Tag Signal)
    public string PlcTriggerPlcId { get; set; } = "PLC1";

    public string PlcTriggerTagName { get; set; } = "X0_Trigger";

    public PlcTriggerEdge PlcTriggerEdge { get; set; } = PlcTriggerEdge.RisingEdge;
}

public sealed class VisionConfig
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    public double PixelsPerMm { get; set; } = 1.0;

    public ToolGraph ToolGraph { get; set; } = new();

    public PreprocessSettings Preprocess { get; set; } = new();

    public List<PreprocessNodeDefinition> PreprocessNodes { get; set; } = new();

    public PointDefinition Origin { get; set; } = new();

    public List<PointDefinition> Points { get; set; } = new();

    public List<LineToolDefinition> Lines { get; set; } = new();

    public List<CaliperDefinition> Calipers { get; set; } = new();

    public List<LineDistance> Distances { get; set; } = new();

    public List<LineToLineDistance> LineToLineDistances { get; set; } = new();

    public List<PointToLineDistance> PointToLineDistances { get; set; } = new();

    public List<SegmentLineDistance> SegmentLineDistances { get; set; } = new();

    public List<AngleDefinition> Angles { get; set; } = new();

    public List<ConditionDefinition> Conditions { get; set; } = new();

    public List<BlobDetectionDefinition> BlobDetections { get; set; } = new();

    public List<LinePairDetectionDefinition> LinePairDetections { get; set; } = new();

    public List<EdgePairDefinition> EdgePairs { get; set; } = new();

    public List<EdgePairDetectDefinition> EdgePairDetections { get; set; } = new();

    public List<CircleFinderDefinition> CircleFinders { get; set; } = new();

    public List<DiameterDefinition> Diameters { get; set; } = new();

    public List<CodeDetectionDefinition> CodeDetections { get; set; } = new();

    public List<SurfaceCompareDefinition> SurfaceCompares { get; set; } = new();

    public List<ContourCompareDefinition> ContourCompares { get; set; } = new();

    public List<TextNodeDefinition> TextNodes { get; set; } = new();

    public List<ImageSourceDefinition> ImageSources { get; set; } = new();

    public List<ImageOutputDefinition> ImageOutputs { get; set; } = new();

    public List<PlcModel> Plcs { get; set; } = new();

    public List<PlcTag> PlcTags { get; set; } = new();

    public List<PlcReadDefinition> PlcReads { get; set; } = new();

    public List<PlcWriteDefinition> PlcWrites { get; set; } = new();

    public List<PlcWaitDefinition> PlcWaits { get; set; } = new();

    public List<PlcTriggerDefinition> PlcTriggers { get; set; } = new();

    public List<PlcBatchReadDefinition> PlcBatchReads { get; set; } = new();

    public List<PlcBatchWriteDefinition> PlcBatchWrites { get; set; } = new();

    public List<ResultTransferDefinition> ResultTransfers { get; set; } = new();

    public List<DbModel> Databases { get; set; } = new();

    public List<DbNodeDefinition> DbNodes { get; set; } = new();

    public DefectInspectionConfig DefectConfig { get; set; } = new();
}

public enum ImageOutputFormat
{
    PNG,
    JPG,
    BMP
}

public enum ImageOutputCondition
{
    Always,
    OnPass,
    OnFail
}

public sealed class ImageOutputDefinition
{
    public string Name { get; set; } = string.Empty;

    public string InputNodeName { get; set; } = string.Empty;

    public string SaveFolderPath { get; set; } = @"C:\VisionOutput";

    public string FileNameFormat { get; set; } = "IMG_{YYYY}{MM}{DD}_{HH}{mm}{ss}_{Count}";

    public ImageOutputFormat Format { get; set; } = ImageOutputFormat.PNG;

    public bool EnableOutput { get; set; } = true;

    public bool IncludeOverlay { get; set; } = true;

    public bool ShowRoi { get; set; } = true;

    public int TextFontSize { get; set; } = 18;

    public ImageOutputCondition SaveCondition { get; set; } = ImageOutputCondition.Always;
}

public sealed class TextColorConditionDefinition
{
    public string Expression { get; set; } = string.Empty;

    public string Color { get; set; } = "#FF00FF00";
}

public sealed class TextNodeDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public int X { get; set; }

    public int Y { get; set; }

    public string DefaultColor { get; set; } = "#FFFFFFFF";

    public List<TextColorConditionDefinition> Conditions { get; set; } = new();
}

public sealed class EdgePairDefinition
{
    public string Name { get; set; } = string.Empty;

    public string RefA { get; set; } = string.Empty;

    public string RefB { get; set; } = string.Empty;

    public double Nominal { get; set; }

    public double TolerancePlus { get; set; }

    public double ToleranceMinus { get; set; }
}

public sealed class AngleDefinition
{
    public string Name { get; set; } = string.Empty;

    public string LineA { get; set; } = string.Empty;

    public string LineB { get; set; } = string.Empty;

    public double Nominal { get; set; }

    public double TolerancePlus { get; set; }

    public double ToleranceMinus { get; set; }
}

public enum CaliperOrientation
{
    Vertical = 0,
    Horizontal = 1
}

public enum EdgePolarity
{
    Any = 0,
    DarkToLight = 1,
    LightToDark = 2
}

public enum EdgeSelection
{
    MaxStrength = 0,
    First = 1,
    Last = 2
}

public sealed class CaliperDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi SearchRoi { get; set; } = new();

    public CaliperOrientation Orientation { get; set; } = CaliperOrientation.Vertical;

    public EdgePolarity Polarity { get; set; } = EdgePolarity.Any;

    public int StripCount { get; set; } = 10;

    public int StripWidth { get; set; } = 7;

    public int StripLength { get; set; } = 60;

    public double MinEdgeStrength { get; set; } = 10.0;
}

public sealed class LinePairDetectionDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi SearchRoi { get; set; } = new();

    public int Canny1 { get; set; } = 50;

    public int Canny2 { get; set; } = 150;

    public int HoughThreshold { get; set; } = 60;

    public int MinLineLength { get; set; } = 50;

    public int MaxLineGap { get; set; } = 20;

    public double Nominal { get; set; }

    public double TolerancePlus { get; set; }

    public double ToleranceMinus { get; set; }
}

public sealed class CodeDetectionDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi SearchRoi { get; set; } = new();

    public List<CodeSymbology> Symbologies { get; set; } = new();

    public bool TryHarder { get; set; } = true;
}

public enum PreprocessRoiShape
{
    Rectangle = 0,
    Circle = 1,
    Polygon = 2
}

public enum PreprocessRoiMode
{
    Include = 0,
    Exclude = 1
}

public sealed class PreprocessRoiDefinition
{
    public PreprocessRoiShape Shape { get; set; } = PreprocessRoiShape.Rectangle;
    public PreprocessRoiMode Mode { get; set; } = PreprocessRoiMode.Include;

    // Rectangle
    public int X { get; set; } = 50;
    public int Y { get; set; } = 50;
    public int Width { get; set; } = 200;
    public int Height { get; set; } = 200;
    public double Angle { get; set; } = 0.0;

    // Circle (Center X, Y, Radius)
    public int CircleCenterX { get; set; } = 150;
    public int CircleCenterY { get; set; } = 150;
    public int CircleRadius { get; set; } = 50;

    // Polygon
    public List<Point2dModel> PolygonPoints { get; set; } = new();
}

public sealed class PreprocessNodeDefinition
{
    public string Name { get; set; } = string.Empty;

    public PreprocessSettings Settings { get; set; } = new();

    public List<PreprocessRoiDefinition> Rois { get; set; } = new();
}

public sealed class ConditionDefinition
{
    public string Name { get; set; } = string.Empty;

    public int InputCount { get; set; } = 2;

    public string Expression { get; set; } = string.Empty;
}

public sealed class BlobDetectionDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi InspectRoi { get; set; } = new();

    public List<BlobRoiDefinition> Rois { get; set; } = new();

    public BlobPolarity Polarity { get; set; } = BlobPolarity.DarkOnLight;

    public int Threshold { get; set; } = 128;

    public int MinBlobArea { get; set; } = 10;

    public int MaxBlobArea { get; set; } = 5000;
}

public sealed class BlobRoiDefinition
{
    public Roi Roi { get; set; } = new();

    public BlobRoiMode Mode { get; set; } = BlobRoiMode.Include;
}

public sealed class SurfaceCompareDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi TemplateRoi { get; set; } = new();

    public string TemplateImageFile { get; set; } = string.Empty;

    public Roi InspectRoi { get; set; } = new();

    public List<SurfaceCompareRoiDefinition> Rois { get; set; } = new();

    public int DiffThreshold { get; set; } = 25;

    public int MinBlobArea { get; set; } = 10;

    public int MaxBlobArea { get; set; } = 5000;
    
    public int MinCount { get; set; } = 0;

    public int MaxCount { get; set; } = 0;

    public int MorphKernel { get; set; } = 3;

    public int EdgeTolerancePx { get; set; } = 0;

    public SurfaceCompareAlgorithm Algorithm { get; set; } = SurfaceCompareAlgorithm.AbsDiff;

    public int SsimWindowSize { get; set; } = 7;

    public double SsimThreshold { get; set; } = 0.85;

    public double GradientWeight { get; set; } = 0.5;

    public bool AutoAlign { get; set; } = false;

    public int AutoAlignMaxShiftPx { get; set; } = 5;
}

public enum SurfaceCompareAlgorithm
{
    AbsDiff = 0,
    SSIM = 1,
    GradientAdaptive = 2
}

public enum ContourMatchMethod
{
    HuMoments = 0,
    HausdorffDistance = 1,
    AreaPerimeterDiff = 2
}

public sealed class ContourCompareDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi TemplateRoi { get; set; } = new();

    public string TemplateImageFile { get; set; } = string.Empty;

    public Roi InspectRoi { get; set; } = new();

    public string PreprocessChoice { get; set; } = string.Empty;

    public double CannyThreshold1 { get; set; } = 50;

    public double CannyThreshold2 { get; set; } = 150;

    public int MinContourArea { get; set; } = 50;

    public ContourMatchMethod MatchMethod { get; set; } = ContourMatchMethod.HuMoments;

    public double MaxShapeMatchScore { get; set; } = 0.10;

    public double MaxHausdorffDistPx { get; set; } = 5.0;

    public double MaxAreaDiffPercent { get; set; } = 5.0;
}

public sealed class SurfaceCompareRoiDefinition
{
    public Roi Roi { get; set; } = new();

    public BlobRoiMode Mode { get; set; } = BlobRoiMode.Include;
}

public sealed class ToolGraph
{
    public List<ToolGraphNode> Nodes { get; set; } = new();

    public List<ToolGraphEdge> Edges { get; set; } = new();
}

public sealed class ToolGraphNode
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string RefName { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public int InputCount { get; set; } = 1;
}

public sealed class ToolGraphEdge
{
    public string FromNodeId { get; set; } = string.Empty;

    public string ToNodeId { get; set; } = string.Empty;

    public string FromPort { get; set; } = string.Empty;

    public string ToPort { get; set; } = string.Empty;
}

public sealed class PreprocessSettings
{
    public IlluminationCorrectionPreset IlluminationCorrection { get; set; } = IlluminationCorrectionPreset.None;

    public int IlluminationKernel { get; set; } = 51;

    public double ClaheClipLimit { get; set; } = 2.0;

    public int ClaheTileGrid { get; set; } = 8;

    public bool UseGray { get; set; } = true;

    public bool UseGaussianBlur { get; set; }
    public int BlurKernel { get; set; } = 3;

    public bool UseThreshold { get; set; }
    public PreprocessThresholdType ThresholdType { get; set; } = PreprocessThresholdType.Binary;
    public int ThresholdValue
    {
        get => ThresholdLow;
        set => ThresholdLow = value;
    }
    public int ThresholdLow { get; set; } = 128;
    public int ThresholdHigh { get; set; } = 255;
    public bool InvertBinary { get; set; }

    public int MaskWidth { get; set; } = 11;
    public int MaskHeight { get; set; } = 11;
    public double LocalOffset { get; set; } = 10.0;
    public bool InvertLocal { get; set; }

    public bool UseCanny { get; set; }
    public int Canny1 { get; set; } = 50;
    public int Canny2 { get; set; } = 150;

    public bool UseMorphology { get; set; }
}

public enum PreprocessThresholdType
{
    Binary = 0,
    Local = 1
}

public enum IlluminationCorrectionPreset
{
    None = 0,
    BackgroundSubtract = 1,
    FlatFieldNormalize = 2,
    Clahe = 3
}

public enum OriginAlgorithm
{
    ShapeBased = 0,
    TemplateMatch = 1,
    FeatureBased = 2,
    TemplateMatchPyramid = 3,
    ShapePyramid = 4,
    MvpShapeMatch = 5
}

public enum DetectionRoiMode
{
    PartGraph = 0,
    FullGraph = 1
}

public enum PointFindAlgorithm
{
    TemplateMatch = 0,
    EdgePoint = 1,
    FeatureBased = 2,
    ShapeBased = 3,
    ShapePyramid = 4,
    MvpShapeMatch = 5,
    MvpShapePyramid = 6
}

public sealed class EdgePointSettings
{
    public CaliperOrientation Orientation { get; set; } = CaliperOrientation.Vertical;

    public EdgePolarity Polarity { get; set; } = EdgePolarity.Any;

    public int StripCount { get; set; } = 10;

    public int StripWidth { get; set; } = 7;

    public int StripLength { get; set; } = 60;

    public double MinEdgeStrength { get; set; } = 10.0;
}

public sealed class PointDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi SearchRoi { get; set; } = new();

    public Roi TemplateRoi { get; set; } = new();

    public string TemplateImageFile { get; set; } = string.Empty;

    public ShapeModelDefinition? ShapeModel { get; set; }

    public double MatchScoreThreshold { get; set; } = 0.8;

    public double MinScore
    {
        get => MatchScoreThreshold;
        set => MatchScoreThreshold = value;
    }

    public PointFindAlgorithm Algorithm { get; set; } = PointFindAlgorithm.TemplateMatch;

    public OriginAlgorithm OriginAlgorithm { get; set; } = OriginAlgorithm.ShapeBased;

    public double MinAngle { get; set; } = -20.0;
    public double MaxAngle { get; set; } = 20.0;
    public double AngleStep { get; set; } = 1.0;

    public EdgePointSettings EdgePoint { get; set; } = new();

    public Point2dModel WorldPosition { get; set; } = new();

    public Point2dModel OffsetPx { get; set; } = new();

    public int EdgeThresholdMin { get; set; } = 50;
    
    public int EdgeThresholdMax { get; set; } = 150;

    // Chinese MVP Software Shape Matching properties
    public bool MvpAutoThresh { get; set; } = true;

    public int MvpEdgeThreshold { get; set; } = 19;

    public int MvpLengthThreshold { get; set; } = 13;

    public int MvpMaxPyramidLayers { get; set; } = 6;

    public bool MvpLockOriginCenter { get; set; } = true;

    public double MvpOriginX { get; set; }

    public double MvpOriginY { get; set; }

    public DetectionRoiMode MvpDetectionRoiMode { get; set; } = DetectionRoiMode.PartGraph;

    public byte[]? MvpEraserMask { get; set; }
}

public sealed class ShapeModelDefinition
{
    public int TemplateWidth { get; set; }
    public int TemplateHeight { get; set; }

    public int BinCount { get; set; } = 16;

    public int FeatureCount { get; set; }

    public List<ShapeFeatureDefinition> Features { get; set; } = new();
}

public sealed class ShapeFeatureDefinition
{
    public int Dx { get; set; }
    public int Dy { get; set; }

    public int Bin { get; set; }

    public int Weight { get; set; }
}

public sealed class LineToolDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi SearchRoi { get; set; } = new();

    public int Canny1 { get; set; } = 50;

    public int Canny2 { get; set; } = 150;

    public int HoughThreshold { get; set; } = 50;

    public int MinLineLength { get; set; } = 30;

    public int MaxLineGap { get; set; } = 10;
}

public sealed class Roi
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Angle { get; set; } = 0.0;
}

public sealed class LineDistance
{
    public string Name { get; set; } = string.Empty;

    public string PointA { get; set; } = string.Empty;
    public string PointB { get; set; } = string.Empty;

    public double Nominal { get; set; }
    public double TolerancePlus { get; set; }
    public double ToleranceMinus { get; set; }
}

public enum SegmentLineDistanceMode
{
    ClosestPointOnSegmentToInfiniteLine = 0,
    FarthestPointOnSegmentToInfiniteLine = 1,
    MidpointToInfiniteLine = 2
}

public enum SegmentLineExtensionMode
{
    ActualDetectedSegment = 0,
    ExtendToSearchRoiBounds = 1
}

public sealed class SegmentLineDistance
{
    public string Name { get; set; } = string.Empty;

    public string LineA { get; set; } = string.Empty; // Segment Line

    public string LineB { get; set; } = string.Empty; // Infinite Line

    public double Nominal { get; set; }

    public double TolerancePlus { get; set; }

    public double ToleranceMinus { get; set; }

    public SegmentLineDistanceMode Mode { get; set; } = SegmentLineDistanceMode.ClosestPointOnSegmentToInfiniteLine;

    public SegmentLineExtensionMode ExtensionMode { get; set; } = SegmentLineExtensionMode.ActualDetectedSegment;
}

public sealed class LineToLineDistance
{
    public string Name { get; set; } = string.Empty;

    public string LineA { get; set; } = string.Empty;

    public string LineB { get; set; } = string.Empty;

    public double Nominal { get; set; }

    public double TolerancePlus { get; set; }

    public double ToleranceMinus { get; set; }

    public LineLineDistanceMode Mode { get; set; } = LineLineDistanceMode.ClosestPointsOnSegments;
}

public sealed class PointToLineDistance
{
    public string Name { get; set; } = string.Empty;

    public string Point { get; set; } = string.Empty;

    public string Line { get; set; } = string.Empty;

    public double Nominal { get; set; }

    public double TolerancePlus { get; set; }

    public double ToleranceMinus { get; set; }

    public PointLineDistanceMode Mode { get; set; } = PointLineDistanceMode.PointToSegment;
}

public sealed class DefectInspectionConfig
{
    public Roi InspectRoi { get; set; } = new();

    public int ThresholdWhite { get; set; } = 220;
    public int ThresholdBlack { get; set; } = 30;

    public int MinBlobSize { get; set; } = 10;
    public int MaxBlobSize { get; set; } = 5000;
}

public sealed class Point2dModel
{
    public double X { get; set; }
    public double Y { get; set; }
}
