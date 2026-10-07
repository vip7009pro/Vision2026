; ==============================================================================
; MITSUBISHI PLC LADDER INSTRUCTION LIST (IL / MNEMONIC CODE)
; PLATFORM : MITSUBISHI MELSEC iQ-F (FX5U / FX5UC) / iQ-R / Q SERIES
; SOFTWARE : MELSOFT GX WORKS 3 / GX WORKS 2
; PROJECT  : VISION 2026 NON-BLOCKING HANDSHAKE & 20-SLOT PIPELINED QUEUE
; LỆNH DỊCH BIT : BSFLP M200 K20 (DỊCH MẢNG 20 NẤC TỪ M200 LÊN M219)
; ==============================================================================

; ------------------------------------------------------------------------------
; NETWORK 1: ĐIỀU KHIỂN KHỞI ĐỘNG VÀ DỪNG BĂNG TẢI CHÍNH
; ------------------------------------------------------------------------------
LDP     X0              ; Nút nhấn Start chạy máy
ORP     X4              ; Hoặc nút Reset/Start của công nhân sau khi xử lý NG
ANI     M220            ; Điều kiện: Không có sự cố dừng băng tải do hàng NG
SET     Y0              ; Bật chạy động cơ băng tải chính

LDP     X1              ; Nút nhấn Stop dừng máy
RST     Y0              ; Dừng động cơ băng tải chính

; ------------------------------------------------------------------------------
; NETWORK 2: TẠO NHỊP TIM PLC ĐẢO 100ms (PLC_HEARTBEAT M108 SANG PC)
; ------------------------------------------------------------------------------
LD      SM410           ; Cờ xung clock 100ms phần cứng của FX5U
OUT     M108            ; Xuất nhịp tim liên tục sang Vision PC qua MC Protocol

; ------------------------------------------------------------------------------
; NETWORK 3: CẢM BIẾN BUỒNG CHỤP X2 TRIGGER CAMERA
; ------------------------------------------------------------------------------
LDP     X2              ; Cảm biến phôi vào buồng chụp (cạnh lên)
AND     Y0              ; Điều kiện: Băng tải đang chạy
PLS     M10             ; Xung kích Trigger Vision PC qua mạng
PLS     Y1              ; Xung kích phần cứng chân LINE0 tới Camera

; ------------------------------------------------------------------------------
; NETWORK 4: NHẬN KẾT QUẢ TỪ VISION PC (M103 DONE) -> DỊCH HÀNG ĐỢI & NẠP M200
; ------------------------------------------------------------------------------
; Khi Vision kiểm tra xong -> Phát xung M103 Done -> Dịch mảng và nạp nhãn NG/OK
LDP     M103            ; Xung cạnh lên Vision Done từ PC (hoàn tất kiểm tra)
BSFLP   M200    K20     ; Dịch mảng 20 nấc: M200 -> M201 -> M202 ... -> M219
                        ; (Nấc M200 tạm thời nhận 0 = Mặc định OK)

; Nếu kết quả kiểm tra là NG (M105 = 1) -> Đánh dấu nấc M200 là 1
LD      M103            ; Trong thời gian xung Done đang tích cực
AND     M105            ; Và kết quả từ PC báo NG
SET     M200            ; Đánh dấu phôi vừa kiểm tra tại buồng (M200) là hàng NG = 1

; ------------------------------------------------------------------------------
; NETWORK 5: PHÁT HIỆN PHÔI NG TẠI ĐIỂM RA CHỈ ĐỊNH (NẤC 10: M210) -> DỪNG BĂNG TẢI
; ------------------------------------------------------------------------------
; Khi bit tại Điểm Ra Chỉ Định (M210) = 1 (Phôi lỗi trôi tới đúng trạm dừng)
LD      M210            ; Bit nấc 10 mang giá trị 1 (Hàng lỗi NG tới trạm)
RST     Y0              ; Dừng băng tải chính ngay lập tức
SET     Y21             ; Kích hoạt xi lanh Stopper chặn hàng lỗi lại
SET     Y22             ; Bật còi đèn tháp cảnh báo dừng chuyền do NG
SET     M220            ; Bật cờ trạng thái: Đang tạm dừng băng tải do có hàng NG

; ------------------------------------------------------------------------------
; NETWORK 6: CÔNG NHÂN XỬ LÝ HÀNG LỖI XONG -> NHẤN RESET / START (X4)
; ------------------------------------------------------------------------------
; Công nhân bốc hàng lỗi ra khỏi cữ chặn và nhấn nút X4
LDP     X4              ; Nút nhấn Reset/Start của công nhân
AND     M220            ; Khi đang có trạng thái dừng do hàng NG
RST     M210            ; Xóa cờ NG tại điểm ra về 0 (đã giải phóng hàng lỗi)
RST     Y21             ; Hạ xi lanh Stopper
RST     Y22             ; Tắt còi đèn tháp
RST     M220            ; Xóa cờ dừng chuyền do NG
SET     Y0              ; BĂNG TẢI CHẠY TIẾP TỤC (Các phôi khác trong hàng đợi giữ nguyên!)

; ------------------------------------------------------------------------------
; NETWORK 7: CỘNG DỒN BỘ ĐẾM SẢN LƯỢNG 32-BIT
; ------------------------------------------------------------------------------
LDP     M20             ; Mỗi khi có phôi mới vào buồng
DADD    D300    K1      D300    ; Tổng sản lượng kiểm tra (D300)

LDP     M105            ; Mỗi khi có phôi NG
DADD    D304    K1      D304    ; Tổng số phôi lỗi NG (D304)
