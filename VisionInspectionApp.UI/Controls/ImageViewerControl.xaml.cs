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

    public static readonly DependencyProperty RoiDeletedCommandProperty = DependencyProperty.Register(
        nameof(RoiDeletedCommand),
        typeof(ICommand),
        typeof(ImageViewerControl),
        new PropertyMetadata(null));

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

        PART_Overlay.MouseWheel += OverlayOnMouseWheel;
        PART_Overlay.MouseDown += OverlayOnMouseDown;
        PART_Overlay.MouseUp += OverlayOnMouseUp;
        PART_Overlay.MouseMove += OverlayOnPanMouseMove;

        PART_Overlay.SizeChanged += (_, __) => RedrawOverlays();

        Loaded += (_, __) => RedrawOverlays();

        SetupTransforms();
    }

    private readonly ScaleTransform _scale = new(1.0, 1.0);
    private readonly TranslateTransform _translate = new(0.0, 0.0);

    private bool _panning;
    private Point _panStart;
    private double _panStartX;
    private double _panStartY;

    private void SetupTransforms()
    {
        var tg = new TransformGroup();
        tg.Children.Add(_scale);
        tg.Children.Add(_translate);
        PART_Content.RenderTransform = tg;
        PART_Content.RenderTransformOrigin = new Point(0, 0);
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

        // Keep zoom/pan when the preview refreshes with the same-sized image.
        // Reset only when the actual image size changes (or when switching from/to null).
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
            if (changed)
            {
                c.ResetView();
            }
        }
        c.RedrawOverlays();
    }

    private void ResetView()
    {
        _scale.ScaleX = 1.0;
        _scale.ScaleY = 1.0;
        _translate.X = 0.0;
        _translate.Y = 0.0;
        _panning = false;
    }

    private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

    private Point ViewToContent(Point overlayPoint)
    {
        var gt = PART_Content.TransformToVisual(PART_Overlay);
        if (gt is null)
        {
            return overlayPoint;
        }

        if (!gt.TryTransform(new Point(0, 0), out _))
        {
            return overlayPoint;
        }

        if (gt.Inverse is null)
        {
            return overlayPoint;
        }

        return gt.Inverse.Transform(overlayPoint);
    }

    private void OverlayOnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        var viewPos = e.GetPosition(PART_Overlay);
        var before = ViewToContent(viewPos);

        var zoomFactor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        var newScale = Clamp(_scale.ScaleX * zoomFactor, 0.1, 20.0);

        _scale.ScaleX = newScale;
        _scale.ScaleY = newScale;

        _translate.X = viewPos.X - before.X * newScale;
        _translate.Y = viewPos.Y - before.Y * newScale;

        e.Handled = true;
    }

    private void OverlayOnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _panning = true;
        _panStart = e.GetPosition(PART_Overlay);
        _panStartX = _translate.X;
        _panStartY = _translate.Y;
        PART_Overlay.CaptureMouse();
        e.Handled = true;
    }

    private void OverlayOnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _panning = false;
        PART_Overlay.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OverlayOnPanMouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning)
        {
            return;
        }

        var p = e.GetPosition(PART_Overlay);
        var dx = p.X - _panStart.X;
        var dy = p.Y - _panStart.Y;
        _translate.X = _panStartX + dx;
        _translate.Y = _panStartY + dy;
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

    public ICommand? RoiDeletedCommand
    {
        get => (ICommand?)GetValue(RoiDeletedCommandProperty);
        set => SetValue(RoiDeletedCommandProperty, value);
    }

    public IEnumerable<OverlayItem>? OverlayItems
    {
        get => (IEnumerable<OverlayItem>?)GetValue(OverlayItemsProperty);
        set => SetValue(OverlayItemsProperty, value);
    }

    private INotifyCollectionChanged? _overlayNotify;

    private static void OnOverlayItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ImageViewerControl)d;

        if (c._overlayNotify is not null)
        {
            c._overlayNotify.CollectionChanged -= c.OverlayItems_CollectionChanged;
            c._overlayNotify = null;
        }

        c._overlayNotify = e.NewValue as INotifyCollectionChanged;
        if (c._overlayNotify is not null)
        {
            c._overlayNotify.CollectionChanged += c.OverlayItems_CollectionChanged;
        }

        c.RedrawOverlays();
    }

    private void OverlayItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RedrawOverlays();
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
            // Prefer active T; otherwise swap S->T; otherwise any T in overlays.
            if (!string.IsNullOrWhiteSpace(_activeRoiLabel) && EndsWith(_activeRoiLabel!, " T")) return _activeRoiLabel;

            var swapped = TrySwapSuffix(_activeRoiLabel, "T");
            if (!string.IsNullOrWhiteSpace(swapped)) return swapped;

            var t = rectLabels.FirstOrDefault(x => EndsWith(x, " T"));
            if (!string.IsNullOrWhiteSpace(t)) return t;

            // If we only have a Search ROI overlay, synthesize a matching Template label.
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
            // For Search: Point uses " S", Line uses " L", LPD uses " LP", CDT uses " C", Origin uses "Origin S".
            // Prefer active S/L/LP/C; otherwise swap T->S; otherwise any S; otherwise any L/LP/C.
            if (!string.IsNullOrWhiteSpace(_activeRoiLabel)
                && (EndsWith(_activeRoiLabel!, " S")
                    || EndsWith(_activeRoiLabel!, " L")
                    || EndsWith(_activeRoiLabel!, " LP")
                    || EndsWith(_activeRoiLabel!, " C")))
            {
                return _activeRoiLabel;
            }

            var swapped = TrySwapSuffix(_activeRoiLabel, "S");
            if (!string.IsNullOrWhiteSpace(swapped)) return swapped;

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

        return label.EndsWith(" T", StringComparison.OrdinalIgnoreCase)
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
        if (_dragging)
        {
            return;
        }

        PART_Overlay.Children.Clear();

        if (ImageSource is not BitmapSource bmp)
        {
            return;
        }

        PART_Image.Width = bmp.Width;
        PART_Image.Height = bmp.Height;
        PART_Overlay.Width = bmp.Width;
        PART_Overlay.Height = bmp.Height;

        if (OverlayItems is null)
        {
            return;
        }

        var rects = new List<OverlayRectItem>();
        var others = new List<OverlayItem>();
        foreach (var item in OverlayItems)
        {
            if (item is OverlayRectItem r)
            {
                rects.Add(r);
            }
            else
            {
                others.Add(item);
            }
        }

        var active = string.IsNullOrWhiteSpace(_activeRoiLabel)
            ? null
            : rects.FirstOrDefault(x => string.Equals(x.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase));

        foreach (var r in rects)
        {
            if (active is not null && string.Equals(r.Label, active.Label, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DrawRect(bmp, r);
        }

        if (active is not null)
        {
            DrawRect(bmp, active);
        }

        foreach (var item in others)
        {
            switch (item)
            {
                case OverlayPointItem p:
                    DrawPoint(bmp, p);
                    break;
                case OverlayLineItem l:
                    DrawLine(bmp, l);
                    break;
            }
        }
    }

    private void DrawRect(BitmapSource bmp, OverlayRectItem r)
    {
        var sx = bmp.Width / bmp.PixelWidth;
        var sy = bmp.Height / bmp.PixelHeight;

        var vx = r.X * sx;
        var vy = r.Y * sy;
        var vx2 = (r.X + r.Width) * sx;
        var vy2 = (r.Y + r.Height) * sy;

        var rect = new Rectangle
        {
            Stroke = r.Stroke,
            StrokeThickness = r.StrokeThickness,
            Fill = r.Fill
        };

        Canvas.SetLeft(rect, vx);
        Canvas.SetTop(rect, vy);
        rect.Width = Math.Max(1, vx2 - vx);
        rect.Height = Math.Max(1, vy2 - vy);
        PART_Overlay.Children.Add(rect);

        var showHandles = (!string.IsNullOrWhiteSpace(r.Label)
            && (
                string.Equals(r.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Label, _hoverRoiLabel, StringComparison.OrdinalIgnoreCase)
            ));

        if (showHandles)
        {
            DrawRoiHandles(vx, vy, rect.Width, rect.Height, string.Equals(r.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(r.Label))
        {
            var tb = new TextBlock
            {
                Text = r.Label,
                Foreground = r.Stroke,
                Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                Padding = new Thickness(2, 0, 2, 0)
            };

            Canvas.SetLeft(tb, vx + 2);
            Canvas.SetTop(tb, Math.Max(0, vy - 18));
            PART_Overlay.Children.Add(tb);
        }
    }

    private void DrawPoint(BitmapSource bmp, OverlayPointItem p)
    {
        var sx = bmp.Width / bmp.PixelWidth;
        var sy = bmp.Height / bmp.PixelHeight;
        var vx = p.X * sx;
        var vy = p.Y * sy;
        var rr = p.Radius;

        var ellipse = new Ellipse
        {
            Stroke = p.Stroke,
            StrokeThickness = p.StrokeThickness,
            Width = rr * 2,
            Height = rr * 2
        };

        Canvas.SetLeft(ellipse, vx - rr);
        Canvas.SetTop(ellipse, vy - rr);
        PART_Overlay.Children.Add(ellipse);

        if (!string.IsNullOrWhiteSpace(p.Label))
        {
            var tb = new TextBlock
            {
                Text = p.Label,
                Foreground = p.Stroke,
                Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                Padding = new Thickness(2, 0, 2, 0)
            };

            Canvas.SetLeft(tb, vx + rr + 2);
            Canvas.SetTop(tb, vy - rr - 2);
            PART_Overlay.Children.Add(tb);
        }
    }

    private void DrawLine(BitmapSource bmp, OverlayLineItem l)
    {
        var sx = bmp.Width / bmp.PixelWidth;
        var sy = bmp.Height / bmp.PixelHeight;
        var vx1 = l.X1 * sx;
        var vy1 = l.Y1 * sy;
        var vx2 = l.X2 * sx;
        var vy2 = l.Y2 * sy;

        var line = new Line
        {
            X1 = vx1,
            Y1 = vy1,
            X2 = vx2,
            Y2 = vy2,
            Stroke = l.Stroke,
            StrokeThickness = l.StrokeThickness
        };

        PART_Overlay.Children.Add(line);

        if (!string.IsNullOrWhiteSpace(l.Label))
        {
            var tb = new TextBlock
            {
                Text = l.Label,
                Foreground = l.Stroke,
                Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
                Padding = new Thickness(2, 0, 2, 0)
            };

            Canvas.SetLeft(tb, (vx1 + vx2) / 2.0 + 2);
            Canvas.SetTop(tb, (vy1 + vy2) / 2.0 - 10);
            PART_Overlay.Children.Add(tb);
        }
    }

    private void OverlayOnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ImageSource is null)
        {
            return;
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
        if (EnableRoiEditing && !_roiEditing && !_dragging && !_lineDragging && ImageSource is BitmapSource bmp && OverlayItems is not null)
        {
            var viewPos = ViewToContent(e.GetPosition(PART_Overlay));
            var hover = FindTopRoiLabelAt(bmp, viewPos);
            UpdateCursorForRoiHover(bmp, viewPos, hover);
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
        var scaleX = bmp.PixelWidth / bmp.Width;
        var scaleY = bmp.PixelHeight / bmp.Height;

        var px = (int)Math.Round(contentX * scaleX);
        var py = (int)Math.Round(contentY * scaleY);
        var pw = (int)Math.Round(contentW * scaleX);
        var ph = (int)Math.Round(contentH * scaleY);

        var maxX = Math.Max(0, bmp.PixelWidth - 1);
        var maxY = Math.Max(0, bmp.PixelHeight - 1);
        px = Math.Clamp(px, 0, maxX);
        py = Math.Clamp(py, 0, maxY);

        var maxW = bmp.PixelWidth - px;
        var maxH = bmp.PixelHeight - py;
        if (maxW < 1) maxW = 1;
        if (maxH < 1) maxH = 1;

        pw = pw < 1 ? 1 : (pw > maxW ? maxW : pw);
        ph = ph < 1 ? 1 : (ph > maxH ? maxH : ph);

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
        var scaleX = bmp.PixelWidth / bmp.Width;
        var scaleY = bmp.PixelHeight / bmp.Height;

        var px = (int)Math.Round(contentX * scaleX);
        var py = (int)Math.Round(contentY * scaleY);

        px = Math.Clamp(px, 0, Math.Max(0, bmp.PixelWidth - 1));
        py = Math.Clamp(py, 0, Math.Max(0, bmp.PixelHeight - 1));

        return new Point(px, py);
    }

    private bool TryStartRoiEdit(BitmapSource bmp, Point contentPoint)
    {
        if (OverlayItems is null)
        {
            return false;
        }

        const double hitTol = 10.0;

        var candidates = new List<(OverlayRectItem Item, Rect Rect)>();

        foreach (var item in OverlayItems)
        {
            if (item is not OverlayRectItem r)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(r.Label))
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

        (OverlayRectItem Item, Rect Rect) picked;
        if (!string.IsNullOrWhiteSpace(_activeRoiLabel))
        {
            var active = candidates.FirstOrDefault(x => string.Equals(x.Item.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase));
            picked = active.Item is not null ? active : candidates.OrderBy(x => x.Rect.Width * x.Rect.Height).First();
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
        _roiEditMode = HitTestRoiHandle(picked.Rect, contentPoint, tolerance: 20.0);
        if (_roiEditMode == RoiEditMode.None)
        {
            _roiEditMode = picked.Rect.Contains(contentPoint) ? RoiEditMode.Move : RoiEditMode.None;
        }

        if (_roiEditMode == RoiEditMode.None)
        {
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

        const double hitTol = 10.0;

        var candidates = new List<(OverlayRectItem Item, Rect Rect)>();

        foreach (var item in OverlayItems)
        {
            if (item is not OverlayRectItem r)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(r.Label))
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

        return candidates.OrderBy(x => x.Rect.Width * x.Rect.Height).First().Item.Label;
    }

    private string? FindRoiLabelAtForClick(BitmapSource bmp, Point contentPoint)
    {
        if (OverlayItems is null)
        {
            return null;
        }

        const double hitTol = 10.0;

        var candidates = new List<(OverlayRectItem Item, Rect Rect)>();

        foreach (var item in OverlayItems)
        {
            if (item is not OverlayRectItem r)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(r.Label))
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

        var ordered = candidates
            .OrderBy(x => x.Rect.Width * x.Rect.Height)
            .ToList();

        if (!string.IsNullOrWhiteSpace(_activeRoiLabel) && ordered.Count > 1)
        {
            var idx = ordered.FindIndex(x => string.Equals(x.Item.Label, _activeRoiLabel, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                var next = (idx + 1) % ordered.Count;
                return ordered[next].Item.Label;
            }
        }

        return ordered[0].Item.Label;
    }

    private void DrawRoiHandles(double left, double top, double width, double height, bool isActive)
    {
        var corner = isActive ? 12.0 : 10.0;
        var edge = isActive ? 8.0 : 6.0;
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
                StrokeThickness = 1,
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

        var rect = PixelRectToContentRect(bmp, roiItem.X, roiItem.Y, roiItem.Width, roiItem.Height);
        var mode = HitTestRoiHandle(rect, contentPoint, tolerance: 20.0);

        if (mode == RoiEditMode.None)
        {
            Cursor = rect.Contains(contentPoint) ? Cursors.SizeAll : Cursors.Arrow;
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
        var sx = bmp.Width / bmp.PixelWidth;
        var sy = bmp.Height / bmp.PixelHeight;
        return new Rect(x * sx, y * sy, w * sx, h * sy);
    }

    private static RoiEditMode HitTestRoiHandle(Rect rect, Point p, double tolerance)
    {
        var left = Math.Abs(p.X - rect.Left) <= tolerance;
        var right = Math.Abs(p.X - rect.Right) <= tolerance;
        var top = Math.Abs(p.Y - rect.Top) <= tolerance;
        var bottom = Math.Abs(p.Y - rect.Bottom) <= tolerance;

        if (left && top) return RoiEditMode.TopLeft;
        if (right && top) return RoiEditMode.TopRight;
        if (left && bottom) return RoiEditMode.BottomLeft;
        if (right && bottom) return RoiEditMode.BottomRight;

        if (left) return RoiEditMode.Left;
        if (right) return RoiEditMode.Right;
        if (top) return RoiEditMode.Top;
        if (bottom) return RoiEditMode.Bottom;

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

        _roiEditRectShape = new Rectangle
        {
            Stroke = Brushes.Cyan,
            StrokeThickness = 2,
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
                StrokeThickness = 2,
                X1 = left,
                Y1 = cy,
                X2 = right,
                Y2 = cy
            });

            PART_Overlay.Children.Add(new Line
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 2,
                X1 = cx,
                Y1 = top,
                X2 = cx,
                Y2 = bottom
            });
        }

        const double hs = 6.0;
        AddHandle(_roiEditRect.Left, _roiEditRect.Top);
        AddHandle(_roiEditRect.Right, _roiEditRect.Top);
        AddHandle(_roiEditRect.Left, _roiEditRect.Bottom);
        AddHandle(_roiEditRect.Right, _roiEditRect.Bottom);
        AddHandle((_roiEditRect.Left + _roiEditRect.Right) / 2.0, _roiEditRect.Top);
        AddHandle((_roiEditRect.Left + _roiEditRect.Right) / 2.0, _roiEditRect.Bottom);
        AddHandle(_roiEditRect.Left, (_roiEditRect.Top + _roiEditRect.Bottom) / 2.0);
        AddHandle(_roiEditRect.Right, (_roiEditRect.Top + _roiEditRect.Bottom) / 2.0);

        void AddHandle(double x, double y)
        {
            var h = new Rectangle
            {
                Width = hs,
                Height = hs,
                Stroke = Brushes.Cyan,
                StrokeThickness = 1,
                Fill = Brushes.Black
            };

            Canvas.SetLeft(h, x - hs / 2.0);
            Canvas.SetTop(h, y - hs / 2.0);
            _roiEditHandles.Add(h);
            PART_Overlay.Children.Add(h);
        }
    }
}
