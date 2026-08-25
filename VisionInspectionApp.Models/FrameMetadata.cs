using System;

namespace VisionInspectionApp.Models;

/// <summary>
/// Siêu dữ liệu gắn liền với từng Frame ảnh trong hệ thống kiểm tra cuộn liên tục (Continuous Roll Web Inspection).
/// </summary>
public sealed class FrameMetadata
{
    /// <summary>
    /// Số thứ tự Frame tăng đơn điệu
    /// </summary>
    public long FrameIndex { get; set; }

    /// <summary>
    /// Dấu thời gian phần cứng từ màn trập camera (nanoseconds)
    /// </summary>
    public ulong HardwareTimestampNs { get; set; }

    /// <summary>
    /// Thời gian Host OS nhận frame
    /// </summary>
    public DateTime HostTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Giá trị xung Encoder đọc từ High-Speed Counter của PLC
    /// </summary>
    public long EncoderPulses { get; set; }

    /// <summary>
    /// Vị trí dọc mét dài tuyệt đối của Frame trên cuộn (mm) tính từ đầu cuộn
    /// </summary>
    public double WebPositionMm { get; set; }

    /// <summary>
    /// Vận tốc chạy cuộn thời gian thực của máy kéo (mét/phút - mpm)
    /// </summary>
    public double LineSpeedMpm { get; set; }

    /// <summary>
    /// Độ phân giải vật lý (mm trên mỗi pixel)
    /// </summary>
    public double MmPerPixel { get; set; } = 0.05;

    /// <summary>
    /// Hệ số bù trừ phơi sáng tự động theo vận tốc chuyển động cuộn
    /// </summary>
    public double ExposureCompensationRatio { get; set; } = 1.0;

    /// <summary>
    /// Chuyển đổi tọa độ pixel trên frame (px) sang tọa độ vật lý cuộn Web Coordinate (mm)
    /// </summary>
    /// <param name="pixelX">Tọa độ X pixel ngang trên frame</param>
    /// <param name="pixelY">Tọa độ Y pixel dọc trên frame</param>
    /// <returns>(X_web mm từ mép cuộn, Y_web mm từ đầu cuộn)</returns>
    public (double WebXMm, double WebYMm) ConvertToWebCoordinates(double pixelX, double pixelY)
    {
        double webXMm = pixelX * MmPerPixel;
        double webYMm = WebPositionMm + (pixelY * MmPerPixel);
        return (webXMm, webYMm);
    }
}

