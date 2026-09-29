using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Models;
using VisionInspectionApp.VisionEngine;
using ZXing;
using ZXing.Common;

namespace VisionInspectionApp.Application.Services;

/// <summary>Loại công cụ nhận diện được hỗ trợ Auto Tune preprocess.</summary>
public enum AutoTuneToolKind
{
    None = 0,
    Caliper = 1,
    Line = 2,
    EdgePairDetect = 3,
    CircleFinder = 4,
    CodeDetection = 5
}

/// <summary>Thông tin tiến trình Auto Tune để hiển thị lên giao diện.</summary>
public sealed record AutoTuneProgress(int Step, int Total, string ParameterText, double BestScore, string Message);

/// <summary>Kết quả sau khi Auto Tune.</summary>
public sealed record AutoTuneOutcome(bool Success, PreprocessSettings BestSettings, double BestScore, string BestDescription, string Summary);

/// <summary>
/// Bộ máy Auto Tune: thử nhiều cấu hình Preprocess và chấm điểm theo kết quả nhận diện của
/// công cụ (Caliper / Line / EdgePairDetect / CircleFinder / CodeDetection), sau đó trả về
/// cấu hình cho điểm cao nhất để gán lại vào tool Preprocess cha.
/// </summary>
public static class PreprocessAutoTuner
{
    public static AutoTuneToolKind ResolveKind(string? toolType)
    {
        if (string.IsNullOrWhiteSpace(toolType)) return AutoTuneToolKind.None;
        if (toolType.Equals("Caliper", StringComparison.OrdinalIgnoreCase)) return AutoTuneToolKind.Caliper;
        if (toolType.Equals("Line", StringComparison.OrdinalIgnoreCase)) return AutoTuneToolKind.Line;
        if (toolType.Equals("EdgePairDetect", StringComparison.OrdinalIgnoreCase)) return AutoTuneToolKind.EdgePairDetect;
        if (toolType.Equals("CircleFinder", StringComparison.OrdinalIgnoreCase)) return AutoTuneToolKind.CircleFinder;
        if (toolType.Equals("CodeDetection", StringComparison.OrdinalIgnoreCase)) return AutoTuneToolKind.CodeDetection;
        return AutoTuneToolKind.None;
    }

    public static AutoTuneOutcome Tune(
        Mat baseImage,
        AutoTuneToolKind kind,
        CaliperDefinition? caliper,
        LineToolDefinition? line,
        EdgePairDetectDefinition? edgePair,
        CircleFinderDefinition? circle,
        CodeDetectionDefinition? code,
        PreprocessSettings baseline,
        List<PreprocessRoiDefinition>? rois,
        Point2d originTeach,
        Point2d originFound,
        double originAngleDeg,
        ImagePreprocessor preprocessor,
        LineDetector lineDetector,
        IProgress<AutoTuneProgress>? progress,
        CancellationToken ct)
    {
        if (baseImage is null || baseImage.Empty() || preprocessor is null)
        {
            return new AutoTuneOutcome(false, CloneSettings(baseline), 0.0, string.Empty, "Không có ảnh đầu vào để Auto Tune.");
        }

        var bestScore = double.NegativeInfinity;
        PreprocessSettings bestSettings = CloneSettings(baseline);
        var bestDesc = string.Empty;
        var evaluatorCount = 0;
        var bestLock = new object();

        // ĐA LUỒNG ĐA NHÂN: chừa 1 nhân cho UI; giới hạn theo kích thước ảnh để tránh đỉnh RAM quá lớn
        // (mỗi ứng viên giữ 1 ảnh đã preprocess full-size ~60MB với ảnh 20MP).
        var pixels = (long)baseImage.Width * baseImage.Height;
        var perImageCap = pixels > 2_000_000 ? 4 : 8;
        var maxParallel = Math.Clamp(Environment.ProcessorCount - 1, 1, perImageCap);
        var parallelOptions = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = maxParallel };

        // Stage 1: danh sách preset bao quát nhiều phương pháp.
        // Stage 2: COORDINATE DESCENT — quét toàn bộ giá trị của TỪNG nhóm thông số
        //          (màu, chiếu sáng, khử nhiễu, tông màu, cạnh, nhị phân, morphology, đảo)
        //          và lặp lại nhiều vòng cho tới khi hội tụ => bao phủ tổ hợp thông số sâu hơn.
        const int maxPasses = 3;
        var presets = BuildCandidates(CloneSettings(baseline), kind);
        var groups = BuildGroups();
        var totalUpper = presets.Count + groups.Sum(g => g.Options.Count) * maxPasses + 8;

        // Chấm điểm 1 ứng viên. AN TOÀN ĐA LUỒNG: chỉ đọc dữ liệu dùng chung (ảnh, định nghĩa tool),
        // tự tạo và thu hồi Mat riêng cho mỗi lần chạy, cập nhật "điểm cao nhất" qua lock.
        double Evaluate(PreprocessSettings settings, string desc)
        {
            ct.ThrowIfCancellationRequested();
            Mat? processed = null;
            double score;
            try
            {
                processed = preprocessor.Run(baseImage, settings, rois, originTeach, originFound, originAngleDeg);
                score = (processed is null || processed.Empty())
                    ? 0.0
                    : Score(processed, kind, caliper, line, edgePair, circle, code, originTeach, originFound, originAngleDeg, lineDetector);
            }
            catch
            {
                score = 0.0;
            }
            finally
            {
                processed?.Dispose();
            }

            var n = Interlocked.Increment(ref evaluatorCount);

            double snapshotBest;
            lock (bestLock)
            {
                if (score > bestScore)
                {
                    bestScore = score;
                    bestSettings = CloneSettings(settings);
                    bestDesc = desc;
                }
                snapshotBest = bestScore;
            }

            progress?.Report(new AutoTuneProgress(n, totalUpper, desc, snapshotBest, $"Đang thử: {desc} → điểm {score:0.00} (cao nhất {snapshotBest:0.00})"));
            return score;
        }

        // ---- Baseline ----
        var baselineScore = Evaluate(CloneSettings(baseline), "Giữ nguyên (baseline)");

        // ---- Stage 1: presets (chạy SONG SONG) ----
        Parallel.For(0, presets.Count, parallelOptions, i =>
        {
            Evaluate(presets[i].Settings, presets[i].Desc);
        });

        // ---- Stage 2: coordinate descent (mỗi nhóm quét SONG SONG các giá trị) ----
        var current = CloneSettings(bestSettings);
        var currentScore = bestScore;

        for (var pass = 0; pass < maxPasses; pass++)
        {
            var improvedInPass = false;

            foreach (var group in groups)
            {
                var optionCount = group.Options.Count;
                var results = new double[optionCount];

                Parallel.For(0, optionCount, parallelOptions, i =>
                {
                    var cand = CloneSettings(current);
                    group.Options[i].Apply(cand);
                    results[i] = Evaluate(cand, $"[{group.Name}] {group.Options[i].Desc}");
                });

                // Chọn giá trị tốt nhất của nhóm (ưu tiên thứ tự đầu tiên khi bằng điểm => tất định).
                var groupBestIdx = -1;
                var groupBestScore = currentScore;
                for (var i = 0; i < optionCount; i++)
                {
                    if (results[i] > groupBestScore + 1e-6)
                    {
                        groupBestScore = results[i];
                        groupBestIdx = i;
                    }
                }

                if (groupBestIdx >= 0)
                {
                    var cand = CloneSettings(current);
                    group.Options[groupBestIdx].Apply(cand);
                    current = cand;
                    currentScore = groupBestScore;
                    improvedInPass = true;
                }
            }

            if (!improvedInPass)
            {
                break; // Đã hội tụ
            }
        }

        var improved = bestScore > baselineScore + 1e-6;
        var summary = bestDesc.Length == 0
            ? "Không tìm được cấu hình phù hợp."
            : $"Cấu hình tốt nhất: {bestDesc} (điểm {bestScore:0.00}, baseline {baselineScore:0.00}, đã thử {evaluatorCount} tổ hợp trên {maxParallel} luồng).";

        // Nếu không có cải thiện, vẫn trả về baseline để giữ nguyên hành vi cũ.
        return new AutoTuneOutcome(true, improved ? bestSettings : CloneSettings(baseline), improved ? bestScore : baselineScore, bestDesc, summary);
    }

    // ============================ NHÓM THÔNG SỐ (COORDINATE DESCENT) ============================

    private sealed record ParamOption(string Desc, Action<PreprocessSettings> Apply);

    private static List<(string Name, List<ParamOption> Options)> BuildGroups()
    {
        var groups = new List<(string, List<ParamOption>)>();

        // --- Màu / Kênh ---
        var color = new List<ParamOption>();
        void AddColor(string desc, PreprocessColorChannel ch) => color.Add(new ParamOption(desc, s => s.ColorChannel = ch));
        AddColor("Kênh All", PreprocessColorChannel.All);
        AddColor("Kênh Red", PreprocessColorChannel.Red);
        AddColor("Kênh Green", PreprocessColorChannel.Green);
        AddColor("Kênh Blue", PreprocessColorChannel.Blue);
        AddColor("Kênh Hue", PreprocessColorChannel.Hue);
        AddColor("Kênh Saturation", PreprocessColorChannel.Saturation);
        AddColor("Kênh Value", PreprocessColorChannel.Value);
        AddColor("Kênh Lab-L", PreprocessColorChannel.Lab_L);
        groups.Add(("Màu", color));

        // --- Chiếu sáng ---
        var illum = new List<ParamOption>
        {
            new("Tắt", s => { s.IlluminationCorrection = IlluminationCorrectionPreset.None; s.IlluminationKernel = 51; })
        };
        foreach (var k in new[] { 15, 31, 51, 101, 201 })
        {
            var kk = k;
            illum.Add(new($"BackgroundSubtract k{kk}", s => { s.IlluminationCorrection = IlluminationCorrectionPreset.BackgroundSubtract; s.IlluminationKernel = kk; }));
        }
        foreach (var k in new[] { 51, 101 })
        {
            var kk = k;
            illum.Add(new($"FlatField k{kk}", s => { s.IlluminationCorrection = IlluminationCorrectionPreset.FlatFieldNormalize; s.IlluminationKernel = kk; }));
        }
        illum.Add(new("CLAHE clip2 tile8", s => { s.IlluminationCorrection = IlluminationCorrectionPreset.Clahe; s.ClaheClipLimit = 2; s.ClaheTileGrid = 8; }));
        illum.Add(new("CLAHE clip3 tile8", s => { s.IlluminationCorrection = IlluminationCorrectionPreset.Clahe; s.ClaheClipLimit = 3; s.ClaheTileGrid = 8; }));
        illum.Add(new("CLAHE clip4 tile16", s => { s.IlluminationCorrection = IlluminationCorrectionPreset.Clahe; s.ClaheClipLimit = 4; s.ClaheTileGrid = 16; }));
        groups.Add(("Chiếu sáng", illum));

        // --- Khử nhiễu ---
        var denoise = new List<ParamOption>
        {
            new("Tắt", s => { s.UseGaussianBlur = false; s.UseMedianBlur = false; s.UseBilateralFilter = false; })
        };
        foreach (var k in new[] { 3, 5, 7, 9 })
        {
            var kk = k;
            denoise.Add(new($"Gaussian {kk}", s => { s.UseGaussianBlur = true; s.BlurKernel = kk; s.UseMedianBlur = false; s.UseBilateralFilter = false; }));
        }
        foreach (var k in new[] { 3, 5, 7 })
        {
            var kk = k;
            denoise.Add(new($"Median {kk}", s => { s.UseMedianBlur = true; s.MedianKernel = kk; s.UseGaussianBlur = false; s.UseBilateralFilter = false; }));
        }
        foreach (var d in new[] { 3, 5, 9 })
        {
            var dd = d;
            denoise.Add(new($"Bilateral {dd}", s => { s.UseBilateralFilter = true; s.BilateralDiameter = dd; s.UseGaussianBlur = false; s.UseMedianBlur = false; }));
        }
        groups.Add(("Khử nhiễu", denoise));

        // --- Tông màu ---
        var tone = new List<ParamOption>
        {
            new("Tắt", s => { s.UseGamma = false; s.UseAutoContrast = false; s.InvertColors = false; })
        };
        foreach (var g in new[] { 0.5, 0.7, 1.5, 2.0 })
        {
            var gg = g;
            tone.Add(new($"Gamma {gg:0.0}", s => { s.UseGamma = true; s.GammaValue = gg; }));
        }
        tone.Add(new("Auto Contrast", s => s.UseAutoContrast = true));
        tone.Add(new("Đảo màu", s => s.InvertColors = true));
        groups.Add(("Tông màu", tone));

        // --- Cạnh (Canny / Gradient / AutoEdge) ---
        var edge = new List<ParamOption>
        {
            new("Tắt", s => { s.UseCanny = false; s.GradientType = PreprocessGradientType.None; s.UseAutoEdge = false; })
        };
        foreach (var (c1, c2) in new[] { (30, 90), (50, 150), (80, 200), (100, 250) })
        {
            edge.Add(new($"Canny {c1}/{c2}", s => { s.UseCanny = true; s.Canny1 = c1; s.Canny2 = c2; s.GradientType = PreprocessGradientType.None; s.UseAutoEdge = false; }));
        }
        foreach (var (type, name) in new[]
        {
            (PreprocessGradientType.Sobel, "Sobel 3"),
            (PreprocessGradientType.Scharr, "Scharr 3"),
            (PreprocessGradientType.Laplacian, "Laplacian 3"),
            (PreprocessGradientType.MorphGradient, "MorphGradient 3")
        })
        {
            var tt = type;
            edge.Add(new($"Gradient {name}", s => { s.GradientType = tt; s.GradientKernel = 3; s.UseCanny = false; s.UseAutoEdge = false; }));
        }
        foreach (var (method, name) in new[]
        {
            (AutoEdgeMethod.Ensemble, "Ensemble"),
            (AutoEdgeMethod.ScharrOtsu, "Scharr+Otsu"),
            (AutoEdgeMethod.BackgroundDiffTriangle, "BackgroundDiff+Triangle"),
            (AutoEdgeMethod.MorphGradientSauvola, "MorphGradient+Sauvola"),
            (AutoEdgeMethod.LabLumaOtsu, "LabLuma+Otsu")
        })
        {
            var mm = method;
            edge.Add(new($"AutoEdge {name}", s => { s.UseAutoEdge = true; s.AutoEdgeMethod = mm; s.AutoEdgeMinConfidence = 0.5; s.UseCanny = false; s.GradientType = PreprocessGradientType.None; }));
        }
        groups.Add(("Cạnh", edge));

        // --- Nhị phân hóa ---
        var binarize = new List<ParamOption>
        {
            new("Tắt", s => s.UseThreshold = false)
        };
        foreach (var v in new[] { 64, 96, 128, 160, 192, 224 })
        {
            var vv = v;
            binarize.Add(new($"Binary {vv}", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Binary; s.ThresholdLow = vv; }));
        }
        binarize.Add(new("Otsu", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Otsu; }));
        binarize.Add(new("Triangle", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Triangle; }));
        foreach (var (m, off) in new[] { (11, 5), (21, 5), (31, 10), (51, 10) })
        {
            binarize.Add(new($"Local mask{m} off{off}", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Local; s.MaskWidth = m; s.MaskHeight = m; s.LocalOffset = off; }));
        }
        foreach (var (k, m) in new[] { (0.1, 15), (0.2, 15), (0.3, 31) })
        {
            binarize.Add(new($"Sauvola k{k} mask{m}", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Sauvola; s.SauvolaK = k; s.MaskWidth = m; s.MaskHeight = m; }));
        }
        groups.Add(("Nhị phân", binarize));

        // --- Đảo ---
        var invert = new List<ParamOption>
        {
            new("Bình thường", s => { s.InvertBinary = false; s.InvertLocal = false; s.AutoEdgeInvert = false; }),
            new("Đảo nhị phân/cục bộ", s => { s.InvertBinary = true; s.InvertLocal = true; s.AutoEdgeInvert = true; })
        };
        groups.Add(("Đảo", invert));

        // --- Morphology ---
        var morph = new List<ParamOption>
        {
            new("Tắt", s => s.UseMorphology = false)
        };
        void AddMorph(string desc, PreprocessMorphType type, int k, int iter) =>
            morph.Add(new(desc, s => { s.UseMorphology = true; s.MorphType = type; s.MorphKernelSize = k; s.MorphIterations = iter; }));
        AddMorph("Close 3", PreprocessMorphType.Close, 3, 1);
        AddMorph("Close 5", PreprocessMorphType.Close, 5, 1);
        AddMorph("Open 3", PreprocessMorphType.Open, 3, 1);
        AddMorph("Open 5", PreprocessMorphType.Open, 5, 1);
        AddMorph("Erode 3", PreprocessMorphType.Erode, 3, 1);
        AddMorph("Dilate 3", PreprocessMorphType.Dilate, 3, 1);
        AddMorph("Close 3 x2", PreprocessMorphType.Close, 3, 2);
        groups.Add(("Morphology", morph));

        return groups;
    }

    // ============================ CHẤM ĐIỂM ============================

    private static double Score(
        Mat processed,
        AutoTuneToolKind kind,
        CaliperDefinition? caliper,
        LineToolDefinition? line,
        EdgePairDetectDefinition? edgePair,
        CircleFinderDefinition? circle,
        CodeDetectionDefinition? code,
        Point2d ot, Point2d of, double ang,
        LineDetector lineDetector)
    {
        switch (kind)
        {
            case AutoTuneToolKind.Caliper when caliper is not null:
                return ScoreCaliper(processed, caliper, ot, of, ang);

            case AutoTuneToolKind.EdgePairDetect when edgePair is not null:
                return ScoreEdgePair(processed, edgePair, ot, of, ang);

            case AutoTuneToolKind.Line when line is not null:
                return ScoreLine(processed, line, ot, of, ang, lineDetector);

            case AutoTuneToolKind.CircleFinder when circle is not null:
                return ScoreCircle(processed, circle, ot, of, ang);

            case AutoTuneToolKind.CodeDetection when code is not null:
                return ScoreCode(processed, code, ot, of, ang);

            default:
                return 0.0;
        }
    }

    private static double ScoreCaliper(Mat processed, CaliperDefinition def, Point2d ot, Point2d of, double ang)
    {
        var res = CaliperDetector.Detect(processed, def, ot, of, ang);
        if (!res.Found)
        {
            return res.AvgStrength * 0.1; // vẫn thưởng nhẹ nếu có vài điểm mạnh
        }
        var coverage = Math.Min(res.Points.Count, Math.Max(1, def.StripCount)) / (double)Math.Max(1, def.StripCount);
        return res.AvgStrength + coverage * 50.0;
    }

    private static double ScoreEdgePair(Mat processed, EdgePairDetectDefinition def, Point2d ot, Point2d of, double ang)
    {
        var proxy = new CaliperDefinition
        {
            Name = def.Name,
            SearchRoi = def.SearchRoi,
            Orientation = def.Orientation,
            Polarity = def.Polarity,
            StripCount = def.StripCount,
            StripWidth = def.StripWidth,
            StripLength = def.StripLength,
            MinEdgeStrength = def.MinEdgeStrength
        };
        return ScoreCaliper(processed, proxy, ot, of, ang);
    }

    private static double ScoreLine(Mat processed, LineToolDefinition def, Point2d ot, Point2d of, double ang, LineDetector lineDetector)
    {
        if (lineDetector is null || def.SearchRoi.Width <= 0 || def.SearchRoi.Height <= 0) return 0.0;
        var det = lineDetector.DetectLongestLine(processed, def.SearchRoi, def.Canny1, def.Canny2, def.HoughThreshold, def.MinLineLength, def.MaxLineGap, ot, of, ang);
        if (!det.Found) return 0.0;
        var diag = Math.Sqrt(def.SearchRoi.Width * (double)def.SearchRoi.Width + def.SearchRoi.Height * (double)def.SearchRoi.Height);
        var norm = diag > 1e-6 ? det.LengthPx / diag : 0.0;
        return norm * 100.0;
    }

    private static double ScoreCircle(Mat processed, CircleFinderDefinition def, Point2d ot, Point2d of, double ang)
    {
        using var patch = Geometry2D.ExtractStraightRoi(processed, def.SearchRoi, ot, of, ang, out _);
        if (patch.Empty()) return 0.0;

        using var grayOwned = patch.Channels() == 1 ? null : patch.CvtColor(ColorConversionCodes.BGR2GRAY);
        Mat gray = grayOwned ?? patch;

        if (def.Algorithm == CircleFindAlgorithm.RadialCaliper)
        {
            return ScoreCircleRadial(gray, def);
        }

        if (def.Algorithm == CircleFindAlgorithm.HoughCircles)
        {
            using var blur = new Mat();
            Cv2.GaussianBlur(gray, blur, new Size(0, 0), 1.2);
            var minR = Math.Max(0, def.MinRadiusPx);
            var maxR = Math.Max(0, def.MaxRadiusPx);
            var circles = Cv2.HoughCircles(blur, HoughModes.Gradient, Math.Max(1.0, def.HoughDp), Math.Max(1.0, def.HoughMinDistPx), Math.Max(1.0, def.HoughParam1), Math.Max(1.0, def.HoughParam2), minR, maxR);
            if (circles is null || circles.Length == 0) return 0.0;
            var bestR = circles.Max(c => c.Radius);
            var diag = Math.Sqrt(gray.Width * (double)gray.Width + gray.Height * (double)gray.Height);
            return 100.0 + (diag > 1e-6 ? bestR / diag * 50.0 : 0.0);
        }

        // ContourFit / Ransac: dùng circularity + số contour hợp lệ.
        using var edges = new Mat();
        Cv2.Canny(gray, edges, Math.Max(1, def.Canny1), Math.Max(2, def.Canny2));
        Cv2.FindContours(edges, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours is null || contours.Length == 0) return 0.0;
        var contourMinR = Math.Max(0, def.MinRadiusPx);
        var contourMaxR = Math.Max(0, def.MaxRadiusPx);
        var best = 0.0;
        foreach (var cnt in contours)
        {
            if (cnt is null || cnt.Length < 20) continue;
            var area = Math.Abs(Cv2.ContourArea(cnt));
            var peri = Cv2.ArcLength(cnt, true);
            if (peri <= 1e-9) continue;
            var circ = 4.0 * Math.PI * area / (peri * peri);
            if (circ < def.MinCircularity) continue;
            Cv2.MinEnclosingCircle(cnt, out _, out var r);
            if ((contourMinR > 0 && r < contourMinR) || (contourMaxR > 0 && r > contourMaxR)) continue;
            var s = circ * Math.Sqrt(area);
            if (s > best) best = s;
        }
        return best > 0 ? Math.Min(best, 500.0) : 0.0;
    }

    private static double ScoreCircleRadial(Mat gray, CircleFinderDefinition def)
    {
        var stripCount = Math.Clamp(def.StripCount > 0 ? def.StripCount : 32, 4, 360);
        var stripWidth = Math.Max(1, def.StripWidth > 0 ? def.StripWidth : 10);
        var stripLength = Math.Max(5, def.StripLength > 0 ? def.StripLength : 40);
        var minStrength = Math.Max(1, def.MinEdgeStrength);

        var cx = gray.Width / 2.0;
        var cy = gray.Height / 2.0;
        var nominalR = (gray.Width + gray.Height) / 4.0;
        var halfL = stripLength / 2.0;

        var found = 0;
        var strengthSum = 0.0;
        var radialSamples = stripLength + 1;

        for (var i = 0; i < stripCount; i++)
        {
            var angle = 2.0 * Math.PI * (i + 0.5) / stripCount;
            var ux = Math.Cos(angle);
            var uy = Math.Sin(angle);

            var profile = new double[radialSamples];
            for (var rIdx = 0; rIdx < radialSamples; rIdx++)
            {
                var rOffset = -halfL + (rIdx * stripLength / (double)Math.Max(1, radialSamples - 1));
                var px = (int)Math.Round(cx + (nominalR + rOffset) * ux);
                var py = (int)Math.Round(cy + (nominalR + rOffset) * uy);
                if (px < 0 || px >= gray.Width || py < 0 || py >= gray.Height)
                {
                    profile[rIdx] = 0;
                    continue;
                }
                // Trung bình nhỏ theo chiều ngang để giảm nhiễu.
                var sum = 0.0;
                var cnt = 0;
                for (var w = -stripWidth / 2; w <= stripWidth / 2; w++)
                {
                    var qx = (int)Math.Round(px + w * -uy);
                    var qy = (int)Math.Round(py + w * ux);
                    if (qx < 0 || qx >= gray.Width || qy < 0 || qy >= gray.Height) continue;
                    sum += gray.At<byte>(qy, qx);
                    cnt++;
                }
                profile[rIdx] = cnt > 0 ? sum / cnt : 0;
            }

            var bestVal = 0.0;
            for (var rIdx = 1; rIdx < radialSamples - 1; rIdx++)
            {
                var d = (profile[rIdx + 1] - profile[rIdx - 1]) / 2.0;
                var mag = def.Polarity switch
                {
                    EdgePolarity.LightToDark => d < 0 ? -d : 0.0,
                    EdgePolarity.DarkToLight => d > 0 ? d : 0.0,
                    _ => Math.Abs(d)
                };
                if (mag > bestVal) bestVal = mag;
            }

            if (bestVal >= minStrength)
            {
                found++;
                strengthSum += bestVal;
            }
        }

        if (found == 0) return 0.0;
        var ratio = found / (double)stripCount;
        var meanStrength = strengthSum / found;
        return ratio * 100.0 + meanStrength;
    }

    private static double ScoreCode(Mat processed, CodeDetectionDefinition def, Point2d ot, Point2d of, double ang)
    {
        using var crop = Geometry2D.ExtractStraightRoi(processed, def.SearchRoi, ot, of, ang, out _);
        if (crop.Empty() || crop.Width <= 0 || crop.Height <= 0) return 0.0;
        using var grayOwned = crop.Channels() == 1 ? null : crop.CvtColor(ColorConversionCodes.BGR2GRAY);
        Mat gray = grayOwned ?? crop;

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = ResolveFormats(def.Symbologies).ToList(), TryInverted = true }
        };

        ZXing.Result? TryDecode(Mat m)
        {
            if (m is null || m.Empty() || m.Width <= 0 || m.Height <= 0) return null;
            using var cont = m.Clone();
            var buf = new byte[cont.Cols * cont.Rows];
            Marshal.Copy(cont.Data, buf, 0, buf.Length);
            var src = new RGBLuminanceSource(buf, cont.Cols, cont.Rows, RGBLuminanceSource.BitmapFormat.Gray8);
            var r = reader.Decode(src);
            if (r != null && !string.IsNullOrWhiteSpace(r.Text)) return r;
            var multi = reader.DecodeMultiple(src);
            return multi?.FirstOrDefault(x => x != null && !string.IsNullOrWhiteSpace(x.Text));
        }

        var decoded = TryDecode(gray);
        if (decoded is null)
        {
            using var eq = new Mat();
            Cv2.EqualizeHist(gray, eq);
            decoded = TryDecode(eq);
        }
        if (decoded is null)
        {
            using var adapt = new Mat();
            Cv2.AdaptiveThreshold(gray, adapt, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 21, 5);
            decoded = TryDecode(adapt);
        }
        if (decoded is null)
        {
            using var otsu = new Mat();
            Cv2.Threshold(gray, otsu, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            decoded = TryDecode(otsu);
        }

        // Đọc được mã => điểm áp đảo (mục tiêu: làm rõ barcode/QR để decode thành công).
        if (decoded is not null && !string.IsNullOrWhiteSpace(decoded.Text))
        {
            return 1000.0 + Math.Min(decoded.Text.Length, 100) * 0.5;
        }

        // Chưa decode được: dùng chỉ số "độ nét biên" để dẫn hướng tìm kiếm
        // (biên/barcode càng rõ, tương phản càng cao => điểm càng cao).
        return GradientClarity(gray);
    }

    /// <summary>
    /// Chỉ số "độ rõ" của ảnh dựa trên phương sai Laplacian (độ nét biên) — dùng làm
    /// hàm mục tiêu phụ khi công cụ chưa bắt được đối tượng (ví dụ barcode chưa decode được).
    /// </summary>
    private static double GradientClarity(Mat gray)
    {
        try
        {
            using var lap = new Mat();
            Cv2.Laplacian(gray, lap, MatType.CV_64F);
            Cv2.MeanStdDev(lap, out _, out var std);
            var v = std.Val0;
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
            return Math.Min(v, 60.0);
        }
        catch
        {
            return 0.0;
        }
    }

    private static BarcodeFormat[] ResolveFormats(List<CodeSymbology>? sym)
    {
        if (sym is null || sym.Count == 0) return Array.Empty<BarcodeFormat>();
        var fmts = new HashSet<BarcodeFormat>();
        foreach (var s in sym)
        {
            switch (s)
            {
                case CodeSymbology.Qr: fmts.Add(BarcodeFormat.QR_CODE); break;
                case CodeSymbology.DataMatrix: fmts.Add(BarcodeFormat.DATA_MATRIX); break;
                case CodeSymbology.Pdf417: fmts.Add(BarcodeFormat.PDF_417); break;
                case CodeSymbology.Aztec: fmts.Add(BarcodeFormat.AZTEC); break;
                case CodeSymbology.Barcode1D:
                    fmts.Add(BarcodeFormat.CODE_128); fmts.Add(BarcodeFormat.CODE_39); fmts.Add(BarcodeFormat.CODE_93);
                    fmts.Add(BarcodeFormat.EAN_13); fmts.Add(BarcodeFormat.EAN_8); fmts.Add(BarcodeFormat.UPC_A);
                    fmts.Add(BarcodeFormat.UPC_E); fmts.Add(BarcodeFormat.ITF); fmts.Add(BarcodeFormat.CODABAR); break;
            }
        }
        return fmts.ToArray();
    }

    // ============================ SINH CẤU HÌNH ============================

    private static List<(string Desc, PreprocessSettings Settings)> BuildCandidates(PreprocessSettings baseline, AutoTuneToolKind kind)
    {
        var list = new List<(string, PreprocessSettings)>
        {
            ("Giữ nguyên (baseline)", CloneSettings(baseline))
        };

        void Add(string desc, Action<PreprocessSettings> mutate)
        {
            var s = CloneSettings(baseline);
            Reset(s);
            mutate(s);
            list.Add((desc, s));
        }

        Add("Chỉ chuyển xám", _ => { });
        Add("Gaussian Blur 3", s => { s.UseGaussianBlur = true; s.BlurKernel = 3; });
        Add("Gaussian Blur 5", s => { s.UseGaussianBlur = true; s.BlurKernel = 5; });
        Add("Gaussian Blur 7", s => { s.UseGaussianBlur = true; s.BlurKernel = 7; });
        Add("Median Blur 3", s => { s.UseMedianBlur = true; s.MedianKernel = 3; });
        Add("Median Blur 5", s => { s.UseMedianBlur = true; s.MedianKernel = 5; });
        Add("Bilateral 5/50/50", s => { s.UseBilateralFilter = true; s.BilateralDiameter = 5; s.BilateralSigmaColor = 50; s.BilateralSigmaSpace = 50; });
        Add("Bilateral 9/75/75", s => { s.UseBilateralFilter = true; s.BilateralDiameter = 9; s.BilateralSigmaColor = 75; s.BilateralSigmaSpace = 75; });

        Add("Illumination BackgroundSubtract k51", s => { s.IlluminationCorrection = IlluminationCorrectionPreset.BackgroundSubtract; s.IlluminationKernel = 51; });
        Add("Illumination BackgroundSubtract k101", s => { s.IlluminationCorrection = IlluminationCorrectionPreset.BackgroundSubtract; s.IlluminationKernel = 101; });
        Add("Illumination FlatField k51", s => { s.IlluminationCorrection = IlluminationCorrectionPreset.FlatFieldNormalize; s.IlluminationKernel = 51; });
        Add("CLAHE clip2 tile8", s => { s.IlluminationCorrection = IlluminationCorrectionPreset.Clahe; s.ClaheClipLimit = 2; s.ClaheTileGrid = 8; });
        Add("CLAHE clip4 tile8", s => { s.IlluminationCorrection = IlluminationCorrectionPreset.Clahe; s.ClaheClipLimit = 4; s.ClaheTileGrid = 8; });

        Add("Threshold Otsu", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Otsu; });
        Add("Threshold Triangle", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Triangle; });
        Add("Threshold Binary 96", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Binary; s.ThresholdLow = 96; });
        Add("Threshold Binary 128", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Binary; s.ThresholdLow = 128; });
        Add("Threshold Binary 160", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Binary; s.ThresholdLow = 160; });
        Add("Local Threshold mask15 off5", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Local; s.MaskWidth = 15; s.MaskHeight = 15; s.LocalOffset = 5; });
        Add("Local Threshold mask31 off10", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Local; s.MaskWidth = 31; s.MaskHeight = 31; s.LocalOffset = 10; });
        Add("Sauvola k0.2 r128 mask15", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Sauvola; s.SauvolaK = 0.2; s.SauvolaR = 128; s.MaskWidth = 15; s.MaskHeight = 15; });
        Add("Sauvola k0.3 r128 mask31", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Sauvola; s.SauvolaK = 0.3; s.SauvolaR = 128; s.MaskWidth = 31; s.MaskHeight = 31; });

        Add("Canny 50/150", s => { s.UseCanny = true; s.Canny1 = 50; s.Canny2 = 150; });
        Add("Canny 80/200", s => { s.UseCanny = true; s.Canny1 = 80; s.Canny2 = 200; });
        Add("Otsu + Morph Close 3", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Otsu; s.UseMorphology = true; s.MorphType = PreprocessMorphType.Close; s.MorphKernelSize = 3; });
        Add("Otsu + Morph Open 3", s => { s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Otsu; s.UseMorphology = true; s.MorphType = PreprocessMorphType.Open; s.MorphKernelSize = 3; });
        Add("Gradient Sobel 3", s => { s.GradientType = PreprocessGradientType.Sobel; s.GradientKernel = 3; });
        Add("Gradient Scharr", s => { s.GradientType = PreprocessGradientType.Scharr; s.GradientKernel = 3; });
        Add("Gamma 0.7", s => { s.UseGamma = true; s.GammaValue = 0.7; });
        Add("Gamma 1.5", s => { s.UseGamma = true; s.GammaValue = 1.5; });
        Add("Auto Contrast", s => { s.UseAutoContrast = true; });
        Add("Color channel Saturation", s => { s.ColorChannel = PreprocessColorChannel.Saturation; });
        Add("Color channel Value", s => { s.ColorChannel = PreprocessColorChannel.Value; });
        Add("Color channel Lab-L", s => { s.ColorChannel = PreprocessColorChannel.Lab_L; });

        // Auto Edge (đa phương pháp) — đặc biệt hữu ích cho cạnh/đường.
        Add("AutoEdge Ensemble", s => { s.UseAutoEdge = true; s.AutoEdgeMethod = AutoEdgeMethod.Ensemble; });
        Add("AutoEdge Scharr+Otsu", s => { s.UseAutoEdge = true; s.AutoEdgeMethod = AutoEdgeMethod.ScharrOtsu; });
        Add("AutoEdge BackgroundDiff+Triangle", s => { s.UseAutoEdge = true; s.AutoEdgeMethod = AutoEdgeMethod.BackgroundDiffTriangle; });
        Add("AutoEdge MorphGradient+Sauvola", s => { s.UseAutoEdge = true; s.AutoEdgeMethod = AutoEdgeMethod.MorphGradientSauvola; });
        Add("AutoEdge LabLuma+Otsu", s => { s.UseAutoEdge = true; s.AutoEdgeMethod = AutoEdgeMethod.LabLumaOtsu; });

        // Tổ hợp nhiều bước.
        Add("BackgroundSubtract k51 + Gaussian5 + Otsu", s =>
        {
            s.IlluminationCorrection = IlluminationCorrectionPreset.BackgroundSubtract;
            s.IlluminationKernel = 51;
            s.UseGaussianBlur = true; s.BlurKernel = 5;
            s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Otsu;
        });
        Add("Bilateral + AutoEdge Ensemble", s =>
        {
            s.UseBilateralFilter = true; s.BilateralDiameter = 5; s.BilateralSigmaColor = 50; s.BilateralSigmaSpace = 50;
            s.UseAutoEdge = true; s.AutoEdgeMethod = AutoEdgeMethod.Ensemble;
        });
        Add("CLAHE + Gaussian3 + AutoEdge Ensemble", s =>
        {
            s.IlluminationCorrection = IlluminationCorrectionPreset.Clahe;
            s.UseGaussianBlur = true; s.BlurKernel = 3;
            s.UseAutoEdge = true; s.AutoEdgeMethod = AutoEdgeMethod.Ensemble;
        });
        Add("Median3 + Otsu + Morph Close", s =>
        {
            s.UseMedianBlur = true; s.MedianKernel = 3;
            s.UseThreshold = true; s.ThresholdType = PreprocessThresholdType.Otsu;
            s.UseMorphology = true; s.MorphType = PreprocessMorphType.Close; s.MorphKernelSize = 3;
        });

        return list;
    }

    // ============================ SAO CHÉP / GÁN CẤU HÌNH ============================

    private static void Reset(PreprocessSettings s)
    {
        s.IlluminationCorrection = IlluminationCorrectionPreset.None;
        s.IlluminationKernel = 51;
        s.ClaheClipLimit = 2.0;
        s.ClaheTileGrid = 8;
        s.UseGray = true;
        s.UseGaussianBlur = false; s.BlurKernel = 3;
        s.UseThreshold = false; s.ThresholdType = PreprocessThresholdType.Binary; s.ThresholdLow = 128; s.ThresholdHigh = 255; s.InvertBinary = false;
        s.MaskWidth = 11; s.MaskHeight = 11; s.LocalOffset = 10.0; s.InvertLocal = false;
        s.UseCanny = false; s.Canny1 = 50; s.Canny2 = 150;
        s.UseMorphology = false; s.MorphShape = PreprocessMorphShape.Rect; s.MorphType = PreprocessMorphType.Close; s.MorphKernelSize = 3; s.MorphIterations = 1;
        s.SauvolaK = 0.2; s.SauvolaR = 128.0;
        s.UseMedianBlur = false; s.MedianKernel = 3;
        s.UseBilateralFilter = false; s.BilateralDiameter = 5; s.BilateralSigmaColor = 50.0; s.BilateralSigmaSpace = 50.0;
        s.GradientType = PreprocessGradientType.None; s.GradientKernel = 3; s.GradientScale = 1.0;
        s.ColorChannel = PreprocessColorChannel.All; s.UseGamma = false; s.GammaValue = 1.0; s.UseAutoContrast = false; s.InvertColors = false;
        s.UseAutoEdge = false; s.AutoEdgeMethod = AutoEdgeMethod.Ensemble; s.AutoEdgeMinConfidence = 0.6; s.AutoEdgeInvert = false;
    }

    public static PreprocessSettings CloneSettings(PreprocessSettings? src)
    {
        var s = new PreprocessSettings();
        if (src is null) return s;
        ApplySettings(s, src);
        return s;
    }

    /// <summary>Gán toàn bộ thông số từ <paramref name="source"/> sang <paramref name="target"/> (giữ nguyên tham chiếu object).</summary>
    public static void ApplySettings(PreprocessSettings target, PreprocessSettings source)
    {
        if (target is null || source is null) return;
        target.IlluminationCorrection = source.IlluminationCorrection;
        target.IlluminationKernel = source.IlluminationKernel;
        target.ClaheClipLimit = source.ClaheClipLimit;
        target.ClaheTileGrid = source.ClaheTileGrid;
        target.UseGray = source.UseGray;
        target.UseGaussianBlur = source.UseGaussianBlur;
        target.BlurKernel = source.BlurKernel;
        target.UseThreshold = source.UseThreshold;
        target.ThresholdType = source.ThresholdType;
        target.ThresholdLow = source.ThresholdLow;
        target.ThresholdHigh = source.ThresholdHigh;
        target.InvertBinary = source.InvertBinary;
        target.MaskWidth = source.MaskWidth;
        target.MaskHeight = source.MaskHeight;
        target.LocalOffset = source.LocalOffset;
        target.InvertLocal = source.InvertLocal;
        target.UseCanny = source.UseCanny;
        target.Canny1 = source.Canny1;
        target.Canny2 = source.Canny2;
        target.UseMorphology = source.UseMorphology;
        target.MorphShape = source.MorphShape;
        target.MorphType = source.MorphType;
        target.MorphKernelSize = source.MorphKernelSize;
        target.MorphIterations = source.MorphIterations;
        target.SauvolaK = source.SauvolaK;
        target.SauvolaR = source.SauvolaR;
        target.UseMedianBlur = source.UseMedianBlur;
        target.MedianKernel = source.MedianKernel;
        target.UseBilateralFilter = source.UseBilateralFilter;
        target.BilateralDiameter = source.BilateralDiameter;
        target.BilateralSigmaColor = source.BilateralSigmaColor;
        target.BilateralSigmaSpace = source.BilateralSigmaSpace;
        target.GradientType = source.GradientType;
        target.GradientKernel = source.GradientKernel;
        target.GradientScale = source.GradientScale;
        target.ColorChannel = source.ColorChannel;
        target.UseGamma = source.UseGamma;
        target.GammaValue = source.GammaValue;
        target.UseAutoContrast = source.UseAutoContrast;
        target.InvertColors = source.InvertColors;
        target.UseAutoEdge = source.UseAutoEdge;
        target.AutoEdgeMethod = source.AutoEdgeMethod;
        target.AutoEdgeMinConfidence = source.AutoEdgeMinConfidence;
        target.AutoEdgeInvert = source.AutoEdgeInvert;
    }
}
