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

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            string content = await response.Content.ReadAsStringAsync(cancellationToken);

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

    public async Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> UploadImageAsync(
        byte[] imageBytes, string fileName, string productCode, string serverApiUrl, CancellationToken cancellationToken = default)
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

            string safeFileName = string.IsNullOrWhiteSpace(fileName) ? $"teach_{productCode}.png" : Path.GetFileName(fileName);
            content.Add(byteContent, "image_file", safeFileName);
            content.Add(new StringContent(productCode ?? ""), "product_code");

            using var response = await _httpClient.PostAsync(url, content, cancellationToken);
            string resJson = await response.Content.ReadAsStringAsync(cancellationToken);

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
        string jobFilePath, string productCode, string serverApiUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobFilePath) || !File.Exists(jobFilePath))
        {
            return (false, "", "", $"Tệp Job không tồn tại: {jobFilePath}");
        }

        byte[] jobBytes = await File.ReadAllBytesAsync(jobFilePath, cancellationToken);
        return await UploadJobAsync(jobBytes, Path.GetFileName(jobFilePath), productCode, serverApiUrl, cancellationToken);
    }

    public async Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> UploadJobAsync(
        byte[] jobBytes, string fileName, string productCode, string serverApiUrl, CancellationToken cancellationToken = default)
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

            string safeFileName = string.IsNullOrWhiteSpace(fileName) ? $"job_{productCode}.job" : Path.GetFileName(fileName);
            content.Add(byteContent, "job_file", safeFileName);
            content.Add(new StringContent(productCode ?? ""), "product_code");

            using var response = await _httpClient.PostAsync(url, content, cancellationToken);
            string resJson = await response.Content.ReadAsStringAsync(cancellationToken);

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

    public async Task<(bool Success, byte[]? Data, string ErrorMessage)> DownloadFileAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return (false, null, "URL tải về rỗng.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var response = await _httpClient.GetAsync(url.Trim(), cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            return (true, bytes, "");
        }
        catch (OperationCanceledException)
        {
            return (false, null, $"Tải tệp từ URL quá thời gian chờ (5s): {url}");
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
