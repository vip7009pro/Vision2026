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

    public ChessboardCalibrationViewModel(CameraService cameraService)
    {
        _cameraService = cameraService;
        Captures = new ObservableCollection<ChessboardCaptureItem>();
        OverlayItems = new ObservableCollection<OverlayItem>();

        LoadImageCommand = new RelayCommand(LoadImage);
        CaptureCameraCommand = new AsyncRelayCommand(CaptureCameraAsync);
        AddCaptureCommand = new RelayCommand(AddCapture, () => _currentMat is not null);
        RemoveCaptureCommand = new RelayCommand<ChessboardCaptureItem>(RemoveCapture);
        ClearAllCommand = new RelayCommand(ClearAll);
        CalibrateCommand = new RelayCommand(RunCalibrate, () => Captures.Count(c => c.Found) >= 3);
        UndistortPreviewCommand = new RelayCommand(UndistortPreview, () => IsCalibrated && _currentMat is not null);
    }

    public void Initialize(VisionConfig config)
    {
        _config = config;
        if (config.ChessboardCalibration is not null)
        {
            var data = config.ChessboardCalibration;
            BoardCols = data.BoardCols;
            BoardRows = data.BoardRows;
            SquareSizeMm = data.SquareSizeMm;

            if (data.IsCalibrated)
            {
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
                StatusMessage = "✅ Calibration đã lưu trước đó được nạp lại.";
            }
        }
    }

    // ======== Settings ========
    [ObservableProperty]
    private int _boardCols = 8;

    [ObservableProperty]
    private int _boardRows = 6;

    [ObservableProperty]
    private double _squareSizeMm = 29.0;

    // Inner corners = (cols-1, rows-1)
    public int InnerCornersX => Math.Max(1, BoardCols - 1);
    public int InnerCornersY => Math.Max(1, BoardRows - 1);

    partial void OnBoardColsChanged(int value) => OnPropertyChanged(nameof(InnerCornersX));
    partial void OnBoardRowsChanged(int value) => OnPropertyChanged(nameof(InnerCornersY));

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

    // ======== Load / Capture ========
    private void LoadImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        _currentMat?.Dispose();
        _currentMat = Cv2.ImRead(dlg.FileName, ImreadModes.Color);
        ShowCurrentImage();
        DetectAndShowCorners();
        RefreshCommands();
    }

    private async Task CaptureCameraAsync()
    {
        try
        {
            var mat = await _cameraService.CaptureSnapshotAsync();
            if (mat is not null && !mat.Empty())
            {
                _currentMat?.Dispose();
                _currentMat = mat;
                ShowCurrentImage();
                DetectAndShowCorners();
                RefreshCommands();
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
    }

    private void ShowCurrentImage()
    {
        if (_currentMat is null || _currentMat.IsDisposed || _currentMat.Empty()) return;
        Image = _currentMat.ToBitmapSourceForDisplay();
        OverlayItems.Clear();
    }

    // ======== Detect corners ========
    private (bool, Point2f[]) _lastDetection = (false, Array.Empty<Point2f>());

    private void DetectAndShowCorners()
    {
        if (_currentMat is null || _currentMat.IsDisposed || _currentMat.Empty())
        {
            _lastDetection = (false, Array.Empty<Point2f>());
            StatusMessage = "❌ Không có ảnh.";
            return;
        }

        var patternSize = new OpenCvSharp.Size(InnerCornersX, InnerCornersY);
        var (found, corners) = ChessboardCalibrationService.DetectCorners(_currentMat, patternSize);
        _lastDetection = (found, corners);

        OverlayItems.Clear();

        if (found && corners.Length > 0)
        {
            // Draw corners overlay
            using var drawn = ChessboardCalibrationService.DrawCorners(_currentMat, patternSize, corners, true);
            Image = drawn.ToBitmapSourceForDisplay();

            StatusMessage = $"✅ Phát hiện {corners.Length} corners. Bấm [+ Thêm ảnh] để thêm vào danh sách.";
        }
        else
        {
            Image = _currentMat.ToBitmapSourceForDisplay();
            StatusMessage = $"❌ Không tìm thấy chessboard pattern ({InnerCornersX}×{InnerCornersY} inner corners). Kiểm tra số ô / góc chụp.";
        }
    }

    // ======== Captures management ========
    private void AddCapture()
    {
        if (_currentMat is null || _currentMat.IsDisposed) return;

        var (found, corners) = _lastDetection;

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
        var validCaptures = Captures.Where(c => c.Found && c.Corners is not null).ToList();
        if (validCaptures.Count < 3)
        {
            StatusMessage = $"❌ Cần ít nhất 3 ảnh có corners (hiện có {validCaptures.Count}).";
            return;
        }

        var allCorners = validCaptures.Select(c => c.Corners!).ToList();
        var imgSize = validCaptures.First().ImageSize;
        var patternSize = new OpenCvSharp.Size(InnerCornersX, InnerCornersY);

        StatusMessage = "⏳ Đang calibrate...";

        var result = ChessboardCalibrationService.Calibrate(allCorners, imgSize, patternSize, SquareSizeMm);

        if (!result.Success || result.CameraMatrix is null)
        {
            StatusMessage = "❌ Calibration thất bại. Kiểm tra ảnh và thông số.";
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

        // Save to config
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
                IsCalibrated = true
            };
            _config.PixelsPerMm = PixelsPerMm;
            IsDirty = true;
        }

        StatusMessage = $"✅ Calibration thành công! Reprojection Error: {ReprojectionError:F4} px | Pixels/mm: {PixelsPerMm:F4}";
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

    // ======== Helpers ========
    private void RefreshCommands()
    {
        (AddCaptureCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CalibrateCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (UndistortPreviewCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
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
    public OpenCvSharp.Size ImageSize { get; init; }

    public string StatusText => Found ? $"✅ {CornerCount} corners" : "❌ Not found";
    public System.Windows.Media.Brush StatusBrush => Found ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.OrangeRed;
}
