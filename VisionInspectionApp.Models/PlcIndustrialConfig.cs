using System;

namespace VisionInspectionApp.Models;

/// <summary>
/// Cấu hình Máy trạng thái Bắt tay Công nghiệp 2 chiều (Deterministic Industrial Handshake 24/7)
/// </summary>
public sealed class IndustrialHandshakeConfig
{
    public string PlcId { get; set; } = "PLC1";
    public string ReadyTagName { get; set; } = "Y1_VisionReady";
    public string BusyTagName { get; set; } = "Y2_VisionBusy";
    public string DoneTagName { get; set; } = "Y3_VisionDone";
    public string PassTagName { get; set; } = "Y4_VisionPass";
    public string NgTagName { get; set; } = "Y5_VisionNG";
    public string PlcAckTagName { get; set; } = "X1_PlcAck";
    public int HandshakeTimeoutMs { get; set; } = 500;
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Cấu hình Watchdog Heartbeat 2 chiều và Liên động An toàn (Safety Interlock)
/// </summary>
public sealed class PlcHeartbeatConfig
{
    public string PlcId { get; set; } = "PLC1";
    public string VisionHeartbeatTagName { get; set; } = "Y0_VisionHeartbeat";
    public string PlcHeartbeatTagName { get; set; } = "X0_PlcHeartbeat";
    public int IntervalMs { get; set; } = 100;
    public int TimeoutMs { get; set; } = 300;
    public bool EnableEmergencyInterlock { get; set; } = true;
    public string EmergencyStopTagName { get; set; } = "Y10_VisionFault";
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Cấu hình Đồng bộ Chuyển động Cuộn, Xung Encoder và Bù trừ Phơi sáng
/// </summary>
public sealed class PlcMotionConfig
{
    public string PlcId { get; set; } = "PLC1";
    public string EncoderTagName { get; set; } = "D1000";
    public string SpeedTagName { get; set; } = "D1002";
    public double PulsesPerMm { get; set; } = 100.0;
    public double MmPerPixel { get; set; } = 0.05;
    public double NominalSpeedMpm { get; set; } = 30.0;
    public double BaseExposureTimeUs { get; set; } = 500.0;
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Cấu hình Cơ cấu Shift Register theo dõi vị trí lỗi và Kích hoạt Trạm Loại Bỏ (Reject Station)
/// </summary>
public sealed class PlcShiftRegisterConfig
{
    public string PlcId { get; set; } = "PLC1";
    public string RejectTagName { get; set; } = "Y0_RejectPiston";
    public double RejectStationDistanceMm { get; set; } = 1500.0;
    public double RejectToleranceMm { get; set; } = 15.0;
    public int PulseDurationMs { get; set; } = 100;
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Tổng hợp toàn bộ Cấu hình Công nghiệp PLC & Đồng Bộ Chuyển Động
/// </summary>
public sealed class PlcIndustrialConfig
{
    public IndustrialHandshakeConfig Handshake { get; set; } = new();
    public PlcHeartbeatConfig Heartbeat { get; set; } = new();
    public PlcMotionConfig Motion { get; set; } = new();
    public PlcShiftRegisterConfig ShiftRegister { get; set; } = new();
}
