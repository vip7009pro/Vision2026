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

    private readonly DispatcherTimer _blobThresholdPreviewTimer;

    private bool _syncingInputs;

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

        _blobThresholdPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _blobThresholdPreviewTimer.Tick += (_, __) => UpdateBlobThresholdPreviewFromSnapshot();

        AvailableConfigs = new ObservableCollection<string>();
        ToolboxItems = new ObservableCollection<string>
        {
            "Preprocess",
            "Origin",
            "Point",
            "Line",
            "LinePairDetection",
            "Distance",
            "LineLineDistance",
            "PointLineDistance",
            "Condition",
            "BlobDetection",
            "CodeDetection",
            "DefectRoi"
        };

        Nodes = new ObservableCollection<ToolGraphNodeViewModel>();
        Edges = new ObservableCollection<ToolGraphEdgeViewModel>();
        AvailablePreprocessChoices = new ObservableCollection<string>();
        SelectedNodeOverlayItems = new ObservableCollection<OverlayItem>();
        FinalOverlayItems = new ObservableCollection<OverlayItem>();

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

        _sharedImage.ImageChanged += (_, __) => RefreshPreviews();

        RefreshConfigs();
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

    private void BuildFinalOverlayFromRunWithConfig(InspectionResult run, ObservableCollection<OverlayItem> dst)
    {
        BuildFinalOverlayFromRun(run, dst);

        if (_config is null)
        {
            return;
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
    }

    public ObservableCollection<string> AvailablePreprocessChoices { get; }

    public bool IsToolWithPreprocessInput => SelectedNode is not null
                                            && (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase));

    public bool IsBlobDetectionNode => SelectedNode is not null
                                       && string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase);

    public bool IsLinePairDetectionNode => SelectedNode is not null
                                           && string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase);

    public bool IsCodeDetectionNode => SelectedNode is not null
                                       && string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<BlobPolarity> AvailableBlobPolarities { get; }
        = new ObservableCollection<BlobPolarity>((BlobPolarity[])Enum.GetValues(typeof(BlobPolarity)));

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

        if (string.Equals(kind, "LP", StringComparison.OrdinalIgnoreCase))
        {
            var l = _config.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (l is not null) l.SearchRoi = roi;
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

        if (!string.Equals(node.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

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

            if (kind.StartsWith("B", StringComparison.OrdinalIgnoreCase))
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
        RaiseToolPropertyPanelsChanged();
        RefreshPreviews();
        OnPropertyChanged(nameof(Blob_LastRunCount));
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

        DeleteEdge(SelectedEdge);
    }

    public void DeleteEdge(ToolGraphEdgeViewModel edge)
    {
        if (edge is null)
        {
            return;
        }

        Edges.Remove(edge);
        SelectedEdge = null;
        SyncEdgesToConfig();
        SyncSelectedToolPreprocessChoiceFromGraph();
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
    private bool _preprocessPreviewEnabled = true;

    [ObservableProperty]
    private bool _showRoisInSelectedPreview = true;

    [ObservableProperty]
    private bool _showRoisInFinalPreview = true;

    partial void OnShowRoisInSelectedPreviewChanged(bool value)
    {
        RefreshPreviews();
        RaiseToolPropertyPanelsChanged();
    }

    partial void OnShowRoisInFinalPreviewChanged(bool value)
    {
        RefreshPreviews();
        RaiseToolPropertyPanelsChanged();
    }

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
        OnPropertyChanged(nameof(IsLinePairDetectionNode));
        OnPropertyChanged(nameof(IsDistanceNode));
        OnPropertyChanged(nameof(IsLineLineDistanceNode));
        OnPropertyChanged(nameof(IsPointLineDistanceNode));
        OnPropertyChanged(nameof(IsAnyDistanceNode));
        OnPropertyChanged(nameof(IsConditionNode));
        OnPropertyChanged(nameof(IsPreprocessNode));
        OnPropertyChanged(nameof(IsBlobDetectionNode));
        OnPropertyChanged(nameof(IsCodeDetectionNode));

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

        OnPropertyChanged(nameof(Condition_InputCount));
        OnPropertyChanged(nameof(Condition_Expression));

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

        OnPropertyChanged(nameof(ShowRoisInSelectedPreview));
        OnPropertyChanged(nameof(ShowRoisInFinalPreview));
    }

    public bool IsLineNode => string.Equals(SelectedNode?.Type, "Line", StringComparison.OrdinalIgnoreCase);

    public bool IsDistanceNode => string.Equals(SelectedNode?.Type, "Distance", StringComparison.OrdinalIgnoreCase);

    public bool IsLineLineDistanceNode => string.Equals(SelectedNode?.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase);

    public bool IsPointLineDistanceNode => string.Equals(SelectedNode?.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase);

    public bool IsConditionNode => string.Equals(SelectedNode?.Type, "Condition", StringComparison.OrdinalIgnoreCase);

    public bool IsPreprocessNode => string.Equals(SelectedNode?.Type, "Preprocess", StringComparison.OrdinalIgnoreCase);

    public bool IsAnyDistanceNode => IsDistanceNode || IsLineLineDistanceNode || IsPointLineDistanceNode;

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
            if (SelectedLinePairDef() is { } lpd) return lpd.Nominal;
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
            else if (SelectedLinePairDef() is { } lpd)
            {
                if (Math.Abs(lpd.Nominal - value) < 0.0000001) return;
                lpd.Nominal = value;
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
            if (SelectedLinePairDef() is { } lpd) return lpd.TolerancePlus;
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
            else if (SelectedLinePairDef() is { } lpd)
            {
                if (Math.Abs(lpd.TolerancePlus - value) < 0.0000001) return;
                lpd.TolerancePlus = value;
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
            if (SelectedLinePairDef() is { } lpd) return lpd.ToleranceMinus;
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
            else if (SelectedLinePairDef() is { } lpd)
            {
                if (Math.Abs(lpd.ToleranceMinus - value) < 0.0000001) return;
                lpd.ToleranceMinus = value;
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

            if (string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
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
                var r = _lastRun.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return r is not null && r.Found ? r.Text : null;
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

            if (string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
            {
                var d = _lastRun.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
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
        var anyPointNeedsTemplate = _config.Points.Any(p => (p.SearchRoi.Width > 0 && p.SearchRoi.Height > 0) && !HasTemplate(p));
        if (!originOk || anyPointNeedsTemplate)
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

            if (string.Equals(toRemove.Type, "Condition", StringComparison.OrdinalIgnoreCase))
            {
                _config.Conditions.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
            {
                _config.BlobDetections.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(toRemove.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
            {
                _config.LinePairDetections.RemoveAll(x => string.Equals(x.Name, toRemove.RefName, StringComparison.OrdinalIgnoreCase));
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
        else if (string.Equals(type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "LPD";
            exists = n => _config.LinePairDetections.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
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
        else if (string.Equals(type, "Condition", StringComparison.OrdinalIgnoreCase))
        {
            baseName = "C";
            exists = n => _config.Conditions.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
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

                using var processedFinal = _preprocessor.Run(snap, _config.Preprocess);
                FinalPreviewImage = processedFinal.ToBitmapSource();
            }
            else
            {
                using var processedFinal = _preprocessor.Run(snap, _config.Preprocess);
                FinalPreviewImage = processedFinal.ToBitmapSource();

                if (SelectedNode is not null
                    && (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase)))
                {
                    using var processedSel = ResolveToolPreprocessForPreview(snap, SelectedNode);
                    SelectedNodePreviewImage = processedSel.ToBitmapSource();
                }
                else
                {
                    SelectedNodePreviewImage = processedFinal.ToBitmapSource();
                }
            }
        }
        else
        {
            SelectedNodePreviewImage = snap.ToBitmapSource();
            FinalPreviewImage = snap.ToBitmapSource();
        }

        UpdateBlobThresholdPreview(snap);

        if (_config is null)
        {
            LinePreviewImage = null;
            BlobThresholdPreviewImage = null;
            return;
        }

        RefreshLineRoiPreview(snap);

        // If user ran the flow, prefer showing overlays from the inspection result
        if (_lastRun is not null)
        {
            AddConfigRois(FinalOverlayItems);
            BuildFinalOverlayFromRunWithConfig(_lastRun, FinalOverlayItems);
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

            BuildFinalOverlay(snap, FinalOverlayItems);
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

        if (!ShowRoisInFinalPreview)
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

        void AddBlobRoi(string blobName)
        {
            var b = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, blobName, StringComparison.OrdinalIgnoreCase));
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

        if (string.Equals(node.Type, "Distance", StringComparison.OrdinalIgnoreCase))
        {
            var d = _config.Distances.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
            if (d is null)
            {
                return;
            }

            if (showRois)
            {
                AddPointRoi(d.PointA);
                AddPointRoi(d.PointB);
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

    private static void BuildFinalOverlayFromRun(InspectionResult run, ObservableCollection<OverlayItem> dst)
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
                var cx = mr.X + mr.Width / 2.0;
                var cy = mr.Y + mr.Height / 2.0;

                dst.Add(new OverlayLineItem { X1 = mr.X, Y1 = cy, X2 = mr.X + mr.Width, Y2 = cy, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                dst.Add(new OverlayLineItem { X1 = cx, Y1 = mr.Y, X2 = cx, Y2 = mr.Y + mr.Height, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
            }

            dst.Add(new OverlayPointItem
            {
                X = mr.Width > 0 && mr.Height > 0 ? mr.X + mr.Width / 2.0 : p.Position.X,
                Y = mr.Width > 0 && mr.Height > 0 ? mr.Y + mr.Height / 2.0 : p.Position.Y,
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
    }

    private static void BuildOverlayForNodeFromRun(ToolGraphNodeViewModel node, InspectionResult run, ObservableCollection<OverlayItem> dst)
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
                var cx = mr.X + mr.Width / 2.0;
                var cy = mr.Y + mr.Height / 2.0;

                dst.Add(new OverlayLineItem { X1 = mr.X, Y1 = cy, X2 = mr.X + mr.Width, Y2 = cy, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
                dst.Add(new OverlayLineItem { X1 = cx, Y1 = mr.Y, X2 = cx, Y2 = mr.Y + mr.Height, Stroke = p.Pass ? Brushes.DeepSkyBlue : Brushes.Red });
            }

            dst.Add(new OverlayPointItem
            {
                X = mr.Width > 0 && mr.Height > 0 ? mr.X + mr.Width / 2.0 : p.Position.X,
                Y = mr.Width > 0 && mr.Height > 0 ? mr.Y + mr.Height / 2.0 : p.Position.Y,
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
                 || string.Equals(Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(Type, "BlobDetection", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
        {
            InPorts.Add(new NodePortViewModel(this, "In", isInput: true));
            InPorts.Add(new NodePortViewModel(this, "Pre", isInput: true));
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
