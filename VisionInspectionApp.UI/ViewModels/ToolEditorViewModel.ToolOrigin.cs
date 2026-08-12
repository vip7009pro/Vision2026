using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Models;

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
                    if (_config.Origin.OriginAlgorithm == value) return;
                    _config.Origin.OriginAlgorithm = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsOriginShapePyramid));
                    RefreshPreviews();
                    RequestAutoSave();
                }
            }
        }

        public double Origin_MinScore
        {
            get => _config?.Origin?.MinScore ?? 0.6;
            set
            {
                if (_config?.Origin != null)
                {
                    var v = Math.Clamp(value, 0.0, 1.0);
                    if (Math.Abs(_config.Origin.MinScore - v) < 0.000001) return;
                    _config.Origin.MinScore = v;
                    _config.Origin.MatchScoreThreshold = v;
                    OnPropertyChanged();
                    RefreshPreviews();
                    RequestAutoSave();
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
                    if (Math.Abs(_config.Origin.MinAngle - value) < 0.000001) return;
                    _config.Origin.MinAngle = value;
                    OnPropertyChanged();
                    RefreshPreviews();
                    RequestAutoSave();
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
                    if (Math.Abs(_config.Origin.MaxAngle - value) < 0.000001) return;
                    _config.Origin.MaxAngle = value;
                    OnPropertyChanged();
                    RefreshPreviews();
                    RequestAutoSave();
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
                    if (Math.Abs(_config.Origin.AngleStep - value) < 0.000001) return;
                    _config.Origin.AngleStep = value;
                    OnPropertyChanged();
                    RefreshPreviews();
                    RequestAutoSave();
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
                    if (_config.Origin.EdgeThresholdMin == value) return;
                    _config.Origin.EdgeThresholdMin = value;
                    OnPropertyChanged();
                    RefreshPreviews();
                    RequestAutoSave();
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
                    if (_config.Origin.EdgeThresholdMax == value) return;
                    _config.Origin.EdgeThresholdMax = value;
                    OnPropertyChanged();
                    RefreshPreviews();
                    RequestAutoSave();
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
        public ICommand? Origin_OpenTrainWindowCommand { get; internal set; }

        public void Origin_TeachTemplate()
        {
            if (_config?.Origin == null) return;
            var roi = _config.Origin.TemplateRoi;
            if (roi.Width <= 0 || roi.Height <= 0) return;

            _config.Origin.WorldPosition = RoiCenterToWorld(roi);
            TrySaveTemplateImage("origin", roi, isOrigin: true, pointName: null);

            RefreshOriginTemplatePreview();
            RefreshPreviews();
            RequestAutoSave();
        }

        private Mat GetCurrentWorkingImageSnapshot()
        {
            var rawSnap = _sharedImage.GetSnapshot();
            if (rawSnap != null && !rawSnap.Empty())
            {
                return rawSnap;
            }

            if (_config != null && _config.ImageSources.Count > 0)
            {
                var imgSourceNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "ImageSource", StringComparison.OrdinalIgnoreCase));
                var imgSourceDef = _config.ImageSources.FirstOrDefault(x => string.Equals(x.Name, imgSourceNode?.RefName, StringComparison.OrdinalIgnoreCase)) ?? _config.ImageSources.FirstOrDefault();
                if (imgSourceDef != null)
                {
                    var cached = GetImageSourceCache(imgSourceDef.Name);
                    if (cached != null && !cached.Empty())
                    {
                        _sharedImage.SetImage(cached);
                        return cached;
                    }

                    var loaded = LoadImageFromSourceForPreview(imgSourceDef);
                    if (loaded != null && !loaded.Empty())
                    {
                        _sharedImage.SetImage(loaded);
                        return loaded;
                    }
                }
            }

            return new Mat(480, 640, MatType.CV_8UC1, Scalar.All(128));
        }

        public void OpenTrainTemplateWindow()
        {
            if (_config?.Origin == null) return;

            using var snap = GetCurrentWorkingImageSnapshot();
            var toolNode = Nodes.FirstOrDefault(n => string.Equals(n.Type, "Origin", StringComparison.OrdinalIgnoreCase));
            using var prepSnap = toolNode != null ? ResolveToolImageForPreview(snap, toolNode) : snap.Clone();
            using var globalPrepSnap = _preprocessor.Run(snap, _config.Preprocess);

            var workingDir = CurrentTempWorkingDir ?? Path.Combine(Path.GetFullPath(_storeOptions.ConfigRootDirectory), ProductCode ?? "");
            var vm = new OriginTrainViewModel(prepSnap, globalPrepSnap, _config.Origin, workingDir, () =>
            {
                using var freshSnap = GetCurrentWorkingImageSnapshot();
                return toolNode != null ? ResolveToolImageForPreview(freshSnap, toolNode) : freshSnap.Clone();
            });
            var win = new Views.OriginTrainWindow(vm)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            if (win.ShowDialog() == true || true)
            {
                OnPropertyChanged(nameof(Origin_Algorithm));
                OnPropertyChanged(nameof(IsOriginShapePyramid));
                RefreshOriginTemplatePreview();
                RefreshPreviews();
                RequestAutoSave();
            }
        }

        public bool IsOriginShapePyramid => Origin_Algorithm == OriginAlgorithm.ShapePyramid || Origin_Algorithm == OriginAlgorithm.MvpShapeMatch || Origin_Algorithm == OriginAlgorithm.MvpShapeMatch2;
    }
}
