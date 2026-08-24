(* ========================================================================== *)
(* PROGRAM NAME: POU_05_Result_Handler                                       *)
(* DESCRIPTION : Vision Results Processing & Yield Counters (DINT 32-bit)    *)
(* PLATFORM    : Mitsubishi iQ-R / iQ-F (FX5U) / Q Series (GX Works 3 / 2)   *)
(* ========================================================================== *)

VAR
    fbPassR_TRIG : R_TRIG;
    fbNgR_TRIG : R_TRIG;
    rCurrentOriginX : REAL;
    rCurrentOriginY : REAL;
    rCurrentOriginAngle : REAL;
    rCurrentDistance1 : REAL;
END_VAR

(* -------------------------------------------------------------------------- *)
(* 1. ĐỌC DỮ LIỆU ĐO ĐẠC VÀ TỌA ĐỘ TRUYỀN TỪ VISION VÀO THANH GHI PLC         *)
(* Vision gửi giá trị vào D200 (X), D202 (Y), D204 (Angle), D210 (Distance1)  *)
(* -------------------------------------------------------------------------- *)
rCurrentOriginX     := D200;
rCurrentOriginY     := D202;
rCurrentOriginAngle := D204;
rCurrentDistance1   := D210;

(* -------------------------------------------------------------------------- *)
(* 2. CỘNG DỒN BỘ ĐẾM SỐ LƯỢNG SẢN PHẨM KIỂM TRA (TOTAL, OK, NG)             *)
(* -------------------------------------------------------------------------- *)
fbPassR_TRIG(CLK := Y4); (* Cạnh lên của Vision_Pass *)
fbNgR_TRIG(CLK := Y5);   (* Cạnh lên của Vision_NG *)

IF fbPassR_TRIG.Q THEN
    D300 := D300 + 1; (* Tăng tổng số kiểm tra D300 *)
    D302 := D302 + 1; (* Tăng tổng số OK D302 *)
END_IF;

IF fbNgR_TRIG.Q THEN
    D300 := D300 + 1; (* Tăng tổng số kiểm tra D300 *)
    D304 := D304 + 1; (* Tăng tổng số NG D304 *)
END_IF;

(* -------------------------------------------------------------------------- *)
(* 3. NÚT NHẤN RESET BỘ ĐẾM SẢN LƯỢNG (HMI / NÚT NHẤN M310)                  *)
(* -------------------------------------------------------------------------- *)
IF M310 THEN
    D300 := 0;
    D302 := 0;
    D304 := 0;
    M310 := FALSE;
END_IF;
