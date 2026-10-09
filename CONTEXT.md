# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ 8/8 test suite [MvpShapeMatch2PreprocessTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/MvpShapeMatch2PreprocessTests.cs) và 13/13 test suite OQC Scanner Live View).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`

## 3. Hoàn thành Task 400: Khắc Phục Triệt Để Lỗi Khớp Điểm Origin MvpShapeMatch2 Với Ảnh Qua Tiền Xử Lý
1. **Yêu cầu & Vấn đề**:
   - Khi dùng ảnh tĩnh trong Tool Editor / Inspection Flow, đã train template rồi cho run bắt origin chính ảnh đó:
   - Với ảnh không có xử lý gì (chỉ xám, hoặc nối trực tiếp ImageSource -> Origin): điểm số đạt 1.0 bình thường.
   - Với ảnh qua tiền xử lý (Node Preprocess hoặc thiết lập tiền xử lý trong Settings): các thuật toán khác (`TemplateMatch`, `MvpShapeMatch`) ra 1.0, nhưng riêng `MvpShapeMatch2` điểm số chỉ đạt ~0.5 loanh quanh đó.
2. **Nguyên nhân gốc rễ**:
   - *Bất đối xứng tiền xử lý*: Trong `OriginMatcher.MatchWithRotation()`, Search ROI được tiền xử lý thành `roiGray`, nhưng `templateGray` truyền vào `MvpShapeMatch2Engine.Match` bị bỏ qua không gọi `PreprocessTemplateForMatch(templateGray, preprocess)` như các thuật toán khác (`MatchByPyramid`, `TemplateMatch`), khiến ROI và Template lệch không gian biểu diễn ảnh.
   - *Cache Key thiếu Checksum dữ liệu*: `cacheKey` của `_templateModelCache` trong `MvpShapeMatch2Engine.cs` chỉ chứa tên tool và kích thước mà không có hash nội dung pixel, khiến khi thay đổi tiền xử lý hoặc đổi ảnh bị dính lại mô hình vector của ảnh cũ.
   - *Trượt biên 1px và Cực trị địa phương*:
     - Vòng lặp dải góc Coarse Search bỏ qua mốc góc 0.0° khi `minAngle` và `angleStep` không chia hết cho nhau.
     - `RefineSearchFast` ở các tầng pyramid trung gian thiếu Max Pooling 3x3, khiến cho ảnh đã qua tiền xử lý có đường biên sắc mảnh (độ rộng 1 pixel của Threshold/Canny) bị trượt gradient khi subsample, làm điểm số tụt xuống 0.5.
     - Bán kính tìm kiếm `searchRadius: 2` và `angleRange: 0.8` ở Level 0 quá hẹp, không thể bù đắp sai số vị trí từ tầng thô của pyramid.
     - Level 0 chỉ refine cho 1 candidate duy nhất ở tầng thô nên dễ rơi vào cực trị địa phương.
3. **Giải pháp đã thực hiện**:
   - *Đồng bộ tiền xử lý*: Trong [OriginMatcher.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.VisionEngine/OriginMatcher.cs), bổ sung hàm `PrepareTemplateForMatchBorrowed(templateGray, preprocess)` để tiền xử lý template đối xứng tuyệt đối với ROI trước khi gọi `MvpShapeMatch2Engine.Match`.
   - *Tự động làm mới cache*: Trong [MvpShapeMatch2Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.VisionEngine/MvpShapeMatch2Engine.cs), bổ sung hàm băm siêu tốc FNV-1a `ComputeMatChecksum(templInput)` vào `cacheKey` của `_templateModelCache`.
   - *Căn chỉnh góc 0.0°*: Căn chỉnh dải góc Coarse Search luôn chứa mốc góc 0.0° chính xác.
   - *Max Pooling 3x3*: Tích hợp Max Pooling 3x3 vào `RefineSearchFast` cho các tầng pyramid trung gian.
   - *Mở rộng bán kính Level 0 & Refine Đa Candidate*: Mở rộng `lvl0SearchRadius = Math.Max(5, (1 << maxPyramidLevel) * 2)` và `lvl0AngleRange = Math.Max(1.5, coarseAngleStep)`; refine đồng thời cho toàn bộ top 4 candidates ở Level 0 để tìm global maximum chuẩn xác.
   - *Kiểm thử tự động*: Xây dựng test suite [MvpShapeMatch2PreprocessTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/MvpShapeMatch2PreprocessTests.cs) kiểm thử tự động 8 kịch bản (Grayscale, Binary Threshold, Binary Inverted, Otsu, Canny Edge, Gaussian Blur, Runtime PreprocessSettings, Dynamic Cache Invalidation). Toàn bộ 8/8 tests PASSED 100% với Score = 1.0000, Pos sai số < 0.8px, Angle sai số <= 0.1°.
4. **Kết quả xác thực**:
   - 8/8 tests MvpShapeMatch2 PASSED 100%.
   - Toàn bộ Solution biên dịch 0 Errors cả Debug lẫn Release.

## 4. Các sự kiện & thay đổi gần đây
- Task 400: Khắc phục triệt để lỗi khớp điểm Origin MvpShapeMatch2 với ảnh qua tiền xử lý (đồng bộ tiền xử lý template, FNV-1a cache key checksum, Max Pooling 3x3 pyramid, căn góc 0.0° và refine đa candidate Level 0).
- Task 399: Sửa lỗi không lưu lịch sử kiểm tra khi kiểm tra nhiều lần trong 1 phiên Job (mỗi lần kiểm tra đều lưu bản ghi lịch sử và log DB).
- Task 398: Tự động đóng cửa sổ Quản lý Job & Huấn luyện khi bấm Huấn Luyện Từ Xa sau khi nạp xong Job và ảnh mẫu lên Tool Editor.
- Task 397: Khắc phục triệt để lỗi mất dữ liệu cấu hình OQC Scanner & Tra cứu Database khi build app và chạy test; thiết lập Isolated Sandbox và Safe Guard bảo vệ CSDL xưởng CMS_VINA; thêm nút khôi phục xưởng trên UI.
- Task 396: Khắc phục triệt để lỗi mất link Server OTA khi build app & chạy test suite; thiết lập Isolated Sandbox cho unit test và Safe Guard bảo vệ cấu hình sản xuất.
- Task 395: Tự động Focus & Select All Scanned Text trên Tab OQC Scanner (ghi đè tự động chuỗi mã khi scan, không cần xóa thủ công).
- Task 394: Bổ sung phím tắt ESC đóng Job và xóa ô nhập mã TextBox trên Tab OQC Scanner (1 chạm không pop-up, đồng bộ nút UI "🔒 Đóng Job (ESC)").
- Task 393: Bổ sung Chế độ "Cú đấm thép" trên Tab OQC Scanner (mặc định Checked, bảo toàn chuỗi mã scan trên TextBox, vô hiệu hóa phím Enter từ scanner khi đã mở Job, luân phiên Space/Ctrl+F8 kiểm tra mẫu/Live View).
- Task 392: Chuẩn hóa chu trình dịch bit hàng đợi FX5U theo xung kết quả Vision Done (`M103`) thay vì cảm biến `X2`.
- Task 391: Sửa lỗi không truyền bit NG M105 sang GXWorks & Kích hoạt Handshake khi test ảnh trên Tool Editor.
- Task 390: Giải quyết triệt để lỗi Bắt Cạnh PLC (`LDP X0`) không kích hoạt `SET Y0` từ nút Momentary HMI & Nâng cấp Minimum Hold Duration (100ms).
- Task 389: Bắt tay Bất đồng bộ Non-blocking & Hàng đợi 20 phôi BSFLP M200..M219 dừng đúng Điểm Ra Chỉ Định ngoài buồng.
- Task 388: Sửa lỗi mất cấu hình PLC khi build lại app & Thêm cơ chế Simulate PLC Auto-Ack và Fast Direct Read cho kiểm thử Handshake.
- Task 387: Hiển thị Trực Quan Trạng Thái PLC Handshake trên UI Tool Editor & Phân tích Trigger Flow.
- Task 386: Hướng dẫn Thông số Cài Đặt Handshake & Tags trên App Vision WPF.
- Task 385: Chuẩn hóa lệnh dịch bit `BSFRP` cho PLC FX5U (thay thế lệnh lỗi thời `SFT` của FX3U).
- Task 384: Biên soạn bộ mã Ladder Diagram (LD) đầy đủ 9 Networks cho PLC FX5U.
- Task 383: Sửa lỗi cú pháp Structured Text (ST) GX Works 3 `0x110E1A02`.
- Task 382: Chuẩn hóa 100% định dạng CSV GX Works 3 (1.080J) nạp Global Labels và Device Comments.
- Task 381: PLC FX5U Handshake, cơ chế dừng NG ngoài buồng In-Flight Tracking & xem lại ảnh 20 nấc lịch sử.
- Task 380: OQC Scanner 1 nút Space / Ctrl+F8 luân phiên Kiểm tra & Live View, Crosshair căn tâm mặc định bật cho Job Camera Settings.
- Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN (`Parallel.For`, cap 4-8 luồng).
- Task 378: Tối ưu hiệu năng/UX Tool Editor (cache preview Final, chống rò rỉ RAM Mat, thuật toán Coordinate Descent cho Auto Tune).
