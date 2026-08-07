using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.DB.Services;

public class DbManagerService : IDbManagerService
{
    private readonly ConcurrentDictionary<string, DbModel> _databases = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _globalConfigFilePath;

    public IReadOnlyList<DbModel> Databases => _databases.Values.ToList().AsReadOnly();

    public DbManagerService()
    {
        string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vision2026");
        Directory.CreateDirectory(appDataDir);
        _globalConfigFilePath = Path.Combine(appDataDir, "databases_config.json");

        LoadFromDisk();
    }

    public void LoadFromDisk()
    {
        try
        {
            if (File.Exists(_globalConfigFilePath))
            {
                string json = File.ReadAllText(_globalConfigFilePath);
                var list = JsonSerializer.Deserialize<List<DbModel>>(json);
                if (list != null && list.Count > 0)
                {
                    _databases.Clear();
                    foreach (var db in list)
                    {
                        if (!string.IsNullOrWhiteSpace(db.Id))
                        {
                            _databases[db.Id] = db;
                        }
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DB MANAGER] Error loading databases_config.json: {ex.Message}");
        }

        // Initialize default database if file doesn't exist
        if (_databases.IsEmpty)
        {
            var defaultDb = new DbModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = "MainDB",
                ProviderType = DbProviderType.SqlServer,
                Server = "localhost",
                Port = 1433,
                DatabaseName = "VisionDB",
                Username = "sa",
                Password = "",
                IsEnabled = true
            };
            _databases[defaultDb.Id] = defaultDb;
            SaveToDisk();
        }
    }

    public void SaveToDisk()
    {
        try
        {
            var list = _databases.Values.ToList();
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(list, options);
            File.WriteAllText(_globalConfigFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DB MANAGER] Error saving databases_config.json: {ex.Message}");
        }
    }

    public void LoadDatabases(IEnumerable<DbModel> databases)
    {
        if (databases != null && databases.Any())
        {
            foreach (var db in databases)
            {
                if (!string.IsNullOrWhiteSpace(db.Id))
                {
                    _databases[db.Id] = db;
                }
            }
            SaveToDisk();
        }
    }

    public void AddDatabase(DbModel db)
    {
        if (db == null) return;
        if (string.IsNullOrWhiteSpace(db.Id))
        {
            db.Id = Guid.NewGuid().ToString();
        }
        _databases[db.Id] = db;
        SaveToDisk();
    }

    public void UpdateDatabase(DbModel db)
    {
        if (db == null || string.IsNullOrWhiteSpace(db.Id)) return;
        _databases[db.Id] = db;
        SaveToDisk();
    }

    public void DeleteDatabase(string dbId)
    {
        if (string.IsNullOrWhiteSpace(dbId)) return;
        _databases.TryRemove(dbId, out _);
        SaveToDisk();
    }

    public DbModel? GetDatabase(string dbIdOrName)
    {
        if (string.IsNullOrWhiteSpace(dbIdOrName)) return _databases.Values.FirstOrDefault();

        if (_databases.TryGetValue(dbIdOrName, out var dbById))
            return dbById;

        return _databases.Values.FirstOrDefault(d => string.Equals(d.Name, dbIdOrName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(DbModel db)
    {
        if (db == null) return (false, "Database configuration is null.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(db.ConnectionTimeout > 0 ? db.ConnectionTimeout : 3, 1, 5)));
        try
        {
            db.State = "Connecting...";
            using var conn = CreateConnection(db);
            if (conn == null) return (false, $"Unsupported provider type '{db.ProviderType}'.");

            await conn.OpenAsync(cts.Token);
            db.State = "Connected";
            return (true, "Successfully connected to database!");
        }
        catch (OperationCanceledException)
        {
            db.State = "Error";
            return (false, "Connection timed out.");
        }
        catch (Exception ex)
        {
            db.State = "Error";
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, int RowsAffected, string ErrorMessage)> ExecuteNonQueryAsync(string dbIdOrName, string sqlQuery, int timeoutSeconds = 1)
    {
        var db = GetDatabase(dbIdOrName);
        if (db == null) return (false, 0, $"Database '{dbIdOrName}' not found.");
        if (!db.IsEnabled) return (false, 0, $"Database '{db.Name}' is disabled.");

        int effectiveTimeout = Math.Clamp(timeoutSeconds > 0 ? timeoutSeconds : 1, 1, 3);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(effectiveTimeout));

        try
        {
            using var conn = CreateConnection(db);
            if (conn == null) return (false, 0, $"Unsupported provider type '{db.ProviderType}'.");

            await conn.OpenAsync(cts.Token);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sqlQuery;
            cmd.CommandTimeout = effectiveTimeout;

            int rowsAffected = await cmd.ExecuteNonQueryAsync(cts.Token);
            return (true, rowsAffected, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return (false, 0, "Execution timed out.");
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }

    public async Task<(bool Success, DataTable? Table, string ErrorMessage)> ExecuteQueryAsync(string dbIdOrName, string sqlQuery, int timeoutSeconds = 1)
    {
        var db = GetDatabase(dbIdOrName);
        if (db == null) return (false, null, $"Database '{dbIdOrName}' not found.");
        if (!db.IsEnabled) return (false, null, $"Database '{db.Name}' is disabled.");

        int effectiveTimeout = Math.Clamp(timeoutSeconds > 0 ? timeoutSeconds : 1, 1, 3);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(effectiveTimeout));

        try
        {
            using var conn = CreateConnection(db);
            if (conn == null) return (false, null, $"Unsupported provider type '{db.ProviderType}'.");

            await conn.OpenAsync(cts.Token);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sqlQuery;
            cmd.CommandTimeout = effectiveTimeout;

            using var reader = await cmd.ExecuteReaderAsync(cts.Token);
            var table = new DataTable();
            table.Load(reader);

            return (true, table, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return (false, null, "Execution timed out.");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private static DbConnection? CreateConnection(DbModel db)
    {
        string connStr = db.BuildConnectionString();

        return db.ProviderType switch
        {
            DbProviderType.SqlServer => new Microsoft.Data.SqlClient.SqlConnection(connStr),
            DbProviderType.MySql => new MySqlConnector.MySqlConnection(connStr),
            DbProviderType.PostgreSql => new Npgsql.NpgsqlConnection(connStr),
            DbProviderType.Sqlite => new Microsoft.Data.Sqlite.SqliteConnection(connStr),
            DbProviderType.Odbc => new System.Data.Odbc.OdbcConnection(connStr),
            _ => new Microsoft.Data.SqlClient.SqlConnection(connStr)
        };
    }
}
