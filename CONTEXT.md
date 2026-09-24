# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC & Database).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, và nạp bản vẽ kỹ thuật PDF để dạy học (teach) và kiểm tra tự động khớp 100% với sản phẩm thật.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch Release**: 0 Errors, toàn bộ hệ thống kiểm thử tự động PASSED 100% (7/7 PDF tests).

## 3. Bản Vẽ PDF: Khắc Phục Nút Pan & Lỗi Train Template Origin (Task 369)
- **Giao diện Nút Pan Rõ Ràng**:
  - Khắc phục lỗi 4 nút Pan không có biểu tượng/text do ký tự emoji bị thiếu glyph font trên Windows.
  - Thay thế bằng các ký tự tam giác hình học chuẩn: `◀` (Trái), `▶` (Phải), `▲` (Lên), `▼` (Xuống) và nút `⟲ 0` (Reset về 0,0).
  - Tăng kích thước nút lên $25 \times 22\text{px}$, `Padding="0"`, `FontWeight="Bold"`, font size 12 và ràng buộc `Foreground` tương phản cao.
- **Khắc phục Triệt Để Lỗi Train Template Origin Báo "Chưa Lưu Template"**:
  - Nguyên nhân: Khi chưa lưu Job ra đĩa, `CurrentTempWorkingDir` là null. Template được lưu ở thư mục khác nhưng `ResolveTemplatePath` chỉ tìm trong thư mục tạm, dẫn đến không tìm thấy file và preview trả về null.
  - Giải pháp:
    1. Bổ sung `EnsureCurrentTempWorkingDir()` đảm bảo thư mục tạm luôn tồn tại ngay khi cần.
    2. Cập nhật `ResolveTemplatePath` hỗ trợ kiểm tra file tồn tại trực tiếp, tìm trong `templates/` của temp dir và fallback sang `ConfigRootDirectory`.
    3. `OriginTrainViewModel` lưu đường dẫn tệp đầy đủ cho `_originDef.TemplateImageFile`.
    4. Thêm `InvalidateOriginTemplatePreviewCache()` xóa cache trước khi refresh preview.
- **Kiểm thử tự động**:
  - `PdfSourceTests.cs`: Bổ sung `Test 7: PDF ImageSource Origin Train Template & Preview` xác nhận 100% không còn lỗi "Chưa lưu template".

## 4. Các sự kiện & thay đổi gần đây
- Task 369: Cải thiện nút Pan hiển thị rõ nét & sửa lỗi Origin Train Template từ nguồn PDF báo "Chưa lưu template".
- Task 368: Nhập tỉ lệ PixelsPerMm (nhập tay & đồng bộ 2 chiều Calib), Pan dịch chuyển bản vẽ và Xoay 90°.
- Task 367: Tự động khớp bản vẽ PDF 1:1 theo Camera (20MP & tùy biến) & Khung hình cảm biến.
- Task 366: Khắc phục triệt để lỗi Nền Đen & Ảnh Vỡ khi trích xuất bản vẽ PDF.
- Task 365: Bổ sung chế độ nguồn ảnh từ bản vẽ PDF (`ImageSourceType.Pdf`) cho Tool Editor.
- Task 364: Kế toán thời gian tường minh trong Tool Editor (Timing Breakdown động).
