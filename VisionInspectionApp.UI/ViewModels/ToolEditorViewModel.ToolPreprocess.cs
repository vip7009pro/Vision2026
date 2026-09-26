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
        public bool IsToolWithPreprocessInput => SelectedNode is not null && (string.Equals(SelectedNode.Type, "Origin", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase) || string.Equals(SelectedNode.Type, "OCR", StringComparison.OrdinalIgnoreCase));
    
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
        /// ViewModel cho từng kênh đèn chiếu sáng của Job trong Tool Editor.
        /// </summary>
        public sealed partial class JobLightingChannelItemViewModel : ObservableObject
        {
            private readonly ToolEditorViewModel _parent;
            private readonly JobLightingChannelParams _model;

            public int ChannelIndex => _model.ChannelIndex;
            public int ChannelNumber => ChannelIndex + 1;
            public string ChannelLabel => $"CH{ChannelNumber}";

            public bool IsEnabled
            {
                get => _model.IsEnabled;
                set
                {
                    if (_model.IsEnabled == value) return;
                    _model.IsEnabled = value;
                    OnPropertyChanged();
                    _parent.RequestAutoSave();
                }
            }

            public int Brightness
            {
                get => _model.Brightness;
                set
                {
                    int clamped = Math.Clamp(value, 0, 255);
                    if (_model.Brightness == clamped) return;
                    _model.Brightness = clamped;
                    OnPropertyChanged();
                    _parent.RequestAutoSave();
                }
            }

            public int LightingTimeMs
            {
                get => _model.LightingTimeMs;
                set
                {
                    int clamped = Math.Clamp(value, 1, 999);
                    if (_model.LightingTimeMs == clamped) return;
                    _model.LightingTimeMs = clamped;
                    OnPropertyChanged();
                    _parent.RequestAutoSave();
                }
            }

            public JobLightingChannelItemViewModel(JobLightingChannelParams model, ToolEditorViewModel parent)
            {
                _model = model;
                _parent = parent;
            }

            internal void SyncFromParams(JobLightingChannelParams param)
            {
                _model.IsEnabled = param.IsEnabled;
                _model.Brightness = param.Brightness;
                _model.LightingTimeMs = param.LightingTimeMs;
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(Brightness));
                OnPropertyChanged(nameof(LightingTimeMs));
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
                var def = SelectedImageSourceDef();
                if (def == null) return null;
                var idx = def.CameraIndex;
                var found = AvailableCameraItems.FirstOrDefault(c => c.Index == idx);
                if (found != null) return found;

                if (!string.IsNullOrWhiteSpace(def.CameraDeviceDisplayName))
                {
                    var foundByName = AvailableCameraItems.FirstOrDefault(c => c.DisplayName.Contains(def.CameraDeviceDisplayName, StringComparison.OrdinalIgnoreCase));
                    if (foundByName != null) return foundByName;
                }
                return AvailableCameraItems.FirstOrDefault();
            }
            set
            {
                if (value is null) return;
                var def = SelectedImageSourceDef();
                if (def != null)
                {
                    def.CameraIndex = value.Index;
                    if (!string.IsNullOrWhiteSpace(value.DisplayName))
                    {
                        string cleanName = value.DisplayName.Replace("📷 [OQC Gốc] ", "").Trim();
                        def.CameraDeviceDisplayName = cleanName;
                    }
                }
                ImageSource_CameraIndex = value.Index;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ImageSource_IsIndustrialCamera));
                OnPropertyChanged(nameof(ImageSource_IsTimerDriven));
                OnPropertyChanged(nameof(ImageSource_ContinuousModeDescription));
                RequestAutoSave();
            }
        }

        private bool _isScanningCameras;
        /// <summary>
        /// Populates AvailableCameraItems asynchronously using CameraDriverFactory.ScanAllDevices (Hikrobot GigE/USB3, Basler, USB Webcam DirectShow, Simulator).
        /// Tự động bảo lưu và hiển thị Camera OQC gốc nếu Job trước đó đã lưu thông số Camera OQC.
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

                // Bảo lưu thông số Camera OQC gốc: Nếu Job có thông tin Camera OQC mà máy tính hiện tại không cắm, chèn mục Camera OQC gốc
                var def = SelectedImageSourceDef();
                string savedCamName = def?.CameraDeviceDisplayName ?? "";
                int savedCamIndex = def?.CameraIndex ?? 0;

                bool hasSavedCamInHardware = !string.IsNullOrWhiteSpace(savedCamName) && 
                    items.Any(i => string.Equals(i.DisplayName, savedCamName, StringComparison.OrdinalIgnoreCase) ||
                                   i.DisplayName.Contains(savedCamName, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(savedCamName) && !hasSavedCamInHardware)
                {
                    items.Insert(0, new ImageSourceCameraItem
                    {
                        Index = savedCamIndex,
                        DisplayName = $"📷 [OQC Gốc] {savedCamName}"
                    });
                }
                else if (def != null && def.CameraParams != null && items.Count == 0)
                {
                    items.Insert(0, new ImageSourceCameraItem
                    {
                        Index = savedCamIndex,
                        DisplayName = "📷 [OQC Gốc] Hikrobot Camera (Chuyền OQC)"
                    });
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

        public bool ImageSource_IsIntervalVisible =>
            ImageSource_IsFolder ||
            ImageSource_IsFile ||
            ImageSource_TriggerMode == ImageSourceTriggerMode.SoftTrigger;

        public string ImageSource_ContinuousModeDescription
        {
            get
            {
                if (ImageSource_IsLineTrigger)
                {
                    return $"⚡ Chế độ: Hardware Trigger (Chờ xung kích phát từ {ImageSource_LineTriggerName} / Sensor Line 0). Không dùng Interval.";
                }
                if (ImageSource_IsPlcTrigger)
                {
                    return "⚡ Chế độ: PLC Trigger (Lắng nghe sự kiện đổi trạng thái PLC Tag).";
                }
                int interval = ImageSource_FolderIntervalMs;
                if (interval <= 0)
                {
                    return "⏱ Chế độ: Software Trigger - Chạy liên tục tốc độ tối đa (FreeRun 0ms delay).";
                }
                double fps = interval > 0 ? (1000.0 / interval) : 0;
                double eaH = fps * 3600.0;
                double eaD = eaH * 24.0;
                return $"⏱ Chế độ: Software Trigger - Chu kỳ lấy ảnh: {interval} ms (tốc độ ~{fps:F1} pcs/s • {eaH:N0} EA/h • {eaD:N0} EA/day).";
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
                if (value == ImageSourceType.Url)
                {
                    // Khi chuyển sang URL, nếu đã có ImageUrl thì ưu tiên nạp từ cache
                    if (!string.IsNullOrWhiteSpace(def.ImageUrl))
                    {
                        var cached = GetImageSourceCache(def.Name);
                        if (cached == null || cached.Empty())
                        {
                            var diskMat = TryLoadUrlImageFromDiskCache(def.ImageUrl);
                            if (diskMat != null && !diskMat.Empty())
                            {
                                SetImageSourceCache(def.Name, def.ImageUrl, diskMat);
                                _sharedImage.SetImage(diskMat);
                            }
                            else
                            {
                                ScheduleAsyncUrlImageFetch(def.Name, def.ImageUrl);
                            }
                        }
                        else
                        {
                            _sharedImage.SetImage(cached);
                        }
                    }
                }
                else if (value == ImageSourceType.Pdf)
                {
                    if (!string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
                    {
                        int pages = _pdfDocumentService.GetPageCount(def.PdfPath);
                        ImageSource_PdfTotalPages = Math.Max(1, pages);
                    }
                    UpdateSharedImageForImageSource(def);
                }
                else
                {
                    ClearImageSourceCache(def.Name);
                }
                OnPropertyChanged(nameof(ImageSource_IsFile));
                OnPropertyChanged(nameof(ImageSource_IsFolder));
                OnPropertyChanged(nameof(ImageSource_IsCamera));
                OnPropertyChanged(nameof(ImageSource_IsUrl));
                OnPropertyChanged(nameof(ImageSource_IsPdf));
                OnPropertyChanged(nameof(ImageSource_IsPdfPanActive));
                OnPropertyChanged(nameof(ImageSource_PdfIsPanDragEnabled));
                OnPropertyChanged(nameof(ImageSource_IsIndustrialCamera));
                OnPropertyChanged(nameof(ImageSource_IsTimerDriven));
                OnPropertyChanged(nameof(ImageSource_IsIntervalVisible));
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
        public bool ImageSource_IsUrl => ImageSource_SourceType == ImageSourceType.Url;
        public bool ImageSource_IsPdf => ImageSource_SourceType == ImageSourceType.Pdf;

        private bool _imageSource_PdfIsPanDragEnabled = true;
        public bool ImageSource_PdfIsPanDragEnabled
        {
            get => _imageSource_PdfIsPanDragEnabled;
            set
            {
                if (_imageSource_PdfIsPanDragEnabled == value) return;
                _imageSource_PdfIsPanDragEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ImageSource_IsPdfPanActive));
            }
        }

        public bool ImageSource_IsPdfPanActive => ImageSource_IsPdf && ImageSource_PdfIsPanDragEnabled;

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
                OnPropertyChanged(nameof(ImageSource_IsIntervalVisible));
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
            get => SelectedImageSourceDef()?.EnableUndistort ?? true;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                def.EnableUndistort = value;
                OnPropertyChanged();
                UpdateSharedImageForImageSource(def);
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
                UpdateSharedImageForImageSource(def);
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
                UpdateSharedImageForImageSource(def);
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

        public string ImageSource_ImageUrl
        {
            get => SelectedImageSourceDef()?.ImageUrl ?? string.Empty;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null)
                    return;
                value ??= string.Empty;
                if (string.Equals(def.ImageUrl, value, StringComparison.Ordinal))
                    return;
                def.ImageUrl = value;
                ClearImageSourceCache(def.Name);
                if (def.SourceType == ImageSourceType.Url && !string.IsNullOrWhiteSpace(value))
                {
                    var diskMat = TryLoadUrlImageFromDiskCache(value);
                    if (diskMat != null && !diskMat.Empty())
                    {
                        SetImageSourceCache(def.Name, value, diskMat);
                        _sharedImage.SetImage(diskMat);
                    }
                    else
                    {
                        ScheduleAsyncUrlImageFetch(def.Name, value);
                    }
                }
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public async Task ImageSource_FetchUrlImageAsync()
        {
            string url = ImageSource_ImageUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                System.Windows.MessageBox.Show("Vui lòng nhập đường dẫn URL ảnh từ máy chủ!", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            await FetchAndApplyImageUrlAsync(url);
        }

        public async Task FetchAndApplyImageUrlAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                StatusBarText = $"⏳ Đang tải ảnh từ URL: {url}...";
                var (success, data, error) = await _remoteServerService.DownloadFileAsync(url);
                if (!success || data == null || data.Length == 0)
                {
                    StatusBarText = $"❌ Lỗi tải ảnh từ URL: {error}";
                    System.Windows.MessageBox.Show($"Không thể tải ảnh từ URL:\n{url}\nLỗi: {error}", "Lỗi Tải Ảnh", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                var mat = Cv2.ImDecode(data, ImreadModes.Color);
                if (mat == null || mat.Empty())
                {
                    StatusBarText = "❌ Giải mã dữ liệu ảnh thất bại.";
                    System.Windows.MessageBox.Show("Dữ liệu tải về không phải là tệp ảnh hợp lệ!", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }

                // Lưu dữ liệu ảnh vào Disk Cache và thư mục tạm Job để các lần mở Job sau nạp ngay tức khắc mà không cần mạng
                SaveUrlImageToDiskCache(url, data);

                var def = SelectedImageSourceDef() ?? _config?.ImageSources?.FirstOrDefault();
                if (def != null)
                {
                    def.SourceType = ImageSourceType.Url;
                    def.ImageUrl = url;
                    string nodeName = def.Name ?? "ImageSource1";
                    SetImageSourceCache(nodeName, url, mat);
                }
                else
                {
                    SetImageSourceCache("ImageSource1", url, mat);
                }

                _sharedImage.SetImage(mat);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(ImageSource_SourceType));
                    OnPropertyChanged(nameof(ImageSource_IsFile));
                    OnPropertyChanged(nameof(ImageSource_IsFolder));
                    OnPropertyChanged(nameof(ImageSource_IsCamera));
                    OnPropertyChanged(nameof(ImageSource_IsUrl));
                    OnPropertyChanged(nameof(ImageSource_ImageUrl));
                    RaiseToolPropertyPanelsChanged();
                    RefreshPreviews();
                    RequestAutoSave();
                    StatusBarText = $"✅ Đã tải và nạp ảnh ({mat.Width}x{mat.Height}) từ Server URL!";
                });
            }
            catch (Exception ex)
            {
                StatusBarText = $"❌ Ngoại lệ tải ảnh: {ex.Message}";
                System.Windows.MessageBox.Show($"Ngoại lệ khi tải ảnh từ URL: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
                int clamped = Math.Max(0, value);
                if (def.FolderIntervalMs == clamped)
                    return;
                def.FolderIntervalMs = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ImageSource_ContinuousModeDescription));
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

        #region PDF Image Source Properties & Methods

        public sealed class PdfScaleOption
        {
            public double Scale { get; set; }
            public string DisplayText { get; set; } = string.Empty;
            public override string ToString() => DisplayText;
        }

        public ObservableCollection<PdfScaleOption> AvailablePdfScales { get; } = new()
        {
            new() { Scale = 300.0 / 72.0, DisplayText = "🌟 300 DPI (Chuẩn nét công nghiệp - Khuyên dùng)" },
            new() { Scale = 200.0 / 72.0, DisplayText = "200 DPI (Nét cao 2.78x)" },
            new() { Scale = 150.0 / 72.0, DisplayText = "150 DPI (Độ nét trung bình 2.08x)" },
            new() { Scale = 400.0 / 72.0, DisplayText = "400 DPI (Siêu nét 5.56x)" },
            new() { Scale = 600.0 / 72.0, DisplayText = "600 DPI (Cực nét Ultra-HD 8.33x)" },
            new() { Scale = 1.0, DisplayText = "72 DPI (Tỉ lệ 1:1 điểm point gốc - Thô 1.0x)" }
        };

        public sealed class CameraPresetOption
        {
            public string Name { get; set; } = string.Empty;
            public int Width { get; set; }
            public int Height { get; set; }
            public string DisplayText => Width > 0 && Height > 0 ? $"{Name} ({Width} × {Height})" : Name;
            public override string ToString() => DisplayText;
        }

        public ObservableCollection<CameraPresetOption> AvailableCameraPresets { get; } = new()
        {
            new() { Name = "🎯 Camera 20MP (Mặc định)", Width = 5472, Height = 3648 },
            new() { Name = "Camera 12MP", Width = 4096, Height = 3000 },
            new() { Name = "Camera 6MP", Width = 3072, Height = 2048 },
            new() { Name = "Camera 5MP", Width = 2448, Height = 2048 },
            new() { Name = "Full HD (2MP)", Width = 1920, Height = 1080 },
            new() { Name = "⚙️ Tùy chỉnh (Custom)", Width = 0, Height = 0 }
        };

        private CameraPresetOption? _selectedCameraPreset;
        public CameraPresetOption? SelectedCameraPreset
        {
            get
            {
                if (_selectedCameraPreset == null)
                {
                    int w = ImageSource_PdfCameraWidth;
                    int h = ImageSource_PdfCameraHeight;
                    _selectedCameraPreset = AvailableCameraPresets.FirstOrDefault(p => p.Width == w && p.Height == h) 
                                         ?? AvailableCameraPresets.LastOrDefault();
                }
                return _selectedCameraPreset;
            }
            set
            {
                if (value != null)
                {
                    _selectedCameraPreset = value;
                    if (value.Width > 0 && value.Height > 0)
                    {
                        ImageSource_PdfCameraWidth = value.Width;
                        ImageSource_PdfCameraHeight = value.Height;
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ImageSource_PdfOpticalInfoText));
                }
            }
        }

        public PdfRenderMode ImageSource_PdfRenderMode
        {
            get => SelectedImageSourceDef()?.PdfRenderMode ?? PdfRenderMode.MatchCamera1to1;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null || def.PdfRenderMode == value) return;
                def.PdfRenderMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ImageSource_PdfIsMatchCamera));
                OnPropertyChanged(nameof(ImageSource_PdfIsFixedDpi));
                RaiseToolPropertyPanelsChanged();
                RequestAutoSave();
            }
        }

        public bool ImageSource_PdfIsMatchCamera
        {
            get => ImageSource_PdfRenderMode == PdfRenderMode.MatchCamera1to1;
            set
            {
                if (value)
                    ImageSource_PdfRenderMode = PdfRenderMode.MatchCamera1to1;
            }
        }

        public bool ImageSource_PdfIsFixedDpi
        {
            get => ImageSource_PdfRenderMode == PdfRenderMode.FixedDpi;
            set
            {
                if (value)
                    ImageSource_PdfRenderMode = PdfRenderMode.FixedDpi;
            }
        }

        public bool ImageSource_PdfFitToCameraCanvas
        {
            get => SelectedImageSourceDef()?.PdfFitToCameraCanvas ?? true;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null || def.PdfFitToCameraCanvas == value) return;
                def.PdfFitToCameraCanvas = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public int ImageSource_PdfCameraWidth
        {
            get
            {
                var def = SelectedImageSourceDef();
                return def?.PdfCameraWidth is > 0 ? def.PdfCameraWidth : 5472;
            }
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                int clamped = Math.Max(100, value);
                if (def.PdfCameraWidth == clamped) return;
                def.PdfCameraWidth = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ImageSource_PdfOpticalInfoText));
                RequestAutoSave();
            }
        }

        public int ImageSource_PdfCameraHeight
        {
            get
            {
                var def = SelectedImageSourceDef();
                return def?.PdfCameraHeight is > 0 ? def.PdfCameraHeight : 3648;
            }
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                int clamped = Math.Max(100, value);
                if (def.PdfCameraHeight == clamped) return;
                def.PdfCameraHeight = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ImageSource_PdfOpticalInfoText));
                RequestAutoSave();
            }
        }

        public sealed class PdfCanvasAlignmentOption
        {
            public string Key { get; set; } = "Center";
            public string DisplayText { get; set; } = string.Empty;
            public override string ToString() => DisplayText;
        }

        public ObservableCollection<PdfCanvasAlignmentOption> AvailablePdfCanvasAlignments { get; } = new()
        {
            new() { Key = "Center", DisplayText = "🎯 Tâm Giữa (Center)" },
            new() { Key = "TopCenter", DisplayText = "⬆️ Đỉnh - Giữa (Top-Center)" },
            new() { Key = "BottomCenter", DisplayText = "⬇️ Đáy - Giữa (Bottom-Center)" },
            new() { Key = "TopLeft", DisplayText = "↖️ Góc Trên - Trái (Top-Left)" },
            new() { Key = "TopRight", DisplayText = "↗️ Góc Trên - Phải (Top-Right)" },
            new() { Key = "Custom", DisplayText = "⚙️ Tùy Chỉnh (Pan Tự Do)" }
        };

        public PdfCanvasAlignmentOption? SelectedPdfCanvasAlignment
        {
            get
            {
                string cur = ImageSource_PdfCanvasAlignment;
                return AvailablePdfCanvasAlignments.FirstOrDefault(a => string.Equals(a.Key, cur, StringComparison.OrdinalIgnoreCase))
                    ?? AvailablePdfCanvasAlignments.FirstOrDefault();
            }
            set
            {
                if (value != null && !string.Equals(ImageSource_PdfCanvasAlignment, value.Key, StringComparison.OrdinalIgnoreCase))
                {
                    ImageSource_PdfCanvasAlignment = value.Key;
                    OnPropertyChanged();
                    var def = SelectedImageSourceDef();
                    if (def != null && !string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
                    {
                        ImageSource_ConvertPdfToImage();
                    }
                }
            }
        }

        public string ImageSource_PdfCanvasAlignment
        {
            get => SelectedImageSourceDef()?.PdfCanvasAlignment ?? "Center";
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null || def.PdfCanvasAlignment == value) return;
                def.PdfCanvasAlignment = value ?? "Center";
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedPdfCanvasAlignment));
                RequestAutoSave();
            }
        }

        public int ImageSource_PdfCanvasOffsetX
        {
            get => SelectedImageSourceDef()?.PdfCanvasOffsetX ?? 0;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null || def.PdfCanvasOffsetX == value) return;
                def.PdfCanvasOffsetX = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public int ImageSource_PdfCanvasOffsetY
        {
            get => SelectedImageSourceDef()?.PdfCanvasOffsetY ?? 0;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null || def.PdfCanvasOffsetY == value) return;
                def.PdfCanvasOffsetY = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public double ImageSource_PdfPixelsPerMm
        {
            get
            {
                var def = SelectedImageSourceDef();
                if (def != null && def.PdfPixelsPerMm > 0.0001)
                    return def.PdfPixelsPerMm;
                if (_config?.PixelsPerMm is > 0.0001)
                    return _config.PixelsPerMm;
                return 34.2; // Mặc định Camera 20MP
            }
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                double clamped = Math.Max(0.001, Math.Round(value, 4));
                if (Math.Abs(def.PdfPixelsPerMm - clamped) < 0.0001) return;
                def.PdfPixelsPerMm = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ImageSource_PdfOpticalInfoText));
                OnPropertyChanged(nameof(ImageSource_PdfEquivalentDpiText));
                RequestAutoSave();
            }
        }

        public string ImageSource_PdfEquivalentDpiText => $"~ {(int)Math.Round(ImageSource_PdfPixelsPerMm * 25.4)} DPI";

        public int ImageSource_PdfRotation
        {
            get => SelectedImageSourceDef()?.PdfRotation ?? 0;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                int norm = (value % 360 + 360) % 360;
                if (def.PdfRotation == norm) return;
                def.PdfRotation = norm;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ImageSource_PdfRotationText));
                RequestAutoSave();
            }
        }

        public string ImageSource_PdfRotationText => $"{ImageSource_PdfRotation}°";

        public void ImageSource_PdfSyncPixelsPerMmFromCalib()
        {
            double calibPpm = 0.0;
            if (_config?.PixelsPerMm is > 0.0001)
                calibPpm = _config.PixelsPerMm;
            else if (_config?.ChessboardCalibration != null && _config.ChessboardCalibration.PixelsPerMm > 0.0001)
                calibPpm = _config.ChessboardCalibration.PixelsPerMm;

            if (calibPpm > 0.0001)
            {
                ImageSource_PdfPixelsPerMm = calibPpm;
                StatusBarText = $"✅ Đã đồng bộ tỉ lệ quang học {calibPpm:F2} px/mm từ Calib vào nguồn PDF.";
            }
            else
            {
                ImageSource_PdfPixelsPerMm = 34.2;
                StatusBarText = "ℹ️ Job chưa có dữ liệu Calib, đã đặt tỉ lệ mặc định 34.20 px/mm (Camera 20MP).";
            }
            var def = SelectedImageSourceDef();
            if (def != null && !string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
            {
                ImageSource_ConvertPdfToImage();
            }
        }

        public void ImageSource_PdfApplyPixelsPerMmToJob()
        {
            if (_config != null)
            {
                _config.PixelsPerMm = ImageSource_PdfPixelsPerMm;
                RequestAutoSave();
                OnPropertyChanged(nameof(ImageSource_PdfOpticalInfoText));
                StatusBarText = $"✅ Đã áp dụng hệ số {ImageSource_PdfPixelsPerMm:F2} px/mm vào thông số đo lường chung của Job!";
            }
        }

        public void ImageSource_PdfRotate90()
        {
            ImageSource_PdfRotation = (ImageSource_PdfRotation + 90) % 360;
            StatusBarText = $"🔄 Đã xoay bản vẽ PDF sang {ImageSource_PdfRotation}°";
            var def = SelectedImageSourceDef();
            if (def != null && !string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
            {
                ImageSource_ConvertPdfToImage();
            }
        }

        public void ImageSource_PdfPanUp()
        {
            ImageSource_PdfCanvasOffsetY -= 200;
            var def = SelectedImageSourceDef();
            if (def != null && !string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
            {
                ImageSource_ConvertPdfToImage();
            }
        }

        public void ImageSource_PdfPanDown()
        {
            ImageSource_PdfCanvasOffsetY += 200;
            var def = SelectedImageSourceDef();
            if (def != null && !string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
            {
                ImageSource_ConvertPdfToImage();
            }
        }

        public void ImageSource_PdfPanLeft()
        {
            ImageSource_PdfCanvasOffsetX -= 200;
            var def = SelectedImageSourceDef();
            if (def != null && !string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
            {
                ImageSource_ConvertPdfToImage();
            }
        }

        public void ImageSource_PdfPanRight()
        {
            ImageSource_PdfCanvasOffsetX += 200;
            var def = SelectedImageSourceDef();
            if (def != null && !string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
            {
                ImageSource_ConvertPdfToImage();
            }
        }

        public void ImageSource_PdfPanReset()
        {
            ImageSource_PdfCanvasOffsetX = 0;
            ImageSource_PdfCanvasOffsetY = 0;
            var def = SelectedImageSourceDef();
            if (def != null && !string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
            {
                ImageSource_ConvertPdfToImage();
            }
        }

        private bool _isPdfDraggingVm;
        private int _pdfDragStartOffsetX;
        private int _pdfDragStartOffsetY;
        private OpenCvSharp.Mat? _cachedPdfRotatedPageMat;

        /// <summary>
        /// Xử lý thao tác kéo chuột trực tiếp trên Canvas xem trước để Pan vùng hiển thị PDF.
        /// </summary>
        public void OnImageSourcePdfPanDrag(Controls.PdfPanDragInfo? info)
        {
            if (info == null) return;
            var def = SelectedImageSourceDef();
            if (def == null || string.IsNullOrWhiteSpace(def.PdfPath) || !File.Exists(def.PdfPath)) return;

            if (info.IsCancelled)
            {
                if (_isPdfDraggingVm)
                {
                    _isPdfDraggingVm = false;
                    _cachedPdfRotatedPageMat?.Dispose();
                    _cachedPdfRotatedPageMat = null;
                    ImageSource_PdfCanvasOffsetX = _pdfDragStartOffsetX;
                    ImageSource_PdfCanvasOffsetY = _pdfDragStartOffsetY;
                    ImageSource_ConvertPdfToImage();
                    StatusBarText = "❌ Đã hủy thao tác kéo chuột Pan bản vẽ PDF.";
                }
                return;
            }

            if (!_isPdfDraggingVm)
            {
                _isPdfDraggingVm = true;
                _pdfDragStartOffsetX = ImageSource_PdfCanvasOffsetX;
                _pdfDragStartOffsetY = ImageSource_PdfCanvasOffsetY;
                try
                {
                    _cachedPdfRotatedPageMat?.Dispose();
                    _cachedPdfRotatedPageMat = _pdfDocumentService.RenderRotatedPage(
                        def.PdfPath,
                        Math.Max(1, def.PdfPageNumber),
                        ImageSource_PdfPixelsPerMm,
                        def.PdfRotation);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PDF Pan Drag] Lỗi chuẩn bị trang xoay: {ex.Message}");
                    _cachedPdfRotatedPageMat = null;
                }
            }

            int newX = _pdfDragStartOffsetX + info.DeltaX;
            int newY = _pdfDragStartOffsetY + info.DeltaY;

            ImageSource_PdfCanvasOffsetX = newX;
            ImageSource_PdfCanvasOffsetY = newY;

            if (info.IsCompleted)
            {
                _isPdfDraggingVm = false;
                _cachedPdfRotatedPageMat?.Dispose();
                _cachedPdfRotatedPageMat = null;

                ImageSource_ConvertPdfToImage();
                StatusBarText = $"✅ Đã Pan bản vẽ PDF thành công: X = {newX:+0;-0;0}, Y = {newY:+0;-0;0} (ΔX: {info.DeltaX:+0;-0;0}, ΔY: {info.DeltaY:+0;-0;0})";
                return;
            }

            // Live in-memory preview during drag (siêu mượt 60 FPS, không ghi đĩa)
            if (_cachedPdfRotatedPageMat != null && !_cachedPdfRotatedPageMat.IsDisposed && !_cachedPdfRotatedPageMat.Empty())
            {
                try
                {
                    int camW = def.PdfCameraWidth > 0 ? def.PdfCameraWidth : 5472;
                    int camH = def.PdfCameraHeight > 0 ? def.PdfCameraHeight : 3648;
                    string align = string.IsNullOrWhiteSpace(def.PdfCanvasAlignment) ? "Center" : def.PdfCanvasAlignment;

                    using var liveCanvas = _pdfDocumentService.PlacePageOnCameraCanvas(
                        _cachedPdfRotatedPageMat,
                        camW,
                        camH,
                        align,
                        newX,
                        newY);

                    SelectedNodePreviewImage = liveCanvas.ToBitmapSourceForDisplay();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PDF Pan Drag] Lỗi live canvas: {ex.Message}");
                }
            }

            StatusBarText = $"🖐️ Đang kéo Pan bản vẽ PDF: X = {newX:+0;-0;0}, Y = {newY:+0;-0;0} (ΔX: {info.DeltaX:+0;-0;0}, ΔY: {info.DeltaY:+0;-0;0}) — Thả chuột để áp dụng, nhấn Esc để hủy";
        }

        public string ImageSource_PdfOpticalInfoText
        {
            get
            {
                double ppm = ImageSource_PdfPixelsPerMm;
                int camW = ImageSource_PdfCameraWidth;
                int camH = ImageSource_PdfCameraHeight;
                double fovW = camW / ppm;
                double fovH = camH / ppm;
                double dpi = ppm * 25.4;
                return $"📐 Tỉ Lệ: {ppm:F2} px/mm (~{dpi:F0} DPI) • Vùng nhìn FOV: {fovW:F1} × {fovH:F1} mm";
            }
        }

        public void ImageSource_PdfSyncFromCamera()
        {
            try
            {
                int detectedW = 0;
                int detectedH = 0;

                // 1. Thử lấy từ ảnh sharedImage hiện tại nếu có
                using var snap = _sharedImage?.GetSnapshot();
                if (snap != null && !snap.Empty())
                {
                    detectedW = snap.Width;
                    detectedH = snap.Height;
                }
                // 2. Thử lấy từ ChessboardCalibration nếu có
                else if (_config?.ChessboardCalibration != null && _config.ChessboardCalibration.ImageWidth > 0 && _config.ChessboardCalibration.ImageHeight > 0)
                {
                    detectedW = _config.ChessboardCalibration.ImageWidth;
                    detectedH = _config.ChessboardCalibration.ImageHeight;
                }

                if (detectedW > 0 && detectedH > 0)
                {
                    ImageSource_PdfCameraWidth = detectedW;
                    ImageSource_PdfCameraHeight = detectedH;
                    _selectedCameraPreset = AvailableCameraPresets.FirstOrDefault(p => p.Width == detectedW && p.Height == detectedH)
                                         ?? AvailableCameraPresets.LastOrDefault();
                    OnPropertyChanged(nameof(SelectedCameraPreset));
                    OnPropertyChanged(nameof(ImageSource_PdfOpticalInfoText));
                    StatusBarText = $"✅ Đã tự động nhận diện kích thước Camera: {detectedW} × {detectedH} px.";
                }
                else
                {
                    // Đặt về mặc định Camera 20MP 5472x3648
                    ImageSource_PdfCameraWidth = 5472;
                    ImageSource_PdfCameraHeight = 3648;
                    _selectedCameraPreset = AvailableCameraPresets.FirstOrDefault(p => p.Width == 5472 && p.Height == 3648);
                    OnPropertyChanged(nameof(SelectedCameraPreset));
                    OnPropertyChanged(nameof(ImageSource_PdfOpticalInfoText));
                    StatusBarText = "ℹ️ Đã đặt kích thước Camera về chuẩn 20MP: 5472 × 3648 px.";
                }
            }
            catch (Exception ex)
            {
                StatusBarText = $"❌ Lỗi đọc kích thước Camera: {ex.Message}";
            }
        }

        public string ImageSource_PdfPath
        {
            get => SelectedImageSourceDef()?.PdfPath ?? string.Empty;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                value ??= string.Empty;
                if (string.Equals(def.PdfPath, value, StringComparison.Ordinal)) return;
                def.PdfPath = value;
                ClearImageSourceCache(def.Name);
                if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
                {
                    int pages = _pdfDocumentService.GetPageCount(value);
                    ImageSource_PdfTotalPages = Math.Max(1, pages);
                    if (def.PdfPageNumber < 1 || def.PdfPageNumber > ImageSource_PdfTotalPages)
                    {
                        def.PdfPageNumber = 1;
                        OnPropertyChanged(nameof(ImageSource_PdfPageNumber));
                    }
                }
                OnPropertyChanged();
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        private int _imageSourcePdfTotalPages = 1;
        public int ImageSource_PdfTotalPages
        {
            get
            {
                var def = SelectedImageSourceDef();
                if (def != null && !string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
                {
                    int p = _pdfDocumentService.GetPageCount(def.PdfPath);
                    if (p > 0) _imageSourcePdfTotalPages = p;
                }
                return _imageSourcePdfTotalPages;
            }
            set
            {
                if (_imageSourcePdfTotalPages != value)
                {
                    _imageSourcePdfTotalPages = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ImageSource_PdfPageNumber
        {
            get => SelectedImageSourceDef()?.PdfPageNumber ?? 1;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                int maxPages = Math.Max(1, ImageSource_PdfTotalPages);
                int clamped = Math.Clamp(value, 1, maxPages);
                if (def.PdfPageNumber == clamped) return;
                def.PdfPageNumber = clamped;
                OnPropertyChanged();
                if (!string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
                {
                    ImageSource_ConvertPdfToImage();
                }
                else
                {
                    RequestAutoSave();
                }
            }
        }

        public double ImageSource_PdfScale
        {
            get
            {
                var def = SelectedImageSourceDef();
                if (def == null) return 300.0 / 72.0;
                // Nếu là giá trị 1.0 (72 DPI thô cũ) hoặc <= 0, ưu tiên 300 DPI chuẩn nét cao
                if (def.PdfScale <= 1.01) return 300.0 / 72.0;
                return def.PdfScale;
            }
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                double scaleVal = value > 0 ? value : (300.0 / 72.0);
                if (Math.Abs(def.PdfScale - scaleVal) < 0.001) return;
                def.PdfScale = scaleVal;
                def.PdfDpi = (int)Math.Round(scaleVal * 72.0);
                OnPropertyChanged();
                if (!string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
                {
                    ImageSource_ConvertPdfToImage();
                }
                else
                {
                    RequestAutoSave();
                }
            }
        }

        public string ImageSource_PdfRenderedImagePath
        {
            get => SelectedImageSourceDef()?.PdfRenderedImagePath ?? string.Empty;
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                def.PdfRenderedImagePath = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ImageSource_HasPdfRenderedImage));
                RequestAutoSave();
            }
        }

        public bool ImageSource_HasPdfRenderedImage => !string.IsNullOrWhiteSpace(ImageSource_PdfRenderedImagePath) && File.Exists(ImageSource_PdfRenderedImagePath);

        private string _imageSourcePdfImageInfo = string.Empty;
        public string ImageSource_PdfImageInfo
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_imageSourcePdfImageInfo))
                {
                    var def = SelectedImageSourceDef();
                    if (def != null && !string.IsNullOrWhiteSpace(def.PdfRenderedImagePath) && File.Exists(def.PdfRenderedImagePath))
                    {
                        try
                        {
                            using var mat = Cv2.ImRead(def.PdfRenderedImagePath, ImreadModes.Color);
                            if (mat != null && !mat.Empty())
                            {
                                _imageSourcePdfImageInfo = $"{mat.Width} × {mat.Height} px";
                            }
                        }
                        catch { }
                    }
                    else if (def != null && !string.IsNullOrWhiteSpace(def.PdfPath) && File.Exists(def.PdfPath))
                    {
                        var dims = _pdfDocumentService.GetPageDimensions(def.PdfPath, def.PdfPageNumber, def.PdfScale);
                        if (dims.Width > 0 && dims.Height > 0)
                        {
                            _imageSourcePdfImageInfo = $"{dims.Width} × {dims.Height} px";
                        }
                    }
                }
                return _imageSourcePdfImageInfo;
            }
            set
            {
                if (_imageSourcePdfImageInfo != value)
                {
                    _imageSourcePdfImageInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        private void ImageSource_BrowsePdf()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Bản vẽ PDF (*.pdf)|*.pdf|Tất cả tệp (*.*)|*.*",
                Title = "Chọn bản vẽ kỹ thuật PDF để dạy học (Teach)"
            };
            if (dlg.ShowDialog() == true)
            {
                var def = SelectedImageSourceDef();
                if (def == null) return;
                def.PdfPath = dlg.FileName;
                OnPropertyChanged(nameof(ImageSource_PdfPath));

                // Tự động chuyển sang 300 DPI nếu đang ở mức 1.0 (72 DPI thô)
                if (def.PdfScale <= 1.01)
                {
                    def.PdfScale = 300.0 / 72.0;
                    def.PdfDpi = 300;
                    OnPropertyChanged(nameof(ImageSource_PdfScale));
                }

                int totalPages = _pdfDocumentService.GetPageCount(dlg.FileName);
                ImageSource_PdfTotalPages = Math.Max(1, totalPages);
                if (def.PdfPageNumber < 1 || def.PdfPageNumber > ImageSource_PdfTotalPages)
                {
                    def.PdfPageNumber = 1;
                    OnPropertyChanged(nameof(ImageSource_PdfPageNumber));
                }

                // Tự động chuyển đổi bản vẽ PDF ra ảnh và nạp lên canvas để teach
                ImageSource_ConvertPdfToImage();
            }
        }

        public void ImageSource_ConvertPdfToImage()
        {
            var def = SelectedImageSourceDef();
            if (def == null || string.IsNullOrWhiteSpace(def.PdfPath) || !File.Exists(def.PdfPath))
            {
                System.Windows.MessageBox.Show("Vui lòng chọn tệp PDF hợp lệ trước khi chuyển đổi.", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                int page = Math.Max(1, def.PdfPageNumber);
                string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "PdfImages");
                if (!string.IsNullOrWhiteSpace(CurrentTempWorkingDir) && Directory.Exists(CurrentTempWorkingDir))
                {
                    targetDir = Path.Combine(CurrentTempWorkingDir, "pdf_renders");
                }
                Directory.CreateDirectory(targetDir);

                string imagePath;
                int imgW, imgH;
                string detailNote;

                if (def.PdfRenderMode == PdfRenderMode.MatchCamera1to1)
                {
                    double ppm = ImageSource_PdfPixelsPerMm;
                    int camW = def.PdfCameraWidth > 0 ? def.PdfCameraWidth : 5472;
                    int camH = def.PdfCameraHeight > 0 ? def.PdfCameraHeight : 3648;
                    bool fitCanvas = def.PdfFitToCameraCanvas;
                    string align = string.IsNullOrWhiteSpace(def.PdfCanvasAlignment) ? "Center" : def.PdfCanvasAlignment;
                    int offX = def.PdfCanvasOffsetX;
                    int offY = def.PdfCanvasOffsetY;
                    int rot = def.PdfRotation;

                    imagePath = _pdfDocumentService.ConvertPdfToImageFileMatchingCamera(
                        def.PdfPath,
                        page,
                        ppm,
                        fitCanvas,
                        camW,
                        camH,
                        align,
                        offX,
                        offY,
                        rot,
                        targetDir);

                    double dpi = ppm * 25.4;
                    string rotInfo = rot != 0 ? $" • {rot}°" : "";
                    string panInfo = (offX != 0 || offY != 0) ? $" • Pan({offX:+0;-0;0},{offY:+0;-0;0})" : "";
                    if (fitCanvas)
                    {
                        imgW = camW;
                        imgH = camH;
                        detailNote = $"{camW}×{camH} px • Khớp 1:1 Camera ({ppm:F2} px/mm ~ {dpi:F0} DPI{rotInfo}{panInfo})";
                    }
                    else
                    {
                        var dims = _pdfDocumentService.GetPageDimensions(def.PdfPath, page, dpi / 72.0);
                        imgW = dims.Width;
                        imgH = dims.Height;
                        detailNote = $"{imgW}×{imgH} px • Khớp Tỉ lệ 1:1 ({ppm:F2} px/mm{rotInfo}{panInfo})";
                    }
                }
                else
                {
                    double scale = def.PdfScale > 0 ? def.PdfScale : (300.0 / 72.0);
                    int dpi = (int)Math.Round(scale * 72.0);

                    imagePath = _pdfDocumentService.ConvertPdfToImageFile(def.PdfPath, page, scale, targetDir);
                    var dims = _pdfDocumentService.GetPageDimensions(def.PdfPath, page, scale);
                    imgW = dims.Width;
                    imgH = dims.Height;
                    detailNote = $"{imgW} × {imgH} px ({dpi} DPI • Cố định)";
                }

                def.PdfRenderedImagePath = imagePath;
                OnPropertyChanged(nameof(ImageSource_PdfRenderedImagePath));
                OnPropertyChanged(nameof(ImageSource_HasPdfRenderedImage));
                ImageSource_PdfImageInfo = detailNote;

                ClearImageSourceCache(def.Name);
                var rawMat = Cv2.ImRead(imagePath);
                if (rawMat != null && !rawMat.Empty())
                {
                    SetImageSourceCache(def.Name, imagePath, rawMat);
                    using var displayMat = PrepareDisplayImageForSharedContext(rawMat, def);
                    _sharedImage.SetImage(displayMat);
                }

                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();

                StatusBarText = $"✅ Đã chuyển PDF trang {page} sang ảnh ({detailNote}) nền trắng tinh khiết và nạp vào Canvas để dạy học.";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi khi chuyển đổi PDF sang ảnh: {ex.Message}", "Lỗi PDF", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ImageSource_PdfPrevPage()
        {
            var def = SelectedImageSourceDef();
            if (def == null || def.PdfPageNumber <= 1) return;
            ImageSource_PdfPageNumber = def.PdfPageNumber - 1;
        }

        private void ImageSource_PdfNextPage()
        {
            var def = SelectedImageSourceDef();
            if (def == null || def.PdfPageNumber >= ImageSource_PdfTotalPages) return;
            ImageSource_PdfPageNumber = def.PdfPageNumber + 1;
        }

        #endregion

        public void ImageSource_OpenJobCameraSettings()
        {
            var def = SelectedImageSourceDef();
            if (def is null) return;

            def.CameraParams ??= new CameraParameters();
            EnsureImageSourceLightingParams(def);

            var jobName = !string.IsNullOrWhiteSpace(_config?.ProductName)
                ? $"{_config.ProductName} ({_config.ProductCode})"
                : (!string.IsNullOrWhiteSpace(_config?.ProductCode) ? _config.ProductCode : "Job Hiện Tại");

            var vm = new JobCameraSettingsViewModel(
                _cameraService,
                def.CameraParams,
                def.LightingParams,
                _lightingControllerService,
                jobName,
                onSaveCallbackWithLighting: (updatedCamera, updatedLighting) =>
                {
                    def.CameraParams = updatedCamera.Clone();
                    def.LightingParams = updatedLighting.Clone();
                    _ = _cameraService.ApplyParametersAsync(def.CameraParams);
                    SyncLightingChannelViewModels(def);
                    RequestAutoSave();
                },
                onSaveCallback: (updatedParams) =>
                {
                    def.CameraParams = updatedParams.Clone();
                    _ = _cameraService.ApplyParametersAsync(def.CameraParams);
                    RequestAutoSave();
                });

            var win = new Views.JobCameraSettingsWindow(vm)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            win.Show();
        }
    
        public ICommand ImageSource_BrowseFileCommand { get; }
        public ICommand ImageSource_BrowseFolderCommand { get; }
        public ICommand ImageSource_BrowsePdfCommand { get; }
        public ICommand ImageSource_ConvertPdfToImageCommand { get; }
        public ICommand ImageSource_PdfPrevPageCommand { get; }
        public ICommand ImageSource_PdfNextPageCommand { get; }
        public ICommand ImageSource_PdfSyncFromCameraCommand { get; }
        public ICommand ImageSource_PdfSyncPixelsPerMmFromCalibCommand { get; }
        public ICommand ImageSource_PdfApplyPixelsPerMmToJobCommand { get; }
        public ICommand ImageSource_PdfRotate90Command { get; }
        public ICommand ImageSource_PdfPanUpCommand { get; }
        public ICommand ImageSource_PdfPanDownCommand { get; }
        public ICommand ImageSource_PdfPanLeftCommand { get; }
        public ICommand ImageSource_PdfPanRightCommand { get; }
        public ICommand ImageSource_PdfPanResetCommand { get; }
        public ICommand ImageSource_PdfPanDragCommand { get; }
        public ICommand ImageSource_OpenJobCameraSettingsCommand { get; }
        public ICommand ImageSource_ApplyLightingToDeviceCommand { get; }
        public ICommand ImageSource_ReadLightingFromDeviceCommand { get; }

        public ObservableCollection<JobLightingChannelItemViewModel> ImageSource_LightingChannels { get; } = new();

        public int[] AvailableLightingChannelCounts { get; } = { 4, 8 };

        public bool ImageSource_EnableLighting
        {
            get
            {
                var def = SelectedImageSourceDef();
                EnsureImageSourceLightingParams(def);
                return def?.LightingParams?.Enabled ?? true;
            }
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                EnsureImageSourceLightingParams(def);
                if (def.LightingParams.Enabled == value) return;
                def.LightingParams.Enabled = value;
                OnPropertyChanged();
                RequestAutoSave();
            }
        }

        public int ImageSource_LightingChannelCount
        {
            get
            {
                var def = SelectedImageSourceDef();
                EnsureImageSourceLightingParams(def);
                return def?.LightingParams?.ChannelCount ?? 4;
            }
            set
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;
                EnsureImageSourceLightingParams(def);
                int count = value == 8 ? 8 : 4;
                if (def.LightingParams.ChannelCount == count) return;
                def.LightingParams.ChannelCount = count;
                OnPropertyChanged();
                SyncLightingChannelViewModels(def);
                RequestAutoSave();
            }
        }

        public void EnsureImageSourceLightingParams(ImageSourceDefinition? def)
        {
            if (def is null) return;

            def.LightingParams ??= new JobLightingParameters();

            int targetCount = def.LightingParams.ChannelCount == 8 ? 8 : 4;
            if (def.LightingParams.Channels == null)
            {
                def.LightingParams.Channels = new List<JobLightingChannelParams>();
            }

            // Nếu Job cũ chưa có channels, khởi tạo và đọc giá trị từ Lighting Controller LastKnownState
            if (def.LightingParams.Channels.Count == 0)
            {
                var lastState = _lightingControllerService?.LastKnownState;
                for (int i = 0; i < targetCount; i++)
                {
                    bool isEnabled = lastState?.Channels != null && i < lastState.Channels.Length
                        ? lastState.Channels[i].IsEnabled
                        : (i == 0);
                    int br = lastState?.Channels != null && i < lastState.Channels.Length
                        ? lastState.Channels[i].Brightness
                        : 120;
                    int time = lastState?.Channels != null && i < lastState.Channels.Length
                        ? lastState.Channels[i].LightingTimeMs
                        : 100;

                    def.LightingParams.Channels.Add(new JobLightingChannelParams
                    {
                        ChannelIndex = i,
                        IsEnabled = isEnabled,
                        Brightness = br,
                        LightingTimeMs = time
                    });
                }
            }
            else
            {
                // Đảm bảo đủ số lượng targetCount
                while (def.LightingParams.Channels.Count < targetCount)
                {
                    int i = def.LightingParams.Channels.Count;
                    def.LightingParams.Channels.Add(new JobLightingChannelParams
                    {
                        ChannelIndex = i,
                        IsEnabled = i == 0,
                        Brightness = 120,
                        LightingTimeMs = 100
                    });
                }
            }

            SyncLightingChannelViewModels(def);
        }

        public void SyncLightingChannelViewModels(ImageSourceDefinition? def)
        {
            if (def?.LightingParams?.Channels is null)
            {
                ImageSource_LightingChannels.Clear();
                return;
            }

            int targetCount = def.LightingParams.ChannelCount == 8 ? 8 : 4;

            while (ImageSource_LightingChannels.Count < targetCount && ImageSource_LightingChannels.Count < def.LightingParams.Channels.Count)
            {
                int idx = ImageSource_LightingChannels.Count;
                ImageSource_LightingChannels.Add(new JobLightingChannelItemViewModel(def.LightingParams.Channels[idx], this));
            }
            while (ImageSource_LightingChannels.Count > targetCount)
            {
                ImageSource_LightingChannels.RemoveAt(ImageSource_LightingChannels.Count - 1);
            }

            for (int i = 0; i < ImageSource_LightingChannels.Count && i < def.LightingParams.Channels.Count; i++)
            {
                ImageSource_LightingChannels[i].SyncFromParams(def.LightingParams.Channels[i]);
            }
        }

        public async void ImageSource_ApplyLightingToDevice()
        {
            if (_lightingControllerService is null || !_lightingControllerService.IsConnected)
            {
                MessageBox.Show("Bộ điều khiển đèn (Lighting Controller) chưa kết nối!\nVui lòng vào menu Chiếu Sáng -> Lighting Controller để kết nối trước.", "Chưa kết nối đèn", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var def = SelectedImageSourceDef();
                if (def?.LightingParams?.Channels == null) return;

                int count = def.LightingParams.ChannelCount == 8 ? 8 : 4;
                for (int i = 0; i < count && i < def.LightingParams.Channels.Count; i++)
                {
                    var ch = def.LightingParams.Channels[i];
                    var pwrRes = await _lightingControllerService.SetChannelPowerAsync(ch.ChannelIndex, ch.IsEnabled).ConfigureAwait(false);
                    if (!pwrRes.IsSuccess)
                    {
                        var err = !string.IsNullOrWhiteSpace(pwrRes.ErrorMessage) ? pwrRes.ErrorMessage : (pwrRes.ErrorCode ?? "Lỗi gửi lệnh");
                        MessageBox.Show($"❌ Lỗi gửi lệnh bật/tắt kênh CH{ch.ChannelIndex + 1}: {err}", "Lỗi Áp Dụng Đèn", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    if (ch.IsEnabled)
                    {
                        var brRes = await _lightingControllerService.SetBrightnessAsync(ch.ChannelIndex, ch.Brightness).ConfigureAwait(false);
                        if (!brRes.IsSuccess)
                        {
                            var err = !string.IsNullOrWhiteSpace(brRes.ErrorMessage) ? brRes.ErrorMessage : (brRes.ErrorCode ?? "Lỗi gửi độ sáng");
                            MessageBox.Show($"❌ Lỗi gửi độ sáng kênh CH{ch.ChannelIndex + 1}: {err}", "Lỗi Áp Dụng Đèn", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        var tmRes = await _lightingControllerService.SetLightingTimeAsync(ch.ChannelIndex, ch.LightingTimeMs).ConfigureAwait(false);
                        if (!tmRes.IsSuccess)
                        {
                            var err = !string.IsNullOrWhiteSpace(tmRes.ErrorMessage) ? tmRes.ErrorMessage : (tmRes.ErrorCode ?? "Lỗi gửi thời gian sáng");
                            MessageBox.Show($"❌ Lỗi gửi thời gian sáng kênh CH{ch.ChannelIndex + 1}: {err}", "Lỗi Áp Dụng Đèn", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }
                MessageBox.Show($"✅ Đã áp dụng thành công mức sáng của {count} kênh đèn xuống thiết bị!", "Áp Dụng Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi áp dụng đèn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task _lightingServiceSetLightingTime(int ch, int timeMs)
        {
            if (_lightingControllerService != null)
            {
                await _lightingControllerService.SetLightingTimeAsync(ch, timeMs).ConfigureAwait(false);
            }
        }

        public async void ImageSource_ReadLightingFromDevice()
        {
            if (_lightingControllerService is null || !_lightingControllerService.IsConnected)
            {
                MessageBox.Show("Bộ điều khiển đèn (Lighting Controller) chưa kết nối!\nVui lòng vào menu Chiếu Sáng -> Lighting Controller để kết nối trước.", "Chưa kết nối đèn", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var def = SelectedImageSourceDef();
                if (def is null) return;

                int count = def.LightingParams?.ChannelCount == 8 ? 8 : 4;
                var result = await _lightingControllerService.ReadAllParametersAsync(count);
                if (result.IsSuccess && result.Data?.Channels != null)
                {
                    def.LightingParams ??= new JobLightingParameters();
                    def.LightingParams.Channels.Clear();
                    for (int i = 0; i < count && i < result.Data.Channels.Length; i++)
                    {
                        var chState = result.Data.Channels[i];
                        def.LightingParams.Channels.Add(new JobLightingChannelParams
                        {
                            ChannelIndex = i,
                            IsEnabled = chState.IsEnabled,
                            Brightness = chState.Brightness,
                            LightingTimeMs = chState.LightingTimeMs
                        });
                    }

                    SyncLightingChannelViewModels(def);
                    RequestAutoSave();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi đọc thông số đèn: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public ObservableCollection<PreprocessRoiDefinition> PreprocessRois
        {
            get
            {
                var def = SelectedPreprocessNodeDef();
                if (def is null) return new ObservableCollection<PreprocessRoiDefinition>();
                foreach (var r in def.Rois)
                {
                    HookPreprocessRoiEvents(r);
                }
                return new ObservableCollection<PreprocessRoiDefinition>(def.Rois);
            }
        }

        private void HookPreprocessRoiEvents(PreprocessRoiDefinition roi)
        {
            roi.PropertyChanged -= OnPreprocessRoiPropertyChanged;
            roi.PropertyChanged += OnPreprocessRoiPropertyChanged;
            if (roi.PolygonPoints != null)
            {
                foreach (var pt in roi.PolygonPoints)
                {
                    pt.PropertyChanged -= OnPreprocessRoiPropertyChanged;
                    pt.PropertyChanged += OnPreprocessRoiPropertyChanged;
                }
            }
        }

        private void OnPreprocessRoiPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            SchedulePreprocessPreviewUpdate();
            RefreshPreviews();
            RequestAutoSave();
        }

        public void Preprocess_AddRoi(PreprocessRoiShape shape, PreprocessRoiMode mode = PreprocessRoiMode.Include)
        {
            var def = SelectedPreprocessNodeDef();
            if (def is null) return;

            var newRoi = new PreprocessRoiDefinition
            {
                Shape = shape,
                Mode = mode,
                FollowOrigin = true,
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

            HookPreprocessRoiEvents(newRoi);
            def.Rois.Add(newRoi);
            OnPropertyChanged(nameof(PreprocessRois));
            RaiseToolPropertyPanelsChanged();
            SchedulePreprocessPreviewUpdate();
            RefreshPreviews();
            RequestAutoSave();
        }

        public void Preprocess_RemoveRoi(PreprocessRoiDefinition? roi)
        {
            var def = SelectedPreprocessNodeDef();
            if (def is null || roi is null) return;
            roi.PropertyChanged -= OnPreprocessRoiPropertyChanged;
            def.Rois.Remove(roi);
            OnPropertyChanged(nameof(PreprocessRois));
            RaiseToolPropertyPanelsChanged();
            SchedulePreprocessPreviewUpdate();
            RefreshPreviews();
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
            RefreshPreviews();
            RequestAutoSave();
        }

        public void Preprocess_AddPolygonPoint(PreprocessRoiDefinition? roi)
        {
            if (roi is null || roi.Shape != PreprocessRoiShape.Polygon) return;
            if (roi.PolygonPoints is null) roi.PolygonPoints = new List<Point2dModel>();

            Point2dModel newPt;
            if (roi.PolygonPoints.Count == 0)
            {
                newPt = new Point2dModel { X = 100, Y = 100 };
                roi.PolygonPoints.Add(newPt);
                roi.PolygonPoints.Add(new Point2dModel { X = 250, Y = 100 });
                roi.PolygonPoints.Add(new Point2dModel { X = 175, Y = 250 });
            }
            else
            {
                var last = roi.PolygonPoints.Last();
                var first = roi.PolygonPoints.First();
                newPt = new Point2dModel
                {
                    X = Math.Round((last.X + first.X) / 2.0 + 30),
                    Y = Math.Round((last.Y + first.Y) / 2.0 + 30)
                };
                roi.PolygonPoints.Add(newPt);
            }

            HookPreprocessRoiEvents(roi);
            OnPropertyChanged(nameof(PreprocessRois));
            RaiseToolPropertyPanelsChanged();
            SchedulePreprocessPreviewUpdate();
            RefreshPreviews();
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
                        point.PropertyChanged -= OnPreprocessRoiPropertyChanged;
                        roi.PolygonPoints.Remove(point);
                        OnPropertyChanged(nameof(PreprocessRois));
                        RaiseToolPropertyPanelsChanged();
                        SchedulePreprocessPreviewUpdate();
                        RefreshPreviews();
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
