using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.Application;

public sealed class InspectionTimings
{
    public ConcurrentDictionary<string, int> NodeTimings { get; } = new(System.StringComparer.OrdinalIgnoreCase);
    public int TotalMs { get; set; }

    // ─────────────────────────────────────────────────────────────────────
    // Các pha "ẩn" nằm TRONG TotalMs nhưng không thuộc tool nào.
    // Trước đây chúng không được đo nên tổng thời gian các tool luôn nhỏ hơn TotalMs
    // (ví dụ Total=27ms nhưng Origin 15 + CAM1 4 = 19ms).
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Thời gian hiệu chuẩn (EnsureCalibration) + Undistort ảnh full-size. Nằm TRONG TotalMs.</summary>
    public int CalibrationUndistortMs { get; set; }

    /// <summary>
    /// Thời gian "khởi tạo khung chạy": dựng chỉ mục node/edge, các dictionary tra cứu,
    /// các Lazy/cache và khai báo local function. Nằm TRONG TotalMs.
    /// </summary>
    public int FrameworkSetupMs { get; set; }

    /// <summary>Thời gian nạp + chuyển ảnh template của tool Origin (file I/O + convert gray). Nằm TRONG TotalMs.</summary>
    public int OriginTemplateLoadMs { get; set; }

    /// <summary>
    /// Tổng thời gian các heavy tool PHẢI CHỜ slot chạy (semaphore gate) — tức thời gian
    /// xếp hàng/scheduling, KHÔNG phải thời gian tính toán của tool. Nằm TRONG TotalMs.
    /// </summary>
    public int ToolQueueWaitMs { get; set; }

    /// <summary>Thời gian Preprocess toàn ảnh mặc định (global/lazy preprocess). Nằm TRONG TotalMs.</summary>
    public int GlobalPreprocessMs { get; set; }

    /// <summary>
    /// Thời gian khối Defect SAU KHI trừ phần preprocess toàn ảnh dùng chung
    /// (vì <see cref="GlobalPreprocessMs"/> xảy ra bên trong cửa sổ đo Defect).
    /// </summary>
    public int DefectsNetMs => System.Math.Max(0, DefectsMs - GlobalPreprocessMs);

    /// <summary>
    /// Thời gian chuẩn bị ảnh nguồn (chụp camera / đọc file / tải URL), đo Ở TẦNG UI.
    /// ⚠️ Giá trị này NẰM NGOÀI TotalMs — TotalMs chỉ đo bên trong InspectionService.Inspect().
    /// </summary>
    public int SourceCaptureMs { get; set; }

    /// <summary>
    /// Tên các node nguồn ảnh (ImageSource) mà UI đã ghi timing vào <see cref="NodeTimings"/>.
    /// Các node này không được tính vào tổng thời gian bên trong engine.
    /// </summary>
    public HashSet<string> SourceNodeNames { get; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tổng thời gian các tool/node đo được BÊN TRONG engine (đã loại các node nguồn ảnh
    /// vốn được đo ở tầng UI và nằm ngoài TotalMs).
    /// </summary>
    public int EngineNodeSumMs
    {
        get
        {
            var sum = 0;
            foreach (var kv in NodeTimings)
            {
                if (!SourceNodeNames.Contains(kv.Key))
                {
                    sum += kv.Value;
                }
            }
            return sum;
        }
    }

    /// <summary>
    /// Phần thời gian còn lại của TotalMs sau khi đã TRỪ HẾT mọi pha đo được:
    /// hiệu chuẩn/undistort, khởi tạo khung chạy, nạp template Origin, chờ slot tool,
    /// preprocess toàn ảnh, Σ thời gian từng tool, điều kiện logic và defect.
    ///
    /// Đây là phần "ghép kết quả &amp; sai số làm tròn" (mỗi tool được làm tròn XUỐNG theo ms).
    /// Mục tiêu: luôn nhỏ; nếu phình to nghĩa là có pha chưa được đo ở nơi khác.
    /// </summary>
    public int ResultAssemblyMs
    {
        get
        {
            var accounted = CalibrationUndistortMs
                          + FrameworkSetupMs
                          + OriginTemplateLoadMs
                          + ToolQueueWaitMs
                          + GlobalPreprocessMs
                          + ConditionsMs
                          + DefectsNetMs
                          + EngineNodeSumMs;

            return System.Math.Max(0, TotalMs - accounted);
        }
    }

    /// <summary>Tổng các pha đã đo được (không gồm <see cref="ResultAssemblyMs"/>).</summary>
    public int AccountedMs => TotalMs - ResultAssemblyMs;
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
    public int OcrMs { get; set; }
}

public sealed class InspectionResult
{
    public bool Pass { get; set; }

    /// <summary>
    /// Siêu dữ liệu vị trí Encoder, Timestamp và toạ độ cuộn vật lý
    /// </summary>
    public FrameMetadata? Metadata { get; set; }

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
    public List<OcrResult> Ocrs { get; } = new();

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
