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

## 3. Hoàn thành Task 407: Phím Tắt Ctrl + R (Giữ Nguyên ROI Width/Height, Chỉ Đảo Chiều Strip) và Ctrl + F4 (Đóng Job Đang Mở)
1. **Phím tắt `Ctrl + R` (Toggle Tool Orientation) — Giữ nguyên ROI Width/Height**:
   - Áp dụng cho cả hai node `EdgePairDetect` và `Caliper` khi đang được chọn trên Canvas hoặc Properties Panel.
   - **Giữ nguyên 100% kích thước & vị trí khung ROI**: Loại bỏ hoàn toàn việc hoán đổi `Width` và `Height` của `SearchRoi`. Khung chữ nhật bao quanh chi tiết giữ nguyên cố định như người dùng đã vẽ.
   - **Chỉ đảo chiều các strip bên trong ROI**:
     - Khi `Vertical`: Các strip quét theo chiều dọc $\implies \text{StripLength} = \text{SearchRoi.Height}$, $\text{StripWidth} = \text{SearchRoi.Width} / \text{StripCount}$.
     - Khi `Horizontal`: Các strip quét theo chiều ngang $\implies \text{StripLength} = \text{SearchRoi.Width}$, $\text{StripWidth} = \text{SearchRoi.Height} / \text{StripCount}$.
     - Cơ chế đồng bộ: `SyncEpdStripsFromRoi` trong [ToolEditorViewModel.ToolEdgePair.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolEdgePair.cs) và `SyncCaliperStripsFromRoi` trong [ToolEditorViewModel.ToolCaliper.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolCaliper.cs).
     - Đồng bộ cả khi kéo dãn ROI trên canvas ảnh trong `OnRoiEditedCore` và `ApplyRoiForLabel` ([ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs)).
   - Cập nhật tức thì overlay xem trước (mũi tên hướng quét Cyan và các vạch strip Lime/MediumPurple xoay 90 độ mượt mà) và đồng bộ giá trị trên Properties Panel.
2. **Phím tắt `Ctrl + F4` (Đóng Job Đang Mở)**:
   - Bổ sung `CloseJobCommand` vào [MainWindowViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/MainWindowViewModel.cs) và [ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs).
   - Đăng ký `KeyBinding Key="F4" Modifiers="Control"` trên Menu và Window InputBindings của [MainWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/MainWindow.xaml) và [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml).
   - Bắt phím `OnPreviewKeyDown` cấp Window và UserControl đảm bảo đóng Job an toàn, dọn sạch nodes, edges, overlay và reset đường dẫn file.
3. **Kiểm thử tự động**:
   - Cập nhật `Test_11_EdgePairDetect_RoiAutoScaling` và `Test_12_ToggleToolOrientation_And_CloseJobCommand` trong [PerformanceOptimizationTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/PerformanceOptimizationTests.cs).
   - Khẳng định `SearchRoi.Width` và `SearchRoi.Height` giữ nguyên 100%, `StripLength` và `StripWidth` tự động xoay theo hướng quét mới cho cả EdgePairDetect và Caliper, xác nhận `CloseJobCommand` dọn sạch Job. Toàn bộ PASSED 100%.

## 4. Các sự kiện & thay đổi gần đây
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
