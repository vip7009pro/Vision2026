using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Models.ManualInspection;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.Services.ManualInspection;
using VisionInspectionApp.Application.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class ManualInspectionViewModel : ObservableObject
{
    private readonly GlobalAppSettingsService _settings;
    private readonly CameraService _cameraService;

    private Mat? _imageMat;
    private readonly List<GeoPoint2D> _collectedPoints = new();
    private readonly List<OverlayItem> _persistentOverlays = new();

    public ManualInspectionViewModel(GlobalAppSettingsService settings, CameraService cameraService)
    {
        _settings = settings;
        _cameraService = cameraService;

        OverlayItems = new ObservableCollection<OverlayItem>();
        Records = new ObservableCollection<ManualMeasurementRecord>();

        LoadImageCommand = new RelayCommand(LoadImage);
        CaptureCameraImageCommand = new AsyncRelayCommand(CaptureCameraImageAsync);
        ClearMeasurementsCommand = new RelayCommand(ClearMeasurements);
        DeleteRecordCommand = new RelayCommand<ManualMeasurementRecord>(DeleteRecord);
        ExportCsvCommand = new RelayCommand(ExportCsv);
        SelectToolCommand = new RelayCommand<ManualMeasurementType>(SelectTool);
        SelectGroupCommand = new RelayCommand<ManualMeasurementGroup>(SelectGroup);
        SyncCalibrationFromGlobalCommand = new RelayCommand(SyncCalibrationFromGlobal);

        InteractivePointClickedCommand = new RelayCommand<System.Windows.Point?>(OnInteractivePointClicked);
        InteractiveMouseMoveCommand = new RelayCommand<System.Windows.Point?>(OnInteractiveMouseMove);
        InteractiveCancelledCommand = new RelayCommand(OnInteractiveCancelled);

        SyncCalibrationFromGlobal();
        UpdatePromptText();
    }

    [ObservableProperty]
    private ImageSource? _image;

    [ObservableProperty]
    private double _calibrationPixelsPerMm = 1.0;

    [ObservableProperty]
    private ManualMeasurementType _selectedTool = ManualMeasurementType.PointToPointDistance;

    [ObservableProperty]
    private ManualMeasurementGroup _selectedGroup = ManualMeasurementGroup.PointAndDistance;

    [ObservableProperty]
    private string _statusPrompt = string.Empty;

    [ObservableProperty]
    private bool _enableSubpixelSnapping = true;

    public ObservableCollection<OverlayItem> OverlayItems { get; }

    public ObservableCollection<ManualMeasurementRecord> Records { get; }

    public ICommand LoadImageCommand { get; }
    public ICommand CaptureCameraImageCommand { get; }
    public ICommand ClearMeasurementsCommand { get; }
    public ICommand DeleteRecordCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand SelectToolCommand { get; }
    public ICommand SelectGroupCommand { get; }
    public ICommand SyncCalibrationFromGlobalCommand { get; }

    public ICommand InteractivePointClickedCommand { get; }
    public ICommand InteractiveMouseMoveCommand { get; }
    public ICommand InteractiveCancelledCommand { get; }

    public void SyncCalibrationFromGlobal()
    {
        var globalCal = ChessboardCalibrationService.GetGlobalCalibration();
        if (globalCal is not null && globalCal.IsCalibrated && globalCal.PixelsPerMm > 0)
        {
            CalibrationPixelsPerMm = Math.Round(globalCal.PixelsPerMm, 4);
        }
        else if (_settings.Settings.ManualPixelsPerMm > 0)
        {
            CalibrationPixelsPerMm = Math.Round(_settings.Settings.ManualPixelsPerMm, 4);
        }
    }

    partial void OnCalibrationPixelsPerMmChanged(double value)
    {
        if (value <= 0) return;
        _settings.Settings.ManualPixelsPerMm = value;
        _settings.Save();
        RecalculateAllRecordsMm();
    }

    partial void OnSelectedToolChanged(ManualMeasurementType value)
    {
        SelectedGroup = ManualMeasurementTypeExtensions.GetGroup(value);
        _collectedPoints.Clear();
        RefreshAllOverlays();
        UpdatePromptText();
    }

    partial void OnSelectedGroupChanged(ManualMeasurementGroup value)
    {
        // When group changes, auto-select first tool in group if current tool is not in this group
        if (ManualMeasurementTypeExtensions.GetGroup(SelectedTool) != value)
        {
            var toolsInGroup = ManualMeasurementTypeExtensions.GetToolsInGroup(value);
            if (toolsInGroup.Count > 0)
            {
                SelectedTool = toolsInGroup[0];
            }
        }
    }

    private void SelectTool(ManualMeasurementType tool)
    {
        SelectedTool = tool;
    }

    private void SelectGroup(ManualMeasurementGroup group)
    {
        SelectedGroup = group;
    }

    private void LoadImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        _imageMat?.Dispose();
        _imageMat = Cv2.ImRead(dlg.FileName, ImreadModes.Color);
        Image = _imageMat.ToBitmapSourceForDisplay();

        _collectedPoints.Clear();
        RefreshAllOverlays();
        UpdatePromptText();
    }

    private async Task CaptureCameraImageAsync()
    {
        try
        {
            var mat = await _cameraService.CaptureSnapshotAsync();
            if (mat != null && !mat.Empty())
            {
                _imageMat?.Dispose();
                _imageMat = mat;
                Image = _imageMat.ToBitmapSourceForDisplay();
                _collectedPoints.Clear();
                RefreshAllOverlays();
                UpdatePromptText();
            }
            else
            {
                MessageBox.Show("Không thể chụp ảnh từ camera. Vui lòng kiểm tra lại kết nối camera.", "Lỗi camera", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi chụp ảnh: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ClearMeasurements()
    {
        Records.Clear();
        _collectedPoints.Clear();
        _persistentOverlays.Clear();
        OverlayItems.Clear();
        UpdatePromptText();
    }

    private void DeleteRecord(ManualMeasurementRecord? record)
    {
        if (record is null) return;
        Records.Remove(record);
        RebuildPersistentOverlaysFromRecords();
        RefreshAllOverlays();
    }

    private void ExportCsv()
    {
        if (Records.Count == 0)
        {
            MessageBox.Show("Không có dữ liệu đo để xuất báo cáo!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv",
            FileName = $"ManualMeasurement_Report_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                ManualMeasurementExporter.ExportToCsv(dlg.FileName, Records, CalibrationPixelsPerMm);
                MessageBox.Show($"Xuất báo cáo CSV thành công!\nĐường dẫn: {dlg.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi xuất file: {ex.Message}", "Lỗi xuất báo cáo", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void RecalculateAllRecordsMm()
    {
        double scale = CalibrationPixelsPerMm > 0 ? CalibrationPixelsPerMm : 1.0;
        foreach (var r in Records)
        {
            r.ValueMm = Math.Round(r.ValuePx / scale, 4);
        }
        RebuildPersistentOverlaysFromRecords();
        RefreshAllOverlays();
    }

    private void RebuildPersistentOverlaysFromRecords()
    {
        _persistentOverlays.Clear();
        foreach (var r in Records)
        {
            var overlays = GenerateOverlaysForRecord(r);
            _persistentOverlays.AddRange(overlays);
        }
    }
}
