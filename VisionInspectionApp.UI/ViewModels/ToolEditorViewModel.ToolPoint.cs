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
        private PointDefinition? SelectedPointDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "Point", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.Points.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        public PointFindAlgorithm Point_Algorithm
        {
            get => SelectedPointDef()?.Algorithm ?? PointFindAlgorithm.TemplateMatch;
            set
            {
                var def = SelectedPointDef();
                if (def is null)
                    return;
                if (def.Algorithm == value)
                    return;
                def.Algorithm = value;
                def.OriginAlgorithm = value switch
                {
                    PointFindAlgorithm.TemplateMatch => OriginAlgorithm.TemplateMatch,
                    PointFindAlgorithm.FeatureBased => OriginAlgorithm.FeatureBased,
                    _ => def.OriginAlgorithm
                };
                RaiseToolPropertyPanelsChanged();
                RefreshPreviews();
                RequestAutoSave();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPointEdgePointAlgorithm));
            }
        }
    
        public CaliperOrientation Point_Edge_Orientation
        {
            get => SelectedPointDef()?.EdgePoint.Orientation ?? CaliperOrientation.Vertical;
            set
            {
                var def = SelectedPointDef();
                if (def is null)
                    return;
                if (def.EdgePoint.Orientation == value)
                    return;
                def.EdgePoint.Orientation = value;
                RefreshPreviews();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public EdgePolarity Point_Edge_Polarity
        {
            get => SelectedPointDef()?.EdgePoint.Polarity ?? EdgePolarity.Any;
            set
            {
                var def = SelectedPointDef();
                if (def is null)
                    return;
                if (def.EdgePoint.Polarity == value)
                    return;
                def.EdgePoint.Polarity = value;
                RefreshPreviews();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public int Point_Edge_StripCount
        {
            get => SelectedPointDef()?.EdgePoint.StripCount ?? 0;
            set
            {
                var def = SelectedPointDef();
                if (def is null)
                    return;
                var v = Math.Clamp(value, 1, 200);
                if (def.EdgePoint.StripCount == v)
                    return;
                def.EdgePoint.StripCount = v;
                RefreshPreviews();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public int Point_Edge_StripWidth
        {
            get => SelectedPointDef()?.EdgePoint.StripWidth ?? 0;
            set
            {
                var def = SelectedPointDef();
                if (def is null)
                    return;
                var v = Math.Max(1, value);
                if (def.EdgePoint.StripWidth == v)
                    return;
                def.EdgePoint.StripWidth = v;
                RefreshPreviews();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public int Point_Edge_StripLength
        {
            get => SelectedPointDef()?.EdgePoint.StripLength ?? 0;
            set
            {
                var def = SelectedPointDef();
                if (def is null)
                    return;
                var v = Math.Max(3, value);
                if (def.EdgePoint.StripLength == v)
                    return;
                def.EdgePoint.StripLength = v;
                RefreshPreviews();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public double Point_Edge_MinEdgeStrength
        {
            get => SelectedPointDef()?.EdgePoint.MinEdgeStrength ?? 0.0;
            set
            {
                var def = SelectedPointDef();
                if (def is null)
                    return;
                var v = Math.Max(0.0, value);
                if (Math.Abs(def.EdgePoint.MinEdgeStrength - v) < 0.0000001)
                    return;
                def.EdgePoint.MinEdgeStrength = v;
                RefreshPreviews();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public double Point_OffsetX
        {
            get => SelectedPointDef()?.OffsetPx.X ?? 0.0;
            set
            {
                var def = SelectedPointDef();
                if (def is null)
                    return;
                if (Math.Abs(def.OffsetPx.X - value) < 0.0000001)
                    return;
                def.OffsetPx.X = value;
                RefreshPreviews();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    
        public double Point_OffsetY
        {
            get => SelectedPointDef()?.OffsetPx.Y ?? 0.0;
            set
            {
                var def = SelectedPointDef();
                if (def is null)
                    return;
                if (Math.Abs(def.OffsetPx.Y - value) < 0.0000001)
                    return;
                def.OffsetPx.Y = value;
                RefreshPreviews();
                RequestAutoSave();
                OnPropertyChanged();
            }
        }
    }
}
