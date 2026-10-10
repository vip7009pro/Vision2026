# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 402: Bảo Mật Mật Khẩu Xóa Lịch Sử OQC Log & Checkbox Không Tên Lọc PASS (Ẩn NG):
  - [x] Cấu hình mật khẩu xóa:
    - Bổ sung `DeleteHistoryPassword` (mặc định "1234") vào [OqcScannerConfig.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/OqcScannerConfig.cs) và `CreateFactoryStandard()`.
    - Tích hợp 2 chiều trong [OqcScannerViewModel.Settings.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/OqcScannerViewModel.Settings.cs) (`LoadSettingsFromConfig`, `SaveSettingsToConfig`, `ResetFactorySettings`, `ExecuteExportConfig`).
    - Bổ sung GroupBox "8. Bảo mật & Mật khẩu Xóa Lịch Sử OQC" trong [OqcSettingsDialog.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/OqcSettingsDialog.xaml).
  - [x] Hộp thoại xác thực mật khẩu xóa [OqcPasswordPromptDialog.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/OqcPasswordPromptDialog.xaml):
    - Giao diện UI gọn gàng, PasswordBox tự động Focus, phím tắt Enter/Esc, hiển thị cảnh báo lỗi màu đỏ khi sai mật khẩu.
    - Cung cấp phương thức tĩnh `PromptPassword(owner, expectedPassword)` để kiểm soát quyền xóa.
  - [x] Bảo vệ nút xóa trong [OqcScanHistoryWindow.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/OqcScanHistoryWindow.xaml.cs):
    - Bắt buộc xác thực mật khẩu trước khi xóa cho: Nút "Xóa Lịch Sử", nút "Xóa Dòng Đã Chọn", và nút xóa từng dòng.
  - [x] Checkbox không tên lọc PASS (ẩn NG) trong [OqcScanHistoryWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/OqcScanHistoryWindow.xaml):
    - Thêm `CheckBox x:Name="ChkOnlyPass"` không tên, mặc định `IsChecked="False"` trên thanh lọc cạnh ô tìm kiếm.
    - Phương thức `OqcScannerViewModel.IsPassResult()` nhận diện chính xác các định dạng PASS ("PASS", "PASS (OK)", "OK").
    - Khi checked: chỉ hiển thị các kết quả PASS, các kết quả NG (NG (LỖI), FAIL, LỖI...) bị ẩn đi hoàn toàn.
    - Khi unchecked: hiển thị lại toàn bộ kết quả. Nút "Xóa lọc" dọn cả ô tìm kiếm và bỏ chọn checkbox.
  - [x] Kiểm thử tự động: Bổ sung Test 15 vào [OqcLiveViewOnJobLoadTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/OqcLiveViewOnJobLoadTests.cs); 15/15 tests OQC Scanner PASSED 100%, solution build 0 lỗi.

- [x] Task 401: Chuẩn Hóa Chu Trình 3 Bước Tab OQC Scanner Chế Độ Cú Đấm Thép (Tự Động Nạp Job Thứ 2 & Reset Counting Mẫu Không Cần Đóng Job):
  - [x] Nâng cấp `OqcScannerViewModel.cs`: Tách biệt `ScanOrRunJobCommand` và `ScanCommand`, tự động nạp Job mới và reset `CurrentJobTestedCount = 0`.
  - [x] Nâng cấp `OqcScannerView.xaml` & `OqcScannerView.xaml.cs`: Tích hợp Global Barcode Input Routing (`PreviewTextInput`), phím Enter luôn nạp Job.
  - [x] Kiểm thử tự động: Test 14 trong [OqcLiveViewOnJobLoadTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/OqcLiveViewOnJobLoadTests.cs) PASSED 100%.

- [x] Task 400: Khắc Phục Triệt Để Lỗi Khớp Điểm Origin Thuật Toán MvpShapeMatch2 Với Ảnh Qua Tiền Xử Lý:
  - [x] Tiền xử lý template đồng bộ trong `OriginMatcher.cs`, FNV-1a checksum cache key trong `MvpShapeMatch2Engine.cs`, Max Pooling 3x3 và refine đa candidate Level 0.
  - [x] Kiểm thử tự động: 8/8 tests trong [MvpShapeMatch2PreprocessTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/MvpShapeMatch2PreprocessTests.cs) PASSED 100%.

- [x] Task 399: Lưu Lịch Sử Kiểm Tra Cho Mọi Lần Chụp Và Kiểm Tra Trong Cùng 1 Phiên Job Trên Tab OQC Scanner.
- [x] Task 398: Tự Động Đóng Cửa Sổ Quản Lý Job Khi Bấm Huấn Luyện Từ Xa.
- [x] Task 397: Khắc Phục Lỗi Mất Dữ Liệu Cấu Hình OQC Scanner & Tra Cứu Database Khi Build App & Chạy Test.
- [x] Task 396: Khắc Phục Lỗi Mất Link Server OTA Khi Build App & Chạy Test.
- [x] Task 395: Tự Động Focus & Select All Scanned Text Trên Tab OQC Scanner (Ghi Đè Tự Động).
- [x] Task 394: Thêm Phím Tắt ESC Để Đóng Job & Xóa TextBox Trên Tab OQC Scanner (1 chạm không pop-up).
- [x] Task 393: Chuẩn Hóa Chu Trình 4 Bước Chế Độ "Cú đấm thép" Trên Tab OQC Scanner (Mặc Định Checked).
- [x] Task 392: Chuẩn Hóa Chu Trình Dịch Bit Hàng Đợi FX5U — Dịch Bit Theo Xung Kết Quả Vision Done (`M103`).
- [x] Task 391: Sửa Lỗi Không Truyền Bit NG M105 Sang GXWorks & Kích Hoạt Handshake Khi Test Ảnh Trên Tool Editor.
- [x] Task 390: Khắc phục hiện tượng Bắt Cạnh PLC (`LDP X0`) không tác động `SET Y0` từ nút Momentary HMI.
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
