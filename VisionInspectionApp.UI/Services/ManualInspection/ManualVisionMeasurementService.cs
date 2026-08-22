using System;
using System.Collections.Generic;
using OpenCvSharp;
using VisionInspectionApp.UI.Models.ManualInspection;

namespace VisionInspectionApp.UI.Services.ManualInspection;

public static class ManualVisionMeasurementService
{
    public static bool TryFitCircle3Points(GeoPoint2D p1, GeoPoint2D p2, GeoPoint2D p3, out GeoCircle2D circle)
    {
        circle = default;
        double d = 2.0 * (p1.X * (p2.Y - p3.Y) + p2.X * (p3.Y - p1.Y) + p3.X * (p1.Y - p2.Y));
        if (Math.Abs(d) < 1e-7)
        {
            return false;
        }

        double p1Sq = p1.X * p1.X + p1.Y * p1.Y;
        double p2Sq = p2.X * p2.X + p2.Y * p2.Y;
        double p3Sq = p3.X * p3.X + p3.Y * p3.Y;

        double cx = (p1Sq * (p2.Y - p3.Y) + p2Sq * (p3.Y - p1.Y) + p3Sq * (p1.Y - p2.Y)) / d;
        double cy = (p1Sq * (p3.X - p2.X) + p2Sq * (p1.X - p3.X) + p3Sq * (p2.X - p1.X)) / d;

        var center = new GeoPoint2D(cx, cy);
        double radius = center.DistanceTo(p1);

        circle = new GeoCircle2D(center, radius);
        return true;
    }

    public static double CalculateAngle3Points(GeoPoint2D p1, GeoPoint2D vertex, GeoPoint2D p2)
    {
        double v1x = p1.X - vertex.X;
        double v1y = p1.Y - vertex.Y;
        double v2x = p2.X - vertex.X;
        double v2y = p2.Y - vertex.Y;

        double dot = v1x * v2x + v1y * v2y;
        double len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
        double len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

        if (len1 < 1e-9 || len2 < 1e-9) return 0.0;

        double cos = Math.Clamp(dot / (len1 * len2), -1.0, 1.0);
        return Math.Acos(cos) * (180.0 / Math.PI);
    }

    public static double CalculateLineLineAngle(GeoLine2D l1, GeoLine2D l2)
    {
        double a1 = l1.AngleDeg;
        double a2 = l2.AngleDeg;
        double diff = Math.Abs(a1 - a2);
        if (diff > 180.0) diff = 360.0 - diff;
        if (diff > 90.0) diff = 180.0 - diff;
        return diff;
    }

    public static double CalculateAngleBetweenLines(GeoLine2D l1, GeoLine2D l2) => CalculateLineLineAngle(l1, l2);

    public static bool TryFindLineIntersection(GeoLine2D l1, GeoLine2D l2, out GeoPoint2D intersection)
    {
        intersection = default;

        double x1 = l1.P1.X, y1 = l1.P1.Y;
        double x2 = l1.P2.X, y2 = l1.P2.Y;
        double x3 = l2.P1.X, y3 = l2.P1.Y;
        double x4 = l2.P2.X, y4 = l2.P2.Y;

        double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (Math.Abs(denom) < 1e-9)
        {
            return false;
        }

        double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
        double ix = x1 + t * (x2 - x1);
        double iy = y1 + t * (y2 - y1);

        intersection = new GeoPoint2D(ix, iy);
        return true;
    }

    public static bool TryFindIntersection(GeoLine2D l1, GeoLine2D l2, out GeoPoint2D intersection) => TryFindLineIntersection(l1, l2, out intersection);

    public static double CalculateLineLineDistance(GeoLine2D l1, GeoLine2D l2)
    {
        // For two lines: average distance of l1 endpoints to l2
        double d1 = l2.DistanceToPoint(l1.P1);
        double d2 = l2.DistanceToPoint(l1.P2);
        return (d1 + d2) / 2.0;
    }

    public static double CalculateLineToLineDistance(GeoLine2D l1, GeoLine2D l2) => CalculateLineLineDistance(l1, l2);

    public static bool TryFitRotatedRect3Points(GeoPoint2D p1, GeoPoint2D p2, GeoPoint2D p3, out GeoRotatedRect2D rotRect)
    {
        rotRect = default;
        // P1 -> P2 forms the baseline (Length / Width1)
        var baseline = new GeoLine2D(p1, p2);
        double width = baseline.Length;
        if (width < 1e-6) return false;

        // P3 distance to baseline is Height
        double height = baseline.DistanceToPoint(p3);

        // Angle is baseline angle
        double angleDeg = baseline.AngleDeg;

        // Center calculation
        var projP3 = baseline.ProjectPoint(p3);
        double offsetH_X = p3.X - projP3.X;
        double offsetH_Y = p3.Y - projP3.Y;

        var baseMid = baseline.Midpoint;
        var center = new GeoPoint2D(baseMid.X + offsetH_X / 2.0, baseMid.Y + offsetH_Y / 2.0);

        rotRect = new GeoRotatedRect2D(center, width, height, angleDeg);
        return true;
    }

    /// <summary>
    /// Finds sub-pixel edge point on image around click position by scanning local gradient peaks
    /// </summary>
    public static bool TryFindSubpixelEdgePoint(Mat? srcMat, GeoPoint2D clickPos, int roiRadius, out GeoPoint2D edgePoint)
    {
        edgePoint = clickPos;
        if (srcMat == null || srcMat.Empty()) return false;

        int cx = (int)Math.Round(clickPos.X);
        int cy = (int)Math.Round(clickPos.Y);

        int x1 = Math.Max(0, cx - roiRadius);
        int y1 = Math.Max(0, cy - roiRadius);
        int x2 = Math.Min(srcMat.Width - 1, cx + roiRadius);
        int y2 = Math.Min(srcMat.Height - 1, cy + roiRadius);

        int rw = x2 - x1 + 1;
        int rh = y2 - y1 + 1;
        if (rw < 5 || rh < 5) return false;

        using var roi = new Mat(srcMat, new OpenCvSharp.Rect(x1, y1, rw, rh));
        using var gray = new Mat();
        if (roi.Channels() == 3 || roi.Channels() == 4)
        {
            Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            roi.CopyTo(gray);
        }

        // Apply slight Gaussian blur to reduce noise
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(3, 3), 0.8);

        // Compute Sobel gradients in X and Y
        using var gradX = new Mat();
        using var gradY = new Mat();
        Cv2.Sobel(blurred, gradX, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(blurred, gradY, MatType.CV_32F, 0, 1, 3);

        using var magnitude = new Mat();
        Cv2.Magnitude(gradX, gradY, magnitude);

        // Find max gradient magnitude inside ROI
        Cv2.MinMaxLoc(magnitude, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);
        if (maxVal < 10.0)
        {
            return false; // No significant edge found
        }

        // Sub-pixel parabolic refinement in 3x3 patch around maxLoc
        double subX = maxLoc.X;
        double subY = maxLoc.Y;

        if (maxLoc.X > 0 && maxLoc.X < rw - 1 && maxLoc.Y > 0 && maxLoc.Y < rh - 1)
        {
            float vLeft = magnitude.At<float>(maxLoc.Y, maxLoc.X - 1);
            float vMid = magnitude.At<float>(maxLoc.Y, maxLoc.X);
            float vRight = magnitude.At<float>(maxLoc.Y, maxLoc.X + 1);

            double denomX = 2.0 * (2.0 * vMid - vLeft - vRight);
            if (Math.Abs(denomX) > 1e-6)
            {
                double deltaX = (vLeft - vRight) / denomX;
                subX += Math.Clamp(deltaX, -0.5, 0.5);
            }

            float vTop = magnitude.At<float>(maxLoc.Y - 1, maxLoc.X);
            float vBottom = magnitude.At<float>(maxLoc.Y + 1, maxLoc.X);

            double denomY = 2.0 * (2.0 * vMid - vTop - vBottom);
            if (Math.Abs(denomY) > 1e-6)
            {
                double deltaY = (vTop - vBottom) / denomY;
                subY += Math.Clamp(deltaY, -0.5, 0.5);
            }
        }

        edgePoint = new GeoPoint2D(x1 + subX, y1 + subY);
        return true;
    }
}
