using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class ChessboardCalibrationViewModel : ObservableObject
{
    private readonly CameraService _cameraService;
    private VisionConfig? _config;
    private Mat? _currentMat;
    private Mat? _undistortedMat;
    private readonly WriteableBitmapRenderer _liveRenderer = new();
    private bool _isRenderingLiveFrame;

    [ObservableProperty]
    private bool _isLiveActive = true;

    public bool IsGlobalMode => _config is null;
    public bool HasActiveJob => _config is not null;
    public string WindowTitle => IsGlobalMode
        ? "♟ Hiệu Chuẩn Camera Chessboard (Toàn Cục - Global Calibration)"
        : "♟ Hiệu Chuẩn Camera Chessboard (Active Job)";

    public string ModeBadgeText => IsGlobalMode
        ? "🌐 CHẾ ĐỘ TOÀN CỤC (GLOBAL CALIBRATION)"
        : "📁 CẤU HÌNH JOB HIỆN TẠI (ACTIVE JOB)";

    public ChessboardCalibrationViewModel(CameraService cameraService)
    {
        _cameraService = cameraService;
        Captures = new ObservableCollection<ChessboardCaptureItem>();
        OverlayItems = new ObservableCollection<OverlayItem>();

        LoadImageCommand = new AsyncRelayCommand(LoadImageAsync);
        CaptureCameraCommand = new AsyncRelayCommand(CaptureCameraAsync);
        AddCaptureCommand = new RelayCommand(AddCapture, () => _currentMat is not null && !IsDetecting);
        RemoveCaptureCommand = new RelayCommand<ChessboardCaptureItem>(RemoveCapture);
        ClearAllCommand = new RelayCommand(ClearAll, () => !IsDetecting);
        CalibrateCommand = new RelayCommand(RunCalibrate, () => Captures.Count(c => c.Found) >= 3 && !IsDetecting);
        UndistortPreviewCommand = new RelayCommand(UndistortPreview, () => IsCalibrated && _currentMat is not null && !IsDetecting);
        SetAsGlobalCalibrationCommand = new RelayCommand(SetAsGlobalCalibration, () => IsCalibrated && !IsDetecting);
        ApplyGlobalToJobCommand = new RelayCommand(ApplyGlobalToJob, () => HasActiveJob && !IsDetecting);
        ToggleLiveStreamCommand = new RelayCommand(ToggleLiveStream);
        SnapFrameCommand = new AsyncRelayCommand(SnapFrameAsync, () => !IsDetecting);
        SnapAndAddCaptureCommand = new AsyncRelayCommand(SnapAndAddCaptureAsync, () => !IsDetecting);
    }

    private bool _isInitializing;

    [ObservableProperty]
    private bool _forceApplyGlobalCalibration;

    [ObservableProperty]
    private bool _isGuideExpanded = true;

    [RelayCommand]
    private void ToggleGuide() => IsGuideExpanded = !IsGuideExpanded;

    partial void OnForceApplyGlobalCalibrationChanged(bool value)
    {
        if (_isInitializing) return;

        ChessboardCalibrationService.SaveForceApplyGlobalCalibration(value);

        if (value)
        {
            var globalCal = ChessboardCalibrationService.GetGlobalCalibration();
            if (globalCal is not null && globalCal.IsCalibrated)
            {
                ApplyCalibrationDataToUi(globalCal);
                if (_config is not null)
                {
                    _config.ChessboardCalibration = globalCal.Clone();
                    _config.PixelsPerMm = globalCal.PixelsPerMm;
                    IsDirty = true;
                }
                StatusMessage = "🔒 Đã BẬT cưỡng chế sử dụng global calibration: Tất cả các job khi chạy sẽ áp dụng global calib, bất kể job có thông số hiệu chuẩn riêng hay không.";
            }
            else
            {
                StatusMessage = "⚠️ Đã bật cưỡng chế sử dụng global calibration, nhưng hệ thống chưa có dữ liệu Global Calib. Vui lòng thực hiện Calibrate và bấm [🌐 Set As Global Calib] trước.";
            }
        }
        else
        {
            StatusMessage = "🔓 Đã TẮT cưỡng chế: Các job khi chạy sẽ sử dụng thông số hiệu chuẩn riêng của từng job.";
        }
    }

    private void ApplyCalibrationDataToUi(ChessboardCalibrationData data)
    {
        BoardCols = data.BoardCols;
        BoardRows = data.BoardRows;
        SquareSizeMm = data.SquareSizeMm;

        IsCalibrated = true;
        PixelsPerMm = data.PixelsPerMm;
        ReprojectionError = data.ReprojectionError;
        FocalX = data.Fx;
        FocalY = data.Fy;
        PrincipalX = data.Cx;
        PrincipalY = data.Cy;
        DistCoeffsText = data.DistCoeffs is not null
            ? string.Join(", ", data.DistCoeffs.Select(d => d.ToString("F6")))
            : string.Empty;
    }

    public void Initialize(VisionConfig? config = null)
    {
        _isInitializing = true;
        try
        {
            _config = config;
            OnPropertyChanged(nameof(IsGlobalMode));
            OnPropertyChanged(nameof(HasActiveJob));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(ModeBadgeText));

            ChessboardCalibrationData? data = null;
            bool isFromJob = false;

            // Nạp trạng thái cưỡng chế từ cấu hình toàn cục
            ForceApplyGlobalCalibration = ChessboardCalibrationService.IsForceApplyGlobalCalibration;

            var globalCal = ChessboardCalibrationService.GetGlobalCalibration();
            bool hasGlobal = globalCal is not null && globalCal.IsCalibrated;

            if (config is null)
            {
                if (hasGlobal)
                {
                    data = globalCal;
                }
            }
            else if (ForceApplyGlobalCalibration && hasGlobal)
            {
                data = globalCal;
                config.ChessboardCalibration = globalCal!.Clone();
                config.PixelsPerMm = globalCal.PixelsPerMm;
            }
            else if (config.ChessboardCalibration is not null && config.ChessboardCalibration.IsCalibrated)
            {
                data = config.ChessboardCalibration;
                isFromJob = true;
            }
            else if (hasGlobal)
            {
                data = globalCal;
                // Tự động gắn cấu hình Global vào Job hiện tại nếu Job chưa có cấu hình riêng
                config.ChessboardCalibration = globalCal!.Clone();
                if (config.PixelsPerMm <= 0 || Math.Abs(config.PixelsPerMm - 1.0) < 1e-6)
                {
                    config.PixelsPerMm = globalCal.PixelsPerMm;
                }
            }

            if (data is not null && data.IsCalibrated)
            {
                ApplyCalibrationDataToUi(data);

                if (config is null)
                {
                    StatusMessage = "🌐 Chế độ Hiệu Chuẩn Toàn Cục (Global Calibration). Đã nạp thông số Global trước đó. Hãy căn chỉnh bàn cờ qua live stream.";
                }
                else if (ForceApplyGlobalCalibration && hasGlobal)
                {
                    StatusMessage = "🔒 Đang CƯỠNG CHẾ áp dụng Global Calibration cho tất cả các Job.";
                }
                else
                {
                    StatusMessage = isFromJob
                        ? "✅ Calibration đã lưu trước đó của Job được nạp lại."
                        : "🌐 Đang áp dụng Global Calibration (do Job hiện tại chưa có cấu hình riêng).";
                }
            }
            else if (config?.ChessboardCalibration is not null)
            {
                BoardCols = config.ChessboardCalibration.BoardCols;
                BoardRows = config.ChessboardCalibration.BoardRows;
                SquareSizeMm = config.ChessboardCalibration.SquareSizeMm;
                StatusMessage = "🔴 Đang bật Livestream camera. Hãy căn chỉnh bàn cờ, chụp ít nhất 3 ảnh rồi bấm Calibrate.";
            }
            else
            {
                StatusMessage = "🔴 Đang bật Livestream camera. Hãy căn chỉnh bàn cờ, chụp ít nhất 3 ảnh rồi bấm Calibrate.";
            }
        }
        finally
        {
            _isInitializing = false;
            RefreshCommands();
        }
    }

    // ======== Settings ========
    [ObservableProperty]
    private int _boardCols = 9;

    [ObservableProperty]
    private int _boardRows = 6;

    [ObservableProperty]
    private double _squareSizeMm = 29.0;

    private string _squareSizeMmText = "29";

    public string SquareSizeMmText
    {
        get => _squareSizeMmText;
        set
        {
            if (SetProperty(ref _squareSizeMmText, value))
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var normalized = value.Replace(',', '.').Trim();
                if (double.TryParse(normalized, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                {
                    if (Math.Abs(SquareSizeMm - parsed) > 1e-9)
                    {
                        SquareSizeMm = parsed;
                    }
                }
            }
        }
    }

    partial void OnSquareSizeMmChanged(double value)
    {
        var normalized = _squareSizeMmText.Replace(',', '.').Trim();
        if (!double.TryParse(normalized, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var currentVal) ||
            Math.Abs(currentVal - value) > 1e-9)
        {
            _squareSizeMmText = value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(SquareSizeMmText));
        }
    }

    [ObservableProperty]
    private PatternSizeConvention _patternConvention = PatternSizeConvention.InnerCorners;

    public int PatternConventionIndex
    {
        get => (int)PatternConvention;
        set
        {
            if ((int)PatternConvention != value)
            {
                PatternConvention = (PatternSizeConvention)value;
                OnPropertyChanged(nameof(PatternConventionIndex));
            }
        }
    }

    [ObservableProperty]
    private bool _autoSwapDimensions = true;

    [ObservableProperty]
    private bool _useEnhancedSectorBased = true;

    [ObservableProperty]
    private bool _isDetecting;

    public bool IsNotDetecting => !IsDetecting;

    partial void OnIsDetectingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotDetecting));
        RefreshCommands();
    }

    // Inner corners dựa trên quy ước được chọn:
    // - InnerCorners (mặc định): cols và rows chính là số góc trong
    // - SquareCount: số góc trong = (cols - 1, rows - 1)
    public int InnerCornersX => PatternConvention == PatternSizeConvention.InnerCorners
        ? Math.Max(2, BoardCols)
        : Math.Max(2, BoardCols - 1);

    public int InnerCornersY => PatternConvention == PatternSizeConvention.InnerCorners
        ? Math.Max(2, BoardRows)
        : Math.Max(2, BoardRows - 1);

    partial void OnBoardColsChanged(int value) => OnPropertyChanged(nameof(InnerCornersX));
    partial void OnBoardRowsChanged(int value) => OnPropertyChanged(nameof(InnerCornersY));
    partial void OnPatternConventionChanged(PatternSizeConvention value)
    {
        OnPropertyChanged(nameof(InnerCornersX));
        OnPropertyChanged(nameof(InnerCornersY));
        OnPropertyChanged(nameof(PatternConventionIndex));
    }

    // ======== Preview ========
    [ObservableProperty]
    private ImageSource? _image;

    public ObservableCollection<OverlayItem> OverlayItems { get; }

    // ======== Captures ========
    public ObservableCollection<ChessboardCaptureItem> Captures { get; }

    // ======== Results ========
    [ObservableProperty]
    private bool _isCalibrated;

    [ObservableProperty]
    private double _reprojectionError;

    [ObservableProperty]
    private double _pixelsPerMm;

    [ObservableProperty]
    private double _focalX;

    [ObservableProperty]
    private double _focalY;

    [ObservableProperty]
    private double _principalX;

    [ObservableProperty]
    private double _principalY;

    [ObservableProperty]
    private string _distCoeffsText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Chụp hoặc nạp ít nhất 3 ảnh chessboard rồi bấm Calibrate.";

    [ObservableProperty]
    private bool _isDirty;

    // Stored calibration result for undistort
    private double[,]? _cameraMatrix;
    private double[]? _distCoeffs;

    // ======== Commands ========
    public ICommand LoadImageCommand { get; }
    public ICommand CaptureCameraCommand { get; }
    public ICommand AddCaptureCommand { get; }
    public ICommand RemoveCaptureCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand CalibrateCommand { get; }
    public ICommand UndistortPreviewCommand { get; }
    public ICommand SetAsGlobalCalibrationCommand { get; }
    public ICommand ApplyGlobalToJobCommand { get; }
    public ICommand ToggleLiveStreamCommand { get; }
    public ICommand SnapFrameCommand { get; }
    public ICommand SnapAndAddCaptureCommand { get; }

    // ======== Live Stream ========
    public async Task StartLiveStreamAsync()
    {
        IsLiveActive = true;
        _cameraService.FrameCaptured += OnCameraFrameCaptured;
        if (!_cameraService.IsRunning)
        {
            await _cameraService.StartSavedCameraAsync();
        }
        await _cameraService.RequestLiveStreamAsync("ChessboardCalib", true);
        StatusMessage = "🔴 Đang bật Livestream camera. Hãy căn chỉnh bàn cờ dưới ống kính.";
    }

    public async Task StopLiveStreamAsync()
    {
        IsLiveActive = false;
        _cameraService.FrameCaptured -= OnCameraFrameCaptured;
        await _cameraService.RequestLiveStreamAsync("ChessboardCalib", false);
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
        _ = _cameraService.RequestLiveStreamAsync("ChessboardCalib", IsLiveActive);
        if (IsLiveActive)
        {
            StatusMessage = "🔴 Đang bật Livestream camera. Hãy căn chỉnh góc và vị trí bàn cờ.";
        }
        else
        {
            StatusMessage = "⏸ Đã tạm dừng Livestream camera.";
        }
    }

    private async Task SnapFrameAsync()
    {
        if (IsDetecting) return;
        try
        {
            IsDetecting = true;
            IsLiveActive = false;
            _ = _cameraService.RequestLiveStreamAsync("ChessboardCalib", false);
            StatusMessage = "⏳ Đang chụp khung hình và phân tích bàn cờ (Sector-Based SB)...";

            var mat = _cameraService.TryGetLatestFrameClone() ?? await _cameraService.CaptureSnapshotAsync();
            if (mat is not null && !mat.Empty())
            {
                _currentMat?.Dispose();
                _currentMat = mat;
                ShowCurrentImage();
                await DetectAndShowCornersCoreAsync(_currentMat);
            }
            else
            {
                StatusMessage = "❌ Không thể chụp ảnh từ camera.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi camera: {ex.Message}";
        }
        finally
        {
            IsDetecting = false;
            RefreshCommands();
        }
    }

    private async Task SnapAndAddCaptureAsync()
    {
        if (IsDetecting) return;
        try
        {
            IsDetecting = true;
            StatusMessage = "⏳ Đang chụp & phân tích bàn cờ...";

            var mat = _cameraService.TryGetLatestFrameClone() ?? await _cameraService.CaptureSnapshotAsync();
            if (mat is null || mat.Empty())
            {
                StatusMessage = "❌ Không thể chụp ảnh từ camera.";
                return;
            }

            var patternSize = new OpenCvSharp.Size(InnerCornersX, InnerCornersY);
            bool swap = AutoSwapDimensions;
            bool enhanced = UseEnhancedSectorBased;

            // Chạy phân tích hoàn toàn trên ThreadPool
            var result = await Task.Run(() =>
            {
                return ChessboardCalibrationService.DetectCornersMultiStrategy(
                    mat,
                    patternSize,
                    autoSwapDimensions: swap,
                    useEnhancedSectorBased: enhanced,
                    tryAlternativeConvention: true);
            });

            if (!result.Found || result.Corners.Length == 0)
            {
                // Nếu không tìm thấy, tạm dừng live hiển thị ảnh lỗi cho user chỉnh
                IsLiveActive = false;
                _ = _cameraService.RequestLiveStreamAsync("ChessboardCalib", false);
                _currentMat?.Dispose();
                _currentMat = mat;
                ShowCurrentImage();
                StatusMessage = $"⚠️ Không tìm thấy chessboard ({InnerCornersX}×{InnerCornersY} góc). Hãy thử đổi góc chụp, xoay bàn cờ hoặc chỉnh ánh sáng.";
                return;
            }

            // Tạo thumbnail
            BitmapSource? thumb = null;
            try
            {
                using var small = new Mat();
                double scale = 80.0 / Math.Max(mat.Width, mat.Height);
                Cv2.Resize(mat, small, new OpenCvSharp.Size(), scale, scale);
                thumb = small.ToBitmapSource();
                thumb.Freeze();
            }
            catch { }

            var item = new ChessboardCaptureItem
            {
                Index = Captures.Count + 1,
                Found = true,
                CornerCount = result.Corners.Length,
                Thumbnail = thumb,
                Corners = result.Corners,
                DetectedPatternSize = result.PatternSize,
                ImageSize = new OpenCvSharp.Size(mat.Width, mat.Height)
            };

            Captures.Add(item);
            _currentMat?.Dispose();
            _currentMat = mat;

            // Hiển thị ảnh vẽ corners
            using var drawn = ChessboardCalibrationService.DrawCorners(_currentMat, result.PatternSize, result.Corners, true);
            Image = drawn.ToBitmapSourceForDisplay();

            StatusMessage = $"✅ Đã chụp & thêm Ảnh #{item.Index} ({result.Corners.Length} corners - {result.StrategyUsed}). Hãy di chuyển bàn cờ sang góc khác và bấm chụp tiếp!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi chụp ảnh: {ex.Message}";
        }
        finally
        {
            IsDetecting = false;
            RefreshCommands();
        }
    }

    // ======== Load / Capture ========
    private async Task LoadImageAsync()
    {
        if (IsDetecting) return;
        var dlg = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            IsDetecting = true;
            IsLiveActive = false;
            _ = _cameraService.RequestLiveStreamAsync("ChessboardCalib", false);
            _currentMat?.Dispose();
            _currentMat = Cv2.ImRead(dlg.FileName, ImreadModes.Color);
            ShowCurrentImage();
            await DetectAndShowCornersCoreAsync(_currentMat);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi tải ảnh: {ex.Message}";
        }
        finally
        {
            IsDetecting = false;
            RefreshCommands();
        }
    }

    private async Task CaptureCameraAsync()
    {
        await SnapFrameAsync();
    }

    private void ShowCurrentImage()
    {
        if (_currentMat is null || _currentMat.IsDisposed || _currentMat.Empty()) return;
        Image = _currentMat.ToBitmapSourceForDisplay();
        OverlayItems.Clear();
    }

    // ======== Detect corners ========
    private (bool Found, Point2f[] Corners, OpenCvSharp.Size PatternSize) _lastDetection = (false, Array.Empty<Point2f>(), new OpenCvSharp.Size());

    private void DetectAndShowCorners()
    {
        _ = DetectAndShowCornersCoreAsync(_currentMat);
    }

    private async Task DetectAndShowCornersCoreAsync(Mat? matToDetect)
    {
        if (matToDetect is null || matToDetect.IsDisposed || matToDetect.Empty())
        {
            _lastDetection = (false, Array.Empty<Point2f>(), new OpenCvSharp.Size());
            StatusMessage = "❌ Không có ảnh.";
            return;
        }

        var patternSize = new OpenCvSharp.Size(InnerCornersX, InnerCornersY);
        bool swap = AutoSwapDimensions;
        bool enhanced = UseEnhancedSectorBased;

        StatusMessage = "⏳ Đang phân tích góc bàn cờ (Sector-Based SB)...";

        var result = await Task.Run(() =>
        {
            return ChessboardCalibrationService.DetectCornersMultiStrategy(
                matToDetect,
                patternSize,
                autoSwapDimensions: swap,
                useEnhancedSectorBased: enhanced,
                tryAlternativeConvention: true);
        });

        _lastDetection = (result.Found, result.Corners, result.PatternSize);
        OverlayItems.Clear();

        if (result.Found && result.Corners.Length > 0)
        {
            using var drawn = ChessboardCalibrationService.DrawCorners(matToDetect, result.PatternSize, result.Corners, true);
            Image = drawn.ToBitmapSourceForDisplay();
            StatusMessage = $"✅ Phát hiện {result.Corners.Length} corners ({result.StrategyUsed}). Bấm [+ Thêm ảnh] để thêm vào danh sách.";
        }
        else
        {
            Image = matToDetect.ToBitmapSourceForDisplay();
            StatusMessage = $"❌ Không tìm thấy chessboard pattern ({InnerCornersX}×{InnerCornersY} góc). Kiểm tra số góc, độ nghiêng hoặc ánh sáng.";
        }
    }

    // ======== Captures management ========
    private void AddCapture()
    {
        if (_currentMat is null || _currentMat.IsDisposed) return;

        var (found, corners, detectedSize) = _lastDetection;

        // Create thumbnail
        BitmapSource? thumb = null;
        try
        {
            using var small = new Mat();
            double scale = 80.0 / Math.Max(_currentMat.Width, _currentMat.Height);
            Cv2.Resize(_currentMat, small, new OpenCvSharp.Size(), scale, scale);
            thumb = small.ToBitmapSource();
            thumb.Freeze();
        }
        catch { }

        var item = new ChessboardCaptureItem
        {
            Index = Captures.Count + 1,
            Found = found,
            CornerCount = corners.Length,
            Thumbnail = thumb,
            Corners = found ? corners : null,
            DetectedPatternSize = found ? detectedSize : new OpenCvSharp.Size(),
            ImageSize = new OpenCvSharp.Size(_currentMat.Width, _currentMat.Height)
        };

        Captures.Add(item);
        StatusMessage = found
            ? $"✅ Ảnh #{item.Index} đã thêm ({corners.Length} corners)."
            : $"⚠ Ảnh #{item.Index} thêm nhưng KHÔNG phát hiện corners.";

        RefreshCommands();
    }

    private void RemoveCapture(ChessboardCaptureItem? item)
    {
        if (item is null) return;
        Captures.Remove(item);
        // Re-index
        for (int i = 0; i < Captures.Count; i++)
            Captures[i].Index = i + 1;
        RefreshCommands();
    }

    private void ClearAll()
    {
        Captures.Clear();
        IsCalibrated = false;
        StatusMessage = "Đã xóa tất cả. Chụp lại ít nhất 3 ảnh.";
        RefreshCommands();
    }

    // ======== Calibrate ========
    private void RunCalibrate()
    {
        var validCaptures = Captures.Where(c => c.Found && c.Corners is not null && c.Corners.Length >= 4).ToList();
        if (validCaptures.Count < 3)
        {
            StatusMessage = $"❌ Cần ít nhất 3 ảnh có corners hợp lệ (hiện có {validCaptures.Count}).";
            return;
        }

        // 1. Phân nhóm theo số lượng góc để kiểm tra tính đồng nhất
        var groupsByCount = validCaptures.GroupBy(c => c.Corners!.Length).OrderByDescending(g => g.Count()).ToList();
        int skippedCount = 0;

        if (groupsByCount.Count > 1)
        {
            var dominantGroup = groupsByCount[0].ToList();
            if (dominantGroup.Count < 3)
            {
                var summary = string.Join(", ", groupsByCount.Select(g => $"{g.Count()} ảnh có {g.Key} góc"));
                StatusMessage = $"❌ Số lượng góc bàn cờ không đồng nhất giữa các ảnh ({summary}). Cần ít nhất 3 ảnh có cùng kích thước góc.";
                return;
            }

            skippedCount = validCaptures.Count - dominantGroup.Count;
            validCaptures = dominantGroup;
        }

        int targetCornerCount = validCaptures[0].Corners!.Length;
        var detectedSample = validCaptures.FirstOrDefault(c => c.DetectedPatternSize.Width * c.DetectedPatternSize.Height == targetCornerCount)?.DetectedPatternSize;

        // 2. Xác định PatternSize mục tiêu và tự động đồng bộ UI nếu cần
        OpenCvSharp.Size targetPatternSize;
        if (InnerCornersX * InnerCornersY == targetCornerCount)
        {
            targetPatternSize = new OpenCvSharp.Size(InnerCornersX, InnerCornersY);
        }
        else if (detectedSample.HasValue && detectedSample.Value.Width > 1 && detectedSample.Value.Height > 1)
        {
            targetPatternSize = detectedSample.Value;
        }
        else
        {
            targetPatternSize = ChessboardCalibrationService.InferPatternSize(targetCornerCount, new OpenCvSharp.Size(InnerCornersX, InnerCornersY));
        }

        // Tự động điều chỉnh UI cho khớp quy ước nếu người dùng nhập số ô vuông (vd 8x6 ô vuông => 7x5 góc trong)
        if (PatternConvention == PatternSizeConvention.InnerCorners && (BoardCols - 1) * (BoardRows - 1) == targetCornerCount)
        {
            PatternConvention = PatternSizeConvention.SquareCount;
        }
        else if (InnerCornersX != targetPatternSize.Width || InnerCornersY != targetPatternSize.Height)
        {
            BoardCols = targetPatternSize.Width;
            BoardRows = targetPatternSize.Height;
            PatternConvention = PatternSizeConvention.InnerCorners;
        }

        var allCorners = validCaptures.Select(c => c.Corners!).ToList();
        var perViewPatternSizes = validCaptures.Select(c => c.DetectedPatternSize).ToList();
        var imgSize = validCaptures.First().ImageSize;

        StatusMessage = skippedCount > 0
            ? $"⏳ Đang calibrate với {validCaptures.Count} ảnh ({targetCornerCount} góc, bỏ qua {skippedCount} ảnh lệch kích thước)..."
            : "⏳ Đang calibrate camera...";

        ChessboardCalibrationResult result;
        try
        {
            result = ChessboardCalibrationService.Calibrate(allCorners, imgSize, targetPatternSize, SquareSizeMm, perViewPatternSizes);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi khi calibrate: {ex.Message}";
            return;
        }

        if (!result.Success || result.CameraMatrix is null)
        {
            StatusMessage = !string.IsNullOrEmpty(result.ErrorMessage)
                ? $"❌ Calibration thất bại: {result.ErrorMessage}"
                : "❌ Calibration thất bại. Kiểm tra lại ảnh và thông số.";
            return;
        }

        _cameraMatrix = result.CameraMatrix;
        _distCoeffs = result.DistCoeffs;

        FocalX = result.CameraMatrix[0, 0];
        FocalY = result.CameraMatrix[1, 1];
        PrincipalX = result.CameraMatrix[0, 2];
        PrincipalY = result.CameraMatrix[1, 2];
        ReprojectionError = Math.Round(result.ReprojectionError, 4);
        PixelsPerMm = Math.Round(result.PixelsPerMm, 4);
        DistCoeffsText = result.DistCoeffs is not null
            ? string.Join(", ", result.DistCoeffs.Select(d => d.ToString("F6")))
            : string.Empty;
        IsCalibrated = true;

        string skipNotice = skippedCount > 0 ? $" (Đã tự động lọc {skippedCount} ảnh lệch góc)" : string.Empty;

        // Save to config or global
        if (_config is not null)
        {
            _config.ChessboardCalibration = new ChessboardCalibrationData
            {
                BoardCols = BoardCols,
                BoardRows = BoardRows,
                SquareSizeMm = SquareSizeMm,
                Fx = FocalX,
                Fy = FocalY,
                Cx = PrincipalX,
                Cy = PrincipalY,
                DistCoeffs = result.DistCoeffs ?? Array.Empty<double>(),
                ReprojectionError = ReprojectionError,
                PixelsPerMm = PixelsPerMm,
                ImageWidth = imgSize.Width,
                ImageHeight = imgSize.Height,
                IsCalibrated = true
            };
            _config.PixelsPerMm = PixelsPerMm;
            IsDirty = true;
            StatusMessage = $"✅ Calibration thành công cho Job!{skipNotice} Reprojection Error: {ReprojectionError:F4} px | Pixels/mm: {PixelsPerMm:F4}";
        }
        else
        {
            var globalCalib = new ChessboardCalibrationData
            {
                BoardCols = BoardCols,
                BoardRows = BoardRows,
                SquareSizeMm = SquareSizeMm,
                Fx = FocalX,
                Fy = FocalY,
                Cx = PrincipalX,
                Cy = PrincipalY,
                DistCoeffs = result.DistCoeffs ?? Array.Empty<double>(),
                ReprojectionError = ReprojectionError,
                PixelsPerMm = PixelsPerMm,
                ImageWidth = imgSize.Width,
                ImageHeight = imgSize.Height,
                IsCalibrated = true
            };
            ChessboardCalibrationService.SaveGlobalCalibration(globalCalib);
            IsDirty = true;
            StatusMessage = $"🌐 Calibration thành công & ĐÃ LƯU TOÀN CỤC!{skipNotice} Reprojection Error: {ReprojectionError:F4} px | Pixels/mm: {PixelsPerMm:F4} (Áp dụng cho mọi Job mới/chưa calib).";
        }
        RefreshCommands();
    }

    // ======== Undistort ========
    private void UndistortPreview()
    {
        if (_currentMat is null || _config?.ChessboardCalibration is null || !IsCalibrated) return;

        _undistortedMat?.Dispose();
        _undistortedMat = ChessboardCalibrationService.Undistort(_currentMat, _config.ChessboardCalibration);
        Image = _undistortedMat.ToBitmapSourceForDisplay();
        StatusMessage = "🔄 Ảnh đã được Undistort (khử biến dạng ống kính).";
    }

    // ======== Global Calibration ========
    private void SetAsGlobalCalibration()
    {
        if (!IsCalibrated)
        {
            StatusMessage = "⚠️ Vui lòng thực hiện Calibrate trước khi lưu Global Calibration.";
            return;
        }

        var calibData = _config?.ChessboardCalibration ?? new ChessboardCalibrationData
        {
            BoardCols = BoardCols,
            BoardRows = BoardRows,
            SquareSizeMm = SquareSizeMm,
            Fx = FocalX,
            Fy = FocalY,
            Cx = PrincipalX,
            Cy = PrincipalY,
            DistCoeffs = !string.IsNullOrWhiteSpace(DistCoeffsText)
                ? DistCoeffsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(s => double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0.0).ToArray()
                : Array.Empty<double>(),
            ReprojectionError = ReprojectionError,
            PixelsPerMm = PixelsPerMm,
            ImageWidth = _currentMat?.Width ?? 0,
            ImageHeight = _currentMat?.Height ?? 0,
            IsCalibrated = true
        };

        var ok = ChessboardCalibrationService.SaveGlobalCalibration(calibData);
        if (ok)
        {
            if (ForceApplyGlobalCalibration)
            {
                if (_config is not null)
                {
                    _config.ChessboardCalibration = calibData.Clone();
                    _config.PixelsPerMm = calibData.PixelsPerMm;
                    IsDirty = true;
                }
                StatusMessage = $"🌐 Đã lưu cấu hình làm Global Calibration thành công! (Pixels/mm: {PixelsPerMm:F4}, Error: {ReprojectionError:F4} px). 🔒 Đang BẬT cưỡng chế: Tất cả các job khi chạy sẽ áp dụng global calib này.";
            }
            else
            {
                StatusMessage = $"🌐 Đã lưu cấu hình làm Global Calibration thành công! (Pixels/mm: {PixelsPerMm:F4}, Error: {ReprojectionError:F4} px). Từ nay các Job mới hoặc chưa có calib sẽ tự động áp dụng.";
            }
        }
        else
        {
            StatusMessage = "❌ Lưu Global Calibration thất bại. Vui lòng kiểm tra quyền truy cập tệp cấu hình.";
        }
    }

    private void ApplyGlobalToJob()
    {
        if (_config is null)
        {
            StatusMessage = "⚠️ Hiện không có Job nào đang mở để áp dụng Global Calib.";
            return;
        }

        var globalCal = ChessboardCalibrationService.GetGlobalCalibration();
        if (globalCal is null || !globalCal.IsCalibrated)
        {
            StatusMessage = "⚠️ Chưa có dữ liệu Global Calibration để áp dụng. Vui lòng thực hiện Calibrate và bấm [🌐 Set As Global Calib] trước.";
            return;
        }

        _config.ChessboardCalibration = globalCal.Clone();
        _config.PixelsPerMm = globalCal.PixelsPerMm;
        IsDirty = true;

        ApplyCalibrationDataToUi(globalCal);

        StatusMessage = $"📥 Đã áp dụng Global Calib vào Job đang mở thành công! (Pixels/mm: {globalCal.PixelsPerMm:F4}, Error: {globalCal.ReprojectionError:F4} px).";
        RefreshCommands();
    }

    // ======== Helpers ========
    private void RefreshCommands()
    {
        (AddCaptureCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ClearAllCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CalibrateCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (UndistortPreviewCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SetAsGlobalCalibrationCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ApplyGlobalToJobCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SnapFrameCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (SnapAndAddCaptureCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }
}

public enum PatternSizeConvention
{
    InnerCorners = 0, // Nhập số góc giao nhau bên trong (Inner Corners) - Khuyên dùng
    SquareCount = 1   // Nhập số ô vuông (Square count) - Hệ thống tự trừ 1
}

public sealed class ChessboardCaptureItem : ObservableObject
{
    private int _index;
    public int Index
    {
        get => _index;
        set => SetProperty(ref _index, value);
    }

    public bool Found { get; init; }
    public int CornerCount { get; init; }
    public BitmapSource? Thumbnail { get; init; }
    public Point2f[]? Corners { get; init; }
    public OpenCvSharp.Size DetectedPatternSize { get; init; }
    public OpenCvSharp.Size ImageSize { get; init; }

    public string StatusText => Found
        ? (DetectedPatternSize.Width > 0 && DetectedPatternSize.Height > 0
            ? $"✅ {CornerCount} corners ({DetectedPatternSize.Width}×{DetectedPatternSize.Height})"
            : $"✅ {CornerCount} corners")
        : "❌ Not found";
    public System.Windows.Media.Brush StatusBrush => Found ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.OrangeRed;
}
