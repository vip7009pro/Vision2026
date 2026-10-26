# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (6/6 test PlcContinuousQueue 16 Slots, 7/7 test ToolEditor Column Resize & Full-Width, 5/5 test HMI State & Lifecycle, 4/4 test HMI Designer Persistence & DINT 32-bit, 4/4 test PLC Conflict & HMI Isolation, 7/7 test PLC Handshake, 5/5 test Reset Queue & HMI, 5/5 test PLC Signal Types, 11/11 test SurfaceCompare & Caliper, 8/8 test ContourCompare, 12/12 test Tool Calib Factor, 15/15 test OQC Scanner Live View, 8/8 test MvpShapeMatch2).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, PlcAck `""` Non-blocking, Queue In `M200`).

## 3. Hoàn thành Task 421: Khắc Phục Triệt Để Độ Trễ 1 Giây/Nấc Khi Xả Hàng Đợi 16 Nấc (Queue Drain Delay) Trong Run Continuous
1. **Bối cảnh & 3 Điểm nghẽn cốt lõi**:
   - *Triệu chứng*: Người dùng bấm trigger tay dồn dập khiến hàng đợi 16 nấc tăng lên. Tuy nhiên khi để hàng đợi tự xả thì mỗi lần run báo 25ms, nhưng 1 nấc mất tới ~1 giây để hạ.
   - *Điểm nghẽn 1 (Chờ PLC Ack Timeout trong Worker Task)*:
     - Trong `ProcessContinuousFrameAsync`, câu lệnh `await _handshakeStateMachine.CompleteHandshakeAsync(inspectionResult.Pass, token);` được await trực tiếp trên Worker Task của BoundedChannel.
     - Khi `NonBlockingMode` là `false` và cấu hình `ackTag` (mặc định hoặc IO truyền thống), máy trạng thái rơi vào vòng lặp chờ PLC phản hồi Ack: `while (sw.ElapsedMilliseconds < HandshakeTimeoutMs)`. Do người dùng kích phát trigger tay (hoặc PLC không cấp xung Ack cho từng phôi trong queue), vòng lặp **chờ đúng `HandshakeTimeoutMs` (500ms - 1000ms)** mới timeout.
     - Worker Task của hàng đợi bị khóa cứng ~1 giây cho mỗi frame trước khi có thể đọc frame tiếp theo!
   - *Điểm nghẽn 2 (Tranh chấp socket PLC)*: `StartInspectionAsync` và `CompleteHandshakeAsync` await tuần tự 6 lượt ghi socket xuống PLC trực tiếp trong luồng xử lý ảnh.
   - *Điểm nghẽn 3 (UI DispatcherPriority Background)*: `UpdateQueueVisuals()` dùng priority `Background`, dẫn tới việc cập nhật số đếm và LED nấc bị hoãn lại sau cùng.
2. **Các điểm sửa đổi cốt lõi**:
   - `ToolEditorViewModel.Engine.cs`:
     - `StartContinuousCameraFlow`: Tự động kích hoạt `_handshakeStateMachine.NonBlockingMode = true;` khi bắt đầu Run Continuous.
     - `ProcessContinuousFrameAsync`: Chuyển toàn bộ các lệnh bắt tay PLC (`StartInspectionAsync`, `CompleteHandshakeAsync`) sang ThreadPool chạy ngầm độc lập (`_ = Task.Run(...)`), tách rời hoàn toàn khỏi Inspection Worker Task. Nhờ đó, Worker Task xả hàng đợi ở đúng tốc độ tính toán CPU/GPU (25ms/phôi).
     - `UpdateQueueVisuals`: Nâng `DispatcherPriority` từ `Background` lên `Normal` để cập nhật số đếm và LED nấc tức thì ngay khi dequeue.
     - `StopContinuousFlow`: Bổ sung `QueueCurrentCount = 0;` và `UpdateQueueVisuals();` để làm sạch thanh hiển thị ngay khi dừng.
     - Null safety check cho `System.Windows.Application.Current?.Dispatcher` bảo vệ môi trường chạy console/test headless.
3. **Kiểm thử tự động**:
   - Bổ sung Test 6 trong `PlcContinuousQueueTests.cs`: Đo thời gian xả 3 frames khi cấu hình Ack Tag và Timeout 1000ms. Kết quả hoàn thành trong **89ms / 3 nấc** (tương đương ~30ms/nấc) thay vì bị giam 3000ms.
   - Bộ test `PlcContinuousQueueTests.cs` (6/6 test PASSED 100%).

## 4. Hoàn thành Task 420: Tích Hợp Chế Độ PLC Trigger Vào Pipeline Bounded Channel 16 Nấc Bất Đồng Bộ Tool Editor
- Khởi tạo BoundedChannel 16 nấc khi `SourceType == Camera` cho mọi kiểu trigger (kể cả Simulator và Camera thật).
- Bổ sung nút bấm giả lập `⚡ Trigger tay` (màu cam hổ phách `#D97706`) cạnh nút `Chạy liên tục`.
- Đăng ký tag PLC vào PollingEngine, debounce chống rung tiếp điểm 15ms.

## 5. Các sự kiện & thay đổi gần đây
- Task 421: Khắc phục triệt để độ trễ 1 giây/nấc khi xả hàng đợi 16 nấc Run Continuous; kiểm thử 6/6 PASSED (89ms/3 nấc).
- Task 420: Tích hợp PLC Trigger vào BoundedChannel 16 nấc & thêm nút Trigger tay; kiểm thử 5/5 PASSED 100%.
- Task 419: Tự động co giãn Full Width 4 cột trên Tab Tool Editor khi Ẩn/Hiện cột; kiểm thử 7/7 PASSED 100%.
- Task 418: Tự động nạp trạng thái hiện tại của Tag HMI khi Load/Run, khắc phục lỗi mất tên file/không tương tác được & thêm nút Stop HMI; kiểm thử 5/5 PASSED 100%.
- Task 417: Khắc phục triệt để tranh chấp kết nối PLC làm đổi trạng thái xanh/đỏ liên tục & cô lập hoàn toàn HMI Tool Editor; kiểm thử 4/4 PASSED 100%.
- Task 416: Khắc phục triệt để lỗi Thread Ownership làm ngắt kết nối PLC Ethernet & chuẩn hóa chế độ bỏ PLC Ack / Non-blocking; kiểm thử 7/7 PASSED 100%.
- Task 415: Chia đôi cột Preview Tool Editor hiển thị HMI & bổ sung tag Reset Queue Vision trong cài đặt bắt tay 24/7 Handshake (cạnh lên, cạnh xuống, long press); kiểm thử 5/5 PASSED 100%.
- Task 414: Khắc phục triệt để lỗi lệch tịnh tiến ảnh template SurfaceCompare khi sửa job cũ có Origin offset; kiểm thử 11/11 PASSED 100%.
