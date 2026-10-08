# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ test suite `TestExtractApp`, SystemConfigBackup & OQC DB Match, Release Config Persistence, OQC Scanner Live View, Crosshair Overlay).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`

## 3. Hoàn thành Task 397: Khắc Phục Lỗi Mất Dữ Liệu Cấu Hình OQC Scanner & Tra Cứu Database
1. **Nguyên nhân gốc (Root Cause)**:
   - Bài test `SystemConfigBackupAndOqcDbMatchTests.cs` (Test 6) restore cấu hình test giả lập (`machine-b-guid`, `OQC_MASTER`, query rỗng) qua `OqcScannerService.SaveConfig()`, ghi đè thẳng vào `%AppData%\Vision2026\oqc_scanner_config.json` và hạt giống `configs\system`.
   - `DbManagerService.SaveToDisk()` luôn gọi `SyncConfigToAppBackup` ghi đè CSDL hạt giống thành dummy DB (`MES_PRODUCTION`, `machine-b-guid`).
   - Target `SyncReleaseConfigurations` của MSBuild copy các file hỏng này vào bản build khiến app mở lên bị mất toàn bộ cấu hình CSDL và SQL queries thực tế của nhà máy CMS_VINA.
2. **Giải pháp xử lý triệt để**:
   - **Isolated Sandbox cho Test Suite**: Bổ sung constructor `OqcScannerService(string? customConfigFilePath, string? customHistoryFilePath, bool disableBackupSync = false)` và `DbManagerService(string? customConfigFilePath, bool disableBackupSync = false)`. Toàn bộ unit test chạy trên file tạm trong thư mục Temp, tuyệt đối không chạm vào AppData hay file hạt giống backup.
   - **Production Safe Guard (`SanitizeProductionConfig`)**: Tự động phát hiện và thanh lọc các ID dummy test (`machine-a-guid`, `machine-b-guid`...), tự động bảo toàn kết nối CSDL và các câu lệnh SQL tra cứu thực tế của xưởng CMS_VINA.
   - **Nút Khôi phục trên UI**: Bổ sung nút `↺ Khôi Phục Xưởng (CMS_VINA)` kèm lệnh `ResetFactorySettingsCommand` trên [OqcSettingsDialog.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/OqcSettingsDialog.xaml) để phục hồi ngay toàn bộ cấu hình xưởng chỉ với 1 click.
   - **Đồng bộ hạt giống sạch**: Khôi phục lại bản chuẩn xưởng CMS_VINA trên toàn bộ thư mục hạt giống `configs\system` và `%AppData%`.
3. **Kiểm thử và xác thực**:
   - `dotnet run --project TestExtractApp backup`: 6/6 tests PASSED 100%.
   - `dotnet run --project TestExtractApp config-persist`: 6/6 tests PASSED 100%.
   - `dotnet run --project TestExtractApp oqc`: 12/12 tests PASSED 100%.
   - `dotnet build VisionInspectionApp.slnx`: 0 Error(s).
   - Xác nhận dữ liệu trong AppData và build output giữ nguyên 100% CSDL `CMS_VINA` (192.168.1.2:6789), các câu truy vấn SQL thực tế và Server URL `https://192.168.1.192/vision_upload.php`.

## 4. Các sự kiện & thay đổi gần đây
- Task 397: Khắc phục triệt để lỗi mất dữ liệu cấu hình OQC Scanner & Tra cứu Database khi build app và chạy test; thiết lập Isolated Sandbox và Safe Guard bảo vệ CSDL xưởng CMS_VINA; thêm nút khôi phục xưởng trên UI.
- Task 396: Khắc phục triệt để lỗi mất link Server OTA khi build app & chạy test suite; thiết lập Isolated Sandbox cho unit test và Safe Guard bảo vệ cấu hình sản xuất.
- Task 395: Tự động Focus & Select All Scanned Text trên Tab OQC Scanner (ghi đè tự động chuỗi mã khi scan, không cần xóa thủ công, hạn chế tối đa chạm bàn phím).
- Task 394: Bổ sung phím tắt ESC đóng Job và xóa ô nhập mã TextBox trên Tab OQC Scanner (1 chạm không pop-up, đồng bộ nút UI "🔒 Đóng Job (ESC)").
- Task 393: Bổ sung Chế độ "Cú đấm thép" trên Tab OQC Scanner (mặc định Checked, bảo toàn chuỗi mã scan trên TextBox, vô hiệu hóa phím Enter từ scanner khi đã mở Job, luân phiên Space/Ctrl+F8 kiểm tra mẫu/Live View).
- Task 392: Chuẩn hóa chu trình dịch bit hàng đợi FX5U theo xung kết quả Vision Done (`M103`) thay vì cảm biến `X2`.
- Task 391: Sửa lỗi không truyền bit NG M105 sang GXWorks & Kích hoạt Handshake khi test ảnh trên Tool Editor.
- Task 390: Giải quyết triệt để lỗi Bắt Cạnh PLC (`LDP X0`) không kích hoạt `SET Y0` từ nút Momentary HMI & Nâng cấp Minimum Hold Duration (100ms).
- Task 389: Bắt tay Bất đồng bộ Non-blocking & Hàng đợi 20 phôi BSFLP M200..M219 dừng đúng Điểm Ra Chỉ Định ngoài buồng.
- Task 388: Sửa lỗi mất cấu hình PLC khi build lại app & Thêm cơ chế Simulate PLC Auto-Ack và Fast Direct Read cho kiểm thử Handshake.
- Task 387: Hiển thị Trực Quan Trạng Thái PLC Handshake trên UI Tool Editor & Phân tích Trigger Flow.
- Task 386: Hướng dẫn Thông số Cài Đặt Handshake & Tags trên App Vision WPF.
- Task 385: Chuẩn hóa lệnh dịch bit `BSFRP` cho PLC FX5U (thay thế lệnh lỗi thời `SFT` của FX3U).
- Task 384: Biên soạn bộ mã Ladder Diagram (LD) đầy đủ 9 Networks cho PLC FX5U.
- Task 383: Sửa lỗi cú pháp Structured Text (ST) GX Works 3 `0x110E1A02`.
- Task 382: Chuẩn hóa 100% định dạng CSV GX Works 3 (1.080J) nạp Global Labels và Device Comments.
- Task 381: PLC FX5U Handshake, cơ chế dừng NG ngoài buồng In-Flight Tracking & xem lại ảnh 20 nấc lịch sử.
- Task 380: OQC Scanner 1 nút Space / Ctrl+F8 luân phiên Kiểm tra & Live View, Crosshair căn tâm mặc định bật cho Job Camera Settings.
- Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN (`Parallel.For`, cap 4-8 luồng).
- Task 378: Tối ưu hiệu năng/UX Tool Editor (cache preview Final, chống rò rỉ RAM Mat, thuật toán Coordinate Descent cho Auto Tune).
- Task 377: Sửa lỗi Copy/Paste nhiều node trong Tool Editor & thêm Preprocess Auto Tuner.
- Task 376: Ô nhập SỐ ĐO THỰC TẾ (mm) riêng cạnh nút "Đặt Hệ Số Calib" (không ghi đè Spec).
