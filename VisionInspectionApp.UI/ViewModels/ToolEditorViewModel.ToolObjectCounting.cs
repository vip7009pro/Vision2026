using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        public ObservableCollection<ObjectCountingAlgorithm> AvailableObjectCountingAlgorithms { get; } =
            new ObservableCollection<ObjectCountingAlgorithm>((ObjectCountingAlgorithm[])Enum.GetValues(typeof(ObjectCountingAlgorithm)));

        public ICommand ObjectCounting_SetSearchRoiCommand { get; private set; } = null!;
        public ICommand ObjectCounting_SetTemplateRoiCommand { get; private set; } = null!;
        public ICommand ObjectCounting_SetTemplateCommand { get; private set; } = null!;

        private BitmapSource? _objectCountingTemplatePreviewImage;
        public BitmapSource? ObjectCounting_TemplatePreviewImage
        {
            get => _objectCountingTemplatePreviewImage;
            private set => SetProperty(ref _objectCountingTemplatePreviewImage, value);
        }

        private string _objectCountingTemplateInfoText = string.Empty;
        public string ObjectCounting_TemplateInfoText
        {
            get => _objectCountingTemplateInfoText;
            private set => SetProperty(ref _objectCountingTemplateInfoText, value);
        }

        public bool IsObjectCountingNode => SelectedNode is not null &&
            (string.Equals(SelectedNode.Type, "ObjectCounting", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(SelectedNode.Type, "ObjectCouting", StringComparison.OrdinalIgnoreCase));

        private void InitializeObjectCountingCommands()
        {
            ObjectCounting_SetSearchRoiCommand = new RelayCommand(ObjectCounting_SetSearchRoi);
            ObjectCounting_SetTemplateRoiCommand = new RelayCommand(ObjectCounting_SetTemplateRoi);
            ObjectCounting_SetTemplateCommand = new RelayCommand(ObjectCounting_SetTemplate);
        }

        private ObjectCountingDefinition? SelectedObjectCountingDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!IsObjectCountingNode)
                return null;
            return _config.ObjectCountings.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }

        private void ObjectCounting_SetSearchRoi()
        {
            if (!IsObjectCountingNode || SelectedNode is null) return;
            ActiveRoiLabel = $"{SelectedNode.RefName} OC";
        }

        private void ObjectCounting_SetTemplateRoi()
        {
            if (!IsObjectCountingNode || SelectedNode is null) return;
            ActiveRoiLabel = $"{SelectedNode.RefName} OCT";
        }

        private void ObjectCounting_SetTemplate()
        {
            if (!IsObjectCountingNode || SelectedNode is null) return;
            var def = SelectedObjectCountingDef();
            if (def is null) return;

            if (def.TemplateRoi.Width <= 0 || def.TemplateRoi.Height <= 0)
            {
                if (def.SearchRoi.Width > 0 && def.SearchRoi.Height > 0)
                {
                    def.TemplateRoi = new Roi
                    {
                        X = def.SearchRoi.X + def.SearchRoi.Width / 4,
                        Y = def.SearchRoi.Y + def.SearchRoi.Height / 4,
                        Width = Math.Max(20, def.SearchRoi.Width / 2),
                        Height = Math.Max(20, def.SearchRoi.Height / 2)
                    };
                }
                else
                {
                    StatusBarText = $"Vui lòng vẽ vùng Template ROI (dạy mẫu) cho {def.Name} trước.";
                    return;
                }
            }

            TrySaveObjectCountingTemplateImage(def.Name, def.TemplateRoi);
            RefreshPreviews();
            RefreshObjectCountingTemplatePreview();
            RaiseToolPropertyPanelsChanged();
            RequestAutoSave();
            StatusBarText = $"Đã chốt dạy mẫu (teaching) cho {def.Name} thành công ({def.TemplateRoi.Width}x{def.TemplateRoi.Height} px)";
        }

        public void RefreshObjectCountingTemplatePreview()
        {
            if (!IsObjectCountingNode)
            {
                if (ObjectCounting_TemplatePreviewImage is not null)
                {
                    ObjectCounting_TemplatePreviewImage = null;
                    ObjectCounting_TemplateInfoText = string.Empty;
                }
                return;
            }

            var def = SelectedObjectCountingDef();
            if (def is null || string.IsNullOrWhiteSpace(def.TemplateImageFile))
            {
                ObjectCounting_TemplatePreviewImage = null;
                ObjectCounting_TemplateInfoText = "Chưa dạy template (Vẽ Template ROI rồi bấm Dạy Mẫu)";
                return;
            }

            try
            {
                var resolved = ResolveTemplatePath(def.TemplateImageFile, $"{def.Name.ToLowerInvariant()}_oc.png", "*_oc.png");
                if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
                {
                    ObjectCounting_TemplatePreviewImage = null;
                    ObjectCounting_TemplateInfoText = "Chưa tìm thấy tệp ảnh template";
                    return;
                }

                def.TemplateImageFile = resolved;
                using var rawMat = Cv2.ImRead(resolved, ImreadModes.Color);
                if (rawMat.Empty())
                {
                    ObjectCounting_TemplatePreviewImage = null;
                    ObjectCounting_TemplateInfoText = "Lỗi đọc tệp template";
                    return;
                }

                var bmp = rawMat.ToBitmapSource();
                bmp.Freeze();
                ObjectCounting_TemplatePreviewImage = bmp;
                ObjectCounting_TemplateInfoText = $"Template: {rawMat.Width} x {rawMat.Height} px (File: {Path.GetFileName(resolved)})";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RefreshObjectCountingTemplatePreview] Error: {ex.Message}");
                ObjectCounting_TemplatePreviewImage = null;
                ObjectCounting_TemplateInfoText = "Lỗi đọc ảnh template";
            }
        }

        public void TrySaveObjectCountingTemplateImage(string toolName, Roi roi, Roi? cropRoi = null)
        {
            if (_config is null) return;

            using var rawSnap = _sharedImage.GetSnapshot();
            using var snap = rawSnap ?? new Mat();
            if (snap.Empty()) return;

            var oc = _config.ObjectCountings.FirstOrDefault(x => string.Equals(x.Name, toolName, StringComparison.OrdinalIgnoreCase));
            if (oc is null) return;

            var targetRoi = cropRoi ?? roi;
            if (targetRoi.Width <= 0 || targetRoi.Height <= 0) return;

            var toolNode = Nodes.FirstOrDefault(n => IsObjectCountingNode && string.Equals(n.RefName, toolName, StringComparison.OrdinalIgnoreCase));
            using var processedMat = toolNode != null ? ResolveToolImageForPreview(snap, toolNode) : snap.Clone();
            if (processedMat.Empty()) return;

            GetOriginPose(out var originTeach, out var originFound, out var originAngleDeg);
            using var crop = cropRoi != null
                ? Geometry2D.ExtractStraightRoi(processedMat, cropRoi, default, default, 0, out _)
                : Geometry2D.ExtractStraightRoi(processedMat, targetRoi, originTeach, originFound, originAngleDeg, out _);

            if (crop.Empty() || crop.Width <= 0 || crop.Height <= 0) return;

            using var gray = crop.Channels() == 1 ? crop.Clone() : crop.CvtColor(ColorConversionCodes.BGR2GRAY);

            var templateDir = Path.Combine(CurrentTempWorkingDir ?? Path.Combine(Path.GetFullPath(_storeOptions.ConfigRootDirectory), ProductCode), "templates");
            Directory.CreateDirectory(templateDir);
            var fileName = Path.Combine(templateDir, $"{toolName.ToLowerInvariant()}_oc.png");

            Cv2.ImWrite(fileName, gray);
            oc.TemplateImageFile = fileName;
            RequestAutoSave();
            RefreshObjectCountingTemplatePreview();
            RaiseToolPropertyPanelsChanged();
        }

        #region Properties Binding Cho ObjectCounting

        public int ObjectCounting_NominalCount
        {
            get => SelectedObjectCountingDef()?.NominalCount ?? 1;
            set
            {
                var def = SelectedObjectCountingDef();
                if (def is null) return;
                var v = Math.Max(0, value);
                if (def.NominalCount == v) return;
                def.NominalCount = v;
                OnPropertyChanged(nameof(ObjectCounting_NominalCount));
                OnPropertyChanged(nameof(ObjectCounting_MinCount));
                OnPropertyChanged(nameof(ObjectCounting_MaxCount));
                OnPropertyChanged(nameof(ObjectCounting_SpecRangeText));
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public int ObjectCounting_Tolerance
        {
            get => SelectedObjectCountingDef()?.Tolerance ?? 0;
            set
            {
                var def = SelectedObjectCountingDef();
                if (def is null) return;
                var v = Math.Max(0, value);
                if (def.Tolerance == v && def.TolerancePlus == v && def.ToleranceMinus == v) return;
                def.Tolerance = v;
                OnPropertyChanged(nameof(ObjectCounting_Tolerance));
                OnPropertyChanged(nameof(ObjectCounting_TolerancePlus));
                OnPropertyChanged(nameof(ObjectCounting_ToleranceMinus));
                OnPropertyChanged(nameof(ObjectCounting_MinCount));
                OnPropertyChanged(nameof(ObjectCounting_MaxCount));
                OnPropertyChanged(nameof(ObjectCounting_SpecRangeText));
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public int ObjectCounting_TolerancePlus
        {
            get => SelectedObjectCountingDef()?.TolerancePlus ?? 0;
            set
            {
                var def = SelectedObjectCountingDef();
                if (def is null) return;
                var v = Math.Max(0, value);
                if (def.TolerancePlus == v) return;
                def.TolerancePlus = v;
                OnPropertyChanged(nameof(ObjectCounting_TolerancePlus));
                OnPropertyChanged(nameof(ObjectCounting_Tolerance));
                OnPropertyChanged(nameof(ObjectCounting_MinCount));
                OnPropertyChanged(nameof(ObjectCounting_MaxCount));
                OnPropertyChanged(nameof(ObjectCounting_SpecRangeText));
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public int ObjectCounting_ToleranceMinus
        {
            get => SelectedObjectCountingDef()?.ToleranceMinus ?? 0;
            set
            {
                var def = SelectedObjectCountingDef();
                if (def is null) return;
                var v = Math.Max(0, value);
                if (def.ToleranceMinus == v) return;
                def.ToleranceMinus = v;
                OnPropertyChanged(nameof(ObjectCounting_ToleranceMinus));
                OnPropertyChanged(nameof(ObjectCounting_Tolerance));
                OnPropertyChanged(nameof(ObjectCounting_MinCount));
                OnPropertyChanged(nameof(ObjectCounting_MaxCount));
                OnPropertyChanged(nameof(ObjectCounting_SpecRangeText));
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public int ObjectCounting_MinCount => SelectedObjectCountingDef()?.MinCount ?? 0;
        public int ObjectCounting_MaxCount => SelectedObjectCountingDef()?.MaxCount ?? 0;

        public string ObjectCounting_SpecRangeText
        {
            get
            {
                var def = SelectedObjectCountingDef();
                if (def is null) return "Chưa chọn node";
                return $"{def.NominalCount} (±{def.Tolerance}) [Cho phép: {def.MinCount} .. {def.MaxCount}]";
            }
        }

        public double ObjectCounting_MinScore
        {
            get => SelectedObjectCountingDef()?.MinScore ?? 0.65;
            set
            {
                var def = SelectedObjectCountingDef();
                if (def is null) return;
                var v = Math.Clamp(value, 0.1, 1.0);
                if (Math.Abs(def.MinScore - v) < 1e-4) return;
                def.MinScore = v;
                OnPropertyChanged(nameof(ObjectCounting_MinScore));
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ObjectCounting_MinAngle
        {
            get => SelectedObjectCountingDef()?.MinAngle ?? -180.0;
            set
            {
                var def = SelectedObjectCountingDef();
                if (def is null) return;
                var v = Math.Clamp(value, -360.0, 360.0);
                if (Math.Abs(def.MinAngle - v) < 1e-4) return;
                def.MinAngle = v;
                OnPropertyChanged(nameof(ObjectCounting_MinAngle));
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ObjectCounting_MaxAngle
        {
            get => SelectedObjectCountingDef()?.MaxAngle ?? 180.0;
            set
            {
                var def = SelectedObjectCountingDef();
                if (def is null) return;
                var v = Math.Clamp(value, -360.0, 360.0);
                if (Math.Abs(def.MaxAngle - v) < 1e-4) return;
                def.MaxAngle = v;
                OnPropertyChanged(nameof(ObjectCounting_MaxAngle));
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ObjectCounting_AngleStep
        {
            get => SelectedObjectCountingDef()?.AngleStep ?? 3.0;
            set
            {
                var def = SelectedObjectCountingDef();
                if (def is null) return;
                var v = Math.Clamp(value, 0.5, 30.0);
                if (Math.Abs(def.AngleStep - v) < 1e-4) return;
                def.AngleStep = v;
                OnPropertyChanged(nameof(ObjectCounting_AngleStep));
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ObjectCounting_MaxOverlap
        {
            get => SelectedObjectCountingDef()?.MaxOverlap ?? 0.35;
            set
            {
                var def = SelectedObjectCountingDef();
                if (def is null) return;
                var v = Math.Clamp(value, 0.05, 0.95);
                if (Math.Abs(def.MaxOverlap - v) < 1e-4) return;
                def.MaxOverlap = v;
                OnPropertyChanged(nameof(ObjectCounting_MaxOverlap));
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public ObjectCountingAlgorithm ObjectCounting_Algorithm
        {
            get => SelectedObjectCountingDef()?.Algorithm ?? ObjectCountingAlgorithm.ShapeMatching;
            set
            {
                var def = SelectedObjectCountingDef();
                if (def is null) return;
                if (def.Algorithm == value) return;
                def.Algorithm = value;
                OnPropertyChanged(nameof(ObjectCounting_Algorithm));
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public string ObjectCounting_LastRunFoundText
        {
            get
            {
                if (SelectedNode is null || _lastRun is null) return "-";
                var r = _lastRun.ObjectCountings?.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (r is null) return "-";
                return $"{r.FoundCount} vật thể";
            }
        }

        public string ObjectCounting_LastRunStatusText
        {
            get
            {
                if (SelectedNode is null || _lastRun is null) return "-";
                var r = _lastRun.ObjectCountings?.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (r is null) return "-";
                if (r.Pass) return "PASS (ĐẠT)";
                if (r.FoundCount < r.MinCount) return $"NG (Thiếu: {r.FoundCount} < {r.MinCount})";
                return $"NG (Thừa: {r.FoundCount} > {r.MaxCount})";
            }
        }

        public SolidColorBrush ObjectCounting_LastRunStatusBrush
        {
            get
            {
                if (SelectedNode is null || _lastRun is null) return new SolidColorBrush(Colors.Gray);
                var r = _lastRun.ObjectCountings?.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (r is null) return new SolidColorBrush(Colors.Gray);
                return r.Pass ? new SolidColorBrush(Color.FromRgb(46, 204, 113)) : new SolidColorBrush(Color.FromRgb(231, 76, 60));
            }
        }

        public string ObjectCounting_LastRunCount => ObjectCounting_LastRunFoundText;
        public string ObjectCounting_PassStatus => ObjectCounting_LastRunStatusText;
        public SolidColorBrush ObjectCounting_PassColor => ObjectCounting_LastRunStatusBrush;

        #endregion
    }
}
