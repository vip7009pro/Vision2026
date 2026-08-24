(* ========================================================================== *)
(* PROGRAM NAME: POU_01_Watchdog_Heartbeat                                   *)
(* DESCRIPTION : 2-Way Heartbeat Watchdog & Line Safety Interlock (24/7)     *)
(* PLATFORM    : Mitsubishi iQ-R / iQ-F (FX5U) / Q Series (GX Works 3 / 2)   *)
(* ========================================================================== *)

VAR
    fbVisionHeartbeatR_TRIG : R_TRIG;
    fbVisionHeartbeatF_TRIG : F_TRIG;
    bVisionHeartbeatToggled : BOOL;
    tWatchdogTimer : TON;
    tPlcHeartbeatTimer : TON;
END_VAR

(* -------------------------------------------------------------------------- *)
(* 1. TẠO NHỊP TIM PLC ĐẢO 0/1 CHU KỲ 100ms GỬI CHO VISION PC                 *)
(* -------------------------------------------------------------------------- *)
tPlcHeartbeatTimer(IN := NOT tPlcHeartbeatTimer.Q, PT := T#100ms);
IF tPlcHeartbeatTimer.Q THEN
    X0 := NOT X0; (* Đảo bit PLC Heartbeat X0 (hoặc M201) mỗi 100ms *)
END_IF;

(* -------------------------------------------------------------------------- *)
(* 2. GIÁM SÁT NHỊP TIM TỪ VISION PC (Y0 hoặc M200)                            *)
(* Phát hiện cạnh lên (R_TRIG) hoặc cạnh xuống (F_TRIG) của Vision_Heartbeat  *)
(* -------------------------------------------------------------------------- *)
fbVisionHeartbeatR_TRIG(CLK := Y0);
fbVisionHeartbeatF_TRIG(CLK := Y0);
bVisionHeartbeatToggled := fbVisionHeartbeatR_TRIG.Q OR fbVisionHeartbeatF_TRIG.Q;

(* -------------------------------------------------------------------------- *)
(* 3. WATCHDOG TIMER: NẾU QUÁ 300ms KHÔNG THẤY VISION ĐẢO BIT THÌ BÁO LỖI     *)
(* -------------------------------------------------------------------------- *)
tWatchdogTimer(
    IN := NOT bVisionHeartbeatToggled, 
    PT := T#300ms
);

IF tWatchdogTimer.Q THEN
    M202 := TRUE;  (* Bật cờ lỗi Vision Fault (Mất kết nối / PC treo) *)
    Y10  := FALSE; (* Ngắt liên động an toàn: Dừng khẩn cấp động cơ chuyền *)
ELSE
    M202 := FALSE; (* Trạng thái truyền thông Vision bình thường *)
    Y10  := TRUE;  (* Cho phép liên động chạy chuyền *)
END_IF;
