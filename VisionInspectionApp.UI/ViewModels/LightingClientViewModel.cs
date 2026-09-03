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
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

/// <summary>
/// ViewModel cho từng kênh đèn trên giao diện Máy Khách (Client).
/// </summary>
public sealed partial class ClientChannelItemViewModel : ObservableObject
{
    private readonly LightingClientViewModel _parent;

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

    public ClientChannelItemViewModel(int channelIndex, LightingClientViewModel parent)
    {
        ChannelIndex = channelIndex;
        _parent = parent;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (SuppressCommands || !_parent.IsConnected) return;
        _ = _parent.Client.SetChannelPowerAsync(ChannelIndex, value);
    }

    partial void OnBrightnessChanged(int value)
    {
        if (SuppressCommands || !_parent.IsConnected) return;
        int clamped = Math.Clamp(value, 0, 255);
        if (clamped != value) { Brightness = clamped; return; }
        _parent.DebounceBrightness(ChannelIndex, clamped);
    }

    partial void OnLightingTimeMsChanged(int value)
    {
        if (SuppressCommands || !_parent.IsConnected) return;
        int clamped = Math.Clamp(value, 1, 999);
        if (clamped != value) { LightingTimeMs = clamped; return; }
        _ = _parent.Client.SetLightingTimeAsync(ChannelIndex, clamped);
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
/// ViewModel cho Lighting Control Client điều khiển từ xa qua mạng LAN.
/// </summary>
public sealed partial class LightingClientViewModel : ObservableObject
{
    private readonly GlobalAppSettingsService _settingsService;
    private readonly LightingControlClientService _client;
    private readonly DispatcherTimer _debounceTimer;
    private int _pendingBrightnessChannel = -1;
    private int _pendingBrightnessValue;

    public LightingControlClientService Client => _client;

    [ObservableProperty]
    private string _serverIp = "127.0.0.1";

    [ObservableProperty]
    private int _serverPort = 5050;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusText))]
    [NotifyPropertyChangedFor(nameof(IsNotConnected))]
    private LightingConnectionState _connectionState = LightingConnectionState.Disconnected;

    public bool IsConnected => ConnectionState == LightingConnectionState.Connected;
    public bool IsNotConnected => ConnectionState != LightingConnectionState.Connected && ConnectionState != LightingConnectionState.Connecting;

    public string ConnectionStatusText => ConnectionState switch
    {
        LightingConnectionState.Connected => $"🟢 Đã kết nối ({_client.LastLatencyMs}ms)",
        LightingConnectionState.Connecting => "🟡 Đang kết nối...",
        LightingConnectionState.Error => "🔴 Lỗi kết nối",
        _ => "🔴 Chưa kết nối"
    };

    [ObservableProperty]
    private int _selectedChannelCount = 4;

    public int[] AvailableChannelCounts { get; } = { 4, 8 };

    public ObservableCollection<ClientChannelItemViewModel> Channels { get; } = new();

    public LightingTriggerMode[] AvailableTriggerModes { get; } = Enum.GetValues<LightingTriggerMode>();

    [ObservableProperty]
    private LightingTriggerMode _selectedTriggerMode = LightingTriggerMode.ExternalLow;

    [ObservableProperty]
    private string _manualCommandText = "$F0=1#";

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Nhập IP và Port của máy chủ Lighting Server để kết nối.";

    public LightingClientViewModel(GlobalAppSettingsService settingsService)
    {
        _settingsService = settingsService;
        _client = new LightingControlClientService();

        var config = settingsService.Settings.LightingClient;
        _serverIp = !string.IsNullOrWhiteSpace(config.ServerIp) ? config.ServerIp : "127.0.0.1";
        _serverPort = config.ServerPort > 0 ? config.ServerPort : 5050;
        _selectedChannelCount = config.ChannelCount == 8 ? 8 : 4;

        UpdateChannels(_selectedChannelCount);

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _debounceTimer.Tick += async (_, _) =>
        {
            _debounceTimer.Stop();
            if (_pendingBrightnessChannel >= 0 && IsConnected)
            {
                await _client.SetBrightnessAsync(_pendingBrightnessChannel, _pendingBrightnessValue);
            }
        };

        // Wire client events
        _client.OnConnectionStateChanged += (_, state) =>
        {
            RunOnUI(() =>
            {
                ConnectionState = state;
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(IsNotConnected));
                OnPropertyChanged(nameof(ConnectionStatusText));
                ConnectCommand.NotifyCanExecuteChanged();
                DisconnectCommand.NotifyCanExecuteChanged();
            });
        };

        _client.OnStateUpdated += (_, state) =>
        {
            RunOnUI(() =>
            {
                for (int i = 0; i < Channels.Count && i < state.Channels.Length; i++)
                {
                    Channels[i].Sync(state.Channels[i]);
                }
                SelectedTriggerMode = state.TriggerMode;
            });
        };

        _client.OnLogAdded += (_, entry) =>
        {
            RunOnUI(() =>
            {
                var line = $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level}] {entry.Message}";
                LogText += line + Environment.NewLine;
                if (LogText.Length > 50000)
                    LogText = LogText.Substring(LogText.Length - 30000);
            });
        };

        _client.OnError += (_, err) =>
        {
            RunOnUI(() => StatusMessage = $"❌ {err}");
        };
    }

    private static void RunOnUI(Action action)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) action();
        else d.BeginInvoke(action);
    }

    private void UpdateChannels(int count)
    {
        while (Channels.Count < count)
        {
            Channels.Add(new ClientChannelItemViewModel(Channels.Count, this));
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

    [RelayCommand(CanExecute = nameof(IsNotConnected))]
    public async Task ConnectAsync()
    {
        SaveSettings();
        try
        {
            StatusMessage = $"Đang kết nối tới Server {ServerIp}:{ServerPort}...";
            await _client.ConnectAsync(ServerIp, ServerPort);
            StatusMessage = $"🟢 Kết nối thành công tới {ServerIp}:{ServerPort}.";
            await ReadAllAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Không thể kết nối tới {ServerIp}:{ServerPort}: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    public async Task DisconnectAsync()
    {
        try
        {
            await _client.DisconnectAsync();
            StatusMessage = "🔴 Đã ngắt kết nối.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi khi ngắt kết nối: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ReadAllAsync()
    {
        if (!IsConnected) return;
        StatusMessage = "Đang đọc trạng thái từ Server...";
        var res = await _client.ReadAllAsync(SelectedChannelCount);
        if (res.IsSuccess)
        {
            StatusMessage = $"🟢 Đã cập nhật trạng thái {SelectedChannelCount} kênh từ Server ({_client.LastLatencyMs}ms).";
        }
        else
        {
            StatusMessage = $"❌ Lỗi đọc trạng thái: {res.ErrorCode} - {res.ErrorMessage}";
        }
    }

    [RelayCommand]
    public async Task ApplyAllChannelsAsync()
    {
        if (!IsConnected) return;
        StatusMessage = "Đang áp dụng toàn bộ kênh xuống Server...";

        for (int i = 0; i < Channels.Count; i++)
        {
            var ch = Channels[i];
            await _client.SetChannelPowerAsync(ch.ChannelIndex, ch.IsEnabled);
            await _client.SetBrightnessAsync(ch.ChannelIndex, ch.Brightness);
            await _client.SetLightingTimeAsync(ch.ChannelIndex, ch.LightingTimeMs);
        }

        await _client.SetTriggerModeAsync(SelectedTriggerMode);
        StatusMessage = "🟢 Đã áp dụng toàn bộ cấu hình kênh thành công.";
    }

    [RelayCommand]
    public async Task TurnOffAllChannelsAsync()
    {
        if (!IsConnected) return;
        await _client.TurnOffAllAsync(SelectedChannelCount);
        StatusMessage = "💡 Đã tắt toàn bộ các kênh đèn.";
    }

    [RelayCommand]
    public async Task TurnOnAllChannelsAsync()
    {
        if (!IsConnected) return;
        for (int i = 0; i < Channels.Count; i++)
        {
            await _client.SetChannelPowerAsync(i, true);
        }
        StatusMessage = "💡 Đã bật toàn bộ các kênh đèn.";
    }

    [RelayCommand]
    public async Task SaveConfigAsync()
    {
        if (!IsConnected) return;
        var res = await _client.SaveConfigAsync();
        if (res.IsSuccess)
        {
            StatusMessage = "💾 Đã lưu cấu hình vào bộ nhớ thiết bị thành công.";
        }
        else
        {
            StatusMessage = $"❌ Lỗi lưu cấu hình: {res.ErrorCode}";
        }
    }

    [RelayCommand]
    public async Task SendManualCommandAsync()
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(ManualCommandText)) return;
        var res = await _client.SendCommandAsync(ManualCommandText.Trim());
        StatusMessage = res.IsSuccess ? $"✅ Phản hồi: {res.RawResponse}" : $"❌ Lỗi: {res.ErrorCode} ({res.RawResponse})";
    }

    [RelayCommand]
    public void SetTestCommand(string cmd)
    {
        ManualCommandText = cmd;
    }

    [RelayCommand]
    public void ClearLogs()
    {
        LogText = string.Empty;
        _client.ClearLogs();
    }

    private void SaveSettings()
    {
        var config = _settingsService.Settings.LightingClient;
        config.ServerIp = ServerIp;
        config.ServerPort = ServerPort;
        config.ChannelCount = SelectedChannelCount;
        _settingsService.Save();
    }

    public void Cleanup()
    {
        _debounceTimer.Stop();
        _client.Dispose();
    }
}
