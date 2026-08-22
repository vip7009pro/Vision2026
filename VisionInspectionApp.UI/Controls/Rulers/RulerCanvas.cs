using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisionInspectionApp.UI.Controls.Rulers;

public class RulerCanvas : FrameworkElement
{
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(RulerCanvas),
        new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsRender, OnPropertiesChanged));

    public static readonly DependencyProperty PixelsPerMmProperty = DependencyProperty.Register(
        nameof(PixelsPerMm), typeof(double), typeof(RulerCanvas),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(double), typeof(RulerCanvas),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        nameof(Offset), typeof(double), typeof(RulerCanvas),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MousePositionProperty = DependencyProperty.Register(
        nameof(MousePosition), typeof(double?), typeof(RulerCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ViewportMatrixProperty = DependencyProperty.Register(
        nameof(ViewportMatrix), typeof(Matrix), typeof(RulerCanvas),
        new FrameworkPropertyMetadata(Matrix.Identity, OnViewportMatrixChanged));

    public static readonly DependencyProperty MouseScreenPointProperty = DependencyProperty.Register(
        nameof(MouseScreenPoint), typeof(Point?), typeof(RulerCanvas),
        new FrameworkPropertyMetadata(null, OnMouseScreenPointChanged));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(RulerCanvas),
        new FrameworkPropertyMetadata("mm", FrameworkPropertyMetadataOptions.AffectsRender));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public double PixelsPerMm
    {
        get => (double)GetValue(PixelsPerMmProperty);
        set => SetValue(PixelsPerMmProperty, value);
    }

    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public double Offset
    {
        get => (double)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public double? MousePosition
    {
        get => (double?)GetValue(MousePositionProperty);
        set => SetValue(MousePositionProperty, value);
    }

    public Matrix ViewportMatrix
    {
        get => (Matrix)GetValue(ViewportMatrixProperty);
        set => SetValue(ViewportMatrixProperty, value);
    }

    public Point? MouseScreenPoint
    {
        get => (Point?)GetValue(MouseScreenPointProperty);
        set => SetValue(MouseScreenPointProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    private static void OnPropertiesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RulerCanvas r)
        {
            r.UpdateFromMatrix();
            r.UpdateFromMousePoint();
        }
    }

    private static void OnViewportMatrixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RulerCanvas r)
        {
            r.UpdateFromMatrix();
        }
    }

    private static void OnMouseScreenPointChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RulerCanvas r)
        {
            r.UpdateFromMousePoint();
        }
    }

    private void UpdateFromMatrix()
    {
        var m = ViewportMatrix;
        if (Orientation == Orientation.Horizontal)
        {
            Scale = m.M11 > 0 ? m.M11 : 1.0;
            Offset = m.OffsetX;
        }
        else
        {
            Scale = m.M22 > 0 ? m.M22 : 1.0;
            Offset = m.OffsetY;
        }
        InvalidateVisual();
    }

    private void UpdateFromMousePoint()
    {
        var p = MouseScreenPoint;
        if (p.HasValue)
        {
            MousePosition = (Orientation == Orientation.Horizontal) ? p.Value.X : p.Value.Y;
        }
        else
        {
            MousePosition = null;
        }
        InvalidateVisual();
    }

    private static readonly Brush BgBrush = new SolidColorBrush(Color.FromRgb(26, 28, 34));
    private static readonly Brush BorderBrushRuler = new SolidColorBrush(Color.FromRgb(55, 60, 72));
    private static readonly Pen MajorTickPen = new(new SolidColorBrush(Color.FromRgb(180, 195, 210)), 1.0);
    private static readonly Pen MinorTickPen = new(new SolidColorBrush(Color.FromRgb(100, 115, 130)), 1.0);
    private static readonly Pen MouseTrackerPen = new(new SolidColorBrush(Color.FromRgb(255, 215, 0)), 1.5);
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(190, 205, 220));
    private static readonly Typeface RulerTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    static RulerCanvas()
    {
        BgBrush.Freeze();
        BorderBrushRuler.Freeze();
        MajorTickPen.Freeze();
        MinorTickPen.Freeze();
        MouseTrackerPen.Freeze();
        TextBrush.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Draw background
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

        // Draw border separator
        if (Orientation == Orientation.Horizontal)
        {
            dc.DrawLine(new Pen(BorderBrushRuler, 1.0), new Point(0, h - 0.5), new Point(w, h - 0.5));
        }
        else
        {
            dc.DrawLine(new Pen(BorderBrushRuler, 1.0), new Point(w - 0.5, 0), new Point(w - 0.5, h));
        }

        double pxPerMm = PixelsPerMm > 0 ? PixelsPerMm : 1.0;
        double zoom = Scale > 0 ? Scale : 1.0;
        double offset = Offset;

        // 1 mm in screen pixels = pxPerMm * zoom
        double screenPxPerMm = pxPerMm * zoom;
        if (screenPxPerMm < 1e-4) return;

        // Determine nice step in mm so tick marks are roughly 40-100 pixels apart
        double targetPixelDistance = 60.0;
        double rawMmStep = targetPixelDistance / screenPxPerMm;
        double majorStepMm = CalculateNiceStep(rawMmStep);
        double minorStepMm = majorStepMm / 5.0;

        double majorStepScreenPx = majorStepMm * screenPxPerMm;
        double minorStepScreenPx = minorStepMm * screenPxPerMm;

        double totalLength = (Orientation == Orientation.Horizontal) ? w : h;

        // Calculate starting mm at screen coordinate 0
        // Screen = (Mm * pxPerMm) * zoom + offset  =>  Mm = (Screen - offset) / (pxPerMm * zoom)
        double startMm = (0 - offset) / screenPxPerMm;
        double endMm = (totalLength - offset) / screenPxPerMm;

        double firstMajorMm = Math.Floor(startMm / majorStepMm) * majorStepMm;

        // Draw Ticks and Labels
        for (double mm = firstMajorMm; mm <= endMm + majorStepMm; mm += majorStepMm)
        {
            double majorScreenPos = mm * screenPxPerMm + offset;

            // Draw Minor Ticks between major ticks
            for (int i = 1; i < 5; i++)
            {
                double minorMm = mm + i * minorStepMm;
                double minorScreenPos = minorMm * screenPxPerMm + offset;
                if (minorScreenPos >= 0 && minorScreenPos <= totalLength)
                {
                    DrawTick(dc, minorScreenPos, isMajor: false, w, h);
                }
            }

            // Draw Major Tick & Label
            if (majorScreenPos >= 0 && majorScreenPos <= totalLength)
            {
                DrawTick(dc, majorScreenPos, isMajor: true, w, h);
                DrawLabel(dc, majorScreenPos, mm, w, h);
            }
        }

        // Draw Mouse position indicator line
        if (MousePosition.HasValue && MousePosition.Value >= 0 && MousePosition.Value <= totalLength)
        {
            double pos = MousePosition.Value;
            if (Orientation == Orientation.Horizontal)
            {
                dc.DrawLine(MouseTrackerPen, new Point(pos, 0), new Point(pos, h));
            }
            else
            {
                dc.DrawLine(MouseTrackerPen, new Point(0, pos), new Point(w, pos));
            }
        }
    }

    private void DrawTick(DrawingContext dc, double screenPos, bool isMajor, double w, double h)
    {
        var pen = isMajor ? MajorTickPen : MinorTickPen;
        if (Orientation == Orientation.Horizontal)
        {
            double tickLen = isMajor ? h * 0.45 : h * 0.25;
            dc.DrawLine(pen, new Point(screenPos, h - tickLen), new Point(screenPos, h));
        }
        else
        {
            double tickLen = isMajor ? w * 0.45 : w * 0.25;
            dc.DrawLine(pen, new Point(w - tickLen, screenPos), new Point(w, screenPos));
        }
    }

    private void DrawLabel(DrawingContext dc, double screenPos, double mm, double w, double h)
    {
        string text = FormatMmValue(mm);
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            RulerTypeface,
            9.0,
            TextBrush,
            1.0);

        if (Orientation == Orientation.Horizontal)
        {
            double tx = screenPos + 2;
            double ty = 2;
            if (tx + formatted.Width > w) tx = screenPos - formatted.Width - 2;
            dc.DrawText(formatted, new Point(tx, ty));
        }
        else
        {
            // Vertical ruler: rotate label 90 degrees or stack
            dc.PushTransform(new TranslateTransform(2, screenPos + 2));
            dc.PushTransform(new RotateTransform(-90));
            dc.DrawText(formatted, new Point(-formatted.Width, 0));
            dc.Pop();
            dc.Pop();
        }
    }

    private static string FormatMmValue(double mm)
    {
        if (Math.Abs(mm) < 1e-6) return "0";
        if (Math.Abs(mm) >= 100) return mm.ToString("0");
        if (Math.Abs(mm) >= 10) return mm.ToString("0.#");
        if (Math.Abs(mm) >= 1) return mm.ToString("0.##");
        return mm.ToString("0.###");
    }

    private static double CalculateNiceStep(double rawStep)
    {
        if (rawStep <= 0) return 1.0;
        double exponent = Math.Floor(Math.Log10(rawStep));
        double fraction = rawStep / Math.Pow(10, exponent);

        double niceFraction = fraction switch
        {
            <= 1.0 => 1.0,
            <= 2.0 => 2.0,
            <= 5.0 => 5.0,
            _ => 10.0
        };

        return niceFraction * Math.Pow(10, exponent);
    }
}
