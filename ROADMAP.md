# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

## Các nhiệm vụ gần đây & Đang triển khai

- [x] Task 365: Tool Editor: Node ImageSource bổ sung chế độ nguồn từ bản vẽ PDF (`ImageSourceType.Pdf`), trích xuất trang bản vẽ làm đầu vào teach.
- [x] Task 366: Khắc phục triệt để lỗi Nền Đen & Ảnh Vỡ (300 DPI chuẩn công nghiệp) khi trích xuất bản vẽ PDF.
- [x] Task 367: Tự động khớp bản vẽ PDF 1:1 theo Camera & Khung hình cảm biến (Scale quang học theo calib & Canvas 20MP nền trắng).
- [x] Task 368: Nhập tỉ lệ PixelsPerMm, Căn lề, Pan dịch chuyển & Xoay bản vẽ 90° trên Canvas Camera.
- [x] Task 369: Hoàn thiện UI Pan và sửa lỗi Origin Train Template từ nguồn PDF báo "Chưa lưu template".
- [x] Task 370: Tự động khớp CSDL OQC Scanner & Trung tâm Xuất/Nạp toàn bộ cấu hình hệ thống:
  - [x] Sửa lỗi ComboBox CSDL OQC rỗng khi import với thuật toán 5 cấp `ResolveDatabaseId`.
  - [x] Bổ sung Menu Item `📦 Cấu Hình...` mở cửa sổ `SystemConfigBackupWindow` 3 tab.
  - [x] Xuất/Nạp toàn bộ cấu hình ra tệp `.viscfg` duy nhất, 1-click migration.
  - [x] Tự động ánh xạ lại CSDL ID cho OQC config và nạp nóng tức thì.
- [x] Task 371: Hiệu chuẩn tỉ lệ Calib trực tiếp từ Tool đo ("Set as Calib Factor") cho từng Job:
  - [x] Thêm nút `🎯 Đặt làm Hệ Số Calib` vào properties của các công cụ đo: `Distance`, `SegmentLineDistance`, `LineLineDistance`, `PointLineDistance`, `EdgePairDetect`, `EdgePair`, `Diameter`, và `CircleFinder`.
  - [x] Bổ sung `NominalDiameter` trong `CircleFinderDefinition` và ô nhập `Nom Dia (mm)` cho Circle Finder.
  - [x] Tự động trích xuất khoảng cách pixel thực tế (`measuredPx`) và kích thước danh định (`Nominal mm`).
  - [x] Hộp thoại xác nhận hiển thị chi tiết thông số cũ -> mới, % độ lệch và cảnh báo vàng nếu chênh lệch > 10%.
  - [x] Tự động ghi đè vào `VisionConfig.PixelsPerMm`, kích hoạt AutoSave và tự động gọi `RunFlow()` để tính toán lại toàn bộ kết quả đo của Job theo tỉ lệ mm mới.
  - [x] Hoàn thành bộ kiểm thử tự động `ToolCalibFactorTests` (6/6 tests PASSED 100%).

## Định hướng tiếp theo
- [ ] Bổ sung thao tác kéo chuột trực tiếp trên Canvas xem trước để Pan vùng hiển thị PDF.
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
