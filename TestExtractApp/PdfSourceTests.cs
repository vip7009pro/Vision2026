using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Docnet.Core;
using Docnet.Core.Models;
using OpenCvSharp;
using VisionInspectionApp.Application;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels;
using VisionInspectionApp.VisionEngine;

namespace TestExtractApp;

public static class PdfSourceTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=== [PDF SOURCE TESTS] ===");
        Test1_RenderPdfSampleToMat();
        Test2_PdfDocumentService_PageCountAndConversion();
        Test3_ToolEditorViewModel_PdfSourceIntegration();
        Test4_JobPipeline_RunWithPdfImageInput();
        Console.WriteLine("=== [ALL PDF SOURCE TESTS PASSED (100%)] ===\n");
    }

    private static string EnsureSamplePdf()
    {
        string pdfPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_sample.pdf");
        if (!File.Exists(pdfPath))
        {
            string rootPdf = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../test_sample.pdf");
            if (File.Exists(rootPdf))
            {
                File.Copy(rootPdf, pdfPath, true);
            }
        }

        if (!File.Exists(pdfPath))
        {
            byte[] pdfBytes = "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n3 0 obj<</Type/Page/MediaBox[0 0 600 400]/Parent 2 0 R/Resources<<>>/Contents 4 0 R>>endobj\n4 0 obj<</Length 55>>stream\n0 0 0 RG 2 w 50 50 500 300 re S 50 50 m 550 350 l S\nendstream\nendobj\nxref\n0 5\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \n0000000115 00000 n \n0000000216 00000 n \ntrailer<</Size 5/Root 1 0 R>>\nstartxref\n322\n%%EOF"u8.ToArray();
            File.WriteAllBytes(pdfPath, pdfBytes);
        }

        return pdfPath;
    }

    private static void Test1_RenderPdfSampleToMat()
    {
        Console.Write("Test 1: Render PDF sample to OpenCV Mat... ");
        string pdfPath = EnsureSamplePdf();

        using var library = DocLib.Instance;
        using var docReader = library.GetDocReader(pdfPath, new PageDimensions(1.0));
        int pageCount = docReader.GetPageCount();
        if (pageCount != 1) throw new Exception($"Expected 1 page, got {pageCount}");

        using var pageReader = docReader.GetPageReader(0);
        int width = pageReader.GetPageWidth();
        int height = pageReader.GetPageHeight();
        if (width != 600 || height != 400)
            throw new Exception($"Expected 600x400 at 1.0 scale (100%), got {width}x{height}");

        byte[] rawBytes = pageReader.GetImage();
        if (rawBytes.Length != width * height * 4)
            throw new Exception($"Unexpected byte length: {rawBytes.Length} vs {width * height * 4}");

        Console.WriteLine($"PASSED! (Extracted {width}x{height} raw BGRA pixels)");
    }

    private static void Test2_PdfDocumentService_PageCountAndConversion()
    {
        Console.Write("Test 2: PdfDocumentService White Background & 300 DPI High-Res... ");
        string pdfPath = EnsureSamplePdf();

        IPdfDocumentService service = new PdfDocumentService();
        int count = service.GetPageCount(pdfPath);
        if (count != 1) throw new Exception($"Expected 1 page, got {count}");

        // Kiểm tra nền trắng tại điểm (5, 5) - vùng không có nét vẽ
        using var mat = service.RenderPageToMat(pdfPath, 1, 1.0);
        if (mat == null || mat.Empty() || mat.Width != 600 || mat.Height != 400)
            throw new Exception($"RenderPageToMat failed: {mat?.Width}x{mat?.Height}");

        var bgPixel = mat.At<Vec3b>(5, 5);
        if (bgPixel.Item0 != 255 || bgPixel.Item1 != 255 || bgPixel.Item2 != 255)
            throw new Exception($"LỖI NỀN ĐEN: Pixel nền phải là màu trắng (255, 255, 255), thực tế nhận được BGR=({bgPixel.Item0}, {bgPixel.Item1}, {bgPixel.Item2})");

        // Kiểm tra kết xuất ở 300 DPI siêu nét
        double scale300Dpi = 300.0 / 72.0;
        using var mat300 = service.RenderPageToMat(pdfPath, 1, scale300Dpi);
        if (mat300.Width < 2400 || mat300.Height < 1600)
            throw new Exception($"300 DPI resolution expected >= 2400x1600, got {mat300.Width}x{mat300.Height}");

        var bgPixel300 = mat300.At<Vec3b>(5, 5);
        if (bgPixel300.Item0 != 255 || bgPixel300.Item1 != 255 || bgPixel300.Item2 != 255)
            throw new Exception($"LỖI NỀN ĐEN Ở 300 DPI: Pixel nền phải là màu trắng (255, 255, 255)");

        string testOutDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "TestPdfOutput");
        string imageFile = service.ConvertPdfToImageFile(pdfPath, 1, scale300Dpi, testOutDir);
        if (!File.Exists(imageFile))
            throw new Exception($"Image file was not created: {imageFile}");

        Console.WriteLine($"PASSED! (White BG verified BGR=255,255,255, 300 DPI: {mat300.Width}x{mat300.Height}px)");
    }

    private static void Test3_ToolEditorViewModel_PdfSourceIntegration()
    {
        Console.Write("Test 3: ToolEditorViewModel PDF Source properties & conversion... ");
        string pdfPath = EnsureSamplePdf();

        var config = new VisionConfig
        {
            ProductCode = "PDF_PRD_01",
            ProductName = "Bản Vẽ Test",
            ToolGraph = new ToolGraph
            {
                Nodes = { new ToolGraphNode { Id = "node_cam1", Type = "ImageSource", RefName = "CAM1" } }
            },
            ImageSources = new System.Collections.Generic.List<ImageSourceDefinition>
            {
                new ImageSourceDefinition
                {
                    Name = "CAM1",
                    SourceType = ImageSourceType.Pdf,
                    PdfPath = pdfPath,
                    PdfPageNumber = 1,
                    PdfScale = 300.0 / 72.0
                }
            }
        };

        var vm = new ToolEditorViewModel();
        vm.InitializeWithConfig(config);

        if (vm.ImageSource_SourceType != ImageSourceType.Pdf)
            throw new Exception($"SourceType must be Pdf, got {vm.ImageSource_SourceType}");

        if (!vm.ImageSource_IsPdf)
            throw new Exception("ImageSource_IsPdf should be true");

        if (vm.ImageSource_PdfPath != pdfPath)
            throw new Exception($"PdfPath mismatch: {vm.ImageSource_PdfPath}");

        if (vm.ImageSource_PdfTotalPages < 1)
            throw new Exception($"PdfTotalPages should be >= 1, got {vm.ImageSource_PdfTotalPages}");

        // Kích hoạt chuyển đổi PDF ra ảnh
        vm.ImageSource_ConvertPdfToImage();

        if (string.IsNullOrWhiteSpace(vm.ImageSource_PdfRenderedImagePath) || !File.Exists(vm.ImageSource_PdfRenderedImagePath))
            throw new Exception("ImageSource_PdfRenderedImagePath was not created or empty");

        if (!vm.ImageSource_HasPdfRenderedImage)
            throw new Exception("ImageSource_HasPdfRenderedImage should be true");

        // Kiểm tra _sharedImage đã nạp ảnh từ PDF ở độ nét cao
        using var snap = vm.SharedImageContext.GetSnapshot();
        if (snap == null || snap.Empty() || snap.Width < 2000 || snap.Height < 1500)
            throw new Exception($"_sharedImage did not receive High-Res PDF image: {snap?.Width}x{snap?.Height}");

        // Kiểm tra pixel nền trên SharedImage phải là màu trắng
        var px = snap.At<Vec3b>(5, 5);
        if (px.Item0 != 255 || px.Item1 != 255 || px.Item2 != 255)
            throw new Exception($"_sharedImage must have WHITE background, got BGR=({px.Item0},{px.Item1},{px.Item2})");

        Console.WriteLine($"PASSED! (SharedImage: {snap.Width}x{snap.Height}px, White Background=True)");
    }

    private static void Test4_JobPipeline_RunWithPdfImageInput()
    {
        Console.Write("Test 4: Full Job Flow running with PDF image input... ");
        string pdfPath = EnsureSamplePdf();

        var config = new VisionConfig
        {
            ProductCode = "PDF_TEST_FLOW",
            ProductName = "Bản Vẽ Kiểm Tra",
            ToolGraph = new ToolGraph
            {
                Nodes =
                {
                    new ToolGraphNode { Id = "node_cam1", Type = "ImageSource", RefName = "CAM1" },
                    new ToolGraphNode { Id = "node_out1", Type = "ImageOutput", RefName = "IMG_OUT1" }
                },
                Edges =
                {
                    new ToolGraphEdge { FromNodeId = "node_cam1", ToNodeId = "node_out1", FromPort = "Image", ToPort = "Image" }
                }
            },
            ImageSources = new System.Collections.Generic.List<ImageSourceDefinition>
            {
                new ImageSourceDefinition
                {
                    Name = "CAM1",
                    SourceType = ImageSourceType.Pdf,
                    PdfPath = pdfPath,
                    PdfPageNumber = 1,
                    PdfScale = 300.0 / 72.0
                }
            }
        };

        var vm = new ToolEditorViewModel();
        InjectField(vm, "_preprocessor", new ImagePreprocessor());
        InjectField(vm, "_lineDetector", new LineDetector());
        InjectField(vm, "_inspectionService", new InspectionService(
            new ImagePreprocessor(),
            new PatternMatcher(),
            new DistanceCalculator(),
            new LineDetector(),
            new DefectDetector()));

        vm.InitializeWithConfig(config);

        // Chuyển PDF ra ảnh
        vm.ImageSource_ConvertPdfToImage();

        // Kiểm tra nạp preview ảnh từ PDF
        var sourceDef = config.ImageSources[0];
        using var loadedMat = vm.LoadImageFromSourceForPreview(sourceDef);
        if (loadedMat == null || loadedMat.Empty() || loadedMat.Width < 2000 || loadedMat.Height < 1500)
            throw new Exception($"LoadImageFromSourceForPreview failed for PDF source: {loadedMat?.Width}x{loadedMat?.Height}");

        // Chạy Run Flow
        vm.OnRunOnceClicked();

        // Chờ kết quả bất đồng bộ hoàn tất
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (vm.LastResult == null && sw.ElapsedMilliseconds < 3000)
        {
            System.Threading.Thread.Sleep(50);
        }

        var result = vm.LastResult;
        if (result == null)
            throw new Exception("RunFlow did not produce LastResult with PDF image source");

        Console.WriteLine($"PASSED! (Run completed, Success={result.Pass})");
    }

    private static void InjectField(object target, string fieldName, object? value)
    {
        var type = target.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            type = type.BaseType;
        }

        if (field is null)
            throw new Exception($"InjectField: không tìm thấy field '{fieldName}'");

        field.SetValue(target, value);
    }
}
