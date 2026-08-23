using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Services;

public sealed class PendingRejectItem
{
    public required RollDefectItem Defect { get; init; }
    public double TargetWebY_Mm { get; init; }
    public bool Executed { get; set; }
}

/// <summary>
/// Cơ cấu Shift Register thời gian thực: Theo dõi vị trí lỗi theo mét dài di chuyển của cuộn,
/// tự động gửi lệnh kích hoạt cơ cấu Reject / Phun Mực / Dao Cắt đúng thời điểm vết lỗi đi qua trạm ($L_{reject}$ mm).
/// </summary>
public sealed class ShiftRegisterTracker : IDisposable
{
    private readonly IPlcManagerService? _plcManager;
    private readonly ConcurrentQueue<PendingRejectItem> _pendingItems = new();
    private readonly object _lock = new();

    private double _rejectStationDistanceMm = 1500.0; // Khoảng cách từ Camera đến Trạm Reject (1.5 mét)
    private double _rejectToleranceMm = 15.0;         // Dung sai kích hoạt +/- 15mm
    private int _pulseDurationMs = 100;               // Thời gian giữ xung kích hoạt 100ms
    private string _plcId = "PLC1";
    private string _rejectTagName = "Y0_RejectPiston";
    private bool _isEnabled = true;
    private long _totalRejectsTriggered;

    public double RejectStationDistanceMm
    {
        get => _rejectStationDistanceMm;
        set => _rejectStationDistanceMm = value <= 0 ? 100.0 : value;
    }

    public double RejectToleranceMm
    {
        get => _rejectToleranceMm;
        set => _rejectToleranceMm = value <= 0 ? 5.0 : value;
    }

    public int PulseDurationMs
    {
        get => _pulseDurationMs;
        set => _pulseDurationMs = Math.Clamp(value, 20, 2000);
    }

    public string PlcId
    {
        get => _plcId;
        set => _plcId = value ?? "PLC1";
    }

    public string RejectTagName
    {
        get => _rejectTagName;
        set => _rejectTagName = value ?? "Y0_RejectPiston";
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public long TotalRejectsTriggered => Interlocked.Read(ref _totalRejectsTriggered);
    public int PendingCount => _pendingItems.Count;

    public event EventHandler<PendingRejectItem>? OnRejectTriggered;

    public ShiftRegisterTracker(IPlcManagerService? plcManager = null)
    {
        _plcManager = plcManager;
    }

    /// <summary>
    /// Đăng ký một vết lỗi vào hàng đợi theo dõi Shift Register
    /// </summary>
    public void EnqueueDefect(RollDefectItem defect)
    {
        if (defect == null || !_isEnabled) return;

        var item = new PendingRejectItem
        {
            Defect = defect,
            TargetWebY_Mm = defect.WebY_Mm + _rejectStationDistanceMm,
            Executed = false
        };

        _pendingItems.Enqueue(item);
    }

    /// <summary>
    /// Kiểm tra vị trí mét dài hiện tại của cuộn và kích hoạt cơ cấu Reject khi vết lỗi tới trạm
    /// </summary>
    /// <param name="currentWebPositionMm">Vị trí mét dài hiện tại đọc từ Encoder (mm)</param>
    public List<PendingRejectItem> ProcessMotionUpdate(double currentWebPositionMm)
    {
        var triggeredList = new List<PendingRejectItem>();
        if (!_isEnabled || _pendingItems.IsEmpty) return triggeredList;

        // Duyệt qua các item trong hàng đợi
        while (_pendingItems.TryPeek(out var item))
        {
            // Kiểm tra xem vị trí hiện tại đã tới vùng kích hoạt hay chưa
            if (currentWebPositionMm >= (item.TargetWebY_Mm - _rejectToleranceMm))
            {
                // Lấy ra khỏi hàng đợi
                if (_pendingItems.TryDequeue(out var dequeuedItem))
                {
                    dequeuedItem.Executed = true;
                    dequeuedItem.Defect.RejectTriggered = true;
                    dequeuedItem.Defect.RejectTriggeredTime = DateTime.UtcNow;
                    Interlocked.Increment(ref _totalRejectsTriggered);

                    // Kích hoạt tín hiệu phần cứng tới PLC
                    TriggerPlcActuator();

                    triggeredList.Add(dequeuedItem);
                    OnRejectTriggered?.Invoke(this, dequeuedItem);
                }
            }
            else
            {
                // Item đầu tiên chưa tới, các item sau ở xa hơn nên dừng kiểm tra
                break;
            }
        }

        return triggeredList;
    }

    /// <summary>
    /// Gửi xung kích hoạt cơ cấu chấp hành (Actuator) qua PLC Driver
    /// </summary>
    private void TriggerPlcActuator()
    {
        if (_plcManager == null || string.IsNullOrWhiteSpace(_rejectTagName)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                // 1. Gửi lệnh BẬT (ON)
                await _plcManager.WriteTagValueAsync(_plcId, _rejectTagName, true);
                
                // 2. Chờ thời gian xung
                await Task.Delay(_pulseDurationMs);

                // 3. Gửi lệnh TẮT (OFF)
                await _plcManager.WriteTagValueAsync(_plcId, _rejectTagName, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShiftRegisterTracker] Lỗi kích hoạt Reject Actuator: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Xóa toàn bộ hàng đợi khi bắt đầu cuộn mới
    /// </summary>
    public void Reset()
    {
        while (_pendingItems.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _totalRejectsTriggered, 0);
    }

    public void Dispose()
    {
        Reset();
    }
}
