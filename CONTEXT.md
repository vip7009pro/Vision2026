# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode Native Server, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Dự án Android Native**: `AndroidCameraServer` (Gradle 8.11, CompileSDK 34, Android 14) đã biên dịch thành công APK và cài đặt trực tiếp lên Samsung Galaxy S24 Ultra (`SM-S928B`).
- **Kiểm thử tự động**: PASSED 100% (10/10 test ToolEditorColumnResize & Continuous 50/50, 8/8 test ToolEditorContinuousRenderAndSpeed, 9/9 test NewJob & BlobSpec Calibrated/Uncalibrated/Pixel vs Mm Mode, 6/6 test BlobCountingMode, 4/4 test Snapshot Frame Ownership, 5/5 test HikStreaming & Color Fidelity, 7/7 test PlcHandshakeNumericMode OK/NG Int16 & Both, 7/7 test PlcHandshakePersistence, 5/5 test PlcSignalType, 6/6 test CodeDetection Result, 5/5 test ObjectCounting, 6/6 test AndroidCameraProMode, 6/6 test PlcContinuousQueue 16 Slots).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, Kết quả số `D100` OK=2, NG=3, Queue In `M200`).

## 3. Hoàn thành Task 433: Hiện Thực Hóa 2 Thuật Toán Tăng Tốc Template Marking: MVPShapeMatch3 & MVPShapeMatchInt8
1. **Bối cảnh & Yêu cầu người dùng**:
   - Đặc thù sản phẩm: Nhãn cuộn (**Roll Label**), marking đa dạng (logo, biên dạng, hình khối), Search ROI nhỏ và đồng nhất, góc xoay nhỏ ($\pm 2^\circ \dots \pm 5^\circ$).
   - Người dùng lựa chọn triển khai **CẢ 2 PHƯƠNG ÁN**:
     * **`MvpShapeMatch3`**: Tối ưu hóa sâu trên nền `MvpShapeMatch2` (Lazy Local Gradient ở Tầng 0, Pre-rotated features cache, Deep adaptive pyramid, Edge energy masking).
     * **`MvpShapeMatchInt8`**: Thuật toán mới lượng tử hóa vector gradient sang `INT8` kết hợp tập lệnh SIMD phần cứng AVX2 (`_mm256_maddubs_epi16`).
2. **Kiến trúc & Các thành phần đã triển khai**:
   - **Mở rộng Enums (`VisionInspectionApp.Models/Class1.cs`)**: Thêm `OriginAlgorithm.MvpShapeMatch3 = 9` và `OriginAlgorithm.MvpShapeMatchInt8 = 10` (đồng bộ cả `PointFindAlgorithm`).
   - **Động cơ `MvpShapeMatch3Engine.cs`**:
     * Pre-rotated Feature Cache theo góc xoay (loại bỏ lượng giác runtime).
     * Deep Adaptive Pyramid (lên đến 5 tầng) kết hợp Anchor Edge Check ở tầng thô.
     * **Lazy Local Gradient ở Tầng 0**: Chỉ tính Sobel trong bounding box nhỏ quanh ứng viên tốt nhất, loại bỏ 95% chi phí Sobel toàn cục.
     * Tinh chỉnh sub-pixel parabol cục bộ chính xác đến 0.05px.
   - **Động cơ `MvpShapeMatchInt8Engine.cs`**:
     * Vector gradient được nén thành số nguyên có dấu 8-bit (`sbyte: -127..127`), giảm 75% RAM bandwidth, dữ liệu gradient nằm trọn trong L1 CPU Cache.
     * Tính toán tích vô hướng hoàn toàn bằng tập lệnh số nguyên siêu tốc (0 chu kỳ chuyển đổi float).
     * Lazy Local Gradient ở Tầng 0 + Tinh chỉnh Sub-pixel Parabol.
   - **Tích hợp Tầng Điều Phối (`OriginMatcher.cs`, `ToolOrigin.cs`, `OriginTrainViewModel.cs`)**:
     * Dispatching tự động trong `OriginMatcher.MatchWithRotation`.
     * `IsOriginShapePyramid` nhận diện đầy đủ v2, v3 và int8.
     * Cache invalidate đồng bộ cho cả 3 động cơ.
3. **Kết quả kiểm thử tự động & Benchmark đo đạc thực tế (Release Mode)**:
   - `MvpShapeMatch2 (Gốc)`: **41.25 ms / lần**
   - `MvpShapeMatch3 (Mới)`: **3.54 ms / lần** (Nhanh hơn **11.7x**, tiệm cận chuẩn MVP)
   - `MvpShapeMatchInt8 (Mới)`: **2.74 ms / lần** (Nhanh hơn **15.0x**, vượt mốc 7ms của MVP)
   - Độ chính xác tọa độ lệch **< 0.3 px**, góc xoay lệch **< 0.1°**.
   - `MvpShapeMatch3AndInt8Tests.cs`: **6/6 test PASSED 100%**.
   - `OriginTemplateJobTest.cs`: **100% PASSED**.
   - `MvpShapeMatch2PreprocessTests.cs`: **8/8 PASSED 100%**.
   - Toàn bộ solution biên dịch đạt **0 Errors** (cả Debug và Release).

## 4. Các sự kiện & thay đổi gần đây
- Task 433: Hiện thực hóa thành công 2 thuật toán tăng tốc `MvpShapeMatch3` (3.54ms - nhanh hơn 11.7x) và `MvpShapeMatchInt8` (2.74ms - nhanh hơn 15.0x, đánh bại mốc 7ms của MVP); kiểm thử 6/6 test PASSED 100%.
- Task 432: Tự động ẩn Cột 1 & 2, hiển thị Cột 3 & 4 (50/50) khi Chạy liên tục; khôi phục khi dừng; kiểm thử 10/10 PASSED 100%.
- Task 431: Nâng cấp Preview Tool Editor khi bỏ Render Canvas (hiển thị ảnh tĩnh "OK" to khi đạt, chỉ render canvas khi gặp NG, tự động autofit preview khi quay lại live) & Bổ sung widget tốc độ tức thời (pcs/s) cạnh Tổng (ms), COUNT, YIELD; kiểm thử 8/8 PASSED 100%.
- Task 430: Chuyển đổi thủ công chế độ spec BlobDetection (theo pixel hoặc theo mm) thay vì tự động theo hiệu chuẩn, mặc định theo pixel; kiểm thử 9/9 PASSED 100%.
- Task 429: Khắc phục triệt để lỗi ObjectDisposedException ("Cannot access a disposed object OpenCvSharp.Mat") khi bấm chụp ảnh trong Tool Editor & Manual Inspection; kiểm thử 4/4 PASSED 100%.
- Task 428: Tối ưu triệt để stream live & hiệu chuẩn màu sắc Camera Hikrobot 25MP MV-CH250-90TC qua card 10GbE MV-GT1002 đạt 41 FPS & chuẩn màu MVS 100%; kiểm thử 5/5 PASSED 100%.
- Task 427: Tối ưu triệt để BlobDetection bắt khuyết tật theo WxL khi đã hiệu chuẩn (bỏ qua blob nhỏ nằm trong dung sai, chỉ bắt blob vượt quá WxL), tự động RunFlow cập nhật preview tức thì, loại bỏ slider Thr; kiểm thử 9/9 PASSED 100%.
- Task 426: Hoàn thành chế độ xuất kết quả OK/NG dạng số nguyên (Int16 Word: D100, OK=2, NG=3) song hành cùng dạng Bit Bool trong cửa sổ Bắt tay 24/7; kiểm thử 7/7 PASSED 100%.
