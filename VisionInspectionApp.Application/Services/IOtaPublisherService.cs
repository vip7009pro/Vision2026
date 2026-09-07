using System;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models.Ota;

namespace VisionInspectionApp.Application.Services;

/// <summary>
/// Dịch vụ đóng gói tệp nén zip và phát hành bản cập nhật OTA lên máy chủ.
/// </summary>
public interface IOtaPublisherService
{
    /// <summary>
    /// Nén toàn bộ thư mục ứng dụng thành tệp .zip với báo cáo tiến độ %.
    /// </summary>
    Task<string> BuildZipPackageAsync(string sourceDirectory, string outputZipPath, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Tính toán mã băm SHA-256 của tệp.
    /// </summary>
    string ComputeSha256(string filePath);

    /// <summary>
    /// Cập nhật số phiên bản mới vào tệp .csproj (nếu tồn tại).
    /// </summary>
    Task<bool> UpdateCsprojVersionAsync(string csprojPath, string newVersion);

    /// <summary>
    /// Tải gói .zip và dữ liệu manifest lên máy chủ qua HTTP POST multipart.
    /// </summary>
    Task<OtaPublishResult> PublishToServerAsync(OtaPublishConfig config, string zipFilePath, UpdateManifest manifest, IProgress<UpdateProgressInfo>? progress = null, CancellationToken ct = default);
}
