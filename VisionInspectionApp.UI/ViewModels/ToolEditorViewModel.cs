using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    public sealed partial class TextColorConditionRow : ObservableObject
    {
        private readonly Action _onChanged;

        public TextColorConditionDefinition Model { get; }

        [ObservableProperty]
        private string _expression;

        [ObservableProperty]
        private string _color;

        public TextColorConditionRow(TextColorConditionDefinition model, Action onChanged)
        {
            Model = model;
            _onChanged = onChanged;
            _expression = model.Expression ?? string.Empty;
            _color = model.Color ?? "#FF00FF00";
        }

        partial void OnExpressionChanged(string value)
        {
            Model.Expression = value ?? string.Empty;
            _onChanged();
        }

        partial void OnColorChanged(string value)
        {
            Model.Color = value ?? "#FF00FF00";
            _onChanged();
        }
    }

    private readonly IConfigService _configService;
    private readonly ConfigStoreOptions _storeOptions;
    private readonly SharedImageContext _sharedImage;
    private readonly ImagePreprocessor _preprocessor;
    private readonly LineDetector _lineDetector;
    private readonly IInspectionService _inspectionService;

    [ObservableProperty]
    private string? _activeRoiLabel;

    private ToolGraphNodeViewModel? _selectedNodeHook;
    private string? _selectedNodePrevRefName;

    private bool _finalPreviewDirty = true;
    private BitmapSource? _cachedFinalPreviewImage;

    private readonly DispatcherTimer _autoSaveTimer;
    private bool _autoSavePending;

    private readonly DispatcherTimer _specEditPreviewTimer;

    private readonly DispatcherTimer _blobThresholdPreviewTimer;

    private bool _syncingInputs;

    private int _lastPreviewImageWidth;
    private int _lastPreviewImageHeight;

    private const int MaxBlobOverlayCount = 300;

    private const string DefaultPreprocessChoice = "None (Default)";

    [ObservableProperty]
    private double _canvasZoom = 1.0;

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

        _specEditPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _specEditPreviewTimer.Tick += (_, __) =>
        {
            _specEditPreviewTimer.Stop();
            RefreshPreviews();
        };

        _blobThresholdPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _blobThresholdPreviewTimer.Tick += (_, __) => UpdateBlobThresholdPreviewFromSnapshot();

        AvailableConfigs = new ObservableCollection<string>();
        ToolboxItems = new ObservableCollection<string>
        {
            "Preprocess",
            "Origin",
            "Point",
            "Line",
            "Caliper",
            "LinePairDetection",
            "EdgePairDetect",
            "CircleFinder",
            "Diameter",
            "Distance",
            "LineLineDistance",
            "PointLineDistance",
            "Angle",
            "EdgePair",
            "Condition",
            "Text",
            "BlobDetection",
            "SurfaceCompare",
            "CodeDetection",
            "DefectRoi"
        };

        Nodes = new ObservableCollection<ToolGraphNodeViewModel>();
        Edges = new ObservableCollection<ToolGraphEdgeViewModel>();
        AvailablePreprocessChoices = new ObservableCollection<string>();
        SelectedNodeOverlayItems = new ObservableCollection<OverlayItem>();
        FinalOverlayItems = new ObservableCollection<OverlayItem>();
        TextNode_ConditionRows = new ObservableCollection<TextColorConditionRow>();

        RefreshConfigsCommand = new RelayCommand(RefreshConfigs);
        LoadConfigCommand = new RelayCommand(LoadConfig);
        SaveConfigCommand = new RelayCommand(SaveConfig);
        NewGraphCommand = new RelayCommand(NewGraph);
        DeleteSelectedNodeCommand = new RelayCommand(DeleteSelectedNode);
        DeleteSelectedEdgeCommand = new RelayCommand(DeleteSelectedEdge);
        LoadPreviewImageCommand = new RelayCommand(LoadPreviewImage);
        RunFlowCommand = new RelayCommand(RunFlow);
        RoiSelectedCommand = new RelayCommand<object?>(OnRoiSelected);
        RoiEditedCommand = new RelayCommand<RoiSelection?>(OnRoiEdited);
        RoiDeletedCommand = new RelayCommand<string?>(OnRoiDeleted);
        PointClickedCommand = new RelayCommand<PointClickSelection?>(OnPointClicked);
        PointDoubleClickedCommand = new RelayCommand<PointClickSelection?>(OnPointDoubleClicked);

        TextNode_AddConditionCommand = new RelayCommand(TextNode_AddCondition);
        TextNode_RemoveConditionCommand = new RelayCommand<TextColorConditionRow?>(TextNode_RemoveCondition);

        SurfaceCompare_SetSearchRoiCommand = new RelayCommand(SurfaceCompare_SetSearchRoi);
        SurfaceCompare_SetTemplateRoiCommand = new RelayCommand(SurfaceCompare_SetTemplateRoi);

        _sharedImage.ImageChanged += (_, __) => RefreshPreviews();

        RefreshConfigs();
    }

    public ICommand SurfaceCompare_SetSearchRoiCommand { get; }
    public ICommand SurfaceCompare_SetTemplateRoiCommand { get; }

    public ObservableCollection<TextColorConditionRow> TextNode_ConditionRows { get; }

    public ICommand TextNode_AddConditionCommand { get; }
    public ICommand TextNode_RemoveConditionCommand { get; }

    private void SurfaceCompare_SetSearchRoi()
    {
        if (SelectedNode is null || !string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase)) return;
        ActiveRoiLabel = $"{SelectedNode.RefName} SC";
    }

    private void SurfaceCompare_SetTemplateRoi()
    {
        if (SelectedNode is null || !string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase)) return;
        ActiveRoiLabel = $"{SelectedNode.RefName} SCT";
    }

    public IlluminationCorrectionPreset IlluminationCorrection
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.IlluminationCorrection ?? IlluminationCorrectionPreset.None;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            if (s.IlluminationCorrection == value) return;
            s.IlluminationCorrection = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int IlluminationKernel
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.IlluminationKernel ?? 51;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            var v = Math.Clamp(value, 3, 401);
            if (v % 2 == 0) v += 1;
            if (s.IlluminationKernel == v) return;
            s.IlluminationKernel = v;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public double ClaheClipLimit
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.ClaheClipLimit ?? 2.0;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            var v = Math.Clamp(value, 0.1, 40.0);
            if (Math.Abs(s.ClaheClipLimit - v) < 0.0000001) return;
            s.ClaheClipLimit = v;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int ClaheTileGrid
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.ClaheTileGrid ?? 8;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            var v = Math.Clamp(value, 2, 32);
            if (s.ClaheTileGrid == v) return;
            s.ClaheTileGrid = v;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    private void OnRoiDeleted(string? labelRaw)
    {
        if (string.IsNullOrWhiteSpace(labelRaw) || _config is null)
        {
            return;
        }

        var label = labelRaw.Trim();

        // Defect / Origin / Point / Line deletes are not supported (for safety).
        // For BlobDetection we allow deleting: B (legacy inspect roi) and B#/BX# (multi rois).
        var parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return;
        }

        var name = parts[0];
        var kind = parts[1];

        if (string.Equals(kind, "CIR", StringComparison.OrdinalIgnoreCase))
        {
            var c = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (c is null)
            {
                return;
            }

            c.SearchRoi = new Roi();
            RunFlow();
            RequestAutoSave();
            return;
        }

        if (kind.StartsWith("SC", StringComparison.OrdinalIgnoreCase))
        {
            var sc = _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (sc is null)
            {
                return;
            }

            if (string.Equals(kind, "SC", StringComparison.OrdinalIgnoreCase))
            {
                sc.InspectRoi = new Roi();
                sc.Rois.Clear();
                RunFlow();
                RequestAutoSave();
                return;
            }

            // Multi ROI edit labels are index-based: SC1,SC2,... and SCX1,SCX2,...
            var scIsExclude = kind.StartsWith("SCX", StringComparison.OrdinalIgnoreCase);
            var scNumPart = scIsExclude ? kind.Substring(3) : kind.Substring(2);
            if (!int.TryParse(scNumPart, out var scIdx1) || scIdx1 <= 0)
            {
                return;
            }

            var scIdx = scIdx1 - 1;
            if (scIdx < 0 || scIdx >= sc.Rois.Count)
            {
                return;
            }

            sc.Rois.RemoveAt(scIdx);
            sc.InspectRoi = ComputeSurfaceCompareInspectRoi(sc);

            RunFlow();
            RequestAutoSave();
            return;
        }

        if (!kind.StartsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var b = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (b is null)
        {
            return;
        }

        if (string.Equals(kind, "B", StringComparison.OrdinalIgnoreCase))
        {
            b.InspectRoi = new Roi();
            b.Rois.Clear();
            RunFlow();
            RequestAutoSave();
            return;
        }

        // Multi ROI edit labels are index-based: B1,B2,... and BX1,BX2,...
        var isExclude = kind.StartsWith("BX", StringComparison.OrdinalIgnoreCase);
        var numPart = isExclude ? kind.Substring(2) : kind.Substring(1);
        if (!int.TryParse(numPart, out var idx1) || idx1 <= 0)
        {
            return;
        }

        var idx = idx1 - 1;
        if (idx < 0 || idx >= b.Rois.Count)
        {
            return;
        }

        b.Rois.RemoveAt(idx);
        b.InspectRoi = ComputeBlobInspectRoi(b);

        RunFlow();
        RequestAutoSave();
    }

    private void OnPointDoubleClicked(PointClickSelection? click)
    {
        if (click is null)
        {
            return;
        }

        if (_config is null || SelectedNode is null)
        {
            return;
        }

        if (!string.Equals(SelectedNode.Type, "Text", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var t = _config.TextNodes.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        if (t is null)
        {
            return;
        }

        t.X = (int)Math.Round(click.X);
        t.Y = (int)Math.Round(click.Y);

        RaiseToolPropertyPanelsChanged();
        RefreshPreviews();
        RequestAutoSave();
    }

    private static Roi ComputeSurfaceCompareInspectRoi(SurfaceCompareDefinition sc)
    {
        if (sc.Rois is null || sc.Rois.Count == 0)
        {
            return sc.InspectRoi;
        }

        var inc = sc.Rois.Where(x => x.Mode == BlobRoiMode.Include && x.Roi.Width > 0 && x.Roi.Height > 0)
            .Select(x => x.Roi)
            .ToList();

        if (inc.Count == 0)
        {
            return sc.InspectRoi;
        }

        var minX = inc.Min(x => x.X);
        var minY = inc.Min(x => x.Y);
        var maxX = inc.Max(x => x.X + x.Width);
        var maxY = inc.Max(x => x.Y + x.Height);
        return new Roi { X = minX, Y = minY, Width = Math.Max(1, maxX - minX), Height = Math.Max(1, maxY - minY) };
    }

    private void BuildFinalOverlayFromRunWithConfig(InspectionResult run, ObservableCollection<OverlayItem> dst)
    {
        BuildFinalOverlayFromRun(run, dst, _config);

        // Angle overlays need image bounds for full infinite-line rendering.
        if (_lastPreviewImageWidth > 0 && _lastPreviewImageHeight > 0)
        {
            foreach (var a in run.Angles)
            {
                if (double.IsNaN(a.ValueDeg) || !a.Found)
                {
                    continue;
                }

                var ip = new System.Windows.Point(a.Intersection.X, a.Intersection.Y);
                var aDir = new System.Windows.Point(a.ADir.X, a.ADir.Y);
                var bDir = new System.Windows.Point(a.BDir.X, a.BDir.Y);

                if (TryClipInfiniteLineToImage(ip, aDir, _lastPreviewImageWidth, _lastPreviewImageHeight, out var a1, out var a2))
                {
                    dst.Add(new OverlayLineItem { X1 = a1.X, Y1 = a1.Y, X2 = a2.X, Y2 = a2.Y, Stroke = Brushes.MediumPurple, Label = a.LineA });
                }
                if (TryClipInfiniteLineToImage(ip, bDir, _lastPreviewImageWidth, _lastPreviewImageHeight, out var b1, out var b2))
                {
                    dst.Add(new OverlayLineItem { X1 = b1.X, Y1 = b1.Y, X2 = b2.X, Y2 = b2.Y, Stroke = Brushes.Gold, Label = a.LineB });
                }

                AddAngleArc(dst, a.Intersection.X, a.Intersection.Y, a.ADir.X, a.ADir.Y, a.BDir.X, a.BDir.Y, radius: 35.0, stroke: a.Pass ? Brushes.Lime : Brushes.Red);
                dst.Add(new OverlayPointItem { X = a.Intersection.X, Y = a.Intersection.Y, Radius = 3.0, Stroke = a.Pass ? Brushes.Lime : Brushes.Red, Label = $"{a.Name}: {a.ValueDeg:0.###}°" });
            }
        }

        if (_config is null)
        {
            return;
        }

        foreach (var c in _config.CircleFinders)
        {
            if (c.SearchRoi.Width <= 0 || c.SearchRoi.Height <= 0)
            {
                continue;
            }

            dst.Add(new OverlayRectItem
            {
                X = c.SearchRoi.X,
                Y = c.SearchRoi.Y,
                Width = c.SearchRoi.Width,
                Height = c.SearchRoi.Height,
                Stroke = Brushes.MediumPurple,
                Label = $"{c.Name} CIR"
            });
        }

        foreach (var e in _config.EdgePairDetections)
        {
            if (e.SearchRoi.Width <= 0 || e.SearchRoi.Height <= 0)
            {
                continue;
            }

            dst.Add(new OverlayRectItem
            {
                X = e.SearchRoi.X,
                Y = e.SearchRoi.Y,
                Width = e.SearchRoi.Width,
                Height = e.SearchRoi.Height,
                Stroke = Brushes.MediumPurple,
                Label = $"{e.Name} EPD"
            });

            var stripCount = Math.Clamp(e.StripCount, 1, 100);
            var stripLength = Math.Max(3, e.StripLength);
            if (stripCount > 0)
            {
                if (e.Orientation == CaliperOrientation.Vertical)
                {
                    var y1 = e.SearchRoi.Y + (e.SearchRoi.Height - stripLength) / 2.0;
                    var y2 = y1 + stripLength;
                    for (var i = 0; i < stripCount; i++)
                    {
                        var x = e.SearchRoi.X + (i + 0.5) * e.SearchRoi.Width / stripCount;
                        dst.Add(new OverlayLineItem { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = Brushes.MediumPurple, StrokeThickness = 1.0 });
                    }
                }
                else
                {
                    var x1 = e.SearchRoi.X + (e.SearchRoi.Width - stripLength) / 2.0;
                    var x2 = x1 + stripLength;
                    for (var i = 0; i < stripCount; i++)
                    {
                        var y = e.SearchRoi.Y + (i + 0.5) * e.SearchRoi.Height / stripCount;
                        dst.Add(new OverlayLineItem { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = Brushes.MediumPurple, StrokeThickness = 1.0 });
                    }
                }
            }
        }

        foreach (var b in _config.BlobDetections)
        {
            if (b.InspectRoi.Width <= 0 || b.InspectRoi.Height <= 0)
            {
                continue;
            }

            var r = run.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (r is null)
            {
                continue;
            }

            dst.Add(new OverlayPointItem
            {
                X = b.InspectRoi.X + 2,
                Y = b.InspectRoi.Y + 2,
                Radius = 1.0,
                Stroke = Brushes.Gold,
                Label = $"{b.Name}: {r.Count}"
            });

            if (r.Blobs is null || r.Blobs.Count == 0)
            {
                continue;
            }

            var n = Math.Min(r.Blobs.Count, MaxBlobOverlayCount);
            for (var i = 0; i < n; i++)
            {
                var bi = r.Blobs[i];
                var br = bi.BoundingBox;
                if (br.Width > 0 && br.Height > 0)
                {
                    dst.Add(new OverlayRectItem
                    {
                        X = br.X,
                        Y = br.Y,
                        Width = br.Width,
                        Height = br.Height,
                        Stroke = Brushes.Gold,
                        Label = string.Empty
                    });
                }

                dst.Add(new OverlayPointItem
                {
                    X = bi.Centroid.X,
                    Y = bi.Centroid.Y,
                    Radius = 3.0,
                    Stroke = Brushes.Gold,
                    Label = string.Empty
                });
            }

            if (r.Blobs.Count > MaxBlobOverlayCount)
            {
                dst.Add(new OverlayPointItem
                {
                    X = b.InspectRoi.X + 2,
                    Y = b.InspectRoi.Y + 16,
                    Radius = 1.0,
                    Stroke = Brushes.Gold,
                    Label = $"+{r.Blobs.Count - MaxBlobOverlayCount}"
                });
            }
        }

        foreach (var sc in _config.SurfaceCompares)
        {
            if (sc.InspectRoi.Width <= 0 || sc.InspectRoi.Height <= 0)
            {
                continue;
            }

            var r = run.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, sc.Name, StringComparison.OrdinalIgnoreCase));
            if (r is null)
            {
                continue;
            }

            dst.Add(new OverlayPointItem
            {
                X = sc.InspectRoi.X + 2,
                Y = sc.InspectRoi.Y + 2,
                Radius = 1.0,
                Stroke = Brushes.DeepSkyBlue,
                Label = $"{sc.Name}: {r.Count} / {r.MaxArea:0}"
            });

            if (r.Defects is null || r.Defects.Count == 0)
            {
                continue;
            }

            var n = Math.Min(r.Defects.Count, MaxBlobOverlayCount);
            for (var i = 0; i < n; i++)
            {
                var d = r.Defects[i];
                var br = d.BoundingBox;
                if (br.Width > 0 && br.Height > 0)
                {
                    dst.Add(new OverlayRectItem
                    {
                        X = br.X,
                        Y = br.Y,
                        Width = br.Width,
                        Height = br.Height,
                        Stroke = Brushes.DeepSkyBlue,
                        Label = string.Empty
                    });
                }

                dst.Add(new OverlayPointItem
                {
                    X = d.Centroid.X,
                    Y = d.Centroid.Y,
                    Radius = 3.0,
                    Stroke = Brushes.DeepSkyBlue,
                    Label = string.Empty
                });
            }

            if (r.Defects.Count > MaxBlobOverlayCount)
            {
                dst.Add(new OverlayPointItem
                {
                    X = sc.InspectRoi.X + 2,
                    Y = sc.InspectRoi.Y + 16,
                    Radius = 1.0,
                    Stroke = Brushes.DeepSkyBlue,
                    Label = $"+{r.Defects.Count - MaxBlobOverlayCount}"
                });
            }
        }
    }

    public ObservableCollection<string> AvailablePreprocessChoices { get; }

    public bool IsToolWithPreprocessInput => SelectedNode is not null
                                            && (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase));

    public bool IsBlobDetectionNode => SelectedNode is not null
                                       && string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase);

    public bool IsSurfaceCompareNode => SelectedNode is not null
                                        && string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase);

    public bool IsLinePairDetectionNode => SelectedNode is not null
                                           && string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase);

    public bool IsEdgePairDetectNode => SelectedNode is not null
                                        && string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase);

    public bool IsCircleFinderNode => SelectedNode is not null
                                      && string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase);

    public bool IsDiameterNode => SelectedNode is not null
                                  && string.Equals(SelectedNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase);

    public bool IsEdgePairNode => SelectedNode is not null
                                  && string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase);

    public bool IsCodeDetectionNode => SelectedNode is not null
                                       && string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<BlobPolarity> AvailableBlobPolarities { get; }
        = new ObservableCollection<BlobPolarity>((BlobPolarity[])Enum.GetValues(typeof(BlobPolarity)));

    public ObservableCollection<PointFindAlgorithm> AvailablePointFindAlgorithms { get; }
        = new ObservableCollection<PointFindAlgorithm>((PointFindAlgorithm[])Enum.GetValues(typeof(PointFindAlgorithm)));

    [ObservableProperty]
    private string _selectedToolPreprocessChoice = DefaultPreprocessChoice;

    partial void OnSelectedToolPreprocessChoiceChanged(string value)
    {
        if (_syncingInputs)
        {
            return;
        }

        if (_config is null || SelectedNode is null || !IsToolWithPreprocessInput)
        {
            return;
        }

        // Remove existing Pre edge to this tool.
        for (var i = Edges.Count - 1; i >= 0; i--)
        {
            var e = Edges[i];
            if (string.Equals(e.ToNodeId, SelectedNode.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.ToPort, "Pre", StringComparison.OrdinalIgnoreCase))
            {
                Edges.RemoveAt(i);
            }
        }

        if (!string.IsNullOrWhiteSpace(value)
            && !string.Equals(value, DefaultPreprocessChoice, StringComparison.OrdinalIgnoreCase))
        {
            // Find preprocess node by RefName.
            var from = Nodes.FirstOrDefault(n => string.Equals(n.Type, "Preprocess", StringComparison.OrdinalIgnoreCase)
                                                 && string.Equals(n.RefName, value, StringComparison.OrdinalIgnoreCase));
            if (from is not null)
            {
                // Create edge directly; this also syncs to config.
                CreateEdge(from, SelectedNode, fromPort: "Out", toPort: "Pre");
                return;
            }
        }

        // No connection => fallback to default preprocess.
        SyncEdgesToConfig();
        RefreshPreviews();
        RequestAutoSave();
    }

    private Mat ResolveToolPreprocessForPreview(Mat raw, ToolGraphNodeViewModel toolNode)
    {
        if (_config is null)
        {
            return raw.Clone();
        }

        // Determine if tool has a Pre connection from a Preprocess node.
        var edges = _config.ToolGraph?.Edges ?? new();
        var preEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, toolNode.Id, StringComparison.OrdinalIgnoreCase)
                                               && string.Equals(e.ToPort, "Pre", StringComparison.OrdinalIgnoreCase));

        if (preEdge is null)
        {
            return _preprocessor.Run(raw, _config.Preprocess);
        }

        var nodesById = Nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.Id))
            .ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

        if (!nodesById.TryGetValue(preEdge.FromNodeId, out var preNode)
            || !string.Equals(preNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
        {
            return _preprocessor.Run(raw, _config.Preprocess);
        }

        var preprocessSettingsByName = (_config.PreprocessNodes ?? new())
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .ToDictionary(p => p.Name, p => p.Settings ?? new PreprocessSettings(), StringComparer.OrdinalIgnoreCase);

        var cache = new System.Collections.Generic.Dictionary<string, Mat>(StringComparer.OrdinalIgnoreCase);
        var matsToDispose = new System.Collections.Generic.List<Mat>();

        try
        {
            Mat GetPreprocessNodeOutput(string preprocessNodeId)
            {
                if (cache.TryGetValue(preprocessNodeId, out var cached))
                {
                    return cached;
                }

                if (!nodesById.TryGetValue(preprocessNodeId, out var node)
                    || !string.Equals(node.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                {
                    var fallback = _preprocessor.Run(raw, _config.Preprocess);
                    matsToDispose.Add(fallback);
                    cache[preprocessNodeId] = fallback;
                    return fallback;
                }

                var settings = preprocessSettingsByName.TryGetValue(node.RefName, out var s) ? s : new PreprocessSettings();

                var inEdge = edges.FirstOrDefault(e => string.Equals(e.ToNodeId, preprocessNodeId, StringComparison.OrdinalIgnoreCase)
                                                      && string.Equals(e.ToPort, "In", StringComparison.OrdinalIgnoreCase));

                Mat inputMat;
                if (inEdge is null)
                {
                    inputMat = raw;
                }
                else if (!nodesById.TryGetValue(inEdge.FromNodeId, out var fromNode)
                         || !string.Equals(fromNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                {
                    inputMat = raw;
                }
                else
                {
                    inputMat = GetPreprocessNodeOutput(fromNode.Id);
                }

                var output = _preprocessor.Run(inputMat, settings);
                matsToDispose.Add(output);
                cache[preprocessNodeId] = output;
                return output;
            }

            // Clone so caller owns returned Mat.
            return GetPreprocessNodeOutput(preNode.Id).Clone();
        }
        finally
        {
            foreach (var m in matsToDispose)
            {
                m.Dispose();
            }
        }
    }

    public ObservableCollection<LineLineDistanceMode> AvailableLineLineDistanceModes { get; }
        = new ObservableCollection<LineLineDistanceMode>((LineLineDistanceMode[])Enum.GetValues(typeof(LineLineDistanceMode)));

    public ObservableCollection<PointLineDistanceMode> AvailablePointLineDistanceModes { get; }
        = new ObservableCollection<PointLineDistanceMode>((PointLineDistanceMode[])Enum.GetValues(typeof(PointLineDistanceMode)));

    private BlobDetectionDefinition? SelectedBlobDetectionDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    private SurfaceCompareDefinition? SelectedSurfaceCompareDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    public BlobPolarity Blob_Polarity
    {
        get => SelectedBlobDetectionDef()?.Polarity ?? BlobPolarity.DarkOnLight;
        set
        {
            var def = SelectedBlobDetectionDef();
            if (def is null) return;
            if (def.Polarity == value) return;
            def.Polarity = value;
            RequestBlobThresholdPreviewUpdate();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Blob_Threshold
    {
        get => SelectedBlobDetectionDef()?.Threshold ?? 128;
        set
        {
            var def = SelectedBlobDetectionDef();
            if (def is null) return;
            var v = Math.Clamp(value, 0, 255);
            if (def.Threshold == v) return;
            def.Threshold = v;
            RequestBlobThresholdPreviewUpdate();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Blob_MinBlobArea
    {
        get => SelectedBlobDetectionDef()?.MinBlobArea ?? 0;
        set
        {
            var def = SelectedBlobDetectionDef();
            if (def is null) return;
            var v = Math.Max(0, value);
            if (def.MinBlobArea == v) return;
            def.MinBlobArea = v;
            if (def.MaxBlobArea < def.MinBlobArea) def.MaxBlobArea = def.MinBlobArea;
            RequestAutoSave();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Blob_MaxBlobArea));
        }
    }

    public int Blob_MaxBlobArea
    {
        get => SelectedBlobDetectionDef()?.MaxBlobArea ?? 0;
        set
        {
            var def = SelectedBlobDetectionDef();
            if (def is null) return;
            var v = Math.Max(0, value);
            if (v < def.MinBlobArea) v = def.MinBlobArea;
            if (def.MaxBlobArea == v) return;
            def.MaxBlobArea = v;
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    private void RequestBlobThresholdPreviewUpdate()
    {
        if (!_blobThresholdPreviewTimer.IsEnabled)
        {
            _blobThresholdPreviewTimer.Start();
            return;
        }

        _blobThresholdPreviewTimer.Stop();
        _blobThresholdPreviewTimer.Start();
    }

    private void UpdateBlobThresholdPreviewFromSnapshot()
    {
        _blobThresholdPreviewTimer.Stop();

        using var snap = _sharedImage.GetSnapshot();
        if (snap is null)
        {
            BlobThresholdPreviewImage = null;
            return;
        }

        _lastPreviewImageWidth = snap.Width;
        _lastPreviewImageHeight = snap.Height;

        UpdateBlobThresholdPreview(snap);
    }

    public int? Blob_LastRunCount
    {
        get
        {
            if (_lastRun is null || SelectedNode is null) return null;
            if (!string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase)) return null;
            var r = _lastRun.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
            return r is null ? null : r.Count;
        }
    }

    public int SurfaceCompare_DiffThreshold
    {
        get => SelectedSurfaceCompareDef()?.DiffThreshold ?? 25;
        set
        {
            var def = SelectedSurfaceCompareDef();
            if (def is null) return;
            var v = Math.Clamp(value, 0, 255);
            if (def.DiffThreshold == v) return;
            def.DiffThreshold = v;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public int SurfaceCompare_MorphKernel
    {
        get => SelectedSurfaceCompareDef()?.MorphKernel ?? 3;
        set
        {
            var def = SelectedSurfaceCompareDef();
            if (def is null) return;
            var v = Math.Clamp(value, 1, 99);
            if (v % 2 == 0) v += 1;
            if (def.MorphKernel == v) return;
            def.MorphKernel = v;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public int SurfaceCompare_MinBlobArea
    {
        get => SelectedSurfaceCompareDef()?.MinBlobArea ?? 0;
        set
        {
            var def = SelectedSurfaceCompareDef();
            if (def is null) return;
            var v = Math.Max(0, value);
            if (def.MinBlobArea == v) return;
            def.MinBlobArea = v;
            if (def.MaxBlobArea < def.MinBlobArea) def.MaxBlobArea = def.MinBlobArea;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public int SurfaceCompare_MaxBlobArea
    {
        get => SelectedSurfaceCompareDef()?.MaxBlobArea ?? 0;
        set
        {
            var def = SelectedSurfaceCompareDef();
            if (def is null) return;
            var v = Math.Max(0, value);
            if (v < def.MinBlobArea) v = def.MinBlobArea;
            if (def.MaxBlobArea == v) return;
            def.MaxBlobArea = v;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public int SurfaceCompare_MinCount
    {
        get => SelectedSurfaceCompareDef()?.MinCount ?? 0;
        set
        {
            var def = SelectedSurfaceCompareDef();
            if (def is null) return;
            var v = Math.Max(0, value);
            if (def.MinCount == v) return;
            def.MinCount = v;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public int SurfaceCompare_MaxCount
    {
        get => SelectedSurfaceCompareDef()?.MaxCount ?? 0;
        set
        {
            var def = SelectedSurfaceCompareDef();
            if (def is null) return;
            var v = Math.Max(0, value);
            if (def.MaxCount == v) return;
            def.MaxCount = v;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public int? SurfaceCompare_LastRunCount
    {
        get
        {
            if (_lastRun is null || SelectedNode is null) return null;
            if (!string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase)) return null;
            var r = _lastRun.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
            return r is null ? null : r.Count;
        }
    }

    public double? SurfaceCompare_LastRunMaxArea
    {
        get
        {
            if (_lastRun is null || SelectedNode is null) return null;
            if (!string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase)) return null;
            var r = _lastRun.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
            return r is null ? null : r.MaxArea;
        }
    }

    private void OnRoiSelected(object? arg)
    {
        if (_config is null)
        {
            return;
        }

        if (arg is RoiSelection rs)
        {
            // Treat drawing as "set this ROI" for the active label (S/T/L/DefectROI)
            // Special case: BlobDetection supports multi ROI include/exclude
            if (SelectedNode is not null
                && string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(SelectedNode.RefName))
            {
                var def = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                {
                    if (rs.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        def.Rois.Add(new BlobRoiDefinition { Mode = BlobRoiMode.Include, Roi = rs.Roi });
                        def.InspectRoi = ComputeBlobInspectRoi(def);
                    }
                    else if (rs.Modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        def.Rois.Add(new BlobRoiDefinition { Mode = BlobRoiMode.Exclude, Roi = rs.Roi });
                        def.InspectRoi = ComputeBlobInspectRoi(def);
                    }
                    else
                    {
                        ApplyRoiForLabel(rs.Label, rs.Roi);
                        def.InspectRoi = ComputeBlobInspectRoi(def);
                    }
                }
            }
            else
            {
                ApplyRoiForLabel(rs.Label, rs.Roi);
            }

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
            else if (string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
            {
                var b = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (b is not null) b.InspectRoi = roi;
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

    private void RefreshPointEdgePreview(Mat snap)
    {
        if (!PointEdgePreviewEnabled)
        {
            PointEdgePreviewImage = null;
            return;
        }

        if (_config is null || SelectedNode is null || !string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase))
        {
            PointEdgePreviewImage = null;
            return;
        }

        var def = SelectedPointDef();
        if (def is null || def.SearchRoi.Width <= 0 || def.SearchRoi.Height <= 0)
        {
            PointEdgePreviewImage = null;
            return;
        }

        if (def.Algorithm != PointFindAlgorithm.EdgePoint)
        {
            PointEdgePreviewImage = null;
            return;
        }

        using var matForPoint = ResolveToolPreprocessForPreview(snap, SelectedNode);

        var rect = new OpenCvSharp.Rect(def.SearchRoi.X, def.SearchRoi.Y, def.SearchRoi.Width, def.SearchRoi.Height);
        rect = rect.Intersect(new OpenCvSharp.Rect(0, 0, matForPoint.Width, matForPoint.Height));
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            PointEdgePreviewImage = null;
            return;
        }

        using var crop = new Mat(matForPoint, rect);
        using var gray = crop.Channels() == 1 ? crop.Clone() : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var view = crop.Clone();

        var n = Math.Clamp(def.EdgePoint.StripCount, 1, 200);
        var stripW = Math.Max(1, def.EdgePoint.StripWidth);
        var stripL = Math.Max(3, def.EdgePoint.StripLength);
        var minG = Math.Max(0.0, def.EdgePoint.MinEdgeStrength);

        var foundPts = new System.Collections.Generic.List<Point2d>();

        if (def.EdgePoint.Orientation == CaliperOrientation.Vertical)
        {
            var y0 = (int)Math.Round((gray.Rows - (n - 1) * stripW) / 2.0);
            var xMid = gray.Cols / 2;

            for (var i = 0; i < n; i++)
            {
                var y = y0 + i * stripW;
                var rr = new OpenCvSharp.Rect(Math.Max(0, xMid - stripL / 2), Math.Max(0, y), Math.Min(stripL, gray.Cols), Math.Min(stripW, gray.Rows - y));
                if (rr.Width <= 1 || rr.Height <= 0) continue;

                Cv2.Rectangle(view, rr, new Scalar(255, 200, 0), 1);

                var edge = FindEdgeOnStrip(gray, rr, scanAlongX: true, def.EdgePoint.Polarity, minG);
                if (edge.HasValue)
                {
                    foundPts.Add(new Point2d(edge.Value.X, edge.Value.Y));
                    Cv2.Circle(view, new OpenCvSharp.Point((int)Math.Round(edge.Value.X), (int)Math.Round(edge.Value.Y)), 3, new Scalar(0, 255, 0), 2);
                }
            }
        }
        else
        {
            var x0 = (int)Math.Round((gray.Cols - (n - 1) * stripW) / 2.0);
            var yMid = gray.Rows / 2;

            for (var i = 0; i < n; i++)
            {
                var x = x0 + i * stripW;
                var rr = new OpenCvSharp.Rect(Math.Max(0, x), Math.Max(0, yMid - stripL / 2), Math.Min(stripW, gray.Cols - x), Math.Min(stripL, gray.Rows));
                if (rr.Width <= 0 || rr.Height <= 1) continue;

                Cv2.Rectangle(view, rr, new Scalar(255, 200, 0), 1);

                var edge = FindEdgeOnStrip(gray, rr, scanAlongX: false, def.EdgePoint.Polarity, minG);
                if (edge.HasValue)
                {
                    foundPts.Add(new Point2d(edge.Value.X, edge.Value.Y));
                    Cv2.Circle(view, new OpenCvSharp.Point((int)Math.Round(edge.Value.X), (int)Math.Round(edge.Value.Y)), 3, new Scalar(0, 255, 0), 2);
                }
            }
        }

        if (foundPts.Count > 0)
        {
            var avgX = foundPts.Average(p => p.X);
            var avgY = foundPts.Average(p => p.Y);
            Cv2.DrawMarker(view, new OpenCvSharp.Point((int)Math.Round(avgX), (int)Math.Round(avgY)), new Scalar(0, 0, 255), MarkerTypes.Cross, 20, 2);
        }

        PointEdgePreviewImage = view.ToBitmapSource();
    }

    private static Point2d? FindEdgeOnStrip(Mat gray, OpenCvSharp.Rect strip, bool scanAlongX, EdgePolarity polarity, double minG)
    {
        var len = scanAlongX ? strip.Width : strip.Height;
        if (len < 3) return null;

        var prof = new double[len];
        if (scanAlongX)
        {
            var y = strip.Y + strip.Height / 2;
            for (var k = 0; k < len; k++)
            {
                prof[k] = gray.At<byte>(y, strip.X + k);
            }
        }
        else
        {
            var x = strip.X + strip.Width / 2;
            for (var k = 0; k < len; k++)
            {
                prof[k] = gray.At<byte>(strip.Y + k, x);
            }
        }

        var bestIdx = -1;
        var bestG = 0.0;

        for (var k = 0; k < len - 1; k++)
        {
            var g = prof[k + 1] - prof[k];
            var score = polarity switch
            {
                EdgePolarity.DarkToLight => g,
                EdgePolarity.LightToDark => -g,
                _ => Math.Abs(g)
            };

            if (score > bestG)
            {
                bestG = score;
                bestIdx = k;
            }
        }

        if (bestIdx < 1 || bestIdx >= len - 2) return null;
        if (bestG < minG) return null;

        var g0 = (prof[bestIdx] - prof[bestIdx - 1]);
        var g1 = (prof[bestIdx + 1] - prof[bestIdx]);
        var g2 = (prof[bestIdx + 2] - prof[bestIdx + 1]);

        var p0 = polarity switch
        {
            EdgePolarity.DarkToLight => g0,
            EdgePolarity.LightToDark => -g0,
            _ => Math.Abs(g0)
        };
        var p1 = polarity switch
        {
            EdgePolarity.DarkToLight => g1,
            EdgePolarity.LightToDark => -g1,
            _ => Math.Abs(g1)
        };
        var p2 = polarity switch
        {
            EdgePolarity.DarkToLight => g2,
            EdgePolarity.LightToDark => -g2,
            _ => Math.Abs(g2)
        };

        var denom = (p0 - 2.0 * p1 + p2);
        var dx = Math.Abs(denom) < 1e-9 ? 0.0 : 0.5 * (p0 - p2) / denom;
        dx = Math.Clamp(dx, -1.0, 1.0);
        var idx = bestIdx + 0.5 + dx;

        if (scanAlongX)
        {
            var x = strip.X + idx;
            var y = strip.Y + strip.Height / 2.0;
            return new Point2d(x, y);
        }
        else
        {
            var x = strip.X + strip.Width / 2.0;
            var y = strip.Y + idx;
            return new Point2d(x, y);
        }
    }

    private static (double DistPx, Point2d A, Point2d B) CalculateLineLineDistance(LineDetectResult la, LineDetectResult lb, LineLineDistanceMode mode)
    {
        if (mode == LineLineDistanceMode.ExtendToOtherEndpoints)
        {
            var (ea1, ea2) = ExtendSegmentToCoverOtherEndpoints(la.P1, la.P2, lb.P1, lb.P2);
            var (eb1, eb2) = ExtendSegmentToCoverOtherEndpoints(lb.P1, lb.P2, la.P1, la.P2);
            return Geometry2D.SegmentToSegmentDistance(ea1, ea2, eb1, eb2);
        }

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

    private static (Point2d P1, Point2d P2) ExtendSegmentToCoverOtherEndpoints(Point2d s1, Point2d s2, Point2d o1, Point2d o2)
    {
        var d = s2 - s1;
        var len2 = d.X * d.X + d.Y * d.Y;
        if (len2 <= 1e-12)
        {
            return (s1, s2);
        }

        var tO1 = ((o1.X - s1.X) * d.X + (o1.Y - s1.Y) * d.Y) / len2;
        var tO2 = ((o2.X - s1.X) * d.X + (o2.Y - s1.Y) * d.Y) / len2;

        var tMin = Math.Min(0.0, Math.Min(tO1, tO2));
        var tMax = Math.Max(1.0, Math.Max(tO1, tO2));

        var p1 = new Point2d(s1.X + tMin * d.X, s1.Y + tMin * d.Y);
        var p2 = new Point2d(s1.X + tMax * d.X, s1.Y + tMax * d.Y);
        return (p1, p2);
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

        if (string.Equals(kind, "Cal", StringComparison.OrdinalIgnoreCase))
        {
            var c = _config.Calipers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (c is not null) c.SearchRoi = roi;
            return;
        }

        if (string.Equals(kind, "LP", StringComparison.OrdinalIgnoreCase))
        {
            var l = _config.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (l is not null) l.SearchRoi = roi;
            return;
        }

        if (string.Equals(kind, "EPD", StringComparison.OrdinalIgnoreCase))
        {
            var e = _config.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (e is not null) e.SearchRoi = roi;
            return;
        }

        if (string.Equals(kind, "CIR", StringComparison.OrdinalIgnoreCase))
        {
            var c = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (c is not null) c.SearchRoi = roi;
            return;
        }

        if (string.Equals(kind, "C", StringComparison.OrdinalIgnoreCase))
        {
            var c = _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (c is not null) c.SearchRoi = roi;
            return;
        }

        if (kind.StartsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            var b = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (b is null)
            {
                return;
            }

            if (string.Equals(kind, "B", StringComparison.OrdinalIgnoreCase))
            {
                b.InspectRoi = roi;
                return;
            }

            // Multi ROI edit labels:
            // - Include:  B1, B2, ...
            // - Exclude:  BX1, BX2, ...
            var isExclude = kind.StartsWith("BX", StringComparison.OrdinalIgnoreCase);
            var numPart = isExclude ? kind.Substring(2) : kind.Substring(1);
            if (!int.TryParse(numPart, out var idx1) || idx1 <= 0)
            {
                return;
            }

            var idx = idx1 - 1;
            if (idx < 0)
            {
                return;
            }

            while (b.Rois.Count <= idx)
            {
                b.Rois.Add(new BlobRoiDefinition());
            }

            b.Rois[idx].Mode = isExclude ? BlobRoiMode.Exclude : BlobRoiMode.Include;
            b.Rois[idx].Roi = roi;
            b.InspectRoi = ComputeBlobInspectRoi(b);
            return;
        }

        if (kind.StartsWith("SC", StringComparison.OrdinalIgnoreCase))
        {
            var sc = _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (sc is null)
            {
                return;
            }

            if (string.Equals(kind, "SC", StringComparison.OrdinalIgnoreCase))
            {
                sc.InspectRoi = roi;
                if (sc.TemplateRoi.Width <= 0 || sc.TemplateRoi.Height <= 0)
                {
                    sc.TemplateRoi = roi;
                }

                if (string.IsNullOrWhiteSpace(sc.TemplateImageFile))
                {
                    TrySaveSurfaceCompareTemplateImage(name, sc.TemplateRoi);
                }
                return;
            }

            if (string.Equals(kind, "SCT", StringComparison.OrdinalIgnoreCase))
            {
                sc.TemplateRoi = roi;
                TrySaveSurfaceCompareTemplateImage(name, roi);
                return;
            }

            // Multi ROI edit labels:
            // - Include:  SC1, SC2, ...
            // - Exclude:  SCX1, SCX2, ...
            var isExclude = kind.StartsWith("SCX", StringComparison.OrdinalIgnoreCase);
            var numPart = isExclude ? kind.Substring(3) : kind.Substring(2);
            if (!int.TryParse(numPart, out var idx1) || idx1 <= 0)
            {
                return;
            }

            var idx = idx1 - 1;
            if (idx < 0)
            {
                return;
            }

            while (sc.Rois.Count <= idx)
            {
                sc.Rois.Add(new SurfaceCompareRoiDefinition());
            }

            sc.Rois[idx].Mode = isExclude ? BlobRoiMode.Exclude : BlobRoiMode.Include;
            sc.Rois[idx].Roi = roi;
            sc.InspectRoi = ComputeSurfaceCompareInspectRoi(sc);
            return;
        }
    }

    private void TrySaveSurfaceCompareTemplateImage(string surfaceCompareName, Roi roi)
    {
        if (_config is null)
        {
            return;
        }

        using var snap = _sharedImage.GetSnapshot();
        if (snap is null || snap.Empty())
        {
            return;
        }

        var sc = _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, surfaceCompareName, StringComparison.OrdinalIgnoreCase));
        if (sc is null)
        {
            return;
        }

        var templateDir = Path.Combine(Path.GetFullPath(_storeOptions.ConfigRootDirectory), _config.ProductCode, "templates");
        Directory.CreateDirectory(templateDir);
        var fileName = Path.Combine(templateDir, $"{surfaceCompareName.ToLowerInvariant()}_sc.png");

        var r = new OpenCvSharp.Rect(roi.X, roi.Y, roi.Width, roi.Height)
            .Intersect(new OpenCvSharp.Rect(0, 0, snap.Width, snap.Height));
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        using var crop = new Mat(snap, r);
        Mat gray = crop;
        using var grayOwned = crop.Channels() == 1 ? null : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
        if (grayOwned is not null)
        {
            gray = grayOwned;
        }

        Cv2.ImWrite(fileName, gray);
        sc.TemplateImageFile = fileName;
        RequestAutoSave();
    }

    public string? EdgePair_RefA
    {
        get => SelectedEdgePairDef()?.RefA;
        set
        {
            var def = SelectedEdgePairDef();
            if (def is null) return;
            if (string.Equals(def.RefA, value, StringComparison.OrdinalIgnoreCase)) return;
            def.RefA = value ?? string.Empty;
            SyncInputEdgeForEdgePairPort("A", value);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public string? EdgePair_RefB
    {
        get => SelectedEdgePairDef()?.RefB;
        set
        {
            var def = SelectedEdgePairDef();
            if (def is null) return;
            if (string.Equals(def.RefB, value, StringComparison.OrdinalIgnoreCase)) return;
            def.RefB = value ?? string.Empty;
            SyncInputEdgeForEdgePairPort("B", value);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    private void SyncInputEdgeForEdgePairPort(string port, string? lineName)
    {
        if (_syncingInputs) return;
        if (_config is null || SelectedNode is null) return;
        if (!string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase)) return;

        _syncingInputs = true;
        try
        {
            RemoveEdgesToSelectedNodePort(port);
            if (!string.IsNullOrWhiteSpace(lineName))
            {
                var from = Nodes.FirstOrDefault(n => (string.Equals(n.Type, "Line", StringComparison.OrdinalIgnoreCase)
                                                      || string.Equals(n.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
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

    private static Roi ComputeBlobInspectRoi(BlobDetectionDefinition b)
    {
        if (b.Rois is null || b.Rois.Count == 0)
        {
            return b.InspectRoi;
        }

        var inc = b.Rois.Where(x => x.Mode == BlobRoiMode.Include && x.Roi.Width > 0 && x.Roi.Height > 0)
            .Select(x => x.Roi)
            .ToList();

        if (inc.Count == 0)
        {
            return b.InspectRoi;
        }

        var minX = inc.Min(x => x.X);
        var minY = inc.Min(x => x.Y);
        var maxX = inc.Max(x => x.X + x.Width);
        var maxY = inc.Max(x => x.Y + x.Height);
        return new Roi { X = minX, Y = minY, Width = Math.Max(1, maxX - minX), Height = Math.Max(1, maxY - minY) };
    }

    private void BuildOverlayForNodeFromRunWithConfig(ToolGraphNodeViewModel node, InspectionResult run, ObservableCollection<OverlayItem> dst)
    {
        BuildOverlayForNodeFromRun(node, run, dst);

        if (_config is null)
        {
            return;
        }


        if (string.Equals(node.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (def is null || def.InspectRoi.Width <= 0 || def.InspectRoi.Height <= 0)
            {
                return;
            }

            var r = run.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (r is null)
            {
                return;
            }

            dst.Add(new OverlayPointItem
            {
                X = def.InspectRoi.X + 2,
                Y = def.InspectRoi.Y + 2,
                Radius = 1.0,
                Stroke = Brushes.Gold,
                Label = $"{def.Name}: {r.Count}"
            });

            if (r.Blobs is null || r.Blobs.Count == 0)
            {
                return;
            }

            var n = Math.Min(r.Blobs.Count, MaxBlobOverlayCount);
            for (var i = 0; i < n; i++)
            {
                var bi = r.Blobs[i];
                var br = bi.BoundingBox;
                if (br.Width > 0 && br.Height > 0)
                {
                    dst.Add(new OverlayRectItem
                    {
                        X = br.X,
                        Y = br.Y,
                        Width = br.Width,
                        Height = br.Height,
                        Stroke = Brushes.Gold,
                        Label = string.Empty
                    });
                }

                dst.Add(new OverlayPointItem
                {
                    X = bi.Centroid.X,
                    Y = bi.Centroid.Y,
                    Radius = 3.0,
                    Stroke = Brushes.Gold,
                    Label = string.Empty
                });
            }

            if (r.Blobs.Count > MaxBlobOverlayCount)
            {
                dst.Add(new OverlayPointItem
                {
                    X = def.InspectRoi.X + 2,
                    Y = def.InspectRoi.Y + 16,
                    Radius = 1.0,
                    Stroke = Brushes.Gold,
                    Label = $"+{r.Blobs.Count - MaxBlobOverlayCount}"
                });
            }

            return;
        }

        if (string.Equals(node.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (def is null || def.InspectRoi.Width <= 0 || def.InspectRoi.Height <= 0)
            {
                return;
            }

            var r = run.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (r is null)
            {
                return;
            }

            var stroke = r.Pass ? Brushes.Lime : Brushes.Red;
            var status = r.Pass ? "OK" : "NG";

            dst.Add(new OverlayPointItem
            {
                X = def.InspectRoi.X + 2,
                Y = def.InspectRoi.Y + 2,
                Radius = 1.0,
                Stroke = stroke,
                Label = $"{def.Name} [{status}]: Số lỗi: {r.Count}, S.Lớn nhất: {r.MaxArea:0}"
            });

            if (r.Defects is null || r.Defects.Count == 0)
            {
                return;
            }

            var n = Math.Min(r.Defects.Count, MaxBlobOverlayCount);
            for (var i = 0; i < n; i++)
            {
                var d = r.Defects[i];
                var br = d.BoundingBox;
                if (br.Width > 0 && br.Height > 0)
                {
                    dst.Add(new OverlayRectItem
                    {
                        X = br.X,
                        Y = br.Y,
                        Width = br.Width,
                        Height = br.Height,
                        Stroke = stroke,
                        StrokeThickness = 2.0, // Thicker boxes for better visibility
                        Label = string.Empty
                    });
                }
            }

            if (r.Defects.Count > MaxBlobOverlayCount)
            {
                dst.Add(new OverlayPointItem
                {
                    X = def.InspectRoi.X + 2,
                    Y = def.InspectRoi.Y + 16,
                    Radius = 1.0,
                    Stroke = stroke,
                    Label = $"+{r.Defects.Count - MaxBlobOverlayCount}"
                });
            }

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
            RunFlow();
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
            RunFlow();
            RequestAutoSave();
            return;
        }

        if (string.Equals(label, "Origin T", StringComparison.OrdinalIgnoreCase))
        {
            _config.Origin.TemplateRoi = roi;
            _config.Origin.WorldPosition = RoiCenterToWorld(roi);
            TrySaveTemplateImage("origin", roi, isOrigin: true, pointName: null);
            RunFlow();
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
                    RunFlow();
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
                    RunFlow();
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
                    RunFlow();
                    RequestAutoSave();
                    return;
                }
            }

            if (string.Equals(kind, "LP", StringComparison.OrdinalIgnoreCase))
            {
                var l = _config.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (l is not null)
                {
                    l.SearchRoi = roi;
                    RunFlow();
                    RequestAutoSave();
                    return;
                }
            }

            if (string.Equals(kind, "Cal", StringComparison.OrdinalIgnoreCase))
            {
                var c = _config.Calipers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (c is not null)
                {
                    c.SearchRoi = roi;
                    RunFlow();
                    RequestAutoSave();
                    return;
                }
            }

            if (string.Equals(kind, "EPD", StringComparison.OrdinalIgnoreCase))
            {
                var e = _config.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (e is not null)
                {
                    e.SearchRoi = roi;
                    RunFlow();
                    RequestAutoSave();
                    return;
                }
            }

            if (string.Equals(kind, "C", StringComparison.OrdinalIgnoreCase))
            {
                var c = _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (c is not null)
                {
                    c.SearchRoi = roi;
                    RunFlow();
                    RequestAutoSave();
                    return;
                }
            }

            if (string.Equals(kind, "CIR", StringComparison.OrdinalIgnoreCase))
            {
                var c = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (c is not null)
                {
                    c.SearchRoi = roi;
                    RunFlow();
                    RequestAutoSave();
                    return;
                }
            }

            if (kind.StartsWith("B", StringComparison.OrdinalIgnoreCase))
            {
                ApplyRoiForLabel(label, roi);
                RunFlow();
                RequestAutoSave();
                return;
            }

            if (kind.StartsWith("SC", StringComparison.OrdinalIgnoreCase))
            {
                ApplyRoiForLabel(label, roi);
                RunFlow();
                RequestAutoSave();
                return;
            }
        }

        RunFlow();
        RaiseToolPropertyPanelsChanged();
        RequestAutoSave();
    }

    private void RequestAutoSave()
    {
        _autoSavePending = true;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void RequestSpecEditPreviewRefresh()
    {
        _specEditPreviewTimer.Stop();
        _specEditPreviewTimer.Start();
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

        void NormalizeSurfaceCompare(SurfaceCompareDefinition sc)
        {
            if (string.IsNullOrWhiteSpace(sc.TemplateImageFile)) return;
            if (!Path.IsPathRooted(sc.TemplateImageFile))
            {
                sc.TemplateImageFile = Path.GetFullPath(Path.Combine(templateDir, sc.TemplateImageFile));
            }
        }

        NormalizePoint(config.Origin);
        foreach (var p in config.Points) NormalizePoint(p);
        foreach (var sc in config.SurfaceCompares) NormalizeSurfaceCompare(sc);
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
            _config.Origin.ShapeModel = ShapeModelTrainer.Train(gray);
        }
        else if (!string.IsNullOrWhiteSpace(pointName))
        {
            var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, pointName, StringComparison.OrdinalIgnoreCase));
            if (p is not null)
            {
                p.TemplateImageFile = fileName;
                p.ShapeModel = ShapeModelTrainer.Train(gray);
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
            LoadConfig(); // Auto-load when config is selected
        }

        RefreshPreviews();
    }

    [ObservableProperty]
    private string _productCode = "ProductA";

    public ObservableCollection<string> ToolboxItems { get; }

    public ObservableCollection<ToolGraphNodeViewModel> Nodes { get; }

    public ObservableCollection<ToolGraphNodeViewModel> SelectedNodes { get; } = new();

    public ObservableCollection<ToolGraphEdgeViewModel> Edges { get; }

    [ObservableProperty]
    private ToolGraphNodeViewModel? _selectedNode;

    partial void OnSelectedNodeChanged(ToolGraphNodeViewModel? value)
    {
        SelectedEdge = null;
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

        if (value is null)
        {
            ClearNodeSelection();
        }
        else
        {
            // If selection is empty or this node isn't part of multi-selection, treat it as a single-select.
            if (SelectedNodes.Count == 0 || !value.IsSelected)
            {
                SetSingleNodeSelection(value);
            }
            else if (!SelectedNodes.Contains(value))
            {
                // Safety: keep SelectedNodes consistent.
                SelectedNodes.Add(value);
            }
        }

        SyncPreprocessChoices();
        SyncSelectedToolPreprocessChoiceFromGraph();
        SyncTextNodeConditionRows();
        RaiseToolPropertyPanelsChanged();
        RefreshSelectedPreview();
        OnPropertyChanged(nameof(Blob_LastRunCount));
    }

    private void SyncTextNodeConditionRows()
    {
        TextNode_ConditionRows.Clear();

        var def = SelectedTextNodeDef();
        if (def?.Conditions is null)
        {
            return;
        }

        foreach (var c in def.Conditions)
        {
            if (c is null) continue;
            TextNode_ConditionRows.Add(new TextColorConditionRow(c, OnTextNodeConditionEdited));
        }
    }

    private void OnTextNodeConditionEdited()
    {
        RefreshPreviews();
        RequestAutoSave();
    }

    private void TextNode_AddCondition()
    {
        var def = SelectedTextNodeDef();
        if (def is null)
        {
            return;
        }

        def.Conditions ??= new();
        var c = new TextColorConditionDefinition
        {
            Expression = string.Empty,
            Color = "#FF00FF00"
        };
        def.Conditions.Add(c);
        TextNode_ConditionRows.Add(new TextColorConditionRow(c, OnTextNodeConditionEdited));
        RaiseToolPropertyPanelsChanged();
        RefreshPreviews();
        RequestAutoSave();
    }

    private void TextNode_RemoveCondition(TextColorConditionRow? row)
    {
        if (row is null)
        {
            return;
        }

        var def = SelectedTextNodeDef();
        if (def is null || def.Conditions is null)
        {
            return;
        }

        def.Conditions.Remove(row.Model);
        TextNode_ConditionRows.Remove(row);
        RaiseToolPropertyPanelsChanged();
        RefreshPreviews();
        RequestAutoSave();
    }

    public void ClearNodeSelection()
    {
        foreach (var n in SelectedNodes)
        {
            n.IsSelected = false;
        }
        SelectedNodes.Clear();
    }

    public void SetSingleNodeSelection(ToolGraphNodeViewModel node)
    {
        if (node is null)
        {
            return;
        }

        ClearNodeSelection();
        node.IsSelected = true;
        SelectedNodes.Add(node);
    }

    public void ToggleNodeSelection(ToolGraphNodeViewModel node)
    {
        if (node is null)
        {
            return;
        }

        if (node.IsSelected)
        {
            node.IsSelected = false;
            SelectedNodes.Remove(node);

            if (ReferenceEquals(SelectedNode, node))
            {
                SelectedNode = SelectedNodes.Count > 0 ? SelectedNodes[0] : null;
            }
        }
        else
        {
            node.IsSelected = true;
            SelectedNodes.Add(node);

            // Keep the first-selected node as the primary for properties/preview.
            if (SelectedNode is null)
            {
                SelectedNode = node;
            }
        }
    }

    [ObservableProperty]
    private ToolGraphEdgeViewModel? _selectedEdge;

    [ObservableProperty]
    private ImageSource? _selectedNodePreviewImage;

    [ObservableProperty]
    private ImageSource? _finalPreviewImage;

    [ObservableProperty]
    private ImageSource? _linePreviewImage;

    [ObservableProperty]
    private ImageSource? _pointEdgePreviewImage;

    [ObservableProperty]
    private ImageSource? _blobThresholdPreviewImage;

    public ObservableCollection<OverlayItem> SelectedNodeOverlayItems { get; }

    public ObservableCollection<OverlayItem> FinalOverlayItems { get; }

    public ICommand RefreshConfigsCommand { get; }

    public ICommand LoadConfigCommand { get; }

    public ICommand SaveConfigCommand { get; }

    public ICommand NewGraphCommand { get; }

    public ICommand DeleteSelectedNodeCommand { get; }

    public ICommand DeleteSelectedEdgeCommand { get; }

    public ICommand LoadPreviewImageCommand { get; }

    public ICommand RunFlowCommand { get; }

    public ICommand RoiSelectedCommand { get; }

    public ICommand RoiEditedCommand { get; }

    public ICommand RoiDeletedCommand { get; }

    public ICommand PointClickedCommand { get; }

    public ICommand PointDoubleClickedCommand { get; }

    private VisionConfig? _config;

    private InspectionResult? _lastRun;

    public void SelectEdge(ToolGraphEdgeViewModel? edge)
    {
        SelectedNode = null;
        SelectedEdge = edge;
    }

    private void DeleteSelectedEdge()
    {
        if (SelectedEdge is null)
        {
            return;
        }

        ClearToolInputByEdge(SelectedEdge);
        Edges.Remove(SelectedEdge);
        SelectedEdge = null;
        SyncEdgesToConfig();
        RaiseToolPropertyPanelsChanged();
        RefreshPreviews();
        RequestAutoSave();
    }

    public void DeleteEdge(ToolGraphEdgeViewModel edge)
    {
        if (edge is null)
        {
            return;
        }

        ClearToolInputByEdge(edge);
        Edges.Remove(edge);
        SelectedEdge = null;
        SyncEdgesToConfig();
        RaiseToolPropertyPanelsChanged();
        RefreshPreviews();
        RequestAutoSave();
    }

    private void SyncEdgesToConfig()
    {
        if (_config?.ToolGraph is null)
        {
            return;
        }

        _config.ToolGraph.Edges = Edges
            .Select(e => new ToolGraphEdge
            {
                FromNodeId = e.FromNodeId,
                ToNodeId = e.ToNodeId,
                FromPort = e.FromPort,
                ToPort = e.ToPort
            })
            .ToList();
    }

    private void SyncPreprocessChoices()
    {
        // IMPORTANT: don't let ComboBox list refresh reset SelectedItem and trigger graph mutations.
        _syncingInputs = true;
        try
        {
            var prev = SelectedToolPreprocessChoice;

            AvailablePreprocessChoices.Clear();
            AvailablePreprocessChoices.Add(DefaultPreprocessChoice);

            if (_config is not null)
            {
                foreach (var n in (_config.PreprocessNodes ?? new())
                             .Select(x => x.Name)
                             .Where(x => !string.IsNullOrWhiteSpace(x))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    AvailablePreprocessChoices.Add(n);
                }
            }

            // Restore selection (no graph change because _syncingInputs is true)
            if (string.IsNullOrWhiteSpace(prev) || !AvailablePreprocessChoices.Contains(prev))
            {
                SelectedToolPreprocessChoice = DefaultPreprocessChoice;
            }
            else
            {
                SelectedToolPreprocessChoice = prev;
            }

            OnPropertyChanged(nameof(IsToolWithPreprocessInput));
        }
        finally
        {
            _syncingInputs = false;
        }
    }

    private void SyncSelectedToolPreprocessChoiceFromGraph()
    {
        _syncingInputs = true;
        try
        {
            if (!IsToolWithPreprocessInput || SelectedNode is null)
            {
                SelectedToolPreprocessChoice = DefaultPreprocessChoice;
                OnPropertyChanged(nameof(IsToolWithPreprocessInput));
                return;
            }

            var edge = Edges.FirstOrDefault(e => string.Equals(e.ToNodeId, SelectedNode.Id, StringComparison.OrdinalIgnoreCase)
                                                && string.Equals(e.ToPort, "Pre", StringComparison.OrdinalIgnoreCase));
            if (edge is null)
            {
                SelectedToolPreprocessChoice = DefaultPreprocessChoice;
                return;
            }

            var from = Nodes.FirstOrDefault(n => string.Equals(n.Id, edge.FromNodeId, StringComparison.OrdinalIgnoreCase));
            if (from is null || !string.Equals(from.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
            {
                SelectedToolPreprocessChoice = DefaultPreprocessChoice;
                return;
            }

            if (!AvailablePreprocessChoices.Contains(from.RefName))
            {
                SyncPreprocessChoices();
            }

            SelectedToolPreprocessChoice = string.IsNullOrWhiteSpace(from.RefName) ? DefaultPreprocessChoice : from.RefName;
        }
        finally
        {
            _syncingInputs = false;
        }
    }

    [ObservableProperty]
    private bool _linePreviewEnabled = true;

    [ObservableProperty]
    private bool _pointEdgePreviewEnabled = true;

    [ObservableProperty]
    private bool _preprocessPreviewEnabled = true;

    [ObservableProperty]
    private bool _showRoisInSelectedPreview = true;

    [ObservableProperty]
    private bool _showRoisInFinalPreview = true;

    partial void OnShowRoisInSelectedPreviewChanged(bool value)
    {
        RefreshSelectedPreview();
        RaiseToolPropertyPanelsChanged();
    }

    partial void OnShowRoisInFinalPreviewChanged(bool value)
    {
        _finalPreviewDirty = true;
        RefreshFinalPreview();
        RaiseToolPropertyPanelsChanged();
    }

    partial void OnLinePreviewEnabledChanged(bool value)
    {
        RefreshPreviews();
        RaiseToolPropertyPanelsChanged();
    }

    partial void OnPointEdgePreviewEnabledChanged(bool value)
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
        OnPropertyChanged(nameof(IsCaliperNode));
        OnPropertyChanged(nameof(IsPointNode));
        OnPropertyChanged(nameof(IsAnyDistanceNode));
        OnPropertyChanged(nameof(IsDistanceNode));
        OnPropertyChanged(nameof(IsLineLineDistanceNode));
        OnPropertyChanged(nameof(IsPointLineDistanceNode));
        OnPropertyChanged(nameof(IsAngleNode));
        OnPropertyChanged(nameof(IsEdgePairNode));
        OnPropertyChanged(nameof(IsEdgePairDetectNode));
        OnPropertyChanged(nameof(IsDiameterNode));
        OnPropertyChanged(nameof(IsConditionNode));
        OnPropertyChanged(nameof(IsTextNode));
        OnPropertyChanged(nameof(IsBlobDetectionNode));
        OnPropertyChanged(nameof(IsSurfaceCompareNode));

        OnPropertyChanged(nameof(Point_OffsetX));
        OnPropertyChanged(nameof(Point_OffsetY));
        OnPropertyChanged(nameof(IsBlobDetectionNode));
        OnPropertyChanged(nameof(IsSurfaceCompareNode));
        OnPropertyChanged(nameof(IsCodeDetectionNode));

        OnPropertyChanged(nameof(AvailablePointFindAlgorithms));
        OnPropertyChanged(nameof(Point_Algorithm));
        OnPropertyChanged(nameof(IsPointEdgePointAlgorithm));

        OnPropertyChanged(nameof(Point_Edge_Orientation));
        OnPropertyChanged(nameof(Point_Edge_Polarity));
        OnPropertyChanged(nameof(Point_Edge_StripCount));
        OnPropertyChanged(nameof(Point_Edge_StripWidth));
        OnPropertyChanged(nameof(Point_Edge_StripLength));
        OnPropertyChanged(nameof(Point_Edge_MinEdgeStrength));
        OnPropertyChanged(nameof(PointEdgePreviewEnabled));
        OnPropertyChanged(nameof(PointEdgePreviewImage));

        OnPropertyChanged(nameof(AvailableCircleFindAlgorithms));
        OnPropertyChanged(nameof(AvailableCircleFinderNames));

        OnPropertyChanged(nameof(AvailableIlluminationCorrectionPresets));

        OnPropertyChanged(nameof(AvailablePointNames));
        OnPropertyChanged(nameof(AvailableDistanceRefNames));
        OnPropertyChanged(nameof(AvailableLineNames));
        OnPropertyChanged(nameof(Distance_PointA));
        OnPropertyChanged(nameof(Distance_PointB));
        OnPropertyChanged(nameof(LineLineDistance_LineA));
        OnPropertyChanged(nameof(LineLineDistance_LineB));
        OnPropertyChanged(nameof(PointLineDistance_Point));
        OnPropertyChanged(nameof(PointLineDistance_Line));
        OnPropertyChanged(nameof(Angle_LineA));
        OnPropertyChanged(nameof(Angle_LineB));
        OnPropertyChanged(nameof(EdgePair_RefA));
        OnPropertyChanged(nameof(EdgePair_RefB));

        OnPropertyChanged(nameof(AvailableLineLineDistanceModes));
        OnPropertyChanged(nameof(AvailablePointLineDistanceModes));
        OnPropertyChanged(nameof(LineLineDistance_Mode));
        OnPropertyChanged(nameof(PointLineDistance_Mode));

        OnPropertyChanged(nameof(Condition_InputCount));
        OnPropertyChanged(nameof(Condition_Expression));

        OnPropertyChanged(nameof(TextNode_Text));
        OnPropertyChanged(nameof(TextNode_X));
        OnPropertyChanged(nameof(TextNode_Y));
        OnPropertyChanged(nameof(TextNode_DefaultColor));

        OnPropertyChanged(nameof(UseGray));
        OnPropertyChanged(nameof(IlluminationCorrection));
        OnPropertyChanged(nameof(IlluminationKernel));
        OnPropertyChanged(nameof(ClaheClipLimit));
        OnPropertyChanged(nameof(ClaheTileGrid));
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

        OnPropertyChanged(nameof(Lpd_Canny1));
        OnPropertyChanged(nameof(Lpd_Canny2));
        OnPropertyChanged(nameof(Lpd_HoughThreshold));
        OnPropertyChanged(nameof(Lpd_MinLineLength));
        OnPropertyChanged(nameof(Lpd_MaxLineGap));

        OnPropertyChanged(nameof(Distance_Nominal));
        OnPropertyChanged(nameof(Distance_TolPlus));
        OnPropertyChanged(nameof(Distance_TolMinus));
        OnPropertyChanged(nameof(SelectedRunValue));
        OnPropertyChanged(nameof(SelectedRunPass));
        OnPropertyChanged(nameof(SelectedRunText));

        OnPropertyChanged(nameof(AvailableBlobPolarities));
        OnPropertyChanged(nameof(Blob_Polarity));
        OnPropertyChanged(nameof(Blob_Threshold));
        OnPropertyChanged(nameof(Blob_MinBlobArea));
        OnPropertyChanged(nameof(Blob_MaxBlobArea));
        OnPropertyChanged(nameof(Blob_LastRunCount));

        OnPropertyChanged(nameof(SurfaceCompare_DiffThreshold));
        OnPropertyChanged(nameof(SurfaceCompare_MinBlobArea));
        OnPropertyChanged(nameof(SurfaceCompare_MaxBlobArea));
        OnPropertyChanged(nameof(SurfaceCompare_MinCount));
        OnPropertyChanged(nameof(SurfaceCompare_MaxCount));
        OnPropertyChanged(nameof(SurfaceCompare_MorphKernel));

        OnPropertyChanged(nameof(AvailableCaliperOrientations));
        OnPropertyChanged(nameof(AvailableEdgePolarities));
        OnPropertyChanged(nameof(Caliper_Orientation));
        OnPropertyChanged(nameof(Caliper_Polarity));
        OnPropertyChanged(nameof(Caliper_StripCount));
        OnPropertyChanged(nameof(Caliper_StripWidth));
        OnPropertyChanged(nameof(Caliper_StripLength));
        OnPropertyChanged(nameof(Caliper_MinEdgeStrength));
        OnPropertyChanged(nameof(Caliper_LastRunFound));
        OnPropertyChanged(nameof(Caliper_LastRunAvgStrength));

        OnPropertyChanged(nameof(Epd_Orientation));
        OnPropertyChanged(nameof(Epd_Polarity));
        OnPropertyChanged(nameof(Epd_StripCount));
        OnPropertyChanged(nameof(Epd_StripWidth));
        OnPropertyChanged(nameof(Epd_StripLength));
        OnPropertyChanged(nameof(Epd_MinEdgeStrength));
        OnPropertyChanged(nameof(Epd_MinEdgeSeparationPx));

        OnPropertyChanged(nameof(ShowRoisInSelectedPreview));
        OnPropertyChanged(nameof(ShowRoisInFinalPreview));
    }

    public bool IsLineNode => string.Equals(SelectedNode?.Type, "Line", StringComparison.OrdinalIgnoreCase);

    public bool IsCaliperNode => string.Equals(SelectedNode?.Type, "Caliper", StringComparison.OrdinalIgnoreCase);

    public bool IsPointNode => string.Equals(SelectedNode?.Type, "Point", StringComparison.OrdinalIgnoreCase);

    public bool IsPointEdgePointAlgorithm => Point_Algorithm == PointFindAlgorithm.EdgePoint;

    private PointDefinition? SelectedPointDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.Points.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    public PointFindAlgorithm Point_Algorithm
    {
        get => SelectedPointDef()?.Algorithm ?? PointFindAlgorithm.TemplateMatch;
        set
        {
            var def = SelectedPointDef();
            if (def is null) return;
            if (def.Algorithm == value) return;
            def.Algorithm = value;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPointEdgePointAlgorithm));
        }
    }

    public CaliperOrientation Point_Edge_Orientation
    {
        get => SelectedPointDef()?.EdgePoint.Orientation ?? CaliperOrientation.Vertical;
        set
        {
            var def = SelectedPointDef();
            if (def is null) return;
            if (def.EdgePoint.Orientation == value) return;
            def.EdgePoint.Orientation = value;
            RefreshPreviews();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public EdgePolarity Point_Edge_Polarity
    {
        get => SelectedPointDef()?.EdgePoint.Polarity ?? EdgePolarity.Any;
        set
        {
            var def = SelectedPointDef();
            if (def is null) return;
            if (def.EdgePoint.Polarity == value) return;
            def.EdgePoint.Polarity = value;
            RefreshPreviews();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Point_Edge_StripCount
    {
        get => SelectedPointDef()?.EdgePoint.StripCount ?? 0;
        set
        {
            var def = SelectedPointDef();
            if (def is null) return;
            var v = Math.Clamp(value, 1, 200);
            if (def.EdgePoint.StripCount == v) return;
            def.EdgePoint.StripCount = v;
            RefreshPreviews();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Point_Edge_StripWidth
    {
        get => SelectedPointDef()?.EdgePoint.StripWidth ?? 0;
        set
        {
            var def = SelectedPointDef();
            if (def is null) return;
            var v = Math.Max(1, value);
            if (def.EdgePoint.StripWidth == v) return;
            def.EdgePoint.StripWidth = v;
            RefreshPreviews();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Point_Edge_StripLength
    {
        get => SelectedPointDef()?.EdgePoint.StripLength ?? 0;
        set
        {
            var def = SelectedPointDef();
            if (def is null) return;
            var v = Math.Max(3, value);
            if (def.EdgePoint.StripLength == v) return;
            def.EdgePoint.StripLength = v;
            RefreshPreviews();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public double Point_Edge_MinEdgeStrength
    {
        get => SelectedPointDef()?.EdgePoint.MinEdgeStrength ?? 0.0;
        set
        {
            var def = SelectedPointDef();
            if (def is null) return;
            var v = Math.Max(0.0, value);
            if (Math.Abs(def.EdgePoint.MinEdgeStrength - v) < 0.0000001) return;
            def.EdgePoint.MinEdgeStrength = v;
            RefreshPreviews();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public double Point_OffsetX
    {
        get => SelectedPointDef()?.OffsetPx.X ?? 0.0;
        set
        {
            var def = SelectedPointDef();
            if (def is null) return;
            if (Math.Abs(def.OffsetPx.X - value) < 0.0000001) return;
            def.OffsetPx.X = value;
            RefreshPreviews();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public double Point_OffsetY
    {
        get => SelectedPointDef()?.OffsetPx.Y ?? 0.0;
        set
        {
            var def = SelectedPointDef();
            if (def is null) return;
            if (Math.Abs(def.OffsetPx.Y - value) < 0.0000001) return;
            def.OffsetPx.Y = value;
            RefreshPreviews();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    private void OnPointClicked(PointClickSelection? click)
    {
        if (click is null)
        {
            return;
        }

        if (_config is null || SelectedNode is null)
        {
            return;
        }

        if (string.Equals(SelectedNode.Type, "Text", StringComparison.OrdinalIgnoreCase))
        {
            if (!click.Modifiers.HasFlag(ModifierKeys.Control) || !click.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                return;
            }

            var t = _config.TextNodes.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (t is null)
            {
                return;
            }

            t.X = (int)Math.Round(click.X);
            t.Y = (int)Math.Round(click.Y);

            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
            return;
        }

        if (!string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!click.Modifiers.HasFlag(ModifierKeys.Control) || !click.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        var p = _config.Points.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        if (p is null)
        {
            return;
        }

        if (p.TemplateRoi.Width <= 0 || p.TemplateRoi.Height <= 0)
        {
            return;
        }

        var cx = p.TemplateRoi.X + p.TemplateRoi.Width / 2.0;
        var cy = p.TemplateRoi.Y + p.TemplateRoi.Height / 2.0;

        p.OffsetPx.X = click.X - cx;
        p.OffsetPx.Y = click.Y - cy;

        RaiseToolPropertyPanelsChanged();
        RefreshPreviews();
        RequestAutoSave();
    }

    public bool IsDistanceNode => string.Equals(SelectedNode?.Type, "Distance", StringComparison.OrdinalIgnoreCase);

    public bool IsLineLineDistanceNode => string.Equals(SelectedNode?.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase);

    public bool IsPointLineDistanceNode => string.Equals(SelectedNode?.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase);

    public bool IsAngleNode => string.Equals(SelectedNode?.Type, "Angle", StringComparison.OrdinalIgnoreCase);

    public bool IsConditionNode => string.Equals(SelectedNode?.Type, "Condition", StringComparison.OrdinalIgnoreCase);

    public bool IsTextNode => string.Equals(SelectedNode?.Type, "Text", StringComparison.OrdinalIgnoreCase);

    public bool IsPreprocessNode => string.Equals(SelectedNode?.Type, "Preprocess", StringComparison.OrdinalIgnoreCase);

    public bool IsAnyDistanceNode => IsDistanceNode || IsLineLineDistanceNode || IsPointLineDistanceNode || IsAngleNode || IsEdgePairNode || IsEdgePairDetectNode || IsDiameterNode;

    private AngleDefinition? SelectedAngleDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "Angle", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.Angles.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    private TextNodeDefinition? SelectedTextNodeDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "Text", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.TextNodes.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    public string TextNode_Text
    {
        get => SelectedTextNodeDef()?.Text ?? string.Empty;
        set
        {
            var def = SelectedTextNodeDef();
            if (def is null) return;
            value ??= string.Empty;
            if (string.Equals(def.Text, value, StringComparison.Ordinal)) return;
            def.Text = value;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public int TextNode_X
    {
        get => SelectedTextNodeDef()?.X ?? 0;
        set
        {
            var def = SelectedTextNodeDef();
            if (def is null) return;
            if (def.X == value) return;
            def.X = value;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public int TextNode_Y
    {
        get => SelectedTextNodeDef()?.Y ?? 0;
        set
        {
            var def = SelectedTextNodeDef();
            if (def is null) return;
            if (def.Y == value) return;
            def.Y = value;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public string TextNode_DefaultColor
    {
        get => SelectedTextNodeDef()?.DefaultColor ?? "#FFFFFFFF";
        set
        {
            var def = SelectedTextNodeDef();
            if (def is null) return;
            value ??= "#FFFFFFFF";
            if (string.Equals(def.DefaultColor, value, StringComparison.Ordinal)) return;
            def.DefaultColor = value;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    private EdgePairDefinition? SelectedEdgePairDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    private EdgePairDetectDefinition? SelectedEdgePairDetectDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    private CircleFinderDefinition? SelectedCircleFinderDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    private DiameterDefinition? SelectedDiameterDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.Diameters.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    public string? Angle_LineA
    {
        get => SelectedAngleDef()?.LineA;
        set
        {
            var def = SelectedAngleDef();
            if (def is null) return;
            if (string.Equals(def.LineA, value, StringComparison.OrdinalIgnoreCase)) return;
            def.LineA = value ?? string.Empty;
            SyncInputEdgeForAnglePort("A", value);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public string? Angle_LineB
    {
        get => SelectedAngleDef()?.LineB;
        set
        {
            var def = SelectedAngleDef();
            if (def is null) return;
            if (string.Equals(def.LineB, value, StringComparison.OrdinalIgnoreCase)) return;
            def.LineB = value ?? string.Empty;
            SyncInputEdgeForAnglePort("B", value);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    private void SyncInputEdgeForAnglePort(string port, string? lineName)
    {
        if (_syncingInputs) return;
        if (_config is null || SelectedNode is null) return;
        if (!string.Equals(SelectedNode.Type, "Angle", StringComparison.OrdinalIgnoreCase)) return;

        _syncingInputs = true;
        try
        {
            RemoveEdgesToSelectedNodePort(port);
            if (!string.IsNullOrWhiteSpace(lineName))
            {
                var from = Nodes.FirstOrDefault(n => (string.Equals(n.Type, "Line", StringComparison.OrdinalIgnoreCase)
                                                      || string.Equals(n.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
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

    private LinePairDetectionDefinition? SelectedLinePairDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    private CodeDetectionDefinition? SelectedCodeDetectionDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    public int Lpd_Canny1
    {
        get => SelectedLinePairDef()?.Canny1 ?? 0;
        set
        {
            var d = SelectedLinePairDef();
            if (d is null) return;
            if (d.Canny1 == value) return;
            d.Canny1 = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int Lpd_Canny2
    {
        get => SelectedLinePairDef()?.Canny2 ?? 0;
        set
        {
            var d = SelectedLinePairDef();
            if (d is null) return;
            if (d.Canny2 == value) return;
            d.Canny2 = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int Lpd_HoughThreshold
    {
        get => SelectedLinePairDef()?.HoughThreshold ?? 0;
        set
        {
            var d = SelectedLinePairDef();
            if (d is null) return;
            if (d.HoughThreshold == value) return;
            d.HoughThreshold = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int Lpd_MinLineLength
    {
        get => SelectedLinePairDef()?.MinLineLength ?? 0;
        set
        {
            var d = SelectedLinePairDef();
            if (d is null) return;
            if (d.MinLineLength == value) return;
            d.MinLineLength = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int Lpd_MaxLineGap
    {
        get => SelectedLinePairDef()?.MaxLineGap ?? 0;
        set
        {
            var d = SelectedLinePairDef();
            if (d is null) return;
            if (d.MaxLineGap == value) return;
            d.MaxLineGap = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public bool Cdt_TryHarder
    {
        get => SelectedCodeDetectionDef()?.TryHarder ?? true;
        set
        {
            var d = SelectedCodeDetectionDef();
            if (d is null) return;
            if (d.TryHarder == value) return;
            d.TryHarder = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    private bool GetCdtSym(CodeSymbology sym)
    {
        var d = SelectedCodeDetectionDef();
        return d?.Symbologies?.Contains(sym) ?? false;
    }

    private void SetCdtSym(CodeSymbology sym, bool value)
    {
        var d = SelectedCodeDetectionDef();
        if (d is null) return;
        d.Symbologies ??= new();
        var has = d.Symbologies.Contains(sym);
        if (value && !has) d.Symbologies.Add(sym);
        if (!value && has) d.Symbologies.Remove(sym);
        RefreshPreviews();
        RequestAutoSave();
        RaiseToolPropertyPanelsChanged();
    }

    public bool Cdt_EnableQr
    {
        get => GetCdtSym(CodeSymbology.Qr);
        set => SetCdtSym(CodeSymbology.Qr, value);
    }

    public bool Cdt_EnableBarcode1D
    {
        get => GetCdtSym(CodeSymbology.Barcode1D);
        set => SetCdtSym(CodeSymbology.Barcode1D, value);
    }

    public bool Cdt_EnableDataMatrix
    {
        get => GetCdtSym(CodeSymbology.DataMatrix);
        set => SetCdtSym(CodeSymbology.DataMatrix, value);
    }

    public bool Cdt_EnablePdf417
    {
        get => GetCdtSym(CodeSymbology.Pdf417);
        set => SetCdtSym(CodeSymbology.Pdf417, value);
    }

    public bool Cdt_EnableAztec
    {
        get => GetCdtSym(CodeSymbology.Aztec);
        set => SetCdtSym(CodeSymbology.Aztec, value);
    }

    private PreprocessNodeDefinition? SelectedPreprocessNodeDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.PreprocessNodes.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    private PreprocessSettings? GetActivePreprocessSettingsForUi()
    {
        if (_config is null)
        {
            return null;
        }

        var def = SelectedPreprocessNodeDef();
        return def?.Settings ?? _config.Preprocess;
    }

    public bool UseGray
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.UseGray ?? true;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            if (s.UseGray == value) return;
            s.UseGray = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public bool UseGaussianBlur
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.UseGaussianBlur ?? false;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            if (s.UseGaussianBlur == value) return;
            s.UseGaussianBlur = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int BlurKernel
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.BlurKernel ?? 3;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            if (s.BlurKernel == value) return;
            s.BlurKernel = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public bool UseThreshold
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.UseThreshold ?? false;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            if (s.UseThreshold == value) return;
            s.UseThreshold = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int ThresholdValue
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.ThresholdValue ?? 128;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            if (s.ThresholdValue == value) return;
            s.ThresholdValue = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public bool UseCanny
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.UseCanny ?? false;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            if (s.UseCanny == value) return;
            s.UseCanny = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int Canny1
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.Canny1 ?? 50;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            if (s.Canny1 == value) return;
            s.Canny1 = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int Canny2
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.Canny2 ?? 150;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            if (s.Canny2 == value) return;
            s.Canny2 = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public bool UseMorphology
    {
        get
        {
            var s = GetActivePreprocessSettingsForUi();
            return s?.UseMorphology ?? false;
        }
        set
        {
            var s = GetActivePreprocessSettingsForUi();
            if (s is null) return;
            if (s.UseMorphology == value) return;
            s.UseMorphology = value;
            RefreshPreviews();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int Condition_InputCount
    {
        get
        {
            var def = SelectedConditionDef();
            return def?.InputCount ?? 2;
        }
        set
        {
            var def = SelectedConditionDef();
            if (def is null || SelectedNode is null) return;
            var v = Math.Clamp(value, 1, 16);
            if (def.InputCount == v) return;
            def.InputCount = v;
            SelectedNode.InputCount = v;
            RemoveEdgesToSelectedNodePortsBeyondConditionCount(v);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    public string Condition_Expression
    {
        get
        {
            var def = SelectedConditionDef();
            return def?.Expression ?? string.Empty;
        }
        set
        {
            var def = SelectedConditionDef();
            if (def is null) return;
            value ??= string.Empty;
            if (string.Equals(def.Expression, value, StringComparison.Ordinal)) return;
            def.Expression = value;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    }

    private ConditionDefinition? SelectedConditionDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "Condition", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.Conditions.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    private void RemoveEdgesToSelectedNodePortsBeyondConditionCount(int inputCount)
    {
        if (SelectedNode is null) return;

        for (var i = Edges.Count - 1; i >= 0; i--)
        {
            var e = Edges[i];
            if (!string.Equals(e.ToNodeId, SelectedNode.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (e.ToPort.StartsWith("In", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(e.ToPort.Substring(2), out var idx)
                && idx > inputCount)
            {
                Edges.RemoveAt(i);
            }
        }
    }

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

    public ObservableCollection<string> AvailableDistanceRefNames
    {
        get
        {
            var list = new ObservableCollection<string>();
            if (_config is null) return list;

            foreach (var p in _config.Points.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                list.Add(p);
            }

            foreach (var c in _config.CircleFinders.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (!list.Contains(c)) list.Add(c);
            }

            foreach (var d in _config.Diameters.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (!list.Contains(d)) list.Add(d);
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

            foreach (var c in _config.Calipers.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (!list.Contains(c))
                {
                    list.Add(c);
                }
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
                var from = Nodes.FirstOrDefault(n =>
                    string.Equals(n.RefName, pointName, StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(n.Type, "Point", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n.Type, "Diameter", StringComparison.OrdinalIgnoreCase)));
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
                var from = Nodes.FirstOrDefault(n => (string.Equals(n.Type, "Line", StringComparison.OrdinalIgnoreCase)
                                                      || string.Equals(n.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
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
                var from = Nodes.FirstOrDefault(n =>
                    string.Equals(n.RefName, refName, StringComparison.OrdinalIgnoreCase)
                    && (
                        (string.Equals(port, "P", StringComparison.OrdinalIgnoreCase) && string.Equals(n.Type, "Point", StringComparison.OrdinalIgnoreCase))
                        || (string.Equals(port, "L", StringComparison.OrdinalIgnoreCase)
                            && (string.Equals(n.Type, "Line", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(n.Type, "Caliper", StringComparison.OrdinalIgnoreCase)))
                    ));
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

    private LineToolDefinition? SelectedLineDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.Lines.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    private CaliperDefinition? SelectedCaliperDef()
    {
        if (_config is null || SelectedNode is null) return null;
        if (!string.Equals(SelectedNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase)) return null;
        return _config.Calipers.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
    }

    public ObservableCollection<CaliperOrientation> AvailableCaliperOrientations { get; }
        = new ObservableCollection<CaliperOrientation>((CaliperOrientation[])Enum.GetValues(typeof(CaliperOrientation)));

    public ObservableCollection<IlluminationCorrectionPreset> AvailableIlluminationCorrectionPresets { get; }
        = new ObservableCollection<IlluminationCorrectionPreset>((IlluminationCorrectionPreset[])Enum.GetValues(typeof(IlluminationCorrectionPreset)));

    public ObservableCollection<EdgePolarity> AvailableEdgePolarities { get; }
        = new ObservableCollection<EdgePolarity>((EdgePolarity[])Enum.GetValues(typeof(EdgePolarity)));

    public ObservableCollection<CircleFindAlgorithm> AvailableCircleFindAlgorithms { get; }
        = new ObservableCollection<CircleFindAlgorithm>((CircleFindAlgorithm[])Enum.GetValues(typeof(CircleFindAlgorithm)));

    public ObservableCollection<string> AvailableCircleFinderNames
    {
        get
        {
            var list = new ObservableCollection<string>();
            if (_config is null) return list;
            foreach (var c in _config.CircleFinders.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                list.Add(c);
            }
            return list;
        }
    }

    public CircleFindAlgorithm Cf_Algorithm
    {
        get => SelectedCircleFinderDef()?.Algorithm ?? CircleFindAlgorithm.ContourFit;
        set
        {
            var d = SelectedCircleFinderDef();
            if (d is null) return;
            if (d.Algorithm == value) return;
            d.Algorithm = value;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Cf_MinRadiusPx
    {
        get => SelectedCircleFinderDef()?.MinRadiusPx ?? 0;
        set
        {
            var d = SelectedCircleFinderDef();
            if (d is null) return;
            var v = Math.Max(0, value);
            if (d.MinRadiusPx == v) return;
            d.MinRadiusPx = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Cf_MaxRadiusPx
    {
        get => SelectedCircleFinderDef()?.MaxRadiusPx ?? 0;
        set
        {
            var d = SelectedCircleFinderDef();
            if (d is null) return;
            var v = Math.Max(0, value);
            if (d.MaxRadiusPx == v) return;
            d.MaxRadiusPx = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public double Cf_HoughDp
    {
        get => SelectedCircleFinderDef()?.HoughDp ?? 1.2;
        set
        {
            var d = SelectedCircleFinderDef();
            if (d is null) return;
            var v = Math.Max(0.1, value);
            if (Math.Abs(d.HoughDp - v) < 0.0000001) return;
            d.HoughDp = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public double Cf_HoughMinDistPx
    {
        get => SelectedCircleFinderDef()?.HoughMinDistPx ?? 20;
        set
        {
            var d = SelectedCircleFinderDef();
            if (d is null) return;
            var v = Math.Max(1.0, value);
            if (Math.Abs(d.HoughMinDistPx - v) < 0.0000001) return;
            d.HoughMinDistPx = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public double Cf_HoughParam1
    {
        get => SelectedCircleFinderDef()?.HoughParam1 ?? 120;
        set
        {
            var d = SelectedCircleFinderDef();
            if (d is null) return;
            var v = Math.Max(1.0, value);
            if (Math.Abs(d.HoughParam1 - v) < 0.0000001) return;
            d.HoughParam1 = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public double Cf_HoughParam2
    {
        get => SelectedCircleFinderDef()?.HoughParam2 ?? 30;
        set
        {
            var d = SelectedCircleFinderDef();
            if (d is null) return;
            var v = Math.Max(1.0, value);
            if (Math.Abs(d.HoughParam2 - v) < 0.0000001) return;
            d.HoughParam2 = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Cf_Canny1
    {
        get => SelectedCircleFinderDef()?.Canny1 ?? 80;
        set
        {
            var d = SelectedCircleFinderDef();
            if (d is null) return;
            var v = Math.Max(0, value);
            if (d.Canny1 == v) return;
            d.Canny1 = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Cf_Canny2
    {
        get => SelectedCircleFinderDef()?.Canny2 ?? 200;
        set
        {
            var d = SelectedCircleFinderDef();
            if (d is null) return;
            var v = Math.Max(0, value);
            if (d.Canny2 == v) return;
            d.Canny2 = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public double Cf_MinCircularity
    {
        get => SelectedCircleFinderDef()?.MinCircularity ?? 0.6;
        set
        {
            var d = SelectedCircleFinderDef();
            if (d is null) return;
            var v = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(d.MinCircularity - v) < 0.0000001) return;
            d.MinCircularity = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public string? Dia_CircleRef
    {
        get => SelectedDiameterDef()?.CircleRef;
        set
        {
            var d = SelectedDiameterDef();
            if (d is null) return;
            if (string.Equals(d.CircleRef, value, StringComparison.OrdinalIgnoreCase)) return;
            d.CircleRef = value ?? string.Empty;
            SyncInputEdgeForDiameterPort("C", value);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    private void SyncInputEdgeForDiameterPort(string port, string? circleName)
    {
        if (_syncingInputs) return;
        if (_config is null || SelectedNode is null) return;
        if (!string.Equals(SelectedNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase)) return;

        _syncingInputs = true;
        try
        {
            RemoveEdgesToSelectedNodePort(port);
            if (!string.IsNullOrWhiteSpace(circleName))
            {
                var from = Nodes.FirstOrDefault(n => string.Equals(n.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase)
                                                     && string.Equals(n.RefName, circleName, StringComparison.OrdinalIgnoreCase));
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

    public CaliperOrientation Caliper_Orientation
    {
        get => SelectedCaliperDef()?.Orientation ?? CaliperOrientation.Vertical;
        set
        {
            var d = SelectedCaliperDef();
            if (d is null) return;
            if (d.Orientation == value) return;
            d.Orientation = value;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public EdgePolarity Caliper_Polarity
    {
        get => SelectedCaliperDef()?.Polarity ?? EdgePolarity.Any;
        set
        {
            var d = SelectedCaliperDef();
            if (d is null) return;
            if (d.Polarity == value) return;
            d.Polarity = value;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Caliper_StripCount
    {
        get => SelectedCaliperDef()?.StripCount ?? 0;
        set
        {
            var d = SelectedCaliperDef();
            if (d is null) return;
            var v = Math.Clamp(value, 1, 200);
            if (d.StripCount == v) return;
            d.StripCount = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Caliper_StripWidth
    {
        get => SelectedCaliperDef()?.StripWidth ?? 0;
        set
        {
            var d = SelectedCaliperDef();
            if (d is null) return;
            var v = Math.Max(1, value);
            if (d.StripWidth == v) return;
            d.StripWidth = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Caliper_StripLength
    {
        get => SelectedCaliperDef()?.StripLength ?? 0;
        set
        {
            var d = SelectedCaliperDef();
            if (d is null) return;
            var v = Math.Max(3, value);
            if (d.StripLength == v) return;
            d.StripLength = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public double Caliper_MinEdgeStrength
    {
        get => SelectedCaliperDef()?.MinEdgeStrength ?? 0.0;
        set
        {
            var d = SelectedCaliperDef();
            if (d is null) return;
            var v = Math.Max(0.0, value);
            if (Math.Abs(d.MinEdgeStrength - v) < 0.0000001) return;
            d.MinEdgeStrength = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public CaliperOrientation Epd_Orientation
    {
        get => SelectedEdgePairDetectDef()?.Orientation ?? CaliperOrientation.Vertical;
        set
        {
            var d = SelectedEdgePairDetectDef();
            if (d is null) return;
            if (d.Orientation == value) return;
            d.Orientation = value;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public EdgePolarity Epd_Polarity
    {
        get => SelectedEdgePairDetectDef()?.Polarity ?? EdgePolarity.Any;
        set
        {
            var d = SelectedEdgePairDetectDef();
            if (d is null) return;
            if (d.Polarity == value) return;
            d.Polarity = value;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Epd_StripCount
    {
        get => SelectedEdgePairDetectDef()?.StripCount ?? 0;
        set
        {
            var d = SelectedEdgePairDetectDef();
            if (d is null) return;
            var v = Math.Clamp(value, 1, 200);
            if (d.StripCount == v) return;
            d.StripCount = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Epd_StripWidth
    {
        get => SelectedEdgePairDetectDef()?.StripWidth ?? 0;
        set
        {
            var d = SelectedEdgePairDetectDef();
            if (d is null) return;
            var v = Math.Max(1, value);
            if (d.StripWidth == v) return;
            d.StripWidth = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Epd_StripLength
    {
        get => SelectedEdgePairDetectDef()?.StripLength ?? 0;
        set
        {
            var d = SelectedEdgePairDetectDef();
            if (d is null) return;
            var v = Math.Max(3, value);
            if (d.StripLength == v) return;
            d.StripLength = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public double Epd_MinEdgeStrength
    {
        get => SelectedEdgePairDetectDef()?.MinEdgeStrength ?? 0.0;
        set
        {
            var d = SelectedEdgePairDetectDef();
            if (d is null) return;
            var v = Math.Max(0.0, value);
            if (Math.Abs(d.MinEdgeStrength - v) < 0.0000001) return;
            d.MinEdgeStrength = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public int Epd_MinEdgeSeparationPx
    {
        get => SelectedEdgePairDetectDef()?.MinEdgeSeparationPx ?? 0;
        set
        {
            var d = SelectedEdgePairDetectDef();
            if (d is null) return;
            var v = Math.Max(0, value);
            if (d.MinEdgeSeparationPx == v) return;
            d.MinEdgeSeparationPx = v;
            RunFlow();
            RequestAutoSave();
            OnPropertyChanged();
        }
    }

    public bool? Caliper_LastRunFound
        => _lastRun?.Calipers.FirstOrDefault(x => string.Equals(x.Name, SelectedNode?.RefName, StringComparison.OrdinalIgnoreCase))?.Found;

    public double? Caliper_LastRunAvgStrength
        => _lastRun?.Calipers.FirstOrDefault(x => string.Equals(x.Name, SelectedNode?.RefName, StringComparison.OrdinalIgnoreCase))?.AvgStrength;

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

    private static bool TryClipInfiniteLineToImage(System.Windows.Point p, System.Windows.Point dir, int width, int height, out System.Windows.Point p1, out System.Windows.Point p2)
    {
        p1 = default;
        p2 = default;

        var dx = dir.X;
        var dy = dir.Y;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            return false;
        }

        var ts = new List<double>(4);

        // x = 0
        if (Math.Abs(dx) > 1e-9)
        {
            var t = (0.0 - p.X) / dx;
            var y = p.Y + t * dy;
            if (y >= 0 && y <= height) ts.Add(t);

            // x = width
            t = (width - p.X) / dx;
            y = p.Y + t * dy;
            if (y >= 0 && y <= height) ts.Add(t);
        }

        // y = 0
        if (Math.Abs(dy) > 1e-9)
        {
            var t = (0.0 - p.Y) / dy;
            var x = p.X + t * dx;
            if (x >= 0 && x <= width) ts.Add(t);

            // y = height
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

    private static void AddCircle(ObservableCollection<OverlayItem> dst, double cx, double cy, double radius, System.Windows.Media.Brush stroke, double strokeThickness)
    {
        if (radius <= 0.0)
        {
            return;
        }

        const int steps = 72;
        var prevX = cx + radius;
        var prevY = cy;
        for (var i = 1; i <= steps; i++)
        {
            var a = 2.0 * Math.PI * i / steps;
            var x = cx + Math.Cos(a) * radius;
            var y = cy + Math.Sin(a) * radius;
            dst.Add(new OverlayLineItem { X1 = prevX, Y1 = prevY, X2 = x, Y2 = y, Stroke = stroke, StrokeThickness = strokeThickness, Label = string.Empty });
            prevX = x;
            prevY = y;
        }
    }

    private static void AddCross(ObservableCollection<OverlayItem> dst, double cx, double cy, double size, System.Windows.Media.Brush stroke, double strokeThickness)
    {
        var s = Math.Max(1.0, size);
        dst.Add(new OverlayLineItem { X1 = cx - s, Y1 = cy, X2 = cx + s, Y2 = cy, Stroke = stroke, StrokeThickness = strokeThickness, Label = string.Empty });
        dst.Add(new OverlayLineItem { X1 = cx, Y1 = cy - s, X2 = cx, Y2 = cy + s, Stroke = stroke, StrokeThickness = strokeThickness, Label = string.Empty });
    }

    internal static System.Windows.Media.Brush? TryParseHexBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            var obj = System.Windows.Media.ColorConverter.ConvertFromString(hex);
            if (obj is System.Windows.Media.Color c)
            {
                var b = new System.Windows.Media.SolidColorBrush(c);
                b.Freeze();
                return b;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    internal static string EvaluateTextTemplate(string text, Dictionary<string, ConditionEvaluator.Variable>? vars)
    {
        if (string.IsNullOrEmpty(text) || vars is null || vars.Count == 0)
        {
            return text ?? string.Empty;
        }

        return TextTemplateRegex().Replace(text, m =>
        {
            var inner = m.Groups[1].Value?.Trim() ?? string.Empty;
            if (inner.Length == 0) return string.Empty;

            var fmt = string.Empty;
            var colonIdx = inner.IndexOf(':');
            if (colonIdx >= 0)
            {
                fmt = inner[(colonIdx + 1)..].Trim();
                inner = inner[..colonIdx].Trim();
            }

            var varName = inner;
            var prop = string.Empty;
            var dotIdx = inner.IndexOf('.');
            if (dotIdx >= 0)
            {
                varName = inner[..dotIdx].Trim();
                prop = inner[(dotIdx + 1)..].Trim();
            }

            if (string.IsNullOrWhiteSpace(varName) || !vars.TryGetValue(varName, out var v) || v is null)
            {
                return string.Empty;
            }

            object? valueObj = null;
            if (string.IsNullOrWhiteSpace(prop))
            {
                valueObj = v.Value ?? (object)v.Pass;
            }
            else if (string.Equals(prop, "Pass", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Pass;
            }
            else if (string.Equals(prop, "Value", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Value;
            }
            else if (string.Equals(prop, "Score", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Score;
            }
            else if (string.Equals(prop, "Found", StringComparison.OrdinalIgnoreCase))
            {
                valueObj = v.Found;
            }
            else
            {
                return string.Empty;
            }

            if (valueObj is null)
            {
                return string.Empty;
            }

            if (valueObj is double d)
            {
                return string.IsNullOrWhiteSpace(fmt)
                    ? d.ToString("0.###", CultureInfo.InvariantCulture)
                    : d.ToString(fmt, CultureInfo.InvariantCulture);
            }

            if (valueObj is bool b)
            {
                return b ? "True" : "False";
            }

            if (valueObj is bool bn)
            {
                return bn ? "True" : "False";
            }

            if (valueObj is double dn)
            {
                return string.IsNullOrWhiteSpace(fmt)
                    ? dn.ToString("0.###", CultureInfo.InvariantCulture)
                    : dn.ToString(fmt, CultureInfo.InvariantCulture);
            }

            if (valueObj is IFormattable f && !string.IsNullOrWhiteSpace(fmt))
            {
                return f.ToString(fmt, CultureInfo.InvariantCulture);
            }

            return Convert.ToString(valueObj, CultureInfo.InvariantCulture) ?? string.Empty;
        });
    }

    [GeneratedRegex(@"\$\{([^}]+)\}", RegexOptions.Compiled)]
    internal static partial Regex TextTemplateRegex();

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
            if (SelectedAngleDef() is { } a) return a.Nominal;
            if (SelectedLinePairDef() is { } lpd) return lpd.Nominal;
            if (SelectedEdgePairDef() is { } ep) return ep.Nominal;
            if (SelectedEdgePairDetectDef() is { } epd) return epd.Nominal;
            if (SelectedDiameterDef() is { } dia) return dia.Nominal;
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
            else if (SelectedAngleDef() is { } a)
            {
                if (Math.Abs(a.Nominal - value) < 0.0000001) return;
                a.Nominal = value;
            }
            else if (SelectedLinePairDef() is { } lpd)
            {
                if (Math.Abs(lpd.Nominal - value) < 0.0000001) return;
                lpd.Nominal = value;
            }
            else if (SelectedEdgePairDef() is { } ep)
            {
                if (Math.Abs(ep.Nominal - value) < 0.0000001) return;
                ep.Nominal = value;
            }
            else if (SelectedEdgePairDetectDef() is { } epd)
            {
                if (Math.Abs(epd.Nominal - value) < 0.0000001) return;
                epd.Nominal = value;
            }
            else if (SelectedDiameterDef() is { } dia)
            {
                if (Math.Abs(dia.Nominal - value) < 0.0000001) return;
                dia.Nominal = value;
            }
            else
            {
                return;
            }

            RequestSpecEditPreviewRefresh();
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
            if (SelectedAngleDef() is { } a) return a.TolerancePlus;
            if (SelectedLinePairDef() is { } lpd) return lpd.TolerancePlus;
            if (SelectedEdgePairDef() is { } ep) return ep.TolerancePlus;
            if (SelectedEdgePairDetectDef() is { } epd) return epd.TolerancePlus;
            if (SelectedDiameterDef() is { } dia) return dia.TolerancePlus;
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
            else if (SelectedAngleDef() is { } a)
            {
                if (Math.Abs(a.TolerancePlus - value) < 0.0000001) return;
                a.TolerancePlus = value;
            }
            else if (SelectedLinePairDef() is { } lpd)
            {
                if (Math.Abs(lpd.TolerancePlus - value) < 0.0000001) return;
                lpd.TolerancePlus = value;
            }
            else if (SelectedEdgePairDef() is { } ep)
            {
                if (Math.Abs(ep.TolerancePlus - value) < 0.0000001) return;
                ep.TolerancePlus = value;
            }
            else if (SelectedEdgePairDetectDef() is { } epd)
            {
                if (Math.Abs(epd.TolerancePlus - value) < 0.0000001) return;
                epd.TolerancePlus = value;
            }
            else if (SelectedDiameterDef() is { } dia)
            {
                if (Math.Abs(dia.TolerancePlus - value) < 0.0000001) return;
                dia.TolerancePlus = value;
            }
            else
            {
                return;
            }

            RequestSpecEditPreviewRefresh();
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
            if (SelectedAngleDef() is { } a) return a.ToleranceMinus;
            if (SelectedLinePairDef() is { } lpd) return lpd.ToleranceMinus;
            if (SelectedEdgePairDef() is { } ep) return ep.ToleranceMinus;
            if (SelectedEdgePairDetectDef() is { } epd) return epd.ToleranceMinus;
            if (SelectedDiameterDef() is { } dia) return dia.ToleranceMinus;
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
            else if (SelectedAngleDef() is { } a)
            {
                if (Math.Abs(a.ToleranceMinus - value) < 0.0000001) return;
                a.ToleranceMinus = value;
            }
            else if (SelectedLinePairDef() is { } lpd)
            {
                if (Math.Abs(lpd.ToleranceMinus - value) < 0.0000001) return;
                lpd.ToleranceMinus = value;
            }
            else if (SelectedEdgePairDef() is { } ep)
            {
                if (Math.Abs(ep.ToleranceMinus - value) < 0.0000001) return;
                ep.ToleranceMinus = value;
            }
            else if (SelectedEdgePairDetectDef() is { } epd)
            {
                if (Math.Abs(epd.ToleranceMinus - value) < 0.0000001) return;
                epd.ToleranceMinus = value;
            }
            else if (SelectedDiameterDef() is { } dia)
            {
                if (Math.Abs(dia.ToleranceMinus - value) < 0.0000001) return;
                dia.ToleranceMinus = value;
            }
            else
            {
                return;
            }

            RequestSpecEditPreviewRefresh();
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

            if (string.Equals(SelectedNode.Type, "Angle", StringComparison.OrdinalIgnoreCase))
            {
                var a = _lastRun.Angles.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return a?.ValueDeg;
            }

            if (string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Value;
            }

            if (string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Value;
            }

            if (string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Value;
            }

            if (string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.RadiusPx;
            }

            if (string.Equals(SelectedNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.Diameters.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Value;
            }

            return null;
        }
    }

    public string? SelectedRunText
    {
        get
        {
            if (_lastRun is null || SelectedNode is null) return null;

            if (string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Text;
            }

            if (string.Equals(SelectedNode.Type, "Angle", StringComparison.OrdinalIgnoreCase))
            {
                var a = _lastRun.Angles.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return a is null || double.IsNaN(a.ValueDeg) ? null : $"{a.ValueDeg:0.###}°";
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

            if (string.Equals(SelectedNode.Type, "Angle", StringComparison.OrdinalIgnoreCase))
            {
                var a = _lastRun.Angles.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return a?.Pass;
            }

            if (string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Pass;
            }

            if (string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Pass;
            }

            if (string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Pass;
            }

            if (string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return d?.Found;
            }

            if (string.Equals(SelectedNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.Diameters.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
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
        else if (string.Equals(SelectedNode.Type, "Angle", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.Angles.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
        }
        else if (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase))
        {
            _config.Origin.Name = "Origin";
        }
        else if (string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
        }
        else if (string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
        }
        else if (string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
        }
        else if (string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
        }
        else if (string.Equals(SelectedNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.Diameters.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
        }
        else if (string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
        }
        else if (string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
            if (def is not null) def.Name = newName;
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
                Y = n.Y,
                InputCount = n.InputCount
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

        SyncToolGraphToConfig();

        _configService.SaveConfig(_config);
        RefreshPreviews();
    }

    private void SyncToolGraphToConfig()
    {
        if (_config?.ToolGraph is null)
        {
            return;
        }

        _config.ToolGraph.Nodes.Clear();
        foreach (var n in Nodes)
        {
            _config.ToolGraph.Nodes.Add(new ToolGraphNode
            {
                Id = n.Id,
                Type = n.Type,
                RefName = n.RefName,
                X = n.X,
                Y = n.Y,
                InputCount = n.InputCount
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

        SyncToolGraphToConfig();
        EnsureTemplatePathsAbsolute(_config);

        // Guard: auto-run may happen while templates are not taught yet.
        // In that case, do not attempt to inspect (PatternMatcher may throw); just refresh previews.
        bool HasTemplate(PointDefinition p)
        {
            if (p.TemplateRoi.Width <= 0 || p.TemplateRoi.Height <= 0) return false;
            if (string.IsNullOrWhiteSpace(p.TemplateImageFile)) return false;
            return File.Exists(p.TemplateImageFile);
        }

        var originOk = HasTemplate(_config.Origin);
        var anyPointNeedsTemplate = _config.Points.Any(p =>
            p.Algorithm == PointFindAlgorithm.TemplateMatch
            && (p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0)
            && !HasTemplate(p));

        var graphNeedsOrigin = Nodes.Any(n => string.Equals(n.Type, "Origin", StringComparison.OrdinalIgnoreCase));
        var graphNeedsPoint = Nodes.Any(n => string.Equals(n.Type, "Point", StringComparison.OrdinalIgnoreCase));
        if ((graphNeedsOrigin && !originOk) || (graphNeedsPoint && anyPointNeedsTemplate))
        {
            _lastRun = null;
            RefreshPreviews();
            RaiseToolPropertyPanelsChanged();
            OnPropertyChanged(nameof(Blob_LastRunCount));
            return;
        }

        try
        {
            _lastRun = _inspectionService.Inspect(snap, _config);
        }
        catch
        {
            _lastRun = null;
        }

        RefreshPreviews();
        RaiseToolPropertyPanelsChanged();
        OnPropertyChanged(nameof(Blob_LastRunCount));
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

        if (_config is not null && !string.IsNullOrWhiteSpace(toRemove.RefName))
        {
            if (string.Equals(toRemove.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
            {
                _config.PreprocessNodes.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "Point", StringComparison.OrdinalIgnoreCase))
            {
                _config.Points.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "Line", StringComparison.OrdinalIgnoreCase))
            {
                _config.Lines.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
            {
                _config.Calipers.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "Distance", StringComparison.OrdinalIgnoreCase))
            {
                _config.Distances.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                _config.LineToLineDistances.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                _config.PointToLineDistances.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "Angle", StringComparison.OrdinalIgnoreCase))
            {
                _config.Angles.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "Condition", StringComparison.OrdinalIgnoreCase))
            {
                _config.Conditions.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "Text", StringComparison.OrdinalIgnoreCase))
            {
                _config.TextNodes.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
            {
                _config.BlobDetections.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
            {
                _config.SurfaceCompares.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
            {
                _config.LinePairDetections.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
            {
                _config.EdgePairDetections.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
            {
                _config.CircleFinders.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "Diameter", StringComparison.OrdinalIgnoreCase))
            {
                _config.Diameters.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
            {
                _config.EdgePairs.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
            {
                _config.CodeDetections.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }
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

        _lastRun = null;
        SyncToolGraphToConfig();
        RunFlow();
        RequestAutoSave();
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

        if (string.Equals(node.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.PreprocessNodes.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                _config.PreprocessNodes.Add(new PreprocessNodeDefinition { Name = node.RefName, Settings = new PreprocessSettings() });
            }
            return;
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

        if (string.Equals(node.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.Calipers.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                var def = new CaliperDefinition { Name = node.RefName };
                def.SearchRoi = DefaultRoi();
                _config.Calipers.Add(def);
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

        if (string.Equals(node.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.BlobDetections.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                var def = new BlobDetectionDefinition { Name = node.RefName };
                def.InspectRoi = DefaultRoi();
                _config.BlobDetections.Add(def);
            }
            return;
        }

        if (string.Equals(node.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.SurfaceCompares.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                var def = new SurfaceCompareDefinition { Name = node.RefName };
                def.InspectRoi = DefaultRoi();
                def.TemplateRoi = DefaultRoi();
                _config.SurfaceCompares.Add(def);
                ActiveRoiLabel = $"{node.RefName} SC";
            }
            return;
        }

        if (string.Equals(node.Type, "Text", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.TextNodes.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                // Default position roughly near top-left; user can set by Ctrl+Shift click.
                _config.TextNodes.Add(new TextNodeDefinition
                {
                    Name = node.RefName,
                    Text = node.RefName,
                    X = 10,
                    Y = 10,
                    DefaultColor = "#FFFFFFFF"
                });
            }
            return;
        }

        if (string.Equals(node.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.LinePairDetections.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                var def = new LinePairDetectionDefinition { Name = node.RefName };
                def.SearchRoi = DefaultRoi();
                _config.LinePairDetections.Add(def);
            }
            return;
        }

        if (string.Equals(node.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.EdgePairDetections.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                var def = new EdgePairDetectDefinition { Name = node.RefName };
                def.SearchRoi = DefaultRoi();
                _config.EdgePairDetections.Add(def);
            }
            return;
        }

        if (string.Equals(node.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.CircleFinders.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                var def = new CircleFinderDefinition { Name = node.RefName };
                def.SearchRoi = DefaultRoi();
                _config.CircleFinders.Add(def);
            }
            return;
        }

        if (string.Equals(node.Type, "Diameter", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.Diameters.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                _config.Diameters.Add(new DiameterDefinition { Name = node.RefName });
            }
            return;
        }

        if (string.Equals(node.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.CodeDetections.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                var def = new CodeDetectionDefinition { Name = node.RefName };
                def.SearchRoi = DefaultRoi();
                def.Symbologies = new List<CodeSymbology> { CodeSymbology.Qr, CodeSymbology.Barcode1D, CodeSymbology.DataMatrix, CodeSymbology.Pdf417, CodeSymbology.Aztec };
                _config.CodeDetections.Add(def);
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

        if (string.Equals(node.Type, "Angle", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.Angles.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                _config.Angles.Add(new AngleDefinition { Name = node.RefName });
            }
            return;
        }

        if (string.Equals(node.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.EdgePairs.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                _config.EdgePairs.Add(new EdgePairDefinition { Name = node.RefName });
            }
            return;
        }


        if (string.Equals(node.Type, "Condition", StringComparison.OrdinalIgnoreCase))
        {
            var existed = _config.Conditions.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (!existed)
            {
                var def = new ConditionDefinition { Name = node.RefName, InputCount = Math.Clamp(node.InputCount, 1, 16), Expression = string.Empty };
                _config.Conditions.Add(def);
            }

            if (node.InputCount <= 0)
            {
                node.InputCount = 2;
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
        else if (string.Equals(type, "Caliper", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "CAL";
            exists = n => _config.Calipers.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "LPD";
            exists = n => _config.LinePairDetections.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "EPD";
            exists = n => _config.EdgePairDetections.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "CIR";
            exists = n => _config.CircleFinders.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "Diameter", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "DIA";
            exists = n => _config.Diameters.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
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
        else if (string.Equals(type, "Angle", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "ANG";
            exists = n => _config.Angles.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "EdgePair", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "EP";
            exists = n => _config.EdgePairs.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "Condition", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "C";
            exists = n => _config.Conditions.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "Text", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "T";
            exists = n => _config.TextNodes.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "Preprocess", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "PP";
            exists = n => _config.PreprocessNodes.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "DefectRoi", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "Defect";
            exists = _ => false;
        }
        else if (string.Equals(type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "BLD";
            exists = n => _config.BlobDetections.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "SC";
            exists = n => _config.SurfaceCompares.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
        }
        else if (string.Equals(type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "CDT";
            exists = n => _config.CodeDetections.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
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

        // Enforce single incoming connection for certain ports.
        if (string.Equals(toPort, "Pre", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = Edges.Count - 1; i >= 0; i--)
            {
                var e = Edges[i];
                if (string.Equals(e.ToNodeId, toNode.Id, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.ToPort, toPort, StringComparison.OrdinalIgnoreCase))
                {
                    Edges.RemoveAt(i);
                }
            }
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
        SyncEdgesToConfig();

        // Auto-fill tool inputs based on graph wiring (VisionPro-like).
        if (_config is null)
        {
            return;
        }

        if (string.Equals(toNode.Type, "Distance", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(fromNode.Type, "Point", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fromNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fromNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase)))
        {
            var def = _config.Distances.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (def is not null)
            {
                if (string.Equals(toPort, "A", StringComparison.OrdinalIgnoreCase)) def.PointA = fromNode.RefName;
                else if (string.Equals(toPort, "B", StringComparison.OrdinalIgnoreCase)) def.PointB = fromNode.RefName;
            }
        }
        else if (string.Equals(toNode.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase)
                 && (string.Equals(fromNode.Type, "Line", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(fromNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase)))
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
                         && (string.Equals(fromNode.Type, "Line", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(fromNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase)))
                {
                    def.Line = fromNode.RefName;
                }
            }
        }
        else if (string.Equals(toNode.Type, "Angle", StringComparison.OrdinalIgnoreCase)
                 && (string.Equals(fromNode.Type, "Line", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(fromNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase)))
        {
            var def = _config.Angles.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (def is not null)
            {
                if (string.Equals(toPort, "A", StringComparison.OrdinalIgnoreCase)) def.LineA = fromNode.RefName;
                else if (string.Equals(toPort, "B", StringComparison.OrdinalIgnoreCase)) def.LineB = fromNode.RefName;
            }
        }
        else if (string.Equals(toNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase)
                 && (string.Equals(fromNode.Type, "Line", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(fromNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase)))
        {
            var def = _config.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (def is not null)
            {
                if (string.Equals(toPort, "A", StringComparison.OrdinalIgnoreCase)) def.RefA = fromNode.RefName;
                else if (string.Equals(toPort, "B", StringComparison.OrdinalIgnoreCase)) def.RefB = fromNode.RefName;
            }
        }
        else if (string.Equals(toNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase)
                 && string.Equals(fromNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.Diameters.FirstOrDefault(x => string.Equals(x.Name, toNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (def is not null)
            {
                if (string.Equals(toPort, "C", StringComparison.OrdinalIgnoreCase)) def.CircleRef = fromNode.RefName;
            }
        }

        if (!_syncingInputs)
        {
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
        }
    }

    private void ClearToolInputByEdge(ToolGraphEdgeViewModel edge)
    {
        if (_config is null) return;

        var to = Nodes.FirstOrDefault(n => string.Equals(n.Id, edge.ToNodeId, StringComparison.OrdinalIgnoreCase));
        var from = Nodes.FirstOrDefault(n => string.Equals(n.Id, edge.FromNodeId, StringComparison.OrdinalIgnoreCase));
        if (to is null || from is null) return;

        if (string.Equals(to.Type, "Angle", StringComparison.OrdinalIgnoreCase))
        {
            var def = _config.Angles.FirstOrDefault(x => string.Equals(x.Name, to.RefName, StringComparison.OrdinalIgnoreCase));
            if (def is null) return;

            if (string.Equals(edge.ToPort, "A", StringComparison.OrdinalIgnoreCase)
                && string.Equals(def.LineA, from.RefName, StringComparison.OrdinalIgnoreCase))
            {
                def.LineA = string.Empty;
            }
            else if (string.Equals(edge.ToPort, "B", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(def.LineB, from.RefName, StringComparison.OrdinalIgnoreCase))
            {
                def.LineB = string.Empty;
            }

            return;
        }
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
        _finalPreviewDirty = true;
        RefreshFinalPreview();
        RefreshSelectedPreview();
    }

    private void RefreshFinalPreview()
    {
        if (!_finalPreviewDirty)
        {
            return;
        }

        FinalOverlayItems.Clear();

        using var snap = _sharedImage.GetSnapshot();
        if (snap is null)
        {
            FinalPreviewImage = null;
            _cachedFinalPreviewImage = null;
            return;
        }

        if (_config is not null && PreprocessPreviewEnabled)
        {
            using var processedFinal = _preprocessor.Run(snap, _config.Preprocess);
            _cachedFinalPreviewImage = processedFinal.ToBitmapSource();
            FinalPreviewImage = _cachedFinalPreviewImage;
        }
        else
        {
            _cachedFinalPreviewImage = snap.ToBitmapSource();
            FinalPreviewImage = _cachedFinalPreviewImage;
        }

        if (_config is null)
        {
            _finalPreviewDirty = false;
            return;
        }

        // If user ran the flow, prefer showing overlays from the inspection result
        if (_lastRun is not null)
        {
            AddConfigRois(FinalOverlayItems);
            BuildFinalOverlayFromRunWithConfig(_lastRun, FinalOverlayItems);
        }
        else
        {
            BuildFinalOverlay(snap, FinalOverlayItems);
        }

        _finalPreviewDirty = false;
    }

    private void RefreshSelectedPreview()
    {
        SelectedNodeOverlayItems.Clear();

        using var snap = _sharedImage.GetSnapshot();
        if (snap is null)
        {
            SelectedNodePreviewImage = null;
            LinePreviewImage = null;
            PointEdgePreviewImage = null;
            BlobThresholdPreviewImage = null;
            return;
        }

        if (_config is not null && PreprocessPreviewEnabled)
        {
            if (SelectedNode is not null && string.Equals(SelectedNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
            {
                var def = SelectedPreprocessNodeDef();
                if (def is not null)
                {
                    using var processedSel = _preprocessor.Run(snap, def.Settings);
                    SelectedNodePreviewImage = processedSel.ToBitmapSource();
                }
                else
                {
                    using var processedSel = _preprocessor.Run(snap, _config.Preprocess);
                    SelectedNodePreviewImage = processedSel.ToBitmapSource();
                }
            }
            else
            {
                if (SelectedNode is not null
                    && (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(SelectedNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase)))
                {
                    using var processedSel = ResolveToolPreprocessForPreview(snap, SelectedNode);
                    SelectedNodePreviewImage = processedSel.ToBitmapSource();
                }
                else
                {
                    SelectedNodePreviewImage = _cachedFinalPreviewImage ?? snap.ToBitmapSource();
                }
            }
        }
        else
        {
            SelectedNodePreviewImage = snap.ToBitmapSource();
        }

        UpdateBlobThresholdPreview(snap);

        if (_config is null)
        {
            LinePreviewImage = null;
            PointEdgePreviewImage = null;
            BlobThresholdPreviewImage = null;
            return;
        }

        RefreshLineRoiPreview(snap);
        RefreshPointEdgePreview(snap);

        if (_lastRun is not null)
        {
            if (SelectedNode is not null)
            {
                AddConfigRoisForNode(SelectedNode, SelectedNodeOverlayItems);
                BuildOverlayForNodeFromRunWithConfig(SelectedNode, _lastRun, SelectedNodeOverlayItems);
            }
        }
        else
        {
            if (SelectedNode is not null)
            {
                BuildOverlayForNode(SelectedNode, snap, SelectedNodeOverlayItems);
            }
        }
    }

    private void UpdateBlobThresholdPreview(Mat snap)
    {
        if (_config is null || SelectedNode is null || !string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
        {
            BlobThresholdPreviewImage = null;
            return;
        }

        var def = SelectedBlobDetectionDef();
        if (def is null || def.InspectRoi.Width <= 0 || def.InspectRoi.Height <= 0)
        {
            BlobThresholdPreviewImage = null;
            return;
        }

        using var matForBlob = ResolveToolPreprocessForPreview(snap, SelectedNode);

        var previewRoi = def.InspectRoi;
        if (def.Rois is not null && def.Rois.Count > 0)
        {
            previewRoi = ComputeBlobInspectRoi(def);
        }

        var rect = new OpenCvSharp.Rect(previewRoi.X, previewRoi.Y, previewRoi.Width, previewRoi.Height);
        rect = rect.Intersect(new OpenCvSharp.Rect(0, 0, matForBlob.Width, matForBlob.Height));
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            BlobThresholdPreviewImage = null;
            return;
        }

        using var crop = new Mat(matForBlob, rect);
        using var gray = crop.Channels() == 1 ? crop.Clone() : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var bw = new Mat();

        var thr = Math.Clamp(def.Threshold, 0, 255);
        var thrType = def.Polarity == BlobPolarity.DarkOnLight ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
        Cv2.Threshold(gray, bw, thr, 255, thrType);

        if (def.Rois is not null && def.Rois.Count > 0)
        {
            using var mask = new Mat(bw.Rows, bw.Cols, MatType.CV_8UC1, Scalar.Black);
            var anyInclude = false;

            foreach (var rr in def.Rois)
            {
                if (rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                {
                    continue;
                }

                var rx = rr.Roi.X - rect.X;
                var ry = rr.Roi.Y - rect.Y;
                var r = new OpenCvSharp.Rect(rx, ry, rr.Roi.Width, rr.Roi.Height);
                r = r.Intersect(new OpenCvSharp.Rect(0, 0, bw.Cols, bw.Rows));
                if (r.Width <= 0 || r.Height <= 0)
                {
                    continue;
                }

                if (rr.Mode == BlobRoiMode.Include)
                {
                    anyInclude = true;
                    using var sub = new Mat(mask, r);
                    sub.SetTo(Scalar.White);
                }
            }

            if (!anyInclude)
            {
                mask.SetTo(Scalar.White);
            }

            foreach (var rr in def.Rois)
            {
                if (rr.Mode != BlobRoiMode.Exclude || rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                {
                    continue;
                }

                var rx = rr.Roi.X - rect.X;
                var ry = rr.Roi.Y - rect.Y;
                var r = new OpenCvSharp.Rect(rx, ry, rr.Roi.Width, rr.Roi.Height);
                r = r.Intersect(new OpenCvSharp.Rect(0, 0, bw.Cols, bw.Rows));
                if (r.Width <= 0 || r.Height <= 0)
                {
                    continue;
                }

                using var sub = new Mat(mask, r);
                sub.SetTo(Scalar.Black);
            }

            Cv2.BitwiseAnd(bw, mask, bw);
        }

        Mat view = bw;
        if (bw.Width > 260)
        {
            var scale = 260.0 / bw.Width;
            var h = Math.Max(1, (int)Math.Round(bw.Height * scale));
            var resized = new Mat();
            Cv2.Resize(bw, resized, new OpenCvSharp.Size(260, h), 0, 0, InterpolationFlags.Nearest);
            view = resized;
        }

        try
        {
            BlobThresholdPreviewImage = view.ToBitmapSource();
        }
        finally
        {
            if (!ReferenceEquals(view, bw))
            {
                view.Dispose();
            }
        }
    }

    private void AddConfigRois(ObservableCollection<OverlayItem> dst)
    {
        if (_config is null)
        {
            return;
        }

        var config = _config;

        if (!ShowRoisInFinalPreview)
        {
            return;
        }

        if (config.Origin.SearchRoi.Width > 0 && config.Origin.SearchRoi.Height > 0)
        {
            dst.Add(new OverlayRectItem
            {
                X = config.Origin.SearchRoi.X,
                Y = config.Origin.SearchRoi.Y,
                Width = config.Origin.SearchRoi.Width,
                Height = config.Origin.SearchRoi.Height,
                Stroke = Brushes.Lime,
                Label = "Origin S"
            });
        }

        if (config.Origin.TemplateRoi.Width > 0 && config.Origin.TemplateRoi.Height > 0)
        {
            dst.Add(new OverlayRectItem
            {
                X = config.Origin.TemplateRoi.X,
                Y = config.Origin.TemplateRoi.Y,
                Width = config.Origin.TemplateRoi.Width,
                Height = config.Origin.TemplateRoi.Height,
                Stroke = Brushes.Lime,
                Label = "Origin T"
            });
        }

        foreach (var p in config.Points)
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

        foreach (var l in config.Lines)
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

        foreach (var c in config.Calipers)
        {
            if (c.SearchRoi.Width <= 0 || c.SearchRoi.Height <= 0)
            {
                continue;
            }

            dst.Add(new OverlayRectItem
            {
                X = c.SearchRoi.X,
                Y = c.SearchRoi.Y,
                Width = c.SearchRoi.Width,
                Height = c.SearchRoi.Height,
                Stroke = Brushes.Lime,
                Label = $"{c.Name} Cal"
            });

            var stripCount = Math.Clamp(c.StripCount, 1, 100);
            var stripLength = Math.Max(3, c.StripLength);
            if (stripCount > 0)
            {
                if (c.Orientation == CaliperOrientation.Vertical)
                {
                    var y1 = c.SearchRoi.Y + (c.SearchRoi.Height - stripLength) / 2.0;
                    var y2 = y1 + stripLength;
                    for (var i = 0; i < stripCount; i++)
                    {
                        var x = c.SearchRoi.X + (i + 0.5) * c.SearchRoi.Width / stripCount;
                        dst.Add(new OverlayLineItem { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = Brushes.Lime, StrokeThickness = 1.0 });
                    }
                }
                else
                {
                    var x1 = c.SearchRoi.X + (c.SearchRoi.Width - stripLength) / 2.0;
                    var x2 = x1 + stripLength;
                    for (var i = 0; i < stripCount; i++)
                    {
                        var y = c.SearchRoi.Y + (i + 0.5) * c.SearchRoi.Height / stripCount;
                        dst.Add(new OverlayLineItem { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = Brushes.Lime, StrokeThickness = 1.0 });
                    }
                }
            }
        }

        foreach (var b in _config.BlobDetections)
        {
            if (b.Rois is not null && b.Rois.Count > 0)
            {
                var hasValidInclude = false;
                for (var i = 0; i < b.Rois.Count; i++)
                {
                    var rr = b.Rois[i];
                    if (rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                    {
                        continue;
                    }

                    if (rr.Mode == BlobRoiMode.Exclude)
                    {
                        dst.Add(new OverlayRectItem
                        {
                            X = rr.Roi.X,
                            Y = rr.Roi.Y,
                            Width = rr.Roi.Width,
                            Height = rr.Roi.Height,
                            Stroke = Brushes.Red,
                            Label = $"{b.Name} BX{i + 1}"
                        });
                    }
                    else
                    {
                        hasValidInclude = true;
                        dst.Add(new OverlayRectItem
                        {
                            X = rr.Roi.X,
                            Y = rr.Roi.Y,
                            Width = rr.Roi.Width,
                            Height = rr.Roi.Height,
                            Stroke = Brushes.Gold,
                            Label = $"{b.Name} B{i + 1}"
                        });
                    }
                }

                if (!hasValidInclude && b.InspectRoi.Width > 0 && b.InspectRoi.Height > 0)
                {
                    dst.Add(new OverlayRectItem
                    {
                        X = b.InspectRoi.X,
                        Y = b.InspectRoi.Y,
                        Width = b.InspectRoi.Width,
                        Height = b.InspectRoi.Height,
                        Stroke = Brushes.Gold,
                        Label = $"{b.Name} B"
                    });
                }

                continue;
            }

            if (b.InspectRoi.Width <= 0 || b.InspectRoi.Height <= 0)
            {
                continue;
            }

            dst.Add(new OverlayRectItem
            {
                X = b.InspectRoi.X,
                Y = b.InspectRoi.Y,
                Width = b.InspectRoi.Width,
                Height = b.InspectRoi.Height,
                Stroke = Brushes.Gold,
                Label = $"{b.Name} B"
            });
        }

        void AddSurfaceCompareRoi(string surfaceCompareName)
        {
            var sc = config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, surfaceCompareName, StringComparison.OrdinalIgnoreCase));
            if (sc is null)
            {
                return;
            }

            if (sc.InspectRoi.Width > 0 && sc.InspectRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = sc.InspectRoi.X,
                    Y = sc.InspectRoi.Y,
                    Width = sc.InspectRoi.Width,
                    Height = sc.InspectRoi.Height,
                    Stroke = Brushes.DeepSkyBlue,
                    Label = $"{sc.Name} SC"
                });
            }

            if (sc.TemplateRoi.Width > 0 && sc.TemplateRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = sc.TemplateRoi.X,
                    Y = sc.TemplateRoi.Y,
                    Width = sc.TemplateRoi.Width,
                    Height = sc.TemplateRoi.Height,
                    Stroke = Brushes.DeepSkyBlue,
                    Label = $"{sc.Name} SCT"
                });
            }

            if (sc.Rois is not null && sc.Rois.Count > 0)
            {
                for (var i = 0; i < sc.Rois.Count; i++)
                {
                    var rr = sc.Rois[i];
                    if (rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                    {
                        continue;
                    }

                    if (rr.Mode == BlobRoiMode.Exclude)
                    {
                        dst.Add(new OverlayRectItem
                        {
                            X = rr.Roi.X,
                            Y = rr.Roi.Y,
                            Width = rr.Roi.Width,
                            Height = rr.Roi.Height,
                            Stroke = Brushes.Red,
                            Label = $"{sc.Name} SCX{i + 1}"
                        });
                    }
                    else
                    {
                        dst.Add(new OverlayRectItem
                        {
                            X = rr.Roi.X,
                            Y = rr.Roi.Y,
                            Width = rr.Roi.Width,
                            Height = rr.Roi.Height,
                            Stroke = Brushes.DeepSkyBlue,
                            Label = $"{sc.Name} SC{i + 1}"
                        });
                    }
                }
            }
        }

        foreach (var sc in config.SurfaceCompares)
        {
            AddSurfaceCompareRoi(sc.Name);
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

        var config = _config;

        var showRois = ShowRoisInSelectedPreview;

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
            if (l is not null)
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
                return;
            }

            var c = _config.Calipers.FirstOrDefault(x => string.Equals(x.Name, lineName, StringComparison.OrdinalIgnoreCase));
            if (c is null)
            {
                return;
            }

            if (c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = c.SearchRoi.X,
                    Y = c.SearchRoi.Y,
                    Width = c.SearchRoi.Width,
                    Height = c.SearchRoi.Height,
                    Stroke = Brushes.Gold,
                    Label = $"{c.Name} Cal"
                });
            }
        }

        void AddCircleRoi(string circleName)
        {
            var c = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, circleName, StringComparison.OrdinalIgnoreCase));
            if (c is null)
            {
                return;
            }

            if (c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = c.SearchRoi.X,
                    Y = c.SearchRoi.Y,
                    Width = c.SearchRoi.Width,
                    Height = c.SearchRoi.Height,
                    Stroke = Brushes.MediumPurple,
                    Label = $"{c.Name} CIR"
                });
            }
        }

        void AddDistanceAnchorRoi(string anchorName)
        {
            if (string.IsNullOrWhiteSpace(anchorName))
            {
                return;
            }

            // Point
            if (_config.Points.Any(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase)))
            {
                AddPointRoi(anchorName);
                return;
            }

            // CircleFinder
            if (_config.CircleFinders.Any(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase)))
            {
                AddCircleRoi(anchorName);
                return;
            }

            // Diameter -> CircleFinder
            var dia = _config.Diameters.FirstOrDefault(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase));
            if (dia is not null && !string.IsNullOrWhiteSpace(dia.CircleRef))
            {
                AddCircleRoi(dia.CircleRef);
            }
        }

        void AddBlobRoi(string blobName)
        {
            var b = config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, blobName, StringComparison.OrdinalIgnoreCase));
            if (b is null)
            {
                return;
            }

            if (!showRois)
            {
                return;
            }

            if (b.Rois is not null && b.Rois.Count > 0)
            {
                var hasValidInclude = false;
                for (var i = 0; i < b.Rois.Count; i++)
                {
                    var rr = b.Rois[i];
                    if (rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                    {
                        continue;
                    }

                    if (rr.Mode == BlobRoiMode.Exclude)
                    {
                        dst.Add(new OverlayRectItem
                        {
                            X = rr.Roi.X,
                            Y = rr.Roi.Y,
                            Width = rr.Roi.Width,
                            Height = rr.Roi.Height,
                            Stroke = Brushes.Red,
                            Label = $"{b.Name} BX{i + 1}"
                        });
                    }
                    else
                    {
                        hasValidInclude = true;
                        dst.Add(new OverlayRectItem
                        {
                            X = rr.Roi.X,
                            Y = rr.Roi.Y,
                            Width = rr.Roi.Width,
                            Height = rr.Roi.Height,
                            Stroke = Brushes.Gold,
                            Label = $"{b.Name} B{i + 1}"
                        });
                    }
                }

                if (!hasValidInclude && b.InspectRoi.Width > 0 && b.InspectRoi.Height > 0)
                {
                    dst.Add(new OverlayRectItem
                    {
                        X = b.InspectRoi.X,
                        Y = b.InspectRoi.Y,
                        Width = b.InspectRoi.Width,
                        Height = b.InspectRoi.Height,
                        Stroke = Brushes.Gold,
                        Label = $"{b.Name} B"
                    });
                }

                return;
            }

            if (b.InspectRoi.Width > 0 && b.InspectRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
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

        void AddSurfaceCompareRoi(string surfaceCompareName)
        {
            var sc = config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, surfaceCompareName, StringComparison.OrdinalIgnoreCase));
            if (sc is null)
            {
                return;
            }

            if (!showRois)
            {
                return;
            }

            if (sc.InspectRoi.Width > 0 && sc.InspectRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = sc.InspectRoi.X,
                    Y = sc.InspectRoi.Y,
                    Width = sc.InspectRoi.Width,
                    Height = sc.InspectRoi.Height,
                    Stroke = Brushes.DeepSkyBlue,
                    Label = $"{sc.Name} SC"
                });
            }

            if (sc.TemplateRoi.Width > 0 && sc.TemplateRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = sc.TemplateRoi.X,
                    Y = sc.TemplateRoi.Y,
                    Width = sc.TemplateRoi.Width,
                    Height = sc.TemplateRoi.Height,
                    Stroke = Brushes.DeepSkyBlue,
                    Label = $"{sc.Name} SCT"
                });
            }

            if (sc.Rois is not null && sc.Rois.Count > 0)
            {
                for (var i = 0; i < sc.Rois.Count; i++)
                {
                    var rr = sc.Rois[i];
                    if (rr.Roi.Width <= 0 || rr.Roi.Height <= 0)
                    {
                        continue;
                    }

                    if (rr.Mode == BlobRoiMode.Exclude)
                    {
                        dst.Add(new OverlayRectItem
                        {
                            X = rr.Roi.X,
                            Y = rr.Roi.Y,
                            Width = rr.Roi.Width,
                            Height = rr.Roi.Height,
                            Stroke = Brushes.Red,
                            Label = $"{sc.Name} SCX{i + 1}"
                        });
                    }
                    else
                    {
                        dst.Add(new OverlayRectItem
                        {
                            X = rr.Roi.X,
                            Y = rr.Roi.Y,
                            Width = rr.Roi.Width,
                            Height = rr.Roi.Height,
                            Stroke = Brushes.DeepSkyBlue,
                            Label = $"{sc.Name} SC{i + 1}"
                        });
                    }
                }
            }
        }

        if (string.Equals(node.Type, "Origin", StringComparison.OrdinalIgnoreCase))
        {
            if (showRois && _config.Origin.SearchRoi.Width > 0 && _config.Origin.SearchRoi.Height > 0)
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

            if (showRois && _config.Origin.TemplateRoi.Width > 0 && _config.Origin.TemplateRoi.Height > 0)
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

            if (showRois) AddPointRoi(p.Name);
            return;
        }

        if (string.Equals(node.Type, "Line", StringComparison.OrdinalIgnoreCase))
        {
            var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (l is null)
            {
                return;
            }

            if (showRois) AddLineRoi(l.Name);
            return;
        }

        if (string.Equals(node.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
        {
            var c = _config.Calipers.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (c is null)
            {
                return;
            }

            if (showRois && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = c.SearchRoi.X,
                    Y = c.SearchRoi.Y,
                    Width = c.SearchRoi.Width,
                    Height = c.SearchRoi.Height,
                    Stroke = Brushes.Lime,
                    Label = $"{c.Name} Cal"
                });

                var stripCount = Math.Clamp(c.StripCount, 1, 100);
                var stripLength = Math.Max(3, c.StripLength);
                if (c.Orientation == CaliperOrientation.Vertical)
                {
                    var y1 = c.SearchRoi.Y + (c.SearchRoi.Height - stripLength) / 2.0;
                    var y2 = y1 + stripLength;
                    for (var i = 0; i < stripCount; i++)
                    {
                        var x = c.SearchRoi.X + (i + 0.5) * c.SearchRoi.Width / stripCount;
                        dst.Add(new OverlayLineItem { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = Brushes.Lime, StrokeThickness = 1.0 });
                    }
                }
                else
                {
                    var x1 = c.SearchRoi.X + (c.SearchRoi.Width - stripLength) / 2.0;
                    var x2 = x1 + stripLength;
                    for (var i = 0; i < stripCount; i++)
                    {
                        var y = c.SearchRoi.Y + (i + 0.5) * c.SearchRoi.Height / stripCount;
                        dst.Add(new OverlayLineItem { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = Brushes.Lime, StrokeThickness = 1.0 });
                    }
                }
            }
            return;
        }

        if (string.Equals(node.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
        {
            var l = _config.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (l is null)
            {
                return;
            }

            if (showRois && l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = l.SearchRoi.X,
                    Y = l.SearchRoi.Y,
                    Width = l.SearchRoi.Width,
                    Height = l.SearchRoi.Height,
                    Stroke = Brushes.MediumPurple,
                    Label = $"{l.Name} LP"
                });
            }
            return;
        }

        if (string.Equals(node.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
        {
            var c = _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (c is null)
            {
                return;
            }

            if (showRois && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = c.SearchRoi.X,
                    Y = c.SearchRoi.Y,
                    Width = c.SearchRoi.Width,
                    Height = c.SearchRoi.Height,
                    Stroke = Brushes.Lime,
                    Label = $"{c.Name} C"
                });
            }
            return;
        }

        if (string.Equals(node.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
        {
            if (showRois) AddBlobRoi(node.RefName);
            return;
        }

        if (string.Equals(node.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
        {
            if (showRois) AddSurfaceCompareRoi(node.RefName);
            return;
        }

        if (string.Equals(node.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
        {
            var e = _config.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (e is null)
            {
                return;
            }

            if (showRois && e.SearchRoi.Width > 0 && e.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = e.SearchRoi.X,
                    Y = e.SearchRoi.Y,
                    Width = e.SearchRoi.Width,
                    Height = e.SearchRoi.Height,
                    Stroke = Brushes.MediumPurple,
                    Label = $"{e.Name} EPD"
                });

                var stripCount = Math.Clamp(e.StripCount, 1, 100);
                var stripLength = Math.Max(3, e.StripLength);
                if (e.Orientation == CaliperOrientation.Vertical)
                {
                    var y1 = e.SearchRoi.Y + (e.SearchRoi.Height - stripLength) / 2.0;
                    var y2 = y1 + stripLength;
                    for (var i = 0; i < stripCount; i++)
                    {
                        var x = e.SearchRoi.X + (i + 0.5) * e.SearchRoi.Width / stripCount;
                        dst.Add(new OverlayLineItem { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = Brushes.MediumPurple, StrokeThickness = 1.0 });
                    }
                }
                else
                {
                    var x1 = e.SearchRoi.X + (e.SearchRoi.Width - stripLength) / 2.0;
                    var x2 = x1 + stripLength;
                    for (var i = 0; i < stripCount; i++)
                    {
                        var y = e.SearchRoi.Y + (i + 0.5) * e.SearchRoi.Height / stripCount;
                        dst.Add(new OverlayLineItem { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = Brushes.MediumPurple, StrokeThickness = 1.0 });
                    }
                }
            }

            return;
        }

        if (string.Equals(node.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
        {
            var c = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (c is null)
            {
                return;
            }

            if (showRois && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = c.SearchRoi.X,
                    Y = c.SearchRoi.Y,
                    Width = c.SearchRoi.Width,
                    Height = c.SearchRoi.Height,
                    Stroke = Brushes.MediumPurple,
                    Label = $"{c.Name} CIR"
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

            if (showRois)
            {
                AddDistanceAnchorRoi(d.PointA);
                AddDistanceAnchorRoi(d.PointB);
            }
            return;
        }

        if (string.Equals(node.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var dd = _config.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (dd is null)
            {
                return;
            }

            if (showRois)
            {
                AddLineRoi(dd.LineA);
                AddLineRoi(dd.LineB);
            }
            return;
        }

        if (string.Equals(node.Type, "Angle", StringComparison.OrdinalIgnoreCase))
        {
            var ad = _config.Angles.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (ad is null)
            {
                return;
            }

            if (showRois)
            {
                AddLineRoi(ad.LineA);
                AddLineRoi(ad.LineB);
            }
            return;
        }

        if (string.Equals(node.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            var dd = _config.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (dd is null)
            {
                return;
            }

            if (showRois)
            {
                AddPointRoi(dd.Point);
                AddLineRoi(dd.Line);
            }
            return;
        }

        if (string.Equals(node.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
        {
            var ep = _config.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (ep is null)
            {
                return;
            }

            if (showRois)
            {
                AddLineRoi(ep.RefA);
                AddLineRoi(ep.RefB);
            }
            return;
        }

        if (string.Equals(node.Type, "DefectRoi", StringComparison.OrdinalIgnoreCase))
        {
            if (showRois && _config.DefectConfig.InspectRoi.Width > 0 && _config.DefectConfig.InspectRoi.Height > 0)
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

    private static void BuildFinalOverlayFromRun(InspectionResult run, ObservableCollection<OverlayItem> dst, VisionConfig? config)
    {
        if (run.Origin is not null)
        {
            var mr = run.Origin.MatchRect;
            if (mr.Width > 0 && mr.Height > 0)
            {
                var cx = mr.X + mr.Width / 2.0;
                var cy = mr.Y + mr.Height / 2.0;

                dst.Add(new OverlayLineItem { X1 = mr.X, Y1 = cy, X2 = mr.X + mr.Width, Y2 = cy, Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red });
                dst.Add(new OverlayLineItem { X1 = cx, Y1 = mr.Y, X2 = cx, Y2 = mr.Y + mr.Height, Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red });
            }

            dst.Add(new OverlayPointItem
            {
                X = mr.Width > 0 && mr.Height > 0 ? mr.X + mr.Width / 2.0 : run.Origin.Position.X,
                Y = mr.Width > 0 && mr.Height > 0 ? mr.Y + mr.Height / 2.0 : run.Origin.Position.Y,
                Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red,
                Label = $"Origin: {run.Origin.Score:0.00}"
            });
        }

        foreach (var p in run.Points)
        {
            var mr = p.MatchRect;
            if (mr.Width > 0 && mr.Height > 0)
            {
                var cx = p.Position.X;
                var cy = p.Position.Y;

                dst.Add(new OverlayLineItem { X1 = cx - mr.Width / 2.0, Y1 = cy, X2 = cx + mr.Width / 2.0, Y2 = cy, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                dst.Add(new OverlayLineItem { X1 = cx, Y1 = cy - mr.Height / 2.0, X2 = cx, Y2 = cy + mr.Height / 2.0, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
            }

            dst.Add(new OverlayPointItem
            {
                X = p.Position.X,
                Y = p.Position.Y,
                Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red,
                Label = p.Name
            });
        }

        var distanceAnchorMap = new System.Collections.Generic.Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in run.Points)
        {
            distanceAnchorMap[p.Name] = p.Position;
        }
        foreach (var c in run.CircleFinders)
        {
            if (c.Found)
            {
                distanceAnchorMap[c.Name] = c.Center;
            }
        }
        foreach (var d in run.Diameters)
        {
            if (d.Found)
            {
                distanceAnchorMap[d.Name] = d.Center;
            }
        }

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

        foreach (var lpd in run.LinePairDetections)
        {
            if (!lpd.Found)
            {
                continue;
            }

            dst.Add(new OverlayLineItem { X1 = lpd.L1P1.X, Y1 = lpd.L1P1.Y, X2 = lpd.L1P2.X, Y2 = lpd.L1P2.Y, Stroke = Brushes.MediumPurple, Label = lpd.Name });
            dst.Add(new OverlayLineItem { X1 = lpd.L2P1.X, Y1 = lpd.L2P1.Y, X2 = lpd.L2P2.X, Y2 = lpd.L2P2.Y, Stroke = Brushes.MediumPurple, Label = string.Empty });
            dst.Add(new OverlayLineItem { X1 = lpd.ClosestA.X, Y1 = lpd.ClosestA.Y, X2 = lpd.ClosestB.X, Y2 = lpd.ClosestB.Y, Stroke = lpd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{lpd.Name}: {lpd.Value:0.###}" });
        }

        foreach (var epd in run.EdgePairDetections)
        {
            if (!epd.Found || double.IsNaN(epd.Value))
            {
                continue;
            }

            dst.Add(new OverlayLineItem { X1 = epd.L1P1.X, Y1 = epd.L1P1.Y, X2 = epd.L1P2.X, Y2 = epd.L1P2.Y, Stroke = Brushes.MediumPurple, Label = $"{epd.Name} E1" });
            dst.Add(new OverlayLineItem { X1 = epd.L2P1.X, Y1 = epd.L2P1.Y, X2 = epd.L2P2.X, Y2 = epd.L2P2.Y, Stroke = Brushes.MediumPurple, Label = $"{epd.Name} E2" });
            dst.Add(new OverlayLineItem { X1 = epd.ClosestA.X, Y1 = epd.ClosestA.Y, X2 = epd.ClosestB.X, Y2 = epd.ClosestB.Y, Stroke = epd.Pass ? Brushes.Lime : Brushes.Red, Label = $"{epd.Name}: {epd.Value:0.###}" });
        }

        foreach (var ep in run.EdgePairs)
        {
            if (!ep.Found || double.IsNaN(ep.Value))
            {
                continue;
            }

            dst.Add(new OverlayLineItem { X1 = ep.L1P1.X, Y1 = ep.L1P1.Y, X2 = ep.L1P2.X, Y2 = ep.L1P2.Y, Stroke = Brushes.MediumPurple, Label = ep.RefA });
            dst.Add(new OverlayLineItem { X1 = ep.L2P1.X, Y1 = ep.L2P1.Y, X2 = ep.L2P2.X, Y2 = ep.L2P2.Y, Stroke = Brushes.MediumPurple, Label = ep.RefB });
            dst.Add(new OverlayLineItem { X1 = ep.ClosestA.X, Y1 = ep.ClosestA.Y, X2 = ep.ClosestB.X, Y2 = ep.ClosestB.Y, Stroke = ep.Pass ? Brushes.Lime : Brushes.Red, Label = $"{ep.Name}: {ep.Value:0.###}" });
        }

        foreach (var cal in run.Calipers)
        {
            if (!cal.Found)
            {
                continue;
            }

            dst.Add(new OverlayLineItem
            {
                X1 = cal.LineP1.X,
                Y1 = cal.LineP1.Y,
                X2 = cal.LineP2.X,
                Y2 = cal.LineP2.Y,
                Stroke = Brushes.Gold,
                Label = cal.Name
            });

            var step = Math.Max(1, cal.Points.Count / 60);
            for (var i = 0; i < cal.Points.Count; i += step)
            {
                var p = cal.Points[i];
                dst.Add(new OverlayPointItem { X = p.X, Y = p.Y, Radius = 2.0, Stroke = Brushes.Gold, Label = string.Empty });
            }
        }

        foreach (var cdt in run.CodeDetections)
        {
            if (!cdt.Found)
            {
                continue;
            }

            var bb = cdt.BoundingBox;
            if (bb.Width > 0 && bb.Height > 0)
            {
                dst.Add(new OverlayRectItem { X = bb.X, Y = bb.Y, Width = bb.Width, Height = bb.Height, Stroke = Brushes.Lime, Label = cdt.Name });
                dst.Add(new OverlayPointItem { X = bb.X + 2, Y = bb.Y + 2, Radius = 1.0, Stroke = Brushes.Lime, Label = cdt.Text });
            }
        }

        foreach (var d in run.Distances)
        {
            if (!distanceAnchorMap.TryGetValue(d.PointA, out var pa) || !distanceAnchorMap.TryGetValue(d.PointB, out var pb))
            {
                continue;
            }

            dst.Add(new OverlayLineItem
            {
                X1 = pa.X,
                Y1 = pa.Y,
                X2 = pb.X,
                Y2 = pb.Y,
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

        foreach (var c in run.CircleFinders)
        {
            if (!c.Found || c.RadiusPx <= 0)
            {
                continue;
            }

            AddCircle(dst, c.Center.X, c.Center.Y, c.RadiusPx, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
            AddCross(dst, c.Center.X, c.Center.Y, size: 10.0, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
            dst.Add(new OverlayPointItem { X = c.Center.X, Y = c.Center.Y, Radius = 1.0, Stroke = Brushes.MediumPurple, Label = c.Name });
        }

        foreach (var d in run.Diameters)
        {
            if (!d.Found || double.IsNaN(d.Value) || d.RadiusPx <= 0)
            {
                continue;
            }

            var stroke = d.Pass ? Brushes.Lime : Brushes.Red;
            AddCircle(dst, d.Center.X, d.Center.Y, d.RadiusPx, stroke: stroke, strokeThickness: 2.0);
            AddCross(dst, d.Center.X, d.Center.Y, size: 12.0, stroke: stroke, strokeThickness: 2.0);
            dst.Add(new OverlayPointItem { X = d.Center.X, Y = d.Center.Y, Radius = 1.0, Stroke = stroke, Label = $"{d.Name}: {d.Value:0.###} mm" });
        }

        foreach (var a in run.Angles)
        {
            if (double.IsNaN(a.ValueDeg))
            {
                continue;
            }

            if (!a.Found)
            {
                dst.Add(new OverlayPointItem { X = 12, Y = 12, Radius = 1.0, Stroke = a.Pass ? Brushes.Lime : Brushes.Red, Label = $"{a.Name}: {a.ValueDeg:0.###}°" });
                continue;
            }

            // In final overlay we may not know the current preview image size, so draw short rays.
            var len = 60.0;
            dst.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.ADir.X * len, Y2 = a.Intersection.Y + a.ADir.Y * len, Stroke = Brushes.MediumPurple, Label = a.LineA });
            dst.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.BDir.X * len, Y2 = a.Intersection.Y + a.BDir.Y * len, Stroke = Brushes.Gold, Label = a.LineB });
            AddAngleArc(dst, a.Intersection.X, a.Intersection.Y, a.ADir.X, a.ADir.Y, a.BDir.X, a.BDir.Y, radius: 35.0, stroke: a.Pass ? Brushes.Lime : Brushes.Red);
            dst.Add(new OverlayPointItem { X = a.Intersection.X, Y = a.Intersection.Y, Radius = 3.0, Stroke = a.Pass ? Brushes.Lime : Brushes.Red, Label = $"{a.Name}: {a.ValueDeg:0.###}°" });
        }

        if (run.SurfaceCompares is not null)
        {
            foreach (var sc in run.SurfaceCompares)
            {
                var stroke = sc.Pass ? Brushes.Lime : Brushes.Red;
                var status = sc.Pass ? "OK" : "NG";

                if (sc.Defects is not null && sc.Defects.Count > 0)
                {
                    var n = Math.Min(sc.Defects.Count, 300);
                    for (var i = 0; i < n; i++)
                    {
                        var d = sc.Defects[i];
                        var r = d.BoundingBox;
                        if (r.Width > 0 && r.Height > 0)
                        {
                            dst.Add(new OverlayRectItem
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

                var lx = 12.0;
                var ly = 12.0;
                if (config is not null)
                {
                    var scDef = config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, sc.Name, StringComparison.OrdinalIgnoreCase));
                    if (scDef is not null && scDef.InspectRoi.Width > 0 && scDef.InspectRoi.Height > 0)
                    {
                        if (run.Origin is not null)
                        {
                            var originTeach = new Point2d(config.Origin.WorldPosition.X, config.Origin.WorldPosition.Y);
                            var tr = TransformPose(new Point2d(scDef.InspectRoi.X, scDef.InspectRoi.Y), originTeach, run.Origin.Position, run.Origin.AngleDeg);
                            lx = tr.X + 2;
                            ly = tr.Y + 2;
                        }
                        else
                        {
                            lx = scDef.InspectRoi.X + 2;
                            ly = scDef.InspectRoi.Y + 2;
                        }
                    }
                }

                dst.Add(new OverlayPointItem
                {
                    X = lx,
                    Y = ly,
                    Radius = 1.0,
                    Stroke = stroke,
                    Label = $"{sc.Name} [{status}]: Số lỗi: {sc.Count}, S.Lớn nhất: {sc.MaxArea:0}"
                });
            }
        }

        if (run.Conditions.Count > 0)
        {
            var y = 14.0;
            foreach (var c in run.Conditions)
            {
                var okText = c.Pass ? "OK" : "NG";
                dst.Add(new OverlayPointItem
                {
                    X = 12,
                    Y = y,
                    Radius = 1.0,
                    Stroke = c.Pass ? Brushes.Lime : Brushes.Red,
                    Label = $"{c.Name}: {okText}" + (string.IsNullOrWhiteSpace(c.Error) ? string.Empty : $" ({c.Error})")
                });
                y += 16.0;
            }
        }

        if (config is not null && config.TextNodes is not null && config.TextNodes.Count > 0)
        {
            Dictionary<string, ConditionEvaluator.Variable>? vars = null;
            try
            {
                vars = ConditionEvaluator.BuildVariableMap(run);
            }
            catch
            {
                vars = null;
            }

            foreach (var t in config.TextNodes)
            {
                if (t is null || string.IsNullOrWhiteSpace(t.Name))
                {
                    continue;
                }

                var text = EvaluateTextTemplate(t.Text ?? string.Empty, vars);

                var brush = TryParseHexBrush(t.DefaultColor) ?? Brushes.White;
                if (vars is not null && t.Conditions is not null)
                {
                    foreach (var c in t.Conditions)
                    {
                        if (c is null || string.IsNullOrWhiteSpace(c.Expression)) continue;
                        try
                        {
                            if (ConditionEvaluator.Evaluate(c.Expression, vars))
                            {
                                brush = TryParseHexBrush(c.Color) ?? brush;
                                break;
                            }
                        }
                        catch
                        {
                            // ignore bad expressions
                        }
                    }
                }

                dst.Add(new OverlayTextItem
                {
                    X = t.X,
                    Y = t.Y,
                    Text = text,
                    Foreground = brush,
                    Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0))
                });
            }
        }
    }

    private void BuildOverlayForNodeFromRun(ToolGraphNodeViewModel node, InspectionResult run, ObservableCollection<OverlayItem> dst)
    {
        if (string.Equals(node.Type, "Origin", StringComparison.OrdinalIgnoreCase))
        {
            if (run.Origin is null)
            {
                return;
            }

            var mr = run.Origin.MatchRect;
            if (mr.Width > 0 && mr.Height > 0)
            {
                var cx = mr.X + mr.Width / 2.0;
                var cy = mr.Y + mr.Height / 2.0;

                dst.Add(new OverlayLineItem { X1 = mr.X, Y1 = cy, X2 = mr.X + mr.Width, Y2 = cy, Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red });
                dst.Add(new OverlayLineItem { X1 = cx, Y1 = mr.Y, X2 = cx, Y2 = mr.Y + mr.Height, Stroke = run.Origin.Pass ? Brushes.Lime : Brushes.Red });
            }

            dst.Add(new OverlayPointItem
            {
                X = mr.Width > 0 && mr.Height > 0 ? mr.X + mr.Width / 2.0 : run.Origin.Position.X,
                Y = mr.Width > 0 && mr.Height > 0 ? mr.Y + mr.Height / 2.0 : run.Origin.Position.Y,
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

            var mr = p.MatchRect;
            if (mr.Width > 0 && mr.Height > 0)
            {
                var cx = p.Position.X;
                var cy = p.Position.Y;

                dst.Add(new OverlayLineItem { X1 = cx - mr.Width / 2.0, Y1 = cy, X2 = cx + mr.Width / 2.0, Y2 = cy, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                dst.Add(new OverlayLineItem { X1 = cx, Y1 = cy - mr.Height / 2.0, X2 = cx, Y2 = cy + mr.Height / 2.0, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
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

        if (string.Equals(node.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
        {
            var r = run.Calipers.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (r is null)
            {
                return;
            }

            if (r.Found)
            {
                dst.Add(new OverlayLineItem
                {
                    X1 = r.LineP1.X,
                    Y1 = r.LineP1.Y,
                    X2 = r.LineP2.X,
                    Y2 = r.LineP2.Y,
                    Stroke = Brushes.Lime,
                    Label = r.Name
                });
            }

            if (r.Points is not null)
            {
                var n = Math.Min(r.Points.Count, 60);
                for (var i = 0; i < n; i++)
                {
                    var p = r.Points[i];
                    dst.Add(new OverlayPointItem { X = p.X, Y = p.Y, Radius = 2.0, Stroke = Brushes.Gold, Label = string.Empty });
                }
            }

            return;
        }

        if (string.Equals(node.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
        {
            var r = run.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (r is null || !r.Found || double.IsNaN(r.Value))
            {
                return;
            }

            dst.Add(new OverlayLineItem { X1 = r.L1P1.X, Y1 = r.L1P1.Y, X2 = r.L1P2.X, Y2 = r.L1P2.Y, Stroke = Brushes.MediumPurple, Label = $"{r.Name} E1" });
            dst.Add(new OverlayLineItem { X1 = r.L2P1.X, Y1 = r.L2P1.Y, X2 = r.L2P2.X, Y2 = r.L2P2.Y, Stroke = Brushes.MediumPurple, Label = $"{r.Name} E2" });
            dst.Add(new OverlayLineItem { X1 = r.ClosestA.X, Y1 = r.ClosestA.Y, X2 = r.ClosestB.X, Y2 = r.ClosestB.Y, Stroke = r.Pass ? Brushes.Lime : Brushes.Red, Label = $"{r.Name}: {r.Value:0.###}" });
            return;
        }

        if (string.Equals(node.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
        {
            var c = run.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (c is null || !c.Found || c.RadiusPx <= 0)
            {
                return;
            }

            AddCircle(dst, c.Center.X, c.Center.Y, c.RadiusPx, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
            AddCross(dst, c.Center.X, c.Center.Y, size: 12.0, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
            dst.Add(new OverlayPointItem { X = c.Center.X, Y = c.Center.Y, Radius = 1.0, Stroke = Brushes.MediumPurple, Label = c.Name });
            return;
        }

        if (string.Equals(node.Type, "Diameter", StringComparison.OrdinalIgnoreCase))
        {
            var d = run.Diameters.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (d is null || !d.Found || double.IsNaN(d.Value) || d.RadiusPx <= 0)
            {
                return;
            }

            var stroke = d.Pass ? Brushes.Lime : Brushes.Red;
            AddCircle(dst, d.Center.X, d.Center.Y, d.RadiusPx, stroke: stroke, strokeThickness: 2.0);
            AddCross(dst, d.Center.X, d.Center.Y, size: 12.0, stroke: stroke, strokeThickness: 2.0);
            dst.Add(new OverlayPointItem { X = d.Center.X, Y = d.Center.Y, Radius = 1.0, Stroke = stroke, Label = $"{d.Name}: {d.Value:0.###} mm" });
            return;
        }

        if (string.Equals(node.Type, "Angle", StringComparison.OrdinalIgnoreCase))
        {
            var a = run.Angles.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (a is null || double.IsNaN(a.ValueDeg))
            {
                return;
            }

            if (a.Found)
            {
                if (_lastPreviewImageWidth > 0 && _lastPreviewImageHeight > 0)
                {
                    var ip = new System.Windows.Point(a.Intersection.X, a.Intersection.Y);
                    var aDir = new System.Windows.Point(a.ADir.X, a.ADir.Y);
                    var bDir = new System.Windows.Point(a.BDir.X, a.BDir.Y);

                    if (TryClipInfiniteLineToImage(ip, aDir, _lastPreviewImageWidth, _lastPreviewImageHeight, out var a1, out var a2))
                    {
                        dst.Add(new OverlayLineItem { X1 = a1.X, Y1 = a1.Y, X2 = a2.X, Y2 = a2.Y, Stroke = Brushes.MediumPurple, Label = a.LineA });
                    }
                    else
                    {
                        var len = 60.0;
                        dst.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.ADir.X * len, Y2 = a.Intersection.Y + a.ADir.Y * len, Stroke = Brushes.MediumPurple, Label = a.LineA });
                    }

                    if (TryClipInfiniteLineToImage(ip, bDir, _lastPreviewImageWidth, _lastPreviewImageHeight, out var b1, out var b2))
                    {
                        dst.Add(new OverlayLineItem { X1 = b1.X, Y1 = b1.Y, X2 = b2.X, Y2 = b2.Y, Stroke = Brushes.Gold, Label = a.LineB });
                    }
                    else
                    {
                        var len = 60.0;
                        dst.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.BDir.X * len, Y2 = a.Intersection.Y + a.BDir.Y * len, Stroke = Brushes.Gold, Label = a.LineB });
                    }
                }
                else
                {
                    var len = 60.0;
                    dst.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.ADir.X * len, Y2 = a.Intersection.Y + a.ADir.Y * len, Stroke = Brushes.MediumPurple, Label = a.LineA });
                    dst.Add(new OverlayLineItem { X1 = a.Intersection.X, Y1 = a.Intersection.Y, X2 = a.Intersection.X + a.BDir.X * len, Y2 = a.Intersection.Y + a.BDir.Y * len, Stroke = Brushes.Gold, Label = a.LineB });
                }

                AddAngleArc(dst, a.Intersection.X, a.Intersection.Y, a.ADir.X, a.ADir.Y, a.BDir.X, a.BDir.Y, radius: 35.0, stroke: a.Pass ? Brushes.Lime : Brushes.Red);
                dst.Add(new OverlayPointItem { X = a.Intersection.X, Y = a.Intersection.Y, Radius = 3.0, Stroke = a.Pass ? Brushes.Lime : Brushes.Red, Label = $"{a.Name}: {a.ValueDeg:0.###}°" });
            }
            else
            {
                dst.Add(new OverlayPointItem { X = 12, Y = 12, Radius = 1.0, Stroke = a.Pass ? Brushes.Lime : Brushes.Red, Label = $"{a.Name}: {a.ValueDeg:0.###}°" });
            }

            return;
        }

        if (string.Equals(node.Type, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            var d = run.Distances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (d is null)
            {
                return;
            }

            var anchorMap = new Dictionary<string, Point2d>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in run.Points)
            {
                anchorMap[p.Name] = p.Position;
            }
            foreach (var c in run.CircleFinders)
            {
                if (c.Found)
                {
                    anchorMap[c.Name] = c.Center;
                }
            }
            foreach (var dia in run.Diameters)
            {
                if (dia.Found)
                {
                    anchorMap[dia.Name] = dia.Center;
                }
            }

            if (!anchorMap.TryGetValue(d.PointA, out var a) || !anchorMap.TryGetValue(d.PointB, out var b))
            {
                return;
            }

            void AddAnchorOverlay(string anchorName)
            {
                if (string.IsNullOrWhiteSpace(anchorName))
                {
                    return;
                }

                var p = run.Points.FirstOrDefault(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase));
                if (p is not null)
                {
                    dst.Add(new OverlayPointItem
                    {
                        X = p.Position.X,
                        Y = p.Position.Y,
                        Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red,
                        Label = p.Name
                    });
                    return;
                }

                var c = run.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase));
                if (c is not null && c.Found && c.RadiusPx > 0)
                {
                    AddCircle(dst, c.Center.X, c.Center.Y, c.RadiusPx, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
                    AddCross(dst, c.Center.X, c.Center.Y, size: 12.0, stroke: Brushes.MediumPurple, strokeThickness: 2.0);
                    dst.Add(new OverlayPointItem { X = c.Center.X, Y = c.Center.Y, Radius = 1.0, Stroke = Brushes.MediumPurple, Label = c.Name });
                    return;
                }

                var dia = run.Diameters.FirstOrDefault(x => string.Equals(x.Name, anchorName, StringComparison.OrdinalIgnoreCase));
                if (dia is not null && dia.Found && dia.RadiusPx > 0)
                {
                    var stroke = dia.Pass ? Brushes.Lime : Brushes.Red;
                    AddCircle(dst, dia.Center.X, dia.Center.Y, dia.RadiusPx, stroke: stroke, strokeThickness: 2.0);
                    AddCross(dst, dia.Center.X, dia.Center.Y, size: 12.0, stroke: stroke, strokeThickness: 2.0);
                    dst.Add(new OverlayPointItem { X = dia.Center.X, Y = dia.Center.Y, Radius = 1.0, Stroke = stroke, Label = $"{dia.Name}: {dia.Value:0.###} mm" });
                }
            }

            AddAnchorOverlay(d.PointA);
            AddAnchorOverlay(d.PointB);

            dst.Add(new OverlayLineItem
            {
                X1 = a.X,
                Y1 = a.Y,
                X2 = b.X,
                Y2 = b.Y,
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

            static LineDetectResult? ResolveLineRef(InspectionResult r, string name)
            {
                var l = r.Lines.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (l is not null) return l;

                var c = r.Calipers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (c is not null && c.Found)
                {
                    var dx = c.LineP2.X - c.LineP1.X;
                    var dy = c.LineP2.Y - c.LineP1.Y;
                    var len = Math.Sqrt(dx * dx + dy * dy);
                    return new LineDetectResult(c.Name, c.LineP1, c.LineP2, len, Found: true);
                }

                return null;
            }

            var la = ResolveLineRef(run, dd.RefA);
            var lb = ResolveLineRef(run, dd.RefB);
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

            LineDetectResult? l;
            {
                var ll = run.Lines.FirstOrDefault(x => string.Equals(x.Name, dd.RefB, StringComparison.OrdinalIgnoreCase));
                if (ll is not null) l = ll;
                else
                {
                    var c = run.Calipers.FirstOrDefault(x => string.Equals(x.Name, dd.RefB, StringComparison.OrdinalIgnoreCase));
                    if (c is not null && c.Found)
                    {
                        var dx = c.LineP2.X - c.LineP1.X;
                        var dy = c.LineP2.Y - c.LineP1.Y;
                        var len = Math.Sqrt(dx * dx + dy * dy);
                        l = new LineDetectResult(c.Name, c.LineP1, c.LineP2, len, Found: true);
                    }
                    else
                    {
                        l = null;
                    }
                }
            }

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

        if (string.Equals(node.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
        {
            var sc = run.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (sc is null)
            {
                return;
            }

            var scDef = _config?.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, sc.Name, StringComparison.OrdinalIgnoreCase));
            var stroke = sc.Pass ? Brushes.Lime : Brushes.Red;
            var status = sc.Pass ? "OK" : "NG";

            if (sc.Defects is not null && sc.Defects.Count > 0)
            {
                var n = Math.Min(sc.Defects.Count, 300);
                for (var i = 0; i < n; i++)
                {
                    var d = sc.Defects[i];
                    var r = d.BoundingBox;
                    if (r.Width > 0 && r.Height > 0)
                    {
                        dst.Add(new OverlayRectItem
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

            double lx = 12, ly = 12;
            if (scDef is not null)
            {
                lx = scDef.InspectRoi.X + 2;
                ly = scDef.InspectRoi.Y + 2;
            }

            dst.Add(new OverlayPointItem
            {
                X = lx,
                Y = ly,
                Radius = 1.0,
                Stroke = stroke,
                Label = $"{sc.Name} [{status}]: Số lỗi: {sc.Count}, S.Lớn nhất: {sc.MaxArea:0}"
            });
            return;
        }

        if (string.Equals(node.Type, "Condition", StringComparison.OrdinalIgnoreCase))
        {
            var c = run.Conditions.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (c is null)
            {
                return;
            }

            var okText = c.Pass ? "OK" : "NG";
            dst.Add(new OverlayPointItem
            {
                X = 12,
                Y = 12,
                Radius = 1.0,
                Stroke = c.Pass ? Brushes.Lime : Brushes.Red,
                Label = $"{c.Name}: {okText}" + (string.IsNullOrWhiteSpace(c.Error) ? string.Empty : $" ({c.Error})")
            });
            return;
        }

        if (string.Equals(node.Type, "Text", StringComparison.OrdinalIgnoreCase))
        {
            if (_config is null) return;

            var t = _config.TextNodes.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (t is null) return;

            Dictionary<string, ConditionEvaluator.Variable>? vars = null;
            try { vars = ConditionEvaluator.BuildVariableMap(run); } catch { vars = null; }

            var text = EvaluateTextTemplate(t.Text ?? string.Empty, vars);

            var brush = TryParseHexBrush(t.DefaultColor) ?? Brushes.White;
            if (vars is not null && t.Conditions is not null)
            {
                foreach (var c in t.Conditions)
                {
                    if (c is null || string.IsNullOrWhiteSpace(c.Expression)) continue;
                    try
                    {
                        if (ConditionEvaluator.Evaluate(c.Expression, vars))
                        {
                            brush = TryParseHexBrush(c.Color) ?? brush;
                            break;
                        }
                    }
                    catch { /* ignore bad expressions */ }
                }
            }

            dst.Add(new OverlayTextItem
            {
                X = t.X,
                Y = t.Y,
                Text = text,
                Foreground = brush,
                Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0))
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

        var showRois = ShowRoisInSelectedPreview;

        if (string.Equals(node.Type, "Origin", StringComparison.OrdinalIgnoreCase))
        {
            if (showRois && _config.Origin.SearchRoi.Width > 0 && _config.Origin.SearchRoi.Height > 0)
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

            if (showRois && _config.Origin.TemplateRoi.Width > 0 && _config.Origin.TemplateRoi.Height > 0)
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

            if (showRois && p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0)
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

            if (showRois && p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
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

            if (p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
            {
                var cx = p.TemplateRoi.X + p.TemplateRoi.Width / 2.0;
                var cy = p.TemplateRoi.Y + p.TemplateRoi.Height / 2.0;

                dst.Add(new OverlayPointItem
                {
                    X = cx + p.OffsetPx.X,
                    Y = cy + p.OffsetPx.Y,
                    Stroke = Brushes.DeepSkyBlue,
                    Label = p.Name
                });
            }

            return;
        }

        if (string.Equals(node.Type, "Line", StringComparison.OrdinalIgnoreCase))
        {
            var l = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (l is null)
            {
                return;
            }

            if (showRois && l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
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

        var showRois = ShowRoisInFinalPreview;

        if (showRois && _config.Origin.SearchRoi.Width > 0 && _config.Origin.SearchRoi.Height > 0)
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

        if (showRois && _config.Origin.TemplateRoi.Width > 0 && _config.Origin.TemplateRoi.Height > 0)
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
            if (showRois && p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0)
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

            if (showRois && p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
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
            if (showRois && l.SearchRoi.Width > 0 && l.SearchRoi.Height > 0)
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

        foreach (var b in _config.BlobDetections)
        {
            if (showRois && b.InspectRoi.Width > 0 && b.InspectRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
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

        foreach (var c in _config.CircleFinders)
        {
            if (showRois && c.SearchRoi.Width > 0 && c.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = c.SearchRoi.X,
                    Y = c.SearchRoi.Y,
                    Width = c.SearchRoi.Width,
                    Height = c.SearchRoi.Height,
                    Stroke = Brushes.MediumPurple,
                    Label = $"{c.Name} CIR"
                });
            }
        }

        foreach (var e in _config.EdgePairDetections)
        {
            if (showRois && e.SearchRoi.Width > 0 && e.SearchRoi.Height > 0)
            {
                dst.Add(new OverlayRectItem
                {
                    X = e.SearchRoi.X,
                    Y = e.SearchRoi.Y,
                    Width = e.SearchRoi.Width,
                    Height = e.SearchRoi.Height,
                    Stroke = Brushes.MediumPurple,
                    Label = $"{e.Name} EPD"
                });
            }
        }

        if (showRois && _config.DefectConfig.InspectRoi.Width > 0 && _config.DefectConfig.InspectRoi.Height > 0)
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
    // Keep this aligned with ToolEditorView.xaml node template:
    // Border Padding=8, header StackPanel with 2 lines + Margin bottom=6.
    private const double NodeHeaderHeight = 46.0;
    private const double PortItemHeight = 20.0;
    private const double NodeBottomPadding = 16.0;
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

    [ObservableProperty]
    private int _inputCount = 1;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnInputCountChanged(int value)
    {
        RebuildPorts();
        OnPropertyChanged(nameof(NodeHeight));
    }

    public ObservableCollection<NodePortViewModel> InPorts { get; } = new();

    public ObservableCollection<NodePortViewModel> OutPorts { get; } = new();

    public double NodeHeight
    {
        get
        {
            var count = Math.Max(1, InPorts.Count);
            // Header + ports (top-aligned) + bottom padding.
            return Math.Max(100, NodeHeaderHeight + count * PortItemHeight + NodeBottomPadding);
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
        EnsurePortsInitialized();

        var idx = GetInPortIndex(portName);
        return NodeHeaderHeight + idx * PortItemHeight + PortItemHeight / 2.0;
    }

    public double GetOutPortCenterY(string portName)
    {
        EnsurePortsInitialized();

        var idx = GetOutPortIndex(portName);
        return NodeHeaderHeight + idx * PortItemHeight + PortItemHeight / 2.0;
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

        if (string.Equals(Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
        {
            InPorts.Add(new NodePortViewModel(this, "In", isInput: true));
        }
        else if (string.Equals(Type, "Origin", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(Type, "Point", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(Type, "Line", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(Type, "Caliper", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(Type, "CircleFinder", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(Type, "BlobDetection", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
        {
            InPorts.Add(new NodePortViewModel(this, "In", isInput: true));
            InPorts.Add(new NodePortViewModel(this, "Pre", isInput: true));
        }
        else if (string.Equals(Type, "Diameter", StringComparison.OrdinalIgnoreCase))
        {
            InPorts.Add(new NodePortViewModel(this, "C", isInput: true));
        }
        else if (string.Equals(Type, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            InPorts.Add(new NodePortViewModel(this, "A", isInput: true));
            InPorts.Add(new NodePortViewModel(this, "B", isInput: true));
        }
        else if (string.Equals(Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            InPorts.Add(new NodePortViewModel(this, "A", isInput: true));
            InPorts.Add(new NodePortViewModel(this, "B", isInput: true));
        }
        else if (string.Equals(Type, "Angle", StringComparison.OrdinalIgnoreCase))
        {
            InPorts.Add(new NodePortViewModel(this, "A", isInput: true));
            InPorts.Add(new NodePortViewModel(this, "B", isInput: true));
        }
        else if (string.Equals(Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
        {
            InPorts.Add(new NodePortViewModel(this, "A", isInput: true));
            InPorts.Add(new NodePortViewModel(this, "B", isInput: true));
            InPorts.Add(new NodePortViewModel(this, "Pre", isInput: true));
        }
        else if (string.Equals(Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
        {
            InPorts.Add(new NodePortViewModel(this, "P", isInput: true));
            InPorts.Add(new NodePortViewModel(this, "L", isInput: true));
        }
        else if (string.Equals(Type, "Condition", StringComparison.OrdinalIgnoreCase))
        {
            var n = Math.Clamp(InputCount, 1, 16);
            for (var i = 1; i <= n; i++)
            {
                InPorts.Add(new NodePortViewModel(this, $"In{i}", isInput: true));
            }
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

            // Orthogonal (right-angle) edge: horizontal -> vertical -> horizontal.
            var midX = (p1.X + p2.X) * 0.5;
            if (midX < p1.X + 30) midX = p1.X + 30;
            if (midX > p2.X - 30) midX = p2.X - 30;

            var fig = new PathFigure { StartPoint = p1, IsClosed = false, IsFilled = false };
            fig.Segments.Add(new LineSegment(new System.Windows.Point(midX, p1.Y), true));
            fig.Segments.Add(new LineSegment(new System.Windows.Point(midX, p2.Y), true));
            fig.Segments.Add(new LineSegment(p2, true));
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
        // Out port ellipse is pushed outwards by Margin="0,0,-6,0" in XAML.
        return new System.Windows.Point(_from.X + 166, _from.Y + cy);
    }

    private System.Windows.Point GetToPortPosition()
    {
        _to.EnsurePortsInitialized();
        var cy = _to.GetInPortCenterY(ToPort);
        return new System.Windows.Point(_to.X, _to.Y + cy);
    }
}

