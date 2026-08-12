using System;
using System.Collections.Generic;
using System.Linq;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels;

public partial class ToolEditorViewModel
{
    public Array AvailableRectAnchors => Enum.GetValues(typeof(RectAnchorPosition));
    public Array AvailableCreateLineModes => Enum.GetValues(typeof(CreateLineMode));
    public Array AvailableCreateCircleModes => Enum.GetValues(typeof(CreateCircleMode));

    // ==========================================
    // 1. CreatePoint
    // ==========================================
    public bool IsCreatePointNode => string.Equals(SelectedNode?.Type, "CreatePoint", StringComparison.OrdinalIgnoreCase);

    public CreatePointDefinition? SelectedCreatePoint =>
        _config?.CreatePoints?.FirstOrDefault(x => string.Equals(x.Name, SelectedNode?.RefName, StringComparison.OrdinalIgnoreCase));

    public double CreatePoint_X
    {
        get => SelectedCreatePoint?.X ?? 0.0;
        set
        {
            var def = SelectedCreatePoint;
            if (def is null || Math.Abs(def.X - value) < 0.001) return;
            def.X = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreatePoint_Y
    {
        get => SelectedCreatePoint?.Y ?? 0.0;
        set
        {
            var def = SelectedCreatePoint;
            if (def is null || Math.Abs(def.Y - value) < 0.001) return;
            def.Y = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public string CreatePoint_PointRef
    {
        get => SelectedCreatePoint?.PointRef ?? string.Empty;
        set
        {
            var def = SelectedCreatePoint;
            if (def is null) return;
            def.PointRef = value ?? string.Empty;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    // ==========================================
    // 2. CreateLine
    // ==========================================
    public bool IsCreateLineNode => string.Equals(SelectedNode?.Type, "CreateLine", StringComparison.OrdinalIgnoreCase);

    public CreateLineDefinition? SelectedCreateLine =>
        _config?.CreateLines?.FirstOrDefault(x => string.Equals(x.Name, SelectedNode?.RefName, StringComparison.OrdinalIgnoreCase));

    public CreateLineMode CreateLine_Mode
    {
        get => SelectedCreateLine?.Mode ?? CreateLineMode.TwoPoints;
        set
        {
            var def = SelectedCreateLine;
            if (def is null || def.Mode == value) return;
            def.Mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CreateLine_IsTwoPointsMode));
            OnPropertyChanged(nameof(CreateLine_IsPointAndAngleMode));
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public bool CreateLine_IsTwoPointsMode => CreateLine_Mode == CreateLineMode.TwoPoints;
    public bool CreateLine_IsPointAndAngleMode => CreateLine_Mode == CreateLineMode.PointAndAngle;

    public string CreateLine_Point1Ref
    {
        get => SelectedCreateLine?.Point1Ref ?? string.Empty;
        set
        {
            var def = SelectedCreateLine;
            if (def is null) return;
            def.Point1Ref = value ?? string.Empty;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateLine_X1
    {
        get => SelectedCreateLine?.X1 ?? 0.0;
        set
        {
            var def = SelectedCreateLine;
            if (def is null || Math.Abs(def.X1 - value) < 0.001) return;
            def.X1 = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateLine_Y1
    {
        get => SelectedCreateLine?.Y1 ?? 0.0;
        set
        {
            var def = SelectedCreateLine;
            if (def is null || Math.Abs(def.Y1 - value) < 0.001) return;
            def.Y1 = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public string CreateLine_Point2Ref
    {
        get => SelectedCreateLine?.Point2Ref ?? string.Empty;
        set
        {
            var def = SelectedCreateLine;
            if (def is null) return;
            def.Point2Ref = value ?? string.Empty;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateLine_X2
    {
        get => SelectedCreateLine?.X2 ?? 100.0;
        set
        {
            var def = SelectedCreateLine;
            if (def is null || Math.Abs(def.X2 - value) < 0.001) return;
            def.X2 = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateLine_Y2
    {
        get => SelectedCreateLine?.Y2 ?? 100.0;
        set
        {
            var def = SelectedCreateLine;
            if (def is null || Math.Abs(def.Y2 - value) < 0.001) return;
            def.Y2 = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public string CreateLine_PointRef
    {
        get => SelectedCreateLine?.PointRef ?? string.Empty;
        set
        {
            var def = SelectedCreateLine;
            if (def is null) return;
            def.PointRef = value ?? string.Empty;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateLine_X
    {
        get => SelectedCreateLine?.X ?? 0.0;
        set
        {
            var def = SelectedCreateLine;
            if (def is null || Math.Abs(def.X - value) < 0.001) return;
            def.X = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateLine_Y
    {
        get => SelectedCreateLine?.Y ?? 0.0;
        set
        {
            var def = SelectedCreateLine;
            if (def is null || Math.Abs(def.Y - value) < 0.001) return;
            def.Y = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateLine_Angle
    {
        get => SelectedCreateLine?.Angle ?? 0.0;
        set
        {
            var def = SelectedCreateLine;
            if (def is null || Math.Abs(def.Angle - value) < 0.001) return;
            def.Angle = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateLine_Length
    {
        get => SelectedCreateLine?.Length ?? 200.0;
        set
        {
            var def = SelectedCreateLine;
            if (def is null || Math.Abs(def.Length - value) < 0.001) return;
            def.Length = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    // ==========================================
    // 3. CreateRect
    // ==========================================
    public bool IsCreateRectNode => string.Equals(SelectedNode?.Type, "CreateRect", StringComparison.OrdinalIgnoreCase);

    public CreateRectDefinition? SelectedCreateRect =>
        _config?.CreateRects?.FirstOrDefault(x => string.Equals(x.Name, SelectedNode?.RefName, StringComparison.OrdinalIgnoreCase));

    public string CreateRect_PointRef
    {
        get => SelectedCreateRect?.PointRef ?? string.Empty;
        set
        {
            var def = SelectedCreateRect;
            if (def is null) return;
            def.PointRef = value ?? string.Empty;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateRect_X
    {
        get => SelectedCreateRect?.X ?? 0.0;
        set
        {
            var def = SelectedCreateRect;
            if (def is null || Math.Abs(def.X - value) < 0.001) return;
            def.X = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateRect_Y
    {
        get => SelectedCreateRect?.Y ?? 0.0;
        set
        {
            var def = SelectedCreateRect;
            if (def is null || Math.Abs(def.Y - value) < 0.001) return;
            def.Y = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateRect_Width
    {
        get => SelectedCreateRect?.Width ?? 100.0;
        set
        {
            var def = SelectedCreateRect;
            if (def is null || Math.Abs(def.Width - value) < 0.001) return;
            def.Width = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateRect_Height
    {
        get => SelectedCreateRect?.Height ?? 80.0;
        set
        {
            var def = SelectedCreateRect;
            if (def is null || Math.Abs(def.Height - value) < 0.001) return;
            def.Height = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateRect_Angle
    {
        get => SelectedCreateRect?.Angle ?? 0.0;
        set
        {
            var def = SelectedCreateRect;
            if (def is null || Math.Abs(def.Angle - value) < 0.001) return;
            def.Angle = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public RectAnchorPosition CreateRect_Anchor
    {
        get => SelectedCreateRect?.Anchor ?? RectAnchorPosition.TopLeft;
        set
        {
            var def = SelectedCreateRect;
            if (def is null || def.Anchor == value) return;
            def.Anchor = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    // ==========================================
    // 4. CreateCircle
    // ==========================================
    public bool IsCreateCircleNode => string.Equals(SelectedNode?.Type, "CreateCircle", StringComparison.OrdinalIgnoreCase);

    public CreateCircleDefinition? SelectedCreateCircle =>
        _config?.CreateCircles?.FirstOrDefault(x => string.Equals(x.Name, SelectedNode?.RefName, StringComparison.OrdinalIgnoreCase));

    public CreateCircleMode CreateCircle_Mode
    {
        get => SelectedCreateCircle?.Mode ?? CreateCircleMode.CenterAndRadius;
        set
        {
            var def = SelectedCreateCircle;
            if (def is null || def.Mode == value) return;
            def.Mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CreateCircle_IsCenterAndRadiusMode));
            OnPropertyChanged(nameof(CreateCircle_IsTwoPointsMode));
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public bool CreateCircle_IsCenterAndRadiusMode => CreateCircle_Mode == CreateCircleMode.CenterAndRadius;
    public bool CreateCircle_IsTwoPointsMode => CreateCircle_Mode == CreateCircleMode.TwoPoints;

    public string CreateCircle_CenterPointRef
    {
        get => SelectedCreateCircle?.CenterPointRef ?? string.Empty;
        set
        {
            var def = SelectedCreateCircle;
            if (def is null) return;
            def.CenterPointRef = value ?? string.Empty;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateCircle_CenterX
    {
        get => SelectedCreateCircle?.CenterX ?? 0.0;
        set
        {
            var def = SelectedCreateCircle;
            if (def is null || Math.Abs(def.CenterX - value) < 0.001) return;
            def.CenterX = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateCircle_CenterY
    {
        get => SelectedCreateCircle?.CenterY ?? 0.0;
        set
        {
            var def = SelectedCreateCircle;
            if (def is null || Math.Abs(def.CenterY - value) < 0.001) return;
            def.CenterY = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateCircle_Radius
    {
        get => SelectedCreateCircle?.Radius ?? 50.0;
        set
        {
            var def = SelectedCreateCircle;
            if (def is null || Math.Abs(def.Radius - value) < 0.001) return;
            def.Radius = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public string CreateCircle_BoundaryPointRef
    {
        get => SelectedCreateCircle?.BoundaryPointRef ?? string.Empty;
        set
        {
            var def = SelectedCreateCircle;
            if (def is null) return;
            def.BoundaryPointRef = value ?? string.Empty;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateCircle_BoundaryX
    {
        get => SelectedCreateCircle?.BoundaryX ?? 50.0;
        set
        {
            var def = SelectedCreateCircle;
            if (def is null || Math.Abs(def.BoundaryX - value) < 0.001) return;
            def.BoundaryX = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }

    public double CreateCircle_BoundaryY
    {
        get => SelectedCreateCircle?.BoundaryY ?? 0.0;
        set
        {
            var def = SelectedCreateCircle;
            if (def is null || Math.Abs(def.BoundaryY - value) < 0.001) return;
            def.BoundaryY = value;
            OnPropertyChanged();
            IsDirty = true;
            RefreshPreviews();
        }
    }
}
