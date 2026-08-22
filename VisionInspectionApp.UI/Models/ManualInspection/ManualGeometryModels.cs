using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionInspectionApp.UI.Models.ManualInspection;

public record struct GeoPoint2D(double X, double Y)
{
    public double DistanceTo(GeoPoint2D other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static double Distance(GeoPoint2D a, GeoPoint2D b) => a.DistanceTo(b);

    public override string ToString() => $"({X:F1}, {Y:F1})";
}

public record struct GeoLine2D(GeoPoint2D P1, GeoPoint2D P2)
{
    public double Length => P1.DistanceTo(P2);

    public double AngleDeg
    {
        get
        {
            double angle = Math.Atan2(P2.Y - P1.Y, P2.X - P1.X) * 180.0 / Math.PI;
            if (angle < 0) angle += 360.0;
            return angle;
        }
    }

    public double AngleToHorizontalDeg()
    {
        double angle = Math.Atan2(P2.Y - P1.Y, P2.X - P1.X) * 180.0 / Math.PI;
        if (angle < 0) angle += 180.0;
        return angle;
    }

    public GeoPoint2D Midpoint => new((P1.X + P2.X) / 2.0, (P1.Y + P2.Y) / 2.0);

    public double DistanceToPoint(GeoPoint2D pt)
    {
        double dx = P2.X - P1.X;
        double dy = P2.Y - P1.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-9) return P1.DistanceTo(pt);

        double cross = Math.Abs((pt.X - P1.X) * dy - (pt.Y - P1.Y) * dx);
        return cross / Math.Sqrt(lenSq);
    }

    public GeoPoint2D ProjectPoint(GeoPoint2D pt)
    {
        double dx = P2.X - P1.X;
        double dy = P2.Y - P1.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-9) return P1;

        double t = ((pt.X - P1.X) * dx + (pt.Y - P1.Y) * dy) / lenSq;
        return new GeoPoint2D(P1.X + t * dx, P1.Y + t * dy);
    }
}

public record struct GeoCircle2D(GeoPoint2D Center, double Radius)
{
    public double Diameter => Radius * 2.0;
    public double Area => Math.PI * Radius * Radius;
    public double Circumference => 2.0 * Math.PI * Radius;
}

public record struct GeoArc2D(GeoPoint2D Center, double Radius, double StartAngleDeg, double EndAngleDeg, double SweepAngleDeg)
{
    public double ArcLength => Radius * Math.Abs(SweepAngleDeg) * (Math.PI / 180.0);
}

public record struct GeoRectangle2D(double X, double Y, double Width, double Height)
{
    public double Area => Width * Height;
    public double Perimeter => 2.0 * (Width + Height);
    public GeoPoint2D Center => new(X + Width / 2.0, Y + Height / 2.0);

    public static GeoRectangle2D FromTwoPoints(GeoPoint2D p1, GeoPoint2D p2)
    {
        double x = Math.Min(p1.X, p2.X);
        double y = Math.Min(p1.Y, p2.Y);
        double w = Math.Abs(p2.X - p1.X);
        double h = Math.Abs(p2.Y - p1.Y);
        return new GeoRectangle2D(x, y, w, h);
    }
}

public record struct GeoRotatedRect2D(GeoPoint2D Center, double Width, double Height, double AngleDeg)
{
    public double Area => Width * Height;
    public double Perimeter => 2.0 * (Width + Height);

    public List<GeoPoint2D> GetCorners()
    {
        double rad = AngleDeg * Math.PI / 180.0;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);

        double hw = Width / 2.0;
        double hh = Height / 2.0;

        double[] dx = { -hw, hw, hw, -hw };
        double[] dy = { -hh, -hh, hh, hh };

        var list = new List<GeoPoint2D>(4);
        for (int i = 0; i < 4; i++)
        {
            double rx = dx[i] * cos - dy[i] * sin + Center.X;
            double ry = dx[i] * sin + dy[i] * cos + Center.Y;
            list.Add(new GeoPoint2D(rx, ry));
        }
        return list;
    }
}

public record GeoPolygon2D(List<GeoPoint2D> Points)
{
    public double Perimeter
    {
        get
        {
            if (Points == null || Points.Count < 2) return 0;
            double p = 0;
            for (int i = 0; i < Points.Count; i++)
            {
                p += Points[i].DistanceTo(Points[(i + 1) % Points.Count]);
            }
            return p;
        }
    }

    public double Area
    {
        get
        {
            if (Points == null || Points.Count < 3) return 0;
            double area = 0;
            for (int i = 0; i < Points.Count; i++)
            {
                var p1 = Points[i];
                var p2 = Points[(i + 1) % Points.Count];
                area += (p1.X * p2.Y) - (p2.X * p1.Y);
            }
            return Math.Abs(area) / 2.0;
        }
    }

    public GeoPoint2D Centroid
    {
        get
        {
            if (Points == null || Points.Count == 0) return new(0, 0);
            return new(Points.Average(p => p.X), Points.Average(p => p.Y));
        }
    }
}
