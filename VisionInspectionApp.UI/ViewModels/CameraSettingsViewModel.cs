using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Windows.Media;
using System.Windows.Threading;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed class CameraSettingsViewModel : ObservableObject, IDisposable
{
    private readonly CameraService _cameraService;
    private ImageSource? _liveImage;
    private string _statusMessage = "Chọn cấu hình và kéo thanh trượt để điều chỉnh hình ảnh.";
    private int _fps;
    private int _frameCount;
    private DateTime _lastFrameTime = DateTime.Now;
    private Mat? _currentFrame;

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

    // Các thuộc tính điều chỉnh độ sáng, tương phản, ảnh xám được liên kết trực tiếp với CameraService
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

    public IRelayCommand ResetSettingsCommand { get; }

    public CameraSettingsViewModel(CameraService cameraService)
    {
        _cameraService = cameraService;
        _cameraService.FrameCaptured += OnFrameCaptured;
        _cameraService.ErrorOccurred += OnCameraError;

        ResetSettingsCommand = new RelayCommand(ResetSettings);
    }

    private void OnFrameCaptured(object? sender, Mat frame)
    {
        try
        {
            if (frame == null || frame.Empty()) return;

            _currentFrame?.Dispose();
            _currentFrame = frame.Clone();

            // Chuyển đổi sang BitmapSource tĩnh và Freeze để thread-safe
            var bitmap = _currentFrame.ToBitmapSource();
            bitmap.Freeze();

            // Gán hiển thị trên UI Thread
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                LiveImage = bitmap;
                if (_cameraService.IsRunning)
                {
                    StatusMessage = "Camera đang chạy - Stream mượt mà";
                }
            });

            // Tính toán FPS thực tế
            _frameCount++;
            var now = DateTime.Now;
            var elapsed = (now - _lastFrameTime).TotalSeconds;
            if (elapsed >= 1.0)
            {
                var fpsVal = (int)(_frameCount / elapsed);
                _frameCount = 0;
                _lastFrameTime = now;
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Fps = fpsVal);
            }
        }
        catch
        {
            // Bỏ qua lỗi render hình ảnh
        }
    }

    private void OnCameraError(object? sender, string error)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            StatusMessage = $"Lỗi camera: {error}";
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
        _currentFrame?.Dispose();
    }
}
