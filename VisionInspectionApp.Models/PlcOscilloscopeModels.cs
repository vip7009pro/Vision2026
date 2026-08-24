using System;
using System.Collections.Generic;

namespace VisionInspectionApp.Models;

/// <summary>
/// Mẫu đo điểm tín hiệu PLC kèm timestamp độ chính xác cao (Microsecond / Millisecond)
/// </summary>
public readonly struct PlcOscilloscopeSample
{
    public double TimestampMs { get; }
    public DateTime WallClockTime { get; }
    public double Value { get; }
    public bool IsBit { get; }

    public PlcOscilloscopeSample(double timestampMs, DateTime wallClockTime, double value, bool isBit = true)
    {
        TimestampMs = timestampMs;
        WallClockTime = wallClockTime;
        Value = value;
        IsBit = isBit;
    }
}

/// <summary>
/// Sự kiện chuyển trạng thái tín hiệu PLC kèm dấu thời gian thực
/// </summary>
public sealed class PlcOscilloscopeEvent
{
    public int Index { get; set; }
    public DateTime Timestamp { get; set; }
    public string TimeString => Timestamp.ToString("HH:mm:ss.fff");
    public int ChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string OldState { get; set; } = string.Empty;
    public string NewState { get; set; } = string.Empty;
    public double DurationMs { get; set; }
    public double PulseWidthMs { get; set; }
    public string TransitionType { get; set; } = string.Empty;
    public string Description => $"CH{ChannelId} ({Address}) {OldState} → {NewState} ({TransitionType})";
}

/// <summary>
/// Chế độ kích hoạt (Trigger) của PLC Oscilloscope
/// </summary>
public enum OscilloscopeTriggerMode
{
    FreeRun = 0,        // Tự do liên tục (Không chờ sườn)
    RisingEdge = 1,     // Kích hoạt khi có sườn lên (0 -> 1)
    FallingEdge = 2,    // Kích hoạt khi có sườn xuống (1 -> 0)
    AnyEdge = 3         // Kích hoạt khi có bất kỳ thay đổi nào
}

/// <summary>
/// Thang đo thời gian (Time division ms/div)
/// </summary>
public enum OscilloscopeTimeDivision
{
    Ms10 = 10,
    Ms20 = 20,
    Ms50 = 50,
    Ms100 = 100,
    Ms200 = 200,
    Ms500 = 500,
    Sec1 = 1000,
    Sec2 = 2000,
    Sec5 = 5000
}
