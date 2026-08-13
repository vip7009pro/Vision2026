using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.UI.Services.Camera;
using VisionInspectionApp.UI.Services.Camera.Drivers;

namespace VisionInspectionApp.UI.Services;

/// <summary>
/// Service trung tâm quản lý kết nối camera công nghiệp đa hãng (Hikrobot, Basler, Cognex, USB, RTSP, Simulator).
/// Sử dụng hệ thống kiến trúc trừu tượng ICameraDriver mở rộng linh hoạt.
/// </summary>
public sealed class CameraService : IDisposable
{
    public const int SimulatorCameraIndex = -2;
    public const string SimulatorRtspUrl = "simulator://";

    private ICameraDriver? _activeDriver;
    private CameraDeviceInfo? _activeDeviceInfo;
    private CameraParameters _currentParameters = new();

    private bool _isRunning;
    private bool _isDisposed;
    private readonly SemaphoreSlim _cameraLock = new(1, 1);
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
    private string _simulatorCustomImagePath = "";

    public event EventHandler<Mat>? FrameCaptured;
    public event EventHandler<string>? ErrorOccurred;

    public ICameraDriver? ActiveDriver => _activeDriver;
    public CameraDeviceInfo? ActiveDeviceInfo => _activeDeviceInfo;
    public CameraParameters CurrentParameters => _currentParameters;

    public string SimulatorCustomImagePath
    {
        get => _simulatorCustomImagePath;
        set
        {
            _simulatorCustomImagePath = value;
            _currentParameters.CustomImagePath = value;
            SaveSettings();
            if (_activeDriver != null && _activeDriver.IsOpened)
            {
                _ = _activeDriver.ApplyParametersAsync(_currentParameters);
            }
        }
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            _brightness = value;
            _currentParameters.Brightness = value;
            SaveSettings();
            if (_activeDriver != null && _activeDriver.IsOpened)
            {
                _ = _activeDriver.ApplyParametersAsync(_currentParameters);
            }
        }
    }

    public double Contrast
    {
        get => _contrast;
        set
        {
            _contrast = value;
            _currentParameters.Contrast = value;
            SaveSettings();
            if (_activeDriver != null && _activeDriver.IsOpened)
            {
                _ = _activeDriver.ApplyParametersAsync(_currentParameters);
            }
        }
    }

    public bool IsGrayscale
    {
        get => _isGrayscale;
        set
        {
            _isGrayscale = value;
            _currentParameters.IsGrayscale = value;
            SaveSettings();
            if (_activeDriver != null && _activeDriver.IsOpened)
            {
                _ = _activeDriver.ApplyParametersAsync(_currentParameters);
            }
        }
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
        set { _desiredWidth = value; _currentParameters.Width = value; SaveSettings(); }
    }

    public int DesiredHeight
    {
        get => _desiredHeight;
        set { _desiredHeight = value; _currentParameters.Height = value; SaveSettings(); }
    }

    public int DesiredFps
    {
        get => _desiredFps;
        set { _desiredFps = value; _currentParameters.TargetFps = value; SaveSettings(); }
    }

    public CameraService()
    {
        LoadSettings();
        _currentParameters.Brightness = _brightness;
        _currentParameters.Contrast = _contrast;
        _currentParameters.IsGrayscale = _isGrayscale;
        _currentParameters.Width = _desiredWidth;
        _currentParameters.Height = _desiredHeight;
        _currentParameters.TargetFps = _desiredFps;
    }

    public async Task StartSavedCameraAsync()
    {
        if (_isDisposed) return;

        try
        {
            await _cameraLock.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_isDisposed || (_isRunning && _activeDriver != null && _activeDriver.IsOpened))
            {
                return;
            }

            for (int i = 0; i < 3; i++)
            {
                if (_isDisposed) break;

                if (SavedIsRtsp && !string.IsNullOrWhiteSpace(SavedRtspUrl))
                {
                    await StartCameraCaptureInternalAsync(fps: 30, rtspUrl: SavedRtspUrl);
                }
                else
                {
                    await StartCameraCaptureInternalAsync(cameraIndex: SavedCameraIndex, fps: 30);
                }

                if (_isRunning)
                {
                    break;
                }

                await Task.Delay(300);
            }
        }
        finally
        {
            try { _cameraLock.Release(); } catch { }
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
        // Nhằm triệt tiêu 100% ngoại lệ AccessViolationException từ C++ native OpenCV (cả DSHOW lẫn MSMF backend),
        // ứng dụng không gọi cap.Set(FrameWidth/FrameHeight/Fps) vì các webcam USB/Virtual camera trên Windows
        // sẽ bị crash trong C++ native pin filter khi thay đổi thuộc tính luồng.
        // Camera sẽ tự động hoạt động mượt mà ở độ phân giải gốc của thiết bị.
    }

    public static VideoCapture? TryOpenVideoCapture(int cameraIndex, string? rtspUrl, int requestedWidth = 1920, int requestedHeight = 1080, int requestedFps = 120)
    {
        // Luồng RTSP / IP Camera
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

        if (cameraIndex < 0) return null;

        // Kiểm tra xem thiết bị webcam USB thực tế có tồn tại trên máy hay không trước khi gọi OpenCV native C++
        try
        {
            var dsCameras = DirectShowDeviceEnumerator.GetDevices();
            if (dsCameras.Count == 0 || cameraIndex >= dsCameras.Count)
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        // Backend 1: DSHOW (DirectShow - Chuẩn ổn định nhất trên Windows)
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

        Thread.Sleep(100);

        // Backend 2: MSMF (Media Foundation)
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

        Thread.Sleep(100);

        // Backend 3: ANY
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
    /// Khởi động camera công nghiệp bằng Driver đa hãng (Hikrobot, Basler, Cognex, USB, RTSP, Simulator)
    /// Tự động fallback sang Camera Giả Lập nếu thiết bị thực tế chưa cắm hoặc chưa sẵn sàng.
    /// </summary>
    public async Task<bool> StartDriverCameraAsync(CameraDeviceInfo device, CameraParameters? parameters = null)
    {
        if (_isDisposed) return false;

        try
        {
            await _cameraLock.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        try
        {
            if (_isDisposed) return false;
            return await StartDriverCameraInternalAsync(device, parameters);
        }
        finally
        {
            try { _cameraLock.Release(); } catch { }
        }
    }

    private async Task<bool> StartDriverCameraInternalAsync(CameraDeviceInfo device, CameraParameters? parameters = null)
    {
        await StopCameraInternalAsync();

        try
        {
            _activeDeviceInfo = device;
            if (parameters != null)
            {
                _currentParameters = parameters.Clone();
            }

            _activeDriver = CameraDriverFactory.CreateDriver(device.Vendor);
            _activeDriver.FrameCaptured += OnDriverFrameCaptured;
            _activeDriver.ErrorOccurred += OnDriverError;

            bool opened = await _activeDriver.OpenAsync(device);
            if (!opened)
            {
                if (device.Vendor != CameraVendor.Simulator)
                {
                    // Fallback tự động sang Camera Giả Lập khi camera phần cứng chưa cắm
                    _activeDriver.FrameCaptured -= OnDriverFrameCaptured;
                    _activeDriver.ErrorOccurred -= OnDriverError;
                    _activeDriver.Dispose();

                    var simDev = new CameraDeviceInfo
                    {
                        Vendor = CameraVendor.Simulator,
                        InterfaceType = CameraInterfaceType.Virtual,
                        Index = SimulatorCameraIndex,
                        ModelName = "🎮 Camera Giả Lập (Simulator Fallback)"
                    };
                    _activeDeviceInfo = simDev;
                    _activeDriver = CameraDriverFactory.CreateDriver(CameraVendor.Simulator);
                    _activeDriver.FrameCaptured += OnDriverFrameCaptured;
                    _activeDriver.ErrorOccurred += OnDriverError;

                    await _activeDriver.OpenAsync(simDev);
                    await _activeDriver.ApplyParametersAsync(_currentParameters);
                    bool simGrabbing = await _activeDriver.StartGrabbingAsync();
                    _isRunning = simGrabbing;
                    return simGrabbing;
                }

                _isRunning = false;
                return false;
            }

            await _activeDriver.ApplyParametersAsync(_currentParameters);
            bool grabbing = await _activeDriver.StartGrabbingAsync();

            _isRunning = grabbing;
            return grabbing;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Lỗi khởi động Driver camera: {ex.Message}");
            _isRunning = false;
            return false;
        }
    }

    public async Task StartCameraCaptureAsync(int cameraIndex = 0, string? rtspUrl = null, int fps = 30)
    {
        if (_isDisposed) return;

        try
        {
            await _cameraLock.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_isDisposed) return;
            await StartCameraCaptureInternalAsync(cameraIndex, rtspUrl, fps);
        }
        finally
        {
            try { _cameraLock.Release(); } catch { }
        }
    }

    private async Task StartCameraCaptureInternalAsync(int cameraIndex = 0, string? rtspUrl = null, int fps = 30)
    {
        CameraDeviceInfo dev;

        if (IsSimulator(cameraIndex, rtspUrl))
        {
            dev = new CameraDeviceInfo
            {
                Vendor = CameraVendor.Simulator,
                InterfaceType = CameraInterfaceType.Virtual,
                Index = SimulatorCameraIndex,
                ModelName = "📷 Camera Giả Lập Công Nghiệp"
            };
        }
        else if (!string.IsNullOrEmpty(rtspUrl))
        {
            dev = new CameraDeviceInfo
            {
                Vendor = CameraVendor.Rtsp,
                InterfaceType = CameraInterfaceType.RTSP,
                Index = -1,
                ModelName = "Custom RTSP Camera",
                RtspUrl = rtspUrl
            };
        }
        else
        {
            dev = new CameraDeviceInfo
            {
                Vendor = CameraVendor.WebcamDirectShow,
                InterfaceType = CameraInterfaceType.DirectShow,
                Index = cameraIndex,
                ModelName = $"USB Camera Port {cameraIndex}"
            };
        }

        _currentCameraIndex = cameraIndex;
        _lastSelectedRtspUrl = rtspUrl;
        SavedCameraIndex = cameraIndex;
        SavedRtspUrl = rtspUrl ?? "";
        SavedIsRtsp = !string.IsNullOrEmpty(rtspUrl);

        await StartDriverCameraInternalAsync(dev, _currentParameters);
    }

    public async Task StopCameraAsync()
    {
        if (_isDisposed) return;

        try
        {
            await _cameraLock.WaitAsync();
        }
        catch (ObjectDisposedException)
        {
            await StopCameraInternalAsync();
            return;
        }

        try
        {
            await StopCameraInternalAsync();
        }
        finally
        {
            try { _cameraLock.Release(); } catch { }
        }
    }

    private async Task StopCameraInternalAsync()
    {
        _isRunning = false;

        if (_activeDriver != null)
        {
            _activeDriver.FrameCaptured -= OnDriverFrameCaptured;
            _activeDriver.ErrorOccurred -= OnDriverError;
            await _activeDriver.StopGrabbingAsync();
            await _activeDriver.CloseAsync();
            _activeDriver.Dispose();
            _activeDriver = null;
        }

        await Task.CompletedTask;
    }

    public async Task<bool> ExecuteSoftwareTriggerAsync()
    {
        if (_activeDriver != null && _activeDriver.IsOpened)
        {
            return await _activeDriver.ExecuteSoftwareTriggerAsync();
        }
        return false;
    }

    public async Task ApplyParametersAsync(CameraParameters parameters)
    {
        _currentParameters = parameters.Clone();
        Brightness = parameters.Brightness;
        Contrast = parameters.Contrast;
        IsGrayscale = parameters.IsGrayscale;
        DesiredWidth = parameters.Width;
        DesiredHeight = parameters.Height;
        DesiredFps = parameters.TargetFps;

        if (_activeDriver != null && _activeDriver.IsOpened)
        {
            await _activeDriver.ApplyParametersAsync(_currentParameters);
        }
    }

    private void OnDriverFrameCaptured(object? sender, Mat frame)
    {
        if (frame == null || frame.IsDisposed || frame.Empty()) return;

        lock (_lastFrameGate)
        {
            _lastFrame?.Dispose();
            _lastFrame = frame.Clone();
        }

        FrameCaptured?.Invoke(this, frame.Clone());
    }

    private void OnDriverError(object? sender, string message)
    {
        ErrorOccurred?.Invoke(this, message);
    }

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

    public async Task<Mat?> CaptureSnapshotAsync()
    {
        if (_isRunning && _activeDriver != null)
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

    public async Task<Mat?> CaptureSnapshotAsync(int cameraIndex, string? rtspUrl)
    {
        if (_isRunning && _activeDriver != null)
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

    private async Task<Mat?> CaptureSnapshotFromCameraAsync(int cameraIndex, string? rtspUrl)
    {
        CameraDeviceInfo dev;
        if (IsSimulator(cameraIndex, rtspUrl))
        {
            dev = new CameraDeviceInfo { Vendor = CameraVendor.Simulator, InterfaceType = CameraInterfaceType.Virtual, Index = SimulatorCameraIndex, ModelName = "Simulator" };
        }
        else if (!string.IsNullOrEmpty(rtspUrl))
        {
            dev = new CameraDeviceInfo { Vendor = CameraVendor.Rtsp, InterfaceType = CameraInterfaceType.RTSP, Index = -1, RtspUrl = rtspUrl };
        }
        else
        {
            dev = new CameraDeviceInfo { Vendor = CameraVendor.WebcamDirectShow, InterfaceType = CameraInterfaceType.DirectShow, Index = cameraIndex };
        }

        using var driver = CameraDriverFactory.CreateDriver(dev.Vendor);
        if (await driver.OpenAsync(dev))
        {
            await driver.ApplyParametersAsync(_currentParameters);
            var mat = await driver.GrabFrameAsync(2000);
            await driver.CloseAsync();
            return mat;
        }

        return null;
    }

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
                    _simulatorCustomImagePath = settings.SimulatorCustomImagePath ?? "";
                    _currentParameters.CustomImagePath = _simulatorCustomImagePath;
                    return;
                }
            }
        }
        catch { }

        _brightness = 0.0;
        _contrast = 1.0;
        _isGrayscale = false;
        _savedCameraIndex = 0;
        _savedRtspUrl = "";
        _savedIsRtsp = false;
        _desiredWidth = 1920;
        _desiredHeight = 1080;
        _desiredFps = 120;
        _simulatorCustomImagePath = "";
        _currentParameters.CustomImagePath = "";
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
                DesiredFps = _desiredFps,
                SimulatorCustomImagePath = _simulatorCustomImagePath ?? ""
            };
            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(_settingsPath, json);
        }
        catch { }
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
        public string SimulatorCustomImagePath { get; set; } = "";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            if (_cameraLock.Wait(2000))
            {
                try
                {
                    StopCameraInternalAsync().GetAwaiter().GetResult();
                    lock (_lastFrameGate)
                    {
                        _lastFrame?.Dispose();
                        _lastFrame = null;
                    }
                }
                finally
                {
                    try { _cameraLock.Release(); } catch { }
                }
            }
            else
            {
                StopCameraInternalAsync().GetAwaiter().GetResult();
            }
        }
        catch (ObjectDisposedException)
        {
            // Semaphore already disposed
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraService] Dispose error: {ex.Message}");
        }
        finally
        {
            try
            {
                _cameraLock.Dispose();
            }
            catch { }
        }
    }
}
