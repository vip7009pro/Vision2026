using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class InspectionViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly IInspectionService _inspectionService;
    private readonly ConfigStoreOptions _storeOptions;

    private bool _isRunning;

    private Mat? _imageMat;

    private const int MaxBlobOverlayCount = 300;

    public InspectionViewModel(IConfigService configService, IInspectionService inspectionService, ConfigStoreOptions storeOptions)
    {
        _configService = configService;
        _inspectionService = inspectionService;
        _storeOptions = storeOptions;

        LoadImageCommand = new RelayCommand(LoadImage);
        RunInspectionCommand = new AsyncRelayCommand(RunInspectionAsync);
        LoadConfigCommand = new RelayCommand(LoadConfig);
        RefreshConfigsCommand = new RelayCommand(RefreshConfigs);

        OverlayItems = new ObservableCollection<OverlayItem>();
        AvailableConfigs = new ObservableCollection<string>();
        SpecResults = new ObservableCollection<SpecResultRow>();

        RefreshConfigs();
    }

    public sealed record SpecResultRow(
        string Tool,
        string Name,
        string RefA,
        string RefB,
        double Value,
        double Nominal,
        double TolPlus,
        double TolMinus,
        bool Pass);

    [ObservableProperty]
    private string _productCode = "ProductA";

    public ObservableCollection<string> AvailableConfigs { get; }

    [ObservableProperty]
    private string? _selectedConfig;

    [ObservableProperty]
    private System.Windows.Media.ImageSource? _image;

    [ObservableProperty]
    private System.Windows.Media.ImageSource? _debugTemplate;
    [ObservableProperty]
    private System.Windows.Media.ImageSource? _debugCurrent;
    [ObservableProperty]
    private System.Windows.Media.ImageSource? _debugBinary;
    [ObservableProperty]
    private System.Windows.Media.ImageSource? _debugDiff;

    [ObservableProperty]
    private InspectionResult? _lastResult;

    partial void OnLastResultChanged(InspectionResult? value)
    {
        RefreshSpecResults();
        UpdateDebugImages();
    }

    [ObservableProperty]
    private bool _showRois = true;

    [ObservableProperty]
    private bool _showOverlay = true;

    partial void OnShowRoisChanged(bool value)
    {
        RefreshOverlayItems();
    }

    partial void OnShowOverlayChanged(bool value)
    {
        RefreshOverlayItems();
    }

    public ObservableCollection<OverlayItem> OverlayItems { get; }

    public ObservableCollection<SpecResultRow> SpecResults { get; }

    public ICommand LoadImageCommand { get; }

    public ICommand RefreshConfigsCommand { get; }

    public ICommand LoadConfigCommand { get; }

    public ICommand RunInspectionCommand { get; }

    private VisionConfig? _config;

    private static Point2d Rotate(Point2d p, Point2d origin, double angleDeg)
    {
        if (Math.Abs(angleDeg) < 0.000001)
        {
            return p;
        }

        var a = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(a);
        var sin = Math.Sin(a);

        var dx = p.X - origin.X;
        var dy = p.Y - origin.Y;
        var x = dx * cos - dy * sin;
        var y = dx * sin + dy * cos;
        return new Point2d(x + origin.X, y + origin.Y);
    }

    private static Point2d TransformPose(Point2d p, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        var pr = Rotate(p, originTeach, angleDeg);
        var dx = originFound.X - originTeach.X;
        var dy = originFound.Y - originTeach.Y;
        return new Point2d(pr.X + dx, pr.Y + dy);
    }

    private static Point2d InverseTransformPose(Point2d pFound, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        var dx = originFound.X - originTeach.X;
        var dy = originFound.Y - originTeach.Y;
        var p = new Point2d(pFound.X - dx, pFound.Y - dy);
        return Rotate(p, originTeach, -angleDeg);
    }

    private void AddRotatedRoiOverlay(Roi roi, string label, Brush stroke, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return;
        }

        var p1 = TransformPose(new Point2d(roi.X, roi.Y), originTeach, originFound, angleDeg);
        var p2 = TransformPose(new Point2d(roi.X + roi.Width, roi.Y), originTeach, originFound, angleDeg);
        var p3 = TransformPose(new Point2d(roi.X + roi.Width, roi.Y + roi.Height), originTeach, originFound, angleDeg);
        var p4 = TransformPose(new Point2d(roi.X, roi.Y + roi.Height), originTeach, originFound, angleDeg);

        OverlayItems.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = stroke, Label = label });
        OverlayItems.Add(new OverlayLineItem { X1 = p2.X, Y1 = p2.Y, X2 = p3.X, Y2 = p3.Y, Stroke = stroke });
        OverlayItems.Add(new OverlayLineItem { X1 = p3.X, Y1 = p3.Y, X2 = p4.X, Y2 = p4.Y, Stroke = stroke });
        OverlayItems.Add(new OverlayLineItem { X1 = p4.X, Y1 = p4.Y, X2 = p1.X, Y2 = p1.Y, Stroke = stroke });
    }

    private void AddRotatedCrosshair(Point2d center, double halfW, double halfH, string? label, Brush stroke, double angleDeg)
    {
        halfW = Math.Max(1.0, halfW);
        halfH = Math.Max(1.0, halfH);

        var a = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(a);
        var sin = Math.Sin(a);

        var hx = new Point2d(halfW * cos, halfW * sin);
        var hy = new Point2d(-halfH * sin, halfH * cos);

        var p1 = new Point2d(center.X - hx.X, center.Y - hx.Y);
        var p2 = new Point2d(center.X + hx.X, center.Y + hx.Y);
        var p3 = new Point2d(center.X - hy.X, center.Y - hy.Y);
        var p4 = new Point2d(center.X + hy.X, center.Y + hy.Y);

        OverlayItems.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = stroke, Label = label });
        OverlayItems.Add(new OverlayLineItem { X1 = p3.X, Y1 = p3.Y, X2 = p4.X, Y2 = p4.Y, Stroke = stroke });
    }

    private void AddRotatedTemplateAtPoint(Point2d center, int width, int height, string label, Brush stroke, double angleDeg)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var hw = width / 2.0;
        var hh = height / 2.0;

        var p1 = new Point2d(center.X - hw, center.Y - hh);
        var p2 = new Point2d(center.X + hw, center.Y - hh);
        var p3 = new Point2d(center.X + hw, center.Y + hh);
        var p4 = new Point2d(center.X - hw, center.Y + hh);

        p1 = Rotate(p1, center, angleDeg);
        p2 = Rotate(p2, center, angleDeg);
        p3 = Rotate(p3, center, angleDeg);
        p4 = Rotate(p4, center, angleDeg);

        OverlayItems.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = stroke, Label = label });
        OverlayItems.Add(new OverlayLineItem { X1 = p2.X, Y1 = p2.Y, X2 = p3.X, Y2 = p3.Y, Stroke = stroke });
        OverlayItems.Add(new OverlayLineItem { X1 = p3.X, Y1 = p3.Y, X2 = p4.X, Y2 = p4.Y, Stroke = stroke });
        OverlayItems.Add(new OverlayLineItem { X1 = p4.X, Y1 = p4.Y, X2 = p1.X, Y2 = p1.Y, Stroke = stroke });
    }

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

        RefreshOverlayItems();
    }

    private void LoadConfig()
    {
        var code = SelectedConfig ?? ProductCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        ProductCode = code;
        _config = _configService.LoadConfig(code);

        RefreshOverlayItems();
    }

    private async Task RunInspectionAsync()
    {
        if (_isRunning)
        {
            return;
        }

        if (_imageMat is null)
        {
            return;
        }

        var code = SelectedConfig ?? ProductCode;
        if (_config is null || !string.Equals(_config.ProductCode, code, StringComparison.OrdinalIgnoreCase))
        {
            _config = _configService.LoadConfig(code);
        }

        _isRunning = true;
        try
        {
            var img = _imageMat;
            var cfg = _config;
            var result = await Task.Run(() => _inspectionService.Inspect(img, cfg)).ConfigureAwait(true);
            LastResult = result;
            RefreshOverlayItems();
        }
        finally
        {
            _isRunning = false;
        }
    }

    private void RefreshSpecResults()
    {
        SpecResults.Clear();

        if (LastResult is null)
        {
            return;
        }

        foreach (var d in LastResult.Distances)
        {
            SpecResults.Add(new SpecResultRow("Distance", d.Name, d.PointA, d.PointB, d.Value, d.Nominal, d.TolPlus, d.TolMinus, d.Pass));
        }

        foreach (var d in LastResult.LineToLineDistances)
        {
            SpecResults.Add(new SpecResultRow("LLD", d.Name, d.RefA, d.RefB, d.Value, d.Nominal, d.TolPlus, d.TolMinus, d.Pass));
        }

        foreach (var d in LastResult.PointToLineDistances)
        {
            SpecResults.Add(new SpecResultRow("PLD", d.Name, d.RefA, d.RefB, d.Value, d.Nominal, d.TolPlus, d.TolMinus, d.Pass));
        }

        foreach (var a in LastResult.Angles)
        {
            SpecResults.Add(new SpecResultRow("Angle", a.Name, a.LineA, a.LineB, a.ValueDeg, a.Nominal, a.TolPlus, a.TolMinus, a.Pass));
        }

        foreach (var d in LastResult.LinePairDetections)
        {
            SpecResults.Add(new SpecResultRow("LPD", d.Name, "L1", "L2", d.Value, d.Nominal, d.TolPlus, d.TolMinus, d.Pass));
        }

        foreach (var ep in LastResult.EdgePairs)
        {
            SpecResults.Add(new SpecResultRow("EdgePair", ep.Name, ep.RefA, ep.RefB, ep.Value, ep.Nominal, ep.TolPlus, ep.TolMinus, ep.Pass));
        }

        foreach (var epd in LastResult.EdgePairDetections)
        {
            SpecResults.Add(new SpecResultRow("EdgePairDetect", epd.Name, "E1", "E2", epd.Value, epd.Nominal, epd.TolPlus, epd.TolMinus, epd.Pass));
        }

        foreach (var d in LastResult.Diameters)
        {
            SpecResults.Add(new SpecResultRow("Diameter", d.Name, d.CircleRef, string.Empty, d.Value, d.Nominal, d.TolPlus, d.TolMinus, d.Pass));
        }
    }

    private void UpdateDebugImages()
    {
        if (LastResult?.SurfaceCompares is null || LastResult.SurfaceCompares.Count == 0)
        {
            DebugTemplate = null;
            DebugCurrent = null;
            DebugBinary = null;
            DebugDiff = null;
            return;
        }

        // For now, take the first SurfaceCompare result to show debug previews.
        var sc = LastResult.SurfaceCompares[0];
        DebugTemplate = ByteArrayToImageSource(sc.TemplateImage);
        DebugCurrent = ByteArrayToImageSource(sc.CurrentImage);
        DebugBinary = ByteArrayToImageSource(sc.BinaryImage);
        DebugDiff = ByteArrayToImageSource(sc.DiffImage);
    }

    private System.Windows.Media.ImageSource? ByteArrayToImageSource(byte[]? data)
    {
        if (data is null || data.Length == 0) return null;
        try
        {
            using var ms = new System.IO.MemoryStream(data);
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.StreamSource = ms;
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void RefreshOverlayItems()
    {
        OverlayItems.Clear();

        if (!ShowOverlay)
        {
            return;
        }

        var showRois = ShowRois;

        var hasPose = _config is not null && LastResult?.Origin is not null;
        var originTeach = _config is null
            ? new Point2d(0, 0)
            : new Point2d(_config.Origin.WorldPosition.X, _config.Origin.WorldPosition.Y);
        var originFound = LastResult?.Origin?.Position ?? new Point2d(0, 0);
        var angleDeg = LastResult?.Origin?.AngleDeg ?? 0.0;

        var detectedPointMap = LastResult is null
            ? new Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase)
            : LastResult.Points.ToDictionary(x => x.Name, x => x.Position, StringComparer.OrdinalIgnoreCase);

        if (_config is not null && showRois)
        {
            if (hasPose)
            {
                AddRotatedRoiOverlay(_config.Origin.SearchRoi, "Origin S", Brushes.Lime, originTeach, originFound, angleDeg);
                if (LastResult?.Origin is not null && _config.Origin.TemplateRoi.Width > 0 && _config.Origin.TemplateRoi.Height > 0)
                {
                    var originPosForTemplate = LastResult.Origin.Position;
                    var mr = LastResult.Origin.MatchRect;
                    if (mr.Width > 0 && mr.Height > 0)
                    {
                        originPosForTemplate = new Point2d(mr.X + mr.Width / 2.0, mr.Y + mr.Height / 2.0);
                    }

                    AddRotatedTemplateAtPoint(originPosForTemplate, _config.Origin.TemplateRoi.Width, _config.Origin.TemplateRoi.Height, "Origin T", Brushes.Yellow, angleDeg);
                }
            }
            else
            {
                if (_config.Origin.SearchRoi.Width > 0 && _config.Origin.SearchRoi.Height > 0)
                {
                    OverlayItems.Add(new OverlayRectItem
                    {
                        X = _config.Origin.SearchRoi.X,
                        Y = _config.Origin.SearchRoi.Y,
                        Width = _config.Origin.SearchRoi.Width,
                        Height = _config.Origin.SearchRoi.Height,
                        Stroke = Brushes.Lime,
                        Label = "Origin S"
                    });
                }

                if (_config.Origin.TemplateRoi.Width > 0 && _config.Origin.TemplateRoi.Height > 0)
                {
                    OverlayItems.Add(new OverlayRectItem
                    {
                        X = _config.Origin.TemplateRoi.X,
                        Y = _config.Origin.TemplateRoi.Y,
                        Width = _config.Origin.TemplateRoi.Width,
                        Height = _config.Origin.TemplateRoi.Height,
                        Stroke = Brushes.Yellow,
                        Label = "Origin T"
                    });
                }
            }

            foreach (var sc in _config.SurfaceCompares)
            {
                if (sc.InspectRoi.Width > 0 && sc.InspectRoi.Height > 0)
                {
                    if (hasPose)
                    {
                        AddRotatedRoiOverlay(sc.InspectRoi, $"{sc.Name} SC", Brushes.DeepSkyBlue, originTeach, originFound, angleDeg);
                        AddRotatedRoiOverlay(sc.TemplateRoi, $"{sc.Name} SCT", Brushes.DeepSkyBlue, originTeach, originFound, angleDeg);
                    }
                    else
                    {
                        OverlayItems.Add(new OverlayRectItem
                        {
                            X = sc.InspectRoi.X,
                            Y = sc.InspectRoi.Y,
                            Width = sc.InspectRoi.Width,
                            Height = sc.InspectRoi.Height,
                            Stroke = Brushes.DeepSkyBlue,
                            Label = $"{sc.Name} SC"
                        });

                        if (sc.TemplateRoi.Width > 0 && sc.TemplateRoi.Height > 0)
                        {
                            OverlayItems.Add(new OverlayRectItem
                            {
                                X = sc.TemplateRoi.X,
                                Y = sc.TemplateRoi.Y,
                                Width = sc.TemplateRoi.Width,
                                Height = sc.TemplateRoi.Height,
                                Stroke = Brushes.DeepSkyBlue,
                                Label = $"{sc.Name} SCT"
                            });
                        }
                    }
                }

                if (sc.Rois is null || sc.Rois.Count == 0)
                {
                    continue;
                }

                var includeIdx = 0;
                var excludeIdx = 0;
                foreach (var rr in sc.Rois)
                {
                    var isExclude = rr.Mode == BlobRoiMode.Exclude;
                    var idx = isExclude ? ++excludeIdx : ++includeIdx;
                    var label = isExclude ? $"{sc.Name} SCX{idx}" : $"{sc.Name} SC{idx}";
                    var stroke = isExclude ? Brushes.Red : Brushes.DeepSkyBlue;

                    if (hasPose)
                    {
                        AddRotatedRoiOverlay(rr.Roi, label, stroke, originTeach, originFound, angleDeg);
                    }
                    else if (rr.Roi.Width > 0 && rr.Roi.Height > 0)
                    {
                        OverlayItems.Add(new OverlayRectItem
                        {
                            X = rr.Roi.X,
                            Y = rr.Roi.Y,
                            Width = rr.Roi.Width,
                            Height = rr.Roi.Height,
                            Stroke = stroke,
                            Label = label
                        });
                    }
                }
            }

            foreach (var cir in _config.CircleFinders)
            {
                if (cir.SearchRoi.Width <= 0 || cir.SearchRoi.Height <= 0)
                {
                    continue;
                }

                if (hasPose)
                {
                    AddRotatedRoiOverlay(cir.SearchRoi, $"{cir.Name} CIR", Brushes.MediumPurple, originTeach, originFound, angleDeg);
                }
                else
                {
                    OverlayItems.Add(new OverlayRectItem
                    {
                        X = cir.SearchRoi.X,
                        Y = cir.SearchRoi.Y,
                        Width = cir.SearchRoi.Width,
                        Height = cir.SearchRoi.Height,
                        Stroke = Brushes.MediumPurple,
                        Label = $"{cir.Name} CIR"
                    });
                }
            }

            foreach (var epd in _config.EdgePairDetections)
            {
                if (epd.SearchRoi.Width <= 0 || epd.SearchRoi.Height <= 0)
                {
                    continue;
                }

                if (hasPose)
                {
                    AddRotatedRoiOverlay(epd.SearchRoi, $"{epd.Name} EPD", Brushes.MediumPurple, originTeach, originFound, angleDeg);
                }
                else
                {
                    OverlayItems.Add(new OverlayRectItem
                    {
                        X = epd.SearchRoi.X,
                        Y = epd.SearchRoi.Y,
                        Width = epd.SearchRoi.Width,
                        Height = epd.SearchRoi.Height,
                        Stroke = Brushes.MediumPurple,
                        Label = $"{epd.Name} EPD"
                    });
                }
            }

            foreach (var l in _config.Lines)
            {
                if (hasPose)
                {
                    AddRotatedRoiOverlay(l.SearchRoi, $"{l.Name} L", Brushes.MediumPurple, originTeach, originFound, angleDeg);
                }
                else if (l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
                {
                    OverlayItems.Add(new OverlayRectItem
                    {
                        X = l.SearchRoi.X,
                        Y = l.SearchRoi.Y,
                        Width = l.SearchRoi.Width,
                        Height = l.SearchRoi.Height,
                        Stroke = Brushes.MediumPurple,
                        Label = $"{l.Name} L"
                    });
                }
            }

            foreach (var cal in _config.Calipers)
            {
                if (cal.SearchRoi.Width <= 0 || cal.SearchRoi.Height <= 0)
                {
                    continue;
                }

                if (hasPose)
                {
                    AddRotatedRoiOverlay(cal.SearchRoi, $"{cal.Name} Cal", Brushes.Lime, originTeach, originFound, angleDeg);
                }
                else
                {
                    OverlayItems.Add(new OverlayRectItem
                    {
                        X = cal.SearchRoi.X,
                        Y = cal.SearchRoi.Y,
                        Width = cal.SearchRoi.Width,
                        Height = cal.SearchRoi.Height,
                        Stroke = Brushes.Lime,
                        Label = $"{cal.Name} Cal"
                    });
                }

                var stripCount = Math.Clamp(cal.StripCount, 1, 100);
                var stripLength = Math.Max(3, cal.StripLength);

                if (cal.Orientation == CaliperOrientation.Vertical)
                {
                    var y1 = cal.SearchRoi.Y + (cal.SearchRoi.Height - stripLength) / 2.0;
                    var y2 = y1 + stripLength;
                    for (var i = 0; i < stripCount; i++)
                    {
                        var x = cal.SearchRoi.X + (i + 0.5) * cal.SearchRoi.Width / stripCount;
                        var p1 = new Point2d(x, y1);
                        var p2 = new Point2d(x, y2);
                        if (hasPose)
                        {
                            p1 = TransformPose(p1, originTeach, originFound, angleDeg);
                            p2 = TransformPose(p2, originTeach, originFound, angleDeg);
                        }
                        OverlayItems.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Brushes.Lime, StrokeThickness = 1.0 });
                    }
                }
                else
                {
                    var x1 = cal.SearchRoi.X + (cal.SearchRoi.Width - stripLength) / 2.0;
                    var x2 = x1 + stripLength;
                    for (var i = 0; i < stripCount; i++)
                    {
                        var y = cal.SearchRoi.Y + (i + 0.5) * cal.SearchRoi.Height / stripCount;
                        var p1 = new Point2d(x1, y);
                        var p2 = new Point2d(x2, y);
                        if (hasPose)
                        {
                            p1 = TransformPose(p1, originTeach, originFound, angleDeg);
                            p2 = TransformPose(p2, originTeach, originFound, angleDeg);
                        }
                        OverlayItems.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Brushes.Lime, StrokeThickness = 1.0 });
                    }
                }
            }

            foreach (var p in _config.Points)
            {
                if (hasPose)
                {
                    AddRotatedRoiOverlay(p.SearchRoi, $"{p.Name} S", Brushes.DeepSkyBlue, originTeach, originFound, angleDeg);

                    if (detectedPointMap.TryGetValue(p.Name, out var found) && p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
                    {
                        AddRotatedTemplateAtPoint(found, p.TemplateRoi.Width, p.TemplateRoi.Height, $"{p.Name} T", Brushes.Yellow, angleDeg);
                    }
                    else
                    {
                        AddRotatedRoiOverlay(p.TemplateRoi, $"{p.Name} T", Brushes.Yellow, originTeach, originFound, angleDeg);
                    }
                }
                else
                {
                    if (p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0)
                    {
                        OverlayItems.Add(new OverlayRectItem
                        {
                            X = p.SearchRoi.X,
                            Y = p.SearchRoi.Y,
                            Width = p.SearchRoi.Width,
                            Height = p.SearchRoi.Height,
                            Stroke = Brushes.DeepSkyBlue,
                            Label = $"{p.Name} S"
                        });
                    }

                    if (p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
                    {
                        OverlayItems.Add(new OverlayRectItem
                        {
                            X = p.TemplateRoi.X,
                            Y = p.TemplateRoi.Y,
                            Width = p.TemplateRoi.Width,
                            Height = p.TemplateRoi.Height,
                            Stroke = Brushes.Yellow,
                            Label = $"{p.Name} T"
                        });
                    }
                }
            }

            if (hasPose)
            {
                AddRotatedRoiOverlay(_config.DefectConfig.InspectRoi, "DefectROI", Brushes.Orange, originTeach, originFound, angleDeg);
            }
            else if (_config.DefectConfig.InspectRoi.Width > 0 && _config.DefectConfig.InspectRoi.Height > 0)
            {
                OverlayItems.Add(new OverlayRectItem
                {
                    X = _config.DefectConfig.InspectRoi.X,
                    Y = _config.DefectConfig.InspectRoi.Y,
                    Width = _config.DefectConfig.InspectRoi.Width,
                    Height = _config.DefectConfig.InspectRoi.Height,
                    Stroke = Brushes.Orange,
                    Label = "DefectROI"
                });
            }

            foreach (var b in _config.BlobDetections)
            {
                if (b.InspectRoi.Width > 0 && b.InspectRoi.Height > 0)
                {
                    if (hasPose)
                    {
                        AddRotatedRoiOverlay(b.InspectRoi, $"{b.Name} B", Brushes.Gold, originTeach, originFound, angleDeg);
                    }
                    else
                    {
                        OverlayItems.Add(new OverlayRectItem
                        {
                            X = b.InspectRoi.X,
                            Y = b.InspectRoi.Y,
                            Width = b.InspectRoi.Width,
                            Height = b.InspectRoi.Height,
                            Stroke = Brushes.Gold,
                            Label = $"{b.Name} B"
                        });
                    }
                }

                if (b.Rois is null || b.Rois.Count == 0)
                {
                    continue;
                }

                var includeIdx = 0;
                var excludeIdx = 0;
                foreach (var rr in b.Rois)
                {
                    var isExclude = rr.Mode == BlobRoiMode.Exclude;
                    var idx = isExclude ? ++excludeIdx : ++includeIdx;
                    var label = isExclude ? $"{b.Name} BX{idx}" : $"{b.Name} B{idx}";
                    var stroke = isExclude ? Brushes.Red : Brushes.Gold;

                    if (hasPose)
                    {
                        AddRotatedRoiOverlay(rr.Roi, label, stroke, originTeach, originFound, angleDeg);
                    }
                    else if (rr.Roi.Width > 0 && rr.Roi.Height > 0)
                    {
                        OverlayItems.Add(new OverlayRectItem
                        {
                            X = rr.Roi.X,
                            Y = rr.Roi.Y,
                            Width = rr.Roi.Width,
                            Height = rr.Roi.Height,
                            Stroke = stroke,
                            Label = label
                        });
                    }
                }
            }

            foreach (var lpd in _config.LinePairDetections)
            {
                if (lpd.SearchRoi.Width <= 0 || lpd.SearchRoi.Height <= 0)
                {
                    continue;
                }

                if (hasPose)
                {
                    AddRotatedRoiOverlay(lpd.SearchRoi, $"{lpd.Name} LP", Brushes.MediumPurple, originTeach, originFound, angleDeg);
                }
                else
                {
                    OverlayItems.Add(new OverlayRectItem
                    {
                        X = lpd.SearchRoi.X,
                        Y = lpd.SearchRoi.Y,
                        Width = lpd.SearchRoi.Width,
                        Height = lpd.SearchRoi.Height,
                        Stroke = Brushes.MediumPurple,
                        Label = $"{lpd.Name} LP"
                    });
                }
            }

            foreach (var cdt in _config.CodeDetections)
            {
                if (cdt.SearchRoi.Width <= 0 || cdt.SearchRoi.Height <= 0)
                {
                    continue;
                }

                if (hasPose)
                {
                    AddRotatedRoiOverlay(cdt.SearchRoi, $"{cdt.Name} C", Brushes.Lime, originTeach, originFound, angleDeg);
                }
                else
                {
                    OverlayItems.Add(new OverlayRectItem
                    {
                        X = cdt.SearchRoi.X,
                        Y = cdt.SearchRoi.Y,
                        Width = cdt.SearchRoi.Width,
                        Height = cdt.SearchRoi.Height,
                        Stroke = Brushes.Lime,
                        Label = $"{cdt.Name} C"
                    });
                }
            }

            if (LastResult is not null)
            {
                foreach (var cir in LastResult.CircleFinders)
                {
                    if (!cir.Found || cir.RadiusPx <= 0)
                    {
                        continue;
                    }

                    AddCircle(OverlayItems, cir.Center.X, cir.Center.Y, cir.RadiusPx, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
                    AddCross(OverlayItems, cir.Center.X, cir.Center.Y, size: 12.0, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
                    OverlayItems.Add(new OverlayPointItem { X = cir.Center.X, Y = cir.Center.Y, Radius = 1.0, Stroke = Brushes.MediumPurple, Label = cir.Name });
                }

                foreach (var dia in LastResult.Diameters)
                {
                    if (!dia.Found || double.IsNaN(dia.Value) || dia.RadiusPx <= 0)
                    {
                        continue;
                    }

                    var stroke = dia.Pass ? Brushes.Lime : Brushes.Red;
                    AddCircle(OverlayItems, dia.Center.X, dia.Center.Y, dia.RadiusPx, stroke: stroke, strokeThickness: 2.0);
                    AddCross(OverlayItems, dia.Center.X, dia.Center.Y, size: 12.0, stroke: stroke, strokeThickness: 2.0);
                    OverlayItems.Add(new OverlayPointItem { X = dia.Center.X, Y = dia.Center.Y, Radius = 1.0, Stroke = stroke, Label = $"{dia.Name}: {dia.Value:0.###} mm" });
                }
            }
        }

        if (LastResult?.Origin is not null)
        {
            var originPos = LastResult.Origin.Position;
            var angle = LastResult.Origin.AngleDeg;

            var mr = LastResult.Origin.MatchRect;
            if (mr.Width > 0 && mr.Height > 0)
            {
                var cx = mr.X + mr.Width / 2.0;
                var cy = mr.Y + mr.Height / 2.0;
                OverlayItems.Add(new OverlayLineItem { X1 = mr.X, Y1 = cy, X2 = mr.X + mr.Width, Y2 = cy, Stroke = LastResult.Origin.Pass ? Brushes.Lime : Brushes.Red });
                OverlayItems.Add(new OverlayLineItem { X1 = cx, Y1 = mr.Y, X2 = cx, Y2 = mr.Y + mr.Height, Stroke = LastResult.Origin.Pass ? Brushes.Lime : Brushes.Red });

                originPos = new Point2d(cx, cy);
            }

            OverlayItems.Add(new OverlayPointItem
            {
                X = originPos.X,
                Y = originPos.Y,
                Stroke = LastResult.Origin.Pass ? Brushes.Lime : Brushes.Red,
                Label = $"Origin ({angle:0.0}°)"
            });
        }

        if (LastResult is not null)
        {
            foreach (var l in LastResult.Lines)
            {
                if (!l.Found)
                {
                    continue;
                }

                OverlayItems.Add(new OverlayLineItem
                {
                    X1 = l.P1.X,
                    Y1 = l.P1.Y,
                    X2 = l.P2.X,
                    Y2 = l.P2.Y,
                    Stroke = Brushes.MediumPurple,
                    Label = l.Name
                });
            }

            foreach (var p in LastResult.Points)
            {
                var mr = p.MatchRect;
                var pos = p.Position;
                if (mr.Width > 0 && mr.Height > 0)
                {
                    var halfW = mr.Width / 2.0;
                    var halfH = mr.Height / 2.0;
                    AddRotatedCrosshair(pos, halfW, halfH, label: null, p.Pass ? Brushes.DeepSkyBlue : Brushes.Red, angleDeg);
                }
                else
                {
                    AddRotatedCrosshair(pos, 14.0, 14.0, label: null, p.Pass ? Brushes.DeepSkyBlue : Brushes.Red, angleDeg);
                }

                var pTeach = hasPose
                    ? InverseTransformPose(pos, originTeach, originFound, angleDeg)
                    : new Point2d(double.NaN, double.NaN);

                OverlayItems.Add(new OverlayPointItem
                {
                    X = pos.X,
                    Y = pos.Y,
                    Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red,
                    Label = hasPose
                        ? $"{p.Name} ({pos.X:0},{pos.Y:0})  w({pTeach.X:0},{pTeach.Y:0})"
                        : $"{p.Name} ({pos.X:0},{pos.Y:0})"
                });
            }

            var distanceAnchorMap = new Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in LastResult.Points)
            {
                distanceAnchorMap[p.Name] = p.Position;
            }
            foreach (var c in LastResult.CircleFinders)
            {
                if (c.Found)
                {
                    distanceAnchorMap[c.Name] = c.Center;
                }
            }
            foreach (var d2 in LastResult.Diameters)
            {
                if (d2.Found)
                {
                    distanceAnchorMap[d2.Name] = d2.Center;
                }
            }
            foreach (var d in LastResult.Distances)
            {
                if (!distanceAnchorMap.TryGetValue(d.PointA, out var a) || !distanceAnchorMap.TryGetValue(d.PointB, out var b))
                {
                    continue;
                }

                OverlayItems.Add(new OverlayLineItem
                {
                    X1 = a.X,
                    Y1 = a.Y,
                    X2 = b.X,
                    Y2 = b.Y,
                    Stroke = d.Pass ? Brushes.Lime : Brushes.Red,
                    Label = _config?.PixelsPerMm > 0
                        ? $"{d.Name}: {d.Value:0.00} mm ({d.Value * _config.PixelsPerMm:0.0} px)"
                        : $"{d.Name}: {d.Value:0.00}"
                });
            }

            if (LastResult.Defects is not null)
            {
                foreach (var def in LastResult.Defects.Defects)
                {
                    OverlayItems.Add(new OverlayRectItem
                    {
                        X = def.BoundingBox.X,
                        Y = def.BoundingBox.Y,
                        Width = def.BoundingBox.Width,
                        Height = def.BoundingBox.Height,
                        Stroke = Brushes.OrangeRed,
                        Label = def.Type
                    });
                }
            }

            if (LastResult.BlobDetections is not null)
            {
                foreach (var bd in LastResult.BlobDetections)
                {
                    if (bd.Blobs is null || bd.Blobs.Count == 0)
                    {
                        continue;
                    }

                    var n = Math.Min(bd.Blobs.Count, MaxBlobOverlayCount);
                    for (var i = 0; i < n; i++)
                    {
                        var bi = bd.Blobs[i];
                        var br = bi.BoundingBox;
                        if (br.Width > 0 && br.Height > 0)
                        {
                            OverlayItems.Add(new OverlayRectItem
                            {
                                X = br.X,
                                Y = br.Y,
                                Width = br.Width,
                                Height = br.Height,
                                Stroke = Brushes.Gold,
                                Label = string.Empty
                            });
                        }

                        OverlayItems.Add(new OverlayPointItem
                        {
                            X = bi.Centroid.X,
                            Y = bi.Centroid.Y,
                            Radius = 3.0,
                            Stroke = Brushes.Gold,
                            Label = string.Empty
                        });
                    }
                }
            }

            if (LastResult.SurfaceCompares is not null)
            {
                foreach (var sc in LastResult.SurfaceCompares)
                {
                    var scDef = _config?.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, sc.Name, StringComparison.OrdinalIgnoreCase));
                    var stroke = sc.Pass ? Brushes.Lime : Brushes.Red;
                    var status = sc.Pass ? "OK" : "NG";

                    if (sc.Defects is not null && sc.Defects.Count > 0)
                    {
                        var n = Math.Min(sc.Defects.Count, MaxBlobOverlayCount);
                        for (var i = 0; i < n; i++)
                        {
                            var d = sc.Defects[i];
                            var r = d.BoundingBox;
                            if (r.Width > 0 && r.Height > 0)
                            {
                                OverlayItems.Add(new OverlayRectItem
                                {
                                    X = r.X,
                                    Y = r.Y,
                                    Width = r.Width,
                                    Height = r.Height,
                                    Stroke = stroke,
                                    StrokeThickness = 2.0,
                                    Label = string.Empty
                                });
                            }
                        }
                    }

                    // Position label at the transformed Search ROI if available.
                    double lx = 2, ly = 16;
                    if (scDef is not null)
                    {
                        var tr = TransformPose(new Point2d(scDef.InspectRoi.X, scDef.InspectRoi.Y), originTeach, originFound, angleDeg);
                        lx = tr.X + 2;
                        ly = tr.Y + 2;
                    }

                    OverlayItems.Add(new OverlayPointItem
                    {
                        X = lx,
                        Y = ly,
                        Radius = 1.0,
                        Stroke = stroke,
                        Label = $"{sc.Name} [{status}]: Số lỗi: {sc.Count}, S.Lớn nhất: {sc.MaxArea:0}"
                    });

                    if (sc.Defects is not null && sc.Defects.Count > MaxBlobOverlayCount)
                    {
                        OverlayItems.Add(new OverlayPointItem
                        {
                            X = lx,
                            Y = ly + 14,
                            Radius = 1.0,
                            Stroke = stroke,
                            Label = $"+{sc.Defects.Count - MaxBlobOverlayCount}"
                        });
                    }
                }
            }
        }

        if (LastResult is not null)
        {
            foreach (var l in LastResult.Lines)
            {
                if (!l.Found) continue;
                OverlayItems.Add(new OverlayLineItem
                {
                    X1 = l.P1.X,
                    Y1 = l.P1.Y,
                    X2 = l.P2.X,
                    Y2 = l.P2.Y,
                    Stroke = Brushes.MediumPurple,
                    Label = l.Name
                });
            }

            foreach (var d in LastResult.LineToLineDistances)
            {
                if (double.IsNaN(d.Value)) continue;
                OverlayItems.Add(new OverlayLineItem
                {
                    X1 = d.ClosestA.X,
                    Y1 = d.ClosestA.Y,
                    X2 = d.ClosestB.X,
                    Y2 = d.ClosestB.Y,
                    Stroke = d.Pass ? Brushes.Lime : Brushes.Red,
                    Label = $"{d.Name}: {d.Value:0.00} mm"
                });
            }

            foreach (var d in LastResult.PointToLineDistances)
            {
                if (double.IsNaN(d.Value)) continue;
                OverlayItems.Add(new OverlayLineItem
                {
                    X1 = d.ClosestA.X,
                    Y1 = d.ClosestA.Y,
                    X2 = d.ClosestB.X,
                    Y2 = d.ClosestB.Y,
                    Stroke = d.Pass ? Brushes.Lime : Brushes.Red,
                    Label = $"{d.Name}: {d.Value:0.00} mm"
                });
            }

            foreach (var a in LastResult.Angles)
            {
                if (double.IsNaN(a.ValueDeg)) continue;

                if (a.Found)
                {
                    var bmp = Image as System.Windows.Media.Imaging.BitmapSource;
                    var imgW = bmp?.PixelWidth ?? 0;
                    var imgH = bmp?.PixelHeight ?? 0;

                    var ip = new System.Windows.Point(a.Intersection.X, a.Intersection.Y);
                    var aDir = new System.Windows.Point(a.ADir.X, a.ADir.Y);
                    var bDir = new System.Windows.Point(a.BDir.X, a.BDir.Y);

                    if (TryClipInfiniteLineToImage(ip, aDir, imgW, imgH, out var a1, out var a2))
                    {
                        OverlayItems.Add(new OverlayLineItem { X1 = a1.X, Y1 = a1.Y, X2 = a2.X, Y2 = a2.Y, Stroke = Brushes.MediumPurple, Label = a.LineA });
                    }
                    else
                    {
                        var len = 60.0;
                        OverlayItems.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.ADir.X * len, Y2 = a.Intersection.Y + a.ADir.Y * len, Stroke = Brushes.MediumPurple, Label = a.LineA });
                    }

                    if (TryClipInfiniteLineToImage(ip, bDir, imgW, imgH, out var b1, out var b2))
                    {
                        OverlayItems.Add(new OverlayLineItem { X1 = b1.X, Y1 = b1.Y, X2 = b2.X, Y2 = b2.Y, Stroke = Brushes.Gold, Label = a.LineB });
                    }
                    else
                    {
                        var len = 60.0;
                        OverlayItems.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.BDir.X * len, Y2 = a.Intersection.Y + a.BDir.Y * len, Stroke = Brushes.Gold, Label = a.LineB });
                    }

                    AddAngleArc(OverlayItems, a.Intersection.X, a.Intersection.Y, a.ADir.X, a.ADir.Y, a.BDir.X, a.BDir.Y, radius: 35.0, stroke: a.Pass ? Brushes.Lime : Brushes.Red);
                    OverlayItems.Add(new OverlayPointItem { X = a.Intersection.X, Y = a.Intersection.Y, Radius = 3.0, Stroke = a.Pass ? Brushes.Lime : Brushes.Red, Label = $"{a.Name}: {a.ValueDeg:0.###}°" });
                }
                else
                {
                    OverlayItems.Add(new OverlayPointItem { X = 12, Y = 12, Radius = 1.0, Stroke = a.Pass ? Brushes.Lime : Brushes.Red, Label = $"{a.Name}: {a.ValueDeg:0.###}°" });
                }
            }
        }

        if (LastResult is not null)
        {
            foreach (var dd in LastResult.PointToLineDistances)
            {
                OverlayItems.Add(new OverlayLineItem
                {
                    X1 = dd.ClosestA.X,
                    Y1 = dd.ClosestA.Y,
                    X2 = dd.ClosestB.X,
                    Y2 = dd.ClosestB.Y,
                    Stroke = dd.Pass ? Brushes.Lime : Brushes.Red,
                    Label = $"{dd.Name}: {dd.Value:0.00} mm"
                });
            }

            foreach (var epd in LastResult.EdgePairDetections)
            {
                if (!epd.Found || double.IsNaN(epd.Value))
                {
                    continue;
                }

                OverlayItems.Add(new OverlayLineItem { X1 = epd.L1P1.X, Y1 = epd.L1P1.Y, X2 = epd.L1P2.X, Y2 = epd.L1P2.Y, Stroke = Brushes.MediumPurple, Label = $"{epd.Name} E1" });
                OverlayItems.Add(new OverlayLineItem { X1 = epd.L2P1.X, Y1 = epd.L2P1.Y, X2 = epd.L2P2.X, Y2 = epd.L2P2.Y, Stroke = Brushes.MediumPurple, Label = $"{epd.Name} E2" });
                OverlayItems.Add(new OverlayLineItem { X1 = epd.ClosestA.X, Y1 = epd.ClosestA.Y, X2 = epd.ClosestB.X, Y2 = epd.ClosestB.Y, Stroke = epd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{epd.Name}: {epd.Value:0.###}" });
            }

            foreach (var c in LastResult.Calipers)
            {
                if (c.Found)
                {
                    OverlayItems.Add(new OverlayLineItem { X1 = c.LineP1.X, Y1 = c.LineP1.Y, X2 = c.LineP2.X, Y2 = c.LineP2.Y, Stroke = Brushes.Gold, StrokeThickness = 2.0, Label = c.Name });
                }

                if (c.Points is not null)
                {
                    var n = Math.Min(c.Points.Count, 80);
                    for (var i = 0; i < n; i++)
                    {
                        var p = c.Points[i];
                        OverlayItems.Add(new OverlayPointItem { X = p.X, Y = p.Y, Radius = 2.0, Stroke = Brushes.Gold, Label = string.Empty });
                    }
                }
            }
        }

        if (LastResult is not null)
        {
            foreach (var c in LastResult.Calipers)
            {
                if (c.Found)
                {
                    OverlayItems.Add(new OverlayLineItem
                    {
                        X1 = c.LineP1.X,
                        Y1 = c.LineP1.Y,
                        X2 = c.LineP2.X,
                        Y2 = c.LineP2.Y,
                        Stroke = Brushes.Gold,
                        StrokeThickness = 2.0,
                        Label = c.Name
                    });
                }

                if (c.Points is not null)
                {
                    var n = Math.Min(c.Points.Count, 80);
                    for (var i = 0; i < n; i++)
                    {
                        var p = c.Points[i];
                        OverlayItems.Add(new OverlayPointItem { X = p.X, Y = p.Y, Radius = 2.0, Stroke = Brushes.Gold, Label = string.Empty });
                    }
                }
            }
        }

        // TextNode overlays
        if (_config?.TextNodes is not null && _config.TextNodes.Count > 0 && LastResult is not null)
        {
            Dictionary<string, ConditionEvaluator.Variable>? vars = null;
            try { vars = ConditionEvaluator.BuildVariableMap(LastResult); } catch { vars = null; }

            foreach (var t in _config.TextNodes)
            {
                if (t is null || string.IsNullOrWhiteSpace(t.Name)) continue;

                var text = ToolEditorViewModel.EvaluateTextTemplate(t.Text ?? string.Empty, vars);

                var brush = ToolEditorViewModel.TryParseHexBrush(t.DefaultColor) ?? Brushes.White;
                if (vars is not null && t.Conditions is not null)
                {
                    foreach (var c in t.Conditions)
                    {
                        if (c is null || string.IsNullOrWhiteSpace(c.Expression)) continue;
                        try
                        {
                            if (ConditionEvaluator.Evaluate(c.Expression, vars))
                            {
                                brush = ToolEditorViewModel.TryParseHexBrush(c.Color) ?? brush;
                                break;
                            }
                        }
                        catch { /* ignore bad expressions */ }
                    }
                }

                OverlayItems.Add(new OverlayTextItem
                {
                    X = t.X,
                    Y = t.Y,
                    Text = text,
                    Foreground = brush,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 0, 0, 0))
                });
            }
        }
    }

    private static bool TryClipInfiniteLineToImage(System.Windows.Point p, System.Windows.Point dir, int width, int height, out System.Windows.Point p1, out System.Windows.Point p2)
    {
        p1 = default;
        p2 = default;

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var dx = dir.X;
        var dy = dir.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            return false;
        }

        var ts = new List<double>(4);

        if (Math.Abs(dx) > 1e-9)
        {
            var t = (0.0 - p.X) / dx;
            var y = p.Y + t * dy;
            if (y >= 0 && y <= height) ts.Add(t);

            t = (width - p.X) / dx;
            y = p.Y + t * dy;
            if (y >= 0 && y <= height) ts.Add(t);
        }

        if (Math.Abs(dy) > 1e-9)
        {
            var t = (0.0 - p.Y) / dy;
            var x = p.X + t * dx;
            if (x >= 0 && x <= width) ts.Add(t);

            t = (height - p.Y) / dy;
            x = p.X + t * dx;
            if (x >= 0 && x <= width) ts.Add(t);
        }

        if (ts.Count < 2)
        {
            return false;
        }

        ts.Sort();
        var t1 = ts.First();
        var t2 = ts.Last();

        p1 = new System.Windows.Point(p.X + t1 * dx, p.Y + t1 * dy);
        p2 = new System.Windows.Point(p.X + t2 * dx, p.Y + t2 * dy);
        return true;
    }

    private static void AddAngleArc(ObservableCollection<OverlayItem> dst, double cx, double cy, double ax, double ay, double bx, double by, double radius, System.Windows.Media.Brush stroke)
    {
        var a0 = Math.Atan2(ay, ax);
        var a1 = Math.Atan2(by, bx);
        var d = a1 - a0;
        while (d <= -Math.PI) d += 2 * Math.PI;
        while (d > Math.PI) d -= 2 * Math.PI;

        var steps = Math.Clamp((int)Math.Ceiling(Math.Abs(d) / (Math.PI / 18.0)), 4, 36);
        var prevX = cx + Math.Cos(a0) * radius;
        var prevY = cy + Math.Sin(a0) * radius;
        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var aa = a0 + d * t;
            var x = cx + Math.Cos(aa) * radius;
            var y = cy + Math.Sin(aa) * radius;
            dst.Add(new OverlayLineItem { X1 = prevX, Y1 = prevY, X2 = x, Y2 = y, Stroke = stroke, StrokeThickness = 2.0, Label = string.Empty });
            prevX = x;
            prevY = y;
        }
    }

    private static void AddCircle(ObservableCollection<OverlayItem> dst, double cx, double cy, double radius, Brush stroke, double strokeThickness)
    {
        if (radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius))
        {
            return;
        }

        var steps = Math.Clamp((int)Math.Ceiling(2.0 * Math.PI * radius / 12.0), 24, 240);
        var prevX = cx + radius;
        var prevY = cy;
        for (var i = 1; i <= steps; i++)
        {
            var a = (double)i / steps * 2.0 * Math.PI;
            var x = cx + Math.Cos(a) * radius;
            var y = cy + Math.Sin(a) * radius;
            dst.Add(new OverlayLineItem { X1 = prevX, Y1 = prevY, X2 = x, Y2 = y, Stroke = stroke, StrokeThickness = strokeThickness, Label = string.Empty });
            prevX = x;
            prevY = y;
        }
    }

    private static void AddCross(ObservableCollection<OverlayItem> dst, double cx, double cy, double size, Brush stroke, double strokeThickness)
    {
        var s = Math.Max(1.0, size);
        dst.Add(new OverlayLineItem { X1 = cx - s, Y1 = cy, X2 = cx + s, Y2 = cy, Stroke = stroke, StrokeThickness = strokeThickness, Label = string.Empty });
        dst.Add(new OverlayLineItem { X1 = cx, Y1 = cy - s, X2 = cx, Y2 = cy + s, Stroke = stroke, StrokeThickness = strokeThickness, Label = string.Empty });
    }
}
