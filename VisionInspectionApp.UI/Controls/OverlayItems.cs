using System.Windows.Media;

namespace VisionInspectionApp.UI.Controls;

public abstract class OverlayItem
{
    public Brush Stroke { get; init; } = Brushes.Lime;

    public double StrokeThickness { get; init; } = 2.0;

    public string? Label { get; init; }

    public DoubleCollection? DashArray { get; init; }
}

public sealed class OverlayRectItem : OverlayItem
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double Angle { get; init; } = 0;

    public Brush? Fill { get; init; }

    public bool IsCaliperStrip { get; init; } = false;
    public bool IsHorizontalStrip { get; init; } = true;
}

public sealed class OverlayPointItem : OverlayItem
{
    public double X { get; init; }
    public double Y { get; init; }

    public double Radius { get; init; } = 4.0;
    public Brush? Fill { get; init; }
}

public sealed class OverlayCircleItem : OverlayItem
{
    public double CenterX { get; init; }
    public double CenterY { get; init; }
    public double Radius { get; init; }
    public Brush? Fill { get; init; }
}

public sealed class OverlayLineItem : OverlayItem
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
}

public sealed class OverlayPolylineItem : OverlayItem
{
    public List<System.Windows.Point> Points { get; init; } = new();
    public bool IsClosed { get; init; } = true;

    private StreamGeometry? _cachedGeometry;
    private double _cachedSx = double.NaN;
    private double _cachedSy = double.NaN;

    /// <summary>
    /// PERFORMANCE PHASE 4: cache StreamGeometry theo scale đã dùng.
    /// Trước đây chỉ cache khi scale == 1.0 chính xác, nên khi màn hình có DPI scaling khác 1
    /// (hoặc bitmap proxy) thì geometry bị dựng lại ở MỖI frame render.
    /// Vì <see cref="Points"/> là init-only nên cache không bao giờ bị cũ.
    /// </summary>
    public StreamGeometry GetOrCreateGeometry(double sx = 1.0, double sy = 1.0)
    {
        if (_cachedGeometry is not null
            && Math.Abs(_cachedSx - sx) < 1e-6
            && Math.Abs(_cachedSy - sy) < 1e-6)
        {
            return _cachedGeometry;
        }

        var geo = new StreamGeometry();
        if (Points.Count > 1)
        {
            using var ctx = geo.Open();
            var startPt = new System.Windows.Point(Points[0].X * sx, Points[0].Y * sy);
            ctx.BeginFigure(startPt, isFilled: false, isClosed: IsClosed);
            for (int i = 1; i < Points.Count; i++)
            {
                ctx.LineTo(new System.Windows.Point(Points[i].X * sx, Points[i].Y * sy), isStroked: true, isSmoothJoin: true);
            }
        }
        geo.Freeze();

        _cachedGeometry = geo;
        _cachedSx = sx;
        _cachedSy = sy;
        return geo;
    }
}

public sealed class OverlayTextItem : OverlayItem
{
    public double X { get; init; }
    public double Y { get; init; }

    public string Text { get; init; } = string.Empty;

    public Brush Foreground { get; init; } = Brushes.White;

    public Brush? Background { get; init; }
}

public sealed record LineSelection(double X1, double Y1, double X2, double Y2);

public sealed record RoiSelection(string Label, VisionInspectionApp.Models.Roi Roi, System.Windows.Input.ModifierKeys Modifiers);

public sealed record PointClickSelection(double X, double Y, System.Windows.Input.ModifierKeys Modifiers);
