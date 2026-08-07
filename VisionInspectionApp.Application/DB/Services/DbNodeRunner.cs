using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VisionInspectionApp.Models;
using VisionInspectionApp.Application.PLC.Services;

namespace VisionInspectionApp.Application.DB.Services;

public static class DbNodeRunner
{
    public static async Task ExecuteDbNodesAsync(VisionConfig config, InspectionResult result, IDbManagerService dbManager, DbExecutionTiming timing)
    {
        if (config == null || config.DbNodes == null || config.DbNodes.Count == 0 || result == null || dbManager == null)
        {
            return;
        }

        var targetNodes = config.DbNodes.Where(n => n.Enable && n.Timing == timing).ToList();
        if (targetNodes.Count == 0) return;

        foreach (var nodeDef in targetNodes)
        {
            if (string.IsNullOrWhiteSpace(nodeDef.SqlQuery)) continue;

            // Check condition (Always, OnPass, OnFail)
            if (nodeDef.Condition == ImageOutputCondition.OnPass && !result.Pass)
                continue;
            if (nodeDef.Condition == ImageOutputCondition.OnFail && result.Pass)
                continue;

            // Interpolate dynamic SQL query string
            string interpolatedSql = InterpolateSqlQuery(nodeDef.SqlQuery, result, config);

            var dbResult = new DbResult
            {
                NodeName = nodeDef.RefName,
                Executed = true
            };

            // Validate SQL Query Safety (Block dangerous DELETE, UPDATE without WHERE, DROP, TRUNCATE, etc.)
            var (isSafe, safetyError) = ValidateSqlQuerySafety(interpolatedSql, nodeDef.Mode, nodeDef.AllowUpdateDelete);
            if (!isSafe)
            {
                dbResult.Success = false;
                dbResult.ErrorMessage = safetyError;
                dbResult.Text = safetyError;
                lock (result.DbResults)
                {
                    result.DbResults.RemoveAll(r => string.Equals(r.NodeName, dbResult.NodeName, StringComparison.OrdinalIgnoreCase));
                    result.DbResults.Add(dbResult);
                }
                continue;
            }

            try
            {
                if (nodeDef.Mode == DbNodeMode.Write)
                {
                    var (success, rowsAffected, error) = await dbManager.ExecuteNonQueryAsync(nodeDef.DbId, interpolatedSql);
                    dbResult.Success = success;
                    dbResult.RowsAffected = rowsAffected;
                    dbResult.ErrorMessage = error;
                    dbResult.Text = success ? $"OK (Rows: {rowsAffected})" : $"ERROR: {error}";
                }
                else // Read Mode
                {
                    var (success, table, error) = await dbManager.ExecuteQueryAsync(nodeDef.DbId, interpolatedSql);
                    dbResult.Success = success;
                    dbResult.ErrorMessage = error;

                    if (success && table != null)
                    {
                        dbResult.RowCount = table.Rows.Count;
                        dbResult.ColumnCount = table.Columns.Count;

                        // Build Rows list (List of Dictionary)
                        foreach (DataRow row in table.Rows)
                        {
                            var rowDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            foreach (DataColumn col in table.Columns)
                            {
                                rowDict[col.ColumnName] = row[col] == DBNull.Value ? null! : row[col];
                            }
                            dbResult.Rows.Add(rowDict);
                        }

                        // Populate ColumnMap for row 0 (or target row)
                        int targetRowIdx = Math.Clamp(nodeDef.TargetRowIndex, 0, Math.Max(0, table.Rows.Count - 1));
                        if (table.Rows.Count > 0)
                        {
                            DataRow selectedRow = table.Rows[targetRowIdx];
                            foreach (DataColumn col in table.Columns)
                            {
                                dbResult.ColumnMap[col.ColumnName] = selectedRow[col] == DBNull.Value ? "" : selectedRow[col];
                            }
                        }

                        // Extract value & text format according to ReadFormat
                        ProcessReadFormat(nodeDef, table, dbResult);
                    }
                    else
                    {
                        dbResult.Text = $"ERROR: {error}";
                    }
                }
            }
            catch (Exception ex)
            {
                dbResult.Success = false;
                dbResult.ErrorMessage = ex.Message;
                dbResult.Text = $"EXCEPTION: {ex.Message}";
            }

            lock (result.DbResults)
            {
                result.DbResults.RemoveAll(r => string.Equals(r.NodeName, dbResult.NodeName, StringComparison.OrdinalIgnoreCase));
                result.DbResults.Add(dbResult);
            }
        }
    }

    private static void ProcessReadFormat(DbNodeDefinition nodeDef, DataTable table, DbResult dbResult)
    {
        if (table == null || table.Rows.Count == 0 || table.Columns.Count == 0)
        {
            dbResult.Value = null;
            dbResult.Text = "No rows returned";
            return;
        }

        switch (nodeDef.ReadFormat)
        {
            case DbReadOutputFormat.FirstCell:
                {
                    object cellVal = table.Rows[0][0];
                    dbResult.Value = cellVal == DBNull.Value ? null : cellVal;
                    dbResult.Text = dbResult.Value?.ToString() ?? "";
                    break;
                }

            case DbReadOutputFormat.SpecificCell:
                {
                    int rIdx = Math.Clamp(nodeDef.TargetRowIndex, 0, table.Rows.Count - 1);
                    DataRow row = table.Rows[rIdx];

                    object? cellVal = null;
                    if (!string.IsNullOrWhiteSpace(nodeDef.TargetColumnName))
                    {
                        if (table.Columns.Contains(nodeDef.TargetColumnName))
                        {
                            cellVal = row[nodeDef.TargetColumnName];
                        }
                        else if (int.TryParse(nodeDef.TargetColumnName, out int cIdx) && cIdx >= 0 && cIdx < table.Columns.Count)
                        {
                            cellVal = row[cIdx];
                        }
                    }

                    if (cellVal == null)
                    {
                        cellVal = row[0];
                    }

                    dbResult.Value = cellVal == DBNull.Value ? null : cellVal;
                    dbResult.Text = dbResult.Value?.ToString() ?? "";
                    break;
                }

            case DbReadOutputFormat.ColumnJoin:
                {
                    string colName = nodeDef.TargetColumnName;
                    int cIdx = 0;
                    if (!string.IsNullOrWhiteSpace(colName) && table.Columns.Contains(colName))
                    {
                        cIdx = table.Columns[colName]!.Ordinal;
                    }
                    else if (int.TryParse(colName, out int parsedIdx) && parsedIdx >= 0 && parsedIdx < table.Columns.Count)
                    {
                        cIdx = parsedIdx;
                    }

                    string sep = string.IsNullOrEmpty(nodeDef.ColumnJoinSeparator) ? ", " : nodeDef.ColumnJoinSeparator;
                    var values = new List<string>();
                    foreach (DataRow row in table.Rows)
                    {
                        object val = row[cIdx];
                        values.Add(val == DBNull.Value ? "" : val.ToString() ?? "");
                    }

                    string joined = string.Join(sep, values);
                    dbResult.Value = joined;
                    dbResult.Text = joined;
                    break;
                }

            case DbReadOutputFormat.FullTableCsv:
                {
                    var sb = new StringBuilder();
                    // Header
                    var colNames = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                    sb.AppendLine(string.Join(",", colNames));

                    // Rows
                    foreach (DataRow row in table.Rows)
                    {
                        var fields = row.ItemArray.Select(field => field == DBNull.Value ? "" : EscapeCsv(field?.ToString() ?? ""));
                        sb.AppendLine(string.Join(",", fields));
                    }

                    string csv = sb.ToString().TrimEnd();
                    dbResult.Value = csv;
                    dbResult.Text = csv;
                    break;
                }

            case DbReadOutputFormat.FullTableJson:
                {
                    try
                    {
                        string json = JsonSerializer.Serialize(dbResult.Rows, new JsonSerializerOptions { WriteIndented = true });
                        dbResult.Value = json;
                        dbResult.Text = json;
                    }
                    catch
                    {
                        dbResult.Value = "[]";
                        dbResult.Text = "[]";
                    }
                    break;
                }
        }
    }

    private static string EscapeCsv(string str)
    {
        if (str.Contains(',') || str.Contains('"') || str.Contains('\n') || str.Contains('\r'))
        {
            return $"\"{str.Replace("\"", "\"\"")}\"";
        }
        return str;
    }

    public static string InterpolateSqlQuery(string sql, InspectionResult result, VisionConfig config)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;

        // Replaces tokens like {Distance1.Value}, {Origin.X}, {DB1.Text}, {TotalPassBit}, {TotalPass}
        return Regex.Replace(sql, @"\{([^}]+)\}", match =>
        {
            string token = match.Groups[1].Value.Trim();

            // First check if token is DB node output
            var dbTokenVal = ResolveDbResultToken(token, result);
            if (dbTokenVal != null) return dbTokenVal;

            // Otherwise evaluate via PlcResultTransferRunner logic
            object evaluated = PlcResultTransferRunner.EvaluateExpression(token, result, config);
            if (evaluated is string sVal) return FormatSqlValue(sVal);
            if (evaluated is bool bVal) return bVal ? "1" : "0";
            if (evaluated is double dVal) return dVal.ToString("F3", CultureInfo.InvariantCulture);
            if (evaluated is float fVal) return fVal.ToString("F3", CultureInfo.InvariantCulture);
            if (evaluated is int iVal) return iVal.ToString(CultureInfo.InvariantCulture);

            return FormatSqlValue(evaluated?.ToString() ?? "");
        });
    }

    private static string? ResolveDbResultToken(string token, InspectionResult result)
    {
        if (result.DbResults == null || result.DbResults.Count == 0) return null;

        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        string nodeName = parts[0].Trim();
        string propName = parts[1].Trim();

        var dbRes = result.DbResults.FirstOrDefault(r => string.Equals(r.NodeName, nodeName, StringComparison.OrdinalIgnoreCase));
        if (dbRes == null) return null;

        if (string.Equals(propName, "Value", StringComparison.OrdinalIgnoreCase))
            return FormatSqlValue(dbRes.Value?.ToString() ?? "");

        if (string.Equals(propName, "Text", StringComparison.OrdinalIgnoreCase))
            return FormatSqlValue(dbRes.Text);

        if (string.Equals(propName, "RowCount", StringComparison.OrdinalIgnoreCase))
            return dbRes.RowCount.ToString();

        if (string.Equals(propName, "ColumnCount", StringComparison.OrdinalIgnoreCase))
            return dbRes.ColumnCount.ToString();

        if (string.Equals(propName, "Success", StringComparison.OrdinalIgnoreCase))
            return dbRes.Success ? "1" : "0";

        if (dbRes.ColumnMap.TryGetValue(propName, out var colVal))
        {
            return FormatSqlValue(colVal?.ToString() ?? "");
        }

        return null;
    }

    private static string FormatSqlValue(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "''";

        // Escape single quotes for SQL safety
        return "'" + raw.Replace("'", "''") + "'";
    }

    public static (bool IsSafe, string ErrorMessage) ValidateSqlQuerySafety(string sql, DbNodeMode mode, bool allowUpdateDelete)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return (false, "Truy vấn SQL rỗng.");
        }

        string cleanSql = sql.Trim();

        // 1. Block catastrophic DDL commands (DROP, TRUNCATE, ALTER)
        if (Regex.IsMatch(cleanSql, @"\b(DROP\s+(TABLE|DATABASE|SCHEMA|VIEW|INDEX)|TRUNCATE\s+TABLE|TRUNCATE|ALTER\s+TABLE)\b", RegexOptions.IgnoreCase))
        {
            return (false, "❌ [DB SAFETY BLOCKED] Đã chặn lệnh phá hủy CSDL cực kỳ nguy hiểm (DROP / TRUNCATE / ALTER)!");
        }

        // 2. In Read Mode, block DELETE, UPDATE, INSERT, DROP, TRUNCATE, ALTER
        if (mode == DbNodeMode.Read)
        {
            if (Regex.IsMatch(cleanSql, @"\b(DELETE|UPDATE|INSERT|DROP|TRUNCATE|ALTER)\b", RegexOptions.IgnoreCase))
            {
                return (false, "❌ [DB SAFETY BLOCKED] Mode 'Read DB' chỉ cho phép câu lệnh SELECT. Không được dùng DELETE, UPDATE, INSERT, DROP!");
            }
        }

        // 3. For DELETE or UPDATE in Write mode, check WHERE clause and allowUpdateDelete permission
        bool isDelete = Regex.IsMatch(cleanSql, @"\bDELETE\b", RegexOptions.IgnoreCase);
        bool isUpdate = Regex.IsMatch(cleanSql, @"\bUPDATE\b", RegexOptions.IgnoreCase);

        if (isDelete || isUpdate)
        {
            bool hasWhere = Regex.IsMatch(cleanSql, @"\bWHERE\b", RegexOptions.IgnoreCase);
            if (!hasWhere)
            {
                return (false, $"❌ [DB SAFETY BLOCKED] Lệnh {(isDelete ? "DELETE" : "UPDATE")} nguy hiểm không có mệnh đề WHERE! Không được xóa/sửa toàn bộ bảng.");
            }

            if (!allowUpdateDelete)
            {
                return (false, $"❌ [DB SAFETY BLOCKED] Truy vấn chứa lệnh {(isDelete ? "DELETE" : "UPDATE")}. Vui lòng tích chọn 'Xác nhận cho phép UPDATE/DELETE' trong Properties Panel để thực thi.");
            }
        }

        return (true, string.Empty);
    }
}
