using OpenCvSharp;
using System;
using System.Threading.Tasks;

namespace VisionInspectionApp.UI.Services.Camera;

public interface ICameraDriver : IDisposable
{
    CameraDeviceInfo DeviceInfo { get; }
    bool IsOpened { get; }
    bool IsGrabbing { get; }

    event EventHandler<Mat>? FrameCaptured;
    event EventHandler<string>? ErrorOccurred;

    Task<bool> OpenAsync(CameraDeviceInfo device);
    Task CloseAsync();
    Task<bool> StartGrabbingAsync();
    Task StopGrabbingAsync();

    Task<Mat?> GrabFrameAsync(int timeoutMs = 1000);
    Task<bool> ExecuteSoftwareTriggerAsync();

    Task ApplyParametersAsync(CameraParameters parameters);
    Task<CameraParameters> ReadParametersAsync();
}
