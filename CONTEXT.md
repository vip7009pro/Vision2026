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

## 3. Hoàn thành Task 432: Tự Động Ẩn Cột 1 & 2, Hiển Thị Cột 3 & 4 (50/50) Khi Chạy Liên Tục
1. **Bối cảnh & Yêu cầu người dùng**:
   - Khi bấm "Chạy liên tục" (`IsRunningContinuous = true`): Tự động ẩn Cột 1 (Toolbox) và Cột 2 (Canvas), chỉ hiển thị Cột 3 (Preview) và Cột 4 (Debug / Kết quả chi tiết) chia đều theo tỷ lệ 50/50.
   - Khi tắt "Chạy liên tục" (`IsRunningContinuous = false`): Tự động hiển thị lại Cột 1 và Cột 2, khôi phục nguyên vẹn tỷ lệ phân chia kích thước ban đầu của cả 4 cột.
2. **Kiến trúc & Các thành phần đã triển khai**:
   - **Tầng ViewModel (`ToolEditorViewModel.cs` & `ToolEditorViewModel.Engine.cs`)**:
     - `PreviewColumnWidth` & `DebugColumnWidth`: Tự động trả về `1.0*` cho cả Cột 3 và Cột 4 khi `IsRunningContinuous && !IsToolboxColumnVisible && !IsCanvasColumnVisible && IsDebugColumnVisible`; khi tắt trả về tỷ lệ mặc định ban đầu (`4.5*` và `3.2*`).
     - `SetContinuousColumnVisibility(bool isContinuous)`: Khi bật Chạy liên tục -> tự ẩn Cột 1 & 2, bật Cột 4; khi tắt Chạy liên tục -> tự hiện lại Cột 1 & 2. Đảm bảo an toàn UI qua `Dispatcher.BeginInvoke`.
     - Setter `IsRunningFolderFlow`: Gọi `SetContinuousColumnVisibility` và phát sự kiện `PropertyChanged` cho `PreviewColumnWidth`, `DebugColumnWidth`.
   - **Tầng View (`ToolEditorView.xaml.cs`)**:
     - Trong `OnViewModelPropertyChanged`: Lắng nghe thêm `nameof(ToolEditorViewModel.IsRunningContinuous)`.
     - Trong `ApplyColumnLayout`: Xử lý nhánh 50/50 cho Chạy liên tục (thu Cột 0 & 2 về 0, gán `ColPreview.Width = 1.0*` và `ColDebug.Width = 1.0*`). Khi dừng, khôi phục 4 cột với `_savedToolboxStar`, `_savedCanvasStar`, `_savedPreviewStar`, `_savedDebugStar`.
     - Trong `NormalizeColumnWidthsToStar`: Chỉ lưu kích thước 4 cột gốc khi `!vm.IsRunningContinuous` để không ghi đè tỷ lệ 50/50 tạm thời vào layout gốc.
3. **Kiểm thử tự động**:
   - `ToolEditorColumnResizeTests.cs`: **10/10 test PASSED 100%** (bao gồm 3 test mới cho chế độ 50/50, ẩn cột 1&2, hiện lại khi dừng và phát `PropertyChanged`).
   - `ToolEditorContinuousRenderAndSpeedTests.cs`: **8/8 test PASSED 100%**.
   - Toàn bộ solution biên dịch đạt **0 Errors**.

## 4. Các sự kiện & thay đổi gần đây
- Task 432: Tự động ẩn Cột 1 (Toolbox) và Cột 2 (Canvas), chỉ hiển thị Cột 3 (Preview) và Cột 4 (Debug) chia đều 50/50 khi Chạy liên tục; tự động hiển thị lại Cột 1 & 2 và khôi phục tỷ lệ gốc khi dừng; kiểm thử 10/10 PASSED 100%.
- Task 431: Nâng cấp Preview Tool Editor khi bỏ Render Canvas (hiển thị ảnh tĩnh "OK" to khi đạt, chỉ render canvas khi gặp NG, tự động autofit preview khi quay lại live) & Bổ sung widget tốc độ tức thời (pcs/s) cạnh Tổng (ms), COUNT, YIELD; kiểm thử 8/8 PASSED 100%.
- Task 430: Chuyển đổi thủ công chế độ spec BlobDetection (theo pixel hoặc theo mm) thay vì tự động theo hiệu chuẩn, mặc định theo pixel; kiểm thử 9/9 PASSED 100%.
- Task 429: Khắc phục triệt để lỗi ObjectDisposedException ("Cannot access a disposed object OpenCvSharp.Mat") khi bấm chụp ảnh trong Tool Editor & Manual Inspection; kiểm thử 4/4 PASSED 100%.
- Task 428: Tối ưu triệt để stream live & hiệu chuẩn màu sắc Camera Hikrobot 25MP MV-CH250-90TC qua card 10GbE MV-GT1002 đạt 41 FPS & chuẩn màu MVS 100%; kiểm thử 5/5 PASSED 100%.
- Task 427: Tối ưu triệt để BlobDetection bắt khuyết tật theo WxL khi đã hiệu chuẩn (bỏ qua blob nhỏ nằm trong dung sai, chỉ bắt blob vượt quá WxL), tự động RunFlow cập nhật preview tức thì, loại bỏ slider Thr; kiểm thử 9/9 PASSED 100%.
- Task 426: Hoàn thành chế độ xuất kết quả OK/NG dạng số nguyên (Int16 Word: D100, OK=2, NG=3) song hành cùng dạng Bit Bool trong cửa sổ Bắt tay 24/7; kiểm thử 7/7 PASSED 100%.
