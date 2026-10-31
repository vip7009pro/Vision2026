# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

- [x] Task 426: Bổ Sung Chế Độ Xuất Kết Quả OK/NG Dạng Số Nguyên (Int16) Song Hành Cùng Dạng Bit Bool Trong Cửa Sổ Bắt Tay 24/7 (Handshake):
  - [x] **Mô hình Dữ liệu & Cấu hình Bắt tay** ([PlcIndustrialConfig.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/PlcIndustrialConfig.cs)):
    - Định nghĩa enum `PlcResultOutputMode` gồm 3 chế độ: `Bit = 0` (2 bit Pass/NG), `NumericInt16 = 1` (Thanh ghi Word Int16), `Both = 2` (Song hành đồng thời cả Bit & Số).
    - Mở rộng `IndustrialHandshakeConfig`: `ResultOutputMode`, `ResultWordTagName` (mặc định D100 FX5U / MW100 I/O), `PassNumericValue = 2`, `NgNumericValue = 3`, `IdleNumericValue = 0`, `ClearResultWordOnReady = true`, `IsBitModeActive`, `IsNumericModeActive`.
    - Hiện thực `INotifyPropertyChanged` cho `IndustrialHandshakeConfig`, cập nhật `CopyFrom`, `HasConfiguredTags`, `ApplyFx5uDefaults`, `ApplyStandardIoDefaults`.
  - [x] **Máy Trạng Thái & Dịch Vụ PLC** ([IndustrialHandshakeStateMachine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/PLC/Services/IndustrialHandshakeStateMachine.cs), [PlcManagerService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/PLC/Services/PlcManagerService.cs)):
    - Thêm resolver `GetEffectiveResultWordTag()` (fallback `D100_VisionResult`, `D100`, `MW100`).
    - Trong `SetReadyAsync` & `SetIdleAsync`: Tự động ghi `IdleNumericValue` (0) vào thanh ghi Word khi sẵn sàng đón phôi mới.
    - Trong `CompleteHandshakeAsync`: Phát tín hiệu bit khi chọn Bit/Both, ghi giá trị `PassNumericValue` (2) / `NgNumericValue` (3) dạng `short` vào thanh ghi Word khi chọn NumericInt16/Both.
    - `EnsureIndustrialTagsRegistered` & `GetAllTagsToPoll`: Tự động đăng ký và nạp polling cho `ResultWordTagName` với kiểu `PlcDataType.Int16`.
  - [x] **Giao Diện Người Dùng & Giám Sát Thanh Công Cụ** ([PlcManagerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/PLC/PlcManagerViewModel.cs), [PlcManagerWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/PLC/PlcManagerWindow.xaml), [ToolEditorViewModel.HandshakeUi.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.HandshakeUi.cs), [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml)):
    - Tab 2 Bắt Tay 24/7: Bổ sung ComboBox chọn chế độ xuất kết quả (Bit / Dạng Số Int16 / Song Hành Cả Hai); phân vùng cấu hình trực quan cho Bit Bool và Thanh Ghi Số Word (Thanh ghi Word, OK=2, NG=3, Idle=0, Clear on Ready).
    - Thanh công cụ Tool Editor: Bổ sung Mini Indicator `D100: 2` (Xanh) khi OK và `D100: 3` (Đỏ) khi NG; hiển thị chi tiết thanh ghi kết quả trên Handshake ToolTip và Status Bar.
  - [x] **Kiểm Thử Tự Động Toàn Diện** ([PlcHandshakeNumericModeTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/PlcHandshakeNumericModeTests.cs)):
    - 7/7 test PASSED 100%: JSON serialization, defaults/helpers, ghi OK=2 và NG=3 dạng Int16, chế độ Song hành Both đồng thời Bit & Số, giá trị tùy chỉnh (OK=10, NG=99, Idle=-1), đăng ký Tag Int16, ViewModel options.
    - Regression tests PASSED 100%: `PlcHandshakePersistenceTests` (7/7) và `PlcSignalTypeTests` (5/5).

- [x] Task 425: Bổ Sung Chi Tiết Báo Lỗi NG Cho Tool CodeDetection Trong Cột Kết Quả Tool Editor & Inspection View:
  - [x] **Cập nhật Logic Báo Lỗi NG & Spec** ([ToolEditorViewModel.Inspection.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Inspection.cs), [InspectionViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/InspectionViewModel.cs)):
    - Nâng cấp record `CodeDetectionRow` bổ sung `Pass`, `ExpectedSpec`, `StatusText`, `StatusBrush`, `DetailText`.
    - Bổ sung nạp kết quả từng tool `CodeDetection` vào bảng `SpecResults` (RefA: Chuỗi đọc được, RefB: Chuỗi Spec, Pass: Pass, Value: 1.0/0.0, Unit: "code").
    - Cải tiến `UpdateResultSummary`: Đổi lọc sang `.Where(x => !x.Pass)`, phân tách rõ ràng 2 trường hợp: không đọc được mã và đọc được nhưng sai Spec, đính kèm tên tool cụ thể vào `NgReasonsText`.
    - Thêm thuộc tính `ShowCodeDetectionSection => IsCodeDetectionNode || CodeDetectionResults.Count > 0;`.
  - [x] **Giao Diện Tool Editor & Inspector** ([ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml), [ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs)):
    - Bổ sung `SelectedRunPass` cho node type `"CodeDetection"` để hiển thị đúng trạng thái PASS/NG trên Property Panel.
    - Chuyển thẻ "📷 ĐỌC MÃ VẠCH / QR DEBUG" sang binding `ShowCodeDetectionSection` để tự động hiển thị khi flow có đọc mã.
    - Cải tiến DataTemplate đọc mã vạch/QR với badge `[PASS]` / `[NG]`, chuỗi đọc, chuỗi Spec và dòng chi tiết lỗi rõ ràng.
  - [x] **Hệ Thống Biến & Overlay Engine** ([ConditionEvaluator.VariableRegistry.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/ConditionEvaluator.VariableRegistry.cs), [ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs), [InspectionService.ImageOutputs.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/InspectionService.ImageOutputs.cs)):
    - Đăng ký chuẩn xác các biến `Pass`, `Status`, `Expected`, `PassBit` theo Spec trong ConditionEvaluator.
    - Vẽ overlay viền Crimson rõ ràng khi NG: Viền SearchRoi đỏ khi không đọc được mã; Viền bounding box đỏ kèm nhãn `[NG]` khi sai Spec.
  - [x] **Kiểm Thử Tự Động Toàn Diện** ([CodeDetectionResultTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/CodeDetectionResultTests.cs)):
    - 6/6 test PASSED 100%: Kiểm tra Not Found báo lỗi kèm tên tool, đọc sai Spec báo lỗi kèm chuỗi so sánh, đọc đúng Spec PASS, SelectedRunPass phản ánh chuẩn, ConditionEvaluator đăng ký biến chuẩn, InspectionViewModel đồng bộ SpecResults & NgReasons.

- [x] Task 424: Bổ Sung Tool ObjectCounting Trong Tool Editor (Đếm Vật Thể Đa Hướng Hỗn Loạn, Quản Lý Spec & Dung Sai):
  - [x] Model `ObjectCountingDefinition.cs`, Engine `ObjectCountingEngine.cs`, Circular Window, Rotated NMS.
  - [x] Tích hợp Pipeline, Condition Evaluator & Bảng Spec Results.
  - [x] Kiểm thử tự động `ObjectCountingTests.cs`: 5/5 test PASSED 100% (Đếm chính xác tuyệt đối 10/10 viên thuốc trên vỉ thuốc thực tế).

- [x] Task 423: Xây Dựng Ứng Dụng Android Native "VisionCameraServer" Cho Galaxy S24 Ultra & Tích Hợp Camera2 Pro Mode:
  - [x] Dự án Android Native, Camera2 Multi-Camera & Pro Mode, HTTP Server, APK build script.
  - [x] Kiểm thử tự động `AndroidCameraProModeTests.cs`: 6/6 test PASSED 100%.

- [x] Task 422: Tích Hợp Nguồn Ảnh Camera Android (Samsung Galaxy S24 Ultra) Pro Mode Qua Cáp USB / Wi-Fi / Ethernet.
- [x] Task 421: Khắc Phục Triệt Để Độ Trễ 1 Giây/Nấc Khi Xả Hàng Đợi 16 Nấc (Queue Drain Delay) Trong Run Continuous.
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
