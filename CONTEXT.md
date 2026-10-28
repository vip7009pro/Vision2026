# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (6/6 test AndroidCameraProMode, 6/6 test PlcContinuousQueue 16 Slots, 7/7 test ToolEditor Column Resize & Full-Width, 5/5 test HMI State & Lifecycle, 4/4 test HMI Designer Persistence & DINT 32-bit, 4/4 test PLC Conflict & HMI Isolation, 7/7 test PLC Handshake, 5/5 test Reset Queue & HMI, 5/5 test PLC Signal Types, 11/11 test SurfaceCompare & Caliper, 8/8 test ContourCompare, 12/12 test Tool Calib Factor, 15/15 test OQC Scanner Live View, 8/8 test MvpShapeMatch2).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, PlcAck `""` Non-blocking, Queue In `M200`).

## 3. Hoàn thành Task 422: Tích Hợp Nguồn Ảnh Camera Android (Samsung Galaxy S24 Ultra) Pro Mode Qua Cáp USB / Wi-Fi / Ethernet
1. **Bối cảnh & Yêu cầu**:
   - Khách hàng muốn sử dụng điện thoại cao cấp Samsung Galaxy S24 Ultra làm camera soi sản phẩm trực tiếp trong Tool Editor và Camera Settings.
   - Yêu cầu hỗ trợ Pro Mode: điều chỉnh thời gian phơi sáng (Exposure), bù trừ sáng EV, lấy nét Focus (Auto/Macro/Manual), ISO, White Balance, đèn Flash LED trợ sáng (Torch), và chọn các cảm biến quang học (1x 200MP, Tele 3x, Tele 5x Periscope 50MP, Ultra-Wide 0.6x).
   - Xuất hiện đồng bộ trên Tool Editor node ImageSource, Tab Camera Setting màn hình chính và cửa sổ Job Camera Settings.
2. **Các thành phần kiến trúc đã triển khai**:
   - **Model & Contracts** (`Class1.cs`, `CameraParameters.cs`, `CameraDeviceInfo.cs`):
     - Bổ sung `ImageSourceType.AndroidCamera = 5`.
     - Thêm enums `AndroidConnectionMode` (Usb, WiFi, Ethernet), `AndroidCameraLens` (PrimaryWide1x, Tele3x, Tele5xPeriscope, UltraWide06x, FrontSelfie), `AndroidFocusMode` (Auto, Macro, Manual).
     - Bổ sung `CameraVendor.AndroidCamera`, `CameraInterfaceType.Android` với icon `📱` và metadata hiển thị.
   - **Driver Chuyên Dụng** (`AndroidCameraDriver.cs`):
     - Hỗ trợ VideoCapture MJPEG luồng xem trước mượt mà (30-60 FPS) qua `/video` hoặc fallback `/shot.jpg`.
     - Chụp ảnh cảm biến gốc Full-Res Snapshot (12MP/50MP/200MP) nguyên bản qua `/photo.jpg` khi có lệnh trigger kiểm tra.
     - Điều khiển Pro Mode qua HTTP REST API: phơi sáng, EV, focus, ISO, WB, bật/tắt đèn pin Flash Torch.
     - Tích hợp tự động ADB Port Forwarding (`adb forward tcp:8080 tcp:8080`) khi cắm cáp USB Type-C, độ trễ cực thấp < 5ms.
   - **Giao diện & ViewModel** (`ToolEditorViewModel`, `ToolEditorView.xaml`, `CameraSettingsViewModel`, `CameraSettingsView.xaml`, `JobCameraSettingsViewModel`, `JobCameraSettingsWindow.xaml`):
     - Hiển thị khối cấu hình Pro Mode trực quan khi chọn nguồn Android Camera.
     - Cho phép chọn ống kính S24 Ultra, kéo bù trừ sáng EV (-2.0 đến +2.0 EV), chọn lấy nét Manual kéo cự ly (0.0 đến 1.0 diopters), bật/tắt đèn pin Flash Torch, bật Full-Res Snapshot.
     - Quét và nhận diện thiết bị Android trong danh sách ComboBox của Tab Camera Settings và Job Camera Settings.
3. **Kiểm thử tự động**:
   - Tạo mới `TestExtractApp/AndroidCameraProModeTests.cs` (6/6 test PASSED 100%).
   - Kiểm thử hồi quy `PlcContinuousQueueTests.cs` (6/6 test PASSED 100%).

## 4. Hoàn thành Task 421: Khắc Phục Triệt Để Độ Trễ 1 Giây/Nấc Khi Xả Hàng Đợi 16 Nấc (Queue Drain Delay) Trong Run Continuous
- Chuyển toàn bộ các lệnh bắt tay PLC (`StartInspectionAsync`, `CompleteHandshakeAsync`) sang ThreadPool ngầm độc lập (`_ = Task.Run(...)`).
- Nâng `UpdateQueueVisuals` lên `DispatcherPriority.Normal`, làm sạch hàng đợi khi dừng.
- Đo tốc độ xả 3 phôi trong 89ms (~30ms/nấc) thay vì bị giam 3000ms.

## 5. Các sự kiện & thay đổi gần đây
- Task 422: Tích hợp Camera Android Galaxy S24 Ultra Pro Mode (USB/Wi-Fi/LAN, 200MP, EV, Focus, Flash Torch); kiểm thử 6/6 PASSED 100%.
- Task 421: Khắc phục triệt để độ trễ 1 giây/nấc khi xả hàng đợi 16 nấc Run Continuous; kiểm thử 6/6 PASSED (89ms/3 nấc).
- Task 420: Tích hợp PLC Trigger vào BoundedChannel 16 nấc & thêm nút Trigger tay; kiểm thử 5/5 PASSED 100%.
- Task 419: Tự động co giãn Full Width 4 cột trên Tab Tool Editor khi Ẩn/Hiện cột; kiểm thử 7/7 PASSED 100%.
- Task 418: Tự động nạp trạng thái hiện tại của Tag HMI khi Load/Run, khắc phục lỗi mất tên file/không tương tác được & thêm nút Stop HMI; kiểm thử 5/5 PASSED 100%.
- Task 417: Khắc phục triệt để tranh chấp kết nối PLC làm đổi trạng thái xanh/đỏ liên tục & cô lập hoàn toàn HMI Tool Editor; kiểm thử 4/4 PASSED 100%.
- Task 416: Khắc phục triệt để lỗi Thread Ownership làm ngắt kết nối PLC Ethernet & chuẩn hóa chế độ bỏ PLC Ack / Non-blocking; kiểm thử 7/7 PASSED 100%.
- Task 415: Chia đôi cột Preview Tool Editor hiển thị HMI & bổ sung tag Reset Queue Vision Trong Cài Đặt Bắt Tay 24/7 Handshake; kiểm thử 5/5 PASSED 100%.
- Task 414: Khắc phục triệt để lỗi lệch tịnh tiến ảnh template SurfaceCompare khi sửa job cũ có Origin offset; kiểm thử 11/11 PASSED 100%.
