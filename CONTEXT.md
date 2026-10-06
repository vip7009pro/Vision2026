# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ test suite `TestExtractApp`, Continuous Flow, Pipeline Tracking, PLC Tags CSV, Handshake Bypass, OQC Scanner, Crosshair Overlay).

## 3. Hoàn thành Task 388: Khắc phục Mất Cấu Hình PLC Khi Build & Cơ Chế Test Handshake Giả Lập GX Works 3
1. **Tìm ra nguyên nhân gốc & Sửa triệt để lỗi mất cấu hình PLC khi build lại app**:
   - *Nguyên nhân gốc*: Trong bộ test `TestExtractApp/SystemConfigBackupAndOqcDbMatchTests.cs` (Test 1 và Test 6), mã test khởi tạo `new PlcManagerService()` không cách ly đường dẫn. Khi test phục hồi cấu hình trống, nó đã gọi `SaveGlobalConfig()` và **ghi đè xóa trắng** tệp cấu hình thực của người dùng tại `%AppData%\Vision2026\plc_config.json` mỗi lần chạy build/test!
   - *Khắc phục*: Cách ly 100% môi trường test sang thư mục tạm độc lập qua `TestPlcConfigHelper.CreateIsolatedPlcManager()`.
   - *Tự động lưu*: Bổ sung `AutoSaveOnClose()` trong [PlcManagerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/PLC/PlcManagerViewModel.cs) và override `OnClosing` trong [PlcManagerWindow.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/PLC/PlcManagerWindow.xaml.cs).
   - *Khôi phục dữ liệu*: Nạp lại toàn bộ cấu hình đầy đủ FX5U (`M101`..`M105`, `M10`, `M11`, `M100`, `M108`, `Y21`, `D1000`, `D1002`) vào `%AppData%\Vision2026\plc_config.json` và hạt giống `configs\system\plc_config.json`.
2. **Giải pháp kiểm thử Handshake không cần PLC thật & Khắc phục giả lập GX Works 3**:
   - *Bản chất GX Simulator 3*: Trình giả lập của Mitsubishi không mở TCP Socket vật lý trên Windows (Port 5000/5002) mà chỉ giao tiếp qua IPC nội bộ hoặc ActiveX MX Component. Nếu dùng driver MC Protocol trực tiếp, app sẽ không thấy PLC Ack.
   - *Chế độ Mô phỏng PLC Auto-Ack tích hợp sẵn*: Thêm tùy chọn `SimulatePlcAck` vào [PlcIndustrialConfig.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/PlcIndustrialConfig.cs) và [IndustrialHandshakeStateMachine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/PLC/Services/IndustrialHandshakeStateMachine.cs). Khi bật, app tự động đóng vai PLC phản hồi xung `M11` sau 20ms và tự động hạ `M11=0` khi Vision Done hạ, giúp test 100% flow offline mà không cần bất kỳ phần cứng hay giả lập ngoài.
   - *Fast Direct Read*: Nâng cấp vòng lặp chờ Ack: nếu sau 30ms chưa có trong RAM Cache, State Machine tự động đọc trực tiếp từ Driver xuống PLC để triệt tiêu độ trễ Polling Engine.
   - *Giao diện trực quan*: Thêm checkbox `🧪 Mô Phỏng PLC Tự Động Ack (Simulate Auto-Ack khi test offline)` nổi bật tại Tab 2 cửa sổ [PlcManagerWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/PLC/PlcManagerWindow.xaml).

## 4. Các sự kiện & thay đổi gần đây
- Task 388: Sửa lỗi mất cấu hình PLC khi build lại app & Thêm cơ chế Simulate PLC Auto-Ack và Fast Direct Read cho kiểm thử Handshake không cần PLC thật.
- Task 387: Hiển thị Trực Quan Trạng Thái PLC Handshake trên UI Tool Editor & Phân tích Trigger Flow: Giải thích chi tiết 2 cơ chế chụp Hardware Line0 vs Software MC Protocol M10; Tạo [ToolEditorViewModel.HandshakeUi.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.HandshakeUi.cs) giám sát tự động thời gian thực; Thêm Widget PLC Handshake đa năng trên Toolbar [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml) (Tên PLC, trạng thái, 4 đèn LED mini RDY/BSY/DON/ACK, click mở PLC Manager) và tóm tắt dưới StatusBar.
- Task 386: Hướng dẫn Thông số Cài Đặt Handshake & Tags trên App Vision WPF: Xuất tài liệu [Vision_App_Handshake_Settings_Guide.md](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Vision_App_Handshake_Settings_Guide.md) chi tiết 7 phần; Hướng dẫn cả 2 cách cấu hình (nhập trực tiếp Device Bit MC Protocol M101..M105, M11, M108 hoặc chọn Tag Name danh bạ); Bảng tra cứu đầy đủ 5 Tab giao diện WPF (Kết nối, Handshake, Watchdog, Motion Encoder, Shift Register Reject); Bảng ánh xạ I/O FX5U.
- Task 385: Chuẩn hóa lệnh dịch bit `BSFRP` cho PLC FX5U: Khắc phục lệnh lỗi thời `SFT` (FX3U cũ); Ứng dụng lệnh chuẩn `BSFRP M70 K16` (Bit Shift Right Pulse) dịch mảng 16 nấc từ buồng chụp `M85` về trạm ngoài buồng `M70`; Cập nhật toàn bộ tài liệu [Ladder_Program_Full_FX5U.md](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Ladder_Program_Full_FX5U.md) và [Ladder_Mnemonic_GXWorks.il](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Ladder_Mnemonic_GXWorks.il).
- Task 384: Biên soạn bộ mã Ladder Diagram (LD) đầy đủ 9 Networks cho PLC FX5U: Chuyển đổi toàn bộ logic từ ST sang Ladder trực quan; Tạo tài liệu [Ladder_Program_Full_FX5U.md](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Ladder_Program_Full_FX5U.md) với sơ đồ tiếp điểm ASCII chi tiết, phím tắt vẽ nhanh F5/F6/F7/F8; Cập nhật [Ladder_Mnemonic_GXWorks.il](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Ladder_Mnemonic_GXWorks.il) chuẩn Instruction List cho GX Works 3.
- Task 383: Sửa lỗi cú pháp Structured Text (ST) GX Works 3 `0x110E1A02`: Khắc phục lỗi khai báo `VAR...END_VAR` trong vùng code ST; Sửa lệnh cấm ghi ngõ vào vật lý `X0` thành `M108` / `SM410`; Xuất file [POU_01_LocalLabels.csv](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/POU_01_LocalLabels.csv) cho bảng Local Label; Cung cấp bản [POU_01_Watchdog_Heartbeat_Native.st](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/POU_01_Watchdog_Heartbeat_Native.st) (dùng Timer phần cứng T0 và clock SM410, biên dịch F4 ngay 100%).
- Task 382: Chuẩn hóa 100% định dạng CSV GX Works 3 (1.080J): Nhập thành công Global Labels 28 cột; Khắc phục lỗi 2Row Device Comment (chuẩn hóa header `"Device Name"\t"Comment"`, lược bỏ dòng `"COMMON"` gây lệch hàng khi import trực tiếp trong editor, cung cấp phương pháp Copy-Paste siêu tốc). Cập nhật `PlcTagCsvService.cs` vượt qua 28/28 tests.
- Task 381: PLC FX5U Handshake, cơ chế dừng NG ngoài buồng In-Flight Tracking & xem lại ảnh 20 nấc lịch sử.
- Task 380: OQC Scanner 1 nút Space / Ctrl+F8 luân phiên Kiểm tra & Live View, Crosshair căn tâm mặc định bật cho Job Camera Settings.
- Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN (`Parallel.For`, cap 4-8 luồng).
- Task 378: Tối ưu hiệu năng/UX Tool Editor (cache preview Final, chống rò rỉ RAM Mat, thuật toán Coordinate Descent cho Auto Tune).
- Task 377: Sửa lỗi Copy/Paste nhiều node trong Tool Editor & thêm Preprocess Auto Tuner.
- Task 376: Ô nhập SỐ ĐO THỰC TẾ (mm) riêng cạnh nút "Đặt Hệ Số Calib" (không ghi đè Spec).
