(* ========================================================================== *)
(* PROGRAM NAME: POU_01_Watchdog_Heartbeat (Native Mitsubishi Version)       *)
(* ĐẶC ĐIỂM    : SIÊU GỌN - KHÔNG CẦN KHAI BÁO LOCAL LABEL PHỨC TẠP          *)
(*               DÙNG TRỰC TIẾP TIMER PHẦN CỨNG T0 VÀ BIT HỆ THỐNG SM410      *)
(* PLATFORM    : Mitsubishi FX5U (GX Works 3)                                *)
(* ========================================================================== *)

(* -------------------------------------------------------------------------- *)
(* 1. TẠO NHỊP TIM PLC ĐẢO 0/1 CHU KỲ 100ms GỬI CHO VISION PC                 *)
(* Sử dụng trực tiếp cờ xung nhịp hệ thống SM410 (100ms clock) của FX5U       *)
(* -------------------------------------------------------------------------- *)
M108 := SM410; (* PLC_Heartbeat xuất liên tục sang Vision PC qua MC Protocol *)

(* -------------------------------------------------------------------------- *)
(* 2. PHÁT HIỆN NHỊP TIM VISION PC (Y0 hoặc M100) ĐẢO TRẠNG THÁI              *)
(* So sánh bit hiện tại với chu kỳ trước: Đảo bit thì kích xung M201 1 vòng   *)
(* -------------------------------------------------------------------------- *)
IF Y0 <> M200 THEN
    M200 := Y0;    (* M200: Bộ đệm lưu trạng thái chu kỳ trước của Y0 *)
    M201 := TRUE;  (* M201: Xung báo Vision PC vừa đảo nhịp tim *)
ELSE
    M201 := FALSE;
END_IF;

(* -------------------------------------------------------------------------- *)
(* 3. WATCHDOG TIMER T0: NẾU QUÁ 300ms KHÔNG THẤY VISION ĐẢO BIT THÌ BÁO LỖI  *)
(* Lệnh OUT_T(điều kiện đếm, tên Timer, giá trị K đặt): K3 = 3 * 100ms = 300ms*)
(* -------------------------------------------------------------------------- *)
OUT_T(NOT M201, T0, 3);

(* -------------------------------------------------------------------------- *)
(* 4. LIÊN ĐỘNG AN TOÀN TOÀN HỆ THỐNG                                         *)
(* -------------------------------------------------------------------------- *)
IF T0 THEN
    M202 := TRUE;  (* Bật cờ lỗi Vision Fault (Mất kết nối hoặc PC treo) *)
    Y10  := FALSE; (* Ngắt liên động an toàn: Dừng khẩn cấp động cơ chuyền *)
ELSE
    M202 := FALSE; (* Truyền thông bình thường *)
    Y10  := TRUE;  (* Cho phép liên động chạy chuyền *)
END_IF;
