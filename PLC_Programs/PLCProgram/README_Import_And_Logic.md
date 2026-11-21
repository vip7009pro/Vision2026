# Chương Trình PLC Mitsubishi FX5U — Vision FIFO Không Encoder (Chống Trượt Hàng & Chống Lệch Queue WPF)

> **Dòng PLC**: Mitsubishi MELSEC iQ-F `FX5CPU FX5U`  
> **Định dạng**: GX Works3 Ladder CSV (UTF-16 LE BOM, Tab-delimited, CRLF) + Device Comment CSV chung  
> **Thư mục**: `PLC_Programs/PLCProgram`

---

## 1. Bảng I/O Phần Cứng Cố Định

| Địa chỉ | Loại | Chức năng | Ghi chú |
| :---: | :---: | :--- | :--- |
| **`X0`** | Input | **Start** | Nút nhấn chạy băng tải (hoặc HMI `M0`) |
| **`X1`** | Input | **Stop** | Nút nhấn dừng băng tải (hoặc HMI `M1`) |
| **`X4`** | Input | **Reset** | Xóa lỗi / xác nhận đã lấy hàng NG & chạy lại (hoặc HMI `M4`) |
| **`X5`** | Input | **Sensor chụp Camera** | Cảm biến phát hiện sản phẩm tại buồng chụp (hoặc giả lập HMI `M2`) |
| `X3` | Input (Tùy chọn) | Sensor trạm dừng NG | Chỉ dùng khi bật `M51 = ON` (hoặc HMI `M3`) |
| `X6` | Input (Tùy chọn) | Clear FIFO Queue | Xóa sạch hàng đợi PLC + gửi `M106` xóa WPF (hoặc dùng HMI `M6`) |
| `X7` | Input (Tùy chọn) | Reset Counters | Xóa bộ đếm sản lượng 32-bit (hoặc dùng HMI `M7`) |
| **`Y0`** | Output | **Băng tải** | Motor băng tải chính |
| **`Y1`** | Output | **Trigger Camera** | Xung Hardware Trigger Line0 (`D503 × 10ms`, mặc định `20ms`) |
| **`Y2`** | Output | **Đèn xanh (Green)** | Sáng khi băng tải đang chạy bình thường (`Y0 = ON` và không lỗi `M14 = OFF`) |
| **`Y3`** | Output | **Đèn vàng (Yellow)** | Sáng khi máy dừng/chờ bình thường (`Y0 = OFF` và không lỗi `M14 = OFF`) |
| **`Y4`** | Output | **Đèn đỏ (Red)** | Sáng khi có lỗi hoặc dừng do hàng NG (`M14 = ON`) |
| **`Y5`** | Output | **Còi (Buzzer)** | Khi phát sinh lỗi (`LDP M14`) kêu **beep đúng 0.25s (`T16 = K25` × 10ms)** rồi tự tắt |

---

## 2. Cơ Chế 4 Lớp Chống Trượt Hàng & Chống Lệch Queue WPF

1. **Nguyên tử 1–1 (`1 Trigger Y1 = 1 Ảnh WPF = 1 Ô FIFO`)**:
   - Cảm biến `X5` nối vào PLC (KHÔNG nối trực tiếp vào Camera). Camera chỉ nhận xung Trigger từ `Y1`.
   - Mỗi khi PLC phát xung `Y1` (`M30` 1 scan), PLC **đồng thời** nạp 1 ô mới (`0 = Chờ kết quả`) vào `D1000[TAIL]` và tăng `TAIL` (`D1101`).
2. **Lọc cảm biến `X5` 2 tầng chống rung mép khi dừng (`T10` ON / `T11` OFF)**:
   - `X5` phải ON ổn định `D500 × 10ms` (mặc định `10ms`) mới nhận có hàng (`M32`).
   - Sau khi bắt cạnh lên (`M34`), cờ mở khóa `M33` bị `RST` ngay lập tức. `X5` phải OFF ổn định đủ `D501 × 10ms` (mặc định `30ms`) mới `SET M33` lại -> hàng dừng ngay mép cảm biến bị rung lắc **tuyệt đối không bao giờ kích ảnh thứ 2**.
3. **Cổng Trigger chống trượt (`M13`) + Cửa sổ quán tính (`M12` / `T12`)**:
   - **Mặc định (`M50 = OFF` — Khóa cứng khi dừng)**: Ngay khi `Y0 = OFF`, cổng `M13` đóng tức thì -> khóa cứng `Y1`, Camera không thể grab thêm ảnh nào. Nếu có hàng trượt quán tính qua `X5` sau khi dừng, PLC không kích chụp mà tăng `D1110` và bật báo động **`M25` (Coast-Down Part Drift)**.
   - **Tùy chọn (`M50 = ON` — Theo dõi trượt quán tính)**: Trong cửa sổ quán tính `D502 × 10ms` (mặc định `300ms`) ngay sau khi cắt `Y0`, nếu có hàng thật trượt qua `X5`, PLC vẫn kích đúng 1 xung `Y1` **VÀ** nạp đồng bộ 1 ô vào FIFO để giữ khớp 1–1; hết `300ms` khóa cứng `Y1`.
4. **FIFO vòng 100 phần tử với 3 con trỏ độc lập (`HEAD` / `RES` / `TAIL`)**:
   - **`TAIL` (`D1101`)**: Chỉ tăng khi PLC phát xung `Y1` chụp hàng mới.
   - **`RES` (`D1103`)**: Tăng khi WPF trả kết quả (`M103/M104/M105` hoặc `D100`). Khối `05_POU_VISION_RESULT_RX` **luôn chạy kể cả khi băng tải đã dừng (`Y0 = OFF`)** -> 1–2 ảnh còn tồn trong pipeline 16 nấc của WPF vẫn được ghi vào đúng ô `D1000[RES]` mà **không làm dịch chuyển vị trí băng tải (`HEAD`)**.
   - **`HEAD` (`D1100`)**: Con trỏ hàng cũ nhất tới Trạm dừng NG (`D510` nấc, mặc định `10`, hoặc cảm biến `X3` nếu `M51 = ON`). Nếu NG -> dừng băng tải (`M20`), giữ `HEAD` (`M26`), sáng đèn đỏ `Y4` và còi `Y5` beep `0.25s`; công nhân lấy hàng NG và bấm Reset `X4` mới pop `HEAD` và chạy tiếp.
   - **Chặn kết quả thừa (`M23`)**: Nếu `RES == TAIL` mà WPF vẫn gửi kết quả -> chặn ghi, tăng `D1112`, báo lỗi `M23`.
   - **Đồng bộ Reset Queue 2 chiều (`M106`)**: Khi bấm Clear Queue (`X6` hoặc `M6`), PLC xóa sạch FIFO và phát xung `M106` (500ms) sang WPF để xóa đồng bộ hàng đợi trên App.

---

## 3. Danh Sách Các Khối File CSV & Thứ Tự Quét (Scan Order)

Trong GX Works3 (`Program -> Scan`), tạo 7 Program POU theo **đúng thứ tự**:

| Thứ tự | File CSV | Chức năng |
| :---: | :--- | :--- |
| 1 | `01_POU_INIT_CONFIG.csv` | Khởi tạo lúc bật nguồn (`SM402`), xóa FIFO & nạp thông số mặc định (`D500..D521`). |
| 2 | `02_POU_HEARTBEAT_SAFETY.csv` | Nhịp tim `M108` / Watchdog `M100` (`M24`), xung Reset Queue `M106`, tổng hợp lỗi `M14`, đèn xanh `Y2` / vàng `Y3` / đỏ `Y4`, còi `Y5` beep 0.25s (`T16`). |
| 3 | `03_POU_CONVEYOR_CONTROL.csv` | Start (`X0/M0`), Stop (`X1/M1`), Reset (`X4/M4`), Clear Queue (`X6/M6`), khóa liên động `Y0`, cửa sổ quán tính `M12` (`T12`). |
| 4 | `04_POU_ANTI_DRIFT_TRIGGER_FIFO.csv` | Lọc chống rung cảm biến `X5` (`T10/T11`), cổng chống trượt `M13`, phát xung `Y1` + Push `TAIL` nguyên tử, phát hiện trượt `M25`. |
| 5 | `05_POU_VISION_RESULT_RX.csv` | Nhận kết quả WPF (Bit `M103/M104/M105` hoặc Word `D100`), ghi `D1000[RES]` cả khi dừng máy, phát Ack `M11`, đồng bộ gương `M200..M219`. |
| 6 | `06_POU_STATION_REJECT_POP.csv` | Tính `D1104` / `D1105`, kiểm tra `HEAD` tại trạm dừng (`D510` nấc hoặc cảm biến `X3`), dừng NG (`M20/M26`), dừng thiếu KQ (`M22`), Pop `HEAD`. |
| 7 | `07_POU_PRODUCTION_COUNTERS.csv` | Bộ đếm 32-bit `DINT`: Tổng chụp `D310`, Tổng KQ `D300`, Tổng OK `D302`, Tổng NG `D304`, Tổng Pop `D312`, Reset bộ đếm `X7/M7`. |
| Chung | `DeviceComment_Common.csv` | Bảng Device Comment chung cho toàn bộ chương trình. |

---

## 4. Hướng Dẫn Import Vào GX Works3

1. Tạo Project mới: Series `FX5CPU`, Type `FX5U`, Language `Ladder`.
2. **Import Device Comment chung**: Mở `Device -> Device Comment -> COMMENT` -> **Import File** -> chọn `DeviceComment_Common.csv` (`CSV (tab delimited)`).
3. **Import 7 khối Ladder (`01` → `07`)**: Trong `Program -> Scan`, tạo lần lượt 7 POU, mở từng POU -> `Edit -> Import File` -> chọn file `.csv` tương ứng (`CSV (tab delimited)`).
4. Nhấn **`Shift + Alt + F4` (Rebuild All)** để convert toàn bộ chương trình.

---

## 5. Bảng Ánh Xạ Handshake WPF & Tham Số Tinh Chỉnh

### A. Cấu Hình Handshake Trên App Vision WPF
| Tham số WPF | Device PLC | Ghi chú |
| :--- | :---: | :--- |
| Vision Ready / Busy | `M101` / `M102` | PC -> PLC |
| Vision Done / Pass / NG | `M103` / `M104` / `M105` | Chế độ Bit (`M55 = OFF`), hỗ trợ cả Level & RisingEdge 20ms |
| Result Word (Int16) | `D100` | OK=`2` (`D520`), NG=`3` (`D521`) khi bật `M55 = ON` |
| PLC Ack (Tùy chọn) | `M11` | Xung 20ms (để trống trên WPF nếu dùng Non-blocking) |
| Reset Queue Tag | `M106` | PLC -> PC (xung 500ms khi bấm `X6`/`M6`, chọn `RisingEdge`) |
| PC / PLC Heartbeat | `M100` / `M108` | Bật `M54 = ON` trên PLC để kích hoạt Watchdog |
| Queue Mirror (20 nấc) | `M200..M219` | Mảng 20 bit hiển thị kết quả gần nhất |

### B. Tham Số & Bit Tùy Chọn
| Device | Mặc định | Ý nghĩa |
| :---: | :---: | :--- |
| `D500` / `D501` | `1` (10ms) / `3` (30ms) | Lọc ON / Lọc OFF chống rung mép cảm biến `X5` (×10ms) |
| `D502` | `30` (300ms) | Cửa sổ trượt quán tính sau khi tắt `Y0` (×10ms) |
| `D503` / `D504` | `2` (20ms) / `5` (50ms) | Độ rộng xung Trigger `Y1` / Chu kỳ tối thiểu Trigger (×10ms) |
| `D505` | `5` (500ms) | Timeout Watchdog PC Heartbeat (×100ms) |
| `D510` | `10` | Số nấc từ Camera tới Trạm dừng NG (khi `M51 = OFF`) |
| `D1104` / `D1105` | Read-only | Số hàng trong FIFO / Số ảnh đang chờ WPF trả KQ (`0` = đã xả sạch) |
| `D1110` / `D1112` | Read-only | Bộ đếm trượt hàng sau khi dừng / Bộ đếm kết quả thừa từ WPF |
| `M50` | `OFF` | `OFF`: Khóa cứng `Y1` khi `Y0=OFF` + báo trượt `M25`; `ON`: Cho phép chụp & push trong cửa sổ `T12` |
| `M51` | `OFF` | `OFF`: Trạm dừng theo `D510` nấc; `ON`: Trạm dừng theo cảm biến `X3` |
| `M53` | `OFF` | `ON`: Chờ WPF xả sạch pipeline (`D1105 = 0`) mới cho `Y0` chạy lại |
| `M54` | `OFF` | `ON`: Bật giám sát Watchdog PC Heartbeat `M100` |
| `M55` | `OFF` | `OFF`: Nhận KQ theo Bit (`M103..M105`); `ON`: Nhận KQ theo Word (`D100`) |

> **Lưu ý nút Stop `X1`**: Chương trình đang viết theo nút thường hở (NO: nhấn `X1 = ON` để dừng). Nếu tủ điện dùng nút Stop thường đóng (NC), đổi `ANI X1` / `LD X1` trong `03_POU_CONVEYOR_CONTROL.csv` thành `AND X1` / `LDI X1`.
