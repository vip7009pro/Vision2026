using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace VisionInspectionApp.Models;

public enum DbProviderType
{
    SqlServer,
    MySql,
    PostgreSql,
    Sqlite,
    Oracle,
    Odbc
}

public class DbModel : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = "Database 1";
    private DbProviderType _providerType = DbProviderType.SqlServer;
    private string _server = "localhost";
    private int _port = 1433;
    private string _databaseName = "VisionInspectionDB";
    private string _username = "sa";
    private string _password = "";
    private string _connectionString = "";
    private int _connectionTimeout = 15;
    private bool _isEnabled = true;
    private string _state = "Disconnected";

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public DbProviderType ProviderType
    {
        get => _providerType;
        set
        {
            if (SetField(ref _providerType, value))
            {
                // Set default port based on provider
                if (Port == 0 || Port == 1433 || Port == 3306 || Port == 5432 || Port == 1521)
                {
                    Port = GetDefaultPort(value);
                }
                OnPropertyChanged(nameof(DefaultPortText));
            }
        }
    }

    public string Server
    {
        get => _server;
        set => SetField(ref _server, value);
    }

    public int Port
    {
        get => _port;
        set => SetField(ref _port, value);
    }

    public string DatabaseName
    {
        get => _databaseName;
        set => SetField(ref _databaseName, value);
    }

    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    public string ConnectionString
    {
        get => _connectionString;
        set => SetField(ref _connectionString, value);
    }

    public int ConnectionTimeout
    {
        get => _connectionTimeout;
        set => SetField(ref _connectionTimeout, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    [JsonIgnore]
    public string State
    {
        get => _state;
        set => SetField(ref _state, value);
    }

    [JsonIgnore]
    public string DefaultPortText => $"Default: {GetDefaultPort(ProviderType)}";

    public static int GetDefaultPort(DbProviderType provider)
    {
        return provider switch
        {
            DbProviderType.SqlServer => 1433,
            DbProviderType.MySql => 3306,
            DbProviderType.PostgreSql => 5432,
            DbProviderType.Oracle => 1521,
            DbProviderType.Sqlite => 0,
            DbProviderType.Odbc => 0,
            _ => 1433
        };
    }

    public string BuildConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
            return ConnectionString.Trim();

        return ProviderType switch
        {
            DbProviderType.SqlServer => string.IsNullOrWhiteSpace(Username)
                ? $"Server={Server},{Port};Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True;Connection Timeout={ConnectionTimeout};"
                : $"Server={Server},{Port};Database={DatabaseName};User Id={Username};Password={Password};TrustServerCertificate=True;Connection Timeout={ConnectionTimeout};",

            DbProviderType.MySql => $"Server={Server};Port={Port};Database={DatabaseName};Uid={Username};Pwd={Password};Connection Timeout={ConnectionTimeout};",

            DbProviderType.PostgreSql => $"Host={Server};Port={Port};Database={DatabaseName};Username={Username};Password={Password};Timeout={ConnectionTimeout};",

            DbProviderType.Sqlite => $"Data Source={Server};",

            DbProviderType.Oracle => $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={Server})(PORT={Port}))(CONNECT_DATA=(SERVICE_NAME={DatabaseName})));User Id={Username};Password={Password};",

            DbProviderType.Odbc => $"Driver={{SQL Server}};Server={Server};Database={DatabaseName};Uid={Username};Pwd={Password};",

            _ => ""
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
