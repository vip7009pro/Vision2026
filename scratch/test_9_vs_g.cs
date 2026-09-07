using System;
using OpenCvSharp;

class Program
{
    static void Main()
    {
        using var img = new Mat(120, 120, MatType.CV_8UC1, Scalar.All(0));
        Cv2.PutText(img, "9", new Point(30, 90), HersheyFonts.HersheySimplex, 2.0, Scalar.All(255), 3, LineTypes.AntiAlias);

        using var nonZero = new Mat();
        Cv2.FindNonZero(img, nonZero);
        var r = Cv2.BoundingRect(nonZero);
        using var crop9 = new Mat(img, r);

        // Chuẩn hóa 9
        using var norm9 = Normalize(crop9);

        // Thử template '9' và 'g' với các font
        CheckChar(norm9, "9", HersheyFonts.HersheySimplex);
        CheckChar(norm9, "9", HersheyFonts.HersheyDuplex);
        CheckChar(norm9, "g", HersheyFonts.HersheySimplex);
        CheckChar(norm9, "g", HersheyFonts.HersheyDuplex);
    }

    static void CheckChar(Mat target, string s, HersheyFonts font)
    {
        for (int th = 1; th <= 3; th++)
        {
            using var canvas = new Mat(120, 120, MatType.CV_8UC1, Scalar.All(0));
            Cv2.PutText(canvas, s, new Point(30, 90), font, 2.0, Scalar.All(255), th, LineTypes.AntiAlias);
            using var nz = new Mat();
            Cv2.FindNonZero(canvas, nz);
            if (nz.Total() == 0) continue;
            using var crop = new Mat(canvas, Cv2.BoundingRect(nz));
            using var tpl = Normalize(crop);

            using var res = new Mat();
            Cv2.MatchTemplate(target, tpl, res, TemplateMatchModes.CCoeffNormed);
            float ccoeff = res.At<float>(0, 0);

            using var inter = new Mat();
            using var union = new Mat();
            Cv2.BitwiseAnd(target, tpl, inter);
            Cv2.BitwiseOr(target, tpl, union);
            double iou = (double)Cv2.CountNonZero(inter) / Cv2.CountNonZero(union);

            double score = 0.40 * ccoeff + 0.60 * iou;
            Console.WriteLine($"Char '{s}', Font {font}, th={th}: ccoeff={ccoeff:F3}, iou={iou:F3}, score={score:F3}");
        }
    }

    static Mat Normalize(Mat crop)
    {
        int canvasW = 24, canvasH = 32;
        var dst = new Mat(canvasH, canvasW, MatType.CV_8UC1, Scalar.All(0));
        int targetInnerW = 20, targetInnerH = 28;
        double scale = Math.Min((double)targetInnerW / crop.Width, (double)targetInnerH / crop.Height);
        int newW = Math.Clamp((int)Math.Round(crop.Width * scale), 1, targetInnerW);
        int newH = Math.Clamp((int)Math.Round(crop.Height * scale), 1, targetInnerH);

        using var resized = new Mat();
        Cv2.Resize(crop, resized, new Size(newW, newH), interpolation: InterpolationFlags.Area);
        Cv2.Threshold(resized, resized, 50, 255, ThresholdTypes.Binary);

        int offsetX = (canvasW - newW) / 2;
        int offsetY = (canvasH - newH) / 2;
        using var sub = new Mat(dst, new Rect(offsetX, offsetY, newW, newH));
        resized.CopyTo(sub);
        return dst;
    }
}
