using System;
using System.Threading.Tasks;

namespace VisionInspectionApp.Application.Licensing;

public interface ILicenseService
{
    /// <summary>
    /// Trạng thái bản quyền hiện tại
    /// </summary>
    LicenseStatus Status { get; }

    /// <summary>
    /// Thông tin bản quyền hiện tại (nếu hợp lệ)
    /// </summary>
    LicensePayload? CurrentLicense { get; }

    /// <summary>
    /// Mã định danh máy rút gọn (V26-XXXX-XXXX-XXXX-XXXX)
    /// </summary>
    string FormattedMachineCode { get; }

    /// <summary>
    /// Mã băm phần cứng đầy đủ của máy (SHA-256)
    /// </summary>
    string MachineFingerprint { get; }

    /// <summary>
    /// Số ngày sử dụng còn lại (-1 nếu vĩnh viễn)
    /// </summary>
    int RemainingDays { get; }

    /// <summary>
    /// Cho biết máy trạm có đang trong trạng thái chờ quản trị viên phê duyệt trên Server hay không
    /// </summary>
    bool IsPendingApproval { get; }

    /// <summary>
    /// Thông báo trạng thái phê duyệt (nếu đang chờ hoặc bị từ chối)
    /// </summary>
    string? PendingApprovalMessage { get; }

    /// <summary>
    /// Kiểm tra và xác thực tính hợp lệ của bản quyền trên máy
    /// </summary>
    Task<LicenseValidationResult> ValidateLicenseAsync();

    /// <summary>
    /// Tự động đăng ký máy mới lên Server hoặc thăm dò trạng thái phê duyệt.
    /// Nếu Admin đã duyệt trên Web, hàm sẽ tự động lưu bản quyền và kích hoạt máy trạm.
    /// </summary>
    Task<LicenseActivationResult> AutoRegisterOrCheckApprovalAsync(string? customServerUrl = null);

    /// <summary>
    /// Kích hoạt bản quyền Online qua License Server trên Internet
    /// </summary>
    Task<LicenseActivationResult> ActivateOnlineAsync(string licenseKey, string? customServerUrl = null);

    /// <summary>
    /// Sinh chuỗi mã yêu cầu cấp phép Offline (.req) để gửi cho Admin
    /// </summary>
    Task<string> GenerateOfflineRequestCodeAsync();

    /// <summary>
    /// Kích hoạt bản quyền Offline bằng nội dung file .lic do Admin ký số
    /// </summary>
    Task<LicenseActivationResult> ActivateOfflineAsync(string licenseFileContent);

    /// <summary>
    /// Hủy kích hoạt / Xóa bản quyền local trên máy hiện tại
    /// </summary>
    Task<bool> DeactivateLocalAsync();

    /// <summary>
    /// Kiểm tra xem một tính năng cụ thể có được phép chạy không
    /// </summary>
    bool IsFeatureAllowed(string featureName);

    /// <summary>
    /// Khẳng định bản quyền hợp lệ để thực thi phân tích ảnh (Run Flow / Inspection).
    /// Ném ra ngoại lệ nếu bản quyền không hợp lệ hoặc đã bị thu hồi.
    /// </summary>
    void AssertCanExecuteInspection();

    /// <summary>
    /// Sự kiện thông báo khi trạng thái bản quyền thay đổi (ví dụ khi bị thu hồi từ xa)
    /// </summary>
    event EventHandler<LicenseStatus>? StatusChanged;
}
