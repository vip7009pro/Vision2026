using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class ToolEditorViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly ConfigStoreOptions _storeOptions;
    private readonly SharedImageContext _sharedImage;
    private readonly ImagePreprocessor _preprocessor;
    private readonly LineDetector _lineDetector;
    private readonly IInspectionService _inspectionService;

    private ToolGraphNodeViewModel? _selectedNodeHook;
    private string? _selectedNodePrevRefName;

    private readonly DispatcherTimer _autoSaveTimer;
    private bool _autoSavePending;

    private bool _syncingInputs;

    public ToolEditorViewModel(IConfigService configService, ConfigStoreOptions storeOptions, SharedImageContext sharedImage, ImagePreprocessor preprocessor, LineDetector lineDetector, IInspectionService inspectionService)
    {
        _configService = configService;
        _storeOptions = storeOptions;
        _sharedImage = sharedImage;
        _preprocessor = preprocessor;
        _lineDetector = lineDetector;
        _inspectionService = inspectionService;

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _autoSaveTimer.Tick += (_, __) => AutoSaveNow();

        AvailableConfigs = new ObservableCollection<string>();
        ToolboxItems = new ObservableCollection<string>
        {
            "Origin",
            "Point",
            "Line",
            "Distance",
            "LineLineDistance",
            "PointLineDistance",
            "DefectRoi"
        };

        Nodes = new ObservableCollection<ToolGraphNodeViewModel>();
        Edges = new ObservableCollection<ToolGraphEdgeViewModel>();
        SelectedNodeOverlayItems = new ObservableCollection<OverlayItem>();
        FinalOverlayItems = new ObservableCollection<OverlayItem>();

        RefreshConfigsCommand = new RelayCommand(RefreshConfigs);
        LoadConfigCommand = new RelayCommand(LoadConfig);
        SaveConfigCommand = new RelayCommand(SaveConfig);
        NewGraphCommand = new RelayCommand(NewGraph);
        DeleteSelectedNodeCommand = new RelayCommand(DeleteSelectedNode);
        LoadPreviewImageCommand = new RelayCommand(LoadPreviewImage);
        RunFlowCommand = new RelayCommand(RunFlow);
        RoiSelectedCommand = new RelayCommand<object?>(OnRoiSelected);
        RoiEditedCommand = new RelayCommand<RoiSelection?>(OnRoiEdited);

        _sharedImage.ImageChanged += (_, __) => RefreshPreviews();

        RefreshConfigs();
    }

    public ObservableCollection<LineLineDistanceMode> AvailableLineLineDistanceModes { get; }
        = new ObservableCollection<LineLineDistanceMode>((LineLineDistanceMode[])Enum.GetValues(typeof(LineLineDistanceMode)));

    public ObservableCollection<PointLineDistanceMode> AvailablePointLineDistanceModes { get; }
        = new ObservableCollection<PointLineDistanceMode>((PointLineDistanceMode[])Enum.GetValues(typeof(PointLineDistanceMode)));

    private void OnRoiSelected(object? arg)
    {
        if (_config is null)
        {
            return;
        }

        if (arg is RoiSelection rs)
        {
            // Treat drawing as "set this ROI" for the active label (S/T/L/DefectROI)
            ApplyRoiForLabel(rs.Label, rs.Roi);
            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
            RequestAutoSave();
            return;
        }

        if (arg is Roi roi)
        {
            // Fallback: when no label is available, apply to the selected node's primary ROI
            if (SelectedNode is null)
            {
                return;
            }

            if (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase))
            {
                _config.Origin.SearchRoi = roi;
            }
            else if (string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase))
            {
                var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (p is not null) p.SearchRoi = roi;
            }
            else if (string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase))
            {
                var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (l is not null) l.SearchRoi = roi;
            }
            else if (string.Equals(SelectedNode.Type, "DefectRoi", StringComparison.OrdinalIgnoreCase))
            {
                _config.DefectConfig.InspectRoi = roi;
            }

            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
            RequestAutoSave();
        }
    }

    private void RefreshLineRoiPreview(Mat image)
    {
        if (!LinePreviewEnabled)
        {
            LinePreviewImage = null;
            return;
        }

        var def = SelectedLineDef();
        if (def is null || def.SearchRoi.Width <= 0 || def.SearchRoi.Height <= 0)
        {
            LinePreviewImage = null;
            return;
        }

        var r = new OpenCvSharp.Rect(def.SearchRoi.X, def.SearchRoi.Y, def.SearchRoi.Width, def.SearchRoi.Height);
        r = r.Intersect(new OpenCvSharp.Rect(0, 0, image.Width, image.Height));
        if (r.Width <= 0 || r.Height <= 0)
        {
            LinePreviewImage = null;
            return;
        }

        using var processed = _preprocessor.Run(image, _config!.Preprocess);
        using var crop = new Mat(processed, r);
        using var view = crop.Channels() == 1 ? crop.Clone() : crop.CvtColor(ColorConversionCodes.BGR2GRAY);

        var det = _lineDetector.DetectLongestLine(processed, def.SearchRoi, def.Canny1, def.Canny2, def.HoughThreshold, def.MinLineLength, def.MaxLineGap);
        if (det.Found)
        {
            var p1 = new OpenCvSharp.Point((int)Math.Round(det.P1.X) - r.X, (int)Math.Round(det.P1.Y) - r.Y);
            var p2 = new OpenCvSharp.Point((int)Math.Round(det.P2.X) - r.X, (int)Math.Round(det.P2.Y) - r.Y);
            Cv2.Line(view, p1, p2, Scalar.White, 2);
        }

        LinePreviewImage = view.ToBitmapSource();
    }

    private static (double DistPx, Point2d A, Point2d B) CalculateLineLineDistance(LineDetectResult la, LineDetectResult lb, LineLineDistanceMode mode)
    {
        if (mode == LineLineDistanceMode.MidpointToMidpoint)
        {
            var ma = new Point2d((la.P1.X + la.P2.X) * 0.5, (la.P1.Y + la.P2.Y) * 0.5);
            var mb = new Point2d((lb.P1.X + lb.P2.X) * 0.5, (lb.P1.Y + lb.P2.Y) * 0.5);
            return (Geometry2D.Distance(ma, mb), ma, mb);
        }

        if (mode == LineLineDistanceMode.NearestEndpoints || mode == LineLineDistanceMode.FarthestEndpoints)
        {
            var aEnds = new[] { la.P1, la.P2 };
            var bEnds = new[] { lb.P1, lb.P2 };
            var bestDist = mode == LineLineDistanceMode.FarthestEndpoints ? double.NegativeInfinity : double.PositiveInfinity;
            var bestA = la.P1;
            var bestB = lb.P1;
            foreach (var a in aEnds)
            {
                foreach (var b in bEnds)
                {
                    var d = Geometry2D.Distance(a, b);
                    if (mode == LineLineDistanceMode.NearestEndpoints)
                    {
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestA = a;
                            bestB = b;
                        }
                    }
                    else
                    {
                        if (d > bestDist)
                        {
                            bestDist = d;
                            bestA = a;
                            bestB = b;
                        }
                    }
                }
            }
            return (bestDist, bestA, bestB);
        }

        // Default / legacy
        return Geometry2D.SegmentToSegmentDistance(la.P1, la.P2, lb.P1, lb.P2);
    }

    private static (double DistPx, Point2d ClosestOnLine) CalculatePointLineDistance(Point2d p, LineDetectResult l, PointLineDistanceMode mode)
    {
        if (mode == PointLineDistanceMode.PointToInfiniteLine)
        {
            var a = l.P1;
            var b = l.P2;
            var abx = b.X - a.X;
            var aby = b.Y - a.Y;
            var apx = p.X - a.X;
            var apy = p.Y - a.Y;
            var ab2 = abx * abx + aby * aby;
            if (ab2 <= 1e-12)
            {
                return (Geometry2D.Distance(p, a), a);
            }

            var t = (apx * abx + apy * aby) / ab2;
            var proj = new Point2d(a.X + t * abx, a.Y + t * aby);
            return (Geometry2D.Distance(p, proj), proj);
        }

        return Geometry2D.PointToSegmentDistance(p, l.P1, l.P2);
    }

    private static Point2dModel RoiCenterToWorld(Roi roi)
    {
        return new Point2dModel
        {
            X = roi.X + roi.Width / 2.0,
            Y = roi.Y + roi.Height / 2.0
        };
    }

    private void ApplyRoiForLabel(string labelRaw, Roi roi)
    {
        if (_config is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(labelRaw))
        {
            return;
        }

        var label = labelRaw.Trim();

        if (string.Equals(label, "DefectROI", StringComparison.OrdinalIgnoreCase))
        {
            _config.DefectConfig.InspectRoi = roi;
            return;
        }

        if (string.Equals(label, "Origin S", StringComparison.OrdinalIgnoreCase))
        {
            _config.Origin.SearchRoi = roi;
            return;
        }

        if (string.Equals(label, "Origin T", StringComparison.OrdinalIgnoreCase))
        {
            _config.Origin.TemplateRoi = roi;
            _config.Origin.WorldPosition = RoiCenterToWorld(roi);
            TrySaveTemplateImage("origin", roi, isOrigin: true, pointName: null);
            return;
        }

        var parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return;
        }

        var name = parts[0];
        var kind = parts[1];

        if (string.Equals(kind, "S", StringComparison.OrdinalIgnoreCase))
        {
            var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (p is not null) p.SearchRoi = roi;
            return;
        }

        if (string.Equals(kind, "T", StringComparison.OrdinalIgnoreCase))
        {
            var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (p is not null)
            {
                p.TemplateRoi = roi;
                p.WorldPosition = RoiCenterToWorld(roi);
                TrySaveTemplateImage(name, roi, isOrigin: false, pointName: name);
            }
            return;
        }

        if (string.Equals(kind, "L", StringComparison.OrdinalIgnoreCase))
        {
            var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (l is not null) l.SearchRoi = roi;
            return;
        }
    }

    private void OnRoiEdited(RoiSelection? sel)
    {
        if (sel is null || _config is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sel.Label))
        {
            return;
        }

        var label = sel.Label.Trim();
        var roi = sel.Roi;

        if (string.Equals(label, "DefectROI", StringComparison.OrdinalIgnoreCase))
        {
            _config.DefectConfig.InspectRoi = roi;
            RefreshPreviews();
            RequestAutoSave();
            return;
        }

        if (string.Equals(label, "Origin S", StringComparison.OrdinalIgnoreCase))
        {
            _config.Origin.SearchRoi = roi;
            if (_config.Origin.TemplateRoi.Width <= 0 || _config.Origin.TemplateRoi.Height <= 0)
            {
                _config.Origin.TemplateRoi = roi;
                _config.Origin.WorldPosition = RoiCenterToWorld(roi);
            }
            RefreshPreviews();
            RequestAutoSave();
            return;
        }

        if (string.Equals(label, "Origin T", StringComparison.OrdinalIgnoreCase))
        {
            _config.Origin.TemplateRoi = roi;
            _config.Origin.WorldPosition = RoiCenterToWorld(roi);
            TrySaveTemplateImage("origin", roi, isOrigin: true, pointName: null);
            RefreshPreviews();
            RequestAutoSave();
            return;
        }

        var parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            var name = parts[0];
            var kind = parts[1];
            if (string.Equals(kind, "S", StringComparison.OrdinalIgnoreCase))
            {
                var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p is not null)
                {
                    p.SearchRoi = roi;
                    if (p.TemplateRoi.Width <= 0 || p.TemplateRoi.Height <= 0)
                    {
                        p.TemplateRoi = roi;
                        p.WorldPosition = RoiCenterToWorld(roi);
                    }
                    RefreshPreviews();
                    RequestAutoSave();
                    return;
                }
            }

            if (string.Equals(kind, "T", StringComparison.OrdinalIgnoreCase))
            {
                var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p is not null)
                {
                    p.TemplateRoi = roi;
                    p.WorldPosition = RoiCenterToWorld(roi);
                    TrySaveTemplateImage(name, roi, isOrigin: false, pointName: name);
                    RefreshPreviews();
                    RequestAutoSave();
                    return;
                }
            }

            if (string.Equals(kind, "L", StringComparison.OrdinalIgnoreCase))
            {
                var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (l is not null)
                {
                    l.SearchRoi = roi;
                    RefreshPreviews();
                    RequestAutoSave();
                    return;
                }
            }
        }

        RefreshPreviews();
        RaiseToolPropertyPanelsChanged();
        RequestAutoSave();
    }

    private void RequestAutoSave()
    {
        _autoSavePending = true;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void AutoSaveNow()
    {
        _autoSaveTimer.Stop();
        if (!_autoSavePending)
        {
            return;
        }

        _autoSavePending = false;
        if (_config is null)
        {
            return;
        }

        _config.ProductCode = ProductCode;
        _configService.SaveConfig(_config);
        EnsureTemplatePathsAbsolute(_config);
    }

    private void EnsureTemplatePathsAbsolute(VisionConfig config)
    {
        if (config is null)
        {
            return;
        }

        var templateDir = Path.Combine(Path.GetFullPath(_storeOptions.ConfigRootDirectory), config.ProductCode, "templates");
        void NormalizePoint(PointDefinition p)
        {
            if (string.IsNullOrWhiteSpace(p.TemplateImageFile)) return;
            if (!Path.IsPathRooted(p.TemplateImageFile))
            {
                p.TemplateImageFile = Path.GetFullPath(Path.Combine(templateDir, p.TemplateImageFile));
            }
        }

        NormalizePoint(config.Origin);
        foreach (var p in config.Points) NormalizePoint(p);
    }

    private void TrySaveTemplateImage(string name, Roi roi, bool isOrigin, string? pointName)
    {
        using var snap = _sharedImage.GetSnapshot();
        if (snap is null)
        {
            return;
        }

        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return;
        }

        var templateDir = Path.Combine(Path.GetFullPath(_storeOptions.ConfigRootDirectory), ProductCode, "templates");
        Directory.CreateDirectory(templateDir);

        var safeName = name.Trim();
        var fileName = $"{safeName}.png";
        var fullPath = Path.Combine(templateDir, fileName);

        var rect = new OpenCvSharp.Rect(roi.X, roi.Y, roi.Width, roi.Height);
        rect = rect.Intersect(new OpenCvSharp.Rect(0, 0, snap.Width, snap.Height));
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using var cropped = new OpenCvSharp.Mat(snap, rect);
        using var gray = cropped.Channels() == 1
            ? cropped.Clone()
            : cropped.CvtColor(OpenCvSharp.ColorConversionCodes.BGR2GRAY);
        OpenCvSharp.Cv2.ImWrite(fullPath, gray);

        if (_config is null)
        {
            return;
        }

        if (isOrigin)
        {
            _config.Origin.TemplateImageFile = fileName;
        }
        else if (!string.IsNullOrWhiteSpace(pointName))
        {
            var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, pointName, StringComparison.OrdinalIgnoreCase));
            if (p is not null)
            {
                p.TemplateImageFile = fileName;
            }
        }
    }

    public ObservableCollection<string> AvailableConfigs { get; }

    [ObservableProperty]
    private string? _selectedConfig;

    partial void OnSelectedConfigChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ProductCode = value;
        }

        RefreshPreviews();
    }

    [ObservableProperty]
    private string _productCode = "ProductA";

    public ObservableCollection<string> ToolboxItems { get; }

    public ObservableCollection<ToolGraphNodeViewModel> Nodes { get; }

    public ObservableCollection<ToolGraphEdgeViewModel> Edges { get; }

    [ObservableProperty]
    private ToolGraphNodeViewModel? _selectedNode;

    partial void OnSelectedNodeChanged(ToolGraphNodeViewModel? value)
    {
        if (_selectedNodeHook is not null)
        {
            _selectedNodeHook.PropertyChanged -= SelectedNode_PropertyChanged;
        }

        _selectedNodeHook = value;
        if (_selectedNodeHook is not null)
        {
            _selectedNodeHook.PropertyChanged += SelectedNode_PropertyChanged;
        }

        _selectedNodePrevRefName = value?.RefName;

        RaiseToolPropertyPanelsChanged();
        RefreshPreviews();
    }

    [ObservableProperty]
    private ImageSource? _selectedNodePreviewImage;

    [ObservableProperty]
    private ImageSource? _finalPreviewImage;

    [ObservableProperty]
    private ImageSource? _linePreviewImage;

    public ObservableCollection<OverlayItem> SelectedNodeOverlayItems { get; }

    public ObservableCollection<OverlayItem> FinalOverlayItems { get; }

    public ICommand RefreshConfigsCommand { get; }

    public ICommand LoadConfigCommand { get; }

    public ICommand SaveConfigCommand { get; }

    public ICommand NewGraphCommand { get; }

    public ICommand DeleteSelectedNodeCommand { get; }

    public ICommand LoadPreviewImageCommand { get; }

    public ICommand RunFlowCommand { get; }

    public ICommand RoiSelectedCommand { get; }

    public ICommand RoiEditedCommand { get; }

    private VisionConfig? _config;

    private InspectionResult? _lastRun;

    [ObservableProperty]
    private bool _linePreviewEnabled = true;

    [ObservableProperty]
    private bool _preprocessPreviewEnabled = true;

    partial void OnLinePreviewEnabledChanged(bool value)
    {
        RefreshPreviews();
        RaiseToolPropertyPanelsChanged();
    }

    partial void OnPreprocessPreviewEnabledChanged(bool value)
    {
        RefreshPreviews();
        RaiseToolPropertyPanelsChanged();
    }

    private void RaiseToolPropertyPanelsChanged()
    {
        OnPropertyChanged(nameof(IsLineNode));
        OnPropertyChanged(nameof(IsDistanceNode));
        OnPropertyChanged(nameof(IsLineLineDistanceNode));
        OnPropertyChanged(nameof(IsPointLineDistanceNode));
        OnPropertyChanged(nameof(IsAnyDistanceNode));

        OnPropertyChanged(nameof(AvailablePointNames));
        OnPropertyChanged(nameof(AvailableLineNames));
        OnPropertyChanged(nameof(Distance_PointA));
        OnPropertyChanged(nameof(Distance_PointB));
        OnPropertyChanged(nameof(LineLineDistance_LineA));
        OnPropertyChanged(nameof(LineLineDistance_LineB));
        OnPropertyChanged(nameof(PointLineDistance_Point));
        OnPropertyChanged(nameof(PointLineDistance_Line));

        OnPropertyChanged(nameof(AvailableLineLineDistanceModes));
        OnPropertyChanged(nameof(AvailablePointLineDistanceModes));
        OnPropertyChanged(nameof(LineLineDistance_Mode));
        OnPropertyChanged(nameof(PointLineDistance_Mode));

        OnPropertyChanged(nameof(UseGray));
        OnPropertyChanged(nameof(UseGaussianBlur));
        OnPropertyChanged(nameof(BlurKernel));
        OnPropertyChanged(nameof(UseThreshold));
        OnPropertyChanged(nameof(ThresholdValue));
        OnPropertyChanged(nameof(UseCanny));
        OnPropertyChanged(nameof(Canny1));
        OnPropertyChanged(nameof(Canny2));
        OnPropertyChanged(nameof(UseMorphology));

        OnPropertyChanged(nameof(Line_Canny1));
        OnPropertyChanged(nameof(Line_Canny2));
        OnPropertyChanged(nameof(Line_HoughThreshold));
        OnPropertyChanged(nameof(Line_MinLineLength));
        OnPropertyChanged(nameof(Line_MaxLineGap));

        OnPropertyChanged(nameof(Distance_Nominal));
        OnPropertyChanged(nameof(Distance_TolPlus));
        OnPropertyChanged(nameof(Distance_TolMinus));
        OnPropertyChanged(nameof(SelectedRunValue));
        OnPropertyChanged(nameof(SelectedRunPass));
    }

    public bool IsLineNode => string.Equals(SelectedNode?.Type, "Line", StringComparison.OrdinalIgnoreCase);

    public bool IsDistanceNode => string.Equals(SelectedNode?.Type, "Distance", StringComparison.OrdinalIgnoreCase);

    public bool IsLineLineDistanceNode => string.Equals(SelectedNode?.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase);

    public bool IsPointLineDistanceNode => string.Equals(SelectedNode?.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase);

    public bool IsAnyDistanceNode => IsDistanceNode || IsLineLineDistanceNode || IsPointLineDistanceNode;

    public ObservableCollection<string> AvailablePointNames
    {
        get
        {
            var list = new ObservableCollection<string>();
            if (_config is null) return list;
            foreach (var p in _config.Points.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                list.Add(p);
            }
            return list;
        }
    }

    public ObservableCollection<string> AvailableLineNames
    {
        get
        {
            var list = new ObservableCollection<string>();
            if (_config is null) return list;
            foreach (var l in _config.Lines.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                list.Add(l);
            }
            return list;
        }
    }

    public string? Distance_PointA
    {
        get => SelectedDistanceDef()?.PointA;
        set
        {
            var def = SelectedDistanceDef();
            if (def is null) return;
            if (string.Equals(def.PointA, value, StringComparison.OrdinalIgnoreCase)) return;
            def.PointA = value ?? string.Empty;
            SyncInputEdgeForDistancePort("A", value);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public string? Distance_PointB
    {
        get => SelectedDistanceDef()?.PointB;
        set
        {
            var def = SelectedDistanceDef();
            if (def is null) return;
            if (string.Equals(def.PointB, value, StringComparison.OrdinalIgnoreCase)) return;
            def.PointB = value ?? string.Empty;
            SyncInputEdgeForDistancePort("B", value);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public string? LineLineDistance_LineA
    {
        get => SelectedLineLineDistanceDef()?.LineA;
        set
        {
            var def = SelectedLineLineDistanceDef();
            if (def is null) return;
            if (string.Equals(def.LineA, value, StringComparison.OrdinalIgnoreCase)) return;
            def.LineA = value ?? string.Empty;
            SyncInputEdgeForLineLineDistancePort("A", value);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public string? LineLineDistance_LineB
    {
        get => SelectedLineLineDistanceDef()?.LineB;
        set
        {
            var def = SelectedLineLineDistanceDef();
            if (def is null) return;
            if (string.Equals(def.LineB, value, StringComparison.OrdinalIgnoreCase)) return;
            def.LineB = value ?? string.Empty;
            SyncInputEdgeForLineLineDistancePort("B", value);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public string? PointLineDistance_Point
    {
        get => SelectedPointLineDistanceDef()?.Point;
        set
        {
            var def = SelectedPointLineDistanceDef();
            if (def is null) return;
            if (string.Equals(def.Point, value, StringComparison.OrdinalIgnoreCase)) return;
            def.Point = value ?? string.Empty;
            SyncInputEdgeForPointLineDistancePort("P", value);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public string? PointLineDistance_Line
    {
        get => SelectedPointLineDistanceDef()?.Line;
        set
        {
            var def = SelectedPointLineDistanceDef();
            if (def is null) return;
            if (string.Equals(def.Line, value, StringComparison.OrdinalIgnoreCase)) return;
            def.Line = value ?? string.Empty;
            SyncInputEdgeForPointLineDistancePort("L", value);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public LineLineDistanceMode LineLineDistance_Mode
    {
        get => SelectedLineLineDistanceDef()?.Mode ?? LineLineDistanceMode.ClosestPointsOnSegments;
        set
        {
            var def = SelectedLineLineDistanceDef();
            if (def is null) return;
            if (def.Mode == value) return;
            def.Mode = value;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public PointLineDistanceMode PointLineDistance_Mode
    {
        get => SelectedPointLineDistanceDef()?.Mode ?? PointLineDistanceMode.PointToSegment;
        set
        {
            var def = SelectedPointLineDistanceDef();
            if (def is null) return;
            if (def.Mode == value) return;
            def.Mode = value;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    private void SyncInputEdgeForDistancePort(string port, string? pointName)
    {
        if (_syncingInputs) return;
        if (_config is null || SelectedNode is null) return;
        if (!string.Equals(SelectedNode.Type, "Distance", StringComparison.OrdinalIgnoreCase)) return;

        _syncingInputs = true;
        try
        {
            RemoveEdgesToSelectedNodePort(port);
            if (!string.IsNullOrWhiteSpace(pointName))
            {
                var from = Nodes.FirstOrDefault(n => string.Equals(n.Type, "Point", StringComparison.OrdinalIgnoreCase)
                                                     && string.Equals(n.RefName, pointName, StringComparison.OrdinalIgnoreCase));
                if (from is not null)
                {
                    CreateEdge(from, SelectedNode, "Out", port);
                }
            }
        }
        finally
        {
            _syncingInputs = false;
        }
    }

    private void SyncInputEdgeForLineLineDistancePort(string port, string? lineName)
    {
        if (_syncingInputs) return;
        if (_config is null || SelectedNode is null) return;
        if (!string.Equals(SelectedNode.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase)) return;

        _syncingInputs = true;
        try
        {
            RemoveEdgesToSelectedNodePort(port);
            if (!string.IsNullOrWhiteSpace(lineName))
            {
                var from = Nodes.FirstOrDefault(n => string.Equals(n.Type, "Line", StringComparison.OrdinalIgnoreCase)
                                                     && string.Equals(n.RefName, lineName, StringComparison.OrdinalIgnoreCase));
                if (from is not null)
                {
                    CreateEdge(from, SelectedNode, "Out", port);
                }
            }
        }
        finally
        {
            _syncingInputs = false;
        }
    }

    private void SyncInputEdgeForPointLineDistancePort(string port, string? refName)
    {
        if (_syncingInputs) return;
        if (_config is null || SelectedNode is null) return;
        if (!string.Equals(SelectedNode.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase)) return;

        _syncingInputs = true;
        try
        {
            RemoveEdgesToSelectedNodePort(port);
            if (!string.IsNullOrWhiteSpace(refName))
            {
                var expectedType = string.Equals(port, "P", StringComparison.OrdinalIgnoreCase) ? "Point" : "Line";
                var from = Nodes.FirstOrDefault(n => string.Equals(n.Type, expectedType, StringComparison.OrdinalIgnoreCase)
                                                     && string.Equals(n.RefName, refName, StringComparison.OrdinalIgnoreCase));
                if (from is not null)
                {
                    CreateEdge(from, SelectedNode, "Out", port);
                }
            }
        }
        finally
        {
            _syncingInputs = false;
        }
    }

    private void RemoveEdgesToSelectedNodePort(string toPort)
    {
        if (SelectedNode is null) return;

        for (int i = Edges.Count - 1; i >= 0; i--)
        {
            var e = Edges[i];
            if (string.Equals(e.ToNodeId, SelectedNode.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.ToPort, toPort, StringComparison.OrdinalIgnoreCase))
            {
                Edges.RemoveAt(i);
            }
        }
    }

    public bool UseGray
    {
        get => _config?.Preprocess.UseGray ?? true;
        set
        {
            if (_config is null) return;
            if (_config.Preprocess.UseGray == value) return;
            _config.Preprocess.UseGray = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public bool UseGaussianBlur
    {
        get => _config?.Preprocess.UseGaussianBlur ?? false;
        set
        {
            if (_config is null) return;
            if (_config.Preprocess.UseGaussianBlur == value) return;
            _config.Preprocess.UseGaussianBlur = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public int BlurKernel
    {
        get => _config?.Preprocess.BlurKernel ?? 3;
        set
        {
            if (_config is null) return;
            if (_config.Preprocess.BlurKernel == value) return;
            _config.Preprocess.BlurKernel = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public bool UseThreshold
    {
        get => _config?.Preprocess.UseThreshold ?? false;
        set
        {
            if (_config is null) return;
            if (_config.Preprocess.UseThreshold == value) return;
            _config.Preprocess.UseThreshold = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public int ThresholdValue
    {
        get => _config?.Preprocess.ThresholdValue ?? 128;
        set
        {
            if (_config is null) return;
            if (_config.Preprocess.ThresholdValue == value) return;
            _config.Preprocess.ThresholdValue = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public bool UseCanny
    {
        get => _config?.Preprocess.UseCanny ?? false;
        set
        {
            if (_config is null) return;
            if (_config.Preprocess.UseCanny == value) return;
            _config.Preprocess.UseCanny = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public int Canny1
    {
        get => _config?.Preprocess.Canny1 ?? 50;
        set
        {
            if (_config is null) return;
            if (_config.Preprocess.Canny1 == value) return;
            _config.Preprocess.Canny1 = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public int Canny2
    {
        get => _config?.Preprocess.Canny2 ?? 150;
        set
        {
            if (_config is null) return;
            if (_config.Preprocess.Canny2 == value) return;
            _config.Preprocess.Canny2 = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public bool UseMorphology
    {
        get => _config?.Preprocess.UseMorphology ?? false;
        set
        {
            if (_config is null) return;
            if (_config.Preprocess.UseMorphology == value) return;
            _config.Preprocess.UseMorphology = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    private LineToolDefinition? SelectedLineDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.Lines.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    public int Line_Canny1
    {
        get => SelectedLineDef()?.Canny1 ?? 0;
        set
        {
            var l = SelectedLineDef();
            if (l is null) return;
            if (l.Canny1 == value) return;
            l.Canny1 = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public int Line_Canny2
    {
        get => SelectedLineDef()?.Canny2 ?? 0;
        set
        {
            var l = SelectedLineDef();
            if (l is null) return;
            if (l.Canny2 == value) return;
            l.Canny2 = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public int Line_HoughThreshold
    {
        get => SelectedLineDef()?.HoughThreshold ?? 0;
        set
        {
            var l = SelectedLineDef();
            if (l is null) return;
            if (l.HoughThreshold == value) return;
            l.HoughThreshold = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public int Line_MinLineLength
    {
        get => SelectedLineDef()?.MinLineLength ?? 0;
        set
        {
            var l = SelectedLineDef();
            if (l is null) return;
            if (l.MinLineLength == value) return;
            l.MinLineLength = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public int Line_MaxLineGap
    {
        get => SelectedLineDef()?.MaxLineGap ?? 0;
        set
        {
            var l = SelectedLineDef();
            if (l is null) return;
            if (l.MaxLineGap == value) return;
            l.MaxLineGap = value;
            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    private LineDistance? SelectedDistanceDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "Distance", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.Distances.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    private LineToLineDistance? SelectedLineLineDistanceDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    private PointToLineDistance? SelectedPointLineDistanceDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    public double Distance_Nominal
    {
        get
        {
            if (SelectedDistanceDef() is { } d) return d.Nominal;
            if (SelectedLineLineDistanceDef() is { } ll) return ll.Nominal;
            if (SelectedPointLineDistanceDef() is { } pl) return pl.Nominal;
            return 0.0;
        }
        set
        {
            if (SelectedDistanceDef() is { } d)
            {
                if (Math.Abs(d.Nominal - value) < 0.0000001) return;
                d.Nominal = value;
            }
            else if (SelectedLineLineDistanceDef() is { } ll)
            {
                if (Math.Abs(ll.Nominal - value) < 0.0000001) return;
                ll.Nominal = value;
            }
            else if (SelectedPointLineDistanceDef() is { } pl)
            {
                if (Math.Abs(pl.Nominal - value) < 0.0000001) return;
                pl.Nominal = value;
            }
            else
            {
                return;
            }

            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public double Distance_TolPlus
    {
        get
        {
            if (SelectedDistanceDef() is { } d) return d.TolerancePlus;
            if (SelectedLineLineDistanceDef() is { } ll) return ll.TolerancePlus;
            if (SelectedPointLineDistanceDef() is { } pl) return pl.TolerancePlus;
            return 0.0;
        }
        set
        {
            if (SelectedDistanceDef() is { } d)
            {
                if (Math.Abs(d.TolerancePlus - value) < 0.0000001) return;
                d.TolerancePlus = value;
            }
            else if (SelectedLineLineDistanceDef() is { } ll)
            {
                if (Math.Abs(ll.TolerancePlus - value) < 0.0000001) return;
                ll.TolerancePlus = value;
            }
            else if (SelectedPointLineDistanceDef() is { } pl)
            {
                if (Math.Abs(pl.TolerancePlus - value) < 0.0000001) return;
                pl.TolerancePlus = value;
            }
            else
            {
                return;
            }

            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public double Distance_TolMinus
    {
        get
        {
            if (SelectedDistanceDef() is { } d) return d.ToleranceMinus;
            if (SelectedLineLineDistanceDef() is { } ll) return ll.ToleranceMinus;
            if (SelectedPointLineDistanceDef() is { } pl) return pl.ToleranceMinus;
            return 0.0;
        }
        set
        {
            if (SelectedDistanceDef() is { } d)
            {
                if (Math.Abs(d.ToleranceMinus - value) < 0.0000001) return;
                d.ToleranceMinus = value;
            }
            else if (SelectedLineLineDistanceDef() is { } ll)
            {
                if (Math.Abs(ll.ToleranceMinus - value) < 0.0000001) return;
                ll.ToleranceMinus = value;
            }
            else if (SelectedPointLineDistanceDef() is { } pl)
            {
                if (Math.Abs(pl.ToleranceMinus - value) < 0.0000001) return;
                pl.ToleranceMinus = value;
            }
            else
            {
                return;
            }

            RefreshPreviews();
            OnPropertyChanged();
        }
    }

    public double? SelectedRunValue
    {
        get
        {
            if (_lastRun is null || SelectedNode is null) return null;
            if (string.Equals(SelectedNode.Type, "Distance", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.Distances.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Value;
            }

            if (string.Equals(SelectedNode.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Value;
            }

            if (string.Equals(SelectedNode.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Value;
            }

            return null;
        }
    }

    public bool? SelectedRunPass
    {
        get
        {
            if (_lastRun is null || SelectedNode is null) return null;
            if (string.Equals(SelectedNode.Type, "Distance", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.Distances.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Pass;
            }

            if (string.Equals(SelectedNode.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Pass;
            }

            if (string.Equals(SelectedNode.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Pass;
            }

            return null;
        }
    }

    private void SelectedNode_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ToolGraphNodeViewModel.RefName) or nameof(ToolGraphNodeViewModel.Type))
        {
            if (e.PropertyName is nameof(ToolGraphNodeViewModel.RefName))
            {
                RenameSelectedDefinitionIfNeeded();
            }

            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
        }
    }

    private void RenameSelectedDefinitionIfNeeded()
    {
        if (_config is null || SelectedNode is null)
        {
            _selectedNodePrevRefName = SelectedNode?.RefName;
            return;
        }

        var oldName = _selectedNodePrevRefName;
        var newName = SelectedNode.RefName;
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            _selectedNodePrevRefName = newName;
            return;
        }

        if (string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.Points.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
        }
        else if (string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
        }
        else if (string.Equals(SelectedNode.Type, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.Distances.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
        }
        else if (string.Equals(SelectedNode.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
        }
        else if (string.Equals(SelectedNode.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
        }
        else if (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase))
        {
            _config.Origin.Name = "Origin";
        }

        _selectedNodePrevRefName = newName;
    }

    private void LoadPreviewImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Image Files|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|All Files|*.*"
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        using var mat = Cv2.ImRead(dlg.FileName, ImreadModes.Color);
        _sharedImage.SetImage(mat);
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

    private void LoadConfig()
    {
        var code = SelectedConfig ?? ProductCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        _config = _configService.LoadConfig(code);
        ProductCode = _config.ProductCode;

        Nodes.Clear();
        Edges.Clear();
        foreach (var n in _config.ToolGraph.Nodes)
        {
            var vm = new ToolGraphNodeViewModel
            {
                Id = n.Id,
                Type = n.Type,
                RefName = n.RefName,
                X = n.X,
                Y = n.Y
            };
            vm.PropertyChanged += Node_PropertyChanged;
            Nodes.Add(vm);
        }

        foreach (var e in _config.ToolGraph.Edges)
        {
            var from = Nodes.FirstOrDefault(x => string.Equals(x.Id, e.FromNodeId, StringComparison.OrdinalIgnoreCase));
            var to = Nodes.FirstOrDefault(x => string.Equals(x.Id, e.ToNodeId, StringComparison.OrdinalIgnoreCase));
            if (from is null || to is null)
            {
                continue;
            }

            Edges.Add(new ToolGraphEdgeViewModel(from, to, e.FromPort, e.ToPort));
        }

        SelectedNode = Nodes.Count > 0 ? Nodes[0] : null;
        RaiseToolPropertyPanelsChanged();
        RefreshPreviews();
    }

    private void SaveConfig()
    {
        var code = SelectedConfig ?? ProductCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        // Brand-new products may not have a json yet.
        _config ??= new VisionConfig { ProductCode = code };
        _config.ProductCode = ProductCode;

        _config.ToolGraph.Nodes.Clear();
        foreach (var n in Nodes)
        {
            _config.ToolGraph.Nodes.Add(new ToolGraphNode
            {
                Id = n.Id,
                Type = n.Type,
                RefName = n.RefName,
                X = n.X,
                Y = n.Y
            });
        }

        _config.ToolGraph.Edges.Clear();
        foreach (var e in Edges)
        {
            _config.ToolGraph.Edges.Add(new ToolGraphEdge
            {
                FromNodeId = e.FromNodeId,
                ToNodeId = e.ToNodeId,
                FromPort = e.FromPort,
                ToPort = e.ToPort
            });
        }

        _configService.SaveConfig(_config);
        RefreshPreviews();
    }

    private void RunFlow()
    {
        using var snap = _sharedImage.GetSnapshot();
        if (snap is null || _config is null)
        {
            _lastRun = null;
            RefreshPreviews();
            return;
        }

        EnsureTemplatePathsAbsolute(_config);

        _lastRun = _inspectionService.Inspect(snap, _config);

        RefreshPreviews();
        RaiseToolPropertyPanelsChanged();
    }

    private void NewGraph()
    {
        foreach (var n in Nodes)
        {
            n.PropertyChanged -= Node_PropertyChanged;
        }

        Nodes.Clear();
        Edges.Clear();
        SelectedNode = null;
        SelectedNodePreviewImage = null;
        FinalPreviewImage = null;
        SelectedNodeOverlayItems.Clear();
        FinalOverlayItems.Clear();

        _config ??= new VisionConfig { ProductCode = ProductCode };
        _config.ToolGraph = new ToolGraph();
    }

    private void DeleteSelectedNode()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var toRemove = SelectedNode;
        var idx = Nodes.IndexOf(toRemove);
        if (idx < 0)
        {
            return;
        }

        Nodes.RemoveAt(idx);
        toRemove.PropertyChanged -= Node_PropertyChanged;

        for (var i = Edges.Count - 1; i >= 0; i--)
        {
            var e = Edges[i];
            if (string.Equals(e.FromNodeId, toRemove.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.ToNodeId, toRemove.Id, StringComparison.OrdinalIgnoreCase))
            {
                Edges.RemoveAt(i);
            }
        }

        SelectedNode = Nodes.Count > 0 ? Nodes[Math.Clamp(idx, 0, Nodes.Count - 1)] : null;
        RefreshPreviews();
    }

    public void AddNode(string type, System.Windows.Point canvasPosition)
    {
        _config ??= new VisionConfig { ProductCode = ProductCode };
        _config.ToolGraph ??= new ToolGraph();

        var node = new ToolGraphNodeViewModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            RefName = string.Empty,
            X = canvasPosition.X,
            Y = canvasPosition.Y
        };

        node.PropertyChanged += Node_PropertyChanged;

        Nodes.Add(node);

        // Ensure there is a backing definition in config so ROI overlays exist and can be taught immediately.
        EnsureDefinitionForNewNode(node);

        SelectedNode = node;
        RaiseToolPropertyPanelsChanged();
        RefreshPreviews();
    }

    private void EnsureDefinitionForNewNode(ToolGraphNodeViewModel node)
    {
        if (_config is null)
        {
            return;
        }

        using var snap = _sharedImage.GetSnapshot();
        var imgW = snap?.Width ?? 0;
        var imgH = snap?.Height ?? 0;

        Roi DefaultRoi()
        {
            if (imgW <= 0 || imgH <= 0)
            {
                return new Roi { X = 10, Y = 10, Width = 120, Height = 120 };
            }

            var w = Math.Clamp(imgW / 4, 60, Math.Max(60, imgW));
            var h = Math.Clamp(imgH / 4, 60, Math.Max(60, imgH));
            var x = Math.Clamp((imgW - w) / 2, 0, Math.Max(0, imgW - w));
            var y = Math.Clamp((imgH - h) / 2, 0, Math.Max(0, imgH - h));
            return new Roi { X = x, Y = y, Width = w, Height = h };
        }

        if (string.IsNullOrWhiteSpace(node.RefName))
        {
            node.RefName = GenerateDefaultRefName(node.Type);
        }

        if (string.Equals(node.Type, "Origin", StringComparison.OrdinalIgnoreCase))
        {
            _config.Origin.Name = "Origin";
            if (_config.Origin.SearchRoi.Width <= 0 || _config.Origin.SearchRoi.Height <= 0)
            {
                _config.Origin.SearchRoi = DefaultRoi();
            }

            if (_config.Origin.TemplateRoi.Width <= 0 || _config.Origin.TemplateRoi.Height <= 0)
            {
                _config.Origin.TemplateRoi = DefaultRoi();
            }
            return;
        }

        if (string.Equals(node.Type, "Point", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.Points.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                var def = new PointDefinition { Name = node.RefName };
                def.SearchRoi = DefaultRoi();
                def.TemplateRoi = DefaultRoi();
                _config.Points.Add(def);
            }
            return;
        }

        if (string.Equals(node.Type, "Line", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.Lines.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                var def = new LineToolDefinition { Name = node.RefName };
                def.SearchRoi = DefaultRoi();
                _config.Lines.Add(def);
            }
            return;
        }

        if (string.Equals(node.Type, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.Distances.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                _config.Distances.Add(new LineDistance { Name = node.RefName });
            }
            return;
        }

        if (string.Equals(node.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.LineToLineDistances.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                _config.LineToLineDistances.Add(new LineToLineDistance { Name = node.RefName });
            }
            return;
        }

        if (string.Equals(node.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.PointToLineDistances.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                _config.PointToLineDistances.Add(new PointToLineDistance { Name = node.RefName });
            }
            return;
        }

        if (string.Equals(node.Type, "DefectRoi", StringComparison.OrdinalIgnoreCase))
        {
            // Defect config already exists; ROI can be taught to DefectROI label.
            return;
        }
    }

    private string GenerateDefaultRefName(string type)
    {
        if (_config is null)
        {
            return type;
        }

        string baseName;
        Func<string, bool> exists;

        if (string.Equals(type, "Point", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "P";
            exists = n => _config.Points.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "Line", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "L";
            exists = n => _config.Lines.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "D";
            exists = n => _config.Distances.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "LLD";
            exists = n => _config.LineToLineDistances.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "PLD";
            exists = n => _config.PointToLineDistances.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "DefectRoi", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "Defect";
            exists = _ => false;
        }
        else
        {
            baseName = type;
            exists = _ => false;
        }

        for (var i = 1; i < 10_000; i++)
        {
            var name = $"{baseName}{i}";
            if (!exists(name))
            {
                return name;
            }
        }

        return $"{baseName}{Guid.NewGuid().ToString("N").Substring(0, 6)}";
    }

    public void CreateEdge(ToolGraphNodeViewModel fromNode, ToolGraphNodeViewModel toNode, string fromPort = "Out", string toPort = "In")
    {
        if (fromNode is null || toNode is null)
        {
            return;
        }

        if (ReferenceEquals(fromNode, toNode))
        {
            return;
        }

        var existed = Edges.Any(x => string.Equals(x.FromNodeId, fromNode.Id, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(x.ToNodeId, toNode.Id, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(x.FromPort, fromPort, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(x.ToPort, toPort, StringComparison.OrdinalIgnoreCase));
        if (existed)
        {
            return;
        }

        Edges.Add(new ToolGraphEdgeViewModel(fromNode, toNode, fromPort, toPort));

        // Auto-fill tool inputs based on graph wiring (VisionPro-like).
        if (_config is null)
        {
            return;
        }

        if (string.Equals(toNode.Type, "Distance", StringComparison.OrdinalIgnoreCase)
            && string.Equals(fromNode.Type, "Point", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.Distances.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (def is not null)
            {
                if (string.Equals(toPort, "A", StringComparison.OrdinalIgnoreCase)) def.PointA = fromNode.RefName;
                else if (string.Equals(toPort, "B", StringComparison.OrdinalIgnoreCase)) def.PointB = fromNode.RefName;
            }
        }
        else if (string.Equals(toNode.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase)
                 && string.Equals(fromNode.Type, "Line", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (def is not null)
            {
                if (string.Equals(toPort, "A", StringComparison.OrdinalIgnoreCase)) def.LineA = fromNode.RefName;
                else if (string.Equals(toPort, "B", StringComparison.OrdinalIgnoreCase)) def.LineB = fromNode.RefName;
            }
        }
        else if (string.Equals(toNode.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (def is not null)
            {
                if (string.Equals(toPort, "P", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(fromNode.Type, "Point", StringComparison.OrdinalIgnoreCase))
                {
                    def.Point = fromNode.RefName;
                }
                else if (string.Equals(toPort, "L", StringComparison.OrdinalIgnoreCase)
                         && string.Equals(fromNode.Type, "Line", StringComparison.OrdinalIgnoreCase))
                {
                    def.Line = fromNode.RefName;
                }
            }
        }

        RaiseToolPropertyPanelsChanged();
        RefreshPreviews();
    }

    private void Node_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ToolGraphNodeViewModel.X) or nameof(ToolGraphNodeViewModel.Y)))
        {
            return;
        }

        if (sender is not ToolGraphNodeViewModel n)
        {
            return;
        }

        foreach (var edge in Edges)
        {
            if (string.Equals(edge.FromNodeId, n.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(edge.ToNodeId, n.Id, StringComparison.OrdinalIgnoreCase))
            {
                edge.NotifyGeometryChanged();
            }
        }
    }

    private void RefreshPreviews()
    {
        SelectedNodeOverlayItems.Clear();
        FinalOverlayItems.Clear();

        using var snap = _sharedImage.GetSnapshot();
        if (snap is null)
        {
            SelectedNodePreviewImage = null;
            FinalPreviewImage = null;
            LinePreviewImage = null;
            return;
        }

        if (_config is not null && PreprocessPreviewEnabled)
        {
            using var processed = _preprocessor.Run(snap, _config.Preprocess);
            SelectedNodePreviewImage = processed.ToBitmapSource();
            FinalPreviewImage = processed.ToBitmapSource();
        }
        else
        {
            SelectedNodePreviewImage = snap.ToBitmapSource();
            FinalPreviewImage = snap.ToBitmapSource();
        }

        if (_config is null)
        {
            LinePreviewImage = null;
            return;
        }

        RefreshLineRoiPreview(snap);

        // If user ran the flow, prefer showing overlays from the inspection result
        if (_lastRun is not null)
        {
            AddConfigRois(FinalOverlayItems);
            BuildFinalOverlayFromRun(_lastRun, FinalOverlayItems);
            if (SelectedNode is not null)
            {
                AddConfigRoisForNode(SelectedNode, SelectedNodeOverlayItems);
                BuildOverlayForNodeFromRun(SelectedNode, _lastRun, SelectedNodeOverlayItems);
            }
        }
        else
        {
            if (SelectedNode is not null)
            {
                BuildOverlayForNode(SelectedNode, snap, SelectedNodeOverlayItems);
            }

            BuildFinalOverlay(snap, FinalOverlayItems);
        }
    }

    private void AddConfigRois(ObservableCollection<OverlayItem> dst)
    {
        if (_config is null)
        {
            return;
        }

        if (_config.Origin.SearchRoi.Width > 0 && _config.Origin.SearchRoi.Height > 0)
        {
            dst.Add(new OverlayRectItem
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
            dst.Add(new OverlayRectItem
            {
                X = _config.Origin.TemplateRoi.X,
                Y = _config.Origin.TemplateRoi.Y,
                Width = _config.Origin.TemplateRoi.Width,
                Height = _config.Origin.TemplateRoi.Height,
                Stroke = Brushes.Lime,
                Label = "Origin T"
            });
        }

        foreach (var p in _config.Points)
        {
            if (p.SearchRoi.Width <= 0 || p.SearchRoi.Height <= 0)
            {
                continue;
            }

            dst.Add(new OverlayRectItem
            {
                X = p.SearchRoi.X,
                Y = p.SearchRoi.Y,
                Width = p.SearchRoi.Width,
                Height = p.SearchRoi.Height,
                Stroke = Brushes.DeepSkyBlue,
                Label = $"{p.Name} S"
            });

            if (p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = p.TemplateRoi.X,
                    Y = p.TemplateRoi.Y,
                    Width = p.TemplateRoi.Width,
                    Height = p.TemplateRoi.Height,
                    Stroke = Brushes.DeepSkyBlue,
                    Label = $"{p.Name} T"
                });
            }
        }

        foreach (var l in _config.Lines)
        {
            if (l.SearchRoi.Width <= 0 || l.SearchRoi.Height <= 0)
            {
                continue;
            }

            dst.Add(new OverlayRectItem
            {
                X = l.SearchRoi.X,
                Y = l.SearchRoi.Y,
                Width = l.SearchRoi.Width,
                Height = l.SearchRoi.Height,
                Stroke = Brushes.MediumPurple,
                Label = $"{l.Name} L"
            });
        }

        if (_config.DefectConfig.InspectRoi.Width > 0 && _config.DefectConfig.InspectRoi.Height > 0)
        {
            dst.Add(new OverlayRectItem
            {
                X = _config.DefectConfig.InspectRoi.X,
                Y = _config.DefectConfig.InspectRoi.Y,
                Width = _config.DefectConfig.InspectRoi.Width,
                Height = _config.DefectConfig.InspectRoi.Height,
                Stroke = Brushes.Orange,
                Label = "DefectROI"
            });
        }
    }

    private void AddConfigRoisForNode(ToolGraphNodeViewModel node, ObservableCollection<OverlayItem> dst)
    {
        if (_config is null)
        {
            return;
        }

        void AddPointRoi(string pointName)
        {
            var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, pointName, StringComparison.OrdinalIgnoreCase));
            if (p is null)
            {
                return;
            }

            if (p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
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
                dst.Add(new OverlayRectItem
                {
                    X = p.TemplateRoi.X,
                    Y = p.TemplateRoi.Y,
                    Width = p.TemplateRoi.Width,
                    Height = p.TemplateRoi.Height,
                    Stroke = Brushes.DeepSkyBlue,
                    Label = $"{p.Name} T"
                });
            }
        }

        void AddLineRoi(string lineName)
        {
            var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, lineName, StringComparison.OrdinalIgnoreCase));
            if (l is null)
            {
                return;
            }

            if (l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
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

        if (string.Equals(node.Type, "Origin", StringComparison.OrdinalIgnoreCase))
        {
            if (_config.Origin.SearchRoi.Width > 0 && _config.Origin.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
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
                dst.Add(new OverlayRectItem
                {
                    X = _config.Origin.TemplateRoi.X,
                    Y = _config.Origin.TemplateRoi.Y,
                    Width = _config.Origin.TemplateRoi.Width,
                    Height = _config.Origin.TemplateRoi.Height,
                    Stroke = Brushes.Lime,
                    Label = "Origin T"
                });
            }

            return;
        }

        if (string.Equals(node.Type, "Point", StringComparison.OrdinalIgnoreCase))
        {
            var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (p is null)
            {
                return;
            }

            AddPointRoi(p.Name);

            return;
        }

        if (string.Equals(node.Type, "Line", StringComparison.OrdinalIgnoreCase))
        {
            var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (l is null)
            {
                return;
            }

            AddLineRoi(l.Name);

            return;
        }

        if (string.Equals(node.Type, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            var d = _config.Distances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (d is null)
            {
                return;
            }

            AddPointRoi(d.PointA);
            AddPointRoi(d.PointB);
            return;
        }

        if (string.Equals(node.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var dd = _config.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (dd is null)
            {
                return;
            }

            AddLineRoi(dd.LineA);
            AddLineRoi(dd.LineB);
            return;
        }

        if (string.Equals(node.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var dd = _config.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (dd is null)
            {
                return;
            }

            AddPointRoi(dd.Point);
            AddLineRoi(dd.Line);
            return;
        }

        if (string.Equals(node.Type, "DefectRoi", StringComparison.OrdinalIgnoreCase))
        {
            if (_config.DefectConfig.InspectRoi.Width > 0 && _config.DefectConfig.InspectRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = _config.DefectConfig.InspectRoi.X,
                    Y = _config.DefectConfig.InspectRoi.Y,
                    Width = _config.DefectConfig.InspectRoi.Width,
                    Height = _config.DefectConfig.InspectRoi.Height,
                    Stroke = Brushes.Orange,
                    Label = "DefectROI"
                });
            }

            return;
        }
    }

    private static void BuildFinalOverlayFromRun(InspectionResult run, ObservableCollection<OverlayItem> dst)
    {
        if (run.Origin is not null)
        {
            dst.Add(new OverlayPointItem
            {
                X = run.Origin.Position.X,
                Y = run.Origin.Position.Y,
                Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red,
                Label = $"Origin: {run.Origin.Score:0.00}"
            });
        }

        foreach (var p in run.Points)
        {
            dst.Add(new OverlayPointItem
            {
                X = p.Position.X,
                Y = p.Position.Y,
                Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red,
                Label = p.Name
            });
        }

        var detectedPointMap = run.Points.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var l in run.Lines)
        {
            if (!l.Found)
            {
                continue;
            }

            dst.Add(new OverlayLineItem
            {
                X1 = l.P1.X,
                Y1 = l.P1.Y,
                X2 = l.P2.X,
                Y2 = l.P2.Y,
                Stroke = Brushes.MediumPurple,
                Label = l.Name
            });
        }

        foreach (var d in run.Distances)
        {
            if (!detectedPointMap.TryGetValue(d.PointA, out var pa) || !detectedPointMap.TryGetValue(d.PointB, out var pb))
            {
                continue;
            }

            dst.Add(new OverlayLineItem
            {
                X1 = pa.Position.X,
                Y1 = pa.Position.Y,
                X2 = pb.Position.X,
                Y2 = pb.Position.Y,
                Stroke = d.Pass ? Brushes.Lime : Brushes.Red,
                Label = $"{d.Name}: {d.Value:0.###}"
            });
        }

        foreach (var dd in run.LineToLineDistances)
        {
            dst.Add(new OverlayLineItem
            {
                X1 = dd.ClosestA.X,
                Y1 = dd.ClosestA.Y,
                X2 = dd.ClosestB.X,
                Y2 = dd.ClosestB.Y,
                Stroke = dd.Pass ? Brushes.Lime : Brushes.Red,
                Label = $"{dd.Name}: {dd.Value:0.00}"
            });
        }

        foreach (var dd in run.PointToLineDistances)
        {
            dst.Add(new OverlayLineItem
            {
                X1 = dd.ClosestA.X,
                Y1 = dd.ClosestA.Y,
                X2 = dd.ClosestB.X,
                Y2 = dd.ClosestB.Y,
                Stroke = dd.Pass ? Brushes.Lime : Brushes.Red,
                Label = $"{dd.Name}: {dd.Value:0.00}"
            });
        }
    }

    private static void BuildOverlayForNodeFromRun(ToolGraphNodeViewModel node, InspectionResult run, ObservableCollection<OverlayItem> dst)
    {
        if (string.Equals(node.Type, "Origin", StringComparison.OrdinalIgnoreCase))
        {
            if (run.Origin is null)
            {
                return;
            }

            dst.Add(new OverlayPointItem
            {
                X = run.Origin.Position.X,
                Y = run.Origin.Position.Y,
                Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red,
                Label = $"Origin: {run.Origin.Score:0.00}"
            });
            return;
        }

        if (string.Equals(node.Type, "Point", StringComparison.OrdinalIgnoreCase))
        {
            var p = run.Points.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (p is null)
            {
                return;
            }

            dst.Add(new OverlayPointItem
            {
                X = p.Position.X,
                Y = p.Position.Y,
                Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red,
                Label = p.Name
            });
            return;
        }

        if (string.Equals(node.Type, "Line", StringComparison.OrdinalIgnoreCase))
        {
            var l = run.Lines.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (l is null || !l.Found)
            {
                return;
            }

            dst.Add(new OverlayLineItem
            {
                X1 = l.P1.X,
                Y1 = l.P1.Y,
                X2 = l.P2.X,
                Y2 = l.P2.Y,
                Stroke = Brushes.MediumPurple,
                Label = l.Name
            });
            return;
        }

        if (string.Equals(node.Type, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            var d = run.Distances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (d is null)
            {
                return;
            }

            var pa = run.Points.FirstOrDefault(x => string.Equals(x.Name, d.PointA, StringComparison.OrdinalIgnoreCase));
            var pb = run.Points.FirstOrDefault(x => string.Equals(x.Name, d.PointB, StringComparison.OrdinalIgnoreCase));
            if (pa is null || pb is null)
            {
                return;
            }

            dst.Add(new OverlayPointItem
            {
                X = pa.Position.X,
                Y = pa.Position.Y,
                Stroke = pa.Pass ? Brushes.DeepSkyBlue : Brushes.Red,
                Label = pa.Name
            });

            dst.Add(new OverlayPointItem
            {
                X = pb.Position.X,
                Y = pb.Position.Y,
                Stroke = pb.Pass ? Brushes.DeepSkyBlue : Brushes.Red,
                Label = pb.Name
            });

            dst.Add(new OverlayLineItem
            {
                X1 = pa.Position.X,
                Y1 = pa.Position.Y,
                X2 = pb.Position.X,
                Y2 = pb.Position.Y,
                Stroke = d.Pass ? Brushes.Lime : Brushes.Red,
                Label = $"{d.Name}: {d.Value:0.###}"
            });

            return;
        }

        if (string.Equals(node.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var dd = run.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (dd is null)
            {
                return;
            }

            var la = run.Lines.FirstOrDefault(x => string.Equals(x.Name, dd.RefA, StringComparison.OrdinalIgnoreCase));
            var lb = run.Lines.FirstOrDefault(x => string.Equals(x.Name, dd.RefB, StringComparison.OrdinalIgnoreCase));
            if (la is null || lb is null || !la.Found || !lb.Found)
            {
                return;
            }

            dst.Add(new OverlayLineItem
            {
                X1 = la.P1.X,
                Y1 = la.P1.Y,
                X2 = la.P2.X,
                Y2 = la.P2.Y,
                Stroke = Brushes.MediumPurple,
                Label = la.Name
            });

            dst.Add(new OverlayLineItem
            {
                X1 = lb.P1.X,
                Y1 = lb.P1.Y,
                X2 = lb.P2.X,
                Y2 = lb.P2.Y,
                Stroke = Brushes.MediumPurple,
                Label = lb.Name
            });

            dst.Add(new OverlayLineItem
            {
                X1 = dd.ClosestA.X,
                Y1 = dd.ClosestA.Y,
                X2 = dd.ClosestB.X,
                Y2 = dd.ClosestB.Y,
                Stroke = dd.Pass ? Brushes.Lime : Brushes.Red,
                Label = $"{dd.Name}: {dd.Value:0.###}"
            });

            return;
        }

        if (string.Equals(node.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var dd = run.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (dd is null)
            {
                return;
            }

            var p = run.Points.FirstOrDefault(x => string.Equals(x.Name, dd.RefA, StringComparison.OrdinalIgnoreCase));
            var l = run.Lines.FirstOrDefault(x => string.Equals(x.Name, dd.RefB, StringComparison.OrdinalIgnoreCase));
            if (p is null || l is null || !l.Found)
            {
                return;
            }

            dst.Add(new OverlayPointItem
            {
                X = p.Position.X,
                Y = p.Position.Y,
                Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red,
                Label = p.Name
            });

            dst.Add(new OverlayLineItem
            {
                X1 = l.P1.X,
                Y1 = l.P1.Y,
                X2 = l.P2.X,
                Y2 = l.P2.Y,
                Stroke = Brushes.MediumPurple,
                Label = l.Name
            });

            dst.Add(new OverlayLineItem
            {
                X1 = dd.ClosestA.X,
                Y1 = dd.ClosestA.Y,
                X2 = dd.ClosestB.X,
                Y2 = dd.ClosestB.Y,
                Stroke = dd.Pass ? Brushes.Lime : Brushes.Red,
                Label = $"{dd.Name}: {dd.Value:0.###}"
            });

            return;
        }
    }

    private void BuildOverlayForNode(ToolGraphNodeViewModel node, Mat image, ObservableCollection<OverlayItem> dst)
    {
        if (_config is null)
        {
            return;
        }

        if (string.Equals(node.Type, "Origin", StringComparison.OrdinalIgnoreCase))
        {
            if (_config.Origin.SearchRoi.Width > 0 && _config.Origin.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
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
                dst.Add(new OverlayRectItem
                {
                    X = _config.Origin.TemplateRoi.X,
                    Y = _config.Origin.TemplateRoi.Y,
                    Width = _config.Origin.TemplateRoi.Width,
                    Height = _config.Origin.TemplateRoi.Height,
                    Stroke = Brushes.Gold,
                    Label = "Origin T"
                });
            }

            return;
        }

        if (string.Equals(node.Type, "Point", StringComparison.OrdinalIgnoreCase))
        {
            var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (p is null)
            {
                return;
            }

            if (p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
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
                dst.Add(new OverlayRectItem
                {
                    X = p.TemplateRoi.X,
                    Y = p.TemplateRoi.Y,
                    Width = p.TemplateRoi.Width,
                    Height = p.TemplateRoi.Height,
                    Stroke = Brushes.Gold,
                    Label = $"{p.Name} T"
                });
            }

            dst.Add(new OverlayPointItem
            {
                X = p.WorldPosition.X,
                Y = p.WorldPosition.Y,
                Stroke = Brushes.DeepSkyBlue,
                Label = p.Name
            });

            return;
        }

        if (string.Equals(node.Type, "Line", StringComparison.OrdinalIgnoreCase))
        {
            var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (l is null)
            {
                return;
            }

            if (l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = l.SearchRoi.X,
                    Y = l.SearchRoi.Y,
                    Width = l.SearchRoi.Width,
                    Height = l.SearchRoi.Height,
                    Stroke = Brushes.MediumPurple,
                    Label = $"{l.Name} L"
                });
            }

            if (!LinePreviewEnabled)
            {
                return;
            }

            using var processed = _preprocessor.Run(image, _config.Preprocess);
            var det = _lineDetector.DetectLongestLine(processed, l.SearchRoi, l.Canny1, l.Canny2, l.HoughThreshold, l.MinLineLength, l.MaxLineGap);
            if (det.Found)
            {
                dst.Add(new OverlayLineItem
                {
                    X1 = det.P1.X,
                    Y1 = det.P1.Y,
                    X2 = det.P2.X,
                    Y2 = det.P2.Y,
                    Stroke = Brushes.MediumPurple,
                    Label = l.Name
                });
            }

            return;
        }

        if (string.Equals(node.Type, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            var d = _config.Distances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (d is null)
            {
                return;
            }

            var pa = _config.Points.FirstOrDefault(x => string.Equals(x.Name, d.PointA, StringComparison.OrdinalIgnoreCase));
            var pb = _config.Points.FirstOrDefault(x => string.Equals(x.Name, d.PointB, StringComparison.OrdinalIgnoreCase));
            if (pa is null || pb is null)
            {
                return;
            }

            dst.Add(new OverlayPointItem
            {
                X = pa.WorldPosition.X,
                Y = pa.WorldPosition.Y,
                Stroke = Brushes.DeepSkyBlue,
                Label = pa.Name
            });

            dst.Add(new OverlayPointItem
            {
                X = pb.WorldPosition.X,
                Y = pb.WorldPosition.Y,
                Stroke = Brushes.DeepSkyBlue,
                Label = pb.Name
            });

            var distPx = Geometry2D.Distance(new Point2d(pa.WorldPosition.X, pa.WorldPosition.Y), new Point2d(pb.WorldPosition.X, pb.WorldPosition.Y));
            var value = _config.PixelsPerMm > 0 ? distPx / _config.PixelsPerMm : distPx;

            dst.Add(new OverlayLineItem
            {
                X1 = pa.WorldPosition.X,
                Y1 = pa.WorldPosition.Y,
                X2 = pb.WorldPosition.X,
                Y2 = pb.WorldPosition.Y,
                Stroke = Brushes.Lime,
                Label = $"{d.Name}: {value:0.###}"
            });

            return;
        }

        if (string.Equals(node.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var dd = _config.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (dd is null)
            {
                return;
            }

            var a = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, dd.LineA, StringComparison.OrdinalIgnoreCase));
            var b = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, dd.LineB, StringComparison.OrdinalIgnoreCase));
            if (a is null || b is null)
            {
                return;
            }

            using var processed = _preprocessor.Run(image, _config.Preprocess);
            var la = _lineDetector.DetectLongestLine(processed, a.SearchRoi, a.Canny1, a.Canny2, a.HoughThreshold, a.MinLineLength, a.MaxLineGap);
            var lb = _lineDetector.DetectLongestLine(processed, b.SearchRoi, b.Canny1, b.Canny2, b.HoughThreshold, b.MinLineLength, b.MaxLineGap);
            if (!la.Found || !lb.Found)
            {
                return;
            }

            dst.Add(new OverlayLineItem
            {
                X1 = la.P1.X,
                Y1 = la.P1.Y,
                X2 = la.P2.X,
                Y2 = la.P2.Y,
                Stroke = Brushes.MediumPurple,
                Label = a.Name
            });

            dst.Add(new OverlayLineItem
            {
                X1 = lb.P1.X,
                Y1 = lb.P1.Y,
                X2 = lb.P2.X,
                Y2 = lb.P2.Y,
                Stroke = Brushes.MediumPurple,
                Label = b.Name
            });

            var (distPx, ca, cb) = Geometry2D.SegmentToSegmentDistance(la.P1, la.P2, lb.P1, lb.P2);
            var value = _config.PixelsPerMm > 0 ? distPx / _config.PixelsPerMm : distPx;

            dst.Add(new OverlayLineItem
            {
                X1 = ca.X,
                Y1 = ca.Y,
                X2 = cb.X,
                Y2 = cb.Y,
                Stroke = Brushes.Lime,
                Label = $"{dd.Name}: {value:0.###}"
            });

            return;
        }

        if (string.Equals(node.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var dd = _config.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (dd is null)
            {
                return;
            }

            var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, dd.Point, StringComparison.OrdinalIgnoreCase));
            var ldef = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, dd.Line, StringComparison.OrdinalIgnoreCase));
            if (p is null || ldef is null)
            {
                return;
            }

            using var processed = _preprocessor.Run(image, _config.Preprocess);
            var l = _lineDetector.DetectLongestLine(processed, ldef.SearchRoi, ldef.Canny1, ldef.Canny2, ldef.HoughThreshold, ldef.MinLineLength, ldef.MaxLineGap);
            if (!l.Found)
            {
                return;
            }

            var pp = new Point2d(p.WorldPosition.X, p.WorldPosition.Y);
            var (distPx, closestOnSeg) = Geometry2D.PointToSegmentDistance(pp, l.P1, l.P2);
            var value = _config.PixelsPerMm > 0 ? distPx / _config.PixelsPerMm : distPx;

            dst.Add(new OverlayPointItem
            {
                X = pp.X,
                Y = pp.Y,
                Stroke = Brushes.DeepSkyBlue,
                Label = p.Name
            });

            dst.Add(new OverlayLineItem
            {
                X1 = l.P1.X,
                Y1 = l.P1.Y,
                X2 = l.P2.X,
                Y2 = l.P2.Y,
                Stroke = Brushes.MediumPurple,
                Label = ldef.Name
            });

            dst.Add(new OverlayLineItem
            {
                X1 = pp.X,
                Y1 = pp.Y,
                X2 = closestOnSeg.X,
                Y2 = closestOnSeg.Y,
                Stroke = Brushes.Lime,
                Label = $"{dd.Name}: {value:0.###}"
            });

            return;
        }

        if (string.Equals(node.Type, "DefectRoi", StringComparison.OrdinalIgnoreCase))
        {
            if (_config.DefectConfig.InspectRoi.Width > 0 && _config.DefectConfig.InspectRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = _config.DefectConfig.InspectRoi.X,
                    Y = _config.DefectConfig.InspectRoi.Y,
                    Width = _config.DefectConfig.InspectRoi.Width,
                    Height = _config.DefectConfig.InspectRoi.Height,
                    Stroke = Brushes.Orange,
                    Label = "DefectROI"
                });
            }

            return;
        }

        BuildFinalOverlay(image, dst);
    }

    private void BuildFinalOverlay(Mat image, ObservableCollection<OverlayItem> dst)
    {
        if (_config is null)
        {
            return;
        }

        if (_config.Origin.SearchRoi.Width > 0 && _config.Origin.SearchRoi.Height > 0)
        {
            dst.Add(new OverlayRectItem
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
            dst.Add(new OverlayRectItem
            {
                X = _config.Origin.TemplateRoi.X,
                Y = _config.Origin.TemplateRoi.Y,
                Width = _config.Origin.TemplateRoi.Width,
                Height = _config.Origin.TemplateRoi.Height,
                Stroke = Brushes.Gold,
                Label = "Origin T"
            });
        }

        foreach (var p in _config.Points)
        {
            if (p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
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
                dst.Add(new OverlayRectItem
                {
                    X = p.TemplateRoi.X,
                    Y = p.TemplateRoi.Y,
                    Width = p.TemplateRoi.Width,
                    Height = p.TemplateRoi.Height,
                    Stroke = Brushes.Gold,
                    Label = $"{p.Name} T"
                });
            }

            dst.Add(new OverlayPointItem
            {
                X = p.WorldPosition.X,
                Y = p.WorldPosition.Y,
                Stroke = Brushes.DeepSkyBlue,
                Label = p.Name
            });
        }

        foreach (var l in _config.Lines)
        {
            if (l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
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

        using var processed = _preprocessor.Run(image, _config.Preprocess);
        var detectedLines = new System.Collections.Generic.Dictionary<string, LineDetectResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in _config.Lines)
        {
            if (l.SearchRoi.Width <= 0 || l.SearchRoi.Height <= 0)
            {
                continue;
            }

            var det = _lineDetector.DetectLongestLine(processed, l.SearchRoi, l.Canny1, l.Canny2, l.HoughThreshold, l.MinLineLength, l.MaxLineGap);
            var named = det with { Name = l.Name };
            detectedLines[l.Name] = named;
            if (named.Found)
            {
                dst.Add(new OverlayLineItem
                {
                    X1 = named.P1.X,
                    Y1 = named.P1.Y,
                    X2 = named.P2.X,
                    Y2 = named.P2.Y,
                    Stroke = Brushes.MediumPurple,
                    Label = named.Name
                });
            }
        }

        foreach (var dd in _config.LineToLineDistances)
        {
            if (string.IsNullOrWhiteSpace(dd.Name) || string.IsNullOrWhiteSpace(dd.LineA) || string.IsNullOrWhiteSpace(dd.LineB))
            {
                continue;
            }

            if (!detectedLines.TryGetValue(dd.LineA, out var la) || !detectedLines.TryGetValue(dd.LineB, out var lb) || !la.Found || !lb.Found)
            {
                continue;
            }

            var (distPx, ca, cb) = CalculateLineLineDistance(la, lb, dd.Mode);
            var mm = _config.PixelsPerMm > 0 ? distPx / _config.PixelsPerMm : distPx;
            var pass = mm >= (dd.Nominal - dd.ToleranceMinus) && mm <= (dd.Nominal + dd.TolerancePlus);

            dst.Add(new OverlayLineItem
            {
                X1 = ca.X,
                Y1 = ca.Y,
                X2 = cb.X,
                Y2 = cb.Y,
                Stroke = pass ? Brushes.Lime : Brushes.Red,
                Label = $"{dd.Name}: {mm:0.00} mm"
            });
        }

        foreach (var dd in _config.PointToLineDistances)
        {
            if (string.IsNullOrWhiteSpace(dd.Name) || string.IsNullOrWhiteSpace(dd.Point) || string.IsNullOrWhiteSpace(dd.Line))
            {
                continue;
            }

            var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, dd.Point, StringComparison.OrdinalIgnoreCase));
            if (p is null)
            {
                continue;
            }

            if (!detectedLines.TryGetValue(dd.Line, out var l) || !l.Found)
            {
                continue;
            }

            var pp = new Point2d(p.WorldPosition.X, p.WorldPosition.Y);

            var (distPx, closest) = CalculatePointLineDistance(pp, l, dd.Mode);
            var mm = _config.PixelsPerMm > 0 ? distPx / _config.PixelsPerMm : distPx;
            var pass = mm >= (dd.Nominal - dd.ToleranceMinus) && mm <= (dd.Nominal + dd.TolerancePlus);

            dst.Add(new OverlayLineItem
            {
                X1 = pp.X,
                Y1 = pp.Y,
                X2 = closest.X,
                Y2 = closest.Y,
                Stroke = pass ? Brushes.Lime : Brushes.Red,
                Label = $"{dd.Name}: {mm:0.00} mm"
            });
        }

        foreach (var d in _config.Distances)
        {
            var pa = _config.Points.FirstOrDefault(x => string.Equals(x.Name, d.PointA, StringComparison.OrdinalIgnoreCase));
            var pb = _config.Points.FirstOrDefault(x => string.Equals(x.Name, d.PointB, StringComparison.OrdinalIgnoreCase));
            if (pa is null || pb is null)
            {
                continue;
            }

            var dx = pb.WorldPosition.X - pa.WorldPosition.X;
            var dy = pb.WorldPosition.Y - pa.WorldPosition.Y;
            var distPx = Math.Sqrt(dx * dx + dy * dy);
            var mm = _config.PixelsPerMm > 0 ? distPx / _config.PixelsPerMm : distPx;

            dst.Add(new OverlayLineItem
            {
                X1 = pa.WorldPosition.X,
                Y1 = pa.WorldPosition.Y,
                X2 = pb.WorldPosition.X,
                Y2 = pb.WorldPosition.Y,
                Stroke = Brushes.Yellow,
                Label = $"{d.Name}: {mm:0.00} mm"
            });
        }

        if (_config.DefectConfig.InspectRoi.Width > 0 && _config.DefectConfig.InspectRoi.Height > 0)
        {
            dst.Add(new OverlayRectItem
            {
                X = _config.DefectConfig.InspectRoi.X,
                Y = _config.DefectConfig.InspectRoi.Y,
                Width = _config.DefectConfig.InspectRoi.Width,
                Height = _config.DefectConfig.InspectRoi.Height,
                Stroke = Brushes.Orange,
                Label = "DefectROI"
            });
        }
    }
}

public sealed partial class ToolGraphNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _type = string.Empty;

    partial void OnTypeChanged(string value)
    {
        RebuildPorts();
        OnPropertyChanged(nameof(NodeHeight));
    }

    [ObservableProperty]
    private string _refName = string.Empty;

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    public ObservableCollection<NodePortViewModel> InPorts { get; } = new();

    public ObservableCollection<NodePortViewModel> OutPorts { get; } = new();

    public double NodeHeight
    {
        get
        {
            var count = Math.Max(1, InPorts.Count);
            // Base 52 for 1 port, grow by 18px per extra port.
            return 52 + (count - 1) * 18;
        }
    }

    public int GetInPortIndex(string portName)
    {
        for (var i = 0; i < InPorts.Count; i++)
        {
            if (string.Equals(InPorts[i].Name, portName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    public int GetOutPortIndex(string portName)
    {
        for (var i = 0; i < OutPorts.Count; i++)
        {
            if (string.Equals(OutPorts[i].Name, portName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    public double GetInPortCenterY(string portName)
    {
        if (InPorts.Count <= 1)
        {
            return 26;
        }

        var idx = GetInPortIndex(portName);
        var spacing = 18.0;
        var total = (InPorts.Count - 1) * spacing;
        var start = 26 - total / 2.0;
        return start + idx * spacing;
    }

    public double GetOutPortCenterY(string portName)
    {
        if (OutPorts.Count <= 1)
        {
            return 26;
        }

        var idx = GetOutPortIndex(portName);
        var spacing = 18.0;
        var total = (OutPorts.Count - 1) * spacing;
        var start = 26 - total / 2.0;
        return start + idx * spacing;
    }

    public void EnsurePortsInitialized()
    {
        if (InPorts.Count == 0 && OutPorts.Count == 0)
        {
            RebuildPorts();
        }
    }

    private void RebuildPorts()
    {
        InPorts.Clear();
        OutPorts.Clear();

        // Single output for now.
        OutPorts.Add(new NodePortViewModel(this, "Out", isInput: false));

        if (string.Equals(Type, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            InPorts.Add(new NodePortViewModel(this, "A", isInput: true));
            InPorts.Add(new NodePortViewModel(this, "B", isInput: true));
        }
        else if (string.Equals(Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            InPorts.Add(new NodePortViewModel(this, "A", isInput: true));
            InPorts.Add(new NodePortViewModel(this, "B", isInput: true));
        }
        else if (string.Equals(Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            InPorts.Add(new NodePortViewModel(this, "P", isInput: true));
            InPorts.Add(new NodePortViewModel(this, "L", isInput: true));
        }
        else
        {
            InPorts.Add(new NodePortViewModel(this, "In", isInput: true));
        }
    }
}

public sealed class NodePortViewModel
{
    public NodePortViewModel(ToolGraphNodeViewModel node, string name, bool isInput)
    {
        Node = node;
        Name = name;
        IsInput = isInput;
    }

    public ToolGraphNodeViewModel Node { get; }

    public string Name { get; }

    public bool IsInput { get; }

    public string Tag => IsInput ? $"InPort:{Name}" : $"OutPort:{Name}";
}

public sealed class ToolGraphEdgeViewModel : ObservableObject
{
    private readonly ToolGraphNodeViewModel _from;
    private readonly ToolGraphNodeViewModel _to;

    public ToolGraphEdgeViewModel(ToolGraphNodeViewModel from, ToolGraphNodeViewModel to, string fromPort, string toPort)
    {
        _from = from;
        _to = to;
        FromPort = fromPort;
        ToPort = toPort;
    }

    public string FromNodeId => _from.Id;

    public string ToNodeId => _to.Id;

    public string FromPort { get; }

    public string ToPort { get; }

    public Geometry PathData
    {
        get
        {
            var p1 = GetFromPortPosition();
            var p2 = GetToPortPosition();

            var dx = Math.Abs(p2.X - p1.X);
            var c = Math.Max(40.0, dx * 0.5);

            var c1 = new System.Windows.Point(p1.X + c, p1.Y);
            var c2 = new System.Windows.Point(p2.X - c, p2.Y);

            var fig = new PathFigure { StartPoint = p1, IsClosed = false, IsFilled = false };
            fig.Segments.Add(new BezierSegment(c1, c2, p2, true));
            return new PathGeometry(new[] { fig });
        }
    }

    public void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(PathData));
    }

    private System.Windows.Point GetFromPortPosition()
    {
        _from.EnsurePortsInitialized();
        var cy = _from.GetOutPortCenterY(FromPort);
        return new System.Windows.Point(_from.X + 160, _from.Y + cy);
    }

    private System.Windows.Point GetToPortPosition()
    {
        _to.EnsurePortsInitialized();
        var cy = _to.GetInPortCenterY(ToPort);
        return new System.Windows.Point(_to.X, _to.Y + cy);
    }
}
