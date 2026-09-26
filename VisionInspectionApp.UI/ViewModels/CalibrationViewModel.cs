using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class CalibrationViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isDirty;

    private readonly IConfigService _configService;
    private readonly IJobService _jobService;
    private readonly ConfigStoreOptions _storeOptions;
    private readonly CameraService _cameraService;

    private Mat? _imageMat;
    private VisionConfig? _config;
    private readonly WriteableBitmapRenderer _liveRenderer = new();
    private bool _isRenderingLiveFrame;

    [ObservableProperty]
    private bool _isLiveActive = true;

    [ObservableProperty]
    private string _statusMessage = "🔴 Đang bật Livestream camera. Hãy đặt thước kẻ hoặc mẫu vật chuẩn dưới ống kính.";

    public bool IsGlobalMode => _config is null;
    public string WindowTitle => IsGlobalMode
        ? "📐 Hiệu Chuẩn Tỉ Lệ Pixels/Mm (Toàn Cục - Global Calibration)"
        : "📐 Hiệu Chuẩn Tỉ Lệ Pixels/Mm (Active Job)";

    public string ModeBadgeText => IsGlobalMode
        ? "🌐 CHẾ ĐỘ TOÀN CỤC (GLOBAL CALIBRATION)"
        : $"📁 CẤU HÌNH JOB: {(string.IsNullOrEmpty(CurrentJobFilePath) ? "Chưa lưu" : Path.GetFileName(CurrentJobFilePath))}";

    public CalibrationViewModel(IConfigService configService, ConfigStoreOptions storeOptions, CameraService cameraService, IJobService jobService)
    {
        _configService = configService;
        _jobService = jobService;
        _storeOptions = storeOptions;
        _cameraService = cameraService;

        OverlayItems = new ObservableCollection<OverlayItem>();
        Measurements = new ObservableCollection<CalibrationMeasurement>();

        LoadImageCommand = new RelayCommand(LoadImage);
        CaptureCameraImageCommand = new AsyncRelayCommand(CaptureCameraImageAsync);
        OpenJobCommand = new RelayCommand(OpenJob);
        SaveJobCommand = new RelayCommand(SaveJob);
        SavePixelsPerMmCommand = new RelayCommand(SavePixelsPerMm);
        AddMeasurementCommand = new RelayCommand(AddMeasurement);
        ClearMeasurementsCommand = new RelayCommand(ClearMeasurements);
        LineSelectedCommand = new RelayCommand<LineSelection?>(OnLineSelected);
        ToggleLiveStreamCommand = new RelayCommand(ToggleLiveStream);
        SnapFrameForMeasurementCommand = new AsyncRelayCommand(SnapFrameForMeasurementAsync);
    }

    [ObservableProperty]
    private string? _currentJobFilePath;

    [ObservableProperty]
    private string? _currentTempWorkingDir;

    [ObservableProperty]
    private string _productCode = "";

    [ObservableProperty]
    private ImageSource? _image;

    public ObservableCollection<OverlayItem> OverlayItems { get; }

    public ObservableCollection<CalibrationMeasurement> Measurements { get; }

    [ObservableProperty]
    private double _currentDistancePx;

    [ObservableProperty]
    private double _realDistanceMm = 10.0;

    [ObservableProperty]
    private double _averagePixelsPerMm;

    public ICommand LoadImageCommand { get; }
    public ICommand CaptureCameraImageCommand { get; }
    public ICommand OpenJobCommand { get; }
    public ICommand SaveJobCommand { get; }
    public ICommand SavePixelsPerMmCommand { get; }
    public ICommand AddMeasurementCommand { get; }
    public ICommand ClearMeasurementsCommand { get; }
    public ICommand LineSelectedCommand { get; }
    public ICommand ToggleLiveStreamCommand { get; }
    public ICommand SnapFrameForMeasurementCommand { get; }

    public void InitializeWithConfig(VisionConfig? config, string? jobFilePath, ImageSource? previewImage)
    {
        _config = config;
        CurrentJobFilePath = jobFilePath;

        OnPropertyChanged(nameof(IsGlobalMode));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(ModeBadgeText));

        if (config is not null)
        {
            ProductCode = config.ProductCode ?? string.Empty;
            AveragePixelsPerMm = config.PixelsPerMm;
            StatusMessage = "🔴 Đang bật Livestream camera. Hãy đặt thước hoặc mẫu vật chuẩn dưới ống kính.";
        }
        else
        {
            ProductCode = "(Toàn Cục - Global)";
            var globalCal = ChessboardCalibrationService.GetGlobalCalibration();
            if (globalCal is not null && globalCal.PixelsPerMm > 0)
            {
                AveragePixelsPerMm = globalCal.PixelsPerMm;
                StatusMessage = "🌐 Chế độ Hiệu Chuẩn Toàn Cục (Global Calibration). Đã nạp tỉ lệ Global hiện có. Hãy căn chỉnh mẫu vật qua live stream.";
            }
            else
            {
                AveragePixelsPerMm = 0.0;
                StatusMessage = "🌐 Chế độ Hiệu Chuẩn Toàn Cục (Global Calibration). Hãy căn chỉnh mẫu vật qua live stream, bấm chụp ảnh và đo khoảng cách 2 điểm.";
            }
        }

        if (previewImage != null)
        {
            Image = previewImage;
            IsLiveActive = false;
        }
    }

    public void CloseJob()
    {
        _config = null;
        CurrentJobFilePath = null;
        CurrentTempWorkingDir = null;
        ProductCode = string.Empty;
        OverlayItems.Clear();
        Measurements.Clear();
        Image = null;
        IsDirty = false;
        OnPropertyChanged(nameof(IsGlobalMode));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(ModeBadgeText));
    }

    partial void OnIsDirtyChanged(bool value)
    {
        if (System.Windows.Application.Current?.MainWindow != null)
        {
            var title = System.Windows.Application.Current.MainWindow.Title;
            if (value && !title.EndsWith("*"))
            {
                System.Windows.Application.Current.MainWindow.Title = title + "*";
            }
            else if (!value && title.EndsWith("*"))
            {
                System.Windows.Application.Current.MainWindow.Title = title.TrimEnd('*');
            }
        }
    }

    // ======== Live Stream ========
    public async Task StartLiveStreamAsync()
    {
        IsLiveActive = true;
        _cameraService.FrameCaptured += OnCameraFrameCaptured;
        if (!_cameraService.IsRunning)
        {
            await _cameraService.StartSavedCameraAsync();
        }
        await _cameraService.RequestLiveStreamAsync("TwoPointCalib", true);
        StatusMessage = "🔴 Đang bật Livestream camera. Hãy căn chỉnh thước hoặc mẫu vật dưới ống kính.";
    }

    public async Task StopLiveStreamAsync()
    {
        IsLiveActive = false;
        _cameraService.FrameCaptured -= OnCameraFrameCaptured;
        await _cameraService.RequestLiveStreamAsync("TwoPointCalib", false);
        _liveRenderer.Dispose();
    }

    private void OnCameraFrameCaptured(object? sender, Mat frame)
    {
        if (!IsLiveActive || frame == null || frame.IsDisposed || frame.Empty()) return;
        if (_isRenderingLiveFrame) return;
        _isRenderingLiveFrame = true;

        Mat? frameCopy = null;
        try
        {
            frameCopy = frame.Clone();
        }
        catch
        {
            _isRenderingLiveFrame = false;
            return;
        }

        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            try
            {
                if (IsLiveActive && frameCopy != null && !frameCopy.IsDisposed && !frameCopy.Empty())
                {
                    var bitmap = _liveRenderer.UpdateFromMat(frameCopy, 1920, 1080);
                    if (bitmap != null && !ReferenceEquals(Image, bitmap))
                    {
                        Image = bitmap;
                    }
                }
            }
            finally
            {
                frameCopy?.Dispose();
                _isRenderingLiveFrame = false;
            }
        }));
    }

    private void ToggleLiveStream()
    {
        IsLiveActive = !IsLiveActive;
        _ = _cameraService.RequestLiveStreamAsync("TwoPointCalib", IsLiveActive);
        if (IsLiveActive)
        {
            StatusMessage = "🔴 Đang bật Livestream camera. Hãy căn chỉnh mẫu vật / thước kẻ.";
        }
        else
        {
            StatusMessage = "⏸ Đã tạm dừng Livestream camera.";
        }
    }

    private async Task SnapFrameForMeasurementAsync()
    {
        try
        {
            IsLiveActive = false;
            _ = _cameraService.RequestLiveStreamAsync("TwoPointCalib", false);
            var mat = await _cameraService.CaptureSnapshotAsync();
            if (mat is not null && !mat.Empty())
            {
                _imageMat?.Dispose();
                _imageMat = mat;
                Image = _imageMat.ToBitmapSourceForDisplay();
                OverlayItems.Clear();
                CurrentDistancePx = 0.0;
                StatusMessage = "📸 Đã chụp khung hình! Hãy dùng chuột kéo vẽ đường thẳng nối 2 điểm trên ảnh để đo khoảng cách.";
            }
            else
            {
                StatusMessage = "❌ Không thể chụp ảnh từ camera.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi chụp ảnh: {ex.Message}";
        }
    }

    private void OpenJob()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Job Files (*.job)|*.job|All Files (*.*)|*.*",
            Title = "Open Vision Job"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var cfg = _jobService.LoadJob(dialog.FileName, out var tempDir);
                CurrentJobFilePath = dialog.FileName;
                System.Windows.Application.Current.MainWindow.Title = "CMS VINA VISION SYSTEM - " + Path.GetFileName(CurrentJobFilePath);
                CurrentTempWorkingDir = tempDir;
                ProductCode = cfg.ProductCode;
                _config = cfg;
                if (cfg.PixelsPerMm > 0 && Math.Abs(cfg.PixelsPerMm - 1.0) > 1e-6)
                {
                    AveragePixelsPerMm = cfg.PixelsPerMm;
                }
                else
                {
                    AveragePixelsPerMm = 0.0;
                }
                OverlayItems.Clear();
                Measurements.Clear();
                Image = null;
                OnPropertyChanged(nameof(IsGlobalMode));
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(ModeBadgeText));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open job: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    private void SaveJob()
    {
        if (string.IsNullOrEmpty(CurrentJobFilePath) || string.IsNullOrEmpty(CurrentTempWorkingDir) || _config == null)
            return;

        try
        {
            if (AveragePixelsPerMm > 0)
            {
                _config.PixelsPerMm = AveragePixelsPerMm;
            }
            _config.ProductCode = ProductCode;
            _jobService.SaveJob(_config, CurrentTempWorkingDir, CurrentJobFilePath);
            IsDirty = false;
            System.Windows.MessageBox.Show("Job saved successfully.", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to save job: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void LoadImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*"
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        IsLiveActive = false;
        _ = _cameraService.RequestLiveStreamAsync("TwoPointCalib", false);
        _imageMat?.Dispose();
        _imageMat = Cv2.ImRead(dlg.FileName, ImreadModes.Color);
        Image = _imageMat.ToBitmapSourceForDisplay();
        OverlayItems.Clear();
        CurrentDistancePx = 0.0;
        StatusMessage = "📂 Đã nạp ảnh từ tệp! Hãy dùng chuột kéo vẽ đường thẳng nối 2 điểm để đo khoảng cách.";
    }

    private async Task CaptureCameraImageAsync()
    {
        await SnapFrameForMeasurementAsync();
    }

    private void OnLineSelected(LineSelection? sel)
    {
        if (sel is null)
        {
            return;
        }

        var dx = sel.X2 - sel.X1;
        var dy = sel.Y2 - sel.Y1;
        CurrentDistancePx = Math.Sqrt(dx * dx + dy * dy);

        OverlayItems.Clear();
        OverlayItems.Add(new OverlayPointItem { X = sel.X1, Y = sel.Y1, Stroke = Brushes.Lime, Label = "A" });
        OverlayItems.Add(new OverlayPointItem { X = sel.X2, Y = sel.Y2, Stroke = Brushes.Lime, Label = "B" });
        OverlayItems.Add(new OverlayLineItem
        {
            X1 = sel.X1,
            Y1 = sel.Y1,
            X2 = sel.X2,
            Y2 = sel.Y2,
            Stroke = Brushes.Lime,
            Label = $"{CurrentDistancePx:0.0} px"
        });

        StatusMessage = $"Đoạn thẳng: {CurrentDistancePx:F1} px. Nhập khoảng cách thực (mm) và bấm [Add Measurement].";
    }

    private void AddMeasurement()
    {
        if (CurrentDistancePx <= 0.000001)
        {
            return;
        }

        if (RealDistanceMm <= 0.000001)
        {
            return;
        }

        var ppm = CurrentDistancePx / RealDistanceMm;
        var m = new CalibrationMeasurement(CurrentDistancePx, RealDistanceMm, ppm);
        Measurements.Add(m);

        RecomputeAverage();
    }

    private void ClearMeasurements()
    {
        Measurements.Clear();
        RecomputeAverage();
    }

    private void RecomputeAverage()
    {
        if (Measurements.Count == 0)
        {
            AveragePixelsPerMm = 0.0;
            return;
        }

        AveragePixelsPerMm = Measurements.Average(x => x.PixelsPerMm);
        if (_config != null && AveragePixelsPerMm > 0)
        {
            _config.PixelsPerMm = AveragePixelsPerMm;
            _config.PixelsPerMmX = AveragePixelsPerMm;
            _config.PixelsPerMmY = AveragePixelsPerMm;
            IsDirty = true;
        }
        else if (_config == null && AveragePixelsPerMm > 0)
        {
            IsDirty = true;
        }
    }

    public void SavePixelsPerMm()
    {
        if (AveragePixelsPerMm <= 0)
        {
            StatusMessage = "⚠️ Vui lòng thực hiện ít nhất 1 phép đo để xác định tỉ lệ Pixels/mm.";
            if (System.Windows.Application.Current != null)
            {
                System.Windows.MessageBox.Show("Vui lòng thực hiện ít nhất 1 phép đo để xác định tỉ lệ Pixels/mm.", "Chưa có dữ liệu đo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            return;
        }

        if (_config is null)
        {
            // Lưu vào cấu hình Global Calibration
            var globalCal = ChessboardCalibrationService.GetGlobalCalibration() ?? new ChessboardCalibrationData();
            globalCal.PixelsPerMm = AveragePixelsPerMm;
            globalCal.IsCalibrated = true;
            bool ok = ChessboardCalibrationService.SaveGlobalCalibration(globalCal);
            if (ok)
            {
                IsDirty = true;
                StatusMessage = $"🌐 Đã lưu tỉ lệ Pixels/mm TOÀN CỤC thành công: {AveragePixelsPerMm:0.####} px/mm";
                if (System.Windows.Application.Current != null)
                {
                    System.Windows.MessageBox.Show($"🌐 Đã lưu tỉ lệ Pixels/mm TOÀN CỤC thành công: {AveragePixelsPerMm:0.####} px/mm\n(Tất cả Job chưa có cấu hình riêng sẽ tự động áp dụng hệ số này).", "Global Calibration Saved", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            else
            {
                StatusMessage = "❌ Lưu Global Calibration thất bại. Vui lòng kiểm tra quyền ghi tệp.";
                if (System.Windows.Application.Current != null)
                {
                    System.Windows.MessageBox.Show("❌ Lưu Global Calibration thất bại. Vui lòng kiểm tra quyền ghi tệp.", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            return;
        }

        // Đang mở Job: Lưu vào Job hiện tại
        _config.PixelsPerMm = AveragePixelsPerMm;
        _config.PixelsPerMmX = AveragePixelsPerMm;
        _config.PixelsPerMmY = AveragePixelsPerMm;
        _config.ProductCode = ProductCode ?? string.Empty;
        if (!string.IsNullOrEmpty(CurrentTempWorkingDir) && !string.IsNullOrEmpty(CurrentJobFilePath))
        {
            _jobService.SaveJob(_config, CurrentTempWorkingDir, CurrentJobFilePath);
        }
        IsDirty = false;
        StatusMessage = $"💾 Đã lưu hiệu chuẩn Job: {_config.PixelsPerMm:0.####} px/mm";
        if (System.Windows.Application.Current != null)
        {
            System.Windows.MessageBox.Show($"Đã lưu hiệu chuẩn Job: {_config.PixelsPerMm:0.####} px/mm", "Calibration Saved", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }

    public sealed record CalibrationMeasurement(double DistancePx, double RealMm, double PixelsPerMm);
}
