using OpenCvSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.UI.Services.Camera.Drivers;

public sealed class SimulatorCameraDriver : CameraDriverBase
{
    private readonly Random _rand = new();

    private Mat? _cachedBaseMat;
    private string _cachedImagePath = string.Empty;
    private readonly object _imageLock = new();

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

        lock (_imageLock)
        {
            _cachedBaseMat?.Dispose();
            _cachedBaseMat = null;
            _cachedImagePath = string.Empty;
        }

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

    public override Task ApplyParametersAsync(CameraParameters parameters)
    {
        lock (_lock)
        {
            var p = parameters.Clone();
            if (string.IsNullOrWhiteSpace(p.CustomImagePath))
            {
                if (!string.IsNullOrWhiteSpace(_parameters?.CustomImagePath))
                {
                    p.CustomImagePath = _parameters.CustomImagePath;
                }
                else
                {
                    p.CustomImagePath = TryGetPersistedSimulatorImagePath();
                }
            }
            _parameters = p;
        }

        lock (_imageLock)
        {
            _cachedBaseMat?.Dispose();
            _cachedBaseMat = null;
            _cachedImagePath = string.Empty;
        }

        return Task.CompletedTask;
    }

    public override Task<Mat?> GrabFrameAsync(int timeoutMs = 1000)
    {
        lock (_imageLock)
        {
            string customPath = _parameters?.CustomImagePath?.Trim() ?? "";
            bool enableRandom = _parameters?.EnableRandomTransform ?? false;

            var baseMat = GetOrLoadBaseMat(customPath);
            if (baseMat == null || baseMat.IsDisposed || baseMat.Empty())
            {
                return Task.FromResult<Mat?>(null);
            }

            Mat rawSim = enableRandom ? ApplyRandomTransform(baseMat) : baseMat.Clone();
            var processed = ApplySoftwarePostProcessing(rawSim, _parameters);
            if (!ReferenceEquals(processed, rawSim))
            {
                rawSim.Dispose();
            }

            return Task.FromResult<Mat?>(processed);
        }
    }

    public override Task<bool> ExecuteSoftwareTriggerAsync()
    {
        lock (_imageLock)
        {
            string customPath = _parameters?.CustomImagePath?.Trim() ?? "";
            bool enableRandom = _parameters?.EnableRandomTransform ?? false;

            var baseMat = GetOrLoadBaseMat(customPath);
            if (baseMat != null && !baseMat.IsDisposed && !baseMat.Empty())
            {
                using var rawSim = enableRandom ? ApplyRandomTransform(baseMat) : baseMat.Clone();
                RaiseFrameCaptured(rawSim);
            }
        }
        return Task.FromResult(true);
    }

    private void SimLoop(CancellationToken token)
    {
        var sw = new Stopwatch();

        while (!token.IsCancellationRequested && _isGrabbing)
        {
            sw.Restart();

            try
            {
                Mat? frameToEmit = null;

                lock (_imageLock)
                {
                    string customPath = _parameters?.CustomImagePath?.Trim() ?? "";
                    bool enableRandom = _parameters?.EnableRandomTransform ?? false;

                    var baseMat = GetOrLoadBaseMat(customPath);
                    if (baseMat != null && !baseMat.IsDisposed && !baseMat.Empty())
                    {
                        if (enableRandom)
                        {
                            frameToEmit = ApplyRandomTransform(baseMat);
                        }
                        else
                        {
                            // Trực tiếp clone ma trận đã cache trong bộ nhớ (Zero Disk I/O)
                            frameToEmit = baseMat.Clone();
                        }
                    }
                }

                if (frameToEmit != null && !frameToEmit.Empty())
                {
                    using (frameToEmit)
                    {
                        RaiseFrameCaptured(frameToEmit);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SimulatorCameraDriver] Loop error: {ex.Message}");
            }

            sw.Stop();
            int elapsedMs = (int)sw.ElapsedMilliseconds;
            int sleepTime = Math.Max(1, 33 - elapsedMs); // Target 30 FPS (~33ms per frame)

            try
            {
                Thread.Sleep(sleepTime);
            }
            catch
            {
                break;
            }
        }
    }

    private Mat GetOrLoadBaseMat(string customPath)
    {
        string targetPath = customPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            targetPath = _parameters?.CustomImagePath?.Trim() ?? "";
        }
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            targetPath = TryGetPersistedSimulatorImagePath();
        }

        // Kiểm tra xem ảnh đã được nạp và còn hợp lệ không
        if (!string.IsNullOrEmpty(targetPath) && string.Equals(targetPath, _cachedImagePath, StringComparison.OrdinalIgnoreCase) && _cachedBaseMat != null && !_cachedBaseMat.IsDisposed && !_cachedBaseMat.Empty())
        {
            return _cachedBaseMat;
        }

        // Nếu đường dẫn thay đổi hoặc ảnh chưa được cache: nạp lại từ đĩa đúng 1 lần
        if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
        {
            try
            {
                var loaded = Cv2.ImRead(targetPath, ImreadModes.Color);
                if (loaded != null && !loaded.Empty() && loaded.Width > 0 && loaded.Height > 0)
                {
                    _cachedBaseMat?.Dispose();
                    _cachedBaseMat = loaded;
                    _cachedImagePath = targetPath;
                    return _cachedBaseMat;
                }
                loaded?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SimulatorCameraDriver] ImRead error: {ex.Message}");
            }
        }

        // Nếu không có targetPath hoặc file lỗi: Dùng ảnh mặc định Industrial Grid 640x480
        if (_cachedBaseMat != null && !_cachedBaseMat.IsDisposed && !_cachedBaseMat.Empty() && string.IsNullOrEmpty(_cachedImagePath))
        {
            return _cachedBaseMat;
        }

        _cachedBaseMat?.Dispose();
        _cachedBaseMat = CreateDefaultIndustrialGridMat();
        _cachedImagePath = string.Empty;
        return _cachedBaseMat;
    }

    private static string TryGetPersistedSimulatorImagePath()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vision2026", "camera_adjust_settings.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("SimulatorCustomImagePath", out var prop))
                {
                    var str = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(str) && File.Exists(str))
                    {
                        return str;
                    }
                }
            }
        }
        catch { }
        return string.Empty;
    }

    private Mat CreateDefaultIndustrialGridMat()
    {
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
        Cv2.PutText(mat, $"TIME: {DateTime.Now:HH:mm:ss.fff}", new OpenCvSharp.Point(20, 460), HersheyFonts.HersheySimplex, 0.5, new Scalar(200, 200, 200), 1);

        return mat;
    }

    private Mat ApplyRandomTransform(Mat srcMat)
    {
        if (srcMat == null || srcMat.IsDisposed || srcMat.Empty()) return srcMat?.Clone() ?? new Mat();

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
