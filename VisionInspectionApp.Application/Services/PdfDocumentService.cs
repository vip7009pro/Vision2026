using System;
using System.IO;
using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;
using OpenCvSharp;

namespace VisionInspectionApp.Application.Services;

/// <summary>
/// Dịch vụ chuyển đổi bản vẽ PDF kỹ thuật ra hình ảnh tỉ lệ 100% phục vụ giảng dạy mẫu (teaching) và kiểm tra tự động.
/// </summary>
public sealed class PdfDocumentService : IPdfDocumentService
{
    private static readonly object _syncLock = new();

    /// <summary>
    /// Lấy tổng số trang của tệp PDF.
    /// </summary>
    public int GetPageCount(string pdfFilePath)
    {
        if (string.IsNullOrWhiteSpace(pdfFilePath) || !File.Exists(pdfFilePath))
            return 0;

        lock (_syncLock)
        {
            try
            {
                using var library = DocLib.Instance;
                using var docReader = library.GetDocReader(pdfFilePath, new PageDimensions(1.0));
                return docReader.GetPageCount();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PdfDocumentService] Lỗi khi đọc số trang PDF '{pdfFilePath}': {ex.Message}");
                return 0;
            }
        }
    }

    /// <summary>
    /// Lấy kích thước trang PDF theo pixel ở tỉ lệ chỉ định (1.0 = 100% tỉ lệ gốc của PDF).
    /// </summary>
    public (int Width, int Height) GetPageDimensions(string pdfFilePath, int pageNumber = 1, double scale = 1.0)
    {
        if (string.IsNullOrWhiteSpace(pdfFilePath) || !File.Exists(pdfFilePath))
            return (0, 0);

        if (scale <= 0.0) scale = 1.0;

        lock (_syncLock)
        {
            try
            {
                using var library = DocLib.Instance;
                using var docReader = library.GetDocReader(pdfFilePath, new PageDimensions(scale));
                int totalPages = docReader.GetPageCount();
                if (totalPages <= 0) return (0, 0);

                int pageIndex = Math.Clamp(pageNumber - 1, 0, totalPages - 1);
                using var pageReader = docReader.GetPageReader(pageIndex);
                return (pageReader.GetPageWidth(), pageReader.GetPageHeight());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PdfDocumentService] Lỗi khi đọc kích thước trang PDF '{pdfFilePath}': {ex.Message}");
                return (0, 0);
            }
        }
    }

    /// <summary>
    /// Chuyển đổi một trang PDF sang đối tượng hình ảnh OpenCvSharp Mat (BGR) ở tỉ lệ 100% chuẩn bản vẽ.
    /// </summary>
    public Mat RenderPageToMat(string pdfFilePath, int pageNumber = 1, double scale = 1.0)
    {
        if (string.IsNullOrWhiteSpace(pdfFilePath) || !File.Exists(pdfFilePath))
            throw new FileNotFoundException($"Không tìm thấy tệp bản vẽ PDF: {pdfFilePath}");

        if (scale <= 0.0) scale = 1.0;

        lock (_syncLock)
        {
            using var library = DocLib.Instance;
            using var docReader = library.GetDocReader(pdfFilePath, new PageDimensions(scale));
            int totalPages = docReader.GetPageCount();
            if (totalPages <= 0)
                throw new InvalidOperationException($"Tệp PDF không có trang nào: {pdfFilePath}");

            int pageIndex = Math.Clamp(pageNumber - 1, 0, totalPages - 1);
            using var pageReader = docReader.GetPageReader(pageIndex);

            int width = pageReader.GetPageWidth();
            int height = pageReader.GetPageHeight();
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"Kích thước trang PDF không hợp lệ: {width}x{height}");

            byte[] rawBytes = pageReader.GetImage();
            if (rawBytes == null || rawBytes.Length < width * height * 4)
                throw new InvalidOperationException("Không thể trích xuất dữ liệu pixel từ trang PDF.");

            var bgrMat = new Mat(height, width, MatType.CV_8UC3);
            unsafe
            {
                byte* pDst = (byte*)bgrMat.Data.ToPointer();
                fixed (byte* pSrc = rawBytes)
                {
                    int totalPixels = width * height;
                    byte* src = pSrc;
                    byte* dst = pDst;

                    for (int i = 0; i < totalPixels; i++)
                    {
                        byte b = src[0];
                        byte g = src[1];
                        byte r = src[2];
                        byte a = src[3];

                        if (a == 255)
                        {
                            // Điểm ảnh hoàn toàn đặc
                            dst[0] = b;
                            dst[1] = g;
                            dst[2] = r;
                        }
                        else if (a == 0)
                        {
                            // Vùng nền trong suốt của bản vẽ PDF -> Chuyển thành NỀN TRẮNG TINH (255, 255, 255)
                            dst[0] = 255;
                            dst[1] = 255;
                            dst[2] = 255;
                        }
                        else
                        {
                            // Nét viền bán trong suốt (Anti-Aliasing) -> Alpha Blend mượt mà với nền trắng (255, 255, 255)
                            int invA = 255 - a;
                            dst[0] = (byte)((b * a + 255 * invA + 127) / 255);
                            dst[1] = (byte)((g * a + 255 * invA + 127) / 255);
                            dst[2] = (byte)((r * a + 255 * invA + 127) / 255);
                        }

                        src += 4;
                        dst += 3;
                    }
                }
            }
            return bgrMat;
        }
    }

    /// <summary>
    /// Chuyển đổi trang PDF ra tệp ảnh PNG độ nét cao (lossless) và lưu trữ trên đĩa để sử dụng cho các bước tiếp theo.
    /// </summary>
    public string ConvertPdfToImageFile(string pdfFilePath, int pageNumber = 1, double scale = 1.0, string? outputDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(pdfFilePath) || !File.Exists(pdfFilePath))
            throw new FileNotFoundException($"Không tìm thấy tệp bản vẽ PDF: {pdfFilePath}");

        if (scale <= 0.0) scale = 1.0;

        string targetDir = outputDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "PdfImages");
        Directory.CreateDirectory(targetDir);

        string baseName = Path.GetFileNameWithoutExtension(pdfFilePath);
        int scalePercent = (int)Math.Round(scale * 100);
        string outputFileName = $"{baseName}_p{pageNumber}_{scalePercent}pct.png";
        string outputPath = Path.Combine(targetDir, outputFileName);

        using var mat = RenderPageToMat(pdfFilePath, pageNumber, scale);
        Cv2.ImWrite(outputPath, mat);

        return outputPath;
    }
}
