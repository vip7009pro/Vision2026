# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (11/11 test SurfaceCompare & Caliper, 6/6 test PLC Handshake & Auto Register Tag, 5/5 test PLC Signal Types, 4/4 test HMI Designer Persistence & DINT 32-bit, regression test tệp Job `job_7A11034A_GH68-58216B`, Ctrl+R & Ctrl+F4 ROI, 8/8 test ContourCompare, 12/12 test Tool Calib Factor, 15/15 test OQC Scanner Live View, 8/8 test MvpShapeMatch2).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, Queue In `M200`).

## 3. Hoàn thành Task 414: Khắc Phục Lỗi Lệch Tịnh Tiến Ảnh Template Khi Sửa Job Cũ Trên Tool SurfaceCompare & Nút "Set Template"
1. **Bối cảnh & Bản chất nguyên nhân**:
   - *Nguyên nhân 1 (Lệch tọa độ Teach vs Found)*: Trong job đã có Origin, khi runtime chạy trên ảnh hiện tại `snap`, vị trí Origin tìm thấy `originFound` lệch so với vị trí dạy ban đầu `originTeach` ($Y_f > Y_t$). Khi user move/resize Template ROI (`SCT`) trên canvas, tọa độ trên canvas là tọa độ Found; hàm `UnTransformRoi` đưa về Teach coordinate ($Y_t$) và lưu vào `sc.TemplateRoi`. Trước đây hàm lưu template `TrySaveSurfaceCompareTemplateImage` lại dùng `ExtractRoiPatch(processedMat, targetRoi)` cắt trực tiếp $Y_t$ trên ảnh `processedMat` (vốn đang ở Found coordinate $Y_f$). Do đó ảnh template bị tịnh tiến lên trên một đoạn $dy = Y_f - Y_t$. Trong khi đó, tại runtime `RunSurfaceCompare` lại dùng `ExtractStraightRoi` chuyển đúng $Y_t \rightarrow Y_f$ để cắt ô `DebugCurrent`. Sự lệch pha $dy$ pixel này khiến kết quả so sánh diff luôn bị NG.
   - *Nguyên nhân 2 (Gắn chặt Move/Resize với Ghi đè Template)*: Trước đây mỗi lần move/resize `SCT`, code gọi ngay hàm lưu template khiến template bị ghi đè liên tục, dễ nhầm lẫn và giảm hiệu năng.
2. **Các điểm sửa đổi cốt lõi**:
   - `ToolEditorViewModel.cs`: Cập nhật `TrySaveSurfaceCompareTemplateImage` và `TrySaveContourCompareTemplateImage` dùng `Geometry2D.ExtractStraightRoi(processedMat, targetRoi, originTeach, originFound, originAngleDeg, out _)`. Khi đó, tọa độ Teach được chuyển chính xác sang vị trí hiện tại trên `snap`, trích xuất đúng 100% vùng ảnh hiển thị trong ROI.
   - `ToolEditorViewModel.Engine.cs`: Xóa bỏ việc gọi `TrySaveSurfaceCompareTemplateImage` trong `ApplyRoiForLabel` khi move/resize `SC` và `SCT`. Move/resize chỉ cập nhật hình chữ nhật ROI (`sc.InspectRoi`, `sc.TemplateRoi`), tuyệt đối không ghi đè template.
   - `ToolEditorViewModel.ToolSurface.cs` & `ToolEditorView.xaml`: Bổ sung nút **"Set Template"** và lệnh `SurfaceCompare_SetTemplateCommand` trong Properties Panel, cho phép người dùng chủ động chốt ghi template khi đã ưng ý với vùng ROI.
   - `InspectionService.Pipeline.cs`: Bổ sung `ClearSurfaceCompareTemplateCache` xóa cache bộ nhớ khi template mới được ghi nhận.
3. **Kiểm thử tự động**:
   - Thêm Test 11 `Test_SurfaceCompare_ExistingJob_TemplateShiftWithOriginOffset_Fixed` vào `SurfaceCompareAndCaliperRoiTests.cs`: giả lập Origin dịch chuyển $\Delta Y = 50px$, xác minh trích xuất template và runtime khớp 100%, kết quả Pass. Toàn bộ 11/11 test PASSED.

## 4. Hoàn thành Task 413: Khắc Phục Sự Cố Không Nhận Trigger PLC M10 Trên Tool ImageSource
1. **Bối cảnh & Bản chất nguyên nhân**:
   - *Nguyên nhân PLC Scan Cycle*: Tiếp điểm `LDP X2`/`LDP M2` chỉ ON trong đúng 1 chu kỳ quét CPU (~1ms) $\rightarrow$ `M10` tắt về 0 sau 1ms. Máy tính polling qua Ethernet chu kỳ `50ms` nên 99.9% lần đọc rơi vào lúc M10 = 0.
   - *Nguyên nhân Vision App*: Ô nhập `ImageSource_PlcTriggerTagName` không tự động đăng ký địa chỉ vào `_plcManagerService.Tags`, khiến bộ quét `PlcPollingEngine` không đọc `M10` từ `PLC_2`. ComboBox trên UI bị Two-Way binding kép.
2. **Các điểm sửa đổi cốt lõi**:
   - `IPlcManagerService.cs` & `PlcManagerService.cs`: Bổ sung `EnsureTagRegistered(plcId, addressOrName, ...)` tự động đăng ký và nạp ngay vào chu kỳ Polling Engine.
   - `ToolEditorViewModel.ToolPreprocess.cs` & `ToolEditorViewModel.Engine.cs`: Gọi `EnsureTagRegistered` khi cấu hình tag và khi bấm "Run Continuous".
   - `ToolEditorView.xaml`: Bỏ `SelectedItem` và `PreviewMouseDown` trên ComboBox chọn `ImageSource_PlcTriggerTagName`.
   - Hướng dẫn Ladder: Thêm Timer duy trì `M10` 100ms..200ms hoặc chu trình bắt tay `SET M10` $\rightarrow$ `Vision Done M103` $\rightarrow$ `RST M10`.
3. **Kiểm thử tự động**:
   - Test 6 trong `PlcHandshakePersistenceTests.cs` xác minh `EnsureTagRegistered` hoạt động chuẩn 100%, chống trùng lặp. 6/6 test PASSED.

## 5. Hoàn thành Task 412: Khắc Phục Triệt Để Lỗi Mất Tag Handshake Khi Build Lại App & Cơ Chế Safe Guard / Auto-Recover
1. **Bối cảnh & Nguyên nhân**: Xung đột Two-Way Binding kép trên ComboBox Editable làm mất tag trong RAM khi mở tab; Target MSBuild `SyncReleaseConfigurations` lây lan file rỗng; Thiếu Safe Guard ở service.
2. **Các điểm sửa đổi cốt lõi**:
   - `PlcIndustrialConfig.cs`: Bổ sung `HasConfiguredTags()`, `ApplyFx5uDefaults(plcId)`, `ApplyStandardIoDefaults(plcId)`, `CopyFrom(other)`.
   - `PlcManagerService.cs`: Safe Guard chặn ghi đè rỗng trong `SaveGlobalConfig()`, tự động sao lưu `.backup`; Auto-Recover trong `LoadGlobalConfig()` dò tìm backup/seed hoặc nạp FX5U chuẩn (`M101`..`M105`); `EnsureIndustrialTagsRegistered` tự động đăng ký tag vào catalog.
   - `PlcManagerWindow.xaml` & `PlcManagerViewModel.cs`: Bỏ `SelectedItem` trên ComboBox; thêm thanh công cụ nạp nhanh 1 chạm `↺ Chuẩn FX5U (M101..M105)` và `↺ Chuẩn I/O (Y1..Y5 / X1)`.
3. **Kiểm thử tự động**: 5/5 test `PlcHandshakePersistenceTests.cs` PASSED 100%.

## 6. Các sự kiện & thay đổi gần đây
- Task 414: Khắc phục triệt để lỗi lệch tịnh tiến ảnh template SurfaceCompare khi sửa job cũ có Origin offset (dùng ExtractStraightRoi đồng bộ hệ tọa độ Teach vs Found, tách rời Move/Resize với lưu template, thêm nút "Set Template" trong Properties Panel); kiểm thử 11/11 PASSED 100%.
- Task 413: Khắc phục sự cố không nhận tín hiệu Trigger PLC M10 trên tool ImageSource (phân tích Scan Cycle vs Polling, tự động đăng ký tag vào PollingEngine, sửa ComboBox binding); kiểm thử 6/6 PASSED 100%.
- Task 412: Khắc phục triệt để lỗi mất cấu hình tag bắt tay 24/7 khi build lại app & bổ sung Safe Guard, Auto-Recover, nạp nhanh chuẩn FX5U (M101..M105) 1 chạm; kiểm thử 5/5 PASSED 100%.
- Task 411: Bổ sung tùy chọn kiểu tín hiệu gửi PLC (Level, Rising Edge, Falling Edge) kèm độ rộng xung PulseDurationMs trong cài đặt Handshake; kiểm thử 5/5 PASSED 100%.
- Task 410: Khắc phục triệt để lỗi trắng địa chỉ, mất chọn PLC trên HMI Designer & hỗ trợ chuẩn DINT 32-bit (Int32 Double Word D300, D302...); kiểm thử 4/4 PASSED 100%.
- Task 409: Khắc phục triệt để lỗi ảnh template của node SurfaceCompare bị đen ngòm trên job `job_7A11034A_GH68-58216B` (triệt tiêu tiền xử lý kép, chống ghi đè Search ROI vào file template, tự phục hồi sub-patch cho Job cũ; kiểm thử tự động PASSED 100%).
- Task 408: Rà soát loại bỏ 100% control vô dụng trong cửa sổ Train Template Tool Origin; thiết kế lại layout 2 cột hiện đại chuẩn công nghiệp; kiểm thử tự động PASSED 100%.
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
