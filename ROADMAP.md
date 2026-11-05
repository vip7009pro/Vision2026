# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 427: Tối Ưu Triệt Để BlobDetection Bắt Khuyết Tật Theo WxL Khi Đã Hiệu Chuẩn, Tự Động RunFlow Preview Tức Thì & Loại Bỏ Slider Thr:
  - [x] **Thuật toán Pipeline Bắt Blob Khuyết Tật** ([InspectionService.Pipeline.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/InspectionService.Pipeline.cs)):
    - Chuẩn hóa ý nghĩa `Spec Max (W x L)`: Đây là ngưỡng khuyết tật tối đa cho phép. Khi đã hiệu chuẩn (`isCalibrated == true`), các blob có kích thước nhỏ hơn hoặc bằng WxL là nằm trong dung sai cho phép -> **BỎ QUA (`continue`), KHÔNG BẮT**.
    - Chỉ bắt những blob **VƯỢT QUÁ kích thước cho phép `maxBlobWidth x maxBlobLength`** (khuyết tật / dị vật lỗi).
    - Khi người dùng nhập `Spec Max (W x L)` lớn, toàn bộ các hạt bụi/nhiễu pixel nhỏ li ti đều tự động được bỏ qua, không còn bị bắt hay vẽ khung thừa thãi.
    - Nhận diện hệ số hiệu chuẩn đa tầng `calibPpm` qua `ChessboardCalibrationService.GetEffectiveCalibration`, `ChessboardCalibration` và `PixelsPerMm`.
    - Khi chưa hiệu chuẩn (`isCalibrated == false`), duy trì lọc theo dải diện tích pixel `minArea <= areaPx <= maxArea`.
  - [x] **Tự Động RunFlow Cập Nhật Preview Tức Thì** ([ToolEditorViewModel.ToolBlob.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolBlob.cs)):
    - Bổ sung lệnh `RunFlow()` vào tất cả setter của ToolBlob (`Blob_MaxBlobWidth`, `Blob_MaxBlobLength`, `Blob_MinBlobArea`, `Blob_MaxBlobArea`, `Blob_MaxAllowedBlobs`, `Blob_MinBlobDistance`, `Blob_Polarity`, `Blob_CountingMode`).
    - Khi người dùng thay đổi bất kỳ thông số nào trên panel, Flow tự động chạy lại ngay lập tức và làm mới ảnh preview cùng khung vẽ ROI/Blob tức thời (không cần bấm F5).
    - Cập nhật `Blob_IsCalibrated` kiểm tra đa tầng từ `ChessboardCalibrationService.GetEffectiveCalibration(_config)`.
  - [x] **Đồng Bộ Hệ Số Hiệu Chuẩn Job** ([ChessboardCalibrationService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/ChessboardCalibrationService.cs)):
    - Trong `EnsureCalibration`: Tự động gán `config.PixelsPerMm = config.ChessboardCalibration.PixelsPerMm` nếu Job có ChessboardCalibration nhưng chưa gán PixelsPerMm.
  - [x] **Giao Diện Bảng Properties Tool Editor** ([ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml)):
    - Loại bỏ hoàn toàn thanh trượt `Thr`, nhãn text và khung preview nhị phân `BlobThresholdPreviewImage`.
    - Tự động hiển thị linh hoạt: Đã hiệu chuẩn hiện badge xanh `🎯 Đã hiệu chuẩn — Bắt blob theo Kích thước W x L (mm)` và ô nhập `WxL`; Chưa hiệu chuẩn hiện badge cam `⚠️ Chưa hiệu chuẩn — Bắt blob theo Diện tích MinA / MaxA (px)` và ô nhập `MinA / MaxA`.
  - [x] **Kiểm Thử Tự Động Toàn Diện** ([NewJobAndBlobSpecTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/NewJobAndBlobSpecTests.cs), [BlobCountingModeTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/BlobCountingModeTests.cs)):
    - 9/9 test NewJob & BlobSpec PASSED 100%: Test 7 Calibrated WxL defect filter (WxL 5x5 mm chỉ bắt đúng blob vượt quá kích thước, WxL 15x15 mm bỏ qua toàn bộ không bắt blob nhỏ, WxL 1x1 mm bắt cả 3 blob), Test 8 Uncalibrated MinA/MaxA, Test 9 ViewModel.
    - 6/6 test BlobCountingMode PASSED 100%.

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
