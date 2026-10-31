# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP/Android Pro Mode Native Server, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Dự án Android Native**: `AndroidCameraServer` (Gradle 8.11, CompileSDK 34, Android 14) đã biên dịch thành công APK và cài đặt trực tiếp lên Samsung Galaxy S24 Ultra (`SM-S928B`).
- **Kiểm thử tự động**: PASSED 100% (7/7 test PlcHandshakeNumericMode OK/NG Int16 & Both, 7/7 test PlcHandshakePersistence, 5/5 test PlcSignalType, 6/6 test CodeDetection Result, 5/5 test ObjectCounting, 6/6 test AndroidCameraProMode, 6/6 test PlcContinuousQueue 16 Slots, 7/7 test ToolEditor Column Resize, 5/5 test HMI State & Lifecycle, 4/4 test HMI Designer Persistence & DINT 32-bit, 4/4 test PLC Conflict & HMI Isolation).
- **Cấu hình chuẩn xưởng CMS_VINA**: 
  - Database: `CMS_VINA` (192.168.1.2:6789)
  - OQC Server API: `https://192.168.1.192/vision_upload.php`
  - OTA Update Server: `http://192.168.1.192/update/version.json` & `http://192.168.1.192/ota_server.php`
  - PLC Handshake: `PLC_2` (Mitsubishi FX5U: Ready `M101`, Busy `M102`, Done `M103`, Pass `M104` RisingEdge 20ms, NG `M105` RisingEdge 20ms, Kết quả số `D100` OK=2, NG=3, Queue In `M200`).

## 3. Hoàn thành Task 426: Bổ Sung Chế Độ Xuất Kết Quả OK/NG Dạng Số Nguyên (Int16) Song Hành Cùng Dạng Bit Bool Trong Cửa Sổ Bắt Tay 24/7 (Handshake)
1. **Bối cảnh & Yêu cầu**:
   - Trước đây cửa sổ Bắt tay 24/7 chỉ có 1 chế độ gửi kết quả duy nhất qua các bit Bool rời rạc (Pass `M104`, NG `M105`).
   - Người dùng yêu cầu bổ sung thêm chế độ xuất kết quả dạng số nguyên (Int16 Word, ví dụ OK gửi 2, NG gửi 3) và cho phép người dùng linh hoạt lựa chọn: chỉ gửi Bit, chỉ gửi Số, hoặc song hành gửi cả hai.
2. **Kiến trúc & Các thành phần đã triển khai**:
   - **`PlcIndustrialConfig.cs` & `PlcResultOutputMode`**:
     - Định nghĩa enum `PlcResultOutputMode`: `Bit = 0` (2 bit Pass/NG), `NumericInt16 = 1` (Thanh ghi Word Int16), `Both = 2` (Song hành đồng thời cả Bit & Số).
     - Bổ sung cấu hình `IndustrialHandshakeConfig`: `ResultOutputMode`, `ResultWordTagName` (mặc định `D100` FX5U / `MW100` I/O), `PassNumericValue = 2`, `NgNumericValue = 3`, `IdleNumericValue = 0`, `ClearResultWordOnReady = true`, `IsBitModeActive`, `IsNumericModeActive`.
     - Hiện thực `INotifyPropertyChanged` cho `IndustrialHandshakeConfig`.
   - **`IndustrialHandshakeStateMachine.cs`**:
     - Bổ sung `GetEffectiveResultWordTag()` (fallback `D100_VisionResult`, `D100`, `MW100`).
     - `SetReadyAsync` & `SetIdleAsync`: Tự động reset thanh ghi kết quả về `IdleNumericValue` (0) khi sẵn sàng đón phôi mới.
     - `CompleteHandshakeAsync`: Nếu chọn `Bit` hoặc `Both` thì phát tín hiệu bit Pass/NG; nếu chọn `NumericInt16` hoặc `Both` thì ghi giá trị `PassNumericValue` (2) / `NgNumericValue` (3) vào thanh ghi Word; duy trì tín hiệu Done và hàng đợi M200.
   - **`PlcManagerService.cs`**:
     - `EnsureIndustrialTagsRegistered`: Tự động đăng ký `ResultWordTagName` với kiểu `PlcDataType.Int16`.
     - `GetAllTagsToPoll`: Đưa `ResultWordTagName` vào Polling với `PlcDataType.Int16`.
   - **`PlcManagerViewModel.cs` & `PlcManagerWindow.xaml`**:
     - Cung cấp danh sách lựa chọn `AvailableResultOutputModes` kèm mô tả rõ ràng.
     - Thiết kế giao diện Tab 2 trực quan: Thêm ComboBox chọn chế độ xuất kết quả, phân tách rõ vùng cấu hình Bit Bool và vùng cấu hình Thanh ghi Số Word (D100, OK=2, NG=3, Idle=0, Checkbox Clear on Ready).
   - **`ToolEditorViewModel.HandshakeUi.cs` & `ToolEditorView.xaml`**:
     - Thêm Mini Indicator trên thanh công cụ hiển thị trực tiếp giá trị thanh ghi Word (`D100: 2` màu Xanh khi OK, `D100: 3` màu Đỏ khi NG).
     - Bổ sung thông tin phương thức xuất và trạng thái Word vào Handshake ToolTip và Status Bar.
3. **Kiểm thử tự động**:
   - `PlcHandshakeNumericModeTests.cs`: 7/7 test PASSED 100% (JSON serialization, helper/defaults, Int16 OK=2 / NG=3, Both song hành Bit & Int16, Custom values, Int16 tag registration, ViewModel options).
   - `PlcHandshakePersistenceTests.cs`: 7/7 test PASSED 100%.
   - `PlcSignalTypeTests.cs`: 5/5 test PASSED 100%.

## 4. Các sự kiện & thay đổi gần đây
- Task 426: Hoàn thành chế độ xuất kết quả OK/NG dạng số nguyên (Int16 Word: D100, OK=2, NG=3) song hành cùng dạng Bit Bool trong cửa sổ Bắt tay 24/7; kiểm thử 7/7 PASSED 100%.
- Task 425: Bổ sung chi tiết lỗi NG cho tool CodeDetection trong SpecResults, NgReasonsText và thẻ CodeDetection; kiểm thử 6/6 PASSED 100%.
- Task 424: Hoàn thành Tool ObjectCounting, sửa dứt điểm lỗi đếm 69 vật thể trên vỉ thuốc -> đếm chuẩn xác 10/10 viên thuốc; kiểm thử 5/5 PASSED 100%.
- Task 423: Xây dựng Android App VisionCameraServer (Camera2 Pro Mode, 200MP, EV, Focus, Flash Torch) & cài đặt thành công lên S24 Ultra; kiểm thử 6/6 PASSED 100%.
- Task 422: Tích hợp Camera Android Galaxy S24 Ultra Pro Mode (USB/Wi-Fi/LAN, 200MP, EV, Focus, Flash Torch); kiểm thử 6/6 PASSED 100%.
- Task 421: Khắc phục triệt để độ trễ 1 giây/nấc khi xả hàng đợi 16 nấc Run Continuous; kiểm thử 6/6 PASSED (89ms/3 nấc).
- Task 420: Tích hợp PLC Trigger vào BoundedChannel 16 nấc & thêm nút Trigger tay; kiểm thử 5/5 PASSED 100%.
- Task 419: Tự động co giãn Full Width 4 cột trên Tab Tool Editor khi Ẩn/Hiện cột; kiểm thử 7/7 PASSED 100%.
- Task 418: Tự động nạp trạng thái hiện tại của Tag HMI khi Load/Run; kiểm thử 5/5 PASSED 100%.
- Task 417: Khắc phục triệt để tranh chấp kết nối PLC & cô lập hoàn toàn HMI Tool Editor; kiểm thử 4/4 PASSED 100%.
- Task 416: Khắc phục triệt để lỗi Thread Ownership làm ngắt kết nối PLC Ethernet; kiểm thử 7/7 PASSED 100%.
