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

                // Hoán đổi kích thước SearchRoi theo hướng quét mới để duy trì hình học nhất quán
                if (d.SearchRoi.Width > 0 && d.SearchRoi.Height > 0)
                {
                    var cx = d.SearchRoi.X + d.SearchRoi.Width / 2.0;
                    var cy = d.SearchRoi.Y + d.SearchRoi.Height / 2.0;
                    var oldW = d.SearchRoi.Width;
                    var oldH = d.SearchRoi.Height;
                    d.SearchRoi.Width = oldH;
                    d.SearchRoi.Height = oldW;
                    d.SearchRoi.X = (int)Math.Round(cx - d.SearchRoi.Width / 2.0);
                    d.SearchRoi.Y = (int)Math.Round(cy - d.SearchRoi.Height / 2.0);
                    d.StripLength = d.Orientation == CaliperOrientation.Horizontal ? d.SearchRoi.Width : d.SearchRoi.Height;
                    OnPropertyChanged(nameof(Caliper_StripLength));
                }

                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
                RefreshPreviews();
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
                RefreshPreviews();
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

                // Tự động đồng bộ kích thước cạnh quét của khung Caliper ROI đối xứng quanh tâm
                if (d.SearchRoi.Width > 0 && d.SearchRoi.Height > 0)
                {
                    if (d.Orientation == CaliperOrientation.Horizontal)
                    {
                        if (d.SearchRoi.Width != v)
                        {
                            var cx = d.SearchRoi.X + d.SearchRoi.Width / 2.0;
                            d.SearchRoi.Width = v;
                            d.SearchRoi.X = (int)Math.Round(cx - v / 2.0);
                        }
                    }
                    else
                    {
                        if (d.SearchRoi.Height != v)
                        {
                            var cy = d.SearchRoi.Y + d.SearchRoi.Height / 2.0;
                            d.SearchRoi.Height = v;
                            d.SearchRoi.Y = (int)Math.Round(cy - v / 2.0);
                        }
                    }
                }

                RunFlow();
                RequestAutoSave();
                OnPropertyChanged();
                RefreshPreviews();
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

        public bool Caliper_IsEditingSearchRoi => true;
        public bool Caliper_IsEditingStripRoi => false;

        public void SelectCaliperRoi()
        {
            if (SelectedNode is not null)
            {
                ActiveRoiLabel = $"{SelectedNode.RefName} Cal";
                OnPropertyChanged(nameof(ActiveRoiLabel));
                OnPropertyChanged(nameof(Caliper_IsEditingSearchRoi));
                OnPropertyChanged(nameof(Caliper_IsEditingStripRoi));
                RefreshPreviews();
            }
        }

        public void SelectCaliperSearchRoi() => SelectCaliperRoi();
        public void SelectCaliperStripRoi() => SelectCaliperRoi();
    }
}
