# CHƯƠNG TRÌNH LADDER DIAGRAM ĐẦY ĐỦ — MITSUBISHI FX5U (GX WORKS 3)
## DỰ ÁN: BẮT TAY BẤT ĐỒNG BỘ NON-BLOCKING & HÀNG ĐỢI 20 PHÔI DỪNG ĐÚNG ĐIỂM RA

> **Môi trường lập trình**: MELSOFT GX Works 3 (Ngôn ngữ: **Ladder**)  
> **Dòng PLC áp dụng**: Mitsubishi MELSEC iQ-F (**FX5U** / FX5UC / FX5S / FX5UJ) & iQ-R Series  
> **Lệnh dịch hàng đợi**: **`BSFLP`** (Bit Shift Left Pulse) quản lý mảng 20 phôi `M200..M219`  
> **Giao thức kết nối**: Ethernet MC Protocol / SLMP (3E Frame, Port 5000 / 5002)  
> **Triết lý vận hành**: **Non-blocking (Bất đồng bộ)** — Băng tải chạy liên tục tốc độ cao, không chờ đợi nhau, phôi NG tới đúng điểm ra chỉ định mới dừng băng tải.

---

## ❓ VÌ SAO TRƯỚC ĐÂY CHẠY TEST "KHÔNG THẤY M85 -> M70"?

Trong phiên bản cũ dùng lệnh `BSFRP M70 K16` (Dịch phải):
1. **Thời điểm dịch và thời điểm trả kết quả bị lệch**:
   - Khi có phôi chạm cảm biến buồng chụp `X2`, PLC bắn xung `M20` kích lệnh `BSFRP` dịch mảng **ngay lập tức** (lúc này Vision PC mới bắt đầu chụp và chưa có kết quả, nên bit buồng `M85` vẫn là 0).
   - Khoảng 50ms sau, Vision PC kiểm tra xong và trả kết quả NG $\rightarrow$ PLC bật `SET M85 = 1`.
   - **Vấn đề cốt lõi khi test**: Phôi lúc này đã chạy qua cảm biến `X2` rồi! Nếu bạn chỉ test 1 phôi duy nhất hoặc test thủ công mà không có phôi thứ 2, thứ 3... thứ 16 đi vào kích `X2`, thì lệnh dịch `BSFRP` **KHÔNG BAO GIỜ CHẠY THÊM LẦN NÀO NỮA**. Do đó, bit 1 đứng yên tại `M85` và bạn **không bao giờ thấy dịch tới `M70`**!
2. **Hướng dịch ngược tự nhiên & Bị cố định trạm ra**:
   - Lệnh `BSFR` (dịch phải) dịch từ bit cao về bit thấp (`M85 -> M84 -> ... -> M70`), cố định cứng trạm ra tại `M70` (nấc thứ 16). Nếu bạn muốn trạm dừng ở nấc 5 hay nấc 10 thì không tùy biến được.
3. **Giải pháp chuẩn hóa mới**:
   - Dùng lệnh **`BSFLP M200 K20`** (Dịch trái thuận tự nhiên):
     - Nấc 0 (`M200`): Vị trí buồng chụp camera.
     - Nấc 1..19 (`M201..M219`): Các nấc phôi di chuyển trên băng tải.
     - Điểm ra chỉ định linh hoạt (ví dụ: Nấc 10 = `M210`, hoặc Nấc 5 = `M205`).

---

## 🚀 NGUYÊN LÝ BẮT TAY NON-BLOCKING (TỐC ĐỘ CAO KHÔNG CHỜ ACK)

```text
  [Băng tải Y0 chạy] ──> [Sensor X2 phát hiện] ──> [Trigger Camera chụp]
                               │
                               ├──> Kích [BSFLP M200 K20] (Dịch mảng lên 1 nấc)
                               │    (Bit M200 tự động nạp 0 = Mặc định OK)
                               │
                               v
                    [Camera gửi ảnh về WPF]
                               │
                               v
                    [WPF kiểm tra siêu tốc]
                               │
                 ┌─────────────┴─────────────┐
                 │                           │
          [Nếu OK (Pass)]             [Nếu LỖI (NG)]
                 │                           │
        (M200 giữ nguyên 0)          WPF ghi M200 = 1
        Không cần chờ Ack!           Không cần chờ Ack!
                 │                           │
                 └─────────────┬─────────────┘
                               │
            Băng tải tiếp tục chạy, phôi tiếp tục vào
            Bit NG (1) dịch dần từ M200 -> M201 -> M202...
                               │
                               v
            [Phôi NG tới đúng Điểm Ra Chỉ Định]
            (Ví dụ: Nấc 10 -> Bit M210 = 1)
                               │
                               ├──> DỪNG BĂNG TẢI (RST Y0)
                               ├──> Bật Stopper chặn hàng (SET Y21)
                               └──> Bật còi đèn tháp (SET Y22)
                               │
                               v
            [Công nhân bốc hàng NG / Sửa xong]
            [Nhấn nút Reset/Start (X4)]
                               │
                               ├──> Reset bit tại điểm ra về 0 (RST M210)
                               ├──> Hạ Stopper (RST Y21), Tắt còi đèn (RST Y22)
                               └──> BĂNG TẢI CHẠY TIẾP (SET Y0)
                                    (Các phôi khác trong hàng đợi vẫn giữ nguyên!)
```

---

## BẢNG ÁNH XẠ THIẾT BỊ HÀNG ĐỢI 20 NẤC & I/O FX5U

| Thiết bị | Kiểu | Mô tả chức năng |
| :--- | :--- | :--- |
| **X0** | Input | Nút nhấn Chạy hệ thống (System Start) |
| **X1** | Input | Nút nhấn Dừng hệ thống (System Stop) |
| **X2** | Input | Cảm biến phát hiện phôi vào buồng chụp (Sensor Trigger) |
| **X4** | Input | Nút nhấn **Reset / Start** của công nhân (Khởi động lại sau khi xử lý NG) |
| **Y0** | Output | **Động cơ Băng tải chính** (1 = Chạy, 0 = Dừng) |
| **Y1** | Output | Xung kích Trigger Camera qua chân vật lý LINE0 |
| **Y21** | Output | Xi lanh Stopper chặn hàng NG tại trạm dừng ngoài buồng |
| **Y22** | Output | Còi đèn tháp cảnh báo dừng chuyền do NG |
| **M10** | Bit | Xung kích Trigger Vision PC (Software Trigger qua mạng) |
| **M20** | Bit | Xung kích dịch hàng đợi `BSFLP M200 K20` |
| **M101** | Bit | Vision Ready (PC sẵn sàng kiểm tra) |
| **M102** | Bit | Vision Busy (PC đang chụp & đo) |
| **M103** | Bit | Vision Done (PC đo xong một ảnh) |
| **M104** | Bit | Vision Pass (Kết quả ĐẠT) |
| **M105** | Bit | Vision NG (Kết quả LỖI) |
| **M200** | Bit | **Hàng đợi Nấc 0 (Buồng chụp camera)**: 0 = OK, 1 = NG |
| **M201** | Bit | Hàng đợi Nấc 1 (Phôi thứ 1 sau buồng chụp) |
| **M202** | Bit | Hàng đợi Nấc 2 (Phôi thứ 2 sau buồng chụp) |
| **...** | Bit | ... |
| **M205** | Bit | Hàng đợi Nấc 5 (Tùy chọn trạm dừng nấc 5) |
| **...** | Bit | ... |
| **M210** | Bit | **Hàng đợi Nấc 10 (Điểm ra chỉ định mặc định)** |
| **...** | Bit | ... |
| **M219** | Bit | Hàng đợi Nấc 19 (Nấc thứ 20 - cuối hàng đợi) |
| **M220** | Bit | Cờ trạng thái: Băng tải đang dừng do phát hiện hàng NG tại điểm ra |

---

## CHI TIẾT 7 MẠNG LADDER CHO GX WORKS 3 (FX5U)

### 🟢 MẠNG 1: ĐIỀU KHIỂN KHỞI ĐỘNG VÀ DỪNG BĂNG TẢI CHÍNH
* **Ý nghĩa**: Băng tải chạy khi nhấn Start `X0` hoặc nhấn Reset/Start `X4` của công nhân (với điều kiện không có lỗi dừng do NG `M220 = 0`). Nhấn Stop `X1` thì dừng băng tải.

```text
|   X0 (Nút Start)                                      M220 (Không có cờ dừng NG)   Y0 (Băng tải chạy)
|-------[ ↑ ]--------------------+--------------------------------[/]------------------( SET Y0 )----|
|   X4 (Nút Reset/Start công nhân)|
|-------[ ↑ ]--------------------+
|
|   X1 (Nút Stop)                                                                    Y0 (Dừng băng tải)
|-------[ ↑ ]--------------------------------------------------------------------------( RST Y0 )----|
```

---

### 🟢 MẠNG 2: NHỊP TIM WATCHDOG AN TOÀN (HEARTBEAT 100ms)
* **Ý nghĩa**: PLC xuất nhịp tim sang `M108` bằng xung đồng hồ `SM410` (100ms).

```text
|   SM410 (Clock 100ms)                                                              M108 (PLC_Heartbeat)
|-------[ ]----------------------------------------------------------------------------( )-----------|
```

---

### 🟢 MẠNG 3: CẢM BIẾN BUỒNG CHỤP X2 TRIGGER CAMERA
* **Ý nghĩa**: Khi phôi chạm `X2`:
  1. Kích ngõ ra `Y1` (Hardware Line0) bắn xung phần cứng kích Camera chụp ảnh tức thì (< 5µs).
  2. Kích `PLS M10` (Software Trigger qua MC Protocol).
  3. **LƯU Ý**: Tại bước này **CHƯA DỊCH HÀNG ĐỢI**, vì Camera vừa bắt đầu chụp và Vision PC đang xử lý, chưa có kết quả OK/NG!

```text
|   X2 (Sensor buồng chụp)   Y0 (Băng tải đang chạy)
|-------[ ↑ ]--------------------------[ ]-------------------+----( PLS M10 ) (Xung Trigger PC)
|                                                            |
|                                                            +----( PLS Y1 )  (Xung chân cứng LINE0)
```

---

### 🟢 MẠNG 4: NHẬN KẾT QUẢ TỪ VISION PC (M103 DONE) ➔ DỊCH HÀNG ĐỢI & NẠP KẾT QUẢ VÀO M200
* **Ý nghĩa**:
  - Khi Vision PC kiểm tra xong ảnh (mất ~15ms - 30ms):
    1. Vision PC phát xung **`M103` (Vision Done = 1)** báo hiệu đã có kết quả.
    2. Đồng thời nếu là **NG**: PC bật **`M105 = 1`** (nếu OK: `M104 = 1`, `M105 = 0`).
  - **Logic trong PLC**:
    - Ngay khi nhận xung hoàn tất `LDP M103`: PLC thực hiện lệnh **`[ BSFLP M200 K20 ]`** để dịch toàn bộ các phôi cũ trên băng tải tiến lên 1 nấc (`M200` $\rightarrow$ `M201`, `M201` $\rightarrow$ `M202`... và nấc `M200` tạm thời mang giá trị 0).
    - Ngay sau đó, nếu kết quả là **NG (`M105 = 1`)**: PLC lập tức kích **`SET M200`** (Đánh dấu chính xác phôi vừa kiểm tra tại buồng mang giá trị NG = 1). Nếu là OK (`M105 = 0`), `M200` giữ nguyên giá trị `0`.
  - **Ưu điểm vượt trội**:
    - Luôn đồng bộ 100% giữa phôi thật và kết quả kiểm tra: Phôi kiểm tra xong lúc nào thì kết quả được đẩy vào hàng đợi đúng lúc đó!
    - Không lo Vision xử lý chậm làm trôi phôi.
    - Test 1 phôi đơn lẻ hay chạy cả nghìn phôi liên tục đều dịch bit chuẩn xác từng nấc!

```text
Network 4.1: Khi Vision PC hoàn tất kiểm tra (LDP M103) -> Dịch mảng hàng đợi 20 nấc BSFLP M200 K20
|   M103 (Vision Done từ PC)
|-------[ ↑ ]------------------------------------------------[ BSFLP M200 K20 ] (Dịch M200 -> M219)

Network 4.2: Nếu kết quả kiểm tra là NG (M105 = 1) -> Nạp nhãn lỗi vào nấc đầu buồng chụp (SET M200)
|   M103 (Vision Done)       M105 (Vision NG)
|-------[ ]------------------------[ ]-----------------------( SET M200 )
```

---

### 🟢 MẠNG 5: PHÁT HIỆN PHÔI NG TẠI ĐIỂM RA CHỈ ĐỊNH ➔ DỪNG BĂNG TẢI NGAY
* **Ý nghĩa**:
  - Khi các phôi di chuyển trên băng tải, mỗi nhịp phôi qua `X2` làm mảng dịch lên 1 nấc: `M200` $\rightarrow$ `M201` $\rightarrow$ `M202` ... $\rightarrow$ `M210`.
  - Khi bit tại **Điểm Ra Chỉ Định** (ở đây ví dụ là **Nấc 10 = `M210`**) mang giá trị `1` (tức là phôi lỗi vừa đi tới đúng trạm dừng ngoài buồng):
    1. **DỪNG BĂNG TẢI NGAY LẬP TỨC**: `RST Y0`
    2. **BẬT STOPPER CHẶN HÀNG LỖI**: `SET Y21`
    3. **BẬT CÒI ĐÈN THÁP CẢNH BÁO**: `SET Y22`
    4. **BẬT CỜ BÁO DỪNG CHUYỀN**: `SET M220`

```text
|   M210 (Phôi NG tới đúng Điểm Ra Nấc 10)
|-------[ ]--------------------------------------------------+----( RST Y0 )   (Dừng băng tải ngay)
|                                                            |
|                                                            +----( SET Y21 )  (Bật Stopper chặn hàng)
|                                                            |
|                                                            +----( SET Y22 )  (Bật còi đèn tháp)
|                                                            |
|                                                            +----( SET M220 ) (Cờ báo dừng chuyền do NG)
```

> 💡 *Ghi chú*: Nếu trạm dừng của bạn đặt ở nấc khác (ví dụ Nấc 5): bạn chỉ cần đổi tiếp điểm `M210` thành `M205`!

---

### 🟢 MẠNG 6: CÔNG NHÂN XỬ LÝ HÀNG LỖI XONG ➔ NHẤN RESET / START (X4)
* **Ý nghĩa**:
  - Khi hàng lỗi bị dừng lại ở trạm, công nhân nhặt sản phẩm lỗi ra (hoặc dán tem NG / sửa xong).
  - Công nhân nhấn nút **Reset / Start** (`X4`):
    1. **Reset bit NG tại điểm dừng về 0**: `RST M210` (Hàng lỗi đã được lấy ra).
    2. **Hạ Stopper**: `RST Y21`.
    3. **Tắt còi đèn**: `RST Y22`.
    4. **Xóa cờ dừng chuyền**: `RST M220`.
    5. **KHỞI ĐỘNG LẠI BĂNG TẢI CHẠY TIẾP**: `SET Y0`.
  - **CỰC KỲ AN TOÀN & LIÊN TỤC**: Toàn bộ các phôi khác đang trên đường đi (ở các nấc `M201`, `M202`...) vẫn được bảo lưu 100% trong hàng đợi, khi đến điểm ra chúng vẫn sẽ được dừng nếu là hàng NG!

```text
|   X4 (Nút Reset/Start công nhân)   M220 (Đang có sự cố dừng NG)
|-------[ ↑ ]---------------------------------[ ]------------+----( RST M210 ) (Xóa cờ NG tại điểm ra về 0)
|                                                            |
|                                                            +----( RST Y21 )  (Hạ Stopper)
|                                                            |
|                                                            +----( RST Y22 )  (Tắt còi đèn)
|                                                            |
|                                                            +----( RST M220 ) (Xóa cờ dừng chuyền)
|                                                            |
|                                                            +----( SET Y0 )   (Băng tải chạy tiếp tục)
```

---

### 🟢 MẠNG 7: CỘNG DỒN BỘ ĐẾM SẢN LƯỢNG 32-BIT
* **Ý nghĩa**: Dùng lệnh `DADD` cộng dồn tổng sản lượng kiểm tra:
  - `D300`: Tổng số sản phẩm kiểm tra (`Total_Inspected`)
  - `D302`: Tổng số sản phẩm Đạt (`Total_Pass`)
  - `D304`: Tổng số sản phẩm Lỗi (`Total_NG`)

```text
Network 7.1: Đếm tổng khi có phôi vào buồng
|   M20 (Xung phôi mới vào buồng)
|-------[ ↑ ]------------------------------------------------[ DADD D300 K1 D300 ]

Network 7.2: Đếm số lượng NG
|   M105 (Vision NG)
|-------[ ↑ ]------------------------------------------------[ DADD D304 K1 D304 ]
```

---

## CÁCH TEST THỰC TẾ TRÊN GX WORKS 3 (SIMULATOR HOẶC PLC THẬT)

1. **Khởi động**: Bật `X0` (hoặc `X4`) $\rightarrow$ Băng tải `Y0` bật ON (motor quay).
2. **Phôi 1 vào buồng (Giả sử NG)**:
   - Kích xung `X2`: `BSFLP M200 K20` chạy. `M200` nạp 0.
   - Vision PC kiểm tra thấy NG $\rightarrow$ Ghi `M105 = 1` $\rightarrow$ `M200 = 1`.
3. **Phôi 2 vào buồng (Giả sử OK)**:
   - Kích xung `X2`: `BSFLP M200 K20` chạy $\rightarrow$ Bit 1 từ `M200` dịch sang `M201`! `M200` trở về 0.
   - Bạn sẽ nhìn thấy ngay trên màn hình Monitor GX Works 3: **Bit 1 bắt đầu di chuyển từ `M200` sang `M201`**!
4. **Các phôi tiếp theo lần lượt qua `X2`**:
   - Nhấn `X2` lần 3: Bit 1 dịch sang `M202`.
   - ...
   - Nhấn `X2` lần thứ 11: Bit 1 dịch đến **`M210`** (Điểm ra chỉ định Nấc 10)!
5. **Dừng băng tải**:
   - Ngay lập tức khi `M210 = 1`: `Y0` tắt OFF (Băng tải dừng), `Y21` ON (Stopper bật), `Y22` ON (Còi đèn kêu).
6. **Công nhân xử lý xong**:
   - Nhấn `X4`: `M210` bị xóa về 0, `Y21` hạ, `Y22` tắt, và `Y0` tự động bật ON chạy tiếp!
