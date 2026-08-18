using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.VisionEngine;

public sealed record CaliperEdgePoint(double X, double Y, double Strength);

public sealed record CaliperResult(
    string Name,
    bool Found,
    List<CaliperEdgePoint> Points,
    Point2d LineP1,
    Point2d LineP2,
    double AvgStrength);

public static class CaliperDetector
{
    public static CaliperResult Detect(
        Mat matBgrOrGray,
        CaliperDefinition def,
        Point2d originTeach = default,
        Point2d originFound = default,
        double originAngleDeg = 0.0)
    {
        if (matBgrOrGray is null || matBgrOrGray.Empty() || def is null || def.SearchRoi.Width <= 0 || def.SearchRoi.Height <= 0)
        {
            return new CaliperResult(def?.Name ?? string.Empty, Found: false, new List<CaliperEdgePoint>(), default, default, 0.0);
        }

        using var patch = Geometry2D.ExtractStraightRoi(matBgrOrGray, def.SearchRoi, originTeach, originFound, originAngleDeg, out var centerFound);
        if (patch.Empty())
        {
            return new CaliperResult(def.Name, Found: false, new List<CaliperEdgePoint>(), default, default, 0.0);
        }

        using var patchGrayOwned = patch.Channels() == 1 ? null : patch.CvtColor(ColorConversionCodes.BGR2GRAY);
        Mat gray = patchGrayOwned ?? patch;

        var rect = new Rect(0, 0, patch.Width, patch.Height);

        var stripCount = Math.Clamp(def.StripCount, 1, 200);
        var stripWidth = Math.Clamp(def.StripWidth, 1, Math.Max(1, Math.Min(rect.Width, rect.Height)));
        var stripLength = Math.Clamp(def.StripLength, 3, Math.Max(3, Math.Max(rect.Width, rect.Height)));
        var minStrength = Math.Max(0.5, def.MinEdgeStrength);

        var points = new List<CaliperEdgePoint>(stripCount);
        var strengths = new List<double>(stripCount);

        static double InterpPeak(double a, double b, double c)
        {
            var denom = a - 2 * b + c;
            if (Math.Abs(denom) < 1e-12) return 0.0;
            return 0.5 * (a - c) / denom;
        }

        double totalAngleDeg = originAngleDeg + def.SearchRoi.Angle;

        for (var i = 0; i < stripCount; i++)
        {
            if (def.Orientation == CaliperOrientation.Vertical)
            {
                var xCenter = (i + 0.5) * rect.Width / stripCount;
                var x0 = (int)Math.Round(xCenter - stripWidth / 2.0);
                var y0 = (int)Math.Round((rect.Height - stripLength) / 2.0);
                var sr = new Rect(x0, y0, stripWidth, stripLength)
                    .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                if (sr.Width <= 0 || sr.Height <= 2) continue;

                using var s = new Mat(gray, sr);
                using var prof = new Mat();
                Cv2.Reduce(s, prof, dim: ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_64F);

                var n = prof.Rows;
                if (n < 3) continue;

                // Read raw 1D profile
                var rawProf = new double[n];
                for (var y = 0; y < n; y++)
                {
                    rawProf[y] = prof.Get<double>(y, 0);
                }

                // 3-point Gaussian smooth to reduce sensor noise: [0.25, 0.5, 0.25]
                var smoothProf = new double[n];
                smoothProf[0] = rawProf[0];
                smoothProf[n - 1] = rawProf[n - 1];
                for (var y = 1; y < n - 1; y++)
                {
                    smoothProf[y] = 0.25 * rawProf[y - 1] + 0.5 * rawProf[y] + 0.25 * rawProf[y + 1];
                }

                var bestIdx = -1;
                var bestVal = 0.0;
                for (var y = 1; y < n - 1; y++)
                {
                    var g = (smoothProf[y + 1] - smoothProf[y - 1]) * 0.5;
                    if (def.Polarity == EdgePolarity.DarkToLight)
                    {
                        if (g <= 0) continue;
                    }
                    else if (def.Polarity == EdgePolarity.LightToDark)
                    {
                        if (g >= 0) continue;
                        g = -g;
                    }
                    else
                    {
                        g = Math.Abs(g);
                    }

                    if (g > bestVal)
                    {
                        bestVal = g;
                        bestIdx = y;
                    }
                }

                if (bestIdx < 1 || bestIdx >= n - 1) continue;
                if (bestVal < minStrength) continue;

                var gL = Math.Abs(smoothProf[bestIdx] - smoothProf[bestIdx - 1]);
                var gC = bestVal;
                var gR = Math.Abs(smoothProf[bestIdx + 1] - smoothProf[bestIdx]);
                var sub = InterpPeak(gL, gC, gR);

                var ySub = bestIdx + Math.Clamp(sub, -0.5, 0.5);
                var xLocal = rect.X + sr.X + sr.Width / 2.0;
                var yLocal = rect.Y + sr.Y + ySub;
                var ptGlobal = Geometry2D.MapToGlobal(new Point2d(xLocal, yLocal), patch.Width, patch.Height, centerFound, totalAngleDeg);
                points.Add(new CaliperEdgePoint(ptGlobal.X, ptGlobal.Y, bestVal));
                strengths.Add(bestVal);
            }
            else
            {
                var yCenter = (i + 0.5) * rect.Height / stripCount;
                var y0 = (int)Math.Round(yCenter - stripWidth / 2.0);
                var x0 = (int)Math.Round((rect.Width - stripLength) / 2.0);
                var sr = new Rect(x0, y0, stripLength, stripWidth)
                    .Intersect(new Rect(0, 0, rect.Width, rect.Height));
                if (sr.Width <= 2 || sr.Height <= 0) continue;

                using var s = new Mat(gray, sr);
                using var prof = new Mat();
                Cv2.Reduce(s, prof, dim: ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_64F);

                var n = prof.Cols;
                if (n < 3) continue;

                // Read raw 1D profile
                var rawProf = new double[n];
                for (var x = 0; x < n; x++)
                {
                    rawProf[x] = prof.Get<double>(0, x);
                }

                // 3-point Gaussian smooth: [0.25, 0.5, 0.25]
                var smoothProf = new double[n];
                smoothProf[0] = rawProf[0];
                smoothProf[n - 1] = rawProf[n - 1];
                for (var x = 1; x < n - 1; x++)
                {
                    smoothProf[x] = 0.25 * rawProf[x - 1] + 0.5 * rawProf[x] + 0.25 * rawProf[x + 1];
                }

                var bestIdx = -1;
                var bestVal = 0.0;
                for (var x = 1; x < n - 1; x++)
                {
                    var g = (smoothProf[x + 1] - smoothProf[x - 1]) * 0.5;
                    if (def.Polarity == EdgePolarity.DarkToLight)
                    {
                        if (g <= 0) continue;
                    }
                    else if (def.Polarity == EdgePolarity.LightToDark)
                    {
                        if (g >= 0) continue;
                        g = -g;
                    }
                    else
                    {
                        g = Math.Abs(g);
                    }

                    if (g > bestVal)
                    {
                        bestVal = g;
                        bestIdx = x;
                    }
                }

                if (bestIdx < 1 || bestIdx >= n - 1) continue;
                if (bestVal < minStrength) continue;

                var gL = Math.Abs(smoothProf[bestIdx] - smoothProf[bestIdx - 1]);
                var gC = bestVal;
                var gR = Math.Abs(smoothProf[bestIdx + 1] - smoothProf[bestIdx]);
                var sub = InterpPeak(gL, gC, gR);

                var xSub = bestIdx + Math.Clamp(sub, -0.5, 0.5);
                var xLocal = rect.X + sr.X + xSub;
                var yLocal = rect.Y + sr.Y + sr.Height / 2.0;
                var ptGlobal = Geometry2D.MapToGlobal(new Point2d(xLocal, yLocal), patch.Width, patch.Height, centerFound, totalAngleDeg);
                points.Add(new CaliperEdgePoint(ptGlobal.X, ptGlobal.Y, bestVal));
                strengths.Add(bestVal);
            }
        }

        var avg = strengths.Count == 0 ? 0.0 : strengths.Average();

        if (points.Count < 2)
        {
            return new CaliperResult(def.Name, Found: false, points, default, default, avg);
        }

        var meanX = points.Average(p => p.X);
        var meanY = points.Average(p => p.Y);

        var sxx = 0.0;
        var syy = 0.0;
        var sxy = 0.0;
        foreach (var p in points)
        {
            var dx = p.X - meanX;
            var dy = p.Y - meanY;
            sxx += dx * dx;
            syy += dy * dy;
            sxy += dx * dy;
        }

        var theta = 0.5 * Math.Atan2(2 * sxy, (sxx - syy));
        var dir = new Point2d(Math.Cos(theta), Math.Sin(theta));

        var minT = double.PositiveInfinity;
        var maxT = double.NegativeInfinity;
        foreach (var p in points)
        {
            var t = (p.X - meanX) * dir.X + (p.Y - meanY) * dir.Y;
            if (t < minT) minT = t;
            if (t > maxT) maxT = t;
        }

        if (!double.IsFinite(minT) || !double.IsFinite(maxT) || Math.Abs(maxT - minT) < 1e-6)
        {
            return new CaliperResult(def.Name, Found: false, points, default, default, avg);
        }

        var p1 = new Point2d(meanX + minT * dir.X, meanY + minT * dir.Y);
        var p2 = new Point2d(meanX + maxT * dir.X, meanY + maxT * dir.Y);

        return new CaliperResult(def.Name, Found: true, points, p1, p2, avg);
    }
}
