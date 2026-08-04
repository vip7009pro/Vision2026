using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels.PLC;

public sealed class PlcMonitorItem : ObservableObject
{
    public string PlcId { get; set; } = string.Empty;
    public string PlcName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;

    private PlcConnectionState _state;
    public PlcConnectionState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    private double _latencyMs;
    public double LatencyMs
    {
        get => _latencyMs;
        set => SetProperty(ref _latencyMs, value);
    }

    private double _pollingTimeMs;
    public double PollingTimeMs
    {
        get => _pollingTimeMs;
        set => SetProperty(ref _pollingTimeMs, value);
    }

    private long _packetCount;
    public long PacketCount
    {
        get => _packetCount;
        set => SetProperty(ref _packetCount, value);
    }

    private int _reconnectCount;
    public int ReconnectCount
    {
        get => _reconnectCount;
        set => SetProperty(ref _reconnectCount, value);
    }

    private string _lastError = string.Empty;
    public string LastError
    {
        get => _lastError;
        set => SetProperty(ref _lastError, value);
    }
}

public partial class PlcMonitorViewModel : ObservableObject
{
    private readonly IPlcManagerService _plcService;
    private readonly DispatcherTimer _timer;
    private int _lastLogCount = 0;

    [ObservableProperty]
    private bool _isAutoRefreshEnabled = true;

    public ObservableCollection<PlcMonitorItem> MonitorItems { get; } = new();

    public ObservableCollection<PlcLogEntry> Logs { get; } = new();

    public PlcMonitorViewModel(IPlcManagerService plcService)
    {
        _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += (s, e) => RefreshMetrics();
        _timer.Start();

        InitializeItems();
        RefreshLogs();
    }

    partial void OnIsAutoRefreshEnabledChanged(bool value)
    {
        if (value) _timer.Start();
        else _timer.Stop();
    }

    private void InitializeItems()
    {
        MonitorItems.Clear();
        foreach (var plc in _plcService.Plcs)
        {
            MonitorItems.Add(new PlcMonitorItem
            {
                PlcId = plc.Id,
                PlcName = plc.Name,
                Endpoint = plc.DriverType == PlcDriverType.MitsubishiMxComponent ? $"Station: {plc.LogicalStationNumber}" : $"{plc.IPAddress}:{plc.Port}",
                State = plc.State
            });
        }
    }

    private void RefreshMetrics()
    {
        foreach (var item in MonitorItems)
        {
            var plc = _plcService.Plcs.FirstOrDefault(p => string.Equals(p.Id, item.PlcId, StringComparison.OrdinalIgnoreCase));
            if (plc != null)
            {
                item.State = plc.State;
            }

            var metric = _plcService.PollingEngine.Metrics.GetOrAdd(item.PlcId);
            item.LatencyMs = metric.LatencyMs;
            item.PollingTimeMs = metric.PollingTimeMs;
            item.PacketCount = metric.PacketCount;
            item.ReconnectCount = metric.ReconnectCount;
            item.LastError = metric.LastError;
        }

        RefreshLogs();
    }

    private void RefreshLogs()
    {
        var currentLogs = _plcService.Logger.Logs;
        if (currentLogs.Count != _lastLogCount)
        {
            var latest = currentLogs.TakeLast(200).ToList();
            Logs.Clear();
            foreach (var l in latest)
            {
                Logs.Add(l);
            }
            _lastLogCount = currentLogs.Count;
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _plcService.Logger.Clear();
        Logs.Clear();
        _lastLogCount = 0;
    }
}
