using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class CalibrationViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly IJobService _jobService;
    private readonly ConfigStoreOptions _storeOptions;
    private readonly CameraService _cameraService;

    private Mat? _imageMat;
    private VisionConfig? _config;

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
                CurrentTempWorkingDir = tempDir;
                ProductCode = cfg.ProductCode;
                _config = cfg;
                OverlayItems.Clear();
                Measurements.Clear();
                Image = null;
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
            _jobService.SaveJob(_config, CurrentTempWorkingDir, CurrentJobFilePath);
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

        _imageMat?.Dispose();
        _imageMat = Cv2.ImRead(dlg.FileName, ImreadModes.Color);
        Image = _imageMat.ToBitmapSource();
        OverlayItems.Clear();
        CurrentDistancePx = 0.0;
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
                Image = _imageMat.ToBitmapSource();
                OverlayItems.Clear();
                CurrentDistancePx = 0.0;
            }
            else
            {
                System.Windows.MessageBox.Show("Không thể chụp ảnh từ camera. Vui lòng kiểm tra lại kết nối camera trong tab Live Camera.", "Lỗi camera", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Lỗi chụp ảnh: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
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
    }

    private void SavePixelsPerMm()
    {
        var code = ProductCode;
        if (string.IsNullOrWhiteSpace(code) || _config == null || string.IsNullOrEmpty(CurrentTempWorkingDir))
        {
            return;
        }

        _config.PixelsPerMm = AveragePixelsPerMm;
        _config.ProductCode = ProductCode;
        _jobService.SaveJob(_config, CurrentTempWorkingDir, CurrentJobFilePath ?? "");
    }

    public sealed record CalibrationMeasurement(double DistancePx, double RealMm, double PixelsPerMm);
}
