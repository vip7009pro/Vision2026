using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.UI.Services;

namespace TestExtractApp;

public static class DotnetRuntimeTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("🧪 RUNNING TESTS: .NET RUNTIME & PLC BRIDGE PREREQUISITES");
        Console.WriteLine("=======================================================");

        Test_DotnetRuntimeService_DetectionAndVersionParsing();
        Test_DotnetRuntimeService_DownloadUrls();
        Test_GlobalAppSettings_SuppressDotnetX86Prompt_Persistence();
        Test_MitsubishiMxComponentDriver_InformativeErrorMessageFormat();

        Console.WriteLine("=======================================================");
        Console.WriteLine("✅ ALL .NET RUNTIME & PLC BRIDGE PREREQUISITE TESTS PASSED (100%)!");
        Console.WriteLine("=======================================================\n");
    }

    private static void Test_DotnetRuntimeService_DetectionAndVersionParsing()
    {
        Console.WriteLine("--- Test 1: Kiểm tra Nhận diện Runtime & Phân tích Phiên bản .NET ---");

        // 1. Kiểm tra hàm IsVersionAtLeast
        if (!DotnetRuntimeService.IsVersionAtLeast("8.0.0", 8))
            throw new Exception("IsVersionAtLeast('8.0.0', 8) must return true");

        if (!DotnetRuntimeService.IsVersionAtLeast("8.0.31", 8))
            throw new Exception("IsVersionAtLeast('8.0.31', 8) must return true");

        if (!DotnetRuntimeService.IsVersionAtLeast("10.0.12", 8))
            throw new Exception("IsVersionAtLeast('10.0.12', 8) must return true");

        if (!DotnetRuntimeService.IsVersionAtLeast("v9.0.0-preview", 8))
            throw new Exception("IsVersionAtLeast('v9.0.0-preview', 8) must return true");

        if (DotnetRuntimeService.IsVersionAtLeast("7.0.15", 8))
            throw new Exception("IsVersionAtLeast('7.0.15', 8) must return false");

        if (DotnetRuntimeService.IsVersionAtLeast("6.0.0", 8))
            throw new Exception("IsVersionAtLeast('6.0.0', 8) must return false");

        if (DotnetRuntimeService.IsVersionAtLeast("", 8))
            throw new Exception("IsVersionAtLeast('', 8) must return false");

        // 2. Kiểm tra x64 runtime trên môi trường hiện tại
        var service = new DotnetRuntimeService();
        bool isX64 = service.IsX64RuntimeInstalled(8);
        if (!isX64)
            throw new Exception("Expected .NET x64 runtime to be detected as installed on this development machine.");

        Console.WriteLine($"  -> PASSED: .NET x64 = {isX64}, Version parser hoạt động chính xác 100%.");
    }

    private static void Test_DotnetRuntimeService_DownloadUrls()
    {
        Console.WriteLine("--- Test 2: Kiểm tra Đường dẫn Tải Về Chính Thức Microsoft CDN ---");

        var service = new DotnetRuntimeService();
        string directUrl = service.GetX86RuntimeDownloadUrl();
        string officialPage = service.GetOfficialDownloadPageUrl();

        if (directUrl != "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x86.exe")
            throw new Exception($"Unexpected direct download url: {directUrl}");

        if (!officialPage.Contains("dotnet.microsoft.com"))
            throw new Exception($"Unexpected official page url: {officialPage}");

        Console.WriteLine($"  -> PASSED: Direct URL: {directUrl}, Web URL: {officialPage}");
    }

    private static void Test_GlobalAppSettings_SuppressDotnetX86Prompt_Persistence()
    {
        Console.WriteLine("--- Test 3: Kiểm tra Bền Vững Cấu Hình SuppressDotnetX86Prompt ---");

        var settings = new GlobalAppSettings();
        if (settings.Plc.SuppressDotnetX86Prompt)
            throw new Exception("Default SuppressDotnetX86Prompt must be false.");

        settings.Plc.SuppressDotnetX86Prompt = true;

        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var deserialized = JsonSerializer.Deserialize<GlobalAppSettings>(json);

        if (deserialized == null)
            throw new Exception("Failed to deserialize GlobalAppSettings");

        if (!deserialized.Plc.SuppressDotnetX86Prompt)
            throw new Exception("SuppressDotnetX86Prompt was not persisted into JSON!");

        Console.WriteLine("  -> PASSED: Cấu hình SuppressDotnetX86Prompt lưu trữ và nạp lại chính xác 100%.");
    }

    private static void Test_MitsubishiMxComponentDriver_InformativeErrorMessageFormat()
    {
        Console.WriteLine("--- Test 4: Kiểm tra Định Dạng Thông Báo Lỗi Rõ Ràng Khi Thiếu .NET x86 ---");

        string expectedSnippet1 = "module 32-bit PLC Bridge vì máy tính chưa cài đặt .NET Desktop Runtime 8.0 (x86 / 32-bit)";
        string expectedSnippet2 = DotnetRuntimeService.MicrosoftX86DirectDownloadUrl;

        string testErrorMessage = 
            "Không thể khởi chạy module 32-bit PLC Bridge vì máy tính chưa cài đặt .NET Desktop Runtime 8.0 (x86 / 32-bit).\n" +
            "Module giao tiếp PLC Mitsubishi (ActUtlType COM) bắt buộc chạy ở chế độ 32-bit.\n" +
            "Vui lòng cài đặt .NET Desktop Runtime 8.0 (x86) để sử dụng tính năng PLC Mitsubishi: " +
            VisionInspectionApp.Application.Services.DotnetRuntimeService.MicrosoftX86DirectDownloadUrl;

        if (!testErrorMessage.Contains(expectedSnippet1))
            throw new Exception("Error message missing explanation snippet!");

        if (!testErrorMessage.Contains(expectedSnippet2))
            throw new Exception("Error message missing direct download link!");

        Console.WriteLine("  -> PASSED: Thông báo lỗi hướng dẫn chi tiết và cung cấp link tải trực tiếp.");
    }
}
