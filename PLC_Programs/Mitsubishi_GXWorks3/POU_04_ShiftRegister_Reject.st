(* ========================================================================== *)
(* PROGRAM NAME: POU_04_ShiftRegister_Reject                                 *)
(* DESCRIPTION : Dual FIFO Shift Register & Precision Millimeter Reject Piston*)
(* PLATFORM    : Mitsubishi iQ-R / iQ-F (FX5U) / Q Series (GX Works 3 / 2)   *)
(* ========================================================================== *)

VAR
    (* 1. Hàng đợi phôi đang kiểm tra (In-Flight Buffer) *)
    aInFlightTargets : ARRAY[0..19] OF REAL; 
    iInFlightCount : INT := 0;

    (* 2. Hàng đợi sản phẩm lỗi cần gạt (Reject FIFO Buffer) *)
    aRejectTargets : ARRAY[0..19] OF REAL; 
    iRejectCount : INT := 0;

    fbTriggerR_TRIG : R_TRIG;
    fbNgR_TRIG : R_TRIG;
    fbPassR_TRIG : R_TRIG;
    tRejectPulse : TON;
    bRejectActive : BOOL;
    i : INT;
    j : INT;
    rTargetMm : REAL;
    rCurrentMm : REAL;
    rDistanceSetting : REAL;
    rToleranceSetting : REAL;
END_VAR

(* -------------------------------------------------------------------------- *)
(* ĐỌC THÔNG SỐ CÀI ĐẶT                                                       *)
(* -------------------------------------------------------------------------- *)
rCurrentMm := D1004; (* Tọa độ Encoder mm thời gian thực *)
rDistanceSetting := INT_TO_REAL(D100); (* Khoảng cách từ Camera -> Xylanh Reject, vd: 1500 mm *)
rToleranceSetting := INT_TO_REAL(D102); (* Dung sai kích hoạt, vd: 10 mm *)
IF rToleranceSetting <= 0.0 THEN
    rToleranceSetting := 10.0;
END_IF;

(* -------------------------------------------------------------------------- *)
(* 1. KHI PHÔI ĐƯỢC CHỤP ẢNH (M20): LƯU TỌA ĐỘ REJECT DỰ KIẾN VÀO IN-FLIGHT   *)
(* -------------------------------------------------------------------------- *)
fbTriggerR_TRIG(CLK := M20);
IF fbTriggerR_TRIG.Q THEN
    IF iInFlightCount < 20 THEN
        (* Tính toán chính xác vị trí con hàng này sẽ đến Xylanh theo tọa độ lúc chụp *)
        aInFlightTargets[iInFlightCount] := rCurrentMm + rDistanceSetting;
        iInFlightCount := iInFlightCount + 1;
    END_IF;
END_IF;

(* -------------------------------------------------------------------------- *)
(* 2. KHI VISION BÁO KẾT QUẢ NG (M30): CHUYỂN PHÔI LỖI SANG HÀNG ĐỢI REJECT   *)
(* -------------------------------------------------------------------------- *)
fbNgR_TRIG(CLK := M30);
IF fbNgR_TRIG.Q THEN
    IF iInFlightCount > 0 THEN
        IF iRejectCount < 20 THEN
            (* Nạp tọa độ phôi lỗi đầu tiên vào mảng Reject *)
            aRejectTargets[iRejectCount] := aInFlightTargets[0];
            iRejectCount := iRejectCount + 1;
        END_IF;

        (* Dịch chuyển hàng đợi In-Flight *)
        FOR j := 0 TO (iInFlightCount - 2) DO
            aInFlightTargets[j] := aInFlightTargets[j + 1];
        END_FOR;
        aInFlightTargets[iInFlightCount - 1] := 0.0;
        iInFlightCount := iInFlightCount - 1;
    END_IF;
END_IF;

(* -------------------------------------------------------------------------- *)
(* 3. KHI VISION BÁO KẾT QUẢ PASS (M31): XÓA PHÔI ĐẠT KHỎI IN-FLIGHT          *)
(* -------------------------------------------------------------------------- *)
fbPassR_TRIG(CLK := M31);
IF fbPassR_TRIG.Q THEN
    IF iInFlightCount > 0 THEN
        (* Dịch chuyển giải phóng phôi đạt khỏi hàng đợi In-Flight *)
        FOR j := 0 TO (iInFlightCount - 2) DO
            aInFlightTargets[j] := aInFlightTargets[j + 1];
        END_FOR;
        aInFlightTargets[iInFlightCount - 1] := 0.0;
        iInFlightCount := iInFlightCount - 1;
    END_IF;
END_IF;

(* -------------------------------------------------------------------------- *)
(* 4. SO SÁNH VỊ TRÍ THỰC TẾ & KÍCH HOẠT XYLANH GẠT PHÔI LỖI                  *)
(* -------------------------------------------------------------------------- *)
FOR i := 0 TO (iRejectCount - 1) DO
    rTargetMm := aRejectTargets[i];
    
    (* Kiểm tra phôi lỗi đã tiến vào vùng dung sai của đầu Xylanh Reject hay chưa *)
    IF (rCurrentMm >= (rTargetMm - rToleranceSetting)) AND 
       (rCurrentMm <= (rTargetMm + rToleranceSetting + 50.0)) THEN
        
        bRejectActive := TRUE; (* Kích hoạt van xylanh / khí nén *)
        
        (* Dịch chuyển xóa phần tử đã gạt khỏi hàng đợi Reject *)
        FOR j := i TO (iRejectCount - 2) DO
            aRejectTargets[j] := aRejectTargets[j + 1];
        END_FOR;
        aRejectTargets[iRejectCount - 1] := 0.0;
        iRejectCount := iRejectCount - 1;
        EXIT;
    END_IF;
END_FOR;

(* -------------------------------------------------------------------------- *)
(* 5. ĐIỀU KHIỂN ĐỘ RỘNG XUNG XYLANH REJECT (100ms)                          *)
(* -------------------------------------------------------------------------- *)
tRejectPulse(IN := bRejectActive, PT := T#100ms);
IF bRejectActive THEN
    Y20 := TRUE; (* Xuất tín hiệu ON cho van xylanh / vòi phun khí Reject *)
    IF tRejectPulse.Q THEN
        Y20 := FALSE;
        bRejectActive := FALSE; (* Ngắt xung hoàn tất 1 lần thổi *)
    END_IF;
ELSE
    Y20 := FALSE;
END_IF;
