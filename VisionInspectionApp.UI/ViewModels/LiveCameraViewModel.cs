using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class LiveCameraViewModel : ObservableObject
{
    private readonly CameraService _cameraService;
    private readonly IConfigService _configService;
    private readonly IInspectionService _inspectionService;
    private readonly object _frameLock = new();
    private Mat? _currentFrame;
    private VisionConfig? _config;

    public LiveCameraViewModel(
        CameraService cameraService,
        IConfigService configService,
        IInspectionService inspectionService)
    {
        _cameraService = cameraService;
        _configService = configService;
        _inspectionService = inspectionService;

        StartCameraCommand = new AsyncRelayCommand(StartCameraAsync);
        StopCameraCommand = new AsyncRelayCommand(StopCameraAsync);
        LoadConfigCommand = new RelayCommand(LoadConfig);
        RefreshConfigsCommand = new RelayCommand(RefreshConfigs);
        CaptureSnapshotCommand = new RelayCommand(CaptureSnapshot);
        RunLiveInspectionCommand = new RelayCommand(RunLiveInspectionOnCurrentFrame);
        ToggleLiveInspectionCommand = new RelayCommand(ToggleLiveInspection);

        AvailableConfigs = new ObservableCollection<string>();
        AvailableCameras = new ObservableCollection<CameraInfo>();
        OverlayItems = new ObservableCollection<OverlayItem>();
        LiveResults = new ObservableCollection<string>();

        // Subscribe khi camera capture frame
        _cameraService.FrameCaptured += OnFrameCaptured;
        _cameraService.ErrorOccurred += OnCameraError;

        RefreshConfigs();
        RefreshAvailableCameras();
    }

    public sealed class CameraInfo
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsRtsp { get; set; }

        public override string ToString() => IsRtsp ? Name : $"Camera {Index}: {Name}";
    }

    [ObservableProperty]
    private string _productCode = "";

    [ObservableProperty]
    private string? _selectedConfig;

    partial void OnSelectedConfigChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            LoadConfig();
        }
        else
        {
            _config = null;
            StatusMessage = "Chưa chọn cấu hình";
        }
    }

    [ObservableProperty]
    private CameraInfo? _selectedCamera;

    partial void OnSelectedCameraChanged(CameraInfo? value)
    {
        IsRtspSelected = value?.IsRtsp ?? false;
    }

    [ObservableProperty]
    private string _rtspUrl = "rtsp://192.168.1.100:554/stream1";

    [ObservableProperty]
    private bool _isRtspSelected = false;

    [ObservableProperty]
    private ImageSource? _liveImage;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isCameraRunning = false;

    [ObservableProperty]
    private bool _isLiveInspectionEnabled = false;

    [ObservableProperty]
    private int _fps = 0;

    private DateTime _lastFrameTime = DateTime.Now;
    private int _frameCount = 0;

    [ObservableProperty]
    private string? _lastInspectionResult;

    public ObservableCollection<string> AvailableConfigs { get; }

    public ObservableCollection<CameraInfo> AvailableCameras { get; }

    public ObservableCollection<OverlayItem> OverlayItems { get; }

    public ObservableCollection<string> LiveResults { get; }

    public ICommand StartCameraCommand { get; }

    public ICommand StopCameraCommand { get; }

    public ICommand LoadConfigCommand { get; }

    public ICommand RefreshConfigsCommand { get; }

    public ICommand CaptureSnapshotCommand { get; }

    public ICommand RunLiveInspectionCommand { get; }

    public ICommand ToggleLiveInspectionCommand { get; }

    /// <summary>
    /// Khởi động camera
    /// </summary>
    private async Task StartCameraAsync()
    {
        if (SelectedCamera == null)
        {
            StatusMessage = "Vui lòng chọn camera";
            return;
        }

        try
        {
            StatusMessage = "Đang khởi động camera...";
            if (SelectedCamera.IsRtsp)
            {
                if (string.IsNullOrWhiteSpace(RtspUrl))
                {
                    StatusMessage = "Vui lòng nhập địa chỉ RTSP URL";
                    return;
                }
                await _cameraService.StartCameraCaptureAsync(fps: 30, rtspUrl: RtspUrl);
            }
            else
            {
                await _cameraService.StartCameraCaptureAsync(cameraIndex: SelectedCamera.Index);
            }

            if (_cameraService.IsRunning)
            {
                IsCameraRunning = true;
                StatusMessage = SelectedCamera.Index == CameraService.SimulatorCameraIndex
                    ? "Camera Giả Lập đang hoạt động (30 FPS)"
                    : "Camera đang hoạt động";
            }
            else
            {
                IsCameraRunning = false;
                StatusMessage = "Không thể khởi động camera đã chọn. Vui lòng kiểm tra kết nối.";
            }
        }
        catch (Exception ex)
        {
            IsCameraRunning = false;
            StatusMessage = $"Lỗi: {ex.Message}";
        }
    }

    /// <summary>
    /// Dừng camera
    /// </summary>
    private async Task StopCameraAsync()
    {
        try
        {
            IsLiveInspectionEnabled = false;
            await _cameraService.StopCameraAsync();
            IsCameraRunning = false;
            StatusMessage = "Camera đã dừng";
            LiveImage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi: {ex.Message}";
        }
    }

    public bool IsViewActive { get; set; } = false;
    private readonly WriteableBitmapRenderer _liveRenderer = new();
    private bool _isRenderingFrame;

    private void OnFrameCaptured(object? sender, Mat frame)
    {
        try
        {
            if (!IsViewActive || frame == null || frame.IsDisposed || frame.Empty()) return;

            // Tính FPS
            _frameCount++;
            var now = DateTime.Now;
            var elapsed = (now - _lastFrameTime).TotalSeconds;
            if (elapsed >= 1.0)
            {
                var fps = (int)(_frameCount / elapsed);
                _frameCount = 0;
                _lastFrameTime = now;
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    Fps = fps;
                });
            }

            Mat? frameCopyForInspection = null;
            lock (_frameLock)
            {
                if (_currentFrame == null || _currentFrame.IsDisposed || _currentFrame.Width != frame.Width || _currentFrame.Height != frame.Height || _currentFrame.Type() != frame.Type())
                {
                    _currentFrame?.Dispose();
                    _currentFrame = new Mat(frame.Height, frame.Width, frame.Type());
                }
                frame.CopyTo(_currentFrame);

                if (IsLiveInspectionEnabled && _config != null)
                {
                    frameCopyForInspection = _currentFrame.Clone();
                }
            }

            if (!_isRenderingFrame)
            {
                _isRenderingFrame = true;

                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
                {
                    try
                    {
                        if (IsViewActive && frame != null && !frame.IsDisposed && !frame.Empty())
                        {
                            var bitmap = _liveRenderer.UpdateFromMat(frame, 1920, 1080);
                            if (bitmap != null && !ReferenceEquals(LiveImage, bitmap))
                            {
                                LiveImage = bitmap;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LiveCamera] Live render error: {ex.Message}");
                    }
                    finally
                    {
                        _isRenderingFrame = false;
                    }
                });
            }

            if (frameCopyForInspection != null)
            {
                using (frameCopyForInspection)
                {
                    RunLiveInspection(frameCopyForInspection);
                }
            }
        }
        catch (Exception ex)
        {
            _isRenderingFrame = false;
            System.Diagnostics.Debug.WriteLine($"[LiveCameraViewModel] OnFrameCaptured error: {ex.Message}");
        }
    }

    /// <summary>
    /// Xử lý lỗi camera
    /// </summary>
    private void OnCameraError(object? sender, string error)
    {
        StatusMessage = $"Lỗi camera: {error}";
        IsCameraRunning = false;
    }

    /// <summary>
    /// Load các config sẵn có
    /// </summary>
    private void RefreshConfigs()
    {
        AvailableConfigs.Clear();
        try
        {
            var configRoot = "configs";
            if (Directory.Exists(configRoot))
            {
                foreach (var file in Directory.EnumerateFiles(configRoot, "*.json"))
                {
                    var productCode = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrEmpty(productCode))
                    {
                        AvailableConfigs.Add(productCode);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi load config: {ex.Message}";
        }

        SelectedConfig = null;
        ProductCode = "";
        _config = null;
    }

    /// <summary>
    /// Load config được chọn
    /// </summary>
    private void LoadConfig()
    {
        if (SelectedConfig == null)
            return;

        try
        {
            _config = _configService.LoadConfig(SelectedConfig);
            StatusMessage = _config != null ? $"Config loaded: {SelectedConfig}" : "Failed to load config";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi load config: {ex.Message}";
        }
    }

    /// <summary>
    /// Refresh danh sách camera
    /// </summary>
    private void RefreshAvailableCameras()
    {
        AvailableCameras.Clear();

        // Bổ sung tùy chọn Camera Giả Lập (Simulator)
        AvailableCameras.Add(new CameraInfo
        {
            Index = CameraService.SimulatorCameraIndex,
            Name = "📷 Camera Giả Lập (Simulator)",
            IsRtsp = false
        });

        try
        {
            var dsCameras = DirectShowDeviceEnumerator.GetDevices();
            for (int i = 0; i < dsCameras.Count; i++)
            {
                AvailableCameras.Add(new CameraInfo
                {
                    Index = i,
                    Name = dsCameras[i],
                    IsRtsp = false
                });
            }
        }
        catch
        {
            // Bỏ qua lỗi quét bằng COM DirectShow
        }

        // Bổ sung các Camera Port 0-4 nếu chưa có
        for (int i = 0; i < 5; i++)
        {
            if (!AvailableCameras.Any(c => c.Index == i && !c.IsRtsp))
            {
                AvailableCameras.Add(new CameraInfo
                {
                    Index = i,
                    Name = $"Camera Port {i} (Fallback)",
                    IsRtsp = false
                });
            }
        }

        // Thêm tùy chọn Custom RTSP cho camera công nghiệp hoặc IP
        AvailableCameras.Add(new CameraInfo
        {
            Index = -1,
            Name = "Custom RTSP / IP Camera",
            IsRtsp = true
        });

        // Tự động khôi phục cấu hình camera đã lưu lần trước từ CameraService
        if (AvailableCameras.Count > 0)
        {
            if (_cameraService.SavedIsRtsp)
            {
                SelectedCamera = AvailableCameras.FirstOrDefault(c => c.IsRtsp);
                RtspUrl = _cameraService.SavedRtspUrl;
            }
            else
            {
                SelectedCamera = AvailableCameras.FirstOrDefault(c => !c.IsRtsp && c.Index == _cameraService.SavedCameraIndex) ?? AvailableCameras[0];
            }
        }
    }

    /// <summary>
    /// Chụp ảnh từ camera
    /// </summary>
    private void CaptureSnapshot()
    {
        if (!IsCameraRunning || _currentFrame == null)
        {
            StatusMessage = "Camera chưa khởi động";
            return;
        }

        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"snapshot_{timestamp}.png";
            var filePath = Path.Combine("snapshots", fileName);

            Directory.CreateDirectory("snapshots");
            Cv2.ImWrite(filePath, _currentFrame);

            StatusMessage = $"Ảnh lưu: {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi lưu ảnh: {ex.Message}";
        }
    }

    /// <summary>
    /// Chạy inspection trên frame hiện tại
    /// </summary>
    private void RunLiveInspectionOnCurrentFrame()
    {
        Mat? frameCopy = null;
        lock (_frameLock)
        {
            if (_currentFrame != null && !_currentFrame.IsDisposed && !_currentFrame.Empty())
            {
                frameCopy = _currentFrame.Clone();
            }
        }
        if (frameCopy != null)
        {
            using (frameCopy)
            {
                RunLiveInspection(frameCopy);
            }
        }
    }

    private void RunLiveInspection(Mat? frameForInspection)
    {
        if (frameForInspection == null || frameForInspection.IsDisposed || frameForInspection.Empty() || _config == null)
            return;

        try
        {
            var result = _inspectionService.Inspect(frameForInspection, _config);

            // Dispatch việc thay đổi UI và ObservableCollection lên UI Thread
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                OverlayItems.Clear();
                LiveResults.Clear();

                // Thêm kết quả tóm tắt
                LiveResults.Add($"Status: {(result.Pass ? "✓ PASS" : "✗ FAIL")}");
                LiveResults.Add("");

                var totalMeasurements = result.Points.Count + result.Lines.Count + result.Distances.Count + 
                                       result.Angles.Count + result.Conditions.Count;
                
                LiveResults.Add($"Total Measurements: {totalMeasurements}");
                LiveResults.Add($"  - Points: {result.Points.Count}");
                LiveResults.Add($"  - Lines: {result.Lines.Count}");
                LiveResults.Add($"  - Distances: {result.Distances.Count}");
                LiveResults.Add($"  - Angles: {result.Angles.Count}");

                if (result.BlobDetections.Count > 0)
                    LiveResults.Add($"  - Blobs: {result.BlobDetections.Count}");

                if (result.CodeDetections.Count > 0)
                    LiveResults.Add($"  - Codes: {result.CodeDetections.Count}");

                LastInspectionResult = $"Status: {(result.Pass ? "✓ PASS" : "✗ FAIL")}";
            });
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                StatusMessage = $"Lỗi inspection: {ex.Message}";
            });
        }
    }

    /// <summary>
    /// Bật/tắt live inspection
    /// </summary>
    private void ToggleLiveInspection()
    {
        if (_config == null)
        {
            StatusMessage = "Vui lòng load config trước";
            return;
        }

        IsLiveInspectionEnabled = !IsLiveInspectionEnabled;
        StatusMessage = IsLiveInspectionEnabled ? "Live inspection: ON" : "Live inspection: OFF";
    }
}
