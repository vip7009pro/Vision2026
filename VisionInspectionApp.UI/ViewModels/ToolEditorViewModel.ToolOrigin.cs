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
        public ObservableCollection<OriginAlgorithm> AvailableOriginAlgorithms { get; } = new ObservableCollection<OriginAlgorithm>((OriginAlgorithm[])Enum.GetValues(typeof(OriginAlgorithm)));
    
        public OriginAlgorithm Origin_Algorithm
        {
            get => _config?.Origin?.OriginAlgorithm ?? OriginAlgorithm.ShapeBased;
            set
            {
                if (_config?.Origin != null)
                {
                    _config.Origin.OriginAlgorithm = value;
                    OnPropertyChanged();
                }
            }
        }
    
        public double Origin_MinAngle
        {
            get => _config?.Origin?.MinAngle ?? -20.0;
            set
            {
                if (_config?.Origin != null)
                {
                    _config.Origin.MinAngle = value;
                    OnPropertyChanged();
                }
            }
        }
    
        public double Origin_MaxAngle
        {
            get => _config?.Origin?.MaxAngle ?? 20.0;
            set
            {
                if (_config?.Origin != null)
                {
                    _config.Origin.MaxAngle = value;
                    OnPropertyChanged();
                }
            }
        }
    }
}
