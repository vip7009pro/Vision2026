using System;
using System.Collections.Generic;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

public static class GeometryCreationProcessor
{
    /// <summary>
    /// Calculate top-left position of a rectangle based on point, dimensions, and anchor.
    /// </summary>
    public static (double TopLeftX, double TopLeftY) CalculateRectTopLeft(double pointX, double pointY, double width, double height, RectAnchorPosition anchor)
    {
        return anchor switch
        {
            RectAnchorPosition.TopLeft => (pointX, pointY),
            RectAnchorPosition.TopCenter => (pointX - width / 2.0, pointY),
            RectAnchorPosition.TopRight => (pointX - width, pointY),
            RectAnchorPosition.MiddleLeft => (pointX, pointY - height / 2.0),
            RectAnchorPosition.MiddleCenter => (pointX - width / 2.0, pointY - height / 2.0),
            RectAnchorPosition.MiddleRight => (pointX - width, pointY - height / 2.0),
            RectAnchorPosition.BottomLeft => (pointX, pointY - height),
            RectAnchorPosition.BottomCenter => (pointX - width / 2.0, pointY - height),
            RectAnchorPosition.BottomRight => (pointX - width, pointY - height),
            _ => (pointX, pointY)
        };
    }
    /// <summary>
    /// Calculate anchor position (X, Y) from top-left position, dimensions, and anchor.
    /// </summary>
    public static (double AnchorX, double AnchorY) CalculateAnchorFromTopLeft(double tlX, double tlY, double width, double height, RectAnchorPosition anchor)
    {
        return anchor switch
        {
            RectAnchorPosition.TopLeft => (tlX, tlY),
            RectAnchorPosition.TopCenter => (tlX + width / 2.0, tlY),
            RectAnchorPosition.TopRight => (tlX + width, tlY),
            RectAnchorPosition.MiddleLeft => (tlX, tlY + height / 2.0),
            RectAnchorPosition.MiddleCenter => (tlX + width / 2.0, tlY + height / 2.0),
            RectAnchorPosition.MiddleRight => (tlX + width, tlY + height / 2.0),
            RectAnchorPosition.BottomLeft => (tlX, tlY + height),
            RectAnchorPosition.BottomCenter => (tlX + width / 2.0, tlY + height),
            RectAnchorPosition.BottomRight => (tlX + width, tlY + height),
            _ => (tlX, tlY)
        };
    }


    /// <summary>
    /// Process CreatePoint tool.
    /// </summary>
    public static CreatePointResult EvaluateCreatePoint(CreatePointDefinition def, Dictionary<string, Point2d> pointMap)
    {
        double x = def.X;
        double y = def.Y;

        if (!string.IsNullOrWhiteSpace(def.PointRef) && pointMap.TryGetValue(def.PointRef, out var pt))
        {
            x = pt.X;
            y = pt.Y;
        }

        return new CreatePointResult(def.Name, true, x, y);
    }

    /// <summary>
    /// Process CreateLine tool.
    /// </summary>
    public static CreateLineResult EvaluateCreateLine(CreateLineDefinition def, Dictionary<string, Point2d> pointMap)
    {
        if (def.Mode == CreateLineMode.TwoPoints)
        {
            double x1 = def.X1;
            double y1 = def.Y1;
            double x2 = def.X2;
            double y2 = def.Y2;

            if (!string.IsNullOrWhiteSpace(def.Point1Ref) && pointMap.TryGetValue(def.Point1Ref, out var pt1))
            {
                x1 = pt1.X;
                y1 = pt1.Y;
            }

            if (!string.IsNullOrWhiteSpace(def.Point2Ref) && pointMap.TryGetValue(def.Point2Ref, out var pt2))
            {
                x2 = pt2.X;
                y2 = pt2.Y;
            }

            double dx = x2 - x1;
            double dy = y2 - y1;
            double angleRad = Math.Atan2(dy, dx);
            double angleDeg = angleRad * (180.0 / Math.PI);
            double length = Math.Sqrt(dx * dx + dy * dy);

            return new CreateLineResult(def.Name, true, x1, y1, x2, y2, angleDeg, length);
        }
        else // PointAndAngle mode
        {
            double x = def.X;
            double y = def.Y;

            if (!string.IsNullOrWhiteSpace(def.PointRef) && pointMap.TryGetValue(def.PointRef, out var pt))
            {
                x = pt.X;
                y = pt.Y;
            }

            double length = def.Length > 0 ? def.Length : 200.0;
            double angleRad = def.Angle * (Math.PI / 180.0);

            // Compute end points centered or forward along angle
            double halfLen = length / 2.0;
            double x1 = x - halfLen * Math.Cos(angleRad);
            double y1 = y - halfLen * Math.Sin(angleRad);
            double x2 = x + halfLen * Math.Cos(angleRad);
            double y2 = y + halfLen * Math.Sin(angleRad);

            return new CreateLineResult(def.Name, true, x1, y1, x2, y2, def.Angle, length);
        }
    }

    /// <summary>
    /// Process CreateRect tool.
    /// </summary>
    public static CreateRectResult EvaluateCreateRect(CreateRectDefinition def, Dictionary<string, Point2d> pointMap)
    {
        double x = def.X;
        double y = def.Y;

        if (!string.IsNullOrWhiteSpace(def.PointRef) && pointMap.TryGetValue(def.PointRef, out var pt))
        {
            x = pt.X;
            y = pt.Y;
        }

        var (tlX, tlY) = CalculateRectTopLeft(x, y, def.Width, def.Height, def.Anchor);

        return new CreateRectResult(def.Name, true, x, y, def.Width, def.Height, def.Angle, def.Anchor, tlX, tlY);
    }

    /// <summary>
    /// Process CreateCircle tool.
    /// </summary>
    public static CreateCircleResult EvaluateCreateCircle(CreateCircleDefinition def, Dictionary<string, Point2d> pointMap)
    {
        double cx = def.CenterX;
        double cy = def.CenterY;

        if (!string.IsNullOrWhiteSpace(def.CenterPointRef) && pointMap.TryGetValue(def.CenterPointRef, out var centerPt))
        {
            cx = centerPt.X;
            cy = centerPt.Y;
        }

        double radius = def.Radius;

        if (def.Mode == CreateCircleMode.TwoPoints)
        {
            double bx = def.BoundaryX;
            double by = def.BoundaryY;

            if (!string.IsNullOrWhiteSpace(def.BoundaryPointRef) && pointMap.TryGetValue(def.BoundaryPointRef, out var boundaryPt))
            {
                bx = boundaryPt.X;
                by = boundaryPt.Y;
            }

            double dx = bx - cx;
            double dy = by - cy;
            radius = Math.Sqrt(dx * dx + dy * dy);
        }

        return new CreateCircleResult(def.Name, true, cx, cy, radius);
    }
}
