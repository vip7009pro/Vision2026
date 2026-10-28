# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode Native Server, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Dự án Android Native**: `AndroidCameraServer` (Gradle 8.11, CompileSDK 34, Android 14) đã biên dịch thành công APK và cài đặt trực tiếp lên Samsung Galaxy S24 Ultra (`SM-S928B`).
- **Kiểm thử tự động**: PASSED 100% (6/6 test AndroidCameraProMode, 6/6 test PlcContinuousQueue 16 Slots, 7/7 test ToolEditor Column Resize & Full-Width, 5/5 test HMI State & Lifecycle, 4/4 test HMI Designer Persistence & DINT 32-bit, 4/4 test PLC Conflict & HMI Isolation, 7/7 test PLC Handshake, 5/5 test Reset Queue & HMI, 5/5 test PLC Signal Types, 11/11 test SurfaceCompare & Caliper, 8/8 test ContourCompare, 12/12 test Tool Calib Factor, 15/15 test OQC Scanner Live View, 8/8 test MvpShapeMatch2).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, PlcAck `""` Non-blocking, Queue In `M200`).

## 3. Hoàn thành Task 423: Xây Dựng Ứng Dụng Android Native "VisionCameraServer" Cho Galaxy S24 Ultra & Tích Hợp Camera2 Pro Mode
1. **Bối cảnh & Vấn đề**:
   - Các ứng dụng Camera IP tải trên CH Play không cho phép hoặc bỏ qua việc điều khiển phơi sáng, lấy nét, ISO, WB và đặc biệt là chuyển đổi 4 ống kính vật lý từ máy tính.
   - Xây dựng trọn bộ ứng dụng Android Native chuyên biệt: `AndroidCameraServer/` (`com.vision.cameraserver`).
2. **Kiến trúc & Tính năng đã hoàn thành**:
   - `Camera2Manager.java`:
     - Tích hợp chuẩn Camera2 API cho Samsung Galaxy S24 Ultra: Cảm biến chính 200MP (Lens 0), Tele 3x (Lens 1), Tele 5x Periscope 50MP (Lens 2), Ultra-Wide 0.6x Macro (Lens 3), Camera trước (Lens 4).
     - Hỗ trợ phơi sáng thủ công nano-giây (`SENSOR_EXPOSURE_TIME`), bù trừ sáng EV (-2.0 đến +2.0), lấy nét thủ công Manual Focus diopters (`LENS_FOCUS_DISTANCE`), ISO thủ công (50 - 3200), White Balance và đèn Flash LED trợ sáng Torch.
     - Luồng ảnh tĩnh Full-Res Sensor Snapshot (12MP/50MP/200MP) qua `ImageReader` chuyên dụng khi có yêu cầu `/photo.jpg`.
   - `HttpCameraServer.java`:
     - Máy chủ HTTP nhúng hiệu năng cao cổng 8080.
     - Phát luồng video MJPEG `/video` thời gian thực (30-60 FPS), khung hình `/shot.jpg`, ảnh tĩnh `/photo.jpg`, trạng thái `/status.json` và nhận toàn bộ lệnh `/settings/...`.
   - `MainActivity.java` & `activity_main.xml`:
     - Giao diện Dark Mode công nghiệp hiện đại, hiển thị IP Wi-Fi, Ethernet, USB 127.0.0.1, Live HUD thông số Pro Mode thời gian thực.
     - Tích hợp `FLAG_KEEP_SCREEN_ON` và `WakeLock` giữ máy hoạt động liên tục 24/7.
   - Script tự động `build_and_install.ps1`:
     - Tự động dùng Gradle 8.11 biên dịch APK `app-debug.apk`.
     - Tự động cài đặt qua `adb install -r`, cấp quyền `CAMERA`, mở ứng dụng và kích hoạt `adb forward tcp:8080 tcp:8080`.
   - Cấu hình `.gitignore`: Loại trừ toàn bộ các thư mục build sinh ra bởi Android Gradle (`.gradle/`, `build/`, `app/build/`, `local.properties`, `*.apk`, `*.dex`, `.cxx/`, `captures/`).
3. **Kiểm thử thực tế trên thiết bị thật Galaxy S24 Ultra (`SM-S928B`)**:
   - Ứng dụng đã được cài đặt và đang chạy trên điện thoại qua cổng USB ADB `127.0.0.1:8080`.
   - Xác thực truyền lệnh thành công: `/settings/exposure_compensation?set=1.0` -> phản hồi EV 1.00; `/enabletorch` -> đèn Flash sáng; tải ảnh `/shot.jpg` đạt 287 KB nguyên bản.
   - Kiểm thử tự động `AndroidCameraProModeTests.cs` (6/6 test PASSED 100%).

## 4. Hoàn thành Task 422: Tích Hợp Nguồn Ảnh Camera Android (Samsung Galaxy S24 Ultra) Pro Mode Trên Phần Mềm Vision 2026
- Bổ sung `ImageSourceType.AndroidCamera`, driver `AndroidCameraDriver.cs`, giao diện Tool Editor và Tab Camera Settings.

## 5. Các sự kiện & thay đổi gần đây
- Task 423: Xây dựng Android App VisionCameraServer (Camera2 Pro Mode, 200MP, EV, Focus, Flash Torch) & cài đặt thành công lên S24 Ultra; kiểm thử 6/6 PASSED 100%.
- Task 422: Tích hợp Camera Android Galaxy S24 Ultra Pro Mode (USB/Wi-Fi/LAN, 200MP, EV, Focus, Flash Torch); kiểm thử 6/6 PASSED 100%.
- Task 421: Khắc phục triệt để độ trễ 1 giây/nấc khi xả hàng đợi 16 nấc Run Continuous; kiểm thử 6/6 PASSED (89ms/3 nấc).
- Task 420: Tích hợp PLC Trigger vào BoundedChannel 16 nấc & thêm nút Trigger tay; kiểm thử 5/5 PASSED 100%.
- Task 419: Tự động co giãn Full Width 4 cột trên Tab Tool Editor khi Ẩn/Hiện cột; kiểm thử 7/7 PASSED 100%.
- Task 418: Tự động nạp trạng thái hiện tại của Tag HMI khi Load/Run, khắc phục lỗi mất tên file/không tương tác được & thêm nút Stop HMI; kiểm thử 5/5 PASSED 100%.
- Task 417: Khắc phục triệt để tranh chấp kết nối PLC làm đổi trạng thái xanh/đỏ liên tục & cô lập hoàn toàn HMI Tool Editor; kiểm thử 4/4 PASSED 100%.
- Task 416: Khắc phục triệt để lỗi Thread Ownership làm ngắt kết nối PLC Ethernet & chuẩn hóa chế độ bỏ PLC Ack / Non-blocking; kiểm thử 7/7 PASSED 100%.
- Task 415: Chia đôi cột Preview Tool Editor hiển thị HMI & bổ sung tag Reset Queue Vision Trong Cài Đặt Bắt Tay 24/7 Handshake; kiểm thử 5/5 PASSED 100%.
- Task 414: Khắc phục triệt để lỗi lệch tịnh tiến ảnh template SurfaceCompare khi sửa job cũ có Origin offset; kiểm thử 11/11 PASSED 100%.
