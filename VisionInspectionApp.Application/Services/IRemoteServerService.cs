using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.Application.Services;

public interface IRemoteServerService
{
    Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> UploadImageAsync(
        byte[] imageBytes, string fileName, string productCode, string serverApiUrl, CancellationToken cancellationToken = default);

    Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> UploadJobAsync(
        string jobFilePath, string productCode, string serverApiUrl, CancellationToken cancellationToken = default);

    Task<(bool Success, string Url, string RelativePath, string ErrorMessage)> UploadJobAsync(
        byte[] jobBytes, string fileName, string productCode, string serverApiUrl, CancellationToken cancellationToken = default);

    Task<(bool Success, byte[]? Data, string ErrorMessage)> DownloadFileAsync(
        string url, CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> PingServerAsync(
        string serverApiUrl, CancellationToken cancellationToken = default);
}
