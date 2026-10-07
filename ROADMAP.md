# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 389: Bắt Tay Bất Đồng Bộ Non-Blocking & Hàng Đợi 20 Phôi BSFLP M200..M219 Dừng Đúng Điểm Ra:
  - [x] Làm rõ nguyên nhân gốc "không thấy M85 -> M70": Do lệnh cũ `BSFRP` dịch mảng trước khi có kết quả và khi test 1 phôi đơn lẻ không có phôi tiếp theo kích sensor `X2` nên bit không dịch tiếp.
  - [x] Thiết kế & triển khai cơ chế Non-Blocking Handshake trong [IndustrialHandshakeStateMachine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/PLC/Services/IndustrialHandshakeStateMachine.cs): Vision PC ghi kết quả thẳng vào `QueueRegisterStart` (`M200`) và giải phóng ngay trong <2ms, không chờ PLC Ack, bảo đảm băng tải chạy liên tục tốc độ cao.
  - [x] Nâng cấp [PlcIndustrialConfig.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/PlcIndustrialConfig.cs), [ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs) và [PlcManagerWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/PLC/PlcManagerWindow.xaml): Thêm `NonBlockingMode`, `TargetStopStationIndex` (1..20, mặc định: 10), `QueueRegisterStart` (mặc định: `M200`), và card giao diện cấu hình trực quan tại Tab 2.
  - [x] Viết lại toàn diện chương trình Ladder Diagram FX5U trong [Ladder_Program_Full_FX5U.md](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Ladder_Program_Full_FX5U.md) và [Ladder_Mnemonic_GXWorks.il](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Ladder_Mnemonic_GXWorks.il) dùng lệnh dịch trái chuẩn `BSFLP M200 K20`:
    - [x] Mạng 1: Chạy / Dừng băng tải chính `Y0`.
    - [x] Mạng 3: Sensor buồng `X2` kích Trigger Camera (Line0/`M10`) và phát xung `M20` dịch mảng `BSFLP M200 K20` (nấc `M200` tự nạp 0).
    - [x] Mạng 4: Vision PC nạp kết quả NG `M105=1` $\rightarrow$ `SET M200` bất đồng bộ, không chờ Ack.
    - [x] Mạng 5: Phôi NG tới Điểm Ra Chỉ Định (Nấc 10: `M210 = 1`) $\rightarrow$ Dừng băng tải ngay (`RST Y0`), bật Stopper `Y21`, còi đèn `Y22`, cờ `M220`.
    - [x] Mạng 6: Công nhân xử lý hàng NG xong nhấn `X4` $\rightarrow$ Reset bit `M210 = 0`, hạ Stopper `Y21`, tắt còi đèn `Y22`, xóa cờ `M220`, và tiếp tục chạy băng tải `SET Y0`. Các phôi khác trong hàng đợi bảo lưu nguyên vẹn.
  - [x] Cập nhật danh bạ [DeviceComments_GXWorks.csv](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/DeviceComments_GXWorks.csv) chuẩn UTF-16 LE Tab-delimited cho toàn bộ dải `M200..M219`.
  - [x] Toàn bộ solution biên dịch 0 lỗi, kiểm thử tự động PASSED 100%.

- [x] Task 388: Khắc phục Mất Cấu Hình PLC Khi Build & Cơ Chế Test Handshake Giả Lập GX Works 3:
  - [x] Rà soát và tìm ra nguyên nhân gốc lỗi mất cấu hình PLC khi build lại app: Bộ test `SystemConfigBackupAndOqcDbMatchTests.cs` nạp cấu hình trống và ghi đè `%AppData%\Vision2026\plc_config.json`. Khắc phục bằng cách cô lập môi trường test sang thư mục tạm độc lập (`TestPlcConfigHelper.CreateIsolatedPlcManager()`).
  - [x] Bổ sung cơ chế tự động lưu an toàn `AutoSaveOnClose()` khi đóng cửa sổ PLC Manager và khôi phục 100% các giá trị Tag PLC FX5U chuẩn (`M101`..`M105`, `M10`, `M11`, `M100`, `M108`, `Y21`, `D1000`, `D1002`).
  - [x] Giải quyết vấn đề kiểm thử Handshake khi không có PLC thật / giả lập GX Works 3:
    - [x] Bổ sung tính năng `SimulatePlcAck` (Mô Phỏng PLC Tự Động Ack) ngay trong Handshake State Machine và giao diện Tab 2 PLC Manager, tự động phát xung Ack (`M11`) trễ 20ms và tự động hạ khi xong chu trình. Cho phép test 100% flow offline mà không cần PLC.
    - [x] Bổ sung cơ chế Fast Direct Read: Sau 30ms nếu Cache chưa cập nhật, State Machine tự động đọc trực tiếp từ Driver xuống PLC để triệt tiêu độ trễ Polling Engine.
    - [x] Làm rõ nguyên lý giả lập GX Works 3 (không mở cổng MC Protocol TCP thật) và hướng dẫn kết nối qua MX Component.
  - [x] Toàn bộ solution biên dịch 0 lỗi, kiểm thử tự động PASSED 100%.

- [x] Task 387: Hiển thị Trực Quan Trạng Thái PLC Handshake trên UI Tool Editor & Phân tích Trigger Flow:
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

- [x] Task 387: Hiển thị Trực Quan Trạng Thái PLC Handshake trên UI Tool Editor & Phân tích Trigger Flow:
  - [x] Phân tích & làm rõ 2 luồng chụp ảnh: Hardware Trigger (Cảm biến/PLC kích xung chân vật lý LINE0 vào camera độ trễ micro-giây) vs Software Trigger qua mạng (PLC bật bit `M10` quét MC Protocol).
  - [x] Thiết kế & triển khai [ToolEditorViewModel.HandshakeUi.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.HandshakeUi.cs): module hóa logic giám sát trạng thái bắt tay, tự động cập nhật real-time theo sự kiện StateChanged, TagChanged, ConnectionStateChanged.
  - [x] Bổ sung Widget PLC Handshake đa năng trên Top Toolbar [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml): Tên PLC, Trạng thái (ARMED/BUSY/CHỜ ACK/OFFLINE), 4 đèn LED mini (`RDY`, `BSY`, `DON`, `ACK`), ToolTip thông tin toàn diện và nhấp chuột mở ngay cửa sổ PLC Manager.
  - [x] Bổ sung tóm tắt Handshake I/O tại thanh trạng thái StatusBar dòng dưới cùng.
  - [x] Toàn bộ solution biên dịch 0 lỗi, kiểm thử tự động đạt 100%.

## Định hướng tiếp theo
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
- [ ] Tối ưu hóa render đa luồng (Multi-threaded Rendering) cho tài liệu PDF kích thước lớn >50MB.



