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
        public ICommand SurfaceCompare_SetSearchRoiCommand { get; }
        public ICommand SurfaceCompare_SetTemplateRoiCommand { get; }
    
        private void SurfaceCompare_SetSearchRoi()
        {
            if (SelectedNode is null || !string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
                return;
            ActiveRoiLabel = $"{SelectedNode.RefName} SC";
        }
    
        private void SurfaceCompare_SetTemplateRoi()
        {
            if (SelectedNode is null || !string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
                return;
            ActiveRoiLabel = $"{SelectedNode.RefName} SCT";
        }
    
        private SurfaceCompareDefinition? SelectedSurfaceCompareDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        public int SurfaceCompare_DiffThreshold
        {
            get => SelectedSurfaceCompareDef()?.DiffThreshold ?? 25;
            set
            {
                var def = SelectedSurfaceCompareDef();
                if (def is null)
                    return;
                var v = Math.Clamp(value, 0, 255);
                if (def.DiffThreshold == v)
                    return;
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
                if (def is null)
                    return;
                var v = Math.Clamp(value, 1, 99);
                if (v % 2 == 0)
                    v += 1;
                if (def.MorphKernel == v)
                    return;
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
                if (def is null)
                    return;
                var v = Math.Max(0, value);
                if (def.MinBlobArea == v)
                    return;
                def.MinBlobArea = v;
                if (def.MaxBlobArea < def.MinBlobArea)
                    def.MaxBlobArea = def.MinBlobArea;
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
                if (def is null)
                    return;
                var v = Math.Max(0, value);
                if (v < def.MinBlobArea)
                    v = def.MinBlobArea;
                if (def.MaxBlobArea == v)
                    return;
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
                if (def is null)
                    return;
                var v = Math.Max(0, value);
                if (def.MinCount == v)
                    return;
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
                if (def is null)
                    return;
                var v = Math.Max(0, value);
                if (def.MaxCount == v)
                    return;
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
                if (_lastRun is null || SelectedNode is null)
                    return null;
                if (!string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
                    return null;
                var r = _lastRun.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return r is null ? null : r.Count;
            }
        }
    
        public double? SurfaceCompare_LastRunMaxArea
        {
            get
            {
                if (_lastRun is null || SelectedNode is null)
                    return null;
                if (!string.Equals(SelectedNode.Type, "SurfaceCompare", StringComparison.OrdinalIgnoreCase))
                    return null;
                var r = _lastRun.SurfaceCompares.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
                return r is null ? null : r.MaxArea;
            }
        }
    }
}
