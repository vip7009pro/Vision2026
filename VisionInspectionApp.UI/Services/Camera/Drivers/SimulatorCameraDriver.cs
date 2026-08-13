using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.UI.Services.Camera.Drivers;

public sealed class SimulatorCameraDriver : CameraDriverBase
{
    private int _frameCounter;

    public SimulatorCameraDriver()
    {
        _deviceInfo = new CameraDeviceInfo
        {
            Vendor = CameraVendor.Simulator,
            InterfaceType = CameraInterfaceType.Virtual,
            Index = CameraService.SimulatorCameraIndex,
            ModelName = "📷 Camera Giả Lập Công Nghiệp (Simulator)"
        };
    }

    public override Task<bool> OpenAsync(CameraDeviceInfo device)
    {
        _deviceInfo = device;
        _isOpened = true;
        return Task.FromResult(true);
    }

    public override Task CloseAsync()
    {
        _isGrabbing = false;
        _isOpened = false;
        return Task.CompletedTask;
    }

    public override async Task<bool> StartGrabbingAsync()
    {
        if (!_isOpened) return false;
        if (_isGrabbing) return true;

        _isGrabbing = true;
        _cts = new CancellationTokenSource();

        _grabThread = new Thread(() => SimLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "SimulatorCameraGrabThread"
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

    public override Task<Mat?> GrabFrameAsync(int timeoutMs = 1000)
    {
        var rawSim = GenerateFrame(ref _frameCounter);
        var processed = ApplySoftwarePostProcessing(rawSim, _parameters);
        rawSim.Dispose();
        return Task.FromResult<Mat?>(processed);
    }

    public override Task<bool> ExecuteSoftwareTriggerAsync()
    {
        var rawSim = GenerateFrame(ref _frameCounter);
        RaiseFrameCaptured(rawSim);
        rawSim.Dispose();
        return Task.FromResult(true);
    }

    private void SimLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isGrabbing)
        {
            try
            {
                using var rawSim = GenerateFrame(ref _frameCounter);
                RaiseFrameCaptured(rawSim);
                Thread.Sleep(33); // 30 FPS
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SimulatorCameraDriver] Loop error: {ex.Message}");
                Thread.Sleep(33);
            }
        }
    }

    private Mat GenerateFrame(ref int frameCounter)
    {
        frameCounter++;
        string customPath = _parameters?.CustomImagePath?.Trim() ?? "";

        if (!string.IsNullOrEmpty(customPath) && System.IO.File.Exists(customPath))
        {
            try
            {
                var customMat = Cv2.ImRead(customPath, ImreadModes.Color);
                if (customMat != null && !customMat.Empty() && customMat.Width > 0 && customMat.Height > 0)
                {
                    return customMat;
                }
                customMat?.Dispose();
            }
            catch { }
        }

        var mat = new Mat(480, 640, MatType.CV_8UC3, new Scalar(40, 40, 40));

        // Nền lưới Industrial Grid
        for (int x = 0; x < 640; x += 40)
        {
            Cv2.Line(mat, new OpenCvSharp.Point(x, 0), new OpenCvSharp.Point(x, 480), new Scalar(60, 60, 60), 1);
        }
        for (int y = 0; y < 480; y += 40)
        {
            Cv2.Line(mat, new OpenCvSharp.Point(0, y), new OpenCvSharp.Point(640, y), new Scalar(60, 60, 60), 1);
        }

        // Target di chuyển sinh động
        double angle = (frameCounter % 120) * (2 * Math.PI / 120);
        int cx = 320 + (int)(150 * Math.Cos(angle));
        int cy = 240 + (int)(100 * Math.Sin(angle));

        Cv2.Circle(mat, new OpenCvSharp.Point(cx, cy), 40, new Scalar(0, 255, 255), 2);
        Cv2.Circle(mat, new OpenCvSharp.Point(cx, cy), 15, new Scalar(0, 165, 255), -1);
        Cv2.DrawMarker(mat, new OpenCvSharp.Point(cx, cy), new Scalar(0, 0, 255), MarkerTypes.Cross, 30, 2);

        Cv2.PutText(mat, "INDUSTRIAL CAMERA SIMULATOR", new OpenCvSharp.Point(20, 35), HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);
        Cv2.PutText(mat, $"TIME: {DateTime.Now:HH:mm:ss.fff}  FRAME: {frameCounter}", new OpenCvSharp.Point(20, 460), HersheyFonts.HersheySimplex, 0.5, new Scalar(200, 200, 200), 1);

        return mat;
    }
}
