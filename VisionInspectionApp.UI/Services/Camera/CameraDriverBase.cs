using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.UI.Services.Camera;

public abstract class CameraDriverBase : ICameraDriver
{
    protected CameraDeviceInfo _deviceInfo = new();
    protected CameraParameters _parameters = new();
    protected bool _isOpened;
    protected bool _isGrabbing;
    protected Thread? _grabThread;
    protected CancellationTokenSource? _cts;
    protected readonly object _lock = new();

    protected bool _hardwareReverseXApplied;
    protected bool _hardwareReverseYApplied;

    public CameraDeviceInfo DeviceInfo => _deviceInfo;
    public bool IsOpened => _isOpened;
    public bool IsGrabbing => _isGrabbing;
    public bool IsHardwareReverseXApplied => _hardwareReverseXApplied;
    public bool IsHardwareReverseYApplied => _hardwareReverseYApplied;

    public event EventHandler<Mat>? FrameCaptured;
    public event EventHandler<string>? ErrorOccurred;

    public abstract Task<bool> OpenAsync(CameraDeviceInfo device);
    public abstract Task CloseAsync();
    public abstract Task<bool> StartGrabbingAsync();
    public abstract Task StopGrabbingAsync();
    public abstract Task<Mat?> GrabFrameAsync(int timeoutMs = 1000);
    public abstract Task<bool> ExecuteSoftwareTriggerAsync();

    public virtual Task ApplyParametersAsync(CameraParameters parameters)
    {
        lock (_lock)
        {
            _parameters = parameters.Clone();
        }
        return Task.CompletedTask;
    }

    public virtual Task<CameraParameters> ReadParametersAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(_parameters.Clone());
        }
    }

    protected void RaiseFrameCaptured(Mat frame)
    {
        if (frame == null || frame.IsDisposed || frame.Empty()) return;

        CameraParameters p;
        bool hwRevX, hwRevY;
        lock (_lock)
        {
            p = _parameters;
            hwRevX = _hardwareReverseXApplied;
            hwRevY = _hardwareReverseYApplied;
        }

        bool needFlipX = (p != null) && p.ReverseX && !hwRevX;
        bool needFlipY = (p != null) && p.ReverseY && !hwRevY;

        bool needProcessing = (p != null) &&
            (Math.Abs(p.Contrast - 1.0) > 0.01 ||
             Math.Abs(p.Brightness) > 0.01 ||
             p.IsGrayscale ||
             needFlipX ||
             needFlipY);

        if (needProcessing && p != null)
        {
            using var processed = ApplySoftwarePostProcessing(frame, p, hwRevX, hwRevY);
            FrameCaptured?.Invoke(this, processed);
        }
        else
        {
            FrameCaptured?.Invoke(this, frame);
        }
    }

    protected void RaiseErrorOccurred(string message)
    {
        ErrorOccurred?.Invoke(this, message);
    }

    public static Mat ApplySoftwarePostProcessing(Mat input, CameraParameters paramsObj, bool hardwareReverseXApplied = false, bool hardwareReverseYApplied = false)
    {
        if (input == null || input.IsDisposed || input.Empty()) return new Mat();
        if (paramsObj == null) return input.Clone();

        bool needFlipX = paramsObj.ReverseX && !hardwareReverseXApplied;
        bool needFlipY = paramsObj.ReverseY && !hardwareReverseYApplied;

        bool needProcessing = Math.Abs(paramsObj.Contrast - 1.0) > 0.01 ||
                              Math.Abs(paramsObj.Brightness) > 0.01 ||
                              paramsObj.IsGrayscale ||
                              needFlipX ||
                              needFlipY;

        if (!needProcessing)
        {
            return input.Clone();
        }

        try
        {
            var output = new Mat();
            double contrast = paramsObj.Contrast <= 0.01 ? 1.0 : Math.Clamp(paramsObj.Contrast, 0.1, 5.0);
            double brightness = Math.Clamp(paramsObj.Brightness, -255.0, 255.0);

            input.ConvertTo(output, -1, contrast, brightness);

            if (paramsObj.IsGrayscale)
            {
                if (output.Channels() == 3)
                {
                    using var gray = new Mat();
                    Cv2.CvtColor(output, gray, ColorConversionCodes.BGR2GRAY);
                    Cv2.CvtColor(gray, output, ColorConversionCodes.GRAY2BGR);
                }
                else if (output.Channels() == 4)
                {
                    using var gray = new Mat();
                    Cv2.CvtColor(output, gray, ColorConversionCodes.BGRA2GRAY);
                    Cv2.CvtColor(gray, output, ColorConversionCodes.GRAY2BGR);
                }
            }

            // Software Reverse X / Y if driver doesn't support hardware flip
            if (needFlipX && needFlipY)
            {
                Cv2.Flip(output, output, FlipMode.XY);
            }
            else if (needFlipX)
            {
                Cv2.Flip(output, output, FlipMode.Y);
            }
            else if (needFlipY)
            {
                Cv2.Flip(output, output, FlipMode.X);
            }

            return output;
        }
        catch
        {
            return input.Clone();
        }
    }

    public virtual void Dispose()
    {
        StopGrabbingAsync().Wait();
        CloseAsync().Wait();
    }
}
