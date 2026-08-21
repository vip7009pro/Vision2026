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
using VisionInspectionApp.UI.Services.Camera;
using VisionInspectionApp.VisionEngine;
namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        private const string DefaultPreprocessChoice = "None (Default)";
        public ObservableCollection<string> AvailablePreprocessChoices { get; }
        public bool IsToolWithPreprocessInput => SelectedNode is not null && (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase));
    
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
    
            // The graph now uses a single Image input.
            if (string.Equals(value, DefaultPreprocessChoice, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value))
            {
                // When selecting "None" (or triggered by UI virtualization pushing null),
                // only remove edges that come from a Preprocess node. Leave ImageSource edges intact.
                for (var i = Edges.Count - 1; i >= 0; i--)
                {
                    var e = Edges[i];
                    if (string.Equals(e.ToNodeId, SelectedNode.Id, StringComparison.OrdinalIgnoreCase) && (string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase) || string.Equals(e.ToPort, "Preprocess", StringComparison.OrdinalIgnoreCase)))
                    {
                        var fromNode = Nodes.FirstOrDefault(n => string.Equals(n.Id, e.FromNodeId, StringComparison.OrdinalIgnoreCase));
                        if (fromNode != null && string.Equals(fromNode.Type, "Preprocess", StringComparison.OrdinalIgnoreCase))
                        {
                            Edges.RemoveAt(i);
                        }
                    }
                }
            }
            else
            {
                // When selecting a specific Preprocess node, remove ALL existing edges to the Image port to replace it.
                for (var i = Edges.Count - 1; i >= 0; i--)
                {
                    var e = Edges[i];
                    if (string.Equals(e.ToNodeId, SelectedNode.Id, StringComparison.OrdinalIgnoreCase) && (string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase) || string.Equals(e.ToPort, "Preprocess", StringComparison.OrdinalIgnoreCase)))
                    {
                        Edges.RemoveAt(i);
                    }
                }

                // Find preprocess node by RefName.
                var from = Nodes.FirstOrDefault(n => string.Equals(n.Type, "Preprocess", StringComparison.OrdinalIgnoreCase) && string.Equals(n.RefName, value, StringComparison.OrdinalIgnoreCase));
                if (from is not null)
                {
                    from.EnsurePortsInitialized();
                    CreateEdge(from, SelectedNode, from.OutPorts.FirstOrDefault()?.Name ?? "Out", "Image");
                }
            }
    
            SyncEdgesToConfig();
            RefreshPreviews();
            RequestAutoSave();
        }
    
        private void SyncPreprocessChoices()
        {
            // IMPORTANT: don't let ComboBox list refresh reset SelectedItem and trigger graph mutations.
            var wasSyncing = _syncingInputs;
            _syncingInputs = true;
            try
            {
                var prev = SelectedToolPreprocessChoice;
                AvailablePreprocessChoices.Clear();
                AvailablePreprocessChoices.Add(DefaultPreprocessChoice);
                if (_config is not null)
                {
                    foreach (var n in (_config.PreprocessNodes ?? new()).Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
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
                _syncingInputs = wasSyncing;
            }
        }
    
        private void SyncSelectedToolPreprocessChoiceFromGraph()
        {
            var wasSyncing = _syncingInputs;
            _syncingInputs = true;
            try
            {
                if (!IsToolWithPreprocessInput || SelectedNode is null)
                {
                    SelectedToolPreprocessChoice = DefaultPreprocessChoice;
                    OnPropertyChanged(nameof(IsToolWithPreprocessInput));
                    return;
                }
    
                var edge = Edges.FirstOrDefault(e => string.Equals(e.ToNodeId, SelectedNode.Id, StringComparison.OrdinalIgnoreCase) && (string.Equals(e.ToPort, "Image", StringComparison.OrdinalIgnoreCase) || string.Equals(e.ToPort, "Preprocess", StringComparison.OrdinalIgnoreCase)));
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
                _syncingInputs = wasSyncing;
            }
        }
    
        /// <summary>
        /// Simple camera item for the ImageSource camera selector ComboBox.
        /// </summary>
        public sealed class ImageSourceCameraItem
        {
            public int Index { get; set; }
            public string DisplayName { get; set; } = string.Empty;
            public override string ToString() => DisplayName;
        }

        public ObservableCollection<ImageSourceCameraItem> AvailableCameraItems { get; } = new();

        /// <summary>
        /// The currently selected camera item in the ComboBox.
        /// Syncs with <see cref="ImageSource_CameraIndex"/>.
        /// </summary>
        public ImageSourceCameraItem? SelectedCameraItem
        {
            get
            {
                var idx = ImageSource_CameraIndex;
                return AvailableCameraItems.FirstOrDefault(c => c.Index == idx);
            }
            set
            {
                if (value is null) return;
                ImageSource_CameraIndex = value.Index;
                OnPropertyChanged();
            }
        }

        private bool _isScanningCameras;
        /// <summary>
        /// Populates AvailableCameraItems asynchronously using CameraDriverFactory.ScanAllDevices (Hikrobot GigE/USB3, Basler, USB Webcam DirectShow, Simulator).
        /// </summary>
        public void RefreshAvailableCameraItems(bool forceRescan = false)
        {
            if (!forceRescan && AvailableCameraItems.Count > 0)
            {
                return;
            }

            if (_isScanningCameras)
            {
                return;
            }

            _isScanningCameras = true;
            Task.Run(() =>
            {
                var items = new List<ImageSourceCameraItem>();
                try
                {
                    var allDevices = CameraDriverFactory.ScanAllDevices();
                    foreach (var dev in allDevices)
                    {
                        items.Add(new ImageSourceCameraItem
                        {
                            Index = dev.Index,
                            DisplayName = dev.DisplayName
                        });
                    }
                }
                catch
                {
                    items.Add(new ImageSourceCameraItem
                    {
                        Index = CameraService.SimulatorCameraIndex,
                        DisplayName = "🎮 Camera Giả Lập (Simulator)"
                    });
                }
                finally
                {
                    _isScanningCameras = false;
                }

                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    AvailableCameraItems.Clear();
                    foreach (var itm in items)
                    {
                        AvailableCameraItems.Add(itm);
                    }
                    OnPropertyChanged(nameof(SelectedCameraItem));
                    OnPropertyChanged(nameof(ImageSource_IsIndustrialCamera));
                    OnPropertyChanged(nameof(ImageSource_IsTimerDriven));
                    OnPropertyChanged(nameof(ImageSource_ContinuousModeDescription));
                }, System.Windows.Threading.DispatcherPriority.Background);
            });
        }

        public bool ImageSource_IsIndustrialCamera
        {
            get
            {
                var def = SelectedImageSourceDef();
                if (def == null) return false;
                if (def.SourceType != ImageSourceType.Camera) return false;
                if (def.TriggerMode == ImageSourceTriggerMode.LineTrigger) return true;
                var item = SelectedCameraItem;
                if (item != null && item.DisplayName.Contains("Hikrobot", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
        }

        public bool ImageSource_IsTimerDriven => !ImageSource_IsIndustrialCamera && ImageSource_TriggerMode != ImageSourceTriggerMode.PlcTrigger;

        public string ImageSource_ContinuousModeDescription
        {
            get
            {
                if (ImageSource_IsIndustrialCamera)
                {
                    return "⚡ Chế độ: Event-Driven (Chờ tín hiệu Hardware Trigger / PLC Line 0 từ Camera Hikrobot GigE). Không dùng Interval.";
                }
                if (ImageSource_IsPlcTrigger)
                {
                    return "⚡ Chế độ: PLC Trigger (Lắng nghe sự kiện đổi trạng thái PLC Tag).";
                }
                return "⏱ Chế độ: Timer-Driven (Chạy tuần tự theo chu kỳ Interval đã định).";
            }
        }

        public ImageSourceType ImageSource_SourceType
        {
            get => SelectedImageSourceDef()?.SourceType ?? ImageSourceType.File;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null)
                    return;
                if (def.SourceType == value)
                    return;
                def.SourceType = value;
                ClearImageSourceCache(def.Name);
                OnPropertyChanged(nameof(ImageSource_IsFile));
                OnPropertyChanged(nameof(ImageSource_IsFolder));
                OnPropertyChanged(nameof(ImageSource_IsCamera));
                OnPropertyChanged(nameof(ImageSource_IsIndustrialCamera));
                OnPropertyChanged(nameof(ImageSource_IsTimerDriven));
                OnPropertyChanged(nameof(ImageSource_ContinuousModeDescription));
                // Refresh camera list when switching to Camera source
                if (value == ImageSourceType.Camera)
                    RefreshAvailableCameraItems();
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public bool ImageSource_IsFile => ImageSource_SourceType == ImageSourceType.File;
        public bool ImageSource_IsFolder => ImageSource_SourceType == ImageSourceType.Folder;
        public bool ImageSource_IsCamera => ImageSource_SourceType == ImageSourceType.Camera;

        public Array AvailableImageSourceTriggerModes => Enum.GetValues(typeof(ImageSourceTriggerMode));

        public ImageSourceTriggerMode ImageSource_TriggerMode
        {
            get => SelectedImageSourceDef()?.TriggerMode ?? ImageSourceTriggerMode.SoftTrigger;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null || def.TriggerMode == value) return;
                def.TriggerMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ImageSource_IsSoftTrigger));
                OnPropertyChanged(nameof(ImageSource_IsLineTrigger));
                OnPropertyChanged(nameof(ImageSource_IsPlcTrigger));
                OnPropertyChanged(nameof(ImageSource_IsIndustrialCamera));
                OnPropertyChanged(nameof(ImageSource_IsTimerDriven));
                OnPropertyChanged(nameof(ImageSource_ContinuousModeDescription));
                RaiseToolPropertyPanelsChanged();
                RequestAutoSave();
            }
        }

        public bool ImageSource_IsSoftTrigger => ImageSource_TriggerMode == ImageSourceTriggerMode.SoftTrigger;
        public bool ImageSource_IsLineTrigger => ImageSource_TriggerMode == ImageSourceTriggerMode.LineTrigger;
        public bool ImageSource_IsPlcTrigger => ImageSource_TriggerMode == ImageSourceTriggerMode.PlcTrigger;

        public string ImageSource_LineTriggerName
        {
            get => SelectedImageSourceDef()?.LineTriggerName ?? "Line1";
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                def.LineTriggerName = value ?? "Line1";
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public string ImageSource_PlcTriggerPlcId
        {
            get => SelectedImageSourceDef()?.PlcTriggerPlcId ?? "PLC1";
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                def.PlcTriggerPlcId = value ?? "PLC1";
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public string ImageSource_PlcTriggerTagName
        {
            get => SelectedImageSourceDef()?.PlcTriggerTagName ?? "X0_Trigger";
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                def.PlcTriggerTagName = value ?? "X0_Trigger";
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public PlcTriggerEdge ImageSource_PlcTriggerEdge
        {
            get => SelectedImageSourceDef()?.PlcTriggerEdge ?? PlcTriggerEdge.RisingEdge;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                def.PlcTriggerEdge = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public bool ImageSource_EnableUndistort
        {
            get => SelectedImageSourceDef()?.EnableUndistort ?? false;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                def.EnableUndistort = value;
                OnPropertyChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public string ImageSource_FilePath
        {
            get => SelectedImageSourceDef()?.FilePath ?? string.Empty;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null)
                    return;
                value ??= string.Empty;
                if (string.Equals(def.FilePath, value, StringComparison.Ordinal))
                    return;
                def.FilePath = value;
                ClearImageSourceCache(def.Name);
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public string ImageSource_FolderPath
        {
            get => SelectedImageSourceDef()?.FolderPath ?? string.Empty;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null)
                    return;
                value ??= string.Empty;
                if (string.Equals(def.FolderPath, value, StringComparison.Ordinal))
                    return;
                def.FolderPath = value;
                ClearImageSourceCache(def.Name);
                _folderImageIndex = 0;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public int ImageSource_CameraIndex
        {
            get => SelectedImageSourceDef()?.CameraIndex ?? 0;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null)
                    return;
                if (def.CameraIndex == value)
                    return;
                def.CameraIndex = value;
                ClearImageSourceCache(def.Name);
                OnPropertyChanged(nameof(SelectedCameraItem));
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public string ImageSource_RtspUrl
        {
            get => SelectedImageSourceDef()?.RtspUrl ?? string.Empty;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null)
                    return;
                value ??= string.Empty;
                if (string.Equals(def.RtspUrl, value, StringComparison.Ordinal))
                    return;
                def.RtspUrl = value;
                ClearImageSourceCache(def.Name);
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public bool ImageSource_LoopFolder
        {
            get => SelectedImageSourceDef()?.LoopFolder ?? true;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null)
                    return;
                if (def.LoopFolder == value)
                    return;
                def.LoopFolder = value;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public int ImageSource_FolderIntervalMs
        {
            get => SelectedImageSourceDef()?.FolderIntervalMs ?? 1000;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null)
                    return;
                if (def.FolderIntervalMs == value)
                    return;
                def.FolderIntervalMs = value;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        private void ImageSource_BrowseFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|All Files|*.*",
                Title = "Select Image File"
            };
            if (dlg.ShowDialog() == true)
            {
                ImageSource_FilePath = dlg.FileName;
            }
        }
    
        private void ImageSource_BrowseFolder()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Image Folder",
                ShowNewFolderButton = false
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ImageSource_FolderPath = dlg.SelectedPath;
            }
        }

        public void ImageSource_OpenJobCameraSettings()
        {
            var def = SelectedImageSourceDef();
            if (def is null) return;

            def.CameraParams ??= new CameraParameters();

            var jobName = !string.IsNullOrWhiteSpace(_config?.ProductName)
                ? $"{_config.ProductName} ({_config.ProductCode})"
                : (!string.IsNullOrWhiteSpace(_config?.ProductCode) ? _config.ProductCode : "Job Hiện Tại");

            var vm = new JobCameraSettingsViewModel(_cameraService, def.CameraParams, jobName, (updatedParams) =>
            {
                def.CameraParams = updatedParams.Clone();
                _ = _cameraService.ApplyParametersAsync(def.CameraParams);
                RequestAutoSave();
            });

            var win = new Views.JobCameraSettingsWindow(vm)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };

            win.ShowDialog();
        }
    
        public ICommand ImageSource_BrowseFileCommand { get; }
        public ICommand ImageSource_BrowseFolderCommand { get; }
        public ICommand ImageSource_OpenJobCameraSettingsCommand { get; }

        public ObservableCollection<PreprocessRoiDefinition> PreprocessRois
        {
            get
            {
                var def = SelectedPreprocessNodeDef();
                if (def is null) return new ObservableCollection<PreprocessRoiDefinition>();
                return new ObservableCollection<PreprocessRoiDefinition>(def.Rois);
            }
        }

        public void Preprocess_AddRoi(PreprocessRoiShape shape, PreprocessRoiMode mode = PreprocessRoiMode.Include)
        {
            var def = SelectedPreprocessNodeDef();
            if (def is null) return;

            var newRoi = new PreprocessRoiDefinition
            {
                Shape = shape,
                Mode = mode,
                X = 50 + def.Rois.Count * 20,
                Y = 50 + def.Rois.Count * 20,
                Width = 200,
                Height = 200,
                CircleCenterX = 150 + def.Rois.Count * 20,
                CircleCenterY = 150 + def.Rois.Count * 20,
                CircleRadius = 60
            };

            if (shape == PreprocessRoiShape.Polygon)
            {
                newRoi.PolygonPoints = new List<Point2dModel>
                {
                    new Point2dModel { X = newRoi.X, Y = newRoi.Y },
                    new Point2dModel { X = newRoi.X + 150, Y = newRoi.Y },
                    new Point2dModel { X = newRoi.X + 200, Y = newRoi.Y + 150 },
                    new Point2dModel { X = newRoi.X + 50, Y = newRoi.Y + 150 }
                };
            }

            def.Rois.Add(newRoi);
            OnPropertyChanged(nameof(PreprocessRois));
            RaiseToolPropertyPanelsChanged();
            SchedulePreprocessPreviewUpdate();
            RequestAutoSave();
        }

        public void Preprocess_RemoveRoi(PreprocessRoiDefinition? roi)
        {
            var def = SelectedPreprocessNodeDef();
            if (def is null || roi is null) return;
            def.Rois.Remove(roi);
            OnPropertyChanged(nameof(PreprocessRois));
            RaiseToolPropertyPanelsChanged();
            SchedulePreprocessPreviewUpdate();
            RequestAutoSave();
        }

        public void Preprocess_ToggleRoiMode(PreprocessRoiDefinition? roi)
        {
            var def = SelectedPreprocessNodeDef();
            if (def is null || roi is null) return;
            roi.Mode = roi.Mode == PreprocessRoiMode.Include ? PreprocessRoiMode.Exclude : PreprocessRoiMode.Include;
            OnPropertyChanged(nameof(PreprocessRois));
            RaiseToolPropertyPanelsChanged();
            SchedulePreprocessPreviewUpdate();
            RequestAutoSave();
        }

        public void Preprocess_AddPolygonPoint(PreprocessRoiDefinition? roi)
        {
            if (roi is null || roi.Shape != PreprocessRoiShape.Polygon) return;
            if (roi.PolygonPoints is null) roi.PolygonPoints = new List<Point2dModel>();

            if (roi.PolygonPoints.Count == 0)
            {
                roi.PolygonPoints.Add(new Point2dModel { X = 100, Y = 100 });
                roi.PolygonPoints.Add(new Point2dModel { X = 250, Y = 100 });
                roi.PolygonPoints.Add(new Point2dModel { X = 175, Y = 250 });
            }
            else
            {
                var last = roi.PolygonPoints.Last();
                var first = roi.PolygonPoints.First();
                roi.PolygonPoints.Add(new Point2dModel
                {
                    X = Math.Round((last.X + first.X) / 2.0 + 30),
                    Y = Math.Round((last.Y + first.Y) / 2.0 + 30)
                });
            }

            OnPropertyChanged(nameof(PreprocessRois));
            RaiseToolPropertyPanelsChanged();
            SchedulePreprocessPreviewUpdate();
            RequestAutoSave();
        }

        public void Preprocess_RemovePolygonPoint(Point2dModel? point)
        {
            var def = SelectedPreprocessNodeDef();
            if (def is null || point is null) return;
            foreach (var roi in def.Rois)
            {
                if (roi.Shape == PreprocessRoiShape.Polygon && roi.PolygonPoints != null && roi.PolygonPoints.Contains(point))
                {
                    if (roi.PolygonPoints.Count > 3)
                    {
                        roi.PolygonPoints.Remove(point);
                        OnPropertyChanged(nameof(PreprocessRois));
                        RaiseToolPropertyPanelsChanged();
                        SchedulePreprocessPreviewUpdate();
                        RequestAutoSave();
                    }
                    break;
                }
            }
        }

        public ICommand Preprocess_AddRectangleRoiCommand { get; set; }
        public ICommand Preprocess_AddCircleRoiCommand { get; set; }
        public ICommand Preprocess_AddPolygonRoiCommand { get; set; }
        public ICommand Preprocess_RemoveRoiCommand { get; set; }
        public ICommand Preprocess_ToggleRoiModeCommand { get; set; }
        public ICommand Preprocess_AddPolygonPointCommand { get; set; }
        public ICommand Preprocess_RemovePolygonPointCommand { get; set; }
    }
}
