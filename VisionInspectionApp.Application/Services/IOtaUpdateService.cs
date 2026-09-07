using System;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models.Ota;

namespace VisionInspectionApp.Application.Services;

/// <summary>
/// Dịch vụ quản lý cập nhật phần mềm từ xa (OTA Update).
/// Hỗ trợ cả máy chủ HTTP nội bộ (Local Server) và GitHub Releases (Cloud).
/// </summary>
public interface IOtaUpdateService
{
    /// <summary>Phiên bản hiện tại của ứng dụng</summary>
    Version CurrentVersion { get; }

    /// <summary>
    /// Kiểm tra phiên bản mới nhất từ máy chủ cập nhật.
    /// </summary>
    /// <param name="serverUrl">Đường dẫn tệp manifest hoặc URL API (nếu null, dùng cấu hình mặc định)</param>
    /// <param name="sourceType">"Auto", "CustomManifest", hoặc "GitHub"</param>
    /// <param name="ct">Token hủy thao tác</param>
    Task<UpdateCheckResult> CheckForUpdateAsync(string? serverUrl = null, string? sourceType = "Auto", CancellationToken ct = default);

    /// <summary>
    /// Tải gói cập nhật (.zip) về thư mục tạm với báo tiến trình thời gian thực.
    /// </summary>
    /// <param name="manifest">Thông tin gói cập nhật</param>
    /// <param name="saveDirectory">Thư mục lưu tệp tải về</param>
    /// <param name="progress">Hàm callback cập nhật tiến độ</param>
    /// <param name="ct">Token hủy thao tác</param>
    /// <returns>Đường dẫn tệp .zip đã tải</returns>
    Task<string> DownloadUpdatePackageAsync(UpdateManifest manifest, string saveDirectory, IProgress<UpdateProgressInfo>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Xác thực tính toàn vẹn của tệp tải về bằng mã băm SHA-256.
    /// </summary>
    bool VerifyPackageChecksum(string filePath, string expectedSha256);

    /// <summary>
    /// Khởi chạy trình cập nhật độc lập (VisionUpdater.exe) và thoát ứng dụng hiện tại để nhả file locks.
    /// </summary>
    /// <param name="zipPackagePath">Đường dẫn tệp gói cập nhật</param>
    /// <param name="restartAfterUpdate">Tự động khởi động lại sau khi giải nén</param>
    /// <returns>True nếu đã kích hoạt trình cập nhật thành công</returns>
    bool LaunchUpdaterAndShutdown(string zipPackagePath, bool restartAfterUpdate = true);

    /// <summary>
    /// Xóa các tệp cập nhật tạm thời cũ.
    /// </summary>
    void CleanupTempUpdates(string updateDirectory);
}
