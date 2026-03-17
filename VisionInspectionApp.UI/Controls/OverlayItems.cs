using System.Windows.Media;

namespace VisionInspectionApp.UI.Controls;

public abstract class OverlayItem
{
    public Brush Stroke { get; init; } = Brushes.Lime;

    public double StrokeThickness { get; init; } = 2.0;

    public string? Label { get; init; }
}

public sealed class OverlayRectItem : OverlayItem
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public Brush? Fill { get; init; }
}

public sealed class OverlayPointItem : OverlayItem
{
    public double X { get; init; }
    public double Y { get; init; }

    public double Radius { get; init; } = 4.0;
}

public sealed class OverlayLineItem : OverlayItem
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
}

public sealed record LineSelection(double X1, double Y1, double X2, double Y2);

public sealed record RoiSelection(string Label, VisionInspectionApp.Models.Roi Roi, System.Windows.Input.ModifierKeys Modifiers);

public sealed record PointClickSelection(double X, double Y, System.Windows.Input.ModifierKeys Modifiers);
