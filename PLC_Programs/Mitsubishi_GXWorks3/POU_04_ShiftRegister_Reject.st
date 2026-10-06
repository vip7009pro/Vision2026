(* ========================================================================== *)
(* PROGRAM NAME: POU_04_ShiftRegister_Reject                                 *)
(* DESCRIPTION : In-Flight Pipelined FIFO Tracking & Outside-Chamber Stop/Reject *)
(* PLATFORM    : Mitsubishi iQ-F (FX5U) / iQ-R Series (GX Works 3)          *)
(* YÊU CẦU ĐẶC BIỆT:                                                        *)
(* 1. Phát hiện NG không dừng ngay trong buồng, mà di chuyển đến vị trí chỉ  *)
(*    định ở BÊN NGOÀI buồng kiểm tra mới dừng.                             *)
(* 2. Trong lúc phôi NG di chuyển từ buồng ra ngoài buồng, các phôi tiếp theo *)
(*    vẫn được trigger và kiểm tra bình thường (Pipelined In-Flight Flow).   *)
(* 3. Khi phôi NG đến đúng vị trí chỉ định: dừng đúng phôi NG (Stopper Y21, *)
(*    dừng băng tải Y0 nếu cài đặt, còi đèn Y22, xylanh gạt Y20).            *)
(* ========================================================================== *)

VAR
    (* Hàng đợi theo dõi hành trình phôi đang di chuyển (In-Flight FIFO, tối đa 20 phôi) *)
    aTrackingTargets : ARRAY[0..19] OF REAL; (* Tọa độ mm dự kiến khi đến trạm ngoài buồng *)
    aTrackingStatus  : ARRAY[0..19] OF INT;  (* 0: Chờ kết quả, 1: PASS (OK), 2: NG (Lỗi) *)
    iTrackingCount   : INT := 0;             (* Số lượng phôi đang trên hành trình *)

    fbTriggerR_TRIG       : R_TRIG;
    fbNgR_TRIG            : R_TRIG;
    fbPassR_TRIG          : R_TRIG;
    fbAckBtnR_TRIG        : R_TRIG;
    fbOutsideSensorR_TRIG : R_TRIG;
    tRejectPulse          : TON;
    bRejectPusherActive   : BOOL := FALSE;
    bNgHoldingActive      : BOOL := FALSE; (* Cờ báo có phôi NG đang dừng tại trạm ngoài buồng *)

    i : INT;
    j : INT;
    k : INT;
    rCurrentMm        : REAL;
    rDistanceSetting  : REAL;
    rToleranceSetting : REAL;
    bStopConveyorOnNg : BOOL;
END_VAR

(* -------------------------------------------------------------------------- *)
(* ĐỌC THÔNG SỐ CÀI ĐẶT TỪ THANH GHI D VÀ BIT ĐIỀU KHIỂN                      *)
(* -------------------------------------------------------------------------- *)
rCurrentMm := D1004; (* Tọa độ mm thời gian thực đọc từ High-Speed Counter POU_03 *)
rDistanceSetting := INT_TO_REAL(D100); (* Khoảng cách từ Camera -> Trạm dừng ngoài buồng, vd 1500mm *)
IF rDistanceSetting <= 0.0 THEN
    rDistanceSetting := 1500.0; (* Mặc định 1500mm *)
END_IF;

rToleranceSetting := INT_TO_REAL(D102); (* Dung sai tọa độ, vd 15mm *)
IF rToleranceSetting <= 0.0 THEN
    rToleranceSetting := 15.0;
END_IF;

(* M120: Tùy chọn Dừng băng tải khi có hàng NG đến trạm ngoài buồng (Mặc định TRUE) *)
bStopConveyorOnNg := M120;

(* -------------------------------------------------------------------------- *)
(* 1. KHI CẢM BIẾN BUỒNG PHÁT HIỆN PHÔI MỚI (M20): NẠP PHÔI VÀO HÀNG ĐỢI FIFO *)
(* Phôi mới vào hàng đợi, gán trạng thái = 0 (Chờ Vision PC phân tích)         *)
(* BĂNG TẢI VẪN TIẾP TỤC CHẠY BÌNH THƯỜNG!                                    *)
(* -------------------------------------------------------------------------- *)
fbTriggerR_TRIG(CLK := M20);
IF fbTriggerR_TRIG.Q THEN
    IF iTrackingCount < 20 THEN
        (* Tính tọa độ mà phôi này sẽ tới trạm dừng ngoài buồng kiểm tra *)
        aTrackingTargets[iTrackingCount] := rCurrentMm + rDistanceSetting;
        aTrackingStatus[iTrackingCount]  := 0; (* Trạng thái: Đang kiểm tra *)
        iTrackingCount := iTrackingCount + 1;
    END_IF;
END_IF;

(* -------------------------------------------------------------------------- *)
(* 2. KHI VISION BÁO KẾT QUẢ NG (M30): CẬP NHẬT TRẠNG THÁI PHÔI THÀNH NG (2)  *)
(* Phôi KHÔNG dừng tại buồng! Nó tiếp tục di chuyển ra trạm ngoài buồng.       *)
(* -------------------------------------------------------------------------- *)
fbNgR_TRIG(CLK := M30);
IF fbNgR_TRIG.Q THEN
    (* Tìm phôi cũ nhất trong hàng đợi đang ở trạng thái chờ kết quả (Status = 0) *)
    FOR k := 0 TO (iTrackingCount - 1) DO
        IF aTrackingStatus[k] = 0 THEN
            aTrackingStatus[k] := 2; (* Đánh dấu phôi này là NG! *)
            EXIT;
        END_IF;
    END_FOR;
END_IF;

(* -------------------------------------------------------------------------- *)
(* 3. KHI VISION BÁO KẾT QUẢ PASS (M31): CẬP NHẬT TRẠNG THÁI PHÔI THÀNH PASS (1)*)
(* -------------------------------------------------------------------------- *)
fbPassR_TRIG(CLK := M31);
IF fbPassR_TRIG.Q THEN
    (* Tìm phôi cũ nhất trong hàng đợi đang ở trạng thái chờ kết quả (Status = 0) *)
    FOR k := 0 TO (iTrackingCount - 1) DO
        IF aTrackingStatus[k] = 0 THEN
            aTrackingStatus[k] := 1; (* Đánh dấu phôi này là ĐẠT (OK) *)
            EXIT;
        END_IF;
    END_FOR;
END_IF;

(* -------------------------------------------------------------------------- *)
(* 4. GIÁM SÁT PHÔI ĐẾN VỊ TRÍ CHỈ ĐỊNH Ở NGOÀI BUỒNG KIỂM TRA                *)
(* Nhận diện bằng tọa độ Encoder HOẶC cảm biến trạm ngoài buồng (X3)          *)
(* -------------------------------------------------------------------------- *)
IF iTrackingCount > 0 THEN
    (* Kiểm tra phôi dẫn đầu hàng đợi (Phôi số 0 - cũ nhất) *)
    IF (rCurrentMm >= (aTrackingTargets[0] - rToleranceSetting)) OR X3 THEN
        
        (* === TRƯỜNG HỢP A: PHÔI DẪN ĐẦU LÀ NG (LỖI) === *)
        IF aTrackingStatus[0] = 2 THEN
            bNgHoldingActive := TRUE; (* Kích hoạt chế độ DỪNG PHÔI LỖI ngoài buồng *)
            bRejectPusherActive := TRUE; (* Kích hoạt van gạt / xylanh nếu có *)

        (* === TRƯỜNG HỢP B: PHÔI DẪN ĐẦU LÀ PASS (ĐẠT) === *)
        ELSIF aTrackingStatus[0] = 1 THEN
            (* Phôi đạt tiêu chuẩn: KHÔNG dừng! Cho trôi qua trạm ngoài buồng *)
            (* Khi phôi đạt đã vượt qua khỏi trạm ngoài buồng: dịch hàng đợi *)
            IF (rCurrentMm >= (aTrackingTargets[0] + rToleranceSetting + 30.0)) OR (NOT X3 AND fbOutsideSensorR_TRIG.Q) THEN
                FOR j := 0 TO (iTrackingCount - 2) DO
                    aTrackingTargets[j] := aTrackingTargets[j + 1];
                    aTrackingStatus[j]  := aTrackingStatus[j + 1];
                END_FOR;
                aTrackingTargets[iTrackingCount - 1] := 0.0;
                aTrackingStatus[iTrackingCount - 1]  := 0;
                iTrackingCount := iTrackingCount - 1;
            END_IF;
        END_IF;
    END_IF;
END_IF;

fbOutsideSensorR_TRIG(CLK := X3);

(* -------------------------------------------------------------------------- *)
(* 5. XỬ LÝ DỪNG TẠI VỊ TRÍ CHỈ ĐỊNH NGOÀI BUỒNG KHI CÓ PHÔI NG                *)
(* -------------------------------------------------------------------------- *)
IF bNgHoldingActive THEN
    Y21 := TRUE; (* Kích hoạt cơ cấu Stopper chặn giữ hàng NG tại vị trí chỉ định *)
    Y22 := TRUE; (* Bật còi và đèn tháp báo động có sản phẩm NG chờ xử lý *)
    M215 := TRUE; (* Cờ HMI báo trạng thái: Sản phẩm NG đang dừng ở trạm chỉ định *)
    
    (* Dừng động cơ băng tải nếu bật tùy chọn M120 *)
    IF bStopConveyorOnNg THEN
        Y0 := FALSE; (* Tạm dừng băng tải chính *)
    END_IF;
ELSE
    Y21 := FALSE; (* Hạ Stopper *)
    Y22 := FALSE; (* Tắt còi đèn *)
    M215 := FALSE;
    Y0 := TRUE;  (* Băng tải hoạt động bình thường *)
END_IF;

(* -------------------------------------------------------------------------- *)
(* 6. NÚT NHẤN XÁC NHẬN ĐÃ LẤY PHÔI NG (Nút nhấn công nhân X4 hoặc HMI M211)  *)
(* Khi công nhân đã lấy phôi NG ra khỏi trạm ngoài buồng và bấm nút xác nhận: *)
(* Giải phóng Stopper, cho băng tải chạy tiếp, và dịch hàng đợi FIFO          *)
(* -------------------------------------------------------------------------- *)
fbAckBtnR_TRIG(CLK := (X4 OR M211));
IF fbAckBtnR_TRIG.Q AND bNgHoldingActive THEN
    bNgHoldingActive := FALSE; (* Hủy trạng thái giữ phôi NG *)
    M211 := FALSE;

    (* Dịch chuyển giải phóng phôi NG đã xử lý xong khỏi hàng đợi *)
    IF iTrackingCount > 0 THEN
        FOR j := 0 TO (iTrackingCount - 2) DO
            aTrackingTargets[j] := aTrackingTargets[j + 1];
            aTrackingStatus[j]  := aTrackingStatus[j + 1];
        END_FOR;
        aTrackingTargets[iTrackingCount - 1] := 0.0;
        aTrackingStatus[iTrackingCount - 1]  := 0;
        iTrackingCount := iTrackingCount - 1;
    END_IF;
END_IF;

(* -------------------------------------------------------------------------- *)
(* 7. ĐIỀU KHIỂN ĐỘ RỘNG XUNG XYLANH / KHÍ NÉN REJECT (150ms)                 *)
(* -------------------------------------------------------------------------- *)
tRejectPulse(IN := bRejectPusherActive, PT := T#150ms);
IF bRejectPusherActive THEN
    Y20 := TRUE; (* Xuất tín hiệu van xylanh đẩy phôi NG *)
    IF tRejectPulse.Q THEN
        Y20 := FALSE;
        bRejectPusherActive := FALSE;
    END_IF;
ELSE
    Y20 := FALSE;
END_IF;
