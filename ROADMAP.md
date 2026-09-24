# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

## Các nhiệm vụ gần đây & Đang triển khai

- [x] Task 359: Triệt tiêu lag tăng dần & rò rỉ bộ nhớ khi chạy Run Continuous (Zero Memory Leak cho frame envelope channel, dọn dẹp cache template & shift register).
- [x] Task 360: Khắc phục lỗi mất kết quả trong "Lịch sử kiểm tra & SPC/CPK" cho mọi luồng (Folder, File, URL, PLC Trigger, Run Once) qua drain channel và ensure session.
- [x] Task 361: Khắc phục lỗi kế toán thanh Queue 16 nấc và thanh 20 con hàng nhảy 2 nấc mỗi lần bấm Run Once.
- [x] Task 362: Tool Editor: Nút "Reset Phiên & Hàng Đợi" (xả sạch queue frame + mở session SPC mới) + Sửa lỗi tương phản giao diện tối & Tái cấu trúc khu vực kết quả.
- [x] Task 363: OQC Scanner: Bổ sung nút "Đóng Job" xóa dòng `Đã nạp Job` và Xóa chọn lọc từng dòng trong bảng Lịch Sử Quét Mã OQC.
- [x] Task 364: Kế toán thời gian tường minh trong Tool Editor: Bóc tách toàn bộ thời gian ẩn & Dải phân bổ thời gian động (Timing Breakdown).
- [x] Task 365: Tool Editor: Node ImageSource bổ sung chế độ nguồn từ bản vẽ PDF (`ImageSourceType.Pdf`), trích xuất trang bản vẽ làm đầu vào teach và job flow.
- [x] Task 366: Khắc phục triệt để lỗi Nền Đen & Ảnh Vỡ khi trích xuất PDF trong Tool Editor:
  - [x] Triệt tiêu Nền Đen: PDF có nền trong suốt (Alpha = 0). Khi ép kiểu BGRA2BGR, OpenCV bỏ kênh A làm nền (0,0,0,0) hóa đen ngòm. Đã thay thế bằng thuật toán Unsafe Alpha Blending với nền trắng tinh khiết (255, 255, 255) cho toàn bộ pixel trong suốt và bán trong suốt (anti-aliasing).
  - [x] Triệt tiêu Ảnh Vỡ: Đơn vị PDF là Point (1/72 inch). Mức scale 1.0 (72 DPI) quá nhỏ cho bản vẽ chi tiết khiến chữ và nét bị vỡ hạt khi zoom canvas. Đã nâng mặc định lên **300 DPI (Chuẩn nét công nghiệp - Khuyên dùng)** (scale ≈ 4.167x) cùng các tùy chọn 150, 200, 300, 400, 600 DPI. Bản vẽ đạt kích thước Megapixel siêu nét, đọc rõ từng chữ nhỏ li ti 1mm.
  - [x] Cập nhật bộ test suite `PdfSourceTests.cs` kiểm tra màu nền trắng chuẩn (255,255,255) và độ phân giải 300 DPI. Toàn bộ test suite chạy đạt 100% PASSED.

## Định hướng tiếp theo
- [ ] Mở rộng hỗ trợ vẽ ROI vùng quan tâm nhiều trang trực tiếp trên bản vẽ PDF đa trang.
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
