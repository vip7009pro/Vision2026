# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 418: Tự Động Nạp Trạng Thái Hiện Tại Của Tag HMI Khi Load/Run, Khắc Phục Lỗi Mất Tên File/Không Tương Tác Được & Thêm Nút Stop HMI:
  - [x] **Nguyên nhân gốc rễ**:
    - *Nguyên nhân 1 (Không tự load trạng thái ban đầu)*: Khi `HmiControlViewModel` tạo mới hoặc chuyển sang `IsRunMode`, controls chỉ thụ động chờ sự kiện `OnTagChanged`. Tuy nhiên `PlcPollingEngine` chỉ bắn `OnTagChanged` khi giá trị thay đổi (`!ValuesEqual(oldVal, newVal)`). Nếu tag trên PLC giữ nguyên trạng thái tĩnh từ trước (hoặc cache đã có), sự kiện không bao giờ bắn ra, dẫn đến các thành phần không cập nhật dữ liệu cho đến khi người dùng click tương tác. Ngoài ra `BeginInvoke` trực tiếp bị treo trong môi trường test/UI sync.
    - *Nguyên nhân 2 (Controls không kích hoạt RunMode & không tương tác được)*: Trong `HmiManagerViewModel`, `OnIsRunModeChanged` rỗng, không truyền `ctrl.IsRunMode = true` cho các control trong `Controls`. Do đó `ctrl.IsRunMode` vẫn là `false`, sự kiện chuột `PreviewMouseDown`/`PreviewMouseUp` trong `HmiControlView.xaml.cs` bỏ qua hoàn toàn, khiến người dùng không thể bấm/tương tác.
    - *Nguyên nhân 3 (ComboBox trắng trơn & hiện layout demo thay vì file HMI)*: `InitHmiPanelMonitoring()` quét tệp khi `SelectedHmiFilePath` còn rỗng; nếu `savedHmiPath` nằm ngoài thư mục mặc định thì không có trong `AvailableHmiFileItems` nên ComboBox hiển thị trắng; `HmiFileItem` không override `Equals`/`GetHashCode` làm mất chọn; `LoadFromFile` không được nạp sớm khiến màn hình hiển thị 5 controls mẫu demo.
  - [x] **Giải pháp triệt để**:
    - [HmiControlViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/HMI/HmiControlViewModel.cs):
      - `EffectiveReadAddress`: Tự động fallback sang `WriteTagName` khi người dùng chỉ nhập địa chỉ ghi.
      - Thêm `RefreshCurrentValueFromPlc()`: Truy vấn ngay `GetTagValue` trong cache hoặc đọc bất đồng bộ từ PLC Service khi mở/chạy HMI.
      - Thay `BeginInvoke` bằng `DispatchOnUiThread` an toàn, thực thi ngay lập tức trên UI thread và môi trường unit test.
    - [HmiManagerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/HMI/HmiManagerViewModel.cs):
      - `OnIsRunModeChanged`: Đồng bộ trạng thái `IsRunMode` cho 100% controls, tự động đăng ký tag đọc/ghi và kích hoạt `RefreshCurrentValueFromPlc()`.
      - Bổ sung phương thức công khai `RefreshAllControlsFromPlc()`; tối ưu `ToggleRunMode()` và `StopRunMode()`.
      - `LoadFromFile`: Tự động làm tươi dữ liệu ngay khi nạp nếu đang ở RunMode.
    - [ToolEditorViewModel.HmiPanel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.HmiPanel.cs):
      - `HmiFileItem`: Kế thừa `IEquatable<HmiFileItem>`, so sánh `FilePath` case-insensitive.
      - `InitHmiPanelMonitoring`: Quét file kèm `savedHmiPath`, chèn ngay vào danh sách, nạp tức thì file của người dùng bằng `LoadFromFile(SelectedHmiFilePath)` xóa sổ layout demo; khôi phục `ToolEditorIsHmiVisible`.
      - Thêm `IsHmiRunning` và các lệnh `StopHmiMonitorCommand`, `StartHmiMonitorCommand`, `ToggleHmiRunMonitorCommand`.
      - Thêm thuộc tính động cho Live Badge: `HmiLiveBadgeText`, `HmiLiveBadgeDot`, `HmiLiveBadgeBackground`, `HmiLiveBadgeBorder`, `HmiLiveBadgeForeground`, `HmiRunStopButtonText`, `HmiRunStopButtonBackground`, `HmiRunStopButtonToolTip`.
    - [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml):
      - Cập nhật ComboBox tệp HMI: `SelectedValuePath="FilePath"`.
      - Thêm nút **`⏹️ Dừng HMI` / `▶️ Chạy HMI`** trực quan ngay cạnh badge trạng thái và trên thanh công cụ bên phải.
      - Badge động chuyển đổi luân phiên giữa `🟢 LIVE (RUN)` và `⏸️ ĐÃ DỪNG (STOP)`.
    - [GlobalAppSettings.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/GlobalAppSettings.cs): Bổ sung `ToolEditorIsHmiVisible` lưu trạng thái Hiện/Ẩn.
    - [PlcManagerService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/PLC/Services/PlcManagerService.cs): Sửa `InferDataTypeFromAddress` nhận diện chính xác `MW200` là `Int16`; nới lỏng nạp tag cấu hình trong `GetAllTagsToPoll`.
  - [x] **Kiểm thử tự động [HmiStateAndLifecycleTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/HmiStateAndLifecycleTests.cs)**:
    - 5/5 test PASSED 100% (Auto-load current tag value, Fallback EffectiveReadAddress, HmiFileItem IEquatable, Stop/Start commands, GlobalAppSettings persistence).
    - Toàn bộ solution biên dịch 0 Errors, toàn bộ test suite hồi quy PASSED 100%.

- [x] Task 417: Khắc Phục Triệt Để Tranh Chấp Kết Nối PLC Làm Đổi Trạng Thái Xanh Lá/Đỏ Liên Tục & Cô Lập Hoàn Toàn HMI Tool Editor:
  - [x] Sửa lệch byte stream MC Protocol, ném `IOException` khi stream đóng (`read == 0`) hoặc `readCount < 11`.
  - [x] Tắt Watchdog Heartbeat khi thiếu tag cấu hình, chặn tự ý ghi tag lỗi `Y10_VisionFault`.
  - [x] Cô lập HMI Panel khi Ẩn, bảo đảm Bit device luôn là `PlcDataType.Bool`.
  - [x] Kiểm thử tự động `PlcConflictAndHmiIsolationTests.cs` (4/4 test PASSED 100%).

- [x] Task 416: Khắc Phục Triệt Để Lỗi Thread Ownership Ngắt Kết Nối PLC Ethernet & Chuẩn Hóa Chế Độ Bỏ PLC Ack / Non-Blocking:
  - [x] Xử lý ngoại lệ đa luồng WPF `0x80131509` bằng `DispatchOnUiThread` bảo vệ UI ViewModel.
  - [x] `IndustrialHandshakeStateMachine.GetEffectivePlcAckTag()` trả về `null` khi chuỗi rỗng; hiển thị chế độ Non-blocking không chờ Ack.
  - [x] Kiểm thử tự động `PlcHandshakePersistenceTests.cs` (7/7 test PASSED 100%).

- [x] Task 415: Chia Đôi Cột Preview Tool Editor Hiển Thị HMI & Bổ Sung Tag Reset Queue Vision Trong Cài Đặt Bắt Tay 24/7 Handshake (Cạnh Lên, Cạnh Xuống, Long Press, Duration):
  - [x] Tích hợp HMI tương tác trực tiếp trên Tool Editor, chia đôi cột Preview bằng GridSplitter, Viewbox Uniform.
  - [x] Bổ sung cấu hình `ResetQueueTagName`, `ResetQueueSignalType` và `PlcInputSignalDetector.cs` phát hiện Rising/Falling/Long Press.
  - [x] Kiểm thử tự động `PlcResetQueueAndHmiTests.cs` (5/5 test PASSED 100%).

- [x] Task 414: Khắc Phục Lỗi Lệch Tịnh Tiến Ảnh Template SurfaceCompare Khi Sửa Job Cũ & Thêm Nút "Set Template".
- [x] Task 413: Khắc Phục Sự Cố Không Nhận Trigger PLC M10 Trên Tool ImageSource.
- [x] Task 412: Khắc Phục Triệt Để Lỗi Mất Cấu Hình Tag Bắt Tay 24/7 Khi Build Lại App & Cơ Chế Safe Guard / Auto-Recover.
- [x] Task 411: Bổ Sung Tùy Chọn Kiểu Tín Hiệu Gửi PLC (Level, Rising Edge, Falling Edge) Trong Cài Đặt Handshake PLC.
- [x] Task 410: Khắc Phục Triệt Để Lỗi Trắng Địa Chỉ, Mất Chọn PLC & Hỗ Trợ DINT 32-bit (Int32: D300, D302).
- [x] Task 409: Khắc Phục Triệt Để Lỗi Ảnh Template Tool SurfaceCompare Bị Đen Ngòm Trên Job GH68-58216B.
- [x] Task 408: Rà Soát, Bỏ Control Vô Dụng và Tái Cấu Trúc Giao Diện Cửa Sổ Train Template Tool Origin.
- [x] Task 407: Bổ Sung Phím Tắt Ctrl + R (Xoay Strip Trong ROI) và Ctrl + F4 (Đóng Job Đang Mở).
- [x] Task 406: Tự Động Co Giãn Strip Length & Strip Width Node EdgePairDetection Theo Kích Thước ROI.
- [x] Task 405: Khắc Phục Triệt Để Lỗi Contour Lệch/Báo Đỏ Trên Cùng Ảnh, Thêm Canny Slider Kèm Live Preview.
- [x] Task 404: Khắc Phục Lỗi ContourCompare Báo NG Trên Cùng 1 Ảnh & Nâng Cấp Template Preview Kèm Viền Trực Quan.
- [x] Task 403: Tự Động Lưu Job Đang Mở Khi Có Thay Đổi Trước Khi Tải Lên Server (JobManager).
- [x] Task 402: Bảo Mật Mật Khẩu Xóa Lịch Sử OQC Log (Mặc Định 1234) & Checkbox Không Tên Lọc PASS.
- [x] Task 401: Chuẩn Hóa Chu Trình 3 Bước Tab OQC Scanner Chế Độ Cú Đấm Thép.

## Định hướng tiếp theo
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
- [ ] Tối ưu hóa render đa luồng (Multi-threaded Rendering) cho tài liệu PDF kích thước lớn >50MB.
