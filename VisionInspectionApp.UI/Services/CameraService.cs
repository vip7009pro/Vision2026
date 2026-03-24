using OpenCvSharp;
using System;
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
    private Task? _captureTask;
    private bool _isRunning;
    private int _currentCameraIndex = 0;

    public event EventHandler<Mat>? FrameCaptured;
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// Khởi động capture camera với chỉ số camera (0 = webcam mặc định)
    /// </summary>
    public async Task StartCameraCaptureAsync(int cameraIndex = 0, int fps = 30)
    {
        if (_isRunning)
            return;

        try
        {
            _currentCameraIndex = cameraIndex;
            _camera = new VideoCapture(cameraIndex);

            if (!_camera.IsOpened())
            {
                ErrorOccurred?.Invoke(this, $"Không thể mở camera {cameraIndex}");
                _camera?.Dispose();
                _camera = null;
                return;
            }

            // Cấu hình camera
            _camera.Set(VideoCaptureProperties.FrameWidth, 1280);
            _camera.Set(VideoCaptureProperties.FrameHeight, 720);
            _camera.Set(VideoCaptureProperties.Fps, fps);
            _camera.Set(VideoCaptureProperties.AutoFocus, 1);

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            // Chạy capture task trong background
            _captureTask = CaptureFramesAsync(_cancellationTokenSource.Token);
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

        if (_captureTask != null)
        {
            try
            {
                await _captureTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
    }

    /// <summary>
    /// Capture frame từ camera
    /// </summary>
    private async Task CaptureFramesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var frameMat = new Mat();
            int frameCount = 0;

            while (!cancellationToken.IsCancellationRequested && _isRunning && _camera != null)
            {
                if (!_camera.Read(frameMat))
                {
                    await Task.Delay(10);
                    continue;
                }

                // Fire event với frame mới
                FrameCaptured?.Invoke(this, frameMat.Clone());
                frameCount++;

                // Yield để UI không bị block
                await Task.Delay(1);
            }

            frameMat.Dispose();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Lỗi capture frame: {ex.Message}");
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
            using var camera = new VideoCapture(i);
            if (camera.IsOpened())
            {
                availableCameras.Add(i);
                camera.Release();
            }
        }

        return availableCameras.ToArray();
    }

    /// <summary>
    /// Chụp ảnh tĩnh từ camera hiện tại
    /// </summary>
    public Mat? CaptureSnapshot()
    {
        if (_camera == null || !_camera.IsOpened())
            return null;

        var frame = new Mat();
        if (_camera.Read(frame))
        {
            return frame;
        }

        frame.Dispose();
        return null;
    }

    /// <summary>
    /// Kiểm tra camera đang chạy hay không
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Camera hiện tại
    /// </summary>
    public int CurrentCameraIndex => _currentCameraIndex;

    public void Dispose()
    {
        StopCameraAsync().Wait();
        _cancellationTokenSource?.Dispose();
        _camera?.Dispose();
        _camera = null;
    }
}
