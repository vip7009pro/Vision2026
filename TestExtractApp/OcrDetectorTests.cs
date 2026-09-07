using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class OcrDetectorTests
{
    private static InspectionService CreateInspectionService()
    {
        var pre = new ImagePreprocessor();
        var matcher = new PatternMatcher();
        var dist = new DistanceCalculator();
        var line = new LineDetector();
        var defect = new DefectDetector();
        return new InspectionService(pre, matcher, dist, line, defect);
    }

    public static void RunTests()
    {
        Console.WriteLine("=== [TEST SUITE] Industrial OCR Tool (AI & Non-AI) Tests ===");

        TestNonAiOcrRecognitionAndSpeed();
        TestDotMatrixConnector();
        TestMatchingModesAndSpecs();
        TestAiFallbackWhenModelMissing();
        TestIntegrationPipeline();
        TestUserCaseLineModeDarkBackground();
        TestCharacterFontTrainingMvs();

        Console.WriteLine("=== [PASSED] All Industrial OCR Tool Tests Completed Successfully! ===");
    }

    private static void TestCharacterFontTrainingMvs()
    {
        Console.WriteLine("--> Test 7: MVS Character Font Training & Custom Teaching Recognition");

        // Tạo ảnh nền tối chữ sáng
        using var img = new Mat(90, 400, MatType.CV_8UC3, new Scalar(45, 45, 45));
        Cv2.PutText(img, "Line Mode", new Point(25, 55), HersheyFonts.HersheySimplex, 1.2, new Scalar(220, 220, 220), 2, LineTypes.AntiAlias);

        var ocrDef = new OcrDefinition
        {
            Name = "OCR_Trained",
            SearchRoi = new Roi { X = 10, Y = 10, Width = 380, Height = 75 },
            EngineMode = OcrEngineMode.HeuristicFast,
            MatchingMode = OcrMatchingMode.ExactMatch,
            ExpectedText = "Line Mode",
            BinarizeMethod = OcrBinarizeMethod.Otsu
        };

        // 1. Thực hiện Dạy ký tự (Character Font Training chuẩn MVS)
        var trainedList = OcrDetector.TeachCharacters(img, ocrDef, labelSequence: "Line Mode");
        Console.WriteLine($"    [Training]: Đã học thành công {trainedList.Count} ký tự mẫu: {string.Join(", ", trainedList.Select(x => $"'{x.Character}'"))}");

        if (trainedList.Count < 8)
            throw new Exception($"Kỳ vọng học đủ 8 ký tự của 'Line Mode' nhưng chỉ học được {trainedList.Count}!");

        ocrDef.TrainedCharacters = trainedList;

        // 2. Chạy nhận diện với thư viện ký tự đã học
        var res = OcrDetector.Detect(img, ocrDef, new Point2d(0, 0), new Point2d(0, 0), 0.0);
        Console.WriteLine($"    [Inference With Trained Font]: Text='{res.RecognizedText}', Conf={res.Confidence:P1}, Pass={res.Pass}");

        foreach (var c in res.Characters)
        {
            Console.WriteLine($"       Char: '{c.Character}', Conf: {c.Confidence:P1}");
        }

        if (res.RecognizedText != "Line Mode")
            throw new Exception($"Kỳ vọng nhận diện đúng 100% 'Line Mode' với font đã học, nhưng nhận được '{res.RecognizedText}'!");

        if (res.Confidence < 0.85)
            throw new Exception($"Độ tin cậy với font đã học phải >= 85%, thực tế: {res.Confidence:P1}!");

        if (!res.Pass)
            throw new Exception("Kết quả kiểm tra với font đã học phải PASS!");
    }

    private static void TestUserCaseLineModeDarkBackground()
    {
        Console.WriteLine("--> Test 6: User Case 'Line Mode' on Dark Background with Invert Image & Separator Line");

        // Tạo ảnh nền xám tối (#2d2d30 ~ BGR 45, 45, 45) kích thước 350x80
        using var img = new Mat(80, 350, MatType.CV_8UC3, new Scalar(45, 45, 45));

        // Vẽ chữ 'Line Mode' màu xám sáng (BGR 210, 210, 210)
        Cv2.PutText(img, "Line Mode", new Point(20, 50), HersheyFonts.HersheySimplex, 1.2, new Scalar(210, 210, 210), 2, LineTypes.AntiAlias);

        // Vẽ đường kẻ ngang viền bảng ở mép dưới (y=78)
        Cv2.Line(img, new Point(0, 78), new Point(350, 78), new Scalar(70, 70, 70), 1);

        // Trường hợp 1: Cấu hình đúng hệt thông số người dùng gửi trong ảnh 2
        var ocrDefUser = new OcrDefinition
        {
            Name = "OCR_LineMode_User",
            SearchRoi = new Roi { X = 5, Y = 10, Width = 330, Height = 68 },
            EngineMode = OcrEngineMode.HeuristicFast,
            MatchingMode = OcrMatchingMode.AnyText,
            BinarizeMethod = OcrBinarizeMethod.Otsu, // Người dùng chọn Otsu
            ExpectedText = "Line Mode",
            CharWhitelist = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-/. :", // Chuỗi cũ trong Job người dùng
            InvertImage = false,
            MinConfidence = 0.2
        };

        var res1 = OcrDetector.Detect(img, ocrDefUser, new Point2d(0, 0), new Point2d(0, 0), 0.0);
        Console.WriteLine($"    User Config Case: Text='{res1.RecognizedText}', Found={res1.Found}, Conf={res1.Confidence:P1}");
        foreach (var c in res1.Characters)
        {
            Console.WriteLine($"       Char: '{c.Character}', Box: [{c.BoundingBox.X},{c.BoundingBox.Y},{c.BoundingBox.Width},{c.BoundingBox.Height}], Conf: {c.Confidence:P1}");
        }

        if (!res1.Found || string.IsNullOrWhiteSpace(res1.RecognizedText))
            throw new Exception($"User Config Case: Không đọc được chữ! Text='{res1.RecognizedText}'");
    }

    private static void TestNonAiOcrRecognitionAndSpeed()
    {
        Console.WriteLine("--> Test 1: Non-AI Heuristic OCR Recognition & Speed (<10ms)");

        // Tạo ảnh 600x200 nền trắng
        using var img = new Mat(200, 600, MatType.CV_8UC3, Scalar.All(255));
        // Vẽ chữ "ABC 123" màu đen
        Cv2.PutText(img, "ABC 123", new Point(50, 120), HersheyFonts.HersheySimplex, 2.0, Scalar.All(0), 3, LineTypes.AntiAlias);

        var ocrDef = new OcrDefinition
        {
            Name = "OCR_Test_1",
            SearchRoi = new Roi { X = 30, Y = 30, Width = 540, Height = 140 },
            EngineMode = OcrEngineMode.HeuristicFast,
            MatchingMode = OcrMatchingMode.AnyText,
            BinarizeMethod = OcrBinarizeMethod.Otsu,
            MinConfidence = 0.4,
            MinCharArea = 20,
            MaxCharArea = 10000
        };

        var sw = Stopwatch.StartNew();
        var res = OcrDetector.Detect(img, ocrDef, new Point2d(0, 0), new Point2d(0, 0), 0.0);
        sw.Stop();

        Console.WriteLine($"    Recognized: '{res.RecognizedText}', Confidence: {res.Confidence:P1}, Elapsed: {sw.ElapsedMilliseconds}ms, Found: {res.Found}, Pass: {res.Pass}");

        if (!res.Found)
            throw new Exception("OCR không tìm thấy ký tự!");

        if (res.RecognizedText.Length == 0)
            throw new Exception("OCR chuỗi nhận diện rỗng!");

        if (sw.ElapsedMilliseconds > 50)
            Console.WriteLine($"    [Warning] Cold run took {sw.ElapsedMilliseconds}ms (font cache init)");

        // Chạy lần 2 (Hot cache)
        sw.Restart();
        var res2 = OcrDetector.Detect(img, ocrDef, new Point2d(0, 0), new Point2d(0, 0), 0.0);
        sw.Stop();
        Console.WriteLine($"    Hot run elapsed: {sw.ElapsedMilliseconds}ms (<10ms expected)");

        if (sw.ElapsedMilliseconds > 35)
            throw new Exception($"Hot run OCR quá chậm ({sw.ElapsedMilliseconds}ms > 35ms)!");
    }

    private static void TestDotMatrixConnector()
    {
        Console.WriteLine("--> Test 2: Dot-Matrix (In kim) Morphology Connector");

        // Tạo ảnh nền trắng có các chấm tròn cách nhau giả lập in kim "H"
        using var img = new Mat(200, 300, MatType.CV_8UC3, Scalar.All(255));
        
        // Vẽ cột chấm dọc rời rạc (cột 1: x = 110, cột 2: x = 150)
        for (int y = 60; y <= 130; y += 10)
        {
            Cv2.Circle(img, new Point(110, y), 3, Scalar.All(0), -1);
            Cv2.Circle(img, new Point(150, y), 3, Scalar.All(0), -1);
        }
        // Nét ngang ở giữa y=95
        for (int x = 110; x <= 150; x += 10)
        {
            Cv2.Circle(img, new Point(x, 95), 3, Scalar.All(0), -1);
        }

        // Test không bật connector: Các chấm rời rạc bị lọc bỏ hoặc tách thành nhiều blob nhỏ
        var ocrNoDot = new OcrDefinition
        {
            Name = "OCR_NoDot",
            SearchRoi = new Roi { X = 80, Y = 30, Width = 120, Height = 140 },
            EngineMode = OcrEngineMode.HeuristicFast,
            EnableDotMatrixConnector = false,
            MinCharArea = 200 // Diện tích 1 chấm pi*3^2 ≈ 28 < 200 nên sẽ bị loại nếu không nối
        };

        var resNoDot = OcrDetector.Detect(img, ocrNoDot, new Point2d(0, 0), new Point2d(0, 0), 0.0);
        Console.WriteLine($"    Without DotMatrix Connector: BoundingBoxes={resNoDot.BoundingBoxes.Count}, Found={resNoDot.Found}");

        // Test có bật connector: Các chấm được kết nối thành 1 ký tự lớn
        var ocrWithDot = new OcrDefinition
        {
            Name = "OCR_WithDot",
            SearchRoi = new Roi { X = 80, Y = 30, Width = 120, Height = 140 },
            EngineMode = OcrEngineMode.HeuristicFast,
            EnableDotMatrixConnector = true,
            DotMatrixKernelSize = 9,
            MinCharArea = 200
        };

        var resWithDot = OcrDetector.Detect(img, ocrWithDot, new Point2d(0, 0), new Point2d(0, 0), 0.0);
        Console.WriteLine($"    With DotMatrix Connector: BoundingBoxes={resWithDot.BoundingBoxes.Count}, Text='{resWithDot.RecognizedText}', Found={resWithDot.Found}");

        if (!resWithDot.Found)
            throw new Exception("Dot-Matrix Connector không kết nối được các chấm in kim!");
    }

    private static void TestMatchingModesAndSpecs()
    {
        Console.WriteLine("--> Test 3: Spec Verification & Matching Modes (ExactMatch, Regex, Contains)");

        using var img = new Mat(200, 600, MatType.CV_8UC3, Scalar.All(255));
        Cv2.PutText(img, "LOT-9988", new Point(50, 120), HersheyFonts.HersheySimplex, 2.0, Scalar.All(0), 3, LineTypes.AntiAlias);

        // Mode 1: RegexPattern match
        var ocrRegexPass = new OcrDefinition
        {
            Name = "OCR_RegexPass",
            SearchRoi = new Roi { X = 30, Y = 30, Width = 540, Height = 140 },
            EngineMode = OcrEngineMode.HeuristicFast,
            MatchingMode = OcrMatchingMode.RegexPattern,
            RegexPattern = @"^LOT-[0-9]{4}$"
        };
        var resRegexPass = OcrDetector.Detect(img, ocrRegexPass, new Point2d(0, 0), new Point2d(0, 0), 0.0);
        Console.WriteLine($"    Regex Pattern Match: Text='{resRegexPass.RecognizedText}', Pass={resRegexPass.Pass}");
        foreach (var c in resRegexPass.Characters)
        {
            Console.WriteLine($"       Char: '{c.Character}', Box: [{c.BoundingBox.X},{c.BoundingBox.Y},{c.BoundingBox.Width},{c.BoundingBox.Height}], Conf: {c.Confidence:P1}");
        }
        if (!resRegexPass.Pass)
            throw new Exception("Regex pattern match phải PASS!");

        // Mode 2: RegexPattern fail
        var ocrRegexFail = new OcrDefinition
        {
            Name = "OCR_RegexFail",
            SearchRoi = new Roi { X = 30, Y = 30, Width = 540, Height = 140 },
            EngineMode = OcrEngineMode.HeuristicFast,
            MatchingMode = OcrMatchingMode.RegexPattern,
            RegexPattern = @"^SN-[0-9]+$"
        };
        var resRegexFail = OcrDetector.Detect(img, ocrRegexFail, new Point2d(0, 0), new Point2d(0, 0), 0.0);
        Console.WriteLine($"    Regex Pattern Mismatch: Text='{resRegexFail.RecognizedText}', Pass={resRegexFail.Pass}");
        if (resRegexFail.Pass)
            throw new Exception("Regex pattern mismatch phải FAIL!");

        // Mode 3: Contains match
        var ocrContains = new OcrDefinition
        {
            Name = "OCR_Contains",
            SearchRoi = new Roi { X = 30, Y = 30, Width = 540, Height = 140 },
            EngineMode = OcrEngineMode.HeuristicFast,
            MatchingMode = OcrMatchingMode.Contains,
            ExpectedText = "9988"
        };
        var resContains = OcrDetector.Detect(img, ocrContains, new Point2d(0, 0), new Point2d(0, 0), 0.0);
        Console.WriteLine($"    Contains Mode: Text='{resContains.RecognizedText}', Expected='9988', Pass={resContains.Pass}");
        if (!resContains.Pass)
            throw new Exception("Contains mode '9988' phải PASS!");
    }

    private static void TestAiFallbackWhenModelMissing()
    {
        Console.WriteLine("--> Test 4: AI Engine Fallback gracefully when ONNX model is missing");

        using var img = new Mat(200, 400, MatType.CV_8UC3, Scalar.All(255));
        Cv2.PutText(img, "TEST", new Point(50, 120), HersheyFonts.HersheySimplex, 2.0, Scalar.All(0), 3, LineTypes.AntiAlias);

        var ocrAiDef = new OcrDefinition
        {
            Name = "OCR_AI_Test",
            SearchRoi = new Roi { X = 20, Y = 20, Width = 360, Height = 160 },
            EngineMode = OcrEngineMode.DeepLearningOnnx,
            OnnxModelPath = "non_existing_model_path_12345.onnx",
            MatchingMode = OcrMatchingMode.AnyText
        };

        // Phải chạy trơn tru, không throw exception crash app, tự fallback sang Non-AI
        var res = OcrDetector.Detect(img, ocrAiDef, new Point2d(0, 0), new Point2d(0, 0), 0.0);
        Console.WriteLine($"    AI Fallback Result: Text='{res.RecognizedText}', Message='{res.Message}', Found={res.Found}");

        if (!res.Found)
            throw new Exception("Fallback sang Non-AI phải nhận diện được chữ 'TEST'!");
    }

    private static void TestIntegrationPipeline()
    {
        Console.WriteLine("--> Test 5: Full Inspection Pipeline Integration");

        using var img = new Mat(300, 600, MatType.CV_8UC3, Scalar.All(255));
        Cv2.PutText(img, "DATE 2026", new Point(50, 150), HersheyFonts.HersheySimplex, 2.0, Scalar.All(0), 3, LineTypes.AntiAlias);

        var config = new VisionConfig();
        config.Ocrs.Add(new OcrDefinition
        {
            Name = "OCR_ExpDate",
            SearchRoi = new Roi { X = 30, Y = 50, Width = 520, Height = 180 },
            EngineMode = OcrEngineMode.HeuristicFast,
            MatchingMode = OcrMatchingMode.Contains,
            ExpectedText = "2026"
        });

        var svc = CreateInspectionService();
        var result = svc.Inspect(img, config);

        Console.WriteLine($"    Pipeline Result: Pass={result.Pass}, Ocrs count={result.Ocrs.Count}");
        if (result.Ocrs.Count != 1)
            throw new Exception($"Kỳ vọng 1 kết quả OCR nhưng nhận {result.Ocrs.Count}!");

        var ocrRes = result.Ocrs[0];
        Console.WriteLine($"    OCR '{ocrRes.Name}': Text='{ocrRes.RecognizedText}', Pass={ocrRes.Pass}, Conf={ocrRes.Confidence:P1}");

        if (!ocrRes.Pass)
            throw new Exception("OCR trong pipeline phải PASS theo tiêu chuẩn Contains '2026'!");

        if (!result.Pass)
            throw new Exception("Tổng thể pipeline result.Pass phải là TRUE!");
    }
}
