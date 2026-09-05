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
using VisionInspectionApp.Application.LightingController;
using VisionInspectionApp.Application.OQC;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.VisionEngine;
namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isDirty;

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
                else if (string.Equals(node.Type, "Crop", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.Crops?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Success: {res.Success}\r\nOutput Size: {res.Width} x {res.Height} px";
                }
                else if (string.Equals(node.Type, "ColorDiff", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.ColorDiffs?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Pass: {res.Pass}\r\nDeltaE: {res.DeltaE:F2} (Max: {res.MaxDeltaE:F2})\r\nMeasured Lab: ({res.MeasuredL:F2}, {res.MeasuredA:F2}, {res.MeasuredB:F2})\r\nRef Lab: ({res.RefL:F2}, {res.RefA:F2}, {res.RefB:F2})";
                }
                else if (string.Equals(node.Type, "ImgArithmetic", StringComparison.OrdinalIgnoreCase))
                {
                    var res = _lastRun.ImgArithmetics?.FirstOrDefault(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                    if (res is not null)
                        val = $"Success: {res.Success}\r\nOp: {res.Op}\r\nSize: {res.Width} x {res.Height} px";
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
        private readonly IJobService _jobService;
        private readonly IRecentJobsService? _recentJobsService;
        private readonly Application.PLC.Services.IPlcManagerService _plcManagerService;
        private readonly Application.PLC.Services.PlcMotionSyncService _motionSyncService;
        private readonly Application.Services.RollDefectManager _rollDefectManager = new();
        private readonly Application.PLC.Services.ShiftRegisterTracker _shiftRegisterTracker;
        private readonly Application.PLC.Services.PlcHeartbeatWatchdog _plcHeartbeatWatchdog;
        private readonly Application.PLC.Services.IndustrialHandshakeStateMachine _handshakeStateMachine;
        private readonly Application.DB.Services.IDbManagerService _dbManagerService;
        private readonly Application.Services.IInspectionLogService _inspectionLogService;
        private readonly LightingControllerService? _lightingControllerService;
        private readonly VisionInspectionApp.Application.Services.IRemoteServerService _remoteServerService;
        private readonly IServiceProvider? _serviceProvider;
        public UndoRedoManager UndoManager { get; }
        public IRelayCommand UndoCommand { get; }
        public IRelayCommand RedoCommand { get; }
        public Application.PLC.Services.PlcMotionSyncService MotionSyncService => _motionSyncService;
        public Application.Services.RollDefectManager RollDefectManager => _rollDefectManager;
        public Application.PLC.Services.ShiftRegisterTracker ShiftRegisterTracker => _shiftRegisterTracker;
        public Application.PLC.Services.PlcHeartbeatWatchdog PlcHeartbeatWatchdog => _plcHeartbeatWatchdog;
        public Application.PLC.Services.IndustrialHandshakeStateMachine HandshakeStateMachine => _handshakeStateMachine;
        public Application.Services.IInspectionLogService InspectionLogService => _inspectionLogService;
        public LightingControllerService? LightingControllerService => _lightingControllerService;
        public SharedImageContext SharedImageContext => _sharedImage;

        public ToolEditorViewModel()
        {
            UndoManager = new UndoRedoManager();
            UndoCommand = new RelayCommand(() => UndoManager.Undo(), () => UndoManager.CanUndo);
            RedoCommand = new RelayCommand(() => UndoManager.Redo(), () => UndoManager.CanRedo);
            _configService = null!;
            _storeOptions = null!;
            _sharedImage = new SharedImageContext();
            _preprocessor = null!;
            _lineDetector = null!;
            _inspectionService = null!;
            _cameraService = null!;
            _jobService = null!;
            _plcManagerService = null!;
            _dbManagerService = null!;
            _lightingControllerService = null;
            _remoteServerService = new VisionInspectionApp.Application.Services.RemoteServerService();
            _inspectionLogService = new Application.Services.InspectionLogService();
            _motionSyncService = new Application.PLC.Services.PlcMotionSyncService(null);
            _shiftRegisterTracker = new Application.PLC.Services.ShiftRegisterTracker(null);
            _plcHeartbeatWatchdog = new Application.PLC.Services.PlcHeartbeatWatchdog(null);
            _handshakeStateMachine = new Application.PLC.Services.IndustrialHandshakeStateMachine(null);
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            Nodes = new ObservableCollection<ToolGraphNodeViewModel>();
            Edges = new ObservableCollection<ToolGraphEdgeViewModel>();
            SelectedNodeOverlayItems = new List<OverlayItem>();
            FinalOverlayItems = new List<OverlayItem>();
            TextNode_ConditionRows = new ObservableCollection<TextColorConditionRow>();
            AvailablePreprocessChoices = new ObservableCollection<string>();
            AllToolboxItems = new List<ToolboxItemModel>();
            ToolboxItems = new ObservableCollection<string>();
            ToolboxCollectionView = System.Windows.Data.CollectionViewSource.GetDefaultView(AllToolboxItems);
        }

        public ToolEditorViewModel(IConfigService configService, ConfigStoreOptions storeOptions, SharedImageContext sharedImage, ImagePreprocessor preprocessor, LineDetector lineDetector, IInspectionService inspectionService, CameraService cameraService, IJobService jobService, UndoRedoManager undoManager, Application.PLC.Services.IPlcManagerService plcManagerService, Application.DB.Services.IDbManagerService dbManagerService, IRecentJobsService? recentJobsService = null, LightingControllerService? lightingControllerService = null, IServiceProvider? serviceProvider = null)
        {
            _serviceProvider = serviceProvider;
            _remoteServerService = (serviceProvider?.GetService(typeof(VisionInspectionApp.Application.Services.IRemoteServerService)) as VisionInspectionApp.Application.Services.IRemoteServerService) ?? new VisionInspectionApp.Application.Services.RemoteServerService();
            _lightingControllerService = lightingControllerService ?? (serviceProvider?.GetService(typeof(LightingControllerService)) as LightingControllerService);
            if (_lightingControllerService != null)
            {
                _lightingControllerService.OnError += (_, err) =>
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        StatusBarText = $"⚠️ [Đèn Chiếu Sáng] {err}";
                    });
                };
            }
            UndoManager = undoManager;
            UndoCommand = new RelayCommand(() => UndoManager.Undo(), () => UndoManager.CanUndo);
            RedoCommand = new RelayCommand(() => UndoManager.Redo(), () => UndoManager.CanRedo);
            UndoManager.PropertyChanged += (_, _) =>
            {
                (UndoCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (RedoCommand as RelayCommand)?.NotifyCanExecuteChanged();
            };

            _jobService = jobService;
            _recentJobsService = recentJobsService;
            _configService = configService;
            _storeOptions = storeOptions;
            _sharedImage = sharedImage;
            _preprocessor = preprocessor;
            _lineDetector = lineDetector;
            _inspectionService = inspectionService;
            _cameraService = cameraService;
            _plcManagerService = plcManagerService;
            _inspectionLogService = serviceProvider?.GetService(typeof(Application.Services.IInspectionLogService)) as Application.Services.IInspectionLogService ?? new Application.Services.InspectionLogService();
            _motionSyncService = new Application.PLC.Services.PlcMotionSyncService(_plcManagerService);
            _shiftRegisterTracker = new Application.PLC.Services.ShiftRegisterTracker(_plcManagerService);
            _rollDefectManager.OnDefectRecorded += (_, defect) => _shiftRegisterTracker.EnqueueDefect(defect);
            _plcHeartbeatWatchdog = new Application.PLC.Services.PlcHeartbeatWatchdog(_plcManagerService);
            _handshakeStateMachine = new Application.PLC.Services.IndustrialHandshakeStateMachine(_plcManagerService);
            _plcHeartbeatWatchdog.Start();
            ApplyIndustrialConfig(_plcManagerService.IndustrialConfig);
            _plcManagerService.OnIndustrialConfigChanged += (_, cfg) => ApplyIndustrialConfig(cfg);
            _dbManagerService = dbManagerService;
            _plcManagerService.OnTagChanged += OnPlcTagChangedForTrigger;
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
            _continuousStatsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _continuousStatsTimer.Tick += (_, __) => UpdateContinuousStats();
            var globalSettings = serviceProvider?.GetService(typeof(GlobalAppSettingsService)) as GlobalAppSettingsService;
            if (globalSettings != null)
            {
                _isOriginalQualityPreview = globalSettings.Settings.UseOriginalQualityPreview;
                MatExtensions.UseOriginalQualityPreview = _isOriginalQualityPreview;
            }
            InitializeSystemMonitor();
            AllToolboxItems = new List<ToolboxItemModel>
            {
                new ToolboxItemModel { Name = "ImageSource", Category = "📷 Nguồn & Định Vị", Icon = "📷" },
                new ToolboxItemModel { Name = "Origin", Category = "📷 Nguồn & Định Vị", Icon = "🎯" },
                new ToolboxItemModel { Name = "Preprocess", Category = "📷 Nguồn & Định Vị", Icon = "⚙️" },

                new ToolboxItemModel { Name = "Point", Category = "🔍 Phát Hiện & Tìm Kiếm", Icon = "📍" },
                new ToolboxItemModel { Name = "Line", Category = "🔍 Phát Hiện & Tìm Kiếm", Icon = "📏" },
                new ToolboxItemModel { Name = "Caliper", Category = "🔍 Phát Hiện & Tìm Kiếm", Icon = "📐" },
                new ToolboxItemModel { Name = "EdgePairDetect", Category = "🔍 Phát Hiện & Tìm Kiếm", Icon = "⏸️" },
                new ToolboxItemModel { Name = "CircleFinder", Category = "🔍 Phát Hiện & Tìm Kiếm", Icon = "🔘" },
                new ToolboxItemModel { Name = "BlobDetection", Category = "🔍 Phát Hiện & Tìm Kiếm", Icon = "🦠" },
                new ToolboxItemModel { Name = "CodeDetection", Category = "🔍 Phát Hiện & Tìm Kiếm", Icon = "🔳" },
                new ToolboxItemModel { Name = "SurfaceCompare", Category = "🔍 Phát Hiện & Tìm Kiếm", Icon = "🔍" },
                new ToolboxItemModel { Name = "ContourCompare", Category = "🔍 Phát Hiện & Tìm Kiếm", Icon = "🌀" },
                new ToolboxItemModel { Name = "Crop", Category = "🔍 Phát Hiện & Tìm Kiếm", Icon = "✂️" },
                new ToolboxItemModel { Name = "ColorDiff", Category = "🔍 Phát Hiện & Tìm Kiếm", Icon = "🎨" },
                new ToolboxItemModel { Name = "ImgArithmetic", Category = "🔍 Phát Hiện & Tìm Kiếm", Icon = "🧮" },

                new ToolboxItemModel { Name = "Distance", Category = "📐 Đo Đạc & Kích Thước", Icon = "↔️" },
                new ToolboxItemModel { Name = "LineLineDistance", Category = "📐 Đo Đạc & Kích Thước", Icon = "⏸️" },
                new ToolboxItemModel { Name = "PointLineDistance", Category = "📐 Đo Đạc & Kích Thước", Icon = "⏯️" },
                new ToolboxItemModel { Name = "SegmentLineDistance", Category = "📐 Đo Đạc & Kích Thước", Icon = "⏩" },
                new ToolboxItemModel { Name = "Angle", Category = "📐 Đo Đạc & Kích Thước", Icon = "∠" },
                new ToolboxItemModel { Name = "Diameter", Category = "📐 Đo Đạc & Kích Thước", Icon = "⭕" },
                new ToolboxItemModel { Name = "EdgePair", Category = "📐 Đo Đạc & Kích Thước", Icon = "⏸️" },

                new ToolboxItemModel { Name = "Condition", Category = "🔀 Điều Kiện & Hiển Thị", Icon = "❓" },
                new ToolboxItemModel { Name = "Text", Category = "🔀 Điều Kiện & Hiển Thị", Icon = "🔤" },
                new ToolboxItemModel { Name = "ImageOutput", Category = "🔀 Điều Kiện & Hiển Thị", Icon = "🖼️" },
                new ToolboxItemModel { Name = "ResultView", Category = "🔀 Điều Kiện & Hiển Thị", Icon = "📊" },

                new ToolboxItemModel { Name = "PlcRead", Category = "🔌 Kết Nối PLC & CSDL", Icon = "📥" },
                new ToolboxItemModel { Name = "PlcWrite", Category = "🔌 Kết Nối PLC & CSDL", Icon = "📤" },

                new ToolboxItemModel { Name = "CreatePoint", Category = "🛠️ Tool Creation", Icon = "📍" },
                new ToolboxItemModel { Name = "CreateLine", Category = "🛠️ Tool Creation", Icon = "📏" },
                new ToolboxItemModel { Name = "CreateRect", Category = "🛠️ Tool Creation", Icon = "▭" },
                new ToolboxItemModel { Name = "CreateCircle", Category = "🛠️ Tool Creation", Icon = "⭕" },
                new ToolboxItemModel { Name = "PlcWait", Category = "🔌 Kết Nối PLC & CSDL", Icon = "⏱️" },
                new ToolboxItemModel { Name = "PlcTrigger", Category = "🔌 Kết Nối PLC & CSDL", Icon = "⚡" },
                new ToolboxItemModel { Name = "PlcBatchRead", Category = "🔌 Kết Nối PLC & CSDL", Icon = "📥" },
                new ToolboxItemModel { Name = "PlcBatchWrite", Category = "🔌 Kết Nối PLC & CSDL", Icon = "📤" },
                new ToolboxItemModel { Name = "ResultTransfer", Category = "🔌 Kết Nối PLC & CSDL", Icon = "🔄" },
                new ToolboxItemModel { Name = "DbNode", Category = "🔌 Kết Nối PLC & CSDL", Icon = "🗄️" }
            };

            ToolboxItems = new ObservableCollection<string>(AllToolboxItems.Select(x => x.Name));

            ToolboxCollectionView = System.Windows.Data.CollectionViewSource.GetDefaultView(AllToolboxItems);
            ToolboxCollectionView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Category"));
            ToolboxCollectionView.Filter = item =>
            {
                if (string.IsNullOrWhiteSpace(ToolboxSearchText)) return true;
                if (item is ToolboxItemModel model)
                {
                    return model.Name.Contains(ToolboxSearchText, StringComparison.OrdinalIgnoreCase) ||
                           model.Category.Contains(ToolboxSearchText, StringComparison.OrdinalIgnoreCase);
                }
                return true;
            };
            Nodes = new ObservableCollection<ToolGraphNodeViewModel>();
            Nodes.CollectionChanged += (_, _) => IsDirty = true;
            Edges = new ObservableCollection<ToolGraphEdgeViewModel>();
            Edges.CollectionChanged += (_, _) => IsDirty = true;
            AvailablePreprocessChoices = new ObservableCollection<string>();
            SelectedNodeOverlayItems = new List<OverlayItem>();
            FinalOverlayItems = new List<OverlayItem>();
            TextNode_ConditionRows = new ObservableCollection<TextColorConditionRow>();
            OpenJobCommand = new RelayCommand(OpenJob);
            SaveJobCommand = new RelayCommand(SaveJob);
            SaveJobAsCommand = new RelayCommand(SaveJobAs);
            NewGraphCommand = new RelayCommand(NewGraph);
            DeleteSelectedNodeCommand = new RelayCommand(DeleteSelectedNode);
            DeleteSelectedEdgeCommand = new RelayCommand(DeleteSelectedEdge);
            DeleteSelectionCommand = new RelayCommand(DeleteSelection);
            CopySelectedNodeCommand = new RelayCommand(CopySelectedNode);
            PasteNodeCommand = new RelayCommand(PasteNode);
            LoadPreviewImageCommand = new RelayCommand(LoadPreviewImage);
            CaptureCameraImageCommand = new AsyncRelayCommand(CaptureCameraImageAsync);
            CaptureAndSaveImageCommand = new AsyncRelayCommand(CaptureAndSaveImageAsync);
            RunFlowCommand = new RelayCommand(OnRunFlowClicked);
            RunOnceCommand = new RelayCommand(OnRunOnceClicked);
            RunContinuousCommand = new RelayCommand(OnRunContinuousClicked);
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
            ImageSource_OpenJobCameraSettingsCommand = new RelayCommand(ImageSource_OpenJobCameraSettings);
            ImageSource_ApplyLightingToDeviceCommand = new RelayCommand(ImageSource_ApplyLightingToDevice);
            ImageSource_ReadLightingFromDeviceCommand = new RelayCommand(ImageSource_ReadLightingFromDevice);
            SurfaceCompare_SetSearchRoiCommand = new RelayCommand(SurfaceCompare_SetSearchRoi);
            SurfaceCompare_SetTemplateRoiCommand = new RelayCommand(SurfaceCompare_SetTemplateRoi);
            ContourCompare_SetSearchRoiCommand = new RelayCommand(ContourCompare_SetSearchRoi);
            ContourCompare_SetTemplateRoiCommand = new RelayCommand(ContourCompare_SetTemplateRoi);
            Origin_TeachTemplateCommand = new RelayCommand(Origin_TeachTemplate);
            Origin_OpenTrainWindowCommand = new RelayCommand(OpenTrainTemplateWindow);
            Preprocess_AddRectangleRoiCommand = new RelayCommand(() => Preprocess_AddRoi(PreprocessRoiShape.Rectangle, PreprocessRoiMode.Include));
            Preprocess_AddCircleRoiCommand = new RelayCommand(() => Preprocess_AddRoi(PreprocessRoiShape.Circle, PreprocessRoiMode.Include));
            Preprocess_AddPolygonRoiCommand = new RelayCommand(() => Preprocess_AddRoi(PreprocessRoiShape.Polygon, PreprocessRoiMode.Include));
            Preprocess_RemoveRoiCommand = new RelayCommand<PreprocessRoiDefinition?>(Preprocess_RemoveRoi);
            Preprocess_ToggleRoiModeCommand = new RelayCommand<PreprocessRoiDefinition?>(Preprocess_ToggleRoiMode);
            Preprocess_AddPolygonPointCommand = new RelayCommand<PreprocessRoiDefinition?>(Preprocess_AddPolygonPoint);
            Preprocess_RemovePolygonPointCommand = new RelayCommand<Point2dModel?>(Preprocess_RemovePolygonPoint);
            OpenCalibrationDialogCommand = new RelayCommand(OpenCalibrationDialog);
            OpenChessboardCalibrationDialogCommand = new RelayCommand(OpenChessboardCalibrationDialog);
            OpenProductAssignDialogCommand = new RelayCommand(OpenProductAssignDialog);
            OpenJobManagerWindowCommand = new RelayCommand(OpenJobManagerWindow);
            ImageSource_FetchUrlImageCommand = new AsyncRelayCommand(ImageSource_FetchUrlImageAsync);
            ImageSource_OpenJobManagerCommand = new RelayCommand(OpenJobManagerWindow);
            OpenRollDefectMapCommand = new RelayCommand(OpenRollDefectMap);
            OpenInspectionLogCommand = new RelayCommand(OpenInspectionLog);
            ColorDiff_TeachRefColorCommand = new RelayCommand(ColorDiff_TeachRefColor);
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
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
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
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
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
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
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
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
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
        public bool IsContourCompareNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase);
        public bool IsLinePairDetectionNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase);
        public bool IsEdgePairDetectNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase);
        public bool IsCircleFinderNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase);
        public bool IsDiameterNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase);
        public bool IsEdgePairNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase);
        public bool IsCodeDetectionNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase);
        public bool IsResultViewNode => SelectedNode is not null && string.Equals(SelectedNode.Type, "ResultView", StringComparison.OrdinalIgnoreCase);
        public bool EnableRoiEditingInPreview => SelectedNode is not null && !string.Equals(SelectedNode.Type, "ResultView", StringComparison.OrdinalIgnoreCase);
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
    
        private void TrySaveSurfaceCompareTemplateImage(string surfaceCompareName, Roi roi, Roi? cropRoi = null)
        {
            if (_config is null)
            {
                return;
            }

            using var rawSnap = _sharedImage.GetSnapshot();
            using var snap = rawSnap ?? new Mat();
            if (snap.Empty())
            {
                return;
            }

            var sc = _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, surfaceCompareName, StringComparison.OrdinalIgnoreCase));
            if (sc is null)
            {
                return;
            }

            var targetRoi = cropRoi ?? roi;
            if (targetRoi.Width <= 0 || targetRoi.Height <= 0)
            {
                return;
            }

            var toolNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase) && string.Equals(n.RefName, surfaceCompareName, StringComparison.OrdinalIgnoreCase));
            using var processedMat = toolNode != null ? ResolveToolImageForPreview(snap, toolNode) : snap.Clone();
            if (processedMat.Empty())
            {
                return;
            }

            using var crop = ExtractRoiPatch(processedMat, targetRoi);
            if (crop.Empty() || crop.Width <= 0 || crop.Height <= 0)
            {
                return;
            }

            using var gray = crop.Channels() == 1 ? crop.Clone() : crop.CvtColor(ColorConversionCodes.BGR2GRAY);

            var templateDir = Path.Combine(CurrentTempWorkingDir ?? Path.Combine(Path.GetFullPath(_storeOptions.ConfigRootDirectory), ProductCode), "templates");
            Directory.CreateDirectory(templateDir);
            var fileName = Path.Combine(templateDir, $"{surfaceCompareName.ToLowerInvariant()}_sc.png");

            Cv2.ImWrite(fileName, gray);
            sc.TemplateImageFile = fileName;
            RequestAutoSave();
        }

        private void TrySaveContourCompareTemplateImage(string contourCompareName, Roi roi, Roi? cropRoi = null)
        {
            if (_config is null) return;

            using var rawSnap = _sharedImage.GetSnapshot();
            using var snap = rawSnap ?? new Mat();
            if (snap.Empty()) return;

            var cc = _config.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, contourCompareName, StringComparison.OrdinalIgnoreCase));
            if (cc is null) return;

            var targetRoi = cropRoi ?? roi;
            if (targetRoi.Width <= 0 || targetRoi.Height <= 0) return;

            var toolNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase) && string.Equals(n.RefName, contourCompareName, StringComparison.OrdinalIgnoreCase));
            using var processedMat = toolNode != null ? ResolveToolImageForPreview(snap, toolNode) : snap.Clone();
            if (processedMat.Empty()) return;

            using var crop = ExtractRoiPatch(processedMat, targetRoi);
            if (crop.Empty() || crop.Width <= 0 || crop.Height <= 0) return;

            using var gray = crop.Channels() == 1 ? crop.Clone() : crop.CvtColor(ColorConversionCodes.BGR2GRAY);

            var templateDir = Path.Combine(CurrentTempWorkingDir ?? Path.Combine(Path.GetFullPath(_storeOptions.ConfigRootDirectory), ProductCode), "templates");
            Directory.CreateDirectory(templateDir);
            var fileName = Path.Combine(templateDir, $"{contourCompareName.ToLowerInvariant()}_contour.png");

            Cv2.ImWrite(fileName, gray);
            cc.TemplateImageFile = fileName;
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
    
        public string? ResolveTemplatePath(string? currentPath, string? fallbackName = null, string? fallbackPattern = null)
        {
            try
            {
                // BẮT BUỘC CHỈ TÌM TRONG THƯ MỤC CỦA JOB HIỆN TẠI (CurrentTempWorkingDir)
                if (string.IsNullOrWhiteSpace(CurrentTempWorkingDir) || !Directory.Exists(CurrentTempWorkingDir))
                {
                    if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
                        return Path.GetFullPath(currentPath);
                    return null;
                }

                var templatesSubdir = Path.Combine(CurrentTempWorkingDir, "templates");

                // Trích xuất tên file sạch (bỏ mọi đường dẫn tuyệt đối cũ nếu có trong json)
                var cleanFileName = !string.IsNullOrWhiteSpace(currentPath) ? Path.GetFileName(currentPath) : fallbackName;

                // 1. Tìm trong CurrentTempWorkingDir/templates/{cleanFileName}
                if (!string.IsNullOrWhiteSpace(cleanFileName) && Directory.Exists(templatesSubdir))
                {
                    var p1 = Path.Combine(templatesSubdir, cleanFileName);
                    if (File.Exists(p1)) return Path.GetFullPath(p1);
                }

                // 2. Tìm trong CurrentTempWorkingDir/{cleanFileName} (gốc zip)
                if (!string.IsNullOrWhiteSpace(cleanFileName))
                {
                    var p2 = Path.Combine(CurrentTempWorkingDir, cleanFileName);
                    if (File.Exists(p2)) return Path.GetFullPath(p2);
                }

                // 3. Nếu cleanFileName khác fallbackName, thử tìm fallbackName
                if (!string.IsNullOrWhiteSpace(fallbackName))
                {
                    var fbClean = Path.GetFileName(fallbackName);
                    if (Directory.Exists(templatesSubdir))
                    {
                        var p3 = Path.Combine(templatesSubdir, fbClean);
                        if (File.Exists(p3)) return Path.GetFullPath(p3);
                    }
                    var p4 = Path.Combine(CurrentTempWorkingDir, fbClean);
                    if (File.Exists(p4)) return Path.GetFullPath(p4);
                }

                // 4. Tìm kiếm theo wildcard pattern trong CurrentTempWorkingDir/templates
                if (!string.IsNullOrWhiteSpace(fallbackPattern) && Directory.Exists(templatesSubdir))
                {
                    var matches = Directory.GetFiles(templatesSubdir, fallbackPattern);
                    if (matches.Length > 0) return Path.GetFullPath(matches[0]);
                }

                // 5. Tìm kiếm theo wildcard pattern trong CurrentTempWorkingDir
                if (!string.IsNullOrWhiteSpace(fallbackPattern))
                {
                    var matches = Directory.GetFiles(CurrentTempWorkingDir, fallbackPattern);
                    if (matches.Length > 0) return Path.GetFullPath(matches[0]);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResolveTemplatePath] Error resolving path: {ex.Message}");
            }

            return null;
        }

        private void EnsureTemplatePathsAbsolute(VisionConfig config)
        {
            if (config is null)
            {
                return;
            }

            // Origin
            if (config.Origin != null)
            {
                var resolved = ResolveTemplatePath(config.Origin.TemplateImageFile, "origin.png", "origin*.png");
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    config.Origin.TemplateImageFile = resolved;
                }
            }

            // Points
            if (config.Points != null)
            {
                foreach (var p in config.Points)
                {
                    if (p == null) continue;
                    var fallback = !string.IsNullOrWhiteSpace(p.Name) ? $"{p.Name.ToLowerInvariant()}.png" : null;
                    var pattern = !string.IsNullOrWhiteSpace(p.Name) ? $"{p.Name.ToLowerInvariant()}*.png" : null;
                    var resolved = ResolveTemplatePath(p.TemplateImageFile, fallback, pattern);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        p.TemplateImageFile = resolved;
                    }
                }
            }

            // SurfaceCompares
            if (config.SurfaceCompares != null)
            {
                foreach (var sc in config.SurfaceCompares)
                {
                    if (sc == null) continue;
                    var fallback = !string.IsNullOrWhiteSpace(sc.Name) ? $"{sc.Name.ToLowerInvariant()}.png" : null;
                    var pattern = !string.IsNullOrWhiteSpace(sc.Name) ? $"{sc.Name.ToLowerInvariant()}*.png" : null;
                    var resolved = ResolveTemplatePath(sc.TemplateImageFile, fallback, pattern);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        sc.TemplateImageFile = resolved;
                    }
                }
            }

            // ContourCompares
            if (config.ContourCompares != null)
            {
                foreach (var cc in config.ContourCompares)
                {
                    if (cc == null) continue;
                    var fallback = !string.IsNullOrWhiteSpace(cc.Name) ? $"{cc.Name.ToLowerInvariant()}.png" : null;
                    var pattern = !string.IsNullOrWhiteSpace(cc.Name) ? $"{cc.Name.ToLowerInvariant()}*.png" : null;
                    var resolved = ResolveTemplatePath(cc.TemplateImageFile, fallback, pattern);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        cc.TemplateImageFile = resolved;
                    }
                }
            }
        }
    
        public static OpenCvSharp.Mat ExtractRoiPatch(OpenCvSharp.Mat source, Roi roi)
        {
            if (source is null || source.Empty() || roi.Width <= 0 || roi.Height <= 0)
            {
                return new OpenCvSharp.Mat();
            }

            var cx = roi.X + roi.Width / 2.0;
            var cy = roi.Y + roi.Height / 2.0;

            if (Math.Abs(roi.Angle) < 0.001)
            {
                var rect = new OpenCvSharp.Rect(roi.X, roi.Y, roi.Width, roi.Height).Intersect(new OpenCvSharp.Rect(0, 0, source.Width, source.Height));
                if (rect.Width <= 0 || rect.Height <= 0) return new OpenCvSharp.Mat();
                return new OpenCvSharp.Mat(source, rect).Clone();
            }

            int diag = (int)Math.Ceiling(Math.Sqrt(roi.Width * roi.Width + roi.Height * roi.Height));
            var bbox = new OpenCvSharp.Rect((int)(cx - diag / 2.0), (int)(cy - diag / 2.0), diag, diag);
            var safeBbox = bbox.Intersect(new OpenCvSharp.Rect(0, 0, source.Width, source.Height));
            if (safeBbox.Width <= 0 || safeBbox.Height <= 0) return new OpenCvSharp.Mat();

            using var subSource = new OpenCvSharp.Mat(source, safeBbox);
            var centerInBbox = new OpenCvSharp.Point2f((float)(cx - safeBbox.X), (float)(cy - safeBbox.Y));
            
            using var M = OpenCvSharp.Cv2.GetRotationMatrix2D(centerInBbox, roi.Angle, 1.0);
            var tx = diag / 2.0 - centerInBbox.X;
            var ty = diag / 2.0 - centerInBbox.Y;
            M.Set(0, 2, M.Get<double>(0, 2) + tx);
            M.Set(1, 2, M.Get<double>(1, 2) + ty);
            using var rotatedBbox = new OpenCvSharp.Mat();
            OpenCvSharp.Cv2.WarpAffine(subSource, rotatedBbox, M, new OpenCvSharp.Size(diag, diag), OpenCvSharp.InterpolationFlags.Linear, OpenCvSharp.BorderTypes.Replicate);

            var patch = new OpenCvSharp.Mat();
            var centerInDst = new OpenCvSharp.Point2f((float)(diag / 2.0), (float)(diag / 2.0));
            OpenCvSharp.Cv2.GetRectSubPix(rotatedBbox, new OpenCvSharp.Size(roi.Width, roi.Height), centerInDst, patch);
            return patch;
        }

        private void TrySaveTemplateImage(string name, Roi roi, bool isOrigin, string? pointName)
        {
            using var rawSnap = _sharedImage.GetSnapshot();
            using var snap = rawSnap ?? new OpenCvSharp.Mat();
            if (roi.Width <= 0 || roi.Height <= 0)
            {
                return;
            }
    
            OpenCvSharp.Mat rawMat;
            ToolGraphNodeViewModel? toolNode = null;
            if (isOrigin)
            {
                toolNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "Origin", StringComparison.OrdinalIgnoreCase));
            }
            else if (!string.IsNullOrWhiteSpace(pointName))
            {
                toolNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "Point", StringComparison.OrdinalIgnoreCase) && string.Equals(n.RefName, pointName, StringComparison.OrdinalIgnoreCase));
            }
            rawMat = toolNode != null ? ResolveToolImageForPreview(snap, toolNode) : _preprocessor.Run(snap, _config?.Preprocess ?? new PreprocessSettings());
            using var tempDisposeMat = rawMat;

            var templateDir = Path.Combine(CurrentTempWorkingDir ?? Path.Combine(Path.GetFullPath(_storeOptions.ConfigRootDirectory), ProductCode), "templates");
            Directory.CreateDirectory(templateDir);
            var safeName = name.Trim();
            var fileName = $"{safeName}.png";
            var fullPath = Path.Combine(templateDir, fileName);

            using var cropped = ExtractRoiPatch(rawMat, roi);
            if (cropped.Empty() || cropped.Width <= 0 || cropped.Height <= 0)
            {
                return;
            }

            using var gray = cropped.Channels() == 1 ? cropped.Clone() : cropped.CvtColor(OpenCvSharp.ColorConversionCodes.BGR2GRAY);
            OpenCvSharp.Cv2.ImWrite(fullPath, gray);
            if (_config is null)
            {
                return;
            }
    
            if (isOrigin)
            {
                _config.Origin.TemplateImageFile = fileName;
                // Train ShapeModel from Image 2 (tool input after local preprocess) to match runtime pipeline
                var originNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "Origin", StringComparison.OrdinalIgnoreCase));
                using var image2Mat = originNode != null ? ResolveToolImageForPreview(snap, originNode) : rawMat.Clone();
                using var cropForModel = ExtractRoiPatch(image2Mat, roi);
                if (!cropForModel.Empty() && cropForModel.Width > 0 && cropForModel.Height > 0)
                {
                    using var grayForModel = cropForModel.Channels() == 1 ? cropForModel.Clone() : cropForModel.CvtColor(OpenCvSharp.ColorConversionCodes.BGR2GRAY);
                    _config.Origin.ShapeModel = ShapeModelTrainer.Train(grayForModel);
                }
                else
                {
                    _config.Origin.ShapeModel = ShapeModelTrainer.Train(gray);
                }
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

            VisionEngine.OriginMatcher.ClearCache();
            VisionEngine.MvpShapeMatch2Engine.ClearCache();
        }
    
        [ObservableProperty]
        private string _productCode = "";
        [ObservableProperty]
        private int _totalExecutionTimeMs = 0;
        [ObservableProperty]
        private string _captureButtonText = "Capture Camera";

        [ObservableProperty]
        private double _canvasPanX = 0.0;

        [ObservableProperty]
        private double _canvasPanY = 0.0;

        public event Action? RequestAutoFitGraph;

        public void TriggerAutoFitGraph()
        {
            RequestAutoFitGraph?.Invoke();
        }

        [ObservableProperty]
        private string _toolboxSearchText = string.Empty;

        partial void OnToolboxSearchTextChanged(string value)
        {
            ToolboxCollectionView?.Refresh();
        }

        public List<ToolboxItemModel> AllToolboxItems { get; }
        public System.ComponentModel.ICollectionView ToolboxCollectionView { get; }
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
            SyncSelectedDbNode(SelectedNode);
            RefreshOriginTemplatePreview();
            OnPropertyChanged(nameof(IsPreprocessNode));
            OnPropertyChanged(nameof(PreprocessRois));
            OnPropertyChanged(nameof(IsLineNode));
            OnPropertyChanged(nameof(IsCaliperNode));
            OnPropertyChanged(nameof(IsOriginNode));
            OnPropertyChanged(nameof(AvailableOriginAlgorithms));
            OnPropertyChanged(nameof(Origin_Algorithm));
            OnPropertyChanged(nameof(Origin_MinScore));
            OnPropertyChanged(nameof(Origin_MinAngle));
            OnPropertyChanged(nameof(Origin_MaxAngle));
            OnPropertyChanged(nameof(Origin_AngleStep));
            OnPropertyChanged(nameof(Origin_EdgeThresholdMin));
            OnPropertyChanged(nameof(Origin_EdgeThresholdMax));
            OnPropertyChanged(nameof(IsOriginShapePyramid));
            OnPropertyChanged(nameof(IsPointNode));
            OnPropertyChanged(nameof(IsAnyDistanceNode));
            OnPropertyChanged(nameof(IsDistanceNode));
            OnPropertyChanged(nameof(IsLineLineDistanceNode));
            OnPropertyChanged(nameof(IsPointLineDistanceNode));
            OnPropertyChanged(nameof(IsSegmentLineDistanceNode));
            OnPropertyChanged(nameof(IsAngleNode));
            OnPropertyChanged(nameof(IsEdgePairNode));
            OnPropertyChanged(nameof(IsEdgePairDetectNode));
            OnPropertyChanged(nameof(IsDiameterNode));
            OnPropertyChanged(nameof(IsConditionNode));
            OnPropertyChanged(nameof(IsTextNode));
            OnPropertyChanged(nameof(IsImageSourceNode));
            if (IsImageSourceNode)
            {
                var curDef = SelectedImageSourceDef();
                EnsureImageSourceLightingParams(curDef);
                OnPropertyChanged(nameof(ImageSource_EnableLighting));
                OnPropertyChanged(nameof(ImageSource_LightingChannelCount));
                OnPropertyChanged(nameof(ImageSource_LightingChannels));
            }
            OnPropertyChanged(nameof(ImageSource_TriggerMode));
            OnPropertyChanged(nameof(ImageSource_IsSoftTrigger));
            OnPropertyChanged(nameof(ImageSource_IsLineTrigger));
            OnPropertyChanged(nameof(ImageSource_IsPlcTrigger));
            OnPropertyChanged(nameof(ImageSource_LineTriggerName));
            OnPropertyChanged(nameof(ImageSource_PlcTriggerPlcId));
            OnPropertyChanged(nameof(ImageSource_PlcTriggerTagName));
            OnPropertyChanged(nameof(ImageSource_PlcTriggerEdge));
            OnPropertyChanged(nameof(ImageSource_EnableUndistort));
            OnPropertyChanged(nameof(IsImageOutputNode));
            OnPropertyChanged(nameof(AvailableImageNodes));
            OnPropertyChanged(nameof(ImageOutput_InputNodeChoice));
            OnPropertyChanged(nameof(ImageOutput_SaveFolderPath));
            OnPropertyChanged(nameof(ImageOutput_FileNameFormat));
            OnPropertyChanged(nameof(ImageOutput_Format));
            OnPropertyChanged(nameof(ImageOutput_EnableOutput));
            OnPropertyChanged(nameof(ImageOutput_IncludeOverlay));
            OnPropertyChanged(nameof(ImageOutput_ShowRoi));
            OnPropertyChanged(nameof(ImageOutput_TextFontSize));
            OnPropertyChanged(nameof(ImageOutput_OverlayScale));
            OnPropertyChanged(nameof(ImageOutput_SaveCondition));
            OnPropertyChanged(nameof(IsBlobDetectionNode));
            OnPropertyChanged(nameof(Blob_Polarity));
            OnPropertyChanged(nameof(Blob_Threshold));
            OnPropertyChanged(nameof(Blob_MinBlobArea));
            OnPropertyChanged(nameof(Blob_MaxBlobArea));
            OnPropertyChanged(nameof(Blob_MaxAllowedBlobs));
            OnPropertyChanged(nameof(Blob_MinBlobDistance));
            OnPropertyChanged(nameof(Blob_MaxBlobWidth));
            OnPropertyChanged(nameof(Blob_MaxBlobLength));
            OnPropertyChanged(nameof(Blob_DistanceUnitText));
            OnPropertyChanged(nameof(Blob_LastRunCount));
            OnPropertyChanged(nameof(Blob_LastRunMinDistanceText));
            OnPropertyChanged(nameof(Blob_LastRunMaxDimensionsText));
            OnPropertyChanged(nameof(Blob_PassStatus));
            OnPropertyChanged(nameof(Blob_PassColor));
            OnPropertyChanged(nameof(IsSurfaceCompareNode));
            OnPropertyChanged(nameof(IsContourCompareNode));
            OnPropertyChanged(nameof(IsCodeDetectionNode));
            OnPropertyChanged(nameof(IsCropNode));
            OnPropertyChanged(nameof(SelectedCrop));
            OnPropertyChanged(nameof(Crop_X));
            OnPropertyChanged(nameof(Crop_Y));
            OnPropertyChanged(nameof(Crop_Width));
            OnPropertyChanged(nameof(Crop_Height));
            OnPropertyChanged(nameof(IsColorDiffNode));
            OnPropertyChanged(nameof(SelectedColorDiff));
            OnPropertyChanged(nameof(ColorDiff_UseRefColor));
            OnPropertyChanged(nameof(ColorDiff_RefL));
            OnPropertyChanged(nameof(ColorDiff_RefA));
            OnPropertyChanged(nameof(ColorDiff_RefB));
            OnPropertyChanged(nameof(ColorDiff_MaxDeltaE));
            OnPropertyChanged(nameof(IsImgArithmeticNode));
            OnPropertyChanged(nameof(SelectedImgArithmetic));
            OnPropertyChanged(nameof(ImgArithmetic_Op));
            OnPropertyChanged(nameof(ImgArithmetic_WeightA));
            OnPropertyChanged(nameof(ImgArithmetic_WeightB));
            OnPropertyChanged(nameof(ImgArithmetic_Offset));

            // Tool Creation Nodes
            OnPropertyChanged(nameof(IsCreatePointNode));
            OnPropertyChanged(nameof(SelectedCreatePoint));
            OnPropertyChanged(nameof(CreatePoint_X));
            OnPropertyChanged(nameof(CreatePoint_Y));
            OnPropertyChanged(nameof(CreatePoint_PointRef));

            OnPropertyChanged(nameof(IsCreateLineNode));
            OnPropertyChanged(nameof(SelectedCreateLine));
            OnPropertyChanged(nameof(CreateLine_Mode));
            OnPropertyChanged(nameof(CreateLine_IsTwoPointsMode));
            OnPropertyChanged(nameof(CreateLine_IsPointAndAngleMode));
            OnPropertyChanged(nameof(CreateLine_Point1Ref));
            OnPropertyChanged(nameof(CreateLine_X1));
            OnPropertyChanged(nameof(CreateLine_Y1));
            OnPropertyChanged(nameof(CreateLine_Point2Ref));
            OnPropertyChanged(nameof(CreateLine_X2));
            OnPropertyChanged(nameof(CreateLine_Y2));
            OnPropertyChanged(nameof(CreateLine_PointRef));
            OnPropertyChanged(nameof(CreateLine_X));
            OnPropertyChanged(nameof(CreateLine_Y));
            OnPropertyChanged(nameof(CreateLine_Angle));
            OnPropertyChanged(nameof(CreateLine_Length));

            OnPropertyChanged(nameof(IsCreateRectNode));
            OnPropertyChanged(nameof(SelectedCreateRect));
            OnPropertyChanged(nameof(CreateRect_PointRef));
            OnPropertyChanged(nameof(CreateRect_X));
            OnPropertyChanged(nameof(CreateRect_Y));
            OnPropertyChanged(nameof(CreateRect_Width));
            OnPropertyChanged(nameof(CreateRect_Height));
            OnPropertyChanged(nameof(CreateRect_Angle));
            OnPropertyChanged(nameof(CreateRect_Anchor));

            OnPropertyChanged(nameof(IsCreateCircleNode));
            OnPropertyChanged(nameof(SelectedCreateCircle));
            OnPropertyChanged(nameof(CreateCircle_Mode));
            OnPropertyChanged(nameof(CreateCircle_IsCenterAndRadiusMode));
            OnPropertyChanged(nameof(CreateCircle_IsTwoPointsMode));
            OnPropertyChanged(nameof(CreateCircle_CenterPointRef));
            OnPropertyChanged(nameof(CreateCircle_CenterX));
            OnPropertyChanged(nameof(CreateCircle_CenterY));
            OnPropertyChanged(nameof(CreateCircle_Radius));
            OnPropertyChanged(nameof(CreateCircle_BoundaryPointRef));
            OnPropertyChanged(nameof(CreateCircle_BoundaryX));
            OnPropertyChanged(nameof(CreateCircle_BoundaryY));

            // PLC Nodes
            OnPropertyChanged(nameof(IsPlcReadNode));
            OnPropertyChanged(nameof(IsPlcWriteNode));
            OnPropertyChanged(nameof(IsPlcWaitNode));
            OnPropertyChanged(nameof(IsPlcTriggerNode));
            OnPropertyChanged(nameof(IsPlcBatchReadNode));
            OnPropertyChanged(nameof(IsPlcBatchWriteNode));
            OnPropertyChanged(nameof(IsResultTransferNode));
            RefreshResultTransferItems();
            OnPropertyChanged(nameof(IsAnyPlcNode));
            OnPropertyChanged(nameof(AvailablePlcNames));
            OnPropertyChanged(nameof(AvailablePlcTagNames));
            OnPropertyChanged(nameof(PlcNode_PlcId));
            OnPropertyChanged(nameof(PlcNode_TagName));
            OnPropertyChanged(nameof(PlcNode_TagDataType));
            OnPropertyChanged(nameof(PlcNode_CurrentValue));
            OnPropertyChanged(nameof(PlcNode_WriteValue));
            OnPropertyChanged(nameof(PlcNode_Operator));
            OnPropertyChanged(nameof(PlcNode_TargetValue));
            OnPropertyChanged(nameof(PlcNode_TimeoutMs));
            OnPropertyChanged(nameof(PlcNode_EdgeMode));
            OnPropertyChanged(nameof(PlcNode_BatchTagListString));
            OnPropertyChanged(nameof(PlcNode_BatchWriteValuesString));
            OnPropertyChanged(nameof(AvailableContourMatchMethods));
            OnPropertyChanged(nameof(ContourCompare_MatchMethod));
            OnPropertyChanged(nameof(ContourCompare_CannyThreshold1));
            OnPropertyChanged(nameof(ContourCompare_CannyThreshold2));
            OnPropertyChanged(nameof(ContourCompare_MinContourArea));
            OnPropertyChanged(nameof(ContourCompare_MaxShapeMatchScore));
            OnPropertyChanged(nameof(ContourCompare_MaxHausdorffDistPx));
            OnPropertyChanged(nameof(ContourCompare_MaxAreaDiffPercent));
            OnPropertyChanged(nameof(ContourCompare_LastRunScore));
            OnPropertyChanged(nameof(ContourCompare_LastRunMaxDist));
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
            OnPropertyChanged(nameof(IsCircleFinderNode));
            OnPropertyChanged(nameof(AvailableCircleFindAlgorithms));
            OnPropertyChanged(nameof(Cf_Algorithm));
            OnPropertyChanged(nameof(Cf_StripCount));
            OnPropertyChanged(nameof(Cf_StripWidth));
            OnPropertyChanged(nameof(Cf_StripLength));
            OnPropertyChanged(nameof(Cf_Polarity));
            OnPropertyChanged(nameof(Cf_EdgeSelection));
            OnPropertyChanged(nameof(Cf_MinEdgeStrength));
            OnPropertyChanged(nameof(Cf_MinAngleDeg));
            OnPropertyChanged(nameof(Cf_MaxAngleDeg));
            OnPropertyChanged(nameof(Cf_MinRadiusPx));
            OnPropertyChanged(nameof(Cf_MaxRadiusPx));
            OnPropertyChanged(nameof(Cf_HoughDp));
            OnPropertyChanged(nameof(Cf_HoughMinDistPx));
            OnPropertyChanged(nameof(Cf_HoughParam1));
            OnPropertyChanged(nameof(Cf_HoughParam2));
            OnPropertyChanged(nameof(Cf_Canny1));
            OnPropertyChanged(nameof(Cf_Canny2));
            OnPropertyChanged(nameof(Cf_MinCircularity));
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
            OnPropertyChanged(nameof(SegmentLineDistance_LineA));
            OnPropertyChanged(nameof(SegmentLineDistance_LineB));
            OnPropertyChanged(nameof(Angle_LineA));
            OnPropertyChanged(nameof(Angle_LineB));
            OnPropertyChanged(nameof(EdgePair_RefA));
            OnPropertyChanged(nameof(EdgePair_RefB));
            OnPropertyChanged(nameof(AvailableLineLineDistanceModes));
            OnPropertyChanged(nameof(AvailablePointLineDistanceModes));
            OnPropertyChanged(nameof(AvailableSegmentLineDistanceModes));
            OnPropertyChanged(nameof(AvailableSegmentLineExtensionModes));
            OnPropertyChanged(nameof(LineLineDistance_Mode));
            OnPropertyChanged(nameof(PointLineDistance_Mode));
            OnPropertyChanged(nameof(SegmentLineDistance_Mode));
            OnPropertyChanged(nameof(SegmentLineDistance_ExtensionMode));
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
            // Refresh camera list asynchronously when empty
            if (ImageSource_IsCamera && AvailableCameraItems.Count == 0)
                RefreshAvailableCameraItems();
            OnPropertyChanged(nameof(SelectedCameraItem));
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
            OnPropertyChanged(nameof(ThresholdType));
            OnPropertyChanged(nameof(IsThresholdBinary));
            OnPropertyChanged(nameof(IsThresholdLocal));
            OnPropertyChanged(nameof(ThresholdValue));
            OnPropertyChanged(nameof(ThresholdLow));
            OnPropertyChanged(nameof(ThresholdHigh));
            OnPropertyChanged(nameof(InvertBinary));
            OnPropertyChanged(nameof(MaskWidth));
            OnPropertyChanged(nameof(MaskHeight));
            OnPropertyChanged(nameof(LocalOffset));
            OnPropertyChanged(nameof(InvertLocal));
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
            OnPropertyChanged(nameof(SurfaceCompare_EdgeTolerancePx));
            OnPropertyChanged(nameof(SurfaceCompare_Algorithm));
            OnPropertyChanged(nameof(SurfaceCompare_SsimWindowSize));
            OnPropertyChanged(nameof(SurfaceCompare_SsimThreshold));
            OnPropertyChanged(nameof(SurfaceCompare_GradientWeight));
            OnPropertyChanged(nameof(SurfaceCompare_AutoAlign));
            OnPropertyChanged(nameof(SurfaceCompare_AutoAlignMaxShiftPx));
            OnPropertyChanged(nameof(ContourCompare_MatchMethod));
            OnPropertyChanged(nameof(ContourCompare_CannyThreshold1));
            OnPropertyChanged(nameof(ContourCompare_CannyThreshold2));
            OnPropertyChanged(nameof(ContourCompare_MinContourArea));
            OnPropertyChanged(nameof(ContourCompare_MaxShapeMatchScore));
            OnPropertyChanged(nameof(ContourCompare_MaxHausdorffDistPx));
            OnPropertyChanged(nameof(ContourCompare_MaxAreaDiffPercent));
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

        public bool IsCropNode => string.Equals(SelectedNode?.Type, "Crop", StringComparison.OrdinalIgnoreCase);
        public bool IsColorDiffNode => string.Equals(SelectedNode?.Type, "ColorDiff", StringComparison.OrdinalIgnoreCase);
        public bool IsImgArithmeticNode => string.Equals(SelectedNode?.Type, "ImgArithmetic", StringComparison.OrdinalIgnoreCase);

        public ICommand OpenCalibrationDialogCommand { get; }

        private static VisionInspectionApp.UI.Views.CalibrationDialog? _calibrationDialogInstance;
        private static VisionInspectionApp.UI.Views.ChessboardCalibrationDialog? _chessboardCalibrationDialogInstance;
        private static Views.OQC.ProductAssignDialog? _productAssignDialogInstance;
        private static Views.InspectionLogWindow? _inspectionLogWindowInstance;
        private static Views.RollDefectMapWindow? _rollDefectMapWindowInstance;

        private void OpenCalibrationDialog()
        {
            if (_config is null)
            {
                System.Windows.MessageBox.Show("Chưa mở Job nào để thực hiện Calibration.", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            if (_calibrationDialogInstance != null && _calibrationDialogInstance.IsLoaded)
            {
                _calibrationDialogInstance.Activate();
                if (_calibrationDialogInstance.WindowState == WindowState.Minimized)
                    _calibrationDialogInstance.WindowState = WindowState.Normal;
                return;
            }

            var calibVm = new CalibrationViewModel(_configService, _storeOptions, _cameraService, _jobService);
            calibVm.InitializeWithConfig(_config, CurrentJobFilePath, SelectedNodePreviewImage);

            _calibrationDialogInstance = new VisionInspectionApp.UI.Views.CalibrationDialog
            {
                DataContext = calibVm
            };

            _calibrationDialogInstance.Closed += (s, e) =>
            {
                _calibrationDialogInstance = null;
                if (calibVm.IsDirty)
                {
                    OnPropertyChanged(nameof(PixelsPerMm));
                    IsDirty = true;
                    RefreshPreviews();
                }
            };

            _calibrationDialogInstance.Show();
        }

        public ICommand OpenChessboardCalibrationDialogCommand { get; }

        private void OpenChessboardCalibrationDialog()
        {
            if (_config is null)
            {
                System.Windows.MessageBox.Show("Chưa mở Job nào để thực hiện Chessboard Calibration.", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            if (_chessboardCalibrationDialogInstance != null && _chessboardCalibrationDialogInstance.IsLoaded)
            {
                _chessboardCalibrationDialogInstance.Activate();
                if (_chessboardCalibrationDialogInstance.WindowState == WindowState.Minimized)
                    _chessboardCalibrationDialogInstance.WindowState = WindowState.Normal;
                return;
            }

            var vm = new ChessboardCalibrationViewModel(_cameraService);
            vm.Initialize(_config);

            _chessboardCalibrationDialogInstance = new VisionInspectionApp.UI.Views.ChessboardCalibrationDialog
            {
                DataContext = vm
            };

            _chessboardCalibrationDialogInstance.Closed += (s, e) =>
            {
                _chessboardCalibrationDialogInstance = null;
                if (vm.IsDirty)
                {
                    OnPropertyChanged(nameof(PixelsPerMm));
                    IsDirty = true;
                    RefreshPreviews();
                }
            };

            _chessboardCalibrationDialogInstance.Show();
        }

        public IRelayCommand OpenProductAssignDialogCommand { get; }
        public IRelayCommand OpenJobManagerWindowCommand { get; }
        public IAsyncRelayCommand ImageSource_FetchUrlImageCommand { get; }
        public IRelayCommand ImageSource_OpenJobManagerCommand { get; }
        public IRelayCommand OpenRollDefectMapCommand { get; }
        public IRelayCommand OpenInspectionLogCommand { get; }

        private static Views.OQC.JobManagerWindow? _jobManagerWindowInstance;

        public void OpenJobManagerWindow()
        {
            try
            {
                if (_jobManagerWindowInstance != null && _jobManagerWindowInstance.IsLoaded)
                {
                    _jobManagerWindowInstance.Activate();
                    if (_jobManagerWindowInstance.WindowState == WindowState.Minimized)
                        _jobManagerWindowInstance.WindowState = WindowState.Normal;
                    return;
                }

                var mainVm = System.Windows.Application.Current?.MainWindow?.DataContext as MainWindowViewModel;
                var oqcVm = _serviceProvider?.GetService(typeof(OqcScannerViewModel)) as OqcScannerViewModel ?? mainVm?.OqcScanner;
                var oqcService = (_serviceProvider?.GetService(typeof(IOqcScannerService)) as IOqcScannerService) ?? new OqcScannerService();
                var dbManager = _dbManagerService ?? new Application.DB.Services.DbManagerService();

                var jobVm = new JobManagerViewModel(
                    oqcService,
                    dbManager,
                    _remoteServerService,
                    _cameraService,
                    _sharedImage,
                    this,
                    mainVm ?? (_serviceProvider?.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel)!,
                    _jobService
                );

                var mainWin = System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()
                              ?? System.Windows.Application.Current?.MainWindow;
                _jobManagerWindowInstance = new Views.OQC.JobManagerWindow(jobVm);
                if (mainWin != null && mainWin != _jobManagerWindowInstance && mainWin.IsLoaded)
                {
                    _jobManagerWindowInstance.Owner = mainWin;
                }
                _jobManagerWindowInstance.Closed += (s, e) => _jobManagerWindowInstance = null;
                _jobManagerWindowInstance.Show();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi mở cửa sổ Quản Lý Job: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void OpenProductAssignDialog()
        {
            try
            {
                if (_productAssignDialogInstance != null && _productAssignDialogInstance.IsLoaded)
                {
                    _productAssignDialogInstance.Activate();
                    if (_productAssignDialogInstance.WindowState == WindowState.Minimized)
                        _productAssignDialogInstance.WindowState = WindowState.Normal;
                    return;
                }

                var oqcVm = _serviceProvider?.GetService(typeof(OqcScannerViewModel)) as OqcScannerViewModel;
                if (oqcVm == null && System.Windows.Application.Current?.MainWindow?.DataContext is MainWindowViewModel mainVm)
                {
                    oqcVm = mainVm.OqcScanner;
                }

                if (oqcVm != null)
                {
                    oqcVm.AssignJobFilePath = !string.IsNullOrWhiteSpace(CurrentJobFilePath) && CurrentJobFilePath != "-" ? CurrentJobFilePath : "";
                    _productAssignDialogInstance = new Views.OQC.ProductAssignDialog(oqcVm);
                    _productAssignDialogInstance.Closed += (s, e) => _productAssignDialogInstance = null;
                    _productAssignDialogInstance.Show();
                }
                else
                {
                    System.Windows.MessageBox.Show("Không thể tìm thấy mô-đun OQC Scanner để mở giao diện Gán Mã Sản Phẩm.", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi mở hộp thoại Gán Mã Sản Phẩm: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void OpenInspectionLog()
        {
            try
            {
                if (_inspectionLogWindowInstance != null && _inspectionLogWindowInstance.IsLoaded)
                {
                    _inspectionLogWindowInstance.Activate();
                    if (_inspectionLogWindowInstance.WindowState == WindowState.Minimized)
                        _inspectionLogWindowInstance.WindowState = WindowState.Normal;
                    return;
                }

                var vm = new InspectionLogViewModel(_inspectionLogService);
                _inspectionLogWindowInstance = new Views.InspectionLogWindow(vm);
                _inspectionLogWindowInstance.Closed += (s, e) => _inspectionLogWindowInstance = null;
                _inspectionLogWindowInstance.Show();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi mở Lịch sử kiểm tra & CPK: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void OpenRollDefectMap()
        {
            try
            {
                if (_rollDefectMapWindowInstance != null && _rollDefectMapWindowInstance.IsLoaded)
                {
                    _rollDefectMapWindowInstance.Activate();
                    if (_rollDefectMapWindowInstance.WindowState == WindowState.Minimized)
                        _rollDefectMapWindowInstance.WindowState = WindowState.Normal;
                    return;
                }

                var vm = new RollDefectMapViewModel(_rollDefectManager, _motionSyncService, _shiftRegisterTracker);
                _rollDefectMapWindowInstance = new Views.RollDefectMapWindow(vm);
                _rollDefectMapWindowInstance.Closed += (s, e) => _rollDefectMapWindowInstance = null;
                _rollDefectMapWindowInstance.Show();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi mở Bản đồ khuyết tật cuộn: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ApplyIndustrialConfig(PlcIndustrialConfig? config)
        {
            if (config == null) return;

            if (config.Handshake != null)
            {
                _handshakeStateMachine.IsEnabled = config.Handshake.IsEnabled;
                _handshakeStateMachine.PlcId = config.Handshake.PlcId;
                _handshakeStateMachine.ReadyTagName = config.Handshake.ReadyTagName;
                _handshakeStateMachine.BusyTagName = config.Handshake.BusyTagName;
                _handshakeStateMachine.DoneTagName = config.Handshake.DoneTagName;
                _handshakeStateMachine.PassTagName = config.Handshake.PassTagName;
                _handshakeStateMachine.NgTagName = config.Handshake.NgTagName;
                _handshakeStateMachine.PlcAckTagName = config.Handshake.PlcAckTagName;
                _handshakeStateMachine.HandshakeTimeoutMs = config.Handshake.HandshakeTimeoutMs;
            }

            if (config.Heartbeat != null)
            {
                _plcHeartbeatWatchdog.PlcId = config.Heartbeat.PlcId;
                _plcHeartbeatWatchdog.VisionHeartbeatTagName = config.Heartbeat.VisionHeartbeatTagName;
                _plcHeartbeatWatchdog.PlcHeartbeatTagName = config.Heartbeat.PlcHeartbeatTagName;
                _plcHeartbeatWatchdog.IntervalMs = config.Heartbeat.IntervalMs;
                _plcHeartbeatWatchdog.TimeoutMs = config.Heartbeat.TimeoutMs;
                _plcHeartbeatWatchdog.EnableEmergencyInterlock = config.Heartbeat.EnableEmergencyInterlock;
                _plcHeartbeatWatchdog.EmergencyStopTagName = config.Heartbeat.EmergencyStopTagName;
            }

            if (config.Motion != null)
            {
                _motionSyncService.PlcId = config.Motion.PlcId;
                _motionSyncService.EncoderTagName = config.Motion.EncoderTagName;
                _motionSyncService.SpeedTagName = config.Motion.SpeedTagName;
                _motionSyncService.PulsesPerMm = config.Motion.PulsesPerMm;
                _motionSyncService.MmPerPixel = config.Motion.MmPerPixel;
                _motionSyncService.NominalSpeedMpm = config.Motion.NominalSpeedMpm;
                _motionSyncService.BaseExposureTimeUs = config.Motion.BaseExposureTimeUs;
            }

            if (config.ShiftRegister != null)
            {
                _shiftRegisterTracker.PlcId = config.ShiftRegister.PlcId;
                _shiftRegisterTracker.RejectTagName = config.ShiftRegister.RejectTagName;
                _shiftRegisterTracker.RejectStationDistanceMm = config.ShiftRegister.RejectStationDistanceMm;
                _shiftRegisterTracker.RejectToleranceMm = config.ShiftRegister.RejectToleranceMm;
                _shiftRegisterTracker.PulseDurationMs = config.ShiftRegister.PulseDurationMs;
                _shiftRegisterTracker.IsEnabled = config.ShiftRegister.IsEnabled;
            }
        }

        public double PixelsPerMm
        {
            get => _config?.PixelsPerMm ?? 1.0;
            set
            {
                if (_config != null && Math.Abs(_config.PixelsPerMm - value) > 0.00001)
                {
                    _config.PixelsPerMm = value;
                    OnPropertyChanged();
                    IsDirty = true;
                }
            }
        }

        // Crop Properties
        public CropDefinition? SelectedCrop => _config?.Crops?.FirstOrDefault(x => string.Equals(x.Name, SelectedNode?.RefName, StringComparison.OrdinalIgnoreCase));
        
        public int Crop_X
        {
            get => SelectedCrop?.CropRoi?.X ?? 0;
            set { if (SelectedCrop?.CropRoi != null && SelectedCrop.CropRoi.X != value) { SelectedCrop.CropRoi.X = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }
        public int Crop_Y
        {
            get => SelectedCrop?.CropRoi?.Y ?? 0;
            set { if (SelectedCrop?.CropRoi != null && SelectedCrop.CropRoi.Y != value) { SelectedCrop.CropRoi.Y = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }
        public int Crop_Width
        {
            get => SelectedCrop?.CropRoi?.Width ?? 100;
            set { if (SelectedCrop?.CropRoi != null && SelectedCrop.CropRoi.Width != value) { SelectedCrop.CropRoi.Width = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }
        public int Crop_Height
        {
            get => SelectedCrop?.CropRoi?.Height ?? 100;
            set { if (SelectedCrop?.CropRoi != null && SelectedCrop.CropRoi.Height != value) { SelectedCrop.CropRoi.Height = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }

        // ColorDiff Properties
        public ColorDiffDefinition? SelectedColorDiff => _config?.ColorDiffs?.FirstOrDefault(x => string.Equals(x.Name, SelectedNode?.RefName, StringComparison.OrdinalIgnoreCase));

        public bool ColorDiff_UseRefColor
        {
            get => SelectedColorDiff?.UseRefColor ?? true;
            set { if (SelectedColorDiff != null && SelectedColorDiff.UseRefColor != value) { SelectedColorDiff.UseRefColor = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }
        public double ColorDiff_RefL
        {
            get => SelectedColorDiff?.RefL ?? 0.0;
            set { if (SelectedColorDiff != null && Math.Abs(SelectedColorDiff.RefL - value) > 0.01) { SelectedColorDiff.RefL = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }
        public double ColorDiff_RefA
        {
            get => SelectedColorDiff?.RefA ?? 0.0;
            set { if (SelectedColorDiff != null && Math.Abs(SelectedColorDiff.RefA - value) > 0.01) { SelectedColorDiff.RefA = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }
        public double ColorDiff_RefB
        {
            get => SelectedColorDiff?.RefB ?? 0.0;
            set { if (SelectedColorDiff != null && Math.Abs(SelectedColorDiff.RefB - value) > 0.01) { SelectedColorDiff.RefB = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }
        public double ColorDiff_MaxDeltaE
        {
            get => SelectedColorDiff?.MaxDeltaE ?? 5.0;
            set { if (SelectedColorDiff != null && Math.Abs(SelectedColorDiff.MaxDeltaE - value) > 0.01) { SelectedColorDiff.MaxDeltaE = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }

        // ImgArithmetic Properties
        public ImgArithmeticDefinition? SelectedImgArithmetic => _config?.ImgArithmetics?.FirstOrDefault(x => string.Equals(x.Name, SelectedNode?.RefName, StringComparison.OrdinalIgnoreCase));

        public ImgArithmeticOp ImgArithmetic_Op
        {
            get => SelectedImgArithmetic?.Op ?? ImgArithmeticOp.ADD;
            set { if (SelectedImgArithmetic != null && SelectedImgArithmetic.Op != value) { SelectedImgArithmetic.Op = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }
        public double ImgArithmetic_WeightA
        {
            get => SelectedImgArithmetic?.WeightA ?? 0.5;
            set { if (SelectedImgArithmetic != null && Math.Abs(SelectedImgArithmetic.WeightA - value) > 0.01) { SelectedImgArithmetic.WeightA = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }
        public double ImgArithmetic_WeightB
        {
            get => SelectedImgArithmetic?.WeightB ?? 0.5;
            set { if (SelectedImgArithmetic != null && Math.Abs(SelectedImgArithmetic.WeightB - value) > 0.01) { SelectedImgArithmetic.WeightB = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }
        public double ImgArithmetic_Offset
        {
            get => SelectedImgArithmetic?.Offset ?? 0.0;
            set { if (SelectedImgArithmetic != null && Math.Abs(SelectedImgArithmetic.Offset - value) > 0.01) { SelectedImgArithmetic.Offset = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }

        public ObservableCollection<ImgArithmeticOp> AvailableImgArithmeticOps { get; } = new()
        {
            ImgArithmeticOp.ADD,
            ImgArithmeticOp.SUB,
            ImgArithmeticOp.MIN,
            ImgArithmeticOp.MAX,
            ImgArithmeticOp.BIT_AND,
            ImgArithmeticOp.BIT_OR,
            ImgArithmeticOp.BIT_XOR,
            ImgArithmeticOp.BIT_NOT
        };

        public string ImgArithmetic_ImageSourceRefA
        {
            get => SelectedImgArithmetic?.ImageSourceRefA ?? string.Empty;
            set { if (SelectedImgArithmetic != null && SelectedImgArithmetic.ImageSourceRefA != value) { SelectedImgArithmetic.ImageSourceRefA = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }

        public string ImgArithmetic_ImageSourceRefB
        {
            get => SelectedImgArithmetic?.ImageSourceRefB ?? string.Empty;
            set { if (SelectedImgArithmetic != null && SelectedImgArithmetic.ImageSourceRefB != value) { SelectedImgArithmetic.ImageSourceRefB = value; OnPropertyChanged(); IsDirty = true; RefreshPreviews(); } }
        }

        public ICommand ColorDiff_TeachRefColorCommand { get; }

        private void ColorDiff_TeachRefColor()
        {
            if (_config is null || SelectedColorDiff is null)
            {
                System.Windows.MessageBox.Show("Vui lòng chọn 1 Node ColorDiff trước khi lấy màu mẫu.", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            using var rawSnap = _sharedImage.GetSnapshot();
            using var snap = rawSnap ?? new OpenCvSharp.Mat();
            if (snap.Empty())
            {
                System.Windows.MessageBox.Show("Chưa có ảnh đầu vào để lấy mẫu màu. Vui lòng bấm Run Once hoặc nạp ảnh.", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            using var inputMat = ResolveToolImageForPreview(snap, SelectedNode!);
            if (inputMat.Empty())
            {
                System.Windows.MessageBox.Show("Không thể lấy ảnh đầu vào cho Node ColorDiff.", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            var roi = SelectedColorDiff.InspectRoi;
            if (roi == null || roi.Width <= 0 || roi.Height <= 0)
            {
                roi = new Roi { X = 50, Y = 50, Width = 150, Height = 150 };
                SelectedColorDiff.InspectRoi = roi;
            }

            // Transform ROI according to current Origin match pose on the active image (symmetric with Inspection pipeline)
            var originTeach = new OpenCvSharp.Point2d(_config.Origin.WorldPosition.X, _config.Origin.WorldPosition.Y);
            if (originTeach.X == 0 && originTeach.Y == 0 && _config.Origin.TemplateRoi.Width > 0)
            {
                originTeach = new OpenCvSharp.Point2d(_config.Origin.TemplateRoi.X + _config.Origin.TemplateRoi.Width / 2.0, _config.Origin.TemplateRoi.Y + _config.Origin.TemplateRoi.Height / 2.0);
            }
            else if (originTeach.X == 0 && originTeach.Y == 0 && _config.Origin.SearchRoi.Width > 0)
            {
                originTeach = new OpenCvSharp.Point2d(_config.Origin.SearchRoi.X + _config.Origin.SearchRoi.Width / 2.0, _config.Origin.SearchRoi.Y + _config.Origin.SearchRoi.Height / 2.0);
            }

            OpenCvSharp.Point2d originFound = originTeach;
            double angleDeg = 0.0;

            if (_lastRun?.Origin != null && _lastRun.Origin.Pass && (_lastRun.Origin.Position.X != 0 || _lastRun.Origin.Position.Y != 0))
            {
                originFound = new OpenCvSharp.Point2d(_lastRun.Origin.Position.X, _lastRun.Origin.Position.Y);
                angleDeg = _lastRun.Origin.AngleDeg;
            }

            var centerTeach = new OpenCvSharp.Point2d(roi.X + roi.Width / 2.0, roi.Y + roi.Height / 2.0);
            var centerFound = TransformPose(centerTeach, originTeach, originFound, angleDeg);

            var sampleRoi = new Roi
            {
                X = (int)Math.Round(centerFound.X - roi.Width / 2.0),
                Y = (int)Math.Round(centerFound.Y - roi.Height / 2.0),
                Width = roi.Width,
                Height = roi.Height,
                Angle = Math.Round(roi.Angle + angleDeg, 1)
            };

            var (l, a, b) = ColorDiffProcessor.GetMeanLab(inputMat, sampleRoi);

            SelectedColorDiff.RefL = Math.Round(l, 2);
            SelectedColorDiff.RefA = Math.Round(a, 2);
            SelectedColorDiff.RefB = Math.Round(b, 2);
            SelectedColorDiff.UseRefColor = true;

            OnPropertyChanged(nameof(ColorDiff_RefL));
            OnPropertyChanged(nameof(ColorDiff_RefA));
            OnPropertyChanged(nameof(ColorDiff_RefB));
            OnPropertyChanged(nameof(ColorDiff_UseRefColor));
            IsDirty = true;
            RefreshPreviews();

            System.Windows.MessageBox.Show($"Đã lấy màu mẫu thành công từ vùng ROI!\r\n\r\nCIELab Ref Values:\r\nL = {l:F2}\r\na = {a:F2}\r\nb = {b:F2}", "Lấy Mẫu Màu Thành Công", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private (OpenCvSharp.Point2d Center, double AngleDeg) GetCurrentPointPatternCenterAndAngle(PointDefinition p)
        {
            var teachCenter = new OpenCvSharp.Point2d(p.TemplateRoi.X + p.TemplateRoi.Width / 2.0, p.TemplateRoi.Y + p.TemplateRoi.Height / 2.0);
            if (_config is null)
            {
                return (teachCenter, p.TemplateRoi.Angle);
            }

            if (_lastRun?.Points is not null)
            {
                var matchRes = _lastRun.Points.FirstOrDefault(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                if (matchRes is not null && matchRes.Pass && matchRes.MatchRect.Width > 0 && matchRes.MatchRect.Height > 0)
                {
                    var matchCenter = new OpenCvSharp.Point2d(matchRes.MatchRect.X + matchRes.MatchRect.Width / 2.0, matchRes.MatchRect.Y + matchRes.MatchRect.Height / 2.0);
                    return (matchCenter, matchRes.AngleDeg);
                }
            }

            if (_lastRun?.Origin is not null && _config.Origin is not null)
            {
                var originTeach = new OpenCvSharp.Point2d(_config.Origin.WorldPosition.X, _config.Origin.WorldPosition.Y);
                var originFound = new OpenCvSharp.Point2d(_lastRun.Origin.Position.X, _lastRun.Origin.Position.Y);
                var angleDeg = _lastRun.Origin.AngleDeg;
                if (Math.Abs(angleDeg) > 0.0001 || Math.Abs(originFound.X - originTeach.X) > 0.0001 || Math.Abs(originFound.Y - originTeach.Y) > 0.0001)
                {
                    var dx = teachCenter.X - originTeach.X;
                    var dy = teachCenter.Y - originTeach.Y;
                    var rad = angleDeg * Math.PI / 180.0;
                    var cos = Math.Cos(rad);
                    var sin = Math.Sin(rad);
                    var rotX = dx * cos - dy * sin + originFound.X;
                    var rotY = dx * sin + dy * cos + originFound.Y;
                    return (new OpenCvSharp.Point2d(rotX, rotY), angleDeg + p.TemplateRoi.Angle);
                }
            }

            return (teachCenter, p.TemplateRoi.Angle);
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
    
            var (patternCenter, patternAngle) = GetCurrentPointPatternCenterAndAngle(p);
            var dx = click.X - patternCenter.X;
            var dy = click.Y - patternCenter.Y;
            var rad = -patternAngle * Math.PI / 180.0;
            var unRotX = dx * Math.Cos(rad) - dy * Math.Sin(rad);
            var unRotY = dx * Math.Sin(rad) + dy * Math.Cos(rad);
            p.OffsetPx.X = Math.Round(unRotX, 2);
            p.OffsetPx.Y = Math.Round(unRotY, 2);
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            RequestAutoSave();
        }
    
        public bool IsDistanceNode => string.Equals(SelectedNode?.Type, "Distance", StringComparison.OrdinalIgnoreCase);
        public bool IsLineLineDistanceNode => string.Equals(SelectedNode?.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase);
        public bool IsPointLineDistanceNode => string.Equals(SelectedNode?.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase);
        public bool IsSegmentLineDistanceNode => string.Equals(SelectedNode?.Type, "SegmentLineDistance", StringComparison.OrdinalIgnoreCase);
        public bool IsAngleNode => string.Equals(SelectedNode?.Type, "Angle", StringComparison.OrdinalIgnoreCase);
        public bool IsConditionNode => string.Equals(SelectedNode?.Type, "Condition", StringComparison.OrdinalIgnoreCase);
        public bool IsTextNode => string.Equals(SelectedNode?.Type, "Text", StringComparison.OrdinalIgnoreCase);
        public bool IsImageSourceNode => string.Equals(SelectedNode?.Type, "ImageSource", StringComparison.OrdinalIgnoreCase);
        public bool IsPreprocessNode => string.Equals(SelectedNode?.Type, "Preprocess", StringComparison.OrdinalIgnoreCase);
        public bool IsAnyDistanceNode => IsDistanceNode || IsLineLineDistanceNode || IsPointLineDistanceNode || IsSegmentLineDistanceNode || IsAngleNode || IsEdgePairNode || IsEdgePairDetectNode || IsDiameterNode;
    
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

        public string Cdt_ExpectedText
        {
            get => SelectedCodeDetectionDef()?.ExpectedText ?? string.Empty;
            set
            {
                var d = SelectedCodeDetectionDef();
                if (d is null)
                    return;
                if (d.ExpectedText == value)
                    return;
                d.ExpectedText = value ?? string.Empty;
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
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
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
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
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
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
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
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
            }
        }

        public int ThresholdValue
        {
            get => GetActivePreprocessSettingsForUi()?.ThresholdValue ?? 128;
            set
            {
                var s = GetActivePreprocessSettingsForUi();
                if (s is null || s.ThresholdValue == value) return;
                s.ThresholdValue = value;
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThresholdLow));
            }
        }

        public IEnumerable<PreprocessThresholdType> AvailableThresholdTypes => Enum.GetValues<PreprocessThresholdType>();

        public PreprocessThresholdType ThresholdType
        {
            get => GetActivePreprocessSettingsForUi()?.ThresholdType ?? PreprocessThresholdType.Binary;
            set
            {
                var s = GetActivePreprocessSettingsForUi();
                if (s is null || s.ThresholdType == value) return;
                s.ThresholdType = value;
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsThresholdBinary));
                OnPropertyChanged(nameof(IsThresholdLocal));
            }
        }

        public bool IsThresholdBinary => ThresholdType == PreprocessThresholdType.Binary;
        public bool IsThresholdLocal => ThresholdType == PreprocessThresholdType.Local;

        public int ThresholdLow
        {
            get => GetActivePreprocessSettingsForUi()?.ThresholdLow ?? 128;
            set
            {
                var s = GetActivePreprocessSettingsForUi();
                if (s is null || s.ThresholdLow == value) return;
                s.ThresholdLow = value;
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThresholdValue));
            }
        }

        public int ThresholdHigh
        {
            get => GetActivePreprocessSettingsForUi()?.ThresholdHigh ?? 255;
            set
            {
                var s = GetActivePreprocessSettingsForUi();
                if (s is null || s.ThresholdHigh == value) return;
                s.ThresholdHigh = value;
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
            }
        }

        public bool InvertBinary
        {
            get => GetActivePreprocessSettingsForUi()?.InvertBinary ?? false;
            set
            {
                var s = GetActivePreprocessSettingsForUi();
                if (s is null || s.InvertBinary == value) return;
                s.InvertBinary = value;
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
            }
        }

        public int MaskWidth
        {
            get => GetActivePreprocessSettingsForUi()?.MaskWidth ?? 11;
            set
            {
                var s = GetActivePreprocessSettingsForUi();
                if (s is null || s.MaskWidth == value) return;
                s.MaskWidth = value;
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
            }
        }

        public int MaskHeight
        {
            get => GetActivePreprocessSettingsForUi()?.MaskHeight ?? 11;
            set
            {
                var s = GetActivePreprocessSettingsForUi();
                if (s is null || s.MaskHeight == value) return;
                s.MaskHeight = value;
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
            }
        }

        public double LocalOffset
        {
            get => GetActivePreprocessSettingsForUi()?.LocalOffset ?? 10.0;
            set
            {
                var s = GetActivePreprocessSettingsForUi();
                if (s is null || Math.Abs(s.LocalOffset - value) < 1e-6) return;
                s.LocalOffset = value;
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
            }
        }

        public bool InvertLocal
        {
            get => GetActivePreprocessSettingsForUi()?.InvertLocal ?? false;
            set
            {
                var s = GetActivePreprocessSettingsForUi();
                if (s is null || s.InvertLocal == value) return;
                s.InvertLocal = value;
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
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
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
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
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
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
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
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
                SchedulePreprocessPreviewUpdate();
                OnPropertyChanged();
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

                if (_config.Origin is not null)
                {
                    list.Add("Origin");
                }

                if (_config.Points != null)
                {
                    foreach (var p in _config.Points.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        if (!list.Contains(p)) list.Add(p);
                    }
                }

                if (_config.CreatePoints != null)
                {
                    foreach (var cp in _config.CreatePoints.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        if (!list.Contains(cp)) list.Add(cp);
                    }
                }

                if (_config.CircleFinders != null)
                {
                    foreach (var cf in _config.CircleFinders.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        if (!list.Contains(cf)) list.Add(cf);
                    }
                }

                if (_config.BlobDetections != null)
                {
                    foreach (var bd in _config.BlobDetections.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        if (!list.Contains(bd)) list.Add(bd);
                    }
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

                if (_config.CreateLines != null)
                {
                    foreach (var cl in _config.CreateLines.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        if (!list.Contains(cl))
                        {
                            list.Add(cl);
                        }
                    }
                }

                if (_config.LinePairDetections != null)
                {
                    foreach (var lpd in _config.LinePairDetections.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        if (!list.Contains(lpd))
                        {
                            list.Add(lpd);
                        }
                    }
                }

                if (_config.EdgePairDetections != null)
                {
                    foreach (var epd in _config.EdgePairDetections.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        if (!list.Contains(epd))
                        {
                            list.Add(epd);
                        }
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
    
        private void SyncInputEdgeForSegmentLineDistancePort(string port, string? lineName)
        {
            if (_syncingInputs)
                return;
            if (_config is null || SelectedNode is null)
                return;
            if (!string.Equals(SelectedNode.Type, "SegmentLineDistance", StringComparison.OrdinalIgnoreCase))
                return;
            _syncingInputs = true;
            try
            {
                RemoveEdgesToSelectedNodePort(port);
                if (!string.IsNullOrWhiteSpace(lineName))
                {
                    var from = Nodes.FirstOrDefault(n => (string.Equals(n.Type, "Line", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n.Type, "Caliper", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n.Type, "LinePairDetect", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n.Type, "EdgePair", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n.Type, "CreateLine", StringComparison.OrdinalIgnoreCase))
                        && string.Equals(n.RefName, lineName, StringComparison.OrdinalIgnoreCase));
                    if (from is not null)
                    {
                        from.EnsurePortsInitialized();
                        SelectedNode.EnsurePortsInitialized();
                        CreateEdge(from, SelectedNode, from.OutPorts.FirstOrDefault()?.Name ?? "Out", port);
                    }
                }
            }
            finally
            {
                _syncingInputs = false;
            }
        }

        public ObservableCollection<SegmentLineDistanceMode> AvailableSegmentLineDistanceModes { get; } = new ObservableCollection<SegmentLineDistanceMode>((SegmentLineDistanceMode[])Enum.GetValues(typeof(SegmentLineDistanceMode)));
        public ObservableCollection<SegmentLineExtensionMode> AvailableSegmentLineExtensionModes { get; } = new ObservableCollection<SegmentLineExtensionMode>((SegmentLineExtensionMode[])Enum.GetValues(typeof(SegmentLineExtensionMode)));
        public ObservableCollection<CaliperOrientation> AvailableCaliperOrientations { get; } = new ObservableCollection<CaliperOrientation>((CaliperOrientation[])Enum.GetValues(typeof(CaliperOrientation)));
        public ObservableCollection<IlluminationCorrectionPreset> AvailableIlluminationCorrectionPresets { get; } = new ObservableCollection<IlluminationCorrectionPreset>((IlluminationCorrectionPreset[])Enum.GetValues(typeof(IlluminationCorrectionPreset)));
        public ObservableCollection<EdgePolarity> AvailableEdgePolarities { get; } = new ObservableCollection<EdgePolarity>((EdgePolarity[])Enum.GetValues(typeof(EdgePolarity)));
        public ObservableCollection<EdgeSelection> AvailableEdgeSelections { get; } = new ObservableCollection<EdgeSelection>((EdgeSelection[])Enum.GetValues(typeof(EdgeSelection)));
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

        public (Point2d Center, double AngleDeg) GetRoiPose(Roi roi)
        {
            if (roi.Width <= 0 || roi.Height <= 0)
            {
                return (new Point2d(0, 0), 0);
            }

            var centerTeach = new Point2d(roi.X + roi.Width / 2.0, roi.Y + roi.Height / 2.0);

            if (_lastRun is not null && _config is not null && _lastRun.Origin is not null && (_lastRun.Origin.MatchRect.Width > 0 || _lastRun.Origin.Position.X != 0 || _lastRun.Origin.Position.Y != 0))
            {
                var originTeach = new Point2d(_config.Origin.WorldPosition.X, _config.Origin.WorldPosition.Y);
                if (originTeach.X == 0 && originTeach.Y == 0 && _config.Origin.TemplateRoi.Width > 0)
                {
                    originTeach = new Point2d(_config.Origin.TemplateRoi.X + _config.Origin.TemplateRoi.Width / 2.0, _config.Origin.TemplateRoi.Y + _config.Origin.TemplateRoi.Height / 2.0);
                }
                else if (originTeach.X == 0 && originTeach.Y == 0 && _config.Origin.SearchRoi.Width > 0)
                {
                    originTeach = new Point2d(_config.Origin.SearchRoi.X + _config.Origin.SearchRoi.Width / 2.0, _config.Origin.SearchRoi.Y + _config.Origin.SearchRoi.Height / 2.0);
                }

                var mr = _lastRun.Origin.MatchRect;
                var originFound = (mr.Width > 0 && mr.Height > 0)
                    ? new Point2d(mr.X + mr.Width / 2.0, mr.Y + mr.Height / 2.0)
                    : new Point2d(_lastRun.Origin.Position.X, _lastRun.Origin.Position.Y);

                var oAngle = _lastRun.Origin.AngleDeg;
                var centerFound = TransformPose(centerTeach, originTeach, originFound, oAngle);
                return (centerFound, roi.Angle + oAngle);
            }

            return (centerTeach, roi.Angle);
        }

        private static void AddRadialCaliperStripsOverlay(
            List<OverlayItem> dst,
            Point2d center,
            double nominalR,
            int stripCount,
            double stripLength,
            double stripWidth,
            double minAngleDeg,
            double maxAngleDeg,
            double poseAngleDeg,
            System.Windows.Media.Brush stroke)
        {
            stripCount = Math.Clamp(stripCount > 0 ? stripCount : 32, 4, 360);
            stripLength = Math.Max(5, stripLength > 0 ? stripLength : 40);
            stripWidth = Math.Max(1, stripWidth > 0 ? stripWidth : 10);

            var poseAngleRad = poseAngleDeg * Math.PI / 180.0;
            var startAngleRad = minAngleDeg * Math.PI / 180.0;
            var endAngleRad = maxAngleDeg * Math.PI / 180.0;
            if (Math.Abs(endAngleRad - startAngleRad) < 1e-4)
            {
                endAngleRad = startAngleRad + 2.0 * Math.PI;
            }

            var angleStep = (endAngleRad - startAngleRad) / stripCount;
            var halfL = stripLength / 2.0;
            var halfW = stripWidth / 2.0;

            for (var i = 0; i < stripCount; i++)
            {
                var angle = poseAngleRad + startAngleRad + (i + 0.5) * angleStep;
                var ux = Math.Cos(angle);
                var uy = Math.Sin(angle);
                var vx = -uy;
                var vy = ux;

                var rMin = nominalR - halfL;
                var rMax = nominalR + halfL;

                var p1x = center.X + rMin * ux - halfW * vx;
                var p1y = center.Y + rMin * uy - halfW * vy;

                var p2x = center.X + rMin * ux + halfW * vx;
                var p2y = center.Y + rMin * uy + halfW * vy;

                var p3x = center.X + rMax * ux + halfW * vx;
                var p3y = center.Y + rMax * uy + halfW * vy;

                var p4x = center.X + rMax * ux - halfW * vx;
                var p4y = center.Y + rMax * uy - halfW * vy;

                dst.Add(new OverlayLineItem { X1 = p1x, Y1 = p1y, X2 = p2x, Y2 = p2y, Stroke = stroke, StrokeThickness = 1.0 });
                dst.Add(new OverlayLineItem { X1 = p2x, Y1 = p2y, X2 = p3x, Y2 = p3y, Stroke = stroke, StrokeThickness = 1.0 });
                dst.Add(new OverlayLineItem { X1 = p3x, Y1 = p3y, X2 = p4x, Y2 = p4y, Stroke = stroke, StrokeThickness = 1.0 });
                dst.Add(new OverlayLineItem { X1 = p4x, Y1 = p4y, X2 = p1x, Y2 = p1y, Stroke = stroke, StrokeThickness = 1.0 });
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
            return ConditionEvaluator.EvaluateTextTemplate(text, vars);
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
    
            // 1. Đổi tên trong collection định nghĩa tương ứng
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
            else if (string.Equals(SelectedNode.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "LineToLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.LineToLineDistances.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "PointToLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.PointToLineDistances.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "SegmentLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.SegmentLineDistances?.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
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
                if (_config.Origin != null)
                    _config.Origin.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Blob", StringComparison.OrdinalIgnoreCase))
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
            else if (string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
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
            else if (string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Code", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.PreprocessNodes.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.Calipers.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "TextNode", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Text", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.TextNodes.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "ImageSource", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.ImageSources.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "ImageOutput", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.ImageOutputs?.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "Crop", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.Crops.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "ColorDiff", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.ColorDiffs.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "ImgArithmetic", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.ImgArithmetics.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "CreatePoint", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.CreatePoints.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "CreateLine", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.CreateLines.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "CreateRect", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.CreateRects.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "CreateCircle", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.CreateCircles.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "Condition", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.Conditions.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "PlcRead", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.PlcReads?.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "PlcWrite", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.PlcWrites?.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "PlcWait", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.PlcWaits?.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "PlcTrigger", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.PlcTriggers?.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "PlcBatchRead", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.PlcBatchReads?.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "PlcBatchWrite", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.PlcBatchWrites?.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "ResultTransfer", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.ResultTransfers?.FirstOrDefault(x => string.Equals(x.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.Name = newName;
            }
            else if (string.Equals(SelectedNode.Type, "DbNode", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.DbNodes?.FirstOrDefault(x => string.Equals(x.RefName, oldName, StringComparison.OrdinalIgnoreCase));
                if (def is not null)
                    def.RefName = newName;
            }
    
            // 2. Cập nhật các tham chiếu downstream từ các tool khác trỏ sang newName
            if (_config.Distances != null)
            {
                foreach (var d in _config.Distances)
                {
                    if (string.Equals(d.PointA, oldName, StringComparison.OrdinalIgnoreCase)) d.PointA = newName;
                    if (string.Equals(d.PointB, oldName, StringComparison.OrdinalIgnoreCase)) d.PointB = newName;
                }
            }
            if (_config.LineToLineDistances != null)
            {
                foreach (var d in _config.LineToLineDistances)
                {
                    if (string.Equals(d.LineA, oldName, StringComparison.OrdinalIgnoreCase)) d.LineA = newName;
                    if (string.Equals(d.LineB, oldName, StringComparison.OrdinalIgnoreCase)) d.LineB = newName;
                }
            }
            if (_config.PointToLineDistances != null)
            {
                foreach (var d in _config.PointToLineDistances)
                {
                    if (string.Equals(d.Point, oldName, StringComparison.OrdinalIgnoreCase)) d.Point = newName;
                    if (string.Equals(d.Line, oldName, StringComparison.OrdinalIgnoreCase)) d.Line = newName;
                }
            }
            if (_config.SegmentLineDistances != null)
            {
                foreach (var d in _config.SegmentLineDistances)
                {
                    if (string.Equals(d.LineA, oldName, StringComparison.OrdinalIgnoreCase)) d.LineA = newName;
                    if (string.Equals(d.LineB, oldName, StringComparison.OrdinalIgnoreCase)) d.LineB = newName;
                }
            }
            if (_config.Angles != null)
            {
                foreach (var a in _config.Angles)
                {
                    if (string.Equals(a.LineA, oldName, StringComparison.OrdinalIgnoreCase)) a.LineA = newName;
                    if (string.Equals(a.LineB, oldName, StringComparison.OrdinalIgnoreCase)) a.LineB = newName;
                }
            }
            if (_config.Diameters != null)
            {
                foreach (var d in _config.Diameters)
                {
                    if (string.Equals(d.CircleRef, oldName, StringComparison.OrdinalIgnoreCase)) d.CircleRef = newName;
                }
            }
            if (_config.EdgePairs != null)
            {
                foreach (var ep in _config.EdgePairs)
                {
                    if (string.Equals(ep.RefA, oldName, StringComparison.OrdinalIgnoreCase)) ep.RefA = newName;
                    if (string.Equals(ep.RefB, oldName, StringComparison.OrdinalIgnoreCase)) ep.RefB = newName;
                }
            }
            if (_config.ImageOutputs != null)
            {
                foreach (var io in _config.ImageOutputs)
                {
                    if (string.Equals(io.InputNodeName, oldName, StringComparison.OrdinalIgnoreCase)) io.InputNodeName = newName;
                }
            }
            if (_config.Crops != null)
            {
                foreach (var c in _config.Crops)
                {
                    if (string.Equals(c.ImageSourceRef, oldName, StringComparison.OrdinalIgnoreCase)) c.ImageSourceRef = newName;
                }
            }
            if (_config.ColorDiffs != null)
            {
                foreach (var cd in _config.ColorDiffs)
                {
                    if (string.Equals(cd.ImageSourceRef, oldName, StringComparison.OrdinalIgnoreCase)) cd.ImageSourceRef = newName;
                }
            }
            if (_config.ImgArithmetics != null)
            {
                foreach (var ia in _config.ImgArithmetics)
                {
                    if (string.Equals(ia.ImageSourceRefA, oldName, StringComparison.OrdinalIgnoreCase)) ia.ImageSourceRefA = newName;
                    if (string.Equals(ia.ImageSourceRefB, oldName, StringComparison.OrdinalIgnoreCase)) ia.ImageSourceRefB = newName;
                }
            }
            if (_config.CreatePoints != null)
            {
                foreach (var cp in _config.CreatePoints)
                {
                    if (string.Equals(cp.ImageSourceRef, oldName, StringComparison.OrdinalIgnoreCase)) cp.ImageSourceRef = newName;
                    if (string.Equals(cp.PointRef, oldName, StringComparison.OrdinalIgnoreCase)) cp.PointRef = newName;
                }
            }
            if (_config.CreateLines != null)
            {
                foreach (var cl in _config.CreateLines)
                {
                    if (string.Equals(cl.ImageSourceRef, oldName, StringComparison.OrdinalIgnoreCase)) cl.ImageSourceRef = newName;
                    if (string.Equals(cl.Point1Ref, oldName, StringComparison.OrdinalIgnoreCase)) cl.Point1Ref = newName;
                    if (string.Equals(cl.Point2Ref, oldName, StringComparison.OrdinalIgnoreCase)) cl.Point2Ref = newName;
                    if (string.Equals(cl.PointRef, oldName, StringComparison.OrdinalIgnoreCase)) cl.PointRef = newName;
                }
            }
            if (_config.CreateRects != null)
            {
                foreach (var cr in _config.CreateRects)
                {
                    if (string.Equals(cr.ImageSourceRef, oldName, StringComparison.OrdinalIgnoreCase)) cr.ImageSourceRef = newName;
                    if (string.Equals(cr.PointRef, oldName, StringComparison.OrdinalIgnoreCase)) cr.PointRef = newName;
                }
            }
            if (_config.CreateCircles != null)
            {
                foreach (var cc in _config.CreateCircles)
                {
                    if (string.Equals(cc.ImageSourceRef, oldName, StringComparison.OrdinalIgnoreCase)) cc.ImageSourceRef = newName;
                    if (string.Equals(cc.CenterPointRef, oldName, StringComparison.OrdinalIgnoreCase)) cc.CenterPointRef = newName;
                    if (string.Equals(cc.BoundaryPointRef, oldName, StringComparison.OrdinalIgnoreCase)) cc.BoundaryPointRef = newName;
                }
            }
            if (_config.ContourCompares != null)
            {
                foreach (var cc in _config.ContourCompares)
                {
                    if (string.Equals(cc.PreprocessChoice, oldName, StringComparison.OrdinalIgnoreCase)) cc.PreprocessChoice = newName;
                }
            }
    
            // 3. Cập nhật ActiveRoiLabel nếu cần
            if (!string.IsNullOrWhiteSpace(ActiveRoiLabel) && ActiveRoiLabel.StartsWith(oldName, StringComparison.OrdinalIgnoreCase))
            {
                ActiveRoiLabel = newName + ActiveRoiLabel.Substring(oldName.Length);
            }
    
            // 4. Đồng bộ ToolGraph để ToolGraph.Nodes mang RefName mới (tránh bị RemoveAll xóa mất)
            SyncToolGraphToConfig();
    
            // 5. Cập nhật biến previous
            _selectedNodePrevRefName = newName;
    
            // 6. Yêu cầu tự động lưu và cập nhật UI
            RequestAutoSave();
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
            LastResult = null;
            _lastRunError = null;
            _sharedImage?.SetImage(null); // Clear ảnh preview
            FinalPreviewImage = null;
            SelectedNodePreviewImage = null;
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
                if (string.Equals(node.Type, "Origin", StringComparison.OrdinalIgnoreCase))
                {
                    node.ExecutionTimeMs = _lastRun.Timings.OriginMs;
                }
                else if (string.Equals(node.Type, "ResultView", StringComparison.OrdinalIgnoreCase))
                {
                    node.ExecutionTimeMs = _lastRun.Timings.TotalMs;
                }
                else if (!string.IsNullOrWhiteSpace(node.RefName) && _lastRun.Timings.NodeTimings.TryGetValue(node.RefName, out var ms))
                {
                    node.ExecutionTimeMs = ms;
                }
                else
                {
                    var matchedKv = _lastRun.Timings.NodeTimings.FirstOrDefault(kv =>
                        string.Equals(kv.Key, node.RefName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(kv.Key, node.Type, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(kv.Key, node.Id, StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(matchedKv.Key))
                    {
                        node.ExecutionTimeMs = matchedKv.Value;
                    }
                    else
                    {
                        node.ExecutionTimeMs = 0;
                    }
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
            VisionInspectionApp.Application.Services.ChessboardCalibrationService.EnsureCalibration(_config);
            ProductCode = "NewProduct";
            CurrentJobFilePath = null;
            CurrentTempWorkingDir = null;
            OnPropertyChanged(nameof(PixelsPerMm));

            _config.ToolGraph ??= new ToolGraph();

            var camNode = new ToolGraphNodeViewModel
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = "ImageSource",
                RefName = "CAM1",
                X = 80,
                Y = 120
            };
            camNode.PropertyChanged += Node_PropertyChanged;
            Nodes.Add(camNode);
            EnsureDefinitionForNewNode(camNode);
            camNode.EnsurePortsInitialized();

            var prepNode = new ToolGraphNodeViewModel
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = "Preprocess",
                RefName = "PRE1",
                X = 320,
                Y = 120
            };
            prepNode.PropertyChanged += Node_PropertyChanged;
            Nodes.Add(prepNode);
            EnsureDefinitionForNewNode(prepNode);
            prepNode.EnsurePortsInitialized();

            var originNode = new ToolGraphNodeViewModel
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = "Origin",
                RefName = "Origin",
                X = 560,
                Y = 120
            };
            originNode.PropertyChanged += Node_PropertyChanged;
            Nodes.Add(originNode);
            EnsureDefinitionForNewNode(originNode);
            originNode.EnsurePortsInitialized();

            CreateEdge(camNode, prepNode, "Image", "Image");
            CreateEdge(prepNode, originNode, "Image", "Image");

            SyncToolGraphToConfig();
            SelectedNode = originNode;
            RaiseToolPropertyPanelsChanged();
            RefreshPreviews();
            TriggerAutoFitGraph();
            IsDirty = false;
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
            var effectiveW = imgW > 0 ? imgW : 1280;
            var effectiveH = imgH > 0 ? imgH : 960;
            Roi DefaultRoi()
            {
                var w = Math.Clamp(effectiveW / 4, 100, Math.Max(100, effectiveW));
                var h = Math.Clamp(effectiveH / 4, 100, Math.Max(100, effectiveH));
                var x = Math.Clamp((effectiveW - w) / 2, 0, Math.Max(0, effectiveW - w));
                var y = Math.Clamp((effectiveH - h) / 2, 0, Math.Max(0, effectiveH - h));
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
    
            if (string.Equals(node.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.ContourCompares.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    var def = new ContourCompareDefinition
                    {
                        Name = node.RefName
                    };
                    def.InspectRoi = DefaultRoi();
                    def.TemplateRoi = DefaultRoi();
                    _config.ContourCompares.Add(def);
                    ActiveRoiLabel = $"{node.RefName} CC";
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
    
            if (string.Equals(node.Type, "ImageOutput", StringComparison.OrdinalIgnoreCase) || string.Equals(node.Type, "OutputImage", StringComparison.OrdinalIgnoreCase))
            {
                _config.ImageOutputs ??= new List<ImageOutputDefinition>();
                var existed = _config.ImageOutputs.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    _config.ImageOutputs.Add(new ImageOutputDefinition { Name = node.RefName, SaveFolderPath = @"C:\VisionOutput" });
                }
    
                return;
            }
    
            if (string.Equals(node.Type, "Text", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.TextNodes.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    _config.TextNodes.Add(new TextNodeDefinition { Name = node.RefName, Text = node.RefName, X = effectiveW / 2, Y = effectiveH / 2, DefaultColor = "#FFFFFFFF" });
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
    
            if (string.Equals(node.Type, "SegmentLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.SegmentLineDistances.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    _config.SegmentLineDistances.Add(new SegmentLineDistance { Name = node.RefName });
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
    
            if (string.Equals(node.Type, "Crop", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.Crops.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    _config.Crops.Add(new CropDefinition { Name = node.RefName, CropRoi = DefaultRoi() });
                }
                return;
            }
    
            if (string.Equals(node.Type, "ColorDiff", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.ColorDiffs.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    _config.ColorDiffs.Add(new ColorDiffDefinition { Name = node.RefName, InspectRoi = DefaultRoi(), RefRoi = DefaultRoi() });
                }
                return;
            }
    
            if (string.Equals(node.Type, "ImgArithmetic", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.ImgArithmetics.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    _config.ImgArithmetics.Add(new ImgArithmeticDefinition { Name = node.RefName });
                }
                return;
            }

            if (string.Equals(node.Type, "CreatePoint", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.CreatePoints.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    _config.CreatePoints.Add(new CreatePointDefinition { Name = node.RefName, X = 100, Y = 100 });
                }
                return;
            }

            if (string.Equals(node.Type, "CreateLine", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.CreateLines.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    _config.CreateLines.Add(new CreateLineDefinition { Name = node.RefName, X1 = 50, Y1 = 50, X2 = 250, Y2 = 150 });
                }
                return;
            }

            if (string.Equals(node.Type, "CreateRect", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.CreateRects.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    _config.CreateRects.Add(new CreateRectDefinition { Name = node.RefName, X = 100, Y = 100, Width = 150, Height = 100 });
                }
                return;
            }

            if (string.Equals(node.Type, "CreateCircle", StringComparison.OrdinalIgnoreCase))
            {
                var existed = _config.CreateCircles.Any(x => string.Equals(x.Name, node.RefName, StringComparison.OrdinalIgnoreCase));
                if (!existed)
                {
                    _config.CreateCircles.Add(new CreateCircleDefinition { Name = node.RefName, CenterX = 150, CenterY = 150, Radius = 60 });
                }
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
            else if (string.Equals(type, "SegmentLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                baseName = "SLD";
                exists = n => _config.SegmentLineDistances.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
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
            else if (string.Equals(type, "ContourCompare", StringComparison.OrdinalIgnoreCase))
            {
                baseName = "CC";
                exists = n => _config.ContourCompares.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
            {
                baseName = "CDT";
                exists = n => _config.CodeDetections.Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(type, "CreatePoint", StringComparison.OrdinalIgnoreCase))
            {
                baseName = "CP_P";
                exists = n => (_config.CreatePoints ?? new List<CreatePointDefinition>()).Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(type, "CreateLine", StringComparison.OrdinalIgnoreCase))
            {
                baseName = "CP_L";
                exists = n => (_config.CreateLines ?? new List<CreateLineDefinition>()).Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(type, "CreateRect", StringComparison.OrdinalIgnoreCase))
            {
                baseName = "CP_R";
                exists = n => (_config.CreateRects ?? new List<CreateRectDefinition>()).Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(type, "CreateCircle", StringComparison.OrdinalIgnoreCase))
            {
                baseName = "CP_C";
                exists = n => (_config.CreateCircles ?? new List<CreateCircleDefinition>()).Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(type, "ImageOutput", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "OutputImage", StringComparison.OrdinalIgnoreCase))
            {
                baseName = "IMG_OUT";
                exists = n => (_config.ImageOutputs ?? new List<ImageOutputDefinition>()).Any(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(type, "DbNode", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "DB", StringComparison.OrdinalIgnoreCase))
            {
                baseName = "DB";
                exists = n => (_config.DbNodes ?? new List<DbNodeDefinition>()).Any(x => string.Equals(x.RefName, n, StringComparison.OrdinalIgnoreCase));
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
            if (string.Equals(to.Type, "ImageOutput", StringComparison.OrdinalIgnoreCase) || string.Equals(to.Type, "OutputImage", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.ImageOutputs.FirstOrDefault(x => string.Equals(x.Name, to.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is not null && string.Equals(def.InputNodeName, from.RefName, StringComparison.OrdinalIgnoreCase))
                {
                    def.InputNodeName = string.Empty;
                    OnPropertyChanged(nameof(ImageOutput_InputNodeChoice));
                }
                return;
            }

            if (string.Equals(to.Type, "SegmentLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.SegmentLineDistances?.FirstOrDefault(x => string.Equals(x.Name, to.RefName, StringComparison.OrdinalIgnoreCase));
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

            if (string.Equals(to.Type, "LineLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.LineToLineDistances?.FirstOrDefault(x => string.Equals(x.Name, to.RefName, StringComparison.OrdinalIgnoreCase));
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

            if (string.Equals(to.Type, "Distance", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.Distances?.FirstOrDefault(x => string.Equals(x.Name, to.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is null)
                    return;
                if (string.Equals(edge.ToPort, "P1", StringComparison.OrdinalIgnoreCase) && string.Equals(def.PointA, from.RefName, StringComparison.OrdinalIgnoreCase))
                {
                    def.PointA = string.Empty;
                }
                else if (string.Equals(edge.ToPort, "P2", StringComparison.OrdinalIgnoreCase) && string.Equals(def.PointB, from.RefName, StringComparison.OrdinalIgnoreCase))
                {
                    def.PointB = string.Empty;
                }
                return;
            }

            if (string.Equals(to.Type, "PointLineDistance", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.PointToLineDistances?.FirstOrDefault(x => string.Equals(x.Name, to.RefName, StringComparison.OrdinalIgnoreCase));
                if (def is null)
                    return;
                if (string.Equals(edge.ToPort, "P1", StringComparison.OrdinalIgnoreCase) && string.Equals(def.Point, from.RefName, StringComparison.OrdinalIgnoreCase))
                {
                    def.Point = string.Empty;
                }
                else if (string.Equals(edge.ToPort, "L1", StringComparison.OrdinalIgnoreCase) && string.Equals(def.Line, from.RefName, StringComparison.OrdinalIgnoreCase))
                {
                    def.Line = string.Empty;
                }
                return;
            }

            if (string.Equals(to.Type, "Angle", StringComparison.OrdinalIgnoreCase))
            {
                var def = _config.Angles?.FirstOrDefault(x => string.Equals(x.Name, to.RefName, StringComparison.OrdinalIgnoreCase));
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

    public sealed class ToolboxItemModel
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Icon { get; set; } = "🔧";

        public override string ToString() => Name;
    }
}
