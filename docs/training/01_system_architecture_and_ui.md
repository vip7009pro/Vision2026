# TÀI LIỆU ĐÀO TẠO KỸ SƯ VISION — PHẦN 1
## KIẾN TRÚC HỆ THỐNG VÀ KHÔNG GIAN LÀM VIỆC (SYSTEM ARCHITECTURE & UI)

---

## 1. GIỚI THIỆU TỔNG QUAN HỆ THỐNG

**CMS VINA VISION SYSTEM** là nền tảng phần mềm thị giác máy tính công nghiệp (Industrial Machine Vision Platform) được thiết kế và phát triển chuyên biệt cho các nhà máy gia công cơ khí chính xác, lắp ráp linh kiện điện tử, bán dẫn và kiểm tra chất lượng tự động.

Hệ thống được xây dựng trên nền tảng công nghệ hiện đại:
- **Ngôn ngữ & Khung làm việc:** C# 12 trên nền **.NET 8.0 Windows (x64)**.
- **Giao diện người dùng:** **WPF (Windows Presentation Foundation)** với cơ chế tăng tốc phần cứng DirectX, kết hợp kiến trúc MVVM (`CommunityToolkit.Mvvm`).
- **Lõi xử lý ảnh:** **OpenCvSharp4 v4.10.0** (bản bọc tối ưu C++ OpenCV native) kết hợp với các thuật toán độc quyền: Vector Edge Matching đa kim tự tháp (`MvpShapeMatch2`), nắn khớp ICP đa điểm, và mạng nơ-ron AI ONNX Runtime.
- **Kiến trúc quản trị vòng đời:** Dependency Injection Container chuẩn Microsoft Hosting (`Microsoft.Extensions.Hosting`).

```
                              KIẾN TRÚC 5 LỚP CỦA VISION SYSTEM
 ┌─────────────────────────────────────────────────────────────────────────────┐
 │  1. VisionInspectionApp.UI (Lớp Giao Diện & Điều Khiển)                     │
 │     - Views: ToolEditorView, OqcScannerView, CameraSettingsView, Dialogs    │
 │     - ViewModels: ToolEditorViewModel, OqcScannerViewModel, CameraSettings...│
 │     - Controls: ImageViewerControl, FastOverlayCanvas (DirectX 60 FPS)      │
 ├─────────────────────────────────────────────────────────────────────────────┤
 │  2. VisionInspectionApp.Application (Lớp Dịch Vụ Nghiệp Vụ & Điều Phối)     │
 │     - InspectionService: Điều phối pipeline thực thi tuần tự & song song     │
 │     - CameraService: Điều khiển đa hãng (Hikrobot MVS, DirectShow, RTSP)    │
 │     - PlcManagerService: Mitsubishi MC Protocol 3E Binary TCP & MX Component│
 │     - OtaUpdateService & OtaPublisherService: Cập nhật & phát hành OTA      │
 │     - DatabaseService: Giao tiếp 6 loại CSDL (SQL Server, MySQL, SQLite...) │
 ├─────────────────────────────────────────────────────────────────────────────┤
 │  3. VisionInspectionApp.VisionEngine (Lớp Thuật Toán Xử Lý Ảnh Thuần Túy)   │
 │     - MvpShapeMatch2Engine: Khớp mẫu hướng gradient thưa (3-15ms)           │
 │     - ImagePreprocessor: Lọc sáng phẳng, CLAHE, Binary Otsu, Morphological  │
 │     - Geometry2D & DistanceCalculator: Tính toán hình học phẳng Sub-pixel   │
 │     - OcrDetector: Nhận diện chữ mẫu (Character Training) & AI ONNX Tensor  │
 ├─────────────────────────────────────────────────────────────────────────────┤
 │  4. VisionInspectionApp.Persistence (Lớp Lưu Trữ Dữ Liệu & Đóng Gói)        │
 │     - JobService: Đóng gói tệp nén Zip chuẩn .job (JSON + Crop PNGs + Model)│
 │     - JsonConfigService: Tuần tự hóa System.Text.Json cấu hình VisionConfig │
 ├─────────────────────────────────────────────────────────────────────────────┤
 │  5. VisionInspectionApp.Models (Lớp Dữ Liệu Thực Thể & Giao Khế Dữ Liệu)    │
 │     - VisionConfig, ToolGraph, NodeDefinitions, Roi, Point2d, Measurement   │
 └─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. BỐ CỤC KHÔNG GIAN LÀM VIỆC CHÍNH

Giao diện của ứng dụng được tối ưu hóa cho màn hình cảm ứng công nghiệp (Touch IPC) và máy trạm kỹ thuật, bao gồm 4 khu vực chính:

```
 ┌─────────────────────────────────────────────────────────────────────────────┐
 │ [Logo] VISION SYSTEM | [Menu] |  📁 Job: Cover_A.job  | 🏷️ SP: SKU-901 | 🚀 🌗 ✕│ ◄── TitleBar
 ├─────────────────────────────────────────────────────────────────────────────┤
 │ [🧰 Tool Editor]   [📷 OQC Scanner]   [🔍 Manual Inspection]   [⚙️ Camera Settings]  │ ◄── Main Tabs
 ├─────────────────────────────────────────────────────────────────────────────┤
 │                                                                             │
 │                                                                             │
 │                       KHÔNG GIAN NỘI DUNG CHÍNH                             │
 │                  (Hiển thị màn hình theo Tab đã chọn)                       │
 │                                                                             │
 │                                                                             │
 ├─────────────────────────────────────────────────────────────────────────────┤
 │ Hệ thống sẵn sàng. | Chế độ: Ready               CMS VINA Vision System v2.0│ ◄── Status Bar
 └─────────────────────────────────────────────────────────────────────────────┘
```

### 2.1. Thanh Tiêu Đề Thông Minh (TitleBar)
- **CMS VINA Logo & Nhãn hệ thống:** Định danh phiên bản và thương hiệu.
- **Integrated MenuStrip:** Menu chức năng chuẩn công nghiệp:
  - `📁 Tệp (File)`: Tạo mới, Mở, Lưu, Lưu thành, Mở gần đây (Recent Jobs), Nạp ảnh, Thoát.
  - `👁️ Màn Hình`: Chuyển đổi nhanh 4 màn hình chính (`F1` ➔ `F4`).
  - `🔌 Truyền Thông`: Quản lý PLC (PLC Manager), Roll Defect Map, HMI Manager, PLC Monitor, PLC Tags.
  - `💡 Chiếu Sáng`: Bộ điều khiển đèn cục bộ (COM RS232), Server/Client điều khiển đèn qua LAN.
  - `🗄️ Dữ Liệu`: Quản lý Job Server, Gán mã sản phẩm ↔ Job, Cấu hình DB Manager.
  - `📐 Hiệu Chuẩn`: Hiệu chuẩn tỷ lệ Pixels/mm (2 điểm), Hiệu chuẩn camera bàn cờ (Chessboard Calib).
  - `⚡ Tác Vụ`: Chạy kiểm tra 1 lần (`F5`), Chạy liên tục (`F6`).
  - `❓ Trợ Giúp`: Kiểm tra cập nhật OTA, Trung tâm tài liệu hướng dẫn, Thông tin phiên bản (About).
- **Vùng trạng thái trung tâm (Info Pill):** Hiển thị trực quan trạng thái (`READY` / `RUNNING`), tên tệp Job đang mở (`📁 Job: ... *` có dấu sao nếu chưa lưu) và mã sản phẩm kích hoạt (`🏷️ SP: ...`).
- **Nút huy hiệu OTA Update (`🚀 Có Bản Mới`):** Tự động phát hiện phiên bản mới từ máy chủ web và nhấp nháy màu xanh lá mời kỹ sư nâng cấp.
- **Nút chuyển đổi giao diện Sáng / Tối (`🌗 Theme Toggle`):** Chuyển đổi linh hoạt giữa Dark Theme (giảm mỏi mắt trong xưởng) và Light Theme (độ tương phản cao).

---

## 3. PHÂN TÍCH 4 MÀN HÌNH CHỨC NĂNG CỐT LÕI

### Tab 1: `🧰 Tool Editor` (Trọng tâm của Kỹ sư Vision)
- **Mục đích:** Môi trường lập trình trực quan (Visual Node Graph Editor) để kỹ sư xây dựng toàn bộ thuật toán kiểm tra sản phẩm.
- **Cấu trúc 3 cột:**
  1. *Cột trái (Toolbox):* Danh sách hơn 25 công cụ xử lý ảnh phân chia theo danh mục (Nguồn ảnh, Định vị, Đo lường, Ngoại quan, AI OCR, Tích hợp PLC/DB, Xuất ảnh).
  2. *Vùng trung tâm (Graph Canvas & Result Preview):* Canvas vô cực hỗ trợ Pan 360°, Zoom chuột, Snap gióng hàng, kết nối cổng bằng dây Bezier mượt mà. Phía trên tích hợp màn hình Preview kết quả độ phân giải cao kèm các lớp vẽ Overlay.
  3. *Cột phải (Properties Panel):* Bảng cấu hình thuộc tính của từng Node được chọn, cho phép chỉnh sửa tham số, ngưỡng điểm, dung sai và nút bấm Dạy mẫu (Teach).

### Tab 2: `📷 OQC Scanner` (Màn hình dành cho Sản xuất & Vận hành)
- **Mục đích:** Phục vụ kiểm tra chất lượng xuất xưởng, tự động hóa toàn diện từ khâu nhận diện mã vạch ➔ nạp Job ➔ căn chỉnh ➔ kiểm tra ➔ hiển thị PASS/NG cỡ lớn.
- **Bố cục 10/45/45 tối ưu thị giác:**
  - *10% hàng trên:* Khung Tên Sản Phẩm (Product Name) tự động co giãn font vừa khít.
  - *45% hàng giữa:* Khối đánh giá kết quả cực đại (Big Result PASS/NG) kèm thông tin cảnh báo lỗi.
  - *45% hàng dưới:* Bảng chi tiết toàn bộ phép đo kèm cột độ lệch **Over Spec** (+Δ, -Δ) và mô tả vượt cận trên/dưới.

### Tab 3: `🔍 Manual Inspection` (Đo Đạc Thủ Công)
- **Mục đích:** Cho phép kỹ sư dùng các công cụ đo tương tác trực tiếp bằng chuột trên bức ảnh chụp từ camera hoặc file ảnh:
  - Đo khoảng cách 2 điểm (Point-to-Point Distance).
  - Đo đường thẳng, góc nghiêng giữa 2 đường (Angle).
  - Đo đường tròn 3 điểm, xác định tâm và bán kính (Circle Center & Radius).
  - Lấy mẫu mức xám (Pixel Inspector) tại vị trí trỏ chuột.

### Tab 4: `⚙️ Camera Settings` (Cài Đặt & Căn Chỉnh Quang Học)
- **Mục đích:** Quản lý kết nối phần cứng camera và tinh chỉnh các thông số quang học:
  - Chọn thiết bị: Hikrobot GigE/USB3 MVS, USB DirectShow, RTSP IP Camera, Camera Giả lập.
  - Tinh chỉnh Exposure Time, Gain, Gamma, Cân bằng trắng, Chế độ đen trắng/màu.
  - Cấu hình Trigger phần cứng/phần mềm.
  - Khung hình xem trực tiếp 60 FPS kèm thông số độ phân giải và FPS thời gian thực.

---

## 4. HỆ THỐNG PHÍM TẮT TIÊU CHUẨN (KEYBOARD SHORTCUTS)

Để đạt hiệu suất vận hành cao nhất trong môi trường công nghiệp, kỹ sư cần ghi nhớ hệ thống phím tắt sau:

| Phím tắt | Chức năng thực thi | Phạm vi hoạt động |
| :---: | :--- | :--- |
| **`Ctrl + N`** | Tạo một luồng kiểm tra mới (New Job) | Toàn hệ thống |
| **`Ctrl + O`** | Mở tệp Vision Job (`.job`) có sẵn từ máy tính | Toàn hệ thống |
| **`Ctrl + S`** | Lưu tệp Vision Job hiện tại | Toàn hệ thống |
| **`Ctrl + Z`** | Hoàn tác thao tác vừa thực hiện (Undo) | Tool Editor Canvas / ROI |
| **`Ctrl + Y`** | Làm lại thao tác vừa hủy (Redo) | Tool Editor Canvas / ROI |
| **`F1`** | Chuyển nhanh sang màn hình **`🧰 Tool Editor`** | Toàn hệ thống |
| **`F2`** | Chuyển nhanh sang màn hình **`📷 OQC Scanner`** | Toàn hệ thống |
| **`F3`** | Chuyển nhanh sang màn hình **`🔍 Manual Inspection`** | Toàn hệ thống |
| **`F4`** | Chuyển nhanh sang màn hình **`⚙️ Camera Settings`** | Toàn hệ thống |
| **`F5`** | Chạy kiểm tra 1 lần (**Run Once**) / Bật Live Cam | Tool Editor / OQC Scanner |
| **`F6`** | Chạy kiểm tra liên tục (**Run Continuous**) | Tool Editor |
| **`SPACE`** | Kích hoạt chụp và kiểm tra mẫu (Trigger Scan) | OQC Scanner |
| **`Delete`** | Xóa Node hoặc Cạnh kết nối đang chọn | Tool Editor Canvas |

---

## 5. CƠ CHẾ ĐÓNG GÓI & CẤU TRÚC TỆP DỰ ÁN (`.JOB`)

Trong các hệ thống vision cũ, cấu hình thường bị phân tán: file JSON ở một nơi, các ảnh template cắt mẫu (`origin.png`, `template.png`) ở một thư mục khác, dẫn đến việc copy sang máy IPC khác thường xuyên bị mất ảnh mẫu.

**CMS VINA VISION SYSTEM** giải quyết triệt để vấn đề này bằng định dạng tệp nén **`.job`**:
- Bản chất tệp `.job` là một gói nén Zip chuẩn (Deflate stream).
- **Cấu trúc bên trong một tệp `.job`:**
  ```
  Sample_Product.job (Zip Archive)
   ├── config.json              (Toàn bộ thông số cấu hình, danh sách node, edge, dung sai)
   ├── origin.png               (Ảnh mẫu cắt từ vật thể cho Tool Origin)
   ├── origin_shape.bin         (Mô hình đặc trưng vector gradient tiền xử lý)
   ├── point_template.png       (Ảnh mẫu cho Tool Point nếu có)
   ├── surface_template.png     (Ảnh phôi chuẩn cho Tool SurfaceCompare)
   └── contour_template.png     (Tập hợp viền vector chuẩn cho Tool ContourCompare)
  ```
- **Ưu điểm:** Khi chuyển đổi sản phẩm hoặc sao lưu sang máy khác, kỹ sư chỉ cần copy duy nhất 1 file `.job`. Mọi tài nguyên ảnh mẫu và mô hình hình học đều được bảo toàn 100%.
