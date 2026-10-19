# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ kiểm thử phím tắt Ctrl+R & Ctrl+F4 với ROI cố định, ROI Edit & Auto Scaling, 8/8 test ContourCompare, 12/12 test Tool Calib Factor, 15/15 test OQC Scanner Live View, 8/8 test MvpShapeMatch2 Preprocess, 6/6 test Seeding Config).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`

## 3. Hoàn thành Task 408: Rà Soát, Bỏ Control Vô Dụng và Tái Cấu Trúc Giao Diện Cửa Sổ Train Template (Tool Origin)
1. **Rà soát & Loại bỏ 100% control vô dụng (dummy) tồn đọng**:
   - Loại bỏ checkbox `Eraser`, ô nhập `Size`, nút `Undo` và `Redo` (tồn đọng từ mockup ảnh cũ, không có cơ chế tẩy xóa).
   - Loại bỏ `Detection Roi: Full graph / Part graph` (tránh xung đột và tránh ghi đè làm mất `SearchRoi` của Origin).
   - Loại bỏ hoàn toàn bảng `Shape Operations List Table` rác (No. 0, Rect, Add/Deduct, ParamSet tĩnh không logic).
   - Loại bỏ nút `Save` lơ lửng tại tiêu đề panel giữa (trùng lặp với nút OK tại footer).
2. **Tái cấu trúc giao diện 2 cột hiện đại, khoa học & gọn gàng**:
   - Bố cục 2 cột chuyên nghiệp (`2.5* | 1.2*`) giúp vùng hiển thị ảnh kiểm tra chiếm không gian tối đa, rộng rãi.
   - Thanh công cụ trên ảnh: 🎯 **Fit View**, 🔄 **Nạp Lại Ảnh (Refresh Image)**, ⌖ **Căn Giữa ROI (Center ROI)**, ⚡ **Huấn Luyện (Train)** kèm badge hiển thị kích thước/vị trí ROI trực quan (`[X, Y | W, H | Góc]`).
   - Cột phải chia thành 4 Card nhóm tham số rõ ràng:
     - **Card 1: Mẫu Đã Huấn Luyện**: Khung xem trước template sắc nét kèm kích thước $W \times H$ (px) và số lượng đường viền/điểm đặc trưng.
     - **Card 2: Tham Số Trích Xuất Biên**: Auto Threshold checkbox, Edge Threshold Slider+TextBox, Length Threshold Slider+TextBox, Max Pyramid Layers.
     - **Card 3: Tọa Độ Tâm Gốc (Origin Center)**: Khóa tâm vào giữa ROI (Lock), Origin X và Origin Y (chỉnh sửa khi mở khóa).
     - **Card 4: Tiêu Chuẩn Khớp Mẫu (Matching Settings)**: Ngưỡng Min Score, Dải góc xoay (Min Angle, Max Angle, Angle Step).
   - Chân trang (Footer): Gợi ý thao tác nhanh (Ctrl + Kéo chuột để vẽ ROI) và 2 nút chuẩn `💾 Áp Dụng & Lưu (OK)` và `Hủy Bỏ (Cancel)`.
3. **Kiểm thử tự động**:
   - Thêm `Test_OriginTrainViewModel_LifecycleAndCommands` trong [OriginTemplateJobTest.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/OriginTemplateJobTest.cs).
   - Kiểm thử toàn bộ luồng huấn luyện mẫu, tính toán thống kê, căn giữa ROI, đổi ngưỡng và lưu cấu hình: PASSED 100%.

## 4. Các sự kiện & thay đổi gần đây
- Task 408: Rà soát loại bỏ 100% control vô dụng trong cửa sổ Train Template Tool Origin (Eraser, Size, Undo, Redo, Detection Roi radio, Shape Operations Table, Save button lơ lửng); thiết kế lại layout 2 cột hiện đại chuẩn công nghiệp; kiểm thử tự động PASSED 100%.
- Task 407: Bổ sung phím tắt Ctrl + R chuyển đổi hướng quét Vertical/Horizontal cho EdgePairDetect & Caliper (giữ nguyên ROI Width/Height, chỉ xoay các strip trong ROI); thêm phím tắt Ctrl + F4 đóng Job đang mở trên toàn ứng dụng; kiểm thử tự động PASSED 100%.
- Task 406: Tự động co giãn StripLength và StripWidth của node EdgePairDetection theo ROI; người dùng không cần nhập số thủ công; hỗ trợ đổi Orientation & StripCount; sửa lỗi overlay Horizontal; kiểm thử tự động PASSED.
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
