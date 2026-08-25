(* ========================================================================== *)
(* PROGRAM NAME: POU_02_Vision_Handshake                                     *)
(* DESCRIPTION : 24/7 Robust Handshake State Machine with Vision System      *)
(* PLATFORM    : Mitsubishi iQ-R / iQ-F (FX5U) / Q Series (GX Works 3 / 2)   *)
(* ========================================================================== *)

VAR
    iState : INT := 0; (* 0: Idle, 10: Triggered, 20: Inspecting, 30: Latched/Ack, 99: Timeout *)
    fbPartSensorR_TRIG : R_TRIG;
    tHandshakeTimeout : TON;
    bPartCapturedOk : BOOL;
    bPartCapturedNg : BOOL;
END_VAR

(* -------------------------------------------------------------------------- *)
(* BẮT CẠNH CẢM BIẾN PHÔI ĐẾN VỊ TRÍ CHỤP (X2)                               *)
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
(* MÁY TRẠNG THÁI BẮT TAY CÔNG NGHIỆP 24/7 (STATE MACHINE)                    *)
(* -------------------------------------------------------------------------- *)
CASE iState OF
    0: (* TRẠNG THÁI IDLE: Chờ sản phẩm vào và Vision sẵn sàng (Y1 = 1) *)
        M10 := FALSE; (* Xung trigger OFF *)
        M20 := FALSE; (* Xung chốt tọa độ Encoder Trigger OFF *)
        X1  := FALSE; (* Xung PLC Ack OFF *)
        
        IF fbPartSensorR_TRIG.Q AND Y1 (* Vision_Ready *) AND NOT M202 (* Không có lỗi *) THEN
            M10 := TRUE;   (* Bắn xung Trigger cho Vision PC / Camera *)
            M20 := TRUE;   (* Kích hoạt POU_04 chốt tọa độ Encoder S_trigger lúc chụp *)
            iState := 10;
        END_IF;

    10: (* TRẠNG THÁI TRIGGER SENT: Chờ Vision xác nhận nhận lệnh (Vision Busy Y2) *)
        IF Y2 (* Vision_Busy *) THEN
            M10 := FALSE;  (* Ngắt xung Trigger ngay khi Vision đã bận *)
            M20 := FALSE;  (* Ngắt cờ chốt tọa độ *)
            iState := 20;
        END_IF;

    20: (* TRẠNG THÁI INSPECTING: Chờ Vision phân tích xong (Vision Done Y3) *)
        IF Y3 (* Vision_Done *) THEN
            (* Chốt kết quả kiểm tra *)
            bPartCapturedOk := Y4; (* Vision_Pass *)
            bPartCapturedNg := Y5; (* Vision_NG *)

            (* Phân luồng kết quả vào Shift Register *)
            IF bPartCapturedNg THEN
                M30 := TRUE; (* Kích hoạt nạp phôi lỗi vào hàng đợi Reject *)
            ELSE
                M31 := TRUE; (* Kích hoạt giải phóng phôi đạt khỏi hàng đợi In-Flight *)
            END_IF;

            X1 := TRUE; (* Gửi tín hiệu PLC_Ack (X1) xác nhận đã nhận kết quả *)
            iState := 30;
        END_IF;

    30: (* TRẠNG THÁI WAIT VISION RESET: Chờ Vision hạ cờ Done (Y3) và đưa Y1 lên 1 *)
        IF NOT Y3 (* Vision_Done hạ về 0 *) THEN
            X1  := FALSE; (* PLC hạ cờ PLC_Ack (X1) hoàn tất chu trình *)
            M30 := FALSE;
            M31 := FALSE;
            iState := 0; (* Quay về IDLE sẵn sàng cho con hàng tiếp theo *)
        END_IF;

    99: (* TRẠNG THÁI XỬ LÝ LỖI TIMEOUT *)
        M10 := FALSE;
        M20 := FALSE;
        M30 := FALSE;
        M31 := FALSE;
        X1  := FALSE;
        M210 := TRUE; (* Cảnh báo lỗi bắt tay Vision Timeout *)
        (* Tự phục hồi sau khi người dùng bấm Reset M211 hoặc Vision Ready trở lại *)
        IF M211 OR Y1 THEN
            M210 := FALSE;
            M211 := FALSE;
            iState := 0;
        END_IF;
END_CASE;
