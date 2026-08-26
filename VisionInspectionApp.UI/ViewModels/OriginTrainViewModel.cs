using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
        private const int MaxHistoryStates = 10;
        private Mat _rawFullMat;
        private Mat _globalPreprocessedMat;
        private Mat? _eraserMaskMat; // 255 = keep edge, 0 = erased edge
        private Stack<Mat> _undoStack = new();
        private Stack<Mat> _redoStack = new();

        private readonly PointDefinition _originDef;
        private readonly string _workingDir;

        [ObservableProperty]
        private BitmapSource? _fullPreviewImage;

        [ObservableProperty]
        private BitmapSource? _generatedTemplateImage;

        [ObservableProperty]
        private ObservableCollection<OverlayItem> _overlayItems = new();

        [ObservableProperty]
        private string _activeRoiLabel = "Origin T";

        [ObservableProperty]
        private bool _isEraserActive;

        [ObservableProperty]
        private int _eraserBrushSize = 10;

        [ObservableProperty]
        private bool _autoThresh = true;

        [ObservableProperty]
        private int _edgeThreshold = 19;

        [ObservableProperty]
        private int _lengthThreshold = 13;

        [ObservableProperty]
        private int _maxPyramidLayers = 6;

        [ObservableProperty]
        private bool _lockOriginCenter = true;

        [ObservableProperty]
        private double _originX;

        [ObservableProperty]
        private double _originY;

        [ObservableProperty]
        private bool _isPartGraph = true;

        [ObservableProperty]
        private bool _isFullGraph;

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

        public ICommand TrainCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public ICommand RefreshCurrentImageCommand { get; }
        public ICommand OkCommand { get; }
        public ICommand RoiEditedCommand { get; }

        public event Action? RequestCloseDialog;

        private Func<Mat>? _refreshImageFunc;

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
            _maxPyramidLayers = originDef.MvpMaxPyramidLayers > 0 ? originDef.MvpMaxPyramidLayers : 6;
            _lockOriginCenter = originDef.MvpLockOriginCenter;

            _minScore = originDef.MinScore > 0 ? originDef.MinScore : 0.6;
            _minAngle = originDef.MinAngle;
            _maxAngle = originDef.MaxAngle;
            _angleStep = originDef.AngleStep > 0 ? originDef.AngleStep : 1.0;

            if (originDef.MvpDetectionRoiMode == DetectionRoiMode.FullGraph)
            {
                _isFullGraph = true;
                _isPartGraph = false;
            }
            else
            {
                _isPartGraph = true;
                _isFullGraph = false;
            }

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

            // Init Eraser mask
            InitEraserMask();

            TrainCommand = new RelayCommand(ExecuteTrain);
            SaveCommand = new RelayCommand(ExecuteSave);
            UndoCommand = new RelayCommand(ExecuteUndo);
            RedoCommand = new RelayCommand(ExecuteRedo);
            RefreshCurrentImageCommand = new RelayCommand(ExecuteRefreshImage);
            OkCommand = new RelayCommand(ExecuteOk);
            RoiEditedCommand = new RelayCommand<RoiSelection>(OnRoiEdited);

            RefreshOverlayItems();
            ExecuteTrain();
        }

        private void InitEraserMask()
        {
            _eraserMaskMat?.Dispose();
            _eraserMaskMat = new Mat(_rawFullMat.Height, _rawFullMat.Width, MatType.CV_8UC1, Scalar.All(255));
            if (_originDef.MvpEraserMask != null && _originDef.MvpEraserMask.Length > 0)
            {
                try
                {
                    using var decoded = Cv2.ImDecode(_originDef.MvpEraserMask, ImreadModes.Grayscale);
                    if (decoded != null && !decoded.Empty() && decoded.Width == _rawFullMat.Width && decoded.Height == _rawFullMat.Height)
                    {
                        decoded.CopyTo(_eraserMaskMat);
                    }
                }
                catch
                {
                }
            }
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

        private void RefreshOverlayItems()
        {
            var list = new ObservableCollection<OverlayItem>();
            list.Add(new OverlayRectItem
            {
                X = (int)RoiX,
                Y = (int)RoiY,
                Width = (int)RoiWidth,
                Height = (int)RoiHeight,
                Angle = RoiAngle,
                Stroke = Brushes.DeepSkyBlue,
                Label = "Origin T"
            });
            OverlayItems = list;
        }

        public void ApplyEraserStroke(OpenCvSharp.Point startPt, OpenCvSharp.Point endPt)
        {
            if (_eraserMaskMat is null || _eraserMaskMat.Empty()) return;

            SaveUndoState();
            var radius = Math.Max(1, EraserBrushSize);
            Cv2.Line(_eraserMaskMat, startPt, endPt, Scalar.All(0), radius * 2, LineTypes.Link8);
            ExecuteTrain();
        }

        private void SaveUndoState()
        {
            if (_eraserMaskMat is not null)
            {
                TrimHistory(_undoStack, MaxHistoryStates - 1);
                _undoStack.Push(_eraserMaskMat.Clone());
                DisposeHistory(_redoStack);
            }
        }

        private static void TrimHistory(Stack<Mat> history, int maxStates)
        {
            if (history.Count <= maxStates) return;

            var newestFirst = history.ToArray();
            history.Clear();
            for (var index = Math.Min(maxStates, newestFirst.Length) - 1; index >= 0; index--)
            {
                history.Push(newestFirst[index]);
            }
            for (var index = maxStates; index < newestFirst.Length; index++)
            {
                newestFirst[index].Dispose();
            }
        }

        private static void DisposeHistory(Stack<Mat> history)
        {
            while (history.Count > 0)
            {
                history.Pop().Dispose();
            }
        }

        private void ExecuteUndo()
        {
            if (_undoStack.Count > 0 && _eraserMaskMat is not null)
            {
                TrimHistory(_redoStack, MaxHistoryStates - 1);
                _redoStack.Push(_eraserMaskMat.Clone());
                _eraserMaskMat.Dispose();
                _eraserMaskMat = _undoStack.Pop();
                ExecuteTrain();
            }
        }

        private void ExecuteRedo()
        {
            if (_redoStack.Count > 0 && _eraserMaskMat is not null)
            {
                TrimHistory(_undoStack, MaxHistoryStates - 1);
                _undoStack.Push(_eraserMaskMat.Clone());
                _eraserMaskMat.Dispose();
                _eraserMaskMat = _redoStack.Pop();
                ExecuteTrain();
            }
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
                        InitEraserMask();
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

                using var roiMat = ToolEditorViewModel.ExtractRoiPatch(_rawFullMat, curRoi);
                if (roiMat.Empty() || roiMat.Width <= 0 || roiMat.Height <= 0)
                {
                    FullPreviewImage = _rawFullMat.ToBitmapSourceForDisplay();
                    return;
                }

                using var roiEraser = _eraserMaskMat != null ? ToolEditorViewModel.ExtractRoiPatch(_eraserMaskMat, curRoi) : null;

                // Extract contours with MvpShapeTrainer
                var contours = MvpShapeTrainer.ExtractContours(roiMat, EdgeThreshold, LengthThreshold, roiEraser);

                // Render green overlay on full image
                using var fullBgr = _rawFullMat.Channels() == 3 ? _rawFullMat.Clone() : _rawFullMat.CvtColor(ColorConversionCodes.GRAY2BGR);

                var rx = (int)Math.Clamp(RoiX, 0, _rawFullMat.Width - 1);
                var ry = (int)Math.Clamp(RoiY, 0, _rawFullMat.Height - 1);
                
                // Shift contours to full image space
                var shiftedContours = new List<OpenCvSharp.Point[]>();
                foreach (var c in contours)
                {
                    var pts = new OpenCvSharp.Point[c.Length];
                    for (int i = 0; i < c.Length; i++)
                    {
                        pts[i] = new OpenCvSharp.Point(c[i].X + rx, c[i].Y + ry);
                    }
                    shiftedContours.Add(pts);
                }

                // Render green contours
                Cv2.DrawContours(fullBgr, shiftedContours, -1, Scalar.FromRgb(0, 255, 0), 1, LineTypes.AntiAlias);

                // Render eraser mask overlay if active
                if (_eraserMaskMat is not null)
                {
                    using var invMask = new Mat();
                    Cv2.BitwiseNot(_eraserMaskMat, invMask);
                    fullBgr.SetTo(new Scalar(0, 0, 180), invMask); // Translucent Red for erased areas
                }

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
            _originDef.MvpDetectionRoiMode = IsFullGraph ? DetectionRoiMode.FullGraph : DetectionRoiMode.PartGraph;

            _originDef.MinScore = MinScore;
            _originDef.MinAngle = MinAngle;
            _originDef.MaxAngle = MaxAngle;
            _originDef.AngleStep = AngleStep;
            _originDef.WorldPosition = new Point2dModel { X = OriginX, Y = OriginY };

            if (IsFullGraph || _originDef.SearchRoi == null || _originDef.SearchRoi.Width <= 0 || _originDef.SearchRoi.Height <= 0 
                || _originDef.SearchRoi.X + _originDef.SearchRoi.Width > _rawFullMat.Width 
                || _originDef.SearchRoi.Y + _originDef.SearchRoi.Height > _rawFullMat.Height)
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

            // Encode Eraser Mask
            if (_eraserMaskMat is not null)
            {
                Cv2.ImEncode(".png", _eraserMaskMat, out var buf);
                _originDef.MvpEraserMask = buf;
            }

            // Crop & Save template image from Image 1 (Global Preprocess only)
            // This file serves as the "base" that gets local-preprocessed at runtime
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
            _originDef.TemplateImageFile = "origin.png";

            // Train ShapeModel from Image 2 (_rawFullMat = tool input after local preprocess)
            // This matches the actual runtime pipeline: origin.png → PreprocessTemplateForMatch(localPre) = Image 2
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
            DisposeHistory(_undoStack);
            DisposeHistory(_redoStack);
            _eraserMaskMat?.Dispose();
            _eraserMaskMat = null;
            _rawFullMat?.Dispose();
            _globalPreprocessedMat?.Dispose();
            FullPreviewImage = null;
            GeneratedTemplateImage = null;
        }
    }
}
