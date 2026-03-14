using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;
using System.Windows.Media;

namespace VisionInspectionApp.UI.ViewModels;

public sealed record ManualDistanceMeasurement(
    double X1,
    double Y1,
    double X2,
    double Y2,
    double DistancePx,
    double DistanceMm)
{
    public string P1Text => $"({X1:0},{Y1:0})";

    public string P2Text => $"({X2:0},{Y2:0})";
}

public sealed partial class ManualInspectionViewModel : ObservableObject
{
    private readonly GlobalAppSettingsService _settings;

    private Mat? _imageMat;

    public ManualInspectionViewModel(GlobalAppSettingsService settings)
    {
        _settings = settings;

        OverlayItems = new ObservableCollection<OverlayItem>();
        Measurements = new ObservableCollection<ManualDistanceMeasurement>();

        LoadImageCommand = new RelayCommand(LoadImage);
        ClearMeasurementsCommand = new RelayCommand(ClearMeasurements);
        LineSelectedCommand = new RelayCommand<LineSelection?>(OnLineSelected);

        CalibrationPixelsPerMm = _settings.Settings.ManualPixelsPerMm;
    }

    [ObservableProperty]
    private ImageSource? _image;

    public ObservableCollection<OverlayItem> OverlayItems { get; }

    public ObservableCollection<ManualDistanceMeasurement> Measurements { get; }

    [ObservableProperty]
    private double _calibrationPixelsPerMm = 1.0;

    partial void OnCalibrationPixelsPerMmChanged(double value)
    {
        if (value <= 0)
        {
            return;
        }

        _settings.Settings.ManualPixelsPerMm = value;
        _settings.Save();
        RefreshOverlays();
    }

    public ICommand LoadImageCommand { get; }

    public ICommand ClearMeasurementsCommand { get; }

    public ICommand LineSelectedCommand { get; }

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
        RefreshOverlays();
    }

    private void ClearMeasurements()
    {
        Measurements.Clear();
        RefreshOverlays();
    }

    private void OnLineSelected(LineSelection? sel)
    {
        if (sel is null)
        {
            return;
        }

        var dx = sel.X2 - sel.X1;
        var dy = sel.Y2 - sel.Y1;
        var distPx = Math.Sqrt(dx * dx + dy * dy);
        var distMm = CalibrationPixelsPerMm > 0 ? distPx / CalibrationPixelsPerMm : distPx;

        Measurements.Add(new ManualDistanceMeasurement(sel.X1, sel.Y1, sel.X2, sel.Y2, distPx, distMm));
        RefreshOverlays();
    }

    private void RefreshOverlays()
    {
        OverlayItems.Clear();

        foreach (var m in Measurements)
        {
            OverlayItems.Add(new OverlayPointItem { X = m.X1, Y = m.Y1, Stroke = Brushes.DeepSkyBlue, Label = $"P1 ({m.X1:0},{m.Y1:0})" });
            OverlayItems.Add(new OverlayPointItem { X = m.X2, Y = m.Y2, Stroke = Brushes.DeepSkyBlue, Label = $"P2 ({m.X2:0},{m.Y2:0})" });

            OverlayItems.Add(new OverlayLineItem
            {
                X1 = m.X1,
                Y1 = m.Y1,
                X2 = m.X2,
                Y2 = m.Y2,
                Stroke = Brushes.Yellow,
                Label = $"{m.DistancePx:0.0} px / {m.DistanceMm:0.000} mm"
            });
        }
    }
}
