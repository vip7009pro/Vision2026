# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC & Database).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, và nạp bản vẽ kỹ thuật PDF để dạy học (teach) và kiểm tra tự động.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch Release**: 0 Errors, toàn bộ hệ thống kiểm thử tự động PASSED 100%.

## 3. Khắc phục triệt để lỗi Nền Đen & Ảnh Vỡ khi trích xuất PDF (Task 366)
- **Nguyên nhân nền đen**:
  - Trang PDF có nền mặc định là TRONG SUỐT (Alpha = 0). Khi Docnet/PDFium kết xuất mảng BGRA và dùng OpenCV `Cv2.CvtColor(bgra, bgr, BGRA2BGR)`, OpenCV vứt bỏ kênh Alpha khiến toàn bộ vùng nền (0,0,0,0) biến thành (0,0,0) ĐEN KỊT, nét đen chìm nghỉm vào nền đen.
  - **Khắc phục**: Triển khai thuật toán Unsafe Alpha Blending với Nền Trắng Tinh (`255, 255, 255`) cho mọi pixel trong suốt và bán trong suốt (anti-aliasing) trong `PdfDocumentService.cs`. Toàn bộ nền biến thành màu trắng sáng tinh khiết, nét chữ và chi tiết nổi bật 100%.
- **Nguyên nhân ảnh vỡ khi để 100%**:
  - Đơn vị PDF là Point (1/72 inch). Mức scale `1.0` chỉ tương đương 72 DPI (khổ A4 hay tem nhãn chỉ có vài trăm pixel). Khi zoom lên canvas để vẽ ROI thì bị vỡ hạt pixelated. Trong khi trình đọc PDF vector tự render lại theo màn hình nên trông nét.
  - **Khắc phục**: Đặt mặc định kết xuất là **300 DPI** (scale ≈ 4.167x - Chuẩn công nghiệp vàng). Bổ sung các tùy chọn 150, 200, 300, 400, 600 DPI trong `AvailablePdfScales`. Bản vẽ đạt kích thước Megapixel siêu nét, đọc rõ từng nét chữ nhỏ 1mm và mã vạch.
- **Kiểm thử tự động**:
  - `PdfSourceTests.cs` (4 test): Kiểm tra pixel nền BGR=(255,255,255) trắng tinh; kiểm tra ảnh 300 DPI độ phân giải cao; ViewModel tích hợp; Chạy full flow. Toàn bộ test suite `TestExtractApp` đạt **100% PASSED**.

## 4. Các sự kiện & thay đổi gần đây
- Task 365: Bổ sung chế độ nguồn ảnh từ bản vẽ PDF (`ImageSourceType.Pdf`) cho Tool Editor.
- Task 364: Kế toán thời gian tường minh trong Tool Editor (Timing Breakdown động).
- Task 363: Nút "Đóng Job" và Xóa chọn lọc từng dòng trong Lịch sử quét mã OQC.
- Task 362: Nút Reset phiên & hàng đợi + sửa lỗi tương phản giao diện tối.
- Task 361: Sửa lỗi kế toán thanh Queue 16 nấc và thanh 20 con hàng nhảy 2 nấc khi Run Once.
