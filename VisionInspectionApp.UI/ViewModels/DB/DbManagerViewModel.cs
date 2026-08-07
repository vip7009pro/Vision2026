using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionInspectionApp.Application.DB.Services;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.ViewModels.DB;

public partial class DbManagerViewModel : ObservableObject
{
    private readonly IDbManagerService _dbManagerService;
    private readonly Action? _onSavedCallback;

    [ObservableProperty]
    private ObservableCollection<DbModel> _databases = new();

    [ObservableProperty]
    private DbModel? _selectedDb;

    [ObservableProperty]
    private string _testStatusMessage = "";

    [ObservableProperty]
    private bool _isTesting = false;

    public DbProviderType[] ProviderTypes => Enum.GetValues<DbProviderType>();

    public DbManagerViewModel(IDbManagerService dbManagerService, Action? onSavedCallback = null)
    {
        _dbManagerService = dbManagerService;
        _onSavedCallback = onSavedCallback;

        AddDbCommand = new RelayCommand(AddDb);
        DeleteDbCommand = new RelayCommand(DeleteDb, () => SelectedDb != null);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => SelectedDb != null && !IsTesting);
        SaveCommand = new RelayCommand(SaveAndClose);

        // Load existing databases
        var existing = _dbManagerService.Databases;
        if (existing != null && existing.Count > 0)
        {
            foreach (var d in existing)
            {
                Databases.Add(d);
            }
            SelectedDb = Databases.FirstOrDefault();
        }
        else
        {
            // Default first database if empty
            var defaultDb = new DbModel
            {
                Name = "MainDB",
                ProviderType = DbProviderType.SqlServer,
                Server = "localhost",
                Port = 1433,
                DatabaseName = "VisionDB",
                Username = "sa",
                Password = ""
            };
            Databases.Add(defaultDb);
            SelectedDb = defaultDb;
        }
    }

    public ICommand AddDbCommand { get; }
    public ICommand DeleteDbCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }
    public ICommand SaveCommand { get; }

    partial void OnSelectedDbChanged(DbModel? value)
    {
        (DeleteDbCommand as RelayCommand)?.NotifyCanExecuteChanged();
        TestConnectionCommand?.NotifyCanExecuteChanged();
        TestStatusMessage = "";
    }

    private void AddDb()
    {
        int newIdx = Databases.Count + 1;
        var newDb = new DbModel
        {
            Name = $"Database {newIdx}",
            ProviderType = DbProviderType.SqlServer,
            Server = "localhost",
            Port = 1433,
            DatabaseName = "VisionDB",
            Username = "sa",
            Password = ""
        };
        Databases.Add(newDb);
        SelectedDb = newDb;
    }

    private void DeleteDb()
    {
        if (SelectedDb == null) return;
        var toRemove = SelectedDb;
        int idx = Databases.IndexOf(toRemove);
        Databases.Remove(toRemove);
        _dbManagerService.DeleteDatabase(toRemove.Id);

        if (Databases.Count > 0)
        {
            SelectedDb = Databases[Math.Clamp(idx, 0, Databases.Count - 1)];
        }
        else
        {
            SelectedDb = null;
        }
    }

    private async Task TestConnectionAsync()
    {
        if (SelectedDb == null) return;

        try
        {
            IsTesting = true;
            TestStatusMessage = "Testing connection...";
            SelectedDb.State = "Testing...";

            var (success, msg) = await _dbManagerService.TestConnectionAsync(SelectedDb);
            TestStatusMessage = msg;
            SelectedDb.State = success ? "Connected" : "Error";
        }
        catch (Exception ex)
        {
            TestStatusMessage = $"Error: {ex.Message}";
            if (SelectedDb != null) SelectedDb.State = "Error";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private void SaveAndClose()
    {
        _dbManagerService.LoadDatabases(Databases);
        _onSavedCallback?.Invoke();

        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            if (window.DataContext == this || window is Views.DB.DbManagerWindow)
            {
                window.DialogResult = true;
                window.Close();
                break;
            }
        }
    }
}
