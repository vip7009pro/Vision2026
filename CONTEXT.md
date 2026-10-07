# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ test suite `TestExtractApp`, Continuous Flow, Pipeline Tracking, PLC Tags CSV, Handshake Bypass, OQC Scanner, Crosshair Overlay).

## 3. Hoàn thành Task 389: Bắt Tay Bất Đồng Bộ Non-Blocking & Hàng Đợi 20 Phôi BSFLP M200..M219 Dừng Đúng Điểm Ra
1. **Làm rõ nguyên nhân "không thấy M85 -> M70"**:
   - Lệnh cũ `BSFRP M70 K16` (dịch phải) chạy theo xung phôi `X2` buồng chụp trước khi PC có kết quả. Khi PC trả kết quả NG vào `M85`, phôi đã qua `X2`. Nếu không có phôi thứ 2 kích `X2`, lệnh dịch không bao giờ chạy lại, nên `M85` đứng yên. Ngoài ra lệnh dịch phải cố định cứng trạm ra ở nấc 16 (`M70`), không thể tùy biến điểm ra.
2. **Kiến trúc Bắt tay Bất Đồng Bộ Non-Blocking (Pipelined Continuous Conveyor)**:
   - Triệt tiêu hoàn toàn việc chờ đợi PLC Ack: Vision PC kiểm tra xong ghi thẳng kết quả vào PLC/Hàng đợi (`M200`) trong <2ms và giải phóng ngay, băng tải chạy liên tục tốc độ cao (không bao giờ nghẽn luồng).
   - Thêm `NonBlockingMode`, `TargetStopStationIndex` (1..20, mặc định: 10), `QueueRegisterStart` (mặc định: `M200`) vào [PlcIndustrialConfig.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/PlcIndustrialConfig.cs) và [IndustrialHandshakeStateMachine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/PLC/Services/IndustrialHandshakeStateMachine.cs).
   - Bổ sung Card cấu hình Hàng đợi 20 sản phẩm và checkbox Non-blocking trực quan tại Tab 2 [PlcManagerWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/PLC/PlcManagerWindow.xaml).
3. **Chương trình Ladder FX5U Hàng đợi 20 Nấc Thuận (`BSFLP M200 K20`)**:
   - Dịch trái thuận tự nhiên từ Buồng chụp (`M200`) sang các nấc `M201..M219` mỗi khi phôi qua cảm biến `X2`. Nấc `M200` tự động nhận 0 (mặc định OK).
   - Khi PC trả kết quả NG: PC ghi `M200 = 1` (hoặc `M105 = 1` kích `SET M200`).
   - Khi bit tại **Điểm Ra Chỉ Định** (ví dụ Nấc 10 = `M210`) = 1: PLC lập tức dừng băng tải (`RST Y0`), bật Stopper `Y21`, còi đèn `Y22`, bật cờ `M220`.
   - Nút nhấn **Reset / Start của công nhân (`X4`)**: Xóa bit NG tại trạm dừng (`RST M210`), hạ Stopper `Y21`, tắt còi đèn `Y22`, xóa cờ `M220`, và kích hoạt băng tải chạy tiếp (`SET Y0`). Toàn bộ phôi khác trong hàng đợi giữ nguyên!
   - Cập nhật toàn bộ [Ladder_Program_Full_FX5U.md](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Ladder_Program_Full_FX5U.md), [Ladder_Mnemonic_GXWorks.il](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Ladder_Mnemonic_GXWorks.il), và [DeviceComments_GXWorks.csv](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/DeviceComments_GXWorks.csv).

## 4. Các sự kiện & thay đổi gần đây
- Task 391: Sửa lỗi không truyền bit NG M105 sang GXWorks & Kích hoạt Handshake khi test ảnh trên Tool Editor ([IndustrialHandshakeStateMachine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/PLC/Services/IndustrialHandshakeStateMachine.cs) và [ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs)).
- Task 390: Giải quyết triệt để lỗi Bắt Cạnh PLC (`LDP X0`) không kích hoạt `SET Y0` từ nút Momentary HMI & Nâng cấp Minimum Hold Duration (100ms) trong [HmiControlViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/HMI/HmiControlViewModel.cs).
- Task 389: Bắt tay Bất đồng bộ Non-blocking & Hàng đợi 20 phôi BSFLP M200..M219 dừng đúng Điểm Ra Chỉ Định ngoài buồng.
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
