using System;
using System.Diagnostics;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class MvpShapeMatch3AndInt8Tests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ❌ FAIL: {message}");
            Console.ResetColor();
            throw new Exception($"Assertion failed: {message}");
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✅ PASS: {message}");
        Console.ResetColor();
    }

    public static void RunAllTests()
    {
        Console.WriteLine("\n========================================================");
        Console.WriteLine("🎯 RUNNING MVPSHAPEMATCH3 & MVPSHAPEMATCHINT8 TESTS");
        Console.WriteLine("========================================================");

        // Tạo ảnh mẫu mô phỏng nhãn cuộn (Roll Label) với marking phức hợp:
        // Logo elip, viền chữ nhật, hình tròn lồng, và vạch định vị
        int imgW = 700;
        int imgH = 600;
        using var labelMat = new Mat(imgH, imgW, MatType.CV_8UC1, new Scalar(40));

        // Nền nhãn
        Cv2.Rectangle(labelMat, new Rect(50, 50, 600, 500), new Scalar(180), -1);
        Cv2.Rectangle(labelMat, new Rect(70, 70, 560, 460), new Scalar(230), -1);

        // Marking trung tâm (Logo + hình khối đặc trưng)
        int markX = 300;
        int markY = 280;
        Cv2.Rectangle(labelMat, new Rect(markX - 60, markY - 50, 120, 100), new Scalar(30), 3);
        Cv2.Circle(labelMat, new Point(markX, markY), 35, new Scalar(30), 2);
        Cv2.Ellipse(labelMat, new RotatedRect(new Point2f(markX, markY), new Size2f(80, 40), 30), new Scalar(30), 2);
        Cv2.Line(labelMat, new Point(markX - 50, markY), new Point(markX + 50, markY), new Scalar(30), 2);
        Cv2.Line(labelMat, new Point(markX, markY - 40), new Point(markX, markY + 40), new Scalar(30), 2);

        // Template ROI bao quanh marking
        var tplRoi = new Rect(markX - 80, markY - 70, 160, 140);
        using var tplMat = new Mat(labelMat, tplRoi).Clone();

        var searchRoi = new Roi { X = 100, Y = 80, Width = 500, Height = 440, Angle = 0 };
        var templateRoi = new Roi { X = tplRoi.X, Y = tplRoi.Y, Width = tplRoi.Width, Height = tplRoi.Height, Angle = 0 };

        var matcher = new OriginMatcher();

        // --- TEST 1: MvpShapeMatch3 Tọa độ chuẩn & Khớp góc 0° ---
        var def3 = new PointDefinition
        {
            Name = "Origin_Label_V3",
            OriginAlgorithm = OriginAlgorithm.MvpShapeMatch3,
            TemplateRoi = templateRoi,
            SearchRoi = searchRoi,
            MinAngle = -5.0,
            MaxAngle = 5.0,
            AngleStep = 0.5,
            MinScore = 0.6,
            MvpAutoThresh = true
        };

        var res3 = matcher.MatchWithRotation(labelMat, def3, tplMat, null, def3.MinAngle, def3.MaxAngle, def3.AngleStep);
        double errX3 = Math.Abs(res3.Position.X - markX);
        double errY3 = Math.Abs(res3.Position.Y - markY);
        Assert(res3.Score >= 0.95 && errX3 <= 1.0 && errY3 <= 1.0,
            $"Test 1: MvpShapeMatch3 góc 0° đạt Score={res3.Score:F4} >= 0.95, Tọa độ ({res3.Position.X:F2}, {res3.Position.Y:F2}) lệch ({errX3:F2}, {errY3:F2}) <= 1.0px");

        // --- TEST 2: MvpShapeMatch3 với góc xoay nhẹ +3° ---
        double testAngleDeg = 3.0;
        using var rotMat = new Mat();
        using var rotM = Cv2.GetRotationMatrix2D(new Point2f(markX, markY), -testAngleDeg, 1.0);
        Cv2.WarpAffine(labelMat, rotMat, rotM, labelMat.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);

        var resRot3 = matcher.MatchWithRotation(rotMat, def3, tplMat, null, def3.MinAngle, def3.MaxAngle, def3.AngleStep);
        double angleErr3 = Math.Abs(resRot3.AngleDeg - testAngleDeg);
        Assert(resRot3.Score >= 0.90 && angleErr3 <= 1.0,
            $"Test 2: MvpShapeMatch3 góc xoay +3° đạt Score={resRot3.Score:F4} >= 0.90, Angle={resRot3.AngleDeg:F2}° (lệch {angleErr3:F2}° <= 1.0°)");

        // --- TEST 3: MvpShapeMatchInt8 Tọa độ chuẩn & Khớp góc 0° ---
        var defInt8 = new PointDefinition
        {
            Name = "Origin_Label_Int8",
            OriginAlgorithm = OriginAlgorithm.MvpShapeMatchInt8,
            TemplateRoi = templateRoi,
            SearchRoi = searchRoi,
            MinAngle = -5.0,
            MaxAngle = 5.0,
            AngleStep = 0.5,
            MinScore = 0.6,
            MvpAutoThresh = true
        };

        var resInt8 = matcher.MatchWithRotation(labelMat, defInt8, tplMat, null, defInt8.MinAngle, defInt8.MaxAngle, defInt8.AngleStep);
        double errXInt8 = Math.Abs(resInt8.Position.X - markX);
        double errYInt8 = Math.Abs(resInt8.Position.Y - markY);
        Assert(resInt8.Score >= 0.90 && errXInt8 <= 1.5 && errYInt8 <= 1.5,
            $"Test 3: MvpShapeMatchInt8 góc 0° đạt Score={resInt8.Score:F4} >= 0.90, Tọa độ ({resInt8.Position.X:F2}, {resInt8.Position.Y:F2}) lệch ({errXInt8:F2}, {errYInt8:F2}) <= 1.5px");

        // --- TEST 4: MvpShapeMatchInt8 với góc xoay nhẹ -3° ---
        double testAngleDegNeg = -3.0;
        using var rotMatNeg = new Mat();
        using var rotMNeg = Cv2.GetRotationMatrix2D(new Point2f(markX, markY), -testAngleDegNeg, 1.0);
        Cv2.WarpAffine(labelMat, rotMatNeg, rotMNeg, labelMat.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);

        var resRotInt8 = matcher.MatchWithRotation(rotMatNeg, defInt8, tplMat, null, defInt8.MinAngle, defInt8.MaxAngle, defInt8.AngleStep);
        double angleErrInt8 = Math.Abs(resRotInt8.AngleDeg - testAngleDegNeg);
        Assert(resRotInt8.Score >= 0.88 && angleErrInt8 <= 1.0,
            $"Test 4: MvpShapeMatchInt8 góc xoay -3° đạt Score={resRotInt8.Score:F4} >= 0.88, Angle={resRotInt8.AngleDeg:F2}° (lệch {angleErrInt8:F2}° <= 1.0°)");

        // --- TEST 5: BENCHMARK TỐC ĐỘ V2 vs V3 vs INT8 ---
        Console.WriteLine("\n--- ⚡ TIẾN HÀNH BENCHMARK TỐC ĐỘ (MvpShapeMatch2 vs MvpShapeMatch3 vs MvpShapeMatchInt8) ---");

        var def2 = new PointDefinition
        {
            Name = "Origin_Label_V2",
            OriginAlgorithm = OriginAlgorithm.MvpShapeMatch2,
            TemplateRoi = templateRoi,
            SearchRoi = searchRoi,
            MinAngle = -5.0,
            MaxAngle = 5.0,
            AngleStep = 0.5,
            MinScore = 0.6,
            MvpAutoThresh = true
        };

        // Warm-up JIT & Caches
        for (int i = 0; i < 3; i++)
        {
            matcher.MatchWithRotation(labelMat, def2, tplMat, null, -5, 5, 0.5);
            matcher.MatchWithRotation(labelMat, def3, tplMat, null, -5, 5, 0.5);
            matcher.MatchWithRotation(labelMat, defInt8, tplMat, null, -5, 5, 0.5);
        }

        int benchIterations = 15;

        var sw2 = Stopwatch.StartNew();
        for (int i = 0; i < benchIterations; i++)
        {
            matcher.MatchWithRotation(labelMat, def2, tplMat, null, -5, 5, 0.5);
        }
        sw2.Stop();
        double msV2 = sw2.Elapsed.TotalMilliseconds / benchIterations;

        var sw3 = Stopwatch.StartNew();
        for (int i = 0; i < benchIterations; i++)
        {
            matcher.MatchWithRotation(labelMat, def3, tplMat, null, -5, 5, 0.5);
        }
        sw3.Stop();
        double msV3 = sw3.Elapsed.TotalMilliseconds / benchIterations;

        var swInt8 = Stopwatch.StartNew();
        for (int i = 0; i < benchIterations; i++)
        {
            matcher.MatchWithRotation(labelMat, defInt8, tplMat, null, -5, 5, 0.5);
        }
        swInt8.Stop();
        double msInt8 = swInt8.Elapsed.TotalMilliseconds / benchIterations;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  [BENCHMARK KẾT QUẢ]:");
        Console.WriteLine($"    • MvpShapeMatch2 (Gốc):     {msV2:F2} ms / lần");
        Console.WriteLine($"    • MvpShapeMatch3 (Mới):     {msV3:F2} ms / lần  (Nhanh hơn {(msV2 / Math.Max(0.1, msV3)):F1}x)");
        Console.WriteLine($"    • MvpShapeMatchInt8 (Mới):  {msInt8:F2} ms / lần  (Nhanh hơn {(msV2 / Math.Max(0.1, msInt8)):F1}x)");
        Console.ResetColor();

        Assert(msV3 < msV2, $"Test 5: MvpShapeMatch3 ({msV3:F2}ms) phải nhanh hơn MvpShapeMatch2 ({msV2:F2}ms)");
        Assert(msInt8 < msV2, $"Test 6: MvpShapeMatchInt8 ({msInt8:F2}ms) phải nhanh hơn MvpShapeMatch2 ({msV2:F2}ms)");

        Console.WriteLine("\n✅ TOÀN BỘ CÁC BÀI KIỂM THỬ MVPSHAPEMATCH3 VÀ MVPSHAPEMATCHINT8 ĐÃ VƯỢT QUA 100%!\n");
    }
}
