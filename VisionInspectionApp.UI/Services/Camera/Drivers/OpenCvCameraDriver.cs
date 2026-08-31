using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.UI.Services.Camera.Drivers;

public sealed class OpenCvCameraDriver : CameraDriverBase
{
    private VideoCapture? _cap;

    public static List<CameraDeviceInfo> ScanDevices()
    {
        var list = new List<CameraDeviceInfo>();

        try
        {
            var dsCameras = DirectShowDeviceEnumerator.GetDevices();
            for (int i = 0; i < dsCameras.Count; i++)
            {
                list.Add(new CameraDeviceInfo
                {
                    Vendor = CameraVendor.WebcamDirectShow,
                    InterfaceType = CameraInterfaceType.DirectShow,
                    Index = i,
                    ModelName = dsCameras[i]
                });
            }
        }
        catch { }

        // Custom RTSP / IP Camera entry
        list.Add(new CameraDeviceInfo
        {
            Vendor = CameraVendor.Rtsp,
            InterfaceType = CameraInterfaceType.RTSP,
            Index = -1,
            ModelName = "🌐 Custom RTSP / IP Camera",
            RtspUrl = "rtsp://192.168.1.100:554/stream1"
        });

        return list;
    }

    public override async Task<bool> OpenAsync(CameraDeviceInfo device)
    {
        _deviceInfo = device;

        return await Task.Run(() =>
        {
            try
            {
                _cap?.Dispose();
                _cap = CameraService.TryOpenVideoCapture(device.Index, device.RtspUrl, _parameters.Width, _parameters.Height, _parameters.TargetFps);

                if (_cap == null || !_cap.IsOpened())
                {
                    _cap?.Dispose();
                    _cap = null;
                    _isOpened = false;
                    RaiseErrorOccurred("Không thể mở VideoCapture bằng OpenCV.");
                    return false;
                }

                _isOpened = true;
                return true;
            }
            catch (Exception ex)
            {
                RaiseErrorOccurred($"Lỗi Open OpenCV Camera: {ex.Message}");
                _isOpened = false;
                return false;
            }
        });
    }

    public override async Task CloseAsync()
    {
        await StopGrabbingAsync();

        if (_cap != null)
        {
            try { _cap.Dispose(); } catch { }
            _cap = null;
        }

        _isOpened = false;
    }

    public override async Task<bool> StartGrabbingAsync()
    {
        if (!_isOpened || _cap == null) return false;
        if (_isGrabbing) return true;

        _isGrabbing = true;
        _cts = new CancellationTokenSource();

        _grabThread = new Thread(() => GrabLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "OpenCvCameraGrabThread"
        };
        _grabThread.Start();

        await Task.Delay(50);
        return true;
    }

    public override async Task StopGrabbingAsync()
    {
        if (!_isGrabbing) return;

        _isGrabbing = false;
        _cts?.Cancel();

        if (_grabThread != null)
        {
            _grabThread.Join(500);
            _grabThread = null;
        }

        await Task.CompletedTask;
    }

    public override async Task<Mat?> GrabFrameAsync(int timeoutMs = 1000)
    {
        if (!_isOpened || _cap == null) return null;

        return await Task.Run(() =>
        {
            try
            {
                using var frame = new Mat();
                if (_cap.Read(frame) && !frame.Empty())
                {
                    return ApplySoftwarePostProcessing(frame, _parameters);
                }
                return null;
            }
            catch
            {
                return null;
            }
        });
    }

    public override Task<bool> ExecuteSoftwareTriggerAsync()
    {
        if (!_isOpened || _cap == null) return Task.FromResult(false);

        try
        {
            using var rawFrame = new Mat();
            if (_cap.Read(rawFrame) && !rawFrame.Empty())
            {
                var processed = ApplySoftwarePostProcessing(rawFrame, _parameters);
                try
                {
                    RaiseFrameCaptured(processed);
                }
                finally
                {
                    if (!ReferenceEquals(processed, rawFrame))
                    {
                        processed.Dispose();
                    }
                }
                return Task.FromResult(true);
            }
        }
        catch { }

        return Task.FromResult(false);
    }

    private void GrabLoop(CancellationToken token)
    {
        var frame = new Mat();

        while (!token.IsCancellationRequested && _isGrabbing)
        {
            if (_parameters?.TriggerMode == CameraTriggerMode.On)
            {
                try
                {
                    Thread.Sleep(30);
                }
                catch
                {
                    break;
                }
                continue;
            }

            try
            {
                if (_cap != null && _cap.Read(frame) && !frame.Empty())
                {
                    RaiseFrameCaptured(frame);
                    Thread.Sleep(5);
                }
                else
                {
                    Thread.Sleep(33);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenCvCameraDriver] Loop error: {ex.Message}");
                Thread.Sleep(33);
            }
        }

        frame.Dispose();
    }
}
