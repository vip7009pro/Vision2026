# TÀI LIỆU ĐÀO TẠO KỸ SƯ VISION — PHẦN 4
## TÍCH HỢP PLC, CƠ SỞ DỮ LIỆU, ĐIỀU KHIỂN ĐÈN VÀ CẬP NHẬT OTA

---

## 1. KHUNG KẾT NỐI PLC CÔNG NGHIỆP (PLC FRAMEWORK)

Hệ thống **CMS VINA VISION SYSTEM** trang bị một khung kết nối PLC độc lập với hãng sản xuất (`PlcManagerService`, `IPlcDriver`), được tối ưu hóa chuyên sâu cho dòng PLC Mitsubishi và sẵn sàng tích hợp vào dây chuyền tự động hóa:

```
                            KIẾN TRÚC GIAO TIẾP PLC
                          ┌───────────────────────────┐
                          │     PlcManagerService     │
                          └─────────────┬─────────────┘
                                        │
             ┌──────────────────────────┴──────────────────────────┐
             ▼                                                     ▼
┌───────────────────────────┐                         ┌───────────────────────────┐
│     McProtocol3eDriver    │                         │      MxComponentDriver    │
│  (MC Protocol 3E Binary)  │                         │     (ActUtlType COM/OLE)  │
│  - Giao tiếp TCP trực tiếp│                         │  - Qua phần mềm Mitsubishi│
│  - CPU Q, L, iQ-R, iQ-F   │                         │  - Hỗ trợ Station No      │
└───────────────────────────┘                         └───────────────────────────┘
```

### 1.1. Cấu hình Driver Mitsubishi MC Protocol 3E Binary TCP
1. Trên PLC Mitsubishi, tạo một kết nối Ethernet dạng **MC Protocol** (Code: Binary, Port: ví dụ `5002` hoặc `1025`).
2. Trên phần mềm Vision, mở menu **`🔌 Truyền Thông` ➔ `🔌 Quản Lý Kết Nối PLC...`**.
3. Thêm một kết nối mới:
   - *Driver Type:* `Mitsubishi MC Protocol 3E (Binary TCP)`.
   - *IP Address:* IP của card Ethernet PLC (Ví dụ: `192.168.1.10`).
   - *Port:* `5002`.
   - *Polling Interval:* `50 ms` (Tần số quét biến).
   - Nhấp **"Kết Nối"** ➔ Trạng thái chuyển sang màu xanh lá **Connected**.

### 1.2. Các Node PLC trên Tool Editor Canvas
Kỹ sư có thể kéo trực tiếp các Node PLC vào luồng xử lý trên Canvas:
- **`PlcTrigger`:** Lắng nghe bit cảm biến (Ví dụ: `X0`, `M100`). Khi xuất hiện sườn dương (`RisingEdge`) hoặc sườn âm (`FallingEdge`), tự động kích hoạt chụp camera và chạy Job Flow.
- **`PlcRead`:** Đọc giá trị từ thanh ghi PLC (D, W, R) đưa vào làm tham số dung sai hoặc mã sản phẩm.
- **`PlcWrite`:** Gửi kết quả đánh giá (PASS = bật `Y10`, NG = bật `Y11`) hoặc ghi giá trị đo đạc thực tế (mm) vào thanh ghi `D200` để robot phân loại.
- **`PlcBatchRead` / `PlcBatchWrite`:** Đọc/ghi cùng lúc hàng chục thanh ghi liên tiếp chỉ trong 1 chu kỳ truyền thông duy nhất, tối ưu hóa băng thông mạng.

### 1.3. Các công cụ giám sát PLC tích hợp
- **`PLC Monitor`:** Bảng theo dõi trực tiếp giá trị của toàn bộ các Tag theo thời gian thực.
- **`PLC Tags Browser`:** Tra cứu danh bạ biến được định nghĩa sẵn.
- **`PLC Oscilloscope`:** Công cụ phân tích dạng sóng số, cho phép đo chính xác độ trễ (latency tính bằng mili-giây) từ thời điểm cảm biến kích hoạt đến thời điểm camera chụp và xuất tín hiệu OK/NG.

---

## 2. HỆ THỐNG QUẢN LÝ CƠ SỞ DỮ LIỆU (DATABASE MANAGER)

Để phục vụ truy xuất nguồn gốc (Traceability) và tiêu chuẩn Smart Factory / MES, phần mềm tích hợp module **DB Manager** đa nền tảng.

### 2.1. Các hệ quản trị CSDL được hỗ trợ
1. **Microsoft SQL Server (MS SQL):** Tiêu chuẩn phổ biến nhất trong các nhà máy sản xuất linh kiện.
2. **MySQL / MariaDB:** Hiệu năng cao, mã nguồn mở, dễ dàng kết hợp với máy chủ XAMPP.
3. **PostgreSQL:** Độ tin cậy cao cho dữ liệu lớn.
4. **SQLite:** CSDL nhúng cục bộ dạng tệp (`.db`), không cần cài đặt server, lưu trữ trực tiếp trên ổ cứng máy IPC.
5. **Oracle Database & ODBC:** Kết nối với các hệ thống ERP / MES truyền thống.

### 2.2. Sử dụng `DbNode` trên Tool Editor Canvas
- Mở menu **`🗄️ Dữ Liệu` ➔ `🗄️ Quản Lý Kết Nối Database (DB Manager)...`** để cấu hình và nhấp **"⚡ Test Connection"** kiểm tra đường truyền.
- Thêm **`DbNode`** vào Canvas:
  - **Chế độ Ghi (Write):**
    - Câu lệnh SQL mẫu:
      ```sql
      INSERT INTO InspectionLogs (ProductCode, Result, Diameter, MeasuredTime) 
      VALUES ('{ProductCode}', '{Result.Pass}', {Circle_1.Radius}*2, GETDATE())
      ```
    - Các thẻ token `{ToolName.Property}` tự động được thay thế bằng kết quả đo thực tế của chu kỳ đó.
    - Điều kiện thực thi: `Always` (Luôn ghi), `OnPass` (Chỉ ghi khi Đạt), `OnFail` (Chỉ ghi khi Lỗi).
  - **Chế độ Đọc (Read):**
    - Truy vấn thông tin cấu hình hoặc tiêu chuẩn từ máy chủ MES đưa vào làm thông số kiểm tra.

---

## 3. BỘ ĐIỀU KHIỂN ĐÈN CHIẾU SÁNG (LIGHTING CONTROLLER)

Hệ thống hỗ trợ cả 2 hình thức điều khiển chiếu sáng công nghiệp:

### 3.1. Điều khiển Đèn Cục Bộ qua cổng nối tiếp (Serial COM RS232 / USB)
- Mở menu **`💡 Chiếu Sáng` ➔ `💡 Bộ Điều Khiển Đèn Cục Bộ (Lighting Controller)...`**.
- Cấu hình cổng COM (Ví dụ: `COM3`, Baudrate: `9600` hoặc `19200`).
- Giao diện cung cấp 4 thanh trượt điều chỉnh cường độ sáng độc lập cho các kênh: `CH1`, `CH2`, `CH3`, `CH4` từ mức `0` (Tắt) đến `255` (Sáng cực đại).
- Tích hợp tính năng lưu giá trị độ sáng đèn riêng biệt theo từng tệp Job (`JobCameraSettingsWindow`). Khi nạp Job nào, đèn sẽ tự động chuyển sang mức sáng tối ưu của Job đó.

### 3.2. Kiến trúc Máy Chủ / Khách Điều Khiển Đèn Qua Mạng LAN (Lighting Server / Client)
- **Lighting Server (`LightingServerWindow`):** Chạy trên máy IPC có cắm cáp vật lý với bộ đèn, mở một cổng TCP Socket (Ví dụ: Port `8888`).
- **Lighting Client (`LightingClientWindow`):** Các máy tính kỹ thuật hoặc máy kiểm tra khác trong mạng LAN có thể kết nối tới IP máy chủ để điều chỉnh ánh sáng từ xa mà không cần cắm thêm dây cáp phần cứng.

---

## 4. HỆ THỐNG GÁN MÃ SẢN PHẨM VÀ QUẢN LÝ JOB TỪ XA

### 4.1. Gán Mã Sản Phẩm ↔ Tệp Job (`ProductAssignDialog`)
Mở menu **`🗄️ Dữ Liệu` ➔ `📋 Gán Mã Sản Phẩm ↔ Tệp Job (Product Assign)...`**:
- Giao diện bảng cho phép kỹ sư liên kết chuỗi mã quét Barcode/QR với đường dẫn tệp `.job` tương ứng trên ổ đĩa hoặc ổ đĩa mạng dùng chung (`\\Server\VisionJobs\`).
- Hỗ trợ câu truy vấn SQL Upsert tự động lưu vào CSDL máy chủ.
- Tích hợp tính năng phân trang server-side (`OFFSET-FETCH`) và DataGrid ảo hóa, xử lý mượt mà danh mục hơn 100,000 mã sản phẩm.

---

## 5. HỆ THỐNG CẬP NHẬT PHẦN MỀM TỪ XA (OVER-THE-AIR UPDATE - OTA)

Trong môi trường nhà máy có hàng chục trạm kiểm tra IPC phân tán, việc kỹ sư phải cầm USB đi cài đặt thủ công từng máy khi có phiên bản mới là vô cùng tốn thời gian và rủi ro.

Hệ thống tích hợp giải pháp **OTA Update tự động khép kín**:

```
 [Máy Kỹ Sư Phát Triển]                       [Máy Chủ Web XAMPP]                  [Các Máy IPC Vision Client]
┌─────────────────────────┐                  ┌────────────────────┐               ┌───────────────────────────┐
│ Tab: 📦 Đóng Gói & Tải  │ ──(HTTP Chunk)──►│ ota_server.php     │ ◄──(GET Ping)─│ Tự động kiểm tra bản mới  │
│ Lên (OtaPublisher)      │                  │ - version.json     │               │                           │
│ - Tự tăng version csproj│                  │ - updates/*.zip    │ ──(Tải Zip)──►│ Tải, Xác thực SHA-256     │
│ - Nén tối ưu (35-45MB)  │                  └────────────────────┘               │ VisionUpdater.exe tự động │
│ - Tính SHA-256 Hex      │                                                       │ Backup và giải nén an toàn│
└─────────────────────────┘                                                       └───────────────────────────┘
```

### 5.1. Quy trình Phát hành Bản Cập Nhật (OTA Publisher)
Dành cho Kỹ sư Vision / Lập trình viên khi xuất bản phiên bản mới:
1. Mở menu **`❓ Trợ Giúp` ➔ `🔄 Kiểm Tra Bản Cập Nhật (OTA Update)...` ➔ Chọn Tab `📦 Đóng Gói & Tải Lên (Publish)`**.
2. Nhấp nút tăng nhanh phiên bản (Ví dụ: `+0.0.0.1 Patch` hoặc `+0.0.1.0 Minor`). Hệ thống tự động cập nhật 4 thẻ version XML trong tệp `VisionInspectionApp.UI.csproj`.
3. Nhập ghi chú phát hành (Release Notes).
4. Nhấp nút **"🚀 BẮT ĐẦU ĐÓNG GÓI & TẢI LÊN MÁY CHỦ"**:
   - Phần mềm tự động nén toàn bộ thư mục Release thành tệp `.zip`.
   - **Tối ưu hóa dung lượng:** Tự động loại trừ các thư viện native không phải Windows (Android, iOS, Linux, OSX) và tệp debug `.pdb`, giúp gói cập nhật giảm từ **321 MB xuống chỉ còn ~35 - 45 MB** (giảm gần 90%).
   - Tính mã băm bảo mật SHA-256.
   - Tự động phân đoạn gói (6MB/chunk) tải lên qua script `ota_server.php`, bypass hoàn toàn giới hạn kích thước upload của web server.
   - Tự động cập nhật tệp manifest `version.json` trên máy chủ web.

### 5.2. Phía Máy Trạm Khách Vận Hành (Client OTA Update)
- Khi mở app, hệ thống tự động kiểm tra bản cập nhật mới trong chế độ nền.
- Nếu có bản mới, huy hiệu màu xanh lá **`🚀 Có Bản Mới: vX.X.X`** sẽ xuất hiện trên thanh tiêu đề.
- Kỹ sư/Quản trị viên chỉ cần nhấp vào huy hiệu ➔ Nhấp **"Tải & Cài Đặt"**:
  - Gói zip được tải về kèm thanh tiến trình và tốc độ mạng (MB/s).
  - Tự động xác thực mã băm SHA-256 để chống lỗi file tải dở hoặc file bị sửa đổi.
  - Khởi chạy trình cập nhật độc lập `VisionUpdater.exe`.
  - Tự động sao lưu toàn bộ thư mục hiện tại sang `backup/backup_{timestamp}/`.
  - Giải nén ghi đè và tự động khởi động lại ứng dụng `VisionInspectionApp.UI.exe` an toàn tuyệt đối. Nếu xảy ra sự cố (như mất điện đột ngột), trình cập nhật tự động kích hoạt cơ chế Rollback khôi phục lại phiên bản cũ nguyên vẹn.
