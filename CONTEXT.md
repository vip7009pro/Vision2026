# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode Native Server, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Dự án Android Native**: `AndroidCameraServer` (Gradle 8.11, CompileSDK 34, Android 14) đã biên dịch thành công APK và cài đặt trực tiếp lên Samsung Galaxy S24 Ultra (`SM-S928B`).
- **Kiểm thử tự động**: PASSED 100% (9/9 test NewJob & BlobSpec Calibrated/Uncalibrated/Pixel vs Mm Mode, 6/6 test BlobCountingMode, 4/4 test Snapshot Frame Ownership, 5/5 test HikStreaming & Color Fidelity, 7/7 test PlcHandshakeNumericMode OK/NG Int16 & Both, 7/7 test PlcHandshakePersistence, 5/5 test PlcSignalType, 6/6 test CodeDetection Result, 5/5 test ObjectCounting, 6/6 test AndroidCameraProMode, 6/6 test PlcContinuousQueue 16 Slots, 7/7 test ToolEditor Column Resize).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, Kết quả số `D100` OK=2, NG=3, Queue In `M200`).

## 3. Hoàn thành Task 430: Chuyển Đổi Thủ Công Chế Độ Spec BlobDetection (Theo Pixel Hoặc Theo mm) Thay Vì Tự Động Theo Hiệu Chuẩn
1. **Bối cảnh & Yêu cầu người dùng**:
   - Người dùng yêu cầu: Không cho chuyển chế độ spec tự động theo hiệu chuẩn nữa. Thay vào đó, tạo tùy chọn trên giao diện để người dùng có thể tự do chọn qua lại các loại spec:
     - **Theo pixel (MinA, MaxA)**: Bắt blob theo diện tích pixel `[minArea, maxArea]`.
     - **Theo mm (Kích thước W x L)**: Bắt blob theo kích thước thực tế W x L (mm) bỏ qua dung sai kích thước nhỏ và bắt các khuyết tật vượt ngưỡng.
     - **Mặc định**: Luôn chọn theo pixel (`BlobSpecMode.Pixel = 0`).
2. **Kiến trúc & Các thành phần đã triển khai**:
   - **`Class1.cs` (Models)**:
     - Bổ sung enum `BlobSpecMode`: `Pixel = 0`, `Millimeter = 1`.
     - Thêm thuộc tính `SpecMode` vào `BlobDetectionDefinition` với giá trị mặc định `BlobSpecMode.Pixel` (đảm bảo tương thích 100% với các file Job JSON cũ).
   - **`InspectionResultModels.cs` & `InspectionService.Pipeline.cs`**:
     - `BlobDetectionResult` lưu kèm `BlobSpecMode SpecMode = BlobSpecMode.Pixel`.
     - Trong `DetectBlobsInCrop`: Phân nhánh lọc blob chuẩn xác dựa trên `specMode`:
       - `specMode == BlobSpecMode.Millimeter`: Lọc theo kích thước W x L (mm), bỏ qua các blob nằm trong dung sai kích thước cho phép, chỉ bắt blob vượt quá spec WxL.
       - `specMode == BlobSpecMode.Pixel`: Lọc theo diện tích pixel `[minArea, maxArea]` (kể cả khi Job đã có hiệu chuẩn, lựa chọn của người dùng luôn được ưu tiên tuyệt đối).
     - Trong đánh giá kích thước (`checkSize`): Chỉ áp dụng cho chế độ Millimeter. Khi ở Pixel mode, kích thước WxL không can thiệp vào `passSize`.
   - **`ToolEditorViewModel.ToolBlob.cs` & `ToolEditorView.xaml`**:
     - Thêm danh sách lựa chọn `AvailableBlobSpecModeNames`: `"Theo pixel (MinA, MaxA)"` và `"Theo mm (Kích thước W x L)"`.
     - Thêm các thuộc tính Binding: `Blob_SpecMode`, `Blob_SpecModeText`, `Blob_IsPixelSpecMode`, `Blob_IsMmSpecMode`, `Blob_SpecUnitText`.
     - Thêm dropdown ComboBox "Chế độ Spec" trực quan trên bảng Properties của Tool Editor. Khi người dùng thay đổi chế độ, tự động gọi `RunFlow()` và cập nhật ảnh preview tức thời.
     - Ẩn/hiện độc lập theo `Blob_IsPixelSpecMode` và `Blob_IsMmSpecMode`. Khi ở chế độ mm, nếu Job chưa hiệu chuẩn sẽ hiện badge cảnh báo cam nhắc nhở người dùng (1 px = 1 mm).
     - Đồng bộ bảng kết quả Spec (`SpecResults`) trong `ToolEditorViewModel.Inspection.cs` và `InspectionViewModel.cs`: Chỉ thêm dòng kiểm tra `BlobSize` khi node ở chế độ Millimeter.
3. **Kiểm thử tự động**:
   - `NewJobAndBlobSpecTests.cs`: **9/9 test PASSED 100%** (bao gồm Test 7 kiểm tra chế độ mm bắt theo WxL, Test 8 kiểm tra chế độ Pixel mặc định lọc theo MinA/MaxA ngay cả khi Job có calib, Test 9 kiểm tra chuyển đổi qua lại giữa Pixel và Millimeter trên ViewModel).
   - `BlobCountingModeTests.cs`: **6/6 test PASSED 100%**.
   - Biên dịch: Toàn bộ solution `VisionInspectionApp.slnx` đạt **0 Errors**.

## 4. Các sự kiện & thay đổi gần đây
- Task 430: Chuyển đổi thủ công chế độ spec BlobDetection (theo pixel hoặc theo mm) thay vì tự động theo hiệu chuẩn, mặc định theo pixel; kiểm thử 9/9 PASSED 100%.
- Task 429: Khắc phục triệt để lỗi ObjectDisposedException ("Cannot access a disposed object OpenCvSharp.Mat") khi bấm chụp ảnh trong Tool Editor & Manual Inspection; kiểm thử 4/4 PASSED 100%.
- Task 428: Tối ưu triệt để stream live & hiệu chuẩn màu sắc Camera Hikrobot 25MP MV-CH250-90TC qua card 10GbE MV-GT1002 đạt 41 FPS & chuẩn màu MVS 100%; kiểm thử 5/5 PASSED 100%.
- Task 427: Tối ưu triệt để BlobDetection bắt khuyết tật theo WxL khi đã hiệu chuẩn (bỏ qua blob nhỏ nằm trong dung sai, chỉ bắt blob vượt quá WxL), tự động RunFlow cập nhật preview tức thì, loại bỏ slider Thr; kiểm thử 9/9 PASSED 100%.
- Task 426: Hoàn thành chế độ xuất kết quả OK/NG dạng số nguyên (Int16 Word: D100, OK=2, NG=3) song hành cùng dạng Bit Bool trong cửa sổ Bắt tay 24/7; kiểm thử 7/7 PASSED 100%.
- Task 425: Bổ sung chi tiết lỗi NG cho tool CodeDetection trong SpecResults, NgReasonsText và thẻ CodeDetection; kiểm thử 6/6 PASSED 100%.
- Task 424: Hoàn thành Tool ObjectCounting, sửa dứt điểm lỗi đếm 69 vật thể trên vỉ thuốc -> đếm chuẩn xác 10/10 viên thuốc; kiểm thử 5/5 PASSED 100%.
- Task 423: Xây dựng Android App VisionCameraServer (Camera2 Pro Mode, 200MP, EV, Focus, Flash Torch) & cài đặt thành công lên S24 Ultra; kiểm thử 6/6 PASSED 100%.
