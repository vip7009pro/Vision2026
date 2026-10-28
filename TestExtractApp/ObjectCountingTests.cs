using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class ObjectCountingTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("🚀 [TEST] CHẠY BỘ KIỂM THỬ OBJECT COUNTING TOOL");
        Console.WriteLine("=======================================================");

        Test_ObjectCounting_RotatedObjects_CountAll();
        Test_ObjectCounting_SpecTolerance_Evaluation();
        Test_ObjectCounting_NMS_NoDuplicates();
        Test_ConditionEvaluator_ObjectCountingVariables();
        Test_RealUserBlisterPack_PillCounting();

        Console.WriteLine("=======================================================");
        Console.WriteLine("✅ TẤT CẢ TEST OBJECT COUNTING ĐÃ HOÀN THÀNH XUẤT SẮC!");
        Console.WriteLine("=======================================================\n");
    }

    /// <summary>
    /// Vẽ vật thể mẫu hình chữ L (bất đối xứng) trên ảnh
    /// </summary>
    private static void DrawTestPattern(Mat target, Point center, double angleDeg, Scalar color)
    {
        // Tạo polygon chữ L kích thước 50x50
        var pts = new Point2f[]
        {
            new Point2f(-20, -20),
            new Point2f(-20, 20),
            new Point2f(20, 20),
            new Point2f(20, 5),
            new Point2f(-5, 5),
            new Point2f(-5, -20)
        };

        var rad = angleDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);

        var transformed = new Point[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            var rx = pts[i].X * cos - pts[i].Y * sin;
            var ry = pts[i].X * sin + pts[i].Y * cos;
            transformed[i] = new Point((int)Math.Round(center.X + rx), (int)Math.Round(center.Y + ry));
        }

        Cv2.FillConvexPoly(target, transformed, color);
    }

    public static void Test_ObjectCounting_RotatedObjects_CountAll()
    {
        Console.WriteLine("--> Test 1: Đếm vật thể xoay phương hướng hỗn loạn (0°, 45°, 90°, 135°, -60°)...");

        // 1. Tạo template mẫu
        using var templateMat = new Mat(100, 100, MatType.CV_8UC1, Scalar.All(0));
        DrawTestPattern(templateMat, new Point(50, 50), 0, Scalar.All(255));

        // 2. Tạo ảnh tìm kiếm với 5 vật thể xoay các góc khác nhau
        using var searchMat = new Mat(700, 900, MatType.CV_8UC1, Scalar.All(0));
        var testAngles = new double[] { 0, 45, 90, 135, -60 };
        var testCenters = new Point[]
        {
            new Point(150, 150),
            new Point(450, 160),
            new Point(750, 200),
            new Point(250, 500),
            new Point(600, 520)
        };

        for (int i = 0; i < testAngles.Length; i++)
        {
            DrawTestPattern(searchMat, testCenters[i], testAngles[i], Scalar.All(255));
        }

        // 3. Khởi tạo định nghĩa ObjectCounting tool
        var def = new ObjectCountingDefinition
        {
            Name = "OC_Test1",
            Algorithm = ObjectCountingAlgorithm.NormalizedCorrelation,
            SearchRoi = new Roi { X = 0, Y = 0, Width = 900, Height = 700 },
            TemplateRoi = new Roi { X = 0, Y = 0, Width = 100, Height = 100 },
            NominalCount = 5,
            Tolerance = 0,
            MinScore = 0.70,
            MinAngle = -180,
            MaxAngle = 180,
            AngleStep = 5.0,
            MaxOverlap = 0.35
        };

        var searchRect = new Rect(0, 0, searchMat.Width, searchMat.Height);
        var (foundCount, pass, candidates) = ObjectCountingEngine.CountObjects(searchMat, templateMat, def, searchRect);

        Console.WriteLine($"    [Kết Quả] Đếm được: {foundCount}/{def.NominalCount} vật thể. Pass: {pass}");
        for (int i = 0; i < candidates.Count; i++)
        {
            var obj = candidates[i];
            Console.WriteLine($"    - Vật thể #{i + 1}: Tâm=({obj.X:F1}, {obj.Y:F1}), Góc={obj.AngleDeg:F1}°, Điểm={obj.Score:P1}");
        }

        if (foundCount != 5)
        {
            throw new Exception($"FAIL: Kỳ vọng đếm đủ 5 vật thể xoay phương hướng hỗn loạn, nhưng thực tế đếm được {foundCount}!");
        }

        if (!pass)
        {
            throw new Exception($"FAIL: Kỳ vọng Pass = true khi đếm đủ 5/5 vật thể!");
        }

        Console.WriteLine("    => PASS: Đếm đầy đủ 5 vật thể xoay đa hướng hỗn loạn không bỏ sót!");
    }

    public static void Test_ObjectCounting_SpecTolerance_Evaluation()
    {
        Console.WriteLine("--> Test 2: Kiểm tra đánh giá Spec và Dung sai (Tolerance, TolMinus, TolPlus)...");

        using var templateMat = new Mat(80, 80, MatType.CV_8UC1, Scalar.All(0));
        DrawTestPattern(templateMat, new Point(40, 40), 0, Scalar.All(255));

        using var searchMat = new Mat(400, 600, MatType.CV_8UC1, Scalar.All(0));
        // Vẽ đúng 4 vật thể
        DrawTestPattern(searchMat, new Point(100, 100), 10, Scalar.All(255));
        DrawTestPattern(searchMat, new Point(300, 100), 40, Scalar.All(255));
        DrawTestPattern(searchMat, new Point(150, 280), 80, Scalar.All(255));
        DrawTestPattern(searchMat, new Point(450, 260), -30, Scalar.All(255));

        var searchRect = new Rect(0, 0, 600, 400);

        // Case A: Nominal = 4, Tol = 0 -> PASS
        var defA = new ObjectCountingDefinition
        {
            Name = "OC_A",
            SearchRoi = new Roi { X = 0, Y = 0, Width = 600, Height = 400 },
            NominalCount = 4,
            Tolerance = 0,
            MinScore = 0.70,
            MinAngle = -90,
            MaxAngle = 90,
            AngleStep = 10.0
        };
        var resA = ObjectCountingEngine.CountObjects(searchMat, templateMat, defA, searchRect);
        if (resA.FoundCount != 4 || !resA.Pass)
            throw new Exception($"FAIL Case A: Found={resA.FoundCount}, Pass={resA.Pass}");

        // Case B: Nominal = 5, Tol = 0 -> NG (Thiếu vật thể)
        var defB = new ObjectCountingDefinition
        {
            Name = "OC_B",
            SearchRoi = new Roi { X = 0, Y = 0, Width = 600, Height = 400 },
            NominalCount = 5,
            Tolerance = 0,
            MinScore = 0.70,
            MinAngle = -90,
            MaxAngle = 90,
            AngleStep = 10.0
        };
        var resB = ObjectCountingEngine.CountObjects(searchMat, templateMat, defB, searchRect);
        if (resB.FoundCount != 4 || resB.Pass)
            throw new Exception($"FAIL Case B: Found={resB.FoundCount}, Pass={resB.Pass}");

        // Case C: Nominal = 5, Tol = 1 (Range 4..6) -> PASS
        var defC = new ObjectCountingDefinition
        {
            Name = "OC_C",
            SearchRoi = new Roi { X = 0, Y = 0, Width = 600, Height = 400 },
            NominalCount = 5,
            Tolerance = 1,
            ToleranceMinus = 1,
            TolerancePlus = 1,
            MinScore = 0.70,
            MinAngle = -90,
            MaxAngle = 90,
            AngleStep = 10.0
        };
        var resC = ObjectCountingEngine.CountObjects(searchMat, templateMat, defC, searchRect);
        if (resC.FoundCount != 4 || !resC.Pass)
            throw new Exception($"FAIL Case C: Found={resC.FoundCount}, Pass={resC.Pass}");

        // Case D: Nominal = 3, TolMinus = 0, TolPlus = 0 -> NG (Thừa vật thể)
        var defD = new ObjectCountingDefinition
        {
            Name = "OC_D",
            SearchRoi = new Roi { X = 0, Y = 0, Width = 600, Height = 400 },
            NominalCount = 3,
            Tolerance = 0,
            MinScore = 0.70,
            MinAngle = -90,
            MaxAngle = 90,
            AngleStep = 10.0
        };
        var resD = ObjectCountingEngine.CountObjects(searchMat, templateMat, defD, searchRect);
        if (resD.FoundCount != 4 || resD.Pass)
            throw new Exception($"FAIL Case D: Found={resD.FoundCount}, Pass={resD.Pass}");

        Console.WriteLine("    => PASS: Tất cả các trường hợp Spec và Dung sai đều đánh giá chính xác!");
    }

    public static void Test_ObjectCounting_NMS_NoDuplicates()
    {
        Console.WriteLine("--> Test 3: Kiểm tra Rotated NMS khử trùng lặp...");

        using var templateMat = new Mat(60, 60, MatType.CV_8UC1, Scalar.All(0));
        DrawTestPattern(templateMat, new Point(30, 30), 0, Scalar.All(255));

        // Chỉ vẽ 2 vật thể gần nhau nhưng không đè lên nhau
        using var searchMat = new Mat(400, 400, MatType.CV_8UC1, Scalar.All(0));
        DrawTestPattern(searchMat, new Point(120, 200), 25, Scalar.All(255));
        DrawTestPattern(searchMat, new Point(240, 200), -45, Scalar.All(255));

        var def = new ObjectCountingDefinition
        {
            Name = "OC_NMS",
            SearchRoi = new Roi { X = 0, Y = 0, Width = 400, Height = 400 },
            NominalCount = 2,
            Tolerance = 0,
            MinScore = 0.65,
            MinAngle = -180,
            MaxAngle = 180,
            AngleStep = 2.0, // Quét bước góc mịn 2 độ
            MaxOverlap = 0.3
        };

        var searchRect = new Rect(0, 0, 400, 400);
        var result = ObjectCountingEngine.CountObjects(searchMat, templateMat, def, searchRect);

        if (result.FoundCount != 2)
        {
            throw new Exception($"FAIL NMS: Kỳ vọng đúng 2 vật thể nhưng đếm ra {result.FoundCount} do trùng lặp!");
        }

        Console.WriteLine($"    [Kết Quả] Đếm đúng 2 vật thể: {result.FoundCount}/2, không bị đếm lặp lân cận.");
        Console.WriteLine("    => PASS: Non-Maximum Suppression hoạt động chuẩn xác!");
    }

    public static void Test_ConditionEvaluator_ObjectCountingVariables()
    {
        Console.WriteLine("--> Test 4: Kiểm tra biến ObjectCounting trong Condition Evaluator...");

        var run = new InspectionResult
        {
            Pass = true
        };

        run.ObjectCountings.Add(new ObjectCountingResult(
            Name: "OC1",
            FoundCount: 5,
            NominalCount: 5,
            MinCount: 5,
            MaxCount: 5,
            Pass: true,
            Objects: new List<DetectedObject>()
        ));

        var config = new VisionConfig
        {
            ObjectCountings = new List<ObjectCountingDefinition>
            {
                new ObjectCountingDefinition { Name = "OC1", NominalCount = 5, Tolerance = 0 }
            }
        };

        var vars = ConditionEvaluator.BuildVariableMap(run, config);

        bool eval1 = ConditionEvaluator.Evaluate("OC1.Count == 5", vars);
        bool eval2 = ConditionEvaluator.Evaluate("OC1.Pass == true", vars);
        bool eval3 = ConditionEvaluator.Evaluate("OC1.Nominal == 5", vars);
        bool eval4 = ConditionEvaluator.Evaluate("OC1.Count >= 4", vars);

        if (!eval1 || !eval2 || !eval3 || !eval4)
        {
            throw new Exception($"FAIL: Condition evaluation failed: eval1={eval1}, eval2={eval2}, eval3={eval3}, eval4={eval4}");
        }

        Console.WriteLine($"    [Evaluator] OC1.Count == 5 -> {eval1}");
        Console.WriteLine($"    [Evaluator] OC1.Pass == true -> {eval2}");
        Console.WriteLine($"    [Evaluator] OC1.Nominal == 5 -> {eval3}");
        Console.WriteLine("    => PASS: Tích hợp biến Condition Evaluator hoàn hảo!");
    }

    public static void Test_RealUserBlisterPack_PillCounting()
    {
        Console.WriteLine("--> Test 5: Kiểm tra đếm vỉ thuốc thực tế từ ảnh người dùng (10 viên thuốc)...");

        string searchPath = @"C:\Users\Admin\.gemini\antigravity-ide\brain\b3629366-addf-4d36-86df-04aa9402ecf2\.user_uploaded\media_1793162942034.png";
        string templatePath = @"C:\Users\Admin\.gemini\antigravity-ide\brain\b3629366-addf-4d36-86df-04aa9402ecf2\.user_uploaded\media_1793162953261.png";

        if (!File.Exists(searchPath) || !File.Exists(templatePath))
        {
            Console.WriteLine("    [BỎ QUA] Không tìm thấy ảnh vỉ thuốc trong thư mục brain uploads.");
            return;
        }

        using var searchMat = Cv2.ImRead(searchPath);
        using var templateMat = Cv2.ImRead(templatePath);

        var def = new ObjectCountingDefinition
        {
            Name = "PillCount",
            NominalCount = 10,
            Tolerance = 0,
            MinScore = 0.65,
            MinAngle = -180,
            MaxAngle = 180,
            AngleStep = 5.0,
            MaxOverlap = 0.30,
            Algorithm = ObjectCountingAlgorithm.NormalizedCorrelation
        };

        var searchRect = new Rect(0, 0, searchMat.Width, searchMat.Height);
        var res = ObjectCountingEngine.CountObjects(searchMat, templateMat, def, searchRect);

        Console.WriteLine($"    [Kết Quả] Đếm được: {res.FoundCount}/10 viên thuốc. Trạng thái Pass={res.Pass}");
        foreach (var pill in res.Objects)
        {
            Console.WriteLine($"       • Viên thuốc tại ({pill.X:F1}, {pill.Y:F1}), Góc={pill.AngleDeg:F1}°, Điểm={pill.Score * 100:F1}%");
        }

        if (res.FoundCount != 10)
        {
            throw new Exception($"FAIL: Kỳ vọng đúng 10 viên thuốc trên vỉ nhưng đếm ra {res.FoundCount}!");
        }

        if (!res.Pass)
        {
            throw new Exception("FAIL: Đã đếm đúng 10 viên thuốc nhưng Pass == false!");
        }

        Console.WriteLine("    => PASS: Đếm chính xác tuyệt đối 10/10 viên thuốc trên vỉ thuốc thực tế của người dùng!");
    }
}
