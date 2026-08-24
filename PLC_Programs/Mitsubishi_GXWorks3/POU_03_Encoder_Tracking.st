(* ========================================================================== *)
(* PROGRAM NAME: POU_03_Encoder_Tracking                                     *)
(* DESCRIPTION : High-Speed Counter (HSC) Read, Position mm & Speed Calc     *)
(* PLATFORM    : Mitsubishi iQ-R / iQ-F (FX5U) / Q Series (GX Works 3 / 2)   *)
(* ========================================================================== *)

VAR
    diRawEncoderPulses : DINT;
    diLastEncoderPulses : DINT;
    diDeltaPulses : DINT;
    tSpeedCalcTimer : TON;
    rPulsesPerMm : REAL := 100.0; (* 100 xung = 1 mm di chuyển *)
    rCurrentPositionMm : REAL;
END_VAR

(* -------------------------------------------------------------------------- *)
(* 1. ĐỌC BỘ ĐẾM XUNG TỐC ĐỘ CAO (HIGH SPEED COUNTER)                         *)
(* Đối với FX5U: SD6000 (HSC1 CH1) hoặc C251; Đối với Q-Series: QD62 buffer    *)
(* -------------------------------------------------------------------------- *)
(* Lấy giá trị xung hiện tại từ bộ đếm phần cứng gán vào D1000 (DINT 32-bit)   *)
diRawEncoderPulses := D1000; 

(* Tính đổi tọa độ vị trí thực tế của cuộn/băng chuyền theo mm *)
IF rPulsesPerMm > 0.0 THEN
    rCurrentPositionMm := DINT_TO_REAL(diRawEncoderPulses) / rPulsesPerMm;
    (* Lưu tọa độ mm thực tế vào D1004 (REAL 32-bit) *)
    D1004 := rCurrentPositionMm;
END_IF;

(* -------------------------------------------------------------------------- *)
(* 2. TÍNH TỐC ĐỘ DÂY CHUYỀN (m/phút) MỖI CHU KỲ 100ms                        *)
(* -------------------------------------------------------------------------- *)
tSpeedCalcTimer(IN := NOT tSpeedCalcTimer.Q, PT := T#100ms);
IF tSpeedCalcTimer.Q THEN
    diDeltaPulses := diRawEncoderPulses - diLastEncoderPulses;
    diLastEncoderPulses := diRawEncoderPulses;

    (* Tốc độ (m/min) = (Delta Pulses / PulsesPerMm) * (60s / 0.1s) / 1000mm *)
    IF rPulsesPerMm > 0.0 THEN
        D1002 := REAL_TO_INT((DINT_TO_REAL(diDeltaPulses) / rPulsesPerMm) * 0.6);
    END_IF;
END_IF;
