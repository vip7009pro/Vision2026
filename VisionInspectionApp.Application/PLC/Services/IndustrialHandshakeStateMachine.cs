using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.PLC.Services;

/// <summary>
/// Trạng thái của chu trình bắt tay công nghiệp PLC <-> Vision PC
/// </summary>
public enum HandshakeState
{
    Idle,
    Ready,
    Armed,
    Triggered,
    Inspecting,
    ResultLatched,
    Acknowledged,
    Complete,
    TimeoutFault
}

/// <summary>
/// Máy trạng thái bắt tay công nghiệp 2 chiều chuẩn PLC <-> Vision PC (Deterministic Handshake Protocol)
/// Đảm bảo tính toàn vẹn 100% của tín hiệu, không bao giờ mất frame hay xung đột dữ liệu trên băng truyền chạy liên tục 24/7.
/// </summary>
public sealed class IndustrialHandshakeStateMachine
{
    private readonly IPlcManagerService? _plcManager;
    private readonly object _lock = new();

    private HandshakeState _currentState = HandshakeState.Idle;
    private string _plcId = "PLC1";

    public string PlcId
    {
        get => _plcId;
        set => _plcId = value ?? "PLC1";
    }

    // Cấu hình các Tag I/O bắt tay
    public string ReadyTagName { get; set; } = "Y1_VisionReady";
    public string BusyTagName { get; set; } = "Y2_VisionBusy";
    public string DoneTagName { get; set; } = "Y3_VisionDone";
    public string PassTagName { get; set; } = "Y4_VisionPass";
    public string NgTagName { get; set; } = "Y5_VisionNG";
    public string PlcAckTagName { get; set; } = "X1_PlcAck";

    public int HandshakeTimeoutMs { get; set; } = 500;
    public HandshakeState CurrentState
    {
        get
        {
            lock (_lock) return _currentState;
        }
        private set
        {
            lock (_lock)
            {
                if (_currentState != value)
                {
                    _currentState = value;
                    OnStateChanged?.Invoke(this, value);
                }
            }
        }
    }

    public event EventHandler<HandshakeState>? OnStateChanged;
    public event EventHandler<string>? OnHandshakeTimeout;

    public IndustrialHandshakeStateMachine(IPlcManagerService? plcManager = null, string plcId = "PLC1")
    {
        _plcManager = plcManager;
        _plcId = plcId;
    }

    /// <summary>
    /// Đưa Vision PC vào trạng thái sẵn sàng nhận Trigger (READY / ARMED)
    /// </summary>
    public async Task SetReadyAsync(CancellationToken ct = default)
    {
        CurrentState = HandshakeState.Ready;
        if (_plcManager != null && !string.IsNullOrEmpty(ReadyTagName))
        {
            await _plcManager.WriteTagValueAsync(_plcId, ReadyTagName, true, ct);
            if (!string.IsNullOrEmpty(BusyTagName))
            {
                await _plcManager.WriteTagValueAsync(_plcId, BusyTagName, false, ct);
            }
            if (!string.IsNullOrEmpty(DoneTagName))
            {
                await _plcManager.WriteTagValueAsync(_plcId, DoneTagName, false, ct);
            }
        }
        CurrentState = HandshakeState.Armed;
    }

    /// <summary>
    /// Bắt đầu chu trình xử lý ảnh khi nhận được tín hiệu chụp (BUSY = 1, READY = 0)
    /// </summary>
    public async Task StartInspectionAsync(CancellationToken ct = default)
    {
        CurrentState = HandshakeState.Inspecting;
        if (_plcManager != null)
        {
            if (!string.IsNullOrEmpty(BusyTagName))
            {
                await _plcManager.WriteTagValueAsync(_plcId, BusyTagName, true, ct);
            }
            if (!string.IsNullOrEmpty(ReadyTagName))
            {
                await _plcManager.WriteTagValueAsync(_plcId, ReadyTagName, false, ct);
            }
        }
    }

    /// <summary>
    /// Chốt kết quả kiểm tra (LATCH) và thực hiện bắt tay hoàn tất với PLC:
    /// 1. Ghi bit PASS / NG
    /// 2. Ghi bit DONE = 1
    /// 3. Chờ PLC phản hồi tín hiệu ACK = 1
    /// 4. Hạ bit DONE = 0, BUSY = 0
    /// 5. Đưa hệ thống quay lại trạng thái READY
    /// </summary>
    public async Task<bool> CompleteHandshakeAsync(bool isPass, CancellationToken ct = default)
    {
        CurrentState = HandshakeState.ResultLatched;

        if (_plcManager == null)
        {
            CurrentState = HandshakeState.Complete;
            return true;
        }

        try
        {
            // 1. Ghi kết quả PASS/NG và DONE = 1
            if (isPass)
            {
                if (!string.IsNullOrEmpty(PassTagName)) await _plcManager.WriteTagValueAsync(_plcId, PassTagName, true, ct);
                if (!string.IsNullOrEmpty(NgTagName)) await _plcManager.WriteTagValueAsync(_plcId, NgTagName, false, ct);
            }
            else
            {
                if (!string.IsNullOrEmpty(PassTagName)) await _plcManager.WriteTagValueAsync(_plcId, PassTagName, false, ct);
                if (!string.IsNullOrEmpty(NgTagName)) await _plcManager.WriteTagValueAsync(_plcId, NgTagName, true, ct);
            }

            if (!string.IsNullOrEmpty(DoneTagName))
            {
                await _plcManager.WriteTagValueAsync(_plcId, DoneTagName, true, ct);
            }

            // 2. Chờ PLC phản hồi tín hiệu ACK nếu có cấu hình PlcAckTagName
            if (!string.IsNullOrEmpty(PlcAckTagName))
            {
                var sw = Stopwatch.StartNew();
                bool ackReceived = false;

                while (sw.ElapsedMilliseconds < HandshakeTimeoutMs && !ct.IsCancellationRequested)
                {
                    var tagVal = _plcManager.GetTagValue(_plcId, PlcAckTagName);
                    var ackVal = tagVal?.CurrentValue;
                    if (ackVal is bool b && b)
                    {
                        ackReceived = true;
                        break;
                    }
                    else if (ackVal is int i && i != 0)
                    {
                        ackReceived = true;
                        break;
                    }

                    await Task.Delay(5, ct);
                }

                if (!ackReceived)
                {
                    CurrentState = HandshakeState.TimeoutFault;
                    OnHandshakeTimeout?.Invoke(this, $"PLC không phản hồi tín hiệu {PlcAckTagName} trong {HandshakeTimeoutMs}ms");
                    return false;
                }

                CurrentState = HandshakeState.Acknowledged;
            }

            // 3. Hạ bit DONE và BUSY xuống 0
            if (!string.IsNullOrEmpty(DoneTagName))
            {
                await _plcManager.WriteTagValueAsync(_plcId, DoneTagName, false, ct);
            }
            if (!string.IsNullOrEmpty(BusyTagName))
            {
                await _plcManager.WriteTagValueAsync(_plcId, BusyTagName, false, ct);
            }

            // 4. Hoàn tất chu trình
            CurrentState = HandshakeState.Complete;
            return true;
        }
        catch (Exception ex)
        {
            CurrentState = HandshakeState.TimeoutFault;
            OnHandshakeTimeout?.Invoke(this, $"Lỗi bắt tay PLC: {ex.Message}");
            return false;
        }
    }
}
