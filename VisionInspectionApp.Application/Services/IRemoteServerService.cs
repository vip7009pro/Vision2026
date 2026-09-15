using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.Application.Services;

public interface IRemoteServerService
{
    Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> UploadImageAsync(
        byte[] imageBytes, string fileName, string productCode, string serverApiUrl, string? productName = null, CancellationToken cancellationToken = default);

    Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> UploadJobAsync(
        string jobFilePath, string productCode, string serverApiUrl, string? productName = null, CancellationToken cancellationToken = default);

    Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> UploadJobAsync(
        byte[] jobBytes, string fileName, string productCode, string serverApiUrl, string? productName = null, CancellationToken cancellationToken = default);

    Task<(bool Success, byte[]? Data, string ErrorMessage)> DownloadFileAsync(
        string url, CancellationToken cancellationToken = default);

    Task<(bool Success, byte[]? Data, string ErrorMessage)> DownloadFileAsync(
        string url, int timeoutSeconds, System.IProgress<FileDownloadProgressInfo>? progress = null, CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> PingServerAsync(
        string serverApiUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thông tin tiến trình và tốc độ mạng khi tải tệp từ xa.
/// </summary>
public class FileDownloadProgressInfo
{
    public long BytesDownloaded { get; set; }
    public long? TotalBytes { get; set; }
    public double Percentage { get; set; }
    public double SpeedBytesPerSec { get; set; }
    public string? StatusText { get; set; }

    public string SpeedFormatted => SpeedBytesPerSec > 1024 * 1024
        ? $"{(SpeedBytesPerSec / (1024.0 * 1024.0)):F2} MB/s"
        : $"{(SpeedBytesPerSec / 1024.0):F1} KB/s";

    public string ProgressFormatted => TotalBytes.HasValue && TotalBytes.Value > 0
        ? $"{(BytesDownloaded / (1024.0 * 1024.0)):F2} MB / {(TotalBytes.Value / (1024.0 * 1024.0)):F2} MB ({Percentage:F1}%)"
        : $"Đã tải: {(BytesDownloaded / (1024.0 * 1024.0)):F2} MB";
}
