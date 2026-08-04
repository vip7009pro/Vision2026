using System.Collections.Generic;

namespace VisionInspectionApp.Models;

public sealed class PlcReadDefinition
{
    public string Name { get; set; } = string.Empty;

    public string PlcId { get; set; } = string.Empty;

    public string TagName { get; set; } = string.Empty;
}

public sealed class PlcWriteDefinition
{
    public string Name { get; set; } = string.Empty;

    public string PlcId { get; set; } = string.Empty;

    public string TagName { get; set; } = string.Empty;

    public string WriteValue { get; set; } = "0";

    public bool UseInputPort { get; set; } = true;
}

public sealed class PlcWaitDefinition
{
    public string Name { get; set; } = string.Empty;

    public string PlcId { get; set; } = string.Empty;

    public string TagName { get; set; } = string.Empty;

    public PlcCompareOperator Operator { get; set; } = PlcCompareOperator.Equal;

    public string TargetValue { get; set; } = "true";

    public int TimeoutMs { get; set; } = 5000;
}

public sealed class PlcTriggerDefinition
{
    public string Name { get; set; } = string.Empty;

    public string PlcId { get; set; } = string.Empty;

    public string TagName { get; set; } = string.Empty;

    public PlcTriggerEdge EdgeMode { get; set; } = PlcTriggerEdge.RisingEdge;
}

public sealed class PlcBatchReadDefinition
{
    public string Name { get; set; } = string.Empty;

    public string PlcId { get; set; } = string.Empty;

    public List<string> TagNames { get; set; } = new();
}

public sealed class PlcBatchWriteDefinition
{
    public string Name { get; set; } = string.Empty;

    public string PlcId { get; set; } = string.Empty;

    public Dictionary<string, string> TagValues { get; set; } = new();
}
