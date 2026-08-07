using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.DB.Services;

public interface IDbManagerService
{
    IReadOnlyList<DbModel> Databases { get; }

    void LoadDatabases(IEnumerable<DbModel> databases);
    
    void AddDatabase(DbModel db);

    void UpdateDatabase(DbModel db);

    void DeleteDatabase(string dbId);

    DbModel? GetDatabase(string dbIdOrName);

    Task<(bool Success, string Message)> TestConnectionAsync(DbModel db);

    Task<(bool Success, int RowsAffected, string ErrorMessage)> ExecuteNonQueryAsync(string dbIdOrName, string sqlQuery, int timeoutSeconds = 15);

    Task<(bool Success, DataTable? Table, string ErrorMessage)> ExecuteQueryAsync(string dbIdOrName, string sqlQuery, int timeoutSeconds = 15);
}
