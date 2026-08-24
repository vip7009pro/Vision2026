# Hướng Dẫn Nạp Chương Trình Vào Mitsubishi GX Works 3 / GX Works 2
## Dành Cho Hệ Thống Thị Giác Công Nghiệp Vision 2026

---

## 1. Cấu Trúc Gói Chương Trình PLC

Trong thư mục `g:\NODEJS\Vision2026\PLC_Programs\Mitsubishi_GXWorks3\`:
- 📄 `GlobalLabels_GXWorks3.csv`: Danh sách biến toàn cục (Global Labels) có thể Import trực tiếp vào GX Works 3.
- 📄 `DeviceComments_GXWorks.csv`: Danh sách chú thích thiết bị (Device Comments) cho GX Works 2 & GX Works 3.
- 📜 `POU_01_Watchdog_Heartbeat.st`: Chương trình Watchdog 2 chiều & Liên động an toàn.
- 📜 `POU_02_Vision_Handshake.st`: Máy trạng thái chu trình bắt tay công nghiệp 24/7.
- 📜 `POU_03_Encoder_Tracking.st`: Đọc bộ đếm xung tốc độ cao và tính tốc độ chuyền.
- 📜 `POU_04_ShiftRegister_Reject.st`: Hàng đợi Shift Register & Kích hoạt Xylanh loại bỏ chính xác theo mm.
- 📜 `POU_05_Result_Handler.st`: Xử lý dữ liệu đo đạc tọa độ và cộng dồn sản lượng.
- 📐 `Ladder_Diagram_Visual.md`: Sơ đồ thang trực quan dạng Rung & Mã Instruction List (IL / Mnemonic).
- ⚙️ `Ladder_Mnemonic_GXWorks.il`: Mã lệnh Mnemonic để gõ/paste nhanh vào PLC.

---

## 2. Các Bước Nạp Vào Phần Mềm GX Works 3 (iQ-F / FX5U / iQ-R)

### Bước 1: Import Global Labels
1. Mở dự án trên **GX Works 3**.
2. Trong cửa sổ **Navigation** (cột bên trái), tìm đến mục: `Global Label` $\rightarrow$ Nhấp chuột phải chọn **Import from File**.
3. Chọn tệp `GlobalLabels_GXWorks3.csv`. Toàn bộ tên biến, địa chỉ Device (`X0`, `Y0`, `M10`, `D1000`, ...) và kiểu dữ liệu sẽ tự động được điền đầy đủ.

### Bước 2: Tạo Các Khối Chương Trình (POU)
1. Trong mục `Program` $\rightarrow$ `Scan`, nhấp chuột phải chọn **Add New Data**.
2. Đặt tên POU tương ứng và chọn ngôn ngữ:
   - **Tùy chọn A (Ngôn ngữ ST - Khuyên Dùng)**: Chọn `Data Type: Structured Text (ST)`, sau đó sao chép nội dung từ các file `POU_01..05.st` dán vào.
   - **Tùy chọn B (Ngôn ngữ Ladder)**: Chọn `Data Type: Ladder`, vẽ các Rung theo sơ đồ trong file `Ladder_Diagram_Visual.md` hoặc chuyển sang chế độ Mnemonic để nạp mã `Ladder_Mnemonic_GXWorks.il`.

### Bước 3: Cấu Hình Cổng Truyền Thông Ethernet SLMP / MC Protocol Trên PLC
Để Vision PC có thể đọc/ghi trực tiếp vào các thanh ghi PLC qua giao thức MC Protocol (3E Frame):
1. Vào mục `Parameter` $\rightarrow$ `FX5UCPU` (hoặc `Ethernet Port`).
2. Chọn **Basic Settings** $\rightarrow$ **External Device Configuration**.
3. Nhấp đúp vào lưới cấu hình, kéo thả mục **SLMP Connection Module** (TCP):
   - **Protocol**: `TCP`
   - **Port No.**: `5000` (hoặc `5002` khớp với cài đặt trong tab PLC Manager của Vision App).
   - **Communication Data Code**: `Binary` (Khuyên dùng) hoặc `ASCII`.
4. Nhấn **Apply** $\rightarrow$ **Check Parameter** $\rightarrow$ Biên dịch dự án (**Rebuild All - F4**) $\rightarrow$ Nạp xuống PLC (**Write to PLC**).

---

## 3. Khớp Nối Với Cấu Hình Trên Phần Mềm Vision 2026

Trong phần mềm Vision App, mở cửa sổ **Quản Lý PLC** (PLC Manager Window) và kiểm tra các thông số tương ứng:

| Mục trên Vision App | Tag / Địa chỉ PLC | Chức năng |
|---|---|---|
| **Handshake $\rightarrow$ Ready Tag** | `Y1` (hoặc `Y1_VisionReady`) | Vision sẵn sàng nhận trigger |
| **Handshake $\rightarrow$ Busy Tag** | `Y2` (hoặc `Y2_VisionBusy`) | Vision đang bận xử lý ảnh |
| **Handshake $\rightarrow$ Done Tag** | `Y3` (hoặc `Y3_VisionDone`) | Vision hoàn thành chu trình |
| **Handshake $\rightarrow$ Pass Tag** | `Y4` (hoặc `Y4_VisionPass`) | Kết quả sản phẩm OK |
| **Handshake $\rightarrow$ NG Tag** | `Y5` (hoặc `Y5_VisionNG`) | Kết quả sản phẩm NG |
| **Handshake $\rightarrow$ PLC Ack Tag** | `X1` (hoặc `X1_PlcAck`) | PLC xác nhận đã nhận kết quả |
| **Watchdog $\rightarrow$ Vision Heartbeat** | `Y0` (hoặc `Y0_VisionHeartbeat`) | Nhịp tim do Vision PC phát |
| **Watchdog $\rightarrow$ PLC Heartbeat** | `X0` (hoặc `X0_PlcHeartbeat`) | Nhịp tim do PLC phát |
| **Motion $\rightarrow$ Encoder Tag** | `D1000` | Xung Encoder 32-bit |
| **Motion $\rightarrow$ Speed Tag** | `D1002` | Tốc độ dây chuyền (m/phút) |
| **Shift Register $\rightarrow$ Reject Tag** | `Y20` | Tín hiệu van xylanh thổi loại bỏ |
| **Result Transfer $\rightarrow$ Tọa độ X, Y, Angle** | `D200`, `D202`, `D204` | Truyền tọa độ sản phẩm |
