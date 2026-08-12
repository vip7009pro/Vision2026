using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.UI.Services;

/// <summary>
/// Service để quản lý kết nối camera và capture video stream.
/// Hỗ trợ camera MSMF, DirectShow, OpenCV ANY, RTSP IP stream, và Camera Giả Lập (Simulator).
/// </summary>
public sealed class CameraService : IDisposable
{
    public const int SimulatorCameraIndex = -2;
    public const string SimulatorRtspUrl = "simulator://";

    private VideoCapture? _camera;
    private CancellationTokenSource? _cancellationTokenSource;
    private Thread? _captureThread;
    private bool _isRunning;
    private int _currentCameraIndex = 0;
    private string? _lastSelectedRtspUrl;
    private readonly object _lastFrameGate = new();
    private Mat? _lastFrame;

    private readonly string _settingsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "camera_adjust_settings.json");

    // Camera settings properties
    private double _brightness = 0.0;
    private double _contrast = 1.0;
    private bool _isGrayscale = false;
    private int _savedCameraIndex = 0;
    private string _savedRtspUrl = "";
    private bool _savedIsRtsp = false;
    private int _desiredWidth = 1920;
    private int _desiredHeight = 1080;
    private int _desiredFps = 120;

    public event EventHandler<Mat>? FrameCaptured;
    public event EventHandler<string>? ErrorOccurred;

    public double Brightness
    {
        get => _brightness;
        set { _brightness = value; SaveSettings(); }
    }

    public double Contrast
    {
        get => _contrast;
        set { _contrast = value; SaveSettings(); }
    }

    public bool IsGrayscale
    {
        get => _isGrayscale;
        set { _isGrayscale = value; SaveSettings(); }
    }

    public int SavedCameraIndex
    {
        get => _savedCameraIndex;
        set { _savedCameraIndex = value; SaveSettings(); }
    }

    public string SavedRtspUrl
    {
        get => _savedRtspUrl;
        set { _savedRtspUrl = value; SaveSettings(); }
    }

    public bool SavedIsRtsp
    {
        get => _savedIsRtsp;
        set { _savedIsRtsp = value; SaveSettings(); }
    }

    public int DesiredWidth
    {
        get => _desiredWidth;
        set { _desiredWidth = value; SaveSettings(); }
    }

    public int DesiredHeight
    {
        get => _desiredHeight;
        set { _desiredHeight = value; SaveSettings(); }
    }

    public int DesiredFps
    {
        get => _desiredFps;
        set { _desiredFps = value; SaveSettings(); }
    }

    public CameraService()
    {
        LoadSettings();
    }

    public async Task StartSavedCameraAsync()
    {
        for (int i = 0; i < 3; i++)
        {
            if (SavedIsRtsp && !string.IsNullOrWhiteSpace(SavedRtspUrl))
            {
                await StartCameraCaptureAsync(fps: 30, rtspUrl: SavedRtspUrl);
            }
            else
            {
                await StartCameraCaptureAsync(cameraIndex: SavedCameraIndex, fps: 30);
            }

            if (_isRunning)
            {
                break;
            }

            await Task.Delay(1000);
        }
    }

    public static bool IsSimulator(int cameraIndex, string? rtspUrl)
    {
        return cameraIndex == SimulatorCameraIndex || 
               string.Equals(rtspUrl, SimulatorRtspUrl, StringComparison.OrdinalIgnoreCase) || 
               string.Equals(rtspUrl, "SIMULATOR", StringComparison.OrdinalIgnoreCase);
    }

    public static void ConfigureCaptureFormat(VideoCapture cap, int requestedWidth = 1920, int requestedHeight = 1080, int requestedFps = 120)
    {
        if (cap == null || !cap.IsOpened()) return;

        // 1. Try MJPG video format first - crucial for USB 2.0/3.0 cameras to deliver 1080P/120FPS
        try
        {
            cap.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
        }
        catch { }

        // 2. Request Frame Width & Height
        if (requestedWidth > 0 && requestedHeight > 0)
        {
            try
            {
                cap.Set(VideoCaptureProperties.FrameWidth, requestedWidth);
                cap.Set(VideoCaptureProperties.FrameHeight, requestedHeight);
            }
            catch { }
        }

        // 3. Request FPS
        if (requestedFps > 0)
        {
            try
            {
                cap.Set(VideoCaptureProperties.Fps, requestedFps);
            }
            catch { }
        }
    }

    /// <summary>
    /// Thử mở VideoCapture bằng nhiều backend an toàn (MSMF -> DSHOW -> ANY -> FFMPEG) kèm cấu hình độ phân giải & FPS
    /// </summary>
    public static VideoCapture? TryOpenVideoCapture(int cameraIndex, string? rtspUrl, int requestedWidth = 1920, int requestedHeight = 1080, int requestedFps = 120)
    {
        // 1. Luồng RTSP / IP Camera
        if (!string.IsNullOrWhiteSpace(rtspUrl) && !IsSimulator(cameraIndex, rtspUrl))
        {
            try
            {
                var cap = new VideoCapture(rtspUrl, VideoCaptureAPIs.FFMPEG);
                if (cap.IsOpened())
                {
                    using var testMat = new Mat();
                    for (int i = 0; i < 5; i++)
                    {
                        if (cap.Read(testMat) && !testMat.Empty() && testMat.Width > 0 && testMat.Height > 0)
                        {
                            return cap;
                        }
                        Thread.Sleep(50);
                    }
                }
                cap.Dispose();
            }
            catch { }

            try
            {
                var cap = new VideoCapture(rtspUrl);
                if (cap.IsOpened())
                {
                    using var testMat = new Mat();
                    for (int i = 0; i < 5; i++)
                    {
                        if (cap.Read(testMat) && !testMat.Empty() && testMat.Width > 0 && testMat.Height > 0)
                        {
                            return cap;
                        }
                        Thread.Sleep(50);
                    }
                }
                cap.Dispose();
            }
            catch { }

            return null;
        }

        // 2. Camera chỉ số địa phương (USB, Built-in, Industrial DirectShow/MSMF)
        if (cameraIndex < 0) return null;

        // Backend 1: MSMF (Media Foundation - Khuyên dùng trên Windows 10/11)
        try
        {
            var cap = new VideoCapture(cameraIndex, VideoCaptureAPIs.MSMF);
            if (cap.IsOpened())
            {
                ConfigureCaptureFormat(cap, requestedWidth, requestedHeight, requestedFps);
                using var testMat = new Mat();
                for (int i = 0; i < 15; i++)
                {
                    if (cap.Read(testMat) && !testMat.Empty() && testMat.Width > 0 && testMat.Height > 0)
                    {
                        return cap;
                    }
                    Thread.Sleep(30);
                }
            }
            cap.Dispose();
        }
        catch { }

        // Backend 2: DSHOW (DirectShow - Bắt buộc cho DroidCam, Cam ảo OBS, DirectShow Industrial Filter)
        try
        {
            var cap = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
            if (cap.IsOpened())
            {
                ConfigureCaptureFormat(cap, requestedWidth, requestedHeight, requestedFps);
                using var testMat = new Mat();
                for (int i = 0; i < 15; i++)
                {
                    if (cap.Read(testMat) && !testMat.Empty() && testMat.Width > 0 && testMat.Height > 0)
                    {
                        return cap;
                    }
                    Thread.Sleep(30);
                }
            }
            cap.Dispose();
        }
        catch { }

        // Backend 3: ANY (OpenCV mặc định)
        try
        {
            var cap = new VideoCapture(cameraIndex, VideoCaptureAPIs.ANY);
            if (cap.IsOpened())
            {
                ConfigureCaptureFormat(cap, requestedWidth, requestedHeight, requestedFps);
                using var testMat = new Mat();
                for (int i = 0; i < 15; i++)
                {
                    if (cap.Read(testMat) && !testMat.Empty() && testMat.Width > 0 && testMat.Height > 0)
                    {
                        return cap;
                    }
                    Thread.Sleep(30);
                }
            }
            cap.Dispose();
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Khởi động capture camera với chỉ số camera hoặc địa chỉ RTSP
    /// </summary>
    public async Task StartCameraCaptureAsync(int cameraIndex = 0, string? rtspUrl = null, int fps = 30)
    {
        if (_isRunning)
        {
            bool isSame = (IsSimulator(cameraIndex, rtspUrl) && IsSimulator(_currentCameraIndex, _lastSelectedRtspUrl))
                || (!string.IsNullOrEmpty(rtspUrl) && string.Equals(rtspUrl, _lastSelectedRtspUrl, StringComparison.OrdinalIgnoreCase))
                || (string.IsNullOrEmpty(rtspUrl) && string.IsNullOrEmpty(_lastSelectedRtspUrl) && cameraIndex == _currentCameraIndex);

            if (isSame && _captureThread != null && _captureThread.IsAlive)
            {
                return;
            }

            await StopCameraAsync();
        }

        try
        {
            _currentCameraIndex = cameraIndex;
            _lastSelectedRtspUrl = rtspUrl;

            SavedCameraIndex = cameraIndex;
            SavedRtspUrl = rtspUrl ?? "";
            SavedIsRtsp = !string.IsNullOrEmpty(rtspUrl);

            bool isSim = IsSimulator(cameraIndex, rtspUrl);
            VideoCapture? cap = null;

            if (!isSim)
            {
                // Giải phóng camera cũ nếu có trước khi mở mới
                try
                {
                    _camera?.Dispose();
                    _camera = null;
                }
                catch { }

                cap = TryOpenVideoCapture(cameraIndex, rtspUrl, _desiredWidth, _desiredHeight, _desiredFps);
                if (cap == null || !cap.IsOpened())
                {
                    cap?.Dispose();
                    _isRunning = false;
                    ErrorOccurred?.Invoke(this, "Không thể nhận dữ liệu từ camera. Vui lòng đảm bảo DroidCam Client / Camera đã kết nối và ứng dụng khác (OBS, Zoom, Windows Camera) không chiếm quyền.");
                    return;
                }

                if (string.IsNullOrEmpty(rtspUrl))
                {
                    try { cap.Set(VideoCaptureProperties.BufferSize, 1); } catch { }
                }
            }

            _camera = cap;
            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _captureThread = new Thread(() => CaptureLoop(_cancellationTokenSource.Token, isSim))
            {
                IsBackground = true,
                Name = "CameraCaptureThread"
            };
            _captureThread.Start();

            await Task.Delay(100);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Lỗi khởi động camera: {ex.Message}");
            _isRunning = false;
        }
    }

    /// <summary>
    /// Dừng capture camera
    /// </summary>
    public async Task StopCameraAsync()
    {
        if (!_isRunning && _captureThread == null)
            return;

        _isRunning = false;
        _cancellationTokenSource?.Cancel();

        if (_captureThread != null)
        {
            _captureThread.Join(500);
            _captureThread = null;
        }

        _camera?.Dispose();
        _camera = null;

        await Task.CompletedTask;
    }

    /// <summary>
    /// Tạo khung ảnh giả lập (Camera Simulator) cho mục đích kiểm thử động
    /// </summary>
    private static Mat GenerateSimulatorFrame(ref int frameCounter)
    {
        frameCounter++;
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

        // Mục tiêu di chuyển sinh động
        double angle = (frameCounter % 120) * (2 * Math.PI / 120);
        int cx = 320 + (int)(150 * Math.Cos(angle));
        int cy = 240 + (int)(100 * Math.Sin(angle));

        Cv2.Circle(mat, new OpenCvSharp.Point(cx, cy), 40, new Scalar(0, 255, 255), 2);
        Cv2.Circle(mat, new OpenCvSharp.Point(cx, cy), 15, new Scalar(0, 165, 255), -1);
        Cv2.DrawMarker(mat, new OpenCvSharp.Point(cx, cy), new Scalar(0, 0, 255), MarkerTypes.Cross, 30, 2);

        Cv2.PutText(mat, "CAMERA SIMULATOR (INDUSTRIAL TEST)", new OpenCvSharp.Point(20, 35), HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);
        Cv2.PutText(mat, $"TIME: {DateTime.Now:HH:mm:ss.fff}  FRAME: {frameCounter}", new OpenCvSharp.Point(20, 460), HersheyFonts.HersheySimplex, 0.5, new Scalar(200, 200, 200), 1);

        return mat;
    }

    /// <summary>
    /// Vòng lặp capture frame chính chạy trên Thread nền
    /// </summary>
    private void CaptureLoop(CancellationToken cancellationToken, bool isSimulator)
    {
        var frameMat = new Mat();
        var sw = new System.Diagnostics.Stopwatch();
        int errorCount = 0;
        int simFrameCounter = 0;

        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            sw.Restart();

            try
            {
                if (isSimulator)
                {
                    frameMat.Dispose();
                    frameMat = GenerateSimulatorFrame(ref simFrameCounter);
                }
                else
                {
                    if (_camera == null || !_camera.Read(frameMat) || frameMat.Empty())
                    {
                        errorCount++;
                        if (errorCount > 150)
                        {
                            ErrorOccurred?.Invoke(this, "Mất luồng truyền hình ảnh từ camera hoặc camera bị chiếm dụng bởi ứng dụng khác.");
                            break;
                        }
                        Thread.Sleep(33);
                        continue;
                    }
                }

                errorCount = 0;

                using var processedFrame = ApplyCameraSettings(frameMat);

                lock (_lastFrameGate)
                {
                    _lastFrame?.Dispose();
                    _lastFrame = processedFrame.Clone();
                }

                if (FrameCaptured != null)
                {
                    using var eventFrame = processedFrame.Clone();
                    FrameCaptured.Invoke(this, eventFrame);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraService] Lỗi capture frame: {ex.Message}");
            }

            sw.Stop();
            int elapsed = (int)sw.ElapsedMilliseconds;
            int delay = Math.Max(5, 33 - elapsed);
            Thread.Sleep(delay);
        }

        frameMat.Dispose();
        try
        {
            _camera?.Dispose();
            _camera = null;
        }
        catch { }
        _isRunning = false;
    }

    /// <summary>
    /// Áp dụng các cài đặt Brightness, Contrast, Grayscale lên Mat gốc của camera
    /// </summary>
    private Mat ApplyCameraSettings(Mat input)
    {
        if (input == null || input.Empty())
            return new Mat();

        try
        {
            var output = new Mat();
            double safeContrast = _contrast <= 0.01 ? 1.0 : Math.Clamp(_contrast, 0.1, 5.0);
            double safeBrightness = Math.Clamp(_brightness, -255.0, 255.0);

            input.ConvertTo(output, -1, safeContrast, safeBrightness);

            if (_isGrayscale)
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

            return output;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraService] ApplyCameraSettings error: {ex.Message}");
            return input.Clone();
        }
    }

    /// <summary>
    /// Lấy danh sách camera sẵn có
    /// </summary>
    public static int[] GetAvailableCameras()
    {
        var availableCameras = new List<int>();

        for (int i = 0; i < 10; i++)
        {
            try
            {
                using var camera = TryOpenVideoCapture(i, null);
                if (camera != null && camera.IsOpened())
                {
                    availableCameras.Add(i);
                }
            }
            catch { }
        }

        return availableCameras.ToArray();
    }

    /// <summary>
    /// Chụp ảnh tĩnh bất đồng bộ từ camera hiện tại.
    /// Trả về frame mới nhất nếu camera đang chạy.
    /// </summary>
    public async Task<Mat?> CaptureSnapshotAsync()
    {
        if (_isRunning)
        {
            for (int i = 0; i < 10; i++)
            {
                var f = TryGetLatestFrameClone();
                if (f != null && !f.Empty()) return f;
                await Task.Delay(50);
            }
            return TryGetLatestFrameClone();
        }

        return await CaptureSnapshotFromCameraAsync(_currentCameraIndex, _lastSelectedRtspUrl);
    }

    /// <summary>
    /// Chụp ảnh tĩnh từ camera theo chỉ số hoặc RTSP URL chỉ định.
    /// Dùng cho ImageSource node khi cần capture từ camera cấu hình riêng.
    /// </summary>
    public async Task<Mat?> CaptureSnapshotAsync(int cameraIndex, string? rtspUrl)
    {
        bool isSameCamera = _isRunning
            && ((IsSimulator(cameraIndex, rtspUrl) && IsSimulator(_currentCameraIndex, _lastSelectedRtspUrl))
                || (!string.IsNullOrEmpty(rtspUrl) && string.Equals(rtspUrl, _lastSelectedRtspUrl, StringComparison.OrdinalIgnoreCase))
                || (string.IsNullOrEmpty(rtspUrl) && string.IsNullOrEmpty(_lastSelectedRtspUrl) && cameraIndex == _currentCameraIndex));

        if (isSameCamera)
        {
            for (int i = 0; i < 10; i++)
            {
                var f = TryGetLatestFrameClone();
                if (f != null && !f.Empty()) return f;
                await Task.Delay(50);
            }
            return TryGetLatestFrameClone();
        }

        return await CaptureSnapshotFromCameraAsync(cameraIndex, rtspUrl);
    }

    /// <summary>
    /// Logic chung: mở camera tạm thời, xả warmup frame để đọc ảnh thực tế, chụp 1 frame rồi đóng.
    /// </summary>
    private async Task<Mat?> CaptureSnapshotFromCameraAsync(int cameraIndex, string? rtspUrl)
    {
        return await Task.Run(() =>
        {
            if (IsSimulator(cameraIndex, rtspUrl))
            {
                int cnt = (int)(DateTime.Now.Ticks % 1000);
                var rawSim = GenerateSimulatorFrame(ref cnt);
                var processedSim = ApplyCameraSettings(rawSim);
                rawSim.Dispose();
                return processedSim;
            }

            VideoCapture? cap = null;
            try
            {
                cap = TryOpenVideoCapture(cameraIndex, rtspUrl, _desiredWidth, _desiredHeight, _desiredFps);
                if (cap == null || !cap.IsOpened())
                {
                    cap?.Dispose();
                    return null;
                }

                try { cap.Set(VideoCaptureProperties.BufferSize, 1); } catch { }

                var frame = new Mat();
                bool gotFrame = false;
                for (int i = 0; i < 30; i++)
                {
                    if (cap.Read(frame) && !frame.Empty() && frame.Width > 0 && frame.Height > 0)
                    {
                        gotFrame = true;
                        break;
                    }
                    Thread.Sleep(100);
                }

                if (!gotFrame || frame.Empty())
                {
                    frame.Dispose();
                    cap.Dispose();
                    return null;
                }

                var processed = ApplyCameraSettings(frame);
                frame.Dispose();
                cap.Dispose();
                return processed;
            }
            catch
            {
                cap?.Dispose();
                return null;
            }
        });
    }

    /// <summary>
    /// Lấy frame mới nhất (clone) theo kiểu thread-safe.
    /// </summary>
    public Mat? TryGetLatestFrameClone()
    {
        lock (_lastFrameGate)
        {
            return _lastFrame?.Clone();
        }
    }

    public bool IsRunning => _isRunning;

    public int CurrentCameraIndex => _currentCameraIndex;

    public string? CurrentRtspUrl => _lastSelectedRtspUrl;

    private void LoadSettings()
    {
        try
        {
            if (System.IO.File.Exists(_settingsPath))
            {
                var json = System.IO.File.ReadAllText(_settingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<CameraAdjustSettings>(json);
                if (settings != null)
                {
                    _brightness = settings.Brightness;
                    _contrast = settings.Contrast;
                    _isGrayscale = settings.IsGrayscale;
                    _savedCameraIndex = settings.SavedCameraIndex;
                    _savedRtspUrl = settings.SavedRtspUrl;
                    _savedIsRtsp = settings.SavedIsRtsp;
                    _desiredWidth = settings.DesiredWidth > 0 ? settings.DesiredWidth : 1920;
                    _desiredHeight = settings.DesiredHeight > 0 ? settings.DesiredHeight : 1080;
                    _desiredFps = settings.DesiredFps > 0 ? settings.DesiredFps : 120;
                    return;
                }
            }
        }
        catch
        {
        }

        _brightness = 0.0;
        _contrast = 1.0;
        _isGrayscale = false;
        _savedCameraIndex = 0;
        _savedRtspUrl = "";
        _savedIsRtsp = false;
        _desiredWidth = 1920;
        _desiredHeight = 1080;
        _desiredFps = 120;
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new CameraAdjustSettings
            {
                Brightness = _brightness,
                Contrast = _contrast,
                IsGrayscale = _isGrayscale,
                SavedCameraIndex = _savedCameraIndex,
                SavedRtspUrl = _savedRtspUrl ?? "",
                SavedIsRtsp = _savedIsRtsp,
                DesiredWidth = _desiredWidth,
                DesiredHeight = _desiredHeight,
                DesiredFps = _desiredFps
            };
            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(_settingsPath, json);
        }
        catch
        {
        }
    }

    private class CameraAdjustSettings
    {
        public double Brightness { get; set; }
        public double Contrast { get; set; }
        public bool IsGrayscale { get; set; }
        public int SavedCameraIndex { get; set; }
        public string SavedRtspUrl { get; set; } = "";
        public bool SavedIsRtsp { get; set; }
        public int DesiredWidth { get; set; } = 1920;
        public int DesiredHeight { get; set; } = 1080;
        public int DesiredFps { get; set; } = 120;
    }

    public void Dispose()
    {
        StopCameraAsync().Wait();
        _cancellationTokenSource?.Dispose();
        _camera?.Dispose();
        _camera = null;
        lock (_lastFrameGate)
        {
            _lastFrame?.Dispose();
            _lastFrame = null;
        }
    }
}
