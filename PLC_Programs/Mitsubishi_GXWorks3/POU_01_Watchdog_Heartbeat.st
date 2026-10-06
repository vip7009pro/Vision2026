(* ========================================================================== *)
(* PROGRAM NAME: POU_01_Watchdog_Heartbeat                                   *)
(* DESCRIPTION : 2-Way Heartbeat Watchdog & Line Safety Interlock (24/7)     *)
(* PLATFORM    : Mitsubishi iQ-F (FX5U) / iQ-R / Q Series (GX Works 3)       *)
(* -------------------------------------------------------------------------- *)
(* LƯU Ý QUAN TRỌNG VỚI GX WORKS 3:                                          *)
(* 1. GX Works 3 KHÔNG cho phép khối VAR...END_VAR nằm trong cửa sổ code ST.  *)
(*    Các biến nội bộ cần được khai báo trong bảng [Local Label] của POU:    *)
(*    - fbVisionHeartbeatR_TRIG : R_TRIG                                     *)
(*    - fbVisionHeartbeatF_TRIG : F_TRIG                                     *)
(*    - bVisionHeartbeatToggled : BOOL                                       *)
(*    - bVisionHeartbeatPrev    : BOOL                                       *)
(*    - tWatchdogTimer          : TON                                        *)
(*    (Đã có sẵn file mẫu POU_01_LocalLabels.csv để nạp vào Local Label)    *)
(* 2. Không được ghi vào thiết bị X (X0 là ngõ vào vật lý, cấm ghi).          *)
(*    Nhịp tim PLC gửi cho PC được xuất qua bit nội bộ M108 hoặc bit Y.      *)
(* ========================================================================== *)

(* -------------------------------------------------------------------------- *)
(* 1. TẠO NHỊP TIM PLC ĐẢO 0/1 CHU KỲ 100ms GỬI CHO VISION PC                 *)
(* Sử dụng trực tiếp bit xung đồng hồ hệ thống SM410 (100ms clock) của FX5U   *)
(* -------------------------------------------------------------------------- *)
M108 := SM410; (* PLC_Heartbeat xuất liên tục nhịp 100ms sang Vision PC *)

(* -------------------------------------------------------------------------- *)
(* 2. GIÁM SÁT NHỊP TIM TỪ VISION PC (Vision_Heartbeat: Y0 hoặc M100)         *)
(* Phát hiện mỗi khi Vision PC đảo trạng thái (0 -> 1 hoặc 1 -> 0)           *)
(* -------------------------------------------------------------------------- *)
fbVisionHeartbeatR_TRIG(CLK := Y0);
fbVisionHeartbeatF_TRIG(CLK := Y0);
bVisionHeartbeatToggled := fbVisionHeartbeatR_TRIG.Q OR fbVisionHeartbeatF_TRIG.Q;

(* -------------------------------------------------------------------------- *)
(* 3. WATCHDOG TIMER: NẾU QUÁ 300ms KHÔNG THẤY VISION ĐẢO BIT THÌ BÁO LỖI     *)
(* -------------------------------------------------------------------------- *)
tWatchdogTimer(
    IN := NOT bVisionHeartbeatToggled, 
    PT := T#300MS
);

IF tWatchdogTimer.Q THEN
    M202 := TRUE;  (* Bật cờ lỗi Vision Fault (Mất kết nối / PC treo) *)
    Y10  := FALSE; (* Ngắt liên động an toàn: Dừng khẩn cấp động cơ chuyền *)
ELSE
    M202 := FALSE; (* Trạng thái truyền thông Vision bình thường *)
    Y10  := TRUE;  (* Cho phép liên động chạy chuyền *)
END_IF;
