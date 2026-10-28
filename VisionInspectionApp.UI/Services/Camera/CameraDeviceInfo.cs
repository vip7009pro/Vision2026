using System;

namespace VisionInspectionApp.UI.Services.Camera;

public enum CameraVendor
{
    Hikrobot,
    Basler,
    Cognex,
    WebcamDirectShow,
    Rtsp,
    Simulator,
    AndroidCamera
}

public enum CameraInterfaceType
{
    GigE,
    USB3,
    DirectShow,
    RTSP,
    Virtual,
    Android
}

public sealed class CameraDeviceInfo
{
    public CameraVendor Vendor { get; set; } = CameraVendor.Simulator;
    public CameraInterfaceType InterfaceType { get; set; } = CameraInterfaceType.Virtual;
    
    public int Index { get; set; } = -1;
    public string ModelName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string UserDefinedName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string RtspUrl { get; set; } = string.Empty;

    // Cấu hình kết nối cho Android Camera
    public VisionInspectionApp.Models.AndroidConnectionMode AndroidConnectionMode { get; set; } = VisionInspectionApp.Models.AndroidConnectionMode.Usb;
    public string AndroidHost { get; set; } = "127.0.0.1";
    public int AndroidPort { get; set; } = 8080;

    public string VendorName => Vendor switch
    {
        CameraVendor.Hikrobot => "Hikrobot MVS",
        CameraVendor.Basler => "Basler Pylon",
        CameraVendor.Cognex => "Cognex Vision",
        CameraVendor.WebcamDirectShow => "USB Webcam",
        CameraVendor.Rtsp => "RTSP Stream",
        CameraVendor.Simulator => "Simulator",
        CameraVendor.AndroidCamera => "Samsung Galaxy (Android)",
        _ => Vendor.ToString()
    };

    public string InterfaceTypeName => InterfaceType switch
    {
        CameraInterfaceType.GigE => "GigE Vision",
        CameraInterfaceType.USB3 => "USB3 Vision",
        CameraInterfaceType.DirectShow => "DirectShow",
        CameraInterfaceType.RTSP => "IP RTSP",
        CameraInterfaceType.Virtual => "Virtual Engine",
        CameraInterfaceType.Android => "USB / Wi-Fi / LAN",
        _ => InterfaceType.ToString()
    };

    public string DeviceIcon => Vendor switch
    {
        CameraVendor.Hikrobot => "📷",
        CameraVendor.Basler => "📷",
        CameraVendor.Cognex => "📷",
        CameraVendor.WebcamDirectShow => "📹",
        CameraVendor.Rtsp => "🌐",
        CameraVendor.Simulator => "🎮",
        CameraVendor.AndroidCamera => "📱",
        _ => "📷"
    };

    public string Subtitle => Vendor switch
    {
        CameraVendor.Simulator => "Nguồn ảnh giả lập công nghiệp",
        CameraVendor.Rtsp => string.IsNullOrEmpty(RtspUrl) ? "Luồng Video IP RTSP" : RtspUrl,
        CameraVendor.WebcamDirectShow => $"Cổng USB #{Index} DirectShow",
        CameraVendor.AndroidCamera => string.IsNullOrEmpty(IpAddress) 
            ? "Samsung Galaxy S24 Ultra (USB / Wi-Fi / Ethernet)" 
            : $"{IpAddress}:{AndroidPort} • Galaxy S24 Ultra",
        _ => string.IsNullOrEmpty(IpAddress) 
            ? (string.IsNullOrEmpty(SerialNumber) ? "Industrial Vision Camera" : $"S/N: {SerialNumber}") 
            : $"IP: {IpAddress}  •  S/N: {SerialNumber}"
    };

    public string DisplayName
    {
        get
        {
            return Vendor switch
            {
                CameraVendor.Hikrobot => $"📷 [Hikrobot] {ModelName} ({SerialNumber}) {(string.IsNullOrEmpty(IpAddress) ? "" : "IP: " + IpAddress)}",
                CameraVendor.Basler => $"📷 [Basler] {ModelName} ({SerialNumber})",
                CameraVendor.Cognex => $"📷 [Cognex] {ModelName} ({SerialNumber})",
                CameraVendor.WebcamDirectShow => $"📹 [USB Cam {Index}] {ModelName}",
                CameraVendor.Rtsp => $"🌐 [RTSP] {ModelName} ({RtspUrl})",
                CameraVendor.Simulator => "🎮 [Simulator] Camera Giả Lập Công Nghiệp",
                CameraVendor.AndroidCamera => $"📱 [Android] {ModelName} {(string.IsNullOrEmpty(IpAddress) ? "(USB/LAN)" : "IP: " + IpAddress)}",
                _ => ModelName
            };
        }
    }

    public override string ToString() => DisplayName;
}
