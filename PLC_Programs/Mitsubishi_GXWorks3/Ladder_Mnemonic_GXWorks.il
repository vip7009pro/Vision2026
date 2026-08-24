; ==============================================================================
; MITSUBISHI PLC LADDER INSTRUCTION LIST (IL / MNEMONIC CODE)
; COMPATIBLE WITH: GX WORKS 3 / GX WORKS 2 / GX DEVELOPER / FX5U / Q / L / FX3U
; PROJECT        : VISION 2026 INDUSTRIAL INSPECTION INTERFACE
; ==============================================================================

; ------------------------------------------------------------------------------
; NETWORK 1: TẠO NHỊP TIM PLC HEARTBEAT (100ms ĐẢO 1 LẦN)
; ------------------------------------------------------------------------------
LD      M8000           ; Cờ luôn ON
ANI     T1              ; Khi Timer T1 chưa tràn
OUT     T1      K1      ; Timer T1 đếm 100ms (K1 = 100ms với timer chuẩn)
LD      T1              ; Khi T1 đếm xong 100ms
ALT     X0              ; Đảo trạng thái bit PLC_Heartbeat (X0 hoặc M201)

; ------------------------------------------------------------------------------
; NETWORK 2: WATCHDOG GIÁM SÁT NHỊP TIM VISION PC (TIMEOUT 300ms)
; ------------------------------------------------------------------------------
LD      Y0              ; Đọc bit Vision_Heartbeat (Y0 hoặc M200)
PLS     M205            ; Bắt xung cạnh lên
LDF     Y0              ; Đọc cạnh xuống
PLS     M206            ; Bắt xung cạnh xuống

LD      M8000           ; Luôn ON
ANI     M205            ; Khi không có xung cạnh lên
ANI     M206            ; Và không có xung cạnh xuống
OUT     T0      K3      ; Timer T0 đếm 300ms không có nhịp tim

LD      T0              ; Nếu T0 đếm đến 300ms (Mất nhịp tim Vision)
SET     M202            ; Bật cờ lỗi Vision_Fault_Alarm
RST     Y10             ; Ngắt rơ-le an toàn liên động chuyền (Line E-Stop)

LD      M205            ; Khi có nhịp tim trở lại
OR      M206
RST     M202            ; Xóa cờ lỗi
SET     Y10             ; Cho phép đóng liên động an toàn

; ------------------------------------------------------------------------------
; NETWORK 3: CẢM BIẾN PHÔI VÀ BẮN XUNG TRIGGER CHO VISION (HANDSHAKE START)
; ------------------------------------------------------------------------------
LD      X2              ; Cảm biến phôi vào vị trí chụp (X2)
AND     Y1              ; Điều kiện 1: Vision sẵn sàng (Vision_Ready Y1)
ANI     M202            ; Điều kiện 2: Không có lỗi Watchdog
ANI     Y2              ; Điều kiện 3: Vision không bận (Vision_Busy = 0)
PLS     M10             ; Bắn xung kích camera chụp (PLC_Trigger M10)

; ------------------------------------------------------------------------------
; NETWORK 4: XÁC NHẬN KẾT QUẢ VÀ BẮN XUNG PLC_ACK (HANDSHAKE COMPLETE)
; ------------------------------------------------------------------------------
LD      Y3              ; Khi Vision hoàn thành kiểm tra (Vision_Done Y3)
OUT     X1              ; Bật cờ xác nhận PLC_Ack (X1)
AND     Y5              ; Nếu kết quả là NG (Vision_NG Y5)
PLS     M30             ; Bắn xung M30 nạp vị trí sản phẩm vào Shift Register

LDI     Y3              ; Khi Vision nhận được Ack và hạ cờ Done (Y3 = 0)
RST     X1              ; PLC hạ cờ PLC_Ack (X1 = 0)

; ------------------------------------------------------------------------------
; NETWORK 5: CỘNG DỒN BỘ ĐẾM SẢN LƯỢNG (TOTAL, PASS, NG)
; ------------------------------------------------------------------------------
LD      Y3              ; Khi Vision_Done
PLS     M301
LD      M301
DADD    D300    K1      D300    ; Tăng tổng số kiểm tra D300

LD      Y4              ; Khi Vision_Pass (OK)
PLS     M302
LD      M302
DADD    D302    K1      D302    ; Tăng tổng số OK D302

LD      Y5              ; Khi Vision_NG (Lỗi)
PLS     M303
LD      M303
DADD    D304    K1      D304    ; Tăng tổng số NG D304

; ------------------------------------------------------------------------------
; NETWORK 6: SO SÁNH VỊ TRÍ ENCODER VÀ KÍCH HOẠT XYLANH REJECT (100ms)
; ------------------------------------------------------------------------------
LD>=    D1000   D1010   ; So sánh: Xung Encoder hiện tại >= Vị trí đích sản phẩm lỗi
AND<=   D1000   D1012   ; và <= Vị trí đích + Dung sai
OUT     M40             ; Bật cờ kích hoạt trạm Reject

LD      M40
OUT     Y20             ; Bật van điện từ Xylanh Reject Y20
OUT     T10     K1      ; Timer duy trì 100ms

LD      T10
RST     Y20             ; Ngắt van điện từ sau 100ms
RST     M40

; ------------------------------------------------------------------------------
; END PROGRAM
; ------------------------------------------------------------------------------
END
