using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isDiagnosing;

    [ObservableProperty]
    private string _diagnosticSummary = string.Empty;

    [ObservableProperty]
    private string _diagnosticReportText = string.Empty;

    [ObservableProperty]
    private string _lastDiagnosticLogPath = string.Empty;

    [ObservableProperty]
    private bool _showDiagnosticPanel;

    public ObservableCollection<PlcModel> Plcs => _plcService.Plcs;

    public ObservableCollection<PlcTag> Tags => _plcService.Tags;

    public ObservableCollection<PlcTag> FilteredTags { get; } = new();

    public Array DriverTypes => Enum.GetValues(typeof(PlcDriverType));

    public Array DataTypes => Enum.GetValues(typeof(PlcDataType));

    public PlcIndustrialConfig IndustrialConfig
    {
        get => _plcService.IndustrialConfig;
        set
        {
            _plcService.IndustrialConfig = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Handshake));
            OnPropertyChanged(nameof(Heartbeat));
            OnPropertyChanged(nameof(Motion));
            OnPropertyChanged(nameof(ShiftRegister));
        }
    }

    public IndustrialHandshakeConfig Handshake => IndustrialConfig.Handshake;
    public PlcHeartbeatConfig Heartbeat => IndustrialConfig.Heartbeat;
    public PlcMotionConfig Motion => IndustrialConfig.Motion;
    public PlcShiftRegisterConfig ShiftRegister => IndustrialConfig.ShiftRegister;

    public ObservableCollection<string> AvailableTagNames { get; } = new();
    public ObservableCollection<string> AvailablePlcNames { get; } = new();

    public void RefreshAvailableTagsAndPlcs()
    {
        // 1. Refresh AvailablePlcNames
        AvailablePlcNames.Clear();
        if (_plcService.Plcs.Count > 0)
        {
            foreach (var p in _plcService.Plcs)
            {
                string name = !string.IsNullOrWhiteSpace(p.Name) ? p.Name : p.Id;
                if (!string.IsNullOrWhiteSpace(name) && !AvailablePlcNames.Contains(name))
                {
                    AvailablePlcNames.Add(name);
                }
            }
        }

        // Bổ sung các ID PLC mặc định
        var defaultPlcs = new[] { "PLC1", "PLC2", "PLC_MAIN", "PLC_01" };
        foreach (var pName in defaultPlcs)
        {
            if (!AvailablePlcNames.Contains(pName))
            {
                AvailablePlcNames.Add(pName);
            }
        }

        // 2. Refresh AvailableTagNames
        AvailableTagNames.Clear();

        // Standard Common Addresses (Bit & Word)
        var commonAddresses = new[]
        {
            "X0", "X1", "X2", "X3", "X4", "X5", "X6", "X7", "X10", "X11",
            "Y0", "Y1", "Y2", "Y3", "Y4", "Y5", "Y6", "Y7", "Y10", "Y11",
            "M0", "M1", "M2", "M10", "M100",
            "D0", "D10", "D100", "D200", "D1000", "D1002", "D2000",
            "MW100", "MW102", "MW200"
        };
        foreach (var addr in commonAddresses)
        {
            if (!AvailableTagNames.Contains(addr)) AvailableTagNames.Add(addr);
        }

        // Standard Industrial Tags
        var standardIndustrialTags = new[]
        {
            "Y0_VisionHeartbeat", "X0_PlcHeartbeat", "Y10_VisionFault",
            "Y1_VisionReady", "Y2_VisionBusy", "Y3_VisionDone", "Y4_VisionPass", "Y5_VisionNG", "X1_PlcAck",
            "D1000_EncoderPulses", "D1002_LineSpeed",
            "Y0_RejectPiston", "Y1_RejectMarker"
        };
        foreach (var tag in standardIndustrialTags)
        {
            if (!AvailableTagNames.Contains(tag)) AvailableTagNames.Add(tag);
        }

        // Tags from PLC Manager Service (Name and Address)
        if (_plcService.Tags.Count > 0)
        {
            foreach (var t in _plcService.Tags)
            {
                if (!string.IsNullOrWhiteSpace(t.Name) && !AvailableTagNames.Contains(t.Name)) AvailableTagNames.Add(t.Name);
                if (!string.IsNullOrWhiteSpace(t.Address) && !AvailableTagNames.Contains(t.Address)) AvailableTagNames.Add(t.Address);
            }
        }

        // Tags from current Industrial Config
        var configTags = new[]
        {
            Handshake?.ReadyTagName, Handshake?.BusyTagName, Handshake?.DoneTagName, Handshake?.PassTagName, Handshake?.NgTagName, Handshake?.PlcAckTagName,
            Heartbeat?.VisionHeartbeatTagName, Heartbeat?.PlcHeartbeatTagName, Heartbeat?.EmergencyStopTagName,
            Motion?.EncoderTagName, Motion?.SpeedTagName,
            ShiftRegister?.RejectTagName
        };
        foreach (var ct in configTags)
        {
            if (!string.IsNullOrWhiteSpace(ct) && !AvailableTagNames.Contains(ct))
            {
                AvailableTagNames.Add(ct);
            }
        }
    }

    private bool _isRefreshingFilteredTags;

    public PlcManagerViewModel(IPlcManagerService plcService)
    {
        _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
        SelectedPlc = Plcs.FirstOrDefault();
        
        RefreshAvailableTagsAndPlcs();

        FilteredTags.CollectionChanged += FilteredTags_CollectionChanged;
        _plcService.Tags.CollectionChanged += (s, e) =>
        {
            RefreshAvailableTagsAndPlcs();
        };
        _plcService.Plcs.CollectionChanged += (s, e) =>
        {
            RefreshAvailableTagsAndPlcs();
        };

        RefreshFilteredTags();
    }

    private void FilteredTags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isRefreshingFilteredTags) return;

        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (PlcTag tag in e.OldItems)
            {
                var matchingInService = _plcService.Tags.Where(t => 
                    ReferenceEquals(t, tag) || 
                    string.Equals(t.Id, tag.Id, StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase) && 
                     string.Equals(t.PlcId, tag.PlcId, StringComparison.OrdinalIgnoreCase))).ToList();

                foreach (var t in matchingInService)
                {
                    _plcService.Tags.Remove(t);
                }
            }
            _plcService.SaveGlobalConfig();
            OnPropertyChanged(nameof(AvailableTagNames));
        }
    }

    public bool IsMxComponent => SelectedPlc != null && SelectedPlc.DriverType == PlcDriverType.MitsubishiMxComponent;

    public bool IsMcProtocol => SelectedPlc == null || SelectedPlc.DriverType == PlcDriverType.Mitsubishi;

    public bool CanConnectSelectedPlc => SelectedPlc != null 
        && SelectedPlc.Enabled 
        && SelectedPlc.State != PlcConnectionState.Connected 
        && SelectedPlc.State != PlcConnectionState.Connecting;

    public bool CanDisconnectSelectedPlc => SelectedPlc != null 
        && (SelectedPlc.State == PlcConnectionState.Connected || SelectedPlc.State == PlcConnectionState.Connecting);

    public void UpdateConnectionCommandStates()
    {
        OnPropertyChanged(nameof(CanConnectSelectedPlc));
        OnPropertyChanged(nameof(CanDisconnectSelectedPlc));
        ConnectSelectedPlcCommand.NotifyCanExecuteChanged();
        DisconnectSelectedPlcCommand.NotifyCanExecuteChanged();
    }

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
        UpdateConnectionCommandStates();
        RefreshFilteredTags();
    }

    private void SelectedPlc_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlcModel.DriverType))
        {
            OnPropertyChanged(nameof(IsMxComponent));
            OnPropertyChanged(nameof(IsMcProtocol));
        }
        if (e.PropertyName == nameof(PlcModel.State) || e.PropertyName == nameof(PlcModel.Enabled) || e.PropertyName == nameof(PlcModel.IsManuallyDisconnected))
        {
            UpdateConnectionCommandStates();
        }
    }

    private void RefreshFilteredTags()
    {
        _isRefreshingFilteredTags = true;
        try
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
        finally
        {
            _isRefreshingFilteredTags = false;
        }
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
            Enabled = true,
            State = PlcConnectionState.Disconnected,
            IsManuallyDisconnected = false
        };
        Plcs.Add(newPlc);
        SelectedPlc = newPlc;
        _plcService.SaveGlobalConfig();
        if (_plcService.IsPollingActive)
        {
            _plcService.StartPollingAsync();
        }
        RefreshAvailableTagsAndPlcs();
        UpdateConnectionCommandStates();
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

        Plcs.Remove(plcToDelete);
        SelectedPlc = Plcs.FirstOrDefault();
        _plcService.SaveGlobalConfig();
        if (_plcService.IsPollingActive)
        {
            _plcService.StartPollingAsync();
        }
        RefreshAvailableTagsAndPlcs();
        UpdateConnectionCommandStates();
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
        RefreshAvailableTagsAndPlcs();
        if (_plcService.IsPollingActive)
        {
            _plcService.StartPollingAsync();
        }
    }

    [RelayCommand]
    private void DeleteTag()
    {
        if (SelectedTag == null) return;

        var tagToDelete = SelectedTag;
        
        var realTagsInService = _plcService.Tags.Where(t => 
            ReferenceEquals(t, tagToDelete) ||
            string.Equals(t.Id, tagToDelete.Id, StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(t.Name, tagToDelete.Name, StringComparison.OrdinalIgnoreCase) && 
             string.Equals(t.PlcId, tagToDelete.PlcId, StringComparison.OrdinalIgnoreCase))).ToList();

        foreach (var t in realTagsInService)
        {
            _plcService.Tags.Remove(t);
        }

        FilteredTags.Remove(tagToDelete);
        SelectedTag = FilteredTags.FirstOrDefault();
        _plcService.SaveGlobalConfig();
        RefreshAvailableTagsAndPlcs();
        if (_plcService.IsPollingActive)
        {
            _plcService.StartPollingAsync();
        }
    }

    [RelayCommand]
    private void SaveConfig()
    {
        _plcService.SaveGlobalConfig();
        System.Windows.MessageBox.Show("Toàn bộ Cấu hình Kết nối PLC, Danh bạ Tags và Thông số Công nghiệp (Handshake, Heartbeat, Motion, Shift Register) đã được lưu thành công!", "Lưu Cấu Hình PLC", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand(CanExecute = nameof(CanConnectSelectedPlc))]
    private async System.Threading.Tasks.Task ConnectSelectedPlcAsync()
    {
        if (SelectedPlc == null) return;
        SelectedPlc.IsManuallyDisconnected = false;
        var driver = _plcService.GetDriver(SelectedPlc.Id);
        if (driver != null)
        {
            SelectedPlc.State = PlcConnectionState.Connecting;
            UpdateConnectionCommandStates();

            bool ok = false;
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(6000);
                ok = await driver.ConnectAsync(cts.Token);
            }
            catch (Exception ex)
            {
                SelectedPlc.CpuName = ex.Message;
                ok = false;
            }

            SelectedPlc.State = ok ? PlcConnectionState.Connected : PlcConnectionState.Error;
            UpdateConnectionCommandStates();

            if (ok)
            {
                _plcService.Logger.LogConnect(SelectedPlc.Id, SelectedPlc.Name);
                _plcService.AcquirePollingLock("PlcManager");
                System.Windows.MessageBox.Show(
                    $"Connected successfully to PLC '{SelectedPlc.Name}'!\nCPU Name: {SelectedPlc.CpuName}",
                    "PLC Connection Successful",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                _plcService.ReleasePollingLock("PlcManager");
                _plcService.ReleasePollingLock("PlcManagerWindow");
                _plcService.Logger.LogDisconnect(SelectedPlc.Id, SelectedPlc.Name);
                string errDetail = string.IsNullOrWhiteSpace(SelectedPlc.CpuName) ? "Connection failed or timed out" : SelectedPlc.CpuName;
                System.Windows.MessageBox.Show(
                    $"Failed to connect to PLC '{SelectedPlc.Name}' (Station {SelectedPlc.LogicalStationNumber}).\nDetail: {errDetail}\n\nPlease check:\n1. Mitsubishi MX Component Communication Utility station setup.\n2. Station Number ({SelectedPlc.LogicalStationNumber}) matches Communication Utility.\n3. Physical PLC connection and cable power.",
                    "PLC Connection Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectSelectedPlc))]
    private async System.Threading.Tasks.Task DisconnectSelectedPlcAsync()
    {
        if (SelectedPlc == null) return;
        SelectedPlc.IsManuallyDisconnected = true;
        _plcService.ReleasePollingLock("PlcManager");
        _plcService.ReleasePollingLock("PlcManagerWindow");
        _plcService.ReleasePollingLock("AutoStartup");
        
        SelectedPlc.State = PlcConnectionState.Disconnected;
        SelectedPlc.CpuName = string.Empty;
        UpdateConnectionCommandStates();

        var driver = _plcService.GetDriver(SelectedPlc.Id);
        if (driver != null)
        {
            try
            {
                await driver.DisconnectAsync();
            }
            catch { }
            _plcService.Logger.LogDisconnect(SelectedPlc.Id, SelectedPlc.Name);
        }
        UpdateConnectionCommandStates();
    }

    [RelayCommand]
    private void ImportTags()
    {
        if (SelectedPlc == null)
        {
            System.Windows.MessageBox.Show("Vui lòng chọn một PLC trước khi nạp danh bạ biến!", "Chưa chọn PLC", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Import Danh Bạ Biến PLC cho '{SelectedPlc.Name}' (GX Works / Standard CSV)",
            Filter = "Tất cả tệp CSV PLC (*.csv;*.txt)|*.csv;*.txt|Mitsubishi GX Works 3 Global Labels (*.csv)|*.csv|Mitsubishi GX Works Device Comments (*.csv)|*.csv|Tất cả các tệp (*.*)|*.*",
            FilterIndex = 1
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                string csvContent = System.IO.File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
                int count = ImportTagsFromCsvText(csvContent, overwriteExisting: true);

                if (count > 0)
                {
                    var detectedFormat = PlcTagCsvService.DetectCsvFormat(csvContent);
                    string formatName = detectedFormat switch
                    {
                        PlcTagCsvFormat.GxWorks3GlobalLabels => "Mitsubishi GX Works 3 Global Labels",
                        PlcTagCsvFormat.GxWorksDeviceComments => "Mitsubishi GX Works Device Comments",
                        _ => "Standard PLC Tags"
                    };

                    System.Windows.MessageBox.Show(
                        $"Đã nạp thành công {count} Tags vào PLC '{SelectedPlc.Name}'!\nĐịnh dạng nhận diện: {formatName}\nNguồn: {System.IO.Path.GetFileName(dlg.FileName)}",
                        "Import Tags Thành Công",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        "Không tìm thấy biến (Tag) hợp lệ nào trong tệp CSV đã chọn.\nVui lòng kiểm tra lại cấu trúc file!",
                        "Không Có Dữ Liệu Tag",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Có lỗi xảy ra trong quá trình đọc file CSV:\n{ex.Message}",
                    "Lỗi Import CSV",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Nạp danh sách Tags từ chuỗi CSV vào SelectedPlc hiện tại
    /// </summary>
    public int ImportTagsFromCsvText(string csvContent, bool overwriteExisting = true)
    {
        if (SelectedPlc == null || string.IsNullOrWhiteSpace(csvContent)) return 0;

        var importedTags = PlcTagCsvService.ParseCsv(csvContent, SelectedPlc.Id, PlcTagCsvFormat.AutoDetect);
        if (importedTags.Count == 0) return 0;

        int addedOrUpdatedCount = 0;

        foreach (var tag in importedTags)
        {
            tag.PlcId = SelectedPlc.Id;

            // Tìm tag đã tồn tại có cùng Address hoặc cùng Name trong PLC này
            var existing = _plcService.Tags.FirstOrDefault(t => 
                (string.Equals(t.PlcId, SelectedPlc.Id, StringComparison.OrdinalIgnoreCase) || 
                 string.Equals(t.PlcId, SelectedPlc.Name, StringComparison.OrdinalIgnoreCase)) &&
                (string.Equals(t.Address, tag.Address, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                if (overwriteExisting)
                {
                    existing.Name = tag.Name;
                    existing.Address = tag.Address;
                    existing.DataType = tag.DataType;
                    existing.Description = tag.Description;
                    existing.ReadOnly = tag.ReadOnly;
                    addedOrUpdatedCount++;
                }
            }
            else
            {
                _plcService.Tags.Add(tag);
                addedOrUpdatedCount++;
            }
        }

        _plcService.SaveGlobalConfig();
        RefreshFilteredTags();
        RefreshAvailableTagsAndPlcs();

        if (_plcService.IsPollingActive)
        {
            _plcService.StartPollingAsync();
        }

        return addedOrUpdatedCount;
    }

    [RelayCommand]
    private void ExportTags()
    {
        if (SelectedPlc == null)
        {
            System.Windows.MessageBox.Show("Vui lòng chọn một PLC trước khi xuất danh bạ biến!", "Chưa chọn PLC", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (FilteredTags.Count == 0)
        {
            System.Windows.MessageBox.Show($"PLC '{SelectedPlc.Name}' hiện chưa có biến (Tag) nào để xuất!", "Danh bạ biến rỗng", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Export Danh Bạ Biến PLC '{SelectedPlc.Name}' Ra Tệp CSV",
            Filter = "Standard PLC Tags CSV (*.csv)|*.csv|Mitsubishi GX Works 3 Global Labels (*.csv)|*.csv|Mitsubishi GX Works Device Comments (*.csv)|*.csv",
            FileName = $"{SelectedPlc.Name}_Tags.csv",
            FilterIndex = 1
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                string csvOutput = dlg.FilterIndex switch
                {
                    2 => PlcTagCsvService.ExportToGxWorksGlobalLabelsCsv(FilteredTags),
                    3 => PlcTagCsvService.ExportToGxWorksDeviceCommentsCsv(FilteredTags),
                    _ => PlcTagCsvService.ExportToStandardCsv(FilteredTags)
                };

                System.IO.File.WriteAllText(dlg.FileName, csvOutput, System.Text.Encoding.UTF8);

                System.Windows.MessageBox.Show(
                    $"Đã xuất thành công {FilteredTags.Count} tags ra tệp:\n{dlg.FileName}",
                    "Export Tags Thành Công",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Có lỗi xảy ra khi xuất file CSV:\n{ex.Message}",
                    "Lỗi Export CSV",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }

    #region Diagnostic Network & Packet Probe Commands

    [RelayCommand]
    private async System.Threading.Tasks.Task DiagnoseSelectedPlcAsync()
    {
        if (SelectedPlc == null)
        {
            System.Windows.MessageBox.Show("Vui lòng chọn một PLC để thực hiện chẩn đoán!", "Chưa Chọn PLC", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        SelectedTabIndex = 4;
        IsDiagnosing = true;
        ShowDiagnosticPanel = true;
        DiagnosticSummary = $"Đang chạy chẩn đoán tới {SelectedPlc.IPAddress}:{SelectedPlc.Port}...";
        DiagnosticReportText = "Đang kiểm tra kết nối mạng ICMP Ping, TCP Socket và gửi gói tin thăm dò MC Protocol 3E Binary. Vui lòng đợi...";

        try
        {
            var report = await PlcDiagnosticService.RunDiagnosticAsync(
                SelectedPlc.IPAddress,
                SelectedPlc.Port,
                SelectedPlc.DriverType,
                SelectedPlc.LogicalStationNumber);

            DiagnosticReportText = report.FullReportText;
            LastDiagnosticLogPath = report.SavedLogFilePath ?? string.Empty;

            if (report.McProtocolSuccess)
            {
                DiagnosticSummary = $"✅ MC Protocol 3E THÀNH CÔNG! CPU: {report.CpuModelDetected} (Ping: {report.PingRoundtripMs}ms)";
            }
            else if (report.SocketConnected)
            {
                DiagnosticSummary = $"⚠️ Socket TCP mở OK ({report.SocketConnectMs}ms) nhưng PLC không phản hồi 3E (Có thể do Port {SelectedPlc.Port} là cổng MELSOFT hoặc HMI Weintek chiếm dụng)";
            }
            else if (report.PingSuccess)
            {
                DiagnosticSummary = $"❌ Cổng TCP {SelectedPlc.Port} bị ĐÓNG (Ping IP {report.PingRoundtripMs}ms OK nhưng Port không mở)";
            }
            else
            {
                DiagnosticSummary = $"❌ Không tìm thấy thiết bị tại IP {SelectedPlc.IPAddress} (Ping & Socket đều thất bại)";
            }
        }
        catch (Exception ex)
        {
            DiagnosticSummary = $"❌ Lỗi thực thi chẩn đoán: {ex.Message}";
            DiagnosticReportText = $"Lỗi ngoại lệ: {ex}\nStackTrace:\n{ex.StackTrace}";
        }
        finally
        {
            IsDiagnosing = false;
        }
    }

    [RelayCommand]
    private void CopyDiagnosticLog()
    {
        if (string.IsNullOrWhiteSpace(DiagnosticReportText))
        {
            System.Windows.MessageBox.Show("Chưa có báo cáo chẩn đoán nào để sao chép!", "Báo Cáo Trống", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(DiagnosticReportText);
            System.Windows.MessageBox.Show(
                "Đã sao chép toàn bộ báo cáo chẩn đoán kèm Hex TX/RX Log vào Clipboard!\nBạn có thể dán (Ctrl+V) vào Zalo / Notepad / Email gửi cho bộ phận Dev kiểm tra.",
                "Đã Sao Chép Báo Cáo",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Không thể sao chép vào Clipboard: {ex.Message}", "Lỗi Clipboard", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string logDir = System.IO.Path.Combine(appData, "Vision2026", "logs");
            System.IO.Directory.CreateDirectory(logDir);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Không thể mở thư mục Log: {ex.Message}", "Lỗi Mở Thư Mục", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CloseDiagnosticPanel()
    {
        ShowDiagnosticPanel = false;
    }

    #endregion

    #region Full PLC Configuration Export / Import (Backup & Restore)

    [RelayCommand]
    private void ExportPlcConfig()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Sao Lưu Toàn Bộ Cấu Hình PLC & Tags Ra Tệp JSON",
            Filter = "Cấu Hình PLC JSON (*.json)|*.json|Tất cả tệp (*.*)|*.*",
            FileName = $"plc_config_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            FilterIndex = 1
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var container = new PlcConfigContainer
                {
                    Plcs = _plcService.Plcs.ToList(),
                    Tags = _plcService.Tags.ToList(),
                    IndustrialConfig = _plcService.IndustrialConfig
                };

                string json = System.Text.Json.JsonSerializer.Serialize(container, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(dlg.FileName, json, System.Text.Encoding.UTF8);

                System.Windows.MessageBox.Show(
                    $"Đã sao lưu cấu hình thành công!\nSố PLC: {container.Plcs.Count}\nSố Tags: {container.Tags.Count}\nĐường dẫn: {dlg.FileName}",
                    "Sao Lưu Cấu Hình Thành Công",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi sao lưu cấu hình: {ex.Message}", "Lỗi Export", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void ImportPlcConfig()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Phục Hồi Cấu Hình PLC & Tags Từ Tệp JSON",
            Filter = "Cấu Hình PLC JSON (*.json)|*.json|Tất cả tệp (*.*)|*.*",
            FilterIndex = 1
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                string json = System.IO.File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
                var container = System.Text.Json.JsonSerializer.Deserialize<PlcConfigContainer>(json);

                if (container != null)
                {
                    if (container.IndustrialConfig != null)
                    {
                        _plcService.IndustrialConfig = container.IndustrialConfig;
                        IndustrialConfig = container.IndustrialConfig;
                    }

                    _plcService.LoadConfig(container.Plcs ?? new List<PlcModel>(), container.Tags ?? new List<PlcTag>());
                    SelectedPlc = Plcs.FirstOrDefault();
                    RefreshFilteredTags();
                    RefreshAvailableTagsAndPlcs();

                    System.Windows.MessageBox.Show(
                        $"Phục hồi cấu hình thành công!\nSố PLC nạp vào: {Plcs.Count}\nSố Tags nạp vào: {Tags.Count}",
                        "Phục Hồi Thành Công",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi đọc file cấu hình: {ex.Message}", "Lỗi Import", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    #endregion
}
