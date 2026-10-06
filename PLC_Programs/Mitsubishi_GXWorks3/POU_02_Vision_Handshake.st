(* ========================================================================== *)
(* PROGRAM NAME: POU_02_Vision_Handshake                                     *)
(* DESCRIPTION : 24/7 Robust Handshake State Machine with Vision System      *)
(* PLATFORM    : Mitsubishi iQ-F (FX5U) / iQ-R Series (GX Works 3)          *)
(* COMMUNICATION: Ethernet SLMP / MC Protocol 3E Frame                       *)
(* ========================================================================== *)

VAR
    iState : INT := 0; (* 0: Idle, 10: Triggered, 20: Inspecting, 30: Latched/Ack, 99: Timeout *)
    fbPartSensorR_TRIG : R_TRIG;
    tHandshakeTimeout : TON;
    bPartCapturedOk : BOOL;
    bPartCapturedNg : BOOL;
END_VAR

(* -------------------------------------------------------------------------- *)
(* BẮT CẠNH CẢM BIẾN PHÔI ĐẾN VỊ TRÍ CHỤP TRONG BUỒNG KIỂM TRA (X2)          *)
(* -------------------------------------------------------------------------- *)
fbPartSensorR_TRIG(CLK := X2);

(* -------------------------------------------------------------------------- *)
(* TIMER BẢO VỆ CHU TRÌNH BẮT TAY (TIMEOUT 2000ms)                            *)
(* -------------------------------------------------------------------------- *)
tHandshakeTimeout(
    IN := (iState > 0 AND iState < 99),
    PT := T#2000ms
);

IF tHandshakeTimeout.Q THEN
    iState := 99; (* Chuyển sang trạng thái xử lý lỗi Timeout *)
END_IF;

(* -------------------------------------------------------------------------- *)
(* MÁY TRẠNG THÁI BẮT TAY CÔNG NGHIỆP 24/7 (STATE MACHINE - FX5U STANDARD)     *)
(* Giao tiếp qua thanh ghi nội bộ M:                                         *)
(* - PC -> PLC : M101 (Ready), M102 (Busy), M103 (Done), M104 (Pass), M105 (NG)*)
(* - PLC -> PC : M10 (Trigger), M11 (PLC_Ack)                                *)
(* -------------------------------------------------------------------------- *)
CASE iState OF
    0: (* TRẠNG THÁI IDLE: Chờ sản phẩm vào và Vision PC sẵn sàng (M101 = 1) *)
        M10 := FALSE; (* Xung trigger chụp ảnh OFF *)
        M20 := FALSE; (* Xung chốt phôi mới vào hàng đợi Tracking OFF *)
        M11 := FALSE; (* Xung PLC Ack OFF *)
        
        (* Khi cảm biến buồng có phôi (X2) VÀ Vision PC sẵn sàng (M101) VÀ không có lỗi (NOT M202) *)
        IF fbPartSensorR_TRIG.Q AND M101 (* Vision_Ready *) AND NOT M202 THEN
            M10 := TRUE;   (* Phát xung Trigger M10 cho Vision PC / Camera *)
            M20 := TRUE;   (* Kích hoạt POU_04 chốt phôi mới vào hàng đợi In-Flight Tracking *)
            iState := 10;
        END_IF;

    10: (* TRẠNG THÁI TRIGGER SENT: Chờ Vision xác nhận nhận lệnh (Vision Busy M102) *)
        IF M102 (* Vision_Busy *) THEN
            M10 := FALSE;  (* Ngắt xung Trigger ngay khi Vision đã nhận lệnh *)
            M20 := FALSE;  (* Ngắt cờ chốt tọa độ *)
            iState := 20;
        END_IF;

    20: (* TRẠNG THÁI INSPECTING: Chờ Vision phân tích xong (Vision Done M103) *)
        IF M103 (* Vision_Done *) THEN
            (* Chốt kết quả kiểm tra từ Vision PC *)
            bPartCapturedOk := M104; (* Vision_Pass *)
            bPartCapturedNg := M105; (* Vision_NG *)

            (* Phân luồng kết quả vào Hàng đợi Tracking phôi (POU_04) *)
            IF bPartCapturedNg THEN
                M30 := TRUE; (* Kích hoạt nạp kết quả NG vào phôi đang theo dõi *)
            ELSE
                M31 := TRUE; (* Kích hoạt nạp kết quả OK vào phôi đang theo dõi *)
            END_IF;

            M11 := TRUE; (* Gửi tín hiệu PLC_Ack (M11) xác nhận PLC đã nhận kết quả *)
            iState := 30;
        END_IF;

    30: (* TRẠNG THÁI WAIT VISION RESET: Chờ Vision hạ cờ Done (M103) và khôi phục Ready (M101) *)
        IF NOT M103 (* Vision_Done hạ về 0 *) THEN
            M11 := FALSE; (* PLC hạ cờ PLC_Ack (M11) hoàn tất chu trình *)
            M30 := FALSE;
            M31 := FALSE;
            iState := 0;  (* Quay về IDLE sẵn sàng ngay cho con hàng tiếp theo! *)
        END_IF;

    99: (* TRẠNG THÁI XỬ LÝ LỖI TIMEOUT *)
        M10 := FALSE;
        M20 := FALSE;
        M30 := FALSE;
        M31 := FALSE;
        M11 := FALSE;
        M210 := TRUE; (* Cảnh báo lỗi bắt tay Vision Timeout *)
        (* Tự phục hồi sau khi người dùng bấm Reset M211 hoặc Vision Ready trở lại *)
        IF M211 OR M101 THEN
            M210 := FALSE;
            M211 := FALSE;
            iState := 0;
        END_IF;
END_CASE;
