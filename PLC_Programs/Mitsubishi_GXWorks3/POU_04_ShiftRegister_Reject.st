(* ========================================================================== *)
(* PROGRAM NAME: POU_04_ShiftRegister_Reject                                 *)
(* DESCRIPTION : FIFO Shift Register & Precision Millimeter Reject Piston    *)
(* PLATFORM    : Mitsubishi iQ-R / iQ-F (FX5U) / Q Series (GX Works 3 / 2)   *)
(* ========================================================================== *)

VAR
    aRejectTargets : ARRAY[0..19] OF REAL; (* Bộ đệm lưu tối đa 20 sản phẩm NG đang trôi *)
    iQueueCount : INT := 0;
    fbPushR_TRIG : R_TRIG;
    tRejectPulse : TON;
    bRejectActive : BOOL;
    i : INT;
    j : INT;
    rTargetMm : REAL;
    rCurrentMm : REAL;
    rDistanceSetting : REAL;
    rToleranceSetting : REAL;
END_VAR

(* Đọc các thông số cài đặt *)
rCurrentMm := D1004; (* Tọa độ mm hiện tại *)
rDistanceSetting := INT_TO_REAL(D100); (* Khoảng cách từ Camera đến Trạm Reject, vd 1500 mm *)
rToleranceSetting := INT_TO_REAL(D102); (* Dung sai kích hoạt, vd 10 mm *)
IF rToleranceSetting <= 0.0 THEN
    rToleranceSetting := 10.0;
END_IF;

(* -------------------------------------------------------------------------- *)
(* 1. NẠP SẢN PHẨM LỖI (NG) VÀO HÀNG ĐỢI FIFO KHI NHẬN CỜ M30 TỪ HANDSHAKE   *)
(* -------------------------------------------------------------------------- *)
fbPushR_TRIG(CLK := M30);
IF fbPushR_TRIG.Q THEN
    IF iQueueCount < 20 THEN
        (* Tính vị trí tọa độ mm mà con hàng lỗi này sẽ đến trạm Reject *)
        aRejectTargets[iQueueCount] := rCurrentMm + rDistanceSetting;
        iQueueCount := iQueueCount + 1;
    END_IF;
END_IF;

(* -------------------------------------------------------------------------- *)
(* 2. SO SÁNH VỊ TRÍ THỰC TẾ VÀ KÍCH HOẠT THỔI / GẠT SẢN PHẨM LỖI             *)
(* -------------------------------------------------------------------------- *)
FOR i := 0 TO (iQueueCount - 1) DO
    rTargetMm := aRejectTargets[i];
    
    (* Kiểm tra nếu phôi đã tiến vào vùng dung sai của trạm Reject *)
    IF (rCurrentMm >= (rTargetMm - rToleranceSetting)) AND 
       (rCurrentMm <= (rTargetMm + rToleranceSetting + 50.0)) THEN
        
        bRejectActive := TRUE; (* Kích hoạt van xylanh / khí nén *)
        
        (* Dịch chuyển xóa phần tử đã xử lý khỏi hàng đợi FIFO *)
        FOR j := i TO (iQueueCount - 2) DO
            aRejectTargets[j] := aRejectTargets[j + 1];
        END_FOR;
        aRejectTargets[iQueueCount - 1] := 0.0;
        iQueueCount := iQueueCount - 1;
        EXIT;
    END_IF;
END_FOR;

(* -------------------------------------------------------------------------- *)
(* 3. ĐIỀU KHIỂN ĐỘ RỘNG XUNG XYLANH REJECT (100ms)                          *)
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
