using System;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

public interface ISystemConfigBackupService
{
    /// <summary>
    /// Bắn ra khi toàn bộ hoặc một phần cấu hình hệ thống vừa được phục hồi từ tệp sao lưu
    /// </summary>
    event EventHandler? SystemConfigRestored;

    /// <summary>
    /// Tạo gói sao lưu hoàn chỉnh từ các cấu hình đang hoạt động trên máy này
    /// </summary>
    Task<SystemConfigPackage> CreateBackupPackageAsync(SystemConfigBackupOptions? options = null);

    /// <summary>
    /// Xuất gói sao lưu ra tệp JSON
    /// </summary>
    Task<(bool Success, string FilePath, string ErrorMessage)> ExportBackupPackageToFileAsync(string filePath, SystemConfigBackupOptions? options = null);

    /// <summary>
    /// Phân tích và kiểm tra gói sao lưu trước khi thực hiện nạp
    /// </summary>
    SystemConfigPackageInspection InspectBackupPackage(string filePath);

    /// <summary>
    /// Phục hồi cấu hình từ tệp sao lưu, tự động ghi ra đĩa và kích hoạt runtime nóng
    /// </summary>
    Task<SystemConfigRestoreResult> RestoreBackupPackageFromFileAsync(string filePath, SystemConfigRestoreOptions? options = null);
}
