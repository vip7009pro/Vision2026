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
    }
}
