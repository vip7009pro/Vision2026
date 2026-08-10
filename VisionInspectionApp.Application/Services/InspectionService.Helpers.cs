using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace VisionInspectionApp.Application;

public partial class InspectionService
{
    private static (double DistPx, Point2d A, Point2d B) CalculateLineLineDistance(LineDetectResult la, LineDetectResult lb, LineLineDistanceMode mode)
    {
        // Default / legacy
        if (mode == LineLineDistanceMode.ClosestPointsOnSegments)
        {
            return Geometry2D.SegmentToSegmentDistance(la.P1, la.P2, lb.P1, lb.P2);
        }

        if (mode == LineLineDistanceMode.ExtendToOtherEndpoints)
        {
            var (ea1, ea2) = ExtendSegmentToCoverOtherEndpoints(la.P1, la.P2, lb.P1, lb.P2);
            var (eb1, eb2) = ExtendSegmentToCoverOtherEndpoints(lb.P1, lb.P2, la.P1, la.P2);
            return Geometry2D.SegmentToSegmentDistance(ea1, ea2, eb1, eb2);
        }

        if (mode == LineLineDistanceMode.MidpointToMidpoint)
        {
            var ma = new Point2d((la.P1.X + la.P2.X) * 0.5, (la.P1.Y + la.P2.Y) * 0.5);
            var mb = new Point2d((lb.P1.X + lb.P2.X) * 0.5, (lb.P1.Y + lb.P2.Y) * 0.5);
            return (Geometry2D.Distance(ma, mb), ma, mb);
        }

        // Endpoints based
        var aEnds = new[] { la.P1, la.P2 };
        var bEnds = new[] { lb.P1, lb.P2 };

        var bestDist = mode == LineLineDistanceMode.FarthestEndpoints ? double.NegativeInfinity : double.PositiveInfinity;
        var bestA = la.P1;
        var bestB = lb.P1;

        foreach (var a in aEnds)
        {
            foreach (var b in bEnds)
            {
                var d = Geometry2D.Distance(a, b);
                if (mode == LineLineDistanceMode.NearestEndpoints)
                {
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestA = a;
                        bestB = b;
                    }
                }
                else if (mode == LineLineDistanceMode.FarthestEndpoints)
                {
                    if (d > bestDist)
                    {
                        bestDist = d;
                        bestA = a;
                        bestB = b;
                    }
                }
            }
        }

        return (bestDist, bestA, bestB);
    }

    private static (Point2d P1, Point2d P2) ExtendSegmentToCoverOtherEndpoints(Point2d s1, Point2d s2, Point2d o1, Point2d o2)
    {
        var d = s2 - s1;
        var len2 = d.X * d.X + d.Y * d.Y;
        if (len2 <= 1e-12)
        {
            return (s1, s2);
        }

        // Param along the segment's infinite line: p(t)=s1 + t*d, original endpoints are t=0 and t=1.
        var tO1 = ((o1.X - s1.X) * d.X + (o1.Y - s1.Y) * d.Y) / len2;
        var tO2 = ((o2.X - s1.X) * d.X + (o2.Y - s1.Y) * d.Y) / len2;

        var tMin = Math.Min(0.0, Math.Min(tO1, tO2));
        var tMax = Math.Max(1.0, Math.Max(tO1, tO2));

        var p1 = new Point2d(s1.X + tMin * d.X, s1.Y + tMin * d.Y);
        var p2 = new Point2d(s1.X + tMax * d.X, s1.Y + tMax * d.Y);
        return (p1, p2);
    }

    private static (double DistPx, Point2d Closest) CalculatePointLineDistance(Point2d p, LineDetectResult l, PointLineDistanceMode mode)
    {
        if (mode == PointLineDistanceMode.PointToInfiniteLine)
        {
            var a = l.P1;
            var b = l.P2;
            var ab = b - a;
            var ap = p - a;
            var ab2 = ab.X * ab.X + ab.Y * ab.Y;
            if (ab2 <= 1e-12)
            {
                return (Geometry2D.Distance(p, a), a);
            }

            var t = (ap.X * ab.X + ap.Y * ab.Y) / ab2;
            var proj = new Point2d(a.X + t * ab.X, a.Y + t * ab.Y);
            return (Geometry2D.Distance(p, proj), proj);
        }

        // Default / legacy
        return Geometry2D.PointToSegmentDistance(p, l.P1, l.P2);
    }

    private static (double DistPx, Point2d SegmentPt, Point2d LinePt) CalculateSegmentLineDistance(
        LineDetectResult la,
        LineDetectResult lb,
        SegmentLineDistanceMode mode,
        SegmentLineExtensionMode extensionMode,
        Roi? searchRoiA,
        Point2d originTeach,
        Point2d originFound,
        double angleDeg)
    {
        var segP1 = la.P1;
        var segP2 = la.P2;

        if (extensionMode == SegmentLineExtensionMode.ExtendToSearchRoiBounds && searchRoiA is not null && searchRoiA.Width > 0 && searchRoiA.Height > 0)
        {
            var dir = segP2 - segP1;
            if (dir.X * dir.X + dir.Y * dir.Y > 1e-9)
            {
                var roiCenter = new Point2d(searchRoiA.X + searchRoiA.Width / 2.0, searchRoiA.Y + searchRoiA.Height / 2.0);
                if (originFound.X != 0 || originFound.Y != 0)
                {
                    var rotC = Rotate(roiCenter, originTeach, angleDeg);
                    roiCenter = new Point2d(rotC.X + (originFound.X - originTeach.X), rotC.Y + (originFound.Y - originTeach.Y));
                }
                var totalAngle = angleDeg + searchRoiA.Angle;
                var halfW = searchRoiA.Width / 2.0;
                var halfH = searchRoiA.Height / 2.0;

                var rad = totalAngle * Math.PI / 180.0;
                var cos = Math.Cos(rad);
                var sin = Math.Sin(rad);

                Point2d ToLocal(Point2d g)
                {
                    var dx = g.X - roiCenter.X;
                    var dy = g.Y - roiCenter.Y;
                    return new Point2d(dx * cos + dy * sin, -dx * sin + dy * cos);
                }

                Point2d ToGlobal(Point2d loc)
                {
                    var gx = roiCenter.X + loc.X * cos - loc.Y * sin;
                    var gy = roiCenter.Y + loc.X * sin + loc.Y * cos;
                    return new Point2d(gx, gy);
                }

                var locP1 = ToLocal(segP1);
                var locP2 = ToLocal(segP2);
                var locDir = locP2 - locP1;

                if (Math.Abs(locDir.X) > 1e-9 || Math.Abs(locDir.Y) > 1e-9)
                {
                    var ts = new List<double>();
                    if (Math.Abs(locDir.X) > 1e-9)
                    {
                        var tLeft = (-halfW - locP1.X) / locDir.X;
                        var yLeft = locP1.Y + tLeft * locDir.Y;
                        if (yLeft >= -halfH - 1e-3 && yLeft <= halfH + 1e-3) ts.Add(tLeft);

                        var tRight = (halfW - locP1.X) / locDir.X;
                        var yRight = locP1.Y + tRight * locDir.Y;
                        if (yRight >= -halfH - 1e-3 && yRight <= halfH + 1e-3) ts.Add(tRight);
                    }

                    if (Math.Abs(locDir.Y) > 1e-9)
                    {
                        var tTop = (-halfH - locP1.Y) / locDir.Y;
                        var xTop = locP1.X + tTop * locDir.X;
                        if (xTop >= -halfW - 1e-3 && xTop <= halfW + 1e-3) ts.Add(tTop);

                        var tBottom = (halfH - locP1.Y) / locDir.Y;
                        var xBottom = locP1.X + tBottom * locDir.X;
                        if (xBottom >= -halfW - 1e-3 && xBottom <= halfW + 1e-3) ts.Add(tBottom);
                    }

                    if (ts.Count >= 2)
                    {
                        var minT = ts.Min();
                        var maxT = ts.Max();
                        var extLoc1 = new Point2d(locP1.X + minT * locDir.X, locP1.Y + minT * locDir.Y);
                        var extLoc2 = new Point2d(locP1.X + maxT * locDir.X, locP1.Y + maxT * locDir.Y);
                        segP1 = ToGlobal(extLoc1);
                        segP2 = ToGlobal(extLoc2);
                    }
                }
            }
        }

        static Point2d ClosestPointOnInfiniteLine(Point2d p, Point2d q1, Point2d q2)
        {
            var v = q2 - q1;
            var len2 = v.X * v.X + v.Y * v.Y;
            if (len2 <= 1e-12) return q1;
            var t = ((p.X - q1.X) * v.X + (p.Y - q1.Y) * v.Y) / len2;
            return new Point2d(q1.X + t * v.X, q1.Y + t * v.Y);
        }

        if (mode == SegmentLineDistanceMode.MidpointToInfiniteLine)
        {
            var mid = new Point2d((segP1.X + segP2.X) * 0.5, (segP1.Y + segP2.Y) * 0.5);
            var proj = ClosestPointOnInfiniteLine(mid, lb.P1, lb.P2);
            return (Geometry2D.Distance(mid, proj), mid, proj);
        }

        var c1 = ClosestPointOnInfiniteLine(segP1, lb.P1, lb.P2);
        var c2 = ClosestPointOnInfiniteLine(segP2, lb.P1, lb.P2);
        var d1 = Geometry2D.Distance(segP1, c1);
        var d2 = Geometry2D.Distance(segP2, c2);

        if (mode == SegmentLineDistanceMode.ClosestPointOnSegmentToInfiniteLine)
        {
            var vLine = lb.P2 - lb.P1;
            var cross1 = (lb.P2.X - lb.P1.X) * (segP1.Y - lb.P1.Y) - (lb.P2.Y - lb.P1.Y) * (segP1.X - lb.P1.X);
            var cross2 = (lb.P2.X - lb.P1.X) * (segP2.Y - lb.P1.Y) - (lb.P2.Y - lb.P1.Y) * (segP2.X - lb.P1.X);
            if (cross1 * cross2 <= 0 && (Math.Abs(cross1) > 1e-9 || Math.Abs(cross2) > 1e-9))
            {
                var denom = (segP2.X - segP1.X) * vLine.Y - (segP2.Y - segP1.Y) * vLine.X;
                if (Math.Abs(denom) > 1e-9)
                {
                    var tSeg = ((lb.P1.X - segP1.X) * vLine.Y - (lb.P1.Y - segP1.Y) * vLine.X) / denom;
                    var inter = new Point2d(segP1.X + tSeg * (segP2.X - segP1.X), segP1.Y + tSeg * (segP2.Y - segP1.Y));
                    return (0.0, inter, inter);
                }
            }

            return d1 <= d2 ? (d1, segP1, c1) : (d2, segP2, c2);
        }

        return d1 >= d2 ? (d1, segP1, c1) : (d2, segP2, c2);
    }

    private static Point2d Rotate(Point2d p, Point2d origin, double angleDeg)
    {
        if (Math.Abs(angleDeg) < 0.000001)
        {
            return p;
        }

        var a = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(a);
        var sin = Math.Sin(a);

        var dx = p.X - origin.X;
        var dy = p.Y - origin.Y;
        var x = dx * cos - dy * sin;
        var y = dx * sin + dy * cos;
        return new Point2d(x + origin.X, y + origin.Y);
    }

    private static Mat ExtractStraightRoi(Mat source, Roi roiTeach, Point2d originTeach, Point2d originFound, double angleDeg, out Point2d centerFound)
    {
        var centerTeach = new Point2d(roiTeach.X + roiTeach.Width / 2.0, roiTeach.Y + roiTeach.Height / 2.0);
        var centerRot = Rotate(centerTeach, originTeach, angleDeg);
        var dx = originFound.X - originTeach.X;
        var dy = originFound.Y - originTeach.Y;
        centerFound = new Point2d(centerRot.X + dx, centerRot.Y + dy);

        double totalAngleDeg = angleDeg + roiTeach.Angle;

        if (Math.Abs(totalAngleDeg) < 0.001)
        {
            var rx = (int)Math.Round(centerFound.X - roiTeach.Width / 2.0);
            var ry = (int)Math.Round(centerFound.Y - roiTeach.Height / 2.0);
            var rect = new Rect(rx, ry, roiTeach.Width, roiTeach.Height).Intersect(new Rect(0, 0, source.Width, source.Height));
            if (rect.Width <= 0 || rect.Height <= 0) return new Mat();
            return new Mat(source, rect).Clone();
        }

        int diag = (int)Math.Ceiling(Math.Sqrt(roiTeach.Width * roiTeach.Width + roiTeach.Height * roiTeach.Height));
        var bbox = new Rect((int)(centerFound.X - diag / 2.0), (int)(centerFound.Y - diag / 2.0), diag, diag);
        var safeBbox = bbox.Intersect(new Rect(0, 0, source.Width, source.Height));
        if (safeBbox.Width <= 0 || safeBbox.Height <= 0) return new Mat();

        using var subSource = new Mat(source, safeBbox);
        var centerInBbox = new Point2f((float)(centerFound.X - safeBbox.X), (float)(centerFound.Y - safeBbox.Y));
        
        using var M = Cv2.GetRotationMatrix2D(centerInBbox, totalAngleDeg, 1.0);
        var tx = diag / 2.0 - centerInBbox.X;
        var ty = diag / 2.0 - centerInBbox.Y;
        M.Set(0, 2, M.Get<double>(0, 2) + tx);
        M.Set(1, 2, M.Get<double>(1, 2) + ty);
        using var rotatedBbox = new Mat();
        Cv2.WarpAffine(subSource, rotatedBbox, M, new Size(diag, diag), InterpolationFlags.Linear, BorderTypes.Replicate);

        var patch = new Mat();
        var centerInDst = new Point2f((float)(diag / 2.0), (float)(diag / 2.0));
        Cv2.GetRectSubPix(rotatedBbox, new Size(roiTeach.Width, roiTeach.Height), centerInDst, patch);
        return patch;
    }

    private static Point2d MapToGlobal(Point2d ptLocal, double w, double h, Point2d centerFound, double angleDeg)
    {
        var ptCenter = new Point2d(ptLocal.X - w / 2.0, ptLocal.Y - h / 2.0);
        var ptRot = Rotate(ptCenter, new Point2d(0, 0), angleDeg);
        return new Point2d(ptRot.X + centerFound.X, ptRot.Y + centerFound.Y);
    }

    private static Roi TransformRoi(Roi roi, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return new Roi();
        }

        var p1 = new Point2d(roi.X, roi.Y);
        var p2 = new Point2d(roi.X + roi.Width, roi.Y);
        var p3 = new Point2d(roi.X + roi.Width, roi.Y + roi.Height);
        var p4 = new Point2d(roi.X, roi.Y + roi.Height);

        p1 = Rotate(p1, originTeach, angleDeg);
        p2 = Rotate(p2, originTeach, angleDeg);
        p3 = Rotate(p3, originTeach, angleDeg);
        p4 = Rotate(p4, originTeach, angleDeg);

        var dx = originFound.X - originTeach.X;
        var dy = originFound.Y - originTeach.Y;

        p1 = new Point2d(p1.X + dx, p1.Y + dy);
        p2 = new Point2d(p2.X + dx, p2.Y + dy);
        p3 = new Point2d(p3.X + dx, p3.Y + dy);
        p4 = new Point2d(p4.X + dx, p4.Y + dy);

        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

        return new Roi
        {
            X = (int)Math.Round(minX),
            Y = (int)Math.Round(minY),
            Width = (int)Math.Round(maxX - minX),
            Height = (int)Math.Round(maxY - minY),
            Angle = Math.Round(roi.Angle + angleDeg, 1)
        };
    }

    private static Roi TransformRoiKeepSize(Roi roi, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return new Roi();
        }

        var centerTeach = new Point2d(roi.X + roi.Width / 2.0, roi.Y + roi.Height / 2.0);
        var centerRot = Rotate(centerTeach, originTeach, angleDeg);

        var dx = originFound.X - originTeach.X;
        var dy = originFound.Y - originTeach.Y;
        var centerFound = new Point2d(centerRot.X + dx, centerRot.Y + dy);

        return new Roi
        {
            X = (int)Math.Round(centerFound.X - roi.Width / 2.0),
            Y = (int)Math.Round(centerFound.Y - roi.Height / 2.0),
            Width = roi.Width,
            Height = roi.Height,
            Angle = Math.Round(roi.Angle + angleDeg, 1)
        };
    }

    private static PointDefinition TransformPointDefinition(PointDefinition p, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        return new PointDefinition
        {
            Name = p.Name,
            MatchScoreThreshold = p.MatchScoreThreshold,
            TemplateImageFile = p.TemplateImageFile,
            TemplateRoi = p.TemplateRoi,
            SearchRoi = TransformRoiKeepSize(p.SearchRoi, originTeach, originFound, angleDeg),
            WorldPosition = p.WorldPosition,
            OffsetPx = p.OffsetPx,
            Algorithm = p.Algorithm,
            OriginAlgorithm = p.Algorithm switch
            {
                PointFindAlgorithm.TemplateMatch => OriginAlgorithm.TemplateMatch,
                PointFindAlgorithm.FeatureBased => OriginAlgorithm.FeatureBased,
                _ => p.OriginAlgorithm
            },
            MinAngle = p.MinAngle,
            MaxAngle = p.MaxAngle,
            AngleStep = p.AngleStep,
            EdgePoint = p.EdgePoint,

            ShapeModel = p.ShapeModel,
            EdgeThresholdMin = p.EdgeThresholdMin,
            EdgeThresholdMax = p.EdgeThresholdMax
        };
    }

    private static DefectInspectionConfig TransformDefectConfig(DefectInspectionConfig cfg, Point2d originTeach, Point2d originFound, double angleDeg)
    {
        return new DefectInspectionConfig
        {
            InspectRoi = TransformRoi(cfg.InspectRoi, originTeach, originFound, angleDeg),
            ThresholdWhite = cfg.ThresholdWhite,
            ThresholdBlack = cfg.ThresholdBlack,
            MinBlobSize = cfg.MinBlobSize,
            MaxBlobSize = cfg.MaxBlobSize
        };
    }
}
