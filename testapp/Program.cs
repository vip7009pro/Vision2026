using System;
using OpenCvSharp;

class Program {
    static void Main() {
        var center = new Point2f(50, 50);
        var angle = 30.0; // degrees
        var m = Cv2.GetRotationMatrix2D(center, angle, 1.0);
        
        var pt = new Point2f(100, 50); // right of center
        
        // warpAffine equivalent for a single point:
        var x = m.At<double>(0, 0) * pt.X + m.At<double>(0, 1) * pt.Y + m.At<double>(0, 2);
        var y = m.At<double>(1, 0) * pt.X + m.At<double>(1, 1) * pt.Y + m.At<double>(1, 2);
        
        Console.WriteLine($"Cv2 Rotate {angle} deg: x={x:F2}, y={y:F2}");
        
        // Manual standard rotation:
        var rad = angle * Math.PI / 180.0;
        var dx = pt.X - center.X;
        var dy = pt.Y - center.Y;
        var manualX = center.X + dx * Math.Cos(rad) - dy * Math.Sin(rad);
        var manualY = center.Y + dx * Math.Sin(rad) + dy * Math.Cos(rad);
        
        Console.WriteLine($"Manual Rotate {angle} deg: x={manualX:F2}, y={manualY:F2}");
    }
}
