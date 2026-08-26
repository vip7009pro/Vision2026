using System;
using System.Collections.Generic;
using System.Linq;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.Application.Services;

/// <summary>
/// Động cơ tính toán phân tích thống kê quá trình SPC (Statistical Process Control) & CPK chuẩn Shewhart
/// </summary>
public static class SpcEngine
{
    // Bảng tra cứu hệ số kiểm soát chuẩn Shewhart (A2, D3, D4, d2) cho cỡ mẫu n = 2..50
    private static readonly Dictionary<int, (double A2, double D3, double D4, double d2)> ShewhartConstants = new()
    {
        { 2,  (1.880, 0.000, 3.267, 1.128) },
        { 3,  (1.023, 0.000, 2.574, 1.693) },
        { 4,  (0.729, 0.000, 2.282, 2.059) },
        { 5,  (0.577, 0.000, 2.114, 2.326) },
        { 6,  (0.483, 0.000, 2.004, 2.534) },
        { 7,  (0.419, 0.076, 1.924, 2.704) },
        { 8,  (0.373, 0.136, 1.864, 2.847) },
        { 9,  (0.337, 0.184, 1.816, 2.970) },
        { 10, (0.308, 0.223, 1.777, 3.078) },
        { 12, (0.266, 0.283, 1.717, 3.258) },
        { 15, (0.223, 0.347, 1.653, 3.472) },
        { 20, (0.180, 0.415, 1.585, 3.735) },
        { 25, (0.153, 0.459, 1.541, 3.931) },
        { 30, (0.134, 0.490, 1.510, 4.086) },
        { 32, (0.128, 0.501, 1.499, 4.140) },
        { 40, (0.110, 0.530, 1.470, 4.300) },
        { 50, (0.094, 0.555, 1.445, 4.498) }
    };

    public static (double A2, double D3, double D4, double d2) GetConstants(int n)
    {
        if (ShewhartConstants.TryGetValue(n, out var val))
            return val;

        // Xấp xỉ cho n lớn
        double d2 = 3.97 * Math.Pow(n, 0.025);
        if (n >= 30) d2 = 4.0 + 0.015 * (n - 30);
        double A2 = 3.0 / (d2 * Math.Sqrt(n));
        double D3 = Math.Max(0.0, 1.0 - 3.0 * (0.8 / Math.Sqrt(2 * n)));
        double D4 = 1.0 + 3.0 * (0.8 / Math.Sqrt(2 * n));
        return (A2, D3, D4, d2);
    }

    /// <summary>
    /// Thực hiện phân tích SPC & CPK cho danh sách mẫu đo
    /// </summary>
    public static SpcAnalysisResult Analyze(
        string itemName,
        IReadOnlyList<double> rawValues,
        double nominal,
        double tolPlus,
        double tolMinus,
        string unit = "mm",
        int requestedSubgroupSizeN = 32)
    {
        var result = new SpcAnalysisResult
        {
            ItemName = itemName,
            Unit = unit,
            Target = nominal,
            Lsl = nominal - Math.Abs(tolMinus),
            Usl = nominal + Math.Abs(tolPlus),
            TotalSamples = rawValues?.Count ?? 0
        };

        if (rawValues == null || rawValues.Count == 0)
            return result;

        var values = rawValues.ToList();
        int totalN = values.Count;

        // 1. Quy tắc cỡ mẫu n:
        // Mặc định n = 32. Nếu tổng mẫu < 32 thì giảm về 5. Nếu tổng mẫu < 5 thì dùng toàn bộ (n = totalN).
        int n = requestedSubgroupSizeN > 0 ? requestedSubgroupSizeN : 32;
        if (totalN < n)
        {
            n = totalN >= 5 ? 5 : Math.Max(2, totalN);
        }
        result.SubgroupSizeN = n;

        // 2. Chia nhóm con k, bỏ qua phần dư
        int k = totalN / n;
        result.SubgroupCountK = k;
        result.DroppedRemainder = totalN % n;

        if (k <= 0)
        {
            // Nếu không đủ cả 1 nhóm đầy đủ, gom toàn bộ vào 1 nhóm tạm
            k = 1;
            n = totalN;
            result.SubgroupSizeN = n;
            result.SubgroupCountK = 1;
            result.DroppedRemainder = 0;
        }

        var (a2, d3, d4, d2) = GetConstants(n);

        var subgroups = new List<SpcSubgroupData>();
        double sumXbar = 0;
        double sumR = 0;

        for (int i = 0; i < k; i++)
        {
            var subValues = values.Skip(i * n).Take(n).ToList();
            double mean = subValues.Average();
            double min = subValues.Min();
            double max = subValues.Max();
            double range = max - min;

            // Độ lệch chuẩn nhóm con (Sample Standard Deviation)
            double sumSq = subValues.Sum(v => Math.Pow(v - mean, 2));
            double sigma = n > 1 ? Math.Sqrt(sumSq / (n - 1)) : 0.0001;

            double cpk = 0;
            if (sigma > 1e-9)
            {
                double cpu = (result.Usl - mean) / (3 * sigma);
                double cpl = (mean - result.Lsl) / (3 * sigma);
                cpk = Math.Min(cpu, cpl);
            }

            var sg = new SpcSubgroupData
            {
                GroupIndex = i + 1,
                Mean = mean,
                Min = min,
                Max = max,
                Range = range,
                Sigma = sigma,
                Cpk = cpk,
                Values = subValues
            };
            subgroups.Add(sg);

            sumXbar += mean;
            sumR += range;
        }

        result.Subgroups = subgroups;

        // 3. Tính đường trung tâm và các giới hạn kiểm soát
        double grandMean = sumXbar / k;
        double rBar = sumR / k;

        result.OverallMean = values.Average();
        result.OverallMin = values.Min();
        result.OverallMax = values.Max();

        // Độ lệch chuẩn toàn thể (Overall Sigma)
        double totalSumSq = values.Sum(v => Math.Pow(v - result.OverallMean, 2));
        double overallSigma = totalN > 1 ? Math.Sqrt(totalSumSq / (totalN - 1)) : 0.0001;
        result.OverallSigma = overallSigma;

        // Giới hạn kiểm soát X-bar
        result.Xbar_CL = grandMean;
        result.Xbar_UCL = grandMean + a2 * rBar;
        result.Xbar_LCL = grandMean - a2 * rBar;

        // Giới hạn kiểm soát R
        result.R_CL = rBar;
        result.R_UCL = d4 * rBar;
        result.R_LCL = d3 * rBar;

        // 4. Tính toán chỉ số Cp, Cpk, Cpu, Cpl toàn bộ
        double specWidth = result.Usl - result.Lsl;
        if (overallSigma > 1e-9 && specWidth > 1e-9)
        {
            result.Cp = specWidth / (6 * overallSigma);
            result.Cpu = (result.Usl - result.OverallMean) / (3 * overallSigma);
            result.Cpl = (result.OverallMean - result.Lsl) / (3 * overallSigma);
            result.Cpk = Math.Min(result.Cpu, result.Cpl);
        }
        else
        {
            result.Cp = 0;
            result.Cpk = 0;
        }

        // 5. Tạo dữ liệu biểu đồ Histogram (15-25 Bins) + Đường phân bố chuẩn Gauss
        result.HistogramBins = BuildHistogram(values, result.OverallMean, result.OverallSigma, result.Lsl, result.Usl);

        return result;
    }

    private static List<HistogramBinData> BuildHistogram(List<double> values, double mean, double sigma, double lsl, double usl)
    {
        var bins = new List<HistogramBinData>();
        if (values == null || values.Count == 0) return bins;

        double minVal = Math.Min(values.Min(), lsl);
        double maxVal = Math.Max(values.Max(), usl);
        double padding = (maxVal - minVal) * 0.08;
        if (padding < 1e-4) padding = 0.5;

        double start = minVal - padding;
        double end = maxVal + padding;
        int numBins = Math.Clamp((int)Math.Sqrt(values.Count) * 2, 12, 28);
        double binWidth = (end - start) / numBins;

        for (int i = 0; i < numBins; i++)
        {
            double bStart = start + i * binWidth;
            double bEnd = bStart + binWidth;
            double bCenter = (bStart + bEnd) / 2.0;

            int count = values.Count(v => v >= bStart && (i == numBins - 1 ? v <= bEnd : v < bEnd));

            // Tính hàm mật độ xác suất Gauss (Normal PDF)
            double gaussY = 0;
            if (sigma > 1e-9)
            {
                double z = (bCenter - mean) / sigma;
                double pdf = (1.0 / (sigma * Math.Sqrt(2 * Math.PI))) * Math.Exp(-0.5 * z * z);
                gaussY = pdf * values.Count * binWidth; // Quy đổi ra số lượng tương đương
            }

            bins.Add(new HistogramBinData
            {
                BinIndex = i + 1,
                BinStart = bStart,
                BinEnd = bEnd,
                Count = count,
                NormalCurveHeight = gaussY
            });
        }

        return bins;
    }
}
