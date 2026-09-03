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
    public string ConfigFilePath => _globalConfigFilePath;

    public DbManagerService(string? customConfigFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customConfigFilePath))
        {
            _globalConfigFilePath = customConfigFilePath;
            var dir = Path.GetDirectoryName(_globalConfigFilePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        }
        else
        {
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vision2026");
            Directory.CreateDirectory(appDataDir);
            _globalConfigFilePath = Path.Combine(appDataDir, "databases_config.json");
        }

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

        int timeoutSec = Math.Clamp(db.ConnectionTimeout > 0 ? db.ConnectionTimeout : 3, 1, 5);

        return await Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
            try
            {
                db.State = "Connecting...";
                using var conn = CreateConnection(db, timeoutSec);
                if (conn == null) return (false, $"Unsupported provider type '{db.ProviderType}'.");

                var openTask = conn.OpenAsync(cts.Token);
                var completedTask = await Task.WhenAny(openTask, Task.Delay(TimeSpan.FromSeconds(timeoutSec), cts.Token));
                if (completedTask != openTask)
                {
                    db.State = "Error";
                    return (false, $"Kết nối cơ sở dữ liệu '{db.Name}' quá thời gian ({timeoutSec}s).");
                }

                await openTask;
                db.State = "Connected";
                return (true, "Successfully connected to database!");
            }
            catch (OperationCanceledException)
            {
                db.State = "Error";
                return (false, $"Kết nối DB '{db.Name}' hết thời gian chờ ({timeoutSec}s).");
            }
            catch (Exception ex)
            {
                db.State = "Error";
                return (false, $"Connection failed: {ex.Message}");
            }
        });
    }

    public async Task<(bool Success, int RowsAffected, string ErrorMessage)> ExecuteNonQueryAsync(string dbIdOrName, string sqlQuery, int timeoutSeconds = 2)
    {
        var db = GetDatabase(dbIdOrName);
        if (db == null) return (false, 0, $"Database '{dbIdOrName}' not found.");
        if (!db.IsEnabled) return (false, 0, $"Database '{db.Name}' is disabled.");

        int effectiveTimeout = Math.Clamp(timeoutSeconds > 0 ? timeoutSeconds : 2, 1, 5);

        return await Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(effectiveTimeout));
            try
            {
                using var conn = CreateConnection(db, effectiveTimeout);
                if (conn == null) return (false, 0, $"Unsupported provider type '{db.ProviderType}'.");

                var openTask = conn.OpenAsync(cts.Token);
                var completedOpen = await Task.WhenAny(openTask, Task.Delay(TimeSpan.FromSeconds(effectiveTimeout), cts.Token));
                if (completedOpen != openTask)
                {
                    return (false, 0, $"Kết nối DB '{db.Name}' quá thời gian ({effectiveTimeout}s).");
                }
                await openTask;

                using var cmd = conn.CreateCommand();
                cmd.CommandText = sqlQuery;
                cmd.CommandTimeout = effectiveTimeout;

                var execTask = cmd.ExecuteNonQueryAsync(cts.Token);
                var completedExec = await Task.WhenAny(execTask, Task.Delay(TimeSpan.FromSeconds(effectiveTimeout), cts.Token));
                if (completedExec != execTask)
                {
                    return (false, 0, $"Thực thi truy vấn DB '{db.Name}' quá thời gian ({effectiveTimeout}s).");
                }

                int rowsAffected = await execTask;
                return (true, rowsAffected, string.Empty);
            }
            catch (OperationCanceledException)
            {
                return (false, 0, $"Truy vấn DB '{db.Name}' hết thời gian chờ ({effectiveTimeout}s).");
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
        });
    }

    public async Task<(bool Success, DataTable? Table, string ErrorMessage)> ExecuteQueryAsync(string dbIdOrName, string sqlQuery, int timeoutSeconds = 2)
    {
        var db = GetDatabase(dbIdOrName);
        if (db == null) return (false, null, $"Database '{dbIdOrName}' not found.");
        if (!db.IsEnabled) return (false, null, $"Database '{db.Name}' is disabled.");

        int effectiveTimeout = Math.Clamp(timeoutSeconds > 0 ? timeoutSeconds : 2, 1, 5);

        return await Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(effectiveTimeout));
            try
            {
                using var conn = CreateConnection(db, effectiveTimeout);
                if (conn == null) return (false, null, $"Unsupported provider type '{db.ProviderType}'.");

                var openTask = conn.OpenAsync(cts.Token);
                var completedOpen = await Task.WhenAny(openTask, Task.Delay(TimeSpan.FromSeconds(effectiveTimeout), cts.Token));
                if (completedOpen != openTask)
                {
                    return (false, null, $"Kết nối DB '{db.Name}' quá thời gian ({effectiveTimeout}s).");
                }
                await openTask;

                using var cmd = conn.CreateCommand();
                cmd.CommandText = sqlQuery;
                cmd.CommandTimeout = effectiveTimeout;

                var readerTask = cmd.ExecuteReaderAsync(cts.Token);
                var completedReader = await Task.WhenAny(readerTask, Task.Delay(TimeSpan.FromSeconds(effectiveTimeout), cts.Token));
                if (completedReader != readerTask)
                {
                    return (false, null, $"Đọc kết quả DB '{db.Name}' quá thời gian ({effectiveTimeout}s).");
                }

                using var reader = await readerTask;
                var table = new DataTable();
                table.Load(reader);

                return (true, table, string.Empty);
            }
            catch (OperationCanceledException)
            {
                return (false, null, $"Truy vấn DB '{db.Name}' hết thời gian chờ ({effectiveTimeout}s).");
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        });
    }

    private static DbConnection? CreateConnection(DbModel db, int timeoutSeconds = 2)
    {
        string connStr = db.BuildConnectionString();

        try
        {
            if (db.ProviderType == DbProviderType.SqlServer)
            {
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr);
                builder.ConnectTimeout = Math.Clamp(timeoutSeconds, 1, 5);
                connStr = builder.ConnectionString;
            }
            else if (db.ProviderType == DbProviderType.MySql)
            {
                var builder = new MySqlConnector.MySqlConnectionStringBuilder(connStr);
                builder.ConnectionTimeout = (uint)Math.Clamp(timeoutSeconds, 1, 5);
                connStr = builder.ConnectionString;
            }
            else if (db.ProviderType == DbProviderType.PostgreSql)
            {
                var builder = new Npgsql.NpgsqlConnectionStringBuilder(connStr);
                builder.Timeout = Math.Clamp(timeoutSeconds, 1, 5);
                connStr = builder.ConnectionString;
            }
        }
        catch { }

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
