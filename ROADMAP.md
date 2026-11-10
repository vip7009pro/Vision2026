# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 431: Nâng Cấp Preview Tool Editor Khi Bỏ Render Canvas & Bổ Sung Widget Tốc Độ Tức Thời (pcs/s):
  - [x] **Hiển Thị Ảnh Tĩnh OK & Bật Canvas Khi NG Trong Run Continuous** ([ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs)):
    - Khởi tạo ảnh tĩnh "OK" chuẩn công nghiệp 1280x720 (`GetOrCreateStaticOkImage`) nền tối `#0F172A`, vòng tròn xanh lá `#10B981` cùng chữ "OK" to (150pt) ở giữa, đóng băng `Freeze()` dùng chung.
    - Trong chế độ Chạy liên tục (`IsRunningContinuous`), khi uncheck `Render Canvas`: Sản phẩm OK chỉ hiển thị ảnh tĩnh "OK", bỏ qua chuyển đổi OpenCV và render overlay (tối ưu hóa 100% FPS).
    - Khi phát hiện sản phẩm lỗi NG: Tự động kích hoạt render canvas để hiển thị ảnh chụp thực tế kèm bounding box lỗi. Sau đó khi gặp con hàng OK tiếp theo thì tự động quay về ảnh tĩnh "OK".
    - Ưu tiên đẩy frame lỗi NG lên UI ngay lập tức không bị throttle/drop trong `RunContinuousInspectionLoopAsync`.
  - [x] **Tự Động AutoFit Preview Khi Bấm Quay Lại Live** ([ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs), [ToolEditorView.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml.cs)):
    - Bổ sung sự kiện `RequestAutoFitImagePreview` trên ViewModel và kích hoạt trong `ExitRecentPartView()`.
    - Trong code-behind `ToolEditorView.xaml.cs`, đăng ký sự kiện và tự động gọi `PreviewImageViewer?.ResetView()`.
  - [x] **Widget Tốc Độ Tức Thời (Instantaneous Speed pcs/s)** ([ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml), [ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs)):
    - Áp dụng cơ chế Rolling Window đo dấu thời gian của tối đa 8 sản phẩm gần nhất (`RecordPieceCompleted()`).
    - Tự động hạ về `0.0` ngay khi máy tạm dừng / chờ phôi (> 1.5s), khắc phục triệt để nhược điểm của tốc độ tính trung bình toàn phiên từ lúc bấm Chạy liên tục.
    - Sửa `UniformGrid Columns="3"` thành `Columns="4"`, bổ sung ô chip `TỨC THỜI (pcs/s)` màu xanh lam `#0284C7` đặt cạnh `TỔNG (ms)`, `COUNT`, `YIELD 20`.
  - [x] **Kiểm Thử Tự Động Toàn Diện** ([ToolEditorContinuousRenderAndSpeedTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/ToolEditorContinuousRenderAndSpeedTests.cs)):
    - 8/8 test PASSED 100% (ảnh tĩnh OK, AutoFit event, OK hiện ảnh tĩnh, NG hiện canvas, chuyển đổi qua lại OK/NG, tính tốc độ tức thời rolling window, tự động về 0 khi dừng, reset khi Stop).
    - Toàn bộ solution biên dịch đạt 0 Errors.

- [x] Task 430: Chuyển Đổi Thủ Công Chế Độ Spec BlobDetection (Theo Pixel Hoặc Theo mm) Thay Vì Tự Động Theo Hiệu Chuẩn:
  - [x] Bổ sung enum `BlobSpecMode { Pixel = 0, Millimeter = 1 }` và thuộc tính `SpecMode` vào `BlobDetectionDefinition`.
  - [x] Thuật toán rẽ nhánh lọc blob theo lựa chọn của người dùng (Pixel lọc MinA/MaxA, Millimeter lọc kích thước WxL).
  - [x] Thêm ComboBox lựa chọn trên Tool Editor, kiểm thử 9/9 test PASSED 100%.

- [x] Task 429: Khắc Phục Triệt Để Lỗi ObjectDisposedException ("Cannot access a disposed object OpenCvSharp.Mat") Khi Chụp Ảnh Trong Tool Editor & Manual Inspection.
- [x] Task 428: Tối Ưu Triệt Để Stream Live Camera Hikrobot 25MP MV-CH250-90TC Qua Card 10GbE MV-GT1002 Đạt 41 FPS & Chuẩn Màu 100% MVS.
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
