using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.LightingController;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

/// <summary>
/// ViewModel cho từng kênh đèn trên giao diện Máy Chủ (Server).
/// </summary>
public sealed partial class ServerChannelItemViewModel : ObservableObject
{
    private readonly LightingServerViewModel _parent;

    public int ChannelIndex { get; }
    public int ChannelNumber => ChannelIndex + 1;
    public string ChannelLabel => $"CH{ChannelNumber}";

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _brightness = 100;

    [ObservableProperty]
    private int _lightingTimeMs = 100;

    internal bool SuppressCommands { get; set; }

    public ServerChannelItemViewModel(int channelIndex, LightingServerViewModel parent)
    {
        ChannelIndex = channelIndex;
        _parent = parent;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (SuppressCommands) return;
        _ = _parent.Server.SetChannelPowerDirectAsync(ChannelIndex, value);
    }

    partial void OnBrightnessChanged(int value)
    {
        if (SuppressCommands) return;
        int clamped = Math.Clamp(value, 0, 255);
        if (clamped != value) { Brightness = clamped; return; }
        _parent.DebounceBrightness(ChannelIndex, clamped);
    }

    partial void OnLightingTimeMsChanged(int value)
    {
        if (SuppressCommands) return;
        int clamped = Math.Clamp(value, 1, 999);
        if (clamped != value) { LightingTimeMs = clamped; return; }
        _ = _parent.Server.SetLightingTimeDirectAsync(ChannelIndex, clamped);
    }

    public void Sync(LightingChannelState state)
    {
        SuppressCommands = true;
        try
        {
            IsEnabled = state.IsEnabled;
            Brightness = state.Brightness;
            LightingTimeMs = state.LightingTimeMs;
        }
        finally
        {
            SuppressCommands = false;
        }
    }
}

/// <summary>
/// ViewModel quản lý Lighting Control Server.
/// </summary>
public sealed partial class LightingServerViewModel : ObservableObject
{
    private readonly GlobalAppSettingsService _settingsService;
    private readonly LightingControlServer _server;
    private readonly DispatcherTimer _debounceTimer;
    private int _pendingBrightnessChannel = -1;
    private int _pendingBrightnessValue;

    public LightingControlServer Server => _server;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerStatusText))]
    [NotifyPropertyChangedFor(nameof(IsServerStopped))]
    private bool _isServerRunning;

    public bool IsServerStopped => !IsServerRunning;

    public string ServerStatusText => IsServerRunning
        ? $"🟢 Đang lắng nghe trên cổng {ServerPort}"
        : "🔴 Máy chủ đã dừng";

    [ObservableProperty]
    private int _serverPort = 5050;

    public ObservableCollection<string> LocalIpAddresses { get; } = new();

    [ObservableProperty]
    private string _selectedIpAddress = "127.0.0.1";

    // Hardware (COM) settings
    public ObservableCollection<string> AvailableComPorts { get; } = new();

    [ObservableProperty]
    private string _selectedComPort = "COM3";

    public int[] AvailableBaudRates { get; } = { 9600, 19200, 38400, 57600, 115200 };

    [ObservableProperty]
    private int _selectedBaudRate = 19200;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HardwareStatusText))]
    [NotifyPropertyChangedFor(nameof(IsHardwareDisconnected))]
    private bool _isHardwareConnected;

    public bool IsHardwareDisconnected => !IsHardwareConnected;

    public string HardwareStatusText => IsHardwareConnected
        ? $"🟢 Đã kết nối ({SelectedComPort} @ {SelectedBaudRate}bps)"
        : "🔴 Đèn chưa kết nối (Chế độ mô phỏng)";

    [ObservableProperty]
    private int _selectedChannelCount = 4;

    public int[] AvailableChannelCounts { get; } = { 4, 8 };

    public ObservableCollection<ServerChannelItemViewModel> Channels { get; } = new();

    public ObservableCollection<LightingConnectedClientInfo> ConnectedClients { get; } = new();

    [ObservableProperty]
    private string _clientCountText = "0 Client kết nối";

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng khởi động máy chủ Lighting Control Server.";

    public LightingServerViewModel(
        LightingControllerService hardwareService,
        GlobalAppSettingsService settingsService,
        LightingControlServer? server = null)
    {
        _settingsService = settingsService;
        _server = server ?? new LightingControlServer(hardwareService);

        var config = settingsService.Settings.LightingServer;
        _serverPort = _server.IsRunning ? _server.ListeningPort : (config.Port > 0 ? config.Port : 5050);
        _selectedComPort = !string.IsNullOrWhiteSpace(_server.HardwareService.ActivePortName)
            ? _server.HardwareService.ActivePortName
            : (!string.IsNullOrWhiteSpace(config.ComPort) ? config.ComPort : settingsService.Settings.Lighting.ComPort);
        _selectedBaudRate = _server.HardwareService.ActiveBaudRate > 0
            ? _server.HardwareService.ActiveBaudRate
            : (config.BaudRate > 0 ? config.BaudRate : settingsService.Settings.Lighting.BaudRate);
        _selectedChannelCount = config.ChannelCount == 8 ? 8 : 4;
        _isHardwareConnected = _server.HardwareService.IsConnected;
        _isServerRunning = _server.IsRunning;

        if (_isServerRunning)
        {
            _statusMessage = $"🟢 Server đang lắng nghe trên cổng {_serverPort}.";
        }
        else if (_isHardwareConnected)
        {
            _statusMessage = $"🟢 Cổng {_selectedComPort} đã kết nối sẵn sàng. Bấm Khởi Động Server để mở cổng LAN.";
        }

        UpdateChannels(_selectedChannelCount);
        RefreshLocalIps();
        RefreshComPorts();

        // Nạp danh sách clients đã kết nối nếu server đang chạy sẵn
        foreach (var c in _server.ConnectedClients)
        {
            ConnectedClients.Add(c);
        }
        UpdateClientCountText();

        // Đồng bộ trạng thái các kênh đèn
        if (_server.HardwareService.IsConnected || _server.IsRunning)
        {
            SyncAllChannelsFromState();
        }

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _debounceTimer.Tick += async (_, _) =>
        {
            _debounceTimer.Stop();
            if (_pendingBrightnessChannel >= 0)
            {
                await _server.SetBrightnessDirectAsync(_pendingBrightnessChannel, _pendingBrightnessValue);
            }
        };

        // Wire server events
        _server.OnServerRunningChanged += (_, running) =>
        {
            RunOnUI(() =>
            {
                IsServerRunning = running;
                StatusMessage = running
                    ? $"🟢 Server đang lắng nghe trên cổng {ServerPort}."
                    : "🔴 Server đã dừng.";
                StartServerCommand.NotifyCanExecuteChanged();
                StopServerCommand.NotifyCanExecuteChanged();
            });
        };

        _server.OnClientConnected += (_, info) =>
        {
            RunOnUI(() =>
            {
                if (!ConnectedClients.Any(x => x.ClientId == info.ClientId))
                    ConnectedClients.Add(info);
                UpdateClientCountText();
            });
        };

        _server.OnClientDisconnected += (_, info) =>
        {
            RunOnUI(() =>
            {
                var existing = ConnectedClients.FirstOrDefault(x => x.ClientId == info.ClientId);
                if (existing != null)
                    ConnectedClients.Remove(existing);
                UpdateClientCountText();
            });
        };

        _server.OnTrafficLogged += (_, entry) =>
        {
            RunOnUI(() =>
            {
                var line = $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Direction}] ({entry.ClientEndPoint}) {entry.Content} [{(entry.ElapsedMs > 0 ? entry.ElapsedMs + "ms" : "OK")}]";
                LogText += line + Environment.NewLine;
                if (LogText.Length > 50000)
                    LogText = LogText.Substring(LogText.Length - 30000);
            });
        };

        _server.OnStateChanged += (_, state) =>
        {
            RunOnUI(() =>
            {
                for (int i = 0; i < Channels.Count && i < state.Channels.Length; i++)
                {
                    Channels[i].Sync(state.Channels[i]);
                }
            });
        };

        _server.HardwareService.OnConnectionStateChanged += (_, state) =>
        {
            RunOnUI(() =>
            {
                IsHardwareConnected = state == LightingConnectionState.Connected;
                ConnectHardwareCommand.NotifyCanExecuteChanged();
                DisconnectHardwareCommand.NotifyCanExecuteChanged();
            });
        };

        // Đồng bộ trạng thái phần cứng ban đầu nếu đã kết nối sẵn
        IsHardwareConnected = _server.HardwareService.IsConnected;
    }

    private static void RunOnUI(Action action)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) action();
        else d.BeginInvoke(action);
    }

    private void UpdateClientCountText()
    {
        int count = ConnectedClients.Count;
        ClientCountText = count == 1 ? "1 Client đang kết nối" : $"{count} Client đang kết nối";
    }

    private void UpdateChannels(int count)
    {
        while (Channels.Count < count)
        {
            var item = new ServerChannelItemViewModel(Channels.Count, this);
            if (item.ChannelIndex < _server.CurrentState.Channels.Length)
            {
                item.Sync(_server.CurrentState.Channels[item.ChannelIndex]);
            }
            Channels.Add(item);
        }
        while (Channels.Count > count)
        {
            Channels.RemoveAt(Channels.Count - 1);
        }
    }

    partial void OnSelectedChannelCountChanged(int value)
    {
        UpdateChannels(value);
        SaveSettings();
    }

    public void DebounceBrightness(int channel, int val)
    {
        _pendingBrightnessChannel = channel;
        _pendingBrightnessValue = val;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    // =====================================================================
    // Commands
    // =====================================================================

    [RelayCommand]
    public void RefreshLocalIps()
    {
        LocalIpAddresses.Clear();
        var ips = LightingControlServer.GetLocalIPv4Addresses();
        foreach (var ip in ips) LocalIpAddresses.Add(ip);
        if (LocalIpAddresses.Count > 0) SelectedIpAddress = LocalIpAddresses[0];
    }

    [RelayCommand]
    public void CopyIp()
    {
        if (!string.IsNullOrWhiteSpace(SelectedIpAddress))
        {
            try
            {
                Clipboard.SetText(SelectedIpAddress);
                StatusMessage = $"📋 Đã sao chép địa chỉ IP: {SelectedIpAddress} vào bộ nhớ tạm.";
            }
            catch { }
        }
    }

    [RelayCommand]
    public void RefreshComPorts()
    {
        AvailableComPorts.Clear();
        var ports = System.IO.Ports.SerialPort.GetPortNames();
        if (ports != null && ports.Length > 0)
        {
            foreach (var p in ports.OrderBy(x => x))
                AvailableComPorts.Add(p);
        }
        else
        {
            AvailableComPorts.Add("COM1");
            AvailableComPorts.Add("COM2");
            AvailableComPorts.Add("COM3");
        }

        if (!string.IsNullOrWhiteSpace(SelectedComPort) && AvailableComPorts.Contains(SelectedComPort))
        {
            // keep selection
        }
        else if (AvailableComPorts.Count > 0)
        {
            SelectedComPort = AvailableComPorts[0];
        }
    }

    [RelayCommand(CanExecute = nameof(IsServerStopped))]
    public async Task StartServerAsync()
    {
        SaveSettings();
        try
        {
            // 1. Tự động kết nối phần cứng đèn nếu chưa kết nối
            if (!_server.HardwareService.IsConnected && !string.IsNullOrWhiteSpace(SelectedComPort))
            {
                StatusMessage = $"Đang kết nối cổng {SelectedComPort} ({SelectedBaudRate}bps)...";
                try
                {
                    await _server.HardwareService.ConnectSerialAsync(
                        SelectedComPort,
                        SelectedBaudRate,
                        autoReadState: true);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"⚠️ Cổng {SelectedComPort} chưa kết nối ({ex.Message}), Server chạy chế độ mô phỏng.";
                }
            }

            // 2. Khởi động TCP Server (nếu chưa chạy)
            if (!_server.IsRunning)
            {
                await _server.StartServerAsync(ServerPort);
            }

            // 3. Đọc trạng thái đèn thực tế và đồng bộ lên giao diện
            if (_server.HardwareService.IsConnected)
            {
                StatusMessage = "Đang đọc trạng thái các kênh đèn từ bộ điều khiển...";
                await _server.ReadStateFromHardwareAsync();
                SyncAllChannelsFromState();
                StatusMessage = $"🟢 Server đang chạy trên cổng {ServerPort}. Đã đọc và đồng bộ trạng thái {SelectedChannelCount} kênh từ cổng {SelectedComPort}.";
            }
            else
            {
                SyncAllChannelsFromState();
                StatusMessage = $"🟢 Máy chủ đã khởi động thành công trên cổng {ServerPort} (Mô phỏng - Chưa kết nối đèn).";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi khởi động Server: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsServerRunning))]
    public async Task StopServerAsync()
    {
        try
        {
            await _server.StopServerAsync();
            StatusMessage = "🔴 Máy chủ đã dừng.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi khi dừng Server: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsHardwareDisconnected))]
    public async Task ConnectHardwareAsync()
    {
        SaveSettings();
        try
        {
            StatusMessage = $"Đang kết nối cổng {SelectedComPort} ({SelectedBaudRate}bps)...";
            await _server.HardwareService.ConnectSerialAsync(
                SelectedComPort,
                SelectedBaudRate,
                autoReadState: true);

            if (_server.HardwareService.IsConnected)
            {
                StatusMessage = "Đang đọc trạng thái các kênh đèn từ bộ điều khiển...";
                await _server.ReadStateFromHardwareAsync();
                SyncAllChannelsFromState();
                StatusMessage = $"🟢 Đã kết nối bộ điều khiển đèn qua {SelectedComPort} và đồng bộ thông số các kênh lên giao diện.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi kết nối COM: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ReadHardwareStateAsync()
    {
        if (!_server.HardwareService.IsConnected)
        {
            StatusMessage = "⚠️ Bộ điều khiển đèn chưa kết nối qua cổng COM.";
            return;
        }

        try
        {
            StatusMessage = "Đang đọc lại trạng thái từ bộ điều khiển đèn...";
            var res = await _server.ReadStateFromHardwareAsync();
            SyncAllChannelsFromState();
            StatusMessage = res.IsSuccess
                ? "🟢 Đã đồng bộ thông số tất cả các kênh từ bộ điều khiển đèn lên giao diện."
                : $"⚠️ Phản hồi từ thiết bị: {res.ErrorMessage}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi khi đọc từ thiết bị: {ex.Message}";
        }
    }

    private void SyncAllChannelsFromState()
    {
        RunOnUI(() =>
        {
            var state = _server.CurrentState;
            for (int i = 0; i < Channels.Count && i < state.Channels.Length; i++)
            {
                Channels[i].Sync(state.Channels[i]);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(IsHardwareConnected))]
    public async Task DisconnectHardwareAsync()
    {
        try
        {
            await _server.HardwareService.DisconnectAsync();
            StatusMessage = "🔴 Đã ngắt kết nối cổng COM.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi ngắt kết nối: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task TurnOffAllChannelsAsync()
    {
        await _server.TurnOffAllChannelsDirectAsync(SelectedChannelCount);
        StatusMessage = "💡 Đã gửi lệnh tắt toàn bộ các kênh đèn.";
    }

    [RelayCommand]
    public async Task TurnOnAllChannelsAsync()
    {
        for (int i = 0; i < SelectedChannelCount; i++)
        {
            await _server.SetChannelPowerDirectAsync(i, true);
        }
        StatusMessage = "💡 Đã gửi lệnh bật toàn bộ các kênh đèn.";
    }

    [RelayCommand]
    public void ClearLogs()
    {
        LogText = string.Empty;
        _server.ClearLogs();
    }

    [RelayCommand]
    public void LaunchStandaloneApp()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var exeName = "VisionInspectionApp.LightingServer.exe";
            var path = Path.Combine(baseDir, exeName);

            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                StatusMessage = "🚀 Đã khởi động ứng dụng Lighting Control Server độc lập.";
            }
            else
            {
                // Thử tìm trong thư mục build release/debug
                var altPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "VisionInspectionApp.LightingServer", "bin", "Debug", "net8.0-windows", exeName));
                if (File.Exists(altPath))
                {
                    Process.Start(new ProcessStartInfo(altPath) { UseShellExecute = true });
                    StatusMessage = "🚀 Đã khởi động ứng dụng Lighting Control Server độc lập.";
                }
                else
                {
                    MessageBox.Show($"Không tìm thấy file thực thi {exeName} tại:\n{path}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể khởi động ứng dụng độc lập: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveSettings()
    {
        var config = _settingsService.Settings.LightingServer;
        config.Port = ServerPort;
        config.ComPort = SelectedComPort;
        config.BaudRate = SelectedBaudRate;
        config.ChannelCount = SelectedChannelCount;
        _settingsService.Save();
    }

    public void Cleanup()
    {
        _debounceTimer.Stop();
    }
}
