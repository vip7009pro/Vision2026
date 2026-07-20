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
        public string? EdgePair_RefA
        {
            get => SelectedEdgePairDef()?.RefA;
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                var def = SelectedEdgePairDef();
                if (def is null)
                    return;
                if (string.Equals(def.RefA ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
                    return;
                def.RefA = value ?? string.Empty;
                SyncInputEdgeForEdgePairPort("E1", value);
                OnPropertyChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public string? EdgePair_RefB
        {
            get => SelectedEdgePairDef()?.RefB;
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                var def = SelectedEdgePairDef();
                if (def is null)
                    return;
                if (string.Equals(def.RefB ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
                    return;
                def.RefB = value ?? string.Empty;
                SyncInputEdgeForEdgePairPort("E2", value);
                OnPropertyChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        private EdgePairDefinition? SelectedEdgePairDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "EdgePair", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.EdgePairs.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        private EdgePairDetectDefinition? SelectedEdgePairDetectDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "EdgePairDetect", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.EdgePairDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        public CaliperOrientation Epd_Orientation
        {
            get => SelectedEdgePairDetectDef()?.Orientation ?? CaliperOrientation.Vertical;
            set
            {
                var d = SelectedEdgePairDetectDef();
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
    
        public EdgePolarity Epd_Polarity
        {
            get => SelectedEdgePairDetectDef()?.Polarity ?? EdgePolarity.Any;
            set
            {
                var d = SelectedEdgePairDetectDef();
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
    
        public int Epd_StripCount
        {
            get => SelectedEdgePairDetectDef()?.StripCount ?? 0;
            set
            {
                var d = SelectedEdgePairDetectDef();
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
    
        public int Epd_StripWidth
        {
            get => SelectedEdgePairDetectDef()?.StripWidth ?? 0;
            set
            {
                var d = SelectedEdgePairDetectDef();
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
    
        public int Epd_StripLength
        {
            get => SelectedEdgePairDetectDef()?.StripLength ?? 0;
            set
            {
                var d = SelectedEdgePairDetectDef();
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
    
        public double Epd_MinEdgeStrength
        {
            get => SelectedEdgePairDetectDef()?.MinEdgeStrength ?? 0.0;
            set
            {
                var d = SelectedEdgePairDetectDef();
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
    
        public int Epd_MinEdgeSeparationPx
        {
            get => SelectedEdgePairDetectDef()?.MinEdgeSeparationPx ?? 0;
            set
            {
                var d = SelectedEdgePairDetectDef();
                if (d is null)
                    return;
                var v = Math.Max(0, value);
                if (d.MinEdgeSeparationPx == v)
                    return;
                d.MinEdgeSeparationPx = v;
                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    }
}
