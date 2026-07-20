using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VisionInspectionApp.UI.Controls;

public class FastOverlayCanvas : FrameworkElement
{
    private static readonly Dictionary<(Brush, double), Pen> _penCache = new();

    private static Pen GetCachedPen(Brush brush, double thickness)
    {
        if (brush is null) return new Pen(Brushes.Transparent, thickness);
        var key = (brush, thickness);
        if (_penCache.TryGetValue(key, out var pen))
            return pen;
        
        pen = new Pen(brush, thickness);
        pen.Freeze();
        _penCache[key] = pen;
        return pen;
    }
    public static readonly DependencyProperty OverlayItemsProperty = DependencyProperty.Register(
        nameof(OverlayItems), typeof(IEnumerable<OverlayItem>), typeof(FastOverlayCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnOverlayItemsChanged));

    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource), typeof(ImageSource), typeof(FastOverlayCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnOverlayItemsChanged));

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

    private static void OnOverlayItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((FastOverlayCanvas)d).InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var items = OverlayItems;
        if (items is null)
        {
            System.Diagnostics.Debug.WriteLine("FastOverlayCanvas.OnRender: items is NULL");
            return;
        }

        var itemList = new List<OverlayItem>(items);
        System.Diagnostics.Debug.WriteLine($"FastOverlayCanvas.OnRender: rendering {itemList.Count} items.");


        double sx = 1.0;
        double sy = 1.0;

        if (ImageSource is System.Windows.Media.Imaging.BitmapSource bmp)
        {
            sx = bmp.Width / bmp.PixelWidth;
            sy = bmp.Height / bmp.PixelHeight;
        }

        var typeface = new Typeface("Segoe UI");
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var item in items)
        {
            var pen = GetCachedPen(item.Stroke, item.StrokeThickness > 0 ? item.StrokeThickness : 2.0);

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
                    var text = new FormattedText(r.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 12.0, item.Stroke, dpi);
                    dc.DrawText(text, new Point(vx, vy - text.Height - 2));
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
                dc.DrawEllipse(null, pen, new Point(vx, vy), p.Radius, p.Radius);

                if (!string.IsNullOrWhiteSpace(p.Label))
                {
                    var text = new FormattedText(p.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 12.0, item.Stroke, dpi);
                    dc.DrawText(text, new Point(vx + p.Radius + 2, vy - text.Height / 2));
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
                    var text = new FormattedText(l.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 12.0, item.Stroke, dpi);
                    dc.DrawText(text, new Point((vx1 + vx2) / 2, (vy1 + vy2) / 2));
                }
            }
            else if (item is OverlayTextItem t)
            {
                var vx = t.X * sx;
                var vy = t.Y * sy;
                var text = new FormattedText(t.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 14.0, t.Foreground, dpi);
                if (t.Background is not null)
                {
                    dc.DrawRectangle(t.Background, null, new Rect(new Point(vx, vy), new Size(text.Width, text.Height)));
                }
                dc.DrawText(text, new Point(vx, vy));
            }
        }
    }
}
