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

namespace VisionInspectionApp.UI.ViewModels;

public sealed class CameraInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsRtsp { get; set; }

    public override string ToString() => IsRtsp ? Name : $"Camera {Index}: {Name}";
}

public sealed class CameraSettingsViewModel : ObservableObject, IDisposable
{
    private readonly CameraService _cameraService;
    private ImageSource? _liveImage;
    private string _statusMessage = "Chọn camera và điều chỉnh thông số hình ảnh.";
    private int _fps;
    private int _frameCount;
    private DateTime _lastFrameTime = DateTime.Now;

    private CameraInfo? _selectedCamera;
    private string _rtspUrl = "rtsp://192.168.1.100:554/stream1";
    private bool _isRtspSelected;
    private bool _isCameraRunning;

    public ObservableCollection<CameraInfo> AvailableCameras { get; } = new();

    public CameraInfo? SelectedCamera
    {
        get => _selectedCamera;
        set
        {
            if (SetProperty(ref _selectedCamera, value))
            {
                IsRtspSelected = value?.IsRtsp ?? false;
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

    public double Brightness
    {
        get => _cameraService.Brightness;
        set
        {
            if (Math.Abs(_cameraService.Brightness - value) > 0.01)
            {
                _cameraService.Brightness = value;
                OnPropertyChanged();
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
                OnPropertyChanged();
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
                OnPropertyChanged();
            }
        }
    }

    public IAsyncRelayCommand StartCameraCommand { get; }
    public IAsyncRelayCommand StopCameraCommand { get; }
    public IRelayCommand RefreshAvailableCamerasCommand { get; }
    public IRelayCommand ResetSettingsCommand { get; }

    public CameraSettingsViewModel(CameraService cameraService)
    {
        _cameraService = cameraService;
        _cameraService.FrameCaptured += OnFrameCaptured;
        _cameraService.ErrorOccurred += OnCameraError;

        StartCameraCommand = new AsyncRelayCommand(StartCameraAsync);
        StopCameraCommand = new AsyncRelayCommand(StopCameraAsync);
        RefreshAvailableCamerasCommand = new RelayCommand(RefreshAvailableCameras);
        ResetSettingsCommand = new RelayCommand(ResetSettings);

        RefreshAvailableCameras();
        IsCameraRunning = _cameraService.IsRunning;
    }

    public void RefreshAvailableCameras()
    {
        AvailableCameras.Clear();

        // Simulator Camera
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
            // Ignore DirectShow COM errors
        }

        // Fallback ports 0-4
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

        // RTSP / IP Camera
        AvailableCameras.Add(new CameraInfo
        {
            Index = -1,
            Name = "Custom RTSP / IP Camera",
            IsRtsp = true
        });

        // Restore saved selection
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

    private bool _isRenderingFrame;

    private void OnFrameCaptured(object? sender, Mat frame)
    {
        try
        {
            if (frame == null || frame.IsDisposed || frame.Empty()) return;

            // Compute FPS
            _frameCount++;
            var now = DateTime.Now;
            var elapsed = (now - _lastFrameTime).TotalSeconds;
            if (elapsed >= 1.0)
            {
                var fpsVal = (int)(_frameCount / elapsed);
                _frameCount = 0;
                _lastFrameTime = now;
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => Fps = fpsVal);
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
                        StatusMessage = "Camera đang chạy - Stream mượt mà";
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
        StatusMessage = "Đã khôi phục cài đặt hình ảnh mặc định.";
    }

    public void Dispose()
    {
        _cameraService.FrameCaptured -= OnFrameCaptured;
        _cameraService.ErrorOccurred -= OnCameraError;
    }
}
