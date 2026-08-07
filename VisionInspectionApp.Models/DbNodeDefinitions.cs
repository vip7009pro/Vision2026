using System;

namespace VisionInspectionApp.Models;

public enum DbNodeMode
{
    Read = 0,
    Write = 1
}

public enum DbExecutionTiming
{
    BeforeFlow = 0,
    AfterFlow = 1
}

public enum DbReadOutputFormat
{
    FirstCell = 0,      // (Row 0, Col 0) - Scalar default
    SpecificCell = 1,   // (TargetRowIndex, TargetColumnName)
    ColumnJoin = 2,     // Join all values of TargetColumnName with ColumnJoinSeparator
    FullTableCsv = 3,   // Formatted CSV string of entire result set
    FullTableJson = 4   // JSON array string of entire result set
}

public class DbNodeDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RefName { get; set; } = "DB1";
    public string DbId { get; set; } = "";
    public string DbName { get; set; } = "";
    public DbNodeMode Mode { get; set; } = DbNodeMode.Read;
    public DbExecutionTiming Timing { get; set; } = DbExecutionTiming.AfterFlow;
    public string SqlQuery { get; set; } = "SELECT * FROM InspectionLogs WHERE Id = 1";
    public ImageOutputCondition Condition { get; set; } = ImageOutputCondition.Always;

    // Read Output Formatting Options
    public DbReadOutputFormat ReadFormat { get; set; } = DbReadOutputFormat.FirstCell;
    public int TargetRowIndex { get; set; } = 0;
    public string TargetColumnName { get; set; } = "";
    public string ColumnJoinSeparator { get; set; } = ", ";
    public string OutputVarName { get; set; } = "";

    // Safety & Permission Options
    public bool AllowUpdateDelete { get; set; } = false;

    public bool Enable { get; set; } = true;
}

public class DbResult
{
    public string NodeName { get; set; } = "";
    public bool Executed { get; set; } = false;
    public bool Success { get; set; } = false;
    public string ErrorMessage { get; set; } = "";
    public int RowsAffected { get; set; } = 0;
    public int RowCount { get; set; } = 0;
    public int ColumnCount { get; set; } = 0;
    
    // Extracted primary value (Scalar / Formatted according to ReadFormat)
    public object? Value { get; set; }
    public string Text { get; set; } = "";

    // Columns dictionary for Row 0 (or TargetRowIndex)
    public System.Collections.Generic.Dictionary<string, object> ColumnMap { get; set; } = new System.Collections.Generic.Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

    // Full tabular dataset representation (Rows as dictionaries)
    public System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> Rows { get; set; } = new();
}
