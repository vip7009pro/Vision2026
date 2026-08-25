using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VisionInspectionApp.Models;

public sealed class PlcModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string _id = Guid.NewGuid().ToString();
    public string Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    private string _name = "PLC1";
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    private PlcDriverType _driverType = PlcDriverType.Mitsubishi;
    public PlcDriverType DriverType
    {
        get => _driverType;
        set
        {
            if (_driverType != value)
            {
                _driverType = value;
                OnPropertyChanged();
            }
        }
    }

    private string _ipAddress = "192.168.3.39";
    public string IPAddress
    {
        get => _ipAddress;
        set { if (_ipAddress != value) { _ipAddress = value; OnPropertyChanged(); } }
    }

    private int _port = 5007;
    public int Port
    {
        get => _port;
        set { if (_port != value) { _port = value; OnPropertyChanged(); } }
    }

    private int _station = 1;
    public int Station
    {
        get => _station;
        set
        {
            if (_station != value)
            {
                _station = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LogicalStationNumber));
            }
        }
    }

    public int LogicalStationNumber
    {
        get => Station;
        set => Station = value;
    }

    private int _rack = 0;
    public int Rack
    {
        get => _rack;
        set { if (_rack != value) { _rack = value; OnPropertyChanged(); } }
    }

    private int _slot = 0;
    public int Slot
    {
        get => _slot;
        set { if (_slot != value) { _slot = value; OnPropertyChanged(); } }
    }

    private int _scanIntervalMs = 100;
    public int ScanIntervalMs
    {
        get => _scanIntervalMs;
        set { if (_scanIntervalMs != value) { _scanIntervalMs = value; OnPropertyChanged(); } }
    }

    private int _reconnectIntervalMs = 5000;
    public int ReconnectIntervalMs
    {
        get => _reconnectIntervalMs;
        set { if (_reconnectIntervalMs != value) { _reconnectIntervalMs = value; OnPropertyChanged(); } }
    }

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } }
    }

    private PlcConnectionState _state = PlcConnectionState.Disconnected;
    public PlcConnectionState State
    {
        get => _state;
        set { if (_state != value) { _state = value; OnPropertyChanged(); } }
    }

    private string _cpuName = string.Empty;
    public string CpuName
    {
        get => _cpuName;
        set { if (_cpuName != value) { _cpuName = value; OnPropertyChanged(); } }
    }

    private bool _isManuallyDisconnected = false;
    public bool IsManuallyDisconnected
    {
        get => _isManuallyDisconnected;
        set { if (_isManuallyDisconnected != value) { _isManuallyDisconnected = value; OnPropertyChanged(); } }
    }
}
