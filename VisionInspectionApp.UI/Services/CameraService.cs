using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.UI.Services;

/// <summary>
/// Service để quản lý kết nối camera và capture video stream
/// </summary>
public sealed class CameraService : IDisposable
{
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

    /// <summary>
    /// Khởi động capture camera với chỉ số camera hoặc địa chỉ RTSP
    /// </summary>
    public async Task StartCameraCaptureAsync(int cameraIndex = 0, string? rtspUrl = null, int fps = 30)
    {
        if (_isRunning)
            return;

        try
        {
            _currentCameraIndex = cameraIndex;
            _lastSelectedRtspUrl = rtspUrl;

            // Lưu cài đặt camera đã mở
            SavedCameraIndex = cameraIndex;
            SavedRtspUrl = rtspUrl ?? "";
            SavedIsRtsp = !string.IsNullOrEmpty(rtspUrl);

            // Hàm helper để thử mở và cấu hình camera
            bool TryOpenAndConfigure(bool configureSettings)
            {
                if (!string.IsNullOrEmpty(rtspUrl))
                {
                    _camera = new VideoCapture(rtspUrl);
                }
                else
                {
                    // Thử sử dụng DirectShow backend trước vì nó hoạt động rất ổn định trên Windows
                    _camera = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);

                    if (!_camera.IsOpened())
                    {
                        _camera.Dispose();
                        // Fallback về mặc định
                        _camera = new VideoCapture(cameraIndex);
                    }
                }

                if (!_camera.IsOpened())
                {
                    _camera.Dispose();
                    _camera = null;
                    return false;
                }

                // Cấu hình camera (chỉ đối với camera USB thông thường)
                if (string.IsNullOrEmpty(rtspUrl))
                {
                    if (configureSettings)
                    {
                        _camera.Set(VideoCaptureProperties.FrameWidth, 1280);
                        _camera.Set(VideoCaptureProperties.FrameHeight, 720);
                        _camera.Set(VideoCaptureProperties.Fps, fps);
                        _camera.Set(VideoCaptureProperties.BufferSize, 1);
                    }
                    else
                    {
                        // Fallback: giữ nguyên cấu hình gốc của thiết bị (đối với DroidCam bản Free chỉ hỗ trợ 640x480)
                        _camera.Set(VideoCaptureProperties.BufferSize, 1);
                    }
                }

                // Đọc thử 1 frame để kiểm tra xem camera có xuất hình ảnh thực tế hay đang bị khóa/lỗi
                using var testFrame = new Mat();
                if (_camera.Read(testFrame) && !testFrame.Empty())
                {
                    return true;
                }

                _camera.Dispose();
                _camera = null;
                return false;
            }

            // Thử mở lần 1: với cấu hình chất lượng HD
            bool opened = TryOpenAndConfigure(true);

            // Thử mở lần 2 (nếu lần 1 thất bại): giữ nguyên độ phân giải mặc định của camera
            if (!opened)
            {
                opened = TryOpenAndConfigure(false);
            }

            if (!opened || _camera == null)
            {
                ErrorOccurred?.Invoke(this, "Không thể nhận dữ liệu hình ảnh từ camera. Vui lòng đảm bảo bạn đã đóng các ứng dụng đang chiếm quyền camera khác (như Windows Camera app, OBS, trình duyệt,...) và DroidCam Client đã được kết nối.");
                _isRunning = false;
                return;
            }

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            // Khởi động Thread Capture đồng bộ độc lập giúp chạy cực kỳ mượt mà, không bị trễ/nghẽn luồng Task
            _captureThread = new Thread(() => CaptureLoop(_cancellationTokenSource.Token))
            {
                IsBackground = true,
                Name = "CameraCaptureThread"
            };
            _captureThread.Start();

            await Task.Delay(100); // Chờ camera ready
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
        if (!_isRunning)
            return;

        _isRunning = false;
        _cancellationTokenSource?.Cancel();

        if (_captureThread != null)
        {
            // Chờ luồng capture kết thúc trong tối đa 500ms để không treo UI
            _captureThread.Join(500);
            _captureThread = null;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Vòng lặp capture frame chính chạy trên Thread nền
    /// </summary>
    private void CaptureLoop(CancellationToken cancellationToken)
    {
        var frameMat = new Mat();
        var sw = new System.Diagnostics.Stopwatch();
        int errorCount = 0;

        while (!cancellationToken.IsCancellationRequested && _isRunning && _camera != null)
        {
            sw.Restart();

            try
            {
                if (!_camera.Read(frameMat) || frameMat.Empty())
                {
                    errorCount++;
                    if (errorCount > 100) // Đợi ~3 giây mất frame liên tiếp trước khi ngắt kết nối
                    {
                        ErrorOccurred?.Invoke(this, "Mất luồng truyền hình ảnh từ camera hoặc camera bị chiếm dụng bởi ứng dụng khác.");
                        break;
                    }
                    Thread.Sleep(30);
                    continue;
                }

                errorCount = 0; // Reset đếm lỗi khi đọc thành công

                // Áp dụng các cấu hình điều chỉnh hình ảnh đầu vào gốc
                using var processedFrame = ApplyCameraSettings(frameMat);

                // Store last frame for background consumers (e.g., PLC trigger).
                lock (_lastFrameGate)
                {
                    _lastFrame?.Dispose();
                    _lastFrame = processedFrame.Clone();
                }

                // Fire event với frame mới
                FrameCaptured?.Invoke(this, processedFrame.Clone());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraService] Lỗi capture frame: {ex.Message}");
            }

            sw.Stop();
            // Khống chế cứng tốc độ khung hình ở mức ~30 FPS (33ms cho mỗi frame)
            // Việc này cực kỳ quan trọng để tránh nghẽn buffer camera và tránh quá tải mạng DroidCam
            int elapsed = (int)sw.ElapsedMilliseconds;
            int delay = Math.Max(5, 33 - elapsed);
            Thread.Sleep(delay);
        }

        frameMat.Dispose();
        _isRunning = false;
    }

    /// <summary>
    /// Áp dụng các cài đặt Brightness, Contrast, Grayscale lên Mat gốc của camera
    /// </summary>
    private Mat ApplyCameraSettings(Mat input)
    {
        if (input == null || input.Empty())
            return new Mat();

        var output = new Mat();
        
        // 1. Áp dụng Tương phản (Contrast: alpha) và Độ sáng (Brightness: beta)
        // new_pixel = alpha * old_pixel + beta
        input.ConvertTo(output, -1, _contrast, _brightness);

        // 2. Áp dụng chế độ ảnh Xám (Grayscale)
        if (_isGrayscale)
        {
            using var gray = new Mat();
            Cv2.CvtColor(output, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(gray, output, ColorConversionCodes.GRAY2BGR); // Chuyển lại 3 kênh màu để tương thích với các view hiển thị
        }

        return output;
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
                using var camera = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                if (camera.IsOpened())
                {
                    availableCameras.Add(i);
                    camera.Release();
                    continue;
                }
            }
            catch
            {
                // Bỏ qua
            }

            try
            {
                using var camera = new VideoCapture(i);
                if (camera.IsOpened())
                {
                    availableCameras.Add(i);
                    camera.Release();
                }
            }
            catch
            {
                // Bỏ qua
            }
        }

        return availableCameras.ToArray();
    }

    /// <summary>
    /// Chụp ảnh tĩnh bất đồng bộ từ camera hiện tại.
    /// Nếu camera đang chạy, trả về frame mới nhất ngay lập tức.
    /// Nếu camera không chạy, mở tạm thời để chụp 1 frame rồi tắt.
    /// </summary>
    public async Task<Mat?> CaptureSnapshotAsync()
    {
        if (_isRunning)
        {
            return TryGetLatestFrameClone();
        }

        return await Task.Run(async () =>
        {
            VideoCapture? tempCamera = null;
            try
            {
                if (!string.IsNullOrEmpty(_lastSelectedRtspUrl))
                {
                    tempCamera = new VideoCapture(_lastSelectedRtspUrl);
                }
                else
                {
                    tempCamera = new VideoCapture(_currentCameraIndex, VideoCaptureAPIs.DSHOW);
                    if (!tempCamera.IsOpened())
                    {
                        tempCamera.Dispose();
                        tempCamera = new VideoCapture(_currentCameraIndex);
                    }
                }

                if (tempCamera == null || !tempCamera.IsOpened())
                {
                    tempCamera?.Dispose();
                    return null;
                }

                // Chờ 500ms cho cảm biến camera ổn định độ sáng và phơi sáng
                await Task.Delay(500);

                var frame = new Mat();
                if (tempCamera.Read(frame) && !frame.Empty())
                {
                    // Vẫn áp dụng cài đặt xử lý ảnh cho ảnh chụp tĩnh
                    var processed = ApplyCameraSettings(frame);
                    frame.Dispose();
                    tempCamera.Dispose();
                    return processed;
                }

                frame.Dispose();
                tempCamera.Dispose();
            }
            catch
            {
                tempCamera?.Dispose();
            }
            return null;
        });
    }

    /// <summary>
    /// Lấy frame mới nhất (clone) theo kiểu thread-safe.
    /// Caller phải Dispose Mat trả về.
    /// </summary>
    public Mat? TryGetLatestFrameClone()
    {
        lock (_lastFrameGate)
        {
            return _lastFrame?.Clone();
        }
    }

    /// <summary>
    /// Kiểm tra camera đang chạy hay không
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Camera hiện tại
    /// </summary>
    public int CurrentCameraIndex => _currentCameraIndex;

    /// <summary>
    /// RTSP URL hiện tại của camera mạng
    /// </summary>
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
                    return;
                }
            }
        }
        catch
        {
            // Bỏ qua lỗi load cài đặt mặc định
        }

        // Cài đặt mặc định
        _brightness = 0.0;
        _contrast = 1.0;
        _isGrayscale = false;
        _savedCameraIndex = 0;
        _savedRtspUrl = "";
        _savedIsRtsp = false;
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
                SavedIsRtsp = _savedIsRtsp
            };
            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Bỏ qua
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
