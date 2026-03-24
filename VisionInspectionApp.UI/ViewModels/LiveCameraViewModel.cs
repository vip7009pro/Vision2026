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
        RunLiveInspectionCommand = new RelayCommand(RunLiveInspection);
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

        public override string ToString() => $"Camera {Index}: {Name}";
    }

    [ObservableProperty]
    private string _productCode = "ProductA";

    [ObservableProperty]
    private string? _selectedConfig;

    partial void OnSelectedConfigChanged(string? value)
    {
        if (value != null)
        {
            LoadConfig();
        }
    }

    [ObservableProperty]
    private CameraInfo? _selectedCamera;

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
            await _cameraService.StartCameraCaptureAsync(SelectedCamera.Index);
            IsCameraRunning = true;
            StatusMessage = "Camera đang chạy";
        }
        catch (Exception ex)
        {
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

    /// <summary>
    /// Xử lý frame từ camera
    /// </summary>
    private void OnFrameCaptured(object? sender, Mat frame)
    {
        _currentFrame?.Dispose();
        _currentFrame = frame.Clone();

        // Convert Mat to ImageSource
        LiveImage = _currentFrame.ToWriteableBitmap();

        // Tính FPS
        _frameCount++;
        var now = DateTime.Now;
        var elapsed = (now - _lastFrameTime).TotalSeconds;
        if (elapsed >= 1.0)
        {
            Fps = (int)(_frameCount / elapsed);
            _frameCount = 0;
            _lastFrameTime = now;
        }

        // Chạy live inspection nếu enabled
        if (IsLiveInspectionEnabled && _config != null)
        {
            RunLiveInspection();
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
        var cameraIndices = CameraService.GetAvailableCameras();

        foreach (var index in cameraIndices)
        {
            AvailableCameras.Add(new CameraInfo
            {
                Index = index,
                Name = $"Webcam {index}"
            });
        }

        if (AvailableCameras.Count > 0)
        {
            SelectedCamera = AvailableCameras[0];
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
    private void RunLiveInspection()
    {
        if (_currentFrame == null || _config == null)
            return;

        try
        {
            var result = _inspectionService.Inspect(_currentFrame, _config);

            // Update kết quả
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
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi inspection: {ex.Message}";
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
