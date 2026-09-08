# BỘ TÀI LIỆU KỸ THUẬT ĐÀO TẠO KỸ SƯ VISION
## CMS VINA VISION SYSTEM — NỀN TẢNG THỊ GIÁC MÁY TÍNH CÔNG NGHIỆP

---

## 🎯 MỤC TIÊU KHÓA ĐÀO TẠO

Bộ tài liệu này được biên soạn nhằm đào tạo các **Kỹ sư Vision (Vision Engineer)**, **Kỹ sư Tự động hóa (Automation Engineer)** và **Kỹ sư Đảm bảo Chất lượng (Quality Engineer)** làm chủ 100% phần mềm **CMS VINA VISION SYSTEM**:
1. Hiểu sâu sắc kiến trúc 5 lớp của phần mềm và nguyên lý xử lý ảnh số thời gian thực.
2. Cấu hình chuyên sâu các dòng camera công nghiệp (Hikrobot GigE/USB3 Vision, DirectShow USB), tinh chỉnh thông số quang học và chiếu sáng.
3. Làm chủ kỹ thuật hiệu chuẩn 2 điểm Pixels/mm và khử méo thấu kính bằng bàn cờ chuẩn (Chessboard Calibration).
4. Thiết kế thành thạo các luồng kiểm tra đo lường, định vị Origin 360°, phát hiện khuyết tật và nhận diện AI OCR trên đồ thị Node Graph.
5. Tích hợp truyền thông ngoại vi công nghiệp với PLC Mitsubishi (MC Protocol / MX Component), Cơ sở dữ liệu (MS SQL, MySQL, SQLite), Bộ nguồn điều khiển đèn và hệ thống cập nhật phần mềm từ xa OTA.

---

## 📚 MỤC LỤC CHI TIẾT CÁC CHUYÊN ĐỀ

### [Phần 1: Kiến Trúc Hệ Thống & Không Gian Làm Việc](01_system_architecture_and_ui.md)
- Tổng quan nền tảng công nghệ .NET 8 WPF, OpenCvSharp4, MVVM Toolkit và Dependency Injection.
- Cấu trúc 5 lớp Clean Architecture: Models, VisionEngine, Application, Persistence, UI.
- Phân tích bố cục không gian làm việc: TitleBar, Info Pill, MenuStrip, Global Status Bar.
- Chức năng chi tiết 4 tab cốt lõi: `Tool Editor`, `OQC Scanner`, `Manual Inspection`, `Camera Settings`.
- Hệ thống phím tắt tiêu chuẩn công nghiệp (`Ctrl+N/O/S`, `F1-F6`, `Space`).
- Cơ chế đóng gói và cấu trúc tệp dự án `.job` hoàn chỉnh.

### [Phần 2: Thiết Lập Camera Công Nghiệp & Hiệu Chuẩn Quang Học](02_camera_setup_and_calibration.md)
- Kiến trúc lớp trừu tượng camera đa hãng (`ICameraDriver`, `HikCameraDriver`, `OpenCvCameraDriver`, `Simulator`).
- Quy trình kết nối và cấu hình Camera công nghiệp Hikrobot GigE Vision (IP tĩnh, Jumbo Frames 9014 bytes).
- Tinh chỉnh thông số quang học: Exposure Time, Gain, Gamma, Cân bằng trắng, Chế độ Trigger.
- Hiệu chuẩn tỷ lệ độ phân giải quang học 2 điểm (`PixelsPerMm`).
- Hiệu chuẩn thấu kính bàn cờ (Chessboard Camera Calibration): Thu thập góc cờ Sub-pixel, ma trận camera K, hệ số méo k_1, k_2, p_1, p_2, đánh giá sai số Reprojection Error.
- Cơ chế cưỡng chế áp dụng Global Calibration toàn cục (`IsForceApplyGlobalCalibration`).

### [Phần 3: Lập Trình Đồ Thị Tool & Thiết Kế Thuật Toán Đo Kiểm](03_tool_editor_and_inspection_flow.md)
- Nguyên lý lập trình luồng dữ liệu trực quan trên Node Graph Canvas (Nodes, Ports, Edges Bezier, Pan/Zoom 360°, Fit View).
- Làm chủ công cụ gốc tọa độ Origin: So sánh 3 thuật toán `MvpShapeMatch2` (3-15ms), `TemplateMatch` (NCC) và `FeatureBased` (SIFT + RANSAC Affine 2D).
- Dạy mẫu Origin (Teaching), xoay ROI 360° và cơ chế tự động xoay tịnh tiến các Tool con (`MapToGlobal`).
- Bộ công cụ đo lường kích thước: `Caliper` (Sub-pixel strips), `CircleFinder` (Radial Caliper RANSAC), `Distance`, `Angle`, `LineLineDistance`, `SegmentLineDistance`.
- Cài đặt dung sai tiêu chuẩn và cơ chế tự động tính toán độ lệch **Over Spec** (+Δ, -Δ).
- Bộ công cụ kiểm tra ngoại quan & AI OCR: `BlobDetection`, `SurfaceCompare` (SSIM/Gradient Adaptive), `ContourCompare` (ICP viền nét khuyết/thừa), `OCR Tool` (Dạy chữ MVS & AI ONNX), `CodeDetection` (Đọc mã 360°).

### [Phần 4: Tích Hợp PLC, Cơ Sở Dữ Liệu, Điều Khiển Đèn & Cập Nhật OTA](04_integration_plc_db_lighting.md)
- Khung kết nối PLC công nghiệp: Mitsubishi MC Protocol 3E Binary TCP và MX Component (`ActUtlType`).
- Các Canvas Node PLC: `PlcTrigger`, `PlcRead`, `PlcWrite`, `PlcBatchRead/Write` và bộ công cụ chẩn đoán (`PLC Monitor`, `PLC Oscilloscope`).
- Quản trị Cơ sở dữ liệu (DB Manager): Kết nối 6 loại CSDL (MS SQL, MySQL, SQLite...), `DbNode` đọc/ghi linh hoạt theo token.
- Điều khiển bộ nguồn đèn LED chiếu sáng: Cổng nối tiếp Serial COM RS232 và kiến trúc Lighting Server/Client qua mạng LAN.
- Quản lý Job và Gán mã sản phẩm ↔ Tệp Job (`ProductAssignDialog`).
- Hệ thống phát hành và cập nhật phần mềm từ xa OTA: Nén tối ưu gói cập nhật (35-45MB), tải phân đoạn chunked 6MB qua script PHP, tự động backup, xác thực SHA-256 và khởi động lại an toàn.

---

## 📅 LỘ TRÌNH ĐÀO TẠO ĐỀ XUẤT (4 NGÀY)

| Ngày | Nội dung chính | Bài thực hành kỹ thuật |
| :---: | :--- | :--- |
| **Ngày 1** | - Kiến trúc phần mềm & Giao diện.<br>- Kết nối Camera Hikrobot & Tinh chỉnh quang học. | Cài đặt địa chỉ IP tĩnh GigE, kích hoạt Jumbo Frame và tinh chỉnh Exposure/Gain bắt phôi kim loại chuyển động. |
| **Ngày 2** | - Hiệu chuẩn Pixels/mm.<br>- Hiệu chuẩn bàn cờ Chessboard Calib. | Chụp 5 góc độ tấm bia bàn cờ chuẩn, tính ma trận méo thấu kính, đạt Reprojection Error < 0.2 px. |
| **Ngày 3** | - Lập trình Node Graph Tool Editor.<br>- Dạy mẫu Origin MvpShapeMatch2.<br>- Xây dựng các phép đo Caliper, Circle, Distance & Dung sai OverSpec. | Tạo hoàn chỉnh 1 tệp Job `.job` kiểm tra 1 sản phẩm cơ khí thực tế (định vị góc quay ± 45°, đo 4 đường kính lỗ, kiểm tra khuyết tật bề mặt). |
| **Ngày 4** | - Dạy ký tự OCR & Đọc mã 360°.<br>- Tích hợp PLC Mitsubishi & Ghi log Database.<br>- Đóng gói Job, cấu hình OQC Scanner & Cập nhật OTA. | Kết nối PLC mô phỏng kích hoạt Trigger, cấu hình tự nạp Job qua mã Barcode và xuất gói OTA lên server. |
