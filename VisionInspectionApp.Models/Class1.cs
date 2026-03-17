namespace VisionInspectionApp.Models;

public enum LineLineDistanceMode
{
    ClosestPointsOnSegments = 0,
    MidpointToMidpoint = 1,
    NearestEndpoints = 2,
    FarthestEndpoints = 3
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
    HoughCircles = 0,
    ContourFit = 1,
    Ransac = 2
}

public sealed class CircleFinderDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi SearchRoi { get; set; } = new();

    public CircleFindAlgorithm Algorithm { get; set; } = CircleFindAlgorithm.ContourFit;

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

public sealed class VisionConfig
{
    public string ProductCode { get; set; } = string.Empty;

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

    public DefectInspectionConfig DefectConfig { get; set; } = new();
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

public sealed class PreprocessNodeDefinition
{
    public string Name { get; set; } = string.Empty;

    public PreprocessSettings Settings { get; set; } = new();
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

    public int MorphKernel { get; set; } = 3;
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
    public int ThresholdValue { get; set; } = 128;

    public bool UseCanny { get; set; }
    public int Canny1 { get; set; } = 50;
    public int Canny2 { get; set; } = 150;

    public bool UseMorphology { get; set; }
}

public enum IlluminationCorrectionPreset
{
    None = 0,
    BackgroundSubtract = 1,
    FlatFieldNormalize = 2,
    Clahe = 3
}

public sealed class PointDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi SearchRoi { get; set; } = new();

    public Roi TemplateRoi { get; set; } = new();

    public string TemplateImageFile { get; set; } = string.Empty;

    public ShapeModelDefinition? ShapeModel { get; set; }

    public double MatchScoreThreshold { get; set; } = 0.8;

    public Point2dModel WorldPosition { get; set; } = new();

    public Point2dModel OffsetPx { get; set; } = new();
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
