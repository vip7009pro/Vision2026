# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

## Các nhiệm vụ gần đây & Đang triển khai

- [x] Task 364: Kế toán thời gian tường minh trong Tool Editor: Bóc tách toàn bộ thời gian ẩn & Dải phân bổ thời gian động (Timing Breakdown).
- [x] Task 365: Tool Editor: Node ImageSource bổ sung chế độ nguồn từ bản vẽ PDF (`ImageSourceType.Pdf`), trích xuất trang bản vẽ làm đầu vào teach.
- [x] Task 366: Khắc phục triệt để lỗi Nền Đen (Unsafe Alpha Blending) & Ảnh Vỡ (300 DPI chuẩn công nghiệp) khi trích xuất bản vẽ PDF.
- [x] Task 367: Tự động khớp bản vẽ PDF 1:1 theo Camera & Khung hình cảm biến (Scale quang học theo calib & Canvas 20MP nền trắng).
- [x] Task 368: Nhập tỉ lệ PixelsPerMm, Căn lề, Pan dịch chuyển & Xoay bản vẽ 90° trên Canvas Camera.
- [x] Task 369: Hoàn thiện UI Pan và sửa lỗi Origin Train Template từ nguồn PDF báo "Chưa lưu template":
  - [x] Nút Pan: Thay emoji bằng ký tự tam giác hình học chuẩn `◀`, `▶`, `▲`, `▼`, `⟲ 0`, tăng kích thước $25 \times 22\text{px}$, hiển thị rõ nét trên mọi máy.
  - [x] Origin Train Template: Tự động đảm bảo `EnsureCurrentTempWorkingDir`, lưu đường dẫn đầy đủ `templateFile`, nâng cấp `ResolveTemplatePath` fallback thông minh, và xóa cache preview.
  - [x] Kiểm thử tự động `TestExtractApp`: Bổ sung Test 7 kiểm tra train template từ PDF & hiển thị preview thành công 100%.

## Định hướng tiếp theo
- [ ] Bổ sung thao tác kéo chuột trực tiếp trên Canvas xem trước để Pan vùng hiển thị PDF.
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
