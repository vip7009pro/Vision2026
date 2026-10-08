# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ test suite `TestExtractApp`, Continuous Flow, Pipeline Tracking, PLC Tags CSV, Handshake Bypass, OQC Scanner, Crosshair Overlay).

## 3. Hoàn thành Task 393: Chuẩn Hóa Chu Trình 4 Bước Chế Độ "Cú Đấm Thép" Trên Tab OQC Scanner (Mặc Định Bật)
1. **Chu trình 4 bước chuẩn xác**:
   - **Bước 1 (Mở Job & Tạo Phiên)**: Khi chưa có Job mở, scan label mở Job tương ứng trong DB, tạo phiên mới (`CurrentJobTestedCount = 0`). Text scanned được cắt và hiển thị theo quy tắc cấu hình OQC, không tự xóa textbox, giữ nguyên giá trị.
   - **Bước 2 (Kiểm tra mẫu)**: Công nhân cho mẫu vào gá, bấm Space / Ctrl+F8 để kiểm tra; kết quả hiển thị và lưu lịch sử; textbox giữ nguyên mã scan không bị xóa.
   - **Bước 3 (Lặp lại phiên)**: Bấm Space / Ctrl+F8 luân phiên kiểm tra mẫu và chuyển về Live View cho tới khi hết mẫu của phiên. Trong suốt phiên, phím Enter từ scanner (khi cùng mã phiên) bị triệt tiêu hoàn toàn tránh nạp lại/xóa text.
   - **Bước 4 (Scan mã tiếp theo & chu trình lặp lại)**: Công nhân scan mã tiếp theo (mã khác), hệ thống tự động cắt chuỗi hiển thị lên textbox và load Job tiếp tương ứng, chu trình lặp lại trơn tru từ bước 1.
2. **Kiểm thử tự động**:
   - Bổ sung Test 10 trong [OqcLiveViewOnJobLoadTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/OqcLiveViewOnJobLoadTests.cs) kiểm tra toàn diện Model, ViewModel, View KeyBinding, Text Retention, `IsSameAsCurrentSessionCode`; 100% test PASSED.

## 4. Các sự kiện & thay đổi gần đây
- Task 393: Bổ sung Chế độ "Cú đấm thép" trên Tab OQC Scanner (mặc định Checked, bảo toàn chuỗi mã scan trên TextBox, vô hiệu hóa phím Enter từ scanner khi đã mở Job, luân phiên Space/Ctrl+F8 kiểm tra mẫu/Live View).
- Task 392: Chuẩn hóa chu trình dịch bit hàng đợi FX5U theo xung kết quả Vision Done (`M103`) thay vì cảm biến `X2` ([Ladder_Program_Full_FX5U.md](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Ladder_Program_Full_FX5U.md) và [Ladder_Mnemonic_GXWorks.il](file:///g:/NODEJS/Vision2026/PLC_Programs/Mitsubishi_GXWorks3/Ladder_Mnemonic_GXWorks.il)).
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
