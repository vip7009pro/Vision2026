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
                    OnPropertyChanged(nameof(IsOriginShapePyramid));
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

        public double Origin_AngleStep
        {
            get => _config?.Origin?.AngleStep ?? 1.0;
            set
            {
                if (_config?.Origin != null)
                {
                    _config.Origin.AngleStep = value;
                    OnPropertyChanged();
                }
            }
        }
        public int Origin_EdgeThresholdMin
        {
            get => _config?.Origin?.EdgeThresholdMin ?? 50;
            set
            {
                if (_config?.Origin != null)
                {
                    _config.Origin.EdgeThresholdMin = value;
                    OnPropertyChanged();
                }
            }
        }

        public int Origin_EdgeThresholdMax
        {
            get => _config?.Origin?.EdgeThresholdMax ?? 150;
            set
            {
                if (_config?.Origin != null)
                {
                    _config.Origin.EdgeThresholdMax = value;
                    OnPropertyChanged();
                }
            }
        }

        [ObservableProperty]
        private BitmapSource? _origin_TemplatePreviewImage;

        public void RefreshOriginTemplatePreview()
        {
            if (_config?.Origin == null || string.IsNullOrWhiteSpace(_config.Origin.TemplateImageFile))
            {
                Origin_TemplatePreviewImage = null;
                return;
            }

            try
            {
                var file = _config.Origin.TemplateImageFile;
                if (!Path.IsPathRooted(file))
                {
                    var templateDir = Path.Combine(CurrentTempWorkingDir ?? Path.Combine(Path.GetFullPath(_storeOptions.ConfigRootDirectory), ProductCode ?? ""), "templates");
                    file = Path.Combine(templateDir, file);
                }

                if (File.Exists(file))
                {
                    using var mat = Cv2.ImRead(file, ImreadModes.Grayscale);
                    if (mat != null && !mat.Empty())
                    {
                        Origin_TemplatePreviewImage = mat.ToBitmapSource();
                        return;
                    }
                }
            }
            catch
            {
            }

            Origin_TemplatePreviewImage = null;
        }

        public ICommand? Origin_TeachTemplateCommand { get; internal set; }

        public void Origin_TeachTemplate()
        {
            if (_config?.Origin == null) return;
            var roi = _config.Origin.TemplateRoi;
            if (roi.Width <= 0 || roi.Height <= 0) return;

            _config.Origin.WorldPosition = RoiCenterToWorld(roi);
            TrySaveTemplateImage("origin", roi, isOrigin: true, pointName: null);

            RefreshOriginTemplatePreview();
            RunFlow();
            RequestAutoSave();
        }


        public bool IsOriginShapePyramid => Origin_Algorithm == OriginAlgorithm.ShapePyramid;
    }
}
