using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models.Ota;

namespace VisionInspectionApp.Application.Services;

public class OtaUpdateService : IOtaUpdateService
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(15) // Cho phép tải gói lớn
    };

    public Version CurrentVersion { get; }

    public OtaUpdateService()
    {
        // Xác định version hiện tại của ứng dụng
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var ver = assembly.GetName().Version;
        CurrentVersion = ver ?? new Version(1, 0, 0, 0);
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(string? serverUrl = null, string? sourceType = "Auto", CancellationToken ct = default)
    {
        var result = new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion
        };

        try
        {
            string targetUrl = !string.IsNullOrWhiteSpace(serverUrl) 
                ? serverUrl.Trim() 
                : "http://192.168.1.100:8080/api/updates/version.json";

            bool isGitHub = string.Equals(sourceType, "GitHub", StringComparison.OrdinalIgnoreCase) ||
                            targetUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase);

            UpdateManifest? manifest;

            if (isGitHub)
            {
                manifest = await FetchGitHubReleaseManifestAsync(targetUrl, ct).ConfigureAwait(false);
            }
            else
            {
                manifest = await FetchCustomManifestAsync(targetUrl, ct).ConfigureAwait(false);
            }

            if (manifest == null)
            {
                result.Status = UpdateCheckStatus.Error;
                result.ErrorMessage = "Không thể đọc dữ liệu phiên bản từ máy chủ.";
                return result;
            }

            result.Manifest = manifest;

            if (!Version.TryParse(manifest.Version, out var latestVer))
            {
                result.Status = UpdateCheckStatus.Error;
                result.ErrorMessage = $"Định dạng phiên bản máy chủ không hợp lệ: '{manifest.Version}'";
                return result;
            }

            result.LatestVersion = latestVer;

            if (latestVer > CurrentVersion)
            {
                result.Status = manifest.IsMandatory
                    ? UpdateCheckStatus.MandatoryUpdate
                    : UpdateCheckStatus.UpdateAvailable;
            }
            else
            {
                result.Status = UpdateCheckStatus.UpToDate;
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Status = UpdateCheckStatus.Error;
            result.ErrorMessage = $"Lỗi khi kiểm tra bản cập nhật: {ex.Message}";
            return result;
        }
    }

    private static async Task<UpdateManifest?> FetchCustomManifestAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, options, ct).ConfigureAwait(false);
    }

    private static async Task<UpdateManifest?> FetchGitHubReleaseManifestAsync(string repoOrUrl, CancellationToken ct)
    {
        // Chuẩn hóa URL GitHub Releases API
        string apiUrl = repoOrUrl;
        if (!repoOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            // Dạng "owner/repo"
            apiUrl = $"https://api.github.com/repos/{repoOrUrl.Trim()}/releases/latest";
        }
        else if (repoOrUrl.Contains("github.com") && !repoOrUrl.Contains("api.github.com"))
        {
            // Dạng "https://github.com/owner/repo"
            var uri = new Uri(repoOrUrl);
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length >= 2)
            {
                apiUrl = $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/latest";
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.Add("User-Agent", "VisionInspectionApp-OTA/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;

        string tagName = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string cleanVersion = tagName.TrimStart('v', 'V');
        string body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        DateTime? publishedAt = root.TryGetProperty("published_at", out var p) && p.TryGetDateTime(out var dt) ? dt : null;

        string downloadUrl = "";
        long fileSize = 0;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() ?? "" : "";
                    fileSize = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                    break;
                }
            }
        }

        return new UpdateManifest
        {
            AppName = "CMS VINA Vision System",
            Version = cleanVersion,
            ReleaseNotes = body,
            ReleaseDate = publishedAt,
            DownloadUrl = downloadUrl,
            FileSize = fileSize,
            Channel = "Stable"
        };
    }

    public async Task<string> DownloadUpdatePackageAsync(UpdateManifest manifest, string saveDirectory, IProgress<UpdateProgressInfo>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
        {
            throw new ArgumentException("Đường dẫn tải gói cập nhật (DownloadUrl) không được rỗng.");
        }

        Directory.CreateDirectory(saveDirectory);
        string fileName = $"VisionUpdate_v{manifest.Version}_{DateTime.UtcNow:yyyyMMddHHmmss}.zip";
        string filePath = Path.Combine(saveDirectory, fileName);

        using var request = new HttpRequestMessage(HttpMethod.Get, manifest.DownloadUrl);
        if (manifest.DownloadUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add("User-Agent", "VisionInspectionApp-OTA/1.0");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? manifest.FileSize;
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        byte[] buffer = new byte[81920]; // 80 KB
        long totalDownloaded = 0;
        var stopwatch = Stopwatch.StartNew();
        long lastReportedBytes = 0;
        var lastReportTime = stopwatch.ElapsedMilliseconds;

        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
            totalDownloaded += bytesRead;

            long now = stopwatch.ElapsedMilliseconds;
            if (now - lastReportTime >= 200 || totalDownloaded == totalBytes)
            {
                double timeDeltaSec = (now - lastReportTime) / 1000.0;
                double speed = timeDeltaSec > 0 ? (totalDownloaded - lastReportedBytes) / timeDeltaSec : 0;
                double percentage = totalBytes > 0 ? (double)totalDownloaded / totalBytes * 100.0 : 0.0;

                progress?.Report(new UpdateProgressInfo
                {
                    BytesDownloaded = totalDownloaded,
                    TotalBytes = totalBytes,
                    Percentage = percentage,
                    SpeedBytesPerSec = speed
                });

                lastReportedBytes = totalDownloaded;
                lastReportTime = now;
            }
        }

        await fileStream.FlushAsync(ct).ConfigureAwait(false);
        return filePath;
    }

    public bool VerifyPackageChecksum(string filePath, string expectedSha256)
    {
        if (!File.Exists(filePath))
            return false;

        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            // Nếu manifest không cung cấp sha256 (ví dụ một số link tải trực tiếp), coi như hợp lệ
            return true;
        }

        try
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hashBytes = sha.ComputeHash(stream);
            string actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            string expectedClean = expectedSha256.Trim().ToLowerInvariant();

            return string.Equals(actualHash, expectedClean, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public bool LaunchUpdaterAndShutdown(string zipPackagePath, bool restartAfterUpdate = true)
    {
        try
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string updaterPath = Path.Combine(appDir, "VisionUpdater.exe");

            if (!File.Exists(updaterPath))
            {
                // Thử tìm trong thư mục cha (trong môi trường Debug/Bin)
                var parentUpdater = Path.Combine(appDir, "..", "..", "..", "..", "VisionInspectionApp.Updater", "bin", "Debug", "net8.0-windows", "VisionUpdater.exe");
                if (File.Exists(parentUpdater))
                {
                    updaterPath = Path.GetFullPath(parentUpdater);
                }
            }

            if (!File.Exists(updaterPath))
            {
                throw new FileNotFoundException($"Không tìm thấy tệp trình cập nhật: {updaterPath}");
            }

            string currentExe = Environment.ProcessPath ?? Path.Combine(appDir, "VisionInspectionApp.UI.exe");
            int pid = Environment.ProcessId;

            var startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = $"--pid {pid} --package \"{zipPackagePath}\" --target \"{appDir}\" --restart \"{currentExe}\"",
                UseShellExecute = true,
                CreateNoWindow = false
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OTA] Lỗi kích hoạt VisionUpdater: {ex.Message}");
            return false;
        }
    }

    public void CleanupTempUpdates(string updateDirectory)
    {
        try
        {
            if (!Directory.Exists(updateDirectory)) return;

            var files = Directory.GetFiles(updateDirectory, "VisionUpdate_*.zip");
            var threshold = DateTime.UtcNow.AddDays(-3);

            foreach (var file in files)
            {
                var creationTime = File.GetCreationTimeUtc(file);
                if (creationTime < threshold)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }
}
