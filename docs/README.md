# HỆ THỐNG TÀI LIỆU HƯỚNG DẪN & ĐÀO TẠO
## CMS VINA VISION SYSTEM (INDUSTRIAL MACHINE VISION PLATFORM)

Chào mừng bạn đến với Cổng tài liệu chính thức của nền tảng **CMS VINA VISION SYSTEM**. Hệ thống tài liệu được thiết kế chuyên biệt cho hai nhóm người dùng chính: **Công nhân kiểm tra chất lượng (OQC)** và **Kỹ sư Vision / Tự động hóa**.

---

## 📑 DANH MỤC TÀI LIỆU

### 1. Dành Cho Công Nhân OQC (Sản Xuất & Kiểm Tra)
* **[SOP-OQC-VIS-01: Quy Trình Thao Tác Chuẩn Kiểm Tra Lấy Mẫu Sản Phẩm](sop/SOP_OQC_SAMPLING_INSPECTION.md)**
  - Hướng dẫn quy trình thao tác 6 bước chuẩn kiểm tra lấy mẫu (Sampling Inspection) tại bàn OQC.
  - Phân biệt trực quan kết quả **ĐẠT (PASS - Nền Xanh)** và **LỖI (NG - Nền Đỏ)**.
  - Cách đọc thông số sai lệch **Over Spec** (+Δ, -Δ) và mô tả vượt cận trên/dưới.
  - Bảng xử lý sự cố nhanh (Troubleshooting) và quy trình cách ly mẫu lỗi, báo động chất lượng.

---

### 2. Dành Cho Kỹ Sư Vision & Tự Động Hóa (Kỹ Thuật & Cài Đặt)
* **[Mục Lục Tổng Quan & Lộ Trình Đào Tạo Kỹ Sư 4 Ngày](training/README.md)**
* **[Phần 1: Kiến Trúc Hệ Thống & Không Gian Làm Việc (System Architecture & UI)](training/01_system_architecture_and_ui.md)**
  - Kiến trúc 5 lớp .NET 8 WPF, OpenCvSharp4, Dependency Injection.
  - Bố cục 4 tab chính: `Tool Editor`, `OQC Scanner`, `Manual Inspection`, `Camera Settings`.
  - Hệ thống phím tắt và cơ chế đóng gói tệp dự án `.job`.
* **[Phần 2: Thiết Lập Camera Công Nghiệp & Hiệu Chuẩn Quang Học (Camera & Calibration)](training/02_camera_setup_and_calibration.md)**
  - Cấu hình Camera Hikrobot GigE/USB3 Vision qua SDK MVS, tối ưu Jumbo Frames 9014 bytes.
  - Tinh chỉnh Exposure Time, Gain, Gamma, Cân bằng trắng và chế độ Trigger.
  - Hiệu chuẩn 2 điểm Pixels/mm và Hiệu chuẩn camera bàn cờ (Chessboard Lens Calibration).
* **[Phần 3: Lập Trình Đồ Thị Tool & Thiết Kế Thuật Toán Đo Kiểm (Tool Graph & Algorithms)](training/03_tool_editor_and_inspection_flow.md)**
  - Lập trình đồ thị trực quan trên Node Graph Canvas, kết nối dây Bezier, Pan/Zoom 360°.
  - Làm chủ Tool Origin: Thuật toán siêu tốc `MvpShapeMatch2` (3-15ms) và xoay ROI 360°.
  - Bộ công cụ đo kích thước: Caliper, CircleFinder RANSAC, Distance, Angle, OverSpec.
  - Bộ công cụ ngoại quan & AI OCR: BlobDetection, SurfaceCompare SSIM, ContourCompare ICP, Dạy chữ mẫu Hikrobot MVS, Đọc mã 360°.
* **[Phần 4: Tích Hợp PLC, Cơ Sở Dữ Liệu, Điều Khiển Đèn & Cập Nhật OTA](training/04_integration_plc_db_lighting.md)**
  - Kết nối PLC Mitsubishi qua MC Protocol 3E Binary TCP và MX Component.
  - Quản trị Cơ sở dữ liệu (DB Manager) kết nối MS SQL, MySQL, SQLite, PostgreSQL.
  - Điều khiển bộ nguồn đèn LED qua cổng nối tiếp COM RS232 và LAN Socket TCP.
  - Hệ thống đóng gói và phát hành bản cập nhật phần mềm từ xa OTA Publisher/Updater.

---

## 🖼️ HỆ THỐNG HÌNH ẢNH MINH HỌA TRỰC QUAN

Tất cả hình ảnh minh họa độ phân giải cao được lưu trữ tại thư mục `docs/images/`:
- `oqc_station_setup.jpg`: Bố trí chuẩn của trạm kiểm tra mẫu OQC thực tế.
- `oqc_pass_vs_ng_screen.jpg`: So sánh màn hình kết quả Đạt (PASS) và Lỗi (NG) trên OQC Scanner.
- `vision_tool_graph_flow.jpg`: Giao diện lập trình Node Graph trên Tool Editor.
- `optical_calibration_target.jpg`: Thiết lập bia bàn cờ hiệu chuẩn thấu kính camera.

---
*CMS VINA Machine Vision System © 2026. All rights reserved.*
