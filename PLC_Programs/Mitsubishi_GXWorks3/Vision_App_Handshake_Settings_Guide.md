# HƯỚNG DẪN THÔNG SỐ CÀI ĐẶT HANDSHAKE TRÊN APP VISION WPF
## DỰ ÁN: VISION 2026 — ĐỒNG BỘ BẮT TAY PLC MITSUBISHI FX5U & THEO DÕI NỐI TIẾP IN-FLIGHT

> **Tệp cấu hình**: `PlcManagerWindow.xaml` (Giao diện Quản Lý & Kết Nối PLC)  
> **Dòng PLC áp dụng**: Mitsubishi MELSEC iQ-F (**FX5U** / FX5UC / FX5S / FX5UJ) & FX3U/Q-Series  
> **Giao thức chuẩn**: Ethernet MC Protocol (3E Binary Frame) / MX Component  
> **Địa chỉ lưu trữ cấu hình**: `%LocalAppData%\VisionInspectionApp\plc_config.json`  

---

## 📌 NGUYÊN TẮC NHẬP DỮ LIỆU TRÊN GIAO DIỆN WPF

Trên giao diện **PLC & Industrial Motion Configuration**, các ô chọn Tag (ComboBox) đều hỗ trợ **2 CÁCH NHẬP**:
1. **Cách 1: Nhập trực tiếp Device Address (Khuyên dùng)**: Gõ trực tiếp ký hiệu thanh ghi/bit của FX5U như `M101`, `M102`, `M11`, `D1000`... App sẽ tự động ghi/đọc địa chỉ này.
2. **Cách 2: Chọn Tên Nhãn từ Dropdown**: Nếu bạn đã nhấn **📥 Import CSV** file `GlobalLabels_GXWorks3.csv` vào danh bạ (Tab 1), các ô sẽ tự động xổ ra danh sách tên nhãn tiếng Anh (`Vision_Ready`, `PLC_Ack`, `Encoder_Pulses`...).

---

## 1. TAB 1: KẾT NỐI PLC (🔌 1. KẾT NỐI & TAGS)

Dùng để khai báo địa chỉ IP và giao thức truyền thông tới PLC FX5U:

| Tên trường trên App | Giá trị nhập mẫu | Giải thích chi tiết |
| :--- | :--- | :--- |
| **Tên PLC** | `PLC1` | Tên định danh PLC trong ứng dụng (dùng để liên kết các Tab khác). |
| **Driver** | `Mitsubishi MC Protocol (Ethernet)` | Giao thức Ethernet trực tiếp không cần cài MX Component. |
| **IP Address** | `192.168.3.250` *(hoặc IP thực tế của FX5U)* | Địa chỉ IP cổng Ethernet onboard của CPU FX5U. |
| **Port** | `5000` *(hoặc `5002`)* | Cổng giao tiếp MC Protocol 3E Binary (cài trong FX5U Ethernet Port Settings). |
| **Scan (ms)** | `50` *(hoặc `100`)* | Chu kỳ vòng quét đọc/ghi PLC ngầm định (50ms ~ 100ms). |
| **Kích hoạt (Enabled)** | `[x] Tích chọn (Bật)` | Cho phép khởi chạy driver giao tiếp này. |

> 💡 **Mẹo**: Nhấn nút **🔍 Chẩn Đoán (Ping & Probe)** để kiểm tra thông mạng trước khi bấm **⚡ Kết Nối**.

---

## 2. TAB 2: CÀI ĐẶT BẮT TAY 24/7 (🤝 2. BẮT TAY 24/7 (HANDSHAKE))

Quản lý chu trình bắt tay 2 chiều: Vision sẵn sàng $\rightarrow$ PLC kích Trigger $\rightarrow$ Vision kiểm tra $\rightarrow$ Vision xuất kết quả $\rightarrow$ PLC phản hồi Ack $\rightarrow$ Reset chu trình.

| Tên trường trên App | Cách 1: Nhập Device Bit | Cách 2: Chọn Tag Name | Giải thích kỹ thuật & Vai trò |
| :--- | :--- | :--- | :--- |
| **PLC Đích (Target PLC)** | `PLC1` | `PLC1` | Chọn PLC1 đã tạo ở Tab 1. |
| **Timeout Bắt Tay (ms)** | `500` | `500` | Thời gian tối đa chờ PLC xác nhận Ack trước khi báo lỗi Timeout (khuyên dùng 300 - 500 ms). |
| **1. Vision Ready Tag (Y)** | **`M101`** | `Vision_Ready` | **PC $\rightarrow$ PLC**: Vision PC đã khởi động xong, các thuật toán đã nạp, sẵn sàng nhận phôi chụp. |
| **2. Vision Busy Tag (Y)** | **`M102`** | `Vision_Busy` | **PC $\rightarrow$ PLC**: Camera đang phơi sáng hoặc thuật toán xử lý ảnh đang bận tính toán. |
| **3. Vision Done Tag (Y)** | **`M103`** | `Vision_Done` | **PC $\rightarrow$ PLC**: Xung báo Vision PC đã hoàn tất kiểm tra và đã chốt xong kết quả. |
| **4. PLC Ack Tag (X)** | **`M11`** | `PLC_Ack` | **PLC $\rightarrow$ PC**: PLC xác nhận đã đọc xong kết quả OK/NG và chốt vào hàng đợi Shift Register. |
| **5. Vision Pass (OK) Tag (Y)**| **`M104`** | `Vision_Pass` | **PC $\rightarrow$ PLC**: Sản phẩm kiểm tra **ĐẠT (PASS)** theo tiêu chuẩn dung sai. |
| **6. Vision NG (Lỗi) Tag (Y)** | **`M105`** | `Vision_NG` | **PC $\rightarrow$ PLC**: Sản phẩm kiểm tra **LỖI (NG)** (sai kích thước, khuyết tật, rách biên...). |
| **Kích hoạt Chu trình Bắt tay**| `[x] Tích chọn (Bật)` | `[x] Tích chọn (Bật)` | Bắt buộc bật để kích hoạt động cơ máy trạng thái Deterministic Handshake. |

---

## 3. TAB 3: WATCHDOG & AN TOÀN (💓 3. WATCHDOG & AN TOÀN)

Giám sát nhịp tim 2 chiều để phát hiện đứt cáp mạng, treo app hoặc sự cố PLC, tự động dừng máy liên động:

| Tên trường trên App | Cách 1: Nhập Device Bit | Cách 2: Chọn Tag Name | Giải thích kỹ thuật & Vai trò |
| :--- | :--- | :--- | :--- |
| **PLC Đích (Target PLC)** | `PLC1` | `PLC1` | Chọn PLC mục tiêu. |
| **Chu Kỳ Gửi Nhịp Tim (ms)** | `100` | `100` | Tần số đảo xung nhịp tim giữa PC và PLC (khuyên dùng 100ms). |
| **Timeout Mất Nhịp Tim (ms)**| `300` | `300` | Sau 300ms nếu không thấy tín hiệu nhịp tim phản hồi $\rightarrow$ Cắt rơ-le an toàn chuyền. |
| **Vision Heartbeat Tag (Y)** | **`Y0`** hoặc **`M100`** | `Vision_Heartbeat` | **PC $\rightarrow$ PLC**: PC phát xung đảo bit liên tục 0/1 báo PC đang hoạt động tốt. |
| **PLC Heartbeat Tag (X)** | **`M108`** *(hoặc `X0`)* | `PLC_Heartbeat` | **PLC $\rightarrow$ PC**: PLC phát xung đảo bit từ cờ xung clock hệ thống `SM410` báo PLC đang RUN. |
| **Tag Báo Lỗi Ngắt Motor (Y)**| **`M202`** hoặc **`Y10`** | `Vision_Fault` *(hoặc `Line_E_Stop`)* | **PC $\rightarrow$ PLC**: Kích hoạt cờ ngắt băng tải khi phần mềm gặp lỗi nghiêm trọng hoặc mất liên lạc. |
| **Khóa Liên Động An Toàn** | `[x] Tích chọn (Bật)` | `[x] Tích chọn (Bật)` | Cho phép Vision tự ngắt dừng băng chuyền khi có sự cố Watchdog. |
| **Bật Giám Sát Heartbeat** | `[x] Tích chọn (Bật)` | `[x] Tích chọn (Bật)` | Bắt buộc bật. |

---

## 4. TAB 4: MOTION & ENCODER (🏃 4. MOTION & ENCODER)

Đọc xung Encoder tốc độ cao để tính tọa độ phôi In-Flight và tự động bù trừ phơi sáng cho Camera GigE:

| Tên trường trên App | Cách 1: Nhập Device/Số | Cách 2: Chọn Tag Name | Giải thích kỹ thuật & Vai trò |
| :--- | :--- | :--- | :--- |
| **PLC Đích (Target PLC)** | `PLC1` | `PLC1` | Chọn PLC mục tiêu. |
| **Tag Xung Encoder (D)** | **`D1000`** | `Encoder_Pulses` | Thanh ghi 32-bit đếm xung Encoder trục chuyền (`DINT`). |
| **Tag Vận Tốc Máy Cuộn (D)** | **`D1002`** | `Line_Speed_Mpm` | Thanh ghi lưu vận tốc chuyền hiện tại (`m/phút`). |
| **Hệ Số Xung (Pulses/mm)** | `100.0` | `100.0` | Số xung Encoder phát ra khi sản phẩm đi được 1mm (tùy chỉnh theo bánh xe đo). |
| **Độ Phân Giải (mm/pixel)** | `0.05` | `0.05` | Kích thước thực tế của 1 pixel ảnh sau khi Calib quang học. |
| **Vận Tốc Chuẩn (m/phút)** | `30.0` | `30.0` | Tốc độ băng chuyền định mức khi kỹ sư tinh chỉnh thông số thị giác. |
| **Thời Gian Phơi Sáng (µs)** | `500.0` | `500.0` | Exposure Time của Camera tại tốc độ chuẩn (dùng để tự động tính tỷ lệ bù phơi sáng). |

---

## 5. TAB 5: SHIFT REGISTER & LOẠI BỎ (🎯 5. SHIFT REGISTER & LOẠI BỎ)

Điều khiển cơ cấu Stopper hoặc Ben khí loại bỏ sản phẩm lỗi ngoài buồng kiểm tra:

| Tên trường trên App | Cách 1: Nhập Device/Số | Cách 2: Chọn Tag Name | Giải thích kỹ thuật & Vai trò |
| :--- | :--- | :--- | :--- |
| **PLC Điều Khiển Cơ Cấu** | `PLC1` | `PLC1` | Chọn PLC điều khiển ngõ ra Ben gạt / Stopper. |
| **Tag Kích Hoạt Reject (Y)** | **`Y21`** *(hoặc `Y20`)* | `NG_Stopper_Outside` *(hoặc `Reject_Piston`)* | Ngõ ra PLC kích hoạt Stopper cữ chặn hoặc Xylanh đẩy hàng NG ngoài buồng. |
| **Khoảng Cách Camera ➔ Trạm**| `1500.0` | `1500.0` | Khoảng cách vật lý từ vị trí chụp ảnh đến vị trí dừng ngoài buồng (`mm`). |
| **Dung Sai Kích Hoạt (± mm)**| `15.0` | `15.0` | Cửa sổ dung sai cho phép kích hoạt cơ cấu gạt / chặn (`± mm`). |
| **Độ Rộng Xung Giữ Lệnh (ms)**| `100` | `100` | Thời gian duy trì xung mở van khí trước khi trả về vị trí ban đầu. |
| **Kích hoạt Cơ cấu Loại Bỏ** | `[x] Tích chọn (Bật)` | `[x] Tích chọn (Bật)` | Cho phép module In-Flight Shift Register tự động kích hoạt ngõ ra Reject. |

---

## 6. BẢNG TRA CỨU NHANH TẤT CẢ TAG & DEVICE CHO PLC FX5U

| Chức năng tín hiệu | Chiều truyền | Device FX5U | Tag Name chuẩn GX Works 3 | Kiểu dữ liệu |
| :--- | :---: | :---: | :--- | :---: |
| **Vision Heartbeat** | PC $\rightarrow$ PLC | **`Y0`** *(hoặc `M100`)* | `Vision_Heartbeat` | `BOOL` |
| **PLC Heartbeat** | PLC $\rightarrow$ PC | **`M108`** *(hoặc `X0`)* | `PLC_Heartbeat` | `BOOL` |
| **Vision Ready** | PC $\rightarrow$ PLC | **`M101`** | `Vision_Ready` *(hoặc `Vision_Ready_M`)* | `BOOL` |
| **Vision Busy** | PC $\rightarrow$ PLC | **`M102`** | `Vision_Busy` *(hoặc `Vision_Busy_M`)* | `BOOL` |
| **Vision Done** | PC $\rightarrow$ PLC | **`M103`** | `Vision_Done` *(hoặc `Vision_Done_M`)* | `BOOL` |
| **Vision Pass (OK)** | PC $\rightarrow$ PLC | **`M104`** | `Vision_Pass` *(hoặc `Vision_Pass_M`)* | `BOOL` |
| **Vision NG (Lỗi)** | PC $\rightarrow$ PLC | **`M105`** | `Vision_NG` *(hoặc `Vision_NG_M`)* | `BOOL` |
| **PLC Ack (Xác nhận)** | PLC $\rightarrow$ PC | **`M11`** *(hoặc `X1`)* | `PLC_Ack` | `BOOL` |
| **PLC Trigger chụp** | PLC $\rightarrow$ PC | **`M10`** | `PLC_Trigger` | `BOOL` |
| **Báo lỗi ngắt motor** | PC $\rightarrow$ PLC | **`M202`** *(hoặc `Y10`)* | `Vision_Fault` / `Line_E_Stop` | `BOOL` |
| **Stopper giữ hàng NG ngoài**| PLC $\rightarrow$ Van | **`Y21`** | `NG_Stopper_Outside` | `BOOL` |
| **Còi đèn báo hàng NG ngoài**| PLC $\rightarrow$ Đèn | **`Y22`** | `NG_Alarm_Beacon` | `BOOL` |
| **Xung Encoder 32-bit** | PLC $\rightarrow$ PC | **`D1000`** | `Encoder_Pulses` | `DINT` |
| **Vận tốc cuộn (m/min)** | PLC $\rightarrow$ PC | **`D1002`** | `Line_Speed_Mpm` | `INT` |
| **Tổng sản phẩm kiểm tra** | PLC Nội bộ | **`D300`** | `Total_Inspected_Count` | `DINT` |
| **Tổng sản phẩm Đạt (OK)** | PLC Nội bộ | **`D302`** | `Total_Pass_Count` | `DINT` |
| **Tổng sản phẩm Lỗi (NG)** | PLC Nội bộ | **`D304`** | `Total_NG_Count` | `DINT` |

---

## 7. CÁC BƯỚC THAO TÁC CÀI ĐẶT TRÊN MÁY TÍNH VẬN HÀNH

1. Mở cửa sổ **PLC & Industrial Motion Configuration** từ menu chính của ứng dụng.
2. Tại **Tab 1 (Kết Nối & Tags)**:
   - Điền IP PLC (ví dụ: `192.168.3.250`), Port `5000`.
   - Bấm **📥 Import CSV (GX Works / Standard)** $\rightarrow$ Chọn tệp `GlobalLabels_GXWorks3.csv` trong thư mục dự án để nạp toàn bộ danh bạ biến.
   - Bấm **⚡ Kết Nối (Connect)** và kiểm tra trạng thái chuyển sang **Connected (Màu xanh)**.
3. Chuyển sang **Tab 2 (Bắt Tay 24/7)**:
   - Điền các Device Bit: `M101`, `M102`, `M103`, `M11`, `M104`, `M105` (hoặc chọn tên Tag tương ứng từ danh sách).
   - Tích chọn **☑ Kích hoạt Chu trình Bắt tay Công nghiệp**.
4. Chuyển sang **Tab 3 (Watchdog & An Toàn)**:
   - Điền `Y0` (hoặc `M100`), `M108`, `M202`.
   - Tích chọn **☑ Bật Giám Sát Heartbeat Watchdog** và **☑ Kích hoạt Khóa Liên Động An Toàn**.
5. Nhấn nút **💾 Lưu Toàn Bộ Cấu Hình (Save Config)** ở góc dưới cùng bên phải.
   - *Ứng dụng sẽ tự động ghi nhớ và lưu vào tệp cấu hình JSON của máy, giữ nguyên trạng thái cho các lần khởi động tiếp theo.*
