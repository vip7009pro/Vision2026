using System;
using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VisionInspectionApp.Application.PLC.Services;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class ToolEditorViewModel : ObservableObject
{
    #region Static Reusable Brushes for Handshake UI
    private static readonly Brush BrushMutedBg = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128));
    private static readonly Brush BrushMutedBorder = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128));
    private static readonly Brush BrushMutedText = new SolidColorBrush(Color.FromRgb(140, 140, 140));

    private static readonly Brush BrushGreenBg = new SolidColorBrush(Color.FromArgb(30, 16, 185, 129));
    private static readonly Brush BrushGreenBorder = new SolidColorBrush(Color.FromRgb(16, 185, 129));
    private static readonly Brush BrushGreenText = new SolidColorBrush(Color.FromRgb(16, 185, 129));

    private static readonly Brush BrushBlueBg = new SolidColorBrush(Color.FromArgb(30, 2, 136, 209));
    private static readonly Brush BrushBlueBorder = new SolidColorBrush(Color.FromRgb(2, 136, 209));
    private static readonly Brush BrushBlueText = new SolidColorBrush(Color.FromRgb(2, 136, 209));

    private static readonly Brush BrushAmberBg = new SolidColorBrush(Color.FromArgb(30, 245, 158, 11));
    private static readonly Brush BrushAmberBorder = new SolidColorBrush(Color.FromRgb(245, 158, 11));
    private static readonly Brush BrushAmberText = new SolidColorBrush(Color.FromRgb(245, 158, 11));

    private static readonly Brush BrushRedBg = new SolidColorBrush(Color.FromArgb(35, 239, 68, 68));
    private static readonly Brush BrushRedBorder = new SolidColorBrush(Color.FromRgb(239, 68, 68));
    private static readonly Brush BrushRedText = new SolidColorBrush(Color.FromRgb(239, 68, 68));

    private static readonly Brush BrushLedOffBg = new SolidColorBrush(Color.FromArgb(25, 100, 100, 100));
    private static readonly Brush BrushLedOffText = new SolidColorBrush(Color.FromRgb(120, 120, 120));

    private static readonly Brush BrushWhite = new SolidColorBrush(Colors.White);
    #endregion

    #region Observable Handshake Properties

    [ObservableProperty]
    private string _handshakeStatusBadgeText = "PLC: CHƯA KẾT NỐI";

    [ObservableProperty]
    private string _handshakeStatusToolTip = "Đang khởi tạo giám sát bắt tay PLC...";

    [ObservableProperty]
    private string _handshakeStatusBarSummary = "🤝 PLC: Chưa kết nối";

    [ObservableProperty]
    private Brush _handshakeStatusBackgroundBrush = BrushMutedBg;

    [ObservableProperty]
    private Brush _handshakeStatusBorderBrush = BrushMutedBorder;

    [ObservableProperty]
    private Brush _handshakeStatusForegroundBrush = BrushMutedText;

    // Mini Indicators cho 4 Tag chính: RDY, BSY, DON, ACK
    [ObservableProperty]
    private Brush _handshakeRdyBrush = BrushLedOffBg;
    [ObservableProperty]
    private Brush _handshakeRdyForeBrush = BrushLedOffText;

    [ObservableProperty]
    private Brush _handshakeBsyBrush = BrushLedOffBg;
    [ObservableProperty]
    private Brush _handshakeBsyForeBrush = BrushLedOffText;

    [ObservableProperty]
    private Brush _handshakeDonBrush = BrushLedOffBg;
    [ObservableProperty]
    private Brush _handshakeDonForeBrush = BrushLedOffText;

    [ObservableProperty]
    private Brush _handshakeAckBrush = BrushLedOffBg;
    [ObservableProperty]
    private Brush _handshakeAckForeBrush = BrushLedOffText;

    #endregion

    /// <summary>
    /// Khởi tạo theo dõi trạng thái Handshake và đăng ký các sự kiện từ PLC Service & State Machine
    /// </summary>
    private void InitHandshakeUiMonitoring()
    {
        if (_handshakeStateMachine != null)
        {
            _handshakeStateMachine.OnStateChanged += (_, _) => RequestHandshakeUiUpdate();
            _handshakeStateMachine.OnHandshakeTimeout += (_, _) => RequestHandshakeUiUpdate();
        }

        if (_plcManagerService != null)
        {
            _plcManagerService.OnConnected += (_, _) => RequestHandshakeUiUpdate();
            _plcManagerService.OnDisconnected += (_, _) => RequestHandshakeUiUpdate();
            _plcManagerService.OnIndustrialConfigChanged += (_, _) => RequestHandshakeUiUpdate();
            _plcManagerService.OnTagChanged += (_, _) => RequestHandshakeUiUpdate();
        }

        RequestHandshakeUiUpdate();
    }

    private int _handshakeUiUpdateScheduled = 0;

    /// <summary>
    /// Yêu cầu cập nhật giao diện Handshake (Throttle chống nghẽn Dispatcher)
    /// </summary>
    public void RequestHandshakeUiUpdate()
    {
        if (System.Windows.Application.Current?.Dispatcher == null)
        {
            UpdateHandshakeUiCore();
            return;
        }

        if (System.Threading.Interlocked.CompareExchange(ref _handshakeUiUpdateScheduled, 1, 0) == 0)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    System.Threading.Interlocked.Exchange(ref _handshakeUiUpdateScheduled, 0);
                    UpdateHandshakeUiCore();
                }));
        }
    }

    /// <summary>
    /// Tính toán và cập nhật toàn bộ trạng thái Handshake lên UI
    /// </summary>
    private void UpdateHandshakeUiCore()
    {
        string plcId = _handshakeStateMachine?.PlcId ?? "PLC1";
        bool isHandshakeEnabled = _handshakeStateMachine?.IsEnabled ?? false;
        bool isPlcConnected = _plcManagerService?.IsPlcConnected(plcId) ?? false;
        var state = _handshakeStateMachine?.CurrentState ?? HandshakeState.Idle;

        // 1. Đọc giá trị 4 bit tín hiệu từ PLC Cache
        string rdyTag = _handshakeStateMachine?.GetEffectiveReadyTag() ?? "M101";
        string bsyTag = _handshakeStateMachine?.GetEffectiveBusyTag() ?? "M102";
        string donTag = _handshakeStateMachine?.GetEffectiveDoneTag() ?? "M103";
        string ackTag = _handshakeStateMachine?.GetEffectivePlcAckTag() ?? "M11";
        string passTag = _handshakeStateMachine?.GetEffectivePassTag() ?? "M104";
        string ngTag = _handshakeStateMachine?.GetEffectiveNgTag() ?? "M105";

        bool bitRdy = ReadTagBool(plcId, rdyTag);
        bool bitBsy = ReadTagBool(plcId, bsyTag);
        bool bitDon = ReadTagBool(plcId, donTag);
        bool bitAck = ReadTagBool(plcId, ackTag);
        bool bitPass = ReadTagBool(plcId, passTag);
        bool bitNg = ReadTagBool(plcId, ngTag);

        // 2. Cập nhật Mini LED Brushes cho 4 bit
        HandshakeRdyBrush = bitRdy ? BrushGreenBg : BrushLedOffBg;
        HandshakeRdyForeBrush = bitRdy ? BrushGreenText : BrushLedOffText;

        HandshakeBsyBrush = bitBsy ? BrushBlueBg : BrushLedOffBg;
        HandshakeBsyForeBrush = bitBsy ? BrushBlueText : BrushLedOffText;

        HandshakeDonBrush = bitDon ? BrushAmberBg : BrushLedOffBg;
        HandshakeDonForeBrush = bitDon ? BrushAmberText : BrushLedOffText;

        HandshakeAckBrush = bitAck ? BrushGreenBg : BrushLedOffBg;
        HandshakeAckForeBrush = bitAck ? BrushGreenText : BrushLedOffText;

        // 3. Phân loại trạng thái tổng thể Badge & Màu sắc
        if (!isHandshakeEnabled)
        {
            HandshakeStatusBadgeText = $"{plcId}: TẮT BẮT TAY";
            HandshakeStatusBackgroundBrush = BrushMutedBg;
            HandshakeStatusBorderBrush = BrushMutedBorder;
            HandshakeStatusForegroundBrush = BrushMutedText;
            HandshakeStatusBarSummary = $"🤝 PLC: {plcId} (Tắt Handshake) | RDY:{(bitRdy ? 1 : 0)} BSY:{(bitBsy ? 1 : 0)} DON:{(bitDon ? 1 : 0)} ACK:{(bitAck ? 1 : 0)}";
            HandshakeStatusToolTip = $"🤝 Chu Trình Bắt Tay PLC ({plcId})\n" +
                                     $"• Trạng thái: ĐÃ TẮT trong cài đặt Handshake\n" +
                                     $"• Bấm chuột vào đây để mở Cấu Hình PLC & Bắt Tay.";
            return;
        }

        if (!isPlcConnected)
        {
            HandshakeStatusBadgeText = $"{plcId}: OFFLINE";
            HandshakeStatusBackgroundBrush = BrushRedBg;
            HandshakeStatusBorderBrush = BrushRedBorder;
            HandshakeStatusForegroundBrush = BrushRedText;
            HandshakeStatusBarSummary = $"🤝 PLC: {plcId} (Mất kết nối Ethernet) | RDY:0 BSY:0 DON:0 ACK:0";
            HandshakeStatusToolTip = $"🤝 Chu Trình Bắt Tay PLC ({plcId})\n" +
                                     $"• Trạng thái: CHƯA KẾT NỐI (Offline)\n" +
                                     $"• Giao thức: Mitsubishi MC Protocol / MX Component\n" +
                                     $"• Kiểm tra cáp mạng Ethernet và IP {plcId}\n" +
                                     $"• Bấm chuột vào đây để mở Cấu Hình & Chẩn Đoán (Ping & Probe).";
            return;
        }

        // PLC đang kết nối và Handshake đang bật: Dựa vào State
        string stateDesc;
        switch (state)
        {
            case HandshakeState.Armed:
            case HandshakeState.Ready:
                HandshakeStatusBadgeText = $"{plcId}: SẴN SÀNG";
                HandshakeStatusBackgroundBrush = BrushGreenBg;
                HandshakeStatusBorderBrush = BrushGreenBorder;
                HandshakeStatusForegroundBrush = BrushGreenText;
                stateDesc = "Sẵn Sàng (Armed) — Chờ Phôi";
                break;

            case HandshakeState.Inspecting:
            case HandshakeState.Triggered:
                HandshakeStatusBadgeText = $"{plcId}: ĐANG ĐO (BUSY)";
                HandshakeStatusBackgroundBrush = BrushBlueBg;
                HandshakeStatusBorderBrush = BrushBlueBorder;
                HandshakeStatusForegroundBrush = BrushBlueText;
                stateDesc = "Đang Chụp & Đo (Inspecting)";
                break;

            case HandshakeState.ResultLatched:
                HandshakeStatusBadgeText = $"{plcId}: CHỜ ACK (DONE)";
                HandshakeStatusBackgroundBrush = BrushAmberBg;
                HandshakeStatusBorderBrush = BrushAmberBorder;
                HandshakeStatusForegroundBrush = BrushAmberText;
                stateDesc = "Đã Chốt Kết Quả — Chờ PLC Ack";
                break;

            case HandshakeState.Acknowledged:
            case HandshakeState.Complete:
                HandshakeStatusBadgeText = $"{plcId}: ĐÃ ACK";
                HandshakeStatusBackgroundBrush = BrushGreenBg;
                HandshakeStatusBorderBrush = BrushGreenBorder;
                HandshakeStatusForegroundBrush = BrushGreenText;
                stateDesc = "PLC Đã Xác Nhận (Acked)";
                break;

            case HandshakeState.TimeoutFault:
                HandshakeStatusBadgeText = $"{plcId}: LỖI TIMEOUT";
                HandshakeStatusBackgroundBrush = BrushRedBg;
                HandshakeStatusBorderBrush = BrushRedBorder;
                HandshakeStatusForegroundBrush = BrushRedText;
                stateDesc = "Lỗi Timeout (PLC không phản hồi Ack)";
                break;

            case HandshakeState.Idle:
            default:
                HandshakeStatusBadgeText = $"{plcId}: CHỜ KHỞI CHẠY";
                HandshakeStatusBackgroundBrush = BrushMutedBg;
                HandshakeStatusBorderBrush = BrushMutedBorder;
                HandshakeStatusForegroundBrush = BrushMutedText;
                stateDesc = "Chờ Khởi Chạy (Idle)";
                break;
        }

        HandshakeStatusBarSummary = $"🤝 PLC: {plcId} ({stateDesc}) | RDY:{(bitRdy ? 1 : 0)} BSY:{(bitBsy ? 1 : 0)} DON:{(bitDon ? 1 : 0)} ACK:{(bitAck ? 1 : 0)}";

        HandshakeStatusToolTip =
            $"🤝 CHU TRÌNH BẮT TAY CÔNG NGHIỆP DETERMINISTIC 24/7\n" +
            $"──────────────────────────────────────────────────\n" +
            $"• PLC Mục Tiêu : {plcId} (Đã Kết Nối - Online)\n" +
            $"• Trạng Thái   : {stateDesc}\n" +
            $"• Timeout Chờ  : {_handshakeStateMachine?.HandshakeTimeoutMs ?? 500} ms\n" +
            $"──────────────────────────────────────────────────\n" +
            $"Tín Hiệu I/O Hiện Tại (MC Protocol / Internal Bit):\n" +
            $"• [Y] Vision Ready ({rdyTag}) : {(bitRdy ? "ON (1) — Sẵn sàng chụp" : "OFF (0)")}\n" +
            $"• [Y] Vision Busy  ({bsyTag}) : {(bitBsy ? "ON (1) — Đang đo" : "OFF (0)")}\n" +
            $"• [Y] Vision Done  ({donTag}) : {(bitDon ? "ON (1) — Đo xong" : "OFF (0)")}\n" +
            $"• [X] PLC Ack      ({ackTag}) : {(bitAck ? "ON (1) — PLC đã chốt" : "OFF (0)")}\n" +
            $"• [Y] Vision Pass  ({passTag}) : {(bitPass ? "ON (1) — ĐẠT" : "OFF (0)")}\n" +
            $"• [Y] Vision NG    ({ngTag}) : {(bitNg ? "ON (1) — LỖI" : "OFF (0)")}\n" +
            $"──────────────────────────────────────────────────\n" +
            $"👉 Bấm chuột vào đây để mở Cửa Sổ Cấu Hình & Chẩn Đoán PLC.";
    }

    private bool ReadTagBool(string plcId, string tagNameOrAddress)
    {
        if (_plcManagerService == null || string.IsNullOrWhiteSpace(tagNameOrAddress))
            return false;

        var tagVal = _plcManagerService.GetTagValue(plcId, tagNameOrAddress);
        if (tagVal?.CurrentValue == null)
            return false;

        var val = tagVal.CurrentValue;
        if (val is bool b) return b;
        if (val is int i) return i != 0;
        if (val is short s) return s != 0;
        if (val is byte by) return by != 0;
        if (bool.TryParse(val.ToString(), out var parsedB)) return parsedB;
        if (int.TryParse(val.ToString(), out var parsedI)) return parsedI != 0;

        return false;
    }
}
