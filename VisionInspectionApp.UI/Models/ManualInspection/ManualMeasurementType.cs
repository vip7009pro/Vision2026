using System.ComponentModel;

namespace VisionInspectionApp.UI.Models.ManualInspection;

public enum ManualMeasurementGroup
{
    [Description("Điểm & Khoảng cách")]
    PointAndDistance,

    [Description("Đoạn thẳng & Line")]
    LineAndSegments,

    [Description("Đường tròn & Cung")]
    CircleAndArc,

    [Description("Hình học & Diện tích")]
    ShapesAndArea,

    [Description("Góc lượn & Khúc xạ")]
    Angles,

    [Description("Vision Edge Detection")]
    VisionEdge
}

public enum ManualMeasurementType
{
    // Nhóm 1: Điểm & Khoảng cách
    [Description("Tọa độ điểm (XY)")]
    PointCoordinates,

    [Description("Khoảng cách 2 điểm (2P)")]
    PointToPointDistance,

    [Description("Độ lệch ΔX")]
    DeltaXDistance,

    [Description("Độ lệch ΔY")]
    DeltaYDistance,

    [Description("Khoảng cách Điểm - Đường (3P)")]
    PointToLineDistance,

    // Nhóm 2: Đoạn thẳng & Line
    [Description("Đoạn thẳng 2 điểm")]
    LineTwoPoints,

    [Description("Trung điểm (Midpoint)")]
    LineMidpoint,

    [Description("Khoảng cách 2 đường (4P)")]
    LineDistance,

    [Description("Giao điểm 2 đường (4P)")]
    LineIntersection,

    [Description("Góc 2 đường (4P)")]
    LineAngle,

    // Nhóm 3: Đường tròn & Cung
    [Description("Đường tròn qua 3 điểm (3P)")]
    CircleThreePoints,

    [Description("Tâm & Bán kính (2P)")]
    CircleCenterRadius,

    [Description("Đo Bán kính / Đường kính")]
    CircleRadiusDiameter,

    [Description("Cung tròn (Arc 3P)")]
    ArcThreePoints,

    [Description("Khoảng cách 2 Đường tròn (6P)")]
    CircleDistance,

    // Nhóm 4: Hình học & Diện tích
    [Description("Hình chữ nhật thẳng (2P)")]
    RectangleTwoPoints,

    [Description("Hình chữ nhật xoay (3P)")]
    RotatedRectangleThreePoints,

    // Nhóm 5: Góc
    [Description("Góc tạo bởi 3 điểm (Đỉnh P2)")]
    AngleThreePoints,

    [Description("Góc giữa 2 đường (4P)")]
    AngleTwoLines,

    [Description("Góc nghiêng trục ngang (2P)")]
    AngleToAxis,

    // Nhóm 6: Vision Edge Detection
    [Description("🎯 Dò mép Sub-pixel (1P)")]
    VisionEdgePoint,

    [Description("🎯 Đo khoảng cách 2 Mép Sub-pixel (2P)")]
    VisionEdgeDistance
}

public static class ManualMeasurementTypeExtensions
{
    public static ManualMeasurementGroup GetGroup(ManualMeasurementType tool) => tool switch
    {
        ManualMeasurementType.PointCoordinates or
        ManualMeasurementType.PointToPointDistance or
        ManualMeasurementType.DeltaXDistance or
        ManualMeasurementType.DeltaYDistance or
        ManualMeasurementType.PointToLineDistance => ManualMeasurementGroup.PointAndDistance,

        ManualMeasurementType.LineTwoPoints or
        ManualMeasurementType.LineMidpoint or
        ManualMeasurementType.LineDistance or
        ManualMeasurementType.LineIntersection or
        ManualMeasurementType.LineAngle => ManualMeasurementGroup.LineAndSegments,

        ManualMeasurementType.CircleThreePoints or
        ManualMeasurementType.CircleCenterRadius or
        ManualMeasurementType.CircleRadiusDiameter or
        ManualMeasurementType.ArcThreePoints or
        ManualMeasurementType.CircleDistance => ManualMeasurementGroup.CircleAndArc,

        ManualMeasurementType.RectangleTwoPoints or
        ManualMeasurementType.RotatedRectangleThreePoints => ManualMeasurementGroup.ShapesAndArea,

        ManualMeasurementType.AngleThreePoints or
        ManualMeasurementType.AngleTwoLines or
        ManualMeasurementType.AngleToAxis => ManualMeasurementGroup.Angles,

        ManualMeasurementType.VisionEdgePoint or
        ManualMeasurementType.VisionEdgeDistance => ManualMeasurementGroup.VisionEdge,

        _ => ManualMeasurementGroup.PointAndDistance
    };

    public static string GetDisplayName(ManualMeasurementType tool) => tool switch
    {
        ManualMeasurementType.PointCoordinates => "Tọa độ điểm (XY)",
        ManualMeasurementType.PointToPointDistance => "Khoảng cách 2 điểm (2P)",
        ManualMeasurementType.DeltaXDistance => "Độ lệch ΔX",
        ManualMeasurementType.DeltaYDistance => "Độ lệch ΔY",
        ManualMeasurementType.PointToLineDistance => "Khoảng cách Điểm - Đường (3P)",

        ManualMeasurementType.LineTwoPoints => "Đoạn thẳng 2 điểm",
        ManualMeasurementType.LineMidpoint => "Trung điểm (Midpoint)",
        ManualMeasurementType.LineDistance => "Khoảng cách 2 đường (4P)",
        ManualMeasurementType.LineIntersection => "Giao điểm 2 đường (4P)",
        ManualMeasurementType.LineAngle => "Góc 2 đường (4P)",

        ManualMeasurementType.CircleThreePoints => "Đường tròn qua 3 điểm (3P)",
        ManualMeasurementType.CircleCenterRadius => "Tâm & Bán kính (2P)",
        ManualMeasurementType.CircleRadiusDiameter => "Đo Bán kính / Đường kính",
        ManualMeasurementType.ArcThreePoints => "Cung tròn (Arc 3P)",
        ManualMeasurementType.CircleDistance => "Khoảng cách 2 Đường tròn (6P)",

        ManualMeasurementType.RectangleTwoPoints => "Hình chữ nhật thẳng (2P)",
        ManualMeasurementType.RotatedRectangleThreePoints => "Hình chữ nhật xoay (3P)",

        ManualMeasurementType.AngleThreePoints => "Góc tạo bởi 3 điểm",
        ManualMeasurementType.AngleTwoLines => "Góc giữa 2 đường (4P)",
        ManualMeasurementType.AngleToAxis => "Góc nghiêng trục ngang",

        ManualMeasurementType.VisionEdgePoint => "🎯 Dò mép Sub-pixel (1P)",
        ManualMeasurementType.VisionEdgeDistance => "🎯 Đo khoảng cách 2 Mép Sub-pixel (2P)",

        _ => tool.ToString()
    };

    public static List<ManualMeasurementType> GetToolsInGroup(ManualMeasurementGroup group) => group switch
    {
        ManualMeasurementGroup.PointAndDistance => new()
        {
            ManualMeasurementType.PointCoordinates,
            ManualMeasurementType.PointToPointDistance,
            ManualMeasurementType.DeltaXDistance,
            ManualMeasurementType.DeltaYDistance,
            ManualMeasurementType.PointToLineDistance
        },
        ManualMeasurementGroup.LineAndSegments => new()
        {
            ManualMeasurementType.LineTwoPoints,
            ManualMeasurementType.LineMidpoint,
            ManualMeasurementType.LineDistance,
            ManualMeasurementType.LineIntersection,
            ManualMeasurementType.LineAngle
        },
        ManualMeasurementGroup.CircleAndArc => new()
        {
            ManualMeasurementType.CircleThreePoints,
            ManualMeasurementType.CircleCenterRadius,
            ManualMeasurementType.CircleRadiusDiameter,
            ManualMeasurementType.ArcThreePoints,
            ManualMeasurementType.CircleDistance
        },
        ManualMeasurementGroup.ShapesAndArea => new()
        {
            ManualMeasurementType.RectangleTwoPoints,
            ManualMeasurementType.RotatedRectangleThreePoints
        },
        ManualMeasurementGroup.Angles => new()
        {
            ManualMeasurementType.AngleThreePoints,
            ManualMeasurementType.AngleTwoLines,
            ManualMeasurementType.AngleToAxis
        },
        ManualMeasurementGroup.VisionEdge => new()
        {
            ManualMeasurementType.VisionEdgePoint,
            ManualMeasurementType.VisionEdgeDistance
        },
        _ => new()
    };
}
