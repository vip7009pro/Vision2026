# CHƯƠNG TRÌNH LADDER DIAGRAM ĐẦY ĐỦ — MITSUBISHI FX5U (GX WORKS 3)
## DỰ ÁN: KIỂM TRA THỊ GIÁC VISION 2026 (IN-FLIGHT TRACKING & DỪNG NG NGOÀI BUỒNG)

> **Môi trường lập trình**: MELSOFT GX Works 3 (Ngôn ngữ: **Ladder**)  
> **Dòng PLC áp dụng**: Mitsubishi MELSEC iQ-F (**FX5U** / FX5UC / FX5S / FX5UJ) & iQ-R Series  
> **Lệnh dịch bit chuẩn FX5U**: **`BSFRP`** (Bit Shift Right Pulse) thay thế cho lệnh `SFT` cũ  
> **Giao thức kết nối**: Ethernet MC Protocol / SLMP (3E Frame, Port 5000 / 5002)

---

## BẢNG PHÍM TẮT NHANH KHI VẼ LADDER TRONG GX WORKS 3
* **F5**: Tiếp điểm thường mở `-[ ]-` (`LD`, `AND`)
* **F6**: Tiếp điểm thường đóng `-[/]-` (`LDI`, `ANI`)
* **Shift + F7**: Xung cạnh lên `-[ ↑ ]-` (`LDP`, `ANDP`)
* **Shift + F8**: Xung cạnh xuống `-[ ↓ ]-` (`LDF`, `ANDF`)
* **F7**: Cuộn hút ngõ ra `-( )--` (`OUT`)
* **F8**: Khối lệnh ứng dụng `-[ ]--` (`SET`, `RST`, `PLS`, `OUT T`, `BSFRP`, `DADD`, `DMOV`...)
* **Ctrl + Mũi tên**: Nối dây rẽ nhánh (Branch line)
* **F4**: Biên dịch toàn bộ (Convert / Build)

---

## NGUYÊN LÝ LỆNH DỊCH BIT `BSFRP` TRÊN PLC FX5U

Trên dòng FX5U (MELSEC iQ-F), lệnh `SFT` cổ điển của FX3U đã được thay thế bằng lệnh **`BSFR` / `BSFRP` (Bit Shift Right)**:
* **Cú pháp**: `[ BSFRP   d   n ]`
  * `d`: Bit đầu của dải thiết bị (Head device), ví dụ: `M70`
  * `n`: Số lượng bit dịch (Length), ví dụ: `K16` (tương ứng dải 16 bit `M70..M85`)
* **Cơ chế dịch**:
  * Mỗi khi có xung kích, toàn bộ 16 bit sẽ **dịch phải 1 bước** về phía bit chỉ số thấp:  
    `M85` $\rightarrow$ `M84` $\rightarrow$ `M83` $\rightarrow$ ... $\rightarrow$ `M71` $\rightarrow$ `M70`
  * Bit cao nhất `M85` tự động được điền `0`.
  * Bit thấp nhất `M70` lưu trữ trạng thái của phôi đã đến trạm ngoài buồng.
* **Ứng dụng cho bài toán In-Flight**:
  * **Tại buồng chụp (Trạm 15)**: Đặt tại bit `M85`. Khi Vision báo NG $\rightarrow$ `SET M85`.
  * **Tại trạm ngoài buồng (Trạm 0)**: Đặt tại bit `M70`. Phôi NG trôi tới đây thì `M70 = 1`.

---

## DANH MỤC 9 MẠNG LADDER (NETWORKS) CHO FX5U

### 🟢 MẠNG 1: TẠO NHỊP TIM PLC ĐẢO 100ms (PLC HEARTBEAT SANG VISION PC)
* **Ý nghĩa**: Sử dụng trực tiếp cờ xung đồng hồ phần cứng `SM410` (100ms clock: 50ms ON / 50ms OFF) của FX5U để xuất nhịp tim sang bit `M108`. Vision PC đọc bit này để biết PLC đang chạy tốt.

```text
|   SM410 (Clock 100ms)                                M108 (PLC_Heartbeat)
|-------[ ]-------------------------------------------------( )-------------|
```

---

### 🟢 MẠNG 2: WATCHDOG GIÁM SÁT NHỊP TIM VISION PC (TIMEOUT 300ms)
* **Ý nghĩa**: Vision PC đảo bit `Y0` (hoặc `M100`) chu kỳ 100ms. Mỗi khi `Y0` đảo trạng thái (từ 0 lên 1 hoặc từ 1 xuống 0), PLC phát xung `M201` để reset Timer `T0`. Nếu quá 300ms mà `Y0` không đổi (PC treo hoặc đứt cáp Ethernet), Timer `T0` đếm đủ 300ms $\rightarrow$ Bật cờ lỗi `M202` và ngắt rơ-le an toàn chuyền `Y10`.

```text
Network 2.1: Bắt cạnh lên và cạnh xuống nhịp tim Vision PC
|   Y0 (Vision Heartbeat)                              M205 (Xung cạnh lên)
|-------[ ↑ ]-----------------------------------------------( PLS M205 )----|
|
|   Y0 (Vision Heartbeat)                              M206 (Xung cạnh xuống)
|-------[ ↓ ]-----------------------------------------------( PLS M206 )----|
|
|   M205 (Xung lên)                                    M201 (Xung reset Watchdog)
|-------[ ]--------------------+----------------------------( )-------------|
|   M206 (Xung xuống)          |
|-------[ ]--------------------+

Network 2.2: Timer Watchdog T0 (K3 = 300ms với timer chuẩn 100ms T0)
|   SM400 (Luôn ON)             M201 (Xung đảo nhịp tim)      T0 K3 (300ms Timer)
|-------[ ]----------------------------[/]------------------( OUT T0 K3 )---|

Network 2.3: Xử lý lỗi Watchdog & Liên động an toàn chuyền (Line E-Stop)
|   T0 (Mất nhịp tim > 300ms)                          M202 (Vision Fault)
|-------[ ]-------------------------------------------------( SET M202 )----|
|                                                      Y10 (Ngắt chạy chuyền)
|                                                      -----( RST Y10 )-----|
|
|   M201 (Nhịp tim hồi phục)                           M202 (Xóa lỗi)
|-------[ ]-------------------------------------------------( RST M202 )----|
|                                                      Y10 (Cho phép chạy)
|                                                      -----( SET Y10 )-----|
```

---

### 🟢 MẠNG 3: BẮT TAY KÍCH HOẠT CAMERA CHỤP PHÔI TRONG BUỒNG (TRIGGER START)
* **Ý nghĩa**: Khi phôi chạm cảm biến `X2` trong buồng chụp, kiểm tra điều kiện an toàn: Vision đã sẵn sàng (`M101=1`), Vision không bận (`M102=0`), không có lỗi Watchdog (`M202=0`). PLC bắn xung `M10` (PLC_Trigger) cho Vision PC và đồng thời bắn xung `M20` để kích lệnh dịch hàng đợi In-Flight FIFO.

```text
|   X2 (Sensor buồng)   M101 (Ready)    M102 (Not Busy) M202 (No Fault)
|-------[ ↑ ]----------------[ ]--------------[/]------------[/]-----+
|                                                                    |
|                                                                    +----( PLS M10 ) (Trigger PC chụp)
|                                                                    |
|                                                                    +----( PLS M20 ) (Dịch hàng đợi FIFO)
```

---

### 🟢 MẠNG 4: NHẬN KẾT QUẢ TỪ VISION PC & XÁC NHẬN CHỐT BẮT TAY (PLC ACK)
* **Ý nghĩa**: Khi Vision PC xử lý ảnh xong, PC bật cờ `M103` (Vision_Done) và bật kèm kết quả: `M104` (Pass/OK) hoặc `M105` (NG/Lỗi).
  - PLC nhận `M103` $\rightarrow$ bật cờ `M11` (PLC_Ack) để báo cho PC biết đã nhận kết quả.
  - Nếu kết quả là NG (`M105=1`) $\rightarrow$ phát xung `M30` đánh dấu phôi tại buồng là NG.
  - Nếu kết quả là OK (`M104=1`) $\rightarrow$ phát xung `M31` đánh dấu phôi tại buồng là OK.
  - Khi Vision PC thấy `M11=1`, PC hạ `M103=0` $\rightarrow$ PLC tự động hạ `M11=0`.

```text
Network 4.1: Nhận kết quả và kích hoạt PLC Ack
|   M103 (Vision Done)
|-------[ ]-------------------------------------------------+----( OUT M11 ) (PLC_Ack)
|                                                           |
|                               M105 (Vision NG)            |
|                              -------[ ]-------------------+----( PLS M30 ) (Chốt cờ NG vào nấc M85)
|                                                           |
|                               M104 (Vision Pass)          |
|                              -------[ ]-------------------+----( PLS M31 ) (Chốt cờ OK vào nấc M85)

Network 4.2: Tự động hạ PLC Ack khi Vision PC đã hoàn tất reset
|   M103 (Vision Done)
|-------[/]------------------------------------------------------( RST M11 )
```

---

### 🟢 MẠNG 5: QUẢN LÝ HÀNG ĐỢI NỐI TIẾP BẰNG LỆNH `BSFRP` CHUẨN FX5U
* **Ý nghĩa**: Băng tải chở nhiều sản phẩm di chuyển liên tục từ buồng chụp ra trạm ngoài buồng. Chúng ta dùng lệnh **`BSFRP M70 K16`** để dịch chuyển kết quả theo bước chạy của phôi:
  - Khi có phôi mới vào buồng (`M20`): Gọi `[BSFRP M70 K16]`, toàn bộ 16 nấc dịch phải từ `M85` về `M70`.
  - Nấc `M85`: Vị trí phôi đang ở buồng kiểm tra.
  - Nấc `M70`: Vị trí phôi khi đến trạm chỉ định ngoài buồng.
  - Khi Vision báo kết quả:
    - Nếu NG (`M30`): Kích `SET M85` (Ghi nhận phôi ở buồng là NG).
    - Nếu OK (`M31`): Kích `RST M85` (Ghi nhận phôi ở buồng là OK).

```text
Network 5.1: Dịch mảng trạng thái phôi mỗi khi có phôi mới vào buồng
|   M20 (Xung phôi mới vào buồng)
|-------[ ]-------------------------------------------------[ BSFRP M70 K16 ]- (Dịch 16 bit từ M85 về M70)

Network 5.2: Ghi nhận kết quả NG vào nấc phôi vừa kiểm tra tại buồng
|   M30 (Xung chốt kết quả NG)
|-------[ ]-------------------------------------------------( SET M85 )----- (Nấc M85 tại buồng là NG)

Network 5.3: Ghi nhận kết quả OK vào nấc phôi vừa kiểm tra tại buồng
|   M31 (Xung chốt kết quả OK)
|-------[ ]-------------------------------------------------( RST M85 )----- (Nấc M85 tại buồng là OK)
```

---

### 🟢 MẠNG 6: DỪNG ĐÚNG PHÔI NG TẠI VỊ TRÍ CHỈ ĐỊNH Ở NGOÀI BUỒNG KIỂM TRA
* **Ý nghĩa**:
  - Khi phôi di chuyển ra ngoài buồng và chạm cảm biến trạm ngoài `X3`:
  - PLC kiểm tra bit **`M70`** (kết quả của phôi đang ở trạm ngoài):
    - Nếu **`M70 = 1` (Phôi này là NG)**:
      1. Bật Stopper chặn hàng: `SET Y21`
      2. Bật còi đèn tháp báo động: `SET Y22`
      3. Bật cờ báo lên màn hình HMI/SCADA: `SET M215`
      4. Dừng băng tải chính nếu bật tùy chọn dừng: `RST Y0` (khi `M120=1`)
    - Nếu **`M70 = 0` (Phôi này là OK)**:
      Stopper `Y21` KHÔNG bật, còi đèn KHÔNG kêu, phôi OK trôi qua bình thường!
  - **TRONG SUỐT QUÁ TRÌNH PHÔI DI CHUYỂN, CÁC PHÔI PHÍA SAU VẪN ĐƯỢC CHỤP VÀ KIỂM TRA BÌNH THƯỜNG TRONG BUỒNG.**

```text
|   X3 (Sensor trạm ngoài)   M70 (Phôi đến trạm là NG)
|-------[ ]------------------------[ ]----------------------+----( SET Y21 )  (Bật Stopper chặn hàng NG)
|                                                           |
|                                                           +----( SET Y22 )  (Bật còi đèn tháp báo NG)
|                                                           |
|                                                           +----( SET M215 ) (Cờ HMI báo NG dừng ngoài trạm)
|                                                           |
|                                       M120 (Tùy chọn dừng)|
|                                      -------[ ]-----------+----( RST Y0 )   (Dừng băng tải chính Y0)
```

---

### 🟢 MẠNG 7: NÚT NHẤN XÁC NHẬN ĐÃ LẤY HÀNG NG RA KHỎI TRẠM NGOÀI BUỒNG
* **Ý nghĩa**: Sau khi hàng NG bị Stopper giữ lại bên ngoài buồng, công nhân nhấc sản phẩm NG ra khỏi vị trí và nhấn nút xác nhận `X4` (hoặc nút bấm trên màn hình HMI `M211`):
  1. Hạ Stopper: `RST Y21`
  2. Tắt còi đèn báo động: `RST Y22`
  3. Xóa cờ báo trạng thái HMI: `RST M215`
  4. Xóa cờ NG khỏi trạm: `RST M70`
  5. Khởi động lại băng tải: `SET Y0`

```text
|   X4 (Nút nhấn công nhân)     Y21 (Stopper đang kích hoạt)
|-------[ ↑ ]----------------------------[ ]----------------+----( RST Y21 )  (Hạ Stopper)
|   M211 (Nút HMI Reset)                                    |
|-------[ ↑ ]----------------------------+                  +----( RST Y22 )  (Tắt còi đèn)
                                                            |
                                                            +----( RST M215 ) (Xóa cờ HMI)
                                                            |
                                                            +----( RST M70 )  (Xóa cờ NG khỏi trạm ngoài)
                                                            |
                                                            +----( SET Y0 )   (Chạy lại băng tải Y0)
```

---

### 🟢 MẠNG 8: CỘNG DỒN BỘ ĐẾM SẢN LƯỢNG 32-BIT (TOTAL, PASS, NG)
* **Ý nghĩa**: Dùng lệnh cộng Double Word (`DADD`) chuẩn FX5U để đếm chính xác sản lượng:
  - `D300`: Tổng số sản phẩm kiểm tra (`Total_Inspected_Count`)
  - `D302`: Tổng số sản phẩm Đạt (`Total_Pass_Count`)
  - `D304`: Tổng số sản phẩm Lỗi (`Total_NG_Count`)

```text
Network 8.1: Cộng tổng kiểm tra mỗi khi Vision_Done bật
|   M103 (Vision Done)
|-------[ ↑ ]-----------------------------------------------[ DADD D300 K1 D300 ]

Network 8.2: Cộng tổng hàng Đạt (Pass)
|   M104 (Vision Pass)
|-------[ ↑ ]-----------------------------------------------[ DADD D302 K1 D302 ]

Network 8.3: Cộng tổng hàng Lỗi (NG)
|   M105 (Vision NG)
|-------[ ↑ ]-----------------------------------------------[ DADD D304 K1 D304 ]

Network 8.4: Nút Reset bộ đếm sản lượng (Nút X7 hoặc HMI M212)
|   X7 (Nút Reset bộ đếm)
|-------[ ↑ ]-----------------------------------------------+-[ DMOV K0 D300 ] (Reset Total = 0)
|   M212 (HMI Reset Counter)                                |
|-------[ ↑ ]-----------------------------------------------+-[ DMOV K0 D302 ] (Reset Pass = 0)
                                                            |
                                                            +-[ DMOV K0 D304 ] (Reset NG = 0)
```

---

### 🟢 MẠNG 9: ĐIỀU KHIỂN BĂNG TẢI CHÍNH TỰ ĐỘNG & BẢO VỆ LIÊN ĐỘNG
* **Ý nghĩa**: Băng tải chính `Y0` chạy tự động khi:
  - Rơ-le an toàn liên động đóng (`Y10 = 1`).
  - Không có sự cố dừng khẩn cấp E-Stop (`X10 = 1`).
  - Không có hàng NG đang bị kẹt chờ gỡ ngoài buồng (`M215 = 0`).

```text
|   Y10 (An toàn Watchdog)   X10 (Nút E-Stop NC)   M215 (Không có NG chờ gỡ)    Y0 (Băng tải chính)
|-------[ ]------------------------[ ]-----------------------[/]---------------------( )---|
```

---

## TỔNG KẾT BẢNG ÁNH XẠ THIẾT BỊ (FX5U I/O & MEMORY MAP)

| Thiết bị | Loại | Chức năng chi tiết |
| :--- | :--- | :--- |
| **X2** | Digital Input | Cảm biến phát hiện phôi vào vị trí chụp ảnh trong buồng kiểm tra |
| **X3** | Digital Input | Cảm biến vị trí chỉ định trạm dừng ngoài buồng kiểm tra |
| **X4** | Digital Input | Nút nhấn xác nhận công nhân đã lấy sản phẩm NG ra khỏi cữ chặn ngoài buồng |
| **X7** | Digital Input | Nút xóa bộ đếm sản lượng phần cứng |
| **X10** | Digital Input | Nút dừng khẩn cấp phần cứng toàn dây chuyền (Emergency Stop - NC) |
| **Y0** | Digital Output | Động cơ băng tải chính (Conveyor Run/Stop) |
| **Y10** | Digital Output | Rơ-le an toàn liên động chuyền (Line Interlock E-Stop) |
| **Y20** | Digital Output | Van điện từ Xylanh gạt / thổi khí loại bỏ hàng NG |
| **Y21** | Digital Output | Xylanh Stopper chặn hàng NG tại trạm chỉ định ngoài buồng |
| **Y22** | Digital Output | Còi báo động & Đèn tháp cảnh báo hàng NG tại trạm ngoài |
| **M10** | Internal Bit | Xung kích hoạt camera Vision chụp ảnh (PLC_Trigger, gửi sang PC) |
| **M11** | Internal Bit | Xung xác nhận đã nhận kết quả (PLC_Ack, gửi sang PC) |
| **M20** | Internal Bit | Xung chốt kích hoạt dịch hàng đợi In-Flight FIFO (`BSFRP`) |
| **M30** | Internal Bit | Xung chốt kết quả NG vào nấc `M85` tại buồng |
| **M31** | Internal Bit | Xung chốt kết quả Pass vào nấc `M85` tại buồng |
| **M70** | Shift Bit | **Trạng thái phôi khi đến trạm ngoài buồng** (1 = NG, 0 = OK) |
| **M71..M84**| Shift Bits | Trạng thái các phôi đang di chuyển trên băng chuyền từ buồng ra ngoài |
| **M85** | Shift Bit | **Trạng thái phôi tại buồng kiểm tra** (1 = NG, 0 = OK) |
| **M100** | MC Protocol Bit | Nhịp tim Vision PC gửi xuống PLC (Vision_Heartbeat) |
| **M101** | MC Protocol Bit | Vision PC sẵn sàng nhận Trigger (Vision_Ready) |
| **M102** | MC Protocol Bit | Vision PC đang bận chụp & tính toán (Vision_Busy) |
| **M103** | MC Protocol Bit | Vision PC hoàn thành kiểm tra (Vision_Done) |
| **M104** | MC Protocol Bit | Kết quả kiểm tra ĐẠT (Vision_Pass / OK) |
| **M105** | MC Protocol Bit | Kết quả kiểm tra LỖI (Vision_NG / Fail) |
| **M108** | Internal Bit | Nhịp tim PLC phát sang Vision PC chu kỳ 100ms (tạo từ SM410) |
| **M120** | HMI / Parameter | Tùy chọn: 1 = Dừng băng tải khi có hàng NG đến trạm ngoài; 0 = Không dừng băng tải (chỉ bật Stopper) |
| **M202** | Internal Bit | Cờ báo lỗi truyền thông Vision (Vision_Fault_Alarm) |
| **M215** | Internal Bit | Cờ trạng thái HMI: Có hàng NG đang dừng chờ công nhân gỡ ngoài buồng |
| **T0** | Timer (100ms) | Timer Watchdog giám sát nhịp tim PC (K3 = 300ms) |
| **D300..D301** | 32-bit DINT | Tổng số sản phẩm đã kiểm tra (`Total_Inspected_Count`) |
| **D302..D303** | 32-bit DINT | Tổng số sản phẩm Đạt (`Total_Pass_Count`) |
| **D304..D305** | 32-bit DINT | Tổng số sản phẩm Lỗi (`Total_NG_Count`) |
