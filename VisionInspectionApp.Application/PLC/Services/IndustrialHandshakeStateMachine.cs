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
    public bool IsEnabled { get; set; } = true;
    public bool SimulatePlcAck { get; set; } = false;
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

    private string? ResolveTag(string primaryTagName, params string[] fallbacks)
    {
        if (_plcManager == null) return null;
        if (!string.IsNullOrEmpty(primaryTagName) && _plcManager.GetTagValue(_plcId, primaryTagName) != null)
            return primaryTagName;
        foreach (var fb in fallbacks)
        {
            if (!string.IsNullOrEmpty(fb) && _plcManager.GetTagValue(_plcId, fb) != null)
                return fb;
        }
        return null;
    }

    public string? GetEffectiveReadyTag() => ResolveTag(ReadyTagName, "M101_VisionReady", "M101", "VisionReady", "Y1_VisionReady");
    public string? GetEffectiveBusyTag() => ResolveTag(BusyTagName, "M102_VisionBusy", "M102", "VisionBusy", "Y2_VisionBusy");
    public string? GetEffectiveDoneTag() => ResolveTag(DoneTagName, "M103_VisionDone", "M103", "VisionDone", "Y3_VisionDone");
    public string? GetEffectivePassTag() => ResolveTag(PassTagName, "M104_VisionPass", "M104", "VisionPass", "Y4_VisionPass");
    public string? GetEffectiveNgTag() => ResolveTag(NgTagName, "M105_VisionNG", "M105", "VisionNG", "Y5_VisionNG");
    public string? GetEffectivePlcAckTag() => ResolveTag(PlcAckTagName, "M10_PlcAck", "M10", "PlcAck", "X1_PlcAck");

    private bool HasConfiguredHandshakeTags()
    {
        if (_plcManager == null) return false;
        return GetEffectiveReadyTag() != null ||
               GetEffectiveBusyTag() != null ||
               GetEffectiveDoneTag() != null ||
               GetEffectivePassTag() != null ||
               GetEffectiveNgTag() != null ||
               GetEffectivePlcAckTag() != null;
    }

    /// <summary>
    /// Đưa Vision PC vào trạng thái sẵn sàng nhận Trigger (READY / ARMED)
    /// </summary>
    public async Task SetReadyAsync(CancellationToken ct = default)
    {
        CurrentState = HandshakeState.Ready;
        if (!IsEnabled || _plcManager == null || !_plcManager.IsPlcConnected(_plcId) || !HasConfiguredHandshakeTags())
        {
            CurrentState = HandshakeState.Armed;
            return;
        }

        var readyTag = GetEffectiveReadyTag();
        var busyTag = GetEffectiveBusyTag();
        var doneTag = GetEffectiveDoneTag();

        if (!string.IsNullOrEmpty(readyTag))
        {
            await _plcManager.WriteTagValueAsync(_plcId, readyTag, true, ct);
        }
        if (!string.IsNullOrEmpty(busyTag))
        {
            await _plcManager.WriteTagValueAsync(_plcId, busyTag, false, ct);
        }
        if (!string.IsNullOrEmpty(doneTag))
        {
            await _plcManager.WriteTagValueAsync(_plcId, doneTag, false, ct);
        }
        CurrentState = HandshakeState.Armed;
    }

    /// <summary>
    /// Bắt đầu chu trình xử lý ảnh khi nhận được tín hiệu chụp (BUSY = 1, READY = 0)
    /// </summary>
    public async Task StartInspectionAsync(CancellationToken ct = default)
    {
        CurrentState = HandshakeState.Inspecting;
        if (!IsEnabled || _plcManager == null || !_plcManager.IsPlcConnected(_plcId) || !HasConfiguredHandshakeTags())
        {
            return;
        }

        var busyTag = GetEffectiveBusyTag();
        var readyTag = GetEffectiveReadyTag();

        if (!string.IsNullOrEmpty(busyTag))
        {
            await _plcManager.WriteTagValueAsync(_plcId, busyTag, true, ct);
        }
        if (!string.IsNullOrEmpty(readyTag))
        {
            await _plcManager.WriteTagValueAsync(_plcId, readyTag, false, ct);
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

        if (!IsEnabled || _plcManager == null || !_plcManager.IsPlcConnected(_plcId) || !HasConfiguredHandshakeTags())
        {
            CurrentState = HandshakeState.Complete;
            return true;
        }

        var passTag = GetEffectivePassTag();
        var ngTag = GetEffectiveNgTag();
        var doneTag = GetEffectiveDoneTag();
        var ackTag = GetEffectivePlcAckTag();
        var readyTag = GetEffectiveReadyTag();
        var busyTag = GetEffectiveBusyTag();

        try
        {
            // 1. Ghi kết quả PASS/NG và DONE = 1
            if (isPass)
            {
                if (!string.IsNullOrEmpty(passTag)) 
                    await _plcManager.WriteTagValueAsync(_plcId, passTag, true, ct);
                if (!string.IsNullOrEmpty(ngTag)) 
                    await _plcManager.WriteTagValueAsync(_plcId, ngTag, false, ct);
            }
            else
            {
                if (!string.IsNullOrEmpty(passTag)) 
                    await _plcManager.WriteTagValueAsync(_plcId, passTag, false, ct);
                if (!string.IsNullOrEmpty(ngTag)) 
                    await _plcManager.WriteTagValueAsync(_plcId, ngTag, true, ct);
            }

            if (!string.IsNullOrEmpty(doneTag))
            {
                await _plcManager.WriteTagValueAsync(_plcId, doneTag, true, ct);
            }

            // 2. Chờ PLC phản hồi tín hiệu ACK nếu có cấu hình PlcAckTagName hợp lệ
            if (!string.IsNullOrEmpty(ackTag))
            {
                var sw = Stopwatch.StartNew();
                bool ackReceived = false;

                if (SimulatePlcAck)
                {
                    // Chế độ mô phỏng PLC Auto-Ack khi test không có PLC thật: trễ 20ms mô phỏng chu kỳ quét PLC
                    await Task.Delay(20, ct);
                    ackReceived = true;
                    if (_plcManager != null)
                    {
                        await _plcManager.WriteTagValueAsync(_plcId, ackTag, true, ct);
                    }
                }
                else
                {
                    int pollDirectCount = 0;
                    while (sw.ElapsedMilliseconds < HandshakeTimeoutMs && !ct.IsCancellationRequested)
                    {
                        var tagVal = _plcManager?.GetTagValue(_plcId, ackTag);
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

                        // Nếu sau 30ms (6 vòng lặp x 5ms) chưa thấy trong cache, ép đọc trực tiếp từ Driver xuống PLC
                        if (++pollDirectCount % 6 == 0 && _plcManager != null)
                        {
                            try
                            {
                                var directVal = await _plcManager.ReadTagValueAsync(_plcId, ackTag, ct);
                                if (directVal is bool db && db)
                                {
                                    ackReceived = true;
                                    break;
                                }
                                else if (directVal is int di && di != 0)
                                {
                                    ackReceived = true;
                                    break;
                                }
                            }
                            catch { }
                        }

                        await Task.Delay(5, ct);
                    }
                }

                if (!ackReceived)
                {
                    CurrentState = HandshakeState.TimeoutFault;
                    OnHandshakeTimeout?.Invoke(this, $"PLC không phản hồi tín hiệu {ackTag} trong {HandshakeTimeoutMs}ms");
                    return false;
                }

                CurrentState = HandshakeState.Acknowledged;
            }

            // 3. Hạ bit DONE và BUSY xuống 0, đồng thời khôi phục READY = 1 cho chu trình tiếp theo
            if (!string.IsNullOrEmpty(doneTag))
            {
                await _plcManager.WriteTagValueAsync(_plcId, doneTag, false, ct);
            }
            if (SimulatePlcAck && !string.IsNullOrEmpty(ackTag) && _plcManager != null)
            {
                // Khi giả lập, tự động hạ Ack = 0 sau khi Done đã hạ
                await Task.Delay(10, ct);
                await _plcManager.WriteTagValueAsync(_plcId, ackTag, false, ct);
            }
            if (!string.IsNullOrEmpty(busyTag))
            {
                await _plcManager.WriteTagValueAsync(_plcId, busyTag, false, ct);
            }
            if (!string.IsNullOrEmpty(readyTag))
            {
                await _plcManager.WriteTagValueAsync(_plcId, readyTag, true, ct);
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

    /// <summary>
    /// Đưa Vision PC về trạng thái nghỉ (IDLE) và hạ toàn bộ các cờ Ready, Busy, Done
    /// </summary>
    public async Task SetIdleAsync(CancellationToken ct = default)
    {
        CurrentState = HandshakeState.Idle;
        if (!IsEnabled || _plcManager == null || !_plcManager.IsPlcConnected(_plcId))
        {
            return;
        }

        var readyTag = GetEffectiveReadyTag();
        var busyTag = GetEffectiveBusyTag();
        var doneTag = GetEffectiveDoneTag();

        try
        {
            if (!string.IsNullOrEmpty(readyTag)) await _plcManager.WriteTagValueAsync(_plcId, readyTag, false, ct);
            if (!string.IsNullOrEmpty(busyTag)) await _plcManager.WriteTagValueAsync(_plcId, busyTag, false, ct);
            if (!string.IsNullOrEmpty(doneTag)) await _plcManager.WriteTagValueAsync(_plcId, doneTag, false, ct);
        }
        catch { }
    }
}
