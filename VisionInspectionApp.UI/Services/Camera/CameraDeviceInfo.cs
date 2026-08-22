using System;

namespace VisionInspectionApp.UI.Services.Camera;

public enum CameraVendor
{
    Hikrobot,
    Basler,
    Cognex,
    WebcamDirectShow,
    Rtsp,
    Simulator
}

public enum CameraInterfaceType
{
    GigE,
    USB3,
    DirectShow,
    RTSP,
    Virtual
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

    public string VendorName => Vendor switch
    {
        CameraVendor.Hikrobot => "Hikrobot MVS",
        CameraVendor.Basler => "Basler Pylon",
        CameraVendor.Cognex => "Cognex Vision",
        CameraVendor.WebcamDirectShow => "USB Webcam",
        CameraVendor.Rtsp => "RTSP Stream",
        CameraVendor.Simulator => "Simulator",
        _ => Vendor.ToString()
    };

    public string InterfaceTypeName => InterfaceType switch
    {
        CameraInterfaceType.GigE => "GigE Vision",
        CameraInterfaceType.USB3 => "USB3 Vision",
        CameraInterfaceType.DirectShow => "DirectShow",
        CameraInterfaceType.RTSP => "IP RTSP",
        CameraInterfaceType.Virtual => "Virtual Engine",
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
        _ => "📷"
    };

    public string Subtitle => Vendor switch
    {
        CameraVendor.Simulator => "Nguồn ảnh giả lập công nghiệp",
        CameraVendor.Rtsp => string.IsNullOrEmpty(RtspUrl) ? "Luồng Video IP RTSP" : RtspUrl,
        CameraVendor.WebcamDirectShow => $"Cổng USB #{Index} DirectShow",
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
                _ => ModelName
            };
        }
    }

    public override string ToString() => DisplayName;
}
