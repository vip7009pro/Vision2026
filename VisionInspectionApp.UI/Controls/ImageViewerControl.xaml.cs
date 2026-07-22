using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.Controls;

public partial class ImageViewerControl : UserControl
{
    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource),
        typeof(ImageSource),
        typeof(ImageViewerControl),
        new PropertyMetadata(null, OnImageSourceChanged));

    public static readonly DependencyProperty EnableLineSelectionProperty = DependencyProperty.Register(
        nameof(EnableLineSelection),
        typeof(bool),
        typeof(ImageViewerControl),
        new PropertyMetadata(false));

    public static readonly DependencyProperty LineSelectedCommandProperty = DependencyProperty.Register(
        nameof(LineSelectedCommand),
        typeof(ICommand),
        typeof(ImageViewerControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty RoiSelectedCommandProperty = DependencyProperty.Register(
        nameof(RoiSelectedCommand),
        typeof(ICommand),
        typeof(ImageViewerControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty EnableRoiEditingProperty = DependencyProperty.Register(
        nameof(EnableRoiEditing),
        typeof(bool),
        typeof(ImageViewerControl),
        new PropertyMetadata(false));

    public static readonly DependencyProperty RoiEditedCommandProperty = DependencyProperty.Register(
        nameof(RoiEditedCommand),
        typeof(ICommand),
        typeof(ImageViewerControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty PointClickedCommandProperty = DependencyProperty.Register(
        nameof(PointClickedCommand),
        typeof(ICommand),
        typeof(ImageViewerControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty PointDoubleClickedCommandProperty = DependencyProperty.Register(
        nameof(PointDoubleClickedCommand),
        typeof(ICommand),
        typeof(ImageViewerControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty RoiDeletedCommandProperty = DependencyProperty.Register(
        nameof(RoiDeletedCommand),
        typeof(ICommand),
        typeof(ImageViewerControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ActiveRoiLabelProperty = DependencyProperty.Register(
        nameof(ActiveRoiLabel),
        typeof(string),
        typeof(ImageViewerControl),
        new PropertyMetadata(null, OnActiveRoiLabelChanged));

    public static readonly DependencyProperty OverlayItemsProperty = DependencyProperty.Register(
        nameof(OverlayItems),
        typeof(IEnumerable<OverlayItem>),
        typeof(ImageViewerControl),
        new PropertyMetadata(null, OnOverlayItemsChanged));

    public ImageViewerControl()
    {
        InitializeComponent();

        PART_Overlay.MouseLeftButtonDown += OverlayOnMouseLeftButtonDown;
        PART_Overlay.MouseMove += OverlayOnMouseMove;
        PART_Overlay.MouseLeftButtonUp += OverlayOnMouseLeftButtonUp;

        PART_Overlay.KeyDown += OverlayOnKeyDown;

        PART_Overlay.SizeChanged += (_, __) => RedrawOverlays();

        Loaded += (_, __) => RedrawOverlays();

        SetupTransforms();
    }

    private readonly MatrixTransform _transform = new();

    private bool _panning;
    private Point _panStart;
    private Matrix _panStartMatrix;

    private bool _isViewInitialized;

    private void SetupTransforms()
    {
        PART_Content.RenderTransform = _transform;
        PART_Content.RenderTransformOrigin = new Point(0, 0);

        PART_RootGrid.PreviewMouseWheel += RootOnPreviewMouseWheel;
        PART_RootGrid.MouseDown += RootOnMouseDown;
        PART_RootGrid.MouseMove += RootOnMouseMove;
        PART_RootGrid.MouseUp += RootOnMouseUp;
        PART_RootGrid.SizeChanged += OnRootGridSizeChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isViewInitialized && PART_RootGrid.ActualWidth > 0 && PART_RootGrid.ActualHeight > 0)
        {
            _isViewInitialized = true;
            ResetView();
        }
        RedrawOverlays();
    }

    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isViewInitialized && PART_RootGrid.ActualWidth > 0 && PART_RootGrid.ActualHeight > 0)
        {
            _isViewInitialized = true;
            ResetView();
        }
    }

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public bool EnableLineSelection
    {
        get => (bool)GetValue(EnableLineSelectionProperty);
        set => SetValue(EnableLineSelectionProperty, value);
    }

    public ICommand? LineSelectedCommand
    {
        get => (ICommand?)GetValue(LineSelectedCommandProperty);
        set => SetValue(LineSelectedCommandProperty, value);
    }

    private int _lastPixelWidth;
    private int _lastPixelHeight;

    private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ImageViewerControl)d;
        if (c.PART_FastOverlay != null)
        {
            c.PART_FastOverlay.ImageSource = e.NewValue as ImageSource;
        }

        var newBmp = e.NewValue as BitmapSource;
        if (newBmp is null)
        {
            c._lastPixelWidth = 0;
            c._lastPixelHeight = 0;
            c.ResetView();
        }
        else
        {
            var changed = c._lastPixelWidth != newBmp.PixelWidth || c._lastPixelHeight != newBmp.PixelHeight;
            c._lastPixelWidth = newBmp.PixelWidth;
            c._lastPixelHeight = newBmp.PixelHeight;
            if (changed || c._transform.Matrix.IsIdentity)
            {
                c.ResetView();
            }
        }
        c.Dispatcher.BeginInvoke(new Action(c.RedrawOverlays), System.Windows.Threading.DispatcherPriority.Render);
    }

    public void ResetView()
    {
        _panning = false;
        if (ImageSource is not BitmapSource bmp || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0)
        {
            _transform.Matrix = Matrix.Identity;
            UpdateInfoText();
            return;
        }

        double containerW = PART_RootGrid?.ActualWidth ?? 0;
        double containerH = PART_RootGrid?.ActualHeight ?? 0;

        if (containerW <= 0 || containerH <= 0)
        {
            Dispatcher.BeginInvoke(new Action(ResetView), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        double imgW = bmp.Width;
        double imgH = bmp.Height;

        if (imgW <= 0 || imgH <= 0)
        {
            _transform.Matrix = Matrix.Identity;
            UpdateInfoText();
            return;
        }

        var scale = Math.Min(containerW / imgW, containerH / imgH);
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) scale = 1.0;

        var tx = (containerW - imgW * scale) / 2.0;
        var ty = (containerH - imgH * scale) / 2.0;

        var m = Matrix.Identity;
        m.Scale(scale, scale);
        m.Translate(tx, ty);
        _transform.Matrix = m;

        UpdateInfoText();
        RedrawOverlays();
    }

    private void UpdateInfoText()
    {
        if (PART_InfoText != null)
        {
            if (_lastPixelWidth == 0 || _lastPixelHeight == 0)
            {
                PART_InfoText.Text = string.Empty;
                PART_InfoText.Visibility = Visibility.Collapsed;
            }
            else
            {
                var z = _transform.Matrix.M11 * 100.0;
                PART_InfoText.Text = $"{_lastPixelWidth} x {_lastPixelHeight} px  |  Zoom: {z:F0}%";
                PART_InfoText.Visibility = Visibility.Visible;
            }
        }
    }

    private Point ContainerToContent(Point pContainer)
    {
        var m = _transform.Matrix;
        if (!m.HasInverse) return pContainer;
        m.Invert();
        return m.Transform(pContainer);
    }

    private Point ViewToContent(Point overlayPoint) => overlayPoint;

    private void RootOnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mousePos = e.GetPosition(PART_RootGrid);
        var zoomFactor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;

        var currentScale = _transform.Matrix.M11;
        var newScale = currentScale * zoomFactor;
        if (newScale < 0.001 || newScale > 500.0)
        {
            return;
        }

        var m = _transform.Matrix;
        m.ScaleAt(zoomFactor, zoomFactor, mousePos.X, mousePos.Y);
        _transform.Matrix = m;

        UpdateInfoText();
        RedrawOverlays();
        e.Handled = true;
    }

    private void RootOnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = true;
            _panStart = e.GetPosition(PART_RootGrid);
            _panStartMatrix = _transform.Matrix;
            PART_RootGrid.CaptureMouse();
            e.Handled = true;
        }
    }

    private void RootOnMouseMove(object sender, MouseEventArgs e)
    {
        if (_panning)
        {
            var current = e.GetPosition(PART_RootGrid);
            var dx = current.X - _panStart.X;
            var dy = current.Y - _panStart.Y;

            var m = _panStartMatrix;
            m.Translate(dx, dy);
            _transform.Matrix = m;
            e.Handled = true;
            return;
        }

        if (EnableRoiEditing && !_roiEditing && !_dragging && !_lineDragging && ImageSource is BitmapSource bmp && OverlayItems is not null)
        {
            var contentPos = ContainerToContent(e.GetPosition(PART_RootGrid));
            var hover = FindHoverRoiLabel(bmp, contentPos);
            UpdateCursorForRoiHover(bmp, contentPos, hover);
            if (!string.Equals(_hoverRoiLabel, hover, StringComparison.OrdinalIgnoreCase))
            {
                _hoverRoiLabel = hover;
                RedrawOverlays();
            }
        }
    }

    private void RootOnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _panning)
        {
            _panning = false;
            PART_RootGrid.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    public ICommand? RoiSelectedCommand
    {
        get => (ICommand?)GetValue(RoiSelectedCommandProperty);
        set => SetValue(RoiSelectedCommandProperty, value);
    }

    public bool EnableRoiEditing
    {
        get => (bool)GetValue(EnableRoiEditingProperty);
        set => SetValue(EnableRoiEditingProperty, value);
    }

    public ICommand? RoiEditedCommand
    {
        get => (ICommand?)GetValue(RoiEditedCommandProperty);
        set => SetValue(RoiEditedCommandProperty, value);
    }

    public ICommand? PointClickedCommand
    {
        get => (ICommand?)GetValue(PointClickedCommandProperty);
        set => SetValue(PointClickedCommandProperty, value);
    }

    public ICommand? PointDoubleClickedCommand
    {
        get => (ICommand?)GetValue(PointDoubleClickedCommandProperty);
        set => SetValue(PointDoubleClickedCommandProperty, value);
    }

    public ICommand? RoiDeletedCommand
    {
        get => (ICommand?)GetValue(RoiDeletedCommandProperty);
        set => SetValue(RoiDeletedCommandProperty, value);
    }

    public string? ActiveRoiLabel
    {
        get => (string?)GetValue(ActiveRoiLabelProperty);
        set => SetValue(ActiveRoiLabelProperty, value);
    }

    private static void OnActiveRoiLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ImageViewerControl)d;
        c._activeRoiLabel = e.NewValue as string;
        c.Dispatcher.BeginInvoke(new Action(c.RedrawOverlays), System.Windows.Threading.DispatcherPriority.Render);
    }

    public IEnumerable<OverlayItem>? OverlayItems
    {
        get => (IEnumerable<OverlayItem>?)GetValue(OverlayItemsProperty);
        set => SetValue(OverlayItemsProperty, value);
    }

    private static void OnOverlayItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ImageViewerControl)d;
        if (c.PART_FastOverlay != null)
        {
            c.PART_FastOverlay.OverlayItems = e.NewValue as IEnumerable<OverlayItem>;
        }
        c.Dispatcher.BeginInvoke(new Action(c.RedrawOverlays), System.Windows.Threading.DispatcherPriority.Render);
    }

    /// <summary>
    /// Returns true if the point is within 'tolerance' of any edge of the rect
    /// (but not deep inside). This enables edge-only hit testing for ROI move.
    /// </summary>
    private static bool IsNearRoiBorder(Rect rect, Point p, double tolerance)
    {
        var expanded = rect;
        expanded.Inflate(tolerance, tolerance);
        if (!expanded.Contains(p)) return false;

        var shrunk = rect;
        shrunk.Inflate(-tolerance, -tolerance);
        // If shrunk rect still contains the point, it's deep inside -> not near border
        if (shrunk.Width > 0 && shrunk.Height > 0 && shrunk.Contains(p)) return false;

        return true;
    }

    private string? FindHoverRoiLabel(BitmapSource bmp, Point contentPos)
    {
        if (OverlayItems is null)
        {
            return null;
        }

        // Detect hover when mouse is near the border/edge of a ROI (not deep inside).
        // This allows overlapping ROIs to each be individually selectable by their edges.
        const double borderTol = 12.0;
        var rects = OverlayItems.OfType<OverlayRectItem>().ToList();
        for (var i = rects.Count - 1; i >= 0; i--)
        {
            var r = rects[i];
            if (r.Width <= 0 || r.Height <= 0) continue;
            var roiRect = new Rect(r.X, r.Y, r.Width, r.Height);
            if (IsNearRoiBorder(roiRect, contentPos, borderTol))
            {
                return r.Label;
            }
        }

        return null;
    }

    private string? GetDesiredRoiLabelForDraw(RoiDrawKind kind)
    {
        if (OverlayItems is null)
        {
            return _activeRoiLabel;
        }

        var rectLabels = OverlayItems
            .OfType<OverlayRectItem>()
            .Select(x => x.Label)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .ToList();

        bool EndsWith(string label, string suffix) => label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

        // Helper: if active label is for the same tool name, try swapping suffix.
        // Important: when teaching a brand-new Template ROI, the "... T" overlay may not exist yet.
        // In that case we still return the expected label so the VM can create/update it.
        string? TrySwapSuffix(string? active, string toSuffix)
        {
            if (string.IsNullOrWhiteSpace(active)) return null;
            var parts = active.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return null;
            return $"{parts[0]} {toSuffix}";
        }

        if (kind == RoiDrawKind.Template)
        {
            // Prefer active SCT or T; otherwise swap SC->SCT or S->T; otherwise any SCT/T in overlays.
            if (!string.IsNullOrWhiteSpace(_activeRoiLabel) && (EndsWith(_activeRoiLabel!, " SCT") || EndsWith(_activeRoiLabel!, " T"))) return _activeRoiLabel;

            var swapped = TrySwapSuffix(_activeRoiLabel, "SCT") ?? TrySwapSuffix(_activeRoiLabel, "T");
            if (!string.IsNullOrWhiteSpace(swapped)) return swapped;

            var sct = rectLabels.FirstOrDefault(x => EndsWith(x, " SCT"));
            if (!string.IsNullOrWhiteSpace(sct)) return sct;

            var t = rectLabels.FirstOrDefault(x => EndsWith(x, " T"));
            if (!string.IsNullOrWhiteSpace(t)) return t;

            // If we only have a Search ROI overlay, synthesize a matching Template label.
            var sc = rectLabels.FirstOrDefault(x => EndsWith(x, " SC"));
            if (!string.IsNullOrWhiteSpace(sc))
            {
                var parts = sc.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    return $"{parts[0]} SCT";
                }
            }

            var s = rectLabels.FirstOrDefault(x => EndsWith(x, " S"));
            if (!string.IsNullOrWhiteSpace(s))
            {
                var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    return $"{parts[0]} T";
                }
            }

            if (rectLabels.Any(x => string.Equals(x, "Origin S", StringComparison.OrdinalIgnoreCase)))
            {
                return "Origin T";
            }

            return _activeRoiLabel;
        }

        if (kind == RoiDrawKind.Search)
        {
            // For Search: SC, S, L, LP, C.
            if (!string.IsNullOrWhiteSpace(_activeRoiLabel)
                && (EndsWith(_activeRoiLabel!, " SC")
                    || EndsWith(_activeRoiLabel!, " S")
                    || EndsWith(_activeRoiLabel!, " L")
                    || EndsWith(_activeRoiLabel!, " LP")
                    || EndsWith(_activeRoiLabel!, " C")))
            {
                return _activeRoiLabel;
            }

            var swapped = TrySwapSuffix(_activeRoiLabel, "SC") ?? TrySwapSuffix(_activeRoiLabel, "S");
            if (!string.IsNullOrWhiteSpace(swapped)) return swapped;

            var sc = rectLabels.FirstOrDefault(x => EndsWith(x, " SC"));
            if (!string.IsNullOrWhiteSpace(sc)) return sc;

            var s = rectLabels.FirstOrDefault(x => EndsWith(x, " S"));
            if (!string.IsNullOrWhiteSpace(s)) return s;

            var l = rectLabels.FirstOrDefault(x => EndsWith(x, " L"));
            if (!string.IsNullOrWhiteSpace(l)) return l;

            var lp = rectLabels.FirstOrDefault(x => EndsWith(x, " LP"));
            if (!string.IsNullOrWhiteSpace(lp)) return lp;

            var c = rectLabels.FirstOrDefault(x => EndsWith(x, " C"));
            if (!string.IsNullOrWhiteSpace(c)) return c;
        }

        return _activeRoiLabel;
    }

    private bool _dragging;
    private Point _start;
    private Rectangle? _rect;
    private Line? _dragCrosshairH;
    private Line? _dragCrosshairV;

    private enum RoiDrawKind
    {
        None = 0,
        Search = 1,
        Template = 2
    }

    private static bool IsTemplateRoiLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        return label.EndsWith(" SCT", StringComparison.OrdinalIgnoreCase)
            || label.EndsWith(" T", StringComparison.OrdinalIgnoreCase)
            || string.Equals(label, "Origin T", StringComparison.OrdinalIgnoreCase);
    }

    private RoiDrawKind _roiDrawKind;

    private bool _lineDragging;
    private Point _lineStart;
    private Line? _line;

    private enum RoiEditMode
    {
        None = 0,
        Move = 1,
        Left = 2,
        Right = 3,
        Top = 4,
        Bottom = 5,
        TopLeft = 6,
        TopRight = 7,
        BottomLeft = 8,
        BottomRight = 9
    }

    private bool _roiEditing;
    private RoiEditMode _roiEditMode;
    private string? _roiEditLabel;
    private Rect _roiEditRectStart;
    private Rect _roiEditRect;
    private Point _roiEditStart;
    private Rectangle? _roiEditRectShape;
    private readonly List<Rectangle> _roiEditHandles = new();

    private string? _activeRoiLabel;
    private string? _hoverRoiLabel;

    private void OverlayOnKeyDown(object sender, KeyEventArgs e)
    {
        if (!EnableRoiEditing)
        {
            return;
        }

        if (e.Key != Key.Delete)
        {
            return;
        }

        var label = _activeRoiLabel;
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        if (RoiDeletedCommand?.CanExecute(label) == true)
        {
            RoiDeletedCommand.Execute(label);
            e.Handled = true;
        }
    }

    private void RedrawOverlays()
    {
        if (_panning || _dragging)
        {
            return;
        }

        PART_Overlay.Children.Clear();

        if (ImageSource is not BitmapSource bmp)
        {
            return;
        }

        PART_Image.Source = bmp;

        PART_Content.Width = bmp.PixelWidth;
        PART_Content.Height = bmp.PixelHeight;

        PART_Image.Width = bmp.PixelWidth;
        PART_Image.Height = bmp.PixelHeight;

        PART_Overlay.Width = bmp.PixelWidth;
        PART_Overlay.Height = bmp.PixelHeight;

        PART_FastOverlay.Width = bmp.PixelWidth;
        PART_FastOverlay.Height = bmp.PixelHeight;
        PART_FastOverlay.ViewScale = Math.Max(0.001, _transform.Matrix.M11);
        PART_FastOverlay.InvalidateVisual();

        if (OverlayItems is null)
        {
            return;
        }

        foreach (var item in OverlayItems)
        {
            if (item is OverlayRectItem r)
            {
                var showHandles = (!string.IsNullOrWhiteSpace(r.Label)
                    && (
                        string.Equals(r.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.Label, _hoverRoiLabel, StringComparison.OrdinalIgnoreCase)
                    ));

                if (showHandles)
                {
                    DrawRoiHandles(r.X, r.Y, r.Width, r.Height, string.Equals(r.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase));
                }
            }
        }
    }

    private void OverlayOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ImageSource is null)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            var viewPos = ViewToContent(e.GetPosition(PART_Overlay));
            var payload = new PointClickSelection(viewPos.X, viewPos.Y, Keyboard.Modifiers);
            if (PointDoubleClickedCommand is not null && PointDoubleClickedCommand.CanExecute(payload))
            {
                PointDoubleClickedCommand.Execute(payload);
                e.Handled = true;
                return;
            }
        }

        PART_Overlay.Focus();

        if (_panning)
        {
            return;
        }

        if (EnableRoiEditing
            && !EnableLineSelection
            && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (ImageSource is BitmapSource bmpEdit)
            {
                var viewPos = ViewToContent(e.GetPosition(PART_Overlay));

                var active = FindRoiLabelAtForClick(bmpEdit, viewPos);
                if (!string.IsNullOrWhiteSpace(active) && !string.Equals(_activeRoiLabel, active, StringComparison.OrdinalIgnoreCase))
                {
                    _activeRoiLabel = active;
                    RedrawOverlays();
                }

                if (TryStartRoiEdit(bmpEdit, viewPos))
                {
                    PART_Overlay.CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }
        }

        if (EnableLineSelection
            && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _lineDragging = true;
            _lineStart = ViewToContent(e.GetPosition(PART_Overlay));
            PART_Overlay.CaptureMouse();

            PART_Overlay.Children.Clear();
            _line = new Line
            {
                X1 = _lineStart.X,
                Y1 = _lineStart.Y,
                X2 = _lineStart.X,
                Y2 = _lineStart.Y,
                Stroke = Brushes.Lime,
                StrokeThickness = 2
            };
            PART_Overlay.Children.Add(_line);
            return;
        }

        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _panning = true;
            _panStart = e.GetPosition(PART_RootGrid);
            _panStartMatrix = _transform.Matrix;
            PART_Overlay.CaptureMouse();
            Cursor = Cursors.Hand;
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var viewPos = ViewToContent(e.GetPosition(PART_Overlay));
            var payload = new PointClickSelection(viewPos.X, viewPos.Y, Keyboard.Modifiers);
            if (PointClickedCommand is not null && PointClickedCommand.CanExecute(payload))
            {
                PointClickedCommand.Execute(payload);
                e.Handled = true;
                return;
            }
        }

        // Deterministic ROI teaching gesture:
        // - Ctrl + drag => Search ROI
        // - Shift + drag => Template ROI
        // If both are held, prefer Template (Shift).
        // Exception: for BlobDetection ROI labels ("... B" or "... B#"), keep label stable regardless of Shift.
        var isBlobLabel = !string.IsNullOrWhiteSpace(_activeRoiLabel)
            && _activeRoiLabel!.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: 2 } parts
            && parts[1].StartsWith("B", StringComparison.OrdinalIgnoreCase);

        _roiDrawKind = (!isBlobLabel && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            ? RoiDrawKind.Template
            : RoiDrawKind.Search;

        _dragging = true;
        _start = ViewToContent(e.GetPosition(PART_Overlay));

        PART_Overlay.CaptureMouse();

        PART_Overlay.Children.Clear();
        _rect = new Rectangle
        {
            Stroke = Brushes.Lime,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(50, 0, 255, 0))
        };

        Canvas.SetLeft(_rect, _start.X);
        Canvas.SetTop(_rect, _start.Y);
        PART_Overlay.Children.Add(_rect);

        if (_roiDrawKind == RoiDrawKind.Template)
        {
            _dragCrosshairH = new Line { Stroke = Brushes.Lime, StrokeThickness = 2 };
            _dragCrosshairV = new Line { Stroke = Brushes.Lime, StrokeThickness = 2 };
            PART_Overlay.Children.Add(_dragCrosshairH);
            PART_Overlay.Children.Add(_dragCrosshairV);
        }
    }

    private void OverlayOnMouseMove(object sender, MouseEventArgs e)
    {
        if (_panning)
        {
            return;
        }

        if (EnableRoiEditing && !_roiEditing && !_dragging && !_lineDragging && ImageSource is BitmapSource bmp && OverlayItems is not null)
        {
            var viewPos = e.GetPosition(PART_Overlay);
            var contentPos = ViewToContent(viewPos);
            var hover = FindHoverRoiLabel(bmp, contentPos);
            UpdateCursorForRoiHover(bmp, contentPos, hover);
            if (!string.Equals(_hoverRoiLabel, hover, StringComparison.OrdinalIgnoreCase))
            {
                _hoverRoiLabel = hover;
                RedrawOverlays();
                return;
            }
        }

        if (_roiEditing)
        {
            var rp = ViewToContent(e.GetPosition(PART_Overlay));
            UpdateRoiEdit(rp);
            RedrawRoiEditOverlay();
            return;
        }

        if (_lineDragging && _line is not null)
        {
            var lp = ViewToContent(e.GetPosition(PART_Overlay));
            _line.X2 = lp.X;
            _line.Y2 = lp.Y;
            return;
        }

        if (!_dragging || _rect is null)
        {
            return;
        }

        var cp = ViewToContent(e.GetPosition(PART_Overlay));
        var x = Math.Min(cp.X, _start.X);
        var y = Math.Min(cp.Y, _start.Y);
        var w = Math.Abs(cp.X - _start.X);
        var h = Math.Abs(cp.Y - _start.Y);

        Canvas.SetLeft(_rect, x);
        Canvas.SetTop(_rect, y);
        _rect.Width = Math.Max(1, w);
        _rect.Height = Math.Max(1, h);

        if (_roiDrawKind == RoiDrawKind.Template && _dragCrosshairH is not null && _dragCrosshairV is not null)
        {
            var left = x;
            var top = y;
            var right = x + _rect.Width;
            var bottom = y + _rect.Height;
            var cx = (left + right) / 2.0;
            var cy = (top + bottom) / 2.0;

            _dragCrosshairH.X1 = left;
            _dragCrosshairH.Y1 = cy;
            _dragCrosshairH.X2 = right;
            _dragCrosshairH.Y2 = cy;

            _dragCrosshairV.X1 = cx;
            _dragCrosshairV.Y1 = top;
            _dragCrosshairV.X2 = cx;
            _dragCrosshairV.Y2 = bottom;
        }
    }

    private void OverlayOnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning)
        {
            _panning = false;
            if (PART_Overlay.IsMouseCaptured)
            {
                PART_Overlay.ReleaseMouseCapture();
            }
            if (PART_RootGrid.IsMouseCaptured)
            {
                PART_RootGrid.ReleaseMouseCapture();
            }
            Cursor = Cursors.Arrow;
            e.Handled = true;
            return;
        }

        if (_roiEditing)
        {
            _roiEditing = false;
            PART_Overlay.ReleaseMouseCapture();

            if (ImageSource is BitmapSource bmpEdit)
            {
                var startRoi = ConvertContentRoiToPixelRoi(bmpEdit, _roiEditRectStart.X, _roiEditRectStart.Y, _roiEditRectStart.Width, _roiEditRectStart.Height);
                var editedRoi = ConvertContentRoiToPixelRoi(bmpEdit, _roiEditRect.X, _roiEditRect.Y, _roiEditRect.Width, _roiEditRect.Height);
                if (_roiEditLabel is not null)
                {
                    var changed = startRoi.X != editedRoi.X
                        || startRoi.Y != editedRoi.Y
                        || startRoi.Width != editedRoi.Width
                        || startRoi.Height != editedRoi.Height;

                    var sel = new RoiSelection(_roiEditLabel, editedRoi, ModifierKeys.None);
                    if (changed && RoiEditedCommand?.CanExecute(sel) == true)
                    {
                        RoiEditedCommand.Execute(sel);
                    }
                }
            }

            ClearRoiEditVisuals();
            RedrawOverlays();
            return;
        }

        if (_lineDragging)
        {
            _lineDragging = false;
            PART_Overlay.ReleaseMouseCapture();

            if (_line is null)
            {
                RedrawOverlays();
                return;
            }

            if (ImageSource is not BitmapSource bmpLine)
            {
                RedrawOverlays();
                return;
            }

            var a = ConvertContentPointToPixelPoint(bmpLine, _line.X1, _line.Y1);
            var b = ConvertContentPointToPixelPoint(bmpLine, _line.X2, _line.Y2);
            var sel = new LineSelection(a.X, a.Y, b.X, b.Y);

            if (LineSelectedCommand?.CanExecute(sel) == true)
            {
                LineSelectedCommand.Execute(sel);
            }

            RedrawOverlays();
            return;
        }

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        PART_Overlay.ReleaseMouseCapture();

        _dragCrosshairH = null;
        _dragCrosshairV = null;

        if (_rect is null)
        {
            return;
        }

        var x = Canvas.GetLeft(_rect);
        var y = Canvas.GetTop(_rect);
        var w = _rect.Width;
        var h = _rect.Height;

        if (w < 2 || h < 2)
        {
            return;
        }

        if (ImageSource is not BitmapSource bmp)
        {
            return;
        }

        var roi = ConvertContentRoiToPixelRoi(bmp, x, y, w, h);

        object arg = roi;
        if (EnableRoiEditing)
        {
            var desiredLabel = GetDesiredRoiLabelForDraw(_roiDrawKind);
            if (!string.IsNullOrWhiteSpace(desiredLabel))
            {
                arg = new RoiSelection(desiredLabel, roi, Keyboard.Modifiers);
            }
        }

        if (RoiSelectedCommand?.CanExecute(arg) == true)
        {
            RoiSelectedCommand.Execute(arg);
        }

        RedrawOverlays();
    }

    private static Roi ConvertContentRoiToPixelRoi(BitmapSource bmp, double contentX, double contentY, double contentW, double contentH)
    {
        var px = (int)Math.Round(contentX);
        var py = (int)Math.Round(contentY);
        var pw = (int)Math.Round(contentW);
        var ph = (int)Math.Round(contentH);

        var maxX = Math.Max(0, bmp.PixelWidth - 1);
        var maxY = Math.Max(0, bmp.PixelHeight - 1);
        px = Math.Clamp(px, 0, maxX);
        py = Math.Clamp(py, 0, maxY);

        var maxW = bmp.PixelWidth - px;
        var maxH = bmp.PixelHeight - py;
        if (maxW < 1) maxW = 1;
        if (maxH < 1) maxH = 1;

        pw = Math.Clamp(pw, 1, maxW);
        ph = Math.Clamp(ph, 1, maxH);

        return new Roi
        {
            X = px,
            Y = py,
            Width = pw,
            Height = ph
        };
    }

    private static Point ConvertContentPointToPixelPoint(BitmapSource bmp, double contentX, double contentY)
    {
        var px = (int)Math.Round(contentX);
        var py = (int)Math.Round(contentY);

        px = Math.Clamp(px, 0, Math.Max(0, bmp.PixelWidth - 1));
        py = Math.Clamp(py, 0, Math.Max(0, bmp.PixelHeight - 1));

        return new Point(px, py);
    }

    private double GetScreenHitTolerance()
    {
        var scale = Math.Max(0.001, _transform.Matrix.M11);
        return 14.0 / scale;
    }

    private bool TryStartRoiEdit(BitmapSource bmp, Point contentPoint)
    {
        if (OverlayItems is null)
        {
            return false;
        }

        var hitTol = GetScreenHitTolerance();
        var candidates = new List<(OverlayRectItem Item, Rect Rect)>();

        foreach (var item in OverlayItems)
        {
            if (item is not OverlayRectItem r || string.IsNullOrWhiteSpace(r.Label))
            {
                continue;
            }

            var baseRect = PixelRectToContentRect(bmp, r.X, r.Y, r.Width, r.Height);
            var hitRect = baseRect;
            hitRect.Inflate(hitTol, hitTol);
            if (!hitRect.Contains(contentPoint))
            {
                continue;
            }

            candidates.Add((r, baseRect));
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        // Direct border or handle hits get top priority (ordered by smallest rect area)
        var borderHits = candidates
            .Where(x => HitTestRoiHandle(x.Rect, contentPoint, hitTol) != RoiEditMode.None || IsNearRoiBorder(x.Rect, contentPoint, hitTol))
            .OrderBy(x => x.Rect.Width * x.Rect.Height)
            .ToList();

        (OverlayRectItem Item, Rect Rect) picked;
        if (borderHits.Count > 0)
        {
            picked = borderHits.First();
        }
        else if (!string.IsNullOrWhiteSpace(_activeRoiLabel) && candidates.Any(x => string.Equals(x.Item.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase)))
        {
            picked = candidates.First(x => string.Equals(x.Item.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            picked = candidates.OrderBy(x => x.Rect.Width * x.Rect.Height).First();
        }

        _roiEditing = true;
        _roiEditLabel = picked.Item.Label;
        _activeRoiLabel = picked.Item.Label;
        _roiEditStart = contentPoint;
        _roiEditRectStart = picked.Rect;
        _roiEditRect = picked.Rect;
        _roiEditMode = HitTestRoiHandle(picked.Rect, contentPoint, tolerance: hitTol);
        if (_roiEditMode == RoiEditMode.None)
        {
            // Move mode is triggered by clicking near the edge/border, not deep inside
            _roiEditMode = IsNearRoiBorder(picked.Rect, contentPoint, hitTol) ? RoiEditMode.Move : RoiEditMode.None;
        }

        if (_roiEditMode == RoiEditMode.None)
        {
            _roiEditing = false;
            return false;
        }

        RedrawRoiEditOverlay();
        return true;
    }

    private string? FindTopRoiLabelAt(BitmapSource bmp, Point contentPoint)
    {
        if (OverlayItems is null)
        {
            return null;
        }

        var hitTol = GetScreenHitTolerance();
        var candidates = new List<(OverlayRectItem Item, Rect Rect)>();

        foreach (var item in OverlayItems)
        {
            if (item is not OverlayRectItem r || string.IsNullOrWhiteSpace(r.Label))
            {
                continue;
            }

            var baseRect = PixelRectToContentRect(bmp, r.X, r.Y, r.Width, r.Height);
            var hitRect = baseRect;
            hitRect.Inflate(hitTol, hitTol);
            if (!hitRect.Contains(contentPoint))
            {
                continue;
            }

            candidates.Add((r, baseRect));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var borderHits = candidates
            .Where(x => HitTestRoiHandle(x.Rect, contentPoint, hitTol) != RoiEditMode.None || IsNearRoiBorder(x.Rect, contentPoint, hitTol))
            .OrderBy(x => x.Rect.Width * x.Rect.Height)
            .ToList();

        if (borderHits.Count > 0)
        {
            return borderHits.First().Item.Label;
        }

        return candidates.OrderBy(x => x.Rect.Width * x.Rect.Height).First().Item.Label;
    }

    private string? FindRoiLabelAtForClick(BitmapSource bmp, Point contentPoint)
    {
        return FindTopRoiLabelAt(bmp, contentPoint);
    }

    private void DrawRoiHandles(double left, double top, double width, double height, bool isActive)
    {
        var scale = Math.Max(0.001, _transform.Matrix.M11);
        var corner = (isActive ? 12.0 : 10.0) / scale;
        var edge = (isActive ? 8.0 : 6.0) / scale;
        var stroke = isActive ? Brushes.Cyan : Brushes.DeepSkyBlue;

        AddHandle(left, top, corner);
        AddHandle(left + width, top, corner);
        AddHandle(left, top + height, corner);
        AddHandle(left + width, top + height, corner);

        AddHandle(left + width / 2.0, top, edge);
        AddHandle(left + width / 2.0, top + height, edge);
        AddHandle(left, top + height / 2.0, edge);
        AddHandle(left + width, top + height / 2.0, edge);

        void AddHandle(double x, double y, double size)
        {
            var h = new Rectangle
            {
                Width = size,
                Height = size,
                Stroke = stroke,
                StrokeThickness = 1.5 / scale,
                Fill = Brushes.Black
            };

            Canvas.SetLeft(h, x - size / 2.0);
            Canvas.SetTop(h, y - size / 2.0);
            PART_Overlay.Children.Add(h);
        }
    }

    private void UpdateCursorForRoiHover(BitmapSource bmp, Point contentPoint, string? hoverLabel)
    {
        if (string.IsNullOrWhiteSpace(hoverLabel) || OverlayItems is null)
        {
            if (!_roiEditing)
            {
                Cursor = Cursors.Arrow;
            }
            return;
        }

        var roiItem = OverlayItems
            .OfType<OverlayRectItem>()
            .FirstOrDefault(x => string.Equals(x.Label, hoverLabel, StringComparison.OrdinalIgnoreCase));

        if (roiItem is null)
        {
            if (!_roiEditing)
            {
                Cursor = Cursors.Arrow;
            }
            return;
        }

        var hitTol = GetScreenHitTolerance();
        var rect = PixelRectToContentRect(bmp, roiItem.X, roiItem.Y, roiItem.Width, roiItem.Height);
        var mode = HitTestRoiHandle(rect, contentPoint, tolerance: hitTol);

        if (mode == RoiEditMode.None)
        {
            // Show SizeAll cursor only on the border/edge, not deep inside
            Cursor = IsNearRoiBorder(rect, contentPoint, hitTol) ? Cursors.SizeAll : Cursors.Arrow;
            return;
        }

        Cursor = mode switch
        {
            RoiEditMode.Left or RoiEditMode.Right => Cursors.SizeWE,
            RoiEditMode.Top or RoiEditMode.Bottom => Cursors.SizeNS,
            RoiEditMode.TopLeft or RoiEditMode.BottomRight => Cursors.SizeNWSE,
            RoiEditMode.TopRight or RoiEditMode.BottomLeft => Cursors.SizeNESW,
            _ => Cursors.SizeAll
        };
    }

    private static Rect PixelRectToContentRect(BitmapSource bmp, int x, int y, int w, int h)
    {
        return new Rect(x, y, w, h);
    }

    private static RoiEditMode HitTestRoiHandle(Rect rect, Point p, double tolerance)
    {
        var nearLeft = Math.Abs(p.X - rect.Left) <= tolerance;
        var nearRight = Math.Abs(p.X - rect.Right) <= tolerance;
        var nearTop = Math.Abs(p.Y - rect.Top) <= tolerance;
        var nearBottom = Math.Abs(p.Y - rect.Bottom) <= tolerance;
        var nearMidX = Math.Abs(p.X - (rect.Left + rect.Right) / 2.0) <= tolerance;
        var nearMidY = Math.Abs(p.Y - (rect.Top + rect.Bottom) / 2.0) <= tolerance;

        // 4 corners
        if (nearLeft && nearTop) return RoiEditMode.TopLeft;
        if (nearRight && nearTop) return RoiEditMode.TopRight;
        if (nearLeft && nearBottom) return RoiEditMode.BottomLeft;
        if (nearRight && nearBottom) return RoiEditMode.BottomRight;

        // 4 midpoints of edges only
        if (nearMidX && nearTop) return RoiEditMode.Top;
        if (nearMidX && nearBottom) return RoiEditMode.Bottom;
        if (nearLeft && nearMidY) return RoiEditMode.Left;
        if (nearRight && nearMidY) return RoiEditMode.Right;

        return RoiEditMode.None;
    }

    private void UpdateRoiEdit(Point current)
    {
        var dx = current.X - _roiEditStart.X;
        var dy = current.Y - _roiEditStart.Y;
        var start = _roiEditRectStart;

        var left = start.Left;
        var right = start.Right;
        var top = start.Top;
        var bottom = start.Bottom;

        switch (_roiEditMode)
        {
            case RoiEditMode.Move:
                left += dx;
                right += dx;
                top += dy;
                bottom += dy;
                break;
            case RoiEditMode.Left:
                left += dx;
                break;
            case RoiEditMode.Right:
                right += dx;
                break;
            case RoiEditMode.Top:
                top += dy;
                break;
            case RoiEditMode.Bottom:
                bottom += dy;
                break;
            case RoiEditMode.TopLeft:
                left += dx;
                top += dy;
                break;
            case RoiEditMode.TopRight:
                right += dx;
                top += dy;
                break;
            case RoiEditMode.BottomLeft:
                left += dx;
                bottom += dy;
                break;
            case RoiEditMode.BottomRight:
                right += dx;
                bottom += dy;
                break;
        }

        // Normalize and enforce minimum size without ever creating a Rect with negative width/height.
        if (right < left) (left, right) = (right, left);
        if (bottom < top) (top, bottom) = (bottom, top);

        const double minSize = 2.0;

        if (right - left < minSize)
        {
            var mid = (left + right) / 2.0;
            left = mid - minSize / 2.0;
            right = mid + minSize / 2.0;
        }

        if (bottom - top < minSize)
        {
            var mid = (top + bottom) / 2.0;
            top = mid - minSize / 2.0;
            bottom = mid + minSize / 2.0;
        }

        _roiEditRect = new Rect(left, top, right - left, bottom - top);
    }

    private void ClearRoiEditVisuals()
    {
        if (_roiEditRectShape is not null)
        {
            PART_Overlay.Children.Remove(_roiEditRectShape);
            _roiEditRectShape = null;
        }

        foreach (var h in _roiEditHandles)
        {
            PART_Overlay.Children.Remove(h);
        }

        _roiEditHandles.Clear();
    }

    private void RedrawRoiEditOverlay()
    {
        PART_Overlay.Children.Clear();
        var scale = Math.Max(0.001, _transform.Matrix.M11);

        _roiEditRectShape = new Rectangle
        {
            Stroke = Brushes.Cyan,
            StrokeThickness = 2.0 / scale,
            Fill = new SolidColorBrush(Color.FromArgb(20, 0, 255, 255))
        };

        Canvas.SetLeft(_roiEditRectShape, _roiEditRect.X);
        Canvas.SetTop(_roiEditRectShape, _roiEditRect.Y);
        _roiEditRectShape.Width = _roiEditRect.Width;
        _roiEditRectShape.Height = _roiEditRect.Height;
        PART_Overlay.Children.Add(_roiEditRectShape);

        if (IsTemplateRoiLabel(_roiEditLabel))
        {
            var left = _roiEditRect.Left;
            var top = _roiEditRect.Top;
            var right = _roiEditRect.Right;
            var bottom = _roiEditRect.Bottom;
            var cx = (left + right) / 2.0;
            var cy = (top + bottom) / 2.0;

            PART_Overlay.Children.Add(new Line
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 2.0 / scale,
                X1 = left,
                Y1 = cy,
                X2 = right,
                Y2 = cy
            });

            PART_Overlay.Children.Add(new Line
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 2.0 / scale,
                X1 = cx,
                Y1 = top,
                X2 = cx,
                Y2 = bottom
            });
        }

        DrawRoiHandles(_roiEditRect.X, _roiEditRect.Y, _roiEditRect.Width, _roiEditRect.Height, isActive: true);
    }
}
