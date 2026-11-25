using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
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
public sealed class IndustrialHandshakeStateMachine : IDisposable
{
    private readonly IPlcManagerService? _plcManager;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _ioStateWriteLock = new(1, 1);
    private readonly ConcurrentDictionary<string, long> _tagSignalGenerations = new(StringComparer.OrdinalIgnoreCase);
    private readonly DelayedHandshakeDispatcher _delayedDispatcher;

    private long _globalSignalEpoch;
    private volatile bool _isInspecting;
    private bool _disposed;
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

    /// <summary>
    /// Phương thức xuất kết quả kiểm tra sang PLC: Bit (Bool), Dạng Số (Int16) hoặc Song Hành Cả Hai
    /// </summary>
    public PlcResultOutputMode ResultOutputMode { get; set; } = PlcResultOutputMode.Bit;

    /// <summary>
    /// Địa chỉ thanh ghi số nguyên Word trên PLC lưu mã kết quả (Mặc định: D100 hoặc MW100)
    /// </summary>
    public string ResultWordTagName { get; set; } = "D100";

    /// <summary>
    /// Giá trị số nguyên (Int16) gửi sang PLC khi kết quả ĐẠT / OK (Mặc định: 2)
    /// </summary>
    public short PassNumericValue { get; set; } = 2;

    /// <summary>
    /// Giá trị số nguyên (Int16) gửi sang PLC khi kết quả LỖI / NG (Mặc định: 3)
    /// </summary>
    public short NgNumericValue { get; set; } = 3;

    /// <summary>
    /// Giá trị số nguyên (Int16) gửi sang PLC khi ở trạng thái Sẵn sàng / Idle / Reset (Mặc định: 0)
    /// </summary>
    public short IdleNumericValue { get; set; } = 0;

    /// <summary>
    /// Tự động reset giá trị thanh ghi kết quả về IdleNumericValue (0) khi Vision sẵn sàng đón phôi mới
    /// </summary>
    public bool ClearResultWordOnReady { get; set; } = true;

    public int HandshakeTimeoutMs { get; set; } = 500;
    public bool IsEnabled { get; set; } = true;
    public bool EnableHandshakeOnRunOnce { get; set; } = false;
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
    /// Thời gian trễ (ms) trước khi gửi tín hiệu kết quả kiểm tra tới PLC (Mặc định: 0 ms - gửi ngay).
    /// </summary>
    public int DelayTimeMs { get; set; } = 0;

    /// <summary>
    /// Số lượng tín hiệu kết quả đang chờ trong hàng đợi trễ (Delay Queue) chuẩn bị gửi tới PLC.
    /// </summary>
    public int PendingDelayedCount => _delayedDispatcher.PendingCount;

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

    /// <summary>
    /// Sự kiện kích hoạt khi số lượng tín hiệu đang chờ gửi trễ (Delay Queue) thay đổi.
    /// </summary>
    public event EventHandler<int>? OnDelayedQueueCountChanged;

    public IndustrialHandshakeStateMachine(IPlcManagerService? plcManager = null, string plcId = "PLC1")
    {
        _plcManager = plcManager;
        _plcId = plcId;
        _delayedDispatcher = new DelayedHandshakeDispatcher(
            (isPass, token) => CompleteHandshakeCoreAsync(isPass, isDelayed: true, token),
            pendingCount => OnDelayedQueueCountChanged?.Invoke(this, pendingCount));

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
        EnableHandshakeOnRunOnce = config.EnableHandshakeOnRunOnce;
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
        DelayTimeMs = Math.Max(0, config.DelayTimeMs);
        ResetQueueTagName = config.ResetQueueTagName;
        ResetQueueSignalType = config.ResetQueueSignalType;
        ResetQueueDurationMs = config.ResetQueueDurationMs;
        ResultOutputMode = config.ResultOutputMode;
        ResultWordTagName = config.ResultWordTagName;
        PassNumericValue = config.PassNumericValue;
        NgNumericValue = config.NgNumericValue;
        IdleNumericValue = config.IdleNumericValue;
        ClearResultWordOnReady = config.ClearResultWordOnReady;
    }

    /// <summary>
    /// Xóa toàn bộ tín hiệu đang chờ trong hàng đợi Delay Time (ví dụ khi Reset Queue hoặc Stop).
    /// </summary>
    public void ClearDelayedQueue()
    {
        _delayedDispatcher.Clear();
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

    private long _resetQueueEchoSuppressUntilUtcTicks;

    private void ExtendResetQueueEchoSuppressWindow(int extraMs)
    {
        long candidateTicks = DateTime.UtcNow.AddMilliseconds(Math.Max(100, extraMs)).Ticks;
        while (true)
        {
            long current = Volatile.Read(ref _resetQueueEchoSuppressUntilUtcTicks);
            if (current >= candidateTicks) return;
            if (Interlocked.CompareExchange(ref _resetQueueEchoSuppressUntilUtcTicks, candidateTicks, current) == current)
                return;
        }
    }

    public bool HandleResetQueueTagSample(string tagName, object? value, DateTime? timestamp = null)
    {
        if (DateTime.UtcNow.Ticks <= Volatile.Read(ref _resetQueueEchoSuppressUntilUtcTicks))
        {
            _resetQueueDetector.ProcessSampleSilently(value, timestamp);
            return false;
        }

        return _resetQueueDetector.ProcessSample(value, timestamp);
    }

    /// <summary>
    /// Kiểm tra điều kiện hiện tại có thể gửi xung Reset Queue từ giao diện sang PLC hay không.
    /// </summary>
    public bool CanSendResetQueuePulse()
    {
        return !_disposed
               && IsEnabled
               && _plcManager != null
               && _plcManager.IsPlcConnected(_plcId)
               && !string.IsNullOrEmpty(GetEffectiveResetQueueTag());
    }

    /// <summary>
    /// Phát xung Reset Queue từ Vision PC sang PLC qua chính tag ResetQueueTagName
    /// để đồng bộ 2 chiều PLC ⇄ PC, có triệt tiêu self-echo tránh tự kích hoạt lại WPF.
    /// </summary>
    public async Task<bool> SendResetQueuePulseAsync(CancellationToken ct = default)
    {
        if (!CanSendResetQueuePulse() || _plcManager == null)
        {
            return false;
        }

        string? resetTag = GetEffectiveResetQueueTag();
        if (string.IsNullOrEmpty(resetTag))
        {
            return false;
        }

        PlcSignalType outputWave = ResetQueueSignalType == PlcInputSignalType.FallingEdge
            ? PlcSignalType.FallingEdge
            : PlcSignalType.RisingEdge;
        int pulseMs = Math.Max(20, Math.Max(PulseDurationMs, ResetQueueDurationMs));

        // Bật cửa sổ triệt tiêu echo trong suốt độ rộng xung + đệm polling 400ms
        ExtendResetQueueEchoSuppressWindow(pulseMs + 400);

        return await EmitSignalCoreAsync(
            resetTag,
            outputWave,
            isActive: true,
            durationOverrideMs: pulseMs,
            ignoreGlobalEpoch: true,
            onBeforeAutoReset: () => ExtendResetQueueEchoSuppressWindow(400),
            ct: ct);
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

    public string? GetEffectiveResultWordTag()
    {
        if (string.IsNullOrWhiteSpace(ResultWordTagName))
        {
            return null;
        }
        return ResolveTag(ResultWordTagName, "D100_VisionResult", "D100", "D200", "MW100");
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
               GetEffectiveResetQueueTag() != null ||
               GetEffectiveResultWordTag() != null;
    }

    private (long epoch, long tagGen) CaptureNextTagGeneration(string tagName)
    {
        long epoch = Volatile.Read(ref _globalSignalEpoch);
        long tagGen = _tagSignalGenerations.AddOrUpdate(tagName, 1L, (_, current) => current + 1L);
        return (epoch, tagGen);
    }

    private (long epoch, long tagGen) GetCurrentTagGeneration(string tagName)
    {
        long epoch = Volatile.Read(ref _globalSignalEpoch);
        long tagGen = _tagSignalGenerations.TryGetValue(tagName, out long current) ? current : 0L;
        return (epoch, tagGen);
    }

    private bool IsTagGenerationValid(string tagName, long expectedEpoch, long expectedTagGen, bool ignoreGlobalEpoch = false)
    {
        return !_disposed
               && (ignoreGlobalEpoch || Volatile.Read(ref _globalSignalEpoch) == expectedEpoch)
               && _tagSignalGenerations.TryGetValue(tagName, out long current)
               && current == expectedTagGen;
    }

    /// <summary>
    /// Ghi tín hiệu gửi PLC theo dạng sóng đã cấu hình (Level, Rising Edge, Falling Edge)
    /// </summary>
    public async Task EmitSignalAsync(string? tagName, PlcSignalType signalType, bool isActive, CancellationToken ct = default)
    {
        await EmitSignalCoreAsync(tagName, signalType, isActive, durationOverrideMs: null, ignoreGlobalEpoch: false, onBeforeAutoReset: null, ct);
    }

    private async Task<bool> EmitSignalCoreAsync(
        string? tagName,
        PlcSignalType signalType,
        bool isActive,
        int? durationOverrideMs,
        bool ignoreGlobalEpoch,
        Action? onBeforeAutoReset,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tagName) || _plcManager == null || !_plcManager.IsPlcConnected(_plcId))
            return false;

        int duration = Math.Max(20, durationOverrideMs ?? PulseDurationMs);
        var (epoch, gen) = CaptureNextTagGeneration(tagName);
        bool writeOk = false;

        switch (signalType)
        {
            case PlcSignalType.Level:
                // Level: Duy trì trực tiếp mức logic (Active = true, Inactive = false)
                writeOk = await _plcManager.WriteTagValueAsync(_plcId, tagName, isActive, ct);
                break;

            case PlcSignalType.RisingEdge:
                if (isActive)
                {
                    // Phát xung cạnh lên: 0 -> 1 -> giữ duration -> 0
                    writeOk = await _plcManager.WriteTagValueAsync(_plcId, tagName, true, ct);
                    if (writeOk)
                    {
                        SchedulePulseReset(tagName, resetValue: false, duration, epoch, gen, ignoreGlobalEpoch, onBeforeAutoReset);
                    }
                }
                else
                {
                    // Trạng thái tĩnh cạnh lên là 0
                    writeOk = await _plcManager.WriteTagValueAsync(_plcId, tagName, false, ct);
                }
                break;

            case PlcSignalType.FallingEdge:
                if (isActive)
                {
                    // Phát xung cạnh xuống: 1 -> 0 -> giữ duration -> 1
                    writeOk = await _plcManager.WriteTagValueAsync(_plcId, tagName, false, ct);
                    if (writeOk)
                    {
                        SchedulePulseReset(tagName, resetValue: true, duration, epoch, gen, ignoreGlobalEpoch, onBeforeAutoReset);
                    }
                }
                else
                {
                    // Trạng thái tĩnh cạnh xuống là 1
                    writeOk = await _plcManager.WriteTagValueAsync(_plcId, tagName, true, ct);
                }
                break;
        }

        return writeOk;
    }

    private void SchedulePulseReset(
        string tagName,
        bool resetValue,
        int durationMs,
        long epoch,
        long gen,
        bool ignoreGlobalEpoch,
        Action? onBeforeAutoReset)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(durationMs);
                if (IsTagGenerationValid(tagName, epoch, gen, ignoreGlobalEpoch) && _plcManager != null && _plcManager.IsPlcConnected(_plcId))
                {
                    onBeforeAutoReset?.Invoke();
                    await _plcManager.WriteTagValueAsync(_plcId, tagName, resetValue);
                }
            }
            catch { }
        });
    }

    /// <summary>
    /// Đưa Vision PC vào trạng thái sẵn sàng nhận Trigger (READY / ARMED)
    /// </summary>
    public async Task SetReadyAsync(CancellationToken ct = default)
    {
        _isInspecting = false;
        Interlocked.Increment(ref _globalSignalEpoch);
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

        await _ioStateWriteLock.WaitAsync(ct);
        try
        {
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

            // Đưa thanh ghi số nguyên kết quả về trạng thái Idle / Sẵn sàng (0) nếu cấu hình bật
            if ((ResultOutputMode == PlcResultOutputMode.NumericInt16 || ResultOutputMode == PlcResultOutputMode.Both) && ClearResultWordOnReady)
            {
                var wordTag = GetEffectiveResultWordTag();
                if (!string.IsNullOrEmpty(wordTag))
                {
                    await _plcManager.WriteTagValueAsync(_plcId, wordTag, (short)IdleNumericValue, ct);
                }
            }
        }
        finally
        {
            _ioStateWriteLock.Release();
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

        _isInspecting = true;
        var busyTag = GetEffectiveBusyTag();
        var readyTag = GetEffectiveReadyTag();

        var (busyEpoch, busyGen) = !string.IsNullOrEmpty(busyTag) ? CaptureNextTagGeneration(busyTag) : default;
        var (readyEpoch, readyGen) = !string.IsNullOrEmpty(readyTag) ? CaptureNextTagGeneration(readyTag) : default;

        await _ioStateWriteLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(busyTag) && IsTagGenerationValid(busyTag, busyEpoch, busyGen))
            {
                await _plcManager.WriteTagValueAsync(_plcId, busyTag, true, ct);
            }
            if (!string.IsNullOrEmpty(readyTag) && IsTagGenerationValid(readyTag, readyEpoch, readyGen))
            {
                await _plcManager.WriteTagValueAsync(_plcId, readyTag, false, ct);
            }
        }
        finally
        {
            _ioStateWriteLock.Release();
        }
    }

    private async Task ReleaseInspectionBusyStateAsync(
        string? busyTag, long busyEpoch, long busyGen,
        string? readyTag, long readyEpoch, long readyGen)
    {
        if (_plcManager == null || !_plcManager.IsPlcConnected(_plcId))
            return;

        try
        {
            await _ioStateWriteLock.WaitAsync();
            try
            {
                if (!string.IsNullOrEmpty(busyTag) && IsTagGenerationValid(busyTag, busyEpoch, busyGen))
                {
                    await _plcManager.WriteTagValueAsync(_plcId, busyTag, false);
                }
                if (!string.IsNullOrEmpty(readyTag) && IsTagGenerationValid(readyTag, readyEpoch, readyGen))
                {
                    await _plcManager.WriteTagValueAsync(_plcId, readyTag, true);
                }
            }
            finally
            {
                _ioStateWriteLock.Release();
            }
        }
        catch { }
    }

    /// <summary>
    /// Chốt kết quả kiểm tra (LATCH) và thực hiện bắt tay hoàn tất với PLC:
    /// 1. Nếu DelayTimeMs > 0: xếp lịch gửi vào hàng đợi Pipelined FIFO để phát tín hiệu đúng thứ tự sau thời gian trễ.
    /// 2. Ghi bit PASS / NG và thanh ghi số nguyên theo cấu hình.
    /// 3. Ghi bit DONE theo kiểu tín hiệu cấu hình.
    /// 4. Chờ PLC phản hồi tín hiệu ACK = 1 (nếu chế độ đồng bộ) hoặc giải phóng ngay (Non-blocking).
    /// </summary>
    public async Task<bool> CompleteHandshakeAsync(bool isPass, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested || _disposed)
        {
            return false;
        }

        if (!IsEnabled || _plcManager == null || !_plcManager.IsPlcConnected(_plcId) || !HasConfiguredHandshakeTags())
        {
            _isInspecting = false;
            CurrentState = HandshakeState.ResultLatched;
            CurrentState = HandshakeState.Complete;
            return true;
        }

        int delayMs = Math.Max(0, DelayTimeMs);
        if (delayMs > 0)
        {
            var ackTag = GetEffectivePlcAckTag();
            bool isNonBlocking = NonBlockingMode || string.IsNullOrEmpty(ackTag);

            if (isNonBlocking)
            {
                if (_isInspecting)
                {
                    _isInspecting = false;
                    var busyTag = GetEffectiveBusyTag();
                    var readyTag = GetEffectiveReadyTag();
                    var (busyEpoch, busyGen) = !string.IsNullOrEmpty(busyTag) ? CaptureNextTagGeneration(busyTag) : default;
                    var (readyEpoch, readyGen) = !string.IsNullOrEmpty(readyTag) ? CaptureNextTagGeneration(readyTag) : default;
                    _ = ReleaseInspectionBusyStateAsync(busyTag, busyEpoch, busyGen, readyTag, readyEpoch, readyGen);
                }

                CurrentState = HandshakeState.Armed;
            }

            return await _delayedDispatcher.EnqueueAsync(isPass, delayMs, ct);
        }

        _isInspecting = false;
        return await CompleteHandshakeCoreAsync(isPass, isDelayed: false, ct);
    }

    private async Task<bool> CompleteHandshakeCoreAsync(bool isPass, bool isDelayed, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested || _disposed)
        {
            return false;
        }

        CurrentState = HandshakeState.ResultLatched;

        if (!IsEnabled || _plcManager == null || !_plcManager.IsPlcConnected(_plcId) || !HasConfiguredHandshakeTags())
        {
            CurrentState = HandshakeState.Complete;
            return true;
        }

        var passTag = GetEffectivePassTag();
        var ngTag = GetEffectiveNgTag();
        var wordTag = GetEffectiveResultWordTag();
        var doneTag = GetEffectiveDoneTag();
        var ackTag = GetEffectivePlcAckTag();
        var readyTag = GetEffectiveReadyTag();
        var busyTag = GetEffectiveBusyTag();

        try
        {
            // 1. Ghi kết quả theo phương thức xuất được chọn:
            // A. Ghi tín hiệu dạng Bit (Bool) nếu chọn chế độ Bit hoặc Song hành (Both)
            if (ResultOutputMode == PlcResultOutputMode.Bit || ResultOutputMode == PlcResultOutputMode.Both)
            {
                await EmitSignalAsync(passTag, PassSignalType, isPass, ct);
                await EmitSignalAsync(ngTag, NgSignalType, !isPass, ct);
            }

            // B. Ghi kết quả dạng Số (Int16) nếu chọn chế độ NumericInt16 hoặc Song hành (Both)
            if (ResultOutputMode == PlcResultOutputMode.NumericInt16 || ResultOutputMode == PlcResultOutputMode.Both)
            {
                if (!string.IsNullOrEmpty(wordTag) && _plcManager != null)
                {
                    short numericVal = isPass ? PassNumericValue : NgNumericValue;
                    await _plcManager.WriteTagValueAsync(_plcId, wordTag, numericVal, ct);
                }
            }

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
                (long epoch, long tagGen)? doneResetGen = (DoneSignalType == PlcSignalType.Level && !string.IsNullOrEmpty(doneTag))
                    ? GetCurrentTagGeneration(doneTag)
                    : null;
                (long epoch, long tagGen)? busyResetGen = (!isDelayed && !string.IsNullOrEmpty(busyTag))
                    ? CaptureNextTagGeneration(busyTag)
                    : null;

                // Tự động hạ Busy (và hạ Done nếu là Level) sau một khoảng trễ ngắn 15ms trong background
                if (doneResetGen.HasValue || busyResetGen.HasValue)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(15);
                            if (_plcManager == null || !_plcManager.IsPlcConnected(_plcId)) return;

                            if (doneResetGen.HasValue && !string.IsNullOrEmpty(doneTag) &&
                                IsTagGenerationValid(doneTag, doneResetGen.Value.epoch, doneResetGen.Value.tagGen))
                            {
                                await _plcManager.WriteTagValueAsync(_plcId, doneTag, false);
                            }

                            if (busyResetGen.HasValue && !string.IsNullOrEmpty(busyTag) &&
                                IsTagGenerationValid(busyTag, busyResetGen.Value.epoch, busyResetGen.Value.tagGen))
                            {
                                await _plcManager.WriteTagValueAsync(_plcId, busyTag, false);
                            }
                        }
                        catch { }
                    });
                }

                CurrentState = _isInspecting ? HandshakeState.Inspecting : HandshakeState.Armed;
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
            if (DoneSignalType == PlcSignalType.Level && !string.IsNullOrEmpty(doneTag) && _plcManager != null)
            {
                await _plcManager.WriteTagValueAsync(_plcId, doneTag, false, ct);
            }
            if (SimulatePlcAck && !string.IsNullOrEmpty(ackTag) && _plcManager != null)
            {
                // Khi giả lập, tự động hạ Ack = 0 sau khi Done đã hạ
                await Task.Delay(10, ct);
                await _plcManager.WriteTagValueAsync(_plcId, ackTag, false, ct);
            }
            if (!string.IsNullOrEmpty(busyTag) && _plcManager != null)
            {
                await _plcManager.WriteTagValueAsync(_plcId, busyTag, false, ct);
            }
            if (!string.IsNullOrEmpty(readyTag) && _plcManager != null)
            {
                await _plcManager.WriteTagValueAsync(_plcId, readyTag, true, ct);
            }

            // 4. Hoàn tất chu trình
            CurrentState = HandshakeState.Complete;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
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
        _isInspecting = false;
        Interlocked.Increment(ref _globalSignalEpoch);
        _delayedDispatcher.Clear();
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
        var wordTag = GetEffectiveResultWordTag();

        try
        {
            await _ioStateWriteLock.WaitAsync(ct);
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
                if ((ResultOutputMode == PlcResultOutputMode.NumericInt16 || ResultOutputMode == PlcResultOutputMode.Both) && ClearResultWordOnReady && !string.IsNullOrEmpty(wordTag))
                {
                    await _plcManager.WriteTagValueAsync(_plcId, wordTag, (short)IdleNumericValue, ct);
                }
            }
            finally
            {
                _ioStateWriteLock.Release();
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _globalSignalEpoch);
        _delayedDispatcher.Dispose();
        if (_plcManager != null)
        {
            _plcManager.OnTagChanged -= PlcManager_OnTagChanged;
        }
        try { _ioStateWriteLock.Dispose(); } catch { }
    }
}
