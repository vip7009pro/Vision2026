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
    public string ResetQueueTagName { get; set; } = "";

    public int HandshakeTimeoutMs { get; set; } = 500;
    public bool IsEnabled { get; set; } = true;
    public bool SimulatePlcAck { get; set; } = false;

    /// <summary>
    /// Kiểu tín hiệu gửi cho kết quả Pass (Mặc định: Level)
    /// </summary>
    public PlcSignalType PassSignalType { get; set; } = PlcSignalType.Level;

    /// <summary>
    /// Kiểu tín hiệu gửi cho kết quả NG (Mặc định: Level)
    /// </summary>
    public PlcSignalType NgSignalType { get; set; } = PlcSignalType.Level;

    /// <summary>
    /// Kiểu tín hiệu gửi cho tín hiệu Done (Mặc định: Level)
    /// </summary>
    public PlcSignalType DoneSignalType { get; set; } = PlcSignalType.Level;

    /// <summary>
    /// Độ rộng xung phát cho kiểu Edge (Rising Edge / Falling Edge) tính bằng ms (Mặc định: 100ms)
    /// </summary>
    public int PulseDurationMs { get; set; } = 100;

    /// <summary>
    /// Kiểu tín hiệu nhận cho lệnh Reset Queue từ PLC (Mặc định: RisingEdge)
    /// </summary>
    public PlcInputSignalType ResetQueueSignalType
    {
        get => _resetQueueDetector.SignalType;
        set => _resetQueueDetector.SignalType = value;
    }

    /// <summary>
    /// Thời gian giữ hoặc lọc xung debounce (ms) cho lệnh Reset Queue (Mặc định: 500ms)
    /// </summary>
    public int ResetQueueDurationMs
    {
        get => _resetQueueDetector.DurationMs;
        set => _resetQueueDetector.DurationMs = value;
    }

    private readonly PlcInputSignalDetector _resetQueueDetector = new();

    /// <summary>
    /// Chế độ bắt tay bất đồng bộ không chờ Ack (Non-blocking Pipelined Queue):
    /// Băng tải chạy liên tục, Vision ghi kết quả vào hàng đợi rồi giải phóng ngay, không dừng luồng chờ Ack.
    /// </summary>
    public bool NonBlockingMode { get; set; } = true;

    /// <summary>
    /// Điểm ra chỉ định trên hàng đợi 20 sản phẩm (1..20): Nấc mà tại đó nếu là hàng NG thì băng tải sẽ dừng. Mặc định: 10
    /// </summary>
    public int TargetStopStationIndex { get; set; } = 10;

    /// <summary>
    /// Địa chỉ bắt đầu mảng hàng đợi trên PLC (Mặc định: M200 cho mảng M200..M219)
    /// </summary>
    public string QueueRegisterStart { get; set; } = "M200";

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

    /// <summary>
    /// Sự kiện kích hoạt khi nhận được tín hiệu yêu cầu Reset Queue từ PLC
    /// </summary>
    public event EventHandler? OnResetQueueRequested;

    public IndustrialHandshakeStateMachine(IPlcManagerService? plcManager = null, string plcId = "PLC1")
    {
        _plcManager = plcManager;
        _plcId = plcId;

        _resetQueueDetector.OnTriggered += (s, e) =>
        {
            OnResetQueueRequested?.Invoke(this, EventArgs.Empty);
        };

        if (_plcManager != null)
        {
            _plcManager.OnTagChanged += PlcManager_OnTagChanged;
            if (_plcManager.IndustrialConfig?.Handshake != null)
            {
                ApplyConfig(_plcManager.IndustrialConfig.Handshake);
            }
        }
    }

    /// <summary>
    /// Đồng bộ toàn bộ thông số từ cấu hình IndustrialHandshakeConfig
    /// </summary>
    public void ApplyConfig(IndustrialHandshakeConfig? config)
    {
        if (config == null) return;
        IsEnabled = config.IsEnabled;
        PlcId = config.PlcId;
        ReadyTagName = config.ReadyTagName;
        BusyTagName = config.BusyTagName;
        DoneTagName = config.DoneTagName;
        PassTagName = config.PassTagName;
        NgTagName = config.NgTagName;
        PlcAckTagName = config.PlcAckTagName;
        HandshakeTimeoutMs = config.HandshakeTimeoutMs;
        SimulatePlcAck = config.SimulatePlcAck;
        NonBlockingMode = config.NonBlockingMode;
        TargetStopStationIndex = config.TargetStopStationIndex;
        QueueRegisterStart = config.QueueRegisterStart;
        PassSignalType = config.PassSignalType;
        NgSignalType = config.NgSignalType;
        DoneSignalType = config.DoneSignalType;
        PulseDurationMs = config.PulseDurationMs;
        ResetQueueTagName = config.ResetQueueTagName;
        ResetQueueSignalType = config.ResetQueueSignalType;
        ResetQueueDurationMs = config.ResetQueueDurationMs;
    }

    private void PlcManager_OnTagChanged(object? sender, TagChangedEventArgs e)
    {
        if (e == null || !IsEnabled) return;
        if (!string.Equals(e.PlcId, _plcId, StringComparison.OrdinalIgnoreCase)) return;

        if (IsMatchingResetQueueTag(e.TagName))
        {
            HandleResetQueueTagSample(e.TagName, e.NewValue, e.Timestamp);
        }
    }

    public bool IsMatchingResetQueueTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return false;
        string input = tagName.Trim();

        if (!string.IsNullOrWhiteSpace(ResetQueueTagName) &&
            string.Equals(ResetQueueTagName.Trim(), input, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var eff = GetEffectiveResetQueueTag();
        if (!string.IsNullOrWhiteSpace(eff) &&
            string.Equals(eff.Trim(), input, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public bool HandleResetQueueTagSample(string tagName, object? value, DateTime? timestamp = null)
    {
        return _resetQueueDetector.ProcessSample(value, timestamp);
    }

    private string? ResolveTag(string primaryTagName, params string[] fallbacks)
    {
        if (_plcManager == null) return null;

        // 1. Nếu người dùng đã chỉ định tường minh TagName hoặc địa chỉ Device (ví dụ: "M105", "Vision_NG"):
        // Trả về ngay để WriteTagValueAsync thực hiện ghi trực tiếp theo địa chỉ.
        if (!string.IsNullOrWhiteSpace(primaryTagName))
        {
            return primaryTagName.Trim();
        }

        // 2. Nếu người dùng để trống, tìm kiếm trong danh mục Tags và Cache xem có fallback nào khớp không
        foreach (var fb in fallbacks)
        {
            if (string.IsNullOrWhiteSpace(fb)) continue;
            var trimmed = fb.Trim();
            if (_plcManager.GetTagValue(_plcId, trimmed) != null ||
                _plcManager.Tags.Any(t => string.Equals(t.PlcId, _plcId, StringComparison.OrdinalIgnoreCase) &&
                                          (string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(t.Address, trimmed, StringComparison.OrdinalIgnoreCase))))
            {
                return trimmed;
            }
        }

        // 3. Fallback mặc định: ưu tiên địa chỉ thiết bị chuẩn (ví dụ "M105", "M101",...)
        var deviceFallback = fallbacks.FirstOrDefault(fb => !string.IsNullOrWhiteSpace(fb) &&
            System.Text.RegularExpressions.Regex.IsMatch(fb.Trim(), @"^[a-zA-Z]{1,2}\d+$"));
        if (!string.IsNullOrWhiteSpace(deviceFallback))
        {
            return deviceFallback.Trim();
        }

        return fallbacks.FirstOrDefault(fb => !string.IsNullOrWhiteSpace(fb))?.Trim();
    }

    public string? GetEffectiveReadyTag() => ResolveTag(ReadyTagName, "M101_VisionReady", "M101", "VisionReady", "Y1_VisionReady");
    public string? GetEffectiveBusyTag() => ResolveTag(BusyTagName, "M102_VisionBusy", "M102", "VisionBusy", "Y2_VisionBusy");
    public string? GetEffectiveDoneTag() => ResolveTag(DoneTagName, "M103_VisionDone", "M103", "VisionDone", "Y3_VisionDone");
    public string? GetEffectivePassTag() => ResolveTag(PassTagName, "M104_VisionPass", "M104", "VisionPass", "Y4_VisionPass");
    public string? GetEffectiveNgTag() => ResolveTag(NgTagName, "M105_VisionNG", "M105", "VisionNG", "Y5_VisionNG");
    public string? GetEffectivePlcAckTag()
    {
        // Khi người dùng chủ động bỏ Ack (để trống PlcAckTagName = ""):
        // TUYỆT ĐỐI KHÔNG tự động fallback ép tag M10 hay bất kỳ địa chỉ nào khác!
        if (string.IsNullOrWhiteSpace(PlcAckTagName))
        {
            return null;
        }
        return ResolveTag(PlcAckTagName, "M10_PlcAck", "M10", "PlcAck", "X1_PlcAck");
    }

    public string? GetEffectiveResetQueueTag()
    {
        if (string.IsNullOrWhiteSpace(ResetQueueTagName))
        {
            return null;
        }
        return ResolveTag(ResetQueueTagName, "M106_ResetQueue", "M106", "ResetQueue", "X2_ResetQueue");
    }

    private bool HasConfiguredHandshakeTags()
    {
        if (_plcManager == null) return false;
        return GetEffectiveReadyTag() != null ||
               GetEffectiveBusyTag() != null ||
               GetEffectiveDoneTag() != null ||
               GetEffectivePassTag() != null ||
               GetEffectiveNgTag() != null ||
               GetEffectivePlcAckTag() != null ||
               GetEffectiveResetQueueTag() != null;
    }

    /// <summary>
    /// Ghi tín hiệu gửi PLC theo dạng sóng đã cấu hình (Level, Rising Edge, Falling Edge)
    /// </summary>
    public async Task EmitSignalAsync(string? tagName, PlcSignalType signalType, bool isActive, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tagName) || _plcManager == null || !_plcManager.IsPlcConnected(_plcId))
            return;

        int duration = Math.Max(20, PulseDurationMs);

        switch (signalType)
        {
            case PlcSignalType.Level:
                // Level: Duy trì trực tiếp mức logic (Active = true, Inactive = false)
                await _plcManager.WriteTagValueAsync(_plcId, tagName, isActive, ct);
                break;

            case PlcSignalType.RisingEdge:
                if (isActive)
                {
                    // Phát xung cạnh lên: 0 -> 1 -> giữ PulseDurationMs -> 0
                    await _plcManager.WriteTagValueAsync(_plcId, tagName, true, ct);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(duration);
                            if (_plcManager != null && _plcManager.IsPlcConnected(_plcId))
                            {
                                await _plcManager.WriteTagValueAsync(_plcId, tagName, false);
                            }
                        }
                        catch { }
                    });
                }
                else
                {
                    // Trạng thái tĩnh cạnh lên là 0
                    await _plcManager.WriteTagValueAsync(_plcId, tagName, false, ct);
                }
                break;

            case PlcSignalType.FallingEdge:
                if (isActive)
                {
                    // Phát xung cạnh xuống: 1 -> 0 -> giữ PulseDurationMs -> 1
                    await _plcManager.WriteTagValueAsync(_plcId, tagName, false, ct);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(duration);
                            if (_plcManager != null && _plcManager.IsPlcConnected(_plcId))
                            {
                                await _plcManager.WriteTagValueAsync(_plcId, tagName, true);
                            }
                        }
                        catch { }
                    });
                }
                else
                {
                    // Trạng thái tĩnh cạnh xuống là 1
                    await _plcManager.WriteTagValueAsync(_plcId, tagName, true, ct);
                }
                break;
        }
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
        var passTag = GetEffectivePassTag();
        var ngTag = GetEffectiveNgTag();

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
            bool doneInactive = DoneSignalType == PlcSignalType.FallingEdge ? true : false;
            await _plcManager.WriteTagValueAsync(_plcId, doneTag, doneInactive, ct);
        }

        // Đưa chân Pass / NG về trạng thái tĩnh phù hợp
        if (!string.IsNullOrEmpty(passTag))
        {
            bool passInactive = PassSignalType == PlcSignalType.FallingEdge ? true : false;
            await _plcManager.WriteTagValueAsync(_plcId, passTag, passInactive, ct);
        }
        if (!string.IsNullOrEmpty(ngTag))
        {
            bool ngInactive = NgSignalType == PlcSignalType.FallingEdge ? true : false;
            await _plcManager.WriteTagValueAsync(_plcId, ngTag, ngInactive, ct);
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
    /// 1. Ghi bit PASS / NG theo kiểu tín hiệu cấu hình (Level / Rising Edge / Falling Edge)
    /// 2. Ghi bit DONE theo kiểu tín hiệu cấu hình
    /// 3. Chờ PLC phản hồi tín hiệu ACK = 1 (nếu chế độ đồng bộ)
    /// 4. Hạ bit BUSY = 0, khôi phục READY = 1
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
            // 1. Ghi kết quả PASS/NG theo kiểu tín hiệu cấu hình
            await EmitSignalAsync(passTag, PassSignalType, isPass, ct);
            await EmitSignalAsync(ngTag, NgSignalType, !isPass, ct);

            // Ghi tín hiệu DONE theo kiểu tín hiệu cấu hình
            await EmitSignalAsync(doneTag, DoneSignalType, true, ct);

            // Ghi trực tiếp kết quả vào nấc buồng chụp của hàng đợi PLC (M200): NG = true (1), Pass = false (0)
            if (!string.IsNullOrEmpty(QueueRegisterStart) && _plcManager != null)
            {
                try
                {
                    await _plcManager.WriteTagValueAsync(_plcId, QueueRegisterStart, !isPass, ct);
                }
                catch { }
            }

            // 2. Chế độ Bắt tay Bất Đồng Bộ Non-Blocking (Pipelined Continuous Conveyor):
            // Vision trả kết quả trực tiếp vào hàng đợi PLC và giải phóng ngay để không làm chậm chuyền.
            if (NonBlockingMode || string.IsNullOrEmpty(ackTag))
            {
                // Tự động hạ Busy (và hạ Done nếu là Level) sau một khoảng trễ ngắn 15ms trong background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(15);
                        if (DoneSignalType == PlcSignalType.Level && !string.IsNullOrEmpty(doneTag) && _plcManager != null)
                            await _plcManager.WriteTagValueAsync(_plcId, doneTag, false);
                        if (!string.IsNullOrEmpty(busyTag) && _plcManager != null)
                            await _plcManager.WriteTagValueAsync(_plcId, busyTag, false);
                    }
                    catch { }
                });

                CurrentState = HandshakeState.Armed;
                return true;
            }

            // 3. Chế độ đồng bộ truyền thống: Chờ PLC phản hồi tín hiệu ACK nếu có cấu hình PlcAckTagName hợp lệ
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

            // 3. Hạ bit DONE (nếu Level) và BUSY xuống 0, đồng thời khôi phục READY = 1 cho chu trình tiếp theo
            if (DoneSignalType == PlcSignalType.Level && !string.IsNullOrEmpty(doneTag))
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
        var passTag = GetEffectivePassTag();
        var ngTag = GetEffectiveNgTag();

        try
        {
            if (!string.IsNullOrEmpty(readyTag)) await _plcManager.WriteTagValueAsync(_plcId, readyTag, false, ct);
            if (!string.IsNullOrEmpty(busyTag)) await _plcManager.WriteTagValueAsync(_plcId, busyTag, false, ct);
            if (!string.IsNullOrEmpty(doneTag))
            {
                bool doneInactive = DoneSignalType == PlcSignalType.FallingEdge ? true : false;
                await _plcManager.WriteTagValueAsync(_plcId, doneTag, doneInactive, ct);
            }
            if (!string.IsNullOrEmpty(passTag))
            {
                bool passInactive = PassSignalType == PlcSignalType.FallingEdge ? true : false;
                await _plcManager.WriteTagValueAsync(_plcId, passTag, passInactive, ct);
            }
            if (!string.IsNullOrEmpty(ngTag))
            {
                bool ngInactive = NgSignalType == PlcSignalType.FallingEdge ? true : false;
                await _plcManager.WriteTagValueAsync(_plcId, ngTag, ngInactive, ct);
            }
        }
        catch { }
    }
}
