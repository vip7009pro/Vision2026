# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode Native Server, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Dự án Android Native**: `AndroidCameraServer` (Gradle 8.11, CompileSDK 34, Android 14) đã biên dịch thành công APK và cài đặt trực tiếp lên Samsung Galaxy S24 Ultra (`SM-S928B`).
- **Kiểm thử tự động**: PASSED 100% (9/9 test NewJob & BlobSpec Calibrated/Uncalibrated Mode, 6/6 test BlobCountingMode, 7/7 test PlcHandshakeNumericMode OK/NG Int16 & Both, 7/7 test PlcHandshakePersistence, 5/5 test PlcSignalType, 6/6 test CodeDetection Result, 5/5 test ObjectCounting, 6/6 test AndroidCameraProMode, 6/6 test PlcContinuousQueue 16 Slots, 7/7 test ToolEditor Column Resize).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, Kết quả số `D100` OK=2, NG=3, Queue In `M200`).

## 3. Hoàn thành Task 427: Tối Ưu Triệt Để BlobDetection Bắt Khuyết Tật Theo WxL Khi Đã Hiệu Chuẩn & Tự Động Chạy Lại Preview Tức Thì
1. **Bối cảnh & Vấn đề thực tế**:
   - Người dùng phản ánh trên Job có hiệu chuẩn (như `job 223.job` với tỉ lệ 25.5 px/mm): khi nhập `Spec Max (W x L)` rất lớn, tool vẫn bắt hàng loạt các blob nhỏ li ti (1x2 px, 2x2 px, diện tích 2-5 pixel do nhiễu hạt/răng cưa).
   - **Nguyên nhân cốt lõi phát hiện**:
     1. *Logic ngược*: `Spec Max (W x L)` trong kiểm tra khuyết tật ngoại quan (Defect / Contamination) là **ngưỡng khuyết tật tối đa cho phép**. Bất kỳ vết bẩn nào nhỏ hơn hoặc bằng WxL là nằm trong dung sai cho phép (Acceptable) -> **PHẢI BỎ QUA, KHÔNG ĐƯỢC BẮT**. Chỉ những vết bẩn **VƯỢT QUÁ kích thước WxL** mới là khuyết tật cần bắt. Logic cũ lại bắt các blob `<= WxL` và bỏ qua blob `> WxL`, dẫn đến khi nhập WxL rất lớn thì mọi hạt bụi nhỏ li ti đều bị bắt.
     2. *Preview không cập nhật*: Trong `ToolEditorViewModel.ToolBlob.cs`, các setter chỉ gọi `RefreshPreviews()` (vẽ lại từ kết quả cũ) mà không gọi `RunFlow()`, khiến màn hình preview giữ nguyên kết quả cũ không đổi.
     3. *Nhận diện hiệu chuẩn*: Kiểm tra hiệu chuẩn đa tầng qua `ChessboardCalibrationService.GetEffectiveCalibration(config)`, `ChessboardCalibration` và `PixelsPerMm` để đảm bảo 100% nhận diện đúng khi Job đã hiệu chuẩn.
2. **Kiến trúc & Các thành phần đã triển khai**:
   - **`InspectionService.Pipeline.cs`**:
     - Trong `DetectBlobsInCrop`: Khi `isCalibrated == true`, tính kích thước thực tế `blobW = width / scale`, `blobH = height / scale`. Nếu blob nằm trong kích thước dung sai cho phép (`blobW <= maxBlobWidth && blobH <= maxBlobLength`), tự động bỏ qua (`continue`). Chỉ bắt những blob **vượt quá kích thước `maxBlobWidth x maxBlobLength`**.
     - Nhận diện `calibPpm` đa tầng từ `GetEffectiveCalibration`, `ChessboardCalibration` và `PixelsPerMm`.
     - Ở chế độ Calibrated, mọi blob bắt được đều là blob vượt quá WxL, được đánh dấu viền đỏ (NG) chuẩn xác.
   - **`ToolEditorViewModel.ToolBlob.cs`**:
     - Thêm lệnh `RunFlow()` vào tất cả các setter: `Blob_MaxBlobWidth`, `Blob_MaxBlobLength`, `Blob_MaxAllowedBlobs`, `Blob_MinBlobDistance`, `Blob_MinBlobArea`, `Blob_MaxBlobArea`, `Blob_Polarity`, `Blob_CountingMode`. Khi người dùng nhập bất kỳ giá trị nào, Flow tự động chạy lại ngay lập tức và cập nhật kết quả lên màn hình Preview tức thì.
     - Cập nhật `Blob_IsCalibrated` kiểm tra đa tầng từ `ChessboardCalibrationService.GetEffectiveCalibration(_config)`.
   - **`ChessboardCalibrationService.cs`**:
     - Trong `EnsureCalibration`: Tự động đồng bộ `config.PixelsPerMm = config.ChessboardCalibration.PixelsPerMm` nếu Job có calib riêng nhưng `PixelsPerMm <= 1.0`.
3. **Kiểm thử tự động**:
   - `NewJobAndBlobSpecTests.cs`: 9/9 test PASSED 100% (Test 7 kiểm tra chuẩn xác: WxL 5x5 mm chỉ bắt đúng 1 blob vượt quá kích thước và bỏ qua 2 blob nhỏ; WxL 15x15 mm bỏ qua toàn bộ không bắt blob nào; WxL 1x1 mm bắt cả 3 blob; Test 8 uncalibrated MinA/MaxA; Test 9 ViewModel).
   - `BlobCountingModeTests.cs`: 6/6 test PASSED 100%.

## 4. Các sự kiện & thay đổi gần đây
- Task 427: Tối ưu triệt để BlobDetection bắt khuyết tật theo WxL khi đã hiệu chuẩn (bỏ qua blob nhỏ nằm trong dung sai, chỉ bắt blob vượt quá WxL), tự động RunFlow cập nhật preview tức thì, loại bỏ slider Thr; kiểm thử 9/9 PASSED 100%.
- Task 426: Hoàn thành chế độ xuất kết quả OK/NG dạng số nguyên (Int16 Word: D100, OK=2, NG=3) song hành cùng dạng Bit Bool trong cửa sổ Bắt tay 24/7; kiểm thử 7/7 PASSED 100%.
- Task 425: Bổ sung chi tiết lỗi NG cho tool CodeDetection trong SpecResults, NgReasonsText và thẻ CodeDetection; kiểm thử 6/6 PASSED 100%.
- Task 424: Hoàn thành Tool ObjectCounting, sửa dứt điểm lỗi đếm 69 vật thể trên vỉ thuốc -> đếm chuẩn xác 10/10 viên thuốc; kiểm thử 5/5 PASSED 100%.
- Task 423: Xây dựng Android App VisionCameraServer (Camera2 Pro Mode, 200MP, EV, Focus, Flash Torch) & cài đặt thành công lên S24 Ultra; kiểm thử 6/6 PASSED 100%.
- Task 422: Tích hợp Camera Android Galaxy S24 Ultra Pro Mode (USB/Wi-Fi/LAN, 200MP, EV, Focus, Flash Torch); kiểm thử 6/6 PASSED 100%.
- Task 421: Khắc phục triệt để độ trễ 1 giây/nấc khi xả hàng đợi 16 nấc Run Continuous; kiểm thử 6/6 PASSED (89ms/3 nấc).
- Task 420: Tích hợp PLC Trigger vào BoundedChannel 16 nấc & thêm nút Trigger tay; kiểm thử 5/5 PASSED 100%.
- Task 419: Tự động co giãn Full Width 4 cột trên Tab Tool Editor khi Ẩn/Hiện cột; kiểm thử 7/7 PASSED 100%.
- Task 418: Tự động nạp trạng thái hiện tại của Tag HMI khi Load/Run; kiểm thử 5/5 PASSED 100%.
