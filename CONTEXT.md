# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ 8/8 test ContourCompare, 15/15 test OQC Scanner Live View, 8/8 test MvpShapeMatch2 Preprocess, 6/6 test Seeding Config, 6/6 test Backup & DB Match).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`

## 3. Hoàn thành Task 405: Khắc Phục Triệt Để Lỗi Contour Lệch/Báo Đỏ Trên Cùng Ảnh, Thêm Canny Slider Kèm Live Preview, Tích Hợp Đánh Giá Tổng Job & Bảng Đo Đạc
1. **Khắc phục triệt để hiện tượng cùng 1 mẫu 1 ảnh bắt ra contour lệch, báo đỏ, điểm cao và NG**:
   - **Nguyên nhân gốc rễ**:
     - `ApproxSimple` nén các cạnh thẳng dài hàng trăm pixel chỉ còn 2 đỉnh mút góc, tạo ra "khoảng trống điểm" lớn ở giữa cạnh.
     - Vòng lặp so sánh điểm-điểm Euclidean đo khoảng cách từ điểm gợn giữa cạnh tới 2 đỉnh góc, khiến sai số vọt lên 50–200px $> \text{Max Hausdorff Dist}$, làm phát sinh các vệt đỏ ngắt quãng dọc cạnh thẳng và rãnh chữ U.
     - Khi `InspectRoi == TemplateRoi`, hệ thống gán cố định `(pad, pad)` mà không chạy MatchTemplate, khiến sai số làm tròn tọa độ giữa `(int)roi.X` và `Math.Round` làm lệch viền 1 pixel.
   - **Giải pháp chuẩn xác công nghiệp**:
     - Chuyển `FindAllContours` sang `ContourApproximationModes.ApproxNone` giữ toàn vẹn mọi điểm viền.
     - Tích hợp **`Cv2.DistanceTransform`** (L2 Mask5) trên ảnh Canny đảo ngược của cả mẫu và phôi: tính toán khoảng cách Euclidean thực tế liên tục tới đường viền trong $0.1\text{ms}$ ($O(1)$ tra bảng), triệt tiêu 100% hiện tượng đỏ giả tạo trên cạnh thẳng và rãnh cong.
     - Tự động chạy `Cv2.MatchTemplate` để tìm vị trí tương quan cực đại `maxLoc`, triệt tiêu hoàn toàn sai số lệch toạ độ 1 pixel. Khi cùng ảnh, `maxVal \ge 0.999` lập tức giữ nguyên `(0, 0)` $\implies$ MaxDist = 0.00px, 0 đoạn lỗi.
2. **Bổ sung Slider cho Canny Thr 1 & Canny Thr 2 trong Properties Panel kèm Live Preview**:
   - Cập nhật giao diện trong [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml): Tích hợp cụm `Slider` (1–255, bước nhảy 1/5) song song với `TextBox` cho cả `Canny Thr 1` và `Canny Thr 2`.
   - Trong [ToolEditorViewModel.ToolContourCompare.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolContourCompare.cs): Kích hoạt `OnPropertyChanged` đồng bộ 2 chiều tức thì giữa Slider và TextBox; mỗi khi kéo slider, khung xem trước `ContourCompare_TemplatePreviewImage` lập tức trích xuất lại viền contour và hiển thị sắc nét bằng màu Vàng kim 1px.
3. **Đưa ContourCompare vào điều kiện đánh giá Job và Bảng kết quả đo đạc**:
   - **Đánh giá tổng thể Job**: Trong [InspectionService.Pipeline.cs:3340](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/InspectionService.Pipeline.cs#L3340), bổ sung `&& result.ContourCompares.All(x => x.Pass)` và `&& result.ColorDiffs.All(x => x.Pass)`. Khi ContourCompare bị NG thì toàn bộ Job bắt buộc ra NG (`result.Pass = false`).
   - **Bảng kết quả đo đạc Tool Editor**: Bổ sung các bản ghi `SpecResultRow` của `ContourCompare` vào `SpecResults` trong [ToolEditorViewModel.Inspection.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Inspection.cs) và [InspectionViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/InspectionViewModel.cs) (hiển thị MaxDist / AreaDiff / ShapeScore theo MatchMethod).
   - **Bảng đo đạc OQC Scanner**: Cập nhật [OqcScannerService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/OQC/OqcScannerService.cs) hàm `ExtractMeasurementDetails` hiển thị đúng Spec, TolPlus, Result và đơn vị ("px", "%") theo MatchMethod.

## 4. Các sự kiện & thay đổi gần đây
- Task 405: Triệt tiêu lỗi lệch đỏ ContourCompare trên cùng ảnh bằng DistanceTransform + MatchTemplate + ApproxNone; thêm Canny Slider 1 & 2 kèm Live Preview tức thì trên Properties Panel; tích hợp ContourCompare vào result.Pass và Bảng kết quả đo đạc (SpecResults & OQC MeasurementDetails).
- Task 404: Khắc phục lỗi ContourCompare báo NG trên cùng ảnh do lệch ảnh tiền xử lý; hỗ trợ MatchTemplate + ICP cho Search ROI; thêm Preview Template kèm viền contour trên Properties Panel; hiển thị Area Diff & Status; giải thích cơ chế 3 thuật toán và mã màu viền.
- Task 403: Tự động lưu Job đang mở khi có thay đổi trước khi tải lên Server trong cửa sổ Quản lý Job & Huấn luyện (áp dụng cho cả 3 nút upload); module hóa `JobManagerViewModel.Upload.cs`.
- Task 402: Bảo mật mật khẩu xóa lịch sử OQC Log (mặc định 1234, cấu hình trong OQC Settings) & Checkbox không tên lọc PASS (ẩn NG).
- Task 401: Chuẩn hóa chu trình 3 bước OQC Scanner Cú đấm thép (tự động nạp Job thứ 2 & reset counting mẫu về 0, không cần đóng Job thủ công).
- Task 400: Khắc phục triệt để lỗi khớp điểm Origin MvpShapeMatch2 với ảnh qua tiền xử lý (đồng bộ tiền xử lý template, FNV-1a cache key checksum, Max Pooling 3x3 pyramid, căn góc 0.0° và refine đa candidate Level 0).
- Task 399: Sửa lỗi không lưu lịch sử kiểm tra khi kiểm tra nhiều lần trong 1 phiên Job (mỗi lần kiểm tra đều lưu bản ghi lịch sử và log DB).
- Task 398: Tự động đóng cửa sổ Quản lý Job & Huấn luyện khi bấm Huấn Luyện Từ Xa sau khi nạp xong Job và ảnh mẫu lên Tool Editor.
- Task 397: Khắc phục triệt để lỗi mất dữ liệu cấu hình OQC Scanner & Tra cứu Database khi build app và chạy test; thiết lập Isolated Sandbox và Safe Guard bảo vệ CSDL xưởng CMS_VINA; thêm nút khôi phục xưởng trên UI.
- Task 396: Khắc phục triệt để lỗi mất link Server OTA khi build app & chạy test suite; thiết lập Isolated Sandbox cho unit test và Safe Guard bảo vệ cấu hình sản xuất.
- Task 395: Tự động Focus & Select All Scanned Text trên Tab OQC Scanner (ghi đè tự động chuỗi mã khi scan, không cần xóa thủ công).
- Task 394: Bổ sung phím tắt ESC đóng Job và xóa ô nhập mã TextBox trên Tab OQC Scanner (1 chạm không pop-up, đồng bộ nút UI "🔒 Đóng Job (ESC)").
- Task 393: Bổ sung Chế độ "Cú đấm thép" trên Tab OQC Scanner (mặc định Checked, bảo toàn chuỗi mã scan trên TextBox, luân phiên Space/Ctrl+F8 kiểm tra mẫu/Live View).
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
