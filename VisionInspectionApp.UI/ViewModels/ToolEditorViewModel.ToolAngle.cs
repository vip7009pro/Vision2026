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
        private AngleDefinition? SelectedAngleDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "Angle", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.Angles.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    
        public string? Angle_LineA
        {
            get => SelectedAngleDef()?.LineA;
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                var def = SelectedAngleDef();
                if (def is null)
                    return;
                if (string.Equals(def.LineA ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
                    return;
                def.LineA = value ?? string.Empty;
                SyncInputEdgeForAnglePort("L1", value);
                OnPropertyChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    
        public string? Angle_LineB
        {
            get => SelectedAngleDef()?.LineB;
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                var def = SelectedAngleDef();
                if (def is null)
                    return;
                if (string.Equals(def.LineB ?? "", value ?? "", StringComparison.OrdinalIgnoreCase))
                    return;
                def.LineB = value ?? string.Empty;
                SyncInputEdgeForAnglePort("L2", value);
                OnPropertyChanged();
                RefreshPreviews();
                RequestAutoSave();
            }
        }
    }
}
