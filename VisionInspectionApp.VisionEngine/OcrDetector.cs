using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.VisionEngine;

public sealed record OcrCharResult(char Character, Rect BoundingBox, double Confidence);

public sealed record OcrResult(
    string Name,
    bool Found,
    string Text,
    double Confidence,
    Rect BoundingBox,
    double Angle,
    bool Pass,
    string ExpectedSpec,
    List<OcrCharResult> Characters,
    string ErrorReason)
{
    public string RecognizedText => Text;
    public string Message => ErrorReason;
    public List<Rect> BoundingBoxes => Characters?.Select(c => c.BoundingBox).ToList() ?? new List<Rect>();

    public OcrResult(string name, bool found, string text, List<Rect> boundingBoxes, double confidence, bool pass, string expectedSpec, string message)
        : this(name, found, text, confidence, default, 0.0, pass, expectedSpec, boundingBoxes?.Select(b => new OcrCharResult('?', b, confidence)).ToList() ?? new(), message)
    {
    }
}

public static class OcrDetector
{
    private static readonly Lazy<Dictionary<char, List<Mat>>> _industrialFontCache = new(InitializeIndustrialFontLibrary);
    private static readonly ConcurrentDictionary<string, InferenceSession> _onnxSessionCache = new();
    private static readonly object _initLock = new();

    public static OcrResult Detect(
        Mat matBgrOrGray,
        OcrDefinition def,
        Point2d originTeach = default,
        Point2d originFound = default,
        double originAngleDeg = 0.0,
        ImagePreprocessor? preprocessor = null,
        PreprocessSettings? preprocessSettings = null)
    {
        if (matBgrOrGray is null || matBgrOrGray.Empty() || def is null || def.SearchRoi.Width <= 0 || def.SearchRoi.Height <= 0)
        {
            return new OcrResult(
                def?.Name ?? string.Empty,
                Found: false,
                Text: string.Empty,
                Confidence: 0.0,
                BoundingBox: default,
                Angle: 0.0,
                Pass: false,
                ExpectedSpec: def?.ExpectedText ?? string.Empty,
                Characters: new List<OcrCharResult>(),
                ErrorReason: "Ảnh đầu vào hoặc Search ROI không hợp lệ");
        }

        double totalAngleDeg = originAngleDeg + def.SearchRoi.Angle;

        // 1. Trích xuất ROI duỗi thẳng theo góc Origin và góc SearchRoi (ROI First)
        using var patch = Geometry2D.ExtractStraightRoi(matBgrOrGray, def.SearchRoi, originTeach, originFound, originAngleDeg, out var centerFound);
        if (patch.Empty() || patch.Width < 8 || patch.Height < 8)
        {
            return new OcrResult(
                def.Name,
                Found: false,
                Text: string.Empty,
                Confidence: 0.0,
                BoundingBox: default,
                Angle: totalAngleDeg,
                Pass: false,
                ExpectedSpec: def.ExpectedText ?? string.Empty,
                Characters: new List<OcrCharResult>(),
                ErrorReason: "Vùng ROI rỗng hoặc quá nhỏ");
        }

        // 2. Tiền xử lý nếu có cấu hình Tool Preprocess
        Mat processedPatch = patch;
        using var preprocessedOwned = (preprocessor != null && preprocessSettings != null)
            ? preprocessor.Run(patch, preprocessSettings)
            : null;
        if (preprocessedOwned != null && !preprocessedOwned.Empty())
        {
            processedPatch = preprocessedOwned;
        }

        using var patchGrayOwned = processedPatch.Channels() == 1 ? null : processedPatch.CvtColor(ColorConversionCodes.BGR2GRAY);
        Mat gray = patchGrayOwned ?? processedPatch;

        // 3. Thực thi theo Engine được chọn: AI Deep Learning hoặc Non-AI Heuristic
        OcrResult result;
        if (def.Engine == OcrEngineMode.Ai_DeepLearning)
        {
            result = RunAiOcr(gray, def, totalAngleDeg, centerFound);
        }
        else
        {
            result = RunNonAiOcr(gray, def, totalAngleDeg, centerFound);
        }

        return MapResultToGlobal(result, patch.Width, patch.Height, centerFound, totalAngleDeg);
    }

    private static OcrResult MapResultToGlobal(OcrResult localRes, int patchW, int patchH, Point2d centerFound, double totalAngleDeg)
    {
        if (localRes.Characters.Count == 0 && localRes.BoundingBox.Width <= 0)
            return localRes;

        var globalChars = new List<OcrCharResult>(localRes.Characters.Count);
        foreach (var c in localRes.Characters)
        {
            var b = c.BoundingBox;
            var centerLocal = new Point2d(b.X + b.Width / 2.0, b.Y + b.Height / 2.0);
            var centerGlobal = Geometry2D.MapToGlobal(centerLocal, patchW, patchH, centerFound, totalAngleDeg);
            var globalBox = new Rect(
                (int)Math.Round(centerGlobal.X - b.Width / 2.0),
                (int)Math.Round(centerGlobal.Y - b.Height / 2.0),
                b.Width,
                b.Height);
            globalChars.Add(new OcrCharResult(c.Character, globalBox, c.Confidence));
        }

        Rect globalOverallBox = default;
        if (localRes.BoundingBox.Width > 0 && localRes.BoundingBox.Height > 0)
        {
            var ob = localRes.BoundingBox;
            var obCenterLocal = new Point2d(ob.X + ob.Width / 2.0, ob.Y + ob.Height / 2.0);
            var obCenterGlobal = Geometry2D.MapToGlobal(obCenterLocal, patchW, patchH, centerFound, totalAngleDeg);
            globalOverallBox = new Rect(
                (int)Math.Round(obCenterGlobal.X - ob.Width / 2.0),
                (int)Math.Round(obCenterGlobal.Y - ob.Height / 2.0),
                ob.Width,
                ob.Height);
        }

        return localRes with
        {
            Characters = globalChars,
            BoundingBox = globalOverallBox
        };
    }

    #region Non-AI Heuristic & Industrial Segment Classifier

    private static OcrResult RunNonAiOcr(Mat gray, OcrDefinition def, double totalAngleDeg, Point2d centerFound)
    {
        using var bin = new Mat();

        // 1. Binarization
        BinarizePatch(gray, bin, def.BinarizeMethod);

        // Chuẩn hóa phân cực: Đảm bảo chữ luôn là màu TRẮNG (255) và nền luôn là màu ĐEN (0)
        NormalizeTextPolarity(bin, def.InvertImage);

        // 2. Dot-Matrix Connector: Nối các chấm in kim thành nét chữ liền mạch
        if (def.EnableDotMatrixConnector && def.DotMatrixKernelSize >= 2)
        {
            var kSize = Math.Clamp(def.DotMatrixKernelSize, 2, 25);
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(kSize, kSize));
            Cv2.MorphologyEx(bin, bin, MorphTypes.Close, kernel);
        }

        // 3. Character Segmentation qua Connected Components
        var candidateBoxes = ExtractCandidateBoxes(bin, def);

        // Adaptive Binarization Fallback: Nếu phương pháp hiện tại (như Sauvola) không tìm thấy ký tự
        // do tương phản thấp hoặc nền phẳng, tự động thử lại với Otsu để cứu kết quả!
        if (candidateBoxes.Count == 0 && def.BinarizeMethod != OcrBinarizeMethod.Otsu)
        {
            BinarizePatch(gray, bin, OcrBinarizeMethod.Otsu);
            NormalizeTextPolarity(bin, def.InvertImage);
            candidateBoxes = ExtractCandidateBoxes(bin, def);
        }

        if (candidateBoxes.Count == 0)
        {
            return EvaluateSpec(def, new List<OcrCharResult>(), totalAngleDeg, new Rect(0, 0, bin.Width, bin.Height), "Không tìm thấy ký tự hợp lệ");
        }

        // 4. Gom nhóm dấu chấm/dấu thanh, phân tách các chữ cái dính nét và sắp xếp từ Trái qua Phải
        var mergedBoxes = MergeAdjacentAccentsAndDots(candidateBoxes, bin.Height);
        var separatedBoxes = SplitConnectedGlyphs(mergedBoxes, bin);
        var sortedBoxes = SortReadingOrder(separatedBoxes);

        // 5. Nhận diện từng ký tự qua Template & Correlation Classifier
        var fontLibrary = _industrialFontCache.Value;

        // Chuẩn hóa Whitelist thông minh:
        var rawWhitelist = def.CharWhitelist ?? string.Empty;
        var allowedSet = new HashSet<char>();

        // Luôn bổ sung tất cả ký tự trong ExpectedText vào danh sách cho phép
        if (!string.IsNullOrWhiteSpace(def.ExpectedText))
        {
            foreach (char c in def.ExpectedText)
            {
                if (c != ' ') allowedSet.Add(c);
            }
        }

        if (string.IsNullOrWhiteSpace(rawWhitelist) || 
            string.Equals(rawWhitelist.Trim(), "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-/. :", StringComparison.Ordinal) ||
            def.MatchingMode == OcrMatchingMode.AnyText)
        {
            foreach (char c in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-/. :")
                allowedSet.Add(c);
        }
        else
        {
            foreach (char c in rawWhitelist)
                allowedSet.Add(c);
        }

        const int normW = 24;
        const int normH = 32;

        // Chuẩn bị thư viện ký tự đã học của người dùng (User Trained Font Library)
        var userTrainedMats = new List<(char Ch, Mat Mat)>();
        if (def.TrainedCharacters != null && def.TrainedCharacters.Count > 0)
        {
            foreach (var tc in def.TrainedCharacters)
            {
                if (string.IsNullOrWhiteSpace(tc.ImageBase64)) continue;
                try
                {
                    var bytes = Convert.FromBase64String(tc.ImageBase64);
                    var m = Cv2.ImDecode(bytes, ImreadModes.Grayscale);
                    if (!m.Empty())
                    {
                        if (m.Width != normW || m.Height != normH)
                        {
                            var resized = new Mat();
                            Cv2.Resize(m, resized, new Size(normW, normH));
                            m.Dispose();
                            m = resized;
                        }
                        userTrainedMats.Add((tc.Character, m));
                    }
                }
                catch { }
            }
        }

        var charResults = new List<OcrCharResult>();

        double avgHeight = sortedBoxes.Count > 0 ? sortedBoxes.Average(b => b.Height) : 25.0;
        double maxHeight = sortedBoxes.Count > 0 ? sortedBoxes.Max(b => b.Height) : 25.0;
        bool hasMixedCase = sortedBoxes.Any(b => b.Height > maxHeight * 0.82) && sortedBoxes.Any(b => b.Height < maxHeight * 0.75);

        try
        {
            foreach (var box in sortedBoxes)
            {
                using var charCrop = new Mat(bin, box);
                using var charNorm = NormalizeCharPatch(charCrop, normW, normH);

                // Phân tích tỷ lệ phân bố pixel trắng ở 4 góc phần tư (TL, TR, BL, BR)
                using var qTopRight = new Mat(charNorm, new Rect(12, 0, 12, 16));
                using var qTopLeft = new Mat(charNorm, new Rect(0, 0, 12, 16));
                using var qBottomRight = new Mat(charNorm, new Rect(12, 16, 12, 16));
                using var qBottomLeft = new Mat(charNorm, new Rect(0, 16, 12, 16));

                double trRatio = (double)Cv2.CountNonZero(qTopRight) / (12 * 16);
                double tlRatio = (double)Cv2.CountNonZero(qTopLeft) / (12 * 16);
                double brRatio = (double)Cv2.CountNonZero(qBottomRight) / (12 * 16);
                double blRatio = (double)Cv2.CountNonZero(qBottomLeft) / (12 * 16);

                using var qCenter = new Mat(charNorm, new Rect(8, 10, 8, 12));
                double centerDensity = (double)Cv2.CountNonZero(qCenter) / (8 * 12);
                bool isCenterHollow = centerDensity < 0.15;

                double relHeight = (double)box.Height / Math.Max(1.0, maxHeight);
                bool isShortGlyph = hasMixedCase && (relHeight < 0.78);

                char bestChar = '?';
                double bestScore = -1.0;

                // 1. ƯU TIÊN SỐ 1: So khớp với Thư viện ký tự đã học của người dùng (User Trained Font Library)
                if (userTrainedMats.Count > 0)
                {
                    foreach (var tm in userTrainedMats)
                    {
                        if (!allowedSet.Contains(tm.Ch)) continue;

                        double score = ComputeCharacterCorrelation(charNorm, tm.Mat);
                        if (score >= 0.65)
                        {
                            double boosted = Math.Min(1.0, score + 0.12);
                            if (boosted > bestScore)
                            {
                                bestScore = boosted;
                                bestChar = tm.Ch;
                            }
                        }
                        else if (score > bestScore)
                        {
                            bestScore = score;
                            bestChar = tm.Ch;
                        }
                    }
                }

                // 2. ƯU TIÊN SỐ 2: So khớp với Font Library tổng quát (Hershey Vector) nếu chưa khớp mẫu đã học tuyệt đối
                if (bestScore < 0.88)
                {
                    foreach (var kvp in fontLibrary)
                    {
                        char ch = kvp.Key;
                        if (!allowedSet.Contains(ch)) continue;

                        // --- RÀNG BUỘC HÌNH HỌC (Geometric Priors) ---
                        // 1. Dấu chấm '.' và dấu phẩy ',': chỉ cho phép khi box nhỏ cả về chiều rộng lẫn chiều cao
                        if (ch == '.' || ch == ',')
                        {
                            if (box.Width > Math.Max(8, avgHeight * 0.45) || box.Height > Math.Max(8, avgHeight * 0.45))
                                continue;
                        }
                        // 2. Dấu gạch ngang '-': phải dẹt (chiều cao nhỏ, chiều rộng lớn)
                        else if (ch == '-')
                        {
                            if (box.Height > Math.Max(8, avgHeight * 0.45) || box.Width < box.Height * 1.1)
                                continue;
                        }
                        // 3. Dấu hai chấm ':':
                        else if (ch == ':')
                        {
                            if (box.Width > Math.Max(10, avgHeight * 0.50))
                                continue;
                        }
                        else
                        {
                            // Với chữ cái và số: không thể là một chấm nhỏ li ti
                            if (box.Width < Math.Max(3, (int)(avgHeight * 0.15)) && box.Height < Math.Max(4, (int)(avgHeight * 0.25)))
                                continue;

                            // 4. Nếu là ký tự thân lùn (x-height như e, o, a, c, r, s, u, v, w, x, z):
                            // TUYỆT ĐỐI KHÔNG ĐƯỢC LÀ CÁC CHỮ IN HOA TO TOÀN KHUNG!
                            if (isShortGlyph && (char.IsUpper(ch) || char.IsDigit(ch)))
                            {
                                continue; // Ký tự lùn không thể là chữ in hoa (W, M, H, N, U...) hoặc chữ số!
                            }

                            // 5. Phân biệt chữ 'L' vs chữ 'U':
                            // Chữ 'L' có góc trên bên phải (Top-Right) hoàn toàn rỗng. Chữ 'U' bắt buộc có nét cột bên phải.
                            if (ch == 'U' && trRatio < 0.05)
                                continue;

                            if (ch == 'L' && trRatio > 0.16)
                                continue;

                            // 6. Phân biệt chữ 'd' vs 'J':
                            if (ch == 'J' && (blRatio > 0.35 || trRatio < 0.08))
                                continue;

                            // 7. Phân biệt số '9' vs chữ 'g':
                            if (ch == 'g' && !isShortGlyph && blRatio < 0.12)
                                continue;

                            // 8. Phân biệt chữ 'o' vs 'a':
                            if (ch == 'o' && !isCenterHollow)
                                continue;

                            if (ch == 'a' && isCenterHollow && Math.Abs(tlRatio - trRatio) < 0.15 && Math.Abs(blRatio - brRatio) < 0.15)
                                continue;
                        }

                        foreach (var templateMat in kvp.Value)
                        {
                            double score = ComputeCharacterCorrelation(charNorm, templateMat);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestChar = ch;
                            }
                        }
                    }
                }

                double confidence = Math.Clamp(Math.Round(bestScore, 3), 0.0, 1.0);
                if (bestScore >= 0.18 && bestChar != '?')
                {
                    charResults.Add(new OcrCharResult(bestChar, box, confidence));
                }
            }
        }
        finally
        {
            foreach (var tm in userTrainedMats)
            {
                tm.Mat.Dispose();
            }
        }

        var overallBoundingBox = charResults.Count > 0
            ? GetEnclosingRect(charResults.Select(c => c.BoundingBox))
            : new Rect(0, 0, bin.Width, bin.Height);

        return EvaluateSpec(def, charResults, totalAngleDeg, overallBoundingBox);
    }

    /// <summary>
    /// Tính năng Dạy Ký Tự Mẫu (Character Font Training) chuẩn công nghiệp như Hikrobot MVS:
    /// Trích xuất trực tiếp các ô ký tự mẫu từ hình ảnh thực tế của Search ROI và gán nhãn theo chuỗi mẫu.
    /// </summary>
    public static List<OcrUserCharacterTemplate> TeachCharacters(
        Mat matBgrOrGray,
        OcrDefinition def,
        string? labelSequence = null,
        Point2d originTeach = default,
        Point2d originFound = default,
        double originAngleDeg = 0.0,
        ImagePreprocessor? preprocessor = null,
        PreprocessSettings? preprocessSettings = null)
    {
        var result = new List<OcrUserCharacterTemplate>();
        if (matBgrOrGray is null || matBgrOrGray.Empty() || def is null || def.SearchRoi.Width <= 0 || def.SearchRoi.Height <= 0)
            return result;

        using var patch = Geometry2D.ExtractStraightRoi(matBgrOrGray, def.SearchRoi, originTeach, originFound, originAngleDeg, out _);
        if (patch.Empty() || patch.Width < 8 || patch.Height < 8)
            return result;

        Mat processedPatch = patch;
        using var preprocessedOwned = (preprocessor != null && preprocessSettings != null)
            ? preprocessor.Run(patch, preprocessSettings)
            : null;
        if (preprocessedOwned != null && !preprocessedOwned.Empty())
            processedPatch = preprocessedOwned;

        using var patchGrayOwned = processedPatch.Channels() == 1 ? null : processedPatch.CvtColor(ColorConversionCodes.BGR2GRAY);
        Mat gray = patchGrayOwned ?? processedPatch;

        using var bin = new Mat();
        BinarizePatch(gray, bin, def.BinarizeMethod);
        NormalizeTextPolarity(bin, def.InvertImage);

        if (def.EnableDotMatrixConnector && def.DotMatrixKernelSize >= 2)
        {
            var kSize = Math.Clamp(def.DotMatrixKernelSize, 2, 25);
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(kSize, kSize));
            Cv2.MorphologyEx(bin, bin, MorphTypes.Close, kernel);
        }

        var candidateBoxes = ExtractCandidateBoxes(bin, def);
        if (candidateBoxes.Count == 0 && def.BinarizeMethod != OcrBinarizeMethod.Otsu)
        {
            BinarizePatch(gray, bin, OcrBinarizeMethod.Otsu);
            NormalizeTextPolarity(bin, def.InvertImage);
            candidateBoxes = ExtractCandidateBoxes(bin, def);
        }

        if (candidateBoxes.Count == 0) return result;

        var mergedBoxes = MergeAdjacentAccentsAndDots(candidateBoxes, bin.Height);
        var separatedBoxes = SplitConnectedGlyphs(mergedBoxes, bin);
        var sortedBoxes = SortReadingOrder(separatedBoxes);

        string labels = !string.IsNullOrWhiteSpace(labelSequence) ? labelSequence : (def.ExpectedText ?? string.Empty);
        var cleanLabels = labels.Where(c => c != ' ').ToList();

        const int normW = 24;
        const int normH = 32;

        for (int i = 0; i < sortedBoxes.Count && i < cleanLabels.Count; i++)
        {
            char label = cleanLabels[i];
            var box = sortedBoxes[i];
            using var charCrop = new Mat(bin, box);
            using var charNorm = NormalizeCharPatch(charCrop, normW, normH);

            if (Cv2.ImEncode(".png", charNorm, out var buf))
            {
                result.Add(new OcrUserCharacterTemplate
                {
                    Character = label,
                    ImageBase64 = Convert.ToBase64String(buf),
                    Width = normW,
                    Height = normH,
                    CreatedAt = DateTime.Now
                });
            }
        }

        return result;
    }

    #endregion

    #region AI Deep Learning Engine (ONNX Runtime with Safe Fallback)

    private static OcrResult RunAiOcr(Mat gray, OcrDefinition def, double totalAngleDeg, Point2d centerFound)
    {
        // Kiểm tra xem model ONNX có tồn tại không (ưu tiên OnnxModelPath của def, sau đó đến thư mục mặc định)
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string defaultModelPath = Path.Combine(baseDir, "models", "ocr", "crnn_rec.onnx");
        string modelPath = !string.IsNullOrWhiteSpace(def.OnnxModelPath) && File.Exists(def.OnnxModelPath)
            ? def.OnnxModelPath
            : defaultModelPath;

        if (!File.Exists(modelPath))
        {
            // Tự động Fallback sang Non-AI mượt mà nếu chưa nạp mô hình Deep Learning
            var fallbackRes = RunNonAiOcr(gray, def, totalAngleDeg, centerFound);
            var note = string.IsNullOrWhiteSpace(fallbackRes.ErrorReason)
                ? "[AI Model not found -> Fallback to Non-AI]"
                : $"{fallbackRes.ErrorReason} [AI Model not found -> Fallback to Non-AI]";

            return fallbackRes with { ErrorReason = note };
        }

        try
        {
            var session = GetOrLoadOnnxSession(modelPath);
            // Chuẩn hóa tensor ảnh (32xW chuẩn cho CRNN)
            int targetH = 32;
            int targetW = Math.Max(32, (int)Math.Round((double)gray.Width * targetH / gray.Height));
            targetW = (targetW / 8) * 8; // align multiple of 8

            using var resized = new Mat();
            Cv2.Resize(gray, resized, new Size(targetW, targetH));

            var inputTensor = new DenseTensor<float>(new[] { 1, 1, targetH, targetW });
            for (int y = 0; y < targetH; y++)
            {
                for (int x = 0; x < targetW; x++)
                {
                    float val = (resized.At<byte>(y, x) / 255.0f - 0.5f) / 0.5f;
                    inputTensor[0, 0, y, x] = val;
                }
            }

            var inputName = session.InputMetadata.Keys.First();
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };

            using var outputs = session.Run(inputs);
            var outputTensor = outputs.First().AsTensor<float>();

            // CTC Greedy Decoder
            string recognizedText = DecodeCtcOutput(outputTensor, out double avgConf);

            var charResults = new List<OcrCharResult>();
            int charW = targetW / Math.Max(1, recognizedText.Length);
            for (int i = 0; i < recognizedText.Length; i++)
            {
                var charBox = new Rect(i * charW, 0, charW, gray.Height);
                charResults.Add(new OcrCharResult(recognizedText[i], charBox, Math.Round(avgConf * 100.0, 1)));
            }

            return EvaluateSpec(def, charResults, totalAngleDeg, new Rect(0, 0, gray.Width, gray.Height));
        }
        catch (Exception ex)
        {
            // Nếu có lỗi trong quá trình suy luận ONNX, dự phòng sang Non-AI
            var fallbackRes = RunNonAiOcr(gray, def, totalAngleDeg, centerFound);
            return fallbackRes with { ErrorReason = $"[AI Error: {ex.Message} -> Fallback to Non-AI]" };
        }
    }

    private static InferenceSession GetOrLoadOnnxSession(string path)
    {
        return _onnxSessionCache.GetOrAdd(path, p =>
        {
            lock (_initLock)
            {
                var options = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2)
                };
                return new InferenceSession(p, options);
            }
        });
    }

    private static string DecodeCtcOutput(Tensor<float> tensor, out double avgConfidence)
    {
        // Bảng ký tự công nghiệp tiêu chuẩn
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-/. :";
        var dimensions = tensor.Dimensions;
        int timeSteps = dimensions[1];
        int numClasses = dimensions[2];

        var chars = new List<char>();
        var confs = new List<double>();
        int prevClass = 0; // blank index is 0

        for (int t = 0; t < timeSteps; t++)
        {
            int maxIdx = 0;
            float maxVal = tensor[0, t, 0];
            for (int c = 1; c < numClasses; c++)
            {
                float val = tensor[0, t, c];
                if (val > maxVal)
                {
                    maxVal = val;
                    maxIdx = c;
                }
            }

            if (maxIdx != 0 && maxIdx != prevClass && (maxIdx - 1) < alphabet.Length)
            {
                chars.Add(alphabet[maxIdx - 1]);
                confs.Add(Math.Exp(maxVal) / (1.0 + Math.Exp(maxVal)));
            }
            prevClass = maxIdx;
        }

        avgConfidence = confs.Count > 0 ? confs.Average() : 0.0;
        return new string(chars.ToArray());
    }

    #endregion

    #region Spec Verification & Matching Evaluation

    private static OcrResult EvaluateSpec(OcrDefinition def, List<OcrCharResult> characters, double angleDeg, Rect boundingBox, string? initialError = null)
    {
        var sb = new System.Text.StringBuilder();
        double avgHeight = characters.Count > 0 ? characters.Average(c => c.BoundingBox.Height) : 30.0;
        int defaultThreshold = Math.Max(24, (int)(avgHeight * 0.70));
        int spacingThreshold = def.CharSpacingThreshold > 0 ? def.CharSpacingThreshold : defaultThreshold;

        for (int i = 0; i < characters.Count; i++)
        {
            if (i > 0)
            {
                var prevBox = characters[i - 1].BoundingBox;
                var currBox = characters[i].BoundingBox;
                int spaceGap = currBox.Left - prevBox.Right;
                if (spaceGap >= spacingThreshold)
                {
                    sb.Append(' ');
                }
            }
            sb.Append(characters[i].Character);
        }

        string text = sb.ToString().Trim();
        bool found = !string.IsNullOrWhiteSpace(text);
        double avgConf = characters.Count > 0 ? Math.Round(characters.Average(c => c.Confidence), 3) : 0.0;

        bool pass = false;
        string error = initialError ?? string.Empty;

        if (!found)
        {
            return new OcrResult(
                def.Name,
                Found: false,
                Text: string.Empty,
                Confidence: 0.0,
                BoundingBox: boundingBox,
                Angle: angleDeg,
                Pass: false,
                ExpectedSpec: def.ExpectedText ?? string.Empty,
                Characters: characters,
                ErrorReason: string.IsNullOrWhiteSpace(error) ? "Không nhận diện được ký tự nào" : error);
        }

        // Kiểm tra ngưỡng tin cậy tối thiểu (hỗ trợ cả thang 0..1 và 0..100)
        double minConfNorm = def.MinConfidence > 1.0 ? def.MinConfidence / 100.0 : def.MinConfidence;
        bool confPass = avgConf >= minConfNorm;

        // Đánh giá theo MatchingMode
        switch (def.MatchingMode)
        {
            case OcrMatchingMode.AnyText:
                pass = confPass;
                if (!confPass) error = $"Độ tin cậy ({avgConf:P1}) dưới ngưỡng cho phép ({minConfNorm:P1})";
                break;

            case OcrMatchingMode.ExactMatch:
                bool exact = string.Equals(text, def.ExpectedText?.Trim(), StringComparison.OrdinalIgnoreCase);
                pass = exact && confPass;
                if (!exact) error = $"Chuỗi không khớp: Nhận diện '{text}', Kỳ vọng '{def.ExpectedText}'";
                else if (!confPass) error = $"Độ tin cậy ({avgConf:P1}) dưới ngưỡng ({minConfNorm:P1})";
                break;

            case OcrMatchingMode.Contains:
                bool contains = !string.IsNullOrWhiteSpace(def.ExpectedText) && text.Contains(def.ExpectedText.Trim(), StringComparison.OrdinalIgnoreCase);
                pass = contains && confPass;
                if (!contains) error = $"Chuỗi '{text}' không chứa '{def.ExpectedText}'";
                else if (!confPass) error = $"Độ tin cậy ({avgConf:P1}) dưới ngưỡng ({minConfNorm:P1})";
                break;

            case OcrMatchingMode.RegexPattern:
                bool regexMatch = false;
                try
                {
                    if (!string.IsNullOrWhiteSpace(def.RegexPattern))
                    {
                        regexMatch = Regex.IsMatch(text, def.RegexPattern);
                    }
                }
                catch (Exception ex)
                {
                    error = $"Lỗi biểu thức Regex: {ex.Message}";
                }

                pass = regexMatch && confPass;
                if (!regexMatch && string.IsNullOrWhiteSpace(error)) error = $"Chuỗi '{text}' không khớp định dạng Regex '{def.RegexPattern}'";
                else if (!confPass) error = $"Độ tin cậy ({avgConf:P1}) dưới ngưỡng ({minConfNorm:P1})";
                break;
        }

        return new OcrResult(
            def.Name,
            Found: true,
            Text: text,
            Confidence: avgConf,
            BoundingBox: boundingBox,
            Angle: angleDeg,
            Pass: pass,
            ExpectedSpec: !string.IsNullOrWhiteSpace(def.ExpectedText) ? def.ExpectedText : def.RegexPattern,
            Characters: characters,
            ErrorReason: error);
    }

    #endregion

    #region Helper Methods & Industrial Font Library

    private static void ApplySauvolaFast(Mat gray, Mat dst)
    {
        // Sauvola Adaptive Binarization tối ưu hóa cao cho văn bản công nghiệp
        const int kSize = 15;
        const double k = 0.2;
        const double r = 128.0;

        using var mean = new Mat();
        using var sqr = new Mat();
        using var meanSqr = new Mat();
        using var stdDev = new Mat();

        Cv2.Blur(gray, mean, new Size(kSize, kSize));
        Cv2.Multiply(gray, gray, sqr, 1.0, MatType.CV_32F);
        Cv2.Blur(sqr, meanSqr, new Size(kSize, kSize));

        using var mean32f = new Mat();
        mean.ConvertTo(mean32f, MatType.CV_32F);

        using var variance = new Mat();
        Cv2.Subtract(meanSqr, mean32f.Mul(mean32f), variance);
        Cv2.Max(variance, 0.0, variance);
        Cv2.Sqrt(variance, stdDev);

        using var thresholdMat = new Mat();
        // T = mean * (1.0 + k * (stdDev / r - 1.0))
        using var stdDevTerm = new Mat();
        Cv2.Divide(stdDev, r, stdDevTerm);
        Cv2.Subtract(stdDevTerm, 1.0, stdDevTerm);
        Cv2.Multiply(stdDevTerm, k, stdDevTerm);
        Cv2.Add(stdDevTerm, 1.0, stdDevTerm);
        Cv2.Multiply(mean32f, stdDevTerm, thresholdMat);

        using var threshold8u = new Mat();
        thresholdMat.ConvertTo(threshold8u, MatType.CV_8U);

        Cv2.Compare(gray, threshold8u, dst, CmpType.GT);
    }

    private static void BinarizePatch(Mat gray, Mat bin, OcrBinarizeMethod method)
    {
        switch (method)
        {
            case OcrBinarizeMethod.Sauvola:
                ApplySauvolaFast(gray, bin);
                break;
            case OcrBinarizeMethod.AdaptiveMean:
                Cv2.AdaptiveThreshold(gray, bin, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.Binary, 15, 4);
                break;
            case OcrBinarizeMethod.AdaptiveGaussian:
                Cv2.AdaptiveThreshold(gray, bin, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 15, 4);
                break;
            case OcrBinarizeMethod.Otsu:
            default:
                Cv2.Threshold(gray, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                break;
        }
    }

    private static void NormalizeTextPolarity(Mat bin, bool forceInvert)
    {
        if (bin.Width <= 0 || bin.Height <= 0) return;

        // Nếu người dùng yêu cầu đảo màu thủ công
        if (forceInvert)
        {
            Cv2.BitwiseNot(bin, bin);
        }

        // Auto-Polarity thông minh:
        // Đảm bảo chữ luôn là pixel TRẮNG (255) trên nền ĐEN (0).
        // Trong ảnh công nghiệp và UI, vùng văn bản thường chiếm một phần nhỏ (5% - 40%) diện tích ROI.
        int totalPixels = bin.Width * bin.Height;
        int whiteCount = Cv2.CountNonZero(bin);
        double whiteRatio = (double)whiteCount / totalPixels;

        // Kiểm tra 4 đường viền bao quanh (border check): Mép biên ROI hầu như luôn là nền!
        int borderWhite = 0;
        int borderTotal = 0;
        for (int x = 0; x < bin.Width; x++)
        {
            if (bin.At<byte>(0, x) > 128) borderWhite++;
            if (bin.At<byte>(bin.Height - 1, x) > 128) borderWhite++;
            borderTotal += 2;
        }
        for (int y = 0; y < bin.Height; y++)
        {
            if (bin.At<byte>(y, 0) > 128) borderWhite++;
            if (bin.At<byte>(y, bin.Width - 1) > 128) borderWhite++;
            borderTotal += 2;
        }
        double borderWhiteRatio = borderTotal > 0 ? (double)borderWhite / borderTotal : 0.0;

        // Nếu viền chủ yếu là trắng (> 50%) hoặc tổng số pixel trắng chiếm đa số (> 50% diện tích),
        // chứng tỏ nền đang là màu TRẮNG -> Cần đảo ngược lại để nền thành ĐEN (0), chữ thành TRẮNG (255).
        if (borderWhiteRatio > 0.50 || (borderWhiteRatio > 0.35 && whiteRatio > 0.45) || whiteRatio > 0.55)
        {
            Cv2.BitwiseNot(bin, bin);
        }
    }

    private static List<Rect> ExtractCandidateBoxes(Mat bin, OcrDefinition def)
    {
        var candidateBoxes = new List<Rect>();
        if (bin.Width <= 0 || bin.Height <= 0) return candidateBoxes;

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        int nLabels = Cv2.ConnectedComponentsWithStats(bin, labels, stats, centroids);

        int totalPixels = bin.Width * bin.Height;
        int minArea = def.MinCharArea > 0 ? def.MinCharArea : 6;
        int maxArea = def.MaxCharArea > 0 ? def.MaxCharArea : (int)(totalPixels * 0.70);

        for (int i = 1; i < nLabels; i++) // Bỏ qua nhãn 0 (nền đen)
        {
            int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
            int left = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
            int top = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
            int width = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
            int height = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);

            // 1. Lọc bỏ nhiễu hạt quá nhỏ
            if (area < minArea || width < 2 || height < 4) continue;

            // 2. Lọc bỏ nền quá lớn bao trùm gần như toàn bộ ROI
            if (area > maxArea || (width > bin.Width * 0.95 && height > bin.Height * 0.85)) continue;

            // 3. Lọc bỏ đường kẻ ngang phân cách bảng hoặc thanh phân chia dòng
            // (Hiện tượng trong ảnh UI của người dùng: cạnh dưới ROI chạm vào đường kẻ ngang ngăn cách giữa 'Line Mode' và 'Line Source')
            if (width >= bin.Width * 0.75 && height <= Math.Max(6, (int)(bin.Height * 0.25))) continue;

            // 4. Lọc bỏ đường kẻ dọc dài
            if (height >= bin.Height * 0.85 && width <= Math.Max(4, (int)(bin.Width * 0.10))) continue;

            candidateBoxes.Add(new Rect(left, top, width, height));
        }

        return candidateBoxes;
    }

    private static List<Rect> MergeAdjacentAccentsAndDots(List<Rect> boxes, int imgHeight)
    {
        // Gom các dấu chấm, vạch đứt thẳng hàng dọc rất gần nhau thành 1 ký tự
        var result = new List<Rect>(boxes);
        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < result.Count; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    var r1 = result[i];
                    var r2 = result[j];

                    int overlapX = Math.Min(r1.Right, r2.Right) - Math.Max(r1.Left, r2.Left);
                    int distY = r1.Top > r2.Top ? r1.Top - r2.Bottom : r2.Top - r1.Bottom;

                    if (overlapX > Math.Min(r1.Width, r2.Width) * 0.4 && distY >= 0 && distY <= imgHeight * 0.18)
                    {
                        var u = r1.Union(r2);
                        result[i] = u;
                        result.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
                if (merged) break;
            }
        }
        return result;
    }

    private static List<Rect> SplitConnectedGlyphs(List<Rect> boxes, Mat bin)
    {
        var result = new List<Rect>();
        if (boxes.Count == 0) return result;

        double avgHeight = boxes.Average(b => b.Height);

        foreach (var b in boxes)
        {
            // Một chữ cái thông thường có tỷ lệ Width / Height < 1.25 (trừ dấu gạch ngang '-')
            // Nếu một blob có chiều rộng lớn hơn 1.25 lần chiều cao và chiều cao >= 8px:
            // Khả năng rất cao là 2 hay nhiều chữ cái dính nhau!
            if (b.Width > b.Height * 1.25 && b.Width >= 16 && b.Height >= 8)
            {
                var subSplits = FindSplitBoxesByVerticalProjection(bin, b, avgHeight);
                if (subSplits.Count > 1)
                {
                    result.AddRange(subSplits);
                    continue;
                }
            }

            result.Add(b);
        }

        return result;
    }

    private static List<Rect> FindSplitBoxesByVerticalProjection(Mat bin, Rect box, double avgHeight)
    {
        var splits = new List<Rect>();
        if (box.Width <= 0 || box.Height <= 0) return splits;

        using var patch = new Mat(bin, box);
        int w = patch.Width;
        int h = patch.Height;

        // Tính vertical projection (số pixel trắng ở mỗi cột x)
        int[] proj = new int[w];
        for (int x = 0; x < w; x++)
        {
            int cnt = 0;
            for (int y = 0; y < h; y++)
            {
                if (patch.At<byte>(y, x) > 128) cnt++;
            }
            proj[x] = cnt;
        }

        // Ước lượng chiều rộng 1 ký tự chuẩn khoảng 0.5 - 0.7 chiều cao
        double targetCharWidth = Math.Max(8.0, avgHeight * 0.60);
        int minCharWidth = Math.Max(4, (int)(targetCharWidth * 0.45));

        // Tìm các thung lũng (valleys/minima) có proj[x] thấp để cắt
        var cutX = new List<int> { 0 };
        int lastCut = 0;

        for (int x = minCharWidth; x < w - minCharWidth; x++)
        {
            if ((x - lastCut) >= minCharWidth)
            {
                bool isMin = true;
                for (int dx = -2; dx <= 2; dx++)
                {
                    int nx = x + dx;
                    if (nx >= 0 && nx < w && proj[nx] < proj[x])
                    {
                        isMin = false;
                        break;
                    }
                }

                if (isMin && (proj[x] <= Math.Max(2, (int)(h * 0.20))))
                {
                    cutX.Add(x);
                    lastCut = x;
                }
            }
        }
        cutX.Add(w);

        if (cutX.Count <= 2)
        {
            // Không có thung lũng (valley) rõ ràng -> Không cắt bừa bãi tránh xẻ đôi ký tự
            return splits;
        }

        for (int i = 0; i < cutX.Count - 1; i++)
        {
            int startX = cutX[i];
            int subW = cutX[i + 1] - startX;
            if (subW < 2) continue;

            var tight = TightenBoundingBox(patch, startX, 0, subW, h);
            if (tight.Width >= 2 && tight.Height >= 4)
            {
                splits.Add(new Rect(box.X + tight.X, box.Y + tight.Y, tight.Width, tight.Height));
            }
        }

        return splits;
    }

    private static Rect TightenBoundingBox(Mat patch, int x, int y, int w, int h)
    {
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;

        for (int cy = y; cy < y + h; cy++)
        {
            for (int cx = x; cx < x + w; cx++)
            {
                if (patch.At<byte>(cy, cx) > 128)
                {
                    if (cx < minX) minX = cx;
                    if (cx > maxX) maxX = cx;
                    if (cy < minY) minY = cy;
                    if (cy > maxY) maxY = cy;
                }
            }
        }

        if (minX == int.MaxValue) return default;
        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static List<Rect> SortReadingOrder(List<Rect> boxes)
    {
        if (boxes.Count <= 1) return boxes;

        double avgH = boxes.Average(r => r.Height);
        double rowTolerance = Math.Max(12.0, avgH * 0.5);

        // Gom các box vào các hàng
        var rows = new List<List<Rect>>();
        var sortedByY = boxes.OrderBy(r => r.Top + r.Height / 2.0).ToList();

        foreach (var b in sortedByY)
        {
            double cy = b.Top + b.Height / 2.0;
            bool added = false;
            foreach (var row in rows)
            {
                double rowCy = row.Average(r => r.Top + r.Height / 2.0);
                if (Math.Abs(cy - rowCy) <= rowTolerance)
                {
                    row.Add(b);
                    added = true;
                    break;
                }
            }
            if (!added)
            {
                rows.Add(new List<Rect> { b });
            }
        }

        // Sắp xếp các hàng từ trên xuống dưới
        var sortedRows = rows.OrderBy(row => row.Average(r => r.Top + r.Height / 2.0));

        // Trong từng hàng sắp xếp từ trái qua phải
        var result = new List<Rect>();
        foreach (var row in sortedRows)
        {
            result.AddRange(row.OrderBy(r => r.Left));
        }

        return result;
    }

    private static unsafe double ComputeCharacterCorrelation(Mat charNorm, Mat templateMat)
    {
        // 1. Tương quan Pearson CCoeffNormed
        using var res = new Mat();
        Cv2.MatchTemplate(charNorm, templateMat, res, TemplateMatchModes.CCoeffNormed);
        float pScore = res.At<float>(0, 0);
        double ccoeff = float.IsNaN(pScore) ? 0.0 : Math.Max(0.0, (double)pScore);

        // 2. Tính IoU siêu tốc bằng con trỏ bộ nhớ (không cấp phát Mat mới)
        byte* pNorm = (byte*)charNorm.DataPointer;
        byte* pTpl = (byte*)templateMat.DataPointer;
        int total = charNorm.Rows * charNorm.Cols;

        int interCount = 0;
        int unionCount = 0;
        for (int i = 0; i < total; i++)
        {
            bool a = pNorm[i] > 128;
            bool b = pTpl[i] > 128;
            if (a && b) interCount++;
            if (a || b) unionCount++;
        }

        double iou = unionCount > 0 ? (double)interCount / unionCount : 0.0;

        // 3. Kết hợp trọng số: IoU phạt rất nặng các nét thừa hoặc thiếu (như L vs U, E vs F)
        double finalScore = 0.50 * ccoeff + 0.50 * iou;
        return Math.Clamp(finalScore, 0.0, 1.0);
    }

    private static Rect GetEnclosingRect(IEnumerable<Rect> rects)
    {
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        foreach (var r in rects)
        {
            minX = Math.Min(minX, r.Left);
            minY = Math.Min(minY, r.Top);
            maxX = Math.Max(maxX, r.Right);
            maxY = Math.Max(maxY, r.Bottom);
        }

        if (minX == int.MaxValue) return default;
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static Mat NormalizeCharPatch(Mat crop, int canvasW = 24, int canvasH = 32)
    {
        var dst = new Mat(canvasH, canvasW, MatType.CV_8UC1, Scalar.All(0));
        if (crop.Width <= 0 || crop.Height <= 0) return dst;

        // Giữ nguyên tỉ lệ co dãn của ký tự (không bóp méo hình dạng nét chữ)
        int targetInnerW = canvasW - 4; // 20
        int targetInnerH = canvasH - 4; // 28

        double scale = Math.Min((double)targetInnerW / crop.Width, (double)targetInnerH / crop.Height);
        int newW = Math.Clamp((int)Math.Round(crop.Width * scale), 1, targetInnerW);
        int newH = Math.Clamp((int)Math.Round(crop.Height * scale), 1, targetInnerH);

        using var resized = new Mat();
        Cv2.Resize(crop, resized, new Size(newW, newH), interpolation: InterpolationFlags.Area);
        Cv2.Threshold(resized, resized, 50, 255, ThresholdTypes.Binary);

        int offsetX = (canvasW - newW) / 2;
        int offsetY = (canvasH - newH) / 2;

        using var sub = new Mat(dst, new Rect(offsetX, offsetY, newW, newH));
        resized.CopyTo(sub);

        return dst;
    }

    private static Dictionary<char, List<Mat>> InitializeIndustrialFontLibrary()
    {
        var dict = new Dictionary<char, List<Mat>>();
        const int normW = 24;
        const int normH = 32;

        const string supportedChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-/:.";

        // Chọn font tiêu chuẩn công nghiệp sắc nét và hỗ trợ nét chữ đặc ruột
        HersheyFonts[] fonts = { HersheyFonts.HersheySimplex };
        int[] thicknesses = { 2, 3 };

        foreach (char c in supportedChars)
        {
            var list = new List<Mat>();
            string s = c.ToString();

            foreach (var font in fonts)
            {
                foreach (var th in thicknesses)
                {
                    // Vẽ glyph lớn lên canvas 120x120 để giữ độ phân giải nét chữ sắc sảo
                    using var canvas = new Mat(120, 120, MatType.CV_8UC1, Scalar.All(0));
                    var size = Cv2.GetTextSize(s, font, 2.0, th, out int baseline);
                    int x = Math.Max(2, (120 - size.Width) / 2);
                    int y = Math.Min(115, (120 + size.Height) / 2);

                    Cv2.PutText(canvas, s, new Point(x, y), font, 2.0, Scalar.All(255), th, LineTypes.AntiAlias);

                    using var nonZero = new Mat();
                    Cv2.FindNonZero(canvas, nonZero);
                    if (nonZero.Total() > 0)
                    {
                        var glyphRect = Cv2.BoundingRect(nonZero);
                        using var cropped = new Mat(canvas, glyphRect);

                        var tpl = NormalizeCharPatch(cropped, normW, normH);
                        list.Add(tpl);
                    }
                }
            }

            dict[c] = list;
        }

        return dict;
    }

    #endregion
}
