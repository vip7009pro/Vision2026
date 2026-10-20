# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ 5/5 test PLC Signal Types, 4/4 test HMI Designer Persistence & DINT 32-bit, 10/10 test SurfaceCompare & Caliper, regression test tệp Job `job_7A11034A_GH68-58216B`, Ctrl+R & Ctrl+F4 ROI, 8/8 test ContourCompare, 12/12 test Tool Calib Factor, 15/15 test OQC Scanner Live View, 8/8 test MvpShapeMatch2).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`

## 3. Hoàn thành Task 411: Tùy Chọn Kiểu Tín Hiệu Gửi PLC (Level, Rising Edge, Falling Edge) Trong Cài Đặt Handshake
1. **Bối cảnh & Yêu cầu**:
   - Khi giao tiếp với PLC (FX5U, Q-Series...), các lập trình viên PLC có nhu cầu nhận tín hiệu kết quả theo nhiều dạng sóng khác nhau:
     - `Level`: Giữ mức logic cho đến chu trình kế tiếp.
     - `Rising Edge`: Phát xung cạnh lên ($0 \rightarrow 1 \rightarrow 0$) trong khoảng thời gian `PulseDurationMs` (mặc định 100ms), thuận tiện cho PLC dùng lệnh `LDP M104` để set cờ chốt kết quả.
     - `Falling Edge`: Mức tĩnh là 1 (Active-Low), khi kích hoạt phát xung cạnh xuống ($1 \rightarrow 0 \rightarrow 1$) trong `PulseDurationMs`, thuận tiện cho lệnh `LDF M104` hoặc mạch an toàn thường đóng.
2. **Các điểm sửa đổi cốt lõi**:
   - `PlcIndustrialConfig.cs`: Khai báo enum `PlcSignalType` (`Level = 0`, `RisingEdge = 1`, `FallingEdge = 2`). Bổ sung `PassSignalType`, `NgSignalType`, `DoneSignalType` và `PulseDurationMs` vào `IndustrialHandshakeConfig`.
   - `IndustrialHandshakeStateMachine.cs`: Thêm `EmitSignalAsync(...)` xử lý phát xung non-blocking có trễ `PulseDurationMs` cho cả 3 chế độ sóng. Cập nhật `SetReadyAsync`/`SetIdleAsync` đảm bảo đưa chân Falling Edge về mức tĩnh 1 và chân khác về 0.
   - `PlcManagerViewModel.cs` & `PlcManagerWindow.xaml`: Bổ sung menu ComboBox chọn kiểu tín hiệu cho Done, Pass (OK), NG (Lỗi) và ô nhập độ rộng xung Edge (ms) trong Tab 2 Bắt tay 24/7.
   - `ToolEditorViewModel.HandshakeUi.cs`: ToolTip trạng thái Handshake hiển thị trực quan kiểu tín hiệu đã cài đặt.
3. **Kiểm thử tự động**:
   - Thêm bộ test [PlcSignalTypeTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/PlcSignalTypeTests.cs) gồm 5 bài kiểm tra: Serialization JSON, Rising Edge, Falling Edge, Level và danh sách ViewModel.
   - Toàn bộ 5/5 test PASSED 100%. Toàn bộ solution biên dịch: 0 Errors.

## 4. Hoàn thành Task 410: Khắc Phục Triệt Để Lỗi Trắng Địa Chỉ, Mất Chọn PLC & Dãy Số Dài Ngoẵng D300, D302 Trên HMI Designer
1. **Bản chất nguyên nhân gốc rễ**:
   - *Nguyên nhân 1 (Xung đột Two-Way Binding kép trên ComboBox Editable)*: `HmiPropertyInspectorView.xaml` vừa bind `Text` vừa bind `SelectedItem` Two-Way vào `ReadTagName`. Khi gõ phím, chuỗi đang gõ chưa có trong `AvailableTags` khiến ComboBox set `SelectedItem = null`, lập tức ghi đè `ReadTagName = null` và xóa trắng ô nhập.
   - *Nguyên nhân 2 (Bão sự kiện `Clear()` tập dữ liệu khi gõ phím)*: Mỗi ký tự gõ vào gọi `EnsureDirectAddressRegistered()` $\rightarrow$ đăng ký vào `_plcService.Tags` $\rightarrow$ `Tags.CollectionChanged` $\rightarrow$ gọi `RefreshAvailablePlcsAndTags()` $\rightarrow$ gọi `AvailablePlcItems.Clear()` và `AvailableTags.Clear()`. Việc `Clear()` làm mất hết dữ liệu ComboBox khiến WPF gán `SelectedValue = null`, làm mất chọn PLC và xóa trắng địa chỉ.
   - *Nguyên nhân 3 (Nạp tệp `.hmi`)*: Các PLC ID trong tệp (như `PLC1`, `PLC_OSC`) chưa có trong `AvailablePlcItems` nên ComboBox không tìm thấy item tương ứng $\rightarrow$ hiển thị trắng trơn không tự chọn được; tương tự với các Tags chưa có sẵn.
   - *Nguyên nhân 4 (Can thiệp chuột)*: Sự kiện `ComboBox_PreviewMouseDown` ép mở Dropdown khi click vào ô Text gây mất trỏ chuột và lỗi nhập.
   - *Nguyên nhân 5 (Dãy số dài ngoẵng khoa học `4.4E-323` cho D300, D302, D304)*: Trong GX Works 3, thanh ghi đếm sản phẩm `D300, D302, D304` là kiểu **Double Word (DINT - 32 bit Integer, chiếm 2 thanh ghi D)**. Người dùng thấy chữ "Double" nên chọn nhầm kiểu `Double` trong HMI (vốn là số thực 64-bit IEEE 754 chiếm 4 thanh ghi D). Driver đọc 4 words từ PLC và giải mã thành số thực subnormal $4.4 \times 10^{-323}$. Ngoài ra `HmiControlViewModel` thiếu format chuỗi cho `Double`/`UInt32` khiến in chuỗi số mũ rác tràn viền.
2. **Các điểm sửa đổi cốt lõi**:
   - `HmiPropertyInspectorView.xaml`: Loại bỏ `SelectedItem` và `PreviewMouseDown`, chỉ giữ `Text="{Binding ..., UpdateSourceTrigger=PropertyChanged}"` cho phép gõ tự do tức thì và mượt mà. Bổ sung nhãn hướng dẫn: *Đếm sản phẩm (D300, D302...) trong GXWorks là DINT 32-bit $\rightarrow$ Chọn 'Int32 (Double Word)'*.
   - `HmiManagerViewModel.cs`: Chuẩn hóa danh sách kiểu dữ liệu `AvailableDataTypes` đưa `Int32 (Double Word / DINT 32-bit: D300, D302...)` lên đầu; nâng cấp `RefreshAvailablePlcsAndTags()` loại bỏ cơ chế xóa phá hủy `Clear()`; bổ sung `SyncPlcsAndTagsFromConfig` nạp trước toàn bộ PLC ID và Tags từ tệp `.hmi`.
   - `HmiControlViewModel.cs`: Chuẩn hóa setter chống null (`value ?? ""`); ngắt đăng ký sớm `EnsureDirectAddressRegistered` khi đang ở chế độ thiết kế; bổ sung case format `Double` và `UInt32`, khử triệt để số mũ rác subnormal $< 10^{-9}$ về 0, tự động cập nhật DataType của tag vào `_plcService.Tags`.
3. **Kiểm thử tự động**:
   - Bổ sung bộ test [HmiDesignerPersistenceTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/HmiDesignerPersistenceTests.cs) gồm 4 test: Nạp tệp `.hmi` bảo toàn PLC & địa chỉ, Nhập địa chỉ tự do không bị trắng, Lưu và nạp địa chỉ ghi riêng độc lập, Khử số mũ rác subnormal và format DINT 32-bit.
   - 4/4 test HMI PASSED 100%. Biên dịch 0 Errors.

## 4. Hoàn thành Task 409: Khắc Phục Triệt Để Lỗi Ảnh Template SurfaceCompare Bị Đen Ngòm (Job GH68-58216B)
1. **Bản chất nguyên nhân gốc rễ**:
   - *Nguyên nhân 1 (Tiền xử lý kép)*: File template `kt mat net_sc.png` khi teach đã được lưu từ ảnh sau node tiền xử lý `Nhi phan 4` (dùng `InRange(128, 246)` nên chỉ gồm pixel `0` và `255`). Tại runtime, pipeline lại gọi tiếp `preprocessor.Run(templRaw, settings)` một lần nữa. Vì `0 < 128` và `255 > 246`, không có pixel nào nằm trong $[128, 246]$, hàm `InRange` biến 100% pixel thành `0` (đen ngòm).
   - *Nguyên nhân 2 (Ghi đè Search ROI)*: Trong `ToolEditorViewModel.Engine.cs`, khi người dùng kéo Search ROI (`SC`), code nhầm lẫn gọi hàm lưu template với kích thước $500 \times 284$ của Search ROI thay vì giữ $453 \times 39$ của Template ROI.
2. **Các điểm sửa đổi cốt lõi**:
   - `InspectionService.Pipeline.cs`:
     - Bỏ tiền xử lý lần 2 trên `templRaw`, giữ nguyên dữ liệu gốc sắc nét của template đã huấn luyện.
     - Cơ chế tự phục hồi thông minh: nếu file template trên đĩa có kích thước khớp `InspectRoi`, tự động trích xuất sub-patch tọa độ `TemplateRoi` thay vì méo hình bằng `Resize`.
     - Đồng bộ `ResolveToolPreprocess("SurfaceCompare", sc.Name)` tương tự `ContourCompare` để nhận trọn vẹn chuỗi tiền xử lý đa tầng (`Den trang` $\rightarrow$ `Nhi phan 4`).
   - `ToolEditorViewModel.Engine.cs`:
     - Loại bỏ nhánh ghi đè template khi người dùng di chuyển hoặc co dãn Search ROI `SC`. Chỉ cập nhật template khi chủ động chỉnh sửa Template ROI `SCT`.
3. **Kiểm thử tự động**:
   - Bổ sung `Test_ReproduceUserJobBlackTemplate` trong [SurfaceCompareAndCaliperRoiTests.cs](file:///g:/NODEJS\Vision2026\TestExtractApp\SurfaceCompareAndCaliperRoiTests.cs).
   - Xác minh trên đúng file Job của người dùng: `Template Image: 453x39, MinVal: 0, MaxVal: 255`, `Pass: True, Count: 0, MaxArea: 0` (ảnh sáng rõ nét 100%, không còn đen ngòm).
   - 9/9 test SurfaceCompare & Caliper PASSED 100%.

## 5. Các sự kiện & thay đổi gần đây
- Task 409: Khắc phục triệt để lỗi ảnh template của node SurfaceCompare bị đen ngòm trên job `job_7A11034A_GH68-58216B` (triệt tiêu tiền xử lý kép, chống ghi đè Search ROI vào file template, tự phục hồi sub-patch cho Job cũ; kiểm thử tự động PASSED 100%).
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
