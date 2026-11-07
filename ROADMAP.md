# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 429: Khắc Phục Triệt Để Lỗi ObjectDisposedException ("Cannot access a disposed object OpenCvSharp.Mat") Khi Chụp Ảnh Trong Tool Editor & Manual Inspection:
  - [x] **Tách Bạch Quyền Sở Hữu Mat Cho Caller Snapshot** ([HikCameraDriver.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Services/Camera/Drivers/HikCameraDriver.cs)):
    - Loại bỏ `using var rawMat` gây tự động dispose Mat trước khi trả về caller trong `GrabFrameAsync`.
    - Clone 1 bản Mat độc lập `rawMat.Clone()` khi trả về snapshot từ Ring Buffer `_matPool`, bảo toàn tuyệt đối 100% ô nhớ của Driver khi caller gọi `mat.Dispose()`.
  - [x] **Chuẩn Hóa Xử Lý Frame Trên Toàn Bộ Camera Drivers** ([OpenCvCameraDriver.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Services/Camera/Drivers/OpenCvCameraDriver.cs), [AndroidCameraDriver.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Services/Camera/Drivers/AndroidCameraDriver.cs)):
    - Triệt tiêu hoàn toàn `using var` trên các đường dẫn đọc frame của `GrabFrameAsync` để không bao giờ trả về Mat đã bị Dispose.
  - [x] **Bảo Vệ Tài Nguyên CameraService & Helper An Toàn** ([CameraService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Services/CameraService.cs), [MatExtensions.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Services/MatExtensions.cs)):
    - Trong `CaptureSnapshotFromCameraAsync`: clone ảnh snapshot độc lập trước khi `driver.CloseAsync()` và `driver.Dispose()`.
    - Trong `TryGetLatestFrameClone()`: kiểm tra an toàn `!_lastFrame.IsDisposed && !_lastFrame.Empty()` trước khi gọi `.Clone()`.
    - Bổ sung extension method `MatExtensions.IsValid()` và kiểm tra an toàn `!mat.IsDisposed` trên tất cả ViewModels.
  - [x] **Kiểm Thử Tự Động Toàn Diện** ([CameraTest.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/CameraTest.cs)):
    - 4/4 tests PASSED 100% (`MatExtensions.IsValid` an toàn với null/disposed; `NativeMatPool` ring buffer isolation; `SimulatorCameraDriver.GrabFrameAsync` caller disposal safety; `CameraService.CaptureSnapshotAsync` & `TryGetLatestFrameClone` lifecycle safety).

- [x] Task 428: Tối Ưu Triệt Để Stream Live Camera Hikrobot 25MP MV-CH250-90TC-C-NF Qua Card Mạng 10GbE MV-GT1002 Đạt 41 FPS & Chuẩn Màu 100% Như Phần Mềm MVS.
- [x] Task 427: Tối Ưu Triệt Để BlobDetection Bắt Khuyết Tật Theo WxL Khi Đã Hiệu Chuẩn, Tự Động RunFlow Preview Tức Thì & Loại Bỏ Slider Thr.
- [x] Task 426: Bổ Sung Chế Độ Xuất Kết QuẢ OK/NG Dạng Số Nguyên (Int16) Song Hành Cùng Dạng Bit Bool Trong Cửa Sổ Bắt Tay 24/7 (Handshake).
- [x] Task 425: Bổ Sung Chi Tiết Báo Lỗi NG Cho Tool CodeDetection Trong Cột Kết Quả Tool Editor & Inspection View.
- [x] Task 424: Bổ Sung Tool ObjectCounting Trong Tool Editor (Đếm Vật Thể Đa Hướng Hỗn Loạn, Quản Lý Spec & Dung Sai).
- [x] Task 423: Xây Dựng Ứng Dụng Android Native "VisionCameraServer" Cho Galaxy S24 Ultra & Tích Hợp Camera2 Pro Mode.
- [x] Task 422: Tích Hợp Nguồn Ảnh Camera Android (Samsung Galaxy S24 Ultra) Pro Mode Qua Cáp USB / Wi-Fi / Ethernet.
- [x] Task 421: Khắc Phục Triệt Để Độ Trễ 1 Giây/Nấc Khi Xả Hàng Đợi 16 Nấc (Queue Drain Delay) Trong Run Continuous.
- [x] Task 420: Tích Hợp Chế Độ PLC Trigger Vào Pipeline Bounded Channel 16 Nấc Bất Đồng Bộ Tool Editor.
- [x] Task 419: Tự Động Co Giãn Full Width 4 Cột Trên Tab Tool Editor Khi Ẩn/Hiện Cột.
- [x] Task 418: Tự Động Nạp Trạng Thái Hiện Tại Của Tag HMI Khi Load/Run & Bổ Sung Nút Stop HMI.
- [x] Task 417: Khắc Phục Triệt Để Tranh Chấp Kết Nối PLC & Cô Lập Hoàn Toàn HMI Tool Editor.
- [x] Task 416: Khắc Phục Triệt Để Lỗi Thread Ownership Ngắt Kết Nối PLC Ethernet & Chuẩn Hóa Chế Độ Bỏ PLC Ack.

## Định hướng tiếp theo
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
- [ ] Tối ưu hóa render đa luồng (Multi-threaded Rendering) cho tài liệu PDF kích thước lớn >50MB.
