using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels.PLC;

public partial class PlcManagerViewModel : ObservableObject
{
    private readonly IPlcManagerService _plcService;

    [ObservableProperty]
    private PlcModel? _selectedPlc;

    [ObservableProperty]
    private PlcTag? _selectedTag;

    public ObservableCollection<PlcModel> Plcs => _plcService.Plcs;

    public ObservableCollection<PlcTag> Tags => _plcService.Tags;

    public ObservableCollection<PlcTag> FilteredTags { get; } = new();

    public Array DriverTypes => Enum.GetValues(typeof(PlcDriverType));

    public Array DataTypes => Enum.GetValues(typeof(PlcDataType));

    public PlcManagerViewModel(IPlcManagerService plcService)
    {
        _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
        SelectedPlc = Plcs.FirstOrDefault();
        RefreshFilteredTags();
    }

    public bool IsMxComponent => SelectedPlc != null && SelectedPlc.DriverType == PlcDriverType.MitsubishiMxComponent;

    public bool IsMcProtocol => SelectedPlc == null || SelectedPlc.DriverType == PlcDriverType.Mitsubishi;

    partial void OnSelectedPlcChanged(PlcModel? oldValue, PlcModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= SelectedPlc_PropertyChanged;
        }

        if (newValue != null)
        {
            newValue.PropertyChanged += SelectedPlc_PropertyChanged;
        }

        OnPropertyChanged(nameof(IsMxComponent));
        OnPropertyChanged(nameof(IsMcProtocol));
        RefreshFilteredTags();
    }

    private void SelectedPlc_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlcModel.DriverType))
        {
            OnPropertyChanged(nameof(IsMxComponent));
            OnPropertyChanged(nameof(IsMcProtocol));
        }
    }

    private void RefreshFilteredTags()
    {
        FilteredTags.Clear();
        if (SelectedPlc == null) return;

        var list = _plcService.Tags.Where(t => string.Equals(t.PlcId, SelectedPlc.Id, StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(t.PlcId, SelectedPlc.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var tag in list)
        {
            FilteredTags.Add(tag);
        }
        SelectedTag = FilteredTags.FirstOrDefault();
    }

    [RelayCommand]
    private void AddPlc()
    {
        var newPlc = new PlcModel
        {
            Name = $"PLC_{Plcs.Count + 1}",
            IPAddress = "192.168.3.39",
            Port = 5007,
            DriverType = PlcDriverType.MitsubishiMxComponent,
            LogicalStationNumber = 1,
            Enabled = true
        };
        Plcs.Add(newPlc);
        SelectedPlc = newPlc;
        _plcService.SaveGlobalConfig();
        _plcService.StartPollingAsync();
    }

    [RelayCommand]
    private void DeletePlc()
    {
        if (SelectedPlc == null) return;

        var plcToDelete = SelectedPlc;
        var tagsToRemove = _plcService.Tags.Where(t => 
            string.Equals(t.PlcId, plcToDelete.Id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.PlcId, plcToDelete.Name, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var t in tagsToRemove)
        {
            _plcService.Tags.Remove(t);
        }

        _plcService.Plcs.Remove(plcToDelete);
        SelectedPlc = Plcs.FirstOrDefault();
        _plcService.SaveGlobalConfig();
        _plcService.StartPollingAsync();
    }

    [RelayCommand]
    private void AddTag()
    {
        if (SelectedPlc == null) return;

        var newTag = new PlcTag
        {
            PlcId = SelectedPlc.Id,
            Name = $"Tag_{FilteredTags.Count + 1}",
            Address = "X0",
            DataType = PlcDataType.Bool,
            Description = "General Tag"
        };
        _plcService.Tags.Add(newTag);
        FilteredTags.Add(newTag);
        SelectedTag = newTag;
        _plcService.SaveGlobalConfig();
        _plcService.StartPollingAsync();
    }

    [RelayCommand]
    private void DeleteTag()
    {
        if (SelectedTag == null) return;

        var tagToDelete = SelectedTag;
        var realTagInService = _plcService.Tags.FirstOrDefault(t => 
            ReferenceEquals(t, tagToDelete) || 
            (string.Equals(t.Name, tagToDelete.Name, StringComparison.OrdinalIgnoreCase) && 
             string.Equals(t.Address, tagToDelete.Address, StringComparison.OrdinalIgnoreCase)));

        if (realTagInService != null)
        {
            _plcService.Tags.Remove(realTagInService);
        }

        FilteredTags.Remove(tagToDelete);
        SelectedTag = FilteredTags.FirstOrDefault();
        _plcService.SaveGlobalConfig();
        _plcService.StartPollingAsync();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ConnectSelectedPlcAsync()
    {
        if (SelectedPlc == null) return;
        var driver = _plcService.GetDriver(SelectedPlc.Id);
        if (driver != null)
        {
            SelectedPlc.State = PlcConnectionState.Connecting;
            bool ok = await driver.ConnectAsync();
            SelectedPlc.State = ok ? PlcConnectionState.Connected : PlcConnectionState.Error;

            if (ok)
            {
                _plcService.Logger.LogConnect(SelectedPlc.Id, SelectedPlc.Name);
                System.Windows.MessageBox.Show(
                    $"Connected successfully to PLC '{SelectedPlc.Name}'!\nCPU Name: {SelectedPlc.CpuName}",
                    "PLC Connection Successful",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                _plcService.Logger.LogDisconnect(SelectedPlc.Id, SelectedPlc.Name);
                string errDetail = string.IsNullOrWhiteSpace(SelectedPlc.CpuName) ? "Connection failed" : SelectedPlc.CpuName;
                System.Windows.MessageBox.Show(
                    $"Failed to connect to PLC '{SelectedPlc.Name}' (Station {SelectedPlc.LogicalStationNumber}).\nDetail: {errDetail}\n\nPlease check:\n1. Mitsubishi MX Component Communication Utility station setup.\n2. Station Number ({SelectedPlc.LogicalStationNumber}) matches Communication Utility.\n3. Physical PLC connection and cable power.",
                    "PLC Connection Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DisconnectSelectedPlcAsync()
    {
        if (SelectedPlc == null) return;
        var driver = _plcService.GetDriver(SelectedPlc.Id);
        if (driver != null)
        {
            await driver.DisconnectAsync();
            SelectedPlc.State = PlcConnectionState.Disconnected;
            _plcService.Logger.LogDisconnect(SelectedPlc.Id, SelectedPlc.Name);
        }
    }
}
