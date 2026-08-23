using System;
using System.Collections.Generic;

namespace VisionInspectionApp.Models;

public struct DefectBox
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public DefectBox(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// Mức độ nghiêm trọng của vết lỗi trên cuộn
/// </summary>
public enum DefectSeverity
{
    Info = 0,
    Warning = 1,
    Reject = 2,
    Critical = 3
}

/// <summary>
/// Đại diện cho một vết khuyết tật vật lý được ghi nhớ trên cuộn sản phẩm
/// </summary>
public sealed class RollDefectItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RollSessionId { get; set; } = string.Empty;
    public long FrameIndex { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string DefectType { get; set; } = "Defect";
    public DefectSeverity Severity { get; set; } = DefectSeverity.Reject;
    
    /// <summary>
    /// Vị trí ngang trên dải cuộn (mm tính từ mép cuộn bên trái)
    /// </summary>
    public double WebX_Mm { get; set; }

    /// <summary>
    /// Vị trí dọc mét dài tuyệt đối của vết lỗi trên cuộn (mm tính từ đầu cuộn)
    /// </summary>
    public double WebY_Mm { get; set; }

    public double Width_Mm { get; set; }
    public double Length_Mm { get; set; }
    public double Area_Mm2 { get; set; }
    public DefectBox BoundingBox { get; set; }
    public bool RejectTriggered { get; set; }
    public DateTime? RejectTriggeredTime { get; set; }
    public string? ImageCropBase64 { get; set; }
}

/// <summary>
/// Phiên kiểm tra của một cuộn sản phẩm hoàn chỉnh từ 0m đến N mét
/// </summary>
public sealed class RollSession
{
    public string SessionId { get; set; } = $"ROLL-{DateTime.Now:yyyyMMdd-HHmmss}";
    public string RollId { get => SessionId; set => SessionId = value; }
    public string LotNumber { get; set; } = "LOT-001";
    public string OperatorName { get; set; } = "Operator";
    public string JobName { get; set; } = "DefaultJob";
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public double TotalLengthMeters { get; set; }
    public double RollWidthMm { get; set; } = 500.0; // Khổ cuộn tiêu chuẩn 500mm
    public List<RollDefectItem> Defects { get; set; } = new();

    public int TotalDefects => Defects.Count;
    public int TotalDefectsCount => Defects.Count;
    public int RejectCount => Defects.FindAll(d => d.Severity >= DefectSeverity.Reject).Count;
    public int WarningCount => Defects.FindAll(d => d.Severity == DefectSeverity.Warning).Count;

    /// <summary>
    /// Tỷ lệ mét dài sản phẩm đạt tiêu chuẩn chất lượng (%)
    /// </summary>
    public double QualityYieldPercentage
    {
        get
        {
            if (TotalLengthMeters <= 0) return 100.0;
            // Tính tổng mét dài bị ảnh hưởng bởi lỗi Reject (mỗi lỗi ước tính ảnh hưởng 0.1m)
            double defectLengthMeters = RejectCount * 0.1;
            double validMeters = Math.Max(0, TotalLengthMeters - defectLengthMeters);
            return Math.Min(100.0, (validMeters / TotalLengthMeters) * 100.0);
        }
    }
}
