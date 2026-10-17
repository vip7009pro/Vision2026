# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ 15/15 test suite OQC Scanner Live View, 8/8 test MvpShapeMatch2 Preprocess, 6/6 test Seeding Config, 6/6 test Backup & DB Match).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`

## 3. Hoàn thành Task 403: Tự Động Lưu Job Đang Mở Khi Có Thay Đổi Trước Khi Tải Lên Server (JobManager)
1. **Yêu cầu & Mục tiêu**:
   - Trong cửa sổ Quản lý Job và huấn luyện job (`JobManagerWindow`), có 3 nút có chức năng tải Job lên server:
     - Nút 1: "📤 Tải Job Lên Server" trên Toolbar (`UploadCurrentJobCommand`).
     - Nút 2: "🚀 Up Ảnh + Job" trên Toolbar (`UploadTeachAndJobCommand`).
     - Nút 3: "📤 Tải Job Hiện Tại Lên Server" trong Panel Chi Tiết bên phải (`UploadCurrentJobCommand`).
   - Nếu Job đang mở trong Tool Editor chưa được lưu (do có thay đổi - `IsDirty == true` hoặc chưa có file trên đĩa), phải lưu Job trước rồi mới up lên Server.
2. **Giải pháp đã thực hiện**:
   - **Xây dựng phương thức cốt lõi `EnsureActiveJobSavedBeforeUploadAsync`**:
     - Kiểm tra trạng thái Job đang mở trong Tool Editor (`hasActiveJob`).
     - Xác định điều kiện cần lưu: `_toolEditorViewModel.IsDirty || !File.Exists(CurrentJobFilePath)`.
     - Nếu Job mới chưa có đường dẫn: Tự động gán đường dẫn file mặc định `{ProductCode}.job` trong thư mục Jobs chuẩn (`JobRootDirectory` hoặc `AppStoragePaths.JobsDirectory`).
     - Thực thi lưu Job trên UI Dispatcher: Chuẩn bị môi trường sản xuất (chuyển URL/File ảnh mẫu sang Camera OQC thực tế) qua `PrepareJobForProductionUpload()` và gọi `SaveJob()`.
     - Cập nhật thông báo trạng thái `💾 Đã lưu Job '{Path.GetFileName(finalJobPath)}' thành công. Đang tải lên Server...`.
   - **Tích hợp vào cả 3 nút tải Job lên Server**:
     - `ExecuteUploadCurrentJobAsync` (Nút 1 & Nút 3): Tự động lưu Job đang mở trước khi upload; nếu Tool Editor không có Job nào đang mở thì mới cho phép người dùng chọn tệp từ đĩa qua `OpenFileDialog`.
     - `ExecuteUploadTeachAndJobAsync` (Nút 2): Tự động lưu Job đang mở trước khi chụp ảnh mẫu và tải đồng thời ảnh + Job lên Server.
   - **Module hóa theo Quy tắc 5**:
     - Tách toàn bộ các phương thức Upload (`EnsureActiveJobSavedBeforeUploadAsync`, `ExecuteUploadCurrentJobAsync`, `ExecuteUploadTeachAndJobAsync`, `TryUploadTeachImageAsync`, `TryUploadJobAsync`, `ExecuteUploadTeachFromCameraAsync`, `ExecuteUploadTeachFromFileAsync`) sang partial class [JobManagerViewModel.Upload.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/JobManagerViewModel.Upload.cs).
     - Giảm kích thước tệp [JobManagerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/JobManagerViewModel.cs) từ gần 1.500 dòng về ~1.000 dòng.
3. **Kết quả xác thực**:
   - Bổ sung test tự động `Test_JobManager_AutoSaveUnsavedJobBeforeUploadAsync` vào [RemoteServerAndJobManagerTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/RemoteServerAndJobManagerTests.cs).
   - Kiểm tra 3 kịch bản: Job có thay đổi `IsDirty = true`, Job mới chưa có đường dẫn tệp, và Tool Editor trống: Đều vượt qua 100%.
   - Toàn solution build 0 Errors, 15/15 test OQC Scanner Live View PASSED 100%.

## 4. Các sự kiện & thay đổi gần đây
- Task 403: Tự động lưu Job đang mở khi có thay đổi trước khi tải lên Server trong cửa sổ Quản lý Job & Huấn luyện (áp dụng cho cả 3 nút upload); module hóa `JobManagerViewModel.Upload.cs`.
- Task 402: Bảo mật mật khẩu xóa lịch sử OQC Log (mặc định 1234, cấu hình trong OQC Settings) & Checkbox không tên lọc PASS (ẩn NG).

- Task 401: Chuẩn hóa chu trình 3 bước OQC Scanner Cú đấm thép (tự động nạp Job thứ 2 & reset counting mẫu về 0, không cần đóng Job thủ công).
- Task 400: Khắc phục triệt để lỗi khớp điểm Origin MvpShapeMatch2 với ảnh qua tiền xử lý (đồng bộ tiền xử lý template, FNV-1a cache key checksum, Max Pooling 3x3 pyramid, căn góc 0.0° và refine đa candidate Level 0).
- Task 399: Sửa lỗi không lưu lịch sử kiểm tra khi kiểm tra nhiều lần trong 1 phiên Job (mỗi lần kiểm tra đều lưu bản ghi lịch sử và log DB).
- Task 398: Tự động đóng cửa sổ Quản lý Job & Huấn luyện khi bấm Huấn Luyện Từ Xa sau khi nạp xong Job và ảnh mẫu lên Tool Editor.
- Task 397: Khắc phục triệt để lỗi mất dữ liệu cấu hình OQC Scanner & Tra cứu Database khi build app và chạy test; thiết lập Isolated Sandbox và Safe Guard bảo vệ CSDL xưởng CMS_VINA; thêm nút khôi phục xưởng trên UI.
- Task 396: Khắc phục triệt để lỗi mất link Server OTA khi build app & chạy test suite; thiết lập Isolated Sandbox cho unit test và Safe Guard bảo vệ cấu hình sản xuất.
- Task 395: Tự động Focus & Select All Scanned Text trên Tab OQC Scanner (ghi đè tự động chuỗi mã khi scan, không cần xóa thủ công).
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
