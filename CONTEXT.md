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

## 3. Hoàn thành Task 402: Bảo Mật Mật Khẩu Xóa Lịch Sử OQC Log & Checkbox Lọc PASS
1. **Yêu cầu & Mục tiêu**:
   - Tab OQC Scanner, trong cửa sổ xem lịch sử OQC log (`OqcScanHistoryWindow`), nút xóa dữ liệu khi bấm vào phải nhập mật khẩu thì mới cho xóa (mặc định 1234), có thể cấu hình trong cửa sổ cấu hình OQC.
   - Thêm 1 checkbox không tên, mặc định unchecked (khi checked, chỉ show các kết quả được đánh giá pass, các kết quả NG ẩn đi).
2. **Giải pháp đã thực hiện**:
   - **Cấu hình mật khẩu**:
     - Thêm thuộc tính `DeleteHistoryPassword` (mặc định "1234") vào `OqcScannerConfig`, hỗ trợ serialization JSON và cấu hình xưởng `CreateFactoryStandard()`.
     - Thêm binding `DeleteHistoryPassword` vào `OqcScannerViewModel.Settings.cs` (tự động nạp, lưu, khôi phục xưởng và xuất/nhập tệp JSON).
     - Thêm mục GroupBox "8. Bảo mật & Mật khẩu Xóa Lịch Sử OQC (Delete History Password)" trong `OqcSettingsDialog.xaml`.
   - **Hộp thoại xác thực mật khẩu**:
     - Tạo `OqcPasswordPromptDialog` (.xaml/.xaml.cs) giao diện trực quan, tự động focus PasswordBox, phím tắt Enter/Esc, hiển thị cảnh báo lỗi màu đỏ khi sai mật khẩu.
     - Hàm tĩnh tiện ích `OqcPasswordPromptDialog.PromptPassword(owner, expectedPassword)` kiểm soát chặt chẽ quyền xóa.
   - **Bảo vệ thao tác xóa dữ liệu**:
     - Trong `OqcScanHistoryWindow.xaml.cs`: Bổ sung kiểm tra mật khẩu trước khi cho phép xóa với tất cả các thao tác: Xóa toàn bộ lịch sử (`BtnClearAllHistory_Click`), Xóa các dòng đã chọn (`BtnDeleteSelected_Click`), và Xóa từng dòng (`DeleteRowBtn_Click`).
   - **Checkbox không tên lọc PASS (ẩn NG)**:
     - Thêm `<CheckBox x:Name="ChkOnlyPass">` không tên, mặc định `IsChecked="False"` trên thanh Top Header & Filter Bar cạnh ô tìm kiếm.
     - Hàm nhận diện chuẩn `OqcScannerViewModel.IsPassResult()` (chấp nhận "PASS", "PASS (OK)", "OK").
     - Khi `ChkOnlyPass` được tick: Bộ lọc `FilterHistory` chỉ giữ lại các bản ghi PASS, các bản ghi NG (NG (LỖI), FAIL, LỖI TRA CỨU DB, LỖI BỘ LỌC MÃ...) bị ẩn đi.
     - Khi bỏ tick hoặc bấm "Xóa lọc": Hiển thị đầy đủ tất cả bản ghi.
3. **Kết quả xác thực**:
   - Bổ sung Test 15 vào `OqcLiveViewOnJobLoadTests.cs`: Kiểm thử toàn diện giá trị mặc định, đổi mật khẩu, xác thực đúng/sai, nhận diện chuỗi PASS/NG và lọc ẩn NG.
   - 15/15 tests OQC Scanner PASSED 100%. Toàn solution build 0 Errors.

## 4. Các sự kiện & thay đổi gần đây
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
