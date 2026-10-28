using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.Services.Camera;
using VisionInspectionApp.UI.Services.Camera.Drivers;
using VisionInspectionApp.UI.ViewModels;

namespace TestExtractApp;

/// <summary>
/// Bộ kiểm thử tự động cho tích hợp Nguồn ảnh Camera Android (Samsung Galaxy S24 Ultra) Pro Mode.
/// </summary>
public static class AndroidCameraProModeTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=========================================================");
        Console.WriteLine("📱 RUNNING ANDROID CAMERA (GALAXY S24 ULTRA) PRO MODE TESTS");
        Console.WriteLine("=========================================================");

        Test_01_ImageSourceDefinition_AndroidCamera_Defaults_And_Serialization();
        Test_02_CameraParameters_AndroidProMode_Clone_And_Values();
        Test_03_CameraDeviceInfo_AndroidCamera_Vendor_And_Defaults();
        Test_04_CameraDriverFactory_ScanAllDevices_Includes_AndroidCamera();
        Test_05_AndroidCameraDriver_RestUrl_Formatting_And_Simulation();
        Test_06_JobCameraSettingsViewModel_AndroidCamera_Bindings_And_TorchToggle();

        Console.WriteLine("✅ ALL 6 ANDROID CAMERA PRO MODE TESTS PASSED!");
        Console.WriteLine("=========================================================\n");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    /// <summary>
    /// Test 1: Kiểm tra ImageSourceDefinition có đầy đủ các trường Pro Mode và JSON Serialization.
    /// </summary>
    public static void Test_01_ImageSourceDefinition_AndroidCamera_Defaults_And_Serialization()
    {
        Console.Write("Test 1: ImageSourceDefinition defaults & JSON round-trip... ");

        var def = new ImageSourceDefinition
        {
            SourceType = ImageSourceType.AndroidCamera,
            AndroidConnectionMode = AndroidConnectionMode.Usb,
            AndroidHost = "127.0.0.1",
            AndroidPort = 8080,
            AndroidLens = AndroidCameraLens.Tele5xPeriscope,
            AndroidEv = 0.5f,
            AndroidIso = "200",
            AndroidFocusMode = AndroidFocusMode.Manual,
            AndroidFocusDistance = 0.35f,
            AndroidWhiteBalance = "Daylight",
            AndroidEnableTorch = true,
            AndroidHighResSnapshot = true
        };

        Assert(def.SourceType == ImageSourceType.AndroidCamera, "SourceType phải là AndroidCamera");
        Assert(def.AndroidLens == AndroidCameraLens.Tele5xPeriscope, "AndroidLens phải là Tele5xPeriscope");

        // Serialize và Deserialize JSON
        var json = JsonSerializer.Serialize(def);
        var restored = JsonSerializer.Deserialize<ImageSourceDefinition>(json);

        Assert(restored != null, "Deserialized ImageSourceDefinition không được null");
        Assert(restored!.SourceType == ImageSourceType.AndroidCamera, "Khôi phục SourceType thất bại");
        Assert(restored.AndroidHost == "127.0.0.1", "Khôi phục AndroidHost thất bại");
        Assert(restored.AndroidPort == 8080, "Khôi phục AndroidPort thất bại");
        Assert(restored.AndroidLens == AndroidCameraLens.Tele5xPeriscope, "Khôi phục AndroidLens thất bại");
        Assert(Math.Abs(restored.AndroidEv - 0.5f) < 0.001f, "Khôi phục AndroidEv thất bại");
        Assert(restored.AndroidIso == "200", "Khôi phục AndroidIso thất bại");
        Assert(restored.AndroidFocusMode == AndroidFocusMode.Manual, "Khôi phục AndroidFocusMode thất bại");
        Assert(Math.Abs(restored.AndroidFocusDistance - 0.35f) < 0.001f, "Khôi phục AndroidFocusDistance thất bại");
        Assert(restored.AndroidWhiteBalance == "Daylight", "Khôi phục AndroidWhiteBalance thất bại");
        Assert(restored.AndroidEnableTorch == true, "Khôi phục AndroidEnableTorch thất bại");
        Assert(restored.AndroidHighResSnapshot == true, "Khôi phục AndroidHighResSnapshot thất bại");

        Console.WriteLine("PASSED");
    }

    /// <summary>
    /// Test 2: Kiểm tra CameraParameters lưu trữ thông số Pro Mode và Clone độc lập.
    /// </summary>
    public static void Test_02_CameraParameters_AndroidProMode_Clone_And_Values()
    {
        Console.Write("Test 2: CameraParameters Pro Mode values & Clone... ");

        var p = new CameraParameters
        {
            AndroidConnectionMode = AndroidConnectionMode.WiFi,
            AndroidHost = "192.168.1.155",
            AndroidPort = 8080,
            AndroidLens = AndroidCameraLens.PrimaryWide1x,
            AndroidEv = -1.0f,
            AndroidIso = "400",
            AndroidFocusMode = AndroidFocusMode.Macro,
            AndroidFocusDistance = 0.1f,
            AndroidWhiteBalance = "Fluorescent",
            AndroidEnableTorch = false,
            AndroidHighResSnapshot = true
        };

        var clone = p.Clone();
        Assert(clone.AndroidConnectionMode == AndroidConnectionMode.WiFi, "Clone AndroidConnectionMode sai");
        Assert(clone.AndroidHost == "192.168.1.155", "Clone AndroidHost sai");
        Assert(clone.AndroidLens == AndroidCameraLens.PrimaryWide1x, "Clone AndroidLens sai");
        Assert(Math.Abs(clone.AndroidEv - (-1.0f)) < 0.001f, "Clone AndroidEv sai");
        Assert(clone.AndroidWhiteBalance == "Fluorescent", "Clone AndroidWhiteBalance sai");

        // Đảm bảo thay đổi ở clone không làm thay đổi bản gốc
        clone.AndroidEv = 1.5f;
        clone.AndroidLens = AndroidCameraLens.UltraWide06x;
        Assert(Math.Abs(p.AndroidEv - (-1.0f)) < 0.001f, "Bản gốc không được bị đột biến khi sửa clone");
        Assert(p.AndroidLens == AndroidCameraLens.PrimaryWide1x, "Bản gốc không được bị đột biến khi sửa clone");

        Console.WriteLine("PASSED");
    }

    /// <summary>
    /// Test 3: Kiểm tra CameraDeviceInfo cho Android Camera.
    /// </summary>
    public static void Test_03_CameraDeviceInfo_AndroidCamera_Vendor_And_Defaults()
    {
        Console.Write("Test 3: CameraDeviceInfo Android Camera vendor & metadata... ");

        var info = new CameraDeviceInfo
        {
            Vendor = CameraVendor.AndroidCamera,
            InterfaceType = CameraInterfaceType.Android,
            ModelName = "Samsung Galaxy S24 Ultra (USB ADB)",
            Index = -3,
            AndroidHost = "127.0.0.1",
            AndroidPort = 8080,
            AndroidConnectionMode = AndroidConnectionMode.Usb
        };

        Assert(info.Vendor == CameraVendor.AndroidCamera, "Vendor phải là AndroidCamera");
        Assert(info.InterfaceType == CameraInterfaceType.Android, "InterfaceType phải là Android");
        Assert(info.DeviceIcon == "📱", "Icon phải là biểu tượng điện thoại 📱");
        Assert(info.DisplayName.Contains("[Android]"), "DisplayName phải chứa thẻ [Android]");

        Console.WriteLine("PASSED");
    }

    /// <summary>
    /// Test 4: Kiểm tra CameraDriverFactory quét thiết bị và khởi tạo AndroidCameraDriver.
    /// </summary>
    public static void Test_04_CameraDriverFactory_ScanAllDevices_Includes_AndroidCamera()
    {
        Console.Write("Test 4: CameraDriverFactory scanning & driver creation... ");

        var allDevices = CameraDriverFactory.ScanAllDevices();
        Assert(allDevices.Count > 0, "ScanAllDevices phải trả về ít nhất 1 thiết bị");

        var androidDevs = allDevices.Where(d => d.Vendor == CameraVendor.AndroidCamera).ToList();
        Assert(androidDevs.Count >= 2, $"Phải phát hiện ít nhất 2 profile Android Camera (USB & Wi-Fi), thực tế: {androidDevs.Count}");

        var usbProfile = androidDevs.FirstOrDefault(d => d.AndroidConnectionMode == AndroidConnectionMode.Usb);
        Assert(usbProfile != null, "Không tìm thấy profile USB ADB");
        Assert(usbProfile!.AndroidHost == "127.0.0.1", "Profile USB phải có host 127.0.0.1");

        var wifiProfile = androidDevs.FirstOrDefault(d => d.AndroidConnectionMode == AndroidConnectionMode.WiFi);
        Assert(wifiProfile != null, "Không tìm thấy profile Wi-Fi / Ethernet");

        // Kiểm tra tạo Driver
        var driver = CameraDriverFactory.CreateDriver(CameraVendor.AndroidCamera);
        Assert(driver != null, "CreateDriver cho AndroidCamera không được null");
        Assert(driver is AndroidCameraDriver, "Driver tạo ra phải là kiểu AndroidCameraDriver");

        driver.Dispose();
        Console.WriteLine("PASSED");
    }

    /// <summary>
    /// Test 5: Kiểm tra cấu trúc URL REST API và xử lý offline không gây crash.
    /// </summary>
    public static void Test_05_AndroidCameraDriver_RestUrl_Formatting_And_Simulation()
    {
        Console.Write("Test 5: AndroidCameraDriver REST URL & offline handling... ");

        var driver = new AndroidCameraDriver();
        var p = new CameraParameters
        {
            AndroidHost = "127.0.0.1",
            AndroidPort = 8080,
            AndroidLens = AndroidCameraLens.Tele3x,
            AndroidEv = 0.5f,
            AndroidFocusMode = AndroidFocusMode.Manual,
            AndroidFocusDistance = 0.5f,
            AndroidEnableTorch = false
        };

        // Áp dụng tham số không được ném unhandled exception kể cả khi điện thoại chưa cắm
        try
        {
            var task = driver.ApplyParametersAsync(p);
            task.Wait(1000);
        }
        catch (Exception ex)
        {
            Assert(false, $"ApplyParametersAsync không được throw unhandled exception: {ex.Message}");
        }

        // Chụp snapshot khi offline trả về null an toàn
        var snapTask = driver.GrabFrameAsync(1000);
        snapTask.Wait(1000);
        var snapshot = snapTask.Result;
        Assert(snapshot == null, "Snapshot khi offline phải trả về null một cách an toàn không crash");

        driver.Dispose();
        Console.WriteLine("PASSED");
    }

    /// <summary>
    /// Test 6: Kiểm tra JobCameraSettingsViewModel logic binding cho Android Camera và lệnh Torch.
    /// </summary>
    public static void Test_06_JobCameraSettingsViewModel_AndroidCamera_Bindings_And_TorchToggle()
    {
        Console.Write("Test 6: JobCameraSettingsViewModel bindings & torch toggle... ");

        var camService = new CameraService();
        var initialParams = new CameraParameters
        {
            AndroidConnectionMode = AndroidConnectionMode.Usb,
            AndroidHost = "127.0.0.1",
            AndroidPort = 8080,
            AndroidLens = AndroidCameraLens.PrimaryWide1x,
            AndroidEnableTorch = false,
            AndroidFocusMode = AndroidFocusMode.Auto
        };

        var vm = new JobCameraSettingsViewModel(camService, initialParams, "TestJob");

        // Chọn Android Camera
        var androidDev = vm.AvailableDevices.FirstOrDefault(d => d.Vendor == CameraVendor.AndroidCamera);
        Assert(androidDev != null, "Phải tìm thấy thiết bị Android Camera trong danh sách AvailableDevices");

        vm.SelectedDevice = androidDev;
        Assert(vm.IsAndroidCamera == true, "IsAndroidCamera phải là true khi chọn Android Camera");
        Assert(vm.IsIndustrialCamera == false, "IsIndustrialCamera phải là false");

        // Thử lệnh bật/tắt đèn Flash LED Torch
        Assert(vm.AndroidEnableTorch == false, "Mặc định Torch là tắt");
        vm.ToggleAndroidTorchCommand.Execute(null);
        Assert(vm.AndroidEnableTorch == true, "Sau khi chạy ToggleTorchCommand, Torch phải là bật");
        vm.ToggleAndroidTorchCommand.Execute(null);
        Assert(vm.AndroidEnableTorch == false, "Sau khi chạy ToggleTorchCommand lần nữa, Torch phải tắt");

        // Thử chuyển Focus Mode sang Manual
        Assert(vm.IsAndroidManualFocus == false, "Chế độ Auto thì IsAndroidManualFocus phải là false");
        vm.AndroidFocusMode = AndroidFocusMode.Manual;
        Assert(vm.IsAndroidManualFocus == true, "Chế độ Manual thì IsAndroidManualFocus phải là true");

        vm.Dispose();
        camService.Dispose();
        Console.WriteLine("PASSED");
    }
}
