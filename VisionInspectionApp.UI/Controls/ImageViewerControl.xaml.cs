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
using VisionInspectionApp.UI.Services;

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

    private bool _hasFirstFit;

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
        if (PART_RootGrid.ActualWidth > 0 && PART_RootGrid.ActualHeight > 0)
        {
            ResetView();
            if (ImageSource is BitmapSource)
            {
                _hasFirstFit = true;
            }
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(ResetView), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        RedrawOverlays();
    }

    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_hasFirstFit && PART_RootGrid.ActualWidth > 0 && PART_RootGrid.ActualHeight > 0 && ImageSource is BitmapSource)
        {
            _hasFirstFit = true;
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
            c._hasFirstFit = false;
            c.ResetView();
        }
        else
        {
            newBmp.TryGetSourcePixelSize(out var sourceWidth, out var sourceHeight);
            c._lastPixelWidth = sourceWidth;
            c._lastPixelHeight = sourceHeight;
            if (!c._hasFirstFit || c._transform.Matrix.IsIdentity)
            {
                c._hasFirstFit = true;
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

        bmp.TryGetSourcePixelSize(out var sourceWidth, out var sourceHeight);
        double imgW = sourceWidth;
        double imgH = sourceHeight;

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

    public void ZoomIn(double factor = 1.25)
    {
        double cx = (PART_RootGrid?.ActualWidth ?? 0) / 2.0;
        double cy = (PART_RootGrid?.ActualHeight ?? 0) / 2.0;
        var m = _transform.Matrix;
        m.ScaleAt(factor, factor, cx, cy);
        _transform.Matrix = m;
        UpdateInfoText();
        RedrawOverlays();
    }

    public void ZoomOut(double factor = 1.25)
    {
        double cx = (PART_RootGrid?.ActualWidth ?? 0) / 2.0;
        double cy = (PART_RootGrid?.ActualHeight ?? 0) / 2.0;
        var m = _transform.Matrix;
        m.ScaleAt(1.0 / factor, 1.0 / factor, cx, cy);
        _transform.Matrix = m;
        UpdateInfoText();
        RedrawOverlays();
    }

    public void ResetView(double targetScale)
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
        bmp.TryGetSourcePixelSize(out var sourceWidth, out var sourceHeight);
        double imgW = sourceWidth;
        double imgH = sourceHeight;

        var tx = (containerW - imgW * targetScale) / 2.0;
        var ty = (containerH - imgH * targetScale) / 2.0;

        var m = Matrix.Identity;
        m.Scale(targetScale, targetScale);
        m.Translate(tx, ty);
        _transform.Matrix = m;

        UpdateInfoText();
        RedrawOverlays();
    }

    private Point? _lastMousePos;

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
                string baseText = $"{_lastPixelWidth} x {_lastPixelHeight} px  |  Zoom: {z:F0}%";

                if (_lastMousePos.HasValue && ImageSource is BitmapSource bmp)
                {
                    var contentPos = ContainerToContent(_lastMousePos.Value);
                    int px = (int)Math.Floor(contentPos.X);
                    int py = (int)Math.Floor(contentPos.Y);

                    if (px >= 0 && px < _lastPixelWidth && py >= 0 && py < _lastPixelHeight)
                    {
                        string colorStr = SamplePixelColor(bmp, px, py, _lastPixelWidth, _lastPixelHeight);
                        PART_InfoText.Text = $"{baseText}  |  X: {px}, Y: {py}  |  {colorStr}";
                        PART_InfoText.Visibility = Visibility.Visible;
                        return;
                    }
                    else
                    {
                        PART_InfoText.Text = $"{baseText}  |  X: --, Y: --  |  Val: --";
                        PART_InfoText.Visibility = Visibility.Visible;
                        return;
                    }
                }

                PART_InfoText.Text = baseText;
                PART_InfoText.Visibility = Visibility.Visible;
            }
        }
    }

    private static string SamplePixelColor(BitmapSource bmp, int x, int y, int sourceWidth, int sourceHeight)
    {
        try
        {
            int bytesPerPixel = (bmp.Format.BitsPerPixel + 7) / 8;
            if (bytesPerPixel <= 0) return "Val: --";

            byte[] pixels = new byte[bytesPerPixel];
            int stride = bytesPerPixel;
            var sampleX = Math.Clamp((int)Math.Floor(x * bmp.PixelWidth / (double)Math.Max(1, sourceWidth)), 0, bmp.PixelWidth - 1);
            var sampleY = Math.Clamp((int)Math.Floor(y * bmp.PixelHeight / (double)Math.Max(1, sourceHeight)), 0, bmp.PixelHeight - 1);
            var rect = new Int32Rect(sampleX, sampleY, 1, 1);

            bmp.CopyPixels(rect, pixels, stride, 0);

            var fmt = bmp.Format;
            if (fmt == PixelFormats.Gray8 || fmt == PixelFormats.Indexed8 || fmt == PixelFormats.Gray2 || fmt == PixelFormats.Gray4)
            {
                byte val = pixels[0];
                return $"Val: {val}";
            }
            else if (fmt == PixelFormats.Bgr24)
            {
                byte b = pixels[0];
                byte g = pixels[1];
                byte r = pixels[2];
                byte gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                return $"Val: {gray} (R:{r} G:{g} B:{b})";
            }
            else if (fmt == PixelFormats.Bgra32 || fmt == PixelFormats.Pbgra32)
            {
                byte b = pixels[0];
                byte g = pixels[1];
                byte r = pixels[2];
                byte gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                return $"Val: {gray} (R:{r} G:{g} B:{b})";
            }
            else if (fmt == PixelFormats.Rgb24)
            {
                byte r = pixels[0];
                byte g = pixels[1];
                byte b = pixels[2];
                byte gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                return $"Val: {gray} (R:{r} G:{g} B:{b})";
            }
            else
            {
                byte val = pixels[0];
                return $"Val: {val}";
            }
        }
        catch
        {
            return "Val: --";
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
        var mousePos = e.GetPosition(PART_RootGrid);
        _lastMousePos = mousePos;
        UpdateInfoText();

        if (_panning)
        {
            var current = mousePos;
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
            var contentPos = ContainerToContent(mousePos);
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
    private static bool IsNearRoiBorder(Rect rect, double angle, Point p, double tolerance)
    {
        var center = new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
        var unrotP = RotatePoint(p, center, -angle);

        var expanded = rect;
        expanded.Inflate(tolerance, tolerance);
        if (!expanded.Contains(unrotP)) return false;

        var shrunk = rect;
        shrunk.Inflate(-tolerance, -tolerance);
        if (shrunk.Width > 0 && shrunk.Height > 0 && shrunk.Contains(unrotP)) return false;

        return true;
    }

    private string? FindHoverRoiLabel(BitmapSource bmp, Point contentPos)
    {
        if (OverlayItems is null)
        {
            return null;
        }

        const double borderTol = 12.0;
        var rects = OverlayItems.OfType<OverlayRectItem>().ToList();
        for (var i = rects.Count - 1; i >= 0; i--)
        {
            var r = rects[i];
            if (r.Width <= 0 || r.Height <= 0) continue;
            var roiRect = new Rect(r.X, r.Y, r.Width, r.Height);
            if (IsNearRoiBorder(roiRect, r.Angle, contentPos, borderTol))
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
            // Prefer active CCT, SCT or T; otherwise swap CC->CCT, SC->SCT or S->T; otherwise any CCT/SCT/T in overlays.
            if (!string.IsNullOrWhiteSpace(_activeRoiLabel) && (EndsWith(_activeRoiLabel!, " CCT") || EndsWith(_activeRoiLabel!, " SCT") || EndsWith(_activeRoiLabel!, " T"))) return _activeRoiLabel;

            var swapped = TrySwapSuffix(_activeRoiLabel, "CCT") ?? TrySwapSuffix(_activeRoiLabel, "SCT") ?? TrySwapSuffix(_activeRoiLabel, "T");
            if (!string.IsNullOrWhiteSpace(swapped)) return swapped;

            var cct = rectLabels.FirstOrDefault(x => EndsWith(x, " CCT"));
            if (!string.IsNullOrWhiteSpace(cct)) return cct;

            var sct = rectLabels.FirstOrDefault(x => EndsWith(x, " SCT"));
            if (!string.IsNullOrWhiteSpace(sct)) return sct;

            var t = rectLabels.FirstOrDefault(x => EndsWith(x, " T"));
            if (!string.IsNullOrWhiteSpace(t)) return t;

            // If we only have a Search ROI overlay, synthesize a matching Template label.
            var cc = rectLabels.FirstOrDefault(x => EndsWith(x, " CC"));
            if (!string.IsNullOrWhiteSpace(cc))
            {
                var parts = cc.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    return $"{parts[0]} CCT";
                }
            }

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
            // For Search: CC, SC, S, L, LP, C, CIR, Cal, EPD.
            if (!string.IsNullOrWhiteSpace(_activeRoiLabel)
                && (EndsWith(_activeRoiLabel!, " CC")
                    || EndsWith(_activeRoiLabel!, " SC")
                    || EndsWith(_activeRoiLabel!, " S")
                    || EndsWith(_activeRoiLabel!, " L")
                    || EndsWith(_activeRoiLabel!, " LP")
                    || EndsWith(_activeRoiLabel!, " C")
                    || EndsWith(_activeRoiLabel!, " CIR")
                    || EndsWith(_activeRoiLabel!, " Cal")
                    || EndsWith(_activeRoiLabel!, " EPD")))
            {
                return _activeRoiLabel;
            }

            var swapped = TrySwapSuffix(_activeRoiLabel, "CC") ?? TrySwapSuffix(_activeRoiLabel, "SC") ?? TrySwapSuffix(_activeRoiLabel, "S");
            if (!string.IsNullOrWhiteSpace(swapped)) return swapped;

            var cc1 = rectLabels.FirstOrDefault(x => EndsWith(x, " CC"));
            if (!string.IsNullOrWhiteSpace(cc1)) return cc1;

            var cir = rectLabels.FirstOrDefault(x => EndsWith(x, " CIR"));
            if (!string.IsNullOrWhiteSpace(cir)) return cir;

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

            var cal = rectLabels.FirstOrDefault(x => EndsWith(x, " Cal"));
            if (!string.IsNullOrWhiteSpace(cal)) return cal;

            var epd = rectLabels.FirstOrDefault(x => EndsWith(x, " EPD"));
            if (!string.IsNullOrWhiteSpace(epd)) return epd;
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

        var l = label.Split('[')[0].Trim();
        return l.EndsWith(" CCT", StringComparison.OrdinalIgnoreCase)
            || l.EndsWith(" SCT", StringComparison.OrdinalIgnoreCase)
            || l.EndsWith(" T", StringComparison.OrdinalIgnoreCase)
            || string.Equals(l, "Origin T", StringComparison.OrdinalIgnoreCase);
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
        BottomRight = 9,
        Rotate = 10
    }

    private bool _roiEditing;
    private RoiEditMode _roiEditMode;
    private string? _roiEditLabel;
    private Rect _roiEditRectStart;
    private Rect _roiEditRect;
    private double _roiEditRectAngleStart;
    private double _roiEditRectAngle;
    private Point _roiEditStart;
    private Shape? _roiEditRectShape;
    private readonly List<Rectangle> _roiEditHandles = new();

    private string? _activeRoiLabel;
    private string? _hoverRoiLabel;

    private static Point RotatePoint(Point p, Point center, double angleDeg)
    {
        if (Math.Abs(angleDeg) < 0.001) return p;
        var rad = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var dx = p.X - center.X;
        var dy = p.Y - center.Y;
        var rx = dx * cos - dy * sin + center.X;
        var ry = dx * sin + dy * cos + center.Y;
        return new Point(rx, ry);
    }

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

        bmp.TryGetSourcePixelSize(out var sourceWidth, out var sourceHeight);

        PART_Content.Width = sourceWidth;
        PART_Content.Height = sourceHeight;

        PART_Image.Width = sourceWidth;
        PART_Image.Height = sourceHeight;

        PART_Overlay.Width = sourceWidth;
        PART_Overlay.Height = sourceHeight;

        PART_FastOverlay.Width = sourceWidth;
        PART_FastOverlay.Height = sourceHeight;
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
                    DrawRoiHandles(r.X, r.Y, r.Width, r.Height, r.Angle, string.Equals(r.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase));
                }
            }
            else if (item is OverlayCircleItem c)
            {
                var showHandles = (!string.IsNullOrWhiteSpace(c.Label)
                    && (
                        string.Equals(c.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(c.Label, _hoverRoiLabel, StringComparison.OrdinalIgnoreCase)
                    ));

                if (showHandles)
                {
                    DrawCircleRoiHandles(c.CenterX, c.CenterY, c.Radius, string.Equals(c.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase));
                }
            }
            else if (item is OverlayPointItem pt)
            {
                var showHandles = (!string.IsNullOrWhiteSpace(pt.Label)
                    && (
                        string.Equals(pt.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(pt.Label, _hoverRoiLabel, StringComparison.OrdinalIgnoreCase)
                    ));

                if (showHandles)
                {
                    DrawPointRoiHandle(pt.X, pt.Y, pt.Radius, string.Equals(pt.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase));
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
                var startRoi = ConvertContentRoiToPixelRoi(bmpEdit, _roiEditRectStart.X, _roiEditRectStart.Y, _roiEditRectStart.Width, _roiEditRectStart.Height, _roiEditRectAngleStart);
                var editedRoi = ConvertContentRoiToPixelRoi(bmpEdit, _roiEditRect.X, _roiEditRect.Y, _roiEditRect.Width, _roiEditRect.Height, _roiEditRectAngle);
                if (_roiEditLabel is not null)
                {
                    var changed = startRoi.X != editedRoi.X
                        || startRoi.Y != editedRoi.Y
                        || startRoi.Width != editedRoi.Width
                        || startRoi.Height != editedRoi.Height
                        || Math.Abs(startRoi.Angle - editedRoi.Angle) > 0.01;

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

        var roi = ConvertContentRoiToPixelRoi(bmp, x, y, w, h, 0);

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

    private static Roi ConvertContentRoiToPixelRoi(BitmapSource bmp, double contentX, double contentY, double contentW, double contentH, double contentAngle = 0)
    {
        var px = (int)Math.Round(contentX);
        var py = (int)Math.Round(contentY);
        var pw = (int)Math.Round(contentW);
        var ph = (int)Math.Round(contentH);

        bmp.TryGetSourcePixelSize(out var sourceWidth, out var sourceHeight);

        var maxX = Math.Max(0, sourceWidth - 1);
        var maxY = Math.Max(0, sourceHeight - 1);
        px = Math.Clamp(px, 0, maxX);
        py = Math.Clamp(py, 0, maxY);

        var maxW = sourceWidth - px;
        var maxH = sourceHeight - py;
        if (maxW < 1) maxW = 1;
        if (maxH < 1) maxH = 1;

        pw = Math.Clamp(pw, 1, maxW);
        ph = Math.Clamp(ph, 1, maxH);

        return new Roi
        {
            X = px,
            Y = py,
            Width = pw,
            Height = ph,
            Angle = Math.Round(contentAngle, 1)
        };
    }

    private static Point ConvertContentPointToPixelPoint(BitmapSource bmp, double contentX, double contentY)
    {
        var px = (int)Math.Round(contentX);
        var py = (int)Math.Round(contentY);

        bmp.TryGetSourcePixelSize(out var sourceWidth, out var sourceHeight);

        px = Math.Clamp(px, 0, Math.Max(0, sourceWidth - 1));
        py = Math.Clamp(py, 0, Math.Max(0, sourceHeight - 1));

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
        var scale = Math.Max(0.001, _transform.Matrix.M11);

        // 1. Highest Priority: Point items (e.g. Polygon vertex handles or point markers)
        foreach (var item in OverlayItems)
        {
            if (item is OverlayPointItem pt && !string.IsNullOrWhiteSpace(pt.Label))
            {
                double dist = Math.Sqrt((contentPoint.X - pt.X) * (contentPoint.X - pt.X) + (contentPoint.Y - pt.Y) * (contentPoint.Y - pt.Y));
                double maxDist = Math.Max(12.0 / scale, pt.Radius + 6.0 / scale);
                if (dist <= maxDist)
                {
                    _roiEditing = true;
                    _roiEditLabel = pt.Label;
                    _activeRoiLabel = pt.Label;
                    _roiEditStart = contentPoint;
                    _roiEditMode = RoiEditMode.Move;
                    _roiEditRectStart = new Rect(pt.X - 5, pt.Y - 5, 10, 10);
                    _roiEditRect = _roiEditRectStart;
                    _roiEditRectAngleStart = 0;
                    _roiEditRectAngle = 0;
                    RedrawRoiEditOverlay();
                    return true;
                }
            }
        }

        // 2. Second Priority: Circle items
        foreach (var item in OverlayItems)
        {
            if (item is OverlayCircleItem c && !string.IsNullOrWhiteSpace(c.Label))
            {
                double dist = Math.Sqrt((contentPoint.X - c.CenterX) * (contentPoint.X - c.CenterX) + (contentPoint.Y - c.CenterY) * (contentPoint.Y - c.CenterY));
                double rimDist = Math.Abs(dist - c.Radius);
                if (rimDist <= hitTol + 6.0 / scale)
                {
                    _roiEditing = true;
                    _roiEditLabel = c.Label;
                    _activeRoiLabel = c.Label;
                    _roiEditStart = contentPoint;
                    _roiEditMode = RoiEditMode.Right;
                    _roiEditRectStart = new Rect(c.CenterX - c.Radius, c.CenterY - c.Radius, c.Radius * 2, c.Radius * 2);
                    _roiEditRect = _roiEditRectStart;
                    _roiEditRectAngleStart = 0;
                    _roiEditRectAngle = 0;
                    RedrawRoiEditOverlay();
                    return true;
                }
                else if (dist < c.Radius)
                {
                    _roiEditing = true;
                    _roiEditLabel = c.Label;
                    _activeRoiLabel = c.Label;
                    _roiEditStart = contentPoint;
                    _roiEditMode = RoiEditMode.Move;
                    _roiEditRectStart = new Rect(c.CenterX - c.Radius, c.CenterY - c.Radius, c.Radius * 2, c.Radius * 2);
                    _roiEditRect = _roiEditRectStart;
                    _roiEditRectAngleStart = 0;
                    _roiEditRectAngle = 0;
                    RedrawRoiEditOverlay();
                    return true;
                }
            }
        }

        // 3. Third Priority: Rectangle items (Checked before Polygon interior body so Rectangles overlapping under/over Polygons can be interacted with)
        var candidates = new List<(OverlayRectItem Item, Rect Rect)>();

        foreach (var item in OverlayItems)
        {
            if (item is not OverlayRectItem r || string.IsNullOrWhiteSpace(r.Label))
            {
                continue;
            }

            var baseRect = PixelRectToContentRect(bmp, r.X, r.Y, r.Width, r.Height);
            var center = new Point(baseRect.Left + baseRect.Width / 2.0, baseRect.Top + baseRect.Height / 2.0);
            var unrotP = RotatePoint(contentPoint, center, -r.Angle);
            var hitRect = baseRect;
            hitRect.Inflate(hitTol + 25.0 / scale, hitTol + 25.0 / scale);
            if (!hitRect.Contains(unrotP))
            {
                continue;
            }

            candidates.Add((r, baseRect));
        }

        if (candidates.Count > 0)
        {
            // Direct border or handle hits get top priority (ordered by smallest rect area)
            var borderHits = candidates
                .Where(x => HitTestRoiHandle(x.Rect, x.Item.Angle, contentPoint, hitTol, scale) != RoiEditMode.None || IsNearRoiBorder(x.Rect, x.Item.Angle, contentPoint, hitTol))
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

            var mode = HitTestRoiHandle(picked.Rect, picked.Item.Angle, contentPoint, tolerance: hitTol, scale: scale);
            if (mode == RoiEditMode.None)
            {
                mode = IsNearRoiBorder(picked.Rect, picked.Item.Angle, contentPoint, hitTol) ? RoiEditMode.Move : RoiEditMode.None;
            }

            if (mode != RoiEditMode.None)
            {
                _roiEditing = true;
                _roiEditLabel = picked.Item.Label;
                _activeRoiLabel = picked.Item.Label;
                _roiEditStart = contentPoint;
                _roiEditRectStart = picked.Rect;
                _roiEditRect = picked.Rect;
                _roiEditRectAngleStart = picked.Item.Angle;
                _roiEditRectAngle = picked.Item.Angle;
                _roiEditMode = mode;

                RedrawRoiEditOverlay();
                return true;
            }
        }

        // 4. Fourth Priority: Polyline items (Polygon body interior)
        foreach (var item in OverlayItems)
        {
            if (item is OverlayPolylineItem pl && !string.IsNullOrWhiteSpace(pl.Label) && pl.Points != null && pl.Points.Count >= 3)
            {
                if (IsPointInPolygon(contentPoint, pl.Points))
                {
                    double minX = pl.Points.Min(p => p.X);
                    double minY = pl.Points.Min(p => p.Y);
                    double maxX = pl.Points.Max(p => p.X);
                    double maxY = pl.Points.Max(p => p.Y);

                    _roiEditing = true;
                    _roiEditLabel = pl.Label;
                    _activeRoiLabel = pl.Label;
                    _roiEditStart = contentPoint;
                    _roiEditMode = RoiEditMode.Move;
                    _roiEditRectStart = new Rect(minX, minY, Math.Max(10, maxX - minX), Math.Max(10, maxY - minY));
                    _roiEditRect = _roiEditRectStart;
                    _roiEditRectAngleStart = 0;
                    _roiEditRectAngle = 0;
                    RedrawRoiEditOverlay();
                    return true;
                }
            }
        }

        return false;
    }

    private string? FindTopRoiLabelAt(BitmapSource bmp, Point contentPoint)
    {
        if (OverlayItems is null)
        {
            return null;
        }

        var hitTol = GetScreenHitTolerance();
        var scale = Math.Max(0.001, _transform.Matrix.M11);

        foreach (var item in OverlayItems)
        {
            if (item is OverlayPointItem pt && !string.IsNullOrWhiteSpace(pt.Label))
            {
                double dist = Math.Sqrt((contentPoint.X - pt.X) * (contentPoint.X - pt.X) + (contentPoint.Y - pt.Y) * (contentPoint.Y - pt.Y));
                if (dist <= Math.Max(12.0 / scale, pt.Radius + 6.0 / scale))
                {
                    return pt.Label;
                }
            }
        }

        foreach (var item in OverlayItems)
        {
            if (item is OverlayCircleItem c && !string.IsNullOrWhiteSpace(c.Label))
            {
                double dist = Math.Sqrt((contentPoint.X - c.CenterX) * (contentPoint.X - c.CenterX) + (contentPoint.Y - c.CenterY) * (contentPoint.Y - c.CenterY));
                if (dist <= c.Radius + hitTol)
                {
                    return c.Label;
                }
            }
        }

        var candidates = new List<(OverlayRectItem Item, Rect Rect)>();

        foreach (var item in OverlayItems)
        {
            if (item is not OverlayRectItem r || string.IsNullOrWhiteSpace(r.Label))
            {
                continue;
            }

            var baseRect = PixelRectToContentRect(bmp, r.X, r.Y, r.Width, r.Height);
            var center = new Point(baseRect.Left + baseRect.Width / 2.0, baseRect.Top + baseRect.Height / 2.0);
            var unrotP = RotatePoint(contentPoint, center, -r.Angle);
            var hitRect = baseRect;
            hitRect.Inflate(hitTol + 25.0 / scale, hitTol + 25.0 / scale);
            if (!hitRect.Contains(unrotP))
            {
                continue;
            }

            candidates.Add((r, baseRect));
        }

        if (candidates.Count > 0)
        {
            var borderHits = candidates
                .Where(x => HitTestRoiHandle(x.Rect, x.Item.Angle, contentPoint, hitTol, scale) != RoiEditMode.None || IsNearRoiBorder(x.Rect, x.Item.Angle, contentPoint, hitTol))
                .OrderBy(x => x.Rect.Width * x.Rect.Height)
                .ToList();

            if (borderHits.Count > 0)
            {
                return borderHits.First().Item.Label;
            }

            return candidates.OrderBy(x => x.Rect.Width * x.Rect.Height).First().Item.Label;
        }

        foreach (var item in OverlayItems)
        {
            if (item is OverlayPolylineItem pl && !string.IsNullOrWhiteSpace(pl.Label) && pl.Points != null && pl.Points.Count >= 3)
            {
                if (IsPointInPolygon(contentPoint, pl.Points))
                {
                    return pl.Label;
                }
            }
        }

        return null;
    }

    private string? FindRoiLabelAtForClick(BitmapSource bmp, Point contentPoint)
    {
        return FindTopRoiLabelAt(bmp, contentPoint);
    }

    private void DrawRoiHandles(double left, double top, double width, double height, double angle, bool isActive)
    {
        var scale = Math.Max(0.001, _transform.Matrix.M11);
        var corner = (isActive ? 12.0 : 10.0) / scale;
        var edge = (isActive ? 8.0 : 6.0) / scale;
        var rotSize = (isActive ? 12.0 : 10.0) / scale;
        var stroke = isActive ? Brushes.Cyan : Brushes.DeepSkyBlue;

        var center = new Point(left + width / 2.0, top + height / 2.0);

        // 4 corners (unrotated)
        AddHandle(new Point(left, top), corner);
        AddHandle(new Point(left + width, top), corner);
        AddHandle(new Point(left, top + height), corner);
        AddHandle(new Point(left + width, top + height), corner);

        // 4 edge midpoints (unrotated)
        AddHandle(new Point(left + width / 2.0, top), edge);
        AddHandle(new Point(left + width / 2.0, top + height), edge);
        AddHandle(new Point(left, top + height / 2.0), edge);
        AddHandle(new Point(left + width, top + height / 2.0), edge);

        // Top rotation stem line & handle
        double rotOffsetY = 25.0 / scale;
        var unrotStemStart = new Point(left + width / 2.0, top);
        var unrotStemEnd = new Point(left + width / 2.0, top - rotOffsetY);

        var stemStart = RotatePoint(unrotStemStart, center, angle);
        var stemEnd = RotatePoint(unrotStemEnd, center, angle);

        var stem = new Line
        {
            X1 = stemStart.X,
            Y1 = stemStart.Y,
            X2 = stemEnd.X,
            Y2 = stemEnd.Y,
            Stroke = stroke,
            StrokeThickness = 1.5 / scale
        };
        PART_Overlay.Children.Add(stem);

        var rotHandle = new Ellipse
        {
            Width = rotSize,
            Height = rotSize,
            Stroke = stroke,
            StrokeThickness = 1.5 / scale,
            Fill = Brushes.Orange
        };
        Canvas.SetLeft(rotHandle, stemEnd.X - rotSize / 2.0);
        Canvas.SetTop(rotHandle, stemEnd.Y - rotSize / 2.0);
        PART_Overlay.Children.Add(rotHandle);

        void AddHandle(Point unrotPt, double size)
        {
            var rotPt = RotatePoint(unrotPt, center, angle);
            var h = new Rectangle
            {
                Width = size,
                Height = size,
                Stroke = stroke,
                StrokeThickness = 1.5 / scale,
                Fill = Brushes.Black,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = Math.Abs(angle) > 0.001 ? new RotateTransform(angle) : null
            };

            Canvas.SetLeft(h, rotPt.X - size / 2.0);
            Canvas.SetTop(h, rotPt.Y - size / 2.0);
            PART_Overlay.Children.Add(h);
        }
    }

    private void DrawCircleRoiHandles(double cx, double cy, double radius, bool isActive)
    {
        var scale = Math.Max(0.001, _transform.Matrix.M11);
        var handleSize = (isActive ? 10.0 : 8.0) / scale;
        var stroke = isActive ? Brushes.Cyan : Brushes.DeepSkyBlue;

        // 4 cardinal handles on rim (Top, Bottom, Left, Right)
        AddHandle(new Point(cx, cy - radius));
        AddHandle(new Point(cx, cy + radius));
        AddHandle(new Point(cx - radius, cy));
        AddHandle(new Point(cx + radius, cy));

        void AddHandle(Point pt)
        {
            var h = new Ellipse
            {
                Width = handleSize,
                Height = handleSize,
                Stroke = stroke,
                StrokeThickness = 1.5 / scale,
                Fill = Brushes.Lime
            };
            Canvas.SetLeft(h, pt.X - handleSize / 2.0);
            Canvas.SetTop(h, pt.Y - handleSize / 2.0);
            PART_Overlay.Children.Add(h);
        }
    }

    private void DrawPointRoiHandle(double x, double y, double radius, bool isActive)
    {
        var scale = Math.Max(0.001, _transform.Matrix.M11);
        var handleSize = (isActive ? 14.0 : 10.0) / scale;
        var stroke = isActive ? Brushes.Cyan : Brushes.Yellow;

        var ring = new Ellipse
        {
            Width = handleSize,
            Height = handleSize,
            Stroke = stroke,
            StrokeThickness = 2.0 / scale,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(ring, x - handleSize / 2.0);
        Canvas.SetTop(ring, y - handleSize / 2.0);
        PART_Overlay.Children.Add(ring);
    }

    private static bool IsPointInPolygon(Point p, List<Point> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            if (((poly[i].Y > p.Y) != (poly[j].Y > p.Y)) &&
                (p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X))
            {
                inside = !inside;
            }
        }
        return inside;
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
        var scale = Math.Max(0.001, _transform.Matrix.M11);
        var rect = PixelRectToContentRect(bmp, roiItem.X, roiItem.Y, roiItem.Width, roiItem.Height);
        var mode = HitTestRoiHandle(rect, roiItem.Angle, contentPoint, tolerance: hitTol, scale: scale);

        if (mode == RoiEditMode.None)
        {
            Cursor = IsNearRoiBorder(rect, roiItem.Angle, contentPoint, hitTol) ? Cursors.SizeAll : Cursors.Arrow;
            return;
        }

        Cursor = mode switch
        {
            RoiEditMode.Rotate => Cursors.Hand,
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

    private static RoiEditMode HitTestRoiHandle(Rect rect, double angle, Point p, double tolerance, double scale = 1.0)
    {
        var center = new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
        var unrotP = RotatePoint(p, center, -angle);

        // Check top rotation handle (25px above top-center)
        var rotOffsetY = 25.0 / scale;
        var rotHandleCenter = new Point(rect.Left + rect.Width / 2.0, rect.Top - rotOffsetY);
        var dxRot = unrotP.X - rotHandleCenter.X;
        var dyRot = unrotP.Y - rotHandleCenter.Y;
        if (Math.Sqrt(dxRot * dxRot + dyRot * dyRot) <= tolerance * 1.3)
        {
            return RoiEditMode.Rotate;
        }

        var nearLeft = Math.Abs(unrotP.X - rect.Left) <= tolerance;
        var nearRight = Math.Abs(unrotP.X - rect.Right) <= tolerance;
        var nearTop = Math.Abs(unrotP.Y - rect.Top) <= tolerance;
        var nearBottom = Math.Abs(unrotP.Y - rect.Bottom) <= tolerance;
        var nearMidX = Math.Abs(unrotP.X - (rect.Left + rect.Right) / 2.0) <= tolerance;
        var nearMidY = Math.Abs(unrotP.Y - (rect.Top + rect.Bottom) / 2.0) <= tolerance;

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
        if (_roiEditMode == RoiEditMode.Rotate)
        {
            var center = new Point(_roiEditRect.Left + _roiEditRect.Width / 2.0, _roiEditRect.Top + _roiEditRect.Height / 2.0);
            var dx = current.X - center.X;
            var dy = current.Y - center.Y;
            var angleRad = Math.Atan2(dy, dx);
            var rawAngleDeg = angleRad * 180.0 / Math.PI + 90.0;
            while (rawAngleDeg > 180.0) rawAngleDeg -= 360.0;
            while (rawAngleDeg <= -180.0) rawAngleDeg += 360.0;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Fine rotation mode: apply 20% damping to the delta angle
                var delta = rawAngleDeg - _roiEditRectAngle;
                // Normalize delta to [-180, 180] to handle wrap-around
                while (delta > 180.0) delta -= 360.0;
                while (delta <= -180.0) delta += 360.0;
                _roiEditRectAngle += delta * 0.2;
            }
            else
            {
                _roiEditRectAngle = rawAngleDeg;
            }

            // Normalize final angle to [-180, 180]
            while (_roiEditRectAngle > 180.0) _roiEditRectAngle -= 360.0;
            while (_roiEditRectAngle <= -180.0) _roiEditRectAngle += 360.0;
            _roiEditRectAngle = Math.Round(_roiEditRectAngle, 1);
            return;
        }

        var dxMove = current.X - _roiEditStart.X;
        var dyMove = current.Y - _roiEditStart.Y;
        var start = _roiEditRectStart;

        if (_roiEditMode == RoiEditMode.Move)
        {
            _roiEditRect = new Rect(start.X + dxMove, start.Y + dyMove, start.Width, start.Height);
            return;
        }

        // Check if editing a Circle ROI
        var isCircleEdit = !string.IsNullOrWhiteSpace(_activeRoiLabel) && OverlayItems?.FirstOrDefault(x => string.Equals(x.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase)) is OverlayCircleItem;
        if (isCircleEdit)
        {
            var circleCenter = new Point(start.X + start.Width / 2.0, start.Y + start.Height / 2.0);
            var newRadius = Math.Max(2.0, Math.Sqrt((current.X - circleCenter.X) * (current.X - circleCenter.X) + (current.Y - circleCenter.Y) * (current.Y - circleCenter.Y)));
            _roiEditRect = new Rect(circleCenter.X - newRadius, circleCenter.Y - newRadius, newRadius * 2, newRadius * 2);
            return;
        }

        // For Rectangle ROIs (both rotated and unrotated):
        // Transform the displacement vector into the local (unrotated) coordinate space of the ROI
        var angle = _roiEditRectAngle;
        var rad = angle * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);

        // Rotation matrix for local coordinates: R(-angle) * [dxMove, dyMove]^T
        var dxLocal = dxMove * cos + dyMove * sin;
        var dyLocal = -dxMove * sin + dyMove * cos;

        var startW = start.Width;
        var startH = start.Height;
        var newW = startW;
        var newH = startH;
        var deltaCxLocal = 0.0;
        var deltaCyLocal = 0.0;

        const double minSize = 2.0;

        switch (_roiEditMode)
        {
            case RoiEditMode.Right:
                newW = Math.Max(minSize, startW + dxLocal);
                deltaCxLocal = (newW - startW) / 2.0;
                break;
            case RoiEditMode.Left:
                newW = Math.Max(minSize, startW - dxLocal);
                deltaCxLocal = -(newW - startW) / 2.0;
                break;
            case RoiEditMode.Bottom:
                newH = Math.Max(minSize, startH + dyLocal);
                deltaCyLocal = (newH - startH) / 2.0;
                break;
            case RoiEditMode.Top:
                newH = Math.Max(minSize, startH - dyLocal);
                deltaCyLocal = -(newH - startH) / 2.0;
                break;
            case RoiEditMode.BottomRight:
                newW = Math.Max(minSize, startW + dxLocal);
                newH = Math.Max(minSize, startH + dyLocal);
                deltaCxLocal = (newW - startW) / 2.0;
                deltaCyLocal = (newH - startH) / 2.0;
                break;
            case RoiEditMode.BottomLeft:
                newW = Math.Max(minSize, startW - dxLocal);
                newH = Math.Max(minSize, startH + dyLocal);
                deltaCxLocal = -(newW - startW) / 2.0;
                deltaCyLocal = (newH - startH) / 2.0;
                break;
            case RoiEditMode.TopRight:
                newW = Math.Max(minSize, startW + dxLocal);
                newH = Math.Max(minSize, startH - dyLocal);
                deltaCxLocal = (newW - startW) / 2.0;
                deltaCyLocal = -(newH - startH) / 2.0;
                break;
            case RoiEditMode.TopLeft:
                newW = Math.Max(minSize, startW - dxLocal);
                newH = Math.Max(minSize, startH - dyLocal);
                deltaCxLocal = -(newW - startW) / 2.0;
                deltaCyLocal = -(newH - startH) / 2.0;
                break;
        }

        // Transform deltaCenter from local space back to world space: R(angle) * [deltaCxLocal, deltaCyLocal]^T
        var deltaCxWorld = deltaCxLocal * cos - deltaCyLocal * sin;
        var deltaCyWorld = deltaCxLocal * sin + deltaCyLocal * cos;

        var startCenter = new Point(start.X + startW / 2.0, start.Y + startH / 2.0);
        var newCenter = new Point(startCenter.X + deltaCxWorld, startCenter.Y + deltaCyWorld);

        _roiEditRect = new Rect(newCenter.X - newW / 2.0, newCenter.Y - newH / 2.0, newW, newH);
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

        var targetLabel = _roiEditLabel ?? _activeRoiLabel;

        // Check if editing a polygon vertex (e.g. "PRE1 PRP1_V2")
        int vIndex = -1;
        string? parentPolyLabel = null;
        if (!string.IsNullOrWhiteSpace(targetLabel) && targetLabel.Contains("_V"))
        {
            var parts = targetLabel.Split("_V", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[1], out var idx))
            {
                vIndex = idx - 1;
                parentPolyLabel = parts[0];
            }
        }

        OverlayPolylineItem? polyItem = null;
        if (!string.IsNullOrWhiteSpace(parentPolyLabel))
        {
            polyItem = OverlayItems?.OfType<OverlayPolylineItem>()
                .FirstOrDefault(x => string.Equals(x.Label, parentPolyLabel, StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(targetLabel))
        {
            polyItem = OverlayItems?.OfType<OverlayPolylineItem>()
                .FirstOrDefault(x => string.Equals(x.Label, targetLabel, StringComparison.OrdinalIgnoreCase));
        }

        if (polyItem != null && polyItem.Points != null && polyItem.Points.Count >= 3)
        {
            List<Point> livePoints;
            if (vIndex >= 0 && vIndex < polyItem.Points.Count)
            {
                // Single vertex dragging in real-time!
                livePoints = polyItem.Points.ToList();
                var currentVx = _roiEditRect.X + 5;
                var currentVy = _roiEditRect.Y + 5;
                livePoints[vIndex] = new Point(currentVx, currentVy);
            }
            else
            {
                // Polygon body dragging in real-time!
                var dx = _roiEditRect.X - _roiEditRectStart.X;
                var dy = _roiEditRect.Y - _roiEditRectStart.Y;
                livePoints = polyItem.Points.Select(p => new Point(p.X + dx, p.Y + dy)).ToList();
            }

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(livePoints[0], isFilled: true, isClosed: true);
                ctx.PolyLineTo(livePoints.Skip(1).ToList(), isStroked: true, isSmoothJoin: false);
            }

            var livePolyPath = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Stroke = Brushes.Cyan,
                StrokeThickness = 2.0 / scale,
                Fill = new SolidColorBrush(Color.FromArgb(30, 0, 255, 255))
            };
            PART_Overlay.Children.Add(livePolyPath);

            for (int i = 0; i < livePoints.Count; i++)
            {
                var p = livePoints[i];
                bool isDragged = (i == vIndex);
                double rSize = (isDragged ? 14.0 : 8.0) / scale;
                var dot = new Ellipse
                {
                    Width = rSize,
                    Height = rSize,
                    Fill = isDragged ? Brushes.Cyan : Brushes.Yellow,
                    Stroke = isDragged ? Brushes.White : Brushes.DarkBlue,
                    StrokeThickness = 1.5 / scale
                };
                Canvas.SetLeft(dot, p.X - rSize / 2.0);
                Canvas.SetTop(dot, p.Y - rSize / 2.0);
                PART_Overlay.Children.Add(dot);
            }

            _roiEditRectShape = livePolyPath;
            return;
        }

        var cx = _roiEditRect.X + _roiEditRect.Width / 2.0;
        var cy = _roiEditRect.Y + _roiEditRect.Height / 2.0;

        var isCircleEdit = !string.IsNullOrWhiteSpace(_activeRoiLabel) && OverlayItems?.FirstOrDefault(x => string.Equals(x.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase)) is OverlayCircleItem;
        if (isCircleEdit)
        {
            _roiEditRectShape = new Ellipse
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 2.0 / scale,
                Fill = new SolidColorBrush(Color.FromArgb(20, 0, 255, 255))
            };
        }
        else
        {
            _roiEditRectShape = new Rectangle
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 2.0 / scale,
                Fill = new SolidColorBrush(Color.FromArgb(20, 0, 255, 255)),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = Math.Abs(_roiEditRectAngle) > 0.001 ? new RotateTransform(_roiEditRectAngle) : null
            };
        }

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
            var center = new Point(cx, cy);

            var ptH1 = RotatePoint(new Point(left, cy), center, _roiEditRectAngle);
            var ptH2 = RotatePoint(new Point(right, cy), center, _roiEditRectAngle);
            var ptV1 = RotatePoint(new Point(cx, top), center, _roiEditRectAngle);
            var ptV2 = RotatePoint(new Point(cx, bottom), center, _roiEditRectAngle);

            PART_Overlay.Children.Add(new Line
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 2.0 / scale,
                X1 = ptH1.X,
                Y1 = ptH1.Y,
                X2 = ptH2.X,
                Y2 = ptH2.Y
            });

            PART_Overlay.Children.Add(new Line
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 2.0 / scale,
                X1 = ptV1.X,
                Y1 = ptV1.Y,
                X2 = ptV2.X,
                Y2 = ptV2.Y
            });
        }

        DrawRoiHandles(_roiEditRect.X, _roiEditRect.Y, _roiEditRect.Width, _roiEditRect.Height, _roiEditRectAngle, isActive: true);

        if (_roiEditing && (_roiEditMode == RoiEditMode.Rotate || Math.Abs(_roiEditRectAngle) > 0.001))
        {
            double rotOffsetY = 25.0 / scale;
            var center = new Point(cx, cy);
            var unrotStemEnd = new Point(cx, _roiEditRect.Top - rotOffsetY);
            var stemEnd = RotatePoint(unrotStemEnd, center, _roiEditRectAngle);

            var isFineMode = _roiEditMode == RoiEditMode.Rotate && Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var angleText = isFineMode ? $"[Fine] {_roiEditRectAngle:F1}°" : $"{_roiEditRectAngle:F1}°";
            var textBlock = new TextBlock
            {
                Text = angleText,
                FontSize = Math.Max(11.0, 13.0 / scale),
                FontWeight = FontWeights.Bold,
                Foreground = isFineMode ? Brushes.LimeGreen : Brushes.Yellow,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20)),
                BorderBrush = isFineMode ? Brushes.LimeGreen : (_roiEditMode == RoiEditMode.Rotate ? Brushes.Orange : Brushes.Cyan),
                BorderThickness = new Thickness(1.5 / scale),
                CornerRadius = new CornerRadius(4 / scale),
                Padding = new Thickness(5 / scale, 2 / scale, 5 / scale, 2 / scale),
                Child = textBlock
            };

            border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var badgeW = border.DesiredSize.Width;
            var badgeH = border.DesiredSize.Height;

            Canvas.SetLeft(border, stemEnd.X - badgeW / 2.0);
            Canvas.SetTop(border, stemEnd.Y - badgeH - 6.0 / scale);
            PART_Overlay.Children.Add(border);
        }
    }
}
