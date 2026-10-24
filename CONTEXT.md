# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (7/7 test ToolEditor Column Resize & Full-Width, 5/5 test HMI State & Lifecycle, 4/4 test HMI Designer Persistence & DINT 32-bit, 4/4 test PLC Conflict & HMI Isolation, 7/7 test PLC Handshake, 5/5 test Reset Queue & HMI, 5/5 test PLC Signal Types, 11/11 test SurfaceCompare & Caliper, 8/8 test ContourCompare, 12/12 test Tool Calib Factor, 15/15 test OQC Scanner Live View, 8/8 test MvpShapeMatch2).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, PlcAck `""` Non-blocking, Queue In `M200`).

## 3. Hoàn thành Task 419: Tự Động Co Giãn Full Width 4 Cột Trên Tab Tool Editor Khi Ẩn/Hiện Cột
1. **Bối cảnh & 4 Nguyên nhân gốc rễ**:
   - *Nguyên nhân 1 (Pixel Width thay vì Star)*: Cột 0 (Toolbox) định nghĩa `new GridLength(340)` fixed pixel. Khi Cột 2 (Canvas) hoặc Cột 6 (Debug) bị ẩn, Toolbox vẫn giữ nguyên 340px, không tự co giãn chia sẻ không gian trống.
   - *Nguyên nhân 2 (Preview Column không có dynamic binding)*: Cột 4 (Preview) bị hardcode `Width="4.5*"` trong XAML mà không có binding thích ứng và không có cơ chế tái phân bổ tỷ lệ Star khi số lượng cột thay đổi.
   - *Nguyên nhân 3 (GridSplitter ghi đè Local Value và cắt đứt Binding)*: Khi người dùng kéo `GridSplitter`, WPF Grid gán Local Value dạng Pixel trực tiếp vào `ColumnDefinition.WidthProperty`, cắt đứt OneWay binding. Khi bấm nút ToggleButton ẩn cột, ViewModel cập nhật Width=0 nhưng giao diện không nhận được, khiến cột bị ẩn thành khoảng trống rỗng có kích thước pixel cũ, còn các cột còn lại bị ghim cứng không co giãn được.
   - *Nguyên nhân 4 (Splitter Cột 5 thiếu Visibility binding)*: `SplitterDebug` trước đây thiếu binding Visibility nên vẫn chiếm diện tích khi Debug ẩn.
2. **Các điểm sửa đổi cốt lõi**:
   - `ToolEditorViewModel.cs`:
     - Chuyển `ToolboxColumnWidth` sang Star (`new GridLength(2.0, GridUnitType.Star)`) khi hiện, `0` khi ẩn; `ToolboxColumnMinWidth` = 220 khi hiện, 0 khi ẩn.
     - Chuẩn hóa `CanvasColumnWidth` (`3.5*`), `PreviewColumnWidth` (`4.5*`), `DebugColumnWidth` (`3.2*`) và các SplitterWidth, ColumnMinWidth tương ứng.
   - `ToolEditorView.xaml`:
     - Đặt tên cho Grid (`EditorMainGrid`) và 7 `ColumnDefinition` (`ColToolbox`, `ColToolboxSplitter`, `ColCanvas`, `ColCanvasSplitter`, `ColPreview`, `ColDebugSplitter`, `ColDebug`).
     - Đăng ký `DragCompleted="MainSplitter_DragCompleted"` cho cả 3 Splitter (`SplitterToolbox`, `SplitterCanvas`, `SplitterDebug`).
     - Bổ sung `Visibility="{Binding IsDebugColumnVisible, Converter={StaticResource BoolToVis}}"` cho `SplitterDebug`.
     - Bổ sung nút ToggleButton `◧` (Ẩn/Hiện Toolbox) vào thanh công cụ Cột Preview (Cột 3) để người dùng có trọn bộ 3 nút `[◧ Toolbox, ▦ Canvas, ▤ Debug]` ngay tại trung tâm, điều khiển được cả khi Cột Canvas đã bị ẩn.
   - `ToolEditorView.xaml.cs`:
     - Quản lý lưu giữ tỷ lệ `_savedToolboxStar`, `_savedCanvasStar`, `_savedPreviewStar`, `_savedDebugStar`.
     - Khi người dùng kéo Splitter nhả chuột (`DragCompleted`), tự động đọc `ActualWidth` của các cột đang hiển thị và chuẩn hóa lại thành `Star`, loại bỏ hoàn toàn các giá trị Pixel cục bộ do GridSplitter gán vào DependencyProperty Width.
     - Lắng nghe `PropertyChanged` của ViewModel: khi cột thay đổi trạng thái Hiện/Ẩn, gọi `ApplyColumnLayout` để các cột còn hiển thị luôn là Star và lấp đầy 100% chiều rộng Grid (Full Width).
3. **Kiểm thử tự động**:
   - Tạo bộ test `ToolEditorColumnResizeTests.cs` (7/7 test PASSED 100%):
     1. Khởi tạo 4 cột đều là `GridUnitType.Star`.
     2. Ẩn cột Canvas (Cột 2): Width=0, MinWidth=0, các cột khác giữ Star.
     3. Ẩn cột Debug (Cột 4): Width=0, MinWidth=0, các cột khác giữ Star.
     4. Ẩn cột Toolbox (Cột 1): Width=0, MinWidth=0, các cột khác giữ Star.
     5. Ẩn cả 3 cột (Toolbox, Canvas, Debug): Preview chiếm trọn 100% full width.
     6. Sự kiện PropertyChanged bắn ra đầy đủ cho tất cả thuộc tính layout liên quan.
     7. Phục hồi hoàn hảo tỷ lệ Star và MinWidth khi bật lại các cột.
   - Toàn bộ solution biên dịch 0 Errors, toàn bộ test suite hồi quy PASSED 100%.

## 4. Hoàn thành Task 418: Tự Động Nạp Trạng Thái Hiện Tại Của Tag HMI Khi Load/Run, Khắc Phục Lỗi Mất Tên File/Không Tương Tác Được & Bổ Sung Nút Stop HMI
1. **Bối cảnh & 3 Nguyên nhân gốc rễ**:
   - *Nguyên nhân 1 (Không tự load trạng thái ban đầu của tag)*:
     - `HmiControlViewModel` chỉ thụ động chờ sự kiện `OnPlcTagUpdated` khi `PlcPollingEngine` bắn `OnTagChanged`.
     - Tuy nhiên `PlcPollingEngine` chỉ bắn `OnTagChanged` khi giá trị thay đổi (`!ValuesEqual(oldVal, newVal)`). Nếu tag trên PLC giữ nguyên trạng thái tĩnh từ trước (hoặc cache đã có từ vòng polling trước), `OnTagChanged` không bao giờ bắn ra. Thành phần HMI hiển thị sai trạng thái ban đầu (ví dụ xi lanh đang mở hiển thị đóng, đèn đang sáng hiển thị tắt). Chỉ khi người dùng click tương tác (kích hoạt `WriteTagValueAsync` $\rightarrow$ `NotifyTagChanged`) thì thành phần đó mới nhảy dữ liệu.
     - Ngoài ra, hàm `OnPlcTagUpdated` dùng `System.Windows.Application.Current?.Dispatcher?.BeginInvoke` với priority `Background`. Trong unit test hoặc khi không có message pump, biểu thức bị nuốt hoặc hoãn vô thời hạn.
   - *Nguyên nhân 2 (Controls không kích hoạt RunMode & không tương tác được khi mở app)*:
     - Trong `HmiManagerViewModel`, `partial void OnIsRunModeChanged(bool value)` rỗng, không truyền `ctrl.IsRunMode = value` cho các control con.
     - Do đó `ctrl.IsRunMode` vẫn là `false`. Trong `HmiControlView.xaml.cs`, `PreviewMouseDown` và `PreviewMouseUp` kiểm tra `if (vm.IsRunMode)` nên bỏ qua hoàn toàn sự kiện chuột $\rightarrow$ người dùng click vào button/switch không có bất kỳ phản hồi nào.
   - *Nguyên nhân 3 (ComboBox trắng trơn và hiển thị layout demo)*:
     - `InitHmiPanelMonitoring()` gọi `RefreshAvailableHmiFiles()` khi `SelectedHmiFilePath` còn rỗng. Nếu `savedHmiPath` nằm ngoài các thư mục mặc định, nó không được thêm vào `AvailableHmiFileItems` $\rightarrow$ ComboBox bị trắng trơn.
     - `HmiFileItem` không override `Equals`/`GetHashCode` khiến ComboBox so sánh tham chiếu thất bại khi bind lại.
     - `_hmiMonitorViewModel` khởi tạo với 5 controls demo của `InitDefaultLayout()`. Vì không nạp tệp thật của người dùng ngay từ đầu, màn hình hiển thị 5 nút demo không có địa chỉ PLC, người dùng phải nạp lại thủ công mới dùng được.
2. **Các điểm sửa đổi cốt lõi**:
   - `HmiControlViewModel.cs`:
     - `EffectiveReadAddress`: Tự động fallback sang `WriteTagName` khi người dùng chỉ cấu hình địa chỉ ghi mà không đặt địa chỉ đọc riêng biệt.
     - Bổ sung `RefreshCurrentValueFromPlc()`: Kiểm tra ngay giá trị trong Cache của `_plcService.GetTagValue(...)` và cập nhật tức thì; nếu chưa có trong cache thì đọc bất đồng bộ từ PLC qua `ReadTagValueAsync`.
     - Bổ sung `DispatchOnUiThread(Action)`: Thực thi trực tiếp nếu đang ở UI thread hoặc test console, chỉ gọi `BeginInvoke` khi ở background thread có dispatcher.
     - Gọi `RefreshCurrentValueFromPlc()` ngay trong `OnIsRunModeChanged(true)`.
   - `HmiManagerViewModel.cs`:
     - Chuyển toàn bộ logic quản lý Run Mode vào `partial void OnIsRunModeChanged(bool value)`: Duyệt 100% controls gán `ctrl.IsRunMode = value`, đăng ký địa chỉ và kích hoạt `RefreshCurrentValueFromPlc()`.
     - Bổ sung phương thức công khai `RefreshAllControlsFromPlc()` làm tươi toàn bộ controls.
     - `LoadFromFile`: Tự động gọi `RefreshCurrentValueFromPlc()` cho từng control và kích hoạt làm tươi toàn bộ sau nhịp ngắn nếu đang ở RunMode.
   - `ToolEditorViewModel.HmiPanel.cs`:
     - `HmiFileItem`: Kế thừa `IEquatable<HmiFileItem>`, so sánh `FilePath` không phân biệt hoa thường.
     - `InitHmiPanelMonitoring`: Nhận `savedHmiPath`, thêm ngay vào `AvailableHmiFileItems`, nạp thẳng tệp thật vào `_hmiMonitorViewModel.LoadFromFile(SelectedHmiFilePath)` triệt tiêu hoàn toàn layout demo; khôi phục `ToolEditorIsHmiVisible`.
     - Bổ sung thuộc tính `IsHmiRunning` và các lệnh: `StopHmiMonitorCommand` (dừng HMI, ngắt polling, khóa tương tác), `StartHmiMonitorCommand` (chạy HMI, kích hoạt RunMode, làm tươi tag), `ToggleHmiRunMonitorCommand`.
     - Bổ sung thuộc tính Live Badge và nút bấm: `HmiLiveBadgeText` (`LIVE (RUN)` / `ĐÃ DỪNG (STOP)`), `HmiLiveBadgeDot`, `HmiLiveBadgeBackground`, `HmiLiveBadgeBorder`, `HmiLiveBadgeForeground`, `HmiRunStopButtonText`, `HmiRunStopButtonBackground`, `HmiRunStopButtonToolTip`.
   - `ToolEditorView.xaml`:
     - ComboBox HMI: Bổ sung `SelectedValuePath="FilePath"`.
     - Bổ sung nút **`⏹️ Dừng HMI` / `▶️ Chạy HMI`** ngay cạnh badge LIVE và trên thanh công cụ bên phải.
     - Badge động chuyển đổi trực quan theo trạng thái `IsHmiRunning`.
   - `GlobalAppSettings.cs`: Bổ sung thuộc tính `ToolEditorIsHmiVisible` để ghi nhớ trạng thái Hiện/Ẩn HMI khi tắt/mở app.
   - `PlcManagerService.cs`: Sửa `InferDataTypeFromAddress` nhận diện chính xác `MW` là `Int16`; nới lỏng nạp tag cấu hình trong `GetAllTagsToPoll`.
3. **Kiểm thử tự động**:
   - Tạo bộ test `HmiStateAndLifecycleTests.cs` (5/5 test PASSED 100%):
     1. Tự động nạp giá trị hiện tại của Tag khi Load và Run (không cần tương tác click).
     2. Fallback EffectiveReadAddress sang WriteTagName khi không dùng địa chỉ riêng.
     3. So sánh bình đẳng HmiFileItem (IEquatable) và chống trùng lặp ComboBox.
     4. Nút Dừng HMI / Chạy HMI và trạng thái Live Badge cô lập an toàn.
     5. Lưu và phục hồi ToolEditorIsHmiVisible & ToolEditorSelectedHmiPath.
   - Toàn bộ solution biên dịch 0 Errors, toàn bộ test suite hồi quy PASSED 100%.

## 4. Hoàn thành Task 417: Khắc Phục Triệt Để Tranh Chấp Kết Nối PLC & Cô Lập Hoàn Toàn HMI Tool Editor
- Sửa lệch byte stream MC Protocol 3E, drain sạch `dataLen - 2` byte khỏi socket, ném `IOException` khi stream đóng.
- Tắt Watchdog Heartbeat khi thiếu tag cấu hình, chặn tự ý ghi tag lỗi `Y10_VisionFault`.
- Cô lập HMI Panel khi Ẩn, bảo đảm Bit device luôn là `PlcDataType.Bool`.
- Kiểm thử tự động `PlcConflictAndHmiIsolationTests.cs` (4/4 test PASSED 100%).

## 5. Các sự kiện & thay đổi gần đây
- Task 418: Tự động nạp trạng thái hiện tại của Tag HMI khi Load/Run, khắc phục lỗi mất tên file/không tương tác được & thêm nút Stop HMI; kiểm thử 5/5 PASSED 100%.
- Task 417: Khắc phục triệt để tranh chấp kết nối PLC làm đổi trạng thái xanh/đỏ liên tục & cô lập hoàn toàn HMI Tool Editor; kiểm thử 4/4 PASSED 100%.
- Task 416: Khắc phục triệt để lỗi Thread Ownership làm ngắt kết nối PLC Ethernet & chuẩn hóa chế độ bỏ PLC Ack / Non-blocking; kiểm thử 7/7 PASSED 100%.
- Task 415: Chia đôi cột Preview Tool Editor hiển thị HMI & bổ sung tag Reset Queue Vision trong cài đặt bắt tay 24/7 Handshake (cạnh lên, cạnh xuống, long press); kiểm thử 5/5 PASSED 100%.
- Task 414: Khắc phục triệt để lỗi lệch tịnh tiến ảnh template SurfaceCompare khi sửa job cũ có Origin offset (dùng ExtractStraightRoi đồng bộ hệ tọa độ Teach vs Found, tách rời Move/Resize với lưu template, thêm nút "Set Template" trong Properties Panel); kiểm thử 11/11 PASSED 100%.
- Task 413: Khắc phục sự cố không nhận tín hiệu Trigger PLC M10 trên tool ImageSource (phân tích Scan Cycle vs Polling, tự động đăng ký tag vào PollingEngine, sửa ComboBox binding); kiểm thử 6/6 PASSED 100%.
- Task 412: Khắc phục triệt để lỗi mất cấu hình tag bắt tay 24/7 khi build lại app & bổ sung Safe Guard, Auto-Recover, nạp nhanh chuẩn FX5U (M101..M105) 1 chạm; kiểm thử 5/5 PASSED 100%.
- Task 411: Bổ sung tùy chọn kiểu tín hiệu gửi PLC (Level, Rising Edge, Falling Edge) kèm độ rộng xung PulseDurationMs trong cài đặt Handshake; kiểm thử 5/5 PASSED 100%.
- Task 410: Khắc phục triệt để lỗi trắng địa chỉ, mất chọn PLC trên HMI Designer & hỗ trợ chuẩn DINT 32-bit (Int32 Double Word D300, D302...); kiểm thử 4/4 PASSED 100%.
- Task 409: Khắc phục triệt để lỗi ảnh template của node SurfaceCompare bị đen ngòm trên job GH68-58216B (triệt tiêu tiền xử lý kép, chống ghi đè Search ROI vào file template; kiểm thử 9/9 PASSED 100%).
- Task 408: Rà soát loại bỏ 100% control vô dụng trong cửa sổ Train Template Tool Origin; thiết kế lại layout 2 cột hiện đại chuẩn công nghiệp; kiểm thử tự động PASSED 100%.
- Task 407: Bổ sung phím tắt Ctrl + R (xoay strip Caliper/EdgePair) và Ctrl + F4 (đóng Job đang mở); kiểm thử tự động PASSED 100%.
