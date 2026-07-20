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
        private CaliperDefinition? SelectedCaliperDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "Caliper", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.Calipers.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        public CaliperOrientation Caliper_Orientation
        {
            get => SelectedCaliperDef()?.Orientation ?? CaliperOrientation.Vertical;
            set
            {
                var d = SelectedCaliperDef();
                if (d is null)
                    return;
                if (d.Orientation == value)
                    return;
                d.Orientation = value;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public EdgePolarity Caliper_Polarity
        {
            get => SelectedCaliperDef()?.Polarity ?? EdgePolarity.Any;
            set
            {
                var d = SelectedCaliperDef();
                if (d is null)
                    return;
                if (d.Polarity == value)
                    return;
                d.Polarity = value;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public int Caliper_StripCount
        {
            get => SelectedCaliperDef()?.StripCount ?? 0;
            set
            {
                var d = SelectedCaliperDef();
                if (d is null)
                    return;
                var v = Math.Clamp(value, 1, 200);
                if (d.StripCount == v)
                    return;
                d.StripCount = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public int Caliper_StripWidth
        {
            get => SelectedCaliperDef()?.StripWidth ?? 0;
            set
            {
                var d = SelectedCaliperDef();
                if (d is null)
                    return;
                var v = Math.Max(1, value);
                if (d.StripWidth == v)
                    return;
                d.StripWidth = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public int Caliper_StripLength
        {
            get => SelectedCaliperDef()?.StripLength ?? 0;
            set
            {
                var d = SelectedCaliperDef();
                if (d is null)
                    return;
                var v = Math.Max(3, value);
                if (d.StripLength == v)
                    return;
                d.StripLength = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public double Caliper_MinEdgeStrength
        {
            get => SelectedCaliperDef()?.MinEdgeStrength ?? 0.0;
            set
            {
                var d = SelectedCaliperDef();
                if (d is null)
                    return;
                var v = Math.Max(0.0, value);
                if (Math.Abs(d.MinEdgeStrength - v) < 0.0000001)
                    return;
                d.MinEdgeStrength = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public bool? Caliper_LastRunFound => _lastRun?.Calipers.FirstOrDefault(x => string.Equals(x.Name, SelectedNode?.RefName, StringComparison.OrdinalIgnoreCase))?.Found;
        public double? Caliper_LastRunAvgStrength => _lastRun?.Calipers.FirstOrDefault(x => string.Equals(x.Name, SelectedNode?.RefName, StringComparison.OrdinalIgnoreCase))?.AvgStrength;
    }
}
