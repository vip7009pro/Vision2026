# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

## Các nhiệm vụ gần đây & Đang triển khai

- [x] Task 362: Tool Editor: Nút "Reset Phiên & Hàng Đợi" (xả sạch queue frame + mở session SPC mới) + Sửa lỗi tương phản giao diện tối.
- [x] Task 363: OQC Scanner: Bổ sung nút "Đóng Job" xóa dòng `Đã nạp Job` và Xóa chọn lọc từng dòng trong bảng Lịch Sử Quét Mã OQC.
- [x] Task 364: Kế toán thời gian tường minh trong Tool Editor: Bóc tách toàn bộ thời gian ẩn & Dải phân bổ thời gian động (Timing Breakdown).
- [x] Task 365: Tool Editor: Node ImageSource bổ sung chế độ nguồn từ bản vẽ PDF (`ImageSourceType.Pdf`), trích xuất trang bản vẽ làm đầu vào teach.
- [x] Task 366: Khắc phục triệt để lỗi Nền Đen (Unsafe Alpha Blending) & Ảnh Vỡ (300 DPI chuẩn công nghiệp) khi trích xuất bản vẽ PDF.
- [x] Task 367: Tự động khớp bản vẽ PDF 1:1 theo Camera & Khung hình cảm biến (Scale quang học theo calib & Canvas 20MP nền trắng).
- [x] Task 368: Nhập tỉ lệ PixelsPerMm, Căn lề, Pan dịch chuyển & Xoay bản vẽ 90° trên Canvas Camera:
  - [x] Nhập tỉ lệ `PixelsPerMm` linh hoạt: Nhập tay tự do, tự động fallback Calib, 2 nút đồng bộ `⚡ Từ Calib` và `💾 Cho Job`, hiển thị DPI tức thì.
  - [x] Căn lề đa hướng: `Top-Center`, `Center`, `Bottom-Center`, `Top-Left`, `Top-Right`, `Custom` đưa vùng quan tâm vào khung hình.
  - [x] Xoay 90° luân phiên ($0^\circ, 90^\circ, 180^\circ, 270^\circ$) giải quyết triệt để vấn đề bản vẽ đứng (portrait) trên camera ngang (landscape).
  - [x] Cụm điều hướng Pan: Nhập tọa độ offset $X, Y$ và nút bấm `⬅ ➡ ⬆ ⬇` dịch chuyển theo bước (200px) kèm nút `🎯` Reset gốc.
  - [x] Kiểm thử tự động `TestExtractApp`: Thêm Test 6 kiểm tra Pan, Xoay 90°, PixelsPerMm, 100% test suite PASSED.

## Định hướng tiếp theo
- [ ] Bổ sung thao tác kéo chuột trực tiếp trên Canvas xem trước để Pan vùng hiển thị PDF.
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
