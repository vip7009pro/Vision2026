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

    public bool IsAnyPlcNode => IsPlcReadNode || IsPlcWriteNode || IsPlcWaitNode || IsPlcTriggerNode || IsPlcBatchReadNode || IsPlcBatchWriteNode;

    public IEnumerable<string> AvailablePlcNames => _plcManagerService.Plcs.Select(p => p.Name);

    public IEnumerable<string> AvailablePlcTagNames
    {
        get
        {
            var plcName = PlcNode_PlcId;
            if (string.IsNullOrWhiteSpace(plcName)) return _plcManagerService.Tags.Select(t => t.Name);

            var plc = _plcManagerService.Plcs.FirstOrDefault(p => string.Equals(p.Name, plcName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Id, plcName, StringComparison.OrdinalIgnoreCase));
            string targetId = plc?.Id ?? plcName;

            return _plcManagerService.Tags
                .Where(t => string.Equals(t.PlcId, targetId, StringComparison.OrdinalIgnoreCase) || string.Equals(t.PlcId, plcName, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Name);
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

    #endregion

    #region Commands for Toolbar/UI

    [RelayCommand]
    private void OpenPlcManager()
    {
        var vm = new PlcManagerViewModel(_plcManagerService);
        var win = new PlcManagerWindow(vm)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        win.ShowDialog();

        _plcManagerService.SaveGlobalConfig();
        _plcManagerService.StartPollingAsync();

        OnPropertyChanged(nameof(AvailablePlcNames));
        OnPropertyChanged(nameof(AvailablePlcTagNames));
    }

    [RelayCommand]
    private void OpenPlcMonitor()
    {
        var vm = new PlcMonitorViewModel(_plcManagerService);
        var win = new PlcMonitorWindow(vm)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        win.Show();
    }

    [RelayCommand]
    private void OpenPlcBrowser()
    {
        var win = new Window
        {
            Title = "PLC Tag Browser",
            Width = 700,
            Height = 450,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = System.Windows.Application.Current?.MainWindow,
            Content = new PlcBrowserControl { DataContext = new PlcBrowserViewModel(_plcManagerService) }
        };
        win.Show();
    }

    #endregion
}
