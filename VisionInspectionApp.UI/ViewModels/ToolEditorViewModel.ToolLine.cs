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
        private LinePairDetectionDefinition? SelectedLinePairDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "LinePairDetection", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.LinePairDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        private LineToolDefinition? SelectedLineDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "Line", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.Lines.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        public int Line_Canny1
        {
            get => SelectedLineDef()?.Canny1 ?? 0;
            set
            {
                var l = SelectedLineDef();
                if (l is null)
                    return;
                if (l.Canny1 == value)
                    return;
                l.Canny1 = value;
                RefreshPreviews();
                OnPropertyChanged();
            }
        }
    
        public int Line_Canny2
        {
            get => SelectedLineDef()?.Canny2 ?? 0;
            set
            {
                var l = SelectedLineDef();
                if (l is null)
                    return;
                if (l.Canny2 == value)
                    return;
                l.Canny2 = value;
                RefreshPreviews();
                OnPropertyChanged();
            }
        }
    
        public int Line_HoughThreshold
        {
            get => SelectedLineDef()?.HoughThreshold ?? 0;
            set
            {
                var l = SelectedLineDef();
                if (l is null)
                    return;
                if (l.HoughThreshold == value)
                    return;
                l.HoughThreshold = value;
                RefreshPreviews();
                OnPropertyChanged();
            }
        }
    
        public int Line_MinLineLength
        {
            get => SelectedLineDef()?.MinLineLength ?? 0;
            set
            {
                var l = SelectedLineDef();
                if (l is null)
                    return;
                if (l.MinLineLength == value)
                    return;
                l.MinLineLength = value;
                RefreshPreviews();
                OnPropertyChanged();
            }
        }
    
        public int Line_MaxLineGap
        {
            get => SelectedLineDef()?.MaxLineGap ?? 0;
            set
            {
                var l = SelectedLineDef();
                if (l is null)
                    return;
                if (l.MaxLineGap == value)
                    return;
                l.MaxLineGap = value;
                RefreshPreviews();
                OnPropertyChanged();
            }
        }
    }
}
