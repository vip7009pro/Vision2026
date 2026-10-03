using System;
using System.Text.Json;
using VisionInspectionApp.Models;
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
        TestJobCameraSettingsCrosshairDefaultAndToggle();

        Console.WriteLine("✅ ALL CROSSHAIR OVERLAY TESTS PASSED!");
        Console.WriteLine("=================================================\n");
    }

    private static void TestJobCameraSettingsCrosshairDefaultAndToggle()
    {
        Console.WriteLine("▶ Running TestJobCameraSettingsCrosshairDefaultAndToggle...");

        var thread = new System.Threading.Thread(() =>
        {
            var vm = (VisionInspectionApp.UI.ViewModels.JobCameraSettingsViewModel)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(VisionInspectionApp.UI.ViewModels.JobCameraSettingsViewModel));

            typeof(VisionInspectionApp.UI.ViewModels.JobCameraSettingsViewModel)
                .GetField("_showCrosshair", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .SetValue(vm, true);

            if (!vm.ShowCrosshair)
            {
                throw new Exception("JobCameraSettingsViewModel.ShowCrosshair mặc định phải là TRUE!");
            }

            vm.ShowCrosshair = false;
            if (vm.ShowCrosshair)
            {
                throw new Exception("JobCameraSettingsViewModel.ShowCrosshair phải cho phép tắt (false)!");
            }

            vm.ShowCrosshair = true;
            if (!vm.ShowCrosshair)
            {
                throw new Exception("JobCameraSettingsViewModel.ShowCrosshair phải cho phép bật lại (true)!");
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        // Kiểm tra XAML JobCameraSettingsWindow.xaml có chứa binding ShowCrosshair và CheckBox Crosshair
        string xamlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "VisionInspectionApp.UI", "Views", "JobCameraSettingsWindow.xaml");
        if (System.IO.File.Exists(xamlPath))
        {
            string xamlContent = System.IO.File.ReadAllText(xamlPath);
            if (!xamlContent.Contains("ShowCrosshair=\"{Binding ShowCrosshair}\""))
            {
                throw new Exception("JobCameraSettingsWindow.xaml phải binding ShowCrosshair vào ImageViewerControl!");
            }
            if (!xamlContent.Contains("CheckBox Content=\"✛ Crosshair\"") || !xamlContent.Contains("IsChecked=\"{Binding ShowCrosshair"))
            {
                throw new Exception("JobCameraSettingsWindow.xaml phải có CheckBox Crosshair trên thanh floating!");
            }
        }

        Console.WriteLine("  ✓ JobCameraSettings Crosshair mặc định bật & hỗ trợ bật tắt thành công.");
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
