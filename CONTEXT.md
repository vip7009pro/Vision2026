# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ 14/14 test suite OQC Scanner Live View, 8/8 test MvpShapeMatch2 Preprocess, 6/6 test Seeding Config).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`

## 3. Hoàn thành Task 401: Chuẩn Hóa Chu Trình 3 Bước Tab OQC Scanner Chế Độ Cú Đấm Thép
1. **Yêu cầu & Phản ánh của công nhân**:
   - Tuần tự chuẩn:
     1. User scan load Job đầu tiên (có ký tự Enter).
     2. Bấm Space hoặc Ctrl+F8 để kiểm tra, bấm lần nữa để Live View, luân phiên cho tới khi hết phiên.
     3. User scan Job thứ 2 (có ký tự Enter) -> Nạp Job và reset counting mẫu về 0.
   - Vấn đề: Scan Job thứ 2 không load được mà phải đóng Job đã rồi mới scan sang Job tiếp theo được.
2. **Nguyên nhân gốc rễ**:
   - Phím Enter bị chặn khi cùng mã phiên: Trong `OqcScannerView.xaml.cs`, `IsSameAsCurrentSessionCode` set `e.Handled = true` nuốt chửng Enter khi công nhân quét tem mở phiên mới cho cùng model/job.
   - Cờ `IsJobLoadedFromManager == true` chặn không cho query DB khi quét mã mới, ép gọi `RunJob()`.
   - Khi công nhân click chuột xem ảnh hay bảng kết quả ở bước 2, TextBox mất focus; ký tự từ máy quét barcode không vào được TextBox và phím Enter bị trượt vì `ScanCommand` chỉ nằm trong TextBox InputBindings.
   - `ExecuteScanInternalAsync()` thiếu dòng lệnh `CurrentJobTestedCount = 0;` khi nạp Job mới.
3. **Giải pháp đã thực hiện**:
   - **Tách biệt lệnh**: Bổ sung `ScanOrRunJobCommand` cho nút UI "CHẠY JOB (SPACE / Ctrl+F8)" (chạy kiểm tra khi có Job), dành riêng phím Enter cho `ScanCommand` (luôn nạp Job / tạo phiên mới / reset counting).
   - **Global Barcode Input Routing**: Bổ sung `PreviewTextInput` tự động focus và `SelectAll()` ô nhập mã ngay khi ký tự đầu tiên từ đầu đọc barcode bắn tới, dù công nhân vừa click chuột vào ảnh hay bảng kết quả.
   - **Xử lý nạp Job & Reset bộ đếm**:
     - Khi scan mã mới: tự động giải phóng cờ `IsJobLoadedFromManager`, dọn pending cũ, nạp Job mới từ DB và reset `CurrentJobTestedCount = 0`.
     - Khi scan lại cùng mã: tự động làm mới phiên Job, reset `CurrentJobTestedCount = 0`, chuyển về Live View camera sẵn sàng kiểm tra phiên mới.
   - **Đồng bộ KeyBinding**: Thêm `KeyBinding Enter` vào `UserControl.InputBindings` để đảm bảo phím Enter luôn kích hoạt nạp Job.
4. **Kết quả xác thực**:
   - Toàn bộ 14/14 tests OQC Scanner PASSED 100% (bổ sung Test 14 trong [OqcLiveViewOnJobLoadTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/OqcLiveViewOnJobLoadTests.cs)).
   - Solution biên dịch 0 Errors cả Debug và Release.

## 4. Các sự kiện & thay đổi gần đây
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
- Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN (`Parallel.For`, cap 4-8 luồng).
- Task 378: Tối ưu hiệu năng/UX Tool Editor (cache preview Final, chống rò rỉ RAM Mat, thuật toán Coordinate Descent cho Auto Tune).
