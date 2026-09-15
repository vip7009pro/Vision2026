using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.Application.Services;

public class RemoteServerService : IRemoteServerService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;

    public RemoteServerService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _disposeClient = false;
        }
        else
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _disposeClient = true;
        }
    }

    public async Task<(bool Success, string Message)> PingServerAsync(string serverApiUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serverApiUrl))
        {
            return (false, "Địa chỉ Server API rỗng.");
        }

        try
        {
            string url = serverApiUrl.Trim();
            if (!url.Contains("?"))
            {
                url += "?action=ping";
            }
            else if (!url.Contains("action="))
            {
                url += "&action=ping";
            }

            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("success", out var succProp) && succProp.GetBoolean())
                    {
                        string msg = doc.RootElement.TryGetProperty("message", out var mProp) ? mProp.GetString() ?? "Server Online" : "Server Online";
                        return (true, $"✅ {msg}");
                    }
                }
                catch { }

                return (true, "✅ Kết nối tới Server thành công!");
            }

            return (false, $"HTTP Error {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            return (false, $"Không thể kết nối tới Server: {ex.Message}");
        }
    }

    /// <summary>
    /// Chuẩn hóa chuỗi (mã sản phẩm, tên sản phẩm) thành chuỗi định danh an toàn cho tên tệp:
    /// Khử dấu tiếng Việt, thay khoảng trắng và ký tự đặc biệt thành dấu gạch dưới.
    /// </summary>
    public static string SanitizeIdentifier(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        string text = input.Replace("đ", "d").Replace("Đ", "D");
        string normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (char c in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
        }
        string cleaned = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
        return cleaned;
    }

    public async Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> UploadImageAsync(
        byte[] imageBytes, string fileName, string productCode, string serverApiUrl, string? productName = null, CancellationToken cancellationToken = default)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            return (false, "", "", "Dữ liệu ảnh rỗng.");
        }

        if (string.IsNullOrWhiteSpace(serverApiUrl))
        {
            return (false, "", "", "Địa chỉ Server API rỗng.");
        }

        try
        {
            string url = serverApiUrl.Trim();
            if (!url.Contains("?"))
            {
                url += "?action=upload_image";
            }
            else if (!url.Contains("action="))
            {
                url += "&action=upload_image";
            }

            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(imageBytes);
            byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");

            string safeCode = SanitizeIdentifier(productCode);
            string safeName = SanitizeIdentifier(productName);
            string identifier = !string.IsNullOrWhiteSpace(safeName) ? $"{safeCode}_{safeName}" : (string.IsNullOrWhiteSpace(safeCode) ? "PROD" : safeCode);

            string safeFileName = string.IsNullOrWhiteSpace(fileName) ? $"teach_{identifier}.png" : Path.GetFileName(fileName);
            content.Add(byteContent, "image_file", safeFileName);
            content.Add(new StringContent(productCode ?? ""), "product_code");
            if (!string.IsNullOrWhiteSpace(productName))
            {
                content.Add(new StringContent(productName), "product_name");
            }

            using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            string resJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (false, "", "", $"HTTP {(int)response.StatusCode}: {resJson}");
            }

            using var doc = JsonDocument.Parse(resJson);
            var root = doc.RootElement;
            bool success = root.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
            if (success)
            {
                string fullUrl = root.TryGetProperty("url", out var uProp) ? uProp.GetString() ?? "" :
                                 root.TryGetProperty("full_url", out var fuProp) ? fuProp.GetString() ?? "" : "";
                string relPath = root.TryGetProperty("file_path", out var fProp) ? fProp.GetString() ?? "" :
                                 root.TryGetProperty("relative_path", out var rfProp) ? rfProp.GetString() ?? "" : "";
                return (true, fullUrl, relPath, "");
            }

            string err = root.TryGetProperty("error", out var eProp) ? eProp.GetString() ?? "Lỗi upload ảnh" : "Lỗi upload ảnh";
            return (false, "", "", err);
        }
        catch (Exception ex)
        {
            return (false, "", "", $"Lỗi upload ảnh lên Server: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> UploadJobAsync(
        string jobFilePath, string productCode, string serverApiUrl, string? productName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobFilePath) || !File.Exists(jobFilePath))
        {
            return (false, "", "", $"Tệp Job không tồn tại: {jobFilePath}");
        }

        byte[] jobBytes = await File.ReadAllBytesAsync(jobFilePath, cancellationToken).ConfigureAwait(false);
        return await UploadJobAsync(jobBytes, Path.GetFileName(jobFilePath), productCode, serverApiUrl, productName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> UploadJobAsync(
        byte[] jobBytes, string fileName, string productCode, string serverApiUrl, string? productName = null, CancellationToken cancellationToken = default)
    {
        if (jobBytes == null || jobBytes.Length == 0)
        {
            return (false, "", "", "Dữ liệu tệp Job rỗng.");
        }

        if (string.IsNullOrWhiteSpace(serverApiUrl))
        {
            return (false, "", "", "Địa chỉ Server API rỗng.");
        }

        try
        {
            string url = serverApiUrl.Trim();
            if (!url.Contains("?"))
            {
                url += "?action=upload_job";
            }
            else if (!url.Contains("action="))
            {
                url += "&action=upload_job";
            }

            using var content = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(jobBytes);
            byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");

            string safeCode = SanitizeIdentifier(productCode);
            string safeName = SanitizeIdentifier(productName);
            string identifier = !string.IsNullOrWhiteSpace(safeName) ? $"{safeCode}_{safeName}" : (string.IsNullOrWhiteSpace(safeCode) ? "JOB" : safeCode);

            string safeFileName = string.IsNullOrWhiteSpace(fileName) ? $"job_{identifier}.job" : Path.GetFileName(fileName);
            content.Add(byteContent, "job_file", safeFileName);
            content.Add(new StringContent(productCode ?? ""), "product_code");
            if (!string.IsNullOrWhiteSpace(productName))
            {
                content.Add(new StringContent(productName), "product_name");
            }

            using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            string resJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (false, "", "", $"HTTP {(int)response.StatusCode}: {resJson}");
            }

            using var doc = JsonDocument.Parse(resJson);
            var root = doc.RootElement;
            bool success = root.TryGetProperty("success", out var sProp) && sProp.GetBoolean();
            if (success)
            {
                string fullUrl = root.TryGetProperty("url", out var uProp) ? uProp.GetString() ?? "" :
                                 root.TryGetProperty("full_url", out var fuProp) ? fuProp.GetString() ?? "" : "";
                string relPath = root.TryGetProperty("file_path", out var fProp) ? fProp.GetString() ?? "" :
                                 root.TryGetProperty("relative_path", out var rfProp) ? rfProp.GetString() ?? "" : "";
                return (true, fullUrl, relPath, "");
            }

            string err = root.TryGetProperty("error", out var eProp) ? eProp.GetString() ?? "Lỗi upload Job" : "Lỗi upload Job";
            return (false, "", "", err);
        }
        catch (Exception ex)
        {
            return (false, "", "", $"Lỗi upload Job lên Server: {ex.Message}");
        }
    }

    public Task<(bool Success, byte[]? Data, string ErrorMessage)> DownloadFileAsync(string url, CancellationToken cancellationToken = default)
    {
        return DownloadFileAsync(url, timeoutSeconds: 60, progress: null, cancellationToken: cancellationToken);
    }

    public async Task<(bool Success, byte[]? Data, string ErrorMessage)> DownloadFileAsync(
        string url,
        int timeoutSeconds,
        IProgress<FileDownloadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return (false, null, "URL tải về rỗng.");
        }

        int effectiveTimeout = timeoutSeconds > 0 ? timeoutSeconds : 60;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(effectiveTimeout));

            using var response = await _httpClient.GetAsync(
                url.Trim(),
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (false, null, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            long? totalBytes = response.Content.Headers.ContentLength;
            using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var memoryStream = new MemoryStream();

            byte[] buffer = new byte[65536]; // 64 KB buffer
            int bytesRead;
            long totalDownloaded = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastReportTime = 0;
            long lastReportBytes = 0;

            // Báo cáo ban đầu
            progress?.Report(new FileDownloadProgressInfo
            {
                BytesDownloaded = 0,
                TotalBytes = totalBytes,
                Percentage = 0,
                SpeedBytesPerSec = 0,
                StatusText = totalBytes.HasValue && totalBytes.Value > 0
                    ? $"0% (0.0 MB / {totalBytes.Value / (1024.0 * 1024.0):F1} MB)"
                    : "Đang tải dữ liệu..."
            });

            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).ConfigureAwait(false)) > 0)
            {
                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token).ConfigureAwait(false);
                totalDownloaded += bytesRead;

                long elapsed = sw.ElapsedMilliseconds;
                if (elapsed - lastReportTime >= 100 || (totalBytes.HasValue && totalDownloaded >= totalBytes.Value))
                {
                    double deltaSec = (elapsed - lastReportTime) / 1000.0;
                    double speed = deltaSec > 0 ? (totalDownloaded - lastReportBytes) / deltaSec : 0;
                    double pct = totalBytes.HasValue && totalBytes.Value > 0
                        ? Math.Min(100.0, (double)totalDownloaded / totalBytes.Value * 100.0)
                        : 0;

                    progress?.Report(new FileDownloadProgressInfo
                    {
                        BytesDownloaded = totalDownloaded,
                        TotalBytes = totalBytes,
                        Percentage = pct,
                        SpeedBytesPerSec = speed,
                        StatusText = totalBytes.HasValue && totalBytes.Value > 0
                            ? $"{pct:F0}% ({totalDownloaded / (1024.0 * 1024.0):F1} MB / {totalBytes.Value / (1024.0 * 1024.0):F1} MB)"
                            : $"Đã tải: {totalDownloaded / (1024.0 * 1024.0):F1} MB"
                    });

                    lastReportTime = elapsed;
                    lastReportBytes = totalDownloaded;
                }
            }

            // Báo cáo hoàn tất
            progress?.Report(new FileDownloadProgressInfo
            {
                BytesDownloaded = totalDownloaded,
                TotalBytes = totalBytes ?? totalDownloaded,
                Percentage = 100.0,
                SpeedBytesPerSec = 0,
                StatusText = $"Hoàn tất: {totalDownloaded / (1024.0 * 1024.0):F2} MB"
            });

            return (true, memoryStream.ToArray(), "");
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return (false, null, "Quá trình tải tệp đã bị hủy bởi người dùng.");
            }
            return (false, null, $"Tải tệp từ URL quá thời gian chờ ({effectiveTimeout}s): {url}");
        }
        catch (Exception ex)
        {
            return (false, null, $"Lỗi tải tệp từ URL: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _httpClient.Dispose();
        }
    }
}
