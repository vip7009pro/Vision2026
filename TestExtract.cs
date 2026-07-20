using System;
using OpenCvSharp;

class Program
{
    static void Main()
    {
        // Simulate physical part rotated by 30 degrees CCW
        var src = new Mat(200, 200, MatType.CV_8UC1, Scalar.All(0));
        // Draw a rotated rectangle
        var rRect = new RotatedRect(new Point2f(100, 100), new Size2f(80, 20), 30);
        Cv2.Ellipse(src, rRect, Scalar.All(255), -1);
        Cv2.ImWrite("test_src.png", src);

        var centerInBbox = new Point2f(100, 100);
        
        // originFound.Angle = 30, originTeach.Angle = 0
        double angleDeg = 30.0;
        int diag = 120;

        using var M = Cv2.GetRotationMatrix2D(centerInBbox, angleDeg, 1.0);
        var tx = diag / 2.0 - centerInBbox.X;
        var ty = diag / 2.0 - centerInBbox.Y;
        M.Set(0, 2, M.Get<double>(0, 2) + tx);
        M.Set(1, 2, M.Get<double>(1, 2) + ty);
        
        using var rotatedBbox = new Mat();
        Cv2.WarpAffine(src, rotatedBbox, M, new Size(diag, diag), InterpolationFlags.Linear, BorderTypes.Replicate);

        var patch = new Mat();
        var centerInDst = new Point2f((float)(diag / 2.0), (float)(diag / 2.0));
        Cv2.GetRectSubPix(rotatedBbox, new Size(80, 20), centerInDst, patch);

        Cv2.ImWrite("test_patch.png", patch);
    }
}
