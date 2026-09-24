using System;
using OpenCvSharp;

namespace VisionInspectionApp.Application.Services;

/// <summary>
/// Dịch vụ kết xuất và chuyển đổi tài liệu bản vẽ kỹ thuật PDF thành hình ảnh tỉ lệ 100% phục vụ dạy học (teaching) và kiểm tra thị giác.
/// </summary>
public interface IPdfDocumentService
{
    /// <summary>
    /// Lấy tổng số trang của tệp PDF.
    /// </summary>
    /// <param name="pdfFilePath">Đường dẫn tệp PDF trên đĩa.</param>
    /// <returns>Số trang của tệp PDF.</returns>
    int GetPageCount(string pdfFilePath);

    /// <summary>
    /// Lấy kích thước pixel của trang PDF theo tỉ lệ chỉ định (1.0 = 100% tỉ lệ gốc của PDF).
    /// </summary>
    /// <param name="pdfFilePath">Đường dẫn tệp PDF.</param>
    /// <param name="pageNumber">Số trang (1-based, mặc định = 1).</param>
    /// <param name="scale">Tỉ lệ chuyển đổi (mặc định = 1.0 tương ứng 100% kích thước bản vẽ).</param>
    /// <returns>Bộ giá trị Width, Height tính theo pixel.</returns>
    (int Width, int Height) GetPageDimensions(string pdfFilePath, int pageNumber = 1, double scale = 1.0);

    /// <summary>
    /// Chuyển đổi một trang PDF sang đối tượng hình ảnh OpenCvSharp Mat (BGR) ở tỉ lệ 100% bảo toàn nguyên vẹn bản vẽ.
    /// </summary>
    /// <param name="pdfFilePath">Đường dẫn tệp PDF.</param>
    /// <param name="pageNumber">Số trang cần trích xuất (1-based, mặc định = 1).</param>
    /// <param name="scale">Tỉ lệ chuyển đổi (mặc định = 1.0 tương ứng 100%).</param>
    /// <returns>Mat hình ảnh dạng BGR 3 kênh.</returns>
    Mat RenderPageToMat(string pdfFilePath, int pageNumber = 1, double scale = 1.0);

    /// <summary>
    /// Chuyển đổi trang PDF ra tệp ảnh PNG độ nét cao (lossless) và lưu trữ trên đĩa để sử dụng cho các bước tiếp theo.
    /// </summary>
    /// <param name="pdfFilePath">Đường dẫn tệp PDF.</param>
    /// <param name="pageNumber">Số trang (1-based, mặc định = 1).</param>
    /// <param name="scale">Tỉ lệ chuyển đổi (mặc định = 1.0).</param>
    /// <param name="outputDirectory">Thư mục đích lưu ảnh (nếu null sẽ dùng Cache/PdfImages/).</param>
    /// <returns>Đường dẫn tệp ảnh PNG đã lưu trên đĩa.</returns>
    string ConvertPdfToImageFile(string pdfFilePath, int pageNumber = 1, double scale = 1.0, string? outputDirectory = null);
}
