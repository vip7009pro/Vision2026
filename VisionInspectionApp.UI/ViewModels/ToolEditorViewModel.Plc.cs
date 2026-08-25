using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels.PLC;
using VisionInspectionApp.UI.Views.PLC;

namespace VisionInspectionApp.UI.ViewModels;

public sealed partial class ToolEditorViewModel : ObservableObject
{
    public bool IsPlcReadNode => SelectedNode != null && string.Equals(SelectedNode.Type, "PlcRead", StringComparison.OrdinalIgnoreCase);

    public bool IsPlcWriteNode => SelectedNode != null && string.Equals(SelectedNode.Type, "PlcWrite", StringComparison.OrdinalIgnoreCase);

    public bool IsPlcWaitNode => SelectedNode != null && string.Equals(SelectedNode.Type, "PlcWait", StringComparison.OrdinalIgnoreCase);

    public bool IsPlcTriggerNode => SelectedNode != null && string.Equals(SelectedNode.Type, "PlcTrigger", StringComparison.OrdinalIgnoreCase);

    public bool IsPlcBatchReadNode => SelectedNode != null && string.Equals(SelectedNode.Type, "PlcBatchRead", StringComparison.OrdinalIgnoreCase);

    public bool IsPlcBatchWriteNode => SelectedNode != null && string.Equals(SelectedNode.Type, "PlcBatchWrite", StringComparison.OrdinalIgnoreCase);

    public bool IsResultTransferNode => SelectedNode != null && string.Equals(SelectedNode.Type, "ResultTransfer", StringComparison.OrdinalIgnoreCase);

    public bool IsAnyPlcNode => IsPlcReadNode || IsPlcWriteNode || IsPlcWaitNode || IsPlcTriggerNode || IsPlcBatchReadNode || IsPlcBatchWriteNode || IsResultTransferNode;

    public IEnumerable<string> AvailablePlcNames
    {
        get
        {
            var list = _plcManagerService.Plcs.Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            if (list.Count == 0)
            {
                list.AddRange(new[] { "PLC1", "PLC2", "PLC_MAIN", "PLC_01" });
            }
            return list.Distinct().ToList();
        }
    }

    public IEnumerable<string> AvailablePlcAllTagNames
    {
        get
        {
            var list = new List<string>();

            // 1. All Tag Names & Addresses from Service
            foreach (var t in _plcManagerService.Tags)
            {
                if (!string.IsNullOrWhiteSpace(t.Name)) list.Add(t.Name);
                if (!string.IsNullOrWhiteSpace(t.Address)) list.Add(t.Address);
            }

            // 2. Standard Common Direct Addresses
            for (int i = 0; i <= 15; i++) list.Add($"X{i}");
            for (int i = 0; i <= 15; i++) list.Add($"Y{i}");
            for (int i = 0; i <= 20; i++) list.Add($"M{i}");
            list.AddRange(new[] { "D0", "D10", "D100", "D200", "D500", "D1000", "D1002", "D1004", "D2000" });
            list.AddRange(new[] { "MW100", "MW102", "MW200" });

            // 3. Standard Industrial Named Tags
            list.AddRange(new[] {
                "X0_Trigger", "X0_PlcHeartbeat", "X1_PlcAck", "X2_SensorIn",
                "Y0_VisionHeartbeat", "Y0_RejectPiston", "Y1_VisionReady",
                "Y2_VisionBusy", "Y3_VisionDone", "Y4_VisionPass", "Y5_VisionNG",
                "Y10_VisionFault", "D1000_Encoder", "D1002_Speed"
            });

            return list.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        }
    }

    public IEnumerable<string> AvailablePlcTagNames
    {
        get
        {
            var plcName = PlcNode_PlcId;
            if (string.IsNullOrWhiteSpace(plcName)) return AvailablePlcAllTagNames;

            var plc = _plcManagerService.Plcs.FirstOrDefault(p => string.Equals(p.Name, plcName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Id, plcName, StringComparison.OrdinalIgnoreCase));
            string targetId = plc?.Id ?? plcName;

            var list = new List<string>();
            var filteredTags = _plcManagerService.Tags
                .Where(t => string.Equals(t.PlcId, targetId, StringComparison.OrdinalIgnoreCase) || string.Equals(t.PlcId, plcName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var t in filteredTags)
            {
                if (!string.IsNullOrWhiteSpace(t.Name)) list.Add(t.Name);
                if (!string.IsNullOrWhiteSpace(t.Address)) list.Add(t.Address);
            }

            // Always provide standard direct addresses & named tags as fallbacks
            list.AddRange(AvailablePlcAllTagNames);

            return list.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        }
    }

    public Array AvailablePlcCompareOperators => Enum.GetValues(typeof(PlcCompareOperator));

    public Array AvailablePlcTriggerEdges => Enum.GetValues(typeof(PlcTriggerEdge));

    #region PLC Node Binding Properties

    public string PlcNode_PlcId
    {
        get
        {
            if (SelectedNode == null || _config == null) return string.Empty;
            if (IsPlcReadNode) return _config.PlcReads.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.PlcId ?? string.Empty;
            if (IsPlcWriteNode) return _config.PlcWrites.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.PlcId ?? string.Empty;
            if (IsPlcWaitNode) return _config.PlcWaits.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.PlcId ?? string.Empty;
            if (IsPlcTriggerNode) return _config.PlcTriggers.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.PlcId ?? string.Empty;
            if (IsPlcBatchReadNode) return _config.PlcBatchReads.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.PlcId ?? string.Empty;
            if (IsPlcBatchWriteNode) return _config.PlcBatchWrites.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.PlcId ?? string.Empty;
            return string.Empty;
        }
        set
        {
            if (SelectedNode == null || _config == null) return;
            if (IsPlcReadNode)
            {
                var def = GetOrCreatePlcRead(SelectedNode.RefName);
                def.PlcId = value;
            }
            else if (IsPlcWriteNode)
            {
                var def = GetOrCreatePlcWrite(SelectedNode.RefName);
                def.PlcId = value;
            }
            else if (IsPlcWaitNode)
            {
                var def = GetOrCreatePlcWait(SelectedNode.RefName);
                def.PlcId = value;
            }
            else if (IsPlcTriggerNode)
            {
                var def = GetOrCreatePlcTrigger(SelectedNode.RefName);
                def.PlcId = value;
            }
            else if (IsPlcBatchReadNode)
            {
                var def = GetOrCreatePlcBatchRead(SelectedNode.RefName);
                def.PlcId = value;
            }
            else if (IsPlcBatchWriteNode)
            {
                var def = GetOrCreatePlcBatchWrite(SelectedNode.RefName);
                def.PlcId = value;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvailablePlcTagNames));
            RequestAutoSave();
        }
    }

    public string PlcNode_TagName
    {
        get
        {
            if (SelectedNode == null || _config == null) return string.Empty;
            if (IsPlcReadNode) return _config.PlcReads.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.TagName ?? string.Empty;
            if (IsPlcWriteNode) return _config.PlcWrites.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.TagName ?? string.Empty;
            if (IsPlcWaitNode) return _config.PlcWaits.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.TagName ?? string.Empty;
            if (IsPlcTriggerNode) return _config.PlcTriggers.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.TagName ?? string.Empty;
            return string.Empty;
        }
        set
        {
            if (SelectedNode == null || _config == null) return;
            if (IsPlcReadNode)
            {
                var def = GetOrCreatePlcRead(SelectedNode.RefName);
                def.TagName = value;
            }
            else if (IsPlcWriteNode)
            {
                var def = GetOrCreatePlcWrite(SelectedNode.RefName);
                def.TagName = value;
            }
            else if (IsPlcWaitNode)
            {
                var def = GetOrCreatePlcWait(SelectedNode.RefName);
                def.TagName = value;
            }
            else if (IsPlcTriggerNode)
            {
                var def = GetOrCreatePlcTrigger(SelectedNode.RefName);
                def.TagName = value;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlcNode_CurrentValue));
            OnPropertyChanged(nameof(PlcNode_TagDataType));
            RequestAutoSave();
        }
    }

    public string PlcNode_WriteValue
    {
        get
        {
            if (SelectedNode == null || _config == null) return "0";
            return _config.PlcWrites.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.WriteValue ?? "0";
        }
        set
        {
            if (SelectedNode == null || _config == null) return;
            var def = GetOrCreatePlcWrite(SelectedNode.RefName);
            def.WriteValue = value;
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public PlcCompareOperator PlcNode_Operator
    {
        get
        {
            if (SelectedNode == null || _config == null) return PlcCompareOperator.Equal;
            return _config.PlcWaits.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.Operator ?? PlcCompareOperator.Equal;
        }
        set
        {
            if (SelectedNode == null || _config == null) return;
            var def = GetOrCreatePlcWait(SelectedNode.RefName);
            def.Operator = value;
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public string PlcNode_TargetValue
    {
        get
        {
            if (SelectedNode == null || _config == null) return "true";
            return _config.PlcWaits.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.TargetValue ?? "true";
        }
        set
        {
            if (SelectedNode == null || _config == null) return;
            var def = GetOrCreatePlcWait(SelectedNode.RefName);
            def.TargetValue = value;
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public int PlcNode_TimeoutMs
    {
        get
        {
            if (SelectedNode == null || _config == null) return 5000;
            return _config.PlcWaits.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.TimeoutMs ?? 5000;
        }
        set
        {
            if (SelectedNode == null || _config == null) return;
            var def = GetOrCreatePlcWait(SelectedNode.RefName);
            def.TimeoutMs = value;
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public PlcTriggerEdge PlcNode_EdgeMode
    {
        get
        {
            if (SelectedNode == null || _config == null) return PlcTriggerEdge.RisingEdge;
            return _config.PlcTriggers.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase))?.EdgeMode ?? PlcTriggerEdge.RisingEdge;
        }
        set
        {
            if (SelectedNode == null || _config == null) return;
            var def = GetOrCreatePlcTrigger(SelectedNode.RefName);
            def.EdgeMode = value;
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public string PlcNode_BatchTagListString
    {
        get
        {
            if (SelectedNode == null || _config == null) return string.Empty;
            var def = _config.PlcBatchReads.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
            return def != null ? string.Join(", ", def.TagNames) : string.Empty;
        }
        set
        {
            if (SelectedNode == null || _config == null) return;
            var def = GetOrCreatePlcBatchRead(SelectedNode.RefName);
            def.TagNames = (value ?? "").Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public string PlcNode_BatchWriteValuesString
    {
        get
        {
            if (SelectedNode == null || _config == null) return string.Empty;
            var def = _config.PlcBatchWrites.FirstOrDefault(x => string.Equals(x.Name, SelectedNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (def == null || def.TagValues == null) return string.Empty;
            return string.Join(", ", def.TagValues.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        set
        {
            if (SelectedNode == null || _config == null) return;
            var def = GetOrCreatePlcBatchWrite(SelectedNode.RefName);
            def.TagValues.Clear();
            var pairs = (value ?? "").Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in pairs)
            {
                var kv = p.Split('=');
                if (kv.Length == 2)
                {
                    def.TagValues[kv[0].Trim()] = kv[1].Trim();
                }
            }
            OnPropertyChanged();
            RequestAutoSave();
        }
    }

    public string PlcNode_CurrentValue
    {
        get
        {
            var val = _plcManagerService.GetTagValue(PlcNode_PlcId, PlcNode_TagName);
            return val?.CurrentValue?.ToString() ?? "N/A";
        }
    }

    public string PlcNode_TagDataType
    {
        get
        {
            var tag = _plcManagerService.Tags.FirstOrDefault(t => string.Equals(t.Name, PlcNode_TagName, StringComparison.OrdinalIgnoreCase));
            return tag?.DataType.ToString() ?? "Unknown";
        }
    }

    #endregion

    #region Helper Creators

    private PlcReadDefinition GetOrCreatePlcRead(string name)
    {
        _config ??= new VisionConfig();
        var def = _config.PlcReads.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def == null)
        {
            def = new PlcReadDefinition { Name = name, PlcId = _plcManagerService.Plcs.FirstOrDefault()?.Name ?? "PLC1" };
            _config.PlcReads.Add(def);
        }
        return def;
    }

    private PlcWriteDefinition GetOrCreatePlcWrite(string name)
    {
        _config ??= new VisionConfig();
        var def = _config.PlcWrites.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def == null)
        {
            def = new PlcWriteDefinition { Name = name, PlcId = _plcManagerService.Plcs.FirstOrDefault()?.Name ?? "PLC1" };
            _config.PlcWrites.Add(def);
        }
        return def;
    }

    private PlcWaitDefinition GetOrCreatePlcWait(string name)
    {
        _config ??= new VisionConfig();
        var def = _config.PlcWaits.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def == null)
        {
            def = new PlcWaitDefinition { Name = name, PlcId = _plcManagerService.Plcs.FirstOrDefault()?.Name ?? "PLC1" };
            _config.PlcWaits.Add(def);
        }
        return def;
    }

    private PlcTriggerDefinition GetOrCreatePlcTrigger(string name)
    {
        _config ??= new VisionConfig();
        var def = _config.PlcTriggers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def == null)
        {
            def = new PlcTriggerDefinition { Name = name, PlcId = _plcManagerService.Plcs.FirstOrDefault()?.Name ?? "PLC1" };
            _config.PlcTriggers.Add(def);
        }
        return def;
    }

    private PlcBatchReadDefinition GetOrCreatePlcBatchRead(string name)
    {
        _config ??= new VisionConfig();
        var def = _config.PlcBatchReads.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def == null)
        {
            def = new PlcBatchReadDefinition { Name = name, PlcId = _plcManagerService.Plcs.FirstOrDefault()?.Name ?? "PLC1" };
            _config.PlcBatchReads.Add(def);
        }
        return def;
    }

    private PlcBatchWriteDefinition GetOrCreatePlcBatchWrite(string name)
    {
        _config ??= new VisionConfig();
        var def = _config.PlcBatchWrites.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def == null)
        {
            def = new PlcBatchWriteDefinition { Name = name, PlcId = _plcManagerService.Plcs.FirstOrDefault()?.Name ?? "PLC1" };
            _config.PlcBatchWrites.Add(def);
        }
        return def;
    }

    public ResultTransferDefinition? GetOrCreateResultTransfer(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        _config ??= new VisionConfig();
        var def = _config.ResultTransfers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def == null)
        {
            def = new ResultTransferDefinition
            {
                Name = name,
                Items = new List<ResultTransferItem>
                {
                    new ResultTransferItem { PlcId = _plcManagerService.Plcs.FirstOrDefault()?.Name ?? "PLC1", TagName = "Y0_OK", ValueExpression = "TotalPassBit", Condition = ImageOutputCondition.Always },
                    new ResultTransferItem { PlcId = _plcManagerService.Plcs.FirstOrDefault()?.Name ?? "PLC1", TagName = "Y1_NG", ValueExpression = "TotalFailBit", Condition = ImageOutputCondition.Always }
                }
            };
            _config.ResultTransfers.Add(def);
        }
        return def;
    }

    public ObservableCollection<ResultTransferItemVM> ResultTransferItemVMs { get; } = new();

    public void RefreshResultTransferItems()
    {
        ResultTransferItemVMs.Clear();
        if (SelectedNode == null || !IsResultTransferNode || _config == null) return;
        var def = GetOrCreateResultTransfer(SelectedNode.RefName);
        if (def == null) return;

        foreach (var item in def.Items)
        {
            var vm = new ResultTransferItemVM(item, AvailablePlcNames, AvailablePlcTagNames, () => IsDirty = true);
            ResultTransferItemVMs.Add(vm);
        }
    }

    [RelayCommand]
    private void AddResultTransferItem()
    {
        if (SelectedNode == null || !IsResultTransferNode) return;
        var def = GetOrCreateResultTransfer(SelectedNode.RefName);
        if (def == null) return;

        var newItem = new ResultTransferItem
        {
            PlcId = _plcManagerService.Plcs.FirstOrDefault()?.Name ?? "PLC1",
            TagName = _plcManagerService.Tags.FirstOrDefault()?.Name ?? "Y0",
            ValueExpression = "TotalPassBit",
            Condition = ImageOutputCondition.Always
        };
        def.Items.Add(newItem);
        IsDirty = true;
        RefreshResultTransferItems();
    }

    [RelayCommand]
    private void AddResultTransferPresetOkNg()
    {
        if (SelectedNode == null || !IsResultTransferNode) return;
        var def = GetOrCreateResultTransfer(SelectedNode.RefName);
        if (def == null) return;

        var defaultPlc = _plcManagerService.Plcs.FirstOrDefault()?.Name ?? "PLC1";
        def.Items.Add(new ResultTransferItem { PlcId = defaultPlc, TagName = "Y0_OK", ValueExpression = "TotalPassBit", Condition = ImageOutputCondition.Always });
        def.Items.Add(new ResultTransferItem { PlcId = defaultPlc, TagName = "Y1_NG", ValueExpression = "TotalFailBit", Condition = ImageOutputCondition.Always });
        IsDirty = true;
        RefreshResultTransferItems();
    }

    [RelayCommand]
    private void AddResultTransferPresetPose()
    {
        if (SelectedNode == null || !IsResultTransferNode) return;
        var def = GetOrCreateResultTransfer(SelectedNode.RefName);
        if (def == null) return;

        var defaultPlc = _plcManagerService.Plcs.FirstOrDefault()?.Name ?? "PLC1";
        def.Items.Add(new ResultTransferItem { PlcId = defaultPlc, TagName = "D200_PosX", ValueExpression = "{Origin.X}", Condition = ImageOutputCondition.OnPass });
        def.Items.Add(new ResultTransferItem { PlcId = defaultPlc, TagName = "D202_PosY", ValueExpression = "{Origin.Y}", Condition = ImageOutputCondition.OnPass });
        def.Items.Add(new ResultTransferItem { PlcId = defaultPlc, TagName = "D204_Angle", ValueExpression = "{Origin.AngleDeg}", Condition = ImageOutputCondition.OnPass });
        IsDirty = true;
        RefreshResultTransferItems();
    }

    [RelayCommand]
    private void RemoveResultTransferItem(ResultTransferItemVM? itemVm)
    {
        if (itemVm == null || SelectedNode == null || !IsResultTransferNode) return;
        var def = GetOrCreateResultTransfer(SelectedNode.RefName);
        if (def == null) return;

        def.Items.Remove(itemVm.Model);
        IsDirty = true;
        RefreshResultTransferItems();
    }

    #endregion

    #region Commands for Toolbar/UI

    [RelayCommand]
    private void OpenPlcManager()
    {
        var vm = new PlcManagerViewModel(_plcManagerService);
        var activeWin = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current?.MainWindow;
        var win = new PlcManagerWindow(vm)
        {
            Owner = activeWin
        };
        win.ShowDialog();

        _plcManagerService.SaveGlobalConfig();

        OnPropertyChanged(nameof(AvailablePlcNames));
        OnPropertyChanged(nameof(AvailablePlcTagNames));
        OnPropertyChanged(nameof(AvailablePlcAllTagNames));
    }

    [RelayCommand]
    private void OpenHmiManager()
    {
        var vm = new HMI.HmiManagerViewModel(_plcManagerService);
        var activeWin = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current?.MainWindow;
        var win = new Views.HMI.HmiManagerWindow(vm)
        {
            Owner = activeWin
        };
        win.Closed += (s, e) => vm.StopRunMode();
        win.Show();
    }

    [RelayCommand]
    private void OpenPlcMonitor()
    {
        _plcManagerService.AcquirePollingLock("PlcMonitorWindow");
        var vm = new PlcMonitorViewModel(_plcManagerService);
        var activeWin = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current?.MainWindow;
        var win = new PlcMonitorWindow(vm)
        {
            Owner = activeWin
        };
        win.Closed += (s, e) => _plcManagerService.ReleasePollingLock("PlcMonitorWindow");
        win.Show();
    }

    [RelayCommand]
    private void OpenPlcBrowser()
    {
        _plcManagerService.AcquirePollingLock("PlcBrowserWindow");
        var activeWin = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current?.MainWindow;
        var win = new Window
        {
            Title = "PLC Tag Browser",
            Width = 700,
            Height = 450,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Owner = activeWin,
            Content = new PlcBrowserControl { DataContext = new PlcBrowserViewModel(_plcManagerService) }
        };
        win.Closed += (s, e) => _plcManagerService.ReleasePollingLock("PlcBrowserWindow");
        win.Show();
    }

    [RelayCommand]
    private void OpenPlcOscilloscope()
    {
        var vm = new PlcOscilloscopeViewModel(_plcManagerService);
        var activeWin = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current?.MainWindow;
        var win = new Views.PLC.PlcOscilloscopeWindow(vm)
        {
            Owner = activeWin
        };
        win.Show();
    }

    #endregion
}

public partial class ResultTransferItemVM : ObservableObject
{
    public ResultTransferItem Model { get; }
    private readonly Action _onChanged;

    public IEnumerable<string> AvailablePlcs { get; }
    public IEnumerable<string> AvailableTags { get; }
    public Array AvailableConditions => Enum.GetValues(typeof(ImageOutputCondition));
    public Array AvailableModes => Enum.GetValues(typeof(ResultTransferMode));

    public string PlcId
    {
        get => Model.PlcId;
        set
        {
            if (Model.PlcId != value)
            {
                Model.PlcId = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public string TagName
    {
        get => Model.TagName;
        set
        {
            if (Model.TagName != value)
            {
                Model.TagName = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public string ValueExpression
    {
        get => Model.ValueExpression;
        set
        {
            if (Model.ValueExpression != value)
            {
                Model.ValueExpression = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public ImageOutputCondition Condition
    {
        get => Model.Condition;
        set
        {
            if (Model.Condition != value)
            {
                Model.Condition = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public ResultTransferMode Mode
    {
        get => Model.Mode;
        set
        {
            if (Model.Mode != value)
            {
                Model.Mode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPulseMode));
                _onChanged();
            }
        }
    }

    public bool IsPulseMode => Mode == ResultTransferMode.Pulse;

    public int PulseDurationMs
    {
        get => Model.PulseDurationMs;
        set
        {
            if (Model.PulseDurationMs != value)
            {
                Model.PulseDurationMs = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public ResultTransferItemVM(ResultTransferItem model, IEnumerable<string> plcs, IEnumerable<string> tags, Action onChanged)
    {
        Model = model;
        AvailablePlcs = plcs;
        AvailableTags = tags;
        _onChanged = onChanged;
    }
}
