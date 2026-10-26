# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 420: Tích Hợp Chế Độ PLC Trigger Vào Pipeline Bounded Channel 16 Nấc Bất Đồng Bộ Tool Editor:
  - [x] **Nguyên nhân gốc rễ**:
    - *Nguyên nhân 1 (Rẽ nhánh bỏ rơi BoundedChannel 16 nấc)*: Khi `TriggerMode == PlcTrigger`, `OnRunContinuousClicked()` rẽ nhánh riêng, không gọi `StartContinuousCameraFlow()`, khiến `_industrialCameraFrameChannel` là `null`, thanh 16 nấc luôn đọc 0/16 không bao giờ tăng.
    - *Nguyên nhân 2 (Nghẽn UI Dispatcher & Render đồng bộ RefreshPreviewsNow)*: `OnPlcTagChangedForTrigger` gọi `Dispatcher.InvokeAsync(RunFlowAsync)`, ném toàn bộ việc điều phối lên UI thread. Sau khi Inspect xong, `RefreshPreviewsNow()` giải mã và dựng lại toàn bộ Overlay/Canvas đồng bộ ngốn 100-200ms trên UI thread, làm đơ giật giao diện.
    - *Nguyên nhân 3 (Drop mất frame khi trigger nhanh)*: Cờ chống tái nhập `_isExecutingRunFlow` trong `RunFlowAsync` và debounce cứng 100ms chặn nuốt mọi xung trigger gửi đến dồn dập.
  - [x] **Giải pháp triệt để**:
    - [ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs):
      - `OnRunContinuousClicked`: Ưu tiên `SourceType == Camera` chạy qua `StartContinuousCameraFlow` cho mọi kiểu trigger (kể cả Simulator và Camera thật).
      - `StartContinuousCameraFlow`: Hỗ trợ `sourceDef.TriggerMode == PlcTrigger`, đăng ký tag PLC và kích hoạt Polling, đặt camera TriggerMode=On (Software), khởi tạo BoundedChannel 16 nấc và Worker Task chạy ngầm với UI Throttling 120ms.
      - Cập nhật tức thời `QueueCurrentCount = channel.Reader.Count` khi enqueue và dequeue, bổ sung Dispatcher check bảo vệ đa luồng cho `UpdateQueueVisuals()`.
      - `OnPlcTagChangedForTrigger`: Giảm debounce xuống 15ms (chống rung tiếp điểm), kích phát chụp ảnh bất đồng bộ trên luồng nền (ThreadPool) và nạp vào channel, triệt tiêu hoàn toàn `InvokeAsync` lên UI Dispatcher.
      - `ManualTriggerCommand` & `OnManualTriggerClicked`: Bổ sung nút bấm giả lập trigger tay (màu hổ phách `#D97706` đặt ngay cạnh nút Chạy liên tục). Khi đang Run Continuous, hàm nạp frame trực tiếp vào BoundedChannel 16 slot ngầm để người dùng kiểm thử trực quan nấc nhảy của queue; khi đang Idle thì kích hoạt Run Once.
      - `StopContinuousFlow`: Bổ sung null check an toàn cho các service.
    - [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml): Đặt nút `⚡ Trigger tay` cạnh nút `Chạy liên tục`.
    - [PlcContinuousQueueTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/PlcContinuousQueueTests.cs): Bộ kiểm thử tự động 5/5 test PASSED 100%.

- [x] Task 419: Tự Động Co Giãn Full Width 4 Cột Trên Tab Tool Editor Khi Ẩn/Hiện Cột:
  - [x] **Nguyên nhân gốc rễ**:
    - *Nguyên nhân 1 (Pixel Width thay vì Star)*: Cột 0 (Toolbox) định nghĩa `new GridLength(340)` fixed pixel thay vì Star. Khi Cột 2 (Canvas) hoặc Cột 4 (Debug) bị ẩn, Toolbox vẫn giữ nguyên 340px, không thể tự co giãn theo không gian được giải phóng.
    - *Nguyên nhân 2 (Cột Preview không có dynamic binding)*: Cột 4 (Preview) bị hardcode `Width="4.5*"` trong XAML mà không có binding đồng bộ.
    - *Nguyên nhân 3 (GridSplitter ghi đè Local Value và cắt đứt Binding)*: Khi người dùng kéo `GridSplitter`, WPF Grid gán Local Value dạng Pixel trực tiếp vào `ColumnDefinition.WidthProperty`, cắt đứt OneWay binding. Khi bấm nút ToggleButton ẩn cột, ViewModel cập nhật Width=0 nhưng giao diện không nhận được, khiến cột bị ẩn thành khoảng trống rỗng có kích thước pixel cũ, còn các cột còn lại bị ghim cứng không co giãn được.
    - *Nguyên nhân 4 (Splitter Cột 5 thiếu Visibility binding)*: `SplitterDebug` trước đây thiếu binding Visibility nên vẫn chiếm diện tích khi Debug ẩn.
  - [x] **Giải pháp triệt để**:
    - [ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs): Chuyển toàn bộ các cột sang tỷ lệ `GridUnitType.Star` (`ToolboxColumnWidth` 2.0*, `CanvasColumnWidth` 3.5*, `PreviewColumnWidth` 4.5*, `DebugColumnWidth` 3.2*) khi hiện, và `new GridLength(0)` khi ẩn; cập nhật `MinWidth` và `SplitterWidth` về 0 khi ẩn.
    - [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml): Đặt tên cho Grid (`EditorMainGrid`) và 7 `ColumnDefinition` (`ColToolbox`, `ColToolboxSplitter`, `ColCanvas`, `ColCanvasSplitter`, `ColPreview`, `ColDebugSplitter`, `ColDebug`); bổ sung `DragCompleted="MainSplitter_DragCompleted"` cho 3 Splitter; bổ sung `Visibility` cho `SplitterDebug`; bổ sung nút ToggleButton `◧` (Toolbox) vào thanh công cụ Preview (Cột 3) để người dùng có đủ bộ 3 nút điều khiển `[◧ Toolbox, ▦ Canvas, ▤ Debug]` ngay tại trung tâm.
    - [ToolEditorView.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml.cs): Quản lý lưu giữ tỷ lệ `_savedToolboxStar`, `_savedCanvasStar`, `_savedPreviewStar`, `_savedDebugStar`; khi kéo Splitter nhả chuột (`DragCompleted`), tự động đọc `ActualWidth` và chuẩn hóa lại thành `Star` cho toàn bộ các cột đang hiển thị; khi thuộc tính `Is...ColumnVisible` thay đổi, tự động kích hoạt `ApplyColumnLayout` để các cột còn hiển thị luôn là Star và lấp đầy 100% chiều rộng Grid (Full Width).
    - [ToolEditorColumnResizeTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/ToolEditorColumnResizeTests.cs): Bộ kiểm thử tự động 7/7 test PASSED 100%.

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
