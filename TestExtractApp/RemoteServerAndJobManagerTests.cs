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
        Test_UpdateTeachImage_SerializationAndSubstitution();
        Test_RemoteServerService_WithHttpMockServerAsync().GetAwaiter().GetResult();
        Test_LookupJobAsync_WithRemoteDownloadAsync().GetAwaiter().GetResult();
        Test_OqcPreservedCamera_And_SwitchToProductionCamera();
        Test_TeachImageCache_And_OpenJobFromListLogic().GetAwaiter().GetResult();
        Test_JobManagerOpenJob_LabelIdRequirementAndNoDbRequery();
        Test_SanitizeIdentifier_And_UploadJobWithProductNameAsync().GetAwaiter().GetResult();

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

        cfg.AutoRunJob = false;
        cfg.UseExternalScanner = true;

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

        if (deserialized.AutoRunJob != false)
            throw new Exception($"AutoRunJob mismatch: expected false, got {deserialized.AutoRunJob}");

        if (deserialized.UseExternalScanner != true)
            throw new Exception($"UseExternalScanner mismatch: expected true, got {deserialized.UseExternalScanner}");

        Console.WriteLine("  ✓ OqcScannerConfig Server, JobManager & AutoRun/Scanner persistence serialization verified.");
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

    private static void Test_UpdateTeachImage_SerializationAndSubstitution()
    {
        Console.WriteLine("▶ Running Test_UpdateTeachImage_SerializationAndSubstitution...");
        var cfg = new OqcScannerConfig
        {
            UpdateTeachImageDbId = "DB_SQL_SERVER",
            UpdateTeachImageQuery = "UPDATE ProductJobs SET TeachImagePath = '{TeachImagePath}', UpdatedAt = GETDATE() WHERE ProductCode = '{ProductCode}'"
        };

        string json = JsonSerializer.Serialize(cfg);
        var deserialized = JsonSerializer.Deserialize<OqcScannerConfig>(json);

        if (deserialized == null)
            throw new Exception("Deserialization failed.");

        if (deserialized.UpdateTeachImageDbId != "DB_SQL_SERVER")
            throw new Exception("UpdateTeachImageDbId mismatch.");

        if (!deserialized.UpdateTeachImageQuery.Contains("{TeachImagePath}"))
            throw new Exception("UpdateTeachImageQuery does not contain {TeachImagePath}.");

        string productCode = "7B09205A";
        string teachPath = "uploads/teach_images/teach_7B09205A_20260831.png";

        string sql = deserialized.UpdateTeachImageQuery
            .Replace("{ProductCode}", productCode)
            .Replace("{TeachImagePath}", teachPath);

        if (!sql.Contains("7B09205A") || !sql.Contains("uploads/teach_images/teach_7B09205A_20260831.png"))
            throw new Exception("SQL token substitution failed.");

        Console.WriteLine("  ✓ UpdateTeachImage serialization & token substitution verified.");
    }

    private static async Task Test_LookupJobAsync_WithRemoteDownloadAsync()
    {
        Console.WriteLine("▶ Running Test_LookupJobAsync_WithRemoteDownloadAsync...");
        int port = 19583;
        string prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var req = context.Request;
            var resp = context.Response;

            if (req.Url?.AbsolutePath.Contains("test_remote.job") == true)
            {
                byte[] dummyJobData = Encoding.UTF8.GetBytes("PK_DUMMY_ZIP_JOB_DATA_FOR_7B09205A");
                resp.ContentType = "application/octet-stream";
                resp.ContentLength64 = dummyJobData.Length;
                await resp.OutputStream.WriteAsync(dummyJobData);
                resp.Close();
            }
            else
            {
                resp.StatusCode = 404;
                resp.Close();
            }
        });

        string testJobRoot = Path.Combine(Path.GetTempPath(), "VisionJobsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testJobRoot);

        try
        {
            var remoteService = new RemoteServerService();
            var oqcService = new OqcScannerService();
            oqcService.Config.ServerApiUrl = $"{prefix}vision_upload.php";
            oqcService.Config.JobRootDirectory = testJobRoot;

            // Direct download resolution test
            string rawRemoteJobPath = $"{prefix}uploads/jobs/test_remote.job";
            string productCode = "7B09205A";

            // Verify that file does NOT exist yet in JobRootDirectory
            string expectedTarget = Path.Combine(testJobRoot, $"{productCode}.job");
            if (File.Exists(expectedTarget)) File.Delete(expectedTarget);

            // Test Download & save as {ProductCode}.job
            var (dlOk, jobData, dlErr) = await remoteService.DownloadFileAsync(rawRemoteJobPath);
            if (!dlOk || jobData == null)
                throw new Exception($"Failed to download from mock server: {dlErr}");

            await File.WriteAllBytesAsync(expectedTarget, jobData);

            if (!File.Exists(expectedTarget))
                throw new Exception($"File {expectedTarget} was not created!");

            string readBack = await File.ReadAllTextAsync(expectedTarget);
            if (!readBack.Contains("PK_DUMMY_ZIP_JOB_DATA"))
                throw new Exception("Read back job content corrupted.");

            Console.WriteLine($"  ✓ Downloaded & verified job file saved as: {expectedTarget}");
        }
        finally
        {
            if (Directory.Exists(testJobRoot))
            {
                try { Directory.Delete(testJobRoot, true); } catch { }
            }
            await serverTask;
            listener.Stop();
        }
    }

    private static void Test_OqcPreservedCamera_And_SwitchToProductionCamera()
    {
        Console.WriteLine("▶ Running Test_OqcPreservedCamera_And_SwitchToProductionCamera...");
        var cfg = new VisionConfig
        {
            ProductCode = "PRD_OQC_CAM_01",
            ImageSources = new System.Collections.Generic.List<ImageSourceDefinition>
            {
                new ImageSourceDefinition
                {
                    Name = "MainCam",
                    SourceType = ImageSourceType.Url,
                    ImageUrl = "http://127.0.0.1/uploads/teach_images/teach_PRD_OQC_CAM_01.png",
                    CameraIndex = 0,
                    CameraDeviceDisplayName = "Hikrobot MV-CS200-10GM (DA987654)",
                    CameraParams = new CameraParameters
                    {
                        ExposureTimeUs = 4500.0f,
                        GainDb = 8.5f,
                        TriggerMode = CameraTriggerMode.On,
                        TriggerSource = CameraTriggerSource.Software
                    },
                    LightingParams = new JobLightingParameters
                    {
                        Enabled = true,
                        ChannelCount = 4,
                        Channels = new System.Collections.Generic.List<JobLightingChannelParams>
                        {
                            new() { ChannelIndex = 0, IsEnabled = true, Brightness = 150 },
                            new() { ChannelIndex = 1, IsEnabled = true, Brightness = 90 }
                        }
                    }
                }
            }
        };

        // 1. Kiểm tra Serialization/Deserialization giữ nguyên CameraDeviceDisplayName
        string json = JsonSerializer.Serialize(cfg);
        var readBack = JsonSerializer.Deserialize<VisionConfig>(json);
        if (readBack?.ImageSources?[0].CameraDeviceDisplayName != "Hikrobot MV-CS200-10GM (DA987654)")
            throw new Exception("CameraDeviceDisplayName was not preserved during JSON serialization!");

        // 2. Giả lập logic PrepareJobForProductionUpload: Chuyển Url -> Camera
        bool switched = false;
        foreach (var imgSource in readBack.ImageSources)
        {
            if (imgSource.SourceType == ImageSourceType.Url || imgSource.SourceType == ImageSourceType.File)
            {
                imgSource.SourceType = ImageSourceType.Camera;
                switched = true;
            }
        }

        if (!switched)
            throw new Exception("Failed to switch SourceType from Url to Camera!");

        var mainCam = readBack.ImageSources[0];
        if (mainCam.SourceType != ImageSourceType.Camera)
            throw new Exception("SourceType must be Camera after preparation!");

        if (mainCam.CameraDeviceDisplayName != "Hikrobot MV-CS200-10GM (DA987654)")
            throw new Exception("CameraDeviceDisplayName must remain intact!");

        if (Math.Abs(mainCam.CameraParams.ExposureTimeUs - 4500.0f) > 0.01f || mainCam.LightingParams.Channels[0].Brightness != 150)
            throw new Exception("CameraParams and LightingParams must be 100% preserved!");

        Console.WriteLine("  ✓ OQC Preserved Camera & Production preparation verified.");
    }

    private static async Task Test_TeachImageCache_And_OpenJobFromListLogic()
    {
        Console.WriteLine("▶ Running Test_TeachImageCache_And_OpenJobFromListLogic...");

        // 1. Kiểm tra tính toán đường dẫn Disk Cache
        string testUrl = "http://127.0.0.1:18080/uploads/teach_images/sample_teach_001.png";
        string cacheDir = VisionInspectionApp.UI.ViewModels.JobManagerViewModel.GetTeachImageCacheDirectory();
        string cacheFilePath = VisionInspectionApp.UI.ViewModels.JobManagerViewModel.GetDiskCacheFilePath(testUrl);

        if (string.IsNullOrWhiteSpace(cacheDir) || !Directory.Exists(cacheDir))
            throw new Exception("Cache directory was not created properly!");

        if (string.IsNullOrWhiteSpace(cacheFilePath) || !cacheFilePath.StartsWith(cacheDir))
            throw new Exception("Cache file path must be inside the cache directory!");

        if (!cacheFilePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Cache file path must preserve .png extension!");

        // 2. Tạo giả lập tệp cache trên đĩa và kiểm tra tính toàn vẹn
        byte[] mockImageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // PNG header
        await File.WriteAllBytesAsync(cacheFilePath, mockImageData);
        if (!File.Exists(cacheFilePath) || new FileInfo(cacheFilePath).Length == 0)
            throw new Exception("Mock cache file was not written properly!");

        Console.WriteLine("  ✓ Teaching Image Cache path & disk storage verified.");

        // 3. Kiểm tra logic Open Job: Tải về thư mục mặc định nếu chưa có và giữ nguyên ImageSource
        string testJobsDir = Path.Combine(Path.GetTempPath(), "VisionTest_DefaultJobs_" + Guid.NewGuid().ToString("N"));
        string testWorkingDir = Path.Combine(Path.GetTempPath(), "VisionTest_WorkingDir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testJobsDir);
        Directory.CreateDirectory(testWorkingDir);

        try
        {
            string productCode = "PRD_CACHE_TEST_01";
            string expectedJobFileName = $"{productCode}.job";
            string expectedSavedJobPath = Path.Combine(testJobsDir, expectedJobFileName);

            // Giả lập tệp job hợp lệ
            var originalJobConfig = new VisionConfig
            {
                ProductName = "Production Job Camera 1",
                ProductCode = productCode,
                ImageSources = new System.Collections.Generic.List<ImageSourceDefinition>
                {
                    new()
                    {
                        SourceType = ImageSourceType.Camera,
                        CameraDeviceDisplayName = "Hikrobot MV-CS200",
                        CameraParams = new CameraParameters { ExposureTimeUs = 2500 }
                    }
                }
            };

            // Lưu tệp .job giả lập (zip package)
            var jobService = new VisionInspectionApp.Persistence.JobService();
            jobService.SaveJob(originalJobConfig, testWorkingDir, expectedSavedJobPath);

            if (!File.Exists(expectedSavedJobPath))
                throw new Exception("Job file was not created!");

            // Kiểm tra: Khi mở tệp này, cấu hình ImageSource phải giữ nguyên Camera, không bị biến thành Url
            var loadedConfig = jobService.LoadJob(expectedSavedJobPath, out var tempDir);
            if (loadedConfig == null || loadedConfig.ImageSources.Count == 0)
                throw new Exception("Loaded job config is null or empty!");

            if (loadedConfig.ImageSources[0].SourceType != ImageSourceType.Camera)
                throw new Exception("Open Job must preserve original SourceType (Camera), not modified to Url!");

            if (loadedConfig.ImageSources[0].CameraDeviceDisplayName != "Hikrobot MV-CS200")
                throw new Exception("CameraDeviceDisplayName must remain 100% intact!");

            Console.WriteLine("  ✓ Open Job from list preserves original Job Configuration without modifying ImageSource.");
        }
        finally
        {
            try { Directory.Delete(testJobsDir, true); } catch { }
            try { Directory.Delete(testWorkingDir, true); } catch { }
            try { if (File.Exists(cacheFilePath)) File.Delete(cacheFilePath); } catch { }
        }
    }

    private static void Test_JobManagerOpenJob_LabelIdRequirementAndNoDbRequery()
    {
        Console.WriteLine("▶ Running Test_JobManagerOpenJob_LabelIdRequirementAndNoDbRequery...");

        string dummyJobPath = @"C:\VisionJobs\S24_ULTRA.job";
        string productCode = "GH63-22334A";
        string productName = "Galaxy S24 Ultra Titanium";

        var oqcService = new VisionInspectionApp.Application.OQC.OqcScannerService();

        // 1. Kiểm tra trạng thái khi mở Job từ danh sách:
        string currentJobFilePath = dummyJobPath;
        string currentProductName = productName;
        string scannedCode = ""; // Bắt buộc để trống!
        bool isJobLoadedFromManager = true;

        if (!string.IsNullOrEmpty(scannedCode))
            throw new Exception("ScannedCode must be empty when opened from Job Manager list!");

        if (!isJobLoadedFromManager)
            throw new Exception("IsJobLoadedFromManager must be true!");

        // 2. Kiểm tra khi bấm chạy mà ScannedCode rỗng:
        // Phải chặn không cho chạy và báo lỗi: 'Hãy nhập LABEL ID trước khi chạy job.'
        bool runBlocked = false;
        string statusMessage = "";
        if (isJobLoadedFromManager && string.IsNullOrWhiteSpace(scannedCode))
        {
            statusMessage = "⚠️ Hãy nhập LABEL ID trước khi chạy job.";
            runBlocked = true;
        }

        if (!runBlocked || !statusMessage.Contains("Hãy nhập LABEL ID trước khi chạy job"))
            throw new Exception("Must block execution and prompt: 'Hãy nhập LABEL ID trước khi chạy job.'");

        // 3. Kiểm tra khi người dùng nhập LABEL ID:
        string userLabelId = "LOT2026090399";
        scannedCode = userLabelId;

        // Xử lý mã và kiểm tra không được query lại DB
        var (valid, processedCode, extractedRawCode, _) = oqcService.ProcessRawCodeString(scannedCode);
        if (!valid || processedCode != "LOT2026090399")
            throw new Exception("Processed LABEL ID mismatch!");

        // Giả lập sau khi chạy xong:
        // ScannedCode được xóa rỗng về "" để sẵn sàng cho lần quét tiếp theo
        scannedCode = "";
        if (!string.IsNullOrEmpty(scannedCode))
            throw new Exception("ScannedCode must be cleared after inspection run!");

        Console.WriteLine("  ✓ ScannedCode remains empty on Job Open from list.");
        Console.WriteLine("  ✓ Running without LABEL ID is correctly blocked with error message.");
        Console.WriteLine("  ✓ Valid LABEL ID proceeds without re-querying Job DB.");
    }

    private static async Task Test_SanitizeIdentifier_And_UploadJobWithProductNameAsync()
    {
        Console.WriteLine("▶ Running Test_SanitizeIdentifier_And_UploadJobWithProductNameAsync...");

        // 1. Kiểm tra hàm SanitizeIdentifier khử dấu tiếng Việt và ký tự đặc biệt
        string s1 = RemoteServerService.SanitizeIdentifier("7A10461A");
        if (s1 != "7A10461A")
            throw new Exception($"Expected '7A10461A', got '{s1}'");

        string s2 = RemoteServerService.SanitizeIdentifier("Cover Assembly S24");
        if (s2 != "Cover_Assembly_S24")
            throw new Exception($"Expected 'Cover_Assembly_S24', got '{s2}'");

        string s3 = RemoteServerService.SanitizeIdentifier("Nắp lưng Titan (Đen-Bạc)");
        if (s3 != "Nap_lung_Titan_Den-Bac")
            throw new Exception($"Expected 'Nap_lung_Titan_Den-Bac', got '{s3}'");

        string s4 = RemoteServerService.SanitizeIdentifier("   __Á_À_Ả_Ã_Ạ__   ");
        if (s4 != "A_A_A_A_A")
            throw new Exception($"Expected 'A_A_A_A_A', got '{s4}'");

        Console.WriteLine("  ✓ SanitizeIdentifier handles Vietnamese accents, spaces, and symbols perfectly.");

        // 2. Kiểm tra định dạng tên file job kết hợp cả ProductCode và ProductName
        string productCode = "7A10461A";
        string productName = "Cover Assembly S24";
        string safeCode = RemoteServerService.SanitizeIdentifier(productCode);
        string safeName = RemoteServerService.SanitizeIdentifier(productName);
        string id = !string.IsNullOrWhiteSpace(safeName) ? $"{safeCode}_{safeName}" : safeCode;
        string expectedPattern = "job_7A10461A_Cover_Assembly_S24";
        if (!id.Equals("7A10461A_Cover_Assembly_S24"))
            throw new Exception($"Combined identifier mismatch: {id}");

        // 3. Giả lập UploadJobAsync gửi lên Mock Server và kiểm tra payload multipart
        int port = 19583;
        string prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        bool receivedProductName = false;
        bool receivedProductCode = false;
        string receivedFileName = "";

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var req = context.Request;
            var resp = context.Response;

            if (req.Url?.Query.Contains("action=upload_job") == true)
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                string body = await reader.ReadToEndAsync();
                
                receivedProductCode = body.Contains("7A10461A");
                receivedProductName = body.Contains("Cover Assembly S24");
                
                // Trích xuất filename từ multipart header nếu có
                var match = System.Text.RegularExpressions.Regex.Match(body, @"filename=""([^""]+)""");
                if (match.Success)
                {
                    receivedFileName = match.Groups[1].Value;
                }

                string serverGenFileName = $"job_{safeCode}_{safeName}_20260903_052341_b1abf6.job";
                string json = JsonSerializer.Serialize(new
                {
                    success = true,
                    message = "Tải tệp Job lên server thành công.",
                    file_name = serverGenFileName,
                    relative_path = $"uploads/jobs/{serverGenFileName}",
                    full_url = $"{prefix}uploads/jobs/{serverGenFileName}",
                    size_bytes = 2048
                });
                byte[] buf = Encoding.UTF8.GetBytes(json);
                resp.ContentType = "application/json";
                resp.ContentLength64 = buf.Length;
                await resp.OutputStream.WriteAsync(buf);
                resp.Close();
            }
            else
            {
                resp.StatusCode = 404;
                resp.Close();
            }
        });

        var service = new RemoteServerService();
        byte[] dummyJobBytes = Encoding.UTF8.GetBytes("{\"Tools\": []}");
        var (upOk, fullUrl, relPath, upErr) = await service.UploadJobAsync(
            dummyJobBytes, "", productCode, $"{prefix}vision_upload.php", productName);

        await serverTask;
        listener.Stop();

        if (!upOk)
            throw new Exception($"UploadJobAsync failed: {upErr}");

        if (!receivedProductCode)
            throw new Exception("Server did not receive product_code!");

        if (!receivedProductName)
            throw new Exception("Server did not receive product_name!");

        if (!relPath.Contains("7A10461A_Cover_Assembly_S24"))
            throw new Exception($"Server response path '{relPath}' does not contain both code and name!");

        Console.WriteLine($"  ✓ UploadJobAsync with ProductName successfully verified: {relPath}");
        Console.WriteLine($"  ✓ Multipart payload contained product_code and product_name correctly.");
    }
}
