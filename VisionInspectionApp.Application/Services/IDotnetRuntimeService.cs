using System;
using System.Threading;
using System.Threading.Tasks;

namespace VisionInspectionApp.Application.Services;

/// <summary>
/// Dịch vụ kiểm tra và quản lý môi trường .NET Runtime (x64 và x86) cho ứng dụng.
/// </summary>
public interface IDotnetRuntimeService
{
    /// <summary>
    /// Kiểm tra xem .NET Desktop/Core Runtime x86 (32-bit) phiên bản >= minMajorVersion đã được cài đặt hay chưa.
    /// </summary>
    bool IsX86RuntimeInstalled(int minMajorVersion = 8);

    /// <summary>
    /// Kiểm tra xem .NET Runtime x64 (64-bit) phiên bản >= minMajorVersion đã được cài đặt hay chưa.
    /// </summary>
    bool IsX64RuntimeInstalled(int minMajorVersion = 8);

    /// <summary>
    /// Lấy URL tải trực tiếp bộ cài đặt .NET Desktop Runtime x86 chính thức từ Microsoft.
    /// </summary>
    string GetX86RuntimeDownloadUrl();

    /// <summary>
    /// Lấy URL trang web chính thức của Microsoft để tải .NET 8.0.
    /// </summary>
    string GetOfficialDownloadPageUrl();

    /// <summary>
    /// Tải bộ cài đặt .NET x86 từ Microsoft CDN về thư mục tạm.
    /// </summary>
    Task<(bool Success, string InstallerPath, string ErrorMessage)> DownloadX86InstallerAsync(
        IProgress<FileDownloadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Thực thi bộ cài đặt .NET x86 đã tải về.
    /// </summary>
    Task<(bool Success, string ErrorMessage)> RunInstallerAsync(string installerPath, bool passive = true);

    /// <summary>
    /// Mở trang tải về trên trình duyệt web mặc định.
    /// </summary>
    void OpenDownloadPageInBrowser();
}
