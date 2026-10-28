# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode Native Server, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Dự án Android Native**: `AndroidCameraServer` (Gradle 8.11, CompileSDK 34, Android 14) đã biên dịch thành công APK và cài đặt trực tiếp lên Samsung Galaxy S24 Ultra (`SM-S928B`).
- **Kiểm thử tự động**: PASSED 100% (6/6 test CodeDetection Result & NG Details, 5/5 test ObjectCounting, 6/6 test AndroidCameraProMode, 6/6 test PlcContinuousQueue 16 Slots, 7/7 test ToolEditor Column Resize & Full-Width, 5/5 test HMI State & Lifecycle, 4/4 test HMI Designer Persistence & DINT 32-bit, 4/4 test PLC Conflict & HMI Isolation, 7/7 test PLC Handshake, 5/5 test Reset Queue & HMI, 5/5 test PLC Signal Types, 11/11 test SurfaceCompare & Caliper, 8/8 test ContourCompare, 12/12 test Tool Calib Factor, 15/15 test OQC Scanner Live View, 8/8 test MvpShapeMatch2).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, PlcAck `""` Non-blocking, Queue In `M200`).

## 3. Hoàn thành Task 425: Bổ Sung Chi Tiết Báo Lỗi NG Cho Tool CodeDetection Trong Cột Kết Quả Tool Editor & Inspection View
1. **Bối cảnh & Vấn đề**:
   - Khi chạy flow trong Tool Editor hoặc màn hình Inspection, nếu node `CodeDetection` (Barcode/QR/DataMatrix) bị NG thì cột kết quả chỉ hiển thị trạng thái tổng thể là "NG", nhưng hoàn toàn không hiển thị chi tiết là NG do tool nào hay lý do tại sao (không đọc được mã hay sai Spec).
2. **Nguyên nhân cốt lõi**:
   - `ToolEditorViewModel.Inspection.cs` & `InspectionViewModel.cs`: Bảng `SpecResults` bị thiếu hoàn toàn việc nạp các dòng kết quả của tool `CodeDetection`.
   - Hàm `UpdateResultSummary` chỉ lọc `Where(x => !x.Found)`. Do đó, trường hợp đọc được mã nhưng sai chuỗi Spec (`Found == true, Pass == false`) dẫn đến danh sách lý do lỗi `reasons` bị rỗng. Banner kết quả rơi vào tình trạng chỉ hiện "NG" mà không có dòng mô tả cụ thể.
   - Thẻ UI "📷 ĐỌC MÃ VẠCH / QR DEBUG" bị gán cứng thuộc tính `Visibility="{Binding IsCodeDetectionNode}"`, khiến khi chuyển sang node khác hoặc xem toàn luồng thì thẻ kết quả đọc mã bị ẩn hoàn toàn.
   - Trạng thái `SelectedRunPass` của node `CodeDetection` trong Tool Editor chưa được ánh xạ trả về `d?.Pass`.
   - Các biến thành viên `Pass`, `Status`, `Expected` của `CodeDetection` trong `ConditionEvaluator.VariableRegistry.cs` bị gán cứng theo `cd.Found` thay vì đánh giá `cd.Pass` chuẩn xác theo Spec.
3. **Giải pháp & Các thành phần đã triển khai**:
   - `ToolEditorViewModel.Inspection.cs` & `InspectionViewModel.cs`:
     - Nâng cấp record `CodeDetectionRow(string Name, bool Found, string Text, bool Pass = true, string ExpectedSpec = "")` kèm các getter: `StatusText` ("PASS"/"NG"), `StatusBrush`, `StatusBgBrush`, `DetailText` (chỉ rõ không đọc được mã hoặc hiển thị trực quan `Chuỗi đọc: "..." != Chuỗi Spec: "..."`).
     - Bổ sung nạp kết quả từng tool `CodeDetection` vào bảng `SpecResults` (Hiển thị RefA: Mã đọc được, RefB: Mã Spec, Pass: cd.Pass, Value: 1.0/0.0, Unit: "code").
     - Cập nhật hàm `UpdateResultSummary`: Duyệt `.Where(x => !x.Pass)`. Phân tách chi tiết 2 trường hợp lỗi NG kèm tên tool cụ thể; bổ sung cơ chế fallback ghi nhận lý do khi tổng thể bị NG.
     - Bổ sung thuộc tính `ShowCodeDetectionSection => IsCodeDetectionNode || CodeDetectionResults.Count > 0;`.
   - `ToolEditorViewModel.cs` & `ToolEditorView.xaml`:
     - Bổ sung `SelectedRunPass` cho node type `"CodeDetection"` trả về `d?.Pass`.
     - Chuyển thẻ "📷 ĐỌC MÃ VẠCH / QR DEBUG" sang binding `{Binding ShowCodeDetectionSection}` để tự động hiển thị trong cột kết quả ngay khi flow có kết quả đọc mã.
     - Nâng cấp DataTemplate kết quả đọc mã: Thêm badge `[PASS]` / `[NG]`, hiển thị chuỗi đọc được, chuỗi Spec kỳ vọng và dòng chi tiết lỗi rõ ràng.
     - Bổ sung dòng hiển thị kết quả kiểm tra trực quan ngay trên Panel thuộc tính của node CodeDetection.
   - `ConditionEvaluator.VariableRegistry.cs`:
     - Sửa biến `["Pass"] = cd.Pass;`, `["Status"] = cd.Pass ? "OK" : "NG";`, `["PassBit"] = cd.Pass ? 1 : 0;`, `["Expected"] = cd.ExpectedSpec`.
   - `ToolEditorViewModel.Engine.cs` & `InspectionService.ImageOutputs.cs`:
     - Vẽ overlay viền Crimson rõ ràng khi NG: Nếu `!Found` vẽ khung SearchRoi kèm nhãn `NG (Not Found)`; nếu `Found && !Pass` vẽ bounding box kèm nhãn `[NG] {Text}`.
4. **Kiểm thử tự động**:
   - `CodeDetectionResultTests.cs`: 6/6 test PASSED 100% (Not Found báo NG & tên tool, Sai Spec báo NG & chuỗi sai khác, Khớp Spec PASS, SelectedRunPass phản ánh đúng, ConditionEvaluator đăng ký đúng biến, InspectionViewModel đồng bộ SpecResults & NgReasons).

## 4. Hoàn thành Task 424: Bổ Sung Tool ObjectCounting & Khắc Phục Triệt Để Lỗi Đếm 69 Vật Thể Trên Vỉ Thuốc
1. **Bối cảnh & Yêu cầu**:
   - Nhu cầu kiểm tra và đếm số lượng chi tiết/vật thể công nghiệp được teaching từ mẫu chuẩn trong Search ROI xoay tự do (-180° đến +180°). Quản lý Spec `NominalCount` và dung sai.
2. **Kiến trúc & Tính năng đã hoàn thành**:
   - `ObjectCountingDefinition.cs`, `ObjectCountingEngine.cs`, `ToolEditorViewModel.ToolObjectCounting.cs`, `ToolEditorView.xaml`.
   - Đặt mặc định thuật toán sang `NormalizedCorrelation` (NCC) kết hợp `ApplyCircularWindow` (Apodization/Cosine window) làm mờ 4 góc nền nhôm. Rotated NMS nâng cấp với khoảng cách tâm $0.70 \times \min(W, H)$ và tỷ lệ che phủ diện tích vật thể `interArea / minArea > MaxOverlap`.
   - `ObjectCountingTests.cs`: 5/5 test PASSED 100% (Đếm chính xác tuyệt đối **10/10 viên thuốc** trên vỉ thuốc thực tế).

## 5. Các sự kiện & thay đổi gần đây
- Task 425: Khắc phục triệt để lỗi cột kết quả chỉ hiện "NG" mà không có chi tiết tool khi node CodeDetection NG; bổ sung hiển thị chi tiết tên tool, mã đọc được, mã Spec trong cả SpecResults, NgReasonsText và thẻ CodeDetection; kiểm thử 6/6 PASSED 100%.
- Task 424: Hoàn thành Tool ObjectCounting, sửa dứt điểm lỗi đếm 69 vật thể trên vỉ thuốc -> đếm chuẩn xác 10/10 viên thuốc; kiểm thử 5/5 PASSED 100%.
- Task 423: Xây dựng Android App VisionCameraServer (Camera2 Pro Mode, 200MP, EV, Focus, Flash Torch) & cài đặt thành công lên S24 Ultra; kiểm thử 6/6 PASSED 100%.
- Task 422: Tích hợp Camera Android Galaxy S24 Ultra Pro Mode (USB/Wi-Fi/LAN, 200MP, EV, Focus, Flash Torch); kiểm thử 6/6 PASSED 100%.
- Task 421: Khắc phục triệt để độ trễ 1 giây/nấc khi xả hàng đợi 16 nấc Run Continuous; kiểm thử 6/6 PASSED (89ms/3 nấc).
- Task 420: Tích hợp PLC Trigger vào BoundedChannel 16 nấc & thêm nút Trigger tay; kiểm thử 5/5 PASSED 100%.
- Task 419: Tự động co giãn Full Width 4 cột trên Tab Tool Editor khi Ẩn/Hiện cột; kiểm thử 7/7 PASSED 100%.
- Task 418: Tự động nạp trạng thái hiện tại của Tag HMI khi Load/Run; kiểm thử 5/5 PASSED 100%.
- Task 417: Khắc phục triệt để tranh chấp kết nối PLC & cô lập hoàn toàn HMI Tool Editor; kiểm thử 4/4 PASSED 100%.
- Task 416: Khắc phục triệt để lỗi Thread Ownership làm ngắt kết nối PLC Ethernet; kiểm thử 7/7 PASSED 100%.
