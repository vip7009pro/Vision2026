using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Input;
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
using System.Windows.Media;

namespace VisionInspectionApp.UI.ViewModels;

public enum RoiTeachMode
{
    Search = 0,
    Template = 1
}

public sealed partial class TeachViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly ConfigStoreOptions _storeOptions;
    private readonly ImagePreprocessor _preprocessor;
    private readonly LineDetector _lineDetector;
    private readonly UndoRedoManager _undo;
    private readonly SharedImageContext _sharedImage;

    private Mat? _imageMat;

    public TeachViewModel(IConfigService configService, ConfigStoreOptions storeOptions, ImagePreprocessor preprocessor, LineDetector lineDetector, UndoRedoManager undo, SharedImageContext sharedImage)
    {
        _configService = configService;
        _storeOptions = storeOptions;
        _preprocessor = preprocessor;
        _lineDetector = lineDetector;
        _undo = undo;
        _sharedImage = sharedImage;

        VisionConfig = new VisionConfig { ProductCode = "ProductA" };

        AvailableConfigs = new ObservableCollection<string>();

        Targets = new ObservableCollection<TeachTarget>(Enum.GetValues<TeachTarget>());
        SelectedTarget = TeachTarget.Origin;

        Points = new ObservableCollection<string>();
        Lines = new ObservableCollection<string>();
        Distances = new ObservableCollection<LineDistance>();
        LineToLineDistances = new ObservableCollection<LineToLineDistance>();
        PointToLineDistances = new ObservableCollection<PointToLineDistance>();

        LoadImageCommand = new RelayCommand(LoadImage);
        RoiSelectedCommand = new RelayCommand<Roi?>(OnRoiSelected);
        RoiEditedCommand = new RelayCommand<RoiSelection?>(OnRoiEdited);
        AddPointCommand = new RelayCommand(AddPoint);
        AddLineToolCommand = new RelayCommand(AddLineTool);
        AddDistanceCommand = new RelayCommand(AddDistance);
        UpdateDistanceCommand = new RelayCommand(UpdateDistance);
        AddLineToLineDistanceCommand = new RelayCommand(AddLineToLineDistance);
        UpdateLineToLineDistanceCommand = new RelayCommand(UpdateLineToLineDistance);
        DeleteLineToLineDistanceCommand = new RelayCommand(DeleteLineToLineDistance);
        AddPointToLineDistanceCommand = new RelayCommand(AddPointToLineDistance);
        UpdatePointToLineDistanceCommand = new RelayCommand(UpdatePointToLineDistance);
        DeletePointToLineDistanceCommand = new RelayCommand(DeletePointToLineDistance);
        DeletePointCommand = new RelayCommand(DeletePoint);
        DeleteLineToolCommand = new RelayCommand(DeleteLineTool);
        DeleteDistanceCommand = new RelayCommand(DeleteDistance);
        RefreshConfigsCommand = new RelayCommand(RefreshConfigs);
        LoadConfigCommand = new RelayCommand(LoadConfig);
        NewConfigCommand = new RelayCommand(NewConfig);
        SaveConfigCommand = new RelayCommand(SaveConfig);

        UndoCommand = new RelayCommand(_undo.Undo, () => _undo.CanUndo);
        RedoCommand = new RelayCommand(_undo.Redo, () => _undo.CanRedo);

        _undo.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(UndoRedoManager.CanUndo), StringComparison.Ordinal)
                || string.Equals(e.PropertyName, nameof(UndoRedoManager.CanRedo), StringComparison.Ordinal))
            {
                ((RelayCommand)UndoCommand).NotifyCanExecuteChanged();
                ((RelayCommand)RedoCommand).NotifyCanExecuteChanged();
            }
        };

        RefreshLinesFromConfig();

        RefreshConfigs();
    }

    public VisionConfig VisionConfig { get; }

    [ObservableProperty]
    private string _productCode = "ProductA";

    public ObservableCollection<string> AvailableConfigs { get; }

    [ObservableProperty]
    private string? _selectedConfig;

    partial void OnSelectedConfigChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        ProductCode = value;
    }

    [ObservableProperty]
    private System.Windows.Media.ImageSource? _image;

    [ObservableProperty]
    private bool _isPreprocessPreviewEnabled;

    partial void OnIsPreprocessPreviewEnabledChanged(bool value)
    {
        RefreshDisplayedImage();
        RefreshLinePreview();
    }

    [ObservableProperty]
    private Roi? _lastRoi;

    [ObservableProperty]
    private TeachTarget _selectedTarget;

    public ObservableCollection<RoiTeachMode> RoiModes { get; } = new(Enum.GetValues<RoiTeachMode>());

    [ObservableProperty]
    private RoiTeachMode _selectedRoiMode = RoiTeachMode.Search;

    public ObservableCollection<TeachTarget> Targets { get; }

    [ObservableProperty]
    private string _newPointName = "P1";

    [ObservableProperty]
    private string _newLineToolName = "L1";

    public ObservableCollection<string> Points { get; }

    public ObservableCollection<string> Lines { get; }

    [ObservableProperty]
    private string? _selectedPoint;

    [ObservableProperty]
    private string? _selectedLineTool;

    partial void OnSelectedLineToolChanged(string? value)
    {
        OnPropertyChanged(nameof(LineCanny1));
        OnPropertyChanged(nameof(LineCanny2));
        OnPropertyChanged(nameof(LineHoughThreshold));
        OnPropertyChanged(nameof(LineMinLineLength));
        OnPropertyChanged(nameof(LineMaxLineGap));
        RefreshLinePreview();
    }

    private LineToolDefinition? SelectedLineDef
    {
        get
        {
            var name = SelectedLineTool;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return VisionConfig.Lines.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public int LineCanny1
    {
        get => SelectedLineDef?.Canny1 ?? 50;
        set
        {
            var def = SelectedLineDef;
            if (def is null || def.Canny1 == value)
            {
                return;
            }

            def.Canny1 = value;
            OnPropertyChanged();
            RefreshLinePreview();
        }
    }

    public int LineCanny2
    {
        get => SelectedLineDef?.Canny2 ?? 150;
        set
        {
            var def = SelectedLineDef;
            if (def is null || def.Canny2 == value)
            {
                return;
            }

            def.Canny2 = value;
            OnPropertyChanged();
            RefreshLinePreview();
        }
    }

    public int LineHoughThreshold
    {
        get => SelectedLineDef?.HoughThreshold ?? 50;
        set
        {
            var def = SelectedLineDef;
            if (def is null || def.HoughThreshold == value)
            {
                return;
            }

            def.HoughThreshold = value;
            OnPropertyChanged();
            RefreshLinePreview();
        }
    }

    public int LineMinLineLength
    {
        get => SelectedLineDef?.MinLineLength ?? 30;
        set
        {
            var def = SelectedLineDef;
            if (def is null || def.MinLineLength == value)
            {
                return;
            }

            def.MinLineLength = value;
            OnPropertyChanged();
            RefreshLinePreview();
        }
    }

    public int LineMaxLineGap
    {
        get => SelectedLineDef?.MaxLineGap ?? 10;
        set
        {
            var def = SelectedLineDef;
            if (def is null || def.MaxLineGap == value)
            {
                return;
            }

            def.MaxLineGap = value;
            OnPropertyChanged();
            RefreshLinePreview();
        }
    }

    [ObservableProperty]
    private bool _isLinePreviewEnabled;

    partial void OnIsLinePreviewEnabledChanged(bool value)
    {
        RefreshLinePreview();
    }

    [ObservableProperty]
    private ImageSource? _linePreviewImage;

    private void RefreshLinePreview()
    {
        if (!IsLinePreviewEnabled)
        {
            LinePreviewImage = null;
            return;
        }

        if (_imageMat is null)
        {
            LinePreviewImage = null;
            return;
        }

        var def = SelectedLineDef;
        if (def is null || def.SearchRoi.Width <= 0 || def.SearchRoi.Height <= 0)
        {
            LinePreviewImage = null;
            return;
        }

        var r = new OpenCvSharp.Rect(def.SearchRoi.X, def.SearchRoi.Y, def.SearchRoi.Width, def.SearchRoi.Height);
        r = r.Intersect(new OpenCvSharp.Rect(0, 0, _imageMat.Width, _imageMat.Height));
        if (r.Width <= 0 || r.Height <= 0)
        {
            LinePreviewImage = null;
            return;
        }

        using var processed = _preprocessor.Run(_imageMat, VisionConfig.Preprocess);
        using var crop = new Mat(processed, r);
        using var view = crop.Channels() == 1 ? crop.Clone() : crop.CvtColor(ColorConversionCodes.BGR2GRAY);

        var det = _lineDetector.DetectLongestLine(processed, def.SearchRoi, def.Canny1, def.Canny2, def.HoughThreshold, def.MinLineLength, def.MaxLineGap);
        if (det.Found)
        {
            var p1 = new OpenCvSharp.Point((int)Math.Round(det.P1.X) - r.X, (int)Math.Round(det.P1.Y) - r.Y);
            var p2 = new OpenCvSharp.Point((int)Math.Round(det.P2.X) - r.X, (int)Math.Round(det.P2.Y) - r.Y);
            Cv2.Line(view, p1, p2, Scalar.White, 2);
        }

        LinePreviewImage = view.ToBitmapSource();
    }

    [ObservableProperty]
    private LineDistance? _selectedDistance;

    partial void OnSelectedDistanceChanged(LineDistance? value)
    {
        if (value is null)
        {
            return;
        }

        DistanceName = value.Name;
        DistancePointA = value.PointA;
        DistancePointB = value.PointB;
        DistanceNominal = value.Nominal;
        DistanceTolPlus = value.TolerancePlus;
        DistanceTolMinus = value.ToleranceMinus;
    }

    public double PixelsPerMm
    {
        get => VisionConfig.PixelsPerMm;
        set
        {
            if (Math.Abs(VisionConfig.PixelsPerMm - value) < 0.000001)
            {
                return;
            }

            VisionConfig.PixelsPerMm = value;
            OnPropertyChanged();
            RefreshOverlayItems();
        }
    }

    public bool UseGray
    {
        get => VisionConfig.Preprocess.UseGray;
        set
        {
            if (VisionConfig.Preprocess.UseGray == value)
            {
                return;
            }

            VisionConfig.Preprocess.UseGray = value;
            OnPropertyChanged();
            RefreshDisplayedImage();
        }
    }

    public bool UseGaussianBlur
    {
        get => VisionConfig.Preprocess.UseGaussianBlur;
        set
        {
            if (VisionConfig.Preprocess.UseGaussianBlur == value)
            {
                return;
            }

            VisionConfig.Preprocess.UseGaussianBlur = value;
            OnPropertyChanged();
            RefreshDisplayedImage();
        }
    }

    public int BlurKernel
    {
        get => VisionConfig.Preprocess.BlurKernel;
        set
        {
            if (VisionConfig.Preprocess.BlurKernel == value)
            {
                return;
            }

            VisionConfig.Preprocess.BlurKernel = value;
            OnPropertyChanged();
            RefreshDisplayedImage();
        }
    }

    public bool UseThreshold
    {
        get => VisionConfig.Preprocess.UseThreshold;
        set
        {
            if (VisionConfig.Preprocess.UseThreshold == value)
            {
                return;
            }

            VisionConfig.Preprocess.UseThreshold = value;
            OnPropertyChanged();
            RefreshDisplayedImage();
        }
    }

    public int ThresholdValue
    {
        get => VisionConfig.Preprocess.ThresholdValue;
        set
        {
            if (VisionConfig.Preprocess.ThresholdValue == value)
            {
                return;
            }

            VisionConfig.Preprocess.ThresholdValue = value;
            OnPropertyChanged();
            RefreshDisplayedImage();
        }
    }

    public bool UseCanny
    {
        get => VisionConfig.Preprocess.UseCanny;
        set
        {
            if (VisionConfig.Preprocess.UseCanny == value)
            {
                return;
            }

            VisionConfig.Preprocess.UseCanny = value;
            OnPropertyChanged();
            RefreshDisplayedImage();
        }
    }

    public int Canny1
    {
        get => VisionConfig.Preprocess.Canny1;
        set
        {
            if (VisionConfig.Preprocess.Canny1 == value)
            {
                return;
            }

            VisionConfig.Preprocess.Canny1 = value;
            OnPropertyChanged();
            RefreshDisplayedImage();
        }
    }

    public int Canny2
    {
        get => VisionConfig.Preprocess.Canny2;
        set
        {
            if (VisionConfig.Preprocess.Canny2 == value)
            {
                return;
            }

            VisionConfig.Preprocess.Canny2 = value;
            OnPropertyChanged();
            RefreshDisplayedImage();
        }
    }

    public bool UseMorphology
    {
        get => VisionConfig.Preprocess.UseMorphology;
        set
        {
            if (VisionConfig.Preprocess.UseMorphology == value)
            {
                return;
            }

            VisionConfig.Preprocess.UseMorphology = value;
            OnPropertyChanged();
            RefreshDisplayedImage();
        }
    }

    [ObservableProperty]
    private string _distanceName = "D1";

    [ObservableProperty]
    private string? _distancePointA;

    [ObservableProperty]
    private string? _distancePointB;

    [ObservableProperty]
    private double _distanceNominal;

    [ObservableProperty]
    private double _distanceTolPlus;

    [ObservableProperty]
    private double _distanceTolMinus;

    [ObservableProperty]
    private LineToLineDistance? _selectedLineToLineDistance;

    partial void OnSelectedLineToLineDistanceChanged(LineToLineDistance? value)
    {
        if (value is null)
        {
            return;
        }

        LineToLineName = value.Name;
        LineToLineLineA = value.LineA;
        LineToLineLineB = value.LineB;
        LineToLineNominal = value.Nominal;
        LineToLineTolPlus = value.TolerancePlus;
        LineToLineTolMinus = value.ToleranceMinus;
    }

    [ObservableProperty]
    private string _lineToLineName = "LL1";

    [ObservableProperty]
    private string? _lineToLineLineA;

    [ObservableProperty]
    private string? _lineToLineLineB;

    [ObservableProperty]
    private double _lineToLineNominal;

    [ObservableProperty]
    private double _lineToLineTolPlus;

    [ObservableProperty]
    private double _lineToLineTolMinus;

    [ObservableProperty]
    private PointToLineDistance? _selectedPointToLineDistance;

    partial void OnSelectedPointToLineDistanceChanged(PointToLineDistance? value)
    {
        if (value is null)
        {
            return;
        }

        PointToLineName = value.Name;
        PointToLinePoint = value.Point;
        PointToLineLine = value.Line;
        PointToLineNominal = value.Nominal;
        PointToLineTolPlus = value.TolerancePlus;
        PointToLineTolMinus = value.ToleranceMinus;
    }

    [ObservableProperty]
    private string _pointToLineName = "PL1";

    [ObservableProperty]
    private string? _pointToLinePoint;

    [ObservableProperty]
    private string? _pointToLineLine;

    [ObservableProperty]
    private double _pointToLineNominal;

    [ObservableProperty]
    private double _pointToLineTolPlus;

    [ObservableProperty]
    private double _pointToLineTolMinus;

    private void RefreshLinesFromConfig()
    {
        Lines.Clear();
        foreach (var l in VisionConfig.Lines)
        {
            if (!string.IsNullOrWhiteSpace(l.Name) && !Lines.Contains(l.Name))
            {
                Lines.Add(l.Name);
            }
        }
    }

    public ObservableCollection<LineDistance> Distances { get; }

    public ObservableCollection<LineToLineDistance> LineToLineDistances { get; }

    public ObservableCollection<PointToLineDistance> PointToLineDistances { get; }

    public ObservableCollection<OverlayItem> OverlayItems { get; } = new();

    public ICommand LoadImageCommand { get; }

    public ICommand RefreshConfigsCommand { get; }

    public ICommand LoadConfigCommand { get; }

    public ICommand NewConfigCommand { get; }

    public ICommand RoiSelectedCommand { get; }

    public ICommand RoiEditedCommand { get; }

    public ICommand AddPointCommand { get; }

    public ICommand AddLineToolCommand { get; }

    public ICommand AddDistanceCommand { get; }

    public ICommand UpdateDistanceCommand { get; }

    public ICommand AddLineToLineDistanceCommand { get; }

    public ICommand UpdateLineToLineDistanceCommand { get; }

    public ICommand DeleteLineToLineDistanceCommand { get; }

    public ICommand AddPointToLineDistanceCommand { get; }

    public ICommand UpdatePointToLineDistanceCommand { get; }

    public ICommand DeletePointToLineDistanceCommand { get; }

    public ICommand DeletePointCommand { get; }

    public ICommand DeleteLineToolCommand { get; }

    public ICommand DeleteDistanceCommand { get; }

    public ICommand SaveConfigCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

    private void RefreshConfigs()
    {
        AvailableConfigs.Clear();

        var root = Path.GetFullPath(_storeOptions.ConfigRootDirectory);
        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrWhiteSpace(name))
            {
                AvailableConfigs.Add(name);
            }
        }

        if (!string.IsNullOrWhiteSpace(ProductCode) && AvailableConfigs.Contains(ProductCode))
        {
            SelectedConfig = ProductCode;
        }
        else if (AvailableConfigs.Count > 0)
        {
            SelectedConfig ??= AvailableConfigs[0];
        }
    }

    private void LoadConfig()
    {
        var code = SelectedConfig;
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var cfg = _configService.LoadConfig(code);
        ApplyConfig(cfg);
        RaisePreprocessPropertiesChanged();
        RefreshDisplayedImage();
        RefreshOverlayItems();
    }

    private void OnRoiEdited(RoiSelection? sel)
    {
        if (sel is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sel.Label))
        {
            return;
        }

        var label = sel.Label.Trim();
        var roi = sel.Roi;

        ExecuteRoiEditUndo(label, roi);
    }

    private void ExecuteRoiEditUndo(string label, Roi roi)
    {
        var beforeSelectedTarget = SelectedTarget;
        var beforeSelectedPoint = SelectedPoint;
        var beforeSelectedMode = SelectedRoiMode;

        var beforeOriginSearch = VisionConfig.Origin.SearchRoi;
        var beforeOriginTemplate = VisionConfig.Origin.TemplateRoi;
        var beforeOriginTemplateFile = VisionConfig.Origin.TemplateImageFile;
        var beforeOriginShapeModel = VisionConfig.Origin.ShapeModel;
        var beforeOriginWorld = VisionConfig.Origin.WorldPosition;
        var beforeDefectRoi = VisionConfig.DefectConfig.InspectRoi;

        var createdPoint = false;
        var pointName = string.Empty;
        PointDefinition? pointDef = null;
        Roi beforePointSearch = new();
        Roi beforePointTemplate = new();
        string beforePointTemplateFile = string.Empty;
        ShapeModelDefinition? beforePointShapeModel = null;
        Point2dModel beforePointWorld = new();

        var parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            pointName = parts[0];
            pointDef = VisionConfig.Points.FirstOrDefault(x => string.Equals(x.Name, pointName, StringComparison.OrdinalIgnoreCase));
            if (pointDef is null)
            {
                createdPoint = true;
                pointDef = new PointDefinition { Name = pointName };
            }

            beforePointSearch = pointDef.SearchRoi;
            beforePointTemplate = pointDef.TemplateRoi;
            beforePointTemplateFile = pointDef.TemplateImageFile;
            beforePointShapeModel = pointDef.ShapeModel;
            beforePointWorld = pointDef.WorldPosition;
        }

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                ApplyRoiEdit(label, roi);
            },
            () =>
            {
                SelectedTarget = beforeSelectedTarget;
                SelectedPoint = beforeSelectedPoint;
                SelectedRoiMode = beforeSelectedMode;

                VisionConfig.DefectConfig.InspectRoi = beforeDefectRoi;
                VisionConfig.Origin.SearchRoi = beforeOriginSearch;
                VisionConfig.Origin.TemplateRoi = beforeOriginTemplate;
                VisionConfig.Origin.TemplateImageFile = beforeOriginTemplateFile;
                VisionConfig.Origin.ShapeModel = beforeOriginShapeModel;
                VisionConfig.Origin.WorldPosition = beforeOriginWorld;
                if (beforeOriginTemplate.Width > 0 && beforeOriginTemplate.Height > 0)
                {
                    VisionConfig.Origin.TemplateImageFile = SaveTemplateImage("origin", beforeOriginTemplate);
                    using var t = Cv2.ImRead(VisionConfig.Origin.TemplateImageFile, ImreadModes.Grayscale);
                    VisionConfig.Origin.ShapeModel = ShapeModelTrainer.Train(t);
                }
                else
                {
                    VisionConfig.Origin.ShapeModel = null;
                }

                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(pointName) && pointDef is not null)
                {
                    if (createdPoint)
                    {
                        var existing = VisionConfig.Points.FirstOrDefault(x => string.Equals(x.Name, pointName, StringComparison.OrdinalIgnoreCase));
                        if (existing is not null)
                        {
                            VisionConfig.Points.Remove(existing);
                        }
                    }
                    else
                    {
                        var existing = VisionConfig.Points.FirstOrDefault(x => string.Equals(x.Name, pointName, StringComparison.OrdinalIgnoreCase));
                        if (existing is not null)
                        {
                            existing.SearchRoi = beforePointSearch;
                            existing.TemplateRoi = beforePointTemplate;
                            existing.TemplateImageFile = beforePointTemplateFile;
                            existing.ShapeModel = beforePointShapeModel;
                            existing.WorldPosition = beforePointWorld;
                            if (beforePointTemplate.Width > 0 && beforePointTemplate.Height > 0)
                            {
                                existing.TemplateImageFile = SaveTemplateImage(pointName.ToLowerInvariant(), beforePointTemplate);
                                using var t = Cv2.ImRead(existing.TemplateImageFile, ImreadModes.Grayscale);
                                existing.ShapeModel = ShapeModelTrainer.Train(t);
                            }
                            else
                            {
                                existing.ShapeModel = null;
                            }
                        }
                    }

                    if (createdPoint && Points.Contains(pointName))
                    {
                        Points.Remove(pointName);
                    }
                }

                RefreshOverlayItems();
            }));
    }

    private void TeachLineTool(Roi roi)
    {
        SelectedRoiMode = RoiTeachMode.Search;

        var name = SelectedLineTool;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = NewLineToolName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (!Lines.Contains(name))
            {
                Lines.Add(name);
            }

            SelectedLineTool = name;
        }

        var def = VisionConfig.Lines.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def is null)
        {
            def = new LineToolDefinition { Name = name };
            VisionConfig.Lines.Add(def);
        }

        def.SearchRoi = roi;
        RefreshLinePreview();
    }

    private void ApplyRoiEdit(string label, Roi roi)
    {
        if (string.Equals(label, "DefectROI", StringComparison.OrdinalIgnoreCase))
        {
            SelectedTarget = TeachTarget.DefectRoi;
            VisionConfig.DefectConfig.InspectRoi = roi;
            RefreshOverlayItems();
            return;
        }

        if (label.StartsWith("Origin", StringComparison.OrdinalIgnoreCase))
        {
            SelectedTarget = TeachTarget.Origin;

            if (label.EndsWith(" S", StringComparison.OrdinalIgnoreCase))
            {
                SelectedRoiMode = RoiTeachMode.Search;
                VisionConfig.Origin.SearchRoi = roi;
                RefreshOverlayItems();
                return;
            }

            if (label.EndsWith(" T", StringComparison.OrdinalIgnoreCase))
            {
                SelectedRoiMode = RoiTeachMode.Template;
                EnsureTemplateInsideSearch(VisionConfig.Origin.SearchRoi, roi, VisionConfig.Origin.Name);
                VisionConfig.Origin.TemplateRoi = roi;
                VisionConfig.Origin.TemplateImageFile = SaveTemplateImage("origin", roi);
                VisionConfig.Origin.WorldPosition = new Point2dModel
                {
                    X = roi.X + roi.Width / 2.0,
                    Y = roi.Y + roi.Height / 2.0
                };
                RefreshOverlayItems();
                return;
            }
        }

        var parts = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            var pointName = parts[0];
            var mode = parts[1];

            if (!string.IsNullOrWhiteSpace(pointName) && (string.Equals(mode, "S", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "T", StringComparison.OrdinalIgnoreCase)))
            {
                SelectedTarget = TeachTarget.Point;
                SelectedPoint = pointName;

                var p = VisionConfig.Points.FirstOrDefault(x => string.Equals(x.Name, pointName, StringComparison.OrdinalIgnoreCase));
                if (p is null)
                {
                    p = new PointDefinition { Name = pointName };
                    VisionConfig.Points.Add(p);
                    if (!Points.Contains(pointName))
                    {
                        Points.Add(pointName);
                    }
                }

                if (string.Equals(mode, "S", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedRoiMode = RoiTeachMode.Search;
                    p.SearchRoi = roi;
                    if (p.TemplateRoi.Width <= 0 || p.TemplateRoi.Height <= 0)
                    {
                        p.TemplateRoi = roi;
                    }

                    RefreshOverlayItems();
                    return;
                }

                if (string.Equals(mode, "T", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedRoiMode = RoiTeachMode.Template;
                    EnsureTemplateInsideSearch(p.SearchRoi, roi, p.Name);
                    p.TemplateRoi = roi;
                    p.TemplateImageFile = SaveTemplateImage(pointName.ToLowerInvariant(), roi);
                    p.WorldPosition = new Point2dModel
                    {
                        X = roi.X + roi.Width / 2.0,
                        Y = roi.Y + roi.Height / 2.0
                    };

                    RefreshOverlayItems();
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(pointName) && string.Equals(mode, "L", StringComparison.OrdinalIgnoreCase))
            {
                SelectedTarget = TeachTarget.LineTool;
                SelectedLineTool = pointName;
                SelectedRoiMode = RoiTeachMode.Search;

                var def = VisionConfig.Lines.FirstOrDefault(x => string.Equals(x.Name, pointName, StringComparison.OrdinalIgnoreCase));
                if (def is null)
                {
                    def = new LineToolDefinition { Name = pointName };
                    VisionConfig.Lines.Add(def);
                }

                if (!Lines.Contains(pointName))
                {
                    Lines.Add(pointName);
                }

                def.SearchRoi = roi;
                RefreshOverlayItems();
                return;
            }
        }

        RefreshOverlayItems();
    }

    private void UpdateDistance()
    {
        var selected = SelectedDistance;
        if (selected is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(DistanceName) || string.IsNullOrWhiteSpace(DistancePointA) || string.IsNullOrWhiteSpace(DistancePointB))
        {
            return;
        }

        var before = selected;
        var updated = new LineDistance
        {
            Name = DistanceName.Trim(),
            PointA = DistancePointA.Trim(),
            PointB = DistancePointB.Trim(),
            Nominal = DistanceNominal,
            TolerancePlus = DistanceTolPlus,
            ToleranceMinus = DistanceTolMinus
        };

        var idxVm = Distances.IndexOf(selected);
        var idxCfg = VisionConfig.Distances.IndexOf(selected);

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                if (idxVm >= 0) Distances[idxVm] = updated; else Distances.Add(updated);
                if (idxCfg >= 0) VisionConfig.Distances[idxCfg] = updated; else VisionConfig.Distances.Add(updated);
                SelectedDistance = updated;
                RefreshOverlayItems();
            },
            () =>
            {
                if (idxVm >= 0) Distances[idxVm] = before; else Distances.Remove(updated);
                if (idxCfg >= 0) VisionConfig.Distances[idxCfg] = before; else VisionConfig.Distances.Remove(updated);
                SelectedDistance = before;
                RefreshOverlayItems();
            }));
    }

    private void AddDistance()
    {
        if (string.IsNullOrWhiteSpace(DistanceName) || string.IsNullOrWhiteSpace(DistancePointA) || string.IsNullOrWhiteSpace(DistancePointB))
        {
            return;
        }

        var spec = new LineDistance
        {
            Name = DistanceName.Trim(),
            PointA = DistancePointA.Trim(),
            PointB = DistancePointB.Trim(),
            Nominal = DistanceNominal,
            TolerancePlus = DistanceTolPlus,
            ToleranceMinus = DistanceTolMinus
        };

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                VisionConfig.Distances.Add(spec);
                Distances.Add(spec);
                SelectedDistance = spec;
                RefreshOverlayItems();
            },
            () =>
            {
                VisionConfig.Distances.Remove(spec);
                Distances.Remove(spec);
                SelectedDistance = Distances.Count > 0 ? Distances[0] : null;
                RefreshOverlayItems();
            }));
    }

    private void NewConfig()
    {
        _undo.Clear();
        VisionConfig.Preprocess = new PreprocessSettings();
        VisionConfig.PixelsPerMm = 1.0;
        VisionConfig.Origin = new PointDefinition { Name = "Origin" };
        VisionConfig.Points.Clear();
        VisionConfig.Lines.Clear();
        VisionConfig.Distances.Clear();
        VisionConfig.LineToLineDistances.Clear();
        VisionConfig.PointToLineDistances.Clear();
        VisionConfig.DefectConfig = new DefectInspectionConfig();

        Points.Clear();
        Distances.Clear();
        LineToLineDistances.Clear();
        PointToLineDistances.Clear();
        RefreshLinesFromConfig();

        RaisePreprocessPropertiesChanged();
        RefreshDisplayedImage();
        RefreshOverlayItems();
    }

    private void ApplyConfig(VisionConfig cfg)
    {
        _undo.Clear();
        ProductCode = cfg.ProductCode;
        VisionConfig.ProductCode = cfg.ProductCode;
        VisionConfig.PixelsPerMm = cfg.PixelsPerMm;
        VisionConfig.Preprocess = cfg.Preprocess;
        VisionConfig.Origin = cfg.Origin;
        VisionConfig.DefectConfig = cfg.DefectConfig;

        VisionConfig.Points.Clear();
        foreach (var p in cfg.Points)
        {
            VisionConfig.Points.Add(p);
        }

        VisionConfig.Lines.Clear();
        foreach (var l in cfg.Lines)
        {
            VisionConfig.Lines.Add(l);
        }

        VisionConfig.Distances.Clear();
        foreach (var d in cfg.Distances)
        {
            VisionConfig.Distances.Add(d);
        }

        VisionConfig.LineToLineDistances.Clear();
        foreach (var d in cfg.LineToLineDistances)
        {
            VisionConfig.LineToLineDistances.Add(d);
        }

        VisionConfig.PointToLineDistances.Clear();
        foreach (var d in cfg.PointToLineDistances)
        {
            VisionConfig.PointToLineDistances.Add(d);
        }

        Points.Clear();
        foreach (var p in VisionConfig.Points)
        {
            if (!string.IsNullOrWhiteSpace(p.Name) && !Points.Contains(p.Name))
            {
                Points.Add(p.Name);
            }
        }

        RefreshLinesFromConfig();

        Distances.Clear();
        foreach (var d in VisionConfig.Distances)
        {
            Distances.Add(d);
        }

        LineToLineDistances.Clear();
        foreach (var d in VisionConfig.LineToLineDistances)
        {
            LineToLineDistances.Add(d);
        }

        PointToLineDistances.Clear();
        foreach (var d in VisionConfig.PointToLineDistances)
        {
            PointToLineDistances.Add(d);
        }

        SelectedConfig = cfg.ProductCode;
        SelectedPoint = Points.Count > 0 ? Points[0] : null;
        SelectedLineTool = Lines.Count > 0 ? Lines[0] : null;
        SelectedDistance = Distances.Count > 0 ? Distances[0] : null;
        SelectedLineToLineDistance = LineToLineDistances.Count > 0 ? LineToLineDistances[0] : null;
        SelectedPointToLineDistance = PointToLineDistances.Count > 0 ? PointToLineDistances[0] : null;
        OnPropertyChanged(nameof(PixelsPerMm));
        OnPropertyChanged(nameof(LineCanny1));
        OnPropertyChanged(nameof(LineCanny2));
        OnPropertyChanged(nameof(LineHoughThreshold));
        OnPropertyChanged(nameof(LineMinLineLength));
        OnPropertyChanged(nameof(LineMaxLineGap));

        RaisePreprocessPropertiesChanged();
        RefreshDisplayedImage();
        RefreshLinePreview();
    }

    private void LoadImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Image Files|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|All Files|*.*"
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        _imageMat?.Dispose();
        _imageMat = Cv2.ImRead(dlg.FileName, ImreadModes.Color);
        _sharedImage.SetImage(_imageMat);
        RefreshDisplayedImage();

        RefreshLinePreview();

        RefreshOverlayItems();
    }

    private void OnRoiSelected(Roi? roi)
    {
        if (roi is null)
        {
            return;
        }

        LastRoi = roi;
        if (_imageMat is null)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SelectedRoiMode = RoiTeachMode.Search;
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            SelectedRoiMode = RoiTeachMode.Template;
        }

        var beforeSelectedTarget = SelectedTarget;
        var beforeSelectedPoint = SelectedPoint;
        var beforeSelectedMode = SelectedRoiMode;

        var beforeOriginSearch = VisionConfig.Origin.SearchRoi;
        var beforeOriginTemplate = VisionConfig.Origin.TemplateRoi;
        var beforeOriginTemplateFile = VisionConfig.Origin.TemplateImageFile;
        var beforeOriginShapeModel = VisionConfig.Origin.ShapeModel;
        var beforeOriginWorld = VisionConfig.Origin.WorldPosition;
        var beforeDefectRoi = VisionConfig.DefectConfig.InspectRoi;

        var pointName = SelectedPoint;
        var pointDef = (!string.IsNullOrWhiteSpace(pointName))
            ? VisionConfig.Points.FirstOrDefault(x => string.Equals(x.Name, pointName, StringComparison.OrdinalIgnoreCase))
            : null;

        var beforePointSearch = pointDef?.SearchRoi ?? new Roi();
        var beforePointTemplate = pointDef?.TemplateRoi ?? new Roi();
        var beforePointTemplateFile = pointDef?.TemplateImageFile ?? string.Empty;
        var beforePointShapeModel = pointDef?.ShapeModel;
        var beforePointWorld = pointDef?.WorldPosition ?? new Point2dModel();

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                switch (SelectedTarget)
                {
                    case TeachTarget.Origin:
                        TeachOrigin(roi);
                        break;
                    case TeachTarget.Point:
                        TeachPoint(roi);
                        break;
                    case TeachTarget.LineTool:
                        TeachLineTool(roi);
                        break;
                    case TeachTarget.DefectRoi:
                        VisionConfig.DefectConfig.InspectRoi = roi;
                        break;
                }

                RefreshOverlayItems();
            },
            () =>
            {
                SelectedTarget = beforeSelectedTarget;
                SelectedPoint = beforeSelectedPoint;
                SelectedRoiMode = beforeSelectedMode;

                VisionConfig.DefectConfig.InspectRoi = beforeDefectRoi;
                VisionConfig.Origin.SearchRoi = beforeOriginSearch;
                VisionConfig.Origin.TemplateRoi = beforeOriginTemplate;
                VisionConfig.Origin.TemplateImageFile = beforeOriginTemplateFile;
                VisionConfig.Origin.ShapeModel = beforeOriginShapeModel;
                VisionConfig.Origin.WorldPosition = beforeOriginWorld;
                if (beforeOriginTemplate.Width > 0 && beforeOriginTemplate.Height > 0)
                {
                    VisionConfig.Origin.TemplateImageFile = SaveTemplateImage("origin", beforeOriginTemplate);
                    using var t = Cv2.ImRead(VisionConfig.Origin.TemplateImageFile, ImreadModes.Grayscale);
                    VisionConfig.Origin.ShapeModel = ShapeModelTrainer.Train(t);
                }
                else
                {
                    VisionConfig.Origin.ShapeModel = null;
                }

                if (pointDef is not null)
                {
                    pointDef.SearchRoi = beforePointSearch;
                    pointDef.TemplateRoi = beforePointTemplate;
                    pointDef.TemplateImageFile = beforePointTemplateFile;
                    pointDef.ShapeModel = beforePointShapeModel;
                    pointDef.WorldPosition = beforePointWorld;
                    if (beforePointTemplate.Width > 0 && beforePointTemplate.Height > 0 && !string.IsNullOrWhiteSpace(pointDef.Name))
                    {
                        pointDef.TemplateImageFile = SaveTemplateImage(pointDef.Name.ToLowerInvariant(), beforePointTemplate);
                        using var t = Cv2.ImRead(pointDef.TemplateImageFile, ImreadModes.Grayscale);
                        pointDef.ShapeModel = ShapeModelTrainer.Train(t);
                    }
                    else
                    {
                        pointDef.ShapeModel = null;
                    }
                }

                RefreshOverlayItems();
            }));
    }

    private void TeachOrigin(Roi roi)
    {
        VisionConfig.Origin.Name = string.IsNullOrWhiteSpace(VisionConfig.Origin.Name) ? "Origin" : VisionConfig.Origin.Name;

        if (SelectedRoiMode == RoiTeachMode.Search)
        {
            VisionConfig.Origin.SearchRoi = roi;
            if (VisionConfig.Origin.TemplateRoi.Width <= 0 || VisionConfig.Origin.TemplateRoi.Height <= 0)
            {
                VisionConfig.Origin.TemplateRoi = roi;
            }

            return;
        }

        EnsureTemplateInsideSearch(VisionConfig.Origin.SearchRoi, roi, VisionConfig.Origin.Name);
        VisionConfig.Origin.TemplateRoi = roi;
        VisionConfig.Origin.TemplateImageFile = SaveTemplateImage("origin", roi);
        using (var t = Cv2.ImRead(VisionConfig.Origin.TemplateImageFile, ImreadModes.Grayscale))
        {
            VisionConfig.Origin.ShapeModel = ShapeModelTrainer.Train(t);
        }
        VisionConfig.Origin.WorldPosition = new Point2dModel
        {
            X = roi.X + roi.Width / 2.0,
            Y = roi.Y + roi.Height / 2.0
        };
    }

    private void TeachPoint(Roi roi)
    {
        var pointName = SelectedPoint;
        if (string.IsNullOrWhiteSpace(pointName))
        {
            pointName = NewPointName?.Trim();
            if (string.IsNullOrWhiteSpace(pointName))
            {
                return;
            }

            if (!Points.Contains(pointName))
            {
                Points.Add(pointName);
            }

            SelectedPoint = pointName;
        }

        var p = VisionConfig.Points.FirstOrDefault(x => string.Equals(x.Name, pointName, StringComparison.OrdinalIgnoreCase));
        if (p is null)
        {
            p = new PointDefinition { Name = pointName };
            VisionConfig.Points.Add(p);
        }

        if (SelectedRoiMode == RoiTeachMode.Search)
        {
            p.SearchRoi = roi;
            if (p.TemplateRoi.Width <= 0 || p.TemplateRoi.Height <= 0)
            {
                p.TemplateRoi = roi;
            }

            return;
        }

        EnsureTemplateInsideSearch(p.SearchRoi, roi, p.Name);
        p.TemplateRoi = roi;
        p.TemplateImageFile = SaveTemplateImage(pointName, roi);
        using (var t = Cv2.ImRead(p.TemplateImageFile, ImreadModes.Grayscale))
        {
            p.ShapeModel = ShapeModelTrainer.Train(t);
        }

        p.WorldPosition = new Point2dModel
        {
            X = roi.X + roi.Width / 2.0,
            Y = roi.Y + roi.Height / 2.0
        };
    }

    private static void EnsureTemplateInsideSearch(Roi search, Roi template, string name)
    {
        if (search.Width <= 0 || search.Height <= 0)
        {
            throw new InvalidOperationException($"Search ROI is not set for point '{name}'.");
        }

        var searchRight = search.X + search.Width;
        var searchBottom = search.Y + search.Height;

        var templRight = template.X + template.Width;
        var templBottom = template.Y + template.Height;

        var inside = template.X >= search.X
            && template.Y >= search.Y
            && templRight <= searchRight
            && templBottom <= searchBottom;

        if (!inside)
        {
            throw new InvalidOperationException($"Template ROI must be inside Search ROI for point '{name}'.");
        }
    }

    private string SaveTemplateImage(string name, Roi roi)
    {
        if (_imageMat is null)
        {
            throw new InvalidOperationException("No image loaded.");
        }

        var templateDir = Path.Combine(Path.GetFullPath(_storeOptions.ConfigRootDirectory), ProductCode, "templates");
        Directory.CreateDirectory(templateDir);

        var safeName = name.Trim();
        var fileName = $"{safeName}.png";
        var fullPath = Path.Combine(templateDir, fileName);

        var rect = new OpenCvSharp.Rect(roi.X, roi.Y, roi.Width, roi.Height);
        rect = rect.Intersect(new OpenCvSharp.Rect(0, 0, _imageMat.Width, _imageMat.Height));
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            throw new ArgumentException("ROI is out of image bounds.");
        }

        using var cropped = new Mat(_imageMat, rect);
        using var gray = cropped.Channels() == 1 ? cropped.Clone() : cropped.CvtColor(ColorConversionCodes.BGR2GRAY);
        Cv2.ImWrite(fullPath, gray);

        return fullPath;
    }

    private void AddPoint()
    {
        var name = NewPointName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var existed = Points.Contains(name);
        var beforeSelectedPoint = SelectedPoint;
        var beforeTarget = SelectedTarget;

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                if (!existed)
                {
                    Points.Add(name);
                }

                SelectedPoint = name;
                SelectedTarget = TeachTarget.Point;
                RefreshOverlayItems();
            },
            () =>
            {
                if (!existed && Points.Contains(name))
                {
                    Points.Remove(name);
                }

                SelectedPoint = beforeSelectedPoint;
                SelectedTarget = beforeTarget;
                RefreshOverlayItems();
            }));
    }

    private void AddLineTool()
    {
        var name = NewLineToolName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var beforeSelectedTarget = SelectedTarget;
        var beforeSelectedLine = SelectedLineTool;

        var existedInList = Lines.Contains(name);
        var existedInCfg = VisionConfig.Lines.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                if (!existedInList)
                {
                    Lines.Add(name);
                }

                if (!existedInCfg)
                {
                    VisionConfig.Lines.Add(new LineToolDefinition { Name = name });
                }

                SelectedLineTool = name;
                SelectedTarget = TeachTarget.LineTool;
                SelectedRoiMode = RoiTeachMode.Search;
                RefreshOverlayItems();
            },
            () =>
            {
                SelectedTarget = beforeSelectedTarget;
                SelectedLineTool = beforeSelectedLine;

                if (!existedInCfg)
                {
                    var existing = VisionConfig.Lines.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null)
                    {
                        VisionConfig.Lines.Remove(existing);
                    }
                }

                if (!existedInList && Lines.Contains(name))
                {
                    Lines.Remove(name);
                }

                RefreshOverlayItems();
            }));
    }

    private void DeleteLineTool()
    {
        var name = SelectedLineTool;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var beforeSelectedTarget = SelectedTarget;
        var beforeSelectedLine = SelectedLineTool;

        var def = VisionConfig.Lines.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def is null)
        {
            return;
        }

        var idxCfg = VisionConfig.Lines.IndexOf(def);
        var idxVm = Lines.IndexOf(name);

        var removedCfgLL = VisionConfig.LineToLineDistances
            .Select((d, i) => (Item: d, Index: i))
            .Where(x => string.Equals(x.Item.LineA, name, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Item.LineB, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var removedVmLL = LineToLineDistances
            .Select((d, i) => (Item: d, Index: i))
            .Where(x => string.Equals(x.Item.LineA, name, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Item.LineB, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var removedCfgPL = VisionConfig.PointToLineDistances
            .Select((d, i) => (Item: d, Index: i))
            .Where(x => string.Equals(x.Item.Line, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var removedVmPL = PointToLineDistances
            .Select((d, i) => (Item: d, Index: i))
            .Where(x => string.Equals(x.Item.Line, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                VisionConfig.Lines.Remove(def);
                if (idxVm >= 0 && Lines.Contains(name))
                {
                    Lines.Remove(name);
                }

                for (var i = VisionConfig.LineToLineDistances.Count - 1; i >= 0; i--)
                {
                    var d = VisionConfig.LineToLineDistances[i];
                    if (string.Equals(d.LineA, name, StringComparison.OrdinalIgnoreCase) || string.Equals(d.LineB, name, StringComparison.OrdinalIgnoreCase))
                    {
                        VisionConfig.LineToLineDistances.RemoveAt(i);
                    }
                }

                for (var i = LineToLineDistances.Count - 1; i >= 0; i--)
                {
                    var d = LineToLineDistances[i];
                    if (string.Equals(d.LineA, name, StringComparison.OrdinalIgnoreCase) || string.Equals(d.LineB, name, StringComparison.OrdinalIgnoreCase))
                    {
                        LineToLineDistances.RemoveAt(i);
                    }
                }

                for (var i = VisionConfig.PointToLineDistances.Count - 1; i >= 0; i--)
                {
                    var d = VisionConfig.PointToLineDistances[i];
                    if (string.Equals(d.Line, name, StringComparison.OrdinalIgnoreCase))
                    {
                        VisionConfig.PointToLineDistances.RemoveAt(i);
                    }
                }

                for (var i = PointToLineDistances.Count - 1; i >= 0; i--)
                {
                    var d = PointToLineDistances[i];
                    if (string.Equals(d.Line, name, StringComparison.OrdinalIgnoreCase))
                    {
                        PointToLineDistances.RemoveAt(i);
                    }
                }

                SelectedLineTool = Lines.Count > 0 ? Lines[0] : null;
                if (SelectedTarget == TeachTarget.LineTool && SelectedLineTool is null)
                {
                    SelectedTarget = TeachTarget.Origin;
                }

                SelectedLineToLineDistance = LineToLineDistances.Count > 0 ? LineToLineDistances[0] : null;
                SelectedPointToLineDistance = PointToLineDistances.Count > 0 ? PointToLineDistances[0] : null;

                RefreshOverlayItems();
            },
            () =>
            {
                SelectedTarget = beforeSelectedTarget;

                if (!VisionConfig.Lines.Contains(def))
                {
                    if (idxCfg >= 0 && idxCfg <= VisionConfig.Lines.Count) VisionConfig.Lines.Insert(idxCfg, def); else VisionConfig.Lines.Add(def);
                }

                if (!Lines.Contains(name))
                {
                    if (idxVm >= 0 && idxVm <= Lines.Count) Lines.Insert(idxVm, name); else Lines.Add(name);
                }

                SelectedLineTool = beforeSelectedLine;

                foreach (var item in removedCfgLL.OrderBy(x => x.Index))
                {
                    if (!VisionConfig.LineToLineDistances.Contains(item.Item))
                    {
                        if (item.Index >= 0 && item.Index <= VisionConfig.LineToLineDistances.Count) VisionConfig.LineToLineDistances.Insert(item.Index, item.Item); else VisionConfig.LineToLineDistances.Add(item.Item);
                    }
                }

                foreach (var item in removedVmLL.OrderBy(x => x.Index))
                {
                    if (!LineToLineDistances.Contains(item.Item))
                    {
                        if (item.Index >= 0 && item.Index <= LineToLineDistances.Count) LineToLineDistances.Insert(item.Index, item.Item); else LineToLineDistances.Add(item.Item);
                    }
                }

                foreach (var item in removedCfgPL.OrderBy(x => x.Index))
                {
                    if (!VisionConfig.PointToLineDistances.Contains(item.Item))
                    {
                        if (item.Index >= 0 && item.Index <= VisionConfig.PointToLineDistances.Count) VisionConfig.PointToLineDistances.Insert(item.Index, item.Item); else VisionConfig.PointToLineDistances.Add(item.Item);
                    }
                }

                foreach (var item in removedVmPL.OrderBy(x => x.Index))
                {
                    if (!PointToLineDistances.Contains(item.Item))
                    {
                        if (item.Index >= 0 && item.Index <= PointToLineDistances.Count) PointToLineDistances.Insert(item.Index, item.Item); else PointToLineDistances.Add(item.Item);
                    }
                }

                SelectedLineToLineDistance = LineToLineDistances.FirstOrDefault();
                SelectedPointToLineDistance = PointToLineDistances.FirstOrDefault();
                RefreshOverlayItems();
            }));
    }

    private void DeletePoint()
    {
        var name = SelectedPoint;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var beforeSelectedPoint = SelectedPoint;
        var beforeSelectedDistance = SelectedDistance;

        var p = VisionConfig.Points.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        var pointIdxCfg = p is null ? -1 : VisionConfig.Points.IndexOf(p);
        var existedInPoints = Points.Contains(name);
        var pointIdxVm = existedInPoints ? Points.IndexOf(name) : -1;

        var removedCfgDistances = VisionConfig.Distances
            .Select((d, i) => (Item: d, Index: i))
            .Where(x => string.Equals(x.Item.PointA, name, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Item.PointB, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var removedVmDistances = Distances
            .Select((d, i) => (Item: d, Index: i))
            .Where(x => string.Equals(x.Item.PointA, name, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Item.PointB, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var removedCfgPL = VisionConfig.PointToLineDistances
            .Select((d, i) => (Item: d, Index: i))
            .Where(x => string.Equals(x.Item.Point, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var removedVmPL = PointToLineDistances
            .Select((d, i) => (Item: d, Index: i))
            .Where(x => string.Equals(x.Item.Point, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                var pNow = VisionConfig.Points.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (pNow is not null)
                {
                    VisionConfig.Points.Remove(pNow);
                }

                Points.Remove(name);

                for (var i = VisionConfig.Distances.Count - 1; i >= 0; i--)
                {
                    var d = VisionConfig.Distances[i];
                    if (string.Equals(d.PointA, name, StringComparison.OrdinalIgnoreCase) || string.Equals(d.PointB, name, StringComparison.OrdinalIgnoreCase))
                    {
                        VisionConfig.Distances.RemoveAt(i);
                    }
                }

                for (var i = Distances.Count - 1; i >= 0; i--)
                {
                    var d = Distances[i];
                    if (string.Equals(d.PointA, name, StringComparison.OrdinalIgnoreCase) || string.Equals(d.PointB, name, StringComparison.OrdinalIgnoreCase))
                    {
                        Distances.RemoveAt(i);
                    }
                }

                for (var i = VisionConfig.PointToLineDistances.Count - 1; i >= 0; i--)
                {
                    var d = VisionConfig.PointToLineDistances[i];
                    if (string.Equals(d.Point, name, StringComparison.OrdinalIgnoreCase))
                    {
                        VisionConfig.PointToLineDistances.RemoveAt(i);
                    }
                }

                for (var i = PointToLineDistances.Count - 1; i >= 0; i--)
                {
                    var d = PointToLineDistances[i];
                    if (string.Equals(d.Point, name, StringComparison.OrdinalIgnoreCase))
                    {
                        PointToLineDistances.RemoveAt(i);
                    }
                }

                SelectedPoint = Points.Count > 0 ? Points[0] : null;
                SelectedDistance = Distances.Count > 0 ? Distances[0] : null;
                SelectedPointToLineDistance = PointToLineDistances.Count > 0 ? PointToLineDistances[0] : null;
                RefreshOverlayItems();
            },
            () =>
            {
                if (p is not null)
                {
                    if (pointIdxCfg >= 0 && pointIdxCfg <= VisionConfig.Points.Count)
                    {
                        VisionConfig.Points.Insert(pointIdxCfg, p);
                    }
                    else
                    {
                        VisionConfig.Points.Add(p);
                    }
                }

                if (existedInPoints)
                {
                    if (pointIdxVm >= 0 && pointIdxVm <= Points.Count)
                    {
                        Points.Insert(pointIdxVm, name);
                    }
                    else if (!Points.Contains(name))
                    {
                        Points.Add(name);
                    }
                }

                foreach (var item in removedCfgDistances.OrderBy(x => x.Index))
                {
                    if (!VisionConfig.Distances.Contains(item.Item))
                    {
                        if (item.Index >= 0 && item.Index <= VisionConfig.Distances.Count) VisionConfig.Distances.Insert(item.Index, item.Item); else VisionConfig.Distances.Add(item.Item);
                    }
                }

                foreach (var item in removedVmDistances.OrderBy(x => x.Index))
                {
                    if (!Distances.Contains(item.Item))
                    {
                        if (item.Index >= 0 && item.Index <= Distances.Count) Distances.Insert(item.Index, item.Item); else Distances.Add(item.Item);
                    }
                }

                foreach (var item in removedCfgPL.OrderBy(x => x.Index))
                {
                    if (!VisionConfig.PointToLineDistances.Contains(item.Item))
                    {
                        if (item.Index >= 0 && item.Index <= VisionConfig.PointToLineDistances.Count) VisionConfig.PointToLineDistances.Insert(item.Index, item.Item); else VisionConfig.PointToLineDistances.Add(item.Item);
                    }
                }

                foreach (var item in removedVmPL.OrderBy(x => x.Index))
                {
                    if (!PointToLineDistances.Contains(item.Item))
                    {
                        if (item.Index >= 0 && item.Index <= PointToLineDistances.Count) PointToLineDistances.Insert(item.Index, item.Item); else PointToLineDistances.Add(item.Item);
                    }
                }

                SelectedPoint = beforeSelectedPoint;
                SelectedDistance = beforeSelectedDistance;
                SelectedPointToLineDistance = PointToLineDistances.FirstOrDefault();
                RefreshOverlayItems();
            }));
    }

    private void AddLineToLineDistance()
    {
        if (string.IsNullOrWhiteSpace(LineToLineName) || string.IsNullOrWhiteSpace(LineToLineLineA) || string.IsNullOrWhiteSpace(LineToLineLineB))
        {
            return;
        }

        var spec = new LineToLineDistance
        {
            Name = LineToLineName.Trim(),
            LineA = LineToLineLineA.Trim(),
            LineB = LineToLineLineB.Trim(),
            Nominal = LineToLineNominal,
            TolerancePlus = LineToLineTolPlus,
            ToleranceMinus = LineToLineTolMinus
        };

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                VisionConfig.LineToLineDistances.Add(spec);
                LineToLineDistances.Add(spec);
                SelectedLineToLineDistance = spec;
            },
            () =>
            {
                VisionConfig.LineToLineDistances.Remove(spec);
                LineToLineDistances.Remove(spec);
                SelectedLineToLineDistance = LineToLineDistances.Count > 0 ? LineToLineDistances[0] : null;
            }));
    }

    private void UpdateLineToLineDistance()
    {
        var selected = SelectedLineToLineDistance;
        if (selected is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(LineToLineName) || string.IsNullOrWhiteSpace(LineToLineLineA) || string.IsNullOrWhiteSpace(LineToLineLineB))
        {
            return;
        }

        var before = selected;
        var updated = new LineToLineDistance
        {
            Name = LineToLineName.Trim(),
            LineA = LineToLineLineA.Trim(),
            LineB = LineToLineLineB.Trim(),
            Nominal = LineToLineNominal,
            TolerancePlus = LineToLineTolPlus,
            ToleranceMinus = LineToLineTolMinus
        };

        var idxVm = LineToLineDistances.IndexOf(selected);
        var idxCfg = VisionConfig.LineToLineDistances.IndexOf(selected);

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                if (idxVm >= 0) LineToLineDistances[idxVm] = updated; else LineToLineDistances.Add(updated);
                if (idxCfg >= 0) VisionConfig.LineToLineDistances[idxCfg] = updated; else VisionConfig.LineToLineDistances.Add(updated);
                SelectedLineToLineDistance = updated;
            },
            () =>
            {
                if (idxVm >= 0) LineToLineDistances[idxVm] = before; else LineToLineDistances.Remove(updated);
                if (idxCfg >= 0) VisionConfig.LineToLineDistances[idxCfg] = before; else VisionConfig.LineToLineDistances.Remove(updated);
                SelectedLineToLineDistance = before;
            }));
    }

    private void DeleteLineToLineDistance()
    {
        var d = SelectedLineToLineDistance;
        if (d is null)
        {
            return;
        }

        var idxVm = LineToLineDistances.IndexOf(d);
        var idxCfg = VisionConfig.LineToLineDistances.IndexOf(d);

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                LineToLineDistances.Remove(d);
                VisionConfig.LineToLineDistances.Remove(d);
                SelectedLineToLineDistance = LineToLineDistances.Count > 0 ? LineToLineDistances[0] : null;
            },
            () =>
            {
                if (idxVm >= 0 && idxVm <= LineToLineDistances.Count) LineToLineDistances.Insert(idxVm, d); else LineToLineDistances.Add(d);
                if (idxCfg >= 0 && idxCfg <= VisionConfig.LineToLineDistances.Count) VisionConfig.LineToLineDistances.Insert(idxCfg, d); else VisionConfig.LineToLineDistances.Add(d);
                SelectedLineToLineDistance = d;
            }));
    }

    private void AddPointToLineDistance()
    {
        if (string.IsNullOrWhiteSpace(PointToLineName) || string.IsNullOrWhiteSpace(PointToLinePoint) || string.IsNullOrWhiteSpace(PointToLineLine))
        {
            return;
        }

        var spec = new PointToLineDistance
        {
            Name = PointToLineName.Trim(),
            Point = PointToLinePoint.Trim(),
            Line = PointToLineLine.Trim(),
            Nominal = PointToLineNominal,
            TolerancePlus = PointToLineTolPlus,
            ToleranceMinus = PointToLineTolMinus
        };

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                VisionConfig.PointToLineDistances.Add(spec);
                PointToLineDistances.Add(spec);
                SelectedPointToLineDistance = spec;
            },
            () =>
            {
                VisionConfig.PointToLineDistances.Remove(spec);
                PointToLineDistances.Remove(spec);
                SelectedPointToLineDistance = PointToLineDistances.Count > 0 ? PointToLineDistances[0] : null;
            }));
    }

    private void UpdatePointToLineDistance()
    {
        var selected = SelectedPointToLineDistance;
        if (selected is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(PointToLineName) || string.IsNullOrWhiteSpace(PointToLinePoint) || string.IsNullOrWhiteSpace(PointToLineLine))
        {
            return;
        }

        var before = selected;
        var updated = new PointToLineDistance
        {
            Name = PointToLineName.Trim(),
            Point = PointToLinePoint.Trim(),
            Line = PointToLineLine.Trim(),
            Nominal = PointToLineNominal,
            TolerancePlus = PointToLineTolPlus,
            ToleranceMinus = PointToLineTolMinus
        };

        var idxVm = PointToLineDistances.IndexOf(selected);
        var idxCfg = VisionConfig.PointToLineDistances.IndexOf(selected);

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                if (idxVm >= 0) PointToLineDistances[idxVm] = updated; else PointToLineDistances.Add(updated);
                if (idxCfg >= 0) VisionConfig.PointToLineDistances[idxCfg] = updated; else VisionConfig.PointToLineDistances.Add(updated);
                SelectedPointToLineDistance = updated;
            },
            () =>
            {
                if (idxVm >= 0) PointToLineDistances[idxVm] = before; else PointToLineDistances.Remove(updated);
                if (idxCfg >= 0) VisionConfig.PointToLineDistances[idxCfg] = before; else VisionConfig.PointToLineDistances.Remove(updated);
                SelectedPointToLineDistance = before;
            }));
    }

    private void DeletePointToLineDistance()
    {
        var d = SelectedPointToLineDistance;
        if (d is null)
        {
            return;
        }

        var idxVm = PointToLineDistances.IndexOf(d);
        var idxCfg = VisionConfig.PointToLineDistances.IndexOf(d);

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                PointToLineDistances.Remove(d);
                VisionConfig.PointToLineDistances.Remove(d);
                SelectedPointToLineDistance = PointToLineDistances.Count > 0 ? PointToLineDistances[0] : null;
            },
            () =>
            {
                if (idxVm >= 0 && idxVm <= PointToLineDistances.Count) PointToLineDistances.Insert(idxVm, d); else PointToLineDistances.Add(d);
                if (idxCfg >= 0 && idxCfg <= VisionConfig.PointToLineDistances.Count) VisionConfig.PointToLineDistances.Insert(idxCfg, d); else VisionConfig.PointToLineDistances.Add(d);
                SelectedPointToLineDistance = d;
            }));
    }

    private void DeleteDistance()
    {
        var d = SelectedDistance;
        if (d is null)
        {
            return;
        }

        var idxVm = Distances.IndexOf(d);
        var idxCfg = VisionConfig.Distances.IndexOf(d);

        _undo.Execute(new UndoRedoManager.DelegateAction(
            () =>
            {
                Distances.Remove(d);
                VisionConfig.Distances.Remove(d);
                SelectedDistance = Distances.Count > 0 ? Distances[0] : null;
                RefreshOverlayItems();
            },
            () =>
            {
                if (idxVm >= 0 && idxVm <= Distances.Count) Distances.Insert(idxVm, d); else Distances.Add(d);
                if (idxCfg >= 0 && idxCfg <= VisionConfig.Distances.Count) VisionConfig.Distances.Insert(idxCfg, d); else VisionConfig.Distances.Add(d);
                SelectedDistance = d;
                RefreshOverlayItems();
            }));
    }

    private void SaveConfig()
    {
        VisionConfig.ProductCode = ProductCode;
        VisionConfig.Origin.Name = string.IsNullOrWhiteSpace(VisionConfig.Origin.Name) ? "Origin" : VisionConfig.Origin.Name;
        _configService.SaveConfig(VisionConfig);

        RefreshConfigs();
    }

    private void RaisePreprocessPropertiesChanged()
    {
        OnPropertyChanged(nameof(UseGray));
        OnPropertyChanged(nameof(UseGaussianBlur));
        OnPropertyChanged(nameof(BlurKernel));
        OnPropertyChanged(nameof(UseThreshold));
        OnPropertyChanged(nameof(ThresholdValue));
        OnPropertyChanged(nameof(UseCanny));
        OnPropertyChanged(nameof(Canny1));
        OnPropertyChanged(nameof(Canny2));
        OnPropertyChanged(nameof(UseMorphology));
    }

    private void RefreshDisplayedImage()
    {
        if (_imageMat is null)
        {
            Image = null;
            return;
        }

        if (!IsPreprocessPreviewEnabled)
        {
            Image = _imageMat.ToBitmapSource();
            return;
        }

        using var processed = _preprocessor.Run(_imageMat, VisionConfig.Preprocess);
        Image = processed.ToBitmapSource();
    }

    private void RefreshOverlayItems()
    {
        OverlayItems.Clear();

        if (VisionConfig.Origin.SearchRoi.Width > 0 && VisionConfig.Origin.SearchRoi.Height > 0)
        {
            OverlayItems.Add(new OverlayRectItem
            {
                X = VisionConfig.Origin.SearchRoi.X,
                Y = VisionConfig.Origin.SearchRoi.Y,
                Width = VisionConfig.Origin.SearchRoi.Width,
                Height = VisionConfig.Origin.SearchRoi.Height,
                Stroke = Brushes.Lime,
                Label = "Origin S"
            });
        }

        if (VisionConfig.Origin.TemplateRoi.Width > 0 && VisionConfig.Origin.TemplateRoi.Height > 0)
        {
            OverlayItems.Add(new OverlayRectItem
            {
                X = VisionConfig.Origin.TemplateRoi.X,
                Y = VisionConfig.Origin.TemplateRoi.Y,
                Width = VisionConfig.Origin.TemplateRoi.Width,
                Height = VisionConfig.Origin.TemplateRoi.Height,
                Stroke = Brushes.Yellow,
                Label = "Origin T"
            });
        }

        foreach (var p in VisionConfig.Points)
        {
            if (p.SearchRoi.Width <= 0 || p.SearchRoi.Height <= 0)
            {
                continue;
            }

            OverlayItems.Add(new OverlayRectItem
            {
                X = p.SearchRoi.X,
                Y = p.SearchRoi.Y,
                Width = p.SearchRoi.Width,
                Height = p.SearchRoi.Height,
                Stroke = Brushes.DeepSkyBlue,
                Label = $"{p.Name} S"
            });

            if (p.TemplateRoi.Width > 0 && p.TemplateRoi.Height > 0)
            {
                OverlayItems.Add(new OverlayRectItem
                {
                    X = p.TemplateRoi.X,
                    Y = p.TemplateRoi.Y,
                    Width = p.TemplateRoi.Width,
                    Height = p.TemplateRoi.Height,
                    Stroke = Brushes.Yellow,
                    Label = $"{p.Name} T"
                });
            }

            OverlayItems.Add(new OverlayPointItem
            {
                X = p.WorldPosition.X,
                Y = p.WorldPosition.Y,
                Stroke = Brushes.DeepSkyBlue,
                Label = p.Name
            });
        }

        foreach (var l in VisionConfig.Lines)
        {
            if (l.SearchRoi.Width <= 0 || l.SearchRoi.Height <= 0)
            {
                continue;
            }

            OverlayItems.Add(new OverlayRectItem
            {
                X = l.SearchRoi.X,
                Y = l.SearchRoi.Y,
                Width = l.SearchRoi.Width,
                Height = l.SearchRoi.Height,
                Stroke = Brushes.MediumPurple,
                Label = $"{l.Name} L"
            });
        }

        var detectedLines = new Dictionary<string, LineDetectResult>(StringComparer.OrdinalIgnoreCase);
        if (_imageMat is not null)
        {
            using var processed = _preprocessor.Run(_imageMat, VisionConfig.Preprocess);
            foreach (var l in VisionConfig.Lines)
            {
                if (l.SearchRoi.Width <= 0 || l.SearchRoi.Height <= 0)
                {
                    continue;
                }

                var det = _lineDetector.DetectLongestLine(processed, l.SearchRoi, l.Canny1, l.Canny2, l.HoughThreshold, l.MinLineLength, l.MaxLineGap);
                var named = det with { Name = l.Name };
                detectedLines[l.Name] = named;

                if (named.Found)
                {
                    OverlayItems.Add(new OverlayLineItem
                    {
                        X1 = named.P1.X,
                        Y1 = named.P1.Y,
                        X2 = named.P2.X,
                        Y2 = named.P2.Y,
                        Stroke = Brushes.MediumPurple,
                        Label = named.Name
                    });
                }
            }
        }

        foreach (var dd in VisionConfig.LineToLineDistances)
        {
            if (string.IsNullOrWhiteSpace(dd.Name) || string.IsNullOrWhiteSpace(dd.LineA) || string.IsNullOrWhiteSpace(dd.LineB))
            {
                continue;
            }

            if (!detectedLines.TryGetValue(dd.LineA, out var la) || !detectedLines.TryGetValue(dd.LineB, out var lb) || !la.Found || !lb.Found)
            {
                continue;
            }

            var (distPx, ca, cb) = Geometry2D.SegmentToSegmentDistance(la.P1, la.P2, lb.P1, lb.P2);
            var mm = VisionConfig.PixelsPerMm > 0 ? distPx / VisionConfig.PixelsPerMm : distPx;
            var pass = mm >= (dd.Nominal - dd.ToleranceMinus) && mm <= (dd.Nominal + dd.TolerancePlus);

            OverlayItems.Add(new OverlayLineItem
            {
                X1 = ca.X,
                Y1 = ca.Y,
                X2 = cb.X,
                Y2 = cb.Y,
                Stroke = pass ? Brushes.Lime : Brushes.Red,
                Label = $"{dd.Name}: {mm:0.00} mm"
            });
        }

        foreach (var dd in VisionConfig.PointToLineDistances)
        {
            if (string.IsNullOrWhiteSpace(dd.Name) || string.IsNullOrWhiteSpace(dd.Point) || string.IsNullOrWhiteSpace(dd.Line))
            {
                continue;
            }

            var p = VisionConfig.Points.FirstOrDefault(x => string.Equals(x.Name, dd.Point, StringComparison.OrdinalIgnoreCase));
            if (p is null)
            {
                continue;
            }

            if (!detectedLines.TryGetValue(dd.Line, out var l) || !l.Found)
            {
                continue;
            }

            var pp = new Point2d(p.WorldPosition.X, p.WorldPosition.Y);
            var (distPx, closest) = Geometry2D.PointToSegmentDistance(pp, l.P1, l.P2);
            var mm = VisionConfig.PixelsPerMm > 0 ? distPx / VisionConfig.PixelsPerMm : distPx;
            var pass = mm >= (dd.Nominal - dd.ToleranceMinus) && mm <= (dd.Nominal + dd.TolerancePlus);

            OverlayItems.Add(new OverlayLineItem
            {
                X1 = pp.X,
                Y1 = pp.Y,
                X2 = closest.X,
                Y2 = closest.Y,
                Stroke = pass ? Brushes.Lime : Brushes.Red,
                Label = $"{dd.Name}: {mm:0.00} mm"
            });
        }

        foreach (var d in VisionConfig.Distances)
        {
            var pa = VisionConfig.Points.FirstOrDefault(x => string.Equals(x.Name, d.PointA, StringComparison.OrdinalIgnoreCase));
            var pb = VisionConfig.Points.FirstOrDefault(x => string.Equals(x.Name, d.PointB, StringComparison.OrdinalIgnoreCase));
            if (pa is null || pb is null)
            {
                continue;
            }

            var dx = pb.WorldPosition.X - pa.WorldPosition.X;
            var dy = pb.WorldPosition.Y - pa.WorldPosition.Y;
            var distPx = Math.Sqrt(dx * dx + dy * dy);
            var mm = VisionConfig.PixelsPerMm > 0 ? distPx / VisionConfig.PixelsPerMm : distPx;

            OverlayItems.Add(new OverlayLineItem
            {
                X1 = pa.WorldPosition.X,
                Y1 = pa.WorldPosition.Y,
                X2 = pb.WorldPosition.X,
                Y2 = pb.WorldPosition.Y,
                Stroke = Brushes.Yellow,
                Label = $"{d.Name}: {mm:0.00} mm"
            });
        }

        if (VisionConfig.DefectConfig.InspectRoi.Width > 0 && VisionConfig.DefectConfig.InspectRoi.Height > 0)
        {
            OverlayItems.Add(new OverlayRectItem
            {
                X = VisionConfig.DefectConfig.InspectRoi.X,
                Y = VisionConfig.DefectConfig.InspectRoi.Y,
                Width = VisionConfig.DefectConfig.InspectRoi.Width,
                Height = VisionConfig.DefectConfig.InspectRoi.Height,
                Stroke = Brushes.Orange,
                Label = "DefectROI"
            });
        }
    }
}
