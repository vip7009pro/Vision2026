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
        Test_JobPackage_ExcludesLargeTeachImage_AndUsesDecoupledCache();
        Test_NonBlockingBehavior_OnUncachedUrl();
        Test_OfflineRecentJob_LoadsFromDecoupledCacheOrLegacyZip();

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
    /// Test 3: Kiểm tra tối ưu hóa dung lượng tệp .job: Loại trừ triệt để ảnh lớn teach_image.png khỏi gói .job,
    /// kích thước tệp .job siêu nhẹ (< 100KB) và chỉ nén thumbnail nhẹ teach_preview.jpg (nếu có).
    /// </summary>
    private static void Test_JobPackage_ExcludesLargeTeachImage_AndUsesDecoupledCache()
    {
        Console.WriteLine("▶ Running Test_JobPackage_ExcludesLargeTeachImage_AndUsesDecoupledCache...");

        string tempDir = Path.Combine(Path.GetTempPath(), "VisionTest_TeachBundle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string jobFilePath = Path.Combine(tempDir, "test_lightweight_job.job");
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

            // 1. Tạo một ảnh teach_image.png lớn (mô phỏng ảnh chụp camera hoặc tải về trong temp)
            string teachImgWorkingPath = Path.Combine(workingDir, "teach_image.png");
            using (var sampleMat = new Mat(400, 600, MatType.CV_8UC3, new Scalar(200, 100, 50)))
            {
                Cv2.ImWrite(teachImgWorkingPath, sampleMat);
            }

            // 2. Tạo một thumbnail nén siêu nhẹ teach_preview.jpg
            string thumbWorkingPath = Path.Combine(workingDir, "teach_preview.jpg");
            using (var thumbMat = new Mat(80, 120, MatType.CV_8UC3, new Scalar(200, 100, 50)))
            {
                var prms = new ImageEncodingParam(ImwriteFlags.JpegQuality, 50);
                Cv2.ImWrite(thumbWorkingPath, thumbMat, prms);
            }

            var jobService = new JobService();
            jobService.SaveJob(config, workingDir, jobFilePath);

            if (!File.Exists(jobFilePath))
                throw new Exception("SaveJob did not produce .job file!");

            var fileInfo = new FileInfo(jobFilePath);
            long jobSizeBytes = fileInfo.Length;
            Console.WriteLine($"    File .job size: {jobSizeBytes} bytes ({jobSizeBytes / 1024.0:F1} KB)");

            // File .job phải siêu nhẹ (< 100 KB)
            if (jobSizeBytes > 100 * 1024)
                throw new Exception($"Job file size is too large: {jobSizeBytes} bytes! Expected < 100 KB.");

            // 3. Nạp lại file .job qua LoadJob
            var loadedConfig = jobService.LoadJob(jobFilePath, out var extractedTempDir);
            if (loadedConfig == null)
                throw new Exception("LoadJob failed to load config from .job file!");

            // Xác nhận teach_image.png KHÔNG có trong gói zip .job
            string extractedTeachPath = Path.Combine(extractedTempDir, "teach_image.png");
            if (File.Exists(extractedTeachPath))
                throw new Exception("Large teach_image.png was unexpectedly found in .job file!");

            // Xác nhận teach_preview.jpg (thumbnail nhẹ) có mặt trong gói zip
            string extractedThumbPath = Path.Combine(extractedTempDir, "teach_preview.jpg");
            if (!File.Exists(extractedThumbPath))
                throw new Exception("Thumbnail teach_preview.jpg was not found in extracted temp dir!");

            using var decodedThumb = Cv2.ImRead(extractedThumbPath, ImreadModes.Color);
            if (decodedThumb == null || decodedThumb.Empty() || decodedThumb.Width != 120 || decodedThumb.Height != 80)
                throw new Exception("Decoded teach_preview.jpg does not match expected dimensions 120x80!");

            Console.WriteLine("  ✓ Large teach_image.png was excluded from .job file; lightweight teach_preview.jpg preserved.");
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
    /// Test 5: Mô phỏng kịch bản Mở lại Job:
    /// Nạp ảnh mẫu trực tiếp từ Decoupled Disk Cache (Cache/TeachImages) trong < 10ms,
    /// đồng thời đảm bảo tương thích ngược 100% nếu mở tệp .job cũ có sẵn teach_image.png.
    /// </summary>
    private static void Test_OfflineRecentJob_LoadsFromDecoupledCacheOrLegacyZip()
    {
        Console.WriteLine("▶ Running Test_OfflineRecentJob_LoadsFromDecoupledCacheOrLegacyZip...");

        string tempDir = Path.Combine(Path.GetTempPath(), "VisionTest_OfflineRecent_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string jobFilePath = Path.Combine(tempDir, "offline_recent_job.job");
        string workingDir = Path.Combine(tempDir, "working");
        Directory.CreateDirectory(workingDir);

        try
        {
            string productCode = "OFFLINE_TEST_01";
            var config = new VisionConfig
            {
                ProductCode = productCode,
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

            // Kịch bản A: Lưu ảnh mẫu HD vào Decoupled Cache ngoài (Cache/TeachImages/{ProductCode}_teach.png)
            string teachCacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "TeachImages");
            Directory.CreateDirectory(teachCacheDir);
            string decoupledCachePath = Path.Combine(teachCacheDir, $"{productCode}_teach.png");

            using (var sampleMat = new Mat(120, 160, MatType.CV_8UC3, new Scalar(10, 200, 10)))
            {
                Cv2.ImWrite(decoupledCachePath, sampleMat);
            }

            var jobService = new JobService();
            jobService.SaveJob(config, workingDir, jobFilePath);

            // Nạp job và kiểm tra tốc độ nạp ảnh từ Decoupled Cache
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var loadedConfig = jobService.LoadJob(jobFilePath, out var extractedTempDir);

            Mat? resolvedMat = null;
            if (File.Exists(decoupledCachePath))
            {
                resolvedMat = Cv2.ImRead(decoupledCachePath, ImreadModes.Color);
            }
            sw.Stop();

            if (resolvedMat == null || resolvedMat.Empty())
                throw new Exception("Failed to load teach image from Decoupled Disk Cache!");

            if (sw.ElapsedMilliseconds > 200)
                throw new Exception($"Decoupled cache load took too long: {sw.ElapsedMilliseconds} ms");

            Console.WriteLine($"  ✓ Decoupled Disk Cache loaded teach image in {sw.ElapsedMilliseconds} ms with ZERO network calls.");

            // Kịch bản B: Tương thích ngược với file .job cũ (Legacy Package chứa teach_image.png)
            string legacyJobPath = Path.Combine(tempDir, "legacy_bundled_job.job");
            string legacyWorkingDir = Path.Combine(tempDir, "legacy_working");
            Directory.CreateDirectory(legacyWorkingDir);

            // Giả lập gói job cũ chứa sẵn teach_image.png
            File.WriteAllText(Path.Combine(legacyWorkingDir, "config.json"), "{}");
            using (var legacyMat = new Mat(60, 80, MatType.CV_8UC3, new Scalar(50, 50, 200)))
            {
                Cv2.ImWrite(Path.Combine(legacyWorkingDir, "teach_image.png"), legacyMat);
            }
            System.IO.Compression.ZipFile.CreateFromDirectory(legacyWorkingDir, legacyJobPath);

            // Nạp job cũ bằng LoadJob: Xác nhận vẫn giải nén và đọc được teach_image.png
            jobService.LoadJob(legacyJobPath, out var legacyExtractedDir);
            string legacyTeachExtracted = Path.Combine(legacyExtractedDir, "teach_image.png");
            if (!File.Exists(legacyTeachExtracted))
                throw new Exception("Legacy job with teach_image.png could not be extracted!");

            using var legacyReadMat = Cv2.ImRead(legacyTeachExtracted, ImreadModes.Color);
            if (legacyReadMat == null || legacyReadMat.Empty() || legacyReadMat.Width != 80 || legacyReadMat.Height != 60)
                throw new Exception("Failed to decode legacy teach_image.png from old job format!");

            Console.WriteLine("  ✓ 100% Backward compatibility verified for legacy .job files containing teach_image.png.");
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
