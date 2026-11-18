# Vision2026 – FX5U Ladder FIFO (bản sửa v3, 2026-11-14)

Bản viết lại toàn bộ từ `grok xml.xml`, đã sửa các lỗi: Z0/Z1 không được nạp, chỉ số 32-bit
không nhân 2, chồng lấn D2100, so sánh 16-bit với encoder 32-bit, double coil Y1,
điều kiện pop ngược (M61), D300 không đếm, queue đầy / kẹt khi thiếu kết quả,
camera lỡ kích khi có nhiều hàng giữa camera và điểm dừng.

> Phần `// ...` là chú thích, KHÔNG gõ vào GX Works3.
> Mỗi lệnh `LD`/`LDP`/`LD=`... bắt đầu một rung mới.

## 1. Bảng thiết bị

| Thiết bị | Ý nghĩa |
|---|---|
| X0 / X1 / X4 / X5 | Start / Stop / Reset / Sensor nạp hàng |
| M2 / M4 / M5 / M6 | Sensor giả lập / Reset HMI / 1 = dùng encoder thật / Xóa hàng đợi (HMI) |
| Y0 | Băng tải |
| Y1 | Trigger camera (xung 20 ms) |
| Y11 | Còi/đèn NG nháy 1 s khi dừng NG |
| Y12 | Đèn NG (giữ đến khi Reset) |
| M10 | Đang chạy |
| M20 | Dừng do NG / không có kết quả |
| M21 / M22 / M23 | Báo lỗi: hàng đợi đầy / hàng tới điểm dừng chưa có kết quả / PC trả kết quả thừa |
| M103 / M104 / M105 | PC: Done / Pass / NG |
| D600 (32-bit) | Vị trí encoder hiện tại (xung) |
| D10 / D12 (32-bit) | Khoảng cách sensor→camera / sensor→điểm dừng NG (xung) |
| D20 / D22 / D24 / D26 (32-bit) | PPR / Đường kính puly / K/c camera / K/c điểm dừng (cùng đơn vị với D22) |
| D28 (32-bit) | Bước tăng encoder giả lập mỗi 100 ms |
| D150 / D152 | Bộ đếm sensor / Số xung sensor cho 1 hàng (mặc định 1) |
| D1000..D1099 | Trạng thái hàng: 0 = chờ, 1 = OK, 2 = NG |
| D2000..D2199 | Vị trí encoder lúc nạp hàng (32-bit, phần tử i nằm ở D2000 + 2i) |
| D1100 | HEAD – hàng cũ nhất (sẽ tới điểm dừng) |
| D1101 | TAIL – ô trống kế tiếp để nạp |
| D1102 | TRIG – hàng kế tiếp cần kích camera |
| D1103 | RES – hàng kế tiếp chờ kết quả từ PC |
| D1104 | Số hàng trong hàng đợi (hiển thị HMI) |
| D300 / D302 / D304 | Tổng / OK / NG |

Thứ tự luôn đúng: `HEAD ≤ RES ≤ TRIG ≤ TAIL` (vòng tròn, kích thước 100, tối đa 99 hàng).

## 2. Thứ tự chạy chương trình (Program Setting → Scan)

1. KHOI_INIT ← **phải đứng đầu** (bản cũ đang ở vị trí 9)
2. KHOI_0_READ_ENCODER
3. KHOI_1_CONVERT_MM_PULSE
4. KHOI_2_START_STOP_RESET
5. KHOI_3_CHAYBANGTAI
6. KHOI_4_SENSOR_QUEUE_FIFO
7. KHOI_5_TRIGGER_CAM
8. KHOI_6_WPF_RESULT
9. KHOI_7_KIEMTRA_VITRI_DUNG_NG_XOA_HANG_DOI

## 3. Ladder từng khối

### KHOI_INIT
```
LD SM402
FMOV K0 D1100 K5          // HEAD, TAIL, TRIG, RES, COUNT = 0
MOV K0 D150
DMOV K1024 D20            // PPR
DMOV K5000 D22            // đường kính puly
DMOV K20000 D24           // k/c sensor -> camera
DMOV K50000 D26           // k/c sensor -> điểm dừng NG
DMOV K20 D28              // bước encoder giả lập
LD SM402
AND= D152 K0
MOV K1 D152
```

### KHOI_0_READ_ENCODER
```
LD SM400
OUT SM4501                // giữ nguyên theo cấu hình HSC của bạn
LD M5
DHCMOV SD4530 D600 K0     // encoder thật
LD Y0                     // giả lập: chỉ đếm khi băng tải chạy thật
ANI M5
ANI T0                    // T0 tự reset -> xung mỗi 100 ms
OUT T0 K1
LD T0
DADD D600 D28 D600
```

### KHOI_1_CONVERT_MM_PULSE (giữ nguyên)
```
LD SM400
ANDD> D22 K0
ANDD> D20 K0
DINT2FLT D20 D50
DINT2FLT D22 D52
E* D52 E3.141593 D54
E/ D50 D54 D56            // xung / đơn vị chiều dài
DINT2FLT D24 D58
E* D58 D56 D60
FLT2DINT D60 D10          // D10 = xung tới camera
DINT2FLT D26 D62
E* D62 D56 D64
FLT2DINT D64 D12          // D12 = xung tới điểm dừng NG
```

### KHOI_2_START_STOP_RESET
```
LD X0
ANI X1
ANI M20
SET M10
LD X1
RST M10
LD X4
OR M4
PLS M40
LD M40                    // Reset lỗi NG
RST M20
RST M21
RST M22
RST M23
LD M6
PLS M41
LD M41                    // Xóa toàn bộ hàng đợi
FMOV K0 D1100 K5
```

### KHOI_3_CHAYBANGTAI
```
LD M10
ANI M20
OUT Y0
```

### KHOI_4_SENSOR_QUEUE_FIFO (nạp hàng)
```
LD X5
OR M2
AND M10
ANI M20
ADDP D150 K1 D150
LD>= D150 D152
MOV K0 D150
PLS M50                   // M50 = 1 hàng mới (1 scan)
LD SM400                  // D80 = (TAIL + 1) mod 100
MOV D1101 D80
INC D80
LD= D80 K100
MOV K0 D80
LD SM400
AND= D80 D1100
OUT M62                   // M62 = hàng đợi đầy
LD M50
ANI M62
MOV D1101 Z0              // Z0 = TAIL
ADD D1101 D1101 Z1        // Z1 = TAIL x 2 (dữ liệu 32-bit)
MOV K0 D1000Z0            // trạng thái = chờ
DMOV D600 D2000Z1         // lưu vị trí encoder
MOV D80 D1101             // TAIL = TAIL + 1 (vòng)
LD M50
AND M62
SET M21                   // báo hàng đợi đầy
LD SM400                  // D1104 = số hàng trong hàng đợi
SUB D1101 D1100 D1104
LD< D1104 K0
ADD D1104 K100 D1104
```

### KHOI_5_TRIGGER_CAM (kích camera theo con trỏ TRIG riêng)
```
LD SM400
AND= D1102 D1101
OUT M60                   // M60 = không còn hàng chờ chụp
LD SM400
ANI M60
ADD D1102 D1102 Z3        // Z3 = TRIG x 2
DMOV D2000Z3 D72          // vị trí lúc nạp
DADD D72 D10 D74          // vị trí cần chụp
LD M10
ANI M60
ANI M31                   // đang không trong chu kỳ kích
ANDD>= D600 D74           // so sánh 32-bit
OUT M30                   // M30 = kích camera scan này
LD M30
SET Y1
SET M31
MOV D1102 D86             // TRIG = TRIG + 1 (vòng)
INC D86
LD M30
AND= D86 K100
MOV K0 D86
LD M30
MOV D86 D1102
LD M31
OUTH T2 K2                // 20 ms: độ rộng xung Y1
OUTH T3 K5                // 50 ms: chu kỳ tối thiểu giữa 2 lần kích
LD T2
RST Y1
LD T3
RST M31
```

### KHOI_6_WPF_RESULT (ghi kết quả vào con trỏ RES)
```
LD M103
AND M104
ANI M105
PLS M70                   // M70 = kết quả OK
LD M103
AND M105
PLS M71                   // M71 = kết quả NG (ưu tiên NG nếu có cả 2)
LD M70
OR M71
OUT M72                   // M72 = có kết quả mới
LD M72
AND<> D1103 D1102
OUT M73                   // M73 = có hàng đang chờ kết quả
LD M72
ANI M73
SET M23                   // kết quả thừa
LD M73
MOV D1103 Z4
LD M73
AND M70
MOV K1 D1000Z4
INC D302
LD M73
AND M71
MOV K2 D1000Z4
INC D304
LD M73
MOV D1103 D88             // RES = RES + 1 (vòng)
INC D88
LD M73
AND= D88 K100
MOV K0 D88
LD M73
MOV D88 D1103
LD M72
INC D300
RST M103
```

### KHOI_7_KIEMTRA_VITRI_DUNG_NG_XOA_HANG_DOI (xử lý hàng ở HEAD)
```
LD SM400
AND= D1100 D1101
OUT M61                   // M61 = hàng đợi rỗng
LD SM400
ANI M61
MOV D1100 Z5
ADD D1100 D1100 Z6        // Z6 = HEAD x 2
MOV D1000Z5 D70           // trạng thái HEAD
DMOV D2000Z6 D78          // vị trí lúc nạp
DADD D78 D12 D76          // vị trí điểm dừng
LD M10
ANI M61                   // ĐÃ SỬA: hàng đợi KHÔNG rỗng
ANDD>= D600 D76
OUT M63                   // M63 = HEAD tới điểm dừng
LD M63
AND<> D70 K1              // NG (2) hoặc chưa có kết quả (0)
SET M20
LD M63
AND= D70 K0
SET M22
LD M63                    // D84 = HEAD + 1 (vòng)
MOV D1100 D84
INC D84
LD M63
AND= D84 K100
MOV K0 D84
LD M63                    // nếu hàng chưa được chụp/chưa có kết quả thì đẩy con trỏ theo
AND= D1102 D1100
MOV D84 D1102
LD M63
AND= D1103 D1100
MOV D84 D1103
LD M63
MOV D84 D1100             // POP
LD M20
OUT Y12                   // đèn NG
LDP M20
SET M11
LD M11
OUT Y11                   // còi NG 1 s (đã bỏ double coil Y1)
OUT T1 K10
LD T1
RST M11
```

## 4. Kịch bản test mô phỏng

1. Bật M10 (X0), M5 = 0, kiểm tra D600 tăng 20 mỗi 100 ms.
2. Kích M2 3 lần → D2000, D2002, D2004 nhận giá trị D600; D1101 = 3; D1104 = 3.
3. Khi D600 ≥ D2000 + D10 → Y1 nháy, D1102 = 1; tiếp tục với hàng 2, 3.
4. Set M103 + M104 → D1000 = 1, D1103 = 1, D302 = 1, D300 = 1.
5. Khi D600 ≥ D2000 + D12 → D1100 = 1 (pop), băng tải vẫn chạy.
6. Hàng 2 trả NG (M103 + M105) → tới điểm dừng: M20 = 1, Y0 tắt, Y12 sáng, Y11 kêu 1 s.
7. Hàng 3 không trả kết quả → tới điểm dừng: M20 + M22 = 1.
8. Reset (X4/M4) → M20/M21/M22/M23 xóa, chạy lại bằng X0.

## 5. Lưu ý

- D24/D26 phải cùng đơn vị với D22. Phải có D26 > D24, và khoảng D12 − D10 phải đủ dài để PC xử lý xong ảnh trước khi hàng tới điểm dừng; nếu không, hàng sẽ bị dừng vì "chưa có kết quả" (M22).
- PC phải trả kết quả **đúng thứ tự** các lần kích Y1 (FIFO).
- Khi đổi M5 giữa giả lập/thật, D600 nhảy giá trị → nhấn M6 xóa hàng đợi trước.
