# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 422: Tích Hợp Nguồn Ảnh Camera Android (Samsung Galaxy S24 Ultra) Pro Mode Qua Cáp USB / Wi-Fi / Ethernet:
  - [x] **Mô hình & Dữ liệu** ([Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Class1.cs), [CameraParameters.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/CameraParameters.cs), [CameraDeviceInfo.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Services/Camera/CameraDeviceInfo.cs)):
    - Bổ sung `ImageSourceType.AndroidCamera = 5` vào `ImageSourceDefinition`.
    - Bổ sung các enums: `AndroidConnectionMode` (Usb, WiFi, Ethernet), `AndroidCameraLens` (PrimaryWide1x 200MP, Tele3x, Tele5xPeriscope 50MP, UltraWide06x, FrontSelfie), `AndroidFocusMode` (Auto, Macro, Manual).
    - Thêm `CameraVendor.AndroidCamera`, `CameraInterfaceType.Android` với icon `📱`.
  - [x] **Driver Chuyên Dụng** ([AndroidCameraDriver.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Services/Camera/Drivers/AndroidCameraDriver.cs)):
    - Quét thiết bị và quản lý cấu hình USB ADB (`127.0.0.1:8080`) & Wi-Fi/LAN (`192.168.1.150:8080`).
    - Hỗ trợ VideoCapture MJPEG luồng xem trước mượt mà (`/video` hoặc polling `/shot.jpg`).
    - Chụp ảnh cảm biến gốc Full-Res Snapshot (12MP/50MP/200MP) qua `/photo.jpg` khi có tín hiệu trigger hoặc snap frame.
    - Gửi lệnh REST API cấu hình Pro Mode: phơi sáng, bù trừ EV, lấy nét, ISO, cân bằng trắng WB, bật/tắt đèn pin Flash LED (Torch).
    - Tự động chạy `adb forward tcp:8080 tcp:8080` khi cắm cáp USB Type-C cho độ trễ cực thấp < 5ms.
  - [x] **Tích Hợp Giao Diện Tool Editor & Cài Đặt Camera**:
    - [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml) & [ToolEditorViewModel.ToolPreprocess.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolPreprocess.cs): Khối UI cấu hình chuyên biệt khi chọn nguồn Android Camera.
    - [ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs): Nạp ảnh preview từ Android Camera và hỗ trợ trong cả luồng Run Single lẫn Run Continuous.
    - [CameraSettingsView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/CameraSettingsView.xaml) & [CameraSettingsViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/CameraSettingsViewModel.cs): Phân hệ điều khiển Camera Android Pro Mode với đầy đủ Slider EV, Focus, ISO, Torch LED.
    - [JobCameraSettingsWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/JobCameraSettingsWindow.xaml) & [JobCameraSettingsViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/JobCameraSettingsViewModel.cs): Hiển thị và lưu riêng biệt thông số Camera Android Pro Mode cho từng Job.
  - [x] **Kiểm thử tự động**:
    - [AndroidCameraProModeTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/AndroidCameraProModeTests.cs): 6/6 test PASSED 100%.

- [x] Task 421: Khắc Phục Triệt Để Độ Trễ 1 Giây/Nấc Khi Xả Hàng Đợi 16 Nấc (Queue Drain Delay) Trong Run Continuous:
  - [x] Chuyển toàn bộ các lệnh bắt tay PLC sang ThreadPool ngầm độc lập (`_ = Task.Run(...)`).
  - [x] Tự động bật `NonBlockingMode = true;` khi Run Continuous, nâng `UpdateQueueVisuals` lên `DispatcherPriority.Normal`.
  - [x] Kiểm thử tự động `PlcContinuousQueueTests.cs`: 6/6 test PASSED 100% (89ms/3 nấc).

- [x] Task 420: Tích Hợp Chế Độ PLC Trigger Vào Pipeline Bounded Channel 16 Nấc Bất Đồng Bộ Tool Editor:
  - [x] Ưu tiên `SourceType == Camera` chạy qua `StartContinuousCameraFlow` cho mọi kiểu trigger.
  - [x] Bổ sung nút bấm giả lập `⚡ Trigger tay` (màu cam hổ phách `#D97706`).
  - [x] Kiểm thử tự động `PlcContinuousQueueTests.cs` (6/6 test PASSED 100%).

- [x] Task 419: Tự Động Co Giãn Full Width 4 Cột Trên Tab Tool Editor Khi Ẩn/Hiện Cột:
  - [x] Chuyển các cột sang `GridUnitType.Star`, chuẩn hóa `DragCompleted` cho 3 Splitter.
  - [x] Kiểm thử tự động `ToolEditorColumnResizeTests.cs` (7/7 test PASSED 100%).

- [x] Task 418: Tự Động Nạp Trạng Thái Hiện Tại Của Tag HMI Khi Load/Run & Bổ Sung Nút Stop HMI.
- [x] Task 417: Khắc Phục Triệt Để Tranh Chấp Kết Nối PLC & Cô Lập Hoàn Toàn HMI Tool Editor.
- [x] Task 416: Khắc Phục Triệt Để Lỗi Thread Ownership Ngắt Kết Nối PLC Ethernet & Chuẩn Hóa Chế Độ Bỏ PLC Ack.
- [x] Task 415: Chia Đôi Cột Preview Tool Editor Hiển Thị HMI & Bổ Sung Tag Reset Queue Vision.
- [x] Task 414: Khắc Phục Lỗi Lệch Tịnh Tiến Ảnh Template SurfaceCompare Khi Sửa Job Cũ.
- [x] Task 413: Khắc Phục Sự Cố Không Nhận Trigger PLC M10 Trên Tool ImageSource.
- [x] Task 412: Khắc Phục Lỗi Mất Cấu Hình Tag Bắt Tay 24/7 Khi Build Lại App & Cơ Chế Auto-Recover.
- [x] Task 411: Bổ Sung Tùy Chọn Kiểu Tín Hiệu Gửi PLC (Level, Rising Edge, Falling Edge).
- [x] Task 410: Khắc Phục Lỗi Trắng Địa Chỉ, Mất Chọn PLC & Hỗ Trợ DINT 32-bit.
- [x] Task 409: Khắc Phục Lỗi Ảnh Template Tool SurfaceCompare Bị Đen Ngòm Trên Job GH68-58216B.
- [x] Task 408: Rà Soát, Bỏ Control Vô Dụng và Tái Cấu Trúc Giao Diện Cửa Sổ Train Template Tool Origin.
- [x] Task 407: Bổ Sung Phím Tắt Ctrl + R (Xoay Strip Trong ROI) và Ctrl + F4 (Đóng Job Đang Mở).

## Định hướng tiếp theo
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
- [ ] Tối ưu hóa render đa luồng (Multi-threaded Rendering) cho tài liệu PDF kích thước lớn >50MB.
