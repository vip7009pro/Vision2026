using System;
using OpenCvSharp;
class Program {
    static void Main() {
        try {
            var pts = new Point2d[] { new Point2d(10, 10) };
            var h = new Mat(3, 3, MatType.CV_64F);
            h.Set<double>(0,0, 1); h.Set<double>(1,1, 1); h.Set<double>(2,2, 1);
            var res = Cv2.PerspectiveTransform(pts, h);
            Console.WriteLine($"Success: {res[0].X}, {res[0].Y}");
        } catch (Exception ex) {
            Console.WriteLine(ex.ToString());
        }
    }
}
