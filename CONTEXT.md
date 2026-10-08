# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ test suite `TestExtractApp`, Release Config Persistence, Continuous Flow, Pipeline Tracking, PLC Tags CSV, Handshake Bypass, OQC Scanner, Crosshair Overlay).
- **Cấu hình OTA xưởng mặc định**: 
  - `UpdateServerUrl = "http://192.168.1.192/update/version.json"`
  - `PublishServerUploadUrl = "http://192.168.1.192/ota_server.php"`
  - `PublishServerStorageFolder = "update"`

## 3. Hoàn thành Task 396: Khắc Phục Lỗi Mất Link Server OTA Khi Build App & Chạy Test
1. **Nguyên nhân gốc (Root Cause)**:
   - Các bài kiểm thử cấu hình (`ReleaseConfigPersistenceTests.cs` Test 4/6 và `ManualInspectionTest.cs` Test 6c/6d) khởi tạo `GlobalAppSettingsService` mặc định, ghi đè trực tiếp cấu hình dummy (`10.0.0.99`, `192.168.1.200:9090`) vào file `%AppData%\Vision2026\global_settings.json`.
   - MSBuild Target `SyncReleaseConfigurations` trong `VisionInspectionApp.UI.csproj` chạy `AfterTargets="Build"`, sao chép file cấu hình bị test làm hỏng từ AppData vào thư mục build (`TargetDir`), làm mất URL OTA chuẩn của xưởng.
2. **Giải pháp xử lý triệt để**:
   - **Isolated Sandbox cho Test Suite**: Bổ sung constructor `GlobalAppSettingsService(string? customSettingsFilePath, bool disableBackupSync = false)`. Chuyển toàn bộ bài test sang thư mục tạm độc lập (`Path.GetTempPath()`), tự động dọn dẹp trong block `finally`, tuyệt đối không đụng vào thư mục `%AppData%` hay production backup.
   - **Production Safe Guard**: Bổ sung hàm `SanitizeProductionUrls()` tự động phát hiện và loại bỏ các dummy IP từ môi trường test (`10.0.0.99`, `192.168.1.200`, `127.0.0.1`, `localhost`), tự động phục hồi về URL chuẩn xưởng `192.168.1.192`.
   - **Chuẩn hóa Default URLs**: Đồng bộ dải IP `192.168.1.192` trên toàn bộ model và service ([GlobalAppSettings.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/GlobalAppSettings.cs), [OtaPublishModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Ota/OtaPublishModel.cs), [OtaUpdateViewModel.Publisher.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/OtaUpdateViewModel.Publisher.cs), [OtaUpdateService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/OtaUpdateService.cs)).
   - **Nút Khôi phục trên UI**: Bổ sung nút `↺ Khôi Phục Mặc Định (192.168.1.192)` kèm lệnh `ResetDefaultUrlsCommand` trên cả 2 Tab (Kiểm tra cập nhật & Đóng gói phát hành) trong [OtaUpdateDialog.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OTA/OtaUpdateDialog.xaml).
3. **Kiểm thử và xác thực**:
   - `dotnet run --project TestExtractApp config-persist`: 6/6 tests PASSED 100%.
   - File cấu hình `%AppData%` và thư mục `bin\Debug\net8.0-windows` giữ nguyên 100% link `http://192.168.1.192/...` sau khi build lại toàn bộ app.

## 4. Các sự kiện & thay đổi gần đây
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
