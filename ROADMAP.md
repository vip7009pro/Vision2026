# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 424: Bổ Sung Tool ObjectCounting Trong Tool Editor (Đếm Vật Thể Đa Hướng Hỗn Loạn, Quản Lý Spec & Dung Sai):
  - [x] **Model & Cấu Hình** ([ObjectCountingDefinition.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/ObjectCountingDefinition.cs)):
    - Quản lý Search ROI và Template ROI riêng biệt, hỗ trợ đường dẫn lưu ảnh mẫu `TemplateImagePath`.
    - Quản lý tiêu chuẩn kiểm tra: `NominalCount` (Số lượng chuẩn), `Tolerance` (Dung sai đối xứng $\pm$), `ToleranceMinus`, `TolerancePlus`.
    - Đặt mặc định thuật toán là `NormalizedCorrelation` (NCC) khử nhiễu vân nền công nghiệp, `MinScore = 0.65`, `MaxOverlap = 0.30`.
  - [x] **Engine Xử Lý Thị Giác Đa Hướng & Khắc Phục Đếm Sai Vỉ Thuốc** ([ObjectCountingEngine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.VisionEngine/ObjectCountingEngine.cs)):
    - Cơ chế Cửa sổ suy giảm biên mềm (Circular/Apodization Window) triệt tiêu 4 góc vân nền nhôm/băng chuyền của ảnh crop template.
    - Bộ lọc Rotated NMS nâng cấp: Kiểm tra khoảng cách tâm theo tỷ lệ kích thước ($0.70 \times \min(W, H)$) và tỷ lệ giao nhau trên diện tích vật thể (`interArea / minArea > MaxOverlap`).
    - Bổ sung trích xuất Local Maxima trên lưới ShapeMatching ngăn ngừa bùng nổ hàng trăm candidate pixel liền kề.
    - Kim tự tháp Coarse-to-Fine (Pyramid Down 2x) quét nhanh toàn dải $360^\circ$. Đánh giá PASS/NG theo quy chuẩn Spec và Dung sai.
  - [x] **Tích Hợp Pipeline, Condition Evaluator & Bảng Spec Results**:
    - [InspectionService.Pipeline.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/InspectionService.Pipeline.cs): Lập lịch chạy song song trong Pipeline kiểm tra, xuất `ObjectCountingResult`.
    - [ConditionEvaluator.VariableRegistry.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/ConditionEvaluator.VariableRegistry.cs): Đăng ký biến `Count`, `Nominal`, `Pass`, `Status`, `Found`, `Time`.
    - Tích hợp xuất kết quả kiểm tra vào bảng `SpecResults` trong [ToolEditorViewModel.Inspection.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Inspection.cs) và [InspectionViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/InspectionViewModel.cs).
  - [x] **Kiểm Thử Tự Động Toàn Diện** ([ObjectCountingTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/ObjectCountingTests.cs)):
    - 5/5 test PASSED 100%: Đếm vật thể xoay đa hướng hỗn loạn; Đánh giá Spec & Dung sai; Rotated NMS khử trùng lặp; Condition Evaluator; **Đếm chính xác tuyệt đối 10/10 viên thuốc trên vỉ thuốc thực tế của người dùng** (khắc phục hoàn toàn lỗi đếm thành 69 vật thể).

- [x] Task 423: Xây Dựng Ứng Dụng Android Native "VisionCameraServer" Cho Galaxy S24 Ultra & Tích Hợp Camera2 Pro Mode:
  - [x] **Dự án Android Studio Native** ([AndroidCameraServer/](file:///g:/NODEJS/Vision2026/AndroidCameraServer)):
    - Khởi tạo trọn bộ dự án với Gradle 8.11, CompileSDK 34 (Android 14), Package `com.vision.cameraserver`.
    - [AndroidManifest.xml](file:///g:/NODEJS/Vision2026/AndroidCameraServer/app/src/main/AndroidManifest.xml): Cấu hình đầy đủ quyền `CAMERA`, `INTERNET`, `WAKE_LOCK`, `FLASHLIGHT`.
  - [x] **Camera2 Multi-Camera & Pro Mode** ([Camera2Manager.java](file:///g:/NODEJS/Vision2026/AndroidCameraServer/app/src/main/java/com/vision/cameraserver/Camera2Manager.java)):
    - Ánh xạ 4 cảm biến mặt sau Galaxy S24 Ultra: Lens 0 (Chính 1x 200MP), Lens 1 (Tele 3x), Lens 2 (Tele 5x Periscope 50MP), Lens 3 (Ultra-Wide 0.6x Macro), Lens 4 (Front 12MP).
    - Hỗ trợ phơi sáng thủ công nano-giây, bù trừ EV (-2.0 đến +2.0), lấy nét thủ công Manual Focus diopters (0.0 vô cực đến 10.0 cận cảnh), ISO thủ công (50 - 3200), White Balance và đèn Flash LED trợ sáng Torch.
    - Luồng ảnh tĩnh độ nét cao Full-Res Snapshot qua `ImageReader` chuyên dụng khi có yêu cầu `/photo.jpg`.
  - [x] **Máy Chủ HTTP Nhúng Hiệu Năng Cao** ([HttpCameraServer.java](file:///g:/NODEJS/Vision2026/AndroidCameraServer/app/src/main/java/com/vision/cameraserver/HttpCameraServer.java)):
    - Luồng xem trước `/video` (MJPEG 30-60 FPS), khung hình `/shot.jpg`, ảnh tĩnh `/photo.jpg`, trạng thái `/status.json`.
    - Tiếp nhận và áp dụng tức thì toàn bộ các endpoint điều khiển Pro Mode `/settings/...`.
  - [x] **Giao Diện & Vòng Đời Thiết Bị** ([MainActivity.java](file:///g:/NODEJS/Vision2026/AndroidCameraServer/app/src/main/java/com/vision/cameraserver/MainActivity.java), [activity_main.xml](file:///g:/NODEJS/Vision2026/AndroidCameraServer/app/src/main/res/layout/activity_main.xml)):
    - Giao diện Dark Mode công nghiệp hiện đại, hiển thị IP USB 127.0.0.1:8080, Wi-Fi IP, Ethernet IP, HUD Pro Mode thời gian thực.
    - Giữ màn hình luôn sáng (`FLAG_KEEP_SCREEN_ON`) kết hợp `WakeLock` đảm bảo hoạt động 24/7 không bị ngủ.
  - [x] **Tự Động Biên Dịch & Cài Đặt Lên S24 Ultra**:
    - Script [build_and_install.ps1](file:///g:/NODEJS/Vision2026/AndroidCameraServer/build_and_install.ps1) tự động build APK và nạp thẳng vào Galaxy S24 Ultra (`SM-S928B`) qua cáp USB ADB.
  - [x] **Cấu Hình Git & Loại Trừ Thư Mục Build Android**:
    - Bổ sung vào [.gitignore](file:///g:/NODEJS/Vision2026/.gitignore), [AndroidCameraServer/.gitignore](file:///g:/NODEJS/Vision2026/AndroidCameraServer/.gitignore) và [AndroidCameraServer/app/.gitignore](file:///g:/NODEJS/Vision2026/AndroidCameraServer/app/.gitignore) loại trừ triệt để `.gradle/`, `build/`, `local.properties`, `*.apk`, `*.dex`, `.cxx/`, `captures/`.
  - [x] **Kiểm thử thực tế**:
    - Đã xác thực kết nối HTTP và điều khiển Pro Mode thành công trên thiết bị thật Galaxy S24 Ultra.
    - [AndroidCameraProModeTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/AndroidCameraProModeTests.cs): 6/6 test PASSED 100%.

- [x] Task 422: Tích Hợp Nguồn Ảnh Camera Android (Samsung Galaxy S24 Ultra) Pro Mode Qua Cáp USB / Wi-Fi / Ethernet:
  - [x] Bổ sung `ImageSourceType.AndroidCamera = 5`, driver `AndroidCameraDriver.cs`, giao diện Tool Editor và Tab Camera Settings.
  - [x] Tích hợp tự động ADB Port Forwarding `adb forward tcp:8080 tcp:8080`.

- [x] Task 421: Khắc Phục Triệt Để Độ Trễ 1 Giây/Nấc Khi Xả Hàng Đợi 16 Nấc (Queue Drain Delay) Trong Run Continuous:
  - [x] Chuyển toàn bộ các lệnh bắt tay PLC sang ThreadPool ngầm độc lập (`_ = Task.Run(...)`).
  - [x] Tự động bật `NonBlockingMode = true;` khi Run Continuous, nâng `UpdateQueueVisuals` lên `DispatcherPriority.Normal`.
  - [x] Kiểm thử tự động `PlcContinuousQueueTests.cs`: 6/6 test PASSED 100% (89ms/3 nấc).

- [x] Task 420: Tích Hợp Chế Độ PLC Trigger Vào Pipeline Bounded Channel 16 Nấc Bất Đồng Bộ Tool Editor.
- [x] Task 419: Tự Động Co Giãn Full Width 4 Cột Trên Tab Tool Editor Khi Ẩn/Hiện Cột.
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
