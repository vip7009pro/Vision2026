# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

## Các nhiệm vụ gần đây & Đang triển khai

- [x] Task 376: Tách ô nhập SỐ ĐO THỰC TẾ (mm) thành field riêng cạnh nút "Đặt Hệ Số Calib" để không ghi đè Spec.
- [x] Task 377: Sửa lỗi Copy/Paste nhiều node trong Tool Editor & thêm nút Auto Tune cho công cụ nhận diện.
- [x] Task 378: Tối ưu hiệu năng/UX Tool Editor (cache preview Final, chống rò rỉ RAM Mat, thuật toán Coordinate Descent).
- [x] Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN (`Parallel.For`, cap 4-8 luồng).
- [x] Task 380: OQC Scanner thao tác 1 nút Space / Ctrl+F8 luân phiên & Bật tắt Crosshair căn tâm trong Cửa sổ Cấu hình Camera/Đèn Job.
- [x] Task 381: Rà soát & Chuẩn hóa Handshake PLC FX5U, Cơ chế Dừng NG Ngoài Buồng & Xem Lại Ảnh 20 Nấc:
  - [x] Rà soát và sửa máy trạng thái Handshake FX5U: Khắc phục lỗi đọc/ghi vùng `X`/`Y`, chuyển sang bit nội bộ `M101`..`M105`, `M10`, `M11` giao thức MC Protocol.
  - [x] Nâng cấp `IndustrialHandshakeStateMachine.cs`: Dynamic Tag Resolution tự động tương thích cả bit `M101`..`M105` và tag chuẩn `Y1`..`Y5`.
  - [x] Lập trình `POU_04_ShiftRegister_Reject.st`: Hàng đợi tracking 20 phôi FIFO độc lập (In-Flight Pipelined Tracking). Hàng NG không dừng trong buồng; các phôi kế tiếp vẫn trigger kiểm tra bình thường; dừng chính xác phôi NG tại trạm chỉ định ngoài buồng bằng Stopper `Y21`, còi đèn `Y22`, dừng băng tải `Y0` (tùy chọn `M120`), reset tự động qua nút `X4`.
  - [x] Cung cấp bản đồ bộ nhớ và sơ đồ Ladder logic mạng chi tiết trong `Ladder_Diagram_Visual.md` và `DeviceComments_GXWorks.csv`.
  - [x] Giãn kích thước 20 nấc sản phẩm gần nhất lên `12x18px`, hỗ trợ hover và click chuột/cảm ứng mượt mà.
  - [x] Bấm vào từng nấc trên thanh 20 nấc để xem lại ảnh OK/NG: Tự động phân giải ảnh từ tệp trên đĩa hoặc ảnh snapshot trong RAM khi Image Output đặt `OnFail`.
  - [x] Thêm banner màu hổ phách khi xem ảnh lịch sử kèm nút "📁 Mở thư mục ảnh" và nút "✕ Quay lại Live".
  - [x] Kiểm thử toàn bộ regression test suite: 100% test PASSED.

- [x] Task 382: Khắc phục lỗi Import CSV vào GX Works 3 (1.080J) & Chuẩn hóa định dạng xuất nhập:
  - [x] So sánh chi tiết mẫu xuất thực tế từ GX Works 3 (`sample Global label.csv` & `sample Local Label.csv`): Chỉ ra 4 khác biệt then chốt (Encoding UTF-16 LE BOM `FF FE`, Delimiter TAB `\t` thay vì `,`, Dòng 0 định danh Sheet/POU `"statemachineST"`, Chuẩn 28 cột và kiểu IEC `BOOL`/`INT`/`DINT`/`REAL`).
  - [x] Nhận diện nguyên nhân gốc hộp thoại lỗi "Unable to open the imported file": (1) Xung đột khóa tệp khi đang mở trong Excel (`FileShare.None`), (2) Lỗi parsing byte do bảng mã UTF-8 gây vỡ định dạng trong GX Works 3 ("The file is broken").
  - [x] Tái tạo lại toàn bộ 38 Tags trong [GlobalLabels_GXWorks3.csv](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/GlobalLabels_GXWorks3.csv) và 44 Devices trong [DeviceComments_GXWorks.csv](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/DeviceComments_GXWorks.csv) chuẩn 100% UTF-16 LE Tab-Delimited tương thích GX Works 3.
  - [x] Nâng cấp [PlcTagCsvService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/PLC/Services/PlcTagCsvService.cs): Hỗ trợ nhận diện dấu TAB `\t`, bỏ qua tên Sheet/COMMON ở dòng 0, đọc cấu trúc 28 cột chuẩn của GX Works 3.
  - [x] Bổ sung Test 6 trong test suite và xác thực 28/28 unit tests PASSED 100%.

- [x] Task 383: Sửa lỗi cú pháp ST GX Works 3 `0x110E1A02`:
  - [x] Phân tích & khắc phục nguyên nhân lỗi ST trong GX Works 3: loại bỏ khối khai báo `VAR...END_VAR` trong khung soạn thảo code ST (chuyển sang bảng Local Label); sửa lỗi gán ngõ vào vật lý `X0 := NOT X0` thành `M108 := SM410`.
  - [x] Tạo file [POU_01_LocalLabels.csv](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/POU_01_LocalLabels.csv) chuẩn 27 cột UTF-16 LE tương thích mẫu Local Label của GX Works 3.
  - [x] Cung cấp 2 phiên bản mã nguồn: [POU_01_Watchdog_Heartbeat.st](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/POU_01_Watchdog_Heartbeat.st) (chuẩn IEC) và [POU_01_Watchdog_Heartbeat_Native.st](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/POU_01_Watchdog_Heartbeat_Native.st) (bản Native Mitsubishi dùng Timer T0 và xung hệ thống SM410, biên dịch F4 ngay 100%).

- [x] Task 384: Bộ mã Ladder Diagram (LD) đầy đủ cho PLC FX5U:
  - [x] Chuyển đổi toàn diện logic hệ thống sang Ladder trực quan 9 Networks trong [Ladder_Program_Full_FX5U.md](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Ladder_Program_Full_FX5U.md).
  - [x] Cung cấp mã Instruction List (IL / Mnemonic) chuẩn GX Works 3 trong [Ladder_Mnemonic_GXWorks.il](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Ladder_Mnemonic_GXWorks.il).
  - [x] Đầy đủ 9 Networks: Watchdog nhịp tim SM410/T0; Trigger bắt tay buồng chụp; Chốt kết quả Ack M11; Hàng đợi In-Flight FIFO dịch bước SFT; Dừng phôi NG ngoài buồng Stopper Y21/còi đèn Y22; Nút nhấn công nhân X4 giải phóng hàng; Bộ đếm sản lượng 32-bit DADD; Liên động băng tải an toàn Y0.

- [x] Task 386: Hướng dẫn Thông số Cài Đặt Handshake & Tags trên App Vision WPF:
  - [x] Xuất tài liệu hướng dẫn kỹ thuật chi tiết [Vision_App_Handshake_Settings_Guide.md](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Vision_App_Handshake_Settings_Guide.md) gồm 7 phần hoàn chỉnh.
  - [x] Chuẩn hóa 2 cách thức cấu hình trên giao diện WPF: Nhập trực tiếp Device Bit (FX5U MC Protocol `M101`..`M105`, `M11`, `M108`) hoặc chọn Tên Nhãn từ Dropdown danh bạ (`Vision_Ready`, `PLC_Ack`...).
  - [x] Lập bảng thông số cụ thể cho toàn bộ 5 Tab chức năng: Kết nối MC Protocol, Bắt tay Handshake 24/7, Watchdog Nhịp tim & Khóa liên động an toàn, Motion & Đọc xung Encoder, Shift Register kích hoạt cơ cấu Stopper ngoài buồng.
  - [x] Hướng dẫn quy trình nạp danh bạ biến qua nút Import CSV và lưu cấu hình JSON tự động.

## Định hướng tiếp theo
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
- [ ] Tối ưu hóa render đa luồng (Multi-threaded Rendering) cho tài liệu PDF kích thước lớn >50MB.


