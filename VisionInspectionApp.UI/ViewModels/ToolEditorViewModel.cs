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
namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _statusBarText = "Ready.";
        public void ShowPortValueDialog(ToolGraphNodeViewModel node, string portName)
        {
            if (_lastRun is null)
            {
                var msg = _lastRunError ?? "Vui lòng bấm Runflow trước khi xem giá trị Ouput";
                System.Windows.MessageBox.Show(msg, "Không có dữ liệu", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
    
            string val = "Chưa có giá trị.";
            try
            {
                if (string.Equals(node.Type, "Point", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.Points?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Found: {res.Pass}\r\nX: {res.Position.X:F3}\r\nY: {res.Position.Y:F3}\r\nAngle: {res.AngleDeg:F3} deg\r\nScore: {res.Score:F3}";
                }
                else if (string.Equals(node.Type, "Line", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.Lines?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Found: {res.Found}\r\nP1: ({res.P1.X:F3}, {res.P1.Y:F3})\r\nP2: ({res.P2.X:F3}, {res.P2.Y:F3})\r\nLengthPx: {res.LengthPx:F3}";
                }
                else if (string.Equals(node.Type, "Distance", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.Distances?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Pass: {res.Pass}\r\nDistance: {res.Value:F3}\r\nNominal: {res.Nominal:F3}";
                }
                else if (string.Equals(node.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.LineToLineDistances?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Pass: {res.Pass}\r\nDistance: {res.Value:F3}\r\nClosestA: ({res.ClosestA.X:F3}, {res.ClosestA.Y:F3})\r\nClosestB: ({res.ClosestB.X:F3}, {res.ClosestB.Y:F3})";
                }
                else if (string.Equals(node.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.PointToLineDistances?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Pass: {res.Pass}\r\nDistance: {res.Value:F3}\r\nClosestA: ({res.ClosestA.X:F3}, {res.ClosestA.Y:F3})\r\nClosestB: ({res.ClosestB.X:F3}, {res.ClosestB.Y:F3})";
                }
                else if (string.Equals(node.Type, "Angle", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.Angles?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Pass: {res.Pass}\r\nAngle: {res.ValueDeg:F3} deg\r\nIntersection: ({res.Intersection.X:F3}, {res.Intersection.Y:F3})";
                }
                else if (string.Equals(node.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.EdgePairs?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Found: {res.Found}\r\nPass: {res.Pass}\r\nDistance: {res.Value:F3}\r\nClosestA: ({res.ClosestA.X:F3}, {res.ClosestA.Y:F3})\r\nClosestB: ({res.ClosestB.X:F3}, {res.ClosestB.Y:F3})";
                }
                else if (string.Equals(node.Type, "Diameter", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.Diameters?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Found: {res.Found}\r\nPass: {res.Pass}\r\nDiameter: {res.Value:F3}\r\nCenter: ({res.Center.X:F3}, {res.Center.Y:F3})";
                }
                else if (string.Equals(node.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.Calipers?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Found: {res.Found}\r\nEdges Count: {res.Points?.Count ?? 0}\r\nLineP1: ({res.LineP1.X:F3}, {res.LineP1.Y:F3})\r\nLineP2: ({res.LineP2.X:F3}, {res.LineP2.Y:F3})\r\nAvgStrength: {res.AvgStrength:F3}";
                }
                else if (string.Equals(node.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.EdgePairDetections?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Found: {res.Found}\r\nPass: {res.Pass}\r\nDistance: {res.Value:F3}\r\nEdge1Points Count: {res.Edge1Points?.Count ?? 0}\r\nEdge2Points Count: {res.Edge2Points?.Count ?? 0}";
                }
                else if (string.Equals(node.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.CircleFinders?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Found: {res.Found}\r\nCenter: ({res.Center.X:F3}, {res.Center.Y:F3})\r\nRadius: {res.RadiusPx:F3}\r\nScore: {res.Score:F3}";
                }
                else if (string.Equals(node.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.BlobDetections?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Count: {res.Count}";
                }
                else if (string.Equals(node.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.SurfaceCompares?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Pass: {res.Pass}\r\nDefect Count: {res.Count}\r\nMax Area: {res.MaxArea:F3}";
                }
                else if (string.Equals(node.Type, "Condition", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.Conditions?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Pass: {res.Pass}\r\nExpression: {res.Expression}\r\nError: {res.Error ?? "None"}";
                }
            }
            catch (Exception ex)
            {
                val = $"Lỗi khi lấy giá trị: {ex.Message}";
            }
    
            var dlg = new VisionInspectionApp.UI.Views.PortValueDialog(node.RefName ?? node.Type, portName, val);
            dlg.ShowDialog();
        }
    
        private readonly IConfigService _configService;
        private readonly ConfigStoreOptions _storeOptions;
        private readonly SharedImageContext _sharedImage;
        private readonly ImagePreprocessor _preprocessor;
        private readonly LineDetector _lineDetector;
        private readonly IInspectionService _inspectionService;
        private readonly CameraService _cameraService;
        private ToolGraphNodeViewModel? _selectedNodeHook;
        private string? _selectedNodePrevRefName;
        private readonly DispatcherTimer _autoSaveTimer;
        private bool _autoSavePending;
        private bool _syncingInputs;
        [ObservableProperty]
        private double _canvasZoom = 1.0;
        public ToolEditorViewModel(IConfigService configService, ConfigStoreOptions storeOptions, SharedImageContext sharedImage, ImagePreprocessor preprocessor, LineDetector lineDetector, IInspectionService inspectionService, CameraService cameraService)
        {
            _configService = configService;
            _storeOptions = storeOptions;
            _sharedImage = sharedImage;
            _preprocessor = preprocessor;
            _lineDetector = lineDetector;
            _inspectionService = inspectionService;
            _cameraService = cameraService;
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _autoSaveTimer.Tick += (_, __) => AutoSaveNow();
            _specEditPreviewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _specEditPreviewTimer.Tick += (_, __) =>
            {
                _specEditPreviewTimer.Stop();
                RefreshPreviews();
            };
            _blobThresholdPreviewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _blobThresholdPreviewTimer.Tick += (_, __) => UpdateBlobThresholdPreviewFromSnapshot();
            AvailableConfigs = new ObservableCollection<string>();
            ToolboxItems = new ObservableCollection<string>
            {
                "ImageSource",
                "Preprocess",
                "Origin",
                "Point",
                "Line",
                "Caliper",
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
                "CodeDetection"
            };
            Nodes = new ObservableCollection<ToolGraphNodeViewModel>();
            Edges = new ObservableCollection<ToolGraphEdgeViewModel>();
            AvailablePreprocessChoices = new ObservableCollection<string>();
            SelectedNodeOverlayItems = new List<OverlayItem>();
            FinalOverlayItems = new List<OverlayItem>();
            TextNode_ConditionRows = new ObservableCollection<TextColorConditionRow>();
            RefreshConfigsCommand = new RelayCommand(RefreshConfigs);
            LoadConfigCommand = new RelayCommand(LoadConfig);
            SaveConfigCommand = new RelayCommand(SaveConfig);
            NewGraphCommand = new RelayCommand(NewGraph);
            DeleteSelectedNodeCommand = new RelayCommand(DeleteSelectedNode);
            DeleteSelectedEdgeCommand = new RelayCommand(DeleteSelectedEdge);
            CopySelectedNodeCommand = new RelayCommand(CopySelectedNode);
            PasteNodeCommand = new RelayCommand(PasteNode);
            LoadPreviewImageCommand = new RelayCommand(LoadPreviewImage);
            CaptureCameraImageCommand = new AsyncRelayCommand(CaptureCameraImageAsync);
            RunFlowCommand = new RelayCommand(RunFlow);
            RoiSelectedCommand = new RelayCommand<object?>(OnRoiSelected);
            RoiEditedCommand = new RelayCommand<RoiSelection?>(OnRoiEdited);
            RoiDeletedCommand = new RelayCommand<string?>(OnRoiDeleted);
            PointClickedCommand = new RelayCommand<PointClickSelection?>(OnPointClicked);
            PointDoubleClickedCommand = new RelayCommand<PointClickSelection?>(OnPointDoubleClicked);
            TextNode_AddConditionCommand = new RelayCommand(TextNode_AddCondition);
            TextNode_RemoveConditionCommand = new RelayCommand<TextColorConditionRow?>(TextNode_RemoveCondition);
            TextNode_PickDefaultColorCommand = new RelayCommand(TextNode_PickDefaultColor);
            TextNode_PickConditionColorCommand = new RelayCommand<TextColorConditionRow?>(TextNode_PickConditionColor);
            ImageSource_BrowseFileCommand = new RelayCommand(ImageSource_BrowseFile);
            ImageSource_BrowseFolderCommand = new RelayCommand(ImageSource_BrowseFolder);
            SurfaceCompare_SetSearchRoiCommand = new RelayCommand(SurfaceCompare_SetSearchRoi);
            SurfaceCompare_SetTemplateRoiCommand = new RelayCommand(SurfaceCompare_SetTemplateRoi);
            _sharedImage.ImageChanged += (_, __) =>
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefreshPreviews();
                }));
            };
            _cameraService.FrameCaptured += OnCameraFrameCaptured;
            RefreshConfigs();
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
                if (s is null)
                    return;
                if (s.IlluminationCorrection == value)
                    return;
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
                if (s is null)
                    return;
                var v = Math.Clamp(value, 3, 401);
                if (v % 2 == 0)
                    v += 1;
                if (s.IlluminationKernel == v)
                    return;
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
                if (s is null)
                    return;
                var v = Math.Clamp(value, 0.1, 40.0);
                if (Math.Abs(s.ClaheClipLimit - v) < 0.0000001)
                    return;
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
                if (s is null)
                    return;
                var v = Math.Clamp(value, 2, 32);
                if (s.ClaheTileGrid == v)
                    return;
                s.ClaheTileGrid = v;
                RefreshPreviews();
                OnPropertyChanged();
                RequestAutoSave();
            }
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
    
        public bool IsBlobDetectionNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase);
        public bool IsSurfaceCompareNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase);
        public bool IsLinePairDetectionNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase);
        public bool IsEdgePairDetectNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase);
        public bool IsCircleFinderNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase);
        public bool IsDiameterNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase);
        public bool IsEdgePairNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase);
        public bool IsCodeDetectionNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase);
        public ObservableCollection<BlobPolarity> AvailableBlobPolarities { get; } = new ObservableCollection<BlobPolarity>((BlobPolarity[])Enum.GetValues(typeof(BlobPolarity)));
        public ObservableCollection<ImageSourceType> AvailableImageSourceTypes { get; } = new ObservableCollection<ImageSourceType>((ImageSourceType[])Enum.GetValues(typeof(ImageSourceType)));
        public ObservableCollection<PointFindAlgorithm> AvailablePointFindAlgorithms { get; } = new ObservableCollection<PointFindAlgorithm>((PointFindAlgorithm[])Enum.GetValues(typeof(PointFindAlgorithm)));
        public ObservableCollection<LineLineDistanceMode> AvailableLineLineDistanceModes { get; } = new ObservableCollection<LineLineDistanceMode>((LineLineDistanceMode[])Enum.GetValues(typeof(LineLineDistanceMode)));
        public ObservableCollection<PointLineDistanceMode> AvailablePointLineDistanceModes { get; } = new ObservableCollection<PointLineDistanceMode>((PointLineDistanceMode[])Enum.GetValues(typeof(PointLineDistanceMode)));
    
        private static Point2d? FindEdgeOnStrip(Mat gray, OpenCvSharp.Rect strip, bool scanAlongX, EdgePolarity polarity, double minG)
        {
            var len = scanAlongX ? strip.Width : strip.Height;
            if (len < 3)
                return null;
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
                    _ => Math.Abs(g)};
                if (score > bestG)
                {
                    bestG = score;
                    bestIdx = k;
                }
            }
    
            if (bestIdx < 1 || bestIdx >= len - 2)
                return null;
            if (bestG < minG)
                return null;
            var g0 = (prof[bestIdx] - prof[bestIdx - 1]);
            var g1 = (prof[bestIdx + 1] - prof[bestIdx]);
            var g2 = (prof[bestIdx + 2] - prof[bestIdx + 1]);
            var p0 = polarity switch
            {
                EdgePolarity.DarkToLight => g0,
                EdgePolarity.LightToDark => -g0,
                _ => Math.Abs(g0)};
            var p1 = polarity switch
            {
                EdgePolarity.DarkToLight => g1,
                EdgePolarity.LightToDark => -g1,
                _ => Math.Abs(g1)};
            var p2 = polarity switch
            {
                EdgePolarity.DarkToLight => g2,
                EdgePolarity.LightToDark => -g2,
                _ => Math.Abs(g2)};
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
                var(ea1, ea2) = ExtendSegmentToCoverOtherEndpoints(la.P1, la.P2, lb.P1, lb.P2);
                var(eb1, eb2) = ExtendSegmentToCoverOtherEndpoints(lb.P1, lb.P2, la.P1, la.P2);
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
                var aEnds = new[]
                {
                    la.P1,
                    la.P2
                };
                var bEnds = new[]
                {
                    lb.P1,
                    lb.P2
                };
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
    
        private void TrySaveSurfaceCompareTemplateImage(string surfaceCompareName, Roi roi)
        {
            if (_config is null)
            {
                return;
            }
    
            using var rawSnap = _sharedImage.GetSnapshot();
            using var snap = rawSnap ?? new Mat();
            var sc = _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, surfaceCompareName, StringComparison.OrdinalIgnoreCase));
            if (sc is null)
            {
                return;
            }
    
            var toolNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase) && string.Equals(n.RefName, surfaceCompareName, StringComparison.OrdinalIgnoreCase));
            using var processedMat = toolNode != null ? ResolveToolPreprocessForPreview(snap, toolNode) : (_config != null ? _preprocessor.Run(snap, _config.Preprocess) : snap.Clone());
            var templateDir = Path.Combine(Path.GetFullPath(_storeOptions.ConfigRootDirectory), _config?.ProductCode ?? "", "templates");
            Directory.CreateDirectory(templateDir);
            var fileName = Path.Combine(templateDir, $"{surfaceCompareName.ToLowerInvariant()}_sc.png");
            var r = new OpenCvSharp.Rect(roi.X, roi.Y, roi.Width, roi.Height).Intersect(new OpenCvSharp.Rect(0, 0, processedMat.Width, processedMat.Height));
            if (r.Width <= 0 || r.Height <= 0)
            {
                return;
            }
    
            using var crop = new Mat(processedMat, r);
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
    
        private void SyncInputEdgeForEdgePairPort(string port, string? lineName)
        {
            if (_syncingInputs)
                return;
            if (_config is null || SelectedNode is null)
                return;
            if (!string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
                return;
            _syncingInputs = true;
            try
            {
                RemoveEdgesToSelectedNodePort(port);
                if (!string.IsNullOrWhiteSpace(lineName))
                {
                    var from = Nodes.FirstOrDefault(n => (string.Equals(n.Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(n.Type, "Caliper", StringComparison.OrdinalIgnoreCase)) && string.Equals(n.RefName, lineName, StringComparison.OrdinalIgnoreCase));
                    if (from is not null)
                    {
                        from.EnsurePortsInitialized();
                        CreateEdge(from, SelectedNode, from.OutPorts.FirstOrDefault()?.Name ?? "Out", port);
                    }
                }
            }
            finally
            {
                _syncingInputs = false;
            }
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
                if (string.IsNullOrWhiteSpace(p.TemplateImageFile))
                    return;
                if (!Path.IsPathRooted(p.TemplateImageFile))
                {
                    p.TemplateImageFile = Path.GetFullPath(Path.Combine(templateDir, p.TemplateImageFile));
                }
            }
    
            void NormalizeSurfaceCompare(SurfaceCompareDefinition sc)
            {
                if (string.IsNullOrWhiteSpace(sc.TemplateImageFile))
                    return;
                if (!Path.IsPathRooted(sc.TemplateImageFile))
                {
                    sc.TemplateImageFile = Path.GetFullPath(Path.Combine(templateDir, sc.TemplateImageFile));
                }
            }
    
            NormalizePoint(config.Origin);
            foreach (var p in config.Points)
                NormalizePoint(p);
            foreach (var sc in config.SurfaceCompares)
                NormalizeSurfaceCompare(sc);
        }
    
        private void TrySaveTemplateImage(string name, Roi roi, bool isOrigin, string? pointName)
        {
            using var rawSnap = _sharedImage.GetSnapshot();
            using var snap = rawSnap ?? new Mat();
            if (roi.Width <= 0 || roi.Height <= 0)
            {
                return;
            }
    
            ToolGraphNodeViewModel? toolNode = null;
            if (isOrigin)
            {
                toolNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "Origin", StringComparison.OrdinalIgnoreCase));
            }
            else if (!string.IsNullOrWhiteSpace(pointName))
            {
                toolNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "Point", StringComparison.OrdinalIgnoreCase) && string.Equals(n.RefName, pointName, StringComparison.OrdinalIgnoreCase));
            }
    
            using var rawMat = toolNode != null ? ResolveToolImageForPreview(snap, toolNode) : snap.Clone();
            var templateDir = Path.Combine(Path.GetFullPath(_storeOptions.ConfigRootDirectory), ProductCode, "templates");
            Directory.CreateDirectory(templateDir);
            var safeName = name.Trim();
            var fileName = $"{safeName}.png";
            var fullPath = Path.Combine(templateDir, fileName);
            var rect = new OpenCvSharp.Rect(roi.X, roi.Y, roi.Width, roi.Height);
            rect = rect.Intersect(new OpenCvSharp.Rect(0, 0, rawMat.Width, rawMat.Height));
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }
    
            using var cropped = new OpenCvSharp.Mat(rawMat, rect);
            using var gray = cropped.Channels() == 1 ? cropped.Clone() : cropped.CvtColor(OpenCvSharp.ColorConversionCodes.BGR2GRAY);
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
    
        [ObservableProperty]
        private string _productCode = "";
        [ObservableProperty]
        private int _totalExecutionTimeMs = 0;
        [ObservableProperty]
        private string _captureButtonText = "Capture Camera";
        public ObservableCollection<string> ToolboxItems { get; }
        public ObservableCollection<ToolGraphNodeViewModel> Nodes { get; }
        public ObservableCollection<ToolGraphEdgeViewModel> Edges { get; }
    
        [ObservableProperty]
        private ToolGraphNodeViewModel? _selectedNode;
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
                if (c is null)
                    continue;
                TextNode_ConditionRows.Add(new TextColorConditionRow(c, OnTextNodeConditionEdited));
            }
        }
    
        private void OnTextNodeConditionEdited()
        {
            RefreshPreviews();
            RequestAutoSave();
        }
    
        [ObservableProperty]
        private ToolGraphEdgeViewModel? _selectedEdge;
        public ICommand NewGraphCommand { get; }
        public ICommand PointClickedCommand { get; }
        public ICommand PointDoubleClickedCommand { get; }
    
        private VisionConfig? _config;
        private InspectionResult? _lastRun;
        private string? _lastRunError;
        private void RaiseToolPropertyPanelsChanged()
        {
            OnPropertyChanged(nameof(IsLineNode));
            OnPropertyChanged(nameof(IsCaliperNode));
            OnPropertyChanged(nameof(IsOriginNode));
            OnPropertyChanged(nameof(AvailableOriginAlgorithms));
            OnPropertyChanged(nameof(Origin_Algorithm));
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
            OnPropertyChanged(nameof(IsImageSourceNode));
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
            OnPropertyChanged(nameof(ImageSource_SourceType));
            OnPropertyChanged(nameof(ImageSource_IsFile));
            OnPropertyChanged(nameof(ImageSource_IsFolder));
            OnPropertyChanged(nameof(ImageSource_IsCamera));
            OnPropertyChanged(nameof(ImageSource_FilePath));
            OnPropertyChanged(nameof(ImageSource_FolderPath));
            OnPropertyChanged(nameof(ImageSource_CameraIndex));
            OnPropertyChanged(nameof(ImageSource_RtspUrl));
            OnPropertyChanged(nameof(ImageSource_LoopFolder));
            OnPropertyChanged(nameof(ImageSource_FolderIntervalMs));
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
        public bool IsOriginNode => SelectedNode != null && string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase);
        public bool IsPointNode => string.Equals(SelectedNode?.Type, "Point", StringComparison.OrdinalIgnoreCase);
        public bool IsPointEdgePointAlgorithm => Point_Algorithm == PointFindAlgorithm.EdgePoint;
    
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
        public bool IsImageSourceNode => string.Equals(SelectedNode?.Type, "ImageSource", StringComparison.OrdinalIgnoreCase);
        public bool IsPreprocessNode => string.Equals(SelectedNode?.Type, "Preprocess", StringComparison.OrdinalIgnoreCase);
        public bool IsAnyDistanceNode => IsDistanceNode || IsLineLineDistanceNode || IsPointLineDistanceNode || IsAngleNode || IsEdgePairNode || IsEdgePairDetectNode || IsDiameterNode;
    
        private ImageSourceDefinition? SelectedImageSourceDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.ImageSources.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        private void SyncInputEdgeForAnglePort(string port, string? lineName)
        {
            if (_syncingInputs)
                return;
            if (_config is null || SelectedNode is null)
                return;
            if (!string.Equals(SelectedNode.Type, "Angle", StringComparison.OrdinalIgnoreCase))
                return;
            _syncingInputs = true;
            try
            {
                RemoveEdgesToSelectedNodePort(port);
                if (!string.IsNullOrWhiteSpace(lineName))
                {
                    var from = Nodes.FirstOrDefault(n => (string.Equals(n.Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(n.Type, "Caliper", StringComparison.OrdinalIgnoreCase)) && string.Equals(n.RefName, lineName, StringComparison.OrdinalIgnoreCase));
                    if (from is not null)
                    {
                        from.EnsurePortsInitialized();
                        CreateEdge(from, SelectedNode, from.OutPorts.FirstOrDefault()?.Name ?? "Out", port);
                    }
                }
            }
            finally
            {
                _syncingInputs = false;
            }
        }
    
        public int Lpd_Canny1
        {
            get => SelectedLinePairDef()?.Canny1 ?? 0;
            set
            {
                var d = SelectedLinePairDef();
                if (d is null)
                    return;
                if (d.Canny1 == value)
                    return;
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
                if (d is null)
                    return;
                if (d.Canny2 == value)
                    return;
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
                if (d is null)
                    return;
                if (d.HoughThreshold == value)
                    return;
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
                if (d is null)
                    return;
                if (d.MinLineLength == value)
                    return;
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
                if (d is null)
                    return;
                if (d.MaxLineGap == value)
                    return;
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
                if (d is null)
                    return;
                if (d.TryHarder == value)
                    return;
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
            if (d is null)
                return;
            d.Symbologies ??= new();
            var has = d.Symbologies.Contains(sym);
            if (value && !has)
                d.Symbologies.Add(sym);
            if (!value && has)
                d.Symbologies.Remove(sym);
            RefreshPreviews();
            RequestAutoSave();
            RaiseToolPropertyPanelsChanged();
        }
    
        public bool Cdt_EnableQr { get => GetCdtSym(CodeSymbology.Qr); set => SetCdtSym(CodeSymbology.Qr, value); }
        public bool Cdt_EnableBarcode1D { get => GetCdtSym(CodeSymbology.Barcode1D); set => SetCdtSym(CodeSymbology.Barcode1D, value); }
        public bool Cdt_EnableDataMatrix { get => GetCdtSym(CodeSymbology.DataMatrix); set => SetCdtSym(CodeSymbology.DataMatrix, value); }
        public bool Cdt_EnablePdf417 { get => GetCdtSym(CodeSymbology.Pdf417); set => SetCdtSym(CodeSymbology.Pdf417, value); }
        public bool Cdt_EnableAztec { get => GetCdtSym(CodeSymbology.Aztec); set => SetCdtSym(CodeSymbology.Aztec, value); }
    
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
                if (s is null)
                    return;
                if (s.UseGray == value)
                    return;
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
                if (s is null)
                    return;
                if (s.UseGaussianBlur == value)
                    return;
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
                if (s is null)
                    return;
                if (s.BlurKernel == value)
                    return;
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
                if (s is null)
                    return;
                if (s.UseThreshold == value)
                    return;
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
                if (s is null)
                    return;
                if (s.ThresholdValue == value)
                    return;
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
                if (s is null)
                    return;
                if (s.UseCanny == value)
                    return;
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
                if (s is null)
                    return;
                if (s.Canny1 == value)
                    return;
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
                if (s is null)
                    return;
                if (s.Canny2 == value)
                    return;
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
                if (s is null)
                    return;
                if (s.UseMorphology == value)
                    return;
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
                if (def is null || SelectedNode is null)
                    return;
                var v = Math.Clamp(value, 1, 16);
                if (def.InputCount == v)
                    return;
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
                if (def is null)
                    return;
                value ??= string.Empty;
                if (string.Equals(def.Expression, value, StringComparison.Ordinal))
                    return;
                def.Expression = value;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        private ConditionDefinition? SelectedConditionDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "Condition", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.Conditions.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        public ObservableCollection<string> AvailablePointNames
        {
            get
            {
                var list = new ObservableCollection<string>();
                if (_config is null)
                    return list;
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
                if (_config is null)
                    return list;
                foreach (var p in _config.Points.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    list.Add(p);
                }
    
                foreach (var c in _config.CircleFinders.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    if (!list.Contains(c))
                        list.Add(c);
                }
    
                foreach (var d in _config.Diameters.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    if (!list.Contains(d))
                        list.Add(d);
                }
    
                return list;
            }
        }
    
        public ObservableCollection<string> AvailableLineNames
        {
            get
            {
                var list = new ObservableCollection<string>();
                if (_config is null)
                    return list;
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
    
        private void SyncInputEdgeForDistancePort(string port, string? pointName)
        {
            if (_syncingInputs)
                return;
            if (_config is null || SelectedNode is null)
                return;
            if (!string.Equals(SelectedNode.Type, "Distance", StringComparison.OrdinalIgnoreCase))
                return;
            _syncingInputs = true;
            try
            {
                RemoveEdgesToSelectedNodePort(port);
                if (!string.IsNullOrWhiteSpace(pointName))
                {
                    var from = Nodes.FirstOrDefault(n => string.Equals(n.RefName, pointName, StringComparison.OrdinalIgnoreCase) && (string.Equals(n.Type, "Point", StringComparison.OrdinalIgnoreCase) || string.Equals(n.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase) || string.Equals(n.Type, "Diameter", StringComparison.OrdinalIgnoreCase)));
                    if (from is not null)
                    {
                        from.EnsurePortsInitialized();
                        CreateEdge(from, SelectedNode, from.OutPorts.FirstOrDefault()?.Name ?? "Out", port);
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
            if (_syncingInputs)
                return;
            if (_config is null || SelectedNode is null)
                return;
            if (!string.Equals(SelectedNode.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
                return;
            _syncingInputs = true;
            try
            {
                RemoveEdgesToSelectedNodePort(port);
                if (!string.IsNullOrWhiteSpace(lineName))
                {
                    var from = Nodes.FirstOrDefault(n => (string.Equals(n.Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(n.Type, "Caliper", StringComparison.OrdinalIgnoreCase)) && string.Equals(n.RefName, lineName, StringComparison.OrdinalIgnoreCase));
                    if (from is not null)
                    {
                        from.EnsurePortsInitialized();
                        CreateEdge(from, SelectedNode, from.OutPorts.FirstOrDefault()?.Name ?? "Out", port);
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
            if (_syncingInputs)
                return;
            if (_config is null || SelectedNode is null)
                return;
            if (!string.Equals(SelectedNode.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
                return;
            _syncingInputs = true;
            try
            {
                RemoveEdgesToSelectedNodePort(port);
                if (!string.IsNullOrWhiteSpace(refName))
                {
                    var from = Nodes.FirstOrDefault(n => string.Equals(n.RefName, refName, StringComparison.OrdinalIgnoreCase) && ((string.Equals(port, "P1", StringComparison.OrdinalIgnoreCase) && string.Equals(n.Type, "Point", StringComparison.OrdinalIgnoreCase)) || (string.Equals(port, "L1", StringComparison.OrdinalIgnoreCase) && (string.Equals(n.Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(n.Type, "Caliper", StringComparison.OrdinalIgnoreCase)))));
                    if (from is not null)
                    {
                        from.EnsurePortsInitialized();
                        CreateEdge(from, SelectedNode, from.OutPorts.FirstOrDefault()?.Name ?? "Out", port);
                    }
                }
            }
            finally
            {
                _syncingInputs = false;
            }
        }
    
        public ObservableCollection<CaliperOrientation> AvailableCaliperOrientations { get; } = new ObservableCollection<CaliperOrientation>((CaliperOrientation[])Enum.GetValues(typeof(CaliperOrientation)));
        public ObservableCollection<IlluminationCorrectionPreset> AvailableIlluminationCorrectionPresets { get; } = new ObservableCollection<IlluminationCorrectionPreset>((IlluminationCorrectionPreset[])Enum.GetValues(typeof(IlluminationCorrectionPreset)));
        public ObservableCollection<EdgePolarity> AvailableEdgePolarities { get; } = new ObservableCollection<EdgePolarity>((EdgePolarity[])Enum.GetValues(typeof(EdgePolarity)));
        public ObservableCollection<CircleFindAlgorithm> AvailableCircleFindAlgorithms { get; } = new ObservableCollection<CircleFindAlgorithm>((CircleFindAlgorithm[])Enum.GetValues(typeof(CircleFindAlgorithm)));
    
        public ObservableCollection<string> AvailableCircleFinderNames
        {
            get
            {
                var list = new ObservableCollection<string>();
                if (_config is null)
                    return list;
                foreach (var c in _config.CircleFinders.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    list.Add(c);
                }
    
                return list;
            }
        }
    
        private void SyncInputEdgeForDiameterPort(string port, string? circleName)
        {
            if (_syncingInputs)
                return;
            if (_config is null || SelectedNode is null)
                return;
            if (!string.Equals(SelectedNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase))
                return;
            _syncingInputs = true;
            try
            {
                RemoveEdgesToSelectedNodePort(port);
                if (!string.IsNullOrWhiteSpace(circleName))
                {
                    var from = Nodes.FirstOrDefault(n => string.Equals(n.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase) && string.Equals(n.RefName, circleName, StringComparison.OrdinalIgnoreCase));
                    if (from is not null)
                    {
                        from.EnsurePortsInitialized();
                        CreateEdge(from, SelectedNode, from.OutPorts.FirstOrDefault()?.Name ?? "Out", port);
                    }
                }
            }
            finally
            {
                _syncingInputs = false;
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
                if (y >= 0 && y <= height)
                    ts.Add(t);
                // x = width
                t = (width - p.X) / dx;
                y = p.Y + t * dy;
                if (y >= 0 && y <= height)
                    ts.Add(t);
            }
    
            // y = 0
            if (Math.Abs(dy) > 1e-9)
            {
                var t = (0.0 - p.Y) / dy;
                var x = p.X + t * dx;
                if (x >= 0 && x <= width)
                    ts.Add(t);
                // y = height
                t = (height - p.Y) / dy;
                x = p.X + t * dx;
                if (x >= 0 && x <= width)
                    ts.Add(t);
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
    
        private static void AddAngleArc(List<OverlayItem> dst, double cx, double cy, double ax, double ay, double bx, double by, double radius, System.Windows.Media.Brush stroke)
        {
            var a0 = Math.Atan2(ay, ax);
            var a1 = Math.Atan2(by, bx);
            var d = a1 - a0;
            while (d <= -Math.PI)
                d += 2 * Math.PI;
            while (d > Math.PI)
                d -= 2 * Math.PI;
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
    
        private static void AddCircle(List<OverlayItem> dst, double cx, double cy, double radius, System.Windows.Media.Brush stroke, double strokeThickness)
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
    
        private static void AddCross(List<OverlayItem> dst, double cx, double cy, double size, System.Windows.Media.Brush stroke, double strokeThickness)
        {
            var s = Math.Max(1.0, size);
            dst.Add(new OverlayLineItem { X1 = cx - s, Y1 = cy, X2 = cx + s, Y2 = cy, Stroke = stroke, StrokeThickness = strokeThickness, Label = string.Empty });
            dst.Add(new OverlayLineItem { X1 = cx, Y1 = cy - s, X2 = cx, Y2 = cy + s, Stroke = stroke, StrokeThickness = strokeThickness, Label = string.Empty });
        }
    
        internal static System.Windows.Media.Brush? TryParseHexBrush(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return null;
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
                if (inner.Length == 0)
                    return string.Empty;
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
                else if (string.Equals(prop, "Text", StringComparison.OrdinalIgnoreCase))
                {
                    valueObj = v.Text;
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
                    return string.IsNullOrWhiteSpace(fmt) ? d.ToString("0.###", CultureInfo.InvariantCulture) : d.ToString(fmt, CultureInfo.InvariantCulture);
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
                    return string.IsNullOrWhiteSpace(fmt) ? dn.ToString("0.###", CultureInfo.InvariantCulture) : dn.ToString(fmt, CultureInfo.InvariantCulture);
                }
    
                if (valueObj is IFormattable f && !string.IsNullOrWhiteSpace(fmt))
                {
                    return f.ToString(fmt, CultureInfo.InvariantCulture);
                }
    
                return Convert.ToString(valueObj, CultureInfo.InvariantCulture) ?? string.Empty;
            });
        }
    
        [GeneratedRegex(@"(?:\$\{|\{)([^{}]+)\}", RegexOptions.Compiled)]
        internal static partial Regex TextTemplateRegex();
        public double? SelectedRunValue
        {
            get
            {
                if (_lastRun is null || SelectedNode is null)
                    return null;
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
                if (_lastRun is null || SelectedNode is null)
                    return null;
                if (string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
                {
                    var d = _lastRun.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                    return d?.Text;
                }
    
                if (string.Equals(SelectedNode.Type, "Angle", StringComparison.OrdinalIgnoreCase))
                {
                    var a = _lastRun.Angles.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                    return a is null || double.IsNaN(a.ValueDeg) ? null : $"{a.ValueDeg:0.###}";
                }
    
                return null;
            }
        }
    
        public bool? SelectedRunPass
        {
            get
            {
                if (_lastRun is null || SelectedNode is null)
                    return null;
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
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.Lines.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "Distance", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.Distances.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "Angle", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.Angles.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase))
            {
                _config.Origin.Name = "Origin";
            }
            else if (string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.Diameters.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
    
            _selectedNodePrevRefName = newName;
        }
    
        private void ClearActiveGraph()
        {
            foreach (var n in Nodes)
            {
                n.PropertyChanged -= Node_PropertyChanged;
            }
    
            Nodes.Clear();
            Edges.Clear();
            SelectedNode = null;
            _config = null;
            _lastRun = null;
            _lastRunError = null;
            _sharedImage.SetImage(null); // Clear ?nh preview
            SelectedNodeOverlayItems.Clear();
            FinalOverlayItems.Clear();
            TextNode_ConditionRows.Clear();
            RaiseToolPropertyPanelsChanged();
        }
    
        private void UpdateNodeExecutionTimes()
        {
            if (_lastRun == null)
            {
                TotalExecutionTimeMs = 0;
                foreach (var node in Nodes)
                    node.ExecutionTimeMs = null;
                return;
            }
    
            TotalExecutionTimeMs = _lastRun.Timings.TotalMs;
            foreach (var node in Nodes)
            {
                if (!string.IsNullOrWhiteSpace(node.RefName) && _lastRun.Timings.NodeTimings.TryGetValue(node.RefName, out var ms))
                {
                    node.ExecutionTimeMs = ms;
                }
                else
                {
                    node.ExecutionTimeMs = null;
                }
            }
        }
    
        private void NewGraph()
        {
            ClearActiveGraph();
            _config = new VisionConfig
            {
                ProductCode = "NewProduct"
            };
            ProductCode = "NewProduct";
            SelectedConfig = null;
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
                    return new Roi
                    {
                        X = 10,
                        Y = 10,
                        Width = 120,
                        Height = 120
                    };
                }
    
                var w = Math.Clamp(imgW / 4, 60, Math.Max(60, imgW));
                var h = Math.Clamp(imgH / 4, 60, Math.Max(60, imgH));
                var x = Math.Clamp((imgW - w) / 2, 0, Math.Max(0, imgW - w));
                var y = Math.Clamp((imgH - h) / 2, 0, Math.Max(0, imgH - h));
                return new Roi
                {
                    X = x,
                    Y = y,
                    Width = w,
                    Height = h
                };
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
                    var def = new PointDefinition
                    {
                        Name = node.RefName
                    };
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
                    var def = new LineToolDefinition
                    {
                        Name = node.RefName
                    };
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
                    var def = new CaliperDefinition
                    {
                        Name = node.RefName
                    };
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
                    var def = new BlobDetectionDefinition
                    {
                        Name = node.RefName
                    };
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
                    var def = new SurfaceCompareDefinition
                    {
                        Name = node.RefName
                    };
                    def.InspectRoi = DefaultRoi();
                    def.TemplateRoi = DefaultRoi();
                    _config.SurfaceCompares.Add(def);
                    ActiveRoiLabel = $"{node.RefName} SC";
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.ImageSources.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    _config.ImageSources.Add(new ImageSourceDefinition { Name = node.RefName, SourceType = ImageSourceType.File });
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "Text", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.TextNodes.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    // Default position roughly near top-left; user can set by Ctrl+Shift click.
                    _config.TextNodes.Add(new TextNodeDefinition { Name = node.RefName, Text = node.RefName, X = 10, Y = 10, DefaultColor = "#FFFFFFFF" });
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.LinePairDetections.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    var def = new LinePairDetectionDefinition
                    {
                        Name = node.RefName
                    };
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
                    var def = new EdgePairDetectDefinition
                    {
                        Name = node.RefName
                    };
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
                    var def = new CircleFinderDefinition
                    {
                        Name = node.RefName
                    };
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
                    var def = new CodeDetectionDefinition
                    {
                        Name = node.RefName
                    };
                    def.SearchRoi = DefaultRoi();
                    def.Symbologies = new List<CodeSymbology>
                    {
                        CodeSymbology.Qr,
                        CodeSymbology.Barcode1D,
                        CodeSymbology.DataMatrix,
                        CodeSymbology.Pdf417,
                        CodeSymbology.Aztec
                    };
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
                    var def = new ConditionDefinition
                    {
                        Name = node.RefName,
                        InputCount = Math.Clamp(node.InputCount, 1, 16),
                        Expression = string.Empty
                    };
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
    
        private void ClearToolInputByEdge(ToolGraphEdgeViewModel edge)
        {
            if (_config is null)
                return;
            var to = Nodes.FirstOrDefault(n => string.Equals(n.Id, edge.ToNodeId, StringComparison.OrdinalIgnoreCase));
            var from = Nodes.FirstOrDefault(n => string.Equals(n.Id, edge.FromNodeId, StringComparison.OrdinalIgnoreCase));
            if (to is null || from is null)
                return;
            if (string.Equals(to.Type, "Angle", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.Angles.FirstOrDefault(x => string.Equals(x.Name, to.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is null)
                    return;
                if (string.Equals(edge.ToPort, "L1", StringComparison.OrdinalIgnoreCase) && string.Equals(def.LineA, from.RefName, StringComparison.OrdinalIgnoreCase))
                {
                    def.LineA = string.Empty;
                }
                else if (string.Equals(edge.ToPort, "L2", StringComparison.OrdinalIgnoreCase) && string.Equals(def.LineB, from.RefName, StringComparison.OrdinalIgnoreCase))
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
                if (string.Equals(edge.FromNodeId, n.Id, StringComparison.OrdinalIgnoreCase) || string.Equals(edge.ToNodeId, n.Id, StringComparison.OrdinalIgnoreCase))
                {
                    edge.NotifyGeometryChanged();
                }
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
    }
}
