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

    private Mat? _imageMat;

    private const int MaxBlobOverlayCount = 300;

    public InspectionViewModel(IConfigService configService, IInspectionService inspectionService, ConfigStoreOptions storeOptions)
    {
        _configService = configService;
        _inspectionService = inspectionService;
        _storeOptions = storeOptions;

        LoadImageCommand = new RelayCommand(LoadImage);
        RunInspectionCommand = new RelayCommand(RunInspection);
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
    private InspectionResult? _lastResult;

    partial void OnLastResultChanged(InspectionResult? value)
    {
        RefreshSpecResults();
    }

    [ObservableProperty]
    private bool _showRois = true;

    partial void OnShowRoisChanged(bool value)
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

    private void RunInspection()
    {
        if (_imageMat is null)
        {
            return;
        }

        var code = SelectedConfig ?? ProductCode;
        if (_config is null || !string.Equals(_config.ProductCode, code, StringComparison.OrdinalIgnoreCase))
        {
            _config = _configService.LoadConfig(code);
        }

        LastResult = _inspectionService.Inspect(_imageMat, _config);

        RefreshOverlayItems();
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
    }

    private void RefreshOverlayItems()
    {
        OverlayItems.Clear();

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
                AddRotatedRoiOverlay(_config.Origin.TemplateRoi, "Origin T", Brushes.Yellow, originTeach, originFound, angleDeg);
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
                    var cx = mr.X + mr.Width / 2.0;
                    var cy = mr.Y + mr.Height / 2.0;
                    OverlayItems.Add(new OverlayLineItem { X1 = mr.X, Y1 = cy, X2 = mr.X + mr.Width, Y2 = cy, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                    OverlayItems.Add(new OverlayLineItem { X1 = cx, Y1 = mr.Y, X2 = cx, Y2 = mr.Y + mr.Height, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                    pos = new Point2d(cx, cy);
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

            var pointMap = LastResult.Points.ToDictionary(x => x.Name, x => x.Position, StringComparer.OrdinalIgnoreCase);
            foreach (var d in LastResult.Distances)
            {
                if (!pointMap.TryGetValue(d.PointA, out var a) || !pointMap.TryGetValue(d.PointB, out var b))
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
        }
    }
}
