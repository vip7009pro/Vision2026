using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class PreprocessAndImageOutputTests
{
    public static void RunTests()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine(" RUNNING SUITE: PREPROCESS & IMAGE OUTPUT ENHANCEMENTS");
        Console.WriteLine("==========================================================");
        Console.ResetColor();

        Test_01_LegacyPreprocessRegression();
        Test_02_Thresholding_Otsu_Triangle_Sauvola();
        Test_03_Denoising_Median_Bilateral();
        Test_04_ColorChannel_Gamma_AutoContrast_Invert();
        Test_05_Gradients_Sobel_Scharr_Laplacian_MorphGradient();
        Test_06_Morphology_Extended_Shapes_Types_Iterations();
        Test_07_AutoEdge_WhiteOnWhite_CandidateEvaluation();
        Test_08_ImageOutput_ResultTableOverlay();
        Test_09_JsonSerialization_And_BackwardCompatibility();
        Test_10_MemoryLeak_SoakLoop();
        Test_11_AsyncImageSaver_PreProcessBeforeSave_NonBlocking();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("----------------------------------------------------------");
        Console.WriteLine(" ALL PREPROCESS & IMAGE OUTPUT TESTS PASSED (11/11)!");
        Console.WriteLine("----------------------------------------------------------\n");
        Console.ResetColor();
    }

    private static void Test_01_LegacyPreprocessRegression()
    {
        Console.Write("[Test 01] Legacy Preprocess Regression Test... ");
        using var src = new Mat(100, 100, MatType.CV_8UC3, new Scalar(128, 128, 128));
        var settings = new PreprocessSettings
        {
            UseGray = true,
            UseGaussianBlur = true,
            BlurKernel = 3,
            UseThreshold = true,
            ThresholdType = PreprocessThresholdType.Binary,
            ThresholdValue = 100,
            UseMorphology = true,
            MorphType = PreprocessMorphType.Close,
            MorphKernelSize = 3,
            MorphIterations = 1
        };

        var preprocessor = new ImagePreprocessor();
        using var dst = preprocessor.Run(src, settings);
        Assert(dst != null, "Output should not be null");
        Assert(dst!.Width == 100 && dst.Height == 100, "Dimensions should match");
        Assert(dst.Channels() == 1, "Should be single channel grayscale/binary");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED");
        Console.ResetColor();
    }

    private static void Test_02_Thresholding_Otsu_Triangle_Sauvola()
    {
        Console.Write("[Test 02] Thresholding (Otsu, Triangle, Sauvola)... ");
        using var src = new Mat(100, 100, MatType.CV_8UC1, new Scalar(50));
        Cv2.Rectangle(src, new Rect(25, 25, 50, 50), new Scalar(200), -1);

        var preprocessor = new ImagePreprocessor();

        // Otsu
        var otsuSettings = new PreprocessSettings
        {
            UseThreshold = true,
            ThresholdType = PreprocessThresholdType.Otsu
        };
        using var otsuDst = preprocessor.Run(src, otsuSettings);
        Assert(otsuDst != null && otsuDst.Channels() == 1, "Otsu output valid");
        Assert(otsuDst!.Get<byte>(50, 50) == 255, "Otsu center bright");
        Assert(otsuDst.Get<byte>(5, 5) == 0, "Otsu corner dark");

        // Triangle
        var triSettings = new PreprocessSettings
        {
            UseThreshold = true,
            ThresholdType = PreprocessThresholdType.Triangle
        };
        using var triDst = preprocessor.Run(src, triSettings);
        Assert(triDst != null && triDst.Channels() == 1, "Triangle output valid");

        // Sauvola
        var sauvolaSettings = new PreprocessSettings
        {
            UseThreshold = true,
            ThresholdType = PreprocessThresholdType.Sauvola,
            SauvolaK = 0.2,
            SauvolaR = 128.0,
            MaskWidth = 15,
            MaskHeight = 15
        };
        using var sauvolaDst = preprocessor.Run(src, sauvolaSettings);
        Assert(sauvolaDst != null && sauvolaDst.Channels() == 1, "Sauvola output valid");
        Assert(sauvolaDst!.Get<byte>(50, 50) == 255, "Sauvola center bright");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED");
        Console.ResetColor();
    }

    private static void Test_03_Denoising_Median_Bilateral()
    {
        Console.Write("[Test 03] Denoising (MedianBlur & BilateralFilter)... ");
        using var src = new Mat(64, 64, MatType.CV_8UC1, new Scalar(100));
        src.Set<byte>(10, 10, 255);
        src.Set<byte>(20, 20, 0);

        var preprocessor = new ImagePreprocessor();

        // Median Blur
        var medianSettings = new PreprocessSettings
        {
            UseMedianBlur = true,
            MedianKernel = 3
        };
        using var medianDst = preprocessor.Run(src, medianSettings);
        Assert(medianDst != null, "Median output valid");
        Assert(medianDst!.Get<byte>(10, 10) == 100, "Salt noise removed by median filter");

        // Bilateral Filter
        var bilateralSettings = new PreprocessSettings
        {
            UseBilateralFilter = true,
            BilateralDiameter = 5,
            BilateralSigmaColor = 50,
            BilateralSigmaSpace = 50
        };
        using var bilateralDst = preprocessor.Run(src, bilateralSettings);
        Assert(bilateralDst != null && bilateralDst.Width == 64, "Bilateral output valid");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED");
        Console.ResetColor();
    }

    private static void Test_04_ColorChannel_Gamma_AutoContrast_Invert()
    {
        Console.Write("[Test 04] ColorChannel, Gamma, AutoContrast & Invert... ");
        using var colorSrc = new Mat(40, 40, MatType.CV_8UC3, new Scalar(20, 100, 200));

        var preprocessor = new ImagePreprocessor();

        // Color Channel Red
        var rChannelSettings = new PreprocessSettings
        {
            ColorChannel = PreprocessColorChannel.Red
        };
        using var rDst = preprocessor.Run(colorSrc, rChannelSettings);
        Assert(rDst != null && rDst.Channels() == 1, "Red channel is single channel");
        Assert(Math.Abs(rDst!.Get<byte>(10, 10) - 200) <= 2, "Red channel value ~ 200");

        // Gamma < 1.0 (Làm sáng)
        using var graySrc = new Mat(40, 40, MatType.CV_8UC1, new Scalar(50));
        var gammaSettings = new PreprocessSettings
        {
            UseGamma = true,
            GammaValue = 0.5 // Brighten
        };
        using var gammaDst = preprocessor.Run(graySrc, gammaSettings);
        Assert(gammaDst != null, "Gamma output valid");
        Assert(gammaDst!.Get<byte>(10, 10) > 50, "Gamma 0.5 should brighten the image");

        // Invert
        var invertSettings = new PreprocessSettings
        {
            InvertColors = true
        };
        using var invertDst = preprocessor.Run(graySrc, invertSettings);
        Assert(invertDst != null && invertDst!.Get<byte>(10, 10) == (255 - 50), "Inverted 50 should be 205");

        // Auto Contrast
        using var lowContrast = new Mat(40, 40, MatType.CV_8UC1, new Scalar(100));
        lowContrast.Set<byte>(0, 0, 90);
        lowContrast.Set<byte>(39, 39, 110);
        var autoContrastSettings = new PreprocessSettings
        {
            UseAutoContrast = true
        };
        using var contrastDst = preprocessor.Run(lowContrast, autoContrastSettings);
        Assert(contrastDst != null, "Auto contrast valid");
        Cv2.MinMaxLoc(contrastDst!, out double minVal, out double maxVal);
        Assert(minVal == 0 && maxVal == 255, "AutoContrast should stretch 90-110 to 0-255");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED");
        Console.ResetColor();
    }

    private static void Test_05_Gradients_Sobel_Scharr_Laplacian_MorphGradient()
    {
        Console.Write("[Test 05] Gradients (Sobel, Scharr, Laplacian, MorphGradient)... ");
        using var src = new Mat(50, 50, MatType.CV_8UC1, new Scalar(50));
        Cv2.Rectangle(src, new Rect(10, 10, 30, 30), new Scalar(200), -1);

        var preprocessor = new ImagePreprocessor();

        foreach (var gType in new[] { PreprocessGradientType.Sobel, PreprocessGradientType.Scharr, PreprocessGradientType.Laplacian, PreprocessGradientType.MorphGradient })
        {
            var gSettings = new PreprocessSettings
            {
                GradientType = gType,
                GradientKernel = 3,
                GradientScale = 1.0
            };
            using var gDst = preprocessor.Run(src, gSettings);
            Assert(gDst != null && gDst.Channels() == 1, $"Gradient {gType} output valid");
            byte edgeVal = gDst!.Get<byte>(10, 20);
            Assert(edgeVal > 0, $"Gradient {gType} detected edge");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED");
        Console.ResetColor();
    }

    private static void Test_06_Morphology_Extended_Shapes_Types_Iterations()
    {
        Console.Write("[Test 06] Morphology (Shapes, Types, Iterations)... ");
        using var src = new Mat(50, 50, MatType.CV_8UC1, new Scalar(0));
        Cv2.Rectangle(src, new Rect(15, 15, 20, 20), new Scalar(255), -1);

        var preprocessor = new ImagePreprocessor();

        foreach (var shape in new[] { PreprocessMorphShape.Rect, PreprocessMorphShape.Cross, PreprocessMorphShape.Ellipse })
        {
            foreach (var mType in new[] { PreprocessMorphType.Open, PreprocessMorphType.Close, PreprocessMorphType.TopHat, PreprocessMorphType.BlackHat })
            {
                var mSettings = new PreprocessSettings
                {
                    UseMorphology = true,
                    MorphShape = shape,
                    MorphType = mType,
                    MorphKernelSize = 3,
                    MorphIterations = 2
                };
                using var mDst = preprocessor.Run(src, mSettings);
                Assert(mDst != null && mDst.Width == 50, $"Morph {shape}-{mType} output valid");
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED");
        Console.ResetColor();
    }

    private static void Test_07_AutoEdge_WhiteOnWhite_CandidateEvaluation()
    {
        Console.Write("[Test 07] Auto Edge Candidate Evaluation (White-on-White)... ");
        // Nền trắng 250, chi tiết trắng nhạt 240 (tương phản cực thấp)
        using var src = new Mat(80, 80, MatType.CV_8UC1, new Scalar(250));
        Cv2.Rectangle(src, new Rect(20, 20, 40, 40), new Scalar(240), -1);

        var preprocessor = new ImagePreprocessor();

        var autoEdgeSettings = new PreprocessSettings
        {
            UseAutoEdge = true,
            AutoEdgeMethod = AutoEdgeMethod.Ensemble,
            AutoEdgeMinConfidence = 0.05,
            AutoEdgeInvert = false
        };

        using var edgeDst = preprocessor.Run(src, autoEdgeSettings);
        Assert(edgeDst != null, "AutoEdge output valid");
        Assert(edgeDst!.Channels() == 1, "AutoEdge output single channel");

        // Kiểm tra AutoEdgeInvert
        autoEdgeSettings.AutoEdgeInvert = true;
        using var invertedEdge = preprocessor.Run(src, autoEdgeSettings);
        Assert(invertedEdge != null, "AutoEdge invert valid");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED");
        Console.ResetColor();
    }

    private static void Test_08_ImageOutput_ResultTableOverlay()
    {
        Console.Write("[Test 08] ImageOutput Result Table Overlay... ");
        using var img = new Mat(720, 1280, MatType.CV_8UC3, new Scalar(40, 40, 40));

        var inspectionResult = new InspectionResult
        {
            Pass = false
        };

        // Bổ sung các kết quả đo đạc phong phú
        inspectionResult.Distances.Add(new DistanceCheckResult("Dist_Width", "P1", "P2", 50.25, 50.00, 0.5, 0.5, true));
        inspectionResult.Distances.Add(new DistanceCheckResult("Dist_Height", "P3", "P4", 31.80, 30.00, 0.5, 0.5, false));
        // Thêm kết quả đo Caliper và dung sai dài 79.70 (+5.00/-5.00)
        inspectionResult.Calipers.Add(new CaliperResult("CAL1", true, new List<CaliperEdgePoint> { new CaliperEdgePoint(10, 10, 50) }, new Point2d(10, 10), new Point2d(20, 20), 50.0));
        inspectionResult.Distances.Add(new DistanceCheckResult("CAL1_Dis", "CAL1", "CAL2", 79.74, 79.70, 5.0, 5.0, true));
        inspectionResult.Angles.Add(new AngleResult("Angle_Corner", "L1", "L2", 90.15, 90.0, 1.0, 1.0, true, true, new Point2d(100, 100), new Point2d(1, 0), new Point2d(0, 1)));
        inspectionResult.CircleFinders.Add(new CircleFinderResult("Hole_1", true, new Point2d(300, 300), 15.5, 0.98));
        inspectionResult.BlobDetections.Add(new BlobDetectionResult("Defect_Blobs", 0, new List<BlobInfo>(), true, MaxAllowedBlobs: 2));
        inspectionResult.CodeDetections.Add(new CodeDetectionResult("QR_OQC", true, "SN-9988776655", new Rect(50, 50, 100, 100), 0.0, true, "SN-*"));

        var config = new VisionConfig
        {
            ProductName = "PCB_MAIN_V2",
            ProductCode = "PRD-2026-X1"
        };

        var outputDef = new ImageOutputDefinition
        {
            Name = "OUT1",
            IncludeOverlay = true,
            ShowResultTable = true
        };

        using var testMatWithTable = img.Clone();
        InspectionService.BurnOverlaysToMat(testMatWithTable, config, inspectionResult, outputDef);

        // Kiểm tra vùng góc dưới bên phải đã được vẽ bảng (pixel không còn là giá trị xám 40 ban đầu)
        int checkX = testMatWithTable.Width - 50;
        int checkY = testMatWithTable.Height - 50;
        var pixel = testMatWithTable.Get<Vec3b>(checkY, checkX);
        bool modified = (pixel.Item0 != 40 || pixel.Item1 != 40 || pixel.Item2 != 40);
        Assert(modified, "Result table was successfully drawn on bottom-right corner");

        // Kiểm tra độ rộng bảng 680px: Tọa độ x = Width - 600 cũng nằm trong phạm vi bảng (đã được làm tối/blend)
        int checkTableWidthX = testMatWithTable.Width - 600;
        var pixelInner = testMatWithTable.Get<Vec3b>(checkY, checkTableWidthX);
        bool innerModified = (pixelInner.Item0 != 40 || pixelInner.Item1 != 40 || pixelInner.Item2 != 40);
        Assert(innerModified, "Result table width expanded to 680px covers check point (Width - 600px)");

        // Khi ShowResultTable = false thì không vẽ bảng kết quả
        outputDef.ShowResultTable = false;
        outputDef.IncludeOverlay = false;
        using var testMatWithoutTable = img.Clone();
        InspectionService.BurnOverlaysToMat(testMatWithoutTable, config, inspectionResult, outputDef);
        var pixelNoTable = testMatWithoutTable.Get<Vec3b>(checkY, checkX);
        Assert(pixelNoTable.Item0 == 40 && pixelNoTable.Item1 == 40 && pixelNoTable.Item2 == 40, "No table drawn when ShowResultTable is false");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED");
        Console.ResetColor();
    }

    private static void Test_09_JsonSerialization_And_BackwardCompatibility()
    {
        Console.Write("[Test 09] JSON Serialization & Backward Compatibility... ");
        
        // 1. Serialize new settings
        var newSettings = new PreprocessSettings
        {
            ThresholdType = PreprocessThresholdType.Sauvola,
            SauvolaK = 0.25,
            SauvolaR = 120.0,
            UseMedianBlur = true,
            MedianKernel = 5,
            UseBilateralFilter = true,
            BilateralDiameter = 7,
            GradientType = PreprocessGradientType.Sobel,
            ColorChannel = PreprocessColorChannel.Green,
            UseGamma = true,
            GammaValue = 1.8,
            UseAutoContrast = true,
            InvertColors = true,
            UseAutoEdge = true,
            AutoEdgeMethod = AutoEdgeMethod.Ensemble,
            AutoEdgeMinConfidence = 0.35,
            AutoEdgeInvert = true,
            MorphShape = PreprocessMorphShape.Cross,
            MorphType = PreprocessMorphType.TopHat,
            MorphKernelSize = 5,
            MorphIterations = 3
        };

        string json = JsonSerializer.Serialize(newSettings);
        var deserialized = JsonSerializer.Deserialize<PreprocessSettings>(json);
        Assert(deserialized != null, "Deserialized not null");
        Assert(deserialized!.ThresholdType == PreprocessThresholdType.Sauvola, "ThresholdType match");
        Assert(Math.Abs(deserialized.SauvolaK - 0.25) < 1e-6, "SauvolaK match");
        Assert(deserialized.UseMedianBlur && deserialized.MedianKernel == 5, "Median match");
        Assert(deserialized.UseAutoEdge && deserialized.AutoEdgeMethod == AutoEdgeMethod.Ensemble, "AutoEdge match");
        Assert(deserialized.MorphShape == PreprocessMorphShape.Cross && deserialized.MorphIterations == 3, "Morph match");

        // 2. Backward compatibility: Deserializing legacy JSON
        string legacyJson = @"{ ""UseGray"": true, ""UseThreshold"": true, ""ThresholdValue"": 128 }";
        var legacyDeserialized = JsonSerializer.Deserialize<PreprocessSettings>(legacyJson);
        Assert(legacyDeserialized != null, "Legacy not null");
        Assert(legacyDeserialized!.UseGray == true, "Legacy UseGray preserved");
        Assert(legacyDeserialized.ThresholdType == PreprocessThresholdType.Binary, "Default threshold is Binary");
        Assert(legacyDeserialized.UseMedianBlur == false, "Default UseMedianBlur is false");
        Assert(legacyDeserialized.UseAutoEdge == false, "Default UseAutoEdge is false");

        // 3. ImageOutputDefinition ShowResultTable backward compatibility
        string legacyImageOutputJson = @"{ ""Name"": ""OUT1"", ""IncludeOverlay"": true }";
        var legacyOutput = JsonSerializer.Deserialize<ImageOutputDefinition>(legacyImageOutputJson);
        Assert(legacyOutput != null && legacyOutput.ShowResultTable == true, "Default ShowResultTable should be true for backward compatibility");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED");
        Console.ResetColor();
    }

    private static void Test_10_MemoryLeak_SoakLoop()
    {
        Console.Write("[Test 10] Memory Leak Soak Loop (100 iterations)... ");
        using var testSrc = new Mat(200, 200, MatType.CV_8UC3, new Scalar(100, 150, 200));

        var fullPipelineSettings = new PreprocessSettings
        {
            ColorChannel = PreprocessColorChannel.All,
            UseGamma = true,
            GammaValue = 1.2,
            UseAutoContrast = true,
            UseMedianBlur = true,
            MedianKernel = 3,
            UseBilateralFilter = true,
            BilateralDiameter = 3,
            GradientType = PreprocessGradientType.Sobel,
            UseMorphology = true,
            MorphShape = PreprocessMorphShape.Rect,
            MorphType = PreprocessMorphType.Close,
            MorphKernelSize = 3,
            MorphIterations = 1,
            UseAutoEdge = true,
            AutoEdgeMethod = AutoEdgeMethod.ScharrOtsu
        };

        var preprocessor = new ImagePreprocessor();
        long memBefore = GC.GetTotalMemory(true);

        for (int i = 0; i < 100; i++)
        {
            using var processed = preprocessor.Run(testSrc, fullPipelineSettings);
            Assert(processed != null, $"Iteration {i} returned valid Mat");
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memAfter = GC.GetTotalMemory(true);
        long memDiff = memAfter - memBefore;

        Assert(memDiff < 5 * 1024 * 1024, $"Memory diff should be small, actual: {memDiff / 1024} KB");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("PASSED");
        Console.ResetColor();
    }

    private static void Test_11_AsyncImageSaver_PreProcessBeforeSave_NonBlocking()
    {
        Console.Write("[Test 11] AsyncImageSaver PreProcessBeforeSave (Non-Blocking)... ");
        var tempFolder = Path.Combine(Path.GetTempPath(), "VisionTest_AsyncSaver_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        var tempFile = Path.Combine(tempFolder, "async_test.jpg");

        try
        {
            var saver = VisionInspectionApp.Application.Services.AsyncImageSaver.Instance;
            using var sampleMat = new Mat(400, 600, MatType.CV_8UC3, new Scalar(120, 120, 120));

            bool preProcessExecuted = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Enqueue kèm delegate preProcess
            bool enqueued = saver.Enqueue(sampleMat.Clone(), tempFile, "TestOutput", m =>
            {
                preProcessExecuted = true;
                // Vẽ chữ nhật mẫu đánh dấu
                Cv2.Rectangle(m, new Rect(10, 10, 50, 50), new Scalar(0, 255, 0), -1);
                return m;
            });

            sw.Stop();

            Assert(enqueued, "Enqueue must return true");
            Assert(sw.ElapsedMilliseconds < 50, $"Enqueue must be non-blocking (< 50ms), actual: {sw.ElapsedMilliseconds}ms");

            // Chờ worker background xử lý xong
            saver.FlushAsync(5000).GetAwaiter().GetResult();

            for (int i = 0; i < 30 && !File.Exists(tempFile); i++)
            {
                System.Threading.Thread.Sleep(50);
            }

            Assert(File.Exists(tempFile), "Saved file must exist on disk");
            Assert(preProcessExecuted, "PreProcessBeforeSave delegate must be executed on background worker");

            // Đọc lại ảnh đã lưu để xác nhận nội dung đã được xử lý bởi delegate
            using var savedMat = Cv2.ImRead(tempFile);
            Assert(savedMat != null && !savedMat.Empty(), "Saved image must be valid and readable");
            var markPixel = savedMat.Get<Vec3b>(20, 20);
            Assert(markPixel.Item1 > 200, "Green marker drawn by preProcess must be present in saved file");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASSED");
            Console.ResetColor();
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, true);
                }
            }
            catch { }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ASSERTION FAILED]: {message}");
            Console.ResetColor();
            throw new Exception($"Test assertion failed: {message}");
        }
    }
}
