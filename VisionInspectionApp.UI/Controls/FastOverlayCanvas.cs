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

    private static readonly Brush CrosshairForegroundBrush = new SolidColorBrush(Color.FromRgb(0, 229, 255)); // Cyan #00E5FF
    private static readonly Brush CrosshairShadowBrush = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)); // Shadow đen tương phản

    static FastOverlayCanvas()
    {
        DarkLabelBackgroundBrush.Freeze();
        DarkLabelBorderBrush.Freeze();
        DarkLabelBorderPen.Freeze();
        CrosshairForegroundBrush.Freeze();
        CrosshairShadowBrush.Freeze();
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

    public static readonly DependencyProperty ShowCrosshairProperty = DependencyProperty.Register(
        nameof(ShowCrosshair), typeof(bool), typeof(FastOverlayCanvas),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowCrosshair
    {
        get => (bool)GetValue(ShowCrosshairProperty);
        set => SetValue(ShowCrosshairProperty, value);
    }

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

        var items = OverlayItems;
        if (items is not null)
        {
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

        // Vẽ đường tâm Crosshair công nghiệp nổi bật trên nền live camera
        if (ShowCrosshair)
        {
            double imgW = Width > 0 ? Width : ActualWidth;
            double imgH = Height > 0 ? Height : ActualHeight;

            if (imgW <= 0 || imgH <= 0)
            {
                if (ImageSource is System.Windows.Media.Imaging.BitmapSource bmpSrc)
                {
                    imgW = bmpSrc.PixelWidth * sx;
                    imgH = bmpSrc.PixelHeight * sy;
                }
                else if (ImageSource != null)
                {
                    imgW = ImageSource.Width * sx;
                    imgH = ImageSource.Height * sy;
                }
            }

            if (imgW > 0 && imgH > 0)
            {
                DrawCrosshair(dc, imgW, imgH, scale, dpi);
            }
        }
    }

    private void DrawCrosshair(DrawingContext dc, double imgW, double imgH, double scale, double dpi)
    {
        double cx = imgW / 2.0;
        double cy = imgH / 2.0;

        double shadowThickness = 2.4 / scale;
        double mainThickness = 1.0 / scale;

        var shadowPen = GetCachedPen(CrosshairShadowBrush, shadowThickness);
        var mainPen = GetCachedPen(CrosshairForegroundBrush, mainThickness);

        // 1. Trục ngang toàn phần
        dc.DrawLine(shadowPen, new Point(0, cy), new Point(imgW, cy));
        dc.DrawLine(mainPen, new Point(0, cy), new Point(imgW, cy));

        // 2. Trục dọc toàn phần
        dc.DrawLine(shadowPen, new Point(cx, 0), new Point(cx, imgH));
        dc.DrawLine(mainPen, new Point(cx, 0), new Point(cx, imgH));

        // 3. Vòng tròn tâm (Target Concentric Rings)
        double r1 = 30.0 / scale;
        double r2 = 75.0 / scale;
        double rCenter = 3.5 / scale;

        dc.DrawEllipse(null, shadowPen, new Point(cx, cy), r1, r1);
        dc.DrawEllipse(null, mainPen, new Point(cx, cy), r1, r1);

        dc.DrawEllipse(null, shadowPen, new Point(cx, cy), r2, r2);
        dc.DrawEllipse(null, mainPen, new Point(cx, cy), r2, r2);

        // Điểm tâm nhỏ
        dc.DrawEllipse(CrosshairForegroundBrush, shadowPen, new Point(cx, cy), rCenter, rCenter);

        // 4. Các vạch chia (Tick marks) mỗi 50px và 100px từ tâm
        double tickStep = 50.0;
        double maxDist = Math.Max(cx, cy);
        for (double d = tickStep; d < maxDist; d += tickStep)
        {
            bool isMajor = ((int)Math.Round(d) % 100) == 0;
            double tickLen = (isMajor ? 10.0 : 5.0) / scale;

            // Trên trục ngang (+d và -d)
            if (cx + d < imgW)
            {
                dc.DrawLine(shadowPen, new Point(cx + d, cy - tickLen), new Point(cx + d, cy + tickLen));
                dc.DrawLine(mainPen, new Point(cx + d, cy - tickLen), new Point(cx + d, cy + tickLen));
            }
            if (cx - d > 0)
            {
                dc.DrawLine(shadowPen, new Point(cx - d, cy - tickLen), new Point(cx - d, cy + tickLen));
                dc.DrawLine(mainPen, new Point(cx - d, cy - tickLen), new Point(cx - d, cy + tickLen));
            }

            // Trên trục dọc (+d và -d)
            if (cy + d < imgH)
            {
                dc.DrawLine(shadowPen, new Point(cx - tickLen, cy + d), new Point(cx + tickLen, cy + d));
                dc.DrawLine(mainPen, new Point(cx - tickLen, cy + d), new Point(cx + tickLen, cy + d));
            }
            if (cy - d > 0)
            {
                dc.DrawLine(shadowPen, new Point(cx - tickLen, cy - d), new Point(cx + tickLen, cy - d));
                dc.DrawLine(mainPen, new Point(cx - tickLen, cy - d), new Point(cx + tickLen, cy - d));
            }
        }

        // 5. Nhãn hiển thị tọa độ tâm (Cx, Cy)
        string centerText = $"✛ Center: ({(int)Math.Round(cx)}, {(int)Math.Round(cy)})";
        var ft = new FormattedText(centerText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _defaultTypeface, 11.0 / scale, CrosshairForegroundBrush, dpi);
        double tx = cx + r1 + 6.0 / scale;
        double ty = cy - ft.Height - 3.0 / scale;
        var bgRect = new Rect(tx - 4.0 / scale, ty - 2.0 / scale, ft.Width + 8.0 / scale, ft.Height + 4.0 / scale);
        dc.DrawRoundedRectangle(DarkLabelBackgroundBrush, DarkLabelBorderPen, bgRect, 3.0 / scale, 3.0 / scale);
        dc.DrawText(ft, new Point(tx, ty));
    }
}
