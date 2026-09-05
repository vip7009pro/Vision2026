using System;
using System.Text.Json;
using VisionInspectionApp.UI.Services;

namespace TestExtractApp;

public static class CrosshairOverlayTests
{
    public static void RunTests()
    {
        Console.WriteLine("\n=================================================");
        Console.WriteLine("✛ RUNNING CROSSHAIR OVERLAY TESTS");
        Console.WriteLine("=================================================");

        Test_GlobalAppSettings_ShowCrosshair_Defaults();
        Test_GlobalAppSettings_ShowCrosshair_Serialization();

        Console.WriteLine("✅ ALL CROSSHAIR OVERLAY TESTS PASSED!");
        Console.WriteLine("=================================================\n");
    }

    private static void Test_GlobalAppSettings_ShowCrosshair_Defaults()
    {
        Console.WriteLine("▶ Running Test_GlobalAppSettings_ShowCrosshair_Defaults...");
        var settings = new GlobalAppSettings();
        if (settings.ShowCrosshair != false)
        {
            throw new Exception($"Expected default ShowCrosshair to be false, but was {settings.ShowCrosshair}");
        }
        Console.WriteLine("  ✓ Default ShowCrosshair is false.");
    }

    private static void Test_GlobalAppSettings_ShowCrosshair_Serialization()
    {
        Console.WriteLine("▶ Running Test_GlobalAppSettings_ShowCrosshair_Serialization...");
        
        var settings = new GlobalAppSettings
        {
            ShowCrosshair = true,
            UseOriginalQualityPreview = true
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        if (!json.Contains("\"ShowCrosshair\": true"))
        {
            throw new Exception($"Serialized JSON does not contain ShowCrosshair: true. JSON:\n{json}");
        }

        var deserialized = JsonSerializer.Deserialize<GlobalAppSettings>(json);
        if (deserialized == null || !deserialized.ShowCrosshair)
        {
            throw new Exception("Deserialized GlobalAppSettings failed to preserve ShowCrosshair = true");
        }

        // Kiểm tra backward compatibility với JSON không có ShowCrosshair
        var legacyJson = "{\"Theme\":\"Dark\",\"Language\":\"vi-VN\",\"UseOriginalQualityPreview\":false}";
        var legacyDeserialized = JsonSerializer.Deserialize<GlobalAppSettings>(legacyJson);
        if (legacyDeserialized == null || legacyDeserialized.ShowCrosshair != false)
        {
            throw new Exception("Legacy JSON without ShowCrosshair should default to false");
        }

        Console.WriteLine("  ✓ GlobalAppSettings ShowCrosshair serialization & backward compatibility verified.");
    }
}
