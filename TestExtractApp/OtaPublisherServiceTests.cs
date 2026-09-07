using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VisionInspectionApp.Application.Services;
using VisionInspectionApp.Models.Ota;

namespace TestExtractApp;

public static class OtaPublisherServiceTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("🧪 RUNNING TESTS: OTA PUBLISHER SERVICE TESTS");
        Console.WriteLine("=======================================================");

        TestZipCompressionAndSha256().GetAwaiter().GetResult();
        TestCsprojVersionUpdate().GetAwaiter().GetResult();
        TestMockHttpUploadWithCustomServerFolder().GetAwaiter().GetResult();
        TestChunkedHttpUploadWithMultipleChunks().GetAwaiter().GetResult();
        TestEndToEndPublisherToOtaUpdateService().GetAwaiter().GetResult();

        Console.WriteLine("=======================================================");
        Console.WriteLine("✅ ALL OTA PUBLISHER SERVICE TESTS PASSED (100%)!");
        Console.WriteLine("=======================================================\n");
    }

    private static async Task TestZipCompressionAndSha256()
    {
        Console.WriteLine("--- Test 1: Kiểm tra Gom Nén Zip & Tính Mã Băm SHA-256 ---");

        string testRoot = Path.Combine(Path.GetTempPath(), "VisionOtaPublishTest_" + Guid.NewGuid().ToString("N"));
        string sourceDir = Path.Combine(testRoot, "source");
        string outZip = Path.Combine(testRoot, "output", "test_package.zip");

        try
        {
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(Path.Combine(sourceDir, "subfolder"));
            Directory.CreateDirectory(Path.Combine(sourceDir, "backup")); // Thư mục cần loại trừ

            await File.WriteAllTextAsync(Path.Combine(sourceDir, "VisionInspectionApp.UI.exe"), "EXE DUMMY BINARY CONTENT");
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "OpenCvSharp.dll"), "DLL CONTENT");
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "subfolder", "config.json"), "{\"app\":\"test\"}");
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "temp.tmp"), "TMP CONTENT"); // Cần loại trừ
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "backup", "old.dll"), "OLD BACKUP"); // Cần loại trừ

            var publisher = new OtaPublisherService();
            double lastProgress = 0;
            var progress = new Progress<double>(p => lastProgress = p);

            string resultZip = await publisher.BuildZipPackageAsync(sourceDir, outZip, progress);

            if (!File.Exists(resultZip))
                throw new Exception("Tệp zip đầu ra không được tạo.");

            if (lastProgress < 99.9)
                throw new Exception($"Tiến độ nén zip không đạt 100% (thực tế: {lastProgress}%).");

            // Kiểm tra các tệp bên trong zip
            using (var archive = ZipFile.OpenRead(resultZip))
            {
                bool hasExe = false;
                bool hasDll = false;
                bool hasSubConfig = false;
                bool hasTmp = false;
                bool hasBackup = false;

                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.Equals("VisionInspectionApp.UI.exe", StringComparison.OrdinalIgnoreCase)) hasExe = true;
                    if (entry.FullName.Equals("OpenCvSharp.dll", StringComparison.OrdinalIgnoreCase)) hasDll = true;
                    if (entry.FullName.Replace('\\', '/').Equals("subfolder/config.json", StringComparison.OrdinalIgnoreCase)) hasSubConfig = true;
                    if (entry.FullName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) hasTmp = true;
                    if (entry.FullName.Replace('\\', '/').StartsWith("backup/", StringComparison.OrdinalIgnoreCase)) hasBackup = true;
                }

                if (!hasExe || !hasDll || !hasSubConfig)
                    throw new Exception("Tệp zip thiếu các tệp ứng dụng hợp lệ.");

                if (hasTmp)
                    throw new Exception("Tệp zip không loại trừ tệp .tmp theo yêu cầu.");

                if (hasBackup)
                    throw new Exception("Tệp zip không loại trừ thư mục backup/ theo yêu cầu.");
            }

            // Kiểm tra hàm tính SHA-256
            string computedHash = publisher.ComputeSha256(resultZip);
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(resultZip);
            string expectedHash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();

            if (!string.Equals(computedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Mã băm SHA-256 không khớp: {computedHash} vs {expectedHash}");

            Console.WriteLine($"  -> PASSED: Nén zip thành công ({new FileInfo(resultZip).Length} bytes), SHA-256 = {computedHash[..12]}..., loại trừ tệp rác chuẩn xác.");
        }
        finally
        {
            try { Directory.Delete(testRoot, true); } catch { }
        }
    }

    private static async Task TestCsprojVersionUpdate()
    {
        Console.WriteLine("--- Test 2: Kiểm tra Tự Động Cập Nhật Version Vào File .csproj ---");

        string testCsproj = Path.Combine(Path.GetTempPath(), "TestApp_" + Guid.NewGuid().ToString("N") + ".csproj");
        string originalXml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Version>1.0.0.0</Version>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
    <InformationalVersion>1.0.0.0</InformationalVersion>
  </PropertyGroup>
</Project>";

        try
        {
            await File.WriteAllTextAsync(testCsproj, originalXml);

            var publisher = new OtaPublisherService();
            string newTargetVersion = "2.1.0.5";
            bool updated = await publisher.UpdateCsprojVersionAsync(testCsproj, newTargetVersion);

            if (!updated)
                throw new Exception("Hàm UpdateCsprojVersionAsync trả về false.");

            string updatedXml = await File.ReadAllTextAsync(testCsproj);
            if (!updatedXml.Contains("<Version>2.1.0.5</Version>") ||
                !updatedXml.Contains("<AssemblyVersion>2.1.0.5</AssemblyVersion>") ||
                !updatedXml.Contains("<FileVersion>2.1.0.5</FileVersion>") ||
                !updatedXml.Contains("<InformationalVersion>2.1.0.5</InformationalVersion>"))
            {
                throw new Exception($"Nội dung .csproj sau khi cập nhật không chứa phiên bản mới: {updatedXml}");
            }

            Console.WriteLine($"  -> PASSED: Cập nhật thẻ Version, AssemblyVersion, FileVersion, InformationalVersion sang '2.1.0.5' thành công 100%.");
        }
        finally
        {
            try { File.Delete(testCsproj); } catch { }
        }
    }

    private static async Task TestMockHttpUploadWithCustomServerFolder()
    {
        Console.WriteLine("--- Test 3: Kiểm tra Upload Multipart Lên Máy Chủ & Tùy Chỉnh Thư Mục Server ---");

        int port = 59134;
        string prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        string tempZip = Path.Combine(Path.GetTempPath(), "pkg_" + Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllTextAsync(tempZip, "DUMMY ZIP ARCHIVE PAYLOAD");

        string receivedTargetDir = "";
        string receivedVersion = "";
        string receivedApiToken = "";
        bool receivedFile = false;

        var serverTask = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();

            receivedTargetDir = ExtractMultipartField(body, "target_dir");
            receivedVersion = ExtractMultipartField(body, "version");
            receivedApiToken = ExtractMultipartField(body, "api_token");

            if (body.IndexOf("package_file", StringComparison.OrdinalIgnoreCase) >= 0 &&
                body.IndexOf("filename", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                receivedFile = true;
            }

            // Trả về JSON phản hồi mô phỏng ota_server.php
            var respObj = new
            {
                success = true,
                message = "Phát hành bản cập nhật thành công!",
                version = receivedVersion,
                download_url = $"{prefix}{receivedTargetDir}/VisionUpdate_v{receivedVersion}.zip",
                manifest_url = $"{prefix}version.json",
                server_folder = receivedTargetDir,
                file_size = 1024,
                sha256 = "dummy_sha256_hash"
            };

            byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(respObj));
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = jsonBytes.Length;
            await ctx.Response.OutputStream.WriteAsync(jsonBytes);
            ctx.Response.Close();
        });

        try
        {
            var publisher = new OtaPublisherService();
            var manifest = new UpdateManifest
            {
                AppName = "CMS VINA Vision System",
                Version = "1.5.0.0",
                Channel = "Beta",
                IsMandatory = true,
                ReleaseNotes = "Bản thử nghiệm Beta 1.5"
            };

            var publishConfig = new OtaPublishConfig
            {
                ServerUploadUrl = $"{prefix}ota_server.php?action=publish",
                ServerStorageFolder = "custom_releases/sub_line_01",
                ApiToken = "SECRET_TOKEN_999",
                TargetVersion = "1.5.0.0"
            };

            var result = await publisher.PublishToServerAsync(publishConfig, tempZip, manifest);
            await serverTask;

            if (!result.Success)
                throw new Exception($"PublishToServerAsync thất bại: {result.ErrorMessage}");

            if (!receivedFile)
                throw new Exception("Máy chủ không nhận được tệp zip đính kèm.");

            if (!string.Equals(receivedTargetDir, "custom_releases/sub_line_01", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Máy chủ nhận sai thư mục lưu trữ: '{receivedTargetDir}' (kỳ vọng: 'custom_releases/sub_line_01')");

            if (!string.Equals(receivedVersion, "1.5.0.0", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Máy chủ nhận sai version: '{receivedVersion}'");

            if (!string.Equals(receivedApiToken, "SECRET_TOKEN_999", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Máy chủ nhận sai API Token: '{receivedApiToken}'");

            if (!result.DownloadUrl!.Contains("custom_releases/sub_line_01"))
                throw new Exception($"DownloadUrl phản hồi không chứa thư mục đã cấu hình: '{result.DownloadUrl}'");

            Console.WriteLine($"  -> PASSED: Upload HTTP multipart thành công, truyền chuẩn xác thư mục server '{receivedTargetDir}', version '{receivedVersion}' và Token.");
        }
        finally
        {
            listener.Stop();
            try { File.Delete(tempZip); } catch { }
        }
    }

    private static async Task TestChunkedHttpUploadWithMultipleChunks()
    {
        Console.WriteLine("--- Test 4: Kiểm tra Tải Lên Phân Đoạn (Chunked Upload) Cho Gói Lớn ---");

        int port = 59136;
        string prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        string tempLargeZip = Path.Combine(Path.GetTempPath(), "large_pkg_" + Guid.NewGuid().ToString("N") + ".zip");
        // Tạo file 7 MB (> 6 MB ChunkSize) để bắt buộc kích hoạt Chunked Upload
        byte[] dummyData = new byte[7 * 1024 * 1024];
        new Random(42).NextBytes(dummyData);
        await File.WriteAllBytesAsync(tempLargeZip, dummyData);

        var receivedChunkIndices = new System.Collections.Generic.List<int>();
        int receivedTotalChunks = 0;
        string receivedFileId = "";

        var serverTask = Task.Run(async () =>
        {
            // Nhận 2 phân đoạn (Chunk 0: 6MB, Chunk 1: 1MB)
            for (int i = 0; i < 2; i++)
            {
                var ctx = await listener.GetContextAsync();
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                string body = await reader.ReadToEndAsync();

                string idxStr = ExtractMultipartField(body, "chunk_index");
                string totStr = ExtractMultipartField(body, "total_chunks");
                receivedFileId = ExtractMultipartField(body, "file_id");

                if (int.TryParse(idxStr, out int cIdx))
                    receivedChunkIndices.Add(cIdx);
                if (int.TryParse(totStr, out int cTot))
                    receivedTotalChunks = cTot;

                object respObj;
                if (i == 0)
                {
                    respObj = new
                    {
                        success = true,
                        message = "Đã nhận thành công phân đoạn 1/2.",
                        chunk_index = 0,
                        total_chunks = 2,
                        is_finished = false
                    };
                }
                else
                {
                    respObj = new
                    {
                        success = true,
                        message = "Phát hành bản cập nhật v3.0.0.0 thành công!",
                        version = "3.0.0.0",
                        download_url = $"{prefix}update/VisionUpdate_v3.0.0.0.zip",
                        manifest_url = $"{prefix}version.json",
                        server_folder = "update",
                        file_size = dummyData.Length,
                        sha256 = "test_chunked_sha256"
                    };
                }

                byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(respObj));
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = jsonBytes.Length;
                await ctx.Response.OutputStream.WriteAsync(jsonBytes);
                ctx.Response.Close();
            }
        });

        try
        {
            var publisher = new OtaPublisherService();
            var manifest = new UpdateManifest
            {
                AppName = "CMS VINA Vision System",
                Version = "3.0.0.0",
                Channel = "Stable",
                IsMandatory = false,
                ReleaseNotes = "Kiểm tra Chunked Upload 7MB"
            };

            var publishConfig = new OtaPublishConfig
            {
                ServerUploadUrl = $"{prefix}ota_server.php",
                ServerStorageFolder = "update",
                TargetVersion = "3.0.0.0"
            };

            var result = await publisher.PublishToServerAsync(publishConfig, tempLargeZip, manifest);
            await serverTask;

            if (!result.Success)
                throw new Exception($"Chunked Upload thất bại: {result.ErrorMessage}");

            if (receivedChunkIndices.Count != 2 || receivedChunkIndices[0] != 0 || receivedChunkIndices[1] != 1)
                throw new Exception($"Thứ tự phân đoạn nhận được không đúng: {string.Join(", ", receivedChunkIndices)}");

            if (receivedTotalChunks != 2)
                throw new Exception($"Tổng số phân đoạn nhận được không đúng: {receivedTotalChunks} (kỳ vọng: 2)");

            if (string.IsNullOrWhiteSpace(receivedFileId))
                throw new Exception("Không nhận được file_id của phiên upload phân đoạn.");

            if (!string.Equals(result.Version, "3.0.0.0", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Version trả về không đúng: {result.Version}");

            Console.WriteLine($"  -> PASSED: Chunked Upload thành công 2 phân đoạn ({dummyData.Length / (1024.0 * 1024.0):F1} MB), FileId={receivedFileId}, Version={result.Version}.");
        }
        finally
        {
            listener.Stop();
            try { File.Delete(tempLargeZip); } catch { }
        }
    }

    private static async Task TestEndToEndPublisherToOtaUpdateService()
    {
        Console.WriteLine("--- Test 4: Kiểm tra Khớp Nối Toàn Diện Giữa Publisher Và Receiver (OtaUpdateService) ---");

        int port = 59135;
        string prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var publishedManifest = new UpdateManifest
        {
            AppName = "CMS VINA Vision System",
            Version = "4.0.0.0",
            ReleaseDate = DateTime.UtcNow,
            Channel = "Stable",
            IsMandatory = false,
            DownloadUrl = $"{prefix}uploads/ota_packages/VisionUpdate_v4.0.0.0.zip",
            FileSize = 5242880,
            Sha256 = "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9",
            ReleaseNotes = "Tính năng tự động đóng gói OTA mới ra mắt"
        };

        var serverTask = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(publishedManifest));
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        });

        var otaUpdateService = new OtaUpdateService();
        var checkResult = await otaUpdateService.CheckForUpdateAsync($"{prefix}version.json", "CustomManifest");

        await serverTask;
        listener.Stop();

        if (checkResult.Status != UpdateCheckStatus.UpdateAvailable && checkResult.Status != UpdateCheckStatus.MandatoryUpdate)
        {
            throw new Exception($"OtaUpdateService không nhận ra bản mới. Status: {checkResult.Status}, Error: {checkResult.ErrorMessage}");
        }

        if (!string.Equals(checkResult.Manifest?.Version, "4.0.0.0", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"Phiên bản nhận được không đúng: '{checkResult.Manifest?.Version}'");
        }

        Console.WriteLine($"  -> PASSED: Khớp nối 100% giữa Publisher và OtaUpdateService (Nhận diện bản v4.0.0.0, HasUpdate = True).");
    }

    private static string ExtractMultipartField(string body, string fieldName)
    {
        int nameIdx = body.IndexOf($"name={fieldName}", StringComparison.OrdinalIgnoreCase);
        if (nameIdx < 0) nameIdx = body.IndexOf($"name=\"{fieldName}\"", StringComparison.OrdinalIgnoreCase);
        if (nameIdx < 0) return "";

        int headerEnd = body.IndexOf("\r\n\r\n", nameIdx);
        if (headerEnd < 0) headerEnd = body.IndexOf("\n\n", nameIdx);
        if (headerEnd < 0) return "";
        while (headerEnd < body.Length && (body[headerEnd] == '\r' || body[headerEnd] == '\n'))
            headerEnd++;

        int nextBoundary = body.IndexOf("--", headerEnd);
        if (nextBoundary < 0) nextBoundary = body.Length;
        return body.Substring(headerEnd, nextBoundary - headerEnd).Trim();
    }
}
