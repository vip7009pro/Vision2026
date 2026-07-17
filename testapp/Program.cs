using System;
using OpenCvSharp;

class Program {
    static void Main() {
        var w = 100;
        var h = 100;
        var pad = 4;
        
        using var H = Mat.Eye(3, 3, MatType.CV_64FC1).ToMat();
        H.Set<double>(0, 2, 50.0); // Translate by 50
        H.Set<double>(1, 2, 50.0);
        
        using var T_inv = Mat.Eye(3, 3, MatType.CV_64FC1).ToMat();
        T_inv.Set<double>(0, 2, -pad);
        T_inv.Set<double>(1, 2, -pad);
        
        using var H_warped = new Mat();
        Cv2.Gemm(H, T_inv, 1.0, new Mat(), 0.0, H_warped);
        
        Console.WriteLine("H_warped:");
        Console.WriteLine(H_warped.At<double>(0, 2));
        Console.WriteLine(H_warped.At<double>(1, 2));
    }
}
