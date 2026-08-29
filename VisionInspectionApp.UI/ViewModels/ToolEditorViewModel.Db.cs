using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.Views.DB;
using VisionInspectionApp.UI.ViewModels.DB;

namespace VisionInspectionApp.UI.ViewModels;

public partial class ToolEditorViewModel
{
    private DbNodeDefinition? _selectedDbNode;

    public bool IsDbNode => SelectedNode != null && string.Equals(SelectedNode.Type, "DbNode", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<DbModel> AvailableDatabases => new(_dbManagerService?.Databases ?? Array.Empty<DbModel>());

    public DbNodeMode[] DbNodeModes => Enum.GetValues<DbNodeMode>();
    public DbExecutionTiming[] DbExecutionTimings => Enum.GetValues<DbExecutionTiming>();
    public DbReadOutputFormat[] DbReadOutputFormats => Enum.GetValues<DbReadOutputFormat>();

    public DbModel? Db_SelectedDbChoice
    {
        get => _selectedDbNode != null ? _dbManagerService?.GetDatabase(_selectedDbNode.DbId) : null;
        set
        {
            if (_selectedDbNode != null && value != null)
            {
                _selectedDbNode.DbId = value.Id;
                _selectedDbNode.DbName = value.Name;
                OnPropertyChanged(nameof(Db_SelectedDbChoice));
                RequestAutoSave();
            }
        }
    }

    public DbNodeMode Db_Mode
    {
        get => _selectedDbNode?.Mode ?? DbNodeMode.Read;
        set
        {
            if (_selectedDbNode != null && _selectedDbNode.Mode != value)
            {
                _selectedDbNode.Mode = value;
                OnPropertyChanged(nameof(Db_Mode));
                OnPropertyChanged(nameof(IsDbReadMode));
                RequestAutoSave();
            }
        }
    }

    public bool IsDbReadMode => Db_Mode == DbNodeMode.Read;

    public DbExecutionTiming Db_Timing
    {
        get => _selectedDbNode?.Timing ?? DbExecutionTiming.AfterFlow;
        set
        {
            if (_selectedDbNode != null && _selectedDbNode.Timing != value)
            {
                _selectedDbNode.Timing = value;
                OnPropertyChanged(nameof(Db_Timing));
                RequestAutoSave();
            }
        }
    }

    public string Db_SqlQuery
    {
        get => _selectedDbNode?.SqlQuery ?? "";
        set
        {
            if (_selectedDbNode != null && _selectedDbNode.SqlQuery != value)
            {
                _selectedDbNode.SqlQuery = value;
                OnPropertyChanged(nameof(Db_SqlQuery));
                RequestAutoSave();
            }
        }
    }

    public ImageOutputCondition Db_Condition
    {
        get => _selectedDbNode?.Condition ?? ImageOutputCondition.Always;
        set
        {
            if (_selectedDbNode != null && _selectedDbNode.Condition != value)
            {
                _selectedDbNode.Condition = value;
                OnPropertyChanged(nameof(Db_Condition));
                RequestAutoSave();
            }
        }
    }

    public DbReadOutputFormat Db_ReadFormat
    {
        get => _selectedDbNode?.ReadFormat ?? DbReadOutputFormat.FirstCell;
        set
        {
            if (_selectedDbNode != null && _selectedDbNode.ReadFormat != value)
            {
                _selectedDbNode.ReadFormat = value;
                OnPropertyChanged(nameof(Db_ReadFormat));
                OnPropertyChanged(nameof(IsSpecificCellFormat));
                OnPropertyChanged(nameof(IsColumnJoinFormat));
                RequestAutoSave();
            }
        }
    }

    public bool IsSpecificCellFormat => Db_ReadFormat == DbReadOutputFormat.SpecificCell;
    public bool IsColumnJoinFormat => Db_ReadFormat == DbReadOutputFormat.ColumnJoin;

    public int Db_TargetRowIndex
    {
        get => _selectedDbNode?.TargetRowIndex ?? 0;
        set
        {
            if (_selectedDbNode != null && _selectedDbNode.TargetRowIndex != value)
            {
                _selectedDbNode.TargetRowIndex = Math.Max(0, value);
                OnPropertyChanged(nameof(Db_TargetRowIndex));
                RequestAutoSave();
            }
        }
    }

    public string Db_TargetColumnName
    {
        get => _selectedDbNode?.TargetColumnName ?? "";
        set
        {
            if (_selectedDbNode != null && _selectedDbNode.TargetColumnName != value)
            {
                _selectedDbNode.TargetColumnName = value;
                OnPropertyChanged(nameof(Db_TargetColumnName));
                RequestAutoSave();
            }
        }
    }

    public string Db_ColumnJoinSeparator
    {
        get => _selectedDbNode?.ColumnJoinSeparator ?? ", ";
        set
        {
            if (_selectedDbNode != null && _selectedDbNode.ColumnJoinSeparator != value)
            {
                _selectedDbNode.ColumnJoinSeparator = value;
                OnPropertyChanged(nameof(Db_ColumnJoinSeparator));
                RequestAutoSave();
            }
        }
    }

    public string Db_OutputVarName
    {
        get => _selectedDbNode?.OutputVarName ?? "";
        set
        {
            if (_selectedDbNode != null && _selectedDbNode.OutputVarName != value)
            {
                _selectedDbNode.OutputVarName = value;
                OnPropertyChanged(nameof(Db_OutputVarName));
                RequestAutoSave();
            }
        }
    }

    public bool Db_AllowUpdateDelete
    {
        get => _selectedDbNode?.AllowUpdateDelete ?? false;
        set
        {
            if (_selectedDbNode != null && _selectedDbNode.AllowUpdateDelete != value)
            {
                _selectedDbNode.AllowUpdateDelete = value;
                OnPropertyChanged(nameof(Db_AllowUpdateDelete));
                RequestAutoSave();
            }
        }
    }

    public string Db_LastRunResultText
    {
        get
        {
            if (_selectedDbNode == null || _lastRun?.DbResults == null) return "No run data available";
            var res = _lastRun.DbResults.FirstOrDefault(r => string.Equals(r.NodeName, _selectedDbNode.RefName, StringComparison.OrdinalIgnoreCase));
            if (res == null) return "Not executed in last run";

            if (!res.Success) return $"❌ ERROR: {res.ErrorMessage}";

            if (_selectedDbNode.Mode == DbNodeMode.Write)
            {
                return $"✅ WRITE SUCCESS (Rows affected: {res.RowsAffected})";
            }
            else
            {
                return $"✅ READ SUCCESS (Rows: {res.RowCount}, Cols: {res.ColumnCount})\nValue: {res.Text}";
            }
        }
    }

    public ICommand OpenDbManagerCommand => new RelayCommand(OpenDbManager);

    private static DbManagerWindow? _dbManagerWindowInstance;

    private void OpenDbManager()
    {
        if (_dbManagerService == null) return;

        if (_dbManagerWindowInstance != null && _dbManagerWindowInstance.IsLoaded)
        {
            _dbManagerWindowInstance.Activate();
            if (_dbManagerWindowInstance.WindowState == WindowState.Minimized)
                _dbManagerWindowInstance.WindowState = WindowState.Normal;
            return;
        }

        var vm = new DbManagerViewModel(_dbManagerService, () =>
        {
            OnPropertyChanged(nameof(AvailableDatabases));
            OnPropertyChanged(nameof(Db_SelectedDbChoice));
            RequestAutoSave();
        });

        _dbManagerWindowInstance = new DbManagerWindow
        {
            DataContext = vm
        };

        _dbManagerWindowInstance.Closed += (s, e) =>
        {
            _dbManagerWindowInstance = null;
            OnPropertyChanged(nameof(AvailableDatabases));
            OnPropertyChanged(nameof(Db_SelectedDbChoice));
        };

        _dbManagerWindowInstance.Show();
    }

    public bool Db_Enable
    {
        get => _selectedDbNode?.Enable ?? true;
        set
        {
            if (_selectedDbNode != null && _selectedDbNode.Enable != value)
            {
                _selectedDbNode.Enable = value;
                OnPropertyChanged(nameof(Db_Enable));
                RequestAutoSave();
            }
        }
    }

    private void SyncSelectedDbNode(ToolGraphNodeViewModel? node)
    {
        if (node != null && string.Equals(node.Type, "DbNode", StringComparison.OrdinalIgnoreCase))
        {
            _config.DbNodes ??= new();
            _selectedDbNode = _config.DbNodes.FirstOrDefault(n => string.Equals(n.RefName, node.RefName, StringComparison.OrdinalIgnoreCase));

            if (_selectedDbNode == null)
            {
                _selectedDbNode = new DbNodeDefinition
                {
                    RefName = node.RefName,
                    DbId = AvailableDatabases.FirstOrDefault()?.Id ?? "",
                    DbName = AvailableDatabases.FirstOrDefault()?.Name ?? "",
                    Mode = DbNodeMode.Read,
                    Timing = DbExecutionTiming.AfterFlow,
                    SqlQuery = "SELECT * FROM InspectionLogs WHERE Pass = 1"
                };
                _config.DbNodes.Add(_selectedDbNode);
            }
        }
        else
        {
            _selectedDbNode = null;
        }

        OnPropertyChanged(nameof(IsDbNode));
        OnPropertyChanged(nameof(Db_Enable));
        OnPropertyChanged(nameof(Db_SelectedDbChoice));
        OnPropertyChanged(nameof(Db_Mode));
        OnPropertyChanged(nameof(IsDbReadMode));
        OnPropertyChanged(nameof(Db_Timing));
        OnPropertyChanged(nameof(Db_SqlQuery));
        OnPropertyChanged(nameof(Db_Condition));
        OnPropertyChanged(nameof(Db_ReadFormat));
        OnPropertyChanged(nameof(IsSpecificCellFormat));
        OnPropertyChanged(nameof(IsColumnJoinFormat));
        OnPropertyChanged(nameof(Db_TargetRowIndex));
        OnPropertyChanged(nameof(Db_TargetColumnName));
        OnPropertyChanged(nameof(Db_ColumnJoinSeparator));
        OnPropertyChanged(nameof(Db_OutputVarName));
        OnPropertyChanged(nameof(Db_AllowUpdateDelete));
        OnPropertyChanged(nameof(Db_LastRunResultText));
        OnPropertyChanged(nameof(AvailableDatabases));
    }
}
