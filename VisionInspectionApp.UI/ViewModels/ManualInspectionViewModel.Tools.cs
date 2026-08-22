using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using OpenCvSharp;
using VisionInspectionApp.UI.Controls;
using VisionInspectionApp.UI.Models.ManualInspection;
using VisionInspectionApp.UI.Services.ManualInspection;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class ManualInspectionViewModel
{
    private void OnInteractivePointClicked(System.Windows.Point? p)
    {
        if (p is null || Image is null) return;

        var pt = new GeoPoint2D(p.Value.X, p.Value.Y);

        // Sub-pixel snapping if enabled and image mat is available
        if (EnableSubpixelSnapping && _imageMat is not null && !_imageMat.Empty())
        {
            if (ManualVisionMeasurementService.TryFindSubpixelEdgePoint(_imageMat, pt, 15, out var subpixelPt))
            {
                pt = subpixelPt;
            }
        }

        _collectedPoints.Add(pt);

        int requiredPoints = GetRequiredPointCount(SelectedTool);

        if (_collectedPoints.Count >= requiredPoints)
        {
            CompleteMeasurement();
        }
        else
        {
            RefreshAllOverlays();
            UpdatePromptText();
        }
    }

    private void OnInteractiveMouseMove(System.Windows.Point? currentPos)
    {
        if (_collectedPoints.Count == 0 || currentPos is null || Image is null)
        {
            if (_collectedPoints.Count == 0)
            {
                RefreshAllOverlays();
            }
            return;
        }

        var curPt = new GeoPoint2D(currentPos.Value.X, currentPos.Value.Y);
        var previewOverlays = GenerateRubberbandOverlays(SelectedTool, _collectedPoints, curPt);

        RefreshAllOverlays(previewOverlays);
    }

    private void OnInteractiveCancelled()
    {
        _collectedPoints.Clear();
        RefreshAllOverlays();
        UpdatePromptText();
    }

    private int GetRequiredPointCount(ManualMeasurementType tool) => tool switch
    {
        ManualMeasurementType.PointCoordinates => 1,
        ManualMeasurementType.PointToPointDistance => 2,
        ManualMeasurementType.DeltaXDistance => 2,
        ManualMeasurementType.DeltaYDistance => 2,
        ManualMeasurementType.PointToLineDistance => 3, // P1, P2 (line) + P3 (target pt)
        ManualMeasurementType.LineTwoPoints => 2,
        ManualMeasurementType.LineIntersection => 4, // L1(P1,P2) + L2(P3,P4)
        ManualMeasurementType.LineMidpoint => 2,
        ManualMeasurementType.LineDistance => 4, // L1(P1,P2) + L2(P3,P4)
        ManualMeasurementType.LineAngle => 4, // L1(P1,P2) + L2(P3,P4)
        ManualMeasurementType.CircleThreePoints => 3,
        ManualMeasurementType.CircleCenterRadius => 2,
        ManualMeasurementType.CircleRadiusDiameter => 3,
        ManualMeasurementType.CircleDistance => 6, // C1(3P) + C2(3P)
        ManualMeasurementType.ArcThreePoints => 3,
        ManualMeasurementType.RectangleTwoPoints => 2,
        ManualMeasurementType.RotatedRectangleThreePoints => 3,
        ManualMeasurementType.AngleTwoLines => 4,
        ManualMeasurementType.AngleThreePoints => 3,
        ManualMeasurementType.AngleToAxis => 2,
        ManualMeasurementType.VisionEdgePoint => 1,
        ManualMeasurementType.VisionEdgeDistance => 2,
        _ => 2
    };

    private void CompleteMeasurement()
    {
        var record = CalculateMeasurementRecord(SelectedTool, _collectedPoints);
        if (record is not null)
        {
            Records.Add(record);
            var overlays = GenerateOverlaysForRecord(record);
            _persistentOverlays.AddRange(overlays);
        }

        _collectedPoints.Clear();
        RefreshAllOverlays();
        UpdatePromptText();
    }

    private ManualMeasurementRecord? CalculateMeasurementRecord(ManualMeasurementType tool, List<GeoPoint2D> pts)
    {
        double scale = CalibrationPixelsPerMm > 0 ? CalibrationPixelsPerMm : 1.0;
        int nextId = Records.Count + 1;
        string toolName = ManualMeasurementTypeExtensions.GetDisplayName(tool);

        switch (tool)
        {
            case ManualMeasurementType.PointCoordinates:
            case ManualMeasurementType.VisionEdgePoint:
            {
                var p1 = pts[0];
                return new ManualMeasurementRecord
                {
                    Id = nextId,
                    ToolType = tool,
                    ToolName = toolName,
                    ValuePx = 0,
                    ValueMm = 0,
                    Unit = "px",
                    Details = $"X: {p1.X:0.00}, Y: {p1.Y:0.00} (mm: {p1.X/scale:0.000}, {p1.Y/scale:0.000})",
                    Points = pts.ToList()
                };
            }

            case ManualMeasurementType.PointToPointDistance:
            case ManualMeasurementType.LineTwoPoints:
            case ManualMeasurementType.VisionEdgeDistance:
            {
                var p1 = pts[0];
                var p2 = pts[1];
                double distPx = GeoPoint2D.Distance(p1, p2);
                double distMm = Math.Round(distPx / scale, 4);
                return new ManualMeasurementRecord
                {
                    Id = nextId,
                    ToolType = tool,
                    ToolName = toolName,
                    ValuePx = Math.Round(distPx, 2),
                    ValueMm = distMm,
                    Unit = "mm",
                    Details = $"P1({p1.X:0.0},{p1.Y:0.0}) -> P2({p2.X:0.0},{p2.Y:0.0})",
                    Points = pts.ToList()
                };
            }

            case ManualMeasurementType.DeltaXDistance:
            {
                var p1 = pts[0];
                var p2 = pts[1];
                double dxPx = Math.Abs(p2.X - p1.X);
                double dxMm = Math.Round(dxPx / scale, 4);
                return new ManualMeasurementRecord
                {
                    Id = nextId,
                    ToolType = tool,
                    ToolName = toolName,
                    ValuePx = Math.Round(dxPx, 2),
                    ValueMm = dxMm,
                    Unit = "mm",
                    Details = $"ΔX: |{p2.X:0.0} - {p1.X:0.0}|",
                    Points = pts.ToList()
                };
            }

            case ManualMeasurementType.DeltaYDistance:
            {
                var p1 = pts[0];
                var p2 = pts[1];
                double dyPx = Math.Abs(p2.Y - p1.Y);
                double dyMm = Math.Round(dyPx / scale, 4);
                return new ManualMeasurementRecord
                {
                    Id = nextId,
                    ToolType = tool,
                    ToolName = toolName,
                    ValuePx = Math.Round(dyPx, 2),
                    ValueMm = dyMm,
                    Unit = "mm",
                    Details = $"ΔY: |{p2.Y:0.0} - {p1.Y:0.0}|",
                    Points = pts.ToList()
                };
            }

            case ManualMeasurementType.PointToLineDistance:
            {
                var p1 = pts[0];
                var p2 = pts[1];
                var pTarget = pts[2];
                var line = new GeoLine2D(p1, p2);
                double distPx = line.DistanceToPoint(pTarget);
                double distMm = Math.Round(distPx / scale, 4);
                return new ManualMeasurementRecord
                {
                    Id = nextId,
                    ToolType = tool,
                    ToolName = toolName,
                    ValuePx = Math.Round(distPx, 2),
                    ValueMm = distMm,
                    Unit = "mm",
                    Details = $"Khoảng cách từ P({pTarget.X:0.0},{pTarget.Y:0.0}) tới Line",
                    Points = pts.ToList()
                };
            }

            case ManualMeasurementType.LineMidpoint:
            {
                var p1 = pts[0];
                var p2 = pts[1];
                var mid = new GeoPoint2D((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0);
                double lengthPx = GeoPoint2D.Distance(p1, p2);
                return new ManualMeasurementRecord
                {
                    Id = nextId,
                    ToolType = tool,
                    ToolName = toolName,
                    ValuePx = Math.Round(lengthPx, 2),
                    ValueMm = Math.Round(lengthPx / scale, 4),
                    Unit = "mm",
                    Details = $"Midpoint: ({mid.X:0.00}, {mid.Y:0.00})",
                    Points = pts.ToList()
                };
            }

            case ManualMeasurementType.LineDistance:
            {
                var l1 = new GeoLine2D(pts[0], pts[1]);
                var l2 = new GeoLine2D(pts[2], pts[3]);
                double distPx = ManualVisionMeasurementService.CalculateLineToLineDistance(l1, l2);
                double distMm = Math.Round(distPx / scale, 4);
                return new ManualMeasurementRecord
                {
                    Id = nextId,
                    ToolType = tool,
                    ToolName = toolName,
                    ValuePx = Math.Round(distPx, 2),
                    ValueMm = distMm,
                    Unit = "mm",
                    Details = $"Khoảng cách 2 đường thẳng",
                    Points = pts.ToList()
                };
            }

            case ManualMeasurementType.LineAngle:
            case ManualMeasurementType.AngleTwoLines:
            {
                var l1 = new GeoLine2D(pts[0], pts[1]);
                var l2 = new GeoLine2D(pts[2], pts[3]);
                double angleDeg = ManualVisionMeasurementService.CalculateAngleBetweenLines(l1, l2);
                return new ManualMeasurementRecord
                {
                    Id = nextId,
                    ToolType = tool,
                    ToolName = toolName,
                    ValuePx = Math.Round(angleDeg, 2),
                    ValueMm = Math.Round(angleDeg, 2),
                    Unit = "°",
                    Details = $"Góc giữa 2 đoạn thẳng: {angleDeg:0.00}°",
                    Points = pts.ToList()
                };
            }

            case ManualMeasurementType.LineIntersection:
            {
                var l1 = new GeoLine2D(pts[0], pts[1]);
                var l2 = new GeoLine2D(pts[2], pts[3]);
                if (ManualVisionMeasurementService.TryFindIntersection(l1, l2, out var inter))
                {
                    return new ManualMeasurementRecord
                    {
                        Id = nextId,
                        ToolType = tool,
                        ToolName = toolName,
                        ValuePx = 0,
                        ValueMm = 0,
                        Unit = "px",
                        Details = $"Giao điểm: ({inter.X:0.00}, {inter.Y:0.00})",
                        Points = pts.ToList()
                    };
                }
                return null;
            }

            case ManualMeasurementType.CircleThreePoints:
            case ManualMeasurementType.CircleRadiusDiameter:
            {
                if (ManualVisionMeasurementService.TryFitCircle3Points(pts[0], pts[1], pts[2], out var circle))
                {
                    double rMm = Math.Round(circle.Radius / scale, 4);
                    double dMm = Math.Round(circle.Diameter / scale, 4);
                    return new ManualMeasurementRecord
                    {
                        Id = nextId,
                        ToolType = tool,
                        ToolName = toolName,
                        ValuePx = Math.Round(circle.Diameter, 2),
                        ValueMm = dMm,
                        Unit = "mm",
                        Details = $"Center: ({circle.Center.X:0.0},{circle.Center.Y:0.0}), R={rMm:0.000}mm, Ø={dMm:0.000}mm",
                        Points = pts.ToList()
                    };
                }
                return null;
            }

            case ManualMeasurementType.CircleCenterRadius:
            {
                var center = pts[0];
                double rPx = GeoPoint2D.Distance(center, pts[1]);
                double rMm = Math.Round(rPx / scale, 4);
                double dMm = Math.Round((rPx * 2.0) / scale, 4);
                return new ManualMeasurementRecord
                {
                    Id = nextId,
                    ToolType = tool,
                    ToolName = toolName,
                    ValuePx = Math.Round(rPx * 2.0, 2),
                    ValueMm = dMm,
                    Unit = "mm",
                    Details = $"Center: ({center.X:0.0},{center.Y:0.0}), R={rMm:0.000}mm, Ø={dMm:0.000}mm",
                    Points = pts.ToList()
                };
            }

            case ManualMeasurementType.ArcThreePoints:
            {
                if (ManualVisionMeasurementService.TryFitCircle3Points(pts[0], pts[1], pts[2], out var circle))
                {
                    double rMm = Math.Round(circle.Radius / scale, 4);
                    return new ManualMeasurementRecord
                    {
                        Id = nextId,
                        ToolType = tool,
                        ToolName = toolName,
                        ValuePx = Math.Round(circle.Radius, 2),
                        ValueMm = rMm,
                        Unit = "mm",
                        Details = $"Bán kính Cung tròn R = {rMm:0.000}mm",
                        Points = pts.ToList()
                    };
                }
                return null;
            }

            case ManualMeasurementType.RectangleTwoPoints:
            {
                var p1 = pts[0];
                var p2 = pts[1];
                var rect = GeoRectangle2D.FromTwoPoints(p1, p2);
                double wMm = Math.Round(rect.Width / scale, 4);
                double hMm = Math.Round(rect.Height / scale, 4);
                double areaMm2 = Math.Round((rect.Width * rect.Height) / (scale * scale), 4);
                return new ManualMeasurementRecord
                {
                    Id = nextId,
                    ToolType = tool,
                    ToolName = toolName,
                    ValuePx = Math.Round(rect.Width, 2),
                    ValueMm = wMm,
                    Unit = "mm",
                    Details = $"W={wMm:0.000}mm, H={hMm:0.000}mm, Area={areaMm2:0.00}mm²",
                    Points = pts.ToList()
                };
            }

            case ManualMeasurementType.RotatedRectangleThreePoints:
            {
                if (ManualVisionMeasurementService.TryFitRotatedRect3Points(pts[0], pts[1], pts[2], out var rRect))
                {
                    double wMm = Math.Round(rRect.Width / scale, 4);
                    double hMm = Math.Round(rRect.Height / scale, 4);
                    return new ManualMeasurementRecord
                    {
                        Id = nextId,
                        ToolType = tool,
                        ToolName = toolName,
                        ValuePx = Math.Round(rRect.Width, 2),
                        ValueMm = wMm,
                        Unit = "mm",
                        Details = $"W={wMm:0.000}mm, H={hMm:0.000}mm, Angle={rRect.AngleDeg:0.0}°",
                        Points = pts.ToList()
                    };
                }
                return null;
            }

            case ManualMeasurementType.AngleThreePoints:
            {
                double angleDeg = ManualVisionMeasurementService.CalculateAngle3Points(pts[0], pts[1], pts[2]);
                return new ManualMeasurementRecord
                {
                    Id = nextId,
                    ToolType = tool,
                    ToolName = toolName,
                    ValuePx = Math.Round(angleDeg, 2),
                    ValueMm = Math.Round(angleDeg, 2),
                    Unit = "°",
                    Details = $"Đỉnh P2({pts[1].X:0.0},{pts[1].Y:0.0}) -> Góc: {angleDeg:0.00}°",
                    Points = pts.ToList()
                };
            }

            case ManualMeasurementType.AngleToAxis:
            {
                var line = new GeoLine2D(pts[0], pts[1]);
                double angleDeg = line.AngleToHorizontalDeg();
                return new ManualMeasurementRecord
                {
                    Id = nextId,
                    ToolType = tool,
                    ToolName = toolName,
                    ValuePx = Math.Round(angleDeg, 2),
                    ValueMm = Math.Round(angleDeg, 2),
                    Unit = "°",
                    Details = $"Góc nghiêng trục ngang: {angleDeg:0.00}°",
                    Points = pts.ToList()
                };
            }

            default:
                return null;
        }
    }

    private List<OverlayItem> GenerateRubberbandOverlays(ManualMeasurementType tool, List<GeoPoint2D> collected, GeoPoint2D curPt)
    {
        var list = new List<OverlayItem>();
        double scale = CalibrationPixelsPerMm > 0 ? CalibrationPixelsPerMm : 1.0;

        foreach (var pt in collected)
        {
            list.Add(new OverlayPointItem
            {
                X = pt.X,
                Y = pt.Y,
                Stroke = Brushes.DeepSkyBlue,
                Label = $"({pt.X:0.0},{pt.Y:0.0})"
            });
        }

        // Live cursor position indicator
        list.Add(new OverlayPointItem
        {
            X = curPt.X,
            Y = curPt.Y,
            Stroke = Brushes.Yellow,
            Label = $"({curPt.X:0.0},{curPt.Y:0.0})"
        });

        if (collected.Count == 1)
        {
            var p1 = collected[0];
            double distPx = GeoPoint2D.Distance(p1, curPt);
            double distMm = distPx / scale;

            if (tool == ManualMeasurementType.CircleCenterRadius)
            {
                list.Add(new OverlayCircleItem
                {
                    CenterX = p1.X,
                    CenterY = p1.Y,
                    Radius = distPx,
                    Stroke = Brushes.Yellow,
                    Label = $"R: {distMm:0.000} mm (Ø {distMm * 2:0.000} mm)"
                });
            }
            else if (tool == ManualMeasurementType.RectangleTwoPoints)
            {
                var rect = GeoRectangle2D.FromTwoPoints(p1, curPt);
                list.Add(new OverlayRectItem
                {
                    X = (int)rect.X,
                    Y = (int)rect.Y,
                    Width = (int)rect.Width,
                    Height = (int)rect.Height,
                    Stroke = Brushes.Yellow,
                    Label = $"{rect.Width / scale:0.000} x {rect.Height / scale:0.000} mm"
                });
            }
            else
            {
                list.Add(new OverlayLineItem
                {
                    X1 = p1.X,
                    Y1 = p1.Y,
                    X2 = curPt.X,
                    Y2 = curPt.Y,
                    Stroke = Brushes.Yellow,
                    Label = $"{distPx:0.0} px / {distMm:0.000} mm"
                });
            }
        }
        else if (collected.Count == 2)
        {
            var p1 = collected[0];
            var p2 = collected[1];

            if (tool == ManualMeasurementType.CircleThreePoints || tool == ManualMeasurementType.CircleRadiusDiameter || tool == ManualMeasurementType.ArcThreePoints)
            {
                if (ManualVisionMeasurementService.TryFitCircle3Points(p1, p2, curPt, out var circle))
                {
                    list.Add(new OverlayCircleItem
                    {
                        CenterX = circle.Center.X,
                        CenterY = circle.Center.Y,
                        Radius = circle.Radius,
                        Stroke = Brushes.Yellow,
                        Label = $"Ø {circle.Diameter / scale:0.000} mm (R {circle.Radius / scale:0.000} mm)"
                    });
                }
            }
            else if (tool == ManualMeasurementType.RotatedRectangleThreePoints)
            {
                if (ManualVisionMeasurementService.TryFitRotatedRect3Points(p1, p2, curPt, out var rRect))
                {
                    var corners = rRect.GetCorners();
                    list.Add(new OverlayPolylineItem
                    {
                        Points = corners.Select(c => new System.Windows.Point(c.X, c.Y)).ToList(),
                        IsClosed = true,
                        Stroke = Brushes.Yellow,
                        Label = $"{rRect.Width / scale:0.000} x {rRect.Height / scale:0.000} mm ({rRect.AngleDeg:0.0}°)"
                    });
                }
            }
            else if (tool == ManualMeasurementType.AngleThreePoints)
            {
                double angleDeg = ManualVisionMeasurementService.CalculateAngle3Points(p1, p2, curPt);
                list.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Brushes.Cyan });
                list.Add(new OverlayLineItem { X1 = p2.X, Y1 = p2.Y, X2 = curPt.X, Y2 = curPt.Y, Stroke = Brushes.Yellow, Label = $"Góc: {angleDeg:0.00}°" });
            }
            else if (tool == ManualMeasurementType.PointToLineDistance)
            {
                var line = new GeoLine2D(p1, p2);
                double distPx = line.DistanceToPoint(curPt);
                list.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Brushes.Cyan, Label = "Base Line" });
                list.Add(new OverlayPointItem { X = curPt.X, Y = curPt.Y, Stroke = Brushes.Yellow, Label = $"Dist: {distPx / scale:0.000} mm" });
            }
            else
            {
                list.Add(new OverlayLineItem { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = Brushes.DeepSkyBlue });
                list.Add(new OverlayLineItem { X1 = p2.X, Y1 = p2.Y, X2 = curPt.X, Y2 = curPt.Y, Stroke = Brushes.Yellow });
            }
        }
        else if (collected.Count == 3)
        {
            list.Add(new OverlayLineItem { X1 = collected[0].X, Y1 = collected[0].Y, X2 = collected[1].X, Y2 = collected[1].Y, Stroke = Brushes.DeepSkyBlue, Label = "Line 1" });
            list.Add(new OverlayLineItem { X1 = collected[2].X, Y1 = collected[2].Y, X2 = curPt.X, Y2 = curPt.Y, Stroke = Brushes.Yellow, Label = "Line 2" });
        }

        return list;
    }

    private List<OverlayItem> GenerateOverlaysForRecord(ManualMeasurementRecord record)
    {
        var list = new List<OverlayItem>();
        var pts = record.Points;
        if (pts.Count == 0) return list;

        var stroke = record.Status == ToleranceStatus.NG ? Brushes.Red : Brushes.LimeGreen;

        foreach (var pt in pts)
        {
            list.Add(new OverlayPointItem
            {
                X = pt.X,
                Y = pt.Y,
                Stroke = Brushes.Cyan,
                Label = $"P({pt.X:0.0},{pt.Y:0.0})"
            });
        }

        if (pts.Count == 2)
        {
            if (record.ToolType == ManualMeasurementType.CircleCenterRadius)
            {
                double rPx = GeoPoint2D.Distance(pts[0], pts[1]);
                list.Add(new OverlayCircleItem
                {
                    CenterX = pts[0].X,
                    CenterY = pts[0].Y,
                    Radius = rPx,
                    Stroke = stroke,
                    Label = $"#{record.Id} Ø {record.ValueMm:0.000} mm"
                });
            }
            else if (record.ToolType == ManualMeasurementType.RectangleTwoPoints)
            {
                var rect = GeoRectangle2D.FromTwoPoints(pts[0], pts[1]);
                list.Add(new OverlayRectItem
                {
                    X = (int)rect.X,
                    Y = (int)rect.Y,
                    Width = (int)rect.Width,
                    Height = (int)rect.Height,
                    Stroke = stroke,
                    Label = $"#{record.Id} [{record.ValueMm:0.000} mm]"
                });
            }
            else
            {
                list.Add(new OverlayLineItem
                {
                    X1 = pts[0].X,
                    Y1 = pts[0].Y,
                    X2 = pts[1].X,
                    Y2 = pts[1].Y,
                    Stroke = stroke,
                    Label = $"#{record.Id}: {record.ValueMm:0.000} {record.Unit}"
                });
            }
        }
        else if (pts.Count == 3)
        {
            if (record.ToolType == ManualMeasurementType.CircleThreePoints || record.ToolType == ManualMeasurementType.CircleRadiusDiameter || record.ToolType == ManualMeasurementType.ArcThreePoints)
            {
                if (ManualVisionMeasurementService.TryFitCircle3Points(pts[0], pts[1], pts[2], out var circle))
                {
                    list.Add(new OverlayCircleItem
                    {
                        CenterX = circle.Center.X,
                        CenterY = circle.Center.Y,
                        Radius = circle.Radius,
                        Stroke = stroke,
                        Label = $"#{record.Id} Ø {record.ValueMm:0.000} mm"
                    });
                }
            }
            else if (record.ToolType == ManualMeasurementType.RotatedRectangleThreePoints)
            {
                if (ManualVisionMeasurementService.TryFitRotatedRect3Points(pts[0], pts[1], pts[2], out var rRect))
                {
                    var corners = rRect.GetCorners();
                    list.Add(new OverlayPolylineItem
                    {
                        Points = corners.Select(c => new System.Windows.Point(c.X, c.Y)).ToList(),
                        IsClosed = true,
                        Stroke = stroke,
                        Label = $"#{record.Id} [{record.ValueMm:0.000} mm]"
                    });
                }
            }
            else if (record.ToolType == ManualMeasurementType.AngleThreePoints)
            {
                list.Add(new OverlayLineItem { X1 = pts[0].X, Y1 = pts[0].Y, X2 = pts[1].X, Y2 = pts[1].Y, Stroke = stroke });
                list.Add(new OverlayLineItem { X1 = pts[1].X, Y1 = pts[1].Y, X2 = pts[2].X, Y2 = pts[2].Y, Stroke = stroke, Label = $"#{record.Id}: {record.ValueMm:0.00}°" });
            }
            else if (record.ToolType == ManualMeasurementType.PointToLineDistance)
            {
                list.Add(new OverlayLineItem { X1 = pts[0].X, Y1 = pts[0].Y, X2 = pts[1].X, Y2 = pts[1].Y, Stroke = Brushes.Cyan });
                list.Add(new OverlayPointItem { X = pts[2].X, Y = pts[2].Y, Stroke = stroke, Label = $"#{record.Id}: {record.ValueMm:0.000} mm" });
            }
        }
        else if (pts.Count == 4)
        {
            list.Add(new OverlayLineItem { X1 = pts[0].X, Y1 = pts[0].Y, X2 = pts[1].X, Y2 = pts[1].Y, Stroke = stroke, Label = $"#{record.Id} Line1" });
            list.Add(new OverlayLineItem { X1 = pts[2].X, Y1 = pts[2].Y, X2 = pts[3].X, Y2 = pts[3].Y, Stroke = stroke, Label = $"#{record.Id} Line2 ({record.ValueMm:0.000} {record.Unit})" });
        }

        return list;
    }

    private void RefreshAllOverlays(List<OverlayItem>? temporaryOverlays = null)
    {
        OverlayItems.Clear();

        foreach (var item in _persistentOverlays)
        {
            OverlayItems.Add(item);
        }

        if (temporaryOverlays is not null)
        {
            foreach (var item in temporaryOverlays)
            {
                OverlayItems.Add(item);
            }
        }
    }

    private void UpdatePromptText()
    {
        string toolName = ManualMeasurementTypeExtensions.GetDisplayName(SelectedTool);
        int total = GetRequiredPointCount(SelectedTool);
        int current = _collectedPoints.Count;

        if (Image is null)
        {
            StatusPrompt = "Chưa có ảnh. Vui lòng bấm [Mở ảnh file] hoặc [Chụp từ Camera] để bắt đầu đo.";
            return;
        }

        if (current == 0)
        {
            StatusPrompt = $"[{toolName}] - Click chuột trái vào ảnh để chọn điểm số 1/{total} (Nhấp phải/ESC để hủy).";
        }
        else
        {
            StatusPrompt = $"[{toolName}] - Đã chọn {current}/{total} điểm. Di chuyển chuột để xem trước kích thước & Click chọn điểm số {current + 1}/{total}.";
        }
    }
}
