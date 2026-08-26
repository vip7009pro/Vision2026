using System;
using System.Diagnostics;
using System.Threading.Tasks;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application;

public partial class InspectionService
{
    private static void ExecuteDbNodes(VisionConfig config, InspectionResult result, DB.Services.IDbManagerService? dbManager, DbExecutionTiming timing)
    {
        if (config is null || result is null || dbManager is null) return;

        try
        {
            var task = DB.Services.DbNodeRunner.ExecuteDbNodesAsync(config, result, dbManager, timing);
            if (!task.Wait(500))
            {
                System.Diagnostics.Debug.WriteLine($"[DB NODE RUNNER TIMEOUT ({timing})]");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DB NODE RUNNER ERROR ({timing})] {ex.Message}");
        }
    }

    private static void ExecutePlcNodes(VisionConfig config, InspectionResult result, PLC.Services.IPlcManagerService? plcManager)
    {
        if (config is null || result is null) return;

        // 1. PlcReads
        if (config.PlcReads != null)
        {
            foreach (var r in config.PlcReads)
            {
                var __swNode = Stopwatch.StartNew();
                var val = plcManager?.GetTagValue(r.PlcId, r.TagName);
                __swNode.Stop();
                result.Timings.NodeTimings[r.Name] = (int)__swNode.ElapsedMilliseconds;
                result.PlcReads.Add(new PlcReadResult(r.Name, r.PlcId, r.TagName, val?.CurrentValue, val != null));
            }
        }

        // 2. PlcWrites
        if (config.PlcWrites != null)
        {
            foreach (var w in config.PlcWrites)
            {
                var __swNode = Stopwatch.StartNew();
                bool ok = false;
                if (plcManager != null && !string.IsNullOrWhiteSpace(w.PlcId) && !string.IsNullOrWhiteSpace(w.TagName))
                {
                    try
                    {
                        var writeTask = plcManager.WriteTagValueAsync(w.PlcId, w.TagName, w.WriteValue);
                        if (writeTask.Wait(50))
                        {
                            ok = writeTask.Result;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PLC WRITE ERROR] {w.TagName}: {ex.Message}");
                    }
                }
                __swNode.Stop();
                result.Timings.NodeTimings[w.Name] = (int)__swNode.ElapsedMilliseconds;
                result.PlcWrites.Add(new PlcWriteResult(w.Name, w.PlcId, w.TagName, w.WriteValue, ok));
            }
        }

        // 3. PlcWaits
        if (config.PlcWaits != null)
        {
            foreach (var wt in config.PlcWaits)
            {
                var __swNode = Stopwatch.StartNew();
                bool pass = false;
                int timeoutMs = Math.Max(10, wt.TimeoutMs);
                while (__swNode.ElapsedMilliseconds <= timeoutMs)
                {
                    var val = plcManager?.GetTagValue(wt.PlcId, wt.TagName);
                    if (val != null && CompareValues(val.CurrentValue, wt.Operator, wt.TargetValue))
                    {
                        pass = true;
                        break;
                    }
                    if (timeoutMs > 50) System.Threading.Thread.Sleep(10);
                }
                __swNode.Stop();
                result.Timings.NodeTimings[wt.Name] = (int)__swNode.ElapsedMilliseconds;
                result.PlcWaits.Add(new PlcWaitResult(wt.Name, wt.PlcId, wt.TagName, wt.Operator, wt.TargetValue, pass, __swNode.ElapsedMilliseconds));
            }
        }

        // 4. PlcTriggers
        if (config.PlcTriggers != null)
        {
            foreach (var tr in config.PlcTriggers)
            {
                var __swNode = Stopwatch.StartNew();
                var val = plcManager?.GetTagValue(tr.PlcId, tr.TagName);
                bool triggered = false;
                if (val != null)
                {
                    bool cur = ConvertToBool(val.CurrentValue);
                    bool prev = ConvertToBool(val.PreviousValue);
                    triggered = tr.EdgeMode switch
                    {
                        PlcTriggerEdge.RisingEdge => !prev && cur,
                        PlcTriggerEdge.FallingEdge => prev && !cur,
                        PlcTriggerEdge.Changed => prev != cur,
                        _ => false
                    };
                }
                __swNode.Stop();
                result.Timings.NodeTimings[tr.Name] = (int)__swNode.ElapsedMilliseconds;
            }
        }

        // 5. ResultTransfers (Đẩy vào Dedicated Async Queue: 0ms cho luồng chính, background worker tuần tự truyền PLC)
        if (config.ResultTransfers != null && config.ResultTransfers.Count > 0 && plcManager != null)
        {
            try
            {
                // Gán ngay runtime của lần gửi trước đó để UI hiển thị đầy đủ
                foreach (var rt in config.ResultTransfers)
                {
                    if (!string.IsNullOrWhiteSpace(rt.Name))
                    {
                        result.Timings.NodeTimings[rt.Name] = PLC.Services.PlcResultTransferQueue.GetLatestTiming(rt.Name);
                    }
                }

                // Enqueue không khóa, không await (0ms), Background Queue Worker sẽ tự gửi tuần tự
                PLC.Services.PlcResultTransferQueue.Enqueue(config, result, plcManager);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PLC RESULT TRANSFER ENQUEUE ERROR] {ex.Message}");
            }
        }
    }

    private static bool ConvertToBool(object? obj)
    {
        if (obj is bool b) return b;
        if (obj is int i) return i != 0;
        if (obj is double d) return d != 0;
        if (obj != null && bool.TryParse(obj.ToString(), out bool bRes)) return bRes;
        return false;
    }

    private static bool CompareValues(object? curVal, PlcCompareOperator op, string targetStr)
    {
        if (curVal == null) return false;

        if (double.TryParse(curVal.ToString(), out double curD) && double.TryParse(targetStr, out double tgtD))
        {
            return op switch
            {
                PlcCompareOperator.Equal => Math.Abs(curD - tgtD) < 1e-6,
                PlcCompareOperator.NotEqual => Math.Abs(curD - tgtD) >= 1e-6,
                PlcCompareOperator.GreaterThan => curD > tgtD,
                PlcCompareOperator.LessThan => curD < tgtD,
                PlcCompareOperator.GreaterOrEqual => curD >= tgtD,
                PlcCompareOperator.LessOrEqual => curD <= tgtD,
                _ => false
            };
        }

        string curStr = curVal.ToString() ?? string.Empty;
        int comp = string.Compare(curStr, targetStr, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            PlcCompareOperator.Equal => comp == 0,
            PlcCompareOperator.NotEqual => comp != 0,
            PlcCompareOperator.GreaterThan => comp > 0,
            PlcCompareOperator.LessThan => comp < 0,
            PlcCompareOperator.GreaterOrEqual => comp >= 0,
            PlcCompareOperator.LessOrEqual => comp <= 0,
            _ => false
        };
    }

    private static void EvaluateConditions(VisionConfig config, InspectionResult result)
    {
        if (config.Conditions is null || config.Conditions.Count == 0)
        {
            return;
        }

        var vars = ConditionEvaluator.BuildVariableMap(result, config);
        foreach (var c in config.Conditions)
        {
            if (string.IsNullOrWhiteSpace(c.Name))
            {
                continue;
            }

            var expr = c.Expression ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expr))
            {
                result.Conditions.Add(new ConditionResult(c.Name, expr, false, "Empty expression"));
                continue;
            }

            try
            {
                var ok = ConditionEvaluator.Evaluate(expr, vars);
                result.Conditions.Add(new ConditionResult(c.Name, expr, ok, null));
            }
            catch (Exception ex)
            {
                result.Conditions.Add(new ConditionResult(c.Name, expr, false, ex.Message));
            }
        }
    }
}
