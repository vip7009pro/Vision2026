# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 433: Hiện Thực Hóa 2 Thuật Toán Tăng Tốc Template Marking: MVPShapeMatch3 & MVPShapeMatchInt8:
  - [x] Lập kế hoạch kiến trúc chi tiết cho `MvpShapeMatch3` (Lazy Local Gradient, Pre-rotated Cache, Deep Pyramid) và `MvpShapeMatchInt8` (SIMD AVX2 INT8).
  - [x] Mở rộng enum `OriginAlgorithm` và `PointFindAlgorithm` trong [`Class1.cs`](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Class1.cs).
  - [x] Hiện thực hóa động cơ [`MvpShapeMatch3Engine.cs`](file:///g:/NODEJS/Vision2026/VisionInspectionApp.VisionEngine/MvpShapeMatch3Engine.cs) (Đạt 3.54ms, nhanh hơn 11.7x so với v2).
  - [x] Hiện thực hóa động cơ [`MvpShapeMatchInt8Engine.cs`](file:///g:/NODEJS/Vision2026/VisionInspectionApp.VisionEngine/MvpShapeMatchInt8Engine.cs) (Đạt 2.74ms, nhanh hơn 15.0x so với v2, đánh bại mốc 7ms của MVP).
  - [x] Tích hợp điều hướng trong [`OriginMatcher.cs`](file:///g:/NODEJS/Vision2026/VisionInspectionApp.VisionEngine/OriginMatcher.cs), [`ToolEditorViewModel.ToolOrigin.cs`](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolOrigin.cs), [`OriginTrainViewModel.cs`](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/OriginTrainViewModel.cs).
  - [x] Xây dựng bộ test kiểm thử tự động & benchmark đo tốc độ thực tế trong [`MvpShapeMatch3AndInt8Tests.cs`](file:///g:/NODEJS/Vision2026/TestExtractApp/MvpShapeMatch3AndInt8Tests.cs): 6/6 test PASSED 100%.

- [x] Task 432: Tự Động Ẩn Cột 1 & 2, Hiển Thị Cột 3 & 4 (50/50) Khi Chạy Liên Tục:
  - [x] **Quản Lý Trạng Thái Layout Cột Trên ViewModel** ([ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs), [ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs)):
    - Cập nhật getter `PreviewColumnWidth` và `DebugColumnWidth`: Tự động chuyển thành tỷ lệ `1.0* : 1.0*` (50/50) khi `IsRunningContinuous && !IsToolboxColumnVisible && !IsCanvasColumnVisible && IsDebugColumnVisible`; khi dừng quay về tỷ lệ mặc định ban đầu (`4.5* : 3.2*`).
    - Bổ sung hàm `SetContinuousColumnVisibility(bool isContinuous)`: Khi chạy liên tục -> tự động ẩn Cột 1 & 2, hiển thị Cột 4; khi tắt chạy liên tục -> tự động hiển thị lại Cột 1 & 2. Tương thích an toàn đa luồng qua `Dispatcher.BeginInvoke`.
    - Setter `IsRunningFolderFlow`: Tích hợp gọi `SetContinuousColumnVisibility` và kích hoạt sự kiện `PropertyChanged` cho `PreviewColumnWidth`, `DebugColumnWidth`.
  - [x] **Xử Lý Bố Cục Star & Bảo Toàn Kích Thước Trên View** ([ToolEditorView.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml.cs)):
    - Lắng nghe `nameof(ToolEditorViewModel.IsRunningContinuous)` trong `OnViewModelPropertyChanged`.
    - Trong `ApplyColumnLayout`: Xử lý riêng biệt cho trạng thái chạy liên tục (ẩn Cột 0 và Cột 2 về 0; áp dụng tỷ lệ `1.0*` cho Cột 3 và Cột 4 chia trọn màn hình 50% - 50%).
    - Khi dừng chạy liên tục: Tự động phục hồi lại toàn bộ Cột 0, 2, 4, 6 với các giá trị `_savedToolboxStar`, `_savedCanvasStar`, `_savedPreviewStar`, `_savedDebugStar` đã lưu trước đó.
    - Trong `NormalizeColumnWidthsToStar`: Chỉ cập nhật `_saved*` kích thước 4 cột ban đầu khi `!vm.IsRunningContinuous`, giúp không ghi đè tỷ lệ 50/50 tạm thời vào layout gốc.
  - [x] **Kiểm Thử Tự Động Toàn Diện** ([ToolEditorColumnResizeTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/ToolEditorColumnResizeTests.cs)):
    - Bổ sung 3 test mới: Test 8 (Ẩn Cột 1&2, Cột 3&4 tỷ lệ 1.0:1.0 Star 50/50 khi chạy liên tục), Test 9 (Hiện lại Cột 1&2, khôi phục tỷ lệ mặc định ban đầu khi dừng), Test 10 (Phát đầy đủ sự kiện PropertyChanged).
    - **10/10 test PASSED 100%**.
    - Toàn bộ solution biên dịch Debug/Release đạt **0 Errors**.

- [x] Task 431: Nâng Cấp Preview Tool Editor Khi Bỏ Render Canvas & Bổ Sung Widget Tốc Độ Tức Thời (pcs/s):
  - [x] Hiển thị ảnh tĩnh "OK" to khi kết quả Đạt (Pass), chỉ render canvas khi có khuyết tật NG, sau đó lại quay về ảnh tĩnh "OK".
  - [x] Tự động AutoFit View preview 1 lần khi click queue xem ảnh NG rồi bấm "Quay lại Live".
  - [x] Bổ sung widget "TỨC THỜI (pcs/s)" theo Rolling Window, tự động về 0 khi dừng; kiểm thử 8/8 test PASSED 100%.

- [x] Task 430: Chuyển Đổi Thủ Công Chế Độ Spec BlobDetection (Theo Pixel Hoặc Theo mm) Thay Vì Tự Động Theo Hiệu Chuẩn.
- [x] Task 429: Khắc Phục Triệt Để Lỗi ObjectDisposedException Khi Chụp Ảnh Trong Tool Editor & Manual Inspection.
- [x] Task 428: Tối Ưu Triệt Để Stream Live Camera Hikrobot 25MP MV-CH250-90TC Qua Card 10GbE MV-GT1002 Đạt 41 FPS & Chuẩn Màu 100% MVS.
- [x] Task 427: Tối Ưu Triệt Để BlobDetection Bắt Khuyết Tật Theo WxL Khi Đã Hiệu Chuẩn, Tự Động RunFlow Preview Tức Thì & Loại Bỏ Slider Thr.
- [x] Task 426: Bổ Sung Chế Độ Xuất Kết QuẢ OK/NG Dạng Số Nguyên (Int16) Song Hành Cùng Dạng Bit Bool Trong Cửa Sổ Bắt Tay 24/7 (Handshake).
- [x] Task 425: Bổ Sung Chi Tiết Báo Lỗi NG Cho Tool CodeDetection Trong Cột Kết Quả Tool Editor & Inspection View.
- [x] Task 424: Bổ Sung Tool ObjectCounting Trong Tool Editor (Đếm Vật Thể Đa Hướng Hỗn Loạn, Quản Lý Spec & Dung Sai).
- [x] Task 423: Xây Dựng Ứng Dụng Android Native "VisionCameraServer" Cho Galaxy S24 Ultra & Tích Hợp Camera2 Pro Mode.
- [x] Task 422: Tích Hợp Nguồn Ảnh Camera Android Pro Mode Qua Cáp USB / Wi-Fi / Ethernet.
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
