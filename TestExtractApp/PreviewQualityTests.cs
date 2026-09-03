using System;
using System.Text.Json;
using OpenCvSharp;
using VisionInspectionApp.UI.Services;

namespace TestExtractApp;

public static class PreviewQualityTests
{
    public static void RunTests()
    {
        Console.WriteLine("\n=================================================");
        Console.WriteLine("🖼️ RUNNING ORIGINAL VS DOWNSCALED PREVIEW TESTS");
        Console.WriteLine("=================================================");

        Test_OriginalQualityPreview_FullResolutionVsDownscaled();
        Test_GlobalAppSettings_UseOriginalQualityPreview_Serialization();
        Test_WriteableBitmapRenderer_FullResolutionVsDownscaled();

        Console.WriteLine("✅ ALL ORIGINAL VS DOWNSCALED PREVIEW TESTS PASSED!");
        Console.WriteLine("=================================================\n");
    }

    private static void Test_OriginalQualityPreview_FullResolutionVsDownscaled()
    {
        Console.WriteLine("▶ Running Test_OriginalQualityPreview_FullResolutionVsDownscaled...");
        
        using var testMat = new Mat(2160, 3840, MatType.CV_8UC3, new Scalar(100, 150, 200));

        // 1. Chế độ giảm chất lượng / tối ưu hiệu năng (Mặc định: UseOriginalQualityPreview = false)
        MatExtensions.UseOriginalQualityPreview = false;
        var downscaledBmp = testMat.ToBitmapSourceForDisplay(1280, 720);
        
        if (downscaledBmp == null)
            throw new Exception("downscaledBmp is null");
        if (downscaledBmp.PixelWidth > 1280 || downscaledBmp.PixelHeight > 720)
            throw new Exception($"Expected downscaled dimensions <= 1280x720, but got {downscaledBmp.PixelWidth}x{downscaledBmp.PixelHeight}");
        
        if (!downscaledBmp.TryGetSourcePixelSize(out var srcW, out var srcH) || srcW != 3840 || srcH != 2160)
            throw new Exception($"DisplaySourceMetadata failed: expected 3840x2160, got {srcW}x{srcH}");
        
        Console.WriteLine($"  ✓ Downscaled mode verified: 3840x2160 -> {downscaledBmp.PixelWidth}x{downscaledBmp.PixelHeight} (Metadata preserved: {srcW}x{srcH})");

        // 2. Chế độ xem trước ảnh nguyên gốc (UseOriginalQualityPreview = true)
        MatExtensions.UseOriginalQualityPreview = true;
        var originalBmp = testMat.ToBitmapSourceForDisplay(1280, 720);

        if (originalBmp == null)
            throw new Exception("originalBmp is null");
        if (originalBmp.PixelWidth != 3840 || originalBmp.PixelHeight != 2160)
            throw new Exception($"Expected 100% full original resolution 3840x2160, but got {originalBmp.PixelWidth}x{originalBmp.PixelHeight}");

        Console.WriteLine($"  ✓ Original Quality mode verified: 100% full resolution preserved {originalBmp.PixelWidth}x{originalBmp.PixelHeight}");

        // 3. forceOriginalQuality tham số riêng lẻ
        MatExtensions.UseOriginalQualityPreview = false;
        var forcedOriginalBmp = testMat.ToBitmapSourceForDisplay(1280, 720, forceOriginalQuality: true);
        if (forcedOriginalBmp == null || forcedOriginalBmp.PixelWidth != 3840 || forcedOriginalBmp.PixelHeight != 2160)
            throw new Exception("forceOriginalQuality override failed");
        
        Console.WriteLine("  ✓ forceOriginalQuality override parameter verified.");

        // Reset về mặc định
        MatExtensions.UseOriginalQualityPreview = false;
    }

    private static void Test_GlobalAppSettings_UseOriginalQualityPreview_Serialization()
    {
        Console.WriteLine("▶ Running Test_GlobalAppSettings_UseOriginalQualityPreview_Serialization...");

        var settings = new GlobalAppSettings
        {
            UseOriginalQualityPreview = true
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        if (!json.Contains("\"UseOriginalQualityPreview\": true"))
            throw new Exception("Serialization of UseOriginalQualityPreview failed");

        var deserialized = JsonSerializer.Deserialize<GlobalAppSettings>(json);
        if (deserialized == null || !deserialized.UseOriginalQualityPreview)
            throw new Exception("Deserialization of UseOriginalQualityPreview failed");

        Console.WriteLine("  ✓ GlobalAppSettings.UseOriginalQualityPreview serialization & persistence verified.");
    }

    private static void Test_WriteableBitmapRenderer_FullResolutionVsDownscaled()
    {
        Console.WriteLine("▶ Running Test_WriteableBitmapRenderer_FullResolutionVsDownscaled...");

        using var renderer = new WriteableBitmapRenderer();
        using var testMat = new Mat(1440, 2560, MatType.CV_8UC3, new Scalar(50, 80, 120));

        // 1. Downscaled
        MatExtensions.UseOriginalQualityPreview = false;
        var bmpDownscaled = renderer.UpdateFromMat(testMat, 1280, 720);
        if (bmpDownscaled == null || renderer.Width > 1280 || renderer.Height > 720)
            throw new Exception($"WriteableBitmapRenderer downscale failed: got {renderer.Width}x{renderer.Height}");

        Console.WriteLine($"  ✓ WriteableBitmapRenderer downscale: 2560x1440 -> {renderer.Width}x{renderer.Height}");

        // 2. Original Quality
        MatExtensions.UseOriginalQualityPreview = true;
        var bmpOriginal = renderer.UpdateFromMat(testMat, 1280, 720);
        if (bmpOriginal == null || renderer.Width != 2560 || renderer.Height != 1440)
            throw new Exception($"WriteableBitmapRenderer original quality failed: got {renderer.Width}x{renderer.Height}");

        Console.WriteLine($"  ✓ WriteableBitmapRenderer original quality: {renderer.Width}x{renderer.Height} (100% native resolution)");

        // Reset về mặc định
        MatExtensions.UseOriginalQualityPreview = false;
    }
}
