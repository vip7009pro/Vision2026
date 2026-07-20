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
        private CodeDetectionDefinition? SelectedCodeDetectionDef()
        {
            if (_config is null || SelectedNode is null)
                return null;
            if (!string.Equals(SelectedNode.Type, "CodeDetection", StringComparison.OrdinalIgnoreCase))
                return null;
            return _config.CodeDetections.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
