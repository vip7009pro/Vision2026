# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC & Database).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, nạp bản vẽ kỹ thuật PDF, quản lý CSDL MES/ERP và giao tiếp PLC đa hãng.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`).
- **Kiểm thử tự động**: PASSED 100% toàn bộ hệ thống kiểm thử (6/6 tests Backup & OqcDbMatch mới, 7/7 PDF tests, cùng toàn bộ regression tests).

## 3. Tự Động Khớp CSDL OQC Scanner & Sao Lưu/Nạp Toàn Bộ Cấu Hình (Task 370)
- **Tự động chọn ComboBox CSDL khi nạp cấu hình OQC**:
  - Khắc phục lỗi tất cả ComboBox CSDL rỗng khi nạp file cấu hình từ máy khác do lệch ID (GUID).
  - Bổ sung 8 trường `DbName` tương ứng trong `OqcScannerConfig` (`LookupDbName`, `ProductNameDbName`, v.v.).
  - Triển khai thuật toán phân giải 5 cấp `ResolveDatabaseId`: Khớp ID -> Khớp dbId theo Name -> Khớp dbId theo DatabaseName -> Khớp dbName theo Name/Catalog -> Fallback DB đầu tiên.
  - Bổ sung event `DatabasesChanged` trong `IDbManagerService` để UI tự động tải lại ComboBox nóng.
- **Trung tâm Xuất & Nạp Toàn Bộ Cấu Hình Hệ Thống (`SystemConfigBackupWindow`)**:
  - Menu `🗄️ Dữ Liệu` -> `📦 Cấu Hình...` mở cửa sổ quản trị cấu hình 3 tab (Xuất, Nạp, Nhật ký).
  - Đóng gói toàn bộ: Cài đặt App (`global_settings.json`), PLC (`plc_configs.json`), CSDL (`databases_config.json`), OQC Scanner (`oqc_scanner_config.json`), Camera & Calibration Chessboard vào 1 tệp `.viscfg` duy nhất.
  - Sao chép sang máy tính mới chỉ cần nạp 1 lần duy nhất là chạy được ngay, tự động ánh xạ lại CSDL ID cho OQC Scanner và kích hoạt cập nhật nóng không cần khởi động lại app.

## 4. Các sự kiện & thay đổi gần đây
- Task 370: Tự động khớp ComboBox CSDL OQC & Trung tâm Xuất/Nạp toàn bộ cấu hình hệ thống (1-click migrate).
- Task 369: Cải thiện nút Pan hiển thị rõ nét & sửa lỗi Origin Train Template từ nguồn PDF báo "Chưa lưu template".
- Task 368: Nhập tỉ lệ PixelsPerMm, Căn lề, Pan dịch chuyển & Xoay bản vẽ 90° trên Canvas Camera.
- Task 367: Tự động khớp bản vẽ PDF 1:1 theo Camera & Khung hình cảm biến (Scale quang học theo calib & Canvas 20MP nền trắng).
- Task 366: Khắc phục triệt để lỗi Nền Đen & Ảnh Vỡ khi trích xuất bản vẽ PDF.
- Task 365: Bổ sung chế độ nguồn ảnh từ bản vẽ PDF (`ImageSourceType.Pdf`) cho Tool Editor.
- Task 364: Kế toán thời gian tường minh trong Tool Editor (Timing Breakdown động).
