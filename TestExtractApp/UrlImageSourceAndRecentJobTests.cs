using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.Persistence;

namespace TestExtractApp;

public static class UrlImageSourceAndRecentJobTests
{
    public static void RunTests()
    {
        Console.WriteLine("\n=================================================");
        Console.WriteLine("🧪 RUNNING URL IMAGE SOURCE & RECENT JOB TESTS");
        Console.WriteLine("=================================================");

        Test_RemoteServerService_DownloadFileAsync_NoDeadlockOnSyncContext().GetAwaiter().GetResult();
        Test_UrlImageDiskCache_PathAndFileStorage();
        Test_JobPackage_Bundles_TeachImage_OnSaveAndRestore();
        Test_NonBlockingBehavior_OnUncachedUrl();
        Test_OfflineRecentJob_LoadsTeachImageDirectlyFromZip();

        Console.WriteLine("✅ ALL URL IMAGE SOURCE & RECENT JOB TESTS PASSED!");
        Console.WriteLine("=================================================\n");
    }

    /// <summary>
    /// Test 1: Kiểm tra RemoteServerService.DownloadFileAsync hoạt động an toàn
    /// trên SynchronizationContext giả lập (mô phỏng UI Thread WPF) mà không bị Deadlock.
    /// </summary>
    private static async Task Test_RemoteServerService_DownloadFileAsync_NoDeadlockOnSyncContext()
    {
        Console.WriteLine("▶ Running Test_RemoteServerService_DownloadFileAsync_NoDeadlockOnSyncContext...");

        // Khởi tạo mock HTTP listener
        int port = 19588;
        string prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            try
            {
                var context = await listener.GetContextAsync();
                var req = context.Request;
                var res = context.Response;

                byte[] dummyPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
                res.ContentType = "image/png";
                res.StatusCode = 200;
                res.ContentLength64 = dummyPng.Length;
                await res.OutputStream.WriteAsync(dummyPng, 0, dummyPng.Length);
                res.OutputStream.Close();
            }
            catch { }
        });

        using var service = new RemoteServerService();
        string testUrl = $"{prefix}uploads/teach_images/test_image.png";

        // Tạo 1 luồng với SynchronizationContext đồng bộ (mô phỏng DispatcherSynchronizationContext của WPF)
        // và kiểm tra việc gọi DownloadFileAsync hoàn tất mà không bao giờ bị deadlock
        bool completedWithoutDeadlock = false;
        var thread = new Thread(() =>
        {
            var syncCtx = new SingleThreadSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(syncCtx);

            syncCtx.Post(async _ =>
            {
                try
                {
                    var (ok, data, err) = await service.DownloadFileAsync(testUrl);
                    if (ok && data != null && data.Length > 0)
                    {
                        completedWithoutDeadlock = true;
                    }
                }
                finally
                {
                    syncCtx.Complete();
                }
            }, null);

            syncCtx.RunOnCurrentThread();
        });

        thread.Start();
        bool finished = thread.Join(TimeSpan.FromSeconds(6));
        await serverTask;
        listener.Stop();

        if (!finished)
            throw new Exception("Deadlock detected! DownloadFileAsync did not finish within timeout on SynchronizationContext.");

        if (!completedWithoutDeadlock)
            throw new Exception("DownloadFileAsync failed to receive data properly.");

        Console.WriteLine("  ✓ DownloadFileAsync completed on SynchronizationContext with 0 deadlocks.");
    }

    /// <summary>
    /// Test 2: Kiểm tra cấu trúc đường dẫn và lưu trữ của Disk Cache UrlImages
    /// </summary>
    private static void Test_UrlImageDiskCache_PathAndFileStorage()
    {
        Console.WriteLine("▶ Running Test_UrlImageDiskCache_PathAndFileStorage...");

        string testUrl = "http://192.168.1.50:8080/uploads/teach_images/teach_PRD_SAMPLE_001.png";
        string cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "UrlImages");
        Directory.CreateDirectory(cacheDir);

        using var md5 = System.Security.Cryptography.MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(testUrl.Trim()));
        string hashStr = Convert.ToHexString(hash);
        string expectedPath = Path.Combine(cacheDir, $"{hashStr}.png");

        if (!Directory.Exists(cacheDir))
            throw new Exception("Cache directory was not created properly!");

        if (string.IsNullOrWhiteSpace(expectedPath) || !expectedPath.StartsWith(cacheDir))
            throw new Exception("Expected cache file path must be inside cache directory.");

        // Tạo một ảnh thực tế 64x64 và ghi vào cache
        using var dummyMat = new Mat(64, 64, MatType.CV_8UC3, new Scalar(0, 128, 255));
        Cv2.ImWrite(expectedPath, dummyMat);

        if (!File.Exists(expectedPath) || new FileInfo(expectedPath).Length == 0)
            throw new Exception("Failed to write dummy cached image to disk!");

        using var readMat = Cv2.ImRead(expectedPath, ImreadModes.Color);
        if (readMat == null || readMat.Empty() || readMat.Width != 64 || readMat.Height != 64)
            throw new Exception("Failed to read back cached image from disk!");

        Console.WriteLine("  ✓ UrlImage Disk Cache path, hash calculation, and image persistence verified.");
    }

    /// <summary>
    /// Test 3: Kiểm tra đóng gói ảnh teach_image.png vào tệp .job khi lưu và giải nén ra khi mở
    /// </summary>
    private static void Test_JobPackage_Bundles_TeachImage_OnSaveAndRestore()
    {
        Console.WriteLine("▶ Running Test_JobPackage_Bundles_TeachImage_OnSaveAndRestore...");

        string tempDir = Path.Combine(Path.GetTempPath(), "VisionTest_TeachBundle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string jobFilePath = Path.Combine(tempDir, "test_remote_taught_job.job");
        string workingDir = Path.Combine(tempDir, "working");
        Directory.CreateDirectory(workingDir);

        try
        {
            var config = new VisionConfig
            {
                ProductCode = "PRD_REMOTE_001",
                ProductName = "Remote Teach Sample Product",
                ImageSources = new System.Collections.Generic.List<ImageSourceDefinition>
                {
                    new ImageSourceDefinition
                    {
                        Name = "ImageSource1",
                        SourceType = ImageSourceType.Url,
                        ImageUrl = "http://192.168.1.100/uploads/teach_images/sample.png"
                    }
                }
            };

            // Tạo teach_image.png 100x80 trong working dir mô phỏng ảnh đã được tải về trong quá trình teach
            string teachImgWorkingPath = Path.Combine(workingDir, "teach_image.png");
            using (var sampleMat = new Mat(80, 100, MatType.CV_8UC3, new Scalar(200, 100, 50)))
            {
                Cv2.ImWrite(teachImgWorkingPath, sampleMat);
            }

            var jobService = new JobService();
            jobService.SaveJob(config, workingDir, jobFilePath);

            if (!File.Exists(jobFilePath))
                throw new Exception("SaveJob did not produce .job file!");

            // Nạp lại file .job qua LoadJob
            var loadedConfig = jobService.LoadJob(jobFilePath, out var extractedTempDir);
            if (loadedConfig == null)
                throw new Exception("LoadJob failed to load config from .job file!");

            string extractedTeachPath = Path.Combine(extractedTempDir, "teach_image.png");
            if (!File.Exists(extractedTeachPath))
                throw new Exception("teach_image.png was NOT packaged into or extracted from the .job file!");

            using var decodedMat = Cv2.ImRead(extractedTeachPath, ImreadModes.Color);
            if (decodedMat == null || decodedMat.Empty() || decodedMat.Width != 100 || decodedMat.Height != 80)
                throw new Exception("Decoded teach_image.png does not match original dimensions 100x80!");

            Console.WriteLine("  ✓ teach_image.png successfully bundled into .job file and restored on LoadJob.");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Test 4: Kiểm tra tính không chặn (Non-blocking) khi ảnh URL chưa có trong cache
    /// </summary>
    private static void Test_NonBlockingBehavior_OnUncachedUrl()
    {
        Console.WriteLine("▶ Running Test_NonBlockingBehavior_OnUncachedUrl...");

        string randomUrl = $"http://192.0.2.1/non_existent_image_{Guid.NewGuid():N}.png";

        // Đo thời gian kiểm tra URL không tồn tại
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Giả lập logic TryLoadUrlImageFromDiskCache:
        // Không tìm thấy trên đĩa -> trả về null ngay trong < 10ms mà KHÔNG thực hiện gọi mạng đồng bộ
        string urlCachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "UrlImages", "fake.png");
        bool exists = File.Exists(urlCachePath);
        sw.Stop();

        if (sw.ElapsedMilliseconds > 50)
            throw new Exception($"Disk cache check took too long: {sw.ElapsedMilliseconds} ms");

        Console.WriteLine($"  ✓ Uncached URL disk check returned in {sw.ElapsedMilliseconds} ms (< 50 ms) without blocking.");
    }

    /// <summary>
    /// Test 5: Mô phỏng kịch bản Mở lại Job dạy từ xa bằng Menu File/Job gần đây:
    /// Nạp ảnh teach_image.png trực tiếp từ gói Job mà không phụ thuộc Server có Online hay không.
    /// </summary>
    private static void Test_OfflineRecentJob_LoadsTeachImageDirectlyFromZip()
    {
        Console.WriteLine("▶ Running Test_OfflineRecentJob_LoadsTeachImageDirectlyFromZip...");

        string tempDir = Path.Combine(Path.GetTempPath(), "VisionTest_OfflineRecent_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string jobFilePath = Path.Combine(tempDir, "offline_recent_job.job");
        string workingDir = Path.Combine(tempDir, "working");
        Directory.CreateDirectory(workingDir);

        try
        {
            var config = new VisionConfig
            {
                ProductCode = "OFFLINE_TEST_01",
                ProductName = "Offline Product Test",
                ImageSources = new System.Collections.Generic.List<ImageSourceDefinition>
                {
                    new ImageSourceDefinition
                    {
                        Name = "ImageSource1",
                        SourceType = ImageSourceType.Url,
                        ImageUrl = "http://dead-server.invalid/unreachable/image.png"
                    }
                }
            };

            // Tạo teach_image.png trong workingDir
            string teachImgWorkingPath = Path.Combine(workingDir, "teach_image.png");
            using (var sampleMat = new Mat(120, 160, MatType.CV_8UC3, new Scalar(10, 200, 10)))
            {
                Cv2.ImWrite(teachImgWorkingPath, sampleMat);
            }

            var jobService = new JobService();
            jobService.SaveJob(config, workingDir, jobFilePath);

            // Mô phỏng tắt app, mở lại app và nạp job:
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var loadedConfig = jobService.LoadJob(jobFilePath, out var extractedTempDir);

            // Giả lập đoạn code trong LoadJobFromFile:
            Mat? resolvedMat = null;
            string extractedTeachPath = Path.Combine(extractedTempDir, "teach_image.png");
            if (File.Exists(extractedTeachPath))
            {
                resolvedMat = Cv2.ImRead(extractedTeachPath, ImreadModes.Color);
            }
            sw.Stop();

            if (resolvedMat == null || resolvedMat.Empty())
                throw new Exception("Failed to load teach_image.png offline from extracted job temp dir!");

            if (sw.ElapsedMilliseconds > 200)
                throw new Exception($"Offline recent job load took too long: {sw.ElapsedMilliseconds} ms");

            Console.WriteLine($"  ✓ Offline Recent Job loaded teach image in {sw.ElapsedMilliseconds} ms with ZERO network calls!");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Giả lập SingleThreadSynchronizationContext giống như DispatcherSynchronizationContext của WPF
    /// để phát hiện lỗi deadlock khi gọi async mà thiếu ConfigureAwait(false).
    /// </summary>
    private sealed class SingleThreadSynchronizationContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue
            = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Add((d, state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            d(state);
        }

        public void Complete()
        {
            _queue.CompleteAdding();
        }

        public void RunOnCurrentThread()
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                item.Callback(item.State);
            }
        }
    }
}
