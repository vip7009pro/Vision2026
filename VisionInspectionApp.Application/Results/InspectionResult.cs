using System.Collections.Concurrent;
using System.Collections.Generic;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.Application;

public sealed class InspectionTimings
{
    public ConcurrentDictionary<string, int> NodeTimings { get; } = new(System.StringComparer.OrdinalIgnoreCase);
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

    public List<CropResult> Crops { get; } = new();

    public List<ColorDiffResult> ColorDiffs { get; } = new();

    public List<ImgArithmeticResult> ImgArithmetics { get; } = new();

    public List<CreatePointResult> CreatePoints { get; } = new();

    public List<CreateLineResult> CreateLines { get; } = new();

    public List<CreateRectResult> CreateRects { get; } = new();

    public List<CreateCircleResult> CreateCircles { get; } = new();

    public List<ContourCompareResult> ContourCompares { get; } = new();

    public List<LinePairDetectionResult> LinePairDetections { get; } = new();

    public List<EdgePairResult> EdgePairs { get; } = new();

    public List<EdgePairDetectResult> EdgePairDetections { get; } = new();

    public List<CircleFinderResult> CircleFinders { get; } = new();

    public List<DiameterResult> Diameters { get; } = new();

    public List<CaliperResult> Calipers { get; } = new();

    public List<CodeDetectionResult> CodeDetections { get; } = new();

    public List<ImageOutputResult> ImageOutputs { get; } = new();

    public List<DbResult> DbResults { get; } = new();

    public List<PlcReadResult> PlcReads { get; } = new();

    public List<PlcWriteResult> PlcWrites { get; } = new();

    public List<PlcWaitResult> PlcWaits { get; } = new();

    public List<PlcTriggerResult> PlcTriggers { get; } = new();

    public List<PlcBatchReadResult> PlcBatchReads { get; } = new();

    public List<PlcBatchWriteResult> PlcBatchWrites { get; } = new();

    public DefectDetectionResult? Defects { get; set; }
}
