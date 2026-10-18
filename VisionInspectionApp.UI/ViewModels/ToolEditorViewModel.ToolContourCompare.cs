using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;
using Point = OpenCvSharp.Point;

namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class ToolEditorViewModel : ObservableObject
    {
        public IEnumerable<ContourMatchMethod> AvailableContourMatchMethods => Enum.GetValues<ContourMatchMethod>();

        public ICommand ContourCompare_SetSearchRoiCommand { get; }
        public ICommand ContourCompare_SetTemplateRoiCommand { get; }

        private BitmapSource? _contourCompareTemplatePreviewImage;
        public BitmapSource? ContourCompare_TemplatePreviewImage
        {
            get => _contourCompareTemplatePreviewImage;
            private set => SetProperty(ref _contourCompareTemplatePreviewImage, value);
        }

        private string _contourCompareTemplateInfoText = string.Empty;
        public string ContourCompare_TemplateInfoText
        {
            get => _contourCompareTemplateInfoText;
            private set => SetProperty(ref _contourCompareTemplateInfoText, value);
        }

        private string? _contourCompareCachedFile;
        private DateTime _contourCompareCachedWriteTime;
        private double _contourCompareCachedC1 = -1;
        private double _contourCompareCachedC2 = -1;
        private int _contourCompareCachedMinArea = -1;

        public void InvalidateContourCompareTemplatePreviewCache()
        {
            _contourCompareCachedFile = null;
            _contourCompareCachedC1 = -1;
            _contourCompareCachedC2 = -1;
            _contourCompareCachedMinArea = -1;
        }

        public void RefreshContourCompareTemplatePreview()
        {
            if (SelectedNode is null || !string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase))
            {
                if (ContourCompare_TemplatePreviewImage is not null)
                {
                    ContourCompare_TemplatePreviewImage = null;
                    ContourCompare_TemplateInfoText = string.Empty;
                }
                return;
            }

            var def = SelectedContourCompareDef();
            if (def is null || string.IsNullOrWhiteSpace(def.TemplateImageFile))
            {
                InvalidateContourCompareTemplatePreviewCache();
                ContourCompare_TemplatePreviewImage = null;
                ContourCompare_TemplateInfoText = "Chưa lưu template (Vẽ Template ROI rồi bấm Set Template)";
                return;
            }

            var c1 = def.CannyThreshold1 > 0 ? def.CannyThreshold1 : 50;
            var c2 = def.CannyThreshold2 > 0 ? def.CannyThreshold2 : 150;
            var minArea = Math.Max(1, def.MinContourArea);

            try
            {
                var resolvedFile = ResolveTemplatePath(def.TemplateImageFile, $"{def.Name.ToLowerInvariant()}_contour.png", "*_contour.png");
                if (string.IsNullOrWhiteSpace(resolvedFile) || !File.Exists(resolvedFile))
                {
                    InvalidateContourCompareTemplatePreviewCache();
                    ContourCompare_TemplatePreviewImage = null;
                    ContourCompare_TemplateInfoText = "Chưa tìm thấy tệp ảnh template";
                    return;
                }

                def.TemplateImageFile = resolvedFile;
                var fileInfo = new FileInfo(resolvedFile);
                if (string.Equals(_contourCompareCachedFile, resolvedFile, StringComparison.OrdinalIgnoreCase)
                    && _contourCompareCachedWriteTime == fileInfo.LastWriteTimeUtc
                    && Math.Abs(_contourCompareCachedC1 - c1) < 1e-4
                    && Math.Abs(_contourCompareCachedC2 - c2) < 1e-4
                    && _contourCompareCachedMinArea == minArea
                    && ContourCompare_TemplatePreviewImage != null)
                {
                    return;
                }

                using var rawMat = Cv2.ImRead(resolvedFile, ImreadModes.Grayscale);
                if (rawMat.Empty())
                {
                    InvalidateContourCompareTemplatePreviewCache();
                    ContourCompare_TemplatePreviewImage = null;
                    ContourCompare_TemplateInfoText = "Tệp ảnh template rỗng";
                    return;
                }

                // Nâng tỉ lệ mẫu nhỏ (NearestNeighbor) để vẽ viền mảnh 1px khử răng cưa sắc nét
                int targetMinDim = 240;
                int scaleFactor = Math.Max(1, Math.Min(4, targetMinDim / Math.Max(1, Math.Min(rawMat.Width, rawMat.Height))));

                using var displayMat = new Mat();
                if (scaleFactor > 1)
                {
                    Cv2.Resize(rawMat, displayMat, new OpenCvSharp.Size(rawMat.Width * scaleFactor, rawMat.Height * scaleFactor), 0, 0, InterpolationFlags.Nearest);
                }
                else
                {
                    rawMat.CopyTo(displayMat);
                }

                using var colorMat = new Mat();
                Cv2.CvtColor(displayMat, colorMat, ColorConversionCodes.GRAY2BGR);

                using var canny = new Mat();
                Cv2.Canny(displayMat, canny, c1, c2);
                Cv2.FindContours(canny, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone);
                int scaledMinArea = minArea * scaleFactor * scaleFactor;
                var validContours = (contours ?? Array.Empty<Point[]>())
                    .Where(c => c.Length >= 4 && (Cv2.ContourArea(c) >= scaledMinArea || Cv2.ArcLength(c, true) >= scaledMinArea / 2.0))
                    .OrderByDescending(c => Cv2.ArcLength(c, true))
                    .ToList();

                if (validContours.Count > 0)
                {
                    // Vẽ các đường viền mẫu thật mảnh (thickness: 1, AntiAlias) bằng màu Vàng kim (Gold: B=0, G=215, R=255)
                    Cv2.DrawContours(colorMat, validContours, -1, new Scalar(0, 215, 255), 1, LineTypes.AntiAlias);
                }

                ContourCompare_TemplatePreviewImage = colorMat.ToBitmapSourceForDisplay();
                ContourCompare_TemplateInfoText = $"Mẫu: {rawMat.Width}×{rawMat.Height} px | {validContours.Count} viền (Canny: {c1:0}/{c2:0})";

                _contourCompareCachedFile = resolvedFile;
                _contourCompareCachedWriteTime = fileInfo.LastWriteTimeUtc;
                _contourCompareCachedC1 = c1;
                _contourCompareCachedC2 = c2;
                _contourCompareCachedMinArea = minArea;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RefreshContourCompareTemplatePreview] Error: {ex.Message}");
                InvalidateContourCompareTemplatePreviewCache();
                ContourCompare_TemplatePreviewImage = null;
                ContourCompare_TemplateInfoText = "Lỗi đọc template";
            }
        }

        private void ContourCompare_SetSearchRoi()
        {
            if (SelectedNode is null || !string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase))
                return;
            ActiveRoiLabel = $"{SelectedNode.RefName} CC";
        }

        private void ContourCompare_SetTemplateRoi()
        {
            if (SelectedNode is null || !string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase))
                return;
            ActiveRoiLabel = $"{SelectedNode.RefName} CCT";
        }

        private ContourCompareDefinition? SelectedContourCompareDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }

        public ContourMatchMethod ContourCompare_MatchMethod
        {
            get => SelectedContourCompareDef()?.MatchMethod ?? ContourMatchMethod.HuMoments;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                if (def.MatchMethod == value) return;
                def.MatchMethod = value;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ContourCompare_CannyThreshold1
        {
            get => SelectedContourCompareDef()?.CannyThreshold1 ?? 50;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                var v = Math.Clamp(value, 1.0, 500.0);
                if (Math.Abs(def.CannyThreshold1 - v) < 1e-4) return;
                def.CannyThreshold1 = v;
                OnPropertyChanged(nameof(ContourCompare_CannyThreshold1));
                RaiseToolPropertyPanelsChanged();
                RefreshContourCompareTemplatePreview();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ContourCompare_CannyThreshold2
        {
            get => SelectedContourCompareDef()?.CannyThreshold2 ?? 150;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                var v = Math.Clamp(value, 1.0, 500.0);
                if (Math.Abs(def.CannyThreshold2 - v) < 1e-4) return;
                def.CannyThreshold2 = v;
                OnPropertyChanged(nameof(ContourCompare_CannyThreshold2));
                RaiseToolPropertyPanelsChanged();
                RefreshContourCompareTemplatePreview();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public int ContourCompare_MinContourArea
        {
            get => SelectedContourCompareDef()?.MinContourArea ?? 50;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                var v = Math.Max(1, value);
                if (def.MinContourArea == v) return;
                def.MinContourArea = v;
                RaiseToolPropertyPanelsChanged();
                RefreshContourCompareTemplatePreview();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ContourCompare_MaxShapeMatchScore
        {
            get => SelectedContourCompareDef()?.MaxShapeMatchScore ?? 0.10;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                var v = Math.Clamp(value, 0.001, 10.0);
                if (Math.Abs(def.MaxShapeMatchScore - v) < 1e-4) return;
                def.MaxShapeMatchScore = v;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ContourCompare_MaxHausdorffDistPx
        {
            get => SelectedContourCompareDef()?.MaxHausdorffDistPx ?? 5.0;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                var v = Math.Clamp(value, 0.1, 500.0);
                if (Math.Abs(def.MaxHausdorffDistPx - v) < 1e-4) return;
                def.MaxHausdorffDistPx = v;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double ContourCompare_MaxAreaDiffPercent
        {
            get => SelectedContourCompareDef()?.MaxAreaDiffPercent ?? 5.0;
            set
            {
                var def = SelectedContourCompareDef();
                if (def is null) return;
                var v = Math.Clamp(value, 0.1, 100.0);
                if (Math.Abs(def.MaxAreaDiffPercent - v) < 1e-4) return;
                def.MaxAreaDiffPercent = v;
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public double? ContourCompare_LastRunScore
        {
            get
            {
                if (_lastRun is null || SelectedNode is null) return null;
                if (!string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase)) return null;
                var r = _lastRun.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return r is null ? null : r.MatchScore;
            }
        }

        public double? ContourCompare_LastRunMaxDist
        {
            get
            {
                if (_lastRun is null || SelectedNode is null) return null;
                if (!string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase)) return null;
                var r = _lastRun.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return r is null ? null : r.MaxDistancePx;
            }
        }

        public double? ContourCompare_LastRunAreaDiff
        {
            get
            {
                if (_lastRun is null || SelectedNode is null) return null;
                if (!string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase)) return null;
                var r = _lastRun.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return r is null ? null : r.AreaDiffPercent;
            }
        }

        public string? ContourCompare_LastRunStatus
        {
            get
            {
                if (_lastRun is null || SelectedNode is null) return null;
                if (!string.Equals(SelectedNode.Type, "ContourCompare", StringComparison.OrdinalIgnoreCase)) return null;
                var r = _lastRun.ContourCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return r is null ? null : (r.Pass ? "PASS (OK)" : "NG (LỖI)");
            }
        }
    }
}
