using System.Collections.ObjectModel;
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

    public PlcViewModel(GlobalAppSettingsService settingsService, PlcOrchestratorService orchestrator)
    {
        _settingsService = settingsService;
        _orchestrator = orchestrator;

        Logs = new ObservableCollection<string>();

        LoadFromSettings();

        SaveSettingsCommand = new RelayCommand(SaveSettings);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync);

        _orchestrator.Log += (_, msg) => AddLog(msg);
        _orchestrator.StateChanged += (_, _) => RefreshRuntime();

        RefreshRuntime();
    }

    public ObservableCollection<string> Logs { get; }

    public ICommand SaveSettingsCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }

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

    private void AddLog(string msg)
    {
        App.Current?.Dispatcher?.Invoke(() =>
        {
            Logs.Add(msg);
            if (Logs.Count > 2000) Logs.RemoveAt(0);
        });
    }
}

