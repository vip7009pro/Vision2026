using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VisionInspectionApp.Models;

/// <summary>
/// Chế độ xuất kết quả kiểm tra (Pass / NG) sang PLC
/// </summary>
public enum PlcResultOutputMode
{
    /// <summary>
    /// Chế độ Bit (Bool): Gửi kết quả qua 2 chân bit Pass (OK) và NG (Lỗi) riêng biệt.
    /// </summary>
    Bit = 0,

    /// <summary>
    /// Chế độ Dạng Số (Int16 Word): Gửi mã số kết quả (OK = 2, NG = 3,...) vào 1 thanh ghi số nguyên Word.
    /// </summary>
    NumericInt16 = 1,

    /// <summary>
    /// Chế độ Song Hành: Gửi đồng thời cả bit Bool (Pass/NG) và mã số Int16 vào thanh ghi Word.
    /// </summary>
    Both = 2
}

/// <summary>
/// Kiểu dạng sóng / tín hiệu gửi sang PLC (Signal Waveform)
/// </summary>
public enum PlcSignalType
{
    /// <summary>
    /// Giữ mức logic (Level): Duy trì ON khi kích hoạt và OFF khi xóa.
    /// </summary>
    Level = 0,

    /// <summary>
    /// Xung cạnh lên (Rising Edge): Mức tĩnh là 0, khi kích hoạt phát xung 0 -> 1 -> 0 theo thời gian PulseDurationMs.
    /// </summary>
    RisingEdge = 1,

    /// <summary>
    /// Xung cạnh xuống (Falling Edge / Active-Low): Mức tĩnh là 1, khi kích hoạt phát xung 1 -> 0 -> 1 theo thời gian PulseDurationMs.
    /// </summary>
    FallingEdge = 2
}

/// <summary>
/// Kiểu tín hiệu đầu vào nhận từ PLC (Input Trigger / Signal Detection)
/// </summary>
public enum PlcInputSignalType
{
    /// <summary>
    /// Bắt cạnh lên (Rising Edge: 0 ➔ 1)
    /// </summary>
    RisingEdge = 0,

    /// <summary>
    /// Bắt cạnh xuống (Falling Edge: 1 ➔ 0)
    /// </summary>
    FallingEdge = 1,

    /// <summary>
    /// Nhấn giữ liên tục (Long Press: giữ ON liên tục trong ít nhất DurationMs)
    /// </summary>
    LongPress = 2
}

/// <summary>
/// Cấu hình Máy trạng thái Bắt tay Công nghiệp 2 chiều (Deterministic Industrial Handshake 24/7)
/// </summary>
public sealed class IndustrialHandshakeConfig : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string _plcId = "PLC1";
    public string PlcId
    {
        get => _plcId;
        set { if (_plcId != value) { _plcId = value; OnPropertyChanged(); } }
    }

    private string _readyTagName = "Y1_VisionReady";
    public string ReadyTagName
    {
        get => _readyTagName;
        set { if (_readyTagName != value) { _readyTagName = value; OnPropertyChanged(); } }
    }

    private string _busyTagName = "Y2_VisionBusy";
    public string BusyTagName
    {
        get => _busyTagName;
        set { if (_busyTagName != value) { _busyTagName = value; OnPropertyChanged(); } }
    }

    private string _doneTagName = "Y3_VisionDone";
    public string DoneTagName
    {
        get => _doneTagName;
        set { if (_doneTagName != value) { _doneTagName = value; OnPropertyChanged(); } }
    }

    private string _passTagName = "Y4_VisionPass";
    public string PassTagName
    {
        get => _passTagName;
        set { if (_passTagName != value) { _passTagName = value; OnPropertyChanged(); } }
    }

    private string _ngTagName = "Y5_VisionNG";
    public string NgTagName
    {
        get => _ngTagName;
        set { if (_ngTagName != value) { _ngTagName = value; OnPropertyChanged(); } }
    }

    private string _plcAckTagName = "X1_PlcAck";
    public string PlcAckTagName
    {
        get => _plcAckTagName;
        set { if (_plcAckTagName != value) { _plcAckTagName = value; OnPropertyChanged(); } }
    }

    private int _handshakeTimeoutMs = 500;
    public int HandshakeTimeoutMs
    {
        get => _handshakeTimeoutMs;
        set { if (_handshakeTimeoutMs != value) { _handshakeTimeoutMs = value; OnPropertyChanged(); } }
    }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChanged(); } }
    }

    private bool _simulatePlcAck = false;
    public bool SimulatePlcAck
    {
        get => _simulatePlcAck;
        set { if (_simulatePlcAck != value) { _simulatePlcAck = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Phương thức xuất kết quả kiểm tra sang PLC: Bit (Bool), Dạng Số (Int16) hoặc Song Hành Cả Hai
    /// </summary>
    private PlcResultOutputMode _resultOutputMode = PlcResultOutputMode.Bit;
    public PlcResultOutputMode ResultOutputMode
    {
        get => _resultOutputMode;
        set
        {
            if (_resultOutputMode != value)
            {
                _resultOutputMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBitModeActive));
                OnPropertyChanged(nameof(IsNumericModeActive));
            }
        }
    }

    /// <summary>
    /// Địa chỉ thanh ghi số nguyên Word trên PLC lưu mã kết quả (Mặc định: D100 cho FX5U hoặc MW100 cho I/O chuẩn)
    /// </summary>
    private string _resultWordTagName = "D100";
    public string ResultWordTagName
    {
        get => _resultWordTagName;
        set { if (_resultWordTagName != value) { _resultWordTagName = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Giá trị số nguyên (Int16) gửi sang PLC khi sản phẩm ĐẠT / OK (Mặc định: 2)
    /// </summary>
    private short _passNumericValue = 2;
    public short PassNumericValue
    {
        get => _passNumericValue;
        set { if (_passNumericValue != value) { _passNumericValue = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Giá trị số nguyên (Int16) gửi sang PLC khi sản phẩm LỖI / NG (Mặc định: 3)
    /// </summary>
    private short _ngNumericValue = 3;
    public short NgNumericValue
    {
        get => _ngNumericValue;
        set { if (_ngNumericValue != value) { _ngNumericValue = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Giá trị số nguyên (Int16) gửi sang PLC khi ở trạng thái Sẵn sàng / Idle / Reset (Mặc định: 0)
    /// </summary>
    private short _idleNumericValue = 0;
    public short IdleNumericValue
    {
        get => _idleNumericValue;
        set { if (_idleNumericValue != value) { _idleNumericValue = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Tự động reset giá trị thanh ghi kết quả về IdleNumericValue (0) khi Vision sẵn sàng đón phôi mới
    /// </summary>
    private bool _clearResultWordOnReady = true;
    public bool ClearResultWordOnReady
    {
        get => _clearResultWordOnReady;
        set { if (_clearResultWordOnReady != value) { _clearResultWordOnReady = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Cho biết chế độ xuất kết quả hiện tại có phát tín hiệu Bit (Bool) hay không
    /// </summary>
    public bool IsBitModeActive => ResultOutputMode == PlcResultOutputMode.Bit || ResultOutputMode == PlcResultOutputMode.Both;

    /// <summary>
    /// Cho biết chế độ xuất kết quả hiện tại có ghi thanh ghi số nguyên (Int16) hay không
    /// </summary>
    public bool IsNumericModeActive => ResultOutputMode == PlcResultOutputMode.NumericInt16 || ResultOutputMode == PlcResultOutputMode.Both;

    /// <summary>
    /// Kiểu tín hiệu gửi cho kết quả Pass (Mặc định: Level)
    /// </summary>
    private PlcSignalType _passSignalType = PlcSignalType.Level;
    public PlcSignalType PassSignalType
    {
        get => _passSignalType;
        set { if (_passSignalType != value) { _passSignalType = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Kiểu tín hiệu gửi cho kết quả NG (Mặc định: Level)
    /// </summary>
    private PlcSignalType _ngSignalType = PlcSignalType.Level;
    public PlcSignalType NgSignalType
    {
        get => _ngSignalType;
        set { if (_ngSignalType != value) { _ngSignalType = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Kiểu tín hiệu gửi cho tín hiệu Done (Mặc định: Level)
    /// </summary>
    private PlcSignalType _doneSignalType = PlcSignalType.Level;
    public PlcSignalType DoneSignalType
    {
        get => _doneSignalType;
        set { if (_doneSignalType != value) { _doneSignalType = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Độ rộng xung phát cho kiểu Edge (Rising Edge / Falling Edge) tính bằng ms (Mặc định: 100ms)
    /// </summary>
    private int _pulseDurationMs = 100;
    public int PulseDurationMs
    {
        get => _pulseDurationMs;
        set { if (_pulseDurationMs != value) { _pulseDurationMs = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Chế độ bắt tay bất đồng bộ không chờ Ack (Non-blocking Pipelined Queue):
    /// Băng tải chạy liên tục tốc độ cao, Vision ghi kết quả vào hàng đợi rồi giải phóng ngay, không dừng luồng chờ Ack.
    /// </summary>
    private bool _nonBlockingMode = true;
    public bool NonBlockingMode
    {
        get => _nonBlockingMode;
        set { if (_nonBlockingMode != value) { _nonBlockingMode = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Điểm ra chỉ định trên hàng đợi 20 sản phẩm (1..20): Nấc mà tại đó nếu là hàng NG thì băng tải sẽ dừng. Mặc định: 10
    /// </summary>
    private int _targetStopStationIndex = 10;
    public int TargetStopStationIndex
    {
        get => _targetStopStationIndex;
        set { if (_targetStopStationIndex != value) { _targetStopStationIndex = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Địa chỉ bắt đầu mảng hàng đợi trên PLC (Mặc định: M200 cho mảng M200..M219)
    /// </summary>
    private string _queueRegisterStart = "M200";
    public string QueueRegisterStart
    {
        get => _queueRegisterStart;
        set { if (_queueRegisterStart != value) { _queueRegisterStart = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Tag nhận tín hiệu yêu cầu Reset Queue từ PLC (Mặc định: rỗng hoặc M106/X2_ResetQueue).
    /// Khi nhận tín hiệu, Vision sẽ xóa sạch hàng đợi frame và đưa các bộ đếm về 0.
    /// </summary>
    private string _resetQueueTagName = "";
    public string ResetQueueTagName
    {
        get => _resetQueueTagName;
        set { if (_resetQueueTagName != value) { _resetQueueTagName = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Kiểu tín hiệu nhận cho lệnh Reset Queue: RisingEdge (0->1), FallingEdge (1->0), hoặc LongPress (Nhấn giữ liên tục)
    /// </summary>
    private PlcInputSignalType _resetQueueSignalType = PlcInputSignalType.RisingEdge;
    public PlcInputSignalType ResetQueueSignalType
    {
        get => _resetQueueSignalType;
        set { if (_resetQueueSignalType != value) { _resetQueueSignalType = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Thời gian giữ hoặc độ rộng xung lọc / debounce (ms) cho lệnh Reset Queue. Mặc định: 500ms.
    /// Với Long Press: phải giữ liên tục ON trong ít nhất ResetQueueDurationMs.
    /// Với Edge: dùng làm thời gian lọc chống rung (lockout debounce).
    /// </summary>
    private int _resetQueueDurationMs = 500;
    public int ResetQueueDurationMs
    {
        get => _resetQueueDurationMs;
        set { if (_resetQueueDurationMs != value) { _resetQueueDurationMs = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Kiểm tra xem cấu hình Handshake có chứa ít nhất một TagName hợp lệ hay không.
    /// </summary>
    public bool HasConfiguredTags()
    {
        bool hasBasic = !string.IsNullOrWhiteSpace(ReadyTagName) ||
                        !string.IsNullOrWhiteSpace(BusyTagName) ||
                        !string.IsNullOrWhiteSpace(DoneTagName) ||
                        !string.IsNullOrWhiteSpace(PlcAckTagName) ||
                        !string.IsNullOrWhiteSpace(ResetQueueTagName);

        bool hasBitResult = !string.IsNullOrWhiteSpace(PassTagName) ||
                            !string.IsNullOrWhiteSpace(NgTagName);

        bool hasWordResult = ResultOutputMode != PlcResultOutputMode.Bit && !string.IsNullOrWhiteSpace(ResultWordTagName);

        return hasBasic || hasBitResult || hasWordResult;
    }

    /// <summary>
    /// Khôi phục cấu hình bắt tay chuẩn Mitsubishi FX5U (M101..M106, xung Rising Edge 20ms, hàng đợi M200, kết quả số D100).
    /// </summary>
    public void ApplyFx5uDefaults(string? targetPlcId = null)
    {
        if (!string.IsNullOrWhiteSpace(targetPlcId)) PlcId = targetPlcId;
        ReadyTagName = "M101";
        BusyTagName = "M102";
        DoneTagName = "M103";
        PassTagName = "M104";
        NgTagName = "M105";
        PlcAckTagName = "";
        ResetQueueTagName = "M106";
        ResetQueueSignalType = PlcInputSignalType.RisingEdge;
        ResetQueueDurationMs = 50;
        HandshakeTimeoutMs = 300;
        IsEnabled = true;
        SimulatePlcAck = false;
        PassSignalType = PlcSignalType.RisingEdge;
        NgSignalType = PlcSignalType.RisingEdge;
        DoneSignalType = PlcSignalType.Level;
        PulseDurationMs = 20;
        NonBlockingMode = true;
        TargetStopStationIndex = 10;
        QueueRegisterStart = "M200";
        ResultOutputMode = PlcResultOutputMode.Bit;
        ResultWordTagName = "D100";
        PassNumericValue = 2;
        NgNumericValue = 3;
        IdleNumericValue = 0;
        ClearResultWordOnReady = true;
    }

    /// <summary>
    /// Khôi phục cấu hình bắt tay chuẩn I/O Công Nghiệp (Y1..Y5, X1 Ack, X2 Reset Queue, Level, kết quả số MW100).
    /// </summary>
    public void ApplyStandardIoDefaults(string? targetPlcId = null)
    {
        if (!string.IsNullOrWhiteSpace(targetPlcId)) PlcId = targetPlcId;
        ReadyTagName = "Y1_VisionReady";
        BusyTagName = "Y2_VisionBusy";
        DoneTagName = "Y3_VisionDone";
        PassTagName = "Y4_VisionPass";
        NgTagName = "Y5_VisionNG";
        PlcAckTagName = "X1_PlcAck";
        ResetQueueTagName = "X2_ResetQueue";
        ResetQueueSignalType = PlcInputSignalType.RisingEdge;
        ResetQueueDurationMs = 100;
        HandshakeTimeoutMs = 500;
        IsEnabled = true;
        SimulatePlcAck = false;
        PassSignalType = PlcSignalType.Level;
        NgSignalType = PlcSignalType.Level;
        DoneSignalType = PlcSignalType.Level;
        PulseDurationMs = 100;
        NonBlockingMode = false;
        TargetStopStationIndex = 10;
        QueueRegisterStart = "M200";
        ResultOutputMode = PlcResultOutputMode.Bit;
        ResultWordTagName = "MW100";
        PassNumericValue = 2;
        NgNumericValue = 3;
        IdleNumericValue = 0;
        ClearResultWordOnReady = true;
    }

    /// <summary>
    /// Sao chép giá trị từ cấu hình khác.
    /// </summary>
    public void CopyFrom(IndustrialHandshakeConfig other)
    {
        if (other == null) return;
        if (!string.IsNullOrWhiteSpace(other.PlcId)) PlcId = other.PlcId;
        ReadyTagName = other.ReadyTagName;
        BusyTagName = other.BusyTagName;
        DoneTagName = other.DoneTagName;
        PassTagName = other.PassTagName;
        NgTagName = other.NgTagName;
        PlcAckTagName = other.PlcAckTagName;
        ResetQueueTagName = other.ResetQueueTagName;
        ResetQueueSignalType = other.ResetQueueSignalType;
        ResetQueueDurationMs = other.ResetQueueDurationMs;
        HandshakeTimeoutMs = other.HandshakeTimeoutMs;
        IsEnabled = other.IsEnabled;
        SimulatePlcAck = other.SimulatePlcAck;
        PassSignalType = other.PassSignalType;
        NgSignalType = other.NgSignalType;
        DoneSignalType = other.DoneSignalType;
        PulseDurationMs = other.PulseDurationMs;
        NonBlockingMode = other.NonBlockingMode;
        TargetStopStationIndex = other.TargetStopStationIndex;
        QueueRegisterStart = other.QueueRegisterStart;
        ResultOutputMode = other.ResultOutputMode;
        ResultWordTagName = other.ResultWordTagName;
        PassNumericValue = other.PassNumericValue;
        NgNumericValue = other.NgNumericValue;
        IdleNumericValue = other.IdleNumericValue;
        ClearResultWordOnReady = other.ClearResultWordOnReady;
    }
}

/// <summary>
/// Cấu hình Watchdog Heartbeat 2 chiều và Liên động An toàn (Safety Interlock)
/// </summary>
public sealed class PlcHeartbeatConfig
{
    public string PlcId { get; set; } = "PLC1";
    public string VisionHeartbeatTagName { get; set; } = "Y0_VisionHeartbeat";
    public string PlcHeartbeatTagName { get; set; } = "X0_PlcHeartbeat";
    public int IntervalMs { get; set; } = 100;
    public int TimeoutMs { get; set; } = 300;
    public bool EnableEmergencyInterlock { get; set; } = true;
    public string EmergencyStopTagName { get; set; } = "Y10_VisionFault";
    public bool IsEnabled { get; set; } = false;
}

/// <summary>
/// Cấu hình Đồng bộ Chuyển động Cuộn, Xung Encoder và Bù trừ Phơi sáng
/// </summary>
public sealed class PlcMotionConfig
{
    public string PlcId { get; set; } = "PLC1";
    public string EncoderTagName { get; set; } = "D1000";
    public string SpeedTagName { get; set; } = "D1002";
    public double PulsesPerMm { get; set; } = 100.0;
    public double MmPerPixel { get; set; } = 0.05;
    public double NominalSpeedMpm { get; set; } = 30.0;
    public double BaseExposureTimeUs { get; set; } = 500.0;
    public bool IsEnabled { get; set; } = false;
}

/// <summary>
/// Cấu hình Cơ cấu Shift Register theo dõi vị trí lỗi và Kích hoạt Trạm Loại Bỏ (Reject Station)
/// </summary>
public sealed class PlcShiftRegisterConfig
{
    public string PlcId { get; set; } = "PLC1";
    public string RejectTagName { get; set; } = "Y0_RejectPiston";
    public double RejectStationDistanceMm { get; set; } = 1500.0;
    public double RejectToleranceMm { get; set; } = 15.0;
    public int PulseDurationMs { get; set; } = 100;
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Tổng hợp toàn bộ Cấu hình Công nghiệp PLC & Đồng Bộ Chuyển Động
/// </summary>
public sealed class PlcIndustrialConfig
{
    public IndustrialHandshakeConfig Handshake { get; set; } = new();
    public PlcHeartbeatConfig Heartbeat { get; set; } = new();
    public PlcMotionConfig Motion { get; set; } = new();
    public PlcShiftRegisterConfig ShiftRegister { get; set; } = new();
}
