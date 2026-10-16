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
        private BlobDetectionDefinition? SelectedBlobDetectionDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }

        public double Blob_MaxSpecPreviewWidth => GetMmPreviewSide(Blob_MaxBlobWidth);
        public double Blob_MaxSpecPreviewHeight => GetMmPreviewSide(Blob_MaxBlobLength);
        public bool Blob_HasMaxSpecPreview => Blob_MaxBlobWidth > 0 || Blob_MaxBlobLength > 0;
    
        public BlobPolarity Blob_Polarity
        {
            get => SelectedBlobDetectionDef()?.Polarity ?? BlobPolarity.DarkOnLight;
            set
            {
                var def = SelectedBlobDetectionDef();
                if (def is null)
                    return;
                if (def.Polarity == value)
                    return;
                def.Polarity = value;
                RequestBlobThresholdPreviewUpdate();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }

        public bool Blob_HasMinDistancePreview => Blob_MinBlobDistance > 0;
        public double Blob_MinDistancePreviewGap => GetMmPreviewSide(Blob_MinBlobDistance);

        public IReadOnlyList<string> AvailableBlobCountingModeNames { get; } = new[]
        {
            "Đếm riêng biệt",
            "Loại bỏ blob bị đè"
        };

        public BlobCountingMode Blob_CountingMode
        {
            get => SelectedBlobDetectionDef()?.CountingMode ?? BlobCountingMode.Separate;
            set
            {
                var def = SelectedBlobDetectionDef();
                if (def is null)
                    return;
                if (def.CountingMode == value)
                    return;
                def.CountingMode = value;
                RequestAutoSave();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Blob_CountingModeText));
                OnPropertyChanged(nameof(Blob_PassStatus));
                OnPropertyChanged(nameof(Blob_PassColor));
                OnPropertyChanged(nameof(Blob_LastRunCount));
                RefreshPreviews();
            }
        }

        public string Blob_CountingModeText
        {
            get => Blob_CountingMode == BlobCountingMode.ExcludeContained ? "Loại bỏ blob bị đè" : "Đếm riêng biệt";
            set
            {
                var mode = string.Equals(value, "Loại bỏ blob bị đè", StringComparison.OrdinalIgnoreCase)
                    ? BlobCountingMode.ExcludeContained
                    : BlobCountingMode.Separate;
                Blob_CountingMode = mode;
            }
        }
    
        public int Blob_Threshold
        {
            get => SelectedBlobDetectionDef()?.Threshold ?? 128;
            set
            {
                var def = SelectedBlobDetectionDef();
                if (def is null)
                    return;
                var v = Math.Clamp(value, 0, 255);
                if (def.Threshold == v)
                    return;
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
                if (def is null)
                    return;
                var v = Math.Max(0, value);
                if (def.MinBlobArea == v)
                    return;
                def.MinBlobArea = v;
                if (def.MaxBlobArea < def.MinBlobArea)
                    def.MaxBlobArea = def.MinBlobArea;
                RequestAutoSave();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Blob_MaxBlobArea));
                OnPropertyChanged(nameof(Blob_MinAreaPreviewSide));
                OnPropertyChanged(nameof(Blob_MaxAreaPreviewSide));
                OnPropertyChanged(nameof(Blob_MinAreaPreviewText));
                OnPropertyChanged(nameof(Blob_MaxAreaPreviewText));
            }
        }
    
        public int Blob_MaxBlobArea
        {
            get => SelectedBlobDetectionDef()?.MaxBlobArea ?? 0;
            set
            {
                var def = SelectedBlobDetectionDef();
                if (def is null)
                    return;
                var v = Math.Max(0, value);
                if (v < def.MinBlobArea)
                    v = def.MinBlobArea;
                if (def.MaxBlobArea == v)
                    return;
                def.MaxBlobArea = v;
                RequestAutoSave();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Blob_MinAreaPreviewSide));
                OnPropertyChanged(nameof(Blob_MaxAreaPreviewSide));
                OnPropertyChanged(nameof(Blob_MinAreaPreviewText));
                OnPropertyChanged(nameof(Blob_MaxAreaPreviewText));
            }
        }
    
        public int? Blob_LastRunCount
        {
            get
            {
                if (_lastRun is null || SelectedNode is null)
                    return null;
                if (!string.Equals(SelectedNode.Type, "BlobDetection", StringComparison.OrdinalIgnoreCase))
                    return null;
                var r = _lastRun.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return r is null ? null : r.Count;
            }
        }

        public int Blob_MaxAllowedBlobs
        {
            get => SelectedBlobDetectionDef()?.MaxAllowedBlobs ?? 0;
            set
            {
                var def = SelectedBlobDetectionDef();
                if (def is null)
                    return;
                var v = Math.Max(0, value);
                if (def.MaxAllowedBlobs == v)
                    return;
                def.MaxAllowedBlobs = v;
                RequestAutoSave();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Blob_MaxBlobCountPreview));
                OnPropertyChanged(nameof(Blob_PassStatus));
                OnPropertyChanged(nameof(Blob_PassColor));
                RefreshPreviews();
            }
        }

        public double Blob_MinBlobDistance
        {
            get => SelectedBlobDetectionDef()?.MinBlobDistance ?? 0.0;
            set
            {
                var def = SelectedBlobDetectionDef();
                if (def is null)
                    return;
                var v = Math.Max(0.0, value);
                if (Math.Abs(def.MinBlobDistance - v) < 1e-6)
                    return;
                def.MinBlobDistance = v;
                RequestAutoSave();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Blob_HasMinDistancePreview));
                OnPropertyChanged(nameof(Blob_MinDistancePreviewGap));
                OnPropertyChanged(nameof(Blob_PassStatus));
                OnPropertyChanged(nameof(Blob_PassColor));
                OnPropertyChanged(nameof(Blob_LastRunMinDistanceText));
                RefreshPreviews();
            }
        }

        public double Blob_MaxBlobWidth
        {
            get => SelectedBlobDetectionDef()?.MaxBlobWidth ?? 0.0;
            set
            {
                var def = SelectedBlobDetectionDef();
                if (def is null)
                    return;
                var v = Math.Max(0.0, value);
                if (Math.Abs(def.MaxBlobWidth - v) < 1e-6)
                    return;
                def.MaxBlobWidth = v;
                RequestAutoSave();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Blob_HasMaxSpecPreview));
                OnPropertyChanged(nameof(Blob_MaxSpecPreviewWidth));
                OnPropertyChanged(nameof(Blob_MaxSpecPreviewHeight));
                OnPropertyChanged(nameof(Blob_PassStatus));
                OnPropertyChanged(nameof(Blob_PassColor));
                OnPropertyChanged(nameof(Blob_LastRunMaxDimensionsText));
                OnPropertyChanged(nameof(Blob_MaxSpecPreviewText));
                RefreshPreviews();
            }
        }

        public double Blob_MaxBlobLength
        {
            get => SelectedBlobDetectionDef()?.MaxBlobLength ?? 0.0;
            set
            {
                var def = SelectedBlobDetectionDef();
                if (def is null)
                    return;
                var v = Math.Max(0.0, value);
                if (Math.Abs(def.MaxBlobLength - v) < 1e-6)
                    return;
                def.MaxBlobLength = v;
                RequestAutoSave();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Blob_HasMaxSpecPreview));
                OnPropertyChanged(nameof(Blob_MaxSpecPreviewWidth));
                OnPropertyChanged(nameof(Blob_MaxSpecPreviewHeight));
                OnPropertyChanged(nameof(Blob_PassStatus));
                OnPropertyChanged(nameof(Blob_PassColor));
                OnPropertyChanged(nameof(Blob_LastRunMaxDimensionsText));
                OnPropertyChanged(nameof(Blob_MaxSpecPreviewText));
                RefreshPreviews();
            }
        }

        public double Blob_MinAreaPreviewSide => CalculateAreaPreviewSide(Blob_MinBlobArea);
        public double Blob_MinAreaPreviewHeight => Blob_MinAreaPreviewSide;
        public double Blob_MaxAreaPreviewSide => CalculateAreaPreviewSide(Blob_MaxBlobArea);
        public double Blob_MaxAreaPreviewHeight => Blob_MaxAreaPreviewSide;
        public string Blob_MinAreaPreviewText => GetEquivalentCircleAreaText("MinA", Blob_MinBlobArea);
        public string Blob_MaxAreaPreviewText => GetEquivalentCircleAreaText("MaxA", Blob_MaxBlobArea);
        public string Blob_MaxSpecPreviewText => $"Tối đa {Blob_MaxBlobWidth:0.##} × {Blob_MaxBlobLength:0.##} {Blob_DistanceUnitText}";
        public string Blob_MaxBlobCountPreview
        {
            get
            {
                var count = Blob_MaxAllowedBlobs <= 0 ? 5 : Math.Clamp(Blob_MaxAllowedBlobs, 1, 12);
                var glyphs = string.Join(" ", Enumerable.Repeat("●", count));
                var detail = Blob_MaxAllowedBlobs <= 0 ? "không giới hạn" : $"tối đa {Blob_MaxAllowedBlobs}";
                return $"{glyphs}  ({detail})";
            }
        }

        private static string GetEquivalentCircleAreaText(string label, int area)
        {
            var diameter = 2 * Math.Sqrt(Math.Max(0, area) / Math.PI);
            return $"{label} {area:N0} px² ≈ Ø{diameter:0.#} px";
        }

        private double GetMmPreviewSide(double millimeters)
        {
            if (millimeters <= 0) return 1;
            var pixelsPerMm = _config?.PixelsPerMm > 0 ? _config.PixelsPerMm : 1.0;
            return Math.Clamp(millimeters * pixelsPerMm * 0.12, 2, 88);
        }

        public string Blob_DistanceUnitText
        {
            get
            {
                if (_config is not null && _config.PixelsPerMm > 0 && Math.Abs(_config.PixelsPerMm - 1.0) > 1e-6)
                    return "mm";
                return "px";
            }
        }

        public string Blob_LastRunMinDistanceText
        {
            get
            {
                if (_lastRun is null || SelectedNode is null)
                    return "-";
                var r = _lastRun.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (r is null || !r.MeasuredMinDistance.HasValue)
                    return "-";
                return $"{r.MeasuredMinDistance.Value:0.##} {Blob_DistanceUnitText}";
            }
        }

        public string Blob_LastRunMaxDimensionsText
        {
            get
            {
                if (_lastRun is null || SelectedNode is null)
                    return "-";
                var r = _lastRun.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (r is null || !r.MeasuredMaxWidth.HasValue || !r.MeasuredMaxLength.HasValue)
                    return "-";
                return $"{r.MeasuredMaxWidth.Value:0.##} x {r.MeasuredMaxLength.Value:0.##} {Blob_DistanceUnitText}";
            }
        }

        public string Blob_PassStatus
        {
            get
            {
                var def = SelectedBlobDetectionDef();
                if (def is null || _lastRun is null || SelectedNode is null)
                    return "-";
                var r = _lastRun.BlobDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                if (r is null)
                    return "-";
                return r.Pass ? "OK" : "NG";
            }
        }

        public Brush Blob_PassColor
        {
            get
            {
                var status = Blob_PassStatus;
                if (status == "OK") return Brushes.LimeGreen;
                if (status == "NG") return Brushes.Crimson;
                return Brushes.Gray;
            }
        }
    }
}
