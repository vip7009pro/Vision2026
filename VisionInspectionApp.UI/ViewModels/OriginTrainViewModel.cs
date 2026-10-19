using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels
{
    public sealed partial class OriginTrainViewModel : ObservableObject, IDisposable
    {
        private Mat _rawFullMat;
        private Mat _globalPreprocessedMat;
        private readonly PointDefinition _originDef;
        private readonly string _workingDir;
        private Func<Mat>? _refreshImageFunc;

        [ObservableProperty]
        private BitmapSource? _fullPreviewImage;

        [ObservableProperty]
        private BitmapSource? _generatedTemplateImage;

        [ObservableProperty]
        private ObservableCollection<OverlayItem> _overlayItems = new();

        [ObservableProperty]
        private string _activeRoiLabel = "Origin T";

        [ObservableProperty]
        private bool _autoThresh = true;

        [ObservableProperty]
        private int _edgeThreshold = 19;

        [ObservableProperty]
        private int _lengthThreshold = 13;

        [ObservableProperty]
        private int _maxPyramidLayers = 4;

        [ObservableProperty]
        private bool _lockOriginCenter = true;

        [ObservableProperty]
        private double _originX;

        [ObservableProperty]
        private double _originY;

        [ObservableProperty]
        private double _minScore = 0.6;

        [ObservableProperty]
        private double _minAngle = -20.0;

        [ObservableProperty]
        private double _maxAngle = 20.0;

        [ObservableProperty]
        private double _angleStep = 1.0;

        // Template ROI coordinates
        [ObservableProperty]
        private double _roiX;

        [ObservableProperty]
        private double _roiY;

        [ObservableProperty]
        private double _roiWidth;

        [ObservableProperty]
        private double _roiHeight;

        [ObservableProperty]
        private double _roiAngle;

        // Information badges
        [ObservableProperty]
        private string _templateSizeText = "0 × 0 px";

        [ObservableProperty]
        private string _contourStatsText = "0 viền • 0 điểm";

        [ObservableProperty]
        private string _roiSummary = "Chưa xác định";

        public ICommand TrainCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand RefreshCurrentImageCommand { get; }
        public ICommand CenterRoiCommand { get; }
        public ICommand OkCommand { get; }
        public ICommand RoiEditedCommand { get; }

        public event Action? RequestCloseDialog;

        public OriginTrainViewModel(Mat inputMat, PointDefinition originDef, string workingDir)
            : this(inputMat, inputMat, originDef, workingDir, null)
        {
        }

        public OriginTrainViewModel(Mat inputMat, Mat globalPreprocessedMat, PointDefinition originDef, string workingDir, Func<Mat>? refreshImageFunc = null)
        {
            _rawFullMat = inputMat.Clone();
            _globalPreprocessedMat = globalPreprocessedMat?.Clone() ?? inputMat.Clone();
            _originDef = originDef ?? new PointDefinition();
            _workingDir = workingDir;
            _refreshImageFunc = refreshImageFunc;

            // Load initial values from originDef
            _autoThresh = originDef.MvpAutoThresh;
            _edgeThreshold = originDef.MvpEdgeThreshold > 0 ? originDef.MvpEdgeThreshold : 19;
            _lengthThreshold = originDef.MvpLengthThreshold > 0 ? originDef.MvpLengthThreshold : 13;
            _maxPyramidLayers = originDef.MvpMaxPyramidLayers > 0 ? originDef.MvpMaxPyramidLayers : 4;
            _lockOriginCenter = originDef.MvpLockOriginCenter;

            _minScore = originDef.MinScore > 0 ? originDef.MinScore : 0.6;
            _minAngle = originDef.MinAngle;
            _maxAngle = originDef.MaxAngle;
            _angleStep = originDef.AngleStep > 0 ? originDef.AngleStep : 1.0;

            var roi = originDef.TemplateRoi;
            if (roi.Width <= 0 || roi.Height <= 0 ||
                roi.X < 0 || roi.Y < 0 ||
                roi.X >= _rawFullMat.Width || roi.Y >= _rawFullMat.Height ||
                roi.X + roi.Width > _rawFullMat.Width || roi.Y + roi.Height > _rawFullMat.Height)
            {
                var w = Math.Clamp(roi.Width > 0 ? roi.Width : _rawFullMat.Width / 3.0, 10, _rawFullMat.Width);
                var h = Math.Clamp(roi.Height > 0 ? roi.Height : _rawFullMat.Height / 3.0, 10, _rawFullMat.Height);
                _roiX = Math.Max(0, (_rawFullMat.Width - w) / 2.0);
                _roiY = Math.Max(0, (_rawFullMat.Height - h) / 2.0);
                _roiWidth = w;
                _roiHeight = h;
                _roiAngle = 0;
            }
            else
            {
                _roiX = roi.X;
                _roiY = roi.Y;
                _roiWidth = roi.Width;
                _roiHeight = roi.Height;
                _roiAngle = roi.Angle;
            }

            if (originDef.MvpOriginX != 0 || originDef.MvpOriginY != 0)
            {
                _originX = originDef.MvpOriginX;
                _originY = originDef.MvpOriginY;
            }
            else
            {
                _originX = _roiX + _roiWidth / 2.0;
                _originY = _roiY + _roiHeight / 2.0;
            }

            TrainCommand = new RelayCommand(ExecuteTrain);
            SaveCommand = new RelayCommand(ExecuteSave);
            RefreshCurrentImageCommand = new RelayCommand(ExecuteRefreshImage);
            CenterRoiCommand = new RelayCommand(ExecuteCenterRoi);
            OkCommand = new RelayCommand(ExecuteOk);
            RoiEditedCommand = new RelayCommand<RoiSelection>(OnRoiEdited);

            RefreshOverlayItems();
            ExecuteTrain();
        }

        private void OnRoiEdited(RoiSelection? sel)
        {
            if (sel is null || sel.Roi is null) return;
            UpdateRoi(sel.Roi.X, sel.Roi.Y, sel.Roi.Width, sel.Roi.Height, sel.Roi.Angle);
        }

        public void UpdateRoi(double x, double y, double w, double h, double angle = 0)
        {
            RoiX = Math.Max(0, x);
            RoiY = Math.Max(0, y);
            RoiWidth = Math.Max(10, w);
            RoiHeight = Math.Max(10, h);
            RoiAngle = angle;

            if (LockOriginCenter)
            {
                OriginX = Math.Round(RoiX + RoiWidth / 2.0, 3);
                OriginY = Math.Round(RoiY + RoiHeight / 2.0, 3);
            }

            RefreshOverlayItems();
            ExecuteTrain();
        }

        private void ExecuteCenterRoi()
        {
            if (_rawFullMat == null || _rawFullMat.Empty()) return;
            var w = Math.Clamp(RoiWidth > 0 ? RoiWidth : _rawFullMat.Width / 3.0, 10, _rawFullMat.Width);
            var h = Math.Clamp(RoiHeight > 0 ? RoiHeight : _rawFullMat.Height / 3.0, 10, _rawFullMat.Height);
            var x = Math.Max(0, (_rawFullMat.Width - w) / 2.0);
            var y = Math.Max(0, (_rawFullMat.Height - h) / 2.0);
            UpdateRoi(x, y, w, h, 0);
        }

        private void RefreshOverlayItems()
        {
            var list = new ObservableCollection<OverlayItem>
            {
                new OverlayRectItem
                {
                    X = (int)RoiX,
                    Y = (int)RoiY,
                    Width = (int)RoiWidth,
                    Height = (int)RoiHeight,
                    Angle = RoiAngle,
                    Stroke = Brushes.DeepSkyBlue,
                    Label = "Origin T"
                }
            };
            OverlayItems = list;
        }

        private void ExecuteRefreshImage()
        {
            if (_refreshImageFunc != null)
            {
                try
                {
                    var freshMat = _refreshImageFunc();
                    if (freshMat != null && !freshMat.Empty())
                    {
                        _rawFullMat?.Dispose();
                        _globalPreprocessedMat?.Dispose();
                        _rawFullMat = freshMat.Clone();
                        _globalPreprocessedMat = freshMat.Clone();
                        freshMat.Dispose();
                    }
                }
                catch { }
            }
            ExecuteTrain();
        }

        partial void OnEdgeThresholdChanged(int value) => ExecuteTrain();
        partial void OnLengthThresholdChanged(int value) => ExecuteTrain();
        partial void OnAutoThreshChanged(bool value) => ExecuteTrain();

        public void ExecuteTrain()
        {
            if (_rawFullMat is null || _rawFullMat.Empty()) return;

            try
            {
                var curRoi = new Roi
                {
                    X = (int)Math.Clamp(RoiX, 0, Math.Max(0, _rawFullMat.Width - 10)),
                    Y = (int)Math.Clamp(RoiY, 0, Math.Max(0, _rawFullMat.Height - 10)),
                    Width = (int)Math.Clamp(RoiWidth, 10, _rawFullMat.Width),
                    Height = (int)Math.Clamp(RoiHeight, 10, _rawFullMat.Height),
                    Angle = RoiAngle
                };

                RoiSummary = $"X: {curRoi.X:F0}, Y: {curRoi.Y:F0} | W: {curRoi.Width:F0}, H: {curRoi.Height:F0} | Góc: {curRoi.Angle:F1}°";
                TemplateSizeText = $"{curRoi.Width} × {curRoi.Height} px";

                using var roiMat = ToolEditorViewModel.ExtractRoiPatch(_rawFullMat, curRoi);
                if (roiMat.Empty() || roiMat.Width <= 0 || roiMat.Height <= 0)
                {
                    FullPreviewImage = _rawFullMat.ToBitmapSourceForDisplay();
                    ContourStatsText = "0 viền • 0 điểm";
                    return;
                }

                // Extract contours with MvpShapeTrainer
                var contours = MvpShapeTrainer.ExtractContours(roiMat, EdgeThreshold, LengthThreshold, null);
                int totalPoints = contours.Sum(c => c.Length);
                ContourStatsText = $"{contours.Count} viền • {totalPoints} điểm";

                // Render green overlay on full image
                using var fullBgr = _rawFullMat.Channels() == 3 ? _rawFullMat.Clone() : _rawFullMat.CvtColor(ColorConversionCodes.GRAY2BGR);

                var roiCenterX = curRoi.X + curRoi.Width / 2.0;
                var roiCenterY = curRoi.Y + curRoi.Height / 2.0;

                // Restore deskewed contour points to the rotated ROI's position in the source image.
                var shiftedContours = new List<OpenCvSharp.Point[]>();
                foreach (var c in contours)
                {
                    var pts = new OpenCvSharp.Point[c.Length];
                    for (int i = 0; i < c.Length; i++)
                    {
                        pts[i] = TransformTemplatePointToImage(c[i].X, c[i].Y, roiCenterX, roiCenterY, curRoi.Width, curRoi.Height, curRoi.Angle);
                    }
                    shiftedContours.Add(pts);
                }

                // Render green contours
                Cv2.DrawContours(fullBgr, shiftedContours, -1, Scalar.FromRgb(0, 255, 0), 1, LineTypes.AntiAlias);

                FullPreviewImage = fullBgr.ToBitmapSourceForDisplay();

                // Render Generated Template (cropped ROI patch with green contours)
                using var generatedBgr = MvpShapeTrainer.RenderContourOverlay(roiMat, contours);
                GeneratedTemplateImage = generatedBgr.ToBitmapSourceForDisplay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExecuteTrain exception: {ex.Message}");
            }
        }

        public static OpenCvSharp.Point TransformTemplatePointToImage(
            double templateX, double templateY, double roiCenterX, double roiCenterY,
            double roiWidth, double roiHeight, double angleDegrees)
        {
            var dx = templateX - roiWidth / 2.0;
            var dy = templateY - roiHeight / 2.0;
            var radians = angleDegrees * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            var imageX = roiCenterX + cos * dx - sin * dy;
            var imageY = roiCenterY + sin * dx + cos * dy;
            return new OpenCvSharp.Point((int)Math.Round(imageX), (int)Math.Round(imageY));
        }

        private void ExecuteSave()
        {
            SaveToOriginDefinition();
        }

        private void ExecuteOk()
        {
            SaveToOriginDefinition();
            RequestCloseDialog?.Invoke();
        }

        private void SaveToOriginDefinition()
        {
            _originDef.TemplateRoi = new Roi
            {
                X = (int)RoiX,
                Y = (int)RoiY,
                Width = (int)RoiWidth,
                Height = (int)RoiHeight,
                Angle = RoiAngle
            };

            if (_originDef.OriginAlgorithm != OriginAlgorithm.MvpShapeMatch2 && _originDef.OriginAlgorithm != OriginAlgorithm.ShapePyramid)
            {
                _originDef.OriginAlgorithm = OriginAlgorithm.MvpShapeMatch;
            }
            _originDef.MvpAutoThresh = AutoThresh;
            _originDef.MvpEdgeThreshold = EdgeThreshold;
            _originDef.MvpLengthThreshold = LengthThreshold;
            _originDef.MvpMaxPyramidLayers = MaxPyramidLayers;
            _originDef.MvpLockOriginCenter = LockOriginCenter;
            _originDef.MvpOriginX = OriginX;
            _originDef.MvpOriginY = OriginY;

            _originDef.MinScore = MinScore;
            _originDef.MinAngle = MinAngle;
            _originDef.MaxAngle = MaxAngle;
            _originDef.AngleStep = AngleStep;
            _originDef.WorldPosition = new Point2dModel { X = OriginX, Y = OriginY };

            // Ensure SearchRoi is valid; preserve existing SearchRoi if it has positive dimensions
            if (_originDef.SearchRoi == null || _originDef.SearchRoi.Width <= 0 || _originDef.SearchRoi.Height <= 0)
            {
                _originDef.SearchRoi = new Roi
                {
                    X = 0,
                    Y = 0,
                    Width = _rawFullMat.Width,
                    Height = _rawFullMat.Height,
                    Angle = 0
                };
            }

            OriginMatcher.ClearCache();
            MvpShapeMatch2Engine.ClearCache();

            // Crop & Save template image from Image 1 (Global Preprocess only)
            var templateDir = Path.Combine(_workingDir, "templates");
            Directory.CreateDirectory(templateDir);
            var templateFile = Path.Combine(templateDir, "origin.png");

            var curRoi = _originDef.TemplateRoi;
            var sourceMatForSave = _rawFullMat;
            if (sourceMatForSave == null || sourceMatForSave.Empty())
            {
                return;
            }

            using var crop = ToolEditorViewModel.ExtractRoiPatch(sourceMatForSave, curRoi);
            if (crop.Empty() || crop.Width <= 0 || crop.Height <= 0)
            {
                return;
            }

            using var grayToSave = crop.Channels() == 1 ? crop.Clone() : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
            if (grayToSave.Empty() || grayToSave.Width <= 0 || grayToSave.Height <= 0)
            {
                return;
            }

            Cv2.ImWrite(templateFile, grayToSave);
            _originDef.TemplateImageFile = templateFile;

            // Train ShapeModel from Image 2 (_rawFullMat = tool input after local preprocess)
            using var cropForModel = ToolEditorViewModel.ExtractRoiPatch(_rawFullMat, curRoi);
            if (!cropForModel.Empty() && cropForModel.Width > 0 && cropForModel.Height > 0)
            {
                using var grayForModel = cropForModel.Channels() == 1 ? cropForModel.Clone() : cropForModel.CvtColor(ColorConversionCodes.BGR2GRAY);
                _originDef.ShapeModel = ShapeModelTrainer.Train(grayForModel);
            }
            else
            {
                _originDef.ShapeModel = ShapeModelTrainer.Train(grayToSave);
            }
        }

        public void Dispose()
        {
            _rawFullMat?.Dispose();
            _globalPreprocessedMat?.Dispose();
            FullPreviewImage = null;
            GeneratedTemplateImage = null;
        }
    }
}
