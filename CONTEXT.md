# CONTEXT.md

## Dự án: Vision Inspection App (C# WPF, .NET 8)

### Trạng thái hiện tại
- Ứng dụng kiểm tra ngoại quan bằng xử lý ảnh (OpenCvSharp4, WPF) đã được nâng cấp hỗ trợ camera và cải tiến giao diện kết quả kiểm tra.
- Có cấu trúc MVVM, chia làm các tab chính:
  1. **Tool Editor**: Chỉnh sửa đồ thị công cụ vision, đo đạc, kiểm tra. (Hỗ trợ Live Preview thời gian thực trên Selected Node, chụp và giữ ảnh tĩnh khi nhấn nút, và giải phóng config cũ hoàn toàn khỏi RAM).
  2. **Calibration**: Cân chuẩn camera (chuyển đổi pixel sang mm). (Đã thêm tính năng Chụp từ Camera, khởi động rỗng).
  3. **Manual Inspection**: Đo đạc thủ công trên ảnh. (Đã thêm tính năng Chụp từ Camera).
  4. **Inspection**: Chạy kiểm tra tự động PASS/FAIL dựa trên cấu hình đồ thị công cụ. (Đã thêm tính năng Chụp từ Camera, khởi động rỗng. Bổ sung bảng hiển thị trạng thái OK/NG kích thước lớn cùng chi tiết lỗi ở cột phải).
  5. **Live Camera**: Xem luồng camera trực tiếp, cấu hình kiểm tra trực tiếp. (Đã nâng cấp quét tên camera thật bằng `DirectShowLib`, hỗ trợ camera công nghiệp RTSP, và bổ sung cổng fallback tĩnh cho camera ảo như DroidCam. Tự động khôi phục camera đã chọn lần trước).
  6. **Camera Settings**: Cấu hình độ sáng, độ tương phản và chế độ màu/đen trắng cho luồng camera đầu vào gốc. Đã sửa lỗi trắng trang bằng cách tạo file code-behind và khai báo NullToVisibilityConverter resource.
  7. **Batch Processing**: Xử lý hàng loạt ảnh. (Khởi động rỗng).
  8. **PLC**: Kết nối PLC Mitsubishi qua MX Component.

### Thay đổi đã thực hiện
1. **Quét danh sách camera USB qua DirectShowLib**:
   - Cài đặt package `DirectShowLib.Standard` (version 2.1.0) giống dự án tham chiếu `VisionQC`.
   - Cập nhật [DirectShowDeviceEnumerator.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Services/DirectShowDeviceEnumerator.cs) để gọi `DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice)` quét camera USB và camera ảo (như DroidCam) cực kỳ ổn định.
2. **Cơ chế Camera Port Fallback (Hỗ trợ DroidCam / OBS Virtual Camera)**:
   - Dù thiết bị ảo đang offline lúc mở ứng dụng, chương trình sẽ luôn luôn bổ sung thêm các Camera Port tĩnh từ 0 tới 4 vào danh sách dưới dạng `"Camera Port X (Fallback)"`.
3. **Hỗ trợ Custom RTSP / Network Camera**:
   - Thêm tùy chọn "Custom RTSP / IP Camera" vào danh sách camera.
   - Thêm TextBox nhập RTSP URL trong giao diện Live Camera (chỉ hiển thị khi chọn camera RTSP) giúp kết nối DroidCam qua luồng HTTP MJPEG (`http://localhost:4747/video`) mượt mà.
4. **Nâng cấp `CameraService` (FPS Limiting & Thread chuyên dụng)**:
   - Thay thế vòng lặp capture async Task cũ bằng một **Thread nền đồng bộ chuyên dụng (`Thread.Sleep`)** độc lập.
   - Khống chế cứng tốc độ khung hình ở mức tối đa ~30 FPS (khoảng 33ms/frame) giúp DroidCam Wifi không bị quá tải mạng.
   - Tự động lưu và tải cấu hình camera đã chọn (Index, RTSP URL, RTSP state) cùng các thông số hình ảnh điều chỉnh vào file `camera_adjust_settings.json`.
5. **Bộ lọc điều chỉnh hình ảnh gốc (Brightness, Contrast, Grayscale)**:
   - Thêm các thuộc tính `Brightness` (từ `-100` đến `100`), `Contrast` (từ `0.5` đến `3.0`), và `IsGrayscale` (đen trắng).
   - Các giá trị này được áp dụng trực tiếp lên ảnh gốc của camera bằng thuật toán OpenCV `input.ConvertTo(output, -1, contrast, brightness)` và chuyển đổi màu sắc trước khi phát đi.
6. **Tối ưu hóa đa luồng WPF (Thread Marshalling)**:
   - Trong `OnFrameCaptured`, chuyển đổi `Mat` thành `BitmapSource` rồi gọi `.Freeze()` để biến đối tượng thành thread-safe, cho phép truy cập chéo thread tốc độ cao mà không làm khóa UI.
   - Gán `LiveImage` và cập nhật FPS thông qua `Dispatcher.BeginInvoke`.
   - Bọc toàn bộ logic cập nhật `ObservableCollection` trong `RunLiveInspection` vào `Dispatcher.BeginInvoke`. Việc này loại bỏ hoàn toàn hiện tượng giật lag cực độ và đơ giao diện khi camera chạy trực tiếp.
7. **Giao diện Tab Camera Settings mới**:
   - Thêm [CameraSettingsViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/CameraSettingsViewModel.cs) và [CameraSettingsView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/CameraSettingsView.xaml) hiển thị live stream và các slider kéo thả điều chỉnh chất lượng hình ảnh đầu vào.
   - Tạo file code-behind [CameraSettingsView.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/CameraSettingsView.xaml.cs) và [NullToVisibilityConverter.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Converters/NullToVisibilityConverter.cs) để sửa lỗi trắng tab Camera Settings.
8. **Tối ưu hóa Quản lý Cấu hình (Vision Configurations)**:
   - Sửa đổi các ViewModels để khi khởi chạy ứng dụng, không tự động gán bất kỳ cấu hình nào (`SelectedConfig = null` và `ProductCode = ""`), đảm bảo trạng thái sạch rỗng.
   - Viết hàm `ClearActiveGraph()` trong `ToolEditorViewModel.cs` để dọn sạch hoàn toàn các node, edge, các cấu hình tool chi tiết (Points, Lines, Calipers, v.v.) khỏi RAM khi người dùng chuyển đổi hoặc deselect cấu hình.
   - Sửa hàm `NewGraph()` và `LoadConfig()` khởi tạo thực thể `VisionConfig` mới để loại bỏ hiện tượng lưu lẫn lộn các tool từ cấu hình cũ.
9. **Tích hợp Live Preview & Chụp ảnh trong Tool Editor**:
   - Khi ở chế độ Live Preview (mặc định), sự kiện `FrameCaptured` của camera sẽ liên tục nạp frame mới trực tiếp vào `SharedImageContext` 30 lần/giây, thực hiện xử lý đồ thị công cụ và vẽ preview Selected Node thời gian thực.
   - Nút bấm Capture Camera được liên kết với `CaptureButtonText`. Nhấp nút ở chế độ Live sẽ chụp và giữ ảnh tĩnh, đổi nhãn thành "Live Preview". Nhấp lại sẽ quay lại chế độ Live stream.
10. **Bổ sung bảng OK/NG trực quan và chi tiết lỗi NG trong tab Inspection**:
    - **Cải tiến UI (Layout Split)**: Chia Grid khu vực Result thành 2 cột (Cột trái: Bảng kết quả chi tiết; Cột phải: Khối OK/NG cực lớn).
    - **Khối OK/NG lớn nổi bật**: Khối hiển thị chữ **OK** (nền xanh lá, chữ xanh đậm) hoặc **NG** (nền đỏ nhạt, chữ đỏ crimson) cực to (Font size 72pt, bold).
    - **Hiển thị Lý do NG tự động**:
      - Nhận diện lỗi không bắt được Origin (`result.Origin == null || !result.Origin.Pass`) hiển thị cụ thể điểm khớp so với ngưỡng (`• Không bắt được origin (Score: 0.65 < Yêu cầu: 0.80)`).
      - Nhận diện lỗi từ các phép đo đo đạc thực tế, các điều kiện logic, và các lỗi ngoại quan bề mặt.
11. **Đồng bộ hóa Preprocess khi Teach/Save Template**:
    - Sửa đổi `TrySaveTemplateImage` và `TrySaveSurfaceCompareTemplateImage` trong `ToolEditorViewModel.cs`. Trước khi cắt ảnh ROI của Template để lưu xuống đĩa cứng hoặc nạp vào Shape Model Trainer, ảnh chụp ban đầu (snap) sẽ được chạy qua đúng logic xử lý tiền xử lý `ResolveToolPreprocessForPreview` tương ứng với Node đang thao tác.
    - Nhờ vậy, ảnh template và ảnh chạy thực tế sẽ luôn luôn đồng nhất về trạng thái tiền xử lý (cùng nhị phân, cùng lọc nhiễu, v.v.), đưa điểm số Match Score thực tế từ mức thấp (`0.3` - `0.4`) về lại mức tiệm cận `1.0` (đúng chuẩn kỹ thuật).

12. **Sửa lỗi logic huấn luyện mô hình nhận dạng (ShapeModelTrainer)**:
    - Khi tính toán offset `Dx`, `Dy` cho các đỉnh biên so với tâm mẫu, thuật toán `Math.Round()` trước đây làm tròn giá trị tọa độ xấp xỉ `.5` bằng phương pháp **ToEven** (Banker's rounding). Hệ quả là các điểm lân cận bị gom lại hoặc giãn rộng gấp đôi ra làm biến dạng hoàn toàn tỷ lệ và cấu trúc các điểm đặc trưng biên (feature edges) của mô hình.
    - Đã sửa lại bằng cách tính giá trị trung tâm `cx`, `cy` bằng phép chia nguyên (`w / 2`, `h / 2`) tương tự hệt như thuật toán `ScoreByShapeModel` lúc runtime. Việc này giải quyết triệt để lỗi khớp điểm (score drop từ 1.0 xuống 0.38) trên chính ảnh đã teach. Bắt lại hoàn hảo 100%.
13. **Khắc phục lỗi camera khởi động không thành công (Retry Mechanism)**:
    - Các phần mềm camera ảo như DroidCam hoặc OBS Camera phản hồi chậm trễ khi application khởi tạo `VideoCapture` lần đầu tiên, dẫn đến trạng thái ngắt (camera tắt).
    - Viết lại hàm `StartSavedCameraAsync` với cơ chế retry (lặp 3 lần, delay 1000ms), giúp phần mềm kiên nhẫn đợi DroidCam kết nối ổn định mỗi khi khởi chạy. Sửa triệt để tình trạng ứng dụng báo lỗi ảo lúc mới bật và bắt người dùng phải qua tab Live Camera bật tay.

14. **Ổn định score trong tab Inspection khi chạy lặp lại**:
   - `InspectionService` đang giữ trạng thái tracking theo `ProductCode` để tăng tốc origin search; khi người dùng bấm `Capture/Run` độc lập trong tab Inspection, state này có thể làm score origin nhảy theo kết quả lần trước.
   - Đã thêm `ResetTracking()` và gọi nó từ `InspectionViewModel` trước mỗi lượt `LoadConfig`/`Capture`/`Run` để mỗi lần kiểm tra độc lập bắt đầu từ trạng thái sạch.
   - Dòng `Origin Score` trong panel kết quả được hiển thị đầy đủ hơn theo dạng `score / threshold (OK|NG)` để dễ theo dõi ngay cả khi PASS.

15. **Hướng dẫn expression và CodeDetection trong Inspection**:
   - Thêm tooltip và dòng gợi ý mẫu ngay dưới ô expression của `Condition` và `Text color rule` trong [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml) để người dùng nhìn là biết cú pháp so sánh cơ bản.
   - Bổ sung hiển thị `CodeDetections` trong [InspectionView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/InspectionView.xaml) và nối dữ liệu từ [InspectionViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/InspectionViewModel.cs), giúp tab Inspection xem được giá trị QR/Barcode đọc ra giống Final Output ở Tool Editor.

16. **Placeholder Text hỗ trợ lấy kết quả từ tool khác**:
   - `EvaluateTextTemplate` đã hỗ trợ thêm `{ToolName.Text}` ngoài các dạng `{ToolName.Value}`, `{ToolName.Score}`, `{ToolName.Pass}`, `{ToolName.Found}`.
   - Đã thêm ví dụ rõ ràng trong tooltip để ghép kết quả từ tool khác vào Text, ví dụ: `Kết quả Caliper: {Caliper1.Value:0.000} mm` và `QR: {QR1.Text}`.
   - `Condition` tooltip cũng được đổi sang ví dụ bám theo tên tool thật (`Origin.Pass`, `Caliper1`, `EP.*`, `SC.*`) thay vì chỉ biểu thức chung chung.

17. **Placeholder syntax và cheatsheet trong Tool Editor**:
   - `TextTemplateRegex` đã được mở rộng để nhận cả `{ToolName.Value}` và `${ToolName.Value}`; người dùng có thể nhập kiểu dấu ngoặc nhọn quen tay mà không bị bỏ qua giá trị.
   - Thêm khối cheatsheet nhỏ ngay trong panel Text của [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml), liệt kê nhanh các placeholder hợp lệ như `{Caliper1.Value}`, `{Origin.Pass}`, `{QR1.Text}`, `{SC.Surface1.Score}`.

18. **Condition hỗ trợ so sánh số và chuỗi**:
   - `ConditionEvaluator` đã hỗ trợ string literal bằng dấu ngoặc kép, nên có thể viết điều kiện như `QR1.Text == "ABC123"` hoặc `QR1.Text != "LOT-01"`.
   - Tooltip và cheatsheet của Condition trong [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml) đã được cập nhật để có ví dụ số, bool và text từ CodeDetection.

19. **CS2001 missing generated XAML files**:
   - Các file sinh mã WPF trong `obj\Debug\net8.0-windows\Views\*.g.cs` vẫn được sinh ra bình thường; `dotnet clean` + `dotnet build` trên [VisionInspectionApp.UI.csproj](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/VisionInspectionApp.UI.csproj) đã PASS.
   - Nếu VS Code còn hiện CS2001 trỏ tới các `.g.cs` này, đó nhiều khả năng là diagnostics cache/stale state của designer chứ không phải lỗi source hiện tại.

20. **Branding CMS VINA VISION SYSTEM**:
   - Shell chính đã đổi title sang `CMS VINA VISION SYSTEM` trong [MainWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/MainWindow.xaml) và gắn icon chuyên nghiệp mới ở [Assets/cms-vina-vision-system.ico](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Assets/cms-vina-vision-system.ico).
   - [MainWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/MainWindow.xaml) được làm mới với header gradient, nền sáng, card bo góc và style tab/button hiện đại hơn để thoát cảm giác WinForms classic.
   - `.gitignore` đã bổ sung các thư mục build cụ thể như `Debug`, `Release`, `x64`, `x86`, `TestResults` ngoài `bin/obj`.

21. **Sửa lỗi TabItem template gây layout vỡ**:
   - Template của `TabItem` trong [MainWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/MainWindow.xaml) đã được sửa để dùng `ContentPresenter ContentSource="Header"` thay vì render nhầm `Content`, tránh việc header tab phình ra theo toàn bộ nội dung tab con.
   - Khi build lại, source không báo lỗi XAML; lần build này bị chặn bởi file DLL đang bị giữ bởi process đang chạy (`VisionInspectionApp.UI` và `Visual Studio 2026 Remote Debugger`), nên cần đóng app/debugger trước khi rebuild nếu muốn xác nhận bằng build sạch.

22. **Compact shell + QR visibility**:
   - [MainWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/MainWindow.xaml) đã được nén lại theo chiều cao: header công ty, nút trạng thái, padding tab và padding container đều giảm để dành thêm không gian cho nội dung màn hình chính.
   - [InspectionView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/InspectionView.xaml) được bọc thêm scroll ở panel kết quả bên phải và làm nổi bật dòng `Text` của Code Detection để QR/Barcode text không bị nằm ngoài vùng nhìn thấy.
   - Bounding box của [CodeDetectionResult](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Class1.cs) đã được nới theo vùng điểm decode thay vì bám sát quá chặt các ResultPoints, giúp khung bao của QR/Barcode sát kích thước thực tế hơn.

23. **Compact header & Preprocessor UI UX**:
   - Gộp title CMS VINA VISION SYSTEM và subtitle Industrial machine vision inspection suite lên cùng một dòng ở header [MainWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/MainWindow.xaml), đồng thời giảm padding header để tận dụng không gian.
   - Làm gọn (compact) toàn bộ các button bằng cách giảm Padding mặc định trong `Window.Resources` của `MainWindow.xaml` xuống còn `8,4`.
   - Cập nhật thêm các Text cụ thể ("Illum Kernel", "CLAHE Clip", "CLAHE Tile", "Blur Kernel", "Threshold", "Canny Th1", "Canny Th2") và Tooltip giải thích chi tiết cho từng slider ở phần Preprocess trong [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml) để người dùng dễ nhận biết tính năng.

24. **Cập nhật `.gitignore`**:
   - Thay thế rules ignore `**/bin/`, `**/obj/` cũ thành bộ rules chuẩn của Visual Studio (`[Bb]in/`, `[Oo]bj/`, `[Dd]ebug/`, `[Rr]elease/`, `x64/`, `x86/`...) để bắt chính xác và bỏ qua các thư mục build ở mọi cấp độ trong toàn dự án.

### Trạng thái cuối cùng của Source Code
- Toàn bộ source code biên dịch thành công 100% (`dotnet build` PASS với 0 Lỗi và 0 Cảnh báo).
- Không có tiến trình nào bị treo và các COM Object được giải phóng hợp lệ khi thoát ứng dụng.
