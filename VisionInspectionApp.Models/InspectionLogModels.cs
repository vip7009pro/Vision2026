using System;
using System.Collections.Generic;

namespace VisionInspectionApp.Models;

/// <summary>
/// Đại diện cho một phiên làm việc / phiên chạy kiểm tra (Session)
/// Mỗi lần bấm Run Continuous hoặc chạy Batch sẽ tạo 1 Session
/// </summary>
public sealed class InspectionSessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionCode { get; set; } = "";
    public string ProductName { get; set; } = "Chưa gán";
    public string JobFilePath { get; set; } = "";
    public string Material { get; set; } = "-";
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime? EndTime { get; set; }
    public int TotalParts { get; set; }
    public int PassParts { get; set; }
    public int FailParts { get; set; }
    public double YieldPercent => TotalParts > 0 ? (PassParts * 100.0 / TotalParts) : 0.0;
    public bool IsRunning { get; set; } = true;
    public string Notes { get; set; } = "";

    public TimeSpan Duration => (EndTime ?? DateTime.Now) - StartTime;
    public string FormattedDuration => $"{(int)Duration.TotalHours:D2}:{Duration.Minutes:D2}:{Duration.Seconds:D2}";
    public string FormattedStartTime => StartTime.ToString("yyyy-MM-dd HH:mm:ss");
    public string FormattedEndTime => EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Đang chạy...";
    public string FormattedYield => $"{YieldPercent:F1}%";
}

/// <summary>
/// Đại diện cho kết quả kiểm tra của 1 con hàng (Part/Frame)
/// </summary>
public sealed class InspectionPartRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public int PartIndex { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool Pass { get; set; } = true;
    public string DetailedReason { get; set; } = "";
    public List<InspectionItemMeasurement> Measurements { get; set; } = new();

    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");
    public string StatusText => Pass ? "OK" : "NG";
}

/// <summary>
/// Kết quả đo đạc chi tiết của một hạng mục / công cụ kiểm tra trên 1 con hàng
/// </summary>
public sealed class InspectionItemMeasurement
{
    public string ItemName { get; set; } = "";
    public string ToolType { get; set; } = "";
    public double Nominal { get; set; }
    public double TolPlus { get; set; }
    public double TolMinus { get; set; }
    public double Lsl => Nominal - Math.Abs(TolMinus);
    public double Usl => Nominal + Math.Abs(TolPlus);
    public double MeasuredValue { get; set; }
    public string Unit { get; set; } = "mm";
    public bool Pass { get; set; } = true;
    public string Judge => Pass ? "OK" : "NG";

    public string FormattedNominal => $"{Nominal:F3}";
    public string FormattedLsl => $"{Lsl:F3}";
    public string FormattedUsl => $"{Usl:F3}";
    public string FormattedValue => $"{MeasuredValue:F3}";
    public string FormattedTolerance => $"+{TolPlus:F3} / -{Math.Abs(TolMinus):F3}";
}

/// <summary>
/// Dữ liệu một nhóm con (Subgroup) trong tính toán SPC (cỡ mẫu n)
/// </summary>
public sealed class SpcSubgroupData
{
    public int GroupIndex { get; set; }
    public double Mean { get; set; }
    public double Range { get; set; }
    public double Sigma { get; set; }
    public double Cpk { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public List<double> Values { get; set; } = new();
}

/// <summary>
/// Dữ liệu một cột tần suất (Histogram Bin)
/// </summary>
public sealed class HistogramBinData
{
    public int BinIndex { get; set; }
    public double BinStart { get; set; }
    public double BinEnd { get; set; }
    public double BinCenter => (BinStart + BinEnd) / 2.0;
    public int Count { get; set; }
    public double NormalCurveHeight { get; set; }
    public string FormattedRange => $"{BinStart:F3}..{BinEnd:F3}";
}

/// <summary>
/// Tổng hợp kết quả phân tích thống kê SPC & CPK cho 1 hạng mục đo
/// </summary>
public sealed class SpcAnalysisResult
{
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "mm";
    public int TotalSamples { get; set; }
    public int SubgroupSizeN { get; set; } = 32;
    public int SubgroupCountK { get; set; }
    public int DroppedRemainder { get; set; }

    public double Target { get; set; }
    public double Lsl { get; set; }
    public double Usl { get; set; }

    public double OverallMean { get; set; }
    public double OverallSigma { get; set; }
    public double OverallMin { get; set; }
    public double OverallMax { get; set; }
    public double OverallRange => OverallMax - OverallMin;

    public double Cp { get; set; }
    public double Cpk { get; set; }
    public double Cpu { get; set; }
    public double Cpl { get; set; }

    // Các đường giới hạn kiểm soát X-bar Chart
    public double Xbar_CL { get; set; }
    public double Xbar_UCL { get; set; }
    public double Xbar_LCL { get; set; }

    // Các đường giới hạn kiểm soát R Chart
    public double R_CL { get; set; }
    public double R_UCL { get; set; }
    public double R_LCL { get; set; }

    // Các đường cảnh báo Cpk
    public double Cpk_Warning1 { get; set; } = 1.33;
    public double Cpk_Warning2 { get; set; } = 1.67;

    public List<SpcSubgroupData> Subgroups { get; set; } = new();
    public List<HistogramBinData> HistogramBins { get; set; } = new();

    public string Assessment
    {
        get
        {
            if (Cpk >= 1.67) return "Tuyệt vời (A++): Năng lực quá trình vượt trội";
            if (Cpk >= 1.33) return "Tốt (A): Năng lực quá trình đạt chuẩn sản xuất";
            if (Cpk >= 1.00) return "Chấp nhận (B): Cần giám sát chặt chẽ";
            return "Kém (NG): Quá trình không đạt năng lực, nguy cơ lỗi cao";
        }
    }
}
