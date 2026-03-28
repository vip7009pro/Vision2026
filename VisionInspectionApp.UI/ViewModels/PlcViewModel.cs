using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.UI.Services;
using VisionInspectionApp.UI.Services.Plc;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class PlcViewModel : ObservableObject
{
    private readonly GlobalAppSettingsService _settingsService;
    private readonly PlcOrchestratorService _orchestrator;
    private readonly IPlcClient _plc;

    public PlcViewModel(
        GlobalAppSettingsService settingsService,
        PlcOrchestratorService orchestrator,
        IPlcClient plcClient)
    {
        _settingsService = settingsService;
        _orchestrator = orchestrator;
        _plc = plcClient;

        Logs = new ObservableCollection<string>();

        LoadFromSettings();

        SaveSettingsCommand = new RelayCommand(SaveSettings);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);
        ConnectTestOnlyCommand = new AsyncRelayCommand(ConnectTestOnlyAsync);

        TestReadBitCommand = new AsyncRelayCommand(TestReadBitAsync);
        TestWriteBit0Command = new AsyncRelayCommand(TestWriteBit0Async);
        TestWriteBit1Command = new AsyncRelayCommand(TestWriteBit1Async);
        TestReadWordCommand = new AsyncRelayCommand(TestReadWordAsync);
        TestWriteWordCommand = new AsyncRelayCommand(TestWriteWordAsync);
        ReadMappedSnapshotCommand = new AsyncRelayCommand(ReadMappedSnapshotAsync);

        SimTriggerOnCommand = new AsyncRelayCommand(SimTriggerOnAsync);
        SimTriggerOffCommand = new AsyncRelayCommand(SimTriggerOffAsync);
        SimClearErrorOnCommand = new AsyncRelayCommand(SimClearErrorOnAsync);
        SimClearErrorOffCommand = new AsyncRelayCommand(SimClearErrorOffAsync);

        SimBusyOnCommand = new AsyncRelayCommand(SimBusyOnAsync);
        SimBusyOffCommand = new AsyncRelayCommand(SimBusyOffAsync);
        SimDonePulseCommand = new AsyncRelayCommand(SimDonePulseAsync);
        SimWriteResultCodeCommand = new AsyncRelayCommand(SimWriteResultCodeAsync);
        SimWriteAppStateCommand = new AsyncRelayCommand(SimWriteAppStateAsync);

        _orchestrator.Log += (_, msg) => AddLog(msg);
        _orchestrator.StateChanged += (_, _) => RefreshRuntime();

        RefreshRuntime();
    }

    public ObservableCollection<string> Logs { get; }

    /// <summary>Flat text for read-only TextBox (Ctrl+A, Ctrl+C).</summary>
    [ObservableProperty]
    private string _logText = string.Empty;

    public ICommand SaveSettingsCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ConnectTestOnlyCommand { get; }

    public ICommand TestReadBitCommand { get; }
    public ICommand TestWriteBit0Command { get; }
    public ICommand TestWriteBit1Command { get; }
    public ICommand TestReadWordCommand { get; }
    public ICommand TestWriteWordCommand { get; }
    public ICommand ReadMappedSnapshotCommand { get; }

    public ICommand SimTriggerOnCommand { get; }
    public ICommand SimTriggerOffCommand { get; }
    public ICommand SimClearErrorOnCommand { get; }
    public ICommand SimClearErrorOffCommand { get; }

    public ICommand SimBusyOnCommand { get; }
    public ICommand SimBusyOffCommand { get; }
    public ICommand SimDonePulseCommand { get; }
    public ICommand SimWriteResultCodeCommand { get; }
    public ICommand SimWriteAppStateCommand { get; }

    [ObservableProperty] private int _logicalStationNumber;
    [ObservableProperty] private string _productCode = "ProductA";
    [ObservableProperty] private int _pollIntervalMs;
    [ObservableProperty] private int _donePulseMs;

    [ObservableProperty] private string _triggerBitDevice = "M100";
    [ObservableProperty] private string _clearErrorBitDevice = "M101";
    [ObservableProperty] private string _busyBitDevice = "M110";
    [ObservableProperty] private string _doneBitDevice = "M111";
    [ObservableProperty] private string _resultCodeWordDevice = "D200";
    [ObservableProperty] private string _appStateWordDevice = "D210";

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private PlcAppState _appState;
    [ObservableProperty] private string _statusLine = "Idle";
    [ObservableProperty] private string? _lastError;

    [ObservableProperty] private string _testDeviceAddress = "M100";
    [ObservableProperty] private string _testWordValueText = "0";
    [ObservableProperty] private string _testLastReadResult = "—";

    [ObservableProperty] private string _simResultCodeText = "1";
    [ObservableProperty] private string _simAppStateText = "1";

    private void LoadFromSettings()
    {
        var s = _settingsService.Settings.Plc;
        LogicalStationNumber = s.LogicalStationNumber;
        ProductCode = s.ProductCode;
        PollIntervalMs = s.PollIntervalMs;
        DonePulseMs = s.DonePulseMs;

        TriggerBitDevice = s.TriggerBitDevice;
        ClearErrorBitDevice = s.ClearErrorBitDevice;
        BusyBitDevice = s.BusyBitDevice;
        DoneBitDevice = s.DoneBitDevice;
        ResultCodeWordDevice = s.ResultCodeWordDevice;
        AppStateWordDevice = s.AppStateWordDevice;
    }

    private void SaveSettings()
    {
        var s = _settingsService.Settings.Plc;
        s.LogicalStationNumber = LogicalStationNumber;
        s.ProductCode = ProductCode;
        s.PollIntervalMs = PollIntervalMs;
        s.DonePulseMs = DonePulseMs;

        s.TriggerBitDevice = TriggerBitDevice;
        s.ClearErrorBitDevice = ClearErrorBitDevice;
        s.BusyBitDevice = BusyBitDevice;
        s.DoneBitDevice = DoneBitDevice;
        s.ResultCodeWordDevice = ResultCodeWordDevice;
        s.AppStateWordDevice = AppStateWordDevice;

        _settingsService.Save();
        AddLog("Settings saved.");
    }

    private async Task ConnectAsync()
    {
        SaveSettings();
        try
        {
            await _orchestrator.ConnectAndStartAsync();
            RefreshRuntime();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AddLog($"Connect error: {ex.Message}");
            RefreshRuntime();
        }
    }

    private async Task ConnectTestOnlyAsync()
    {
        SaveSettings();
        try
        {
            await _orchestrator.ConnectPlcForTestAsync();
            RefreshRuntime();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AddLog($"Connect (test) error: {ex.Message}");
            RefreshRuntime();
        }
    }

    private async Task DisconnectAsync()
    {
        try
        {
            await _orchestrator.StopAndDisconnectAsync();
            RefreshRuntime();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            AddLog($"Disconnect error: {ex.Message}");
            RefreshRuntime();
        }
    }

    private void RefreshRuntime()
    {
        IsConnected = _orchestrator.IsConnected;
        IsRunning = _orchestrator.IsRunning;
        AppState = _orchestrator.CurrentState;
        LastError = _orchestrator.LastError;
        StatusLine = $"{(IsConnected ? "Connected" : "Disconnected")} | {(IsRunning ? "Polling" : "Stopped")} | State={AppState}";
    }

    private bool EnsurePlcConnected(string actionLabel)
    {
        if (!_plc.IsConnected)
        {
            AddLog($"{actionLabel}: PLC not connected. Use \"Connect (test only)\" or \"Connect + Start\".");
            return false;
        }

        return true;
    }

    private async Task TestReadBitAsync()
    {
        if (!EnsurePlcConnected("Read bit")) return;
        var dev = TestDeviceAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(dev))
        {
            AddLog("Read bit: enter device address.");
            return;
        }

        try
        {
            var v = await _plc.ReadBitAsync(dev).ConfigureAwait(true);
            TestLastReadResult = v ? "1 (ON)" : "0 (OFF)";
            AddLog($"ReadBit {dev} = {TestLastReadResult}");
        }
        catch (Exception ex)
        {
            AddLog($"ReadBit {dev} error: {ex.Message}");
        }
    }

    private async Task TestWriteBit0Async()
    {
        if (!EnsurePlcConnected("Write bit 0")) return;
        var dev = TestDeviceAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(dev))
        {
            AddLog("Write bit: enter device address.");
            return;
        }

        try
        {
            await _plc.WriteBitAsync(dev, false).ConfigureAwait(true);
            AddLog($"WriteBit {dev} = 0");
        }
        catch (Exception ex)
        {
            AddLog($"WriteBit {dev} error: {ex.Message}");
        }
    }

    private async Task TestWriteBit1Async()
    {
        if (!EnsurePlcConnected("Write bit 1")) return;
        var dev = TestDeviceAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(dev))
        {
            AddLog("Write bit: enter device address.");
            return;
        }

        try
        {
            await _plc.WriteBitAsync(dev, true).ConfigureAwait(true);
            AddLog($"WriteBit {dev} = 1");
        }
        catch (Exception ex)
        {
            AddLog($"WriteBit {dev} error: {ex.Message}");
        }
    }

    private async Task TestReadWordAsync()
    {
        if (!EnsurePlcConnected("Read word")) return;
        var dev = TestDeviceAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(dev))
        {
            AddLog("Read word: enter device address.");
            return;
        }

        try
        {
            var w = await _plc.ReadWordAsync(dev).ConfigureAwait(true);
            TestLastReadResult = w.ToString(CultureInfo.InvariantCulture);
            AddLog($"ReadWord {dev} = {w} (decimal)");
        }
        catch (Exception ex)
        {
            AddLog($"ReadWord {dev} error: {ex.Message}");
        }
    }

    private async Task TestWriteWordAsync()
    {
        if (!EnsurePlcConnected("Write word")) return;
        var dev = TestDeviceAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(dev))
        {
            AddLog("Write word: enter device address.");
            return;
        }

        if (!short.TryParse(TestWordValueText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            AddLog("Write word: invalid value (use -32768..32767).");
            return;
        }

        try
        {
            await _plc.WriteWordAsync(dev, value).ConfigureAwait(true);
            AddLog($"WriteWord {dev} = {value}");
        }
        catch (Exception ex)
        {
            AddLog($"WriteWord {dev} error: {ex.Message}");
        }
    }

    private async Task ReadMappedSnapshotAsync()
    {
        if (!EnsurePlcConnected("Mapped snapshot")) return;

        try
        {
            var t = await _plc.ReadBitAsync(TriggerBitDevice).ConfigureAwait(true);
            var c = await _plc.ReadBitAsync(ClearErrorBitDevice).ConfigureAwait(true);
            var b = await _plc.ReadBitAsync(BusyBitDevice).ConfigureAwait(true);
            var d = await _plc.ReadBitAsync(DoneBitDevice).ConfigureAwait(true);
            var r = await _plc.ReadWordAsync(ResultCodeWordDevice).ConfigureAwait(true);
            var a = await _plc.ReadWordAsync(AppStateWordDevice).ConfigureAwait(true);

            AddLog($"Mapped: Trig={t} ClearErr={c} Busy={b} Done={d} | ResultW={r} AppStateW={a}");
            TestLastReadResult = $"R={r} A={a}";
        }
        catch (Exception ex)
        {
            AddLog($"Mapped snapshot error: {ex.Message}");
        }
    }

    private async Task SimTriggerOnAsync()
    {
        if (!EnsurePlcConnected("Sim Trigger ON")) return;
        try
        {
            await _plc.WriteBitAsync(TriggerBitDevice, true).ConfigureAwait(true);
            AddLog($"Sim PLC: Trigger {TriggerBitDevice} = ON");
        }
        catch (Exception ex)
        {
            AddLog($"Sim Trigger ON error: {ex.Message}");
        }
    }

    private async Task SimTriggerOffAsync()
    {
        if (!EnsurePlcConnected("Sim Trigger OFF")) return;
        try
        {
            await _plc.WriteBitAsync(TriggerBitDevice, false).ConfigureAwait(true);
            AddLog($"Sim PLC: Trigger {TriggerBitDevice} = OFF");
        }
        catch (Exception ex)
        {
            AddLog($"Sim Trigger OFF error: {ex.Message}");
        }
    }

    private async Task SimClearErrorOnAsync()
    {
        if (!EnsurePlcConnected("Sim ClearError ON")) return;
        try
        {
            await _plc.WriteBitAsync(ClearErrorBitDevice, true).ConfigureAwait(true);
            AddLog($"Sim PLC: ClearError {ClearErrorBitDevice} = ON");
        }
        catch (Exception ex)
        {
            AddLog($"Sim ClearError ON error: {ex.Message}");
        }
    }

    private async Task SimClearErrorOffAsync()
    {
        if (!EnsurePlcConnected("Sim ClearError OFF")) return;
        try
        {
            await _plc.WriteBitAsync(ClearErrorBitDevice, false).ConfigureAwait(true);
            AddLog($"Sim PLC: ClearError {ClearErrorBitDevice} = OFF");
        }
        catch (Exception ex)
        {
            AddLog($"Sim ClearError OFF error: {ex.Message}");
        }
    }

    private async Task SimBusyOnAsync()
    {
        if (!EnsurePlcConnected("Sim Busy ON")) return;
        try
        {
            await _plc.WriteBitAsync(BusyBitDevice, true).ConfigureAwait(true);
            AddLog($"Sim Vision→PLC: Busy {BusyBitDevice} = ON");
        }
        catch (Exception ex)
        {
            AddLog($"Sim Busy ON error: {ex.Message}");
        }
    }

    private async Task SimBusyOffAsync()
    {
        if (!EnsurePlcConnected("Sim Busy OFF")) return;
        try
        {
            await _plc.WriteBitAsync(BusyBitDevice, false).ConfigureAwait(true);
            AddLog($"Sim Vision→PLC: Busy {BusyBitDevice} = OFF");
        }
        catch (Exception ex)
        {
            AddLog($"Sim Busy OFF error: {ex.Message}");
        }
    }

    private async Task SimDonePulseAsync()
    {
        if (!EnsurePlcConnected("Sim Done pulse")) return;
        var ms = Math.Clamp(_settingsService.Settings.Plc.DonePulseMs, 1, 2000);
        try
        {
            await _plc.WriteBitAsync(DoneBitDevice, true).ConfigureAwait(true);
            await Task.Delay(ms).ConfigureAwait(true);
            await _plc.WriteBitAsync(DoneBitDevice, false).ConfigureAwait(true);
            AddLog($"Sim Vision→PLC: Done {DoneBitDevice} pulse {ms}ms");
        }
        catch (Exception ex)
        {
            AddLog($"Sim Done pulse error: {ex.Message}");
        }
    }

    private async Task SimWriteResultCodeAsync()
    {
        if (!EnsurePlcConnected("Sim ResultCode")) return;
        if (!short.TryParse(SimResultCodeText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            AddLog("Result code: invalid value.");
            return;
        }

        try
        {
            await _plc.WriteWordAsync(ResultCodeWordDevice, value).ConfigureAwait(true);
            AddLog($"Sim Vision→PLC: {ResultCodeWordDevice} = {value}");
        }
        catch (Exception ex)
        {
            AddLog($"Sim ResultCode error: {ex.Message}");
        }
    }

    private async Task SimWriteAppStateAsync()
    {
        if (!EnsurePlcConnected("Sim AppState")) return;
        if (!short.TryParse(SimAppStateText?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            AddLog("App state: invalid value.");
            return;
        }

        try
        {
            await _plc.WriteWordAsync(AppStateWordDevice, value).ConfigureAwait(true);
            AddLog($"Sim Vision→PLC: {AppStateWordDevice} = {value}");
        }
        catch (Exception ex)
        {
            AddLog($"Sim AppState error: {ex.Message}");
        }
    }

    private void AddLog(string msg)
    {
        App.Current?.Dispatcher?.Invoke(() =>
        {
            Logs.Add(msg);
            if (Logs.Count > 2000) Logs.RemoveAt(0);
            LogText = string.Join(Environment.NewLine, Logs);
        });
    }
}
