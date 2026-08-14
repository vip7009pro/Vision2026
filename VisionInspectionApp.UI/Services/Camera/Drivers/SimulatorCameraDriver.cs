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

    private readonly Random _rand = new Random();

    private Mat GenerateFrame(ref int frameCounter)
    {
        frameCounter++;
        string customPath = _parameters?.CustomImagePath?.Trim() ?? "";
        bool enableRandom = _parameters?.EnableRandomTransform ?? false;

        if (!string.IsNullOrEmpty(customPath) && System.IO.File.Exists(customPath))
        {
            try
            {
                var customMat = Cv2.ImRead(customPath, ImreadModes.Color);
                if (customMat != null && !customMat.Empty() && customMat.Width > 0 && customMat.Height > 0)
                {
                    if (enableRandom)
                    {
                        var transformed = ApplyRandomTransform(customMat);
                        customMat.Dispose();
                        return transformed;
                    }
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

        int cx = 320;
        int cy = 240;

        Cv2.Circle(mat, new OpenCvSharp.Point(cx, cy), 40, new Scalar(0, 255, 255), 2);
        Cv2.Circle(mat, new OpenCvSharp.Point(cx, cy), 15, new Scalar(0, 165, 255), -1);
        Cv2.DrawMarker(mat, new OpenCvSharp.Point(cx, cy), new Scalar(0, 0, 255), MarkerTypes.Cross, 30, 2);

        Cv2.PutText(mat, "INDUSTRIAL CAMERA SIMULATOR", new OpenCvSharp.Point(20, 35), HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);
        Cv2.PutText(mat, $"TIME: {DateTime.Now:HH:mm:ss.fff}  FRAME: {frameCounter}", new OpenCvSharp.Point(20, 460), HersheyFonts.HersheySimplex, 0.5, new Scalar(200, 200, 200), 1);

        if (enableRandom)
        {
            var transformed = ApplyRandomTransform(mat);
            mat.Dispose();
            return transformed;
        }

        return mat;
    }

    private Mat ApplyRandomTransform(Mat srcMat)
    {
        if (srcMat == null || srcMat.Empty()) return srcMat ?? new Mat();

        // Góc xoay ngẫu nhiên từ -12.0° đến +12.0°
        double angle = (_rand.NextDouble() * 24.0) - 12.0;

        // Độ xê dịch ngẫu nhiên từ -20.0px đến +20.0px
        double shiftX = (_rand.NextDouble() * 40.0) - 20.0;
        double shiftY = (_rand.NextDouble() * 40.0) - 20.0;

        Point2f center = new Point2f(srcMat.Width / 2.0f, srcMat.Height / 2.0f);
        using var rotMat = Cv2.GetRotationMatrix2D(center, angle, 1.0);

        rotMat.Set<double>(0, 2, rotMat.At<double>(0, 2) + shiftX);
        rotMat.Set<double>(1, 2, rotMat.At<double>(1, 2) + shiftY);

        Mat dstMat = new Mat();
        Cv2.WarpAffine(srcMat, dstMat, rotMat, srcMat.Size(), InterpolationFlags.Linear, BorderTypes.Reflect101);
        return dstMat;
    }
}
