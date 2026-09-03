using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.LightingController;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.LightingServer;

public sealed partial class StandaloneChannelViewModel : ObservableObject
{
    private readonly LightingServerStandaloneViewModel _parent;

    public int ChannelIndex { get; }
    public int ChannelNumber => ChannelIndex + 1;
    public string ChannelLabel => $"CH{ChannelNumber}";

    public bool IsClientMode { get; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _brightness = 100;

    [ObservableProperty]
    private int _lightingTimeMs = 100;

    internal bool SuppressCommands { get; set; }

    public StandaloneChannelViewModel(int channelIndex, LightingServerStandaloneViewModel parent, bool isClientMode = false)
    {
        ChannelIndex = channelIndex;
        _parent = parent;
        IsClientMode = isClientMode;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (SuppressCommands) return;
        if (IsClientMode)
            _ = _parent.ClientSetChannelPowerAsync(ChannelIndex, value);
        else
            _ = _parent.SetChannelPowerAsync(ChannelIndex, value);
    }

    partial void OnBrightnessChanged(int value)
    {
        if (SuppressCommands) return;
        int clamped = Math.Clamp(value, 0, 255);
        if (clamped != value) { Brightness = clamped; return; }
        if (IsClientMode)
            _parent.DebounceClientBrightness(ChannelIndex, clamped);
        else
            _parent.DebounceBrightness(ChannelIndex, clamped);
    }

    partial void OnLightingTimeMsChanged(int value)
    {
        if (SuppressCommands) return;
        int clamped = Math.Clamp(value, 1, 999);
        if (clamped != value) { LightingTimeMs = clamped; return; }
        if (IsClientMode)
            _ = _parent.ClientSetLightingTimeAsync(ChannelIndex, clamped);
        else
            _ = _parent.SetLightingTimeAsync(ChannelIndex, clamped);
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

public sealed partial class LightingServerStandaloneViewModel : ObservableObject
{
    private readonly LightingControlServer _server;
    private readonly LightingControlClientService _client;
    private readonly DispatcherTimer _serverDebounceTimer;
    private readonly DispatcherTimer _clientDebounceTimer;

    private int _serverPendingChannel = -1;
    private int _serverPendingBrightness;
    private int _clientPendingChannel = -1;
    private int _clientPendingBrightness;

    // =====================================================================
    // SERVER MODE PROPERTIES
    // =====================================================================

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
        : "🔴 Đèn chưa kết nối (Mô phỏng)";

    [ObservableProperty]
    private int _selectedChannelCount = 4;

    public int[] AvailableChannelCounts { get; } = { 4, 8 };

    public ObservableCollection<StandaloneChannelViewModel> ServerChannels { get; } = new();
    public ObservableCollection<LightingConnectedClientInfo> ConnectedClients { get; } = new();

    [ObservableProperty]
    private string _clientCountText = "0 Client kết nối";

    [ObservableProperty]
    private string _serverLogText = string.Empty;

    [ObservableProperty]
    private string _serverStatusMessage = "Sẵn sàng khởi động máy chủ Lighting Control Server.";

    // =====================================================================
    // CLIENT MODE PROPERTIES
    // =====================================================================

    [ObservableProperty]
    private string _clientServerIp = "127.0.0.1";

    [ObservableProperty]
    private int _clientServerPort = 5050;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClientConnectionStatusText))]
    [NotifyPropertyChangedFor(nameof(IsClientNotConnected))]
    private LightingConnectionState _clientConnectionState = LightingConnectionState.Disconnected;

    public bool IsClientConnected => ClientConnectionState == LightingConnectionState.Connected;
    public bool IsClientNotConnected => ClientConnectionState != LightingConnectionState.Connected && ClientConnectionState != LightingConnectionState.Connecting;

    public string ClientConnectionStatusText => ClientConnectionState switch
    {
        LightingConnectionState.Connected => $"🟢 Đã kết nối ({_client.LastLatencyMs}ms)",
        LightingConnectionState.Connecting => "🟡 Đang kết nối...",
        LightingConnectionState.Error => "🔴 Lỗi kết nối",
        _ => "🔴 Chưa kết nối"
    };

    [ObservableProperty]
    private int _clientChannelCount = 4;

    public ObservableCollection<StandaloneChannelViewModel> ClientChannels { get; } = new();

    public LightingTriggerMode[] AvailableTriggerModes { get; } = Enum.GetValues<LightingTriggerMode>();

    [ObservableProperty]
    private LightingTriggerMode _selectedTriggerMode = LightingTriggerMode.ExternalLow;

    [ObservableProperty]
    private string _clientManualCommandText = "$F0=1#";

    [ObservableProperty]
    private string _clientLogText = string.Empty;

    [ObservableProperty]
    private string _clientStatusMessage = "Nhập IP và Port của máy chủ để kết nối.";

    public LightingServerStandaloneViewModel()
    {
        _server = new LightingControlServer();
        _client = new LightingControlClientService();
        _isHardwareConnected = _server.HardwareService.IsConnected;

        UpdateServerChannels(_selectedChannelCount);
        UpdateClientChannels(_clientChannelCount);

        RefreshLocalIps();
        RefreshComPorts();

        _serverDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _serverDebounceTimer.Tick += async (_, _) =>
        {
            _serverDebounceTimer.Stop();
            if (_serverPendingChannel >= 0)
            {
                await _server.SetBrightnessDirectAsync(_serverPendingChannel, _serverPendingBrightness);
            }
        };

        _clientDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _clientDebounceTimer.Tick += async (_, _) =>
        {
            _clientDebounceTimer.Stop();
            if (_clientPendingChannel >= 0 && IsClientConnected)
            {
                await _client.SetBrightnessAsync(_clientPendingChannel, _clientPendingBrightness);
            }
        };

        // Wire Server events
        _server.OnServerRunningChanged += (_, running) =>
        {
            RunOnUI(() =>
            {
                IsServerRunning = running;
                ServerStatusMessage = running
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
                ServerLogText += line + Environment.NewLine;
                if (ServerLogText.Length > 50000)
                    ServerLogText = ServerLogText.Substring(ServerLogText.Length - 30000);
            });
        };

        _server.OnStateChanged += (_, state) =>
        {
            RunOnUI(() =>
            {
                for (int i = 0; i < ServerChannels.Count && i < state.Channels.Length; i++)
                {
                    ServerChannels[i].Sync(state.Channels[i]);
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

        // Wire Client events
        _client.OnConnectionStateChanged += (_, state) =>
        {
            RunOnUI(() =>
            {
                ClientConnectionState = state;
                OnPropertyChanged(nameof(IsClientConnected));
                OnPropertyChanged(nameof(IsClientNotConnected));
                OnPropertyChanged(nameof(ClientConnectionStatusText));
                ClientConnectCommand.NotifyCanExecuteChanged();
                ClientDisconnectCommand.NotifyCanExecuteChanged();
            });
        };

        _client.OnStateUpdated += (_, state) =>
        {
            RunOnUI(() =>
            {
                for (int i = 0; i < ClientChannels.Count && i < state.Channels.Length; i++)
                {
                    ClientChannels[i].Sync(state.Channels[i]);
                }
                SelectedTriggerMode = state.TriggerMode;
            });
        };

        _client.OnLogAdded += (_, entry) =>
        {
            RunOnUI(() =>
            {
                var line = $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level}] {entry.Message}";
                ClientLogText += line + Environment.NewLine;
                if (ClientLogText.Length > 50000)
                    ClientLogText = ClientLogText.Substring(ClientLogText.Length - 30000);
            });
        };

        _client.OnError += (_, err) =>
        {
            RunOnUI(() => ClientStatusMessage = $"❌ {err}");
        };
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

    private void UpdateServerChannels(int count)
    {
        while (ServerChannels.Count < count)
        {
            var item = new StandaloneChannelViewModel(ServerChannels.Count, this);
            if (item.ChannelIndex < _server.CurrentState.Channels.Length)
            {
                item.Sync(_server.CurrentState.Channels[item.ChannelIndex]);
            }
            ServerChannels.Add(item);
        }
        while (ServerChannels.Count > count)
            ServerChannels.RemoveAt(ServerChannels.Count - 1);
    }

    private void UpdateClientChannels(int count)
    {
        while (ClientChannels.Count < count)
            ClientChannels.Add(new StandaloneChannelViewModel(ClientChannels.Count, this, isClientMode: true));
        while (ClientChannels.Count > count)
            ClientChannels.RemoveAt(ClientChannels.Count - 1);
    }

    partial void OnSelectedChannelCountChanged(int value) => UpdateServerChannels(value);
    partial void OnClientChannelCountChanged(int value) => UpdateClientChannels(value);

    public async Task ClientSetChannelPowerAsync(int channel, bool on)
    {
        if (IsClientConnected)
            await _client.SetChannelPowerAsync(channel, on);
    }

    public void DebounceClientBrightness(int channel, int val)
    {
        _clientPendingChannel = channel;
        _clientPendingBrightness = val;
        _clientDebounceTimer.Stop();
        _clientDebounceTimer.Start();
    }

    public async Task ClientSetLightingTimeAsync(int channel, int ms)
    {
        if (IsClientConnected)
            await _client.SetLightingTimeAsync(channel, ms);
    }

    // =====================================================================
    // SERVER COMMANDS
    // =====================================================================

    [RelayCommand]
    public void RefreshLocalIps()
    {
        LocalIpAddresses.Clear();
        var ips = LightingControlServer.GetLocalIPv4Addresses();
        foreach (var ip in ips) LocalIpAddresses.Add(ip);
        if (LocalIpAddresses.Count > 0)
        {
            SelectedIpAddress = LocalIpAddresses[0];
            ClientServerIp = LocalIpAddresses[0];
        }
    }

    [RelayCommand]
    public void CopyIp()
    {
        if (!string.IsNullOrWhiteSpace(SelectedIpAddress))
        {
            try
            {
                Clipboard.SetText(SelectedIpAddress);
                ServerStatusMessage = $"📋 Đã sao chép IP: {SelectedIpAddress} vào clipboard.";
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
            foreach (var p in ports.OrderBy(x => x)) AvailableComPorts.Add(p);
        }
        else
        {
            AvailableComPorts.Add("COM1");
            AvailableComPorts.Add("COM2");
            AvailableComPorts.Add("COM3");
        }
        if (!string.IsNullOrWhiteSpace(SelectedComPort) && AvailableComPorts.Contains(SelectedComPort)) { }
        else if (AvailableComPorts.Count > 0) SelectedComPort = AvailableComPorts[0];
    }

    [RelayCommand(CanExecute = nameof(IsServerStopped))]
    public async Task StartServerAsync()
    {
        try
        {
            // 1. Tự động kết nối phần cứng đèn nếu chưa kết nối
            if (!_server.HardwareService.IsConnected && !string.IsNullOrWhiteSpace(SelectedComPort))
            {
                ServerStatusMessage = $"Đang kết nối cổng {SelectedComPort} ({SelectedBaudRate}bps)...";
                try
                {
                    await _server.HardwareService.ConnectSerialAsync(
                        SelectedComPort,
                        SelectedBaudRate,
                        autoReadState: true);
                }
                catch (Exception ex)
                {
                    ServerStatusMessage = $"⚠️ Cổng {SelectedComPort} chưa kết nối ({ex.Message}), Server chạy chế độ mô phỏng.";
                }
            }

            // 2. Khởi động TCP Server
            await _server.StartServerAsync(ServerPort);

            // 3. Đọc trạng thái đèn thực tế và đồng bộ lên giao diện
            if (_server.HardwareService.IsConnected)
            {
                ServerStatusMessage = "Đang đọc trạng thái các kênh đèn từ bộ điều khiển...";
                await _server.ReadStateFromHardwareAsync();
                SyncAllServerChannelsFromState();
                ServerStatusMessage = $"🟢 Server đang chạy trên cổng {ServerPort}. Đã đọc và đồng bộ trạng thái {SelectedChannelCount} kênh từ cổng {SelectedComPort}.";
            }
            else
            {
                SyncAllServerChannelsFromState();
                ServerStatusMessage = $"🟢 Server đã khởi động trên cổng {ServerPort} (Mô phỏng - Chưa kết nối đèn).";
            }
        }
        catch (Exception ex)
        {
            ServerStatusMessage = $"❌ Lỗi khởi động Server: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsServerRunning))]
    public async Task StopServerAsync()
    {
        try
        {
            await _server.StopServerAsync();
            ServerStatusMessage = "🔴 Server đã dừng.";
        }
        catch (Exception ex)
        {
            ServerStatusMessage = $"❌ Lỗi khi dừng Server: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsHardwareDisconnected))]
    public async Task ConnectHardwareAsync()
    {
        try
        {
            ServerStatusMessage = $"Đang kết nối {SelectedComPort} ({SelectedBaudRate}bps)...";
            await _server.HardwareService.ConnectSerialAsync(SelectedComPort, SelectedBaudRate, autoReadState: true);
            if (_server.HardwareService.IsConnected)
            {
                ServerStatusMessage = "Đang đọc trạng thái các kênh đèn từ bộ điều khiển...";
                await _server.ReadStateFromHardwareAsync();
                SyncAllServerChannelsFromState();
                ServerStatusMessage = $"🟢 Đã kết nối đèn cổng {SelectedComPort} và đồng bộ thông số các kênh lên giao diện.";
            }
        }
        catch (Exception ex)
        {
            ServerStatusMessage = $"❌ Lỗi kết nối COM: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ServerReadHardwareStateAsync()
    {
        if (!_server.HardwareService.IsConnected)
        {
            ServerStatusMessage = "⚠️ Bộ điều khiển đèn chưa kết nối qua cổng COM.";
            return;
        }

        try
        {
            ServerStatusMessage = "Đang đọc lại trạng thái từ bộ điều khiển đèn...";
            var res = await _server.ReadStateFromHardwareAsync();
            SyncAllServerChannelsFromState();
            ServerStatusMessage = res.IsSuccess
                ? "🟢 Đã đồng bộ thông số tất cả các kênh từ bộ điều khiển đèn lên giao diện."
                : $"⚠️ Phản hồi từ thiết bị: {res.ErrorMessage}";
        }
        catch (Exception ex)
        {
            ServerStatusMessage = $"❌ Lỗi khi đọc từ thiết bị: {ex.Message}";
        }
    }

    private void SyncAllServerChannelsFromState()
    {
        RunOnUI(() =>
        {
            var state = _server.CurrentState;
            for (int i = 0; i < ServerChannels.Count && i < state.Channels.Length; i++)
            {
                ServerChannels[i].Sync(state.Channels[i]);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(IsHardwareConnected))]
    public async Task DisconnectHardwareAsync()
    {
        try
        {
            await _server.HardwareService.DisconnectAsync();
            ServerStatusMessage = "🔴 Đã ngắt kết nối cổng COM.";
        }
        catch (Exception ex)
        {
            ServerStatusMessage = $"❌ Lỗi ngắt kết nối: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ServerTurnOffAllAsync()
    {
        await _server.TurnOffAllChannelsDirectAsync(SelectedChannelCount);
        ServerStatusMessage = "💡 Đã tắt toàn bộ kênh đèn.";
    }

    [RelayCommand]
    public async Task ServerTurnOnAllAsync()
    {
        for (int i = 0; i < SelectedChannelCount; i++)
            await _server.SetChannelPowerDirectAsync(i, true);
        ServerStatusMessage = "💡 Đã bật toàn bộ kênh đèn.";
    }

    [RelayCommand]
    public void ClearServerLogs()
    {
        ServerLogText = string.Empty;
        _server.ClearLogs();
    }

    public async Task SetChannelPowerAsync(int channel, bool on)
    {
        await _server.SetChannelPowerDirectAsync(channel, on);
    }

    public void DebounceBrightness(int channel, int val)
    {
        _serverPendingChannel = channel;
        _serverPendingBrightness = val;
        _serverDebounceTimer.Stop();
        _serverDebounceTimer.Start();
    }

    public async Task SetLightingTimeAsync(int channel, int ms)
    {
        await _server.SetLightingTimeDirectAsync(channel, ms);
    }

    // =====================================================================
    // CLIENT COMMANDS
    // =====================================================================

    [RelayCommand(CanExecute = nameof(IsClientNotConnected))]
    public async Task ClientConnectAsync()
    {
        try
        {
            ClientStatusMessage = $"Đang kết nối {ClientServerIp}:{ClientServerPort}...";
            await _client.ConnectAsync(ClientServerIp, ClientServerPort);
            ClientStatusMessage = $"🟢 Đã kết nối tới {ClientServerIp}:{ClientServerPort}.";
            await ClientReadAllAsync();
        }
        catch (Exception ex)
        {
            ClientStatusMessage = $"❌ Không thể kết nối: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsClientConnected))]
    public async Task ClientDisconnectAsync()
    {
        try
        {
            await _client.DisconnectAsync();
            ClientStatusMessage = "🔴 Đã ngắt kết nối.";
        }
        catch (Exception ex)
        {
            ClientStatusMessage = $"❌ Lỗi ngắt kết nối: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ClientReadAllAsync()
    {
        if (!IsClientConnected) return;
        ClientStatusMessage = "Đang đọc trạng thái từ Server...";
        var res = await _client.ReadAllAsync(ClientChannelCount);
        if (res.IsSuccess)
            ClientStatusMessage = $"🟢 Đã đọc trạng thái {ClientChannelCount} kênh thành công.";
        else
            ClientStatusMessage = $"❌ Lỗi: {res.ErrorCode}";
    }

    [RelayCommand]
    public async Task ClientApplyAllAsync()
    {
        if (!IsClientConnected) return;
        for (int i = 0; i < ClientChannels.Count; i++)
        {
            var ch = ClientChannels[i];
            await _client.SetChannelPowerAsync(ch.ChannelIndex, ch.IsEnabled);
            await _client.SetBrightnessAsync(ch.ChannelIndex, ch.Brightness);
            await _client.SetLightingTimeAsync(ch.ChannelIndex, ch.LightingTimeMs);
        }
        await _client.SetTriggerModeAsync(SelectedTriggerMode);
        ClientStatusMessage = "🟢 Đã áp dụng toàn bộ cấu hình.";
    }

    [RelayCommand]
    public async Task ClientTurnOffAllAsync()
    {
        if (!IsClientConnected) return;
        await _client.TurnOffAllAsync(ClientChannelCount);
        ClientStatusMessage = "💡 Đã tắt toàn bộ kênh đèn.";
    }

    [RelayCommand]
    public async Task ClientTurnOnAllAsync()
    {
        if (!IsClientConnected) return;
        for (int i = 0; i < ClientChannels.Count; i++)
            await _client.SetChannelPowerAsync(i, true);
        ClientStatusMessage = "💡 Đã bật toàn bộ kênh đèn.";
    }

    [RelayCommand]
    public async Task ClientSaveConfigAsync()
    {
        if (!IsClientConnected) return;
        var res = await _client.SaveConfigAsync();
        ClientStatusMessage = res.IsSuccess ? "💾 Đã lưu cấu hình vào bộ điều khiển." : $"❌ Lỗi: {res.ErrorCode}";
    }

    [RelayCommand]
    public async Task ClientSendManualCommandAsync()
    {
        if (!IsClientConnected || string.IsNullOrWhiteSpace(ClientManualCommandText)) return;
        var res = await _client.SendCommandAsync(ClientManualCommandText.Trim());
        ClientStatusMessage = res.IsSuccess ? $"✅ Phản hồi: {res.RawResponse}" : $"❌ Lỗi: {res.ErrorCode} ({res.RawResponse})";
    }

    [RelayCommand]
    public void SetClientTestCommand(string cmd) => ClientManualCommandText = cmd;

    [RelayCommand]
    public void ClearClientLogs()
    {
        ClientLogText = string.Empty;
        _client.ClearLogs();
    }

    public void Cleanup()
    {
        _serverDebounceTimer.Stop();
        _clientDebounceTimer.Stop();
        _server.Dispose();
        _client.Dispose();
    }
}
