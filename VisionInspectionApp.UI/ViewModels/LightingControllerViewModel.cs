using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.LightingController;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Services;

namespace VisionInspectionApp.UI.ViewModels;

// =====================================================================
// Per-Channel ViewModel (×8 instances)
// =====================================================================

public sealed partial class LightingChannelViewModel : ObservableObject
{
    private readonly LightingControllerViewModel _parent;

    /// <summary>Channel index 0-7.</summary>
    public int ChannelIndex { get; }

    /// <summary>Display number 1-8.</summary>
    public int ChannelNumber => ChannelIndex + 1;

    public string ChannelLabel => $"CH{ChannelNumber}";

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _brightness = 100;

    [ObservableProperty]
    private int _lightingTimeMs = 100;

    // Suppress sending commands during batch updates (e.g., ReadAll sync)
    internal bool SuppressCommands { get; set; }

    public LightingChannelViewModel(int channelIndex, LightingControllerViewModel parent)
    {
        ChannelIndex = channelIndex;
        _parent = parent;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (SuppressCommands || !_parent.IsConnected) return;
        _ = _parent.SendChannelPowerAsync(ChannelIndex, value);
    }

    partial void OnBrightnessChanged(int value)
    {
        if (SuppressCommands || !_parent.IsConnected) return;
        // Clamp
        if (value < 0) { Brightness = 0; return; }
        if (value > 255) { Brightness = 255; return; }
        _parent.DebounceBrightness(ChannelIndex, value);
    }

    partial void OnLightingTimeMsChanged(int value)
    {
        if (SuppressCommands || !_parent.IsConnected) return;
        if (value < 1) { LightingTimeMs = 1; return; }
        if (value > 999) { LightingTimeMs = 999; return; }
    }

    /// <summary>Update from device state without sending commands back.</summary>
    internal void SyncFromDevice(LightingChannelState state)
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

// =====================================================================
// Per-Channel Startup Settings ViewModel
// =====================================================================

public sealed partial class LightingStartupChannelViewModel : ObservableObject
{
    private readonly LightingControllerViewModel _parent;

    public int ChannelIndex { get; }
    public int ChannelNumber => ChannelIndex + 1;
    public string ChannelLabel => $"CH{ChannelNumber}";

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _brightness = 120;

    [ObservableProperty]
    private int _lightingTimeMs = 100;

    public LightingStartupChannelViewModel(int channelIndex, LightingControllerViewModel parent, bool isEnabled = true, int brightness = 120, int lightingTimeMs = 100)
    {
        ChannelIndex = channelIndex;
        _parent = parent;
        _isEnabled = isEnabled;
        _brightness = brightness;
        _lightingTimeMs = lightingTimeMs;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _parent.SaveSettings();
    }

    partial void OnBrightnessChanged(int value)
    {
        if (value < 0) { Brightness = 0; return; }
        if (value > 255) { Brightness = 255; return; }
        _parent.SaveSettings();
    }

    partial void OnLightingTimeMsChanged(int value)
    {
        if (value < 1) { LightingTimeMs = 1; return; }
        if (value > 999) { LightingTimeMs = 999; return; }
        _parent.SaveSettings();
    }
}

// =====================================================================
// Main Lighting Controller ViewModel
// =====================================================================

public sealed partial class LightingControllerViewModel : ObservableObject
{
    private readonly LightingControllerService _service;
    private readonly GlobalAppSettingsService _settingsService;
    private readonly DispatcherTimer _brightnessDebounceTimer;
    private int _pendingBrightnessChannel = -1;
    private int _pendingBrightnessValue;

    public LightingControllerViewModel(LightingControllerService service, GlobalAppSettingsService settingsService)
    {
        _service = service;
        _settingsService = settingsService;

        // Load saved settings
        var settings = settingsService.Settings.Lighting;
        _selectedChannelCount = settings.ChannelCount == 8 ? 8 : 4;
        UpdateChannels(_selectedChannelCount);

        _autoConnect = settings.AutoConnect;
        _enableStartupLighting = settings.EnableStartupLighting;
        UpdateStartupChannels(_selectedChannelCount, settings.StartupChannels);

        _selectedInterfaceType = (LightingInterfaceType)settings.InterfaceType;
        _controllerIp = settings.ControllerIp;
        _port = settings.Port;
        _selectedNetworkMode = (LightingNetworkMode)settings.NetworkMode;
        _subnetMask = settings.SubnetMask;
        _gateway = settings.Gateway;
        _destinationIp = settings.DestinationIp;
        _destinationPort = settings.DestinationPort;

        _selectedComPort = settings.ComPort;
        _selectedBaudRate = settings.BaudRate > 0 ? settings.BaudRate : 19200;
        _selectedDataBits = settings.DataBits > 0 ? settings.DataBits : 8;
        _selectedParity = Enum.IsDefined(typeof(System.IO.Ports.Parity), settings.Parity) ? (System.IO.Ports.Parity)settings.Parity : System.IO.Ports.Parity.None;
        _selectedStopBits = Enum.IsDefined(typeof(System.IO.Ports.StopBits), settings.StopBits) ? (System.IO.Ports.StopBits)settings.StopBits : System.IO.Ports.StopBits.One;
        _selectedLineEndingIndex = Math.Clamp(settings.LineEnding, 0, 3);
        _dtrEnable = settings.DtrEnable;
        _rtsEnable = settings.RtsEnable;
        _autoReadOnConnect = settings.AutoReadOnConnect;

        RefreshComPorts();

        // Debounce timer for brightness slider (50ms debounce)
        _brightnessDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _brightnessDebounceTimer.Tick += async (_, _) =>
        {
            _brightnessDebounceTimer.Stop();
            if (_pendingBrightnessChannel >= 0 && IsConnected)
            {
                await SendBrightnessAsync(_pendingBrightnessChannel, _pendingBrightnessValue);
            }
        };

        // Subscribe to service events
        _service.OnConnectionStateChanged += (_, state) =>
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

        _service.OnStateUpdated += (_, state) =>
        {
            RunOnUI(() => SyncFromDeviceState(state));
        };

        _service.OnLogAdded += (_, entry) =>
        {
            RunOnUI(() =>
            {
                var line = $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level}] {entry.Message}";
                LogText += line + Environment.NewLine;
                // Trim log text if too long
                if (LogText.Length > 50000)
                    LogText = LogText.Substring(LogText.Length - 30000);
            });
        };

        _service.OnError += (_, msg) =>
        {
            RunOnUI(() => LastError = msg);
        };

        // Đồng bộ trạng thái kết nối và tham số hiện tại từ service (nếu đã kết nối sẵn từ khi mở app)
        _connectionState = _service.ConnectionState;
        if (_service.IsConnected && _service.LastKnownState != null)
        {
            SyncFromDeviceState(_service.LastKnownState);
        }
    }

    private static void RunOnUI(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    // =====================================================================
    // Properties
    // =====================================================================

    public ObservableCollection<LightingChannelViewModel> Channels { get; } = new();
    public ObservableCollection<LightingStartupChannelViewModel> StartupChannels { get; } = new();

    public int[] AvailableChannelCounts { get; } = { 4, 8 };

    [ObservableProperty]
    private bool _autoConnect = true;

    partial void OnAutoConnectChanged(bool value)
    {
        SaveSettings();
    }

    [ObservableProperty]
    private bool _enableStartupLighting = true;

    partial void OnEnableStartupLightingChanged(bool value)
    {
        SaveSettings();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridColumns))]
    [NotifyPropertyChangedFor(nameof(GridRows))]
    private int _selectedChannelCount = 4;

    public int GridColumns => 4;
    public int GridRows => SelectedChannelCount == 4 ? 1 : 2;

    partial void OnSelectedChannelCountChanged(int value)
    {
        UpdateChannels(value);
        UpdateStartupChannels(value, _settingsService.Settings.Lighting.StartupChannels);
        SaveSettings();
    }

    private void UpdateChannels(int count)
    {
        while (Channels.Count < count)
        {
            Channels.Add(new LightingChannelViewModel(Channels.Count, this));
        }
        while (Channels.Count > count)
        {
            Channels.RemoveAt(Channels.Count - 1);
        }
    }

    private void UpdateStartupChannels(int count, List<LightingStartupChannelSettings>? savedList = null)
    {
        while (StartupChannels.Count < count)
        {
            int idx = StartupChannels.Count;
            var saved = savedList?.FirstOrDefault(x => x.ChannelIndex == idx);
            bool isEnabled = saved?.IsEnabled ?? (idx == 0);
            int br = saved?.Brightness ?? 120;
            int time = saved?.LightingTimeMs ?? 100;
            StartupChannels.Add(new LightingStartupChannelViewModel(idx, this, isEnabled, br, time));
        }
        while (StartupChannels.Count > count)
        {
            StartupChannels.RemoveAt(StartupChannels.Count - 1);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEthernetSelected))]
    [NotifyPropertyChangedFor(nameof(IsSerialSelected))]
    private LightingInterfaceType _selectedInterfaceType = LightingInterfaceType.Ethernet;

    public bool IsEthernetSelected
    {
        get => SelectedInterfaceType == LightingInterfaceType.Ethernet;
        set
        {
            if (value && SelectedInterfaceType != LightingInterfaceType.Ethernet)
                SelectedInterfaceType = LightingInterfaceType.Ethernet;
        }
    }

    public bool IsSerialSelected
    {
        get => SelectedInterfaceType == LightingInterfaceType.SerialCom;
        set
        {
            if (value && SelectedInterfaceType != LightingInterfaceType.SerialCom)
                SelectedInterfaceType = LightingInterfaceType.SerialCom;
        }
    }

    public LightingInterfaceType[] AvailableInterfaceTypes { get; } =
        Enum.GetValues<LightingInterfaceType>();

    // Ethernet properties
    [ObservableProperty]
    private string _controllerIp = "192.168.1.2";

    [ObservableProperty]
    private int _port = 1200;

    [ObservableProperty]
    private LightingNetworkMode _selectedNetworkMode = LightingNetworkMode.TcpServer;

    [ObservableProperty]
    private string _subnetMask = "255.255.255.0";

    [ObservableProperty]
    private string _gateway = "192.168.1.1";

    [ObservableProperty]
    private string _destinationIp = "192.168.1.3";

    [ObservableProperty]
    private int _destinationPort = 1200;

    // Serial RS-232 / COM Port properties
    public ObservableCollection<string> AvailableComPorts { get; } = new();

    [ObservableProperty]
    private string _selectedComPort = "COM1";

    public int[] AvailableBaudRates { get; } = { 9600, 19200, 38400, 57600, 115200 };

    [ObservableProperty]
    private int _selectedBaudRate = 19200;

    public int[] AvailableDataBits { get; } = { 7, 8 };

    [ObservableProperty]
    private int _selectedDataBits = 8;

    public System.IO.Ports.Parity[] AvailableParities { get; } =
        { System.IO.Ports.Parity.None, System.IO.Ports.Parity.Odd, System.IO.Ports.Parity.Even };

    [ObservableProperty]
    private System.IO.Ports.Parity _selectedParity = System.IO.Ports.Parity.None;

    public System.IO.Ports.StopBits[] AvailableStopBits { get; } =
        { System.IO.Ports.StopBits.One, System.IO.Ports.StopBits.Two };

    [ObservableProperty]
    private System.IO.Ports.StopBits _selectedStopBits = System.IO.Ports.StopBits.One;

    public string[] AvailableLineEndings { get; } =
        { "None (Không)", @"\r\n (CRLF)", @"\r (CR)", @"\n (LF)" };

    [ObservableProperty]
    private int _selectedLineEndingIndex = 0;

    [ObservableProperty]
    private bool _dtrEnable = false;

    [ObservableProperty]
    private bool _rtsEnable = false;

    [ObservableProperty]
    private bool _autoReadOnConnect = false;

    [ObservableProperty]
    private string _manualCommandText = "$F0=1#";

    [ObservableProperty]
    private LightingConnectionState _connectionState = LightingConnectionState.Disconnected;

    [ObservableProperty]
    private LightingTriggerMode _selectedTriggerMode = LightingTriggerMode.ExternalLow;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private string _lastError = string.Empty;

    public bool IsConnected => ConnectionState == LightingConnectionState.Connected;
    public bool IsNotConnected => ConnectionState != LightingConnectionState.Connected && ConnectionState != LightingConnectionState.Connecting;

    public string ConnectionStatusText => ConnectionState switch
    {
        LightingConnectionState.Disconnected => "🔴 Disconnected",
        LightingConnectionState.Connecting => "🟡 Connecting...",
        LightingConnectionState.Connected => "🟢 Connected",
        LightingConnectionState.Error => "🔴 Error",
        _ => "Unknown"
    };

    public LightingNetworkMode[] AvailableNetworkModes { get; } =
        Enum.GetValues<LightingNetworkMode>();

    public LightingTriggerMode[] AvailableTriggerModes { get; } =
        Enum.GetValues<LightingTriggerMode>();

    public string? GetLineEndingString() => SelectedLineEndingIndex switch
    {
        1 => "\r\n",
        2 => "\r",
        3 => "\n",
        _ => null
    };

    // =====================================================================
    // Commands
    // =====================================================================

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
            // keep existing selection
        }
        else if (AvailableComPorts.Count > 0)
        {
            SelectedComPort = AvailableComPorts[0];
        }
    }

    [RelayCommand(CanExecute = nameof(IsNotConnected))]
    private async Task ConnectAsync()
    {
        SaveSettings();
        LastError = string.Empty;
        try
        {
            if (SelectedInterfaceType == LightingInterfaceType.SerialCom)
            {
                await _service.ConnectSerialAsync(
                    SelectedComPort,
                    SelectedBaudRate,
                    SelectedParity,
                    SelectedDataBits,
                    SelectedStopBits,
                    readTimeoutMs: 3000,
                    writeTimeoutMs: 3000,
                    lineEnding: GetLineEndingString(),
                    dtrEnable: DtrEnable,
                    rtsEnable: RtsEnable,
                    autoReadState: false);
            }
            else
            {
                await _service.ConnectAsync(ControllerIp, Port, SelectedNetworkMode);
            }

            // Immediately query and populate all channel values from controller
            if (IsConnected)
            {
                await ReadAllAsync();
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task SendManualCommandAsync()
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(ManualCommandText)) return;
        LastError = string.Empty;
        var cmd = ManualCommandText.Trim();
        var result = await _service.SendCommandAsync(cmd);
        if (!result.IsSuccess)
            LastError = $"Command failed: {result.ErrorCode} - {result.ErrorMessage}";
    }

    [RelayCommand]
    private void SetTestCommand(string cmd)
    {
        ManualCommandText = cmd;
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogText = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task DisconnectAsync()
    {
        try
        {
            await _service.DisconnectAsync();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ReadAllAsync()
    {
        if (!IsConnected) return;
        LastError = string.Empty;
        var result = await _service.ReadAllParametersAsync(SelectedChannelCount);
        if (!result.IsSuccess)
            LastError = $"Read All failed: {result.ErrorCode} - {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task SaveToControllerAsync()
    {
        if (!IsConnected) return;
        LastError = string.Empty;
        var result = await _service.SaveConfigAsync();
        if (result.IsSuccess)
            LastError = string.Empty;
        else
            LastError = $"Save failed: {result.ErrorCode} - {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task FactoryResetAsync()
    {
        if (!IsConnected) return;

        var confirm = MessageBox.Show(
            "Are you sure you want to restore the Lighting Controller to factory defaults?\n\nThis will reset ALL parameters.",
            "⚠️ Factory Reset — Lighting Controller",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        LastError = string.Empty;
        var result = await _service.RestoreFactoryDefaultsAsync();
        if (result.IsSuccess)
        {
            // Re-read state after reset
            await ReadAllAsync();
        }
        else
        {
            LastError = $"Factory Reset failed: {result.ErrorCode} - {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task SetTriggerAsync()
    {
        if (!IsConnected) return;
        var result = await _service.SetTriggerModeAsync(SelectedTriggerMode);
        if (!result.IsSuccess)
            LastError = $"Set Trigger failed: {result.ErrorCode} - {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task ToggleLockAsync()
    {
        if (!IsConnected) return;
        IsLocked = !IsLocked;
        var result = await _service.SetLockAsync(IsLocked);
        if (!result.IsSuccess)
        {
            IsLocked = !IsLocked; // revert
            LastError = $"Lock/Unlock failed: {result.ErrorCode} - {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task ApplyAllChannelsAsync()
    {
        if (!IsConnected) return;
        LastError = string.Empty;

        // Apply channel settings using standard individual commands ($Fx, $Lx, $Tx)
        // only for existing hardware channels (e.g. 4 or 8 channels)
        for (int i = 0; i < Channels.Count; i++)
        {
            var ch = Channels[i];

            // 1. Power (F0-F3 or F0-F7)
            var pwrResult = await _service.SetChannelPowerAsync(ch.ChannelIndex, ch.IsEnabled);
            if (!pwrResult.IsSuccess)
            {
                LastError = $"Apply All CH{ch.ChannelNumber} power failed: {pwrResult.ErrorCode} - {pwrResult.ErrorMessage}";
                return;
            }

            // 2. Brightness (L0-L3 or L0-L7)
            var brResult = await _service.SetBrightnessAsync(ch.ChannelIndex, ch.Brightness);
            if (!brResult.IsSuccess)
            {
                LastError = $"Apply All CH{ch.ChannelNumber} brightness failed: {brResult.ErrorCode} - {brResult.ErrorMessage}";
                return;
            }

            // 3. Lighting Time (T0-T3 or T0-T7)
            var timeResult = await _service.SetLightingTimeAsync(ch.ChannelIndex, ch.LightingTimeMs);
            if (!timeResult.IsSuccess)
            {
                LastError = $"Apply All CH{ch.ChannelNumber} time failed: {timeResult.ErrorCode} - {timeResult.ErrorMessage}";
                return;
            }
        }

        // 4. Trigger Mode (TR)
        var trResult = await _service.SetTriggerModeAsync(SelectedTriggerMode);
        if (!trResult.IsSuccess)
        {
            LastError = $"Apply All Trigger failed: {trResult.ErrorCode} - {trResult.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task ApplyChannelLightingTimeAsync(int channelIndex)
    {
        if (!IsConnected || channelIndex < 0 || channelIndex >= Channels.Count) return;
        var ch = Channels[channelIndex];
        var time = Math.Clamp(ch.LightingTimeMs, 1, 999);
        var result = await _service.SetLightingTimeAsync(channelIndex, time);
        if (!result.IsSuccess)
            LastError = $"Set Time CH{channelIndex + 1} failed: {result.ErrorCode}";
    }

    // =====================================================================
    // Internal methods for channel callbacks
    // =====================================================================

    internal async Task SendChannelPowerAsync(int channel, bool on)
    {
        var result = await _service.SetChannelPowerAsync(channel, on);
        if (!result.IsSuccess)
        {
            LastError = $"CH{channel + 1} power failed: {result.ErrorCode}";
            // Revert UI
            if (channel < Channels.Count)
            {
                Channels[channel].SuppressCommands = true;
                Channels[channel].IsEnabled = !on;
                Channels[channel].SuppressCommands = false;
            }
        }
    }

    internal void DebounceBrightness(int channel, int value)
    {
        _pendingBrightnessChannel = channel;
        _pendingBrightnessValue = value;
        _brightnessDebounceTimer.Stop();
        _brightnessDebounceTimer.Start();
    }

    private async Task SendBrightnessAsync(int channel, int brightness)
    {
        if (!IsConnected) return;
        var clamped = Math.Clamp(brightness, 0, 255);
        var result = await _service.SetBrightnessAsync(channel, clamped);
        if (!result.IsSuccess)
            LastError = $"CH{channel + 1} brightness failed: {result.ErrorCode}";
    }

    // =====================================================================
    // Sync from device
    // =====================================================================

    private void SyncFromDeviceState(LightingControllerState state)
    {
        for (int i = 0; i < Channels.Count && i < state.Channels.Length; i++)
        {
            Channels[i].SyncFromDevice(state.Channels[i]);
        }

        // Sync trigger mode without re-sending
        SelectedTriggerMode = state.TriggerMode;

        IsLocked = state.LC != 0;
    }

    [RelayCommand]
    private void CaptureCurrentAsStartup()
    {
        for (int i = 0; i < Channels.Count && i < StartupChannels.Count; i++)
        {
            StartupChannels[i].IsEnabled = Channels[i].IsEnabled;
            StartupChannels[i].Brightness = Channels[i].Brightness;
            StartupChannels[i].LightingTimeMs = Channels[i].LightingTimeMs;
        }
        SaveSettings();
        LastError = string.Empty;
    }

    // =====================================================================
    // Settings Persistence
    // =====================================================================

    internal void SaveSettings()
    {
        var settings = _settingsService.Settings.Lighting;
        settings.ChannelCount = SelectedChannelCount;
        settings.AutoConnect = AutoConnect;
        settings.EnableStartupLighting = EnableStartupLighting;
        settings.StartupChannels = StartupChannels.Select(x => new LightingStartupChannelSettings
        {
            ChannelIndex = x.ChannelIndex,
            IsEnabled = x.IsEnabled,
            Brightness = x.Brightness,
            LightingTimeMs = x.LightingTimeMs
        }).ToList();

        settings.InterfaceType = (int)SelectedInterfaceType;
        settings.ControllerIp = ControllerIp;
        settings.Port = Port;
        settings.NetworkMode = (int)SelectedNetworkMode;
        settings.SubnetMask = SubnetMask;
        settings.Gateway = Gateway;
        settings.DestinationIp = DestinationIp;
        settings.DestinationPort = DestinationPort;

        settings.ComPort = SelectedComPort;
        settings.BaudRate = SelectedBaudRate;
        settings.DataBits = SelectedDataBits;
        settings.Parity = (int)SelectedParity;
        settings.StopBits = (int)SelectedStopBits;
        settings.LineEnding = SelectedLineEndingIndex;
        settings.DtrEnable = DtrEnable;
        settings.RtsEnable = RtsEnable;
        settings.AutoReadOnConnect = AutoReadOnConnect;

        _settingsService.Save();
    }

    // =====================================================================
    // Cleanup
    // =====================================================================

    public void StopDebounceTimer()
    {
        _brightnessDebounceTimer.Stop();
    }
}
