using System;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

public static class CropProcessor
{
    public static Mat Run(Mat inputMat, Roi cropRoi)
    {
        if (inputMat is null || inputMat.IsDisposed || inputMat.Empty())
        {
            return new Mat();
        }

        if (cropRoi is null || cropRoi.Width <= 0 || cropRoi.Height <= 0)
        {
            return inputMat.Clone();
        }

        int x = Math.Clamp(cropRoi.X, 0, Math.Max(0, inputMat.Width - 1));
        int y = Math.Clamp(cropRoi.Y, 0, Math.Max(0, inputMat.Height - 1));
        int w = Math.Min(cropRoi.Width, inputMat.Width - x);
        int h = Math.Min(cropRoi.Height, inputMat.Height - y);

        if (w <= 0 || h <= 0)
        {
            return inputMat.Clone();
        }

        using var roiSubMat = new Mat(inputMat, new Rect(x, y, w, h));
        return roiSubMat.Clone();
    }
}
