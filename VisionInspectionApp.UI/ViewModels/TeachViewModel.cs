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
    private readonly UndoRedoManager _undo;

    private Mat? _imageMat;

    public TeachViewModel(IConfigService configService, ConfigStoreOptions storeOptions, ImagePreprocessor preprocessor, UndoRedoManager undo)
    {
        _configService = configService;
        _storeOptions = storeOptions;
        _preprocessor = preprocessor;
        _undo = undo;

        VisionConfig = new VisionConfig { ProductCode = "ProductA" };

        AvailableConfigs = new ObservableCollection<string>();

        Targets = new ObservableCollection<TeachTarget>(Enum.GetValues<TeachTarget>());
        SelectedTarget = TeachTarget.Origin;

        Points = new ObservableCollection<string>();
        Distances = new ObservableCollection<LineDistance>();

        LoadImageCommand = new RelayCommand(LoadImage);
        RoiSelectedCommand = new RelayCommand<Roi?>(OnRoiSelected);
        RoiEditedCommand = new RelayCommand<RoiSelection?>(OnRoiEdited);
        AddPointCommand = new RelayCommand(AddPoint);
        AddDistanceCommand = new RelayCommand(AddDistance);
        UpdateDistanceCommand = new RelayCommand(UpdateDistance);
        DeletePointCommand = new RelayCommand(DeletePoint);
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

    public ObservableCollection<string> Points { get; }

    [ObservableProperty]
    private string? _selectedPoint;

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

    public ObservableCollection<LineDistance> Distances { get; }

    public ObservableCollection<OverlayItem> OverlayItems { get; } = new();

    public ICommand LoadImageCommand { get; }

    public ICommand RefreshConfigsCommand { get; }

    public ICommand LoadConfigCommand { get; }

    public ICommand NewConfigCommand { get; }

    public ICommand RoiSelectedCommand { get; }

    public ICommand RoiEditedCommand { get; }

    public ICommand AddPointCommand { get; }

    public ICommand AddDistanceCommand { get; }

    public ICommand UpdateDistanceCommand { get; }

    public ICommand DeletePointCommand { get; }

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
        var beforeOriginWorld = VisionConfig.Origin.WorldPosition;
        var beforeDefectRoi = VisionConfig.DefectConfig.InspectRoi;

        var createdPoint = false;
        var pointName = string.Empty;
        PointDefinition? pointDef = null;
        Roi beforePointSearch = new();
        Roi beforePointTemplate = new();
        string beforePointTemplateFile = string.Empty;
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
                VisionConfig.Origin.WorldPosition = beforeOriginWorld;
                if (beforeOriginTemplate.Width > 0 && beforeOriginTemplate.Height > 0)
                {
                    VisionConfig.Origin.TemplateImageFile = SaveTemplateImage("origin", beforeOriginTemplate);
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
                            existing.WorldPosition = beforePointWorld;
                            if (beforePointTemplate.Width > 0 && beforePointTemplate.Height > 0)
                            {
                                existing.TemplateImageFile = SaveTemplateImage(pointName.ToLowerInvariant(), beforePointTemplate);
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
        VisionConfig.Distances.Clear();
        VisionConfig.DefectConfig = new DefectInspectionConfig();

        Points.Clear();
        Distances.Clear();

        RaisePreprocessPropertiesChanged();
        RefreshDisplayedImage();
        RefreshOverlayItems();
    }

    private void ApplyConfig(VisionConfig cfg)
    {
        _undo.Clear();
        ProductCode = cfg.ProductCode;
        VisionConfig.ProductCode = cfg.ProductCode;
        VisionConfig.Preprocess = cfg.Preprocess;
        VisionConfig.Origin = cfg.Origin;
        VisionConfig.DefectConfig = cfg.DefectConfig;

        VisionConfig.Points.Clear();
        foreach (var p in cfg.Points)
        {
            VisionConfig.Points.Add(p);
        }

        VisionConfig.Distances.Clear();
        foreach (var d in cfg.Distances)
        {
            VisionConfig.Distances.Add(d);
        }

        Points.Clear();
        foreach (var p in VisionConfig.Points)
        {
            if (!string.IsNullOrWhiteSpace(p.Name) && !Points.Contains(p.Name))
            {
                Points.Add(p.Name);
            }
        }

        Distances.Clear();
        foreach (var d in VisionConfig.Distances)
        {
            Distances.Add(d);
        }

        SelectedConfig = cfg.ProductCode;
        SelectedPoint = Points.Count > 0 ? Points[0] : null;
        OnPropertyChanged(nameof(PixelsPerMm));

        RaisePreprocessPropertiesChanged();
        RefreshDisplayedImage();
    }

    private void LoadImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp|All Files|*.*"
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        _imageMat?.Dispose();
        _imageMat = Cv2.ImRead(dlg.FileName, ImreadModes.Color);
        RefreshDisplayedImage();

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
        var beforeOriginWorld = VisionConfig.Origin.WorldPosition;
        var beforeDefectRoi = VisionConfig.DefectConfig.InspectRoi;

        var pointName = SelectedPoint;
        var pointDef = (!string.IsNullOrWhiteSpace(pointName))
            ? VisionConfig.Points.FirstOrDefault(x => string.Equals(x.Name, pointName, StringComparison.OrdinalIgnoreCase))
            : null;

        var beforePointSearch = pointDef?.SearchRoi ?? new Roi();
        var beforePointTemplate = pointDef?.TemplateRoi ?? new Roi();
        var beforePointTemplateFile = pointDef?.TemplateImageFile ?? string.Empty;
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
                VisionConfig.Origin.WorldPosition = beforeOriginWorld;
                if (beforeOriginTemplate.Width > 0 && beforeOriginTemplate.Height > 0)
                {
                    VisionConfig.Origin.TemplateImageFile = SaveTemplateImage("origin", beforeOriginTemplate);
                }

                if (pointDef is not null)
                {
                    pointDef.SearchRoi = beforePointSearch;
                    pointDef.TemplateRoi = beforePointTemplate;
                    pointDef.TemplateImageFile = beforePointTemplateFile;
                    pointDef.WorldPosition = beforePointWorld;
                    if (beforePointTemplate.Width > 0 && beforePointTemplate.Height > 0 && !string.IsNullOrWhiteSpace(pointDef.Name))
                    {
                        pointDef.TemplateImageFile = SaveTemplateImage(pointDef.Name.ToLowerInvariant(), beforePointTemplate);
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

                SelectedPoint = Points.Count > 0 ? Points[0] : null;
                SelectedDistance = Distances.Count > 0 ? Distances[0] : null;
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

                SelectedPoint = beforeSelectedPoint;
                SelectedDistance = beforeSelectedDistance;
                RefreshOverlayItems();
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
