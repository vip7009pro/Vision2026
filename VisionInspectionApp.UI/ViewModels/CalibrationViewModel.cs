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

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class CalibrationViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly ConfigStoreOptions _storeOptions;

    private Mat? _imageMat;
    private VisionConfig? _config;

    public CalibrationViewModel(IConfigService configService, ConfigStoreOptions storeOptions)
    {
        _configService = configService;
        _storeOptions = storeOptions;

        OverlayItems = new ObservableCollection<OverlayItem>();
        Measurements = new ObservableCollection<CalibrationMeasurement>();
        AvailableConfigs = new ObservableCollection<string>();

        LoadImageCommand = new RelayCommand(LoadImage);
        RefreshConfigsCommand = new RelayCommand(RefreshConfigs);
        LoadConfigCommand = new RelayCommand(LoadConfig);
        SavePixelsPerMmCommand = new RelayCommand(SavePixelsPerMm);
        AddMeasurementCommand = new RelayCommand(AddMeasurement);
        ClearMeasurementsCommand = new RelayCommand(ClearMeasurements);
        LineSelectedCommand = new RelayCommand<LineSelection?>(OnLineSelected);

        RefreshConfigs();
    }

    public ObservableCollection<string> AvailableConfigs { get; }

    [ObservableProperty]
    private string? _selectedConfig;

    partial void OnSelectedConfigChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        ProductCode = value;
    }

    [ObservableProperty]
    private string _productCode = "ProductA";

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

    public ICommand RefreshConfigsCommand { get; }

    public ICommand LoadConfigCommand { get; }

    public ICommand SavePixelsPerMmCommand { get; }

    public ICommand AddMeasurementCommand { get; }

    public ICommand ClearMeasurementsCommand { get; }

    public ICommand LineSelectedCommand { get; }

    private void RefreshConfigs()
    {
        AvailableConfigs.Clear();

        var root = Path.GetFullPath(_storeOptions.ConfigRootDirectory);
        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrWhiteSpace(name))
            {
                AvailableConfigs.Add(name);
            }
        }

        if (!string.IsNullOrWhiteSpace(ProductCode) && AvailableConfigs.Contains(ProductCode))
        {
            SelectedConfig = ProductCode;
        }
        else if (AvailableConfigs.Count > 0)
        {
            SelectedConfig ??= AvailableConfigs[0];
        }
    }

    private void LoadConfig()
    {
        var code = SelectedConfig ?? ProductCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        _config = _configService.LoadConfig(code);
        ProductCode = _config.ProductCode;
        AveragePixelsPerMm = _config.PixelsPerMm;
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
        var code = SelectedConfig ?? ProductCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        _config ??= _configService.LoadConfig(code);
        _config.PixelsPerMm = AveragePixelsPerMm;
        _config.ProductCode = ProductCode;
        _configService.SaveConfig(_config);

        RefreshConfigs();
    }

    public sealed record CalibrationMeasurement(double DistancePx, double RealMm, double PixelsPerMm);
}
