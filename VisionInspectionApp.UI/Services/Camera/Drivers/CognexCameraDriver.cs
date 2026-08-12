using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VisionInspectionApp.UI.Services.Camera.Drivers;

/// <summary>
/// Lớp Driver trừu tượng làm sẵn dành cho tích hợp Camera công nghiệp Cognex (DataMan / VisionPro SDK) trong tương lai.
/// Tuân thủ 100% giao diện ICameraDriver.
/// </summary>
public sealed class CognexCameraDriver : CameraDriverBase
{
    public static List<CameraDeviceInfo> ScanDevices()
    {
        // Placeholder scan for Cognex devices
        return new List<CameraDeviceInfo>();
    }

    public override Task<bool> OpenAsync(CameraDeviceInfo device)
    {
        _deviceInfo = device;
        RaiseErrorOccurred("Driver Cognex DataMan SDK chưa được nạp SDK thực tế. Đã kết nối theo chế độ trừu tượng làm sẵn.");
        _isOpened = false;
        return Task.FromResult(false);
    }

    public override Task CloseAsync()
    {
        _isOpened = false;
        return Task.CompletedTask;
    }

    public override Task<bool> StartGrabbingAsync()
    {
        _isGrabbing = false;
        return Task.FromResult(false);
    }

    public override Task StopGrabbingAsync()
    {
        _isGrabbing = false;
        return Task.CompletedTask;
    }

    public override Task<Mat?> GrabFrameAsync(int timeoutMs = 1000)
    {
        return Task.FromResult<Mat?>(null);
    }

    public override Task<bool> ExecuteSoftwareTriggerAsync()
    {
        return Task.FromResult(false);
    }
}
