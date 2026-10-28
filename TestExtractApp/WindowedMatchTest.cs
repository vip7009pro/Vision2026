using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;

namespace TestExtractApp
{
    public static class WindowedMatchTest
    {
        public struct Cand
        {
            public double X, Y, Angle, Score;
        }

        public static void Run()
        {
            string searchPath = @"C:\Users\Admin\.gemini\antigravity-ide\brain\b3629366-addf-4d36-86df-04aa9402ecf2\.user_uploaded\media_1793162942034.png";
            string templatePath = @"C:\Users\Admin\.gemini\antigravity-ide\brain\b3629366-addf-4d36-86df-04aa9402ecf2\.user_uploaded\media_1793162953261.png";

            using var searchMat = Cv2.ImRead(searchPath, ImreadModes.Grayscale);
            using var templMat = Cv2.ImRead(templatePath, ImreadModes.Grayscale);

            Console.WriteLine("=============================================================");
            Console.WriteLine("THỬ NGHIỆM COSINE / CIRCULAR APODIZATION WINDOW TRÊN TEMPLATE");
            Console.WriteLine("=============================================================");

            // Tạo template có áp dụng Hann / Circular Window để triệt tiêu 4 góc vân nền nhôm
            using var windowedTempl = ApplyCircularWindow(templMat);

            // Kiểm tra điểm của cả 10 vị trí viên thuốc và vị trí giả (263, 555)
            // Vị trí 10 viên thuốc thật:
            var truePillLocations = new (double X, double Y, string Name)[]
            {
                (185.5, 111.0, "Viên 1 (Trái hàng 1)"),
                (296.5, 112.0, "Viên 2 (Phải hàng 1)"),
                (186.5, 215.0, "Viên 3 (Trái hàng 2)"),
                (296.5, 215.0, "Viên 4 (Phải hàng 2)"),
                (183.5, 316.0, "Viên 5 (Trái hàng 3)"),
                (300.5, 316.0, "Viên 6 (Phải hàng 3)"),
                (188.5, 419.0, "Viên 7 (Trái hàng 4)"),
                (301.5, 422.0, "Viên 8 (Phải hàng 4)"),
                (190.5, 525.0, "Viên 9 (Trái hàng 5)"),
                (308.5, 536.0, "Viên 10 (Phải hàng 5)")
            };

            // Vị trí giả:
            var falseLocations = new (double X, double Y, string Name)[]
            {
                (263.0, 555.5, "Vật thể giả mép chân vỉ nhôm"),
                (144.0, 555.5, "Vật thể giả mép chân vỉ nhôm trái")
            };

            using var resRaw = new Mat();
            Cv2.MatchTemplate(searchMat, templMat, resRaw, TemplateMatchModes.CCoeffNormed);

            using var resWin = new Mat();
            Cv2.MatchTemplate(searchMat, windowedTempl, resWin, TemplateMatchModes.CCoeffNormed);

            Console.WriteLine("\n--- SO SÁNH ĐIỂM SỐ GIỮA TEMPLATE GỐC VÀ TEMPLATE CÓ CỬA SỔ TRÒN ---");
            Console.WriteLine($"{"Tên vị trí",-30} | {"Gốc (Raw)",-12} | {"Cửa sổ tròn",-12}");
            Console.WriteLine(new string('-', 60));

            foreach (var loc in truePillLocations)
            {
                int x = (int)Math.Round(loc.X - templMat.Width / 2.0);
                int y = (int)Math.Round(loc.Y - templMat.Height / 2.0);
                float sRaw = GetScoreAt(resRaw, x, y);
                float sWin = GetScoreAt(resWin, x, y);
                Console.WriteLine($"{loc.Name,-30} | {sRaw * 100,8:F1}%   | {sWin * 100,8:F1}%");
            }

            Console.WriteLine(new string('-', 60));
            foreach (var loc in falseLocations)
            {
                int x = (int)Math.Round(loc.X - templMat.Width / 2.0);
                int y = (int)Math.Round(loc.Y - templMat.Height / 2.0);
                float sRaw = GetScoreAt(resRaw, x, y);
                float sWin = GetScoreAt(resWin, x, y);
                Console.WriteLine($"{loc.Name,-30} | {sRaw * 100,8:F1}%   | {sWin * 100,8:F1}%");
            }
        }

        private static float GetScoreAt(Mat res, int x, int y)
        {
            float max = 0;
            for (int dy = -3; dy <= 3; dy++)
            {
                for (int dx = -3; dx <= 3; dx++)
                {
                    int px = x + dx;
                    int py = y + dy;
                    if (px >= 0 && px < res.Width && py >= 0 && py < res.Height)
                    {
                        float v = res.At<float>(py, px);
                        if (v > max && !float.IsNaN(v) && !float.IsInfinity(v)) max = v;
                    }
                }
            }
            return max;
        }

        private static Mat ApplyCircularWindow(Mat src)
        {
            var dst = src.Clone();
            int w = src.Width;
            int h = src.Height;
            double cx = w / 2.0;
            double cy = h / 2.0;
            double rx = w * 0.46;
            double ry = h * 0.46;

            // Tính trung bình mức xám của vùng biên để hòa trộn mềm mại
            double meanBg = 0;
            int bgCount = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double nx = (x - cx) / rx;
                    double ny = (y - cy) / ry;
                    double d = Math.Sqrt(nx * nx + ny * ny);
                    if (d > 0.8)
                    {
                        meanBg += src.At<byte>(y, x);
                        bgCount++;
                    }
                }
            }
            meanBg = bgCount > 0 ? meanBg / bgCount : 128.0;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double nx = (x - cx) / rx;
                    double ny = (y - cy) / ry;
                    double d = Math.Sqrt(nx * nx + ny * ny);

                    byte val = src.At<byte>(y, x);
                    if (d <= 0.70)
                    {
                        // Giữ nguyên 100% thân viên thuốc
                        dst.Set(y, x, val);
                    }
                    else if (d <= 1.0)
                    {
                        // Giảm dần độ dốc (Tukey/Cosine transition)
                        double wgt = 0.5 * (1.0 + Math.Cos(Math.PI * (d - 0.70) / 0.30));
                        double blended = val * wgt + meanBg * (1.0 - wgt);
                        dst.Set(y, x, (byte)Math.Clamp(Math.Round(blended), 0, 255));
                    }
                    else
                    {
                        // Ngoài bán kính viên thuốc: hòa thành nền trung tính
                        dst.Set(y, x, (byte)Math.Clamp(Math.Round(meanBg), 0, 255));
                    }
                }
            }

            return dst;
        }
    }
}
