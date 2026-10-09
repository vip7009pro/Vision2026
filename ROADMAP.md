# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 401: Chuẩn Hóa Chu Trình 3 Bước Tab OQC Scanner Chế Độ Cú Đấm Thép (Tự Động Nạp Job Thứ 2 & Reset Counting Mẫu Không Cần Đóng Job):
  - [x] Phân tích nguyên nhân:
    - Hiện tượng 1 (Enter bị chặn khi cùng mã phiên): Trong `OqcScannerView.xaml.cs`, `IsSameAsCurrentSessionCode` set `e.Handled = true` nuốt chửng phím Enter khi công nhân scan tem mở phiên mới cho cùng model/job -> scanner không nạp Job và không reset counting mẫu.
    - Hiện tượng 2 (Chặn bởi cờ Manager): Nếu trước đó Job mở từ Job Manager, `IsJobLoadedFromManager == true` chặn việc nạp Job từ DB và chỉ gọi `RunJob()`.
    - Hiện tượng 3 (Mất focus bàn phím sau khi thao tác/xem ảnh): Khi công nhân click xem ảnh preview hoặc bảng lịch sử ở bước 2, TextBox mất focus; scanner bắn mã tiếp theo không vào TextBox và phím Enter bị trượt vì `ScanCommand` chỉ gắn trên TextBox InputBindings.
    - Hiện tượng 4 (Bộ đếm không reset): Trong `ExecuteScanInternalAsync()`, khi nạp Job mới từ barcode scanner, `CurrentJobTestedCount` bị thiếu lệnh reset về 0.
  - [x] Nâng cấp `OqcScannerViewModel.cs`:
    - Bổ sung `ScanOrRunJobCommand`: Phân tách rành mạch nút UI "CHẠY JOB (SPACE / Ctrl+F8)" (chạy kiểm tra) với phím Enter từ scanner (luôn nạp Job / tạo phiên mới / reset counting).
    - Cập nhật `ExecuteScanAsync()` & `ExecuteScanInternalAsync()`: Khi nhận mã scan, tự động giải phóng cờ `IsJobLoadedFromManager`, dọn dẹp pending cũ, nạp Job mới và reset `CurrentJobTestedCount = 0`.
    - Hỗ trợ quét lại cùng mã phiên: Tự động nhận diện bắt đầu phiên mới cho lô tiếp theo, reset `CurrentJobTestedCount = 0`, chuyển về Live View căn chỉnh sẵn sàng, bảo toàn mã trên ô nhập (Cú đấm thép).
  - [x] Nâng cấp `OqcScannerView.xaml` & `OqcScannerView.xaml.cs`:
    - Tích hợp **Global Barcode Input Routing**: Bắt sự kiện `PreviewTextInput` tự động đưa Focus và `SelectAll()` về `ScanInputTextBox` ngay khi ký tự đầu tiên từ máy quét gửi tới (dù công nhân vừa click chuột vào ảnh hay bảng đo).
    - Cập nhật `PreviewKeyDown`: Phím Enter luôn kích hoạt `ScanCommand` nạp Job mượt mà, không bị chặn.
    - Bổ sung `<KeyBinding Key="Enter" Command="{Binding ScanCommand}" />` vào `UserControl.InputBindings`.
    - Nút UI "CHẠY JOB / QUÉT" liên kết với `ScanOrRunJobCommand`.
  - [x] Kiểm thử tự động: Bổ sung Test 14 vào [OqcLiveViewOnJobLoadTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/OqcLiveViewOnJobLoadTests.cs) kiểm thử toàn vẹn chu trình 3 bước (Scan Job 1 -> Luân phiên Space kiểm tra/Live View -> Scan Job 2 nạp & reset counting mẫu). Toàn bộ 14/14 tests PASSED 100%.

- [x] Task 400: Khắc Phục Triệt Để Lỗi Khớp Điểm Origin Thuật Toán MvpShapeMatch2 Với Ảnh Qua Tiền Xử Lý:
  - [x] Nguyên nhân 1 (Bất đối xứng Preprocess): Trong `OriginMatcher.cs`, Search ROI được tiền xử lý thành `roiGray`, nhưng `templateGray` truyền vào `MvpShapeMatch2Engine.Match` bị bỏ qua không gọi `PreprocessTemplateForMatch(templateGray, preprocess)` như các thuật toán khác (`MatchByPyramid`, `TemplateMatch`).
  - [x] Nguyên nhân 2 (Stale Cache Key): `cacheKey` của `_templateModelCache` trong `MvpShapeMatch2Engine.cs` chỉ chứa tên tool và kích thước mà không có checksum dữ liệu pixel, dẫn đến khi thay đổi tiền xử lý bị dính lại mô hình vector của ảnh cũ.
  - [x] Nguyên nhân 3 (Trượt biên 1px & Cực trị địa phương Pyramid): Coarse Search bỏ qua mốc góc 0.0°; `RefineSearchFast` thiếu Max Pooling 3x3 làm trượt bước nhảy bậc thang 1 pixel của ảnh nhị phân/cạnh; Level 0 có `searchRadius` quá nhỏ và chỉ refine 1 candidate duy nhất nên bị kẹt ở cực trị địa phương với điểm số chỉ 0.5.
  - [x] Nâng cấp `OriginMatcher.cs`: Bổ sung `PrepareTemplateForMatchBorrowed(templateGray, preprocess)` tiền xử lý template đồng bộ với ROI trước khi gọi `MvpShapeMatch2Engine.Match`.
  - [x] Nâng cấp `MvpShapeMatch2Engine.cs`: Thêm FNV-1a `ComputeMatChecksum(templInput)` vào `cacheKey`; căn chỉnh góc qua 0.0°; tích hợp Max Pooling 3x3 vào `RefineSearchFast`; mở rộng `lvl0SearchRadius` và refine đa candidate ở Level 0.
  - [x] Kiểm thử tự động: Bổ sung [MvpShapeMatch2PreprocessTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/MvpShapeMatch2PreprocessTests.cs) kiểm thử 8/8 kịch bản (Grayscale, Binary Threshold, Binary Inverted, Otsu, Canny Edge, Gaussian Blur, Runtime PreprocessSettings, Dynamic Cache Invalidation). Toàn bộ 8/8 tests PASSED 100% với Score = 1.0000, Pos sai số < 0.8px, Angle sai số <= 0.1°. Toàn solution build 0 Errors cả Debug và Release.

- [x] Task 399: Lưu Lịch Sử Kiểm Tra Cho Mọi Lần Chụp Và Kiểm Tra Trong Cùng 1 Phiên Job Trên Tab OQC Scanner:
  - [x] Phân tích nguyên nhân: `HandleInspectionCompletedAsync()` trước đây sử dụng `FirstOrDefault(e => e.ScannedCode == processedCode...)` tìm lại dòng đầu tiên và ghi đè in-place kết quả lên dòng đó thay vì tạo bản ghi mới; `RunJob()` bị thiếu logic tạo dòng pending cho các lần kiểm tra thứ 2 trở đi hoặc khi nạp từ Job Manager; `ExecuteScanAsync()` bị chặn bởi nhánh so sánh mã phiên khiến nút UI không kích hoạt kiểm tra.
  - [x] Nâng cấp `RunJob()`: Tự động phát hiện nếu chưa có dòng pending thì lập tức tạo một bản ghi `OqcScanHistoryEntry` mới với trạng thái `"Đang kiểm tra..."` và thời gian hiện tại (`DateTime.Now`).
  - [x] Nâng cấp `HandleInspectionCompletedAsync()`: Điền kết quả PASS/NG, Uuid, chi tiết đo vào dòng pending hiện hành; nếu không có dòng pending thì tạo mới một bản ghi độc lập đưa vào `ScanHistory`; gán trạng thái `DbLogStatus` trực tiếp trên entry đó.
  - [x] Chuẩn hóa nút bấm UI & Phím tắt: Bấm "Chạy Job" hoặc phím Space / Ctrl+F8 khi đang có Job mở luôn thực hiện `RunJob()` và ghi nhận bản ghi mới.
  - [x] Bảo toàn lịch sử khi đóng Job: Phím `ESC` đóng Job chỉ xóa các dòng pending chưa chạy, các dòng kiểm tra đã hoàn thành (PASS/NG) của phiên đều được bảo toàn nguyên vẹn trong lịch sử.
  - [x] Kiểm thử tự động: Bổ sung Test 13 vào [OqcLiveViewOnJobLoadTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/OqcLiveViewOnJobLoadTests.cs) giả lập 3 lần chụp và kiểm tra liên tiếp trên cùng 1 phiên Job, xác nhận cả 3 lần đều lưu bản ghi riêng với Uuid và kết quả độc lập; 13/13 tests OQC Scanner PASSED 100%, solution build 0 lỗi.

- [x] Task 398: Tự Động Đóng Cửa Sổ Quản Lý Job Khi Bấm Huấn Luyện Từ Xa:
  - [x] Cập nhật phương thức `ExecuteRemoteTeachAsync()` trong [JobManagerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/JobManagerViewModel.cs): Kích hoạt `RequestClose?.Invoke()` ngay sau khi chuyển Tab Tool Editor, tự động đóng cửa sổ Quản lý Job sau khi nạp xong Job và ảnh mẫu.
  - [x] Cập nhật thông báo lên `MainWindowViewModel.GlobalStatusMessage` và `GlobalStatusSeverity = "Success"`.
  - [x] Bổ sung kiểm thử `Test_JobManagerRemoteTeach_WindowCloseBehavior()` trong [RemoteServerAndJobManagerTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/RemoteServerAndJobManagerTests.cs); 13/13 tests PASSED 100%.

- [x] Task 397: Khắc Phục Lỗi Mất Dữ Liệu Cấu Hình OQC Scanner & Tra Cứu Database Khi Build App & Chạy Test:
  - [x] Thiết lập Isolated Sandbox cho `OqcScannerService` & `DbManagerService` khi chạy unit test.
  - [x] Tích hợp Safe Guard bảo vệ cấu hình sản xuất của xưởng CMS_VINA (`192.168.1.2:6789`).
  - [x] Bổ sung nút UI `↺ Khôi Phục Xưởng (CMS_VINA)` trên [OqcSettingsDialog.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/OqcSettingsDialog.xaml).

- [x] Task 396: Khắc Phục Lỗi Mất Link Server OTA Khi Build App & Chạy Test:
  - [x] Cô lập môi trường lưu trữ Unit Test (Isolated Sandbox) cho `GlobalAppSettingsService`.
  - [x] Chuẩn hóa dải mạng nội bộ xưởng mặc định: `DefaultUpdateServerUrl = "http://192.168.1.192/update/version.json"`.
  - [x] Bổ sung nút UI "↺ Khôi Phục Mặc Định" trên [OtaUpdateDialog.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OTA/OtaUpdateDialog.xaml).

- [x] Task 395: Tự Động Focus & Select All Scanned Text Trên Tab OQC Scanner (Ghi Đè Tự Động):
  - [x] Tự động bôi đen toàn bộ văn bản trong `ScanInputTextBox` sau khi nạp Job hoặc kiểm tra xong, quét mã mới sẽ ghi đè ngay lập tức.

- [x] Task 394: Thêm Phím Tắt ESC Để Đóng Job & Xóa TextBox Trên Tab OQC Scanner (1 chạm không pop-up, nút "🔒 Đóng Job (ESC)").
- [x] Task 393: Chuẩn Hóa Chu Trình 4 Bước Chế Độ "Cú đấm thép" Trên Tab OQC Scanner (Mặc Định Checked).
- [x] Task 392: Chuẩn Hóa Chu Trình Dịch Bit Hàng Đợi FX5U — Dịch Bit Theo Xung Kết Quả Vision Done (`M103`).
- [x] Task 391: Sửa Lỗi Không Truyền Bit NG M105 Sang GXWorks & Kích Hoạt Handshake Khi Test Ảnh Trên Tool Editor.
- [x] Task 390: Khắc phục hiện tượng Bắt Cạnh PLC (`LDP X0`) không tác động `SET Y0` từ nút Momentary HMI & Nâng cấp Minimum Hold Duration (100ms).
- [x] Task 389: Bắt Tay Bất Đồng Bộ Non-Blocking & Hàng Đợi 20 Phôi BSFLP M200..M219 Dừng Đúng Điểm Ra Ngoài Buồng.
- [x] Task 388: Khắc phục Mất Cấu Hình PLC Khi Build & Cơ Chế Test Handshake Giả Lập GX Works 3.
- [x] Task 387: Hiển thị Trực Quan Trạng Thái PLC Handshake trên UI Tool Editor & Phân tích Trigger Flow.
- [x] Task 386: Hướng dẫn Thông số Cài Đặt Handshake & Tags trên App Vision WPF.
- [x] Task 385: Chuẩn hóa lệnh dịch bit `BSFRP` cho PLC FX5U.
- [x] Task 384: Bộ mã Ladder Diagram (LD) đầy đủ 9 Networks cho PLC FX5U.
- [x] Task 383: Sửa lỗi cú pháp ST GX Works 3 `0x110E1A02`.
- [x] Task 382: Chuẩn hóa 100% định dạng CSV GX Works 3 (1.080J).
- [x] Task 381: Handshake PLC FX5U, Cơ chế Dừng NG Ngoài Buồng & Xem Lại Ảnh 20 Nấc Lịch Sử.
- [x] Task 380: OQC Scanner 1 nút Space / Ctrl+F8 luân phiên & Bật tắt Crosshair căn tâm.
- [x] Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN (`Parallel.For`, cap 4-8 luồng).
- [x] Task 378: Tối ưu hiệu năng/UX Tool Editor (cache preview Final, chống rò rỉ RAM Mat, thuật toán Coordinate Descent).
- [x] Task 377: Sửa lỗi Copy/Paste nhiều node trong Tool Editor & thêm Preprocess Auto Tuner.
- [x] Task 376: Ô nhập SỐ ĐO THỰC TẾ (mm) riêng cạnh nút "Đặt Hệ Số Calib".

## Định hướng tiếp theo
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
- [ ] Tối ưu hóa render đa luồng (Multi-threaded Rendering) cho tài liệu PDF kích thước lớn >50MB.
