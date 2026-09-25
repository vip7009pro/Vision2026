# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

## Các nhiệm vụ gần đây & Đang triển khai

- [x] Task 364: Kế toán thời gian tường minh trong Tool Editor: Bóc tách toàn bộ thời gian ẩn & Dải phân bổ thời gian động (Timing Breakdown).
- [x] Task 365: Tool Editor: Node ImageSource bổ sung chế độ nguồn từ bản vẽ PDF (`ImageSourceType.Pdf`), trích xuất trang bản vẽ làm đầu vào teach.
- [x] Task 366: Khắc phục triệt để lỗi Nền Đen & Ảnh Vỡ (300 DPI chuẩn công nghiệp) khi trích xuất bản vẽ PDF.
- [x] Task 367: Tự động khớp bản vẽ PDF 1:1 theo Camera & Khung hình cảm biến (Scale quang học theo calib & Canvas 20MP nền trắng).
- [x] Task 368: Nhập tỉ lệ PixelsPerMm, Căn lề, Pan dịch chuyển & Xoay bản vẽ 90° trên Canvas Camera.
- [x] Task 369: Hoàn thiện UI Pan và sửa lỗi Origin Train Template từ nguồn PDF báo "Chưa lưu template".
- [x] Task 370: Tự động khớp CSDL OQC Scanner & Trung tâm Xuất/Nạp toàn bộ cấu hình hệ thống:
  - [x] Sửa lỗi ComboBox CSDL OQC rỗng khi import: Bổ sung các trường `DbName`, thuật toán phân giải 5 cấp `ResolveDatabaseId`, tự động nhận diện khớp ID, Name hoặc Catalog.
  - [x] Bổ sung Menu Item `📦 Cấu Hình...` trong menu `🗄️ Dữ Liệu`: Mở cửa sổ `SystemConfigBackupWindow` hiện đại 3 tab.
  - [x] Xuất/Nạp toàn bộ cấu hình (App, PLC, Database, OQC, Camera, Calib) ra 1 tệp `.viscfg` duy nhất, copy sang máy khác chỉ cần import 1 lần là chạy được luôn.
  - [x] Tự động ánh xạ lại CSDL ID cho OQC config khi nạp trên máy khác và kích hoạt nạp nóng tức thì.
  - [x] Kiểm thử tự động `TestExtractApp`: Hoàn thành 6/6 tests cho tính năng Backup & OqcDbMatch.

## Định hướng tiếp theo
- [ ] Bổ sung thao tác kéo chuột trực tiếp trên Canvas xem trước để Pan vùng hiển thị PDF.
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
