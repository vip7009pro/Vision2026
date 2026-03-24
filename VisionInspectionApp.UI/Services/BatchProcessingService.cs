using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VisionInspectionApp.Application;
using VisionInspectionApp.Models;

namespace VisionInspectionApp.UI.Services;

/// <summary>
/// Service để xử lý hàng loạt ảnh từ folder
/// </summary>
public sealed class BatchProcessingService
{
    private readonly IInspectionService _inspectionService;
    private readonly IConfigService _configService;
    private bool _isProcessing;

    public event EventHandler<(string FileName, Mat Image, List<InspectionResult> Results, int Progress)>? ImageProcessed;
    public event EventHandler<string>? ProcessingCompleted;
    public event EventHandler<string>? ErrorOccurred;

    public BatchProcessingService(IInspectionService inspectionService, IConfigService configService)
    {
        _inspectionService = inspectionService;
        _configService = configService;
    }

    /// <summary>
    /// Xử lý hàng loạt ảnh từ folder
    /// </summary>
    public async Task ProcessBatchAsync(
        string folderPath,
        string productCode,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderPath))
        {
            ErrorOccurred?.Invoke(this, $"Folder không tồn tại: {folderPath}");
            return;
        }

        if (_isProcessing)
        {
            ErrorOccurred?.Invoke(this, "Đã có batch processing đang chạy");
            return;
        }

        _isProcessing = true;

        try
        {
            // Lấy danh sách ảnh
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff" };
            var imageFiles = new List<string>();

            foreach (var file in Directory.GetFiles(folderPath))
            {
                if (imageExtensions.Contains(Path.GetExtension(file).ToLower()))
                {
                    imageFiles.Add(file);
                }
            }

            if (imageFiles.Count == 0)
            {
                ErrorOccurred?.Invoke(this, "Không tìm thấy ảnh nào trong folder");
                return;
            }

            // Load config
            var config = _configService.LoadConfig(productCode);
            if (config == null)
            {
                ErrorOccurred?.Invoke(this, $"Không tải được config: {productCode}");
                return;
            }

            // Xử lý từng ảnh
            for (int i = 0; i < imageFiles.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var filePath = imageFiles[i];
                var fileName = Path.GetFileName(filePath);

                try
                {
                    // Load ảnh
                    var image = Cv2.ImRead(filePath);
                    if (image.Empty())
                    {
                        ErrorOccurred?.Invoke(this, $"Không thể load ảnh: {fileName}");
                        continue;
                    }

                    // Chạy inspection
                    var result = await Task.Run(
                        () => _inspectionService.Inspect(image, config),
                        cancellationToken);

                    // Chuyển đổi kết quả thành List<InspectionResult> để hiển thị
                    var resultList = ConvertInspectionResultToList(result);

                    // Fire event
                    int progress = (int)((i + 1) * 100.0 / imageFiles.Count);
                    ImageProcessed?.Invoke(this, (fileName, image.Clone(), resultList, progress));

                    image.Dispose();
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, $"Lỗi xử lý {fileName}: {ex.Message}");
                }

                // Yield để UI không bị block
                await Task.Delay(1);
            }

            ProcessingCompleted?.Invoke(this, $"Hoàn thành xử lý {imageFiles.Count} ảnh");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Lỗi batch processing: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>
    /// Export kết quả batch processing thành CSV
    /// </summary>
    public async Task ExportResultsAsync(
        string outputPath,
        List<(string FileName, List<InspectionResult> Results)> results)
    {
        try
        {
            using var writer = new StreamWriter(outputPath);

            // Header
            await writer.WriteLineAsync("File Name,Pass/Fail,Total Points,Total Lines,Total Distances");

            // Data
            foreach (var (fileName, inspectionResults) in results)
            {
                if (inspectionResults.Count == 0) continue;

                var firstResult = inspectionResults[0];
                var line = $"{fileName},{(firstResult.Pass ? "PASS" : "FAIL")},{firstResult.Points.Count}," +
                           $"{firstResult.Lines.Count},{firstResult.Distances.Count}";
                await writer.WriteLineAsync(line);
            }

            await writer.FlushAsync();
            ProcessingCompleted?.Invoke(this, $"Kết quả exported: {outputPath}");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Lỗi export: {ex.Message}");
        }
    }

    /// <summary>
    /// Chuyển đổi InspectionResult thành List để hiển thị
    /// </summary>
    private static List<InspectionResult> ConvertInspectionResultToList(InspectionResult result)
    {
        return new List<InspectionResult> { result };
    }

    /// <summary>
    /// Kiểm tra batch processing đang chạy hay không
    /// </summary>
    public bool IsProcessing => _isProcessing;
}
