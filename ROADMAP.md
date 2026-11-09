# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 430: Chuyển Đổi Thủ Công Chế Độ Spec BlobDetection (Theo Pixel Hoặc Theo mm) Thay Vì Tự Động Theo Hiệu Chuẩn:
  - [x] **Mô Hình Dữ Liệu & Khởi Tạo Mặc Định** ([Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Class1.cs), [InspectionResultModels.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Results/InspectionResultModels.cs)):
    - Bổ sung enum `BlobSpecMode { Pixel = 0, Millimeter = 1 }`.
    - Thêm trường `SpecMode` vào `BlobDetectionDefinition` với giá trị mặc định là `BlobSpecMode.Pixel` (0), đảm bảo tương thích ngược 100% khi nạp các tệp Job cũ.
    - Lưu kèm `BlobSpecMode SpecMode` trong `BlobDetectionResult`.
  - [x] **Thuật Toán Lọc Blob Theo Lựa Chọn Của Người Dùng** ([InspectionService.Pipeline.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/InspectionService.Pipeline.cs)):
    - Trong `DetectBlobsInCrop`: Thay vì tự động rẽ nhánh theo `isCalibrated`, thuật toán rẽ nhánh lọc dựa trên `specMode` do người dùng thiết lập.
    - Khi chọn `BlobSpecMode.Pixel`: Lọc chặt chẽ theo dải diện tích pixel `[minArea, maxArea]`, không bị can thiệp bởi WxL hay trạng thái hiệu chuẩn của Job.
    - Khi chọn `BlobSpecMode.Millimeter`: Lọc theo kích thước thực tế W x L (mm), bỏ qua các hạt nhỏ nằm trong dung sai cho phép và chỉ bắt các khuyết tật vượt ngưỡng spec Max (WxL).
    - Trong bước đánh giá kích thước (`checkSize`): Chỉ kiểm tra và đánh dấu lỗi kích thước `BlobSize` khi node đang ở chế độ Millimeter.
  - [x] **Giao Diện & Tương Tác Người Dùng** ([ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml), [ToolEditorViewModel.ToolBlob.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolBlob.cs)):
    - Bổ sung dropdown ComboBox `Chế độ Spec` trên bảng thuộc tính Tool Editor: `"Theo pixel (MinA, MaxA)"` và `"Theo mm (Kích thước W x L)"`.
    - Ẩn/hiện độc lập khung nhập MinA/MaxA (pixel) và khung nhập Spec Max WxL (mm) theo lựa chọn của người dùng (`Blob_IsPixelSpecMode`, `Blob_IsMmSpecMode`).
    - Thêm badge cảnh báo màu cam khi người dùng chọn theo mm nhưng Job chưa được hiệu chuẩn (tỉ lệ 1 px = 1 mm).
    - Tự động kích hoạt `RunFlow()` và làm mới ảnh preview tức thì ngay khi người dùng chuyển đổi chế độ spec.
  - [x] **Đồng Bộ Bảng Kết Quả Spec & Kiểm Thử Tự Động** ([NewJobAndBlobSpecTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/NewJobAndBlobSpecTests.cs)):
    - 9/9 test NewJob & BlobSpec PASSED 100% (Test 7 kiểm tra chế độ Millimeter, Test 8 kiểm tra chế độ Pixel mặc định ngay cả khi Job có hiệu chuẩn, Test 9 kiểm tra chuyển đổi qua lại giữa Pixel và mm).
    - 6/6 test BlobCountingMode PASSED 100%.

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
