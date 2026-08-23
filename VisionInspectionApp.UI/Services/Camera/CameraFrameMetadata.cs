using System;

namespace VisionInspectionApp.UI.Services.Camera;

/// <summary>
/// Siêu dữ liệu thời gian thực của Frame chụp từ Camera phần cứng
/// </summary>
public sealed class CameraFrameMetadata
{
    /// <summary>
    /// Số thứ tự Frame tăng đơn điệu từ phần cứng Camera (Hardware Frame Counter)
    /// </summary>
    public uint FrameNum { get; init; }

    /// <summary>
    /// Dấu thời gian phần cứng chính xác microsecond/nanosecond từ lúc mở màn trập
    /// </summary>
    public ulong DeviceTimestampNs { get; init; }

    /// <summary>
    /// Dấu thời gian Host OS tại thời điểm nhận gói tin
    /// </summary>
    public DateTime HostTimestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Tổng số Frame bị rơi ở tầng phần cứng (Hardware Frame Drop Loss)
    /// </summary>
    public long HardwareDroppedFrames { get; init; }

    /// <summary>
    /// Tổng số Frame bị rơi ở tầng hàng đợi phần mềm (Software Buffer Drop)
    /// </summary>
    public long SoftwareDroppedFrames { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }
    public string PixelFormat { get; init; } = string.Empty;
}
