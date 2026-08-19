using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.Services.Camera;

namespace VisionInspectionApp.UI.ViewModels;

public sealed class JobCameraSettingsViewModel : ObservableObject, IDisposable
{
    private readonly CameraService _cameraService;
    private readonly Action<CameraParameters>? _onSaveCallback;
    private ImageSource? _liveImage;
    private string _statusMessage = "Cấu hình thông số Camera riêng biệt cho Job hiện tại.";
    private int _fps;
    private int _frameCount;
    private DateTime _lastFrameTime = DateTime.Now;

    private CameraDeviceInfo? _selectedDevice;
    private bool _isCameraRunning;
    private CameraParameters _cameraParams;

    public string JobName { get; }
    public Action? RequestClose { get; set; }

    public ObservableCollection<CameraDeviceInfo> AvailableDevices { get; } = new();

    public CameraDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }

    public bool IsCameraRunning
    {
        get => _isCameraRunning;
        private set => SetProperty(ref _isCameraRunning, value);
    }

    public bool IsLiveViewing => _cameraService.ActiveDriver?.IsGrabbing ?? false;

    public string LiveViewButtonIcon => IsLiveViewing ? "⏸" : "👁️";
    public string LiveViewButtonText => IsLiveViewing ? "Dừng Live View" : "Bật Live View";
    public Brush LiveViewButtonBackgroundBrush => IsLiveViewing 
        ? new SolidColorBrush(Color.FromRgb(211, 47, 47))
        : new SolidColorBrush(Color.FromRgb(33, 150, 243));

    public string StreamStatusText => IsCameraRunning
        ? (IsLiveViewing ? "🔴 Live Streaming (Băng thông cao)" : "⏸ Standby (0 Mbps Ethernet)")
        : "Offline";

    public ImageSource? LiveImage
    {
        get => _liveImage;
        private set => SetProperty(ref _liveImage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int Fps
    {
        get => _fps;
        private set => SetProperty(ref _fps, value);
    }

    private string _resolutionText = "Unknown";
    public string ResolutionText
    {
        get => _resolutionText;
        private set => SetProperty(ref _resolutionText, value);
    }

    public ObservableCollection<string> ResolutionOptions { get; } = new()
    {
        "1920 x 1080 (1080p Full HD - Mặc định)",
        "1280 x 720 (720p HD)",
        "2560 x 1440 (2K QHD)",
        "3840 x 2160 (4K UHD)",
        "5472 x 3648 (20MP Full Sensor)",
        "640 x 480 (VGA)"
    };

    public ObservableCollection<int> FpsOptions { get; } = new()
    {
        120,
        60,
        30
    };

    public string SelectedResolution
    {
        get
        {
            int w = _cameraParams.Width;
            int h = _cameraParams.Height;
            return ResolutionOptions.FirstOrDefault(r => r.StartsWith($"{w} x {h}")) ?? ResolutionOptions[0];
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (value.StartsWith("1920")) { _cameraParams.Width = 1920; _cameraParams.Height = 1080; }
            else if (value.StartsWith("1280")) { _cameraParams.Width = 1280; _cameraParams.Height = 720; }
            else if (value.StartsWith("2560")) { _cameraParams.Width = 2560; _cameraParams.Height = 1440; }
            else if (value.StartsWith("3840")) { _cameraParams.Width = 3840; _cameraParams.Height = 2160; }
            else if (value.StartsWith("5472")) { _cameraParams.Width = 5472; _cameraParams.Height = 3648; }
            else if (value.StartsWith("640")) { _cameraParams.Width = 640; _cameraParams.Height = 480; }
            OnPropertyChanged();
            _ = ApplyCameraParametersAsync();
        }
    }

    public int SelectedFps
    {
        get => _cameraParams.TargetFps;
        set
        {
            _cameraParams.TargetFps = value;
            OnPropertyChanged();
            _ = ApplyCameraParametersAsync();
        }
    }

    // Industrial Camera Parameters Binding
    public float ExposureTimeUs
    {
        get => _cameraParams.ExposureTimeUs;
        set
        {
            if (Math.Abs(_cameraParams.ExposureTimeUs - value) > 1.0f)
            {
                _cameraParams.ExposureTimeUs = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public bool AutoExposure
    {
        get => _cameraParams.AutoExposure;
        set
        {
            if (_cameraParams.AutoExposure != value)
            {
                _cameraParams.AutoExposure = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public float GainDb
    {
        get => _cameraParams.GainDb;
        set
        {
            if (Math.Abs(_cameraParams.GainDb - value) > 0.1f)
            {
                _cameraParams.GainDb = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public bool AutoGain
    {
        get => _cameraParams.AutoGain;
        set
        {
            if (_cameraParams.AutoGain != value)
            {
                _cameraParams.AutoGain = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public float Gamma
    {
        get => _cameraParams.Gamma;
        set
        {
            if (Math.Abs(_cameraParams.Gamma - value) > 0.05f)
            {
                _cameraParams.Gamma = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public bool AutoWhiteBalance
    {
        get => _cameraParams.AutoWhiteBalance;
        set
        {
            if (_cameraParams.AutoWhiteBalance != value)
            {
                _cameraParams.AutoWhiteBalance = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public float RedGain
    {
        get => _cameraParams.RedGain;
        set
        {
            if (Math.Abs(_cameraParams.RedGain - value) > 0.05f)
            {
                _cameraParams.RedGain = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public float GreenGain
    {
        get => _cameraParams.GreenGain;
        set
        {
            if (Math.Abs(_cameraParams.GreenGain - value) > 0.05f)
            {
                _cameraParams.GreenGain = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public float BlueGain
    {
        get => _cameraParams.BlueGain;
        set
        {
            if (Math.Abs(_cameraParams.BlueGain - value) > 0.05f)
            {
                _cameraParams.BlueGain = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public bool TriggerModeOn
    {
        get => _cameraParams.TriggerMode == CameraTriggerMode.On;
        set
        {
            _cameraParams.TriggerMode = value ? CameraTriggerMode.On : CameraTriggerMode.Off;
            OnPropertyChanged();
            _ = ApplyCameraParametersAsync();
        }
    }

    public ObservableCollection<CameraTriggerSource> TriggerSources { get; } = new()
    {
        CameraTriggerSource.Software,
        CameraTriggerSource.Line0,
        CameraTriggerSource.Line1,
        CameraTriggerSource.Line2
    };

    public CameraTriggerSource SelectedTriggerSource
    {
        get => _cameraParams.TriggerSource;
        set
        {
            _cameraParams.TriggerSource = value;
            OnPropertyChanged();
            _ = ApplyCameraParametersAsync();
        }
    }

    public float TriggerDelayUs
    {
        get => _cameraParams.TriggerDelayUs;
        set
        {
            _cameraParams.TriggerDelayUs = value;
            OnPropertyChanged();
            _ = ApplyCameraParametersAsync();
        }
    }

    public bool ReverseX
    {
        get => _cameraParams.ReverseX;
        set
        {
            _cameraParams.ReverseX = value;
            OnPropertyChanged();
            _ = ApplyCameraParametersAsync();
        }
    }

    public bool ReverseY
    {
        get => _cameraParams.ReverseY;
        set
        {
            _cameraParams.ReverseY = value;
            OnPropertyChanged();
            _ = ApplyCameraParametersAsync();
        }
    }

    public int PacketSize
    {
        get => _cameraParams.PacketSize;
        set
        {
            _cameraParams.PacketSize = value;
            OnPropertyChanged();
            _ = ApplyCameraParametersAsync();
        }
    }

    public int PacketDelay
    {
        get => _cameraParams.PacketDelay;
        set
        {
            _cameraParams.PacketDelay = value;
            OnPropertyChanged();
            _ = ApplyCameraParametersAsync();
        }
    }

    public double Brightness
    {
        get => _cameraParams.Brightness;
        set
        {
            if (Math.Abs(_cameraParams.Brightness - value) > 0.01)
            {
                _cameraParams.Brightness = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public double Contrast
    {
        get => _cameraParams.Contrast;
        set
        {
            if (Math.Abs(_cameraParams.Contrast - value) > 0.01)
            {
                _cameraParams.Contrast = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public bool IsGrayscale
    {
        get => _cameraParams.IsGrayscale;
        set
        {
            if (_cameraParams.IsGrayscale != value)
            {
                _cameraParams.IsGrayscale = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public CameraParameters CameraParams => _cameraParams;

    public IAsyncRelayCommand StartCameraCommand { get; }
    public IAsyncRelayCommand StopCameraCommand { get; }
    public IAsyncRelayCommand ToggleLiveViewCommand { get; }
    public IAsyncRelayCommand SnapFrameCommand { get; }
    public IAsyncRelayCommand AutoWhiteBalanceOnceCommand { get; }
    public IAsyncRelayCommand ExecuteSoftwareTriggerCommand { get; }
    public IRelayCommand ResetSettingsCommand { get; }
    public IRelayCommand SaveJobCameraSettingsCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public JobCameraSettingsViewModel(CameraService cameraService, CameraParameters initialParams, string jobName, Action<CameraParameters>? onSaveCallback = null)
    {
        _cameraService = cameraService;
        _cameraParams = initialParams != null ? initialParams.Clone() : new CameraParameters();
        JobName = string.IsNullOrWhiteSpace(jobName) ? "Job Hiện Tại" : jobName;
        _onSaveCallback = onSaveCallback;

        _cameraService.FrameCaptured += OnFrameCaptured;
        _cameraService.ErrorOccurred += OnCameraError;

        StartCameraCommand = new AsyncRelayCommand(StartCameraAsync);
        StopCameraCommand = new AsyncRelayCommand(StopCameraAsync);
        ToggleLiveViewCommand = new AsyncRelayCommand(ToggleLiveViewAsync);
        SnapFrameCommand = new AsyncRelayCommand(SnapFrameAsync);
        AutoWhiteBalanceOnceCommand = new AsyncRelayCommand(AutoWhiteBalanceOnceAsync);
        ExecuteSoftwareTriggerCommand = new AsyncRelayCommand(ExecuteSoftwareTriggerAsync);
        ResetSettingsCommand = new RelayCommand(ResetSettings);
        SaveJobCameraSettingsCommand = new RelayCommand(SaveJobCameraSettings);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke());

        RefreshAvailableCameras();
        IsCameraRunning = _cameraService.IsRunning;

        // Tự động áp dụng thông số của Job xuống Camera khi mở cửa sổ
        _ = ApplyCameraParametersAsync();
    }

    public void RefreshAvailableCameras()
    {
        AvailableDevices.Clear();
        var scanned = CameraDriverFactory.ScanAllDevices();
        foreach (var dev in scanned)
        {
            AvailableDevices.Add(dev);
        }

        if (AvailableDevices.Count > 0)
        {
            if (_cameraService.ActiveDeviceInfo != null)
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.Vendor == _cameraService.ActiveDeviceInfo.Vendor && d.Index == _cameraService.ActiveDeviceInfo.Index) ?? AvailableDevices[0];
            }
            else
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.Vendor == CameraVendor.Hikrobot) ?? AvailableDevices[0];
            }
        }
    }

    private async Task StartCameraAsync()
    {
        if (SelectedDevice == null)
        {
            StatusMessage = "Vui lòng chọn camera từ danh sách";
            return;
        }

        try
        {
            StatusMessage = $"Đang kết nối [{SelectedDevice.Vendor}] {SelectedDevice.ModelName}...";
            bool success = await _cameraService.StartDriverCameraAsync(SelectedDevice, _cameraParams);

            if (success)
            {
                IsCameraRunning = true;
                StatusMessage = $"Đã kết nối [{SelectedDevice.Vendor}] {SelectedDevice.ModelName}. " +
                                (_cameraParams.IsLiveViewEnabled ? "Đang phát Live View." : "Trạng thái: Sẵn sàng (0 Mbps Ethernet). Bấm 'Bật Live View' để xem trực tiếp.");
            }
            else
            {
                IsCameraRunning = false;
                StatusMessage = $"Không thể kết nối camera {SelectedDevice.ModelName}.";
            }

            UpdateLiveViewButtonState();
        }
        catch (Exception ex)
        {
            IsCameraRunning = false;
            StatusMessage = $"Lỗi kết nối: {ex.Message}";
            UpdateLiveViewButtonState();
        }
    }

    private async Task StopCameraAsync()
    {
        try
        {
            await _cameraService.StopCameraAsync();
            IsCameraRunning = false;
            StatusMessage = "Camera đã dừng";
            LiveImage = null;
            UpdateLiveViewButtonState();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi: {ex.Message}";
            UpdateLiveViewButtonState();
        }
    }

    private async Task ToggleLiveViewAsync()
    {
        if (!IsCameraRunning)
        {
            StatusMessage = "Vui lòng bấm ▶ Start Camera trước khi bật Live View.";
            return;
        }

        bool targetState = !IsLiveViewing;
        StatusMessage = targetState ? "Đang bật Live View..." : "Đang dừng Live View...";

        bool ok = await _cameraService.SetLiveViewEnabledAsync(targetState);
        _cameraParams.IsLiveViewEnabled = targetState;

        if (ok)
        {
            StatusMessage = targetState 
                ? "🔴 Live View: Đang phát trực tiếp thời gian thực." 
                : "⏸ Live View: Đã tạm dừng. Băng thông Ethernet = 0 Mbps.";
        }
        else
        {
            StatusMessage = "Không thể thay đổi trạng thái Live View.";
        }

        UpdateLiveViewButtonState();
    }

    private async Task SnapFrameAsync()
    {
        try
        {
            StatusMessage = "📸 Đang chụp 1 frame từ camera...";
            var mat = await _cameraService.CaptureSnapshotAsync();
            if (mat != null && !mat.Empty())
            {
                var bitmap = mat.ToBitmapSourceForDisplay(1920, 1080);
                if (bitmap != null)
                {
                    LiveImage = bitmap;
                    ResolutionText = $"{mat.Width} × {mat.Height}";
                    StatusMessage = $"✅ Chụp thành công 1 frame ({mat.Width}x{mat.Height})! Băng thông: 0 Mbps.";
                }
                mat.Dispose();
            }
            else
            {
                StatusMessage = "❌ Không thể lấy ảnh từ camera.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi chụp ảnh: {ex.Message}";
        }
    }

    private async Task AutoWhiteBalanceOnceAsync()
    {
        if (!IsCameraRunning)
        {
            StatusMessage = "Vui lòng kết nối camera trước khi cân bằng trắng.";
            return;
        }

        try
        {
            StatusMessage = "⚡ Đang thực hiện Cân Bằng Trắng 1 lần (Once)...";
            _cameraParams.AutoWhiteBalanceOnce = true;
            await ApplyCameraParametersAsync();
            _cameraParams.AutoWhiteBalanceOnce = false;
            StatusMessage = "✅ Đã áp dụng Cân Bằng Trắng 1 lần thành công!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi Cân Bằng Trắng: {ex.Message}";
        }
    }

    private async Task ExecuteSoftwareTriggerAsync()
    {
        if (_cameraService.IsRunning)
        {
            bool ok = await _cameraService.ExecuteSoftwareTriggerAsync();
            StatusMessage = ok ? "⚡ Software Trigger thành công!" : "Lỗi thực thi Software Trigger.";
        }
    }

    private async Task ApplyCameraParametersAsync()
    {
        try
        {
            await _cameraService.ApplyParametersAsync(_cameraParams);
        }
        catch { }
    }

    private void UpdateLiveViewButtonState()
    {
        OnPropertyChanged(nameof(IsLiveViewing));
        OnPropertyChanged(nameof(LiveViewButtonText));
        OnPropertyChanged(nameof(LiveViewButtonIcon));
        OnPropertyChanged(nameof(LiveViewButtonBackgroundBrush));
        OnPropertyChanged(nameof(StreamStatusText));
    }

    private void SaveJobCameraSettings()
    {
        try
        {
            _onSaveCallback?.Invoke(_cameraParams.Clone());
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi lưu cấu hình: {ex.Message}";
        }
    }

    private bool _isRenderingFrame;

    private void OnFrameCaptured(object? sender, Mat frame)
    {
        try
        {
            if (frame == null || frame.IsDisposed || frame.Empty()) return;

            _frameCount++;
            var now = DateTime.Now;
            var elapsed = (now - _lastFrameTime).TotalSeconds;
            if (elapsed >= 1.0)
            {
                var fpsVal = (int)(_frameCount / elapsed);
                _frameCount = 0;
                _lastFrameTime = now;
                int frameW = frame.Width;
                int frameH = frame.Height;
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    Fps = fpsVal;
                    ResolutionText = $"{frameW} × {frameH}";
                    UpdateLiveViewButtonState();
                });
            }

            if (_isRenderingFrame) return;
            _isRenderingFrame = true;

            var bitmap = frame.ToBitmapSourceForDisplay(1920, 1080);
            if (bitmap == null)
            {
                _isRenderingFrame = false;
                return;
            }

            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                try
                {
                    LiveImage = bitmap;
                    if (_cameraService.IsRunning)
                    {
                        IsCameraRunning = true;
                    }
                }
                finally
                {
                    _isRenderingFrame = false;
                }
            });
        }
        catch
        {
            _isRenderingFrame = false;
        }
    }

    private void OnCameraError(object? sender, string error)
    {
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            StatusMessage = $"Lỗi camera: {error}";
            IsCameraRunning = false;
            Fps = 0;
            UpdateLiveViewButtonState();
        });
    }

    private void ResetSettings()
    {
        _cameraParams.Brightness = 0.0;
        _cameraParams.Contrast = 1.0;
        _cameraParams.IsGrayscale = false;
        _cameraParams.ExposureTimeUs = 10000.0f;
        _cameraParams.GainDb = 0.0f;
        _cameraParams.Gamma = 1.0f;
        _cameraParams.ReverseX = false;
        _cameraParams.ReverseY = false;
        _cameraParams.TriggerMode = CameraTriggerMode.Off;
        _cameraParams.AutoWhiteBalance = true;
        
        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(Contrast));
        OnPropertyChanged(nameof(IsGrayscale));
        OnPropertyChanged(nameof(ExposureTimeUs));
        OnPropertyChanged(nameof(GainDb));
        OnPropertyChanged(nameof(Gamma));
        OnPropertyChanged(nameof(ReverseX));
        OnPropertyChanged(nameof(ReverseY));
        OnPropertyChanged(nameof(TriggerModeOn));
        OnPropertyChanged(nameof(AutoWhiteBalance));

        StatusMessage = "Đã khôi phục cài đặt mặc định.";
        _ = ApplyCameraParametersAsync();
    }

    public void Dispose()
    {
        _cameraService.FrameCaptured -= OnFrameCaptured;
        _cameraService.ErrorOccurred -= OnCameraError;
    }
}
