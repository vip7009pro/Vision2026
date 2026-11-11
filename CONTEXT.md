# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode Native Server, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Dự án Android Native**: `AndroidCameraServer` (Gradle 8.11, CompileSDK 34, Android 14) đã biên dịch thành công APK và cài đặt trực tiếp lên Samsung Galaxy S24 Ultra (`SM-S928B`).
- **Kiểm thử tự động**: PASSED 100% (8/8 test PreprocessRoiMaskingTest, 6/6 test MvpShapeMatch3 & Int8, 8/8 test MvpShapeMatch2 Preprocess, 10/10 test ToolEditorColumnResize & Continuous 50/50, 8/8 test ToolEditorContinuousRenderAndSpeed, 9/9 test NewJob & BlobSpec Calibrated/Uncalibrated/Pixel vs Mm Mode, 6/6 test BlobCountingMode, 4/4 test Snapshot Frame Ownership, 5/5 test HikStreaming & Color Fidelity, 7/7 test PlcHandshakeNumericMode OK/NG Int16 & Both, 7/7 test PlcHandshakePersistence, 5/5 test PlcSignalType, 6/6 test CodeDetection Result, 5/5 test ObjectCounting, 6/6 test AndroidCameraProMode, 6/6 test PlcContinuousQueue 16 Slots).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, Kết quả số `D100` OK=2, NG=3, Queue In `M200`).

## 3. Hoàn thành Task 434: Bổ Sung Tùy Chọn Màu Vùng Che (Đen / Trắng) Cho Tool Preprocess (ROI Masking)
1. **Yêu cầu & Mục đích**:
   - Tool Preprocess hỗ trợ che mặt nạ ROI Masking (Include/Exclude, Rectangle/Circle/Polygon, Bám Origin).
   - Trước đây vùng bị che luôn có màu đen (`Scalar.All(0)`). Khi kiểm tra sản phẩm nền sáng/trắng (nhãn cuộn Roll Label), viền che đen tạo tương phản giả dẫn đến thuật toán Blob/Edge/Caliper nhận nhầm khuyết tật.
   - Bổ sung tùy chọn cho người dùng chọn màu vùng che: **Đen (Black)** hoặc **Trắng (White)**.
2. **Các thành phần đã triển khai**:
   - **Mô hình dữ liệu (`VisionInspectionApp.Models/Class1.cs`)**:
     * Thêm enum `PreprocessMaskColor { Black = 0, White = 1 }`.
     * Bổ sung thuộc tính `MaskColor` trong `PreprocessSettings` (mặc định `Black = 0` bảo toàn 100% backward compatibility).
   - **Động cơ xử lý ảnh (`ImagePreprocessor.cs`)**:
     * Khởi tạo ma trận `blended` với `Scalar.All(maskBgScalar)` (`0.0` nếu Black, `255.0` nếu White).
     * Hoạt động chính xác trên cả ảnh đơn sắc Grayscale (CV_8UC1) và ảnh màu BGR (CV_8UC3, full RGB 255).
   - **Giao diện người dùng & ViewModel (`ToolEditorViewModel.cs`, `TeachViewModel.cs`, `ToolEditorView.xaml`)**:
     * Thêm property `AvailablePreprocessMaskColors` và `PreprocessMaskColor` tự động kích hoạt `SchedulePreprocessPreviewUpdate()`, `RefreshPreviews()`, `RequestAutoSave()`.
     * Đồng bộ thuộc tính khi chọn node trong `RaiseToolPropertyPanelsChanged()`.
     * Cung cấp ComboBox trực quan "Màu vùng che" với icon `⬛ Đen (Black)` và `⬜ Trắng (White)` ngay trong Card "🎯 Region of Interest (ROI Mask)" trên Tool Editor.
   - **Kiểm thử tự động (`PreprocessRoiMaskingTest.cs`, `Program.cs`)**:
     * Thêm 3 test kiểm thử: `TestMaskColorDefaultIsBlack`, `TestMaskColorWhiteIncludeAndExclude_Gray`, `TestMaskColorWhite_ColorBgr`.
     * **8/8 test PASSED 100%**. Toàn bộ solution biên dịch đạt **0 Errors** cả Debug và Release.

## 4. Các sự kiện & thay đổi gần đây
- Task 434: Bổ sung tùy chọn màu vùng che Đen / Trắng cho Tool Preprocess ROI Masking; kiểm thử 8/8 test PASSED 100%.
- Task 433: Hiện thực hóa thành công 2 thuật toán tăng tốc `MvpShapeMatch3` (3.54ms - nhanh hơn 11.7x) và `MvpShapeMatchInt8` (2.74ms - nhanh hơn 15.0x, đánh bại mốc 7ms của MVP); kiểm thử 6/6 test PASSED 100%.
- Task 432: Tự động ẩn Cột 1 & 2, hiển thị Cột 3 & 4 (50/50) khi Chạy liên tục; khôi phục khi dừng; kiểm thử 10/10 PASSED 100%.
- Task 431: Nâng cấp Preview Tool Editor khi bỏ Render Canvas (hiển thị ảnh tĩnh "OK" to khi đạt, chỉ render canvas khi gặp NG, tự động autofit preview khi quay lại live) & Bổ sung widget tốc độ tức thời (pcs/s) cạnh Tổng (ms), COUNT, YIELD; kiểm thử 8/8 PASSED 100%.
- Task 430: Chuyển đổi thủ công chế độ spec BlobDetection (theo pixel hoặc theo mm) thay vì tự động theo hiệu chuẩn, mặc định theo pixel; kiểm thử 9/9 PASSED 100%.
- Task 429: Khắc phục triệt để lỗi ObjectDisposedException ("Cannot access a disposed object OpenCvSharp.Mat") khi bấm chụp ảnh trong Tool Editor & Manual Inspection; kiểm thử 4/4 PASSED 100%.
- Task 428: Tối ưu triệt để stream live & hiệu chuẩn màu sắc Camera Hikrobot 25MP MV-CH250-90TC qua card 10GbE MV-GT1002 đạt 41 FPS & chuẩn màu MVS 100%; kiểm thử 5/5 PASSED 100%.
- Task 427: Tối ưu triệt để BlobDetection bắt khuyết tật theo WxL khi đã hiệu chuẩn (bỏ qua blob nhỏ nằm trong dung sai, chỉ bắt blob vượt quá WxL), tự động RunFlow cập nhật preview tức thì, loại bỏ slider Thr; kiểm thử 9/9 PASSED 100%.
- Task 426: Hoàn thành chế độ xuất kết quả OK/NG dạng số nguyên (Int16 Word: D100, OK=2, NG=3) song hành cùng dạng Bit Bool trong cửa sổ Bắt tay 24/7; kiểm thử 7/7 PASSED 100%.
