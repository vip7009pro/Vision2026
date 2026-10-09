# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ 13/13 test suite OQC Scanner Live View bao gồm Test 13 Multi-Inspect History Retention, Remote Server & Job Manager, Config Persistence, SystemConfigBackup & OQC DB Match).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`

## 3. Hoàn thành Task 399: Sửa Lỗi Không Lưu Lịch Sử Kiểm Tra Khi Kiểm Tra Nhiều Lần Trong 1 Phiên Job
1. **Yêu cầu & Vấn đề**:
   - Khi mở 1 phiên Job trên màn hình Tab OQC Scanner, công nhân chụp và kiểm tra nhiều lần (nhiều sản phẩm/phôi liên tiếp bằng Space / Ctrl+F8 hoặc nút UI "Chạy Job"), bảng lịch sử OQC log chỉ lưu duy nhất 1 lần cho Job đó, các lần kiểm tra sau bị ghi đè lên dòng cũ hoặc không được ghi nhận vào CSDL.
2. **Nguyên nhân gốc rễ**:
   - Trong `OqcScannerViewModel.HandleInspectionCompletedAsync()`: code dùng `FirstOrDefault(e => e.ScannedCode == processedCode...)` tìm lại dòng đầu tiên và gán đè trực tiếp kết quả, khiến bảng `ScanHistory` luôn chỉ giữ 1 dòng của Job.
   - Trong `RunJob()`: logic tạo dòng placeholder `"Đang kiểm tra..."` bị phân mảnh và chỉ tạo khi nạp Job từ barcode/label, ở các lần chạy thứ 2 trở đi hoặc khi nạp từ Job Manager thì không tạo dòng pending mới.
   - Trong `ExecuteScanAsync()`: nhánh chặn mã trùng vô hiệu hóa nút bấm UI khi đã nạp Job thay vì gọi `RunJob()`.
3. **Giải pháp đã thực hiện**:
   - Chuẩn hóa `RunJob()`: Mỗi lần bấm kiểm tra (lần 1, 2, 3...) khi Job đang mở, nếu chưa có dòng pending thì tạo ngay một bản ghi mới `historyEntry` vào `ScanHistory` với trạng thái `"Đang kiểm tra..."` và thời gian hiện tại (`DateTime.Now`).
   - Chuẩn hóa `HandleInspectionCompletedAsync()`: Tìm dòng pending (`"Đang kiểm tra..."` hoặc `"Đã nạp Job"`). Nếu có thì cập nhật kết quả (PASS/NG, Uuid, chi tiết đo), nếu không có thì tạo mới một bản ghi độc lập và đưa vào `ScanHistory`.
   - Ghi log CSDL (`LogInspectionResultAsync`): Gán `currentEntry.DbLogStatus` trực tiếp lên đúng entry của lần kiểm tra đó.
   - Chuẩn hóa `ExecuteTriggerInspectOrLiveAsync` và `ExecuteScanAsync`: Bấm nút UI hoặc Space/Ctrl+F8 luôn gọi `RunJob()` nếu đã nạp Job.
   - Bổ sung Unit Test 13 trong [OqcLiveViewOnJobLoadTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/OqcLiveViewOnJobLoadTests.cs) kiểm tra chạy liên tiếp 3 lần kiểm tra trong 1 phiên Job, xác nhận 3 bản ghi độc lập với Uuid và kết quả PASS/NG riêng biệt.
4. **Kết quả xác thực**:
   - 13/13 tests OQC Scanner PASSED 100%.
   - Toàn bộ Solution biên dịch 0 Errors.

## 4. Các sự kiện & thay đổi gần đây
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
- Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN (`Parallel.For`, cap 4-8 luồng).
- Task 378: Tối ưu hiệu năng/UX Tool Editor (cache preview Final, chống rò rỉ RAM Mat, thuật toán Coordinate Descent cho Auto Tune).
