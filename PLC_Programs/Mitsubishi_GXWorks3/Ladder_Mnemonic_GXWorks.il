; ==============================================================================
; MITSUBISHI PLC LADDER INSTRUCTION LIST (IL / MNEMONIC CODE)
; PLATFORM : MITSUBISHI MELSEC iQ-F (FX5U / FX5UC) / iQ-R / Q SERIES
; SOFTWARE : MELSOFT GX WORKS 3 / GX WORKS 2
; PROJECT  : VISION 2026 INDUSTRIAL INSPECTION INTERFACE
; LỆNH DỊCH BIT : BSFRP M70 K16 (CHUẨN 100% FX5U THAY THẾ CHO SFT CŨ)
; ==============================================================================

; ------------------------------------------------------------------------------
; NETWORK 1: TẠO NHỊP TIM PLC ĐẢO 100ms (PLC_HEARTBEAT M108 SANG PC)
; ------------------------------------------------------------------------------
LD      SM410           ; Cờ xung clock 100ms phần cứng của FX5U
OUT     M108            ; Xuất nhịp tim liên tục sang Vision PC qua MC Protocol

; ------------------------------------------------------------------------------
; NETWORK 2: WATCHDOG GIÁM SÁT NHỊP TIM VISION PC (TIMEOUT 300ms)
; ------------------------------------------------------------------------------
; Bắt cạnh lên nhịp tim Vision PC (Y0 hoặc M100)
LDP     Y0              
PLS     M205            ; Xung cạnh lên

; Bắt cạnh xuống nhịp tim Vision PC
LDF     Y0              
PLS     M206            ; Xung cạnh xuống

; Gom xung đảo bit để reset Watchdog
LD      M205
OR      M206
OUT     M201            ; M201: Xung báo Vision PC vừa đảo nhịp tim

; Timer Watchdog T0 (300ms = K3 x 100ms)
LD      SM400           ; Cờ luôn ON
ANI     M201            ; Khi không có xung đảo nhịp tim
OUT     T0      K3      ; Timer T0 đếm 300ms

; Xử lý khi mất nhịp tim Vision quá 300ms
LD      T0
SET     M202            ; Bật cờ lỗi Vision_Fault_Alarm (M202)
RST     Y10             ; Ngắt rơ-le an toàn liên động chuyền (Line E-Stop)

; Xử lý khi nhịp tim phục hồi
LD      M201
RST     M202            ; Xóa cờ lỗi
SET     Y10             ; Cho phép đóng liên động chạy chuyền

; ------------------------------------------------------------------------------
; NETWORK 3: CẢM BIẾN PHÔI BUỒNG CHỤP & BẮN XUNG TRIGGER (HANDSHAKE START)
; ------------------------------------------------------------------------------
LDP     X2              ; Cảm biến phôi vào buồng chụp (cạnh lên)
AND     M101            ; Điều kiện 1: Vision sẵn sàng (Vision_Ready)
ANI     M102            ; Điều kiện 2: Vision không bận (Vision_Busy = 0)
ANI     M202            ; Điều kiện 3: Không có lỗi Watchdog
PLS     M10             ; Bắn xung kích camera chụp (PLC_Trigger M10)
PLS     M20             ; Bắn xung kích dịch hàng đợi In-Flight FIFO

; ------------------------------------------------------------------------------
; NETWORK 4: NHẬN KẾT QUẢ TỪ PC & BẬT XÁC NHẬN CHỐT BẮT TAY (PLC ACK)
; ------------------------------------------------------------------------------
; Khi Vision hoàn thành (M103=1) -> Bật cờ PLC Ack (M11)
LD      M103
OUT     M11             ; Gửi tín hiệu xác nhận PLC_Ack cho Vision PC

; Nếu kết quả là NG (M105=1) -> Chốt cờ NG vào nấc phôi M85 tại buồng
LD      M103
AND     M105
PLS     M30             ; Xung chốt kết quả NG

; Nếu kết quả là PASS (M104=1) -> Chốt cờ PASS vào nấc phôi M85 tại buồng
LD      M103
AND     M104
PLS     M31             ; Xung chốt kết quả PASS

; Khi Vision nhận được Ack và hạ cờ Done (M103=0) -> PLC tự động hạ Ack (M11=0)
LDI     M103
RST     M11

; ------------------------------------------------------------------------------
; NETWORK 5: QUẢN LÝ HÀNG ĐỢI NỐI TIẾP BẰNG LỆNH BSFRP CHUẨN FX5U
; ------------------------------------------------------------------------------
; Khi có phôi mới vào buồng -> Dịch 16 bit từ M85 về M70 bằng lệnh BSFRP
LD      M20
BSFRP   M70     K16     ; Dịch phải 16 bit: M85 -> M84 -> ... -> M70

; Ghi nhận kết quả NG vào nấc M85 tại buồng
LD      M30
SET     M85             ; M85 = 1: Đánh dấu phôi vừa kiểm tra tại buồng là NG

; Ghi nhận kết quả OK vào nấc M85 tại buồng
LD      M31
RST     M85             ; M85 = 0: Đánh dấu phôi vừa kiểm tra tại buồng là OK

; ------------------------------------------------------------------------------
; NETWORK 6: DỪNG ĐÚNG PHÔI NG TẠI TRẠM CHỈ ĐỊNH Ở NGOÀI BUỒNG KIỂM TRA
; ------------------------------------------------------------------------------
; Khi phôi chạm cảm biến ngoài buồng X3 và phôi đó mang cờ NG (M70=1)
LD      X3              ; Cảm biến trạm chỉ định ngoài buồng
AND     M70             ; Kiểm tra nếu phôi đang đến là hàng NG (nấc M70)
SET     Y21             ; Bật Stopper chặn giữ hàng NG lại
SET     Y22             ; Bật còi đèn tháp cảnh báo hàng NG
SET     M215            ; Bật cờ HMI: Có hàng NG đang dừng chờ công nhân gỡ

; Dừng băng tải chính nếu bật tùy chọn M120
LD      X3
AND     M70
AND     M120            ; Tùy chọn dừng băng tải khi có hàng NG ngoài buồng
RST     Y0              ; Dừng động cơ băng tải chính Y0

; ------------------------------------------------------------------------------
; NETWORK 7: NÚT NHẤN XÁC NHẬN ĐÃ LẤY HÀNG NG RA KHỎI TRẠM NGOÀI BUỒNG
; ------------------------------------------------------------------------------
; Công nhân lấy hàng NG ra và nhấn nút X4 (hoặc nút bấm trên màn hình HMI M211)
LDP     X4              ; Nút nhấn xác nhận phần cứng của công nhân
ORP     M211            ; Hoặc nút Reset trên màn hình HMI
AND     Y21             ; Điều kiện an toàn: Stopper đang được kích hoạt
RST     Y21             ; Hạ Stopper giải phóng trạm
RST     Y22             ; Tắt còi đèn cảnh báo
RST     M215            ; Xóa cờ trạng thái HMI
RST     M70             ; Xóa cờ phôi NG khỏi trạm
SET     Y0              ; Bật lại băng tải chính tiếp tục vận hành tự động

; ------------------------------------------------------------------------------
; NETWORK 8: CỘNG DỒN BỘ ĐẾM SẢN LƯỢNG 32-BIT (TOTAL, PASS, NG)
; ------------------------------------------------------------------------------
; Cộng tổng số sản phẩm kiểm tra
LDP     M103            ; Mỗi khi Vision_Done bật
DADD    D300    K1      D300    ; D300 = D300 + 1 (32-bit)

; Cộng tổng số sản phẩm Đạt (Pass / OK)
LDP     M104            ; Mỗi khi Vision_Pass bật
DADD    D302    K1      D302    ; D302 = D302 + 1 (32-bit)

; Cộng tổng số sản phẩm Lỗi (NG / Fail)
LDP     M105            ; Mỗi khi Vision_NG bật
DADD    D304    K1      D304    ; D304 = D304 + 1 (32-bit)

; Nút xóa bộ đếm sản lượng (Nút X7 hoặc HMI M212)
LDP     X7
ORP     M212
DMOV    K0      D300    ; Reset Total = 0
DMOV    K0      D302    ; Reset Pass = 0
DMOV    K0      D304    ; Reset NG = 0

; ------------------------------------------------------------------------------
; NETWORK 9: ĐIỀU KHIỂN BĂNG TẢI CHÍNH TỰ ĐỘNG & BẢO VỆ LIÊN ĐỘNG
; ------------------------------------------------------------------------------
LD      Y10             ; Rơ-le an toàn Watchdog PC đang đóng (OK)
AND     X10             ; Nút dừng khẩn cấp E-Stop (tiếp điểm thường đóng NC)
ANI     M215            ; Không có hàng NG đang kẹt chờ gỡ ngoài buồng
OUT     Y0              ; Băng tải chính chạy tự động

; ------------------------------------------------------------------------------
; KẾT THÚC CHƯƠNG TRÌNH
; ------------------------------------------------------------------------------
END
