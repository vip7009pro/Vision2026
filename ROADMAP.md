# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 397: Khắc Phục Lỗi Mất Dữ Liệu Cấu Hình OQC Scanner & Tra Cứu Database Khi Build App & Chạy Test:
  - [x] Điều tra nguyên nhân gốc: Unit test `SystemConfigBackupAndOqcDbMatchTests.cs` (Test 6) restore cấu hình test giả lập (`machine-b-guid`, `OQC_MASTER`, query rỗng) qua `OqcScannerService.SaveConfig()`, ghi đè thẳng vào `%AppData%\Vision2026\oqc_scanner_config.json` và thư mục hạt giống `configs\system`. Cùng lúc đó, `DbManagerService.SaveToDisk()` luôn gọi `SyncConfigToAppBackup` làm ghi đè hạt giống CSDL thành dummy DB (`MES_PRODUCTION`, `machine-b-guid`). Target `SyncReleaseConfigurations` của MSBuild copy các file hỏng này vào bản build.
  - [x] Thiết lập Isolated Sandbox cho `OqcScannerService` & `DbManagerService`: Thêm constructor overload nhận đường dẫn file tạm và cờ `disableBackupSync: true`. Khi chạy test, cả hai service chỉ thao tác trên file tạm trong thư mục Temp, tuyệt đối không chạm vào AppData hay thư mục hạt giống backup.
  - [x] Tích hợp Safe Guard bảo vệ cấu hình sản xuất: `OqcScannerService` tự động phát hiện và thanh lọc các ID giả lập từ test (`machine-a-guid`, `machine-b-guid`...), tự động bảo toàn kết nối CSDL và các câu lệnh SQL tra cứu của xưởng CMS_VINA (`192.168.1.2:6789`).
  - [x] Bổ sung nút UI phục hồi nhanh: Thêm nút `↺ Khôi Phục Xưởng (CMS_VINA)` kèm lệnh `ResetFactorySettingsCommand` trên [OqcSettingsDialog.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/OqcSettingsDialog.xaml) để phục hồi ngay toàn bộ cấu hình xưởng chỉ với 1 click.
  - [x] Cô lập 100% các bài Unit Test: Cập nhật `SystemConfigBackupAndOqcDbMatchTests.cs`, `ReleaseConfigPersistenceTests.cs`, `RemoteServerAndJobManagerTests.cs`, `OqcLiveViewOnJobLoadTests.cs`, `NewJobAndBlobSpecTests.cs`.
  - [x] Kiểm thử tự động & Xác thực: Toàn bộ test suite PASSED 100%, solution build 0 lỗi, file cấu hình AppData và thư mục output bảo toàn 100% dữ liệu chuẩn xưởng CMS_VINA.

- [x] Task 396: Khắc Phục Lỗi Mất Link Server OTA Khi Build App & Chạy Test:
  - [x] Rà soát và tìm ra nguyên nhân gốc: Bài test `ReleaseConfigPersistenceTests.cs` (Test 4 & 6) và `ManualInspectionTest.cs` không được cô lập, ghi đè trực tiếp `%AppData%\Vision2026\global_settings.json` thành IP giả lập `10.0.0.99` và `192.168.1.200`; sau đó target `SyncReleaseConfigurations` của MSBuild copy file bị hỏng này vào output build.
  - [x] Cô lập môi trường lưu trữ Unit Test (Isolated Sandbox): Nâng cấp `GlobalAppSettingsService` với constructor overload `customSettingsFilePath` kèm `disableBackupSync: true`; chuyển toàn bộ test sang thư mục tạm độc lập, triệt tiêu 100% việc can thiệp vào cấu hình máy thật.
  - [x] Bổ sung Safe Guard bảo vệ cấu hình sản xuất: Tự động lọc bỏ các URL dummy từ test và phục hồi về chuẩn xưởng nếu vô tình lưu trong môi trường production.
  - [x] Chuẩn hóa dải mạng nội bộ xưởng mặc định: Cập nhật `DefaultUpdateServerUrl = "http://192.168.1.192/update/version.json"`, `DefaultPublishServerUploadUrl = "http://192.168.1.192/ota_server.php"`, `DefaultPublishServerStorageFolder = "update"`.
  - [x] Cập nhật UI `OtaUpdateDialog.xaml`: Thêm nút "↺ Khôi Phục Mặc Định (192.168.1.192)" trên cả 2 Tab (Kiểm tra cập nhật & Đóng gói phát hành); cập nhật ToolTip chỉ dẫn.
  - [x] Kiểm thử tự động & bảo toàn cấu hình: 6/6 bài test độ bền vững vượt qua 100%, solution build 0 lỗi, file cấu hình AppData bảo toàn 100%.

- [x] Task 395: Tự Động Focus & Select All Scanned Text Trên Tab OQC Scanner (Ghi Đè Tự Động):
  - [x] Cơ chế ghi đè tự động: Toàn bộ chuỗi văn bản trong ô TextBox `ScanInputTextBox` luôn ở trạng thái được bôi đen (`SelectAll`) sau khi nạp Job, kiểm tra mẫu (Space / Ctrl+F8), chuyển Live View, hoặc click chuột. Ký tự đầu tiên từ đầu đọc quét mã tiếp theo sẽ lập tức ghi đè toàn bộ chuỗi cũ mà không bị nối chuỗi.
  - [x] Triệt tiêu thao tác bàn phím: Công nhân không cần nhấn phím Backspace, Delete hay ESC để xóa ký tự cũ; hoàn toàn chỉ thao tác quét mã và bấm nút/bàn đạp kiểm tra.
  - [x] Kiến trúc Event & UI Handlers: Bổ sung `RequestFocusAndSelectInput` trong `OqcScannerViewModel.cs`, hook trong `OqcScannerView.xaml.cs`, kèm các handler `GotKeyboardFocus` và `PreviewMouseLeftButtonDown` trong `OqcScannerView.xaml`.
  - [x] Kiểm thử tự động: Bổ sung Test 12 trong [OqcLiveViewOnJobLoadTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/OqcLiveViewOnJobLoadTests.cs), toàn bộ 12/12 tests PASSED 100%.

- [x] Task 394: Thêm Phím Tắt ESC Để Đóng Job & Xóa TextBox Trên Tab OQC Scanner:
  - [x] Phím tắt ESC 1 chạm dứt khoát: Nhấn phím `ESC` tại bất kỳ đâu lập tức giải phóng Job đang nạp, hủy phiên làm việc, xóa các dòng lịch sử pending và xóa sạch ô nhập mã (`ScannedCode = ""`), không pop-up modal cản trở thao tác.
  - [x] Cập nhật nút UI: Đổi nhãn thành `🔒 Đóng Job (ESC)`.

- [x] Task 393: Chuẩn Hóa Chu Trình 4 Bước Chế Độ "Cú đấm thép" Trên Tab OQC Scanner (Mặc Định Checked):
  - [x] Bước 1 (Mở Job & Tạo Phiên): Text scanned được cắt và hiển thị theo quy tắc cấu hình OQC, không tự xóa textbox, giữ nguyên giá trị.
  - [x] Bước 2 & 3 (Kiểm tra mẫu & lặp phiên): Bấm Space / Ctrl+F8 luân phiên kiểm tra mẫu và chuyển về Live View; kết quả hiển thị và lưu lịch sử; textbox giữ nguyên mã scan; phím Enter từ scanner (khi cùng mã phiên) bị triệt tiêu hoàn toàn.
  - [x] Bước 4 (Scan mã tiếp theo): Quét mã khác thì nạp Job mới, chu trình lặp lại.

- [x] Task 392: Chuẩn Hóa Chu Trình Dịch Bit Hàng Đợi FX5U — Dịch Bit Theo Xung Kết Quả Vision Done (`M103`) Thay Vì Sensor `X2`:
  - [x] PLC nhận xung `M103` (Done) $\rightarrow$ `LDP M103` dịch mảng `BSFLP M200 K20` $\rightarrow$ `LD M103 AND M105` kích `SET M200` nếu là hàng NG.

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
