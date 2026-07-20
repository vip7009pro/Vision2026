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
        private CircleFinderDefinition? SelectedCircleFinderDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "CircleFinder", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.CircleFinders.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        private DiameterDefinition? SelectedDiameterDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "Diameter", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.Diameters.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        public CircleFindAlgorithm Cf_Algorithm
        {
            get => SelectedCircleFinderDef()?.Algorithm ?? CircleFindAlgorithm.ContourFit;
            set
            {
                var d = SelectedCircleFinderDef();
                if (d is null)
                    return;
                if (d.Algorithm == value)
                    return;
                d.Algorithm = value;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public int Cf_MinRadiusPx
        {
            get => SelectedCircleFinderDef()?.MinRadiusPx ?? 0;
            set
            {
                var d = SelectedCircleFinderDef();
                if (d is null)
                    return;
                var v = Math.Max(0, value);
                if (d.MinRadiusPx == v)
                    return;
                d.MinRadiusPx = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public int Cf_MaxRadiusPx
        {
            get => SelectedCircleFinderDef()?.MaxRadiusPx ?? 0;
            set
            {
                var d = SelectedCircleFinderDef();
                if (d is null)
                    return;
                var v = Math.Max(0, value);
                if (d.MaxRadiusPx == v)
                    return;
                d.MaxRadiusPx = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public double Cf_HoughDp
        {
            get => SelectedCircleFinderDef()?.HoughDp ?? 1.2;
            set
            {
                var d = SelectedCircleFinderDef();
                if (d is null)
                    return;
                var v = Math.Max(0.1, value);
                if (Math.Abs(d.HoughDp - v) < 0.0000001)
                    return;
                d.HoughDp = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public double Cf_HoughMinDistPx
        {
            get => SelectedCircleFinderDef()?.HoughMinDistPx ?? 20;
            set
            {
                var d = SelectedCircleFinderDef();
                if (d is null)
                    return;
                var v = Math.Max(1.0, value);
                if (Math.Abs(d.HoughMinDistPx - v) < 0.0000001)
                    return;
                d.HoughMinDistPx = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public double Cf_HoughParam1
        {
            get => SelectedCircleFinderDef()?.HoughParam1 ?? 120;
            set
            {
                var d = SelectedCircleFinderDef();
                if (d is null)
                    return;
                var v = Math.Max(1.0, value);
                if (Math.Abs(d.HoughParam1 - v) < 0.0000001)
                    return;
                d.HoughParam1 = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public double Cf_HoughParam2
        {
            get => SelectedCircleFinderDef()?.HoughParam2 ?? 30;
            set
            {
                var d = SelectedCircleFinderDef();
                if (d is null)
                    return;
                var v = Math.Max(1.0, value);
                if (Math.Abs(d.HoughParam2 - v) < 0.0000001)
                    return;
                d.HoughParam2 = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public int Cf_Canny1
        {
            get => SelectedCircleFinderDef()?.Canny1 ?? 80;
            set
            {
                var d = SelectedCircleFinderDef();
                if (d is null)
                    return;
                var v = Math.Max(0, value);
                if (d.Canny1 == v)
                    return;
                d.Canny1 = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public int Cf_Canny2
        {
            get => SelectedCircleFinderDef()?.Canny2 ?? 200;
            set
            {
                var d = SelectedCircleFinderDef();
                if (d is null)
                    return;
                var v = Math.Max(0, value);
                if (d.Canny2 == v)
                    return;
                d.Canny2 = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public double Cf_MinCircularity
        {
            get => SelectedCircleFinderDef()?.MinCircularity ?? 0.6;
            set
            {
                var d = SelectedCircleFinderDef();
                if (d is null)
                    return;
                var v = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(d.MinCircularity - v) < 0.0000001)
                    return;
                d.MinCircularity = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public string? Dia_CircleRef
        {
            get => SelectedDiameterDef()?.CircleRef;
            set
            {
                var d = SelectedDiameterDef();
                if (d is null)
                    return;
                if (string.Equals(d.CircleRef, value, StringComparison.OrdinalIgnoreCase))
                    return;
                d.CircleRef = value ?? string.Empty;
                SyncInputEdgeForDiameterPort("C1", value);
                OnPropertyChanged();
                RefreshPreviews();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    }
}
