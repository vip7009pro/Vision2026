using System;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

public static class ColorDiffProcessor
{
    public static ColorDiffResult Run(Mat inputMat, ColorDiffDefinition def)
    {
        if (inputMat is null || inputMat.IsDisposed || inputMat.Empty() || def is null)
        {
            return new ColorDiffResult(def?.Name ?? string.Empty, false, 0, 0, 0, 0, 0, 0, 999.0, def?.MaxDeltaE ?? 5.0);
        }

        var (measuredL, measuredA, measuredB) = GetMeanLab(inputMat, def.InspectRoi);

        double refL = def.RefL;
        double refA = def.RefA;
        double refB = def.RefB;

        if (!def.UseRefColor && def.RefRoi != null && def.RefRoi.Width > 0 && def.RefRoi.Height > 0)
        {
            (refL, refA, refB) = GetMeanLab(inputMat, def.RefRoi);
        }

        double deltaE = Math.Sqrt(
            Math.Pow(measuredL - refL, 2) +
            Math.Pow(measuredA - refA, 2) +
            Math.Pow(measuredB - refB, 2)
        );

        bool pass = deltaE <= def.MaxDeltaE;

        return new ColorDiffResult(
            def.Name,
            pass,
            Math.Round(measuredL, 2),
            Math.Round(measuredA, 2),
            Math.Round(measuredB, 2),
            Math.Round(refL, 2),
            Math.Round(refA, 2),
            Math.Round(refB, 2),
            Math.Round(deltaE, 2),
            def.MaxDeltaE
        );
    }

    private static (double L, double A, double B) GetMeanLab(Mat mat, Roi roi)
    {
        if (roi == null || roi.Width <= 0 || roi.Height <= 0)
        {
            return (0, 0, 0);
        }

        using var bgrMat = new Mat();
        if (mat.Channels() == 1)
        {
            Cv2.CvtColor(mat, bgrMat, ColorConversionCodes.GRAY2BGR);
        }
        else if (mat.Channels() == 4)
        {
            Cv2.CvtColor(mat, bgrMat, ColorConversionCodes.BGRA2BGR);
        }
        else
        {
            mat.CopyTo(bgrMat);
        }

        using var labMat = new Mat();
        Cv2.CvtColor(bgrMat, labMat, ColorConversionCodes.BGR2Lab);

        double angleDeg = roi.Angle;
        double centerX = roi.X + roi.Width / 2.0;
        double centerY = roi.Y + roi.Height / 2.0;

        Scalar mean;

        if (Math.Abs(angleDeg) < 0.01)
        {
            // Axis-aligned rectangle: crop sub-matrix directly for performance
            int x = Math.Clamp(roi.X, 0, Math.Max(0, labMat.Width - 1));
            int y = Math.Clamp(roi.Y, 0, Math.Max(0, labMat.Height - 1));
            int w = Math.Min(roi.Width, labMat.Width - x);
            int h = Math.Min(roi.Height, labMat.Height - y);

            if (w <= 0 || h <= 0)
            {
                mean = Cv2.Mean(labMat);
            }
            else
            {
                using var sub = new Mat(labMat, new Rect(x, y, w, h));
                mean = Cv2.Mean(sub);
            }
        }
        else
        {
            // Rotated rectangle: compute bounding box, create rotated polygon mask, and calculate mean color inside mask
            var rotRect = new RotatedRect(new Point2f((float)centerX, (float)centerY), new Size2f(roi.Width, roi.Height), (float)angleDeg);
            Rect boundingBox = rotRect.BoundingRect();
            Rect imgRect = new Rect(0, 0, labMat.Width, labMat.Height);
            Rect cropRect = boundingBox.Intersect(imgRect);

            if (cropRect.Width <= 0 || cropRect.Height <= 0)
            {
                mean = Cv2.Mean(labMat);
            }
            else
            {
                using var subMat = new Mat(labMat, cropRect);
                using var mask = new Mat(cropRect.Height, cropRect.Width, MatType.CV_8UC1, Scalar.Black);

                Point2f[] pts = rotRect.Points();
                Point[] polyPts = new Point[4];
                for (int i = 0; i < 4; i++)
                {
                    polyPts[i] = new Point((int)Math.Round(pts[i].X - cropRect.X), (int)Math.Round(pts[i].Y - cropRect.Y));
                }

                Cv2.FillConvexPoly(mask, polyPts, Scalar.White);
                mean = Cv2.Mean(subMat, mask);
            }
        }

        // Convert OpenCV 8-bit Lab to standard CIELab range: L: 0..100, a, b: -128..127
        double l = mean.Val0 * 100.0 / 255.0;
        double a = mean.Val1 - 128.0;
        double b = mean.Val2 - 128.0;

        return (l, a, b);
    }
}
