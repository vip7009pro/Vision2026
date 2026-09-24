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
        Test5_PdfMatchingCamera_20MP_And_CustomPresets();
        Test6_PdfPanAndRotationFeatures();
        Test7_PdfOriginTrainTemplatePreview();
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

    private static void Test5_PdfMatchingCamera_20MP_And_CustomPresets()
    {
        Console.Write("Test 5: PDF Match Camera 1:1 (20MP 5472x3648 & Custom Sensor)... ");
        string pdfPath = EnsureSamplePdf();
        IPdfDocumentService service = new PdfDocumentService();

        // 1. Kiểm tra RenderPageMatchingCamera với canvas 20MP (5472 x 3648) khi bản vẽ nằm gọn trong cảm biến
        using (var matCanvas = service.RenderPageMatchingCamera(
            pdfPath,
            pageNumber: 1,
            pixelsPerMm: 5.0,
            fitToCameraCanvas: true,
            cameraWidth: 5472,
            cameraHeight: 3648,
            alignment: "Center"))
        {
            if (matCanvas.Width != 5472 || matCanvas.Height != 3648)
                throw new Exception($"Expected 20MP canvas 5472x3648, got {matCanvas.Width}x{matCanvas.Height}");

            // 4 góc của khung hình camera phải là màu trắng tinh khiết (255, 255, 255)
            var pTopLeft = matCanvas.At<Vec3b>(10, 10);
            var pTopRight = matCanvas.At<Vec3b>(10, 5460);
            var pBottomLeft = matCanvas.At<Vec3b>(3630, 10);
            var pBottomRight = matCanvas.At<Vec3b>(3630, 5460);

            if (pTopLeft.Item0 != 255 || pTopLeft.Item1 != 255 || pTopLeft.Item2 != 255 ||
                pTopRight.Item0 != 255 || pTopRight.Item1 != 255 || pTopRight.Item2 != 255 ||
                pBottomLeft.Item0 != 255 || pBottomLeft.Item1 != 255 || pBottomLeft.Item2 != 255 ||
                pBottomRight.Item0 != 255 || pBottomRight.Item1 != 255 || pBottomRight.Item2 != 255)
            {
                throw new Exception($"Lỗi: Nền ngoài khung hình cảm biến 20MP không phải màu trắng tinh khiết! (TL={pTopLeft}, TR={pTopRight}, BL={pBottomLeft}, BR={pBottomRight})");
            }
        }

        // 1b. Kiểm tra với tỉ lệ quang học chuẩn Camera 20MP (34.2 px/mm ~ FOV 160mm)
        using (var mat20Mp = service.RenderPageMatchingCamera(
            pdfPath,
            pageNumber: 1,
            pixelsPerMm: 34.2,
            fitToCameraCanvas: true,
            cameraWidth: 5472,
            cameraHeight: 3648,
            alignment: "Center"))
        {
            if (mat20Mp.Width != 5472 || mat20Mp.Height != 3648)
                throw new Exception($"Expected 20MP canvas 5472x3648, got {mat20Mp.Width}x{mat20Mp.Height}");
        }

        // 2. Kiểm tra RenderPageMatchingCamera với Camera 12MP tùy biến (4096 x 3000)
        using (var mat12Mp = service.RenderPageMatchingCamera(
            pdfPath,
            pageNumber: 1,
            pixelsPerMm: 25.0,
            fitToCameraCanvas: true,
            cameraWidth: 4096,
            cameraHeight: 3000,
            alignment: "Center"))
        {
            if (mat12Mp.Width != 4096 || mat12Mp.Height != 3000)
                throw new Exception($"Expected 12MP canvas 4096x3000, got {mat12Mp.Width}x{mat12Mp.Height}");
        }

        // 3. Kiểm tra tích hợp trong ToolEditorViewModel với chế độ MatchCamera1to1
        var config = new VisionConfig
        {
            ProductCode = "TEST_MATCH_CAM",
            ProductName = "Test Camera Match 1:1",
            PixelsPerMm = 34.2,
            ImageSources = new System.Collections.Generic.List<ImageSourceDefinition>
            {
                new ImageSourceDefinition
                {
                    Name = "CAM_20MP",
                    SourceType = ImageSourceType.Pdf,
                    PdfPath = pdfPath,
                    PdfPageNumber = 1,
                    PdfRenderMode = PdfRenderMode.MatchCamera1to1,
                    PdfFitToCameraCanvas = true,
                    PdfCameraWidth = 5472,
                    PdfCameraHeight = 3648
                }
            }
        };

        var vm = new ToolEditorViewModel();
        vm.InitializeWithConfig(config);

        // Kiểm tra thuộc tính ViewModel
        if (!vm.ImageSource_PdfIsMatchCamera)
            throw new Exception("ImageSource_PdfIsMatchCamera should be true");
        if (vm.ImageSource_PdfCameraWidth != 5472 || vm.ImageSource_PdfCameraHeight != 3648)
            throw new Exception($"ViewModel camera size incorrect: {vm.ImageSource_PdfCameraWidth}x{vm.ImageSource_PdfCameraHeight}");

        // Chuyển đổi PDF theo chế độ khớp camera
        vm.ImageSource_ConvertPdfToImage();

        var sourceDef = config.ImageSources[0];
        using var loadedMat = vm.LoadImageFromSourceForPreview(sourceDef);
        if (loadedMat == null || loadedMat.Empty() || loadedMat.Width != 5472 || loadedMat.Height != 3648)
            throw new Exception($"Expected preview to load 5472x3648 image, got {loadedMat?.Width}x{loadedMat?.Height}");

        Console.WriteLine($"PASSED! (Generated 5472x3648 20MP Canvas, Optical PPM={config.PixelsPerMm:F1}, Background=White)");
    }

    private static void Test6_PdfPanAndRotationFeatures()
    {
        Console.Write("Test 6: PDF Manual PixelsPerMm, Rotation (90deg) & Pan Offset... ");
        string pdfPath = EnsureSamplePdf();
        IPdfDocumentService service = new PdfDocumentService();

        // 1. Kiểm tra xoay 90 độ (Vertical sang Horizontal)
        using (var normalMat = service.RenderPageMatchingCamera(
            pdfPath,
            pageNumber: 1,
            pixelsPerMm: 1.0,
            fitToCameraCanvas: false,
            rotationDegrees: 0))
        using (var rotatedMat = service.RenderPageMatchingCamera(
            pdfPath,
            pageNumber: 1,
            pixelsPerMm: 1.0,
            fitToCameraCanvas: false,
            rotationDegrees: 90))
        {
            if (rotatedMat.Width != normalMat.Height || rotatedMat.Height != normalMat.Width)
                throw new Exception($"Expected 90-degree rotated dimensions {normalMat.Height}x{normalMat.Width}, got {rotatedMat.Width}x{rotatedMat.Height}");
        }

        // 2. Kiểm tra Căn Lề TopCenter kết hợp Pan Offset Y
        using (var canvasMat = service.RenderPageMatchingCamera(
            pdfPath,
            pageNumber: 1,
            pixelsPerMm: 2.0,
            fitToCameraCanvas: true,
            cameraWidth: 2000,
            cameraHeight: 2000,
            alignment: "TopCenter",
            offsetX: 50,
            offsetY: 100,
            rotationDegrees: 0))
        {
            if (canvasMat.Width != 2000 || canvasMat.Height != 2000)
                throw new Exception($"Expected canvas 2000x2000, got {canvasMat.Width}x{canvasMat.Height}");
        }

        // 3. Kiểm tra ViewModel: Nhập tay PixelsPerMm, Pan và Xoay
        var config = new VisionConfig
        {
            ProductCode = "TEST_PAN_ROT",
            ProductName = "Test Pan & Rotation",
            PixelsPerMm = 10.0,
            ImageSources = new System.Collections.Generic.List<ImageSourceDefinition>
            {
                new ImageSourceDefinition
                {
                    Name = "CAM_TEST",
                    SourceType = ImageSourceType.Pdf,
                    PdfPath = pdfPath,
                    PdfPageNumber = 1,
                    PdfRenderMode = PdfRenderMode.MatchCamera1to1,
                    PdfFitToCameraCanvas = true,
                    PdfCameraWidth = 3000,
                    PdfCameraHeight = 2000,
                    PdfPixelsPerMm = 28.5,
                    PdfRotation = 0,
                    PdfCanvasAlignment = "TopCenter",
                    PdfCanvasOffsetX = 0,
                    PdfCanvasOffsetY = 0
                }
            }
        };

        var vm = new ToolEditorViewModel();
        vm.InitializeWithConfig(config);

        // Kiểm tra PixelsPerMm nhập tay
        if (Math.Abs(vm.ImageSource_PdfPixelsPerMm - 28.5) > 0.001)
            throw new Exception($"Expected PixelsPerMm 28.5, got {vm.ImageSource_PdfPixelsPerMm}");

        if (!vm.ImageSource_PdfEquivalentDpiText.Contains("724 DPI"))
            throw new Exception($"Expected DPI ~724, got {vm.ImageSource_PdfEquivalentDpiText}");

        // Kiểm tra nút Áp dụng vào Job
        vm.ImageSource_PdfApplyPixelsPerMmToJob();
        if (Math.Abs(config.PixelsPerMm - 28.5) > 0.001)
            throw new Exception($"Job PixelsPerMm not updated: {config.PixelsPerMm}");

        // Kiểm tra nút Xoay 90°
        vm.ImageSource_PdfRotate90();
        if (vm.ImageSource_PdfRotation != 90)
            throw new Exception($"Expected rotation 90, got {vm.ImageSource_PdfRotation}");

        // Kiểm tra các thao tác Pan
        vm.ImageSource_PdfPanUp();
        if (vm.ImageSource_PdfCanvasOffsetY != -200)
            throw new Exception($"Expected Pan Y -200, got {vm.ImageSource_PdfCanvasOffsetY}");

        vm.ImageSource_PdfPanRight();
        if (vm.ImageSource_PdfCanvasOffsetX != 200)
            throw new Exception($"Expected Pan X 200, got {vm.ImageSource_PdfCanvasOffsetX}");

        vm.ImageSource_PdfPanReset();
        if (vm.ImageSource_PdfCanvasOffsetX != 0 || vm.ImageSource_PdfCanvasOffsetY != 0)
            throw new Exception($"PanReset failed: X={vm.ImageSource_PdfCanvasOffsetX}, Y={vm.ImageSource_PdfCanvasOffsetY}");

        Console.WriteLine("PASSED! (Rotation=90°, Pan Offset & Manual PixelsPerMm verified)");
    }

    private static void Test7_PdfOriginTrainTemplatePreview()
    {
        Console.Write("Test 7: PDF ImageSource Origin Train Template & Preview (Avoid 'Chưa lưu template')... ");
        string pdfPath = EnsureSamplePdf();

        var config = new VisionConfig
        {
            ProductCode = "TEST_PDF_ORIGIN_TRAIN",
            ProductName = "Test PDF Origin Train",
            PixelsPerMm = 10.0,
            Origin = new PointDefinition
            {
                Name = "Origin",
                OriginAlgorithm = OriginAlgorithm.MvpShapeMatch2,
                TemplateRoi = new Roi { X = 50, Y = 50, Width = 120, Height = 100 },
                SearchRoi = new Roi { X = 0, Y = 0, Width = 1000, Height = 1000 }
            },
            ImageSources = new System.Collections.Generic.List<ImageSourceDefinition>
            {
                new ImageSourceDefinition
                {
                    Name = "CAM1",
                    SourceType = ImageSourceType.Pdf,
                    PdfPath = pdfPath,
                    PdfPageNumber = 1,
                    PdfRenderMode = PdfRenderMode.MatchCamera1to1,
                    PdfFitToCameraCanvas = true,
                    PdfCameraWidth = 1280,
                    PdfCameraHeight = 960,
                    PdfPixelsPerMm = 10.0
                }
            },
            ToolGraph = new ToolGraph
            {
                Nodes = new System.Collections.Generic.List<ToolGraphNode>
                {
                    new ToolGraphNode { Id = "node_src", Type = "ImageSource", RefName = "CAM1" },
                    new ToolGraphNode { Id = "node_origin", Type = "Origin", RefName = "Origin" }
                }
            }
        };

        var vm = new ToolEditorViewModel();
        vm.InitializeWithConfig(config);

        // Ban đầu chưa train/lưu template -> Origin_TemplatePreviewImage phải là null
        vm.RefreshOriginTemplatePreview();
        if (vm.Origin_TemplatePreviewImage != null)
            throw new Exception("Initially Origin_TemplatePreviewImage should be null before training");

        // 1. Nạp ảnh từ nguồn PDF
        var imgSource = config.ImageSources[0];
        using var loadedMat = vm.LoadImageFromSourceForPreview(imgSource);
        if (loadedMat == null || loadedMat.Empty())
            throw new Exception("Failed to load PDF preview image");

        // 2. Mở Train Template với workingDir từ EnsureCurrentTempWorkingDir
        var workingDir = vm.EnsureCurrentTempWorkingDir();
        using (var trainVm = new OriginTrainViewModel(loadedMat, config.Origin, workingDir))
        {
            trainVm.UpdateRoi(60, 60, 150, 120);
            trainVm.OkCommand.Execute(null);
        }

        // 3. Sau khi bấm OK, gọi RefreshOriginTemplatePreview
        vm.RefreshOriginTemplatePreview();

        // 4. Assert: TemplatePreviewImage PHẢI KHÁC NULL (hiển thị ảnh mẫu thay vì 'Chưa lưu template')
        if (vm.Origin_TemplatePreviewImage == null)
            throw new Exception("FAILED: Origin_TemplatePreviewImage is still null ('Chưa lưu template') after train OK!");

        if (string.IsNullOrWhiteSpace(config.Origin.TemplateImageFile) || !File.Exists(config.Origin.TemplateImageFile))
            throw new Exception($"FAILED: Template file does not exist on disk: {config.Origin.TemplateImageFile}");

        // 5. Kiểm tra ResolveTemplatePath tìm thấy file
        var resolved = vm.ResolveTemplatePath(config.Origin.TemplateImageFile, "origin.png", "origin*.png");
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
            throw new Exception($"ResolveTemplatePath failed to find template: {resolved}");

        Console.WriteLine("PASSED! (Origin template trained from PDF & displayed in Preview successfully)");
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
