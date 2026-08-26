using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

/// <summary>
/// Giao diện quản lý Lịch sử kiểm tra và Background Logging Worker
/// </summary>
public interface IInspectionLogService : IDisposable
{
    /// <summary>
    /// Phiên làm việc hiện tại
    /// </summary>
    InspectionSessionRecord? CurrentSession { get; }

    /// <summary>
    /// Bắt đầu một phiên làm việc mới khi chạy Continuous hoặc Batch
    /// </summary>
    Task<InspectionSessionRecord> StartSessionAsync(string productName, string jobFilePath, string material = "-");

    /// <summary>
    /// Kết thúc phiên làm việc hiện tại khi bấm Stop
    /// </summary>
    Task<InspectionSessionRecord?> EndSessionAsync();

    /// <summary>
    /// Ghi nhận kết quả kiểm tra 1 frame (Đẩy vào Channel Background không chặn luồng Vision)
    /// </summary>
    void EnqueueInspectionResult(InspectionResult result, VisionConfig? config, int partIndex);

    /// <summary>
    /// Lấy danh sách toàn bộ các phiên làm việc đã lưu
    /// </summary>
    Task<IReadOnlyList<InspectionSessionRecord>> GetAllSessionsAsync();

    /// <summary>
    /// Lấy danh sách chi tiết các con hàng trong một phiên
    /// </summary>
    Task<IReadOnlyList<InspectionPartRecord>> GetPartsForSessionAsync(string sessionId);

    /// <summary>
    /// Xóa một phiên kiểm tra khỏi lịch sử
    /// </summary>
    Task<bool> DeleteSessionAsync(string sessionId);

    /// <summary>
    /// Xóa toàn bộ lịch sử kiểm tra
    /// </summary>
    Task ClearAllHistoryAsync();

    /// <summary>
    /// Sự kiện khi có phiên mới được tạo hoặc cập nhật
    /// </summary>
    event EventHandler<InspectionSessionRecord>? SessionUpdated;

    /// <summary>
    /// Sự kiện khi có con hàng mới được ghi nhận trong phiên hiện tại
    /// </summary>
    event EventHandler<InspectionPartRecord>? PartLogged;
}
