# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 421: Khắc Phục Triệt Để Độ Trễ 1 Giây/Nấc Khi Xả Hàng Đợi 16 Nấc (Queue Drain Delay) Trong Run Continuous:
  - [x] **Nguyên nhân gốc rễ**:
    - *Nguyên nhân 1 (Worker Task bị khóa bởi PLC Ack Timeout)*: Trong `ProcessContinuousFrameAsync`, `await _handshakeStateMachine.CompleteHandshakeAsync(inspectionResult.Pass, token);` được thực thi đồng bộ trên luồng Worker của BoundedChannel. Khi `NonBlockingMode` là false và có tag Ack, máy trạng thái rơi vào vòng lặp chờ `sw.ElapsedMilliseconds < HandshakeTimeoutMs`. Vì kích phát trigger tay hoặc PLC không cấp xung Ack cho từng phôi trong hàng đợi, worker task bị giam cứng đúng `HandshakeTimeoutMs` (500ms - 1000ms) trước khi timeout, làm luồng xả queue bị trễ ~1 giây/phôi dù thời gian Inspect chỉ 25ms.
    - *Nguyên nhân 2 (Tranh chấp socket PLC tuần tự)*: `StartInspectionAsync` và `CompleteHandshakeAsync` await tuần tự 6 lượt ghi socket xuống PLC trực tiếp trong luồng xử lý ảnh.
    - *Nguyên nhân 3 (UI DispatcherPriority Background)*: `UpdateQueueVisuals()` dùng priority `Background`, làm chậm hiển thị số đếm và LED nấc trên giao diện.
  - [x] **Giải pháp triệt để**:
    - [ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs):
      - `StartContinuousCameraFlow`: Tự động kích hoạt `_handshakeStateMachine.NonBlockingMode = true;` khi bắt đầu Run Continuous.
      - `ProcessContinuousFrameAsync`: Chuyển việc bắt tay PLC (`StartInspectionAsync`, `CompleteHandshakeAsync`) sang ThreadPool chạy ngầm độc lập (`_ = Task.Run(...)`), tách rời hoàn toàn khỏi Inspection Worker Task. Worker Task xả hàng đợi ở đúng tốc độ CPU/GPU thực tế (25ms/phôi).
      - `UpdateQueueVisuals`: Nâng `DispatcherPriority` từ `Background` lên `Normal` để cập nhật số đếm và LED nấc tức thì ngay khi dequeue.
      - `StopContinuousFlow`: Bổ sung `QueueCurrentCount = 0;` và `UpdateQueueVisuals();` để làm sạch thanh hiển thị ngay khi dừng.
      - Null safety check cho `System.Windows.Application.Current?.Dispatcher` bảo vệ môi trường chạy console/test headless.
    - [PlcContinuousQueueTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/PlcContinuousQueueTests.cs): Bổ sung Test 6 đo tốc độ xả hàng đợi, kết quả đạt **89ms / 3 nấc** (PASSED 100%).

- [x] Task 420: Tích Hợp Chế Độ PLC Trigger Vào Pipeline Bounded Channel 16 Nấc Bất Đồng Bộ Tool Editor:
  - [x] Ưu tiên `SourceType == Camera` chạy qua `StartContinuousCameraFlow` cho mọi kiểu trigger (kể cả Simulator và Camera thật).
  - [x] Bổ sung nút bấm giả lập `⚡ Trigger tay` (màu cam hổ phách `#D97706` đặt ngay cạnh nút `Chạy liên tục`).
  - [x] Cập nhật `QueueCurrentCount = channel.Reader.Count` khi enqueue và dequeue, bổ sung Dispatcher check bảo vệ đa luồng cho `UpdateQueueVisuals()`.
  - [x] Giảm debounce xuống 15ms trong `OnPlcTagChangedForTrigger`, kích phát chụp ảnh bất đồng bộ trên luồng nền (ThreadPool) và nạp vào channel.
  - [x] Kiểm thử tự động `PlcContinuousQueueTests.cs` (6/6 test PASSED 100%).

- [x] Task 419: Tự Động Co Giãn Full Width 4 Cột Trên Tab Tool Editor Khi Ẩn/Hiện Cột:
  - [x] Chuyển toàn bộ các cột sang tỷ lệ `GridUnitType.Star` (`ToolboxColumnWidth` 2.0*, `CanvasColumnWidth` 3.5*, `PreviewColumnWidth` 4.5*, `DebugColumnWidth` 3.2*) khi hiện, và `new GridLength(0)` khi ẩn.
  - [x] Chuẩn hóa `DragCompleted` cho 3 Splitter, bổ sung nút ToggleButton `◧` (Toolbox) vào thanh công cụ Preview.
  - [x] Kiểm thử tự động `ToolEditorColumnResizeTests.cs` (7/7 test PASSED 100%).

- [x] Task 418: Tự Động Nạp Trạng Thái Hiện Tại Của Tag HMI Khi Load/Run, Khắc Phục Lỗi Mất Tên File/Không Tương Tác Được & Bổ Sung Nút Stop HMI:
  - [x] Tự động đọc giá trị ban đầu của tag khi Load/Run qua `RefreshCurrentValueFromPlc()`.
  - [x] Đồng bộ `IsRunMode` cho 100% controls trong `HmiManagerViewModel`.
  - [x] Bổ sung nút **`⏹️ Dừng HMI` / `▶️ Chạy HMI`** và Live Badge trực quan.
  - [x] Kiểm thử tự động `HmiStateAndLifecycleTests.cs` (5/5 test PASSED 100%).

- [x] Task 417: Khắc Phục Triệt Để Tranh Chấp Kết Nối PLC Làm Đổi Trạng Thái Xanh Lá/Đỏ Liên Tục & Cô Lập Hoàn Toàn HMI Tool Editor.
- [x] Task 416: Khắc Phục Triệt Để Lỗi Thread Ownership Ngắt Kết Nối PLC Ethernet & Chuẩn Hóa Chế Độ Bỏ PLC Ack / Non-Blocking.
- [x] Task 415: Chia Đôi Cột Preview Tool Editor Hiển Thị HMI & Bổ Sung Tag Reset Queue Vision Trong Cài Đặt Bắt Tay 24/7 Handshake.
- [x] Task 414: Khắc Phục Lỗi Lệch Tịnh Tiến Ảnh Template SurfaceCompare Khi Sửa Job Cũ & Thêm Nút "Set Template".
- [x] Task 413: Khắc Phục Sự Cố Không Nhận Trigger PLC M10 Trên Tool ImageSource.
- [x] Task 412: Khắc Phục Triệt Để Lỗi Mất Cấu Hình Tag Bắt Tay 24/7 Khi Build Lại App & Cơ Chế Safe Guard / Auto-Recover.
- [x] Task 411: Bổ Sung Tùy Chọn Kiểu Tín Hiệu Gửi PLC (Level, Rising Edge, Falling Edge) Trong Cài Đặt Handshake PLC.
- [x] Task 410: Khắc Phục Triệt Để Lỗi Trắng Địa Chỉ, Mất Chọn PLC & Hỗ Trợ DINT 32-bit (Int32: D300, D302).
- [x] Task 409: Khắc Phục Triệt Để Lỗi Ảnh Template Tool SurfaceCompare Bị Đen Ngòm Trên Job GH68-58216B.
- [x] Task 408: Rà Soát, Bỏ Control Vô Dụng và Tái Cấu Trúc Giao Diện Cửa Sổ Train Template Tool Origin.
- [x] Task 407: Bổ Sung Phím Tắt Ctrl + R (Xoay Strip Trong ROI) và Ctrl + F4 (Đóng Job Đang Mở).

## Định hướng tiếp theo
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
- [ ] Tối ưu hóa render đa luồng (Multi-threaded Rendering) cho tài liệu PDF kích thước lớn >50MB.
