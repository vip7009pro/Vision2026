using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenCvSharp;
using VisionInspectionApp.Application.DB.Services;
using VisionInspectionApp.Models;
using ZXing;

namespace VisionInspectionApp.Application.OQC;

public sealed class OqcScannerService : IOqcScannerService
{
    private readonly string _configFilePath;
    private readonly string _historyFilePath;
    public OqcScannerConfig Config { get; private set; } = new();

    public OqcScannerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Vision2026");
        Directory.CreateDirectory(dir);
        _configFilePath = Path.Combine(dir, "oqc_scanner_config.json");
        _historyFilePath = Path.Combine(dir, "oqc_scan_history.json");

        LoadConfig();
    }

    public void LoadConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var loaded = JsonSerializer.Deserialize<OqcScannerConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (loaded != null)
                {
                    Config = loaded;
                    return;
                }
            }
        }
        catch
        {
            // Fallback to default config on error
        }

        Config = new OqcScannerConfig();
    }

    public void SaveConfig(OqcScannerConfig config)
    {
        if (config == null) return;
        Config = config;

        try
        {
            var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save OQC config: {ex.Message}");
        }
    }

    public bool ExportConfigToFile(string filePath, OqcScannerConfig config)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || config == null) return false;
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ExportConfigToFile error: {ex.Message}");
            return false;
        }
    }

    public (bool success, OqcScannerConfig? config, string errorMessage) ImportConfigFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return (false, null, "Tệp cấu hình không tồn tại.");
            var json = File.ReadAllText(filePath);
            var loaded = JsonSerializer.Deserialize<OqcScannerConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (loaded != null)
            {
                SaveConfig(loaded);
                return (true, loaded, "");
            }
            return (false, null, "Nội dung tệp cấu hình không hợp lệ.");
        }
        catch (Exception ex)
        {
            return (false, null, $"Lỗi đọc tệp cấu hình: {ex.Message}");
        }
    }

    public void SaveScanHistory(IEnumerable<OqcScanHistoryEntry> history)
    {
        try
        {
            if (history == null) return;
            var list = history.Take(500).ToList();
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_historyFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveScanHistory error: {ex.Message}");
        }
    }

    public List<OqcScanHistoryEntry> LoadScanHistory()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                var json = File.ReadAllText(_historyFilePath);
                var loaded = JsonSerializer.Deserialize<List<OqcScanHistoryEntry>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadScanHistory error: {ex.Message}");
        }
        return new List<OqcScanHistoryEntry>();
    }

    private static Mat RotateImageNoClip(Mat src, double angleDeg)
    {
        if (src == null || src.IsDisposed || src.Empty()) return new Mat();

        Point2f center = new Point2f(src.Width / 2.0f, src.Height / 2.0f);
        using var rotMat = Cv2.GetRotationMatrix2D(center, angleDeg, 1.0);

        var rad = angleDeg * Math.PI / 180.0;
        var cos = Math.Abs(Math.Cos(rad));
        var sin = Math.Abs(Math.Sin(rad));
        int newWidth = (int)Math.Round(src.Width * cos + src.Height * sin);
        int newHeight = (int)Math.Round(src.Width * sin + src.Height * cos);

        rotMat.Set<double>(0, 2, rotMat.At<double>(0, 2) + (newWidth / 2.0 - center.X));
        rotMat.Set<double>(1, 2, rotMat.At<double>(1, 2) + (newHeight / 2.0 - center.Y));

        Mat dst = new Mat();
        Cv2.WarpAffine(src, dst, rotMat, new Size(newWidth, newHeight), InterpolationFlags.Linear, BorderTypes.Replicate);
        return dst;
    }

    /// <summary>
    /// Áp dụng cơ cấu kiểm soát độ dài chuỗi và cắt chuỗi cấu hình cho mã nhập/quét từ đầu đọc ngoài hoặc chuỗi mã bất kỳ.
    /// </summary>
    public (bool success, string processedCode, string rawCode, string errorMessage) ProcessRawCodeString(string rawInput, OqcScannerConfig? config = null)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return (false, "", "", "Chuỗi mã quét rỗng.");
        }

        string rawCode = rawInput.Trim();
        var cfg = config ?? Config ?? new OqcScannerConfig();

        // 1. Kiểm tra bộ lọc độ dài (nếu bật)
        if (cfg.EnableLengthFilter && cfg.RequiredCodeLength > 0)
        {
            if (rawCode.Length != cfg.RequiredCodeLength)
            {
                return (false, "", rawCode, $"Độ dài mã ({rawCode.Length}) không khớp với cấu hình yêu cầu ({cfg.RequiredCodeLength} ký tự).");
            }
        }

        // 2. Cắt chuỗi (nếu bật)
        string finalCode = rawCode;
        if (cfg.EnableCodeCrop)
        {
            int start = Math.Max(0, cfg.CropStartIndex);
            if (start < rawCode.Length)
            {
                int cropLen = cfg.CropLength;
                if (cropLen > 0 && start + cropLen <= rawCode.Length)
                {
                    finalCode = rawCode.Substring(start, cropLen);
                }
                else
                {
                    finalCode = rawCode.Substring(start);
                }
            }
            else
            {
                return (false, "", rawCode, $"Vị trí bắt đầu cắt ({start}) vượt quá độ dài mã gốc ({rawCode.Length} ký tự).");
            }
        }

        return (true, finalCode, rawCode, "");
    }

    public CameraCodeScanResult DecodeCodeFromImage(Mat image, OqcScannerConfig? config = null)
    {
        var cfg = config ?? Config ?? new OqcScannerConfig();
        if (image == null || image.IsDisposed || image.Empty() || image.Width <= 0 || image.Height <= 0)
        {
            return new CameraCodeScanResult
            {
                Success = false,
                ErrorMessage = "Hình ảnh camera không hợp lệ hoặc rỗng."
            };
        }

        try
        {
            using var grayMat = image.Channels() == 1 ? image.Clone() : image.CvtColor(ColorConversionCodes.BGR2GRAY);

            var allFoundResults = new List<(string text, string format)>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resultLock = new object();

            void AddCandidate(string text, string format)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                text = text.Trim();
                string key = $"{format}:{text}";
                lock (resultLock)
                {
                    if (seenKeys.Add(key))
                    {
                        allFoundResults.Add((text, format));
                    }
                }
            }

            var list2D = new List<BarcodeFormat>
            {
                BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.PDF_417, BarcodeFormat.AZTEC
            };

            var list1D = new List<BarcodeFormat>
            {
                BarcodeFormat.CODE_128, BarcodeFormat.CODE_39, BarcodeFormat.CODE_93,
                BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A,
                BarcodeFormat.UPC_E, BarcodeFormat.ITF, BarcodeFormat.CODABAR
            };

            // Hàm quét 1 Mat với danh sách Formats chỉ định (quản lý bộ nhớ an toàn)
            void ScanMat(Mat matToScan, List<BarcodeFormat>? allowedFormats)
            {
                if (matToScan == null || matToScan.IsDisposed || matToScan.Empty() || matToScan.Width <= 0 || matToScan.Height <= 0) return;

                Mat continuousMat;
                bool mustDisposeContinuous = false;

                if (matToScan.IsContinuous())
                {
                    continuousMat = matToScan;
                }
                else
                {
                    continuousMat = matToScan.Clone();
                    mustDisposeContinuous = true;
                }

                try
                {
                    int w = continuousMat.Width;
                    int h = continuousMat.Height;
                    var bytes = new byte[w * h];
                    System.Runtime.InteropServices.Marshal.Copy(continuousMat.Data, bytes, 0, bytes.Length);

                    var opts = new ZXing.Common.DecodingOptions
                    {
                        TryHarder = true,
                        TryInverted = true
                    };

                    if (allowedFormats != null && allowedFormats.Count > 0)
                    {
                        opts.PossibleFormats = allowedFormats;
                    }

                    var rdr = new BarcodeReaderGeneric
                    {
                        AutoRotate = true,
                        Options = opts
                    };

                    var lumSrc = new RGBLuminanceSource(bytes, w, h, RGBLuminanceSource.BitmapFormat.Gray8);

                    // 1. Decode Multiple
                    var multiRes = rdr.DecodeMultiple(lumSrc);
                    if (multiRes != null)
                    {
                        foreach (var r in multiRes)
                        {
                            if (r != null && !string.IsNullOrWhiteSpace(r.Text))
                            {
                                AddCandidate(r.Text, r.BarcodeFormat.ToString());
                            }
                        }
                    }

                    // 2. Decode Single fallback
                    var singleRes = rdr.Decode(lumSrc);
                    if (singleRes != null && !string.IsNullOrWhiteSpace(singleRes.Text))
                    {
                        AddCandidate(singleRes.Text, singleRes.BarcodeFormat.ToString());
                    }
                }
                finally
                {
                    if (mustDisposeContinuous)
                    {
                        continuousMat.Dispose();
                    }
                }
            }

            var targetType = cfg.TargetCodeType?.Trim().ToUpperInvariant() ?? "ALL";
            List<BarcodeFormat>? activeFormats2D = list2D;
            List<BarcodeFormat>? activeFormats1D = list1D;
            List<BarcodeFormat>? activeFormatsAll = null;

            if (targetType != "ALL")
            {
                var specificFormats = new List<BarcodeFormat>();
                switch (targetType)
                {
                    case "QR_CODE": case "QR": specificFormats.Add(BarcodeFormat.QR_CODE); break;
                    case "CODE_128": specificFormats.Add(BarcodeFormat.CODE_128); break;
                    case "CODE_39": specificFormats.Add(BarcodeFormat.CODE_39); break;
                    case "DATA_MATRIX": case "DATAMATRIX": specificFormats.Add(BarcodeFormat.DATA_MATRIX); break;
                    case "EAN_13": case "EAN13": specificFormats.Add(BarcodeFormat.EAN_13); break;
                    case "EAN_8": case "EAN8": specificFormats.Add(BarcodeFormat.EAN_8); break;
                    case "PDF_417": case "PDF417": specificFormats.Add(BarcodeFormat.PDF_417); break;
                    case "AZTEC": specificFormats.Add(BarcodeFormat.AZTEC); break;
                    case "BARCODE_1D": specificFormats.AddRange(list1D); break;
                    default:
                        if (Enum.TryParse<BarcodeFormat>(targetType, true, out var parsedFormat))
                        {
                            specificFormats.Add(parsedFormat);
                        }
                        break;
                }
                activeFormats2D = specificFormats;
                activeFormats1D = specificFormats;
                activeFormatsAll = specificFormats;
            }

            bool HasValidCandidate()
            {
                lock (resultLock)
                {
                    if (allFoundResults.Count == 0) return false;
                    foreach (var (text, fmt) in allFoundResults)
                    {
                        if (cfg.EnableLengthFilter && cfg.RequiredCodeLength > 0)
                        {
                            if (text.Length != cfg.RequiredCodeLength) continue;
                        }
                        return true;
                    }
                    return false;
                }
            }

            bool TryScanAngleMat(Mat matToScan)
            {
                ScanMat(matToScan, activeFormats2D);
                if (HasValidCandidate()) return true;

                ScanMat(matToScan, activeFormats1D);
                if (HasValidCandidate()) return true;

                if (activeFormatsAll == null)
                {
                    ScanMat(matToScan, null);
                    if (HasValidCandidate()) return true;
                }
                return false;
            }

            void ScanAnglesParallel(Mat baseMat, double[] angles)
            {
                if (HasValidCandidate()) return;

                var popts = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                Parallel.ForEach(angles, popts, (angle, state) =>
                {
                    if (HasValidCandidate())
                    {
                        state.Stop();
                        return;
                    }

                    using var rotMat = RotateImageNoClip(baseMat, angle);
                    if (TryScanAngleMat(rotMat))
                    {
                        state.Stop();
                    }
                });
            }

            // 🔥 THUẬT TOÁN ĐỌC MÃ 360° SIÊU TỐC ĐA TẦNG (5-STAGE PARALLEL OMNI-ENGINE) 🔥

            // STAGE 0: FAST-PASS TRÊN ĂNH DOWNSCALE (Nếu ảnh phân giải cao > 1200px)
            // Giảm kích thước ảnh giúp tăng tốc quét ZXing gấp 4-8 lần!
            if (grayMat.Width > 1200 || grayMat.Height > 1200)
            {
                double scale = 1000.0 / Math.Max(grayMat.Width, grayMat.Height);
                int nw = (int)(grayMat.Width * scale);
                int nh = (int)(grayMat.Height * scale);

                using var downMat = new Mat();
                Cv2.Resize(grayMat, downMat, new Size(nw, nh), 0, 0, InterpolationFlags.Area);

                // Quét 4 hướng chính (0°, 90°, 180°, 270°) trên ảnh downscale
                if (!TryScanAngleMat(downMat))
                {
                    using (var rot90 = new Mat())
                    {
                        Cv2.Rotate(downMat, rot90, RotateFlags.Rotate90Clockwise);
                        if (!TryScanAngleMat(rot90))
                        {
                            using (var rot180 = new Mat())
                            {
                                Cv2.Rotate(downMat, rot180, RotateFlags.Rotate180);
                                if (!TryScanAngleMat(rot180))
                                {
                                    using (var rot270 = new Mat())
                                    {
                                        Cv2.Rotate(downMat, rot270, RotateFlags.Rotate90Counterclockwise);
                                        TryScanAngleMat(rot270);
                                    }
                                }
                            }
                        }
                    }
                }

                // Quét các góc chéo chính (45°, 135°, 225°, 315°) song song trên ảnh downscale
                if (!HasValidCandidate())
                {
                    ScanAnglesParallel(downMat, new double[] { 45.0, 135.0, 225.0, 315.0 });
                }

                // Quét mịn 15° song song trên ảnh downscale
                if (!HasValidCandidate())
                {
                    ScanAnglesParallel(downMat, new double[] { 15.0, 30.0, 60.0, 75.0, 105.0, 120.0, 150.0, 165.0, 195.0, 210.0, 240.0, 255.0, 285.0, 300.0, 330.0, 345.0 });
                }

                // Nếu ảnh downscale đã tìm thấy mã -> Thoát sớm hoàn thành ngay lập tức trong vài ms!
                if (HasValidCandidate())
                {
                    goto ProcessFinalSelection;
                }
            }

            // STAGE 1: Quét 4 hướng chính trên ảnh gốc (0°, 90°, 180°, 270°)
            if (!TryScanAngleMat(grayMat))
            {
                using (var rot90 = new Mat())
                {
                    Cv2.Rotate(grayMat, rot90, RotateFlags.Rotate90Clockwise);
                    if (!TryScanAngleMat(rot90))
                    {
                        using (var rot180 = new Mat())
                        {
                            Cv2.Rotate(grayMat, rot180, RotateFlags.Rotate180);
                            if (!TryScanAngleMat(rot180))
                            {
                                using (var rot270 = new Mat())
                                {
                                    Cv2.Rotate(grayMat, rot270, RotateFlags.Rotate90Counterclockwise);
                                    TryScanAngleMat(rot270);
                                }
                            }
                        }
                    }
                }
            }

            // STAGE 2: Quét các góc nghiêng chéo chính (45°, 135°, 225°, 315°) song song đa luồng CPU
            if (!HasValidCandidate())
            {
                ScanAnglesParallel(grayMat, new double[] { 45.0, 135.0, 225.0, 315.0 });
            }

            // STAGE 3: Quét bước góc mịn 15° phủ 360° song song đa luồng CPU (đảm bảo sai số góc ≤ 7.5°)
            if (!HasValidCandidate())
            {
                ScanAnglesParallel(grayMat, new double[] { 15.0, 30.0, 60.0, 75.0, 105.0, 120.0, 150.0, 165.0, 195.0, 210.0, 240.0, 255.0, 285.0, 300.0, 330.0, 345.0 });
            }

            // STAGE 4: Tăng cường tương phản (EqualizeHist & Adaptive Threshold Binarization) cho ảnh mờ/tối
            if (!HasValidCandidate())
            {
                using var enhancedMat = new Mat();
                Cv2.EqualizeHist(grayMat, enhancedMat);
                if (!TryScanAngleMat(enhancedMat))
                {
                    ScanAnglesParallel(enhancedMat, new double[] { 45.0, 90.0, 135.0, 180.0, 225.0, 270.0, 315.0 });
                }
            }

            if (!HasValidCandidate())
            {
                using var binMat = new Mat();
                Cv2.AdaptiveThreshold(grayMat, binMat, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 21, 5);
                if (!TryScanAngleMat(binMat))
                {
                    ScanAnglesParallel(binMat, new double[] { 45.0, 90.0, 135.0, 180.0, 225.0, 270.0, 315.0 });
                }
            }

            // STAGE 5: Slicing / Phân vùng ảnh (Nửa trên, nửa dưới, nửa trái, nửa phải)
            if (!HasValidCandidate())
            {
                int w = grayMat.Width;
                int h = grayMat.Height;

                if (w > 100 && h > 100)
                {
                    using (var topCrop = new Mat(grayMat, new Rect(0, 0, w, h / 2))) { TryScanAngleMat(topCrop); }
                    if (!HasValidCandidate())
                    {
                        using (var botCrop = new Mat(grayMat, new Rect(0, h / 2, w, h - h / 2))) { TryScanAngleMat(botCrop); }
                    }
                    if (!HasValidCandidate())
                    {
                        using (var leftCrop = new Mat(grayMat, new Rect(0, 0, w / 2, h))) { TryScanAngleMat(leftCrop); }
                    }
                    if (!HasValidCandidate())
                    {
                        using (var rightCrop = new Mat(grayMat, new Rect(w / 2, 0, w - w / 2, h))) { TryScanAngleMat(rightCrop); }
                    }
                }
            }

        ProcessFinalSelection:

            if (allFoundResults.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[OQC Barcode Scanner] ⚠️ Không tìm thấy mã QR/Barcode nào trong ảnh camera.");
                return new CameraCodeScanResult
                {
                    Success = false,
                    ErrorMessage = "Không tìm thấy mã QR/Barcode trong ảnh camera."
                };
            }

            // Ghi log chi tiết số lượng và nội dung tất cả các mã nhận diện được ra Output Window
            System.Diagnostics.Debug.WriteLine($"==================================================");
            System.Diagnostics.Debug.WriteLine($"[OQC Barcode Scanner] 📊 SỐ LƯỢNG MÃ ĐÃ NHẬN DIỆN ĐƯỢC: {allFoundResults.Count}");
            for (int i = 0; i < allFoundResults.Count; i++)
            {
                System.Diagnostics.Debug.WriteLine($"  ├─ Mã #{i + 1}: '{allFoundResults[i].text}' | Định dạng: {allFoundResults[i].format}");
            }

            // Lọc ứng viên theo độ dài n
            var candidateResults = new List<(string raw, string format)>();
            foreach (var (text, fmt) in allFoundResults)
            {
                if (cfg.EnableLengthFilter && cfg.RequiredCodeLength > 0)
                {
                    if (text.Length != cfg.RequiredCodeLength)
                    {
                        System.Diagnostics.Debug.WriteLine($"  [Bộ lọc] Loại bỏ mã '{text}' do độ dài ({text.Length}) != {cfg.RequiredCodeLength}");
                        continue;
                    }
                }

                candidateResults.Add((text, fmt));
            }

            if (candidateResults.Count == 0)
            {
                var foundRawTexts = string.Join(", ", allFoundResults.Select(r => $"'{r.text}' ({r.format})"));
                string reqMsg = cfg.EnableLengthFilter && cfg.RequiredCodeLength > 0 ? $"độ dài {cfg.RequiredCodeLength} ký tự" : "";
                string typeMsg = targetType != "ALL" ? $"loại mã {targetType}" : "";
                string filterDesc = string.Join(" & ", new[] { typeMsg, reqMsg }.Where(s => !string.IsNullOrEmpty(s)));

                System.Diagnostics.Debug.WriteLine($"[OQC Barcode Scanner] ❌ Tất cả {allFoundResults.Count} mã đều bị loại bởi bộ lọc ({filterDesc}).");
                System.Diagnostics.Debug.WriteLine($"==================================================");

                return new CameraCodeScanResult
                {
                    Success = false,
                    ErrorMessage = $"Đã nhận diện {allFoundResults.Count} mã [{foundRawTexts}], nhưng không mã nào thỏa mãn bộ lọc ({filterDesc})."
                };
            }

            var (selectedRawCode, selectedFormat) = candidateResults[0];
            string finalProcessedCode = selectedRawCode;

            if (cfg.EnableCodeCrop)
            {
                int start = Math.Max(0, cfg.CropStartIndex);
                if (start < selectedRawCode.Length)
                {
                    int cropLen = cfg.CropLength;
                    if (cropLen > 0 && start + cropLen <= selectedRawCode.Length)
                    {
                        finalProcessedCode = selectedRawCode.Substring(start, cropLen);
                    }
                    else
                    {
                        finalProcessedCode = selectedRawCode.Substring(start);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[OQC Barcode Scanner] ❌ Lỗi cắt chuỗi: StartIndex ({start}) >= Độ dài mã gốc ({selectedRawCode.Length}).");
                    System.Diagnostics.Debug.WriteLine($"==================================================");

                    return new CameraCodeScanResult
                    {
                        Success = false,
                        RawCode = selectedRawCode,
                        CodeType = selectedFormat,
                        ErrorMessage = $"Vị trí bắt đầu cắt ({start}) vượt quá độ dài mã gốc ({selectedRawCode.Length} ký tự)."
                    };
                }
            }

            System.Diagnostics.Debug.WriteLine($"[OQC Barcode Scanner] 🎯 NỘI DUNG MÃ ĐƯỢC CHỌN: '{selectedRawCode}' (Loại mã: {selectedFormat})");
            System.Diagnostics.Debug.WriteLine($"[OQC Barcode Scanner] ✂️ GIÁ TRỊ SCANNEDCODE CUỐI CÙNG: '{finalProcessedCode}'");
            System.Diagnostics.Debug.WriteLine($"==================================================");

            return new CameraCodeScanResult
            {
                Success = true,
                RawCode = selectedRawCode,
                ProcessedCode = finalProcessedCode,
                CodeType = selectedFormat,
                ErrorMessage = ""
            };
        }
        catch (Exception ex)
        {
            return new CameraCodeScanResult
            {
                Success = false,
                ErrorMessage = $"Lỗi xử lý đọc mã camera: {ex.Message}"
            };
        }
    }

    private string ResolveEffectiveDbId(string configuredDbId, IDbManagerService dbManager)
    {
        if (dbManager == null) return configuredDbId ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(configuredDbId) && dbManager.GetDatabase(configuredDbId) != null)
        {
            return configuredDbId;
        }

        // Tự động fallback sang các cấu hình DB khác nếu cấu hình hiện tại bị đổi GUID khi chuyển máy
        if (!string.IsNullOrWhiteSpace(Config.ProductListDbId) && dbManager.GetDatabase(Config.ProductListDbId) != null)
            return Config.ProductListDbId;
        if (!string.IsNullOrWhiteSpace(Config.LookupDbId) && dbManager.GetDatabase(Config.LookupDbId) != null)
            return Config.LookupDbId;
        if (!string.IsNullOrWhiteSpace(Config.JobManagerDbId) && dbManager.GetDatabase(Config.JobManagerDbId) != null)
            return Config.JobManagerDbId;
        if (!string.IsNullOrWhiteSpace(Config.AssignDbId) && dbManager.GetDatabase(Config.AssignDbId) != null)
            return Config.AssignDbId;
        if (!string.IsNullOrWhiteSpace(Config.ProductNameDbId) && dbManager.GetDatabase(Config.ProductNameDbId) != null)
            return Config.ProductNameDbId;

        var firstActive = dbManager.Databases.FirstOrDefault(d => d.IsEnabled);
        return firstActive?.Id ?? configuredDbId ?? string.Empty;
    }

    public async Task<(bool Found, string JobFilePath, string ErrorMessage)> LookupJobAsync(
        string scannedCode, IDbManagerService dbManager, VisionInspectionApp.Application.Services.IRemoteServerService? remoteServerService = null)
    {
        if (string.IsNullOrWhiteSpace(scannedCode))
        {
            return (false, string.Empty, "Mã scan rỗng.");
        }

        if (dbManager == null)
        {
            return (false, string.Empty, "Dịch vụ DB Manager chưa được khởi tạo.");
        }

        if (string.IsNullOrWhiteSpace(Config.LookupQuery))
        {
            return (false, string.Empty, "Chưa cấu hình truy vấn tra cứu Job (Lookup Query).");
        }

        string safeCode = EscapeSqlValue(scannedCode.Trim());
        string query = Config.LookupQuery.Replace("{ScannedCode}", safeCode, StringComparison.OrdinalIgnoreCase);

        // Validate safety (Read mode)
        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Read, allowUpdateDelete: false);
        if (!isSafe)
        {
            return (false, string.Empty, safetyError);
        }

        string effectiveDbId = ResolveEffectiveDbId(Config.LookupDbId, dbManager);
        var (success, table, error) = await dbManager.ExecuteQueryAsync(effectiveDbId, query);
        if (!success || table == null || table.Rows.Count == 0)
        {
            return (false, string.Empty, string.IsNullOrWhiteSpace(error) ? $"Không tìm thấy Job cho mã '{scannedCode}' trong cơ sở dữ liệu." : error);
        }

        // Extract column
        string colName = Config.JobFilePathColumn?.Trim() ?? "";
        object? rawVal = null;

        if (!string.IsNullOrEmpty(colName) && table.Columns.Contains(colName))
        {
            rawVal = table.Rows[0][colName];
        }
        else if (table.Columns.Count > 0)
        {
            rawVal = table.Rows[0][0];
        }

        if (rawVal == null || rawVal == DBNull.Value)
        {
            return (false, string.Empty, $"Kết quả DB trả về ô rỗng cho mã '{scannedCode}'.");
        }

        string rawPath = rawVal.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return (false, string.Empty, $"Đường dẫn Job từ DB rỗng cho mã '{scannedCode}'.");
        }

        string productCodeClean = scannedCode.Trim();

        // 1. Kiểm tra tồn tại tệp cục bộ (Local Existence Check)
        // 1.1 Kiểm tra trực tiếp đường dẫn trả về từ DB
        if (File.Exists(rawPath))
        {
            return (true, rawPath, string.Empty);
        }

        // 1.2 Kiểm tra trong thư mục gốc mặc định JobRootDirectory
        if (!string.IsNullOrWhiteSpace(Config.JobRootDirectory))
        {
            string candidate1 = Path.Combine(Config.JobRootDirectory, $"{productCodeClean}.job");
            if (File.Exists(candidate1))
            {
                return (true, candidate1, string.Empty);
            }

            string fileNameOnly = Path.GetFileName(rawPath);
            string candidate2 = Path.Combine(Config.JobRootDirectory, fileNameOnly);
            if (File.Exists(candidate2))
            {
                return (true, candidate2, string.Empty);
            }

            string candidate3 = Path.Combine(Config.JobRootDirectory, rawPath.TrimStart('\\', '/'));
            if (File.Exists(candidate3))
            {
                return (true, candidate3, string.Empty);
            }
        }

        // 1.3 Kiểm tra trong thư mục 'jobs' cùng thư mục chạy ứng dụng
        string localJobsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "jobs");
        string localCandidate1 = Path.Combine(localJobsDir, $"{productCodeClean}.job");
        if (File.Exists(localCandidate1))
        {
            return (true, localCandidate1, string.Empty);
        }
        string localCandidate2 = Path.Combine(localJobsDir, Path.GetFileName(rawPath));
        if (File.Exists(localCandidate2))
        {
            return (true, localCandidate2, string.Empty);
        }

        // 1.4 Kiểm tra tại thư mục gốc ứng dụng
        string appRootCandidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{productCodeClean}.job");
        if (File.Exists(appRootCandidate))
        {
            return (true, appRootCandidate, string.Empty);
        }

        // 2. Nếu cục bộ không có và đường dẫn từ DB là Server/URL -> Tự động tải từ Server về
        bool isRemotePath = rawPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            rawPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                            rawPath.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase) ||
                            rawPath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) ||
                            (!Path.IsPathRooted(rawPath) && rawPath.EndsWith(".job", StringComparison.OrdinalIgnoreCase));

        if (remoteServerService != null && isRemotePath)
        {
            try
            {
                string downloadUrl = rawPath;
                if (!downloadUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    string baseUrl = GetServerBaseUrl(Config.ServerApiUrl);
                    downloadUrl = $"{baseUrl}/{rawPath.TrimStart('/')}";
                }

                var (dlOk, jobData, dlErr) = await remoteServerService.DownloadFileAsync(downloadUrl);
                if (dlOk && jobData != null && jobData.Length > 0)
                {
                    string targetDir = !string.IsNullOrWhiteSpace(Config.JobRootDirectory)
                        ? Config.JobRootDirectory
                        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "jobs");
                    Directory.CreateDirectory(targetDir);

                    string fileNameToSave = Path.GetFileName(rawPath);
                    if (string.IsNullOrWhiteSpace(fileNameToSave) || fileNameToSave.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        fileNameToSave = $"{productCodeClean}.job";
                    }

                    string targetFilePath = Path.Combine(targetDir, fileNameToSave);
                    await File.WriteAllBytesAsync(targetFilePath, jobData);
                    return (true, targetFilePath, string.Empty);
                }
                else
                {
                    return (false, rawPath, $"Không tìm thấy Job cục bộ và tải từ Server ({downloadUrl}) thất bại: {dlErr}");
                }
            }
            catch (Exception ex)
            {
                return (false, rawPath, $"Lỗi tải Job từ Server: {ex.Message}");
            }
        }

        return (false, rawPath, $"Không tìm thấy tệp Job tại đường dẫn: '{rawPath}'" +
            (!string.IsNullOrWhiteSpace(Config.JobRootDirectory) ? $" hoặc trong thư mục gốc '{Config.JobRootDirectory}'." : "."));
    }

    public async Task<(bool Success, string Message)> UpdateTeachImagePathAsync(
        string productCode, string teachImagePath, IDbManagerService dbManager)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return (false, "Mã sản phẩm rỗng.");
        }

        if (string.IsNullOrWhiteSpace(teachImagePath))
        {
            return (false, "Đường dẫn ảnh mẫu rỗng.");
        }

        if (dbManager == null)
        {
            return (false, "Dịch vụ DB Manager chưa được khởi tạo.");
        }

        string queryTemplate = !string.IsNullOrWhiteSpace(Config.UpdateTeachImageQuery)
            ? Config.UpdateTeachImageQuery
            : "IF EXISTS (SELECT 1 FROM ProductJobs WHERE ProductCode = '{ProductCode}') UPDATE ProductJobs SET TeachImagePath = '{TeachImagePath}', UpdatedAt = GETDATE() WHERE ProductCode = '{ProductCode}' ELSE INSERT INTO ProductJobs (ProductCode, TeachImagePath, UpdatedAt) VALUES ('{ProductCode}', '{TeachImagePath}', GETDATE())";

        string safeCode = EscapeSqlValue(productCode.Trim());
        string safeTeachPath = EscapeSqlValue(teachImagePath.Trim());

        string query = queryTemplate
            .Replace("{ProductCode}", safeCode, StringComparison.OrdinalIgnoreCase)
            .Replace("{TeachImagePath}", safeTeachPath, StringComparison.OrdinalIgnoreCase);

        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Write, allowUpdateDelete: true);
        if (!isSafe)
        {
            return (false, safetyError);
        }

        string dbId = !string.IsNullOrWhiteSpace(Config.UpdateTeachImageDbId)
            ? Config.UpdateTeachImageDbId
            : (!string.IsNullOrWhiteSpace(Config.AssignDbId) ? Config.AssignDbId : Config.LookupDbId);

        var (success, rows, error) = await dbManager.ExecuteNonQueryAsync(dbId, query);
        if (success)
        {
            return (true, $"✅ Cập nhật ảnh mẫu cho mã '{productCode}' thành công! (Số dòng tác động: {rows})");
        }
        else
        {
            return (false, $"Lỗi DB: {error}");
        }
    }

    private static string GetServerBaseUrl(string serverApiUrl)
    {
        if (string.IsNullOrWhiteSpace(serverApiUrl)) return "http://localhost";
        try
        {
            var uri = new Uri(serverApiUrl.Trim());
            return $"{uri.Scheme}://{uri.Authority}";
        }
        catch
        {
            return "http://localhost";
        }
    }

    public async Task<(bool Found, string ProductName, string ErrorMessage)> LookupProductNameAsync(
        string scannedCode, IDbManagerService dbManager)
    {
        if (string.IsNullOrWhiteSpace(scannedCode))
        {
            return (false, string.Empty, "Mã scan rỗng.");
        }

        if (!Config.EnableProductNameLookup || string.IsNullOrWhiteSpace(Config.ProductNameQuery))
        {
            return (false, scannedCode, "Chưa bật hoặc chưa cấu hình truy vấn Tên sản phẩm.");
        }

        if (dbManager == null)
        {
            return (false, scannedCode, "Dịch vụ DB Manager chưa được khởi tạo.");
        }

        string safeCode = EscapeSqlValue(scannedCode.Trim());
        string query = Config.ProductNameQuery.Replace("{ScannedCode}", safeCode, StringComparison.OrdinalIgnoreCase);

        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Read, allowUpdateDelete: false);
        if (!isSafe)
        {
            return (false, scannedCode, safetyError);
        }

        string effectiveDbId = ResolveEffectiveDbId(Config.ProductNameDbId, dbManager);
        var (success, table, error) = await dbManager.ExecuteQueryAsync(effectiveDbId, query);
        if (!success || table == null || table.Rows.Count == 0)
        {
            return (false, scannedCode, string.IsNullOrWhiteSpace(error) ? $"Không tìm thấy Tên sản phẩm cho mã '{scannedCode}' trong cơ sở dữ liệu." : error);
        }

        string colName = Config.ProductNameColumn?.Trim() ?? "";
        object? rawVal = null;

        if (!string.IsNullOrEmpty(colName) && table.Columns.Contains(colName))
        {
            rawVal = table.Rows[0][colName];
        }
        else if (table.Columns.Count > 0)
        {
            rawVal = table.Rows[0][0];
        }

        if (rawVal == null || rawVal == DBNull.Value)
        {
            return (false, scannedCode, "Kết quả DB trả về ô tên sản phẩm rỗng.");
        }

        string name = rawVal.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, scannedCode, "Tên sản phẩm trả về rỗng.");
        }

        return (true, name, string.Empty);
    }

    public async Task<(bool Success, DataTable? Table, string ErrorMessage)> GetProductListAsync(
        string searchText, int pageIndex, IDbManagerService dbManager)
    {
        if (dbManager == null)
        {
            return (false, null, "DB Manager service not available.");
        }

        if (string.IsNullOrWhiteSpace(Config.ProductListQuery))
        {
            return (false, null, "Chưa cấu hình truy vấn danh sách sản phẩm.");
        }

        int pageSize = Math.Max(1, Config.ProductListPageSize);
        int offset = Math.Max(0, pageIndex * pageSize);
        string safeSearch = EscapeSqlValue((searchText ?? "").Trim());

        string query = Config.ProductListQuery
            .Replace("{SearchText}", safeSearch, StringComparison.OrdinalIgnoreCase)
            .Replace("{Offset}", offset.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{PageSize}", pageSize.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Read, allowUpdateDelete: false);
        if (!isSafe)
        {
            return (false, null, safetyError);
        }

        string effectiveDbId = ResolveEffectiveDbId(Config.ProductListDbId, dbManager);
        var (success, table, error) = await dbManager.ExecuteQueryAsync(effectiveDbId, query);
        return (success, table, error);
    }

    public async Task<(bool Success, DataTable? Table, string ErrorMessage)> GetJobManagerListAsync(
        string searchText, int pageIndex, IDbManagerService dbManager)
    {
        if (dbManager == null)
        {
            return (false, null, "DB Manager service not available.");
        }

        if (string.IsNullOrWhiteSpace(Config.JobManagerQuery))
        {
            return (false, null, "Chưa cấu hình truy vấn danh sách Job (Job Manager Query). Vui lòng vào Cài Đặt OQC -> Mục 5 để kiểm tra.");
        }

        string effectiveDbId = ResolveEffectiveDbId(Config.JobManagerDbId, dbManager);
        if (string.IsNullOrWhiteSpace(effectiveDbId) || dbManager.GetDatabase(effectiveDbId) == null)
        {
            return (false, null, $"Chưa chọn kết nối CSDL hợp lệ cho Quản Lý Job (DB ID '{Config.JobManagerDbId}' không tồn tại trong danh sách CSDL của máy này). Vui lòng vào menu Cài Đặt OQC -> Mục 5 để chọn lại CSDL.");
        }

        int pageSize = Math.Max(1, Config.JobManagerPageSize);
        int offset = Math.Max(0, pageIndex * pageSize);
        string safeSearch = EscapeSqlValue((searchText ?? "").Trim());

        string query = Config.JobManagerQuery
            .Replace("{SearchText}", safeSearch, StringComparison.OrdinalIgnoreCase)
            .Replace("{Offset}", offset.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{PageSize}", pageSize.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Read, allowUpdateDelete: false);
        if (!isSafe)
        {
            return (false, null, safetyError);
        }

        var (success, table, error) = await dbManager.ExecuteQueryAsync(effectiveDbId, query);
        if (!success)
        {
            if (!string.IsNullOrWhiteSpace(error) && (error.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) || error.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase)))
            {
                return (false, null, $"Lỗi CSDL '{dbManager.GetDatabase(effectiveDbId)?.Name}': Bảng dữ liệu trong câu truy vấn chưa tồn tại.\nChi tiết: {error}\n\n👉 Hướng dẫn: Vui lòng kiểm tra lại tên bảng trong Job Manager Query (Cài Đặt OQC -> Mục 5) hoặc tạo bảng ProductJobs (ProductCode, ProductName, JobFilePath, TeachImagePath, UpdatedAt) trên máy chủ CSDL.");
            }
        }

        return (success, table, error);
    }

    public async Task<(bool Success, string Message)> AssignProductJobAsync(
        string productCode, string jobFilePath, IDbManagerService dbManager, string teachImagePath = "")
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return (false, "Mã sản phẩm rỗng.");
        }

        if (string.IsNullOrWhiteSpace(jobFilePath))
        {
            if (!string.IsNullOrWhiteSpace(teachImagePath))
            {
                return await UpdateTeachImagePathAsync(productCode, teachImagePath, dbManager);
            }
            return (false, "Đường dẫn tệp Job rỗng.");
        }

        if (dbManager == null)
        {
            return (false, "DB Manager service not available.");
        }

        if (string.IsNullOrWhiteSpace(Config.AssignQuery))
        {
            return (false, "Chưa cấu hình truy vấn Gán sản phẩm (Assign Query).");
        }

        string safeCode = EscapeSqlValue(productCode.Trim());
        string safePath = EscapeSqlValue(jobFilePath.Trim());
        string safeTeachPath = EscapeSqlValue((teachImagePath ?? "").Trim());

        string query = Config.AssignQuery
            .Replace("{ProductCode}", safeCode, StringComparison.OrdinalIgnoreCase)
            .Replace("{JobFilePath}", safePath, StringComparison.OrdinalIgnoreCase)
            .Replace("{TeachImagePath}", safeTeachPath, StringComparison.OrdinalIgnoreCase);

        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Write, allowUpdateDelete: true);
        if (!isSafe)
        {
            return (false, safetyError);
        }

        string effectiveDbId = ResolveEffectiveDbId(Config.AssignDbId, dbManager);
        var (success, rows, error) = await dbManager.ExecuteNonQueryAsync(effectiveDbId, query);
        if (success)
        {
            return (true, $"✅ Gán sản phẩm '{productCode}' với Job '{Path.GetFileName(jobFilePath)}' thành công! (Số dòng tác động: {rows})");
        }
        else
        {
            return (false, $"Lỗi DB: {error}");
        }
    }

    public async Task<(bool Success, string Message)> LogInspectionResultAsync(
        string scannedCode, string uuid, string jobFilePath, InspectionResult result, VisionConfig config, IDbManagerService dbManager, List<OqcMeasurementDetail>? measurementDetails = null, string rawCode = "")
    {
        if (string.IsNullOrWhiteSpace(uuid))
        {
            uuid = Guid.NewGuid().ToString("N");
        }

        if (!Config.LogResultToDb && !Config.LogDetailResultToDb)
        {
            return (true, "Ghi log DB bị tắt trong cấu hình.");
        }

        if (dbManager == null)
        {
            return (false, "Dịch vụ Quản lý Cơ sở dữ liệu (DbManager) chưa sẵn sàng.");
        }

        string safeCode = EscapeSqlValue((scannedCode ?? "").Trim());
        string safeRawCode = EscapeSqlValue(string.IsNullOrWhiteSpace(rawCode) ? safeCode : rawCode.Trim());
        string safeUuid = EscapeSqlValue(uuid.Trim());
        string safeProductName = EscapeSqlValue((config?.ProductName ?? "").Trim());
        string safePath = EscapeSqlValue((jobFilePath ?? "").Trim());
        string passBit = (result != null && result.Pass) ? "1" : "0";
        string inspectResultText = (result != null && result.Pass) ? "PASS" : "NG";
        string ngReasons = result != null ? EscapeSqlValue(ExtractNgReasons(result)) : "";

        int totalMasterRows = 0;
        int totalDetailRows = 0;
        var errorList = new List<string>();

        // 1. Ghi Master Log vào bảng Log tổng (LogResultQuery)
        if (Config.LogResultToDb)
        {
            if (string.IsNullOrWhiteSpace(Config.LogResultDbId))
            {
                string msg = "Chưa chọn CSDL cho Master Log (Mục 5 trong Cài đặt OQC).";
                System.Diagnostics.Debug.WriteLine($"[OQC DB Log] ⚠️ {msg}");
                errorList.Add(msg);
            }
            else if (!string.IsNullOrWhiteSpace(Config.LogResultQuery))
            {
                string query = Config.LogResultQuery
                    .Replace("{ScannedCode}", safeCode, StringComparison.OrdinalIgnoreCase)
                    .Replace("{ProductCode}", safeCode, StringComparison.OrdinalIgnoreCase)
                    .Replace("{RawCode}", safeRawCode, StringComparison.OrdinalIgnoreCase)
                    .Replace("{RawScannedCode}", safeRawCode, StringComparison.OrdinalIgnoreCase)
                    .Replace("{FullScannedCode}", safeRawCode, StringComparison.OrdinalIgnoreCase)
                    .Replace("{UUID}", safeUuid, StringComparison.OrdinalIgnoreCase)
                    .Replace("{Uuid}", safeUuid, StringComparison.OrdinalIgnoreCase)
                    .Replace("{ProductName}", safeProductName, StringComparison.OrdinalIgnoreCase)
                    .Replace("{JobFilePath}", safePath, StringComparison.OrdinalIgnoreCase)
                    .Replace("{PassBit}", passBit, StringComparison.OrdinalIgnoreCase)
                    .Replace("{Pass}", passBit, StringComparison.OrdinalIgnoreCase)
                    .Replace("{InspectResult}", inspectResultText, StringComparison.OrdinalIgnoreCase)
                    .Replace("{NgReasons}", ngReasons, StringComparison.OrdinalIgnoreCase);

                if (result != null && config != null)
                {
                    query = DbNodeRunner.InterpolateSqlQuery(query, result, config);
                }

                System.Diagnostics.Debug.WriteLine($"[OQC DB Log] 📤 Ghi Master Log vào DB ID='{Config.LogResultDbId}':\n{query}");

                var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Write, allowUpdateDelete: true);
                if (!isSafe)
                {
                    string msg = $"Master Log query không an toàn: {safetyError}";
                    System.Diagnostics.Debug.WriteLine($"[OQC DB Log] ❌ {msg}");
                    errorList.Add(msg);
                }
                else
                {
                    var (success, rows, error) = await dbManager.ExecuteNonQueryAsync(Config.LogResultDbId, query);
                    if (success)
                    {
                        totalMasterRows = rows;
                        System.Diagnostics.Debug.WriteLine($"[OQC DB Log] ✅ Ghi Master Log thành công ({rows} dòng).");
                    }
                    else
                    {
                        string msg = $"Lỗi ghi Master Log (DB ID: '{Config.LogResultDbId}'): {error}";
                        System.Diagnostics.Debug.WriteLine($"[OQC DB Log] ❌ {msg}");
                        errorList.Add(msg);
                    }
                }
            }
        }

        // 2. Ghi Detail Log từng phép đo vào bảng Chi tiết (LogDetailResultQuery)
        if (Config.LogDetailResultToDb)
        {
            if (string.IsNullOrWhiteSpace(Config.LogDetailResultDbId))
            {
                string msg = "Chưa chọn CSDL cho Detail Log (Mục 6 trong Cài đặt OQC).";
                System.Diagnostics.Debug.WriteLine($"[OQC DB Log] ⚠️ {msg}");
                errorList.Add(msg);
            }
            else if (!string.IsNullOrWhiteSpace(Config.LogDetailResultQuery))
            {
                measurementDetails ??= (result != null && config != null) ? ExtractMeasurementDetails(result, config) : new List<OqcMeasurementDetail>();
                if (measurementDetails.Count > 0)
                {
                    int detailFailCount = 0;
                    string firstDetailError = "";

                    foreach (var detail in measurementDetails)
                    {
                        string safeToolName = EscapeSqlValue(detail.ToolName);
                        string safeToolType = EscapeSqlValue(detail.ToolType);
                        string safeJudge = EscapeSqlValue(detail.Judge);
                        string safeUnit = EscapeSqlValue(detail.Unit);
                        string detailPassBit = detail.Pass ? "1" : "0";

                        // Giá trị số thực (float/double) thuần túy cho các cột số float trong CSDL SQL
                        string specStr = detail.HasNumericSpec && !double.IsNaN(detail.Spec) 
                            ? detail.Spec.ToString(CultureInfo.InvariantCulture) 
                            : "0";

                        string tolPlusStr = detail.HasNumericSpec && !double.IsNaN(detail.TolPlus) 
                            ? detail.TolPlus.ToString(CultureInfo.InvariantCulture) 
                            : "0";

                        string tolMinusStr = detail.HasNumericSpec && !double.IsNaN(detail.TolMinus) 
                            ? detail.TolMinus.ToString(CultureInfo.InvariantCulture) 
                            : "0";

                        string minStr = detail.HasNumericSpec && !double.IsNaN(detail.Min) 
                            ? detail.Min.ToString(CultureInfo.InvariantCulture) 
                            : "0";

                        string maxStr = detail.HasNumericSpec && !double.IsNaN(detail.Max) 
                            ? detail.Max.ToString(CultureInfo.InvariantCulture) 
                            : "0";

                        string resultStr = !double.IsNaN(detail.Result) 
                            ? detail.Result.ToString(CultureInfo.InvariantCulture) 
                            : (detail.Pass ? "1" : "0");

                        // Giá trị chuỗi Text cho các cột TextSpect, TextResult nvarchar trong CSDL SQL
                        string rawTextSpec = !string.IsNullOrEmpty(detail.CustomSpecText) 
                            ? detail.CustomSpecText 
                            : (detail.HasNumericSpec ? $"{detail.Spec:F3}" : "");
                        string safeTextSpec = EscapeSqlValue(rawTextSpec);

                        string rawTextResult = !string.IsNullOrEmpty(detail.CustomResultText) 
                            ? detail.CustomResultText 
                            : (!double.IsNaN(detail.Result) ? $"{detail.Result:F3}" : (detail.Pass ? "PASS" : "NG"));
                        string safeTextResult = EscapeSqlValue(rawTextResult);

                        string detailQuery = Config.LogDetailResultQuery
                            .Replace("{ScannedCode}", safeCode, StringComparison.OrdinalIgnoreCase)
                            .Replace("{ProductCode}", safeCode, StringComparison.OrdinalIgnoreCase)
                            .Replace("{RawCode}", safeRawCode, StringComparison.OrdinalIgnoreCase)
                            .Replace("{RawScannedCode}", safeRawCode, StringComparison.OrdinalIgnoreCase)
                            .Replace("{FullScannedCode}", safeRawCode, StringComparison.OrdinalIgnoreCase)
                            .Replace("{UUID}", safeUuid, StringComparison.OrdinalIgnoreCase)
                            .Replace("{Uuid}", safeUuid, StringComparison.OrdinalIgnoreCase)
                            .Replace("{ToolName}", safeToolName, StringComparison.OrdinalIgnoreCase)
                            .Replace("{ToolType}", safeToolType, StringComparison.OrdinalIgnoreCase)
                            .Replace("{Spec}", specStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{Nominal}", specStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{UpperTor}", tolPlusStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{TolPlus}", tolPlusStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{Tol +}", tolPlusStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{LowerTor}", tolMinusStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{TolMinus}", tolMinusStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{Tol -}", tolMinusStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{MinSpec}", minStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{Min}", minStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{MinVal}", minStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{MaxSpec}", maxStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{Max}", maxStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{MaxVal}", maxStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{Result}", resultStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{ResultVal}", resultStr, StringComparison.OrdinalIgnoreCase)
                            .Replace("{TextSpect}", safeTextSpec, StringComparison.OrdinalIgnoreCase)
                            .Replace("{TextSpec}", safeTextSpec, StringComparison.OrdinalIgnoreCase)
                            .Replace("{TextResult}", safeTextResult, StringComparison.OrdinalIgnoreCase)
                            .Replace("{Unit}", safeUnit, StringComparison.OrdinalIgnoreCase)
                            .Replace("{Judge}", safeJudge, StringComparison.OrdinalIgnoreCase)
                            .Replace("{PassBit}", detailPassBit, StringComparison.OrdinalIgnoreCase)
                            .Replace("{JobFilePath}", safePath, StringComparison.OrdinalIgnoreCase);

                        var (isDetailSafe, detailSafetyError) = DbNodeRunner.ValidateSqlQuerySafety(detailQuery, DbNodeMode.Write, allowUpdateDelete: true);
                        if (isDetailSafe)
                        {
                            var (dSuccess, dRows, dError) = await dbManager.ExecuteNonQueryAsync(Config.LogDetailResultDbId, detailQuery);
                            if (dSuccess)
                            {
                                totalDetailRows += dRows;
                            }
                            else
                            {
                                detailFailCount++;
                                if (string.IsNullOrEmpty(firstDetailError)) firstDetailError = dError;
                                System.Diagnostics.Debug.WriteLine($"[OQC DB Log] ❌ Lỗi Detail #{detail.Index} ({detail.ToolName}): {dError}");
                            }
                        }
                        else
                        {
                            detailFailCount++;
                            if (string.IsNullOrEmpty(firstDetailError)) firstDetailError = detailSafetyError;
                        }
                    }

                    if (detailFailCount > 0)
                    {
                        errorList.Add($"Lỗi ghi Detail Log ({detailFailCount}/{measurementDetails.Count} phép đo bị lỗi). Lỗi đầu tiên: {firstDetailError}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[OQC DB Log] ✅ Ghi toàn bộ Detail Log thành công ({totalDetailRows} dòng).");
                    }
                }
            }
        }

        if (errorList.Count == 0)
        {
            return (true, $"✅ Đã lưu DB thành công! (Master: {totalMasterRows} dòng, Chi tiết: {totalDetailRows} dòng)");
        }
        else
        {
            return (false, string.Join("; ", errorList));
        }
    }

    public List<OqcMeasurementDetail> ExtractMeasurementDetails(InspectionResult result, VisionConfig config)
    {
        var list = new List<OqcMeasurementDetail>();
        if (result == null || config == null) return list;

        bool isCalibrated = config.PixelsPerMm > 0 && Math.Abs(config.PixelsPerMm - 1.0) > 1e-6;
        string defaultUnit = isCalibrated ? "mm" : "px";
        int idx = 1;

        // 1. Origin Matcher
        if (result.Origin != null)
        {
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = "Origin (Gốc tọa độ)",
                ToolType = "Origin",
                HasNumericSpec = false,
                CustomSpecText = $">= {(config.Origin?.MatchScoreThreshold ?? 0.5):F2}",
                CustomResultText = $"{result.Origin.Score:F3}",
                Spec = 0,
                TolPlus = 0,
                TolMinus = 0,
                Min = 0,
                Max = 0,
                Result = result.Origin.Score,
                Unit = "",
                Pass = result.Origin.Pass
            });
        }

        // 2. Distances (Khoảng cách điểm - điểm)
        foreach (var d in result.Distances)
        {
            var def = config.Distances?.FirstOrDefault(x => string.Equals(x.Name, d.Name, StringComparison.OrdinalIgnoreCase));
            double nom = def?.Nominal ?? d.Nominal;
            double tp = def?.TolerancePlus ?? d.TolPlus;
            double tm = def?.ToleranceMinus ?? d.TolMinus;
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = d.Name,
                ToolType = "Distance",
                HasNumericSpec = true,
                Spec = nom,
                TolPlus = tp,
                TolMinus = tm,
                Min = nom - tm,
                Max = nom + tp,
                Result = d.Value,
                Unit = defaultUnit,
                Pass = d.Pass
            });
        }

        // 3. LineToLine Distances
        foreach (var l2l in result.LineToLineDistances)
        {
            var def = config.LineToLineDistances?.FirstOrDefault(x => string.Equals(x.Name, l2l.Name, StringComparison.OrdinalIgnoreCase));
            double nom = def?.Nominal ?? l2l.Nominal;
            double tp = def?.TolerancePlus ?? l2l.TolPlus;
            double tm = def?.ToleranceMinus ?? l2l.TolMinus;
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = l2l.Name,
                ToolType = "LineToLine",
                HasNumericSpec = true,
                Spec = nom,
                TolPlus = tp,
                TolMinus = tm,
                Min = nom - tm,
                Max = nom + tp,
                Result = l2l.Value,
                Unit = defaultUnit,
                Pass = l2l.Pass
            });
        }

        // 4. PointToLine Distances
        foreach (var p2l in result.PointToLineDistances)
        {
            var def = config.PointToLineDistances?.FirstOrDefault(x => string.Equals(x.Name, p2l.Name, StringComparison.OrdinalIgnoreCase));
            double nom = def?.Nominal ?? p2l.Nominal;
            double tp = def?.TolerancePlus ?? p2l.TolPlus;
            double tm = def?.ToleranceMinus ?? p2l.TolMinus;
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = p2l.Name,
                ToolType = "PointToLine",
                HasNumericSpec = true,
                Spec = nom,
                TolPlus = tp,
                TolMinus = tm,
                Min = nom - tm,
                Max = nom + tp,
                Result = p2l.Value,
                Unit = defaultUnit,
                Pass = p2l.Pass
            });
        }

        // 5. SegmentLine Distances
        foreach (var sld in result.SegmentLineDistances)
        {
            var def = config.SegmentLineDistances?.FirstOrDefault(x => string.Equals(x.Name, sld.Name, StringComparison.OrdinalIgnoreCase));
            double nom = def?.Nominal ?? sld.Nominal;
            double tp = def?.TolerancePlus ?? sld.TolPlus;
            double tm = def?.ToleranceMinus ?? sld.TolMinus;
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = sld.Name,
                ToolType = "SegmentLine",
                HasNumericSpec = true,
                Spec = nom,
                TolPlus = tp,
                TolMinus = tm,
                Min = nom - tm,
                Max = nom + tp,
                Result = sld.Value,
                Unit = defaultUnit,
                Pass = sld.Pass
            });
        }

        // 6. Angles
        foreach (var a in result.Angles)
        {
            var def = config.Angles?.FirstOrDefault(x => string.Equals(x.Name, a.Name, StringComparison.OrdinalIgnoreCase));
            double nom = def?.Nominal ?? a.Nominal;
            double tp = def?.TolerancePlus ?? a.TolPlus;
            double tm = def?.ToleranceMinus ?? a.TolMinus;
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = a.Name,
                ToolType = "Angle",
                HasNumericSpec = true,
                Spec = nom,
                TolPlus = tp,
                TolMinus = tm,
                Min = nom - tm,
                Max = nom + tp,
                Result = a.ValueDeg,
                Unit = "deg",
                Pass = a.Pass
            });
        }

        // 7. Diameters
        foreach (var dm in result.Diameters)
        {
            var def = config.Diameters?.FirstOrDefault(x => string.Equals(x.Name, dm.Name, StringComparison.OrdinalIgnoreCase));
            double nom = def?.Nominal ?? dm.Nominal;
            double tp = def?.TolerancePlus ?? dm.TolPlus;
            double tm = def?.ToleranceMinus ?? dm.TolMinus;
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = dm.Name,
                ToolType = "Diameter",
                HasNumericSpec = true,
                Spec = nom,
                TolPlus = tp,
                TolMinus = tm,
                Min = nom - tm,
                Max = nom + tp,
                Result = dm.Value,
                Unit = defaultUnit,
                Pass = dm.Pass
            });
        }

        // 8. EdgePairs
        foreach (var ep in result.EdgePairs)
        {
            var def = config.EdgePairs?.FirstOrDefault(x => string.Equals(x.Name, ep.Name, StringComparison.OrdinalIgnoreCase));
            double nom = def?.Nominal ?? ep.Nominal;
            double tp = def?.TolerancePlus ?? ep.TolPlus;
            double tm = def?.ToleranceMinus ?? ep.TolMinus;
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = ep.Name,
                ToolType = "EdgePair",
                HasNumericSpec = true,
                Spec = nom,
                TolPlus = tp,
                TolMinus = tm,
                Min = nom - tm,
                Max = nom + tp,
                Result = ep.Value,
                Unit = defaultUnit,
                Pass = ep.Pass
            });
        }

        // 9. EdgePairDetections
        foreach (var epd in result.EdgePairDetections)
        {
            var def = config.EdgePairDetections?.FirstOrDefault(x => string.Equals(x.Name, epd.Name, StringComparison.OrdinalIgnoreCase));
            double nom = def?.Nominal ?? epd.Nominal;
            double tp = def?.TolerancePlus ?? epd.TolPlus;
            double tm = def?.ToleranceMinus ?? epd.TolMinus;
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = epd.Name,
                ToolType = "EdgePairDetect",
                HasNumericSpec = true,
                Spec = nom,
                TolPlus = tp,
                TolMinus = tm,
                Min = nom - tm,
                Max = nom + tp,
                Result = epd.Value,
                Unit = defaultUnit,
                Pass = epd.Pass
            });
        }

        // 10. CircleFinders
        foreach (var cf in result.CircleFinders)
        {
            double rVal = isCalibrated ? (cf.RadiusPx / config.PixelsPerMm) : cf.RadiusPx;
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = cf.Name,
                ToolType = "CircleFinder",
                HasNumericSpec = false,
                CustomSpecText = "",
                CustomResultText = cf.Found ? $"{rVal:F3} {defaultUnit}" : "NOT_FOUND",
                Spec = 0,
                TolPlus = 0,
                TolMinus = 0,
                Min = 0,
                Max = 0,
                Result = rVal,
                Unit = "",
                Pass = cf.Found
            });
        }

        // 11. ColorDiffs
        if (result.ColorDiffs != null)
        {
            foreach (var cd in result.ColorDiffs)
            {
                list.Add(new OqcMeasurementDetail
                {
                    Index = idx++,
                    ToolName = cd.Name,
                    ToolType = "ColorDiff",
                    HasNumericSpec = false,
                    CustomSpecText = $"<= {cd.MaxDeltaE:F2} dE",
                    CustomResultText = $"{cd.DeltaE:F2} dE",
                    Spec = 0,
                    TolPlus = cd.MaxDeltaE,
                    TolMinus = 0,
                    Min = 0,
                    Max = cd.MaxDeltaE,
                    Result = cd.DeltaE,
                    Unit = "",
                    Pass = cd.Pass
                });
            }
        }

        // 12. SurfaceCompares
        foreach (var sc in result.SurfaceCompares)
        {
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = sc.Name,
                ToolType = "SurfaceCompare",
                HasNumericSpec = false,
                CustomSpecText = "0 lỗi",
                CustomResultText = $"{sc.Defects.Count} lỗi",
                Spec = 0,
                TolPlus = 0,
                TolMinus = 0,
                Min = 0,
                Max = 0,
                Result = sc.Defects.Count,
                Unit = "",
                Pass = sc.Pass
            });
        }

        // 13. ContourCompares
        foreach (var cc in result.ContourCompares)
        {
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = cc.Name,
                ToolType = "ContourCompare",
                HasNumericSpec = false,
                CustomSpecText = ">= 0.80",
                CustomResultText = $"{cc.MatchScore:F3}",
                Spec = 0.8,
                TolPlus = 0,
                TolMinus = 0,
                Min = 0.8,
                Max = 1.0,
                Result = cc.MatchScore,
                Unit = "",
                Pass = cc.Pass
            });
        }

        // 14. BlobDetections
        foreach (var bd in result.BlobDetections)
        {
            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = bd.Name,
                ToolType = "BlobDetection",
                HasNumericSpec = false,
                CustomSpecText = "",
                CustomResultText = $"{bd.Count} blobs",
                Spec = 0,
                TolPlus = 0,
                TolMinus = 0,
                Min = 0,
                Max = 100,
                Result = bd.Count,
                Unit = "",
                Pass = true
            });
        }

        // 15. CodeDetections
        foreach (var cdt in result.CodeDetections)
        {
            var def = config.CodeDetections?.FirstOrDefault(x => string.Equals(x.Name, cdt.Name, StringComparison.OrdinalIgnoreCase));
            string expectedSpec = !string.IsNullOrWhiteSpace(def?.ExpectedText) ? def.ExpectedText : (!string.IsNullOrWhiteSpace(cdt.ExpectedSpec) ? cdt.ExpectedSpec : "");

            list.Add(new OqcMeasurementDetail
            {
                Index = idx++,
                ToolName = cdt.Name,
                ToolType = "CodeDetection",
                HasNumericSpec = false,
                CustomSpecText = string.IsNullOrWhiteSpace(expectedSpec) ? "" : expectedSpec,
                CustomResultText = cdt.Found ? (string.IsNullOrWhiteSpace(cdt.Text) ? "(Trống)" : cdt.Text) : "NO_READ",
                Spec = 0,
                TolPlus = 0,
                TolMinus = 0,
                Min = 0,
                Max = 0,
                Result = cdt.Found ? 1 : 0,
                Unit = "",
                Pass = cdt.Pass
            });
        }

        return list;
    }

    public static string ExtractNgReasons(InspectionResult result)
    {
        if (result == null) return "Chưa có kết quả";
        if (result.Pass) return "Tất cả công cụ kiểm tra đạt yêu cầu (PASS).";

        var reasons = new System.Collections.Generic.List<string>();

        if (result.Origin != null && !result.Origin.Pass)
        {
            reasons.Add($"Origin NG (Score: {result.Origin.Score:F3})");
        }

        foreach (var d in result.Distances)
        {
            if (!d.Pass)
            {
                reasons.Add($"Distance [{d.Name}] NG: {d.Value:F3}mm (Tiêu chuẩn: {d.Nominal:F3}, Dung sai: +{d.TolPlus}/-{d.TolMinus})");
            }
        }

        foreach (var l2l in result.LineToLineDistances)
        {
            if (!l2l.Pass)
            {
                reasons.Add($"LineToLine [{l2l.Name}] NG: {l2l.Value:F3}mm (Tiêu chuẩn: {l2l.Nominal:F3})");
            }
        }

        foreach (var p2l in result.PointToLineDistances)
        {
            if (!p2l.Pass)
            {
                reasons.Add($"PointToLine [{p2l.Name}] NG: {p2l.Value:F3}mm (Tiêu chuẩn: {p2l.Nominal:F3})");
            }
        }

        foreach (var seg in result.SegmentLineDistances)
        {
            if (!seg.Pass)
            {
                reasons.Add($"SegmentLine [{seg.Name}] NG: {seg.Value:F3}mm");
            }
        }

        foreach (var ang in result.Angles)
        {
            if (!ang.Pass)
            {
                reasons.Add($"Angle [{ang.Name}] NG: {ang.ValueDeg:F2}° (Tiêu chuẩn: {ang.Nominal:F2}°)");
            }
        }

        foreach (var ep in result.EdgePairs)
        {
            if (!ep.Pass)
            {
                reasons.Add($"EdgePair [{ep.Name}] NG: {ep.Value:F3}mm");
            }
        }

        foreach (var epd in result.EdgePairDetections)
        {
            if (!epd.Pass)
            {
                reasons.Add($"EdgePairDetect [{epd.Name}] NG: {epd.Value:F3}mm");
            }
        }

        foreach (var dia in result.Diameters)
        {
            if (!dia.Pass)
            {
                reasons.Add($"Diameter [{dia.Name}] NG: {dia.Value:F3}mm");
            }
        }

        foreach (var c in result.Conditions)
        {
            if (!c.Pass)
            {
                reasons.Add($"Condition [{c.Name}] NG ({c.Expression})");
            }
        }

        foreach (var sc in result.SurfaceCompares)
        {
            if (!sc.Pass)
            {
                reasons.Add($"Ngoại quan [{sc.Name}] NG ({sc.Count} vết lỗi)");
            }
        }

        foreach (var cc in result.ContourCompares)
        {
            if (!cc.Pass)
            {
                reasons.Add($"ContourCompare [{cc.Name}] NG (Score: {cc.MatchScore:F3}, MaxDist: {cc.MaxDistancePx:F1}px)");
            }
        }

        foreach (var cd in result.CodeDetections)
        {
            if (!cd.Pass)
            {
                if (!cd.Found)
                {
                    reasons.Add($"Code [{cd.Name}] NG: Không tìm thấy mã (NO_READ)");
                }
                else
                {
                    reasons.Add($"Code [{cd.Name}] NG: '{cd.Text}' (Tiêu chuẩn: '{cd.ExpectedSpec}')");
                }
            }
        }

        if (reasons.Count == 0) return "NG (Không đạt tiêu chí kiểm tra chung)";

        return string.Join("; ", reasons);
    }

    private static string EscapeSqlValue(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return raw.Replace("'", "''");
    }
}
