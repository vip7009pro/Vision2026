# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode Native Server, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Dự án Android Native**: `AndroidCameraServer` (Gradle 8.11, CompileSDK 34, Android 14) đã biên dịch thành công APK và cài đặt trực tiếp lên Samsung Galaxy S24 Ultra (`SM-S928B`).
- **Kiểm thử tự động**: PASSED 100% (4/4 test Snapshot Frame Ownership & No ObjectDisposedException, 5/5 test HikStreaming & Color Fidelity, 9/9 test NewJob & BlobSpec Calibrated/Uncalibrated Mode, 6/6 test BlobCountingMode, 7/7 test PlcHandshakeNumericMode OK/NG Int16 & Both, 7/7 test PlcHandshakePersistence, 5/5 test PlcSignalType, 6/6 test CodeDetection Result, 5/5 test ObjectCounting, 6/6 test AndroidCameraProMode, 6/6 test PlcContinuousQueue 16 Slots, 7/7 test ToolEditor Column Resize).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, Kết quả số `D100` OK=2, NG=3, Queue In `M200`).

## 3. Hoàn thành Task 429: Khắc Phục Triệt Để Lỗi ObjectDisposedException ("Cannot access a disposed object OpenCvSharp.Mat") Khi Chụp Ảnh Trong Tool Editor & Manual Inspection
1. **Bối cảnh & Vấn đề**:
   - Khi người dùng bấm nút "Chụp ảnh" (hoặc "Capture Snap") trong tab **Tool Editor** hoặc **Manual Inspection**, hộp thoại lỗi lập tức xuất hiện:
     `Lỗi chụp ảnh: Cannot access a disposed object. Object name: 'OpenCvSharp.Mat'.`
2. **Nguyên nhân cốt lõi phát hiện**:
   - Trong Task 428, để tối ưu Zero-Allocation cho luồng Stream Live 41 FPS, `ApplySoftwarePostProcessing` được tối ưu trả về trực tiếp `input` khi không có flip/rotate (`needProcessing == false`).
   - Tuy nhiên, trong `GrabFrameAsync` của các Driver (`HikCameraDriver.cs`, `OpenCvCameraDriver.cs`, `AndroidCameraDriver.cs`), frame được bao bọc trong câu lệnh `using var rawMat = ...` hoặc `using var frame = new Mat()`.
   - Khi phương thức trả về `ApplySoftwarePostProcessing(rawMat, ...)`, khối `using` tự động gọi `Dispose()` lên chính `rawMat` trước khi trao quyền cho caller.
   - Do đó, đối tượng `Mat` trả về cho `CameraService.CaptureSnapshotAsync()` hay `ToolEditorViewModel` / `ManualInspectionViewModel` là một **Mat đã bị Dispose**!
   - Khi tầng trên gọi `.Empty()`, `_lastFrame.Clone()` hoặc xử lý ảnh, OpenCvSharp ném ngay ngoại lệ `ObjectDisposedException: Cannot access a disposed object. Object name: 'OpenCvSharp.Mat'.`
   - Ngoài ra, nếu snapshot lấy trực tiếp từ `NativeMatPool` (Ring Buffer) mà không clone riêng cho caller, khi caller (như ToolEditor) gọi `mat.Dispose()`, ô nhớ nội bộ của Pool bị hủy hoại, gây lỗi dây chuyền cho các lần chụp tiếp theo.
3. **Các giải pháp đã triển khai**:
   - **Tách bạch quyền sở hữu Mat của Snapshot Caller**:
     - `HikCameraDriver.cs`: Bỏ `using var rawMat`. Khi `processed == rawMat` (thuộc `_matPool`), nhân bản 1 bản clone riêng độc lập `rawMat.Clone()` trao quyền sở hữu hoàn toàn cho caller; nếu `processed != rawMat` (đã là Mat mới từ post-processing), trả về `processed` trực tiếp. Bảo toàn 100% Ring Buffer nội bộ của camera và không trả về Mat disposed.
     - `OpenCvCameraDriver.cs`: Loại bỏ `using var frame = new Mat()`. Khi read thành công, gọi `ApplySoftwarePostProcessing(frame, _parameters)` và chuyển quyền sở hữu an toàn cho caller (chỉ dispose `frame` nếu sinh ra `processed` mới).
     - `AndroidCameraDriver.cs`: Loại bỏ toàn bộ `using var` trên các đường dẫn đọc frame full-res snapshot và fallback preview.
     - `CameraService.cs`:
       - Trong `CaptureSnapshotFromCameraAsync`: Clone ảnh snapshot trước khi `driver.CloseAsync()` và `driver.Dispose()` hủy tài nguyên driver.
       - Cập nhật `_lastFrame` an toàn kiểu Copy-On-Write: giữ `old = _lastFrame` và clone trước khi dispose `old` để tránh race condition.
       - Trong `TryGetLatestFrameClone()`: Bổ sung kiểm tra `!_lastFrame.IsDisposed && !_lastFrame.Empty()` trước khi gọi `.Clone()`.
     - Thêm phương thức mở rộng `MatExtensions.IsValid()` kiểm tra an toàn Mat không null, chưa dispose, không rỗng và kích thước > 0.
     - Bổ sung kiểm tra an toàn `!mat.IsDisposed` trên tất cả các ViewModel: `ToolEditorViewModel.Engine.cs`, `ManualInspectionViewModel.cs`, `InspectionViewModel.cs`, `CalibrationViewModel.cs`, `CameraSettingsViewModel.cs`, `JobCameraSettingsViewModel.cs`, `ChessboardCalibrationViewModel.cs`, `OqcScannerViewModel.cs`.
4. **Kiểm thử tự động**:
   - `CameraTest.TestSnapshotFrameOwnershipAndNoObjectDisposedException`: **4/4 tests PASSED 100%**.
     1. `MatExtensions.IsValid` an toàn với null và disposed Mat: PASSED.
     2. `NativeMatPool` Ring Buffer cách ly hoàn toàn, caller dispose không làm hỏng pool: PASSED.
     3. `SimulatorCameraDriver.GrabFrameAsync` caller sở hữu và dispose độc lập: PASSED.
     4. `CameraService.CaptureSnapshotAsync` & `TryGetLatestFrameClone` an toàn tuyệt đối: PASSED.
   - Toàn bộ 5/5 tests `TestHikHighSpeedStreamingOptimization` vẫn đạt **79.5 FPS** và PASSED 100%.

## 4. Các sự kiện & thay đổi gần đây
- Task 429: Khắc phục triệt để lỗi ObjectDisposedException ("Cannot access a disposed object OpenCvSharp.Mat") khi bấm chụp ảnh trong Tool Editor & Manual Inspection; kiểm thử 4/4 PASSED 100%.
- Task 428: Tối ưu triệt để stream live & hiệu chuẩn màu sắc Camera Hikrobot 25MP MV-CH250-90TC qua card 10GbE MV-GT1002 đạt 41 FPS & chuẩn màu MVS 100% (Zero-Copy DMA, Jumbo Frames 9000, 20 buffer nodes, Hik ISP DMA convert, sửa lỗi đảo màu Red/Blue Bayer); kiểm thử 5/5 PASSED 100%.
- Task 427: Tối ưu triệt để BlobDetection bắt khuyết tật theo WxL khi đã hiệu chuẩn (bỏ qua blob nhỏ nằm trong dung sai, chỉ bắt blob vượt quá WxL), tự động RunFlow cập nhật preview tức thì, loại bỏ slider Thr; kiểm thử 9/9 PASSED 100%.
- Task 426: Hoàn thành chế độ xuất kết quả OK/NG dạng số nguyên (Int16 Word: D100, OK=2, NG=3) song hành cùng dạng Bit Bool trong cửa sổ Bắt tay 24/7; kiểm thử 7/7 PASSED 100%.
- Task 425: Bổ sung chi tiết lỗi NG cho tool CodeDetection trong SpecResults, NgReasonsText và thẻ CodeDetection; kiểm thử 6/6 PASSED 100%.
- Task 424: Hoàn thành Tool ObjectCounting, sửa dứt điểm lỗi đếm 69 vật thể trên vỉ thuốc -> đếm chuẩn xác 10/10 viên thuốc; kiểm thử 5/5 PASSED 100%.
- Task 423: Xây dựng Android App VisionCameraServer (Camera2 Pro Mode, 200MP, EV, Focus, Flash Torch) & cài đặt thành công lên S24 Ultra; kiểm thử 6/6 PASSED 100%.
- Task 422: Tích hợp Camera Android Galaxy S24 Ultra Pro Mode (USB/Wi-Fi/LAN, 200MP, EV, Focus, Flash Torch); kiểm thử 6/6 PASSED 100%.
- Task 421: Khắc phục triệt để độ trễ 1 giây/nấc khi xả hàng đợi 16 nấc Run Continuous; kiểm thử 6/6 PASSED (89ms/3 nấc).
