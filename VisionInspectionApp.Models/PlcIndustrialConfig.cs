using System;

namespace VisionInspectionApp.Models;

/// <summary>
/// Kiểu dạng sóng / tín hiệu gửi sang PLC (Signal Waveform)
/// </summary>
public enum PlcSignalType
{
    /// <summary>
    /// Giữ mức logic (Level): Duy trì ON khi kích hoạt và OFF khi xóa.
    /// </summary>
    Level = 0,

    /// <summary>
    /// Xung cạnh lên (Rising Edge): Mức tĩnh là 0, khi kích hoạt phát xung 0 -> 1 -> 0 theo thời gian PulseDurationMs.
    /// </summary>
    RisingEdge = 1,

    /// <summary>
    /// Xung cạnh xuống (Falling Edge / Active-Low): Mức tĩnh là 1, khi kích hoạt phát xung 1 -> 0 -> 1 theo thời gian PulseDurationMs.
    /// </summary>
    FallingEdge = 2
}

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
    public bool SimulatePlcAck { get; set; } = false;

    /// <summary>
    /// Kiểu tín hiệu gửi cho kết quả Pass (Mặc định: Level)
    /// </summary>
    public PlcSignalType PassSignalType { get; set; } = PlcSignalType.Level;

    /// <summary>
    /// Kiểu tín hiệu gửi cho kết quả NG (Mặc định: Level)
    /// </summary>
    public PlcSignalType NgSignalType { get; set; } = PlcSignalType.Level;

    /// <summary>
    /// Kiểu tín hiệu gửi cho tín hiệu Done (Mặc định: Level)
    /// </summary>
    public PlcSignalType DoneSignalType { get; set; } = PlcSignalType.Level;

    /// <summary>
    /// Độ rộng xung phát cho kiểu Edge (Rising Edge / Falling Edge) tính bằng ms (Mặc định: 100ms)
    /// </summary>
    public int PulseDurationMs { get; set; } = 100;

    /// <summary>
    /// Chế độ bắt tay bất đồng bộ không chờ Ack (Non-blocking Pipelined Queue):
    /// Băng tải chạy liên tục tốc độ cao, Vision ghi kết quả vào hàng đợi rồi giải phóng ngay, không dừng luồng chờ Ack.
    /// </summary>
    public bool NonBlockingMode { get; set; } = true;

    /// <summary>
    /// Điểm ra chỉ định trên hàng đợi 20 sản phẩm (1..20): Nấc mà tại đó nếu là hàng NG thì băng tải sẽ dừng. Mặc định: 10
    /// </summary>
    public int TargetStopStationIndex { get; set; } = 10;

    /// <summary>
    /// Địa chỉ bắt đầu mảng hàng đợi trên PLC (Mặc định: M200 cho mảng M200..M219)
    /// </summary>
    public string QueueRegisterStart { get; set; } = "M200";

    /// <summary>
    /// Kiểm tra xem cấu hình Handshake có chứa ít nhất một TagName hợp lệ hay không.
    /// </summary>
    public bool HasConfiguredTags()
    {
        return !string.IsNullOrWhiteSpace(ReadyTagName) ||
               !string.IsNullOrWhiteSpace(BusyTagName) ||
               !string.IsNullOrWhiteSpace(DoneTagName) ||
               !string.IsNullOrWhiteSpace(PassTagName) ||
               !string.IsNullOrWhiteSpace(NgTagName) ||
               !string.IsNullOrWhiteSpace(PlcAckTagName);
    }

    /// <summary>
    /// Khôi phục cấu hình bắt tay chuẩn Mitsubishi FX5U (M101..M105, xung Rising Edge 20ms, hàng đợi M200).
    /// </summary>
    public void ApplyFx5uDefaults(string? targetPlcId = null)
    {
        if (!string.IsNullOrWhiteSpace(targetPlcId)) PlcId = targetPlcId;
        ReadyTagName = "M101";
        BusyTagName = "M102";
        DoneTagName = "M103";
        PassTagName = "M104";
        NgTagName = "M105";
        PlcAckTagName = "";
        HandshakeTimeoutMs = 300;
        IsEnabled = true;
        SimulatePlcAck = false;
        PassSignalType = PlcSignalType.RisingEdge;
        NgSignalType = PlcSignalType.RisingEdge;
        DoneSignalType = PlcSignalType.Level;
        PulseDurationMs = 20;
        NonBlockingMode = true;
        TargetStopStationIndex = 10;
        QueueRegisterStart = "M200";
    }

    /// <summary>
    /// Khôi phục cấu hình bắt tay chuẩn I/O Công Nghiệp (Y1..Y5, X1 Ack, Level).
    /// </summary>
    public void ApplyStandardIoDefaults(string? targetPlcId = null)
    {
        if (!string.IsNullOrWhiteSpace(targetPlcId)) PlcId = targetPlcId;
        ReadyTagName = "Y1_VisionReady";
        BusyTagName = "Y2_VisionBusy";
        DoneTagName = "Y3_VisionDone";
        PassTagName = "Y4_VisionPass";
        NgTagName = "Y5_VisionNG";
        PlcAckTagName = "X1_PlcAck";
        HandshakeTimeoutMs = 500;
        IsEnabled = true;
        SimulatePlcAck = false;
        PassSignalType = PlcSignalType.Level;
        NgSignalType = PlcSignalType.Level;
        DoneSignalType = PlcSignalType.Level;
        PulseDurationMs = 100;
        NonBlockingMode = false;
        TargetStopStationIndex = 10;
        QueueRegisterStart = "M200";
    }

    /// <summary>
    /// Sao chép giá trị từ cấu hình khác.
    /// </summary>
    public void CopyFrom(IndustrialHandshakeConfig other)
    {
        if (other == null) return;
        if (!string.IsNullOrWhiteSpace(other.PlcId)) PlcId = other.PlcId;
        ReadyTagName = other.ReadyTagName;
        BusyTagName = other.BusyTagName;
        DoneTagName = other.DoneTagName;
        PassTagName = other.PassTagName;
        NgTagName = other.NgTagName;
        PlcAckTagName = other.PlcAckTagName;
        HandshakeTimeoutMs = other.HandshakeTimeoutMs;
        IsEnabled = other.IsEnabled;
        SimulatePlcAck = other.SimulatePlcAck;
        PassSignalType = other.PassSignalType;
        NgSignalType = other.NgSignalType;
        DoneSignalType = other.DoneSignalType;
        PulseDurationMs = other.PulseDurationMs;
        NonBlockingMode = other.NonBlockingMode;
        TargetStopStationIndex = other.TargetStopStationIndex;
        QueueRegisterStart = other.QueueRegisterStart;
    }
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
