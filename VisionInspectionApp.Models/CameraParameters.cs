using System;

namespace VisionInspectionApp.Models;

public enum CameraTriggerMode
{
    Off, // Live continuous / Free Run
    On   // Software / Hardware Trigger
}

public enum CameraTriggerSource
{
    Software,
    Line0,
    Line1,
    Line2
}

public sealed class CameraParameters
{
    // Phơi sáng & Độ khuếch đại
    public float ExposureTimeUs { get; set; } = 10000.0f; // Microseconds (10ms)
    public bool AutoExposure { get; set; } = false;
    public float GainDb { get; set; } = 0.0f; // dB
    public bool AutoGain { get; set; } = false;
    public float Gamma { get; set; } = 1.0f;
    public float BlackLevel { get; set; } = 0.0f;

    // Khung hình & Tần số quét
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int TargetFps { get; set; } = 30;

    // Trigger Mode & I/O
    public CameraTriggerMode TriggerMode { get; set; } = CameraTriggerMode.Off;
    public CameraTriggerSource TriggerSource { get; set; } = CameraTriggerSource.Software;
    public float TriggerDelayUs { get; set; } = 0.0f;

    // Định dạng & Hướng ảnh
    public bool ReverseX { get; set; } = false; // Lật ngang
    public bool ReverseY { get; set; } = false; // Lật dọc
    public bool AutoWhiteBalance { get; set; } = true; // Mặc định bật Cân bằng trắng cho camera màu
    public bool AutoWhiteBalanceOnce { get; set; } = false; // Kích hoạt Cân bằng trắng 1 lần
    public float RedGain { get; set; } = 1.0f;
    public float GreenGain { get; set; } = 1.0f;
    public float BlueGain { get; set; } = 1.0f;
    public bool IsLiveViewEnabled { get; set; } = false; // Mặc định sau khi kết nối không tự ý stream liên tục chiếm băng thông

    // Mạng GigE Vision (Chỉ dành cho GigE Camera)
    public int PacketSize { get; set; } = 1500; // GevSCPSPacketSize (Bytes)
    public int PacketDelay { get; set; } = 0;   // GevSCPD (Microseconds)

    // Xử lý mềm OpenCV (Brightness, Contrast, Grayscale)
    public double Brightness { get; set; } = 0.0;
    public double Contrast { get; set; } = 1.0;
    public bool IsGrayscale { get; set; } = false;

    // Nguồn ảnh tùy chỉnh & Tự xoay/di chuyển cho Camera Giả Lập (Simulator)
    public string CustomImagePath { get; set; } = "";
    public bool EnableRandomTransform { get; set; } = false;

    public CameraParameters Clone()
    {
        return (CameraParameters)MemberwiseClone();
    }
}
