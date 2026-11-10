# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode Native Server, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Dự án Android Native**: `AndroidCameraServer` (Gradle 8.11, CompileSDK 34, Android 14) đã biên dịch thành công APK và cài đặt trực tiếp lên Samsung Galaxy S24 Ultra (`SM-S928B`).
- **Kiểm thử tự động**: PASSED 100% (8/8 test ToolEditorContinuousRenderAndSpeed, 9/9 test NewJob & BlobSpec Calibrated/Uncalibrated/Pixel vs Mm Mode, 6/6 test BlobCountingMode, 4/4 test Snapshot Frame Ownership, 5/5 test HikStreaming & Color Fidelity, 7/7 test PlcHandshakeNumericMode OK/NG Int16 & Both, 7/7 test PlcHandshakePersistence, 5/5 test PlcSignalType, 6/6 test CodeDetection Result, 5/5 test ObjectCounting, 6/6 test AndroidCameraProMode, 6/6 test PlcContinuousQueue 16 Slots, 7/7 test ToolEditor Column Resize).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, Kết quả số `D100` OK=2, NG=3, Queue In `M200`).

## 3. Hoàn thành Task 431: Nâng Cấp Preview Tool Editor Khi Bỏ Render Canvas & Bổ Sung Widget Tốc Độ Tức Thời (pcs/s)
1. **Bối cảnh & Yêu cầu người dùng**:
   - Khi chạy liên tục trong Tool Editor, nếu uncheck "Render Canvas":
     - Hiển thị 1 ảnh tĩnh "OK" thật to lên preview khi sản phẩm đạt, không tốn tài nguyên render canvas (tối đa hóa FPS).
     - Chỉ khi kiểm tra đến con hàng NG thì mới hiển thị canvas (ảnh chụp lỗi + overlay bounding box khuyết tật).
     - Sau đó nếu con tiếp theo OK thì lại tiếp tục không render canvas và hiển thị lại ảnh tĩnh "OK".
   - Khi click queue / thanh 20 nấc để xem ảnh NG lịch sử, sau đó bấm "✕ Quay lại Live" thì tự động AutoFit View 1 lần cho preview.
   - Thêm một ô widget hiển thị tốc độ tức thời (pcs/s) ngay phía dưới (bên cạnh các widget TỔNG (ms), COUNT, YIELD 20), tự động về 0 khi máy dừng thay vì bị dãn thời gian như tốc độ trung bình toàn phiên ở góc trên.
2. **Kiến trúc & Các thành phần đã triển khai**:
   - **Tạo ảnh tĩnh "OK" chất lượng cao (`GetOrCreateStaticOkImage`)**:
     - Tạo ảnh tĩnh 1280x720 nền tối công nghiệp `#0F172A`, vòng tròn xanh lá `#10B981` cùng chữ "OK" to (150pt) ở giữa, nhãn "CHẤT LƯỢNG ĐẠT (PASS)" và thông báo chế độ tối ưu.
     - Đóng băng (`Freeze()`) và cache dùng chung (`_cachedStaticOkImage`), đảm bảo 0% tải CPU/GPU lặp lại và an toàn đa luồng.
   - **Luồng hiển thị có điều kiện khi chạy liên tục (`SetStaticOkPreview` & `canRender`)**:
     - Trong `RefreshPreviewsCore`, `RefreshFinalPreview`, `RefreshSelectedPreview`, `SchedulePreprocessPreviewUpdate`:
       - Khi `!EnableCanvasRendering`: Nếu đang chạy liên tục (`IsRunningContinuous`), kiểm tra kết quả `_lastRun.Pass`. Nếu OK -> gọi `SetStaticOkPreview()` hiển thị ảnh tĩnh OK và bỏ qua render canvas. Nếu NG -> bật cờ `canRender` cho phép render canvas đầy đủ chi tiết lỗi. Sau đó khi gặp con hàng OK tiếp theo -> tự động quay về ảnh tĩnh "OK".
     - Trong `RunContinuousInspectionLoopAsync`: Ưu tiên đưa frame NG lên luồng UI ngay lập tức không bị throttle/drop.
     - Khi bấm bắt đầu Chạy liên tục: Nếu `!EnableCanvasRendering`, hiển thị ngay ảnh tĩnh "OK" thay vì để trống canvas.
   - **AutoFit View khi quay lại Live (`RequestAutoFitImagePreview`)**:
     - Bổ sung sự kiện `RequestAutoFitImagePreview` trên `ToolEditorViewModel`.
     - Trong `ToolEditorView.xaml.cs`: Đăng ký sự kiện và gọi `PreviewImageViewer?.ResetView()`.
     - Trong `ExitRecentPartView()`: Kích hoạt sự kiện để khi bấm "✕ Quay lại Live" hoặc "✕ Live", khung nhìn preview tự động căn chỉnh vừa vặn 100%.
   - **Widget Tốc độ tức thời (`InstantaneousSpeedText`)**:
     - Áp dụng thuật toán Rolling Window đo dấu thời gian của tối đa 8 sản phẩm gần nhất (`_instantaneousStopwatchTicks`, `RecordPieceCompleted()`).
     - Tự động kiểm tra độ trễ so với con hàng cuối: Nếu quá 1.5 giây không có con hàng mới -> máy đã dừng -> tốc độ tức thời tự động hạ về `0.0 pcs/s`.
     - Trên `ToolEditorView.xaml`: Đổi `UniformGrid Columns="3"` thành `Columns="4"`, bổ sung chip hiển thị "TỨC THỜI (pcs/s)" màu xanh dương `#0284C7` đặt cạnh `TỔNG (ms)`, `COUNT`, `YIELD 20`.
3. **Kiểm thử tự động**:
   - `ToolEditorContinuousRenderAndSpeedTests.cs`: **8/8 test PASSED 100%** (Ảnh tĩnh OK, AutoFit event, OK hiện ảnh tĩnh, NG hiện canvas, OK sau NG, Rolling Window speed, tự động về 0 khi dừng, reset khi Stop).
   - `NewJobAndBlobSpecTests.cs`: **9/9 test PASSED 100%**.
   - `BlobCountingModeTests.cs`: **6/6 test PASSED 100%**.
   - `ToolEditorColumnResizeTests.cs`: **7/7 test PASSED 100%**.
   - Biên dịch: Toàn bộ solution `VisionInspectionApp.slnx` đạt **0 Errors**.

## 4. Các sự kiện & thay đổi gần đây
- Task 431: Nâng cấp Preview Tool Editor khi bỏ Render Canvas (hiển thị ảnh tĩnh "OK" to khi đạt, chỉ render canvas khi gặp NG, tự động autofit preview khi quay lại live) & Bổ sung widget tốc độ tức thời (pcs/s) cạnh Tổng (ms), COUNT, YIELD; kiểm thử 8/8 PASSED 100%.
- Task 430: Chuyển đổi thủ công chế độ spec BlobDetection (theo pixel hoặc theo mm) thay vì tự động theo hiệu chuẩn, mặc định theo pixel; kiểm thử 9/9 PASSED 100%.
- Task 429: Khắc phục triệt để lỗi ObjectDisposedException ("Cannot access a disposed object OpenCvSharp.Mat") khi bấm chụp ảnh trong Tool Editor & Manual Inspection; kiểm thử 4/4 PASSED 100%.
- Task 428: Tối ưu triệt để stream live & hiệu chuẩn màu sắc Camera Hikrobot 25MP MV-CH250-90TC qua card 10GbE MV-GT1002 đạt 41 FPS & chuẩn màu MVS 100%; kiểm thử 5/5 PASSED 100%.
- Task 427: Tối ưu triệt để BlobDetection bắt khuyết tật theo WxL khi đã hiệu chuẩn (bỏ qua blob nhỏ nằm trong dung sai, chỉ bắt blob vượt quá WxL), tự động RunFlow cập nhật preview tức thì, loại bỏ slider Thr; kiểm thử 9/9 PASSED 100%.
- Task 426: Hoàn thành chế độ xuất kết quả OK/NG dạng số nguyên (Int16 Word: D100, OK=2, NG=3) song hành cùng dạng Bit Bool trong cửa sổ Bắt tay 24/7; kiểm thử 7/7 PASSED 100%.
- Task 425: Bổ sung chi tiết lỗi NG cho tool CodeDetection trong SpecResults, NgReasonsText và thẻ CodeDetection; kiểm thử 6/6 PASSED 100%.
- Task 424: Hoàn thành Tool ObjectCounting, sửa dứt điểm lỗi đếm 69 vật thể trên vỉ thuốc -> đếm chuẩn xác 10/10 viên thuốc; kiểm thử 5/5 PASSED 100%.
