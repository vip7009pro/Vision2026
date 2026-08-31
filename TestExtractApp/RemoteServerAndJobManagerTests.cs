using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VisionInspectionApp.Application.OQC;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;

namespace TestExtractApp;

public static class RemoteServerAndJobManagerTests
{
    public static void RunTests()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("🧪 RUNNING REMOTE SERVER & JOB MANAGER TESTS");
        Console.WriteLine("=================================================");

        Test_JobManagerItem_PropertiesAndNotifications();
        Test_OqcScannerConfig_SerializationWithServerFields();
        Test_ImageSourceDefinition_UrlSupport();
        Test_AssignQuerySubstitutionWithTeachImage();
        Test_RemoteServerService_WithHttpMockServerAsync().GetAwaiter().GetResult();

        Console.WriteLine("✅ ALL REMOTE SERVER & JOB MANAGER TESTS PASSED!");
        Console.WriteLine("=================================================\n");
    }

    private static void Test_JobManagerItem_PropertiesAndNotifications()
    {
        Console.WriteLine("▶ Running Test_JobManagerItem_PropertiesAndNotifications...");
        var item = new JobManagerItem
        {
            ProductCode = "PRD_001",
            ProductName = "Camera Module A",
            JobFilePath = @"C:\VisionJobs\PRD_001.job",
            TeachImagePath = "uploads/teach_images/teach_PRD_001.png",
            UpdatedAt = "2026-08-31 10:00:00"
        };

        if (!item.HasJobFile)
            throw new Exception("HasJobFile must be true when JobFilePath is set.");

        if (!item.HasTeachImage)
            throw new Exception("HasTeachImage must be true when TeachImagePath is set.");

        bool propChangedFired = false;
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(JobManagerItem.JobFilePath))
                propChangedFired = true;
        };

        item.JobFilePath = @"C:\VisionJobs\PRD_001_v2.job";
        if (!propChangedFired)
            throw new Exception("PropertyChanged event was not fired for JobFilePath.");

        item.JobFilePath = "";
        if (item.HasJobFile)
            throw new Exception("HasJobFile must be false when JobFilePath is empty.");

        item.TeachImagePath = "";
        if (item.HasTeachImage)
            throw new Exception("HasTeachImage must be false when TeachImagePath is empty.");

        Console.WriteLine("  ✓ JobManagerItem property notification & flags verified.");
    }

    private static void Test_OqcScannerConfig_SerializationWithServerFields()
    {
        Console.WriteLine("▶ Running Test_OqcScannerConfig_SerializationWithServerFields...");
        var cfg = new OqcScannerConfig
        {
            ServerApiUrl = "http://192.168.1.100:8080/vision_upload.php",
            TeachImageColumn = "TeachImagePath",
            JobManagerDbId = "DB_SQLSERVER",
            JobManagerQuery = "SELECT ProductCode, ProductName, JobFilePath, TeachImagePath, UpdatedAt FROM ProductJobs",
            JobManagerProductCodeColumn = "ProductCode",
            JobManagerProductNameColumn = "ProductName",
            JobManagerJobFileColumn = "JobFilePath",
            JobManagerTeachImageColumn = "TeachImagePath",
            JobManagerUpdatedColumn = "UpdatedAt",
            JobManagerPageSize = 100
        };

        string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        var deserialized = JsonSerializer.Deserialize<OqcScannerConfig>(json);

        if (deserialized == null)
            throw new Exception("Failed to deserialize OqcScannerConfig.");

        if (deserialized.ServerApiUrl != "http://192.168.1.100:8080/vision_upload.php")
            throw new Exception($"ServerApiUrl mismatch: {deserialized.ServerApiUrl}");

        if (deserialized.TeachImageColumn != "TeachImagePath")
            throw new Exception($"TeachImageColumn mismatch: {deserialized.TeachImageColumn}");

        if (deserialized.JobManagerPageSize != 100)
            throw new Exception($"JobManagerPageSize mismatch: {deserialized.JobManagerPageSize}");

        Console.WriteLine("  ✓ OqcScannerConfig Server & JobManager serialization verified.");
    }

    private static void Test_ImageSourceDefinition_UrlSupport()
    {
        Console.WriteLine("▶ Running Test_ImageSourceDefinition_UrlSupport...");
        var imgSource = new ImageSourceDefinition
        {
            Name = "ImageSource1",
            SourceType = ImageSourceType.Url,
            ImageUrl = "http://localhost/uploads/teach_images/teach_PRD_001.png"
        };

        if (imgSource.SourceType != ImageSourceType.Url)
            throw new Exception("SourceType must support ImageSourceType.Url.");

        if (imgSource.ImageUrl != "http://localhost/uploads/teach_images/teach_PRD_001.png")
            throw new Exception("ImageUrl was not preserved.");

        Console.WriteLine("  ✓ ImageSourceDefinition.ImageUrl verified.");
    }

    private static void Test_AssignQuerySubstitutionWithTeachImage()
    {
        Console.WriteLine("▶ Running Test_AssignQuerySubstitutionWithTeachImage...");
        string template = "UPDATE ProductJobs SET JobFilePath = '{JobFilePath}', TeachImagePath = '{TeachImagePath}' WHERE ProductCode = '{ProductCode}'";
        string productCode = "SAMPLE_XYZ";
        string jobPath = @"C:\VisionJobs\SAMPLE_XYZ.job";
        string teachPath = "uploads/teach_images/teach_SAMPLE_XYZ.png";

        string sql = template
            .Replace("{ProductCode}", productCode.Replace("'", "''"))
            .Replace("{JobFilePath}", jobPath.Replace("'", "''"))
            .Replace("{TeachImagePath}", teachPath.Replace("'", "''"));

        if (!sql.Contains("uploads/teach_images/teach_SAMPLE_XYZ.png"))
            throw new Exception("TeachImagePath was not substituted into SQL query.");

        if (!sql.Contains(@"C:\VisionJobs\SAMPLE_XYZ.job"))
            throw new Exception("JobFilePath was not substituted into SQL query.");

        Console.WriteLine("  ✓ SQL Assign substitution with {TeachImagePath} verified.");
    }

    private static async Task Test_RemoteServerService_WithHttpMockServerAsync()
    {
        Console.WriteLine("▶ Running Test_RemoteServerService_WithHttpMockServerAsync...");
        int port = 19582;
        string prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                var context = await listener.GetContextAsync();
                var req = context.Request;
                var resp = context.Response;

                if (req.Url?.Query.Contains("action=ping") == true)
                {
                    string json = JsonSerializer.Serialize(new
                    {
                        success = true,
                        message = "Vision Server API is ONLINE and READY",
                        upload_dir_writable = true,
                        php_version = "8.2.12"
                    });
                    byte[] buf = Encoding.UTF8.GetBytes(json);
                    resp.ContentType = "application/json";
                    resp.ContentLength64 = buf.Length;
                    await resp.OutputStream.WriteAsync(buf);
                    resp.Close();
                }
                else if (req.Url?.Query.Contains("action=upload_image") == true)
                {
                    string json = JsonSerializer.Serialize(new
                    {
                        success = true,
                        message = "File uploaded successfully",
                        file_name = "teach_MOCK_123.png",
                        relative_path = "uploads/teach_images/teach_MOCK_123.png",
                        full_url = $"{prefix}uploads/teach_images/teach_MOCK_123.png",
                        size = 1024
                    });
                    byte[] buf = Encoding.UTF8.GetBytes(json);
                    resp.ContentType = "application/json";
                    resp.ContentLength64 = buf.Length;
                    await resp.OutputStream.WriteAsync(buf);
                    resp.Close();
                }
                else if (req.Url?.AbsolutePath.Contains("test_image.png") == true)
                {
                    byte[] dummyImg = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG Header
                    resp.ContentType = "image/png";
                    resp.ContentLength64 = dummyImg.Length;
                    await resp.OutputStream.WriteAsync(dummyImg);
                    resp.Close();
                }
                else
                {
                    resp.StatusCode = 404;
                    resp.Close();
                }
            }
        });

        var service = new RemoteServerService();
        string apiUrl = $"{prefix}vision_upload.php";

        // 1. Test Ping
        var (pingOk, pingMsg) = await service.PingServerAsync(apiUrl);
        if (!pingOk)
            throw new Exception($"Ping failed: {pingMsg}");
        Console.WriteLine($"  ✓ PingServerAsync succeeded: {pingMsg}");

        // 2. Test Upload Image
        byte[] dummyData = Encoding.UTF8.GetBytes("FAKE_PNG_DATA");
        var (upOk, fullUrl, relPath, upErr) = await service.UploadImageAsync(dummyData, "teach_MOCK_123.png", "MOCK_123", apiUrl);
        if (!upOk)
            throw new Exception($"Upload failed: {upErr}");
        if (relPath != "uploads/teach_images/teach_MOCK_123.png")
            throw new Exception($"Relative path mismatch: {relPath}");
        Console.WriteLine($"  ✓ UploadImageAsync succeeded: {fullUrl} -> {relPath}");

        // 3. Test Download File
        string downloadUrl = $"{prefix}test_image.png";
        var (dlOk, dlData, dlErr) = await service.DownloadFileAsync(downloadUrl);
        if (!dlOk || dlData == null || dlData.Length == 0)
            throw new Exception($"Download failed: {dlErr}");
        if (dlData[0] != 0x89 || dlData[1] != 0x50)
            throw new Exception("Downloaded data corrupted.");
        Console.WriteLine($"  ✓ DownloadFileAsync succeeded: {dlData.Length} bytes received.");

        await serverTask;
        listener.Stop();
    }
}
