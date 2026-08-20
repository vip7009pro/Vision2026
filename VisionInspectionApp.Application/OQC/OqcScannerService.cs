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
    public OqcScannerConfig Config { get; private set; } = new();

    public OqcScannerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Vision2026");
        Directory.CreateDirectory(dir);
        _configFilePath = Path.Combine(dir, "oqc_scanner_config.json");

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

    public async Task<(bool Found, string JobFilePath, string ErrorMessage)> LookupJobAsync(
        string scannedCode, IDbManagerService dbManager)
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

        var (success, table, error) = await dbManager.ExecuteQueryAsync(Config.LookupDbId, query);
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

        // Check file existence
        string resolvedPath = rawPath;

        // 1. Direct existence check
        if (File.Exists(resolvedPath))
        {
            return (true, resolvedPath, string.Empty);
        }

        // 2. Combine with JobRootDirectory if relative path or filename
        if (!string.IsNullOrWhiteSpace(Config.JobRootDirectory))
        {
            string fileNameOnly = Path.GetFileName(rawPath);
            string combinedPath = Path.Combine(Config.JobRootDirectory, fileNameOnly);
            if (File.Exists(combinedPath))
            {
                return (true, combinedPath, string.Empty);
            }

            string combinedRelative = Path.Combine(Config.JobRootDirectory, rawPath.TrimStart('\\', '/'));
            if (File.Exists(combinedRelative))
            {
                return (true, combinedRelative, string.Empty);
            }
        }

        return (false, rawPath, $"Không tìm thấy tệp Job tại đường dẫn: '{rawPath}'" +
            (!string.IsNullOrWhiteSpace(Config.JobRootDirectory) ? $" hoặc trong thư mục gốc '{Config.JobRootDirectory}'." : "."));
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

        var (success, table, error) = await dbManager.ExecuteQueryAsync(Config.ProductNameDbId, query);
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

        var (success, table, error) = await dbManager.ExecuteQueryAsync(Config.ProductListDbId, query);
        return (success, table, error);
    }

    public async Task<(bool Success, string Message)> AssignProductJobAsync(
        string productCode, string jobFilePath, IDbManagerService dbManager)
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return (false, "Mã sản phẩm rỗng.");
        }

        if (string.IsNullOrWhiteSpace(jobFilePath))
        {
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

        string query = Config.AssignQuery
            .Replace("{ProductCode}", safeCode, StringComparison.OrdinalIgnoreCase)
            .Replace("{JobFilePath}", safePath, StringComparison.OrdinalIgnoreCase);

        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Write, allowUpdateDelete: true);
        if (!isSafe)
        {
            return (false, safetyError);
        }

        var (success, rows, error) = await dbManager.ExecuteNonQueryAsync(Config.AssignDbId, query);
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
        string scannedCode, string jobFilePath, InspectionResult result, VisionConfig config, IDbManagerService dbManager)
    {
        if (!Config.LogResultToDb || string.IsNullOrWhiteSpace(Config.LogResultQuery))
        {
            return (true, "Ghi log DB bị tắt.");
        }

        if (dbManager == null)
        {
            return (false, "DB Manager service not available.");
        }

        string safeCode = EscapeSqlValue((scannedCode ?? "").Trim());
        string safeProductName = EscapeSqlValue((config?.ProductName ?? "").Trim());
        string safePath = EscapeSqlValue((jobFilePath ?? "").Trim());
        string passBit = (result != null && result.Pass) ? "1" : "0";
        string inspectResultText = (result != null && result.Pass) ? "PASS" : "NG";
        string ngReasons = result != null ? EscapeSqlValue(ExtractNgReasons(result)) : "";

        string query = Config.LogResultQuery
            .Replace("{ScannedCode}", safeCode, StringComparison.OrdinalIgnoreCase)
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

        var (isSafe, safetyError) = DbNodeRunner.ValidateSqlQuerySafety(query, DbNodeMode.Write, allowUpdateDelete: true);
        if (!isSafe)
        {
            return (false, safetyError);
        }

        var (success, rows, error) = await dbManager.ExecuteNonQueryAsync(Config.LogResultDbId, query);
        if (success)
        {
            return (true, $"✅ Đã lưu kết quả kiểm tra lên DB! (Số dòng: {rows})");
        }
        else
        {
            return (false, $"Lỗi ghi log DB: {error}");
        }
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
            if (!cd.Found)
            {
                reasons.Add($"CodeDetect [{cd.Name}] NG (Không đọc được mã)");
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
