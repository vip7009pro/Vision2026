# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode Native Server, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Dự án Android Native**: `AndroidCameraServer` (Gradle 8.11, CompileSDK 34, Android 14) đã biên dịch thành công APK và cài đặt trực tiếp lên Samsung Galaxy S24 Ultra (`SM-S928B`).
- **Kiểm thử tự động**: PASSED 100% (5/5 test ObjectCounting bao gồm cả test trên ảnh thực tế vỉ thuốc của người dùng, 6/6 test AndroidCameraProMode, 6/6 test PlcContinuousQueue 16 Slots, 7/7 test ToolEditor Column Resize & Full-Width, 5/5 test HMI State & Lifecycle, 4/4 test HMI Designer Persistence & DINT 32-bit, 4/4 test PLC Conflict & HMI Isolation, 7/7 test PLC Handshake, 5/5 test Reset Queue & HMI, 5/5 test PLC Signal Types, 11/11 test SurfaceCompare & Caliper, 8/8 test ContourCompare, 12/12 test Tool Calib Factor, 15/15 test OQC Scanner Live View, 8/8 test MvpShapeMatch2).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, PlcAck `""` Non-blocking, Queue In `M200`).

## 3. Hoàn thành Task 424: Bổ Sung Tool ObjectCounting & Khắc Phục Triệt Để Lỗi Đếm 69 Vật Thể Trên Vỉ Thuốc
1. **Bối cảnh & Yêu cầu**:
   - Nhu cầu kiểm tra và đếm số lượng chi tiết/vật thể công nghiệp (bulong, ốc vít, linh kiện SMD, phôi sản phẩm) được teaching từ một mẫu chuẩn trong Search ROI.
   - Vật thể có thể nằm ngẫu nhiên, hỗn loạn về vị trí và phương hướng xoay tự do trong vùng tìm kiếm (-180° đến +180°).
   - Cho phép người dùng thiết lập tiêu chuẩn kiểm tra (Spec): Số lượng danh định (`NominalCount`) cùng dung sai đối xứng (`Tolerance` ±) hoặc dung sai âm/dương (`ToleranceMinus`, `TolerancePlus`).
2. **Kiến trúc & Tính năng đã hoàn thành**:
   - `ObjectCountingDefinition.cs` & `VisionConfig.cs`:
     - Lưu trữ Search ROI (Cyan) và Template ROI (Gold), đường dẫn tệp ảnh template `TemplateImagePath` (`*_oc.png`).
     - Quản lý thông số Spec: `NominalCount`, `Tolerance`, `ToleranceMinus`, `TolerancePlus`.
     - Tùy chỉnh thuật toán (`ShapeMatching` / `NormalizedCorrelation`), ngưỡng điểm tin cậy `MinScore`, dải góc xoay `MinAngleDeg` / `MaxAngleDeg` / `AngleStepDeg` và ngưỡng chồng lấn `MaxOverlapRatio`.
   - `ObjectCountingEngine.cs`:
     - Mô hình kim tự tháp đa tầng Coarse-to-Fine (Pyramid Down 2x) giúp quét nhanh toàn dải 360° với bước góc linh hoạt.
     - Thuật toán khử trùng lặp Rotated Non-Maximum Suppression (Rotated NMS) dùng `Cv2.RotatedRectangleIntersection` kết hợp khoảng cách tâm Euclidean, đảm bảo không bỏ sót và không đếm lặp vật thể ở các góc lân cận.
     - Quy chuẩn đánh giá PASS/NG: `NominalCount - ToleranceMinus <= FoundCount <= NominalCount + TolerancePlus`.
   - `InspectionService.Pipeline.cs` & `ConditionEvaluator.VariableRegistry.cs`:
     - Tích hợp chạy song song `objectCountingTasks` trong luồng kiểm tra chính của Vision Engine.
     - Đăng ký biến `Count`, `Nominal`, `Pass`, `Status`, `Found`, `Time` cho các alias node `OC`, `ObjectCounting` vào hệ thống đánh giá điều kiện logic.
   - `JobService.cs`: Tự động nạp, lưu trữ và đóng gói ảnh template `*_oc.png` kèm theo Job.
   - `ToolEditorViewModel.ToolObjectCounting.cs`, `ToolGraphNodeViewModel.cs`, `ToolEditorView.xaml`:
     - Thêm Node `ObjectCounting` (InPort Image, OutPort Count), màu cam `#E67E22`, icon Fluent.
     - Panel cấu hình chuyên dụng: Dạy mẫu (Teaching Template), xem trước ảnh mẫu, nhóm thiết lập Spec & Dung sai, nhóm tùy chỉnh góc quét và thuật toán, bảng hiển thị kết quả đếm & trạng thái PASS/NG.
     - Vẽ overlay thời gian thực các khung bao xoay (`RotatedRect`), tâm điểm và nhãn góc/điểm của từng vật thể đếm được.
     - Tích hợp xuất kết quả đếm chi tiết vào bảng dữ liệu `SpecResults` trên cả giao diện Tool Editor và màn hình chạy Inspection tự động.
3. **Phân tích & Khắc phục lỗi đếm 69 vật thể trên vỉ thuốc 10 viên**:
   - **Nguyên nhân**: Thuật toán `ShapeMatching` cũ (Chamfer) bị nhiễu vân lưới nhôm dập quả trám; thiếu trích xuất Local Maxima trên ma trận điểm số; NMS cũ chỉ kiểm tra khoảng cách tâm nhỏ và IoU hộp xoay; 4 góc của ảnh template crop chữ nhật ($115 \times 104$) chứa 34.3% diện tích là vân nền nhôm, khi xoay bắt nhầm vào các viền dập nhiệt.
   - **Giải pháp**: 
     - Thêm `ApplyCircularWindow` (Apodization/Cosine window) làm mờ 4 góc nền nhôm về giá trị trung bình, chỉ giữ lại thân viên thuốc tròn.
     - Cải tiến Rotated NMS: khoảng cách tâm tối thiểu $0.70 \times \min(W, H)$ và tỷ lệ che phủ diện tích vật thể `interArea / minArea > MaxOverlap`.
     - Thêm lọc Local Maxima 8 lân cận cho ShapeMatching; Đặt mặc định sang `NormalizedCorrelation` với `MinScore = 0.65`, `MaxOverlap = 0.30`.
4. **Kiểm thử tự động**:
   - `ObjectCountingTests.cs`: 5/5 test PASSED 100% (bao gồm Test 5 kiểm tra trực tiếp trên ảnh vỉ thuốc của người dùng: Đếm chính xác tuyệt đối **10/10 viên thuốc**, 0 vật thể giả, điểm số 75.5% - 88.6%, Pass = True).

## 4. Hoàn thành Task 423: Xây Dựng Ứng Dụng Android Native "VisionCameraServer" Cho Galaxy S24 Ultra & Tích Hợp Camera2 Pro Mode
- Tích hợp trọn bộ ứng dụng Android Native `AndroidCameraServer` điều khiển Camera2 Pro Mode (200MP, EV, Manual Focus diopters, ISO, Flash LED Torch) qua cáp USB ADB `127.0.0.1:8080`.
- Kiểm thử `AndroidCameraProModeTests.cs`: 6/6 test PASSED 100%.

## 5. Các sự kiện & thay đổi gần đây
- Task 424: Hoàn thành Tool ObjectCounting, sửa dứt điểm lỗi đếm 69 vật thể trên vỉ thuốc -> đếm chuẩn xác 10/10 viên thuốc; kiểm thử 5/5 PASSED 100%.
- Task 423: Xây dựng Android App VisionCameraServer (Camera2 Pro Mode, 200MP, EV, Focus, Flash Torch) & cài đặt thành công lên S24 Ultra; kiểm thử 6/6 PASSED 100%.
- Task 422: Tích hợp Camera Android Galaxy S24 Ultra Pro Mode (USB/Wi-Fi/LAN, 200MP, EV, Focus, Flash Torch); kiểm thử 6/6 PASSED 100%.
- Task 421: Khắc phục triệt để độ trễ 1 giây/nấc khi xả hàng đợi 16 nấc Run Continuous; kiểm thử 6/6 PASSED (89ms/3 nấc).
- Task 420: Tích hợp PLC Trigger vào BoundedChannel 16 nấc & thêm nút Trigger tay; kiểm thử 5/5 PASSED 100%.
- Task 419: Tự động co giãn Full Width 4 cột trên Tab Tool Editor khi Ẩn/Hiện cột; kiểm thử 7/7 PASSED 100%.
- Task 418: Tự động nạp trạng thái hiện tại của Tag HMI khi Load/Run; kiểm thử 5/5 PASSED 100%.
- Task 417: Khắc phục triệt để tranh chấp kết nối PLC & cô lập hoàn toàn HMI Tool Editor; kiểm thử 4/4 PASSED 100%.
- Task 416: Khắc phục triệt để lỗi Thread Ownership làm ngắt kết nối PLC Ethernet; kiểm thử 7/7 PASSED 100%.
