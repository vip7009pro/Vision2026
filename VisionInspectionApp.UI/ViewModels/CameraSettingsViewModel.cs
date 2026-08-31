using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
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

                OnPropertyChanged(nameof(IsIndustrialCamera));
                OnPropertyChanged(nameof(IsSimulatorCamera));
                OnPropertyChanged(nameof(IsStandardOrWebcamCamera));
                OnPropertyChanged(nameof(IsWebcamCamera));
                OnPropertyChanged(nameof(IsRtspCamera));
                OnPropertyChanged(nameof(SelectedDeviceTitle));
                OnPropertyChanged(nameof(SelectedDeviceBadge));
            }
        }
    }

    public bool IsIndustrialCamera => SelectedDevice?.Vendor is CameraVendor.Hikrobot or CameraVendor.Basler or CameraVendor.Cognex;
    public bool IsSimulatorCamera => SelectedDevice == null || SelectedDevice.Vendor == CameraVendor.Simulator;
    public bool IsStandardOrWebcamCamera => SelectedDevice?.Vendor is CameraVendor.WebcamDirectShow or CameraVendor.Rtsp;
    public bool IsWebcamCamera => SelectedDevice?.Vendor == CameraVendor.WebcamDirectShow;
    public bool IsRtspCamera => SelectedDevice?.Vendor == CameraVendor.Rtsp;

    public string SelectedDeviceTitle => SelectedDevice switch
    {
        { Vendor: CameraVendor.Hikrobot } => $"⚙️ Thông Số Camera Công Nghiệp [Hikrobot] {SelectedDevice.ModelName}",
        { Vendor: CameraVendor.Basler } => $"⚙️ Thông Số Camera Công Nghiệp [Basler] {SelectedDevice.ModelName}",
        { Vendor: CameraVendor.Cognex } => $"⚙️ Thông Số Camera Công Nghiệp [Cognex] {SelectedDevice.ModelName}",
        { Vendor: CameraVendor.WebcamDirectShow } => $"📹 Cấu Hình USB Webcam ({SelectedDevice.ModelName})",
        { Vendor: CameraVendor.Rtsp } => $"🌐 Cấu Hình Luồng Video RTSP ({SelectedDevice.ModelName})",
        { Vendor: CameraVendor.Simulator } => "🎮 Cấu Hình Camera Giả Lập (Simulator)",
        _ => "⚙️ Thông Số Camera"
    };

    public string SelectedDeviceBadge => SelectedDevice?.Vendor switch
    {
        CameraVendor.Hikrobot => "HIKROBOT GIGE/USB3",
        CameraVendor.Basler => "BASLER PYLON",
        CameraVendor.Cognex => "COGNEX VISION",
        CameraVendor.WebcamDirectShow => "USB WEBCAM",
        CameraVendor.Rtsp => "RTSP STREAM",
        CameraVendor.Simulator => "VIRTUAL SIMULATOR",
        _ => "CAMERA"
    };

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

    public event Action? RequestFitView;

    public void TriggerFitView()
    {
        RequestFitView?.Invoke();
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
            ScheduleApplyParameters();
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
            ScheduleApplyParameters();
        }
    }

    // Industrial Camera Parameters Binding
    public float ExposureTimeUs
    {
        get => _cameraParams.ExposureTimeUs;
        set
        {
            if (Math.Abs(_cameraParams.ExposureTimeUs - value) > 0.001f)
            {
                _cameraParams.ExposureTimeUs = value;
                OnPropertyChanged();
                ScheduleApplyParameters();
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
                ScheduleApplyParameters();
            }
        }
    }

    public float GainDb
    {
        get => _cameraParams.GainDb;
        set
        {
            if (Math.Abs(_cameraParams.GainDb - value) > 0.001f)
            {
                _cameraParams.GainDb = value;
                OnPropertyChanged();
                ScheduleApplyParameters();
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
                ScheduleApplyParameters();
            }
        }
    }

    public float Gamma
    {
        get => _cameraParams.Gamma;
        set
        {
            if (Math.Abs(_cameraParams.Gamma - value) > 0.001f)
            {
                _cameraParams.Gamma = value;
                OnPropertyChanged();
                ScheduleApplyParameters();
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
            ScheduleApplyParameters();
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
            ScheduleApplyParameters();
        }
    }

    public float TriggerDelayUs
    {
        get => _cameraParams.TriggerDelayUs;
        set
        {
            _cameraParams.TriggerDelayUs = value;
            OnPropertyChanged();
            ScheduleApplyParameters();
        }
    }

    public bool ReverseX
    {
        get => _cameraParams.ReverseX;
        set
        {
            _cameraParams.ReverseX = value;
            OnPropertyChanged();
            ScheduleApplyParameters();
        }
    }

    public bool ReverseY
    {
        get => _cameraParams.ReverseY;
        set
        {
            _cameraParams.ReverseY = value;
            OnPropertyChanged();
            ScheduleApplyParameters();
        }
    }

    public int PacketSize
    {
        get => _cameraParams.PacketSize;
        set
        {
            _cameraParams.PacketSize = value;
            OnPropertyChanged();
            ScheduleApplyParameters();
        }
    }

    public int PacketDelay
    {
        get => _cameraParams.PacketDelay;
        set
        {
            _cameraParams.PacketDelay = value;
            OnPropertyChanged();
            ScheduleApplyParameters();
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
                ScheduleApplyParameters();
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
                ScheduleApplyParameters();
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
                ScheduleApplyParameters();
            }
        }
    }

    public string SimulatorCustomImagePath
    {
        get => _cameraService.SimulatorCustomImagePath;
        set
        {
            _cameraService.SimulatorCustomImagePath = value;
            _cameraParams.CustomImagePath = value;
            OnPropertyChanged();
            ScheduleApplyParameters();
        }
    }

    public bool SimulatorEnableRandomTransform
    {
        get => _cameraService.SimulatorEnableRandomTransform;
        set
        {
            _cameraService.SimulatorEnableRandomTransform = value;
            _cameraParams.EnableRandomTransform = value;
            OnPropertyChanged();
            ScheduleApplyParameters();
        }
    }

    public bool IsLiveViewing => _cameraService.ActiveDriver?.IsGrabbing ?? false;

    public bool IsLiveViewEnabled
    {
        get => _cameraParams.IsLiveViewEnabled;
        set
        {
            if (_cameraParams.IsLiveViewEnabled != value)
            {
                _cameraParams.IsLiveViewEnabled = value;
                _cameraService.IsLiveViewEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLiveViewing));
                OnPropertyChanged(nameof(LiveViewButtonText));
                OnPropertyChanged(nameof(LiveViewButtonIcon));
                OnPropertyChanged(nameof(LiveViewButtonBackgroundBrush));
                OnPropertyChanged(nameof(StreamStatusText));
            }
        }
    }

    public string LiveViewButtonIcon => IsLiveViewing ? "⏸" : "👁️";
    public string LiveViewButtonText => IsLiveViewing ? "Dừng Live View" : "Bật Live View";
    public Brush LiveViewButtonBackgroundBrush => IsLiveViewing 
        ? new SolidColorBrush(Color.FromRgb(211, 47, 47))
        : new SolidColorBrush(Color.FromRgb(33, 150, 243));

    public string StreamStatusText => IsCameraRunning
        ? (IsLiveViewing ? "🔴 Live Streaming (Băng thông cao)" : "⏸ Standby (0 Mbps Ethernet)")
        : "Offline";

    public bool AutoWhiteBalance
    {
        get => _cameraParams.AutoWhiteBalance;
        set
        {
            if (_cameraParams.AutoWhiteBalance != value)
            {
                _cameraParams.AutoWhiteBalance = value;
                OnPropertyChanged();
                ScheduleApplyParameters();
            }
        }
    }

    public float RedGain
    {
        get => _cameraParams.RedGain;
        set
        {
            if (Math.Abs(_cameraParams.RedGain - value) > 0.001f)
            {
                _cameraParams.RedGain = value;
                OnPropertyChanged();
                ScheduleApplyParameters();
            }
        }
    }

    public float GreenGain
    {
        get => _cameraParams.GreenGain;
        set
        {
            if (Math.Abs(_cameraParams.GreenGain - value) > 0.001f)
            {
                _cameraParams.GreenGain = value;
                OnPropertyChanged();
                ScheduleApplyParameters();
            }
        }
    }

    public float BlueGain
    {
        get => _cameraParams.BlueGain;
        set
        {
            if (Math.Abs(_cameraParams.BlueGain - value) > 0.001f)
            {
                _cameraParams.BlueGain = value;
                OnPropertyChanged();
                ScheduleApplyParameters();
            }
        }
    }

    // Pixel Format chuẩn MVS
    public ObservableCollection<string> PixelFormatOptions { get; } = new()
    {
        "Mono 8",
        "Mono 10",
        "Mono 12",
        "RGB 8",
        "BGR 8",
        "YUV 422 (YUYV) Packed",
        "YUV 422 Packed",
        "Bayer GB 8",
        "Bayer GB 10",
        "Bayer GB 10 Packed",
        "Bayer GB 12",
        "Bayer GB 12 Packed"
    };

    public string SelectedPixelFormat
    {
        get => string.IsNullOrWhiteSpace(_cameraParams.PixelFormat) ? "Bayer GB 8" : _cameraParams.PixelFormat;
        set
        {
            if (_cameraParams.PixelFormat != value && !string.IsNullOrWhiteSpace(value))
            {
                _cameraParams.PixelFormat = value;
                OnPropertyChanged();
                ScheduleApplyParameters();
            }
        }
    }

    // Hardware Camera ROI (Cắt từ phần cứng cảm biến Camera)
    public bool EnableHardwareRoi
    {
        get => _cameraParams.EnableHardwareRoi;
        set
        {
            if (_cameraParams.EnableHardwareRoi != value)
            {
                _cameraParams.EnableHardwareRoi = value;
                OnPropertyChanged();
                RefreshOverlayItems();
                ScheduleApplyParameters();
            }
        }
    }

    public int RoiOffsetX
    {
        get => _cameraParams.RoiOffsetX;
        set
        {
            if (_cameraParams.RoiOffsetX != value)
            {
                _cameraParams.RoiOffsetX = Math.Max(0, value);
                OnPropertyChanged();
                if (!_isUpdatingFromRoiDrag)
                {
                    RefreshOverlayItems();
                    ScheduleApplyParameters();
                }
            }
        }
    }

    public int RoiOffsetY
    {
        get => _cameraParams.RoiOffsetY;
        set
        {
            if (_cameraParams.RoiOffsetY != value)
            {
                _cameraParams.RoiOffsetY = Math.Max(0, value);
                OnPropertyChanged();
                if (!_isUpdatingFromRoiDrag)
                {
                    RefreshOverlayItems();
                    ScheduleApplyParameters();
                }
            }
        }
    }

    public int RoiWidth
    {
        get => _cameraParams.RoiWidth;
        set
        {
            if (_cameraParams.RoiWidth != value)
            {
                _cameraParams.RoiWidth = Math.Max(32, value);
                OnPropertyChanged();
                if (!_isUpdatingFromRoiDrag)
                {
                    RefreshOverlayItems();
                    ScheduleApplyParameters();
                }
            }
        }
    }

    public int RoiHeight
    {
        get => _cameraParams.RoiHeight;
        set
        {
            if (_cameraParams.RoiHeight != value)
            {
                _cameraParams.RoiHeight = Math.Max(32, value);
                OnPropertyChanged();
                if (!_isUpdatingFromRoiDrag)
                {
                    RefreshOverlayItems();
                    ScheduleApplyParameters();
                }
            }
        }
    }

    public ObservableCollection<OverlayItem> OverlayItems { get; } = new();

    public IAsyncRelayCommand StartCameraCommand { get; }
    public IAsyncRelayCommand StopCameraCommand { get; }
    public IAsyncRelayCommand ToggleLiveViewCommand { get; }
    public IAsyncRelayCommand SnapFrameCommand { get; }
    public IAsyncRelayCommand AutoWhiteBalanceOnceCommand { get; }
    public IAsyncRelayCommand SyncFromCameraCommand { get; }
    public IRelayCommand RefreshAvailableCamerasCommand { get; }
    public IAsyncRelayCommand ExecuteSoftwareTriggerCommand { get; }
    public IRelayCommand SetFullSensorRoiCommand { get; }
    public IRelayCommand CenterRoiCommand { get; }
    public IRelayCommand ResetSettingsCommand { get; }
    public IRelayCommand FitViewCommand { get; }
    public IRelayCommand BrowseSimulatorImageCommand { get; }
    public IRelayCommand ClearSimulatorImageCommand { get; }
    public IRelayCommand<RoiSelection> RoiEditedCommand { get; }

    private readonly System.Windows.Threading.DispatcherTimer _debounceTimer;
    private bool _isUpdatingFromRoiDrag;

    public CameraSettingsViewModel(CameraService cameraService)
    {
        _cameraService = cameraService;
        _cameraParams = _cameraService.SystemParameters.Clone();

        _debounceTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _debounceTimer.Tick += OnDebounceTimerTick;

        _cameraService.FrameCaptured += OnFrameCaptured;
        _cameraService.ErrorOccurred += OnCameraError;

        StartCameraCommand = new AsyncRelayCommand(StartCameraAsync);
        StopCameraCommand = new AsyncRelayCommand(StopCameraAsync);
        ToggleLiveViewCommand = new AsyncRelayCommand(ToggleLiveViewAsync);
        SnapFrameCommand = new AsyncRelayCommand(SnapFrameAsync);
        AutoWhiteBalanceOnceCommand = new AsyncRelayCommand(AutoWhiteBalanceOnceAsync);
        SyncFromCameraCommand = new AsyncRelayCommand(SyncFromCameraAsync);
        RefreshAvailableCamerasCommand = new RelayCommand(RefreshAvailableCameras);
        ExecuteSoftwareTriggerCommand = new AsyncRelayCommand(ExecuteSoftwareTriggerAsync);
        SetFullSensorRoiCommand = new RelayCommand(SetFullSensorRoi);
        CenterRoiCommand = new RelayCommand(CenterRoi);
        ResetSettingsCommand = new RelayCommand(ResetSettings);
        FitViewCommand = new RelayCommand(TriggerFitView);
        BrowseSimulatorImageCommand = new RelayCommand(ExecuteBrowseSimulatorImage);
        ClearSimulatorImageCommand = new RelayCommand(ExecuteClearSimulatorImage);
        RoiEditedCommand = new RelayCommand<RoiSelection>(OnRoiEdited);

        RefreshAvailableCameras();
        IsCameraRunning = _cameraService.IsRunning;
        RefreshOverlayItems();
    }

    private void ScheduleApplyParameters()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        await ApplyCameraParametersAsync();
    }

    private void OnRoiEdited(RoiSelection? sel)
    {
        if (sel?.Roi == null) return;
        _isUpdatingFromRoiDrag = true;
        try
        {
            var roi = sel.Roi;
            int ox = Math.Max(0, (int)Math.Round((double)roi.X));
            int oy = Math.Max(0, (int)Math.Round((double)roi.Y));
            int w = Math.Max(32, (int)Math.Round((double)roi.Width));
            int h = Math.Max(32, (int)Math.Round((double)roi.Height));

            const int maxW = 5472;
            const int maxH = 3648;
            w = Math.Min(w, maxW);
            h = Math.Min(h, maxH);
            ox = Math.Min(ox, maxW - w);
            oy = Math.Min(oy, maxH - h);

            _cameraParams.EnableHardwareRoi = true;
            _cameraParams.RoiOffsetX = (ox / 4) * 4;
            _cameraParams.RoiOffsetY = (oy / 2) * 2;
            _cameraParams.RoiWidth = (w / 4) * 4;
            _cameraParams.RoiHeight = (h / 2) * 2;

            OnPropertyChanged(nameof(EnableHardwareRoi));
            OnPropertyChanged(nameof(RoiOffsetX));
            OnPropertyChanged(nameof(RoiOffsetY));
            OnPropertyChanged(nameof(RoiWidth));
            OnPropertyChanged(nameof(RoiHeight));

            StatusMessage = $"📐 Đã kéo ROI: {RoiWidth}x{RoiHeight} tại ({RoiOffsetX}, {RoiOffsetY})";
            RefreshOverlayItems();
            ScheduleApplyParameters();
        }
        finally
        {
            _isUpdatingFromRoiDrag = false;
        }
    }

    public void RefreshOverlayItems()
    {
        OverlayItems.Clear();
        if (EnableHardwareRoi)
        {
            OverlayItems.Add(new OverlayRectItem
            {
                Label = "CamROI",
                X = RoiOffsetX,
                Y = RoiOffsetY,
                Width = RoiWidth,
                Height = RoiHeight,
                Stroke = Brushes.Gold,
                StrokeThickness = 2.0,
                Fill = new SolidColorBrush(Color.FromArgb(30, 255, 215, 0))
            });
        }
    }

    private void SetFullSensorRoi()
    {
        EnableHardwareRoi = false;
        RoiOffsetX = 0;
        RoiOffsetY = 0;
        RoiWidth = 5472;
        RoiHeight = 3648;
        StatusMessage = "Đã đặt lại về toàn bộ cảm biến (Full Sensor 5472x3648).";
        RefreshOverlayItems();
        ScheduleApplyParameters();
    }

    private void CenterRoi()
    {
        int sensorW = 5472;
        int sensorH = 3648;
        int targetW = RoiWidth > 0 ? RoiWidth : 1920;
        int targetH = RoiHeight > 0 ? RoiHeight : 1080;

        RoiOffsetX = Math.Max(0, ((sensorW - targetW) / 8) * 4);
        RoiOffsetY = Math.Max(0, ((sensorH - targetH) / 4) * 2);
        EnableHardwareRoi = true;
        StatusMessage = $"Đã căn giữa Hardware ROI: {targetW}x{targetH} tại Offset ({RoiOffsetX}, {RoiOffsetY}).";
        RefreshOverlayItems();
        ScheduleApplyParameters();
    }

    public void RefreshAvailableCameras()
    {
        if (AvailableDevices.Count == 0)
        {
            AvailableDevices.Add(new CameraDeviceInfo
            {
                Vendor = CameraVendor.Simulator,
                InterfaceType = CameraInterfaceType.Virtual,
                Index = CameraService.SimulatorCameraIndex,
                ModelName = "🎮 Camera Giả Lập Công Nghiệp (Simulator)"
            });
            SelectedDevice = AvailableDevices[0];
        }

        Task.Run(() =>
        {
            var scanned = CameraDriverFactory.ScanAllDevices();
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                AvailableDevices.Clear();
                foreach (var dev in scanned)
                {
                    AvailableDevices.Add(dev);
                }

                if (AvailableDevices.Count > 0)
                {
                    if (_cameraService.ActiveDeviceInfo != null)
                    {
                        SelectedDevice = AvailableDevices.FirstOrDefault(c => c.Vendor == _cameraService.ActiveDeviceInfo.Vendor && c.Index == _cameraService.ActiveDeviceInfo.Index)
                                      ?? AvailableDevices[0];
                    }
                    else if (_cameraService.SavedIsRtsp)
                    {
                        SelectedDevice = AvailableDevices.FirstOrDefault(c => c.InterfaceType == CameraInterfaceType.RTSP) ?? AvailableDevices[0];
                    }
                    else
                    {
                        SelectedDevice = AvailableDevices.FirstOrDefault(c => c.Index == _cameraService.SavedCameraIndex) ?? AvailableDevices[0];
                    }
                }
            });
        });
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
                StatusMessage = $"Đã kết nối thành công [{SelectedDevice.Vendor}] {SelectedDevice.ModelName}. " + 
                                (_cameraParams.IsLiveViewEnabled ? "Đang phát Live View." : "Trạng thái: Sẵn sàng / Standby (0 Mbps Ethernet). Bấm 'Bật Live View' để xem trực tiếp.");
                _ = SyncFromCameraAsync();
            }
            else
            {
                IsCameraRunning = false;
                StatusMessage = $"Không thể kết nối camera {SelectedDevice.ModelName}. Kiểm tra lại SDK/dây cáp.";
            }

            UpdateLiveViewButtonState();
        }
        catch (Exception ex)
        {
            IsCameraRunning = false;
            StatusMessage = $"Lỗi kết nối camera: {ex.Message}";
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
            _debounceTimer.Stop();
            await ApplyCameraParametersAsync();

            StatusMessage = "📸 Đang chụp 1 frame từ camera...";
            var mat = await _cameraService.CaptureSnapshotAsync();
            if (mat != null && !mat.Empty())
            {
                var bitmap = mat.ToBitmapSourceForDisplay(1920, 1080);
                if (bitmap != null)
                {
                    LiveImage = bitmap;
                    ResolutionText = $"{mat.Width} × {mat.Height}";
                    StatusMessage = $"✅ Chụp thử thành công 1 frame ({mat.Width}x{mat.Height})! Băng thông mạng: 0 Mbps.";
                }
                mat.Dispose();
            }
            else
            {
                StatusMessage = "❌ Không thể lấy ảnh từ camera. Kiểm tra lại thiết bị.";
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
            StatusMessage = "⚡ Đang thực hiện Cân Bằng Trắng tự động 1 lần (Once)...";
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

    private async Task SyncFromCameraAsync()
    {
        if (!IsCameraRunning)
        {
            StatusMessage = "Vui lòng bấm ▶ Start Camera trước khi đọc thông số từ Camera.";
            return;
        }

        try
        {
            StatusMessage = "🔄 Đang đọc trạng thái thực tế từ phần cứng Camera...";
            var p = await _cameraService.ReadParametersFromCameraAsync();
            if (p != null)
            {
                _cameraParams = p.Clone();

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
                OnPropertyChanged(nameof(AutoExposure));
                OnPropertyChanged(nameof(AutoGain));
                OnPropertyChanged(nameof(SelectedTriggerSource));
                OnPropertyChanged(nameof(TriggerDelayUs));
                OnPropertyChanged(nameof(PacketSize));
                OnPropertyChanged(nameof(PacketDelay));
                OnPropertyChanged(nameof(SelectedPixelFormat));
                OnPropertyChanged(nameof(EnableHardwareRoi));
                OnPropertyChanged(nameof(RoiOffsetX));
                OnPropertyChanged(nameof(RoiOffsetY));
                OnPropertyChanged(nameof(RoiWidth));
                OnPropertyChanged(nameof(RoiHeight));

                RefreshOverlayItems();
                StatusMessage = $"✅ Đã đồng bộ từ Camera! (ReverseX: {(ReverseX ? "BẬT" : "TẮT")}, ReverseY: {(ReverseY ? "BẬT" : "TẮT")}, Exp: {ExposureTimeUs:F0}µs, Gain: {GainDb:F1}dB)";
            }
            else
            {
                StatusMessage = "❌ Không thể đọc thông số từ thiết bị Camera hiện tại.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi đọc thông số camera: {ex.Message}";
        }
    }

    private void UpdateLiveViewButtonState()
    {
        OnPropertyChanged(nameof(IsLiveViewing));
        OnPropertyChanged(nameof(LiveViewButtonText));
        OnPropertyChanged(nameof(LiveViewButtonIcon));
        OnPropertyChanged(nameof(LiveViewButtonBackgroundBrush));
        OnPropertyChanged(nameof(StreamStatusText));
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
            await _cameraService.SaveSystemParametersAsync(_cameraParams);
        }
        catch { }
    }

    public bool IsViewActive { get; set; } = false;
    private readonly WriteableBitmapRenderer _liveRenderer = new();
    private bool _isRenderingFrame;

    public void OnViewActivated()
    {
        IsViewActive = true;
        RefreshAvailableCameras();
        IsCameraRunning = _cameraService.IsRunning;
        if (_cameraService.ActiveDeviceInfo != null)
        {
            SelectedDevice = AvailableDevices.FirstOrDefault(d => d.SerialNumber == _cameraService.ActiveDeviceInfo.SerialNumber)
                             ?? AvailableDevices.FirstOrDefault(d => d.Index == _cameraService.ActiveDeviceInfo.Index)
                             ?? _cameraService.ActiveDeviceInfo;
        }
        _ = _cameraService.RequestLiveStreamAsync("CameraSettings", true);
        UpdateLiveViewButtonState();
    }

    public void OnViewDeactivated()
    {
        IsViewActive = false;
        _ = _cameraService.RequestLiveStreamAsync("CameraSettings", false);
    }

    private void OnFrameCaptured(object? sender, Mat frame)
    {
        try
        {
            if (!IsViewActive || frame == null || frame.IsDisposed || frame.Empty()) return;

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

            Mat? renderFrameCopy = null;
            try
            {
                renderFrameCopy = frame.Clone();
            }
            catch
            {
                _isRenderingFrame = false;
                return;
            }

            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                try
                {
                    if (IsViewActive && renderFrameCopy != null && !renderFrameCopy.IsDisposed && !renderFrameCopy.Empty())
                    {
                        var bitmap = _liveRenderer.UpdateFromMat(renderFrameCopy, 1920, 1080);
                        if (bitmap != null && !ReferenceEquals(LiveImage, bitmap))
                        {
                            LiveImage = bitmap;
                        }
                        if (_cameraService.IsRunning)
                        {
                            IsCameraRunning = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CameraSettings] Live render error: {ex.Message}");
                }
                finally
                {
                    renderFrameCopy?.Dispose();
                    _isRenderingFrame = false;
                }
            });
        }
        catch (Exception ex)
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
        Brightness = 0.0;
        Contrast = 1.0;
        IsGrayscale = false;
        ExposureTimeUs = 10000.0f;
        GainDb = 0.0f;
        Gamma = 1.0f;
        ReverseX = false;
        ReverseY = false;
        TriggerModeOn = false;
        AutoWhiteBalance = true;
        SelectedPixelFormat = "Bayer GB 8";
        EnableHardwareRoi = false;
        RoiOffsetX = 0;
        RoiOffsetY = 0;
        RoiWidth = 5472;
        RoiHeight = 3648;
        StatusMessage = "Đã khôi phục cài đặt mặc định.";
        _ = ApplyCameraParametersAsync();
    }

    private void ExecuteBrowseSimulatorImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.tif)|*.png;*.jpg;*.jpeg;*.bmp;*.tif|All Files (*.*)|*.*",
            Title = "Chọn tệp hình ảnh làm nguồn Camera Giả Lập"
        };

        if (dialog.ShowDialog() == true)
        {
            SimulatorCustomImagePath = dialog.FileName;
            StatusMessage = $"✅ Đã chọn tệp ảnh giả lập: '{System.IO.Path.GetFileName(dialog.FileName)}'";
        }
    }

    private void ExecuteClearSimulatorImage()
    {
        SimulatorCustomImagePath = "";
        StatusMessage = "🔄 Đã chuyển về nguồn camera giả lập mặc định.";
    }

    public void Dispose()
    {
        _cameraService.FrameCaptured -= OnFrameCaptured;
        _cameraService.ErrorOccurred -= OnCameraError;
        _liveRenderer.Dispose();
    }
}
