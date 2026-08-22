using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VisionInspectionApp.UI.Controls;

public class FastOverlayCanvas : FrameworkElement
{
    private static readonly Dictionary<(Brush, double), Pen> _penCache = new();
    private static readonly Typeface _defaultTypeface = new("Segoe UI");

    private static readonly Brush DarkLabelBackgroundBrush = new SolidColorBrush(Color.FromArgb(210, 16, 20, 28)); // #D210141C
    private static readonly Brush DarkLabelBorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
    private static readonly Pen DarkLabelBorderPen = new(DarkLabelBorderBrush, 0.8);

    static FastOverlayCanvas()
    {
        DarkLabelBackgroundBrush.Freeze();
        DarkLabelBorderBrush.Freeze();
        DarkLabelBorderPen.Freeze();
    }

    private static Pen GetCachedPen(Brush brush, double thickness, DoubleCollection? dashArray = null)
    {
        if (brush is null) return new Pen(Brushes.Transparent, thickness);
        if (dashArray is null || dashArray.Count == 0)
        {
            var key = (brush, thickness);
            if (_penCache.TryGetValue(key, out var pen))
                return pen;
            
            pen = new Pen(brush, thickness);
            pen.Freeze();
            _penCache[key] = pen;
            return pen;
        }
        else
        {
            var pen = new Pen(brush, thickness)
            {
                DashStyle = new DashStyle(dashArray, 0)
            };
            pen.Freeze();
            return pen;
        }
    }

    public static readonly DependencyProperty OverlayItemsProperty = DependencyProperty.Register(
        nameof(OverlayItems), typeof(IEnumerable<OverlayItem>), typeof(FastOverlayCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnOverlayItemsChanged));

    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource), typeof(ImageSource), typeof(FastOverlayCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnImageSourceChanged));

    public static readonly DependencyProperty ViewScaleProperty = DependencyProperty.Register(
        nameof(ViewScale), typeof(double), typeof(FastOverlayCanvas),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double ViewScale
    {
        get => (double)GetValue(ViewScaleProperty);
        set => SetValue(ViewScaleProperty, value);
    }

    public IEnumerable<OverlayItem>? OverlayItems
    {
        get => (IEnumerable<OverlayItem>?)GetValue(OverlayItemsProperty);
        set => SetValue(OverlayItemsProperty, value);
    }

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((FastOverlayCanvas)d).InvalidateVisual();
    }

    private static void OnOverlayItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (FastOverlayCanvas)d;
        if (e.OldValue is INotifyCollectionChanged oldColl)
        {
            oldColl.CollectionChanged -= canvas.OnOverlayCollectionChanged;
        }
        if (e.NewValue is INotifyCollectionChanged newColl)
        {
            newColl.CollectionChanged += canvas.OnOverlayCollectionChanged;
        }
        canvas.InvalidateVisual();
    }

    private void OnOverlayCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var items = OverlayItems;
        if (items is null)
        {
            return;
        }

        double sx = 1.0;
        double sy = 1.0;

        if (ImageSource is System.Windows.Media.Imaging.BitmapSource bmp)
        {
            sx = bmp.Width / bmp.PixelWidth;
            sy = bmp.Height / bmp.PixelHeight;
        }

        var typeface = _defaultTypeface;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double scale = Math.Max(0.001, ViewScale);
        double effFontSize = 13.0 / scale;
        double effTextFontSize = 14.0 / scale;

        foreach (var item in items)
        {
            double baseThickness = item.StrokeThickness > 0 ? item.StrokeThickness : 2.0;
            double effThickness = baseThickness / scale;
            var pen = GetCachedPen(item.Stroke, effThickness, item.DashArray);

            if (item is OverlayRectItem r)
            {
                var vx = r.X * sx;
                var vy = r.Y * sy;
                var vw = Math.Max(1.0, r.Width * sx);
                var vh = Math.Max(1.0, r.Height * sy);

                if (Math.Abs(r.Angle) > 0.001)
                {
                    var cx = vx + vw / 2.0;
                    var cy = vy + vh / 2.0;
                    dc.PushTransform(new RotateTransform(r.Angle, cx, cy));
                }

                dc.DrawRectangle(r.Fill, pen, new Rect(vx, vy, vw, vh));

                if (!string.IsNullOrWhiteSpace(r.Label))
                {
                    var text = new FormattedText(r.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, effFontSize, item.Stroke, dpi);
                    double tx = vx;
                    double ty = vy - text.Height - 4 / scale;
                    double padX = 4 / scale;
                    double padY = 2 / scale;
                    var bgRect = new Rect(tx - padX, ty - padY, text.Width + padX * 2, text.Height + padY * 2);
                    dc.DrawRoundedRectangle(DarkLabelBackgroundBrush, DarkLabelBorderPen, bgRect, 3 / scale, 3 / scale);
                    dc.DrawText(text, new Point(tx, ty));
                }

                if (Math.Abs(r.Angle) > 0.001)
                {
                    dc.Pop();
                }
            }
            else if (item is OverlayPointItem p)
            {
                var vx = p.X * sx;
                var vy = p.Y * sy;
                var pr = (p.Radius > 0 ? p.Radius : 4.0) / scale;
                dc.DrawEllipse(p.Fill ?? item.Stroke, pen, new Point(vx, vy), pr, pr);

                if (!string.IsNullOrWhiteSpace(p.Label))
                {
                    var text = new FormattedText(p.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, effFontSize, item.Stroke, dpi);
                    double tx = vx + pr + 6 / scale;
                    double ty = vy - text.Height / 2.0;
                    double padX = 4 / scale;
                    double padY = 2 / scale;
                    var bgRect = new Rect(tx - padX, ty - padY, text.Width + padX * 2, text.Height + padY * 2);
                    dc.DrawRoundedRectangle(DarkLabelBackgroundBrush, DarkLabelBorderPen, bgRect, 3 / scale, 3 / scale);
                    dc.DrawText(text, new Point(tx, ty));
                }
            }
            else if (item is OverlayCircleItem c)
            {
                var cx = c.CenterX * sx;
                var cy = c.CenterY * sy;
                var cr = Math.Max(1.0, c.Radius * Math.Max(sx, sy));
                dc.DrawEllipse(c.Fill, pen, new Point(cx, cy), cr, cr);

                if (!string.IsNullOrWhiteSpace(c.Label))
                {
                    var text = new FormattedText(c.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, effFontSize, item.Stroke, dpi);
                    double tx = cx - text.Width / 2.0;
                    double ty = cy - cr - text.Height - 4 / scale;
                    double padX = 4 / scale;
                    double padY = 2 / scale;
                    var bgRect = new Rect(tx - padX, ty - padY, text.Width + padX * 2, text.Height + padY * 2);
                    dc.DrawRoundedRectangle(DarkLabelBackgroundBrush, DarkLabelBorderPen, bgRect, 3 / scale, 3 / scale);
                    dc.DrawText(text, new Point(tx, ty));
                }
            }
            else if (item is OverlayLineItem l)
            {
                var vx1 = l.X1 * sx;
                var vy1 = l.Y1 * sy;
                var vx2 = l.X2 * sx;
                var vy2 = l.Y2 * sy;
                dc.DrawLine(pen, new Point(vx1, vy1), new Point(vx2, vy2));

                if (!string.IsNullOrWhiteSpace(l.Label))
                {
                    var text = new FormattedText(l.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, effFontSize, item.Stroke, dpi);
                    double midX = (vx1 + vx2) / 2.0;
                    double midY = (vy1 + vy2) / 2.0;
                    double tx = midX - text.Width / 2.0;
                    double ty = midY - text.Height / 2.0;
                    double padX = 6 / scale;
                    double padY = 3 / scale;
                    var bgRect = new Rect(tx - padX, ty - padY, text.Width + padX * 2, text.Height + padY * 2);
                    dc.DrawRoundedRectangle(DarkLabelBackgroundBrush, DarkLabelBorderPen, bgRect, 4 / scale, 4 / scale);
                    dc.DrawText(text, new Point(tx, ty));
                }
            }
            else if (item is OverlayPolylineItem pl)
            {
                if (pl.Points is not null && pl.Points.Count > 1)
                {
                    var geo = pl.GetOrCreateGeometry(sx, sy);
                    dc.DrawGeometry(null, pen, geo);

                    if (!string.IsNullOrWhiteSpace(pl.Label))
                    {
                        var text = new FormattedText(pl.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, effFontSize, item.Stroke, dpi);
                        var firstPt = new Point(pl.Points[0].X * sx, pl.Points[0].Y * sy);
                        double tx = firstPt.X;
                        double ty = firstPt.Y - text.Height - 4 / scale;
                        double padX = 4 / scale;
                        double padY = 2 / scale;
                        var bgRect = new Rect(tx - padX, ty - padY, text.Width + padX * 2, text.Height + padY * 2);
                        dc.DrawRoundedRectangle(DarkLabelBackgroundBrush, DarkLabelBorderPen, bgRect, 3 / scale, 3 / scale);
                        dc.DrawText(text, new Point(tx, ty));
                    }
                }
            }
            else if (item is OverlayTextItem t)
            {
                var vx = t.X * sx;
                var vy = t.Y * sy;
                var text = new FormattedText(t.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, effTextFontSize, t.Foreground, dpi);
                var bg = t.Background ?? DarkLabelBackgroundBrush;
                double padX = 4 / scale;
                double padY = 2 / scale;
                var bgRect = new Rect(vx - padX, vy - padY, text.Width + padX * 2, text.Height + padY * 2);
                dc.DrawRoundedRectangle(bg, DarkLabelBorderPen, bgRect, 3 / scale, 3 / scale);
                dc.DrawText(text, new Point(vx, vy));
            }
        }
    }
}
