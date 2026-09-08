using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models.Ota;

namespace VisionInspectionApp.Application.Services;

public class OtaPublisherService : IOtaPublisherService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(20) // Cho phép upload gói dung lượng lớn
    };

    public Task<string> BuildZipPackageAsync(string sourceDirectory, string outputZipPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException($"Thư mục nguồn không tồn tại: '{sourceDirectory}'");
            }

            string? parentDir = Path.GetDirectoryName(outputZipPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            if (File.Exists(outputZipPath))
            {
                File.Delete(outputZipPath);
            }

            var allFiles = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories);
            var normalizedZipPath = Path.GetFullPath(outputZipPath);

            // Lọc các tệp không cần thiết (tệp tạm, backup, git, chính tệp zip đang tạo)
            var filesToZip = new System.Collections.Generic.List<string>();
            foreach (var file in allFiles)
            {
                var fullPath = Path.GetFullPath(file);
                if (string.Equals(fullPath, normalizedZipPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                string rel = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                if (rel.StartsWith("backup/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith(".vs/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("updates/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("Cache/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("cache/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/android/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/android-", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/ios/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/ios-", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/linux/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/linux-", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/osx/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/osx-", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/browser/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/browser-", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/maccatalyst/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/maccatalyst-", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/unix/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/win-x86/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtimes/win-x86-", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("VisionUpdater", StringComparison.OrdinalIgnoreCase) ||
                    rel.Contains("/VisionUpdater", StringComparison.OrdinalIgnoreCase) ||
                    rel.EndsWith("updater_error.log", StringComparison.OrdinalIgnoreCase) ||
                    rel.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
                    rel.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                    rel.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                filesToZip.Add(file);
            }

            int total = filesToZip.Count;
            if (total == 0)
            {
                throw new InvalidOperationException($"Thư mục nguồn '{sourceDirectory}' không có tệp nào để nén.");
            }

            using var zipToOpen = new FileStream(outputZipPath, FileMode.Create);
            using var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create);

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();

                string filePath = filesToZip[i];
                string relativePath = Path.GetRelativePath(sourceDirectory, filePath);

                archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);

                double pct = (double)(i + 1) / total * 100.0;
                progress?.Report(pct);
            }

            return outputZipPath;
        }, ct);
    }

    public string ComputeSha256(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Không tìm thấy tệp để tính SHA-256: '{filePath}'");

        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = sha.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    public async Task<bool> UpdateCsprojVersionAsync(string csprojPath, string newVersion)
    {
        if (!File.Exists(csprojPath))
            return false;

        try
        {
            string content = await File.ReadAllTextAsync(csprojPath).ConfigureAwait(false);

            // Cập nhật các thẻ version trong csproj
            string patternVer = @"<Version>.*?<\/Version>";
            string patternAssembly = @"<AssemblyVersion>.*?<\/AssemblyVersion>";
            string patternFile = @"<FileVersion>.*?<\/FileVersion>";
            string patternInfo = @"<InformationalVersion>.*?<\/InformationalVersion>";

            bool changed = false;

            if (Regex.IsMatch(content, patternVer))
            {
                content = Regex.Replace(content, patternVer, $"<Version>{newVersion}</Version>");
                changed = true;
            }
            if (Regex.IsMatch(content, patternAssembly))
            {
                content = Regex.Replace(content, patternAssembly, $"<AssemblyVersion>{newVersion}</AssemblyVersion>");
                changed = true;
            }
            if (Regex.IsMatch(content, patternFile))
            {
                content = Regex.Replace(content, patternFile, $"<FileVersion>{newVersion}</FileVersion>");
                changed = true;
            }
            if (Regex.IsMatch(content, patternInfo))
            {
                content = Regex.Replace(content, patternInfo, $"<InformationalVersion>{newVersion}</InformationalVersion>");
                changed = true;
            }

            if (changed)
            {
                await File.WriteAllTextAsync(csprojPath, content).ConfigureAwait(false);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<OtaPublishResult> PublishToServerAsync(OtaPublishConfig config, string zipFilePath, UpdateManifest manifest, IProgress<UpdateProgressInfo>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(zipFilePath))
        {
            return new OtaPublishResult
            {
                Success = false,
                ErrorMessage = $"Tệp gói cập nhật không tồn tại: '{zipFilePath}'"
            };
        }

        if (string.IsNullOrWhiteSpace(config.ServerUploadUrl))
        {
            return new OtaPublishResult
            {
                Success = false,
                ErrorMessage = "Chưa cấu hình địa chỉ URL máy chủ upload."
            };
        }

        var fileInfo = new FileInfo(zipFilePath);
        long fileLength = fileInfo.Length;
        const int ChunkSize = 6 * 1024 * 1024; // 6 MB mỗi phân đoạn an toàn

        // Nếu tệp lớn hơn 6MB: Tự động dùng Chunked Upload để vượt qua mọi giới hạn post_max_size của hosting
        if (fileLength > ChunkSize)
        {
            return await PublishChunkedToServerAsync(config, zipFilePath, manifest, ChunkSize, progress, ct).ConfigureAwait(false);
        }

        return await PublishSingleToServerAsync(config, zipFilePath, manifest, progress, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Tải lên phân đoạn (Chunked Upload) cho gói cập nhật dung lượng lớn, vượt qua giới hạn post_max_size của hosting.
    /// </summary>
    private async Task<OtaPublishResult> PublishChunkedToServerAsync(
        OtaPublishConfig config,
        string zipFilePath,
        UpdateManifest manifest,
        int chunkSize,
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(zipFilePath);
        long totalLength = fileInfo.Length;
        int totalChunks = (int)Math.Ceiling((double)totalLength / chunkSize);
        if (totalChunks <= 0) totalChunks = 1;

        string fileId = Guid.NewGuid().ToString("N");
        string baseServerUrl = config.ServerUploadUrl.Trim();

        // Xây dựng URL đích với action=upload_chunk
        string targetUrl = Regex.Replace(baseServerUrl, @"([?&])action=[^&]*(&|$)", "$1").TrimEnd('?', '&');
        string separator = targetUrl.Contains("?") ? "&" : "?";
        targetUrl = $"{targetUrl}{separator}action=upload_chunk";

        string serverFolder = !string.IsNullOrWhiteSpace(config.ServerStorageFolder)
            ? config.ServerStorageFolder.Trim()
            : "uploads/ota_packages";

        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });

        using var fileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] buffer = new byte[chunkSize];
        long totalUploaded = 0;
        var sw = Stopwatch.StartNew();
        long lastReportTime = sw.ElapsedMilliseconds;
        long lastReportBytes = 0;

        for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
        {
            ct.ThrowIfCancellationRequested();

            int bytesToRead = (int)Math.Min(chunkSize, totalLength - totalUploaded);
            int bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct).ConfigureAwait(false);
            if (bytesRead <= 0)
                break;

            using var multipart = new MultipartFormDataContent();

            // 1. Metadata phân đoạn
            multipart.Add(new StringContent(fileId, Encoding.UTF8), "file_id");
            multipart.Add(new StringContent(chunkIndex.ToString(), Encoding.UTF8), "chunk_index");
            multipart.Add(new StringContent(totalChunks.ToString(), Encoding.UTF8), "total_chunks");

            // 2. Metadata manifest & cấu hình server
            multipart.Add(new StringContent(serverFolder, Encoding.UTF8), "target_dir");
            if (!string.IsNullOrWhiteSpace(config.ApiToken))
            {
                multipart.Add(new StringContent(config.ApiToken.Trim(), Encoding.UTF8), "api_token");
            }
            multipart.Add(new StringContent(manifestJson, Encoding.UTF8, "application/json"), "manifest_json");
            multipart.Add(new StringContent(manifest.Version, Encoding.UTF8), "version");
            multipart.Add(new StringContent(manifest.AppName, Encoding.UTF8), "app_name");
            multipart.Add(new StringContent(manifest.Channel, Encoding.UTF8), "channel");
            multipart.Add(new StringContent(manifest.ReleaseNotes, Encoding.UTF8), "release_notes");
            multipart.Add(new StringContent(manifest.IsMandatory ? "true" : "false", Encoding.UTF8), "is_mandatory");
            if (!string.IsNullOrWhiteSpace(manifest.MinSupportedVersion))
            {
                multipart.Add(new StringContent(manifest.MinSupportedVersion, Encoding.UTF8), "min_supported_version");
            }

            // 3. Dữ liệu phân đoạn tệp
            var chunkContent = new ByteArrayContent(buffer, 0, bytesRead);
            chunkContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(chunkContent, "package_chunk", Path.GetFileName(zipFilePath));

            using var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
            {
                Content = multipart
            };

            if (!string.IsNullOrWhiteSpace(config.ApiToken))
            {
                request.Headers.Add("X-API-Key", config.ApiToken.Trim());
            }

            HttpResponseMessage response;
            string responseBody;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new OtaPublishResult
                {
                    Success = false,
                    ErrorMessage = "Tiến trình upload đã bị hủy bỏ bởi người dùng."
                };
            }
            catch (Exception ex)
            {
                return new OtaPublishResult
                {
                    Success = false,
                    ErrorMessage = $"Lỗi kết nối máy chủ ở phân đoạn {chunkIndex + 1}/{totalChunks}: {ex.Message}"
                };
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    string errMsg = $"Máy chủ phản hồi mã lỗi {(int)response.StatusCode} ({response.ReasonPhrase}) khi tải phân đoạn {chunkIndex + 1}/{totalChunks}";
                    try
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        if (doc.RootElement.TryGetProperty("error", out var e))
                        {
                            errMsg = e.GetString() ?? errMsg;
                        }
                    }
                    catch { }

                    if (errMsg.Contains("Hành động (Action) không hợp lệ") || errMsg.Contains("Action không hợp lệ"))
                    {
                        errMsg = $"Máy chủ PHP trên hosting hiện đang chạy tệp 'ota_server.php' phiên bản cũ chưa có tính năng Tải lên phân đoạn (Chunked Upload). Đồng thời gói zip ({totalLength / (1024.0 * 1024.0):F1} MB) vượt quá giới hạn cấu hình post_max_size (40M) của hosting. Vui lòng tải tệp 'ServerScripts/ota_server.php' mới nhất từ dự án lên hosting (thay thế tệp cũ) để kích hoạt tải lên phân đoạn không giới hạn!";
                    }

                    return new OtaPublishResult
                    {
                        Success = false,
                        ErrorMessage = errMsg,
                        ServerMessage = responseBody
                    };
                }

                totalUploaded += bytesRead;
                long now = sw.ElapsedMilliseconds;
                double deltaSec = (now - lastReportTime) / 1000.0;
                double speed = deltaSec > 0 ? (totalUploaded - lastReportBytes) / deltaSec : 0;
                double pct = (double)totalUploaded / totalLength * 100.0;

                progress?.Report(new UpdateProgressInfo
                {
                    BytesDownloaded = totalUploaded,
                    TotalBytes = totalLength,
                    Percentage = pct,
                    SpeedBytesPerSec = speed,
                    StatusText = $"Đang tải lên phân đoạn {chunkIndex + 1}/{totalChunks}"
                });

                lastReportBytes = totalUploaded;
                lastReportTime = now;

                // Nếu là phân đoạn cuối cùng -> phân tích kết quả hoàn tất xuất bản từ server
                if (chunkIndex == totalChunks - 1)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        var root = doc.RootElement;
                        bool isSuccess = root.TryGetProperty("success", out var s) && s.GetBoolean();
                        string? message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                        string? downloadUrl = root.TryGetProperty("download_url", out var dl) ? dl.GetString() : null;
                        string? manifestUrl = root.TryGetProperty("manifest_url", out var mu) ? mu.GetString() : null;
                        string? serverFolderResp = root.TryGetProperty("server_folder", out var sf) ? sf.GetString() : serverFolder;
                        string? ver = root.TryGetProperty("version", out var v) ? v.GetString() : manifest.Version;
                        string? sha = root.TryGetProperty("sha256", out var sh) ? sh.GetString() : manifest.Sha256;
                        long sz = root.TryGetProperty("file_size", out var fsz) ? fsz.GetInt64() : manifest.FileSize;

                        return new OtaPublishResult
                        {
                            Success = isSuccess,
                            ServerMessage = message,
                            Version = ver,
                            DownloadUrl = downloadUrl,
                            ManifestUrl = manifestUrl,
                            ServerFolder = serverFolderResp,
                            Sha256 = sha,
                            FileSizeBytes = sz,
                            Manifest = manifest
                        };
                    }
                    catch (Exception jsonEx)
                    {
                        return new OtaPublishResult
                        {
                            Success = true,
                            ServerMessage = $"Tải lên tất cả {totalChunks} phân đoạn thành công nhưng phản hồi không phải JSON: {jsonEx.Message}",
                            DownloadUrl = manifest.DownloadUrl,
                            Version = manifest.Version,
                            Manifest = manifest
                        };
                    }
                }
            }
        }

        return new OtaPublishResult
        {
            Success = false,
            ErrorMessage = "Không thể đọc hết dữ liệu tệp gói cập nhật."
        };
    }

    /// <summary>
    /// Tải lên 1 lần thông thường (dành cho gói nhỏ <= 6MB).
    /// </summary>
    private async Task<OtaPublishResult> PublishSingleToServerAsync(
        OtaPublishConfig config,
        string zipFilePath,
        UpdateManifest manifest,
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            // Đảm bảo URL có query action=publish nếu chưa có
            string targetUrl = config.ServerUploadUrl.Trim();
            if (!targetUrl.Contains("action="))
            {
                string separator = targetUrl.Contains("?") ? "&" : "?";
                targetUrl = $"{targetUrl}{separator}action=publish";
            }

            using var multipart = new MultipartFormDataContent();

            // 1. Thêm tệp zip với cơ chế báo cáo tiến độ upload
            var fileInfo = new FileInfo(zipFilePath);
            var fileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var progressContent = new ProgressableStreamContent(fileStream, fileInfo.Length, progress, ct);
            progressContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            multipart.Add(progressContent, "package_file", Path.GetFileName(zipFilePath));

            // 2. Thêm thư mục lưu trữ trên máy chủ do người dùng cấu hình
            string serverFolder = !string.IsNullOrWhiteSpace(config.ServerStorageFolder) 
                ? config.ServerStorageFolder.Trim() 
                : "uploads/ota_packages";
            multipart.Add(new StringContent(serverFolder, Encoding.UTF8), "target_dir");

            // 3. Thêm Token bảo mật nếu có
            if (!string.IsNullOrWhiteSpace(config.ApiToken))
            {
                multipart.Add(new StringContent(config.ApiToken.Trim(), Encoding.UTF8), "api_token");
            }

            // 4. Thêm Manifest JSON và các trường thông tin chi tiết
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            multipart.Add(new StringContent(manifestJson, Encoding.UTF8, "application/json"), "manifest_json");
            multipart.Add(new StringContent(manifest.Version, Encoding.UTF8), "version");
            multipart.Add(new StringContent(manifest.AppName, Encoding.UTF8), "app_name");
            multipart.Add(new StringContent(manifest.Channel, Encoding.UTF8), "channel");
            multipart.Add(new StringContent(manifest.ReleaseNotes, Encoding.UTF8), "release_notes");
            multipart.Add(new StringContent(manifest.IsMandatory ? "true" : "false", Encoding.UTF8), "is_mandatory");
            if (!string.IsNullOrWhiteSpace(manifest.MinSupportedVersion))
            {
                multipart.Add(new StringContent(manifest.MinSupportedVersion, Encoding.UTF8), "min_supported_version");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
            {
                Content = multipart
            };

            if (!string.IsNullOrWhiteSpace(config.ApiToken))
            {
                request.Headers.Add("X-API-Key", config.ApiToken.Trim());
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string errMsg = $"Máy chủ phản hồi mã lỗi {(int)response.StatusCode} ({response.ReasonPhrase})";
                if (responseBody.Contains("POST Content-Length") && responseBody.Contains("exceeds the limit"))
                {
                    errMsg = $"Dung lượng gói ({fileInfo.Length / (1024.0 * 1024.0):F1} MB) vượt quá giới hạn post_max_size của máy chủ PHP hosting. Vui lòng cập nhật file 'ServerScripts/ota_server.php' mới nhất lên hosting để kích hoạt tính năng upload phân đoạn (Chunked Upload).";
                }
                else
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        if (doc.RootElement.TryGetProperty("error", out var e))
                        {
                            errMsg = e.GetString() ?? errMsg;
                        }
                    }
                    catch { }
                }

                return new OtaPublishResult
                {
                    Success = false,
                    ErrorMessage = errMsg,
                    ServerMessage = responseBody
                };
            }

            // Phân tích phản hồi JSON từ PHP
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                bool isSuccess = root.TryGetProperty("success", out var s) && s.GetBoolean();
                string? message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                string? downloadUrl = root.TryGetProperty("download_url", out var dl) ? dl.GetString() : null;
                string? manifestUrl = root.TryGetProperty("manifest_url", out var mu) ? mu.GetString() : null;
                string? serverFolderResp = root.TryGetProperty("server_folder", out var sf) ? sf.GetString() : serverFolder;
                string? ver = root.TryGetProperty("version", out var v) ? v.GetString() : manifest.Version;
                string? sha = root.TryGetProperty("sha256", out var sh) ? sh.GetString() : manifest.Sha256;
                long sz = root.TryGetProperty("file_size", out var fsz) ? fsz.GetInt64() : manifest.FileSize;

                return new OtaPublishResult
                {
                    Success = isSuccess,
                    ServerMessage = message,
                    Version = ver,
                    DownloadUrl = downloadUrl,
                    ManifestUrl = manifestUrl,
                    ServerFolder = serverFolderResp,
                    Sha256 = sha,
                    FileSizeBytes = sz,
                    Manifest = manifest
                };
            }
            catch (Exception jsonEx)
            {
                return new OtaPublishResult
                {
                    Success = true,
                    ServerMessage = $"Upload hoàn tất nhưng phản hồi không phải định dạng JSON: {jsonEx.Message}",
                    DownloadUrl = manifest.DownloadUrl,
                    Version = manifest.Version,
                    Manifest = manifest
                };
            }
        }
        catch (OperationCanceledException)
        {
            return new OtaPublishResult
            {
                Success = false,
                ErrorMessage = "Tiến trình upload đã bị hủy bỏ bởi người dùng."
            };
        }
        catch (Exception ex)
        {
            return new OtaPublishResult
            {
                Success = false,
                ErrorMessage = $"Lỗi kết nối máy chủ khi upload: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Lớp StreamContent tùy biến cho phép báo cáo tiến trình upload và tốc độ mạng.
    /// </summary>
    private sealed class ProgressableStreamContent : HttpContent
    {
        private readonly Stream _sourceStream;
        private readonly long _contentLength;
        private readonly IProgress<UpdateProgressInfo>? _progress;
        private readonly CancellationToken _ct;

        public ProgressableStreamContent(Stream sourceStream, long contentLength, IProgress<UpdateProgressInfo>? progress, CancellationToken ct)
        {
            _sourceStream = sourceStream;
            _contentLength = contentLength;
            _progress = progress;
            _ct = ct;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _contentLength;
            return true;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            byte[] buffer = new byte[81920]; // 80 KB
            long totalUploaded = 0;
            var sw = Stopwatch.StartNew();
            long lastReportBytes = 0;
            long lastReportTime = sw.ElapsedMilliseconds;

            int bytesRead;
            while ((bytesRead = await _sourceStream.ReadAsync(buffer, 0, buffer.Length, _ct).ConfigureAwait(false)) > 0)
            {
                _ct.ThrowIfCancellationRequested();
                await stream.WriteAsync(buffer, 0, bytesRead, _ct).ConfigureAwait(false);
                totalUploaded += bytesRead;

                long now = sw.ElapsedMilliseconds;
                if (now - lastReportTime >= 200 || totalUploaded == _contentLength)
                {
                    double deltaSec = (now - lastReportTime) / 1000.0;
                    double speed = deltaSec > 0 ? (totalUploaded - lastReportBytes) / deltaSec : 0;
                    double pct = _contentLength > 0 ? (double)totalUploaded / _contentLength * 100.0 : 0;

                    _progress?.Report(new UpdateProgressInfo
                    {
                        BytesDownloaded = totalUploaded,
                        TotalBytes = _contentLength,
                        Percentage = pct,
                        SpeedBytesPerSec = speed
                    });

                    lastReportBytes = totalUploaded;
                    lastReportTime = now;
                }
            }

            await stream.FlushAsync(_ct).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sourceStream.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
