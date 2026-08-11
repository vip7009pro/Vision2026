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

        using var roiMat = CropSubMat(inputMat, def.InspectRoi);
        if (roiMat.Empty())
        {
            return new ColorDiffResult(def.Name, false, 0, 0, 0, 0, 0, 0, 999.0, def.MaxDeltaE);
        }

        var (measuredL, measuredA, measuredB) = GetMeanLab(roiMat);

        double refL = def.RefL;
        double refA = def.RefA;
        double refB = def.RefB;

        if (!def.UseRefColor && def.RefRoi != null && def.RefRoi.Width > 0 && def.RefRoi.Height > 0)
        {
            using var refMat = CropSubMat(inputMat, def.RefRoi);
            if (!refMat.Empty())
            {
                (refL, refA, refB) = GetMeanLab(refMat);
            }
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

    private static Mat CropSubMat(Mat src, Roi roi)
    {
        if (roi == null || roi.Width <= 0 || roi.Height <= 0)
        {
            return src.Clone();
        }

        int x = Math.Clamp(roi.X, 0, Math.Max(0, src.Width - 1));
        int y = Math.Clamp(roi.Y, 0, Math.Max(0, src.Height - 1));
        int w = Math.Min(roi.Width, src.Width - x);
        int h = Math.Min(roi.Height, src.Height - y);

        if (w <= 0 || h <= 0)
        {
            return src.Clone();
        }

        using var sub = new Mat(src, new Rect(x, y, w, h));
        return sub.Clone();
    }

    private static (double L, double A, double B) GetMeanLab(Mat mat)
    {
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

        Scalar mean = Cv2.Mean(labMat);

        // Convert OpenCV 8-bit Lab to standard CIELab range: L: 0..100, a, b: -128..127
        double l = mean.Val0 * 100.0 / 255.0;
        double a = mean.Val1 - 128.0;
        double b = mean.Val2 - 128.0;

        return (l, a, b);
    }
}
