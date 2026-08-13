using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.Services.Camera;

namespace VisionInspectionApp.UI.ViewModels;

public sealed class CameraSettingsViewModel : ObservableObject, IDisposable
{
    private readonly CameraService _cameraService;
    private ImageSource? _liveImage;
    private string _statusMessage = "Chọn camera công nghiệp (Hikrobot, Basler, Cognex...) hoặc USB Webcam để bắt đầu.";
    private int _fps;
    private int _frameCount;
    private DateTime _lastFrameTime = DateTime.Now;

    private CameraDeviceInfo? _selectedDevice;
    private string _rtspUrl = "rtsp://192.168.1.100:554/stream1";
    private bool _isRtspSelected;
    private bool _isCameraRunning;

    // Parameter properties
    private CameraParameters _cameraParams = new();

    public ObservableCollection<CameraDeviceInfo> AvailableDevices { get; } = new();

    public CameraDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                IsRtspSelected = value?.InterfaceType == CameraInterfaceType.RTSP;
                if (value != null && value.InterfaceType == CameraInterfaceType.RTSP && !string.IsNullOrEmpty(value.RtspUrl))
                {
                    RtspUrl = value.RtspUrl;
                }
            }
        }
    }

    public string RtspUrl
    {
        get => _rtspUrl;
        set => SetProperty(ref _rtspUrl, value);
    }

    public bool IsRtspSelected
    {
        get => _isRtspSelected;
        private set => SetProperty(ref _isRtspSelected, value);
    }

    public bool IsCameraRunning
    {
        get => _isCameraRunning;
        private set => SetProperty(ref _isCameraRunning, value);
    }

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
            else if (value.StartsWith("640")) { _cameraParams.Width = 640; _cameraParams.Height = 480; }
            _cameraService.DesiredWidth = _cameraParams.Width;
            _cameraService.DesiredHeight = _cameraParams.Height;
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
            _cameraService.DesiredFps = value;
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
        get => _cameraService.Brightness;
        set
        {
            if (Math.Abs(_cameraService.Brightness - value) > 0.01)
            {
                _cameraService.Brightness = value;
                _cameraParams.Brightness = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public double Contrast
    {
        get => _cameraService.Contrast;
        set
        {
            if (Math.Abs(_cameraService.Contrast - value) > 0.01)
            {
                _cameraService.Contrast = value;
                _cameraParams.Contrast = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public bool IsGrayscale
    {
        get => _cameraService.IsGrayscale;
        set
        {
            if (_cameraService.IsGrayscale != value)
            {
                _cameraService.IsGrayscale = value;
                _cameraParams.IsGrayscale = value;
                OnPropertyChanged();
                _ = ApplyCameraParametersAsync();
            }
        }
    }

    public IAsyncRelayCommand StartCameraCommand { get; }
    public IAsyncRelayCommand StopCameraCommand { get; }
    public IRelayCommand RefreshAvailableCamerasCommand { get; }
    public IAsyncRelayCommand ExecuteSoftwareTriggerCommand { get; }
    public IRelayCommand ResetSettingsCommand { get; }

    public CameraSettingsViewModel(CameraService cameraService)
    {
        _cameraService = cameraService;
        _cameraParams = _cameraService.CurrentParameters.Clone();
        _cameraService.FrameCaptured += OnFrameCaptured;
        _cameraService.ErrorOccurred += OnCameraError;

        StartCameraCommand = new AsyncRelayCommand(StartCameraAsync);
        StopCameraCommand = new AsyncRelayCommand(StopCameraAsync);
        RefreshAvailableCamerasCommand = new RelayCommand(RefreshAvailableCameras);
        ExecuteSoftwareTriggerCommand = new AsyncRelayCommand(ExecuteSoftwareTriggerAsync);
        ResetSettingsCommand = new RelayCommand(ResetSettings);

        RefreshAvailableCameras();
        IsCameraRunning = _cameraService.IsRunning;
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
            if (_cameraService.SavedIsRtsp)
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(c => c.InterfaceType == CameraInterfaceType.RTSP) ?? AvailableDevices[0];
            }
            else
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(c => c.Index == _cameraService.SavedCameraIndex) ?? AvailableDevices[0];
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

            if (SelectedDevice.InterfaceType == CameraInterfaceType.RTSP)
            {
                if (string.IsNullOrWhiteSpace(RtspUrl))
                {
                    StatusMessage = "Vui lòng nhập địa chỉ RTSP URL";
                    return;
                }
                SelectedDevice.RtspUrl = RtspUrl;
            }

            bool success = await _cameraService.StartDriverCameraAsync(SelectedDevice, _cameraParams);

            if (success)
            {
                IsCameraRunning = true;
                StatusMessage = $"Đã kết nối thành công [{SelectedDevice.Vendor}] {SelectedDevice.ModelName}";
            }
            else
            {
                IsCameraRunning = false;
                StatusMessage = $"Không thể kết nối camera {SelectedDevice.ModelName}. Kiểm tra lại SDK/dây cáp.";
            }
        }
        catch (Exception ex)
        {
            IsCameraRunning = false;
            StatusMessage = $"Lỗi kết nối camera: {ex.Message}";
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
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi: {ex.Message}";
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
                });
            }

            if (_isRenderingFrame) return;
            _isRenderingFrame = true;

            var bitmap = frame.ToBitmapSourceSafe();
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
        finally
        {
            frame?.Dispose();
        }
    }

    private void OnCameraError(object? sender, string error)
    {
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            StatusMessage = $"Lỗi camera: {error}";
            IsCameraRunning = false;
            Fps = 0;
        });
    }

    private void ResetSettings()
    {
        Brightness = 0.0;
        Contrast = 1.0;
        IsGrayscale = false;
        ExposureTimeUs = 10000.0f;
        GainDb = 0.0f;
        Gamma = 1.0f;
        ReverseX = false;
        ReverseY = false;
        TriggerModeOn = false;
        StatusMessage = "Đã khôi phục cài đặt mặc định.";
        _ = ApplyCameraParametersAsync();
    }

    public void Dispose()
    {
        _cameraService.FrameCaptured -= OnFrameCaptured;
        _cameraService.ErrorOccurred -= OnCameraError;
    }
}
