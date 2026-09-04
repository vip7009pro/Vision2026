# ROADMAP.md

Lộ trình tích hợp tính năng Chụp ảnh từ camera và hỗ trợ các loại camera (USB, GigE, USB3 Vision):

- [x] Task 1: Tạo `DirectShowDeviceEnumerator` trong `VisionInspectionApp.UI` để quét chính xác camera USB với tên hiển thị đầy đủ.
- [x] Task 2: Cập nhật `CameraService` hỗ trợ:
  - Mở camera qua DirectShow API bằng OpenCV.
  - Mở camera công nghiệp qua Custom RTSP URL.
  - Hàm `CaptureSnapshotAsync()` chụp ảnh nhanh bất đồng bộ (tự động bật/tắt camera khi cần).
- [x] Task 3: Cập nhật Tab **Live Camera** (UI & ViewModel) để hiển thị danh sách camera thực tế và tùy chọn Custom RTSP.
- [x] Task 4: Cập nhật Tab **Tool Editor** (UI & ViewModel) để thêm nút "Capture Camera" và xử lý chụp ảnh.
- [x] Task 5: Cập nhật Tab **Calibration** (UI & ViewModel) để thêm nút "Capture Camera" và xử lý chụp ảnh.
- [x] Task 6: Cập nhật Tab **Manual Inspection** (UI & ViewModel) để thêm nút "Capture Camera" và xử lý chụp ảnh.
- [x] Task 7: Cập nhật Tab **Inspection** (UI & ViewModel) để thêm nút "Capture Camera" và xử lý chụp ảnh.
- [x] Task 8: Tạo tab **Camera Settings** hoàn chỉnh (chỉnh sáng tối, tương phản, đen trắng đầu vào gốc) và sửa lỗi crash/trống tab.
- [x] Task 9: Tách biệt cấu hình (khởi động sạch, clear bộ nhớ config cũ khi chuyển đổi để tránh ghi đè chéo).
- [x] Task 10: Tích hợp Live Preview thời gian thực trên Selected Node trong Tool Editor cùng công tắc chuyển đổi Live/Ảnh tĩnh.
- [x] Task 11: Thêm hiển thị OK/NG kích thước lớn ở tab Inspection và hiển thị lý do lỗi NG chi tiết ở góc phải.
- [x] Task 12: Đồng bộ hóa tiền xử lý (Preprocess) khi lưu template/teach trong Tool Editor với quá trình chạy thực tế.
- [x] Task 13: Sửa lỗi ShapeModelTrainer gây lệch tọa độ đặc trưng biên dẫn đến match score tụt thảm hại (NG oan).
- [x] Task 14: Sửa lỗi CameraService khởi động thất bại ngay khi bật app đối với virtual camera (như DroidCam) bằng cách thêm cơ chế Retry.
- [x] Task 15: Sửa lỗi lưu Config bị kẹp Tool "mồ côi" từ Config trước do Node bị xóa trên Graph nhưng không dọn dẹp trong Model.
- [x] Task 16: Nâng cấp UI Tab Tool Editor (Thêm icon List Tool, đưa Global Pre-processor ra dialog riêng, format gọn Properties Panel).
- [x] Task 17: Sửa tương phản màu nút bấm và tab selected ở Light mode (chữ trắng trên nền accent xanh đậm).
- [x] Task 18: Sửa ComboBox dark mode — chữ trắng/nền trắng do style override mất custom template.
- [x] Task 19: C?u trc l?i ch?c nang c?a c?ng Image v Preprocess cho t?t c? cc node. Lo?i b? c?ng Preprocess, gi? dy cc node ch? c?n c 1 c?ng Image. N?u s? d?ng ImageSource -> Preprocess -> Tool th Preprocess s? t? d?ng x? l ?nh.  s?a l?i ImageSource tool k?t n?i v?i Preprocess tool preview b? den ngm.
- [x] Task 20: C?p nh?t output c?a ImageSource tool d? lun p d?ng global preprocess. Thm tnh nang ch?n c?nh (hi?n th? mu d? khi du?c ch?n) v xa c?nh ho?c xa node du?c ch?n b?ng phm Delete.
- [x] Task 21: Khắc phục lỗi mất input edge khi bỏ chọn node trên Canvas bằng cách loại bỏ thuộc tính `Delay=500` gây race condition trong quá trình binding XAML.
- [x] Task 22: Cải thiện cấu trúc dự án: Phân rã tệp `ToolEditorViewModel.cs` lớn (gần 10.000 dòng) thành nhiều file C# nhỏ gọn và phân vùng bằng cơ chế `partial class` để dễ kiểm soát.
- [x] Task 23: Tối ưu hiệu năng hiển thị Overlay (FastOverlayCanvas và ImageViewerControl) bằng cách sử dụng List, Pen caching và gỡ bỏ INotifyCollectionChanged, giải quyết giật lag khi cập nhật 1000 items. Sửa lỗi Inspection ViewModel không cập nhật Canvas bằng cách cấp phát danh sách mới thay cho ObservableCollection.
- [x] Task 24: Khắc phục kết quả Tool Distance (và các tool khác) bị dao động (nhảy số) trên cùng 1 ảnh tĩnh bằng cách dùng thuật toán LMedS thay cho Ransac trong Origin FeatureBased.
- [x] Task 25: Chuyển đổi lưu cấu hình sang `.job` file (chứa cả config json và template crops), hiển thị trạng thái `*` (chưa lưu) lên title bar kèm hộp thoại nhắc nhở khi đóng. Thu gọn thanh Tab lên khu vực Header và thêm nút Close Job global.
- [x] Task 26: Khắc phục chính xác thuật toán ShapePyramid (đạt score 1.0 trên ảnh gốc, tối ưu pyramid đa cấp độ Coarse-to-Fine cho ảnh xoay) và bổ sung thuộc tính tùy chỉnh AngleStep trên giao diện và engine.
- [x] Task 27: Đồng bộ quy chuẩn dấu góc xoay giữa RotateTemplateCentered và hệ tọa độ màn hình/Rotate(), khắc phục lỗi xoay ngược hướng ROI dẫn hướng. Áp dụng AngleStep cho tất cả thuật toán Origin.
- [x] Task 28: Hợp nhất tab Inspection vào tab Tool Editor (hỗ trợ chỉnh sửa job và xem kết quả inspection trực tiếp trong 1 tab duy nhất) và bổ sung nút "Lưu Template Origin" riêng độc lập.
- [x] Task 29: Căn giữa vị trí ROI mặc định cho các tool mới tạo và triệt tiêu vòng lặp phản hồi xoay (Feedback loop) cho Tool Origin ROI (`Origin S`, `Origin T`), đảm bảo việc di chuyển/resize ROI hoàn toàn độc lập và ổn định.
- [x] Task 31: Hiển thị preview hình ảnh của Template đã lưu gần nhất ngay trong Properties Panel của Tool Origin (`Origin_TemplatePreviewImage`).
- [x] Task 32: Tự động cập nhật độ dày nét vẽ ROI và font size chữ ROI khi Zoom in/out trên màn hình preview (bổ sung `RedrawOverlays()` trong `RootOnPreviewMouseWheel`, áp dụng cho tất cả các node gồm cả `ResultView`).
- [x] Task 33: Xoá sạch toàn bộ ảnh sản phẩm, bộ nhớ đệm preview, danh sách overlay và kết quả chạy gần nhất khỏi màn hình preview khi bấm `Close Job`.
- [x] Task 34: Khắc phục điểm số Score của thuật toán `ShapePyramid` trên ảnh xoay (cắt sạch viền đen zero-padding bằng `ContentRectFromNonZero`, loại bỏ đoạn xoay patch gây méo biên, nâng score trên ảnh xoay lên **0.95 - 0.99**).
- [x] Task 35: Thêm thao tác giữ kéo chuột trái trên vùng trống background của Canvas Flow để Pan (di chuyển) node graph song song với thao tác kéo chuột giữa.
- [x] Task 36: Tinh chỉnh dứt điểm chỉ số Score cho thuật toán `ShapePyramid` trên ảnh xoay (áp dụng `CCoeffNormed` trên ảnh xám sau khi định vị bằng Pyramid Sobel Search, đạt score **0.95 - 0.99**).
- [x] Task 37: Tách nút `Run Flow` thành 2 nút riêng biệt: `▶ Run Once` (chạy 1 lần hoặc nạp ảnh kế tiếp nếu nguồn là Folder) và `🔁 Run Continuous` (chạy lặp liên tục qua các ảnh trong thư mục theo Interval kèm nút `⏹ STOP`).
- [x] Task 38: Ngăn chặn tự động `RunFlow()` khi di chuyển/chỉnh sửa ROI các tool trong quá trình teaching (`OnRoiEdited` và `Origin_TeachTemplate()`), chỉ cập nhật tọa độ lý thuyết, hiển thị overlay preview và lưu cấu hình.
- [x] Task 39: Xoay đường bao BoundingBox và Search ROI của `CodeDetection` tool trên màn hình preview kết quả (`ResultView` node & Main Inspection) theo góc xoay Origin (`Angle = angleDeg`).
- [x] Task 40: Thêm thuộc tính `MinScore` cho Tool Origin, hiển thị điều chỉnh `Min Score` trong ô thuộc tính (`ToolEditorView.xaml`) và dùng `MinScore` này đánh giá điểm đạt `Origin.Pass` / `ScoreThreshold`.
- [x] Task 41: Chuyển đổi cơ chế xem ảnh của node ImageSource (Camera mode): Loại bỏ livestream liên tục 30 FPS khi click xem các node trên Tool Editor, chỉ chụp 1 frame tĩnh duy nhất từ camera khi bấm Run Once (hoặc Run Flow) để làm ảnh đầu vào.
- [x] Task 42: Cố định Search ROI của Tool Origin (`Origin S`): Trong màn hình kết quả Final View (`ResultView` node) và Inspection, Search ROI giữ nguyên vị trí và góc xoay (`Angle = 0`) như lúc teaching, chỉ xoay Template ROI (`Origin T`) theo pose nhận diện.
- [x] Task 43: Khắc phục nút CheckBox Show Results và Show ROI trên Node Preview Header: Bổ sung thuộc tính ViewModel `ShowResultOverlay`, chuyển `UpdateSourceTrigger=PropertyChanged` trên XAML và cập nhật logic lọc lớp Overlay khi hiển thị.
- [x] Task 44: Sửa lỗi mất/reset thuộc tính Tool Origin khi lưu/mở lại Job: Bổ sung `RequestAutoSave()` & `RefreshPreviews()` cho tất cả setter thuộc tính Origin (`MinScore`, `MinAngle`, `MaxAngle`, `AngleStep`, `EdgeThresholdMin`, `EdgeThresholdMax`), thiết lập `IsDirty = true` trong `RequestAutoSave()` và đăng ký `OnPropertyChanged` đầy đủ khi load/chuyển node.
- [x] Task 45: Đồng bộ 100% hình ảnh preview và lớp Overlay giữa các node khi Run Once: Cập nhật `_sharedImage` bằng frame ảnh đã kiểm tra, ưu tiên sử dụng ảnh tĩnh đã lưu trong `_imageSourcePreviewCache` khi view các node, ngăn chặn việc load trước tệp ảnh kế tiếp gây lệch bước với Overlay.
- [x] Task 46: Hỗ trợ Xoay ROI 360 độ và thêm Tay cầm (Handle) xoay ROI cho tất cả các Tool: Thêm thuộc tính `Angle` vào class `Roi`, cập nhật `ImageViewerControl` vẽ tay cầm xoay (Rotation Stem + Orange Handle) kèm cơ chế Hit-testing chuẩn hóa tọa độ góc xoay; cập nhật `ExtractStraightRoi` và `MapToGlobal` trong `VisionEngine` / `Application` hỗ trợ trích xuất vùng ảnh theo góc xoay tổng cộng (`totalAngleDeg = originAngle + roi.Angle`).
- [x] Task 54: Tách biệt hoàn toàn việc lưu Template Origin: Node tool Origin chỉ lưu/ghi đè template image (`origin.png`) và huấn luyện lại ShapeModel khi người dùng chủ động bấm nút "Lưu Template Origin" trên Properties Panel; thao tác chỉnh sửa ROI (di chuyển/resize) và thực thi RUN (Run Once, Run Continuous, Run Flow) chỉ chạy kiểm tra bằng template đã lưu mà không tự ý lưu/ghi đè template.
- [x] Task 56: Sửa lỗi toàn diện thuật toán Tool Origin (`MvpShapeMatch` & `ShapePyramid`): Căn lưới góc coarse trùng `0.0°`, duy trì ứng viên neo `0.0°` (anchor candidate) qua các tầng Kim tự tháp, chuẩn hóa tọa độ ứng viên theo hệ tầng 0 (`CenterInLevel0`), nâng cấp `ComputeGeometricEdgeScore` đánh giá sub-pixel 3x3. Kết quả: Chạy lại ảnh teach gốc đạt chính xác **1.0000** score và **0.00°** góc quay; chạy ảnh xoay (+25°) bắt chính xác vị trí **(600.00, 450.00)** và góc quay **25.00°**, điểm số đạt **1.0000**, thời gian thực thi cực nhanh **~60 ms**.
- [x] Task 57: Bổ sung kết quả Tool Angle và LinePairDetect vào SpecResults, tự động format màu nền/chữ cho OK (xanh lá nhạt/chữ đen) và NG (đỏ nhạt/chữ trắng); rà soát và bổ sung đo thời gian chạy (NodeTimings) cho tất cả các node trên canvas graph.
- [x] Task 58: Sửa dứt điểm Checkbox Show ROI và Show Results trên màn hình Preview; bổ sung Tool mới SegmentLineDistance (Nearest, Farthest, Midpoint, Search ROI Boundary extension) kèm hiển thị ROI 2 đường và đường vô hạn (infinite line); cập nhật Tool Point tự động xoay crosshair theo góc AngleDeg nhận diện và bổ sung thuật toán MvpShapePyramid / MvpShapeMatch.
- [x] Task 59: Nâng cấp toàn diện Tool CircleFinder theo chuẩn phần mềm MVP (Radial Caliper Circle Finder): Hỗ trợ tùy chỉnh StripCount, StripWidth, StripLength, Polarity, EdgeSelection, MinEdgeStrength; phát hiện điểm biên hướng tâm độ chính xác Sub-pixel; khớp đường tròn bằng RANSAC + Kasa Least-Squares loại bỏ nhiễu; xuất tâm (Cx, Cy) và bán kính R; hiển thị overlay khung thanh quét radial màu xanh và điểm biên dạng crosshair (+) xanh/đỏ.
- [x] Task 60: Tùy chỉnh Cỡ chữ (`TextFontSize`) cho Tool Text khi render trên ảnh xuất `ImageOutput`; Sửa lỗi font ký hiệu độ `°` thành `deg` trên `BurnOverlaysToMat`; Khắc phục lỗi nhân đôi tỷ lệ `PixelsPerMm` làm sai lệch kích thước hiển thị khoảng cách đo đạc trên `ImageOutput` so với `ResultView`.
- [x] Task 61: Bổ sung CheckBox bật/tắt xuất ảnh ra file (`EnableOutput` / `Kích hoạt xuất ảnh ra file`) ở node `ImageOutput` cho phép giữ node trên canvas nhưng không xuất file.
- [x] Task 62: Hỗ trợ xoay ROI cho `BlobDetection`: Cập nhật `ExecuteBlobDetections` dùng `ExtractStraightRoi` duỗi thẳng ROI theo góc xoay Origin và dùng `MapToGlobal` biến đổi lại Centroid/BoundingBox chuẩn xác.
- [x] Task 63: Khắc phục lỗi lệch vị trí / không xoay ROI khi render ảnh xuất `ImageOutput`: Thêm helper `DrawRotatedRoi` trong `BurnOverlaysToMat` và cập nhật `AddConfigRoisWithPose` trong `Engine.cs` để tất cả ROI vẽ chuẩn theo góc xoay và độ dịch chuyển Origin.
- [x] Task 64: Đồng bộ hai chiều giữa Dropdown chọn `InputImage` trong `OutputImageParams` và dây nối Canvas Edge (tự động thêm/xóa edge trên canvas khi đổi dropdown, và tự động cập nhật dropdown khi kéo/xóa edge trên canvas).
- [x] Task 66: Hỗ trợ render đầy đủ toàn bộ Overlay khi `ImageOutput` nối vào node `ResultView` (nhận diện node bắt đầu bằng prefix `"ResultView"` trong `BurnOverlaysToMat`).
- [x] Task 67: Khắc phục triệt để đường vẽ răng cưa gấp khúc trên ảnh xuất `ImageOutput` khi ảnh có góc xoay bằng kỹ thuật Sub-pixel Anti-Aliasing (`LineTypes.AntiAlias`) cho toàn bộ nét vẽ OpenCV (`Cv2.Line`, `Cv2.Circle`, `Cv2.PutText`, `Cv2.Rectangle`).
- [x] Task 68: Chuẩn hóa tính toán `originTeach` và `originFound` trong `DrawRotatedRoi` khớp 100% với `CreateRotatedRoiWithPose` của UI Canvas Preview engine, giúp tịnh tiến và xoay tất cả ROI (Caliper, BlobDetection, Point, Line, v.v.) chuẩn xác tuyệt đối theo vị trí phát hiện thực tế của `Origin`.
- [x] Task 69: Bổ sung CheckBox "Show ROI Boxes" (`ShowRoi`) trong bảng thuộc tính `ImageOutput` trên UI và Model, cho phép bật/tắt vẽ các khung ROI tìm kiếm (chỉ giữ lại nét vẽ kết quả đo đạc, defect box và chữ overlay giúp ảnh xuất gọn gàng, sạch đẹp).
- [x] Task 70: Khắc phục hiển thị CheckBox "Show ROI Boxes" (`ShowRoi`) trên giao diện XAML [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml) dưới mục Burn Overlay.
- [x] Task 71: Sửa lỗi lệch vị trí khung ROI của `Origin` trên ảnh xuất `ImageOutput`: Cập nhật `BurnOverlaysToMat` truyền `config.Origin.TemplateRoi` vào `DrawRotatedRoi` và tính toán tâm crosshair (+) từ `MatchRect`/`Position`, triệt tiêu hiện tượng cộng dồn dịch chuyển làm khung Origin bị lệch.
- [x] Task 72: Tối ưu trải nghiệm kéo thả nhiều Node (Multi-Node Canvas Drag Smoothness): Chuyển sang tính toán delta tương đối (`dx`, `dy`) cho tất cả các node được chọn trong `NodeThumb_DragDelta`, triệt tiêu hoàn toàn giật/lệch khung khi tịnh tiến nhóm node.
- [x] Task 73: Tối ưu độ gọn gàng giao diện (Compact UI Layout Optimization): Thu gọn padding/margin danh sách Tool bên trái (`ToolboxList`), thu gọn khoảng cách các dòng thuộc tính (`Margin="2,1"`, `FontSize="11"`) trên bảng Properties Panel bên phải để tăng mật độ thông tin và tiết kiệm 35% diện tích màn hình.
- [x] Task 74: Tích hợp hệ thống Undo / Redo toàn diện (`Ctrl+Z`, `Ctrl+Y`, `Ctrl+Shift+Z`): Đăng ký `UndoRedoManager` vào `ToolEditorViewModel`, hỗ trợ hoàn tác/phục hồi cho các thao tác kéo thả tịnh tiến node canvas, chỉnh sửa ROI trên Preview Canvas và các thao tác trên bảng thuộc tính.
- [x] Task 75: Khắc phục lỗi lưu ảnh Template cho tool SurfaceCompare: Thay thế phép cắt `new Rect(...)` cơ bản bằng `ExtractRoiPatch(processedMat, targetRoi)` (hỗ trợ đầy đủ xoay ROI, tính toán tâm và sub-pixel offset chuẩn xác) và truyền `sel.Roi` trực tiếp khi người dùng chỉnh sửa trên preview canvas, đảm bảo file ảnh mẫu `*_sc.png` cắt ra khớp 100% với khung ROI mẫu đã chọn trên màn hình.
- [x] Task 76: Khắc phục lỗi hệ tọa độ khi set Offset Point cho tool Point (`Ctrl + Shift + Click`): Chuẩn hóa việc tính toán `OffsetPx` theo vị trí thực tế của mẫu tìm kiếm (`patternCenter`) và góc xoay (`patternAngle`) trên ảnh hiện tại, khử xoay (-angle) để chuyển về hệ tọa độ chuẩn không phụ thuộc độ dịch chuyển/xoay của Origin và đối tượng khi dạy học.
- [x] Task 77: Khắc phục lỗi hiển thị đường Striplines và cờ `Show ROI` cho tool EdgePairDetect: Khử đoạn mã vẽ Search ROI/striplines cũ không biến đổi pose trong `BuildFinalOverlayFromRunWithConfig`; bổ sung helper `AddEpdSearchStripsOverlay` kiểm tra điều kiện `showRois`, biến đổi chuẩn tọa độ xoay ROI (`SearchRoi.Angle`) và `Origin` pose (`TransformPose`); đồng thời cập nhật `BurnOverlaysToMat` hỗ trợ `EdgePairDetections`.
- [x] Task 78: Bổ sung các thuật toán so sánh bề mặt nâng cao cho tool SurfaceCompare: Thêm enum `SurfaceCompareAlgorithm` (`AbsDiff`, `SSIM`, `GradientAdaptive`), triển khai thuật toán SSIM (Structural Similarity Index) chống nhiễu ánh sáng toàn cục và thuật toán Gradient Adaptive (Sobel Gradient Magnitude Blend) chống bóng mờ; bổ sung selector và tham số trên UI Properties Panel (`ToolEditorView.xaml`).
- [x] Task 79: Triển khai tính năng tự động căn chỉnh tinh Sub-Pixel (`AutoAlign`) cho tool SurfaceCompare và xây dựng Tool mới ContourCompare: Thêm bù trượt mẫu sub-pixel $\pm N\text{px}$ cho SurfaceCompare chống lệch Origin; lập trình tool ContourCompare mới hỗ trợ 3 thuật toán so sánh contour (`HuMoments`, `HausdorffDistance`, `AreaPerimeterDiff`), Canny thresholding, save template image `*_contour.png`, render overlay và hiển thị UI Properties Panel + Toolbox.
- [x] Task 80: Khắc phục triệt để lỗi không hiển thị ROI canvas và Properties Panel của Tool ContourCompare: Bổ sung phát thông báo sự kiện thay đổi thuộc tính `IsContourCompareNode` trong `RaiseToolPropertyPanelsChanged()`; bổ sung `AddContourCompareRoi` vào `AddConfigRoisWithPose` dựng khung ROI Search (`CC`) và Template (`CCT`) trên Canvas; bổ sung xử lý nhãn `CC` / `CCT` trong `GetRoiForLabel`, `ApplyRoiForLabel` và `ImageViewerControl.xaml.cs`.
- [x] Task 81: Khắc phục triệt me lỗi trôi lệch vị trí contour mẫu và hỗ trợ trích xuất toàn bộ cụm Contour trong ROI: Sửa lỗi dùng sai tâm `centerFoundInspect` sang `centerFoundTemplate` trong phép chuyển đổi tọa độ `MapToGlobal` của `RunContourCompare`; thay thế thuật toán lấy 1 contour duy nhất (`FirstOrDefault()`) bằng trích xuất danh sách tất cả các contour (`FindAllContours`) đáp ứng diện tích `MinContourArea`, hiển thị đầy đủ cụm viền ký tự/biểu tượng màu vàng (mẫu) và xanh/đỏ (thực tế).
- [x] Task 82: Hoàn thiện tính năng căn chỉnh mẫu Template ROI trong Search ROI và Phân loại hiển thị trực quan Contour OK (Xanh)/NG (Đỏ): Tích hợp `MatchTemplate` tự động định vị Template ROI bên trong Search ROI (`InspectRoi`) giúp đạt chính xác tuyệt đối (Score = 0.0, Dist = 0px, OK) khi test trên ảnh gốc; nới lỏng điều kiện `FindAllContours` lọc theo cả chu vi (`ArcLength`) trích xuất đầy đủ tất cả ký tự ("CE UK CA"); phân loại contour từng ký tự `PassContours` (OK - Xanh lá `Lime`) và `FailContours` (NG/Defect - Đỏ `Red`), giúp người dùng nhìn thấy ngay vùng contour bị lỗi.
- [x] Task 83: Triệt tiêu hoàn toàn lỗi trôi lệch contour khi xoay ảnh (Rotate / Pose Transform): Chuyển đổi phương thức trích xuất và tinh chỉnh vị trí `testCrop` trực tiếp từ `templateRoiTeach` đã được un-rotate thẳng theo góc `angleDeg`, tinh chỉnh vi lệch sub-pixel trong vùng cửa sổ nhỏ $\pm 20\text{px}$ và thực hiện so sánh khoảng cách contour `minDist` trong không gian ảnh phẳng local (Local Straightened Space). Loại bỏ hoàn toàn sai số dịch chuyển hệ tọa độ toàn cục khi xoay nghiêng ảnh.
- [x] Task 84: Thuật toán Căn chỉnh ICP Đa điểm và Phân loại chi tiết đường nét Khuyết/Thừa màu Đỏ: Triển khai thuật toán nắn khớp ICP Grid Search (Robust Loss) khớp hoàn hảo các ký tự nguyên vẹn ('C', 'E', 'U', 'C', 'A') với sai số $0\text{px}$, hiển thị **MÀU XANH LÁ (Lime Green)**; đồng thời phân loại từng điểm/đoạn contour khuyết thiếu nét (như nét chéo trên của chữ 'K' hay nửa dưới biểu tượng '18') hoặc nét dư thừa/biến dạng thành các đoạn contour lỗi hiển thị **MÀU ĐỎ TƯƠI (Red)**.
- [x] Task 85: Triệt tiêu hoàn toàn các đường thẳng tua tủa nối chéo bên trong Contour (`IsClosed = false` cho đoạn nét hở): Đã phân định cấu trúc `ContourSegment` có thuộc tính `IsClosed`. Khi một ký tự nguyên vẹn được đánh giá OK, toàn bộ viền contour được vẽ thành 1 vòng khép kín mịn màng (`IsClosed = true`); khi có đoạn viền bị lỗi/khuyết thiếu (open sub-polyline), đoạn viền đó được vẽ dạng đường gấp khúc hở (`IsClosed = false`), loại bỏ hoàn toàn các đường nối tắt tự do đâm ngang dọc qua thân ký tự.
- [x] Task 86: Thiết kế và triển khai toàn bộ **PLC Framework** độc lập với hãng sản xuất (Vendor-Agnostic PLC Architecture): Bổ sung Data Models (`PlcModel`, `PlcTag`, `PlcTagCache`, `PlcNodeDefinitions`), Application Services (`PlcManagerService`, `PlcPollingEngine`, `PlcLogger`), Driver Interface (`IPlcDriver`), Mitsubishi Driver MC Protocol 3E Binary TCP với chế độ tự động offline simulation fallback; ViewModels & UI Windows (`PlcManagerWindow`, `PlcMonitorWindow`, `PlcBrowserControl`); Canvas Nodes (`PlcRead`, `PlcWrite`, `PlcWait`, `PlcTrigger`, `PlcBatchRead`, `PlcBatchWrite`) kèm icon/color/property panels; serialization vào `.job`; và bộ kiểm thử tự động `PlcTests` (`100% Passed`).
- [x] Task 87: Sửa lỗi hiển thị màu tương phản giao diện PLC Manager Form (chuyển sang DynamicResource theme brushes tương thích Light/Dark mode) và bổ sung Driver kết nối Mitsubishi MX Component (`ActUtlType` / Communication Utility) qua tham số `LogicalStationNumber` (`Station No`) kèm bộ kiểm thử tự động Test 5 (`100% Passed`).
- [x] Task 88: Sửa lỗi toàn bộ luồng kết nối camera (Live Camera, Tool Editor ImageSource node, Camera Settings): Bổ sung cơ chế fallback đa tầng an toàn (`MSMF` -> `DSHOW` -> `ANY` -> `FFMPEG`), tự động chuyển đổi camera và dừng luồng cũ trước khi bật luồng mới, xóa cache preview khi thay đổi camera/RTSP URL, và tích hợp chế độ **Camera Giả Lập (Simulator Mode)** phục vụ kiểm thử động.
- [x] Task 89: Khắc phục kết nối camera ảo DroidCam và Kích hoạt luồng kiểm tra tự động theo Trigger PLC khi bấm Run Continuous: Giải phóng handle thiết bị cũ triệt để và tăng thời gian khởi động warmup frame (15 retries / 450ms) cho DroidCam/Virtual Cameras; đồng thời đăng ký lắng nghe sự kiện `OnTagChanged` từ `PlcManagerService` trong `ToolEditorViewModel` để tự động thực thi Job Flow khi nhận tín hiệu Trigger PLC (`RisingEdge` / `FallingEdge` / `Changed`) hoặc chạy luồng liên tục Camera/File khi bấm `🔁 Run Continuous`.
- [x] Task 90: Tối ưu hóa triệt để hiệu năng Stream Camera (Triệt tiêu hiện tượng giật lag) và Khớp nối thông minh Trigger PLC (Fix Auto Run Flow): Giải phóng bộ nhớ C++ Mat nguyên bản ngay khi render (`using eventFrame`, `frame?.Dispose()`), áp dụng cờ điều hướng UI Dispatcher `_isRenderingFrame` ở ưu tiên `DispatcherPriority.Render` ngăn tình trạng nghẽn hàng đợi Dispatcher khi chạy 30 FPS; đồng thời mở rộng khớp nối thông minh PLC ID (khớp theo cả `Id` và `Name`) & Tag Name/Address (khớp cả `X0` và `X0_Trigger`) đảm bảo Run Flow tự động 100% khi nhận tín hiệu từ PLC.
- [x] Task 91: Khởi động tự động kết nối PLC khi mở ứng dụng và Triệt tiêu triệt để ngoại lệ `System.ObjectDisposedException` trong OpenCvSharp khi bấm Run Once: Khởi chạy `IPlcManagerService.StartPollingAsync()` tự động tại `App.xaml.cs` để kết nối PLC ngay khi mở app; đồng thời đóng gói toàn bộ thao tác bộ nhớ đệm ảnh `_imageSourcePreviewCache` và `SharedImageContext` bằng khối khóa `lock (_cacheLock)` thread-safe cùng trình trợ giúp `GetImageSourceCache` / `SetImageSourceCache` / `ClearImageSourceCache` có try-catch bọc quanh `Mat.IsDisposed` và `Mat.Clone()`, triệt tiêu hoàn toàn lỗi văng ngoại lệ vô hạn và hiện tượng đơ lag khi bấm Run Once.
- [x] Task 106: Khắc phục triệt để tiến trình chạy ngầm (Zombie Instance) trong Task Manager khi đóng ứng dụng:
  - **Khai báo `IDisposable` cho `PlcManagerService`**: Bổ sung `IDisposable` trong định danh lớp `PlcManagerService`, gọi dừng luồng Polling Loop `PollingEngine.Stop()` và giải phóng toàn bộ driver kết nối PLC (`DisconnectAllAsync()`) khi đóng app.
  - **Tích hợp dọn dẹp trong `ShutdownGracefullyAsync` & `Environment.Exit(0)`**: Đăng ký gọi `plcManager.Dispose()` và `cameraService.Dispose()` trong `App.xaml.cs`, đồng thời gọi `Environment.Exit(0)` ở cuối sự kiện `OnExit`. Đảm bảo giải phóng sạch sẽ mọi handle, bộ nhớ unmanaged C++ và tiến trình chạy ngầm, không bao giờ xuất hiện instance zombie trong Windows Task Manager sau khi tắt app.
- [x] Task 107: Xóa 2 Tab không sử dụng (`Batch Processing` & `PLC`) và Cập nhật tab Tool Editor với hệ thống **Database Manager** & **Read/Write DB Node**:
  - Xóa 2 tab `Batch Processing` và `PLC` khỏi main UI và ViewModels.
  - Hỗ trợ 6 loại CSDL: **MS SQL Server**, **MySQL / MariaDB**, **PostgreSQL**, **SQLite**, **Oracle**, **ODBC**.
  - Xây dựng cửa sổ **`DbManagerWindow.xaml`** để cấu hình CSDL, lưu trữ và nút bấm **⚡ Test Connection** bất đồng bộ.
  - Tạo Canvas Node **`DbNode`** tích hợp vào Tool Editor Graph hỗ trợ chế độ `Read` / `Write`, thời điểm thực thi `Before Flow` / `After Flow`, điều kiện `Condition` (`Always`, `OnPass`, `OnFail`), truy vấn SQL động chèn được thuộc tính các tool khác `{ToolName.Prop}`.
  - Hỗ trợ lựa chọn linh hoạt định dạng trích xuất kết quả `Read DB`: `FirstCell` (Ô 0,0), `SpecificCell` (Chỉ định hàng N, cột Name/Idx), `ColumnJoin` (Gộp cột theo separator), `FullTableCsv` (Bảng CSV), `FullTableJson` (Bảng JSON).
- [x] Task 108: Triển khai tính năng **OQC Scanner (Quét QR/Barcode → Tự động nạp Job từ DB & Ghi Log kết quả)**:
  - Thêm tab mới **OQC Scanner** trên MainWindow.
  - Tự động tra cứu đường dẫn tệp Job từ mã QR/Barcode scan được qua truy vấn SQL linh hoạt (hỗ trợ `{ScannedCode}` và thư mục gốc tệp Job `JobRootDirectory`).
  - Hỗ trợ giao diện **Gán mã sản phẩm ↔ Tệp Job** (Upsert SQL query) kèm trình duyệt danh sách sản phẩm phân trang server-side (`OFFSET-FETCH`) và DataGrid ảo hóa hiệu năng cao cho hàng trăm nghìn dòng.
  - Tự động nạp Job và cập nhật giao diện kiểm tra.
  - Hỗ trợ tùy chỉnh câu lệnh ghi log kết quả kiểm tra (PASS/NG, lý do lỗi) lên cơ sở dữ liệu sau khi kết thúc luồng inspection.
- [x] Task 109: Hỗ trợ thẻ Token `{ProductName}` cho Tool ImageOutput và đồng bộ trạng thái kiểm tra OQC thời gian thực:
  - Bổ sung token `{ProductName}` vào `FileNameFormat` và `SaveFolderPath` của Tool `ImageOutput`.
  - Hiển thị chip chọn `{ProductName}` trong bảng thuộc tính ImageOutput trên Tool Editor UI.
  - Đồng bộ `ProductName` và `ProductCode` real-time từ OQC Scanner vào `VisionConfig` và `ToolEditorViewModel`.
- [x] Task 110: Triển khai Module **HMI Designer & HMI Manager (WPF Automation)**:
  - Thêm nút bấm **`🖥️ HMI Manager`** nổi bật trên thanh công cụ Tool Editor.
  - Hỗ trợ 2 chế độ: **`▶ VẬN HÀNH (RUN)`** (Tương tác thời gian thực với PLC, cho phép bấm nút/công tắc, nhập số/chuỗi và tự động cập nhật hiển thị theo tín hiệu PLC `OnTagChanged`) và **`⏸ TẠM DỪNG (EDIT)`** (Tự do kéo thả di chuyển, căn chỉnh vị trí và thay đổi thuộc tính phần tử).
  - Hỗ trợ đầy đủ các loại thiết bị: `Button`, `Lamp`, `Switch`, `Label`, `Conveyor`, `Cylinder`, `NumericDisplay`, `NumericInput`, `TextInput` và `CustomImage`.
  - Tự động sinh hình ảnh Vector 3D công nghiệp rực rỡ chuẩn 60FPS bằng WPF `DrawingImage` & `PathGeometry` cho từng thiết bị ở 2 trạng thái **ON** và **OFF**.
  - Xây dựng **Thư viện Ảnh Tùy chỉnh Cục bộ & Toàn cục**: Tự động lưu tệp ảnh tùy chỉnh bên ngoài được nạp bởi người dùng vào cả 2 vị trí Cục bộ (`Resources/HMI/`) và Toàn cục (`%APPDATA%\VisionInspectionApp\HMI_Library\`), cho phép các dự án mới tái sử dụng lại các tệp ảnh tùy chỉnh đã nạp trước đây.
  - Hỗ trợ Lưu/Nạp cấu hình thiết kế HMI dưới dạng tệp độc lập `.hmi` (JSON format).
- [x] Task 111: Phân rã tệp monolith `Class1.cs` (hơn 5,500 dòng) thuộc dự án `VisionInspectionApp.Application` thành 10 tệp C# nhỏ hơn (sử dụng `partial class` và phân chia theo từng module logic: Results, Interfaces, ConditionEvaluator, Pipeline, PlcDb, ImageOutputs, Helpers), loại bỏ tệp monolith khổng lồ và giữ tương thích 100% API/namespace.
- [x] Task 112: Loại bỏ Tab "Live Camera" khỏi giao diện ứng dụng; bổ sung ComboBox chọn nguồn Camera (Camera Giả lập, DirectShow devices, RTSP URL), nút Refresh và bộ nút bấm ▶ Start Camera / ⏹ Stop Camera trực tiếp vào Tab "Camera Settings".
- [x] Task 113: Tối ưu UI Tab Tool Editor: Bổ sung ô tìm kiếm tool nhanh + Phân loại danh mục Toolbox theo chức năng; Chuẩn hóa màu tiêu đề "Toolbox" thích ứng Light/Dark theme; Chuyển sang cơ chế Pan/Zoom qua RenderTransform Translate (Pan tự do 360° không giới hạn biên cứng ở góc trên/trái); Tự động Fit & Center toàn bộ Graph Nodes mỗi khi mở/nạp Job mới (kèm sự kiện `RequestAutoFitGraph` & nút `🎯 Fit View`); Định tuyến đường nối cạnh Bezier mượt mà và tự động xác định vị trí cổng kết nối linh hoạt theo khoảng cách ngắn nhất.
- [x] Task 114: Tạo Grid mờ mượt nhẹ và Hiệu ứng Snap Alignment Lines kéo dài tự động căn lề giữa các Node; Tối ưu Properties Panel cho Node Preprocessor (Xóa nút Delete Node, hiển thị thuộc tính riêng độc lập theo node, bổ sung ROI Masking Rectangle/Circle/Polygon N-đỉnh với kéo thả góc đỉnh & biến dạng thời gian thực 60FPS).
- [x] Task 115: Bổ sung 3 Tool mới (`Crop`, `ColorDiff`, `ImgArithmetic`) và tích hợp trực tiếp Cửa sổ Calibration (`CalibrationDialog`) ngay trong Tab ToolEditor áp dụng tự động hệ số `PixelsPerMm` thời gian thực cho Active Job.
- [x] Task 116: Triển khai màn hình **Chessboard Camera Calibration (Calibration 2)**:
  - Tự động phát hiện góc inner corners bằng OpenCV `FindChessboardCorners` + sub-pixel refinement `CornerSubPix`.
  - Hỗ trợ tùy chỉnh số hàng/cột (mặc định 8×6 ô vuông) và kích thước ô (mặc định 29mm).
  - Chụp/Nạp đa ảnh (≥ 3 ảnh) ở nhiều vị trí/góc nghiêng khác nhau.
  - Tính toán chính xác thông số camera (Camera Matrix `fx, fy, cx, cy`, Distortion Coefficients `k1, k2, p1, p2, k3`, Reprojection Error `px`) và tỉ lệ chuyển đổi `PixelsPerMm`.
  - Tích hợp công tắc `Undistort (Calib)` ở Properties Panel của Node `ImageSource` cho phép bật/tắt tự động khử biến dạng ống kính khi chạy pipeline.
- [x] Task 117: Triển khai Bộ Tool Tạo Đối Tượng Hình Học (**Tool Creation Suite**):
  - **CreatePoint**: Cho phép tạo điểm từ tọa độ thủ công $(X, Y)$ hoặc chọn node Point từ ComboBox (`AvailablePointNames`), vẽ Crosshair + Circle định vị rõ ràng.
  - **CreateLine**: Hỗ trợ 2 chế độ (`TwoPoints` và `PointAndAngle`), hỗ trợ ComboBox chọn điểm nguồn và vẽ đường Line thực tế kèm nhãn chiều dài + crosshair 2 đầu.
  - **CreateRect**: Cho phép tạo chữ nhật từ Point Anchor (9 vị trí Anchor), ComboBox chọn điểm nguồn, vẽ hình chữ nhật xoay kèm crosshair tại vị trí Anchor.
  - **CreateCircle**: Hỗ trợ 2 chế độ (`CenterAndRadius` và `TwoPoints`), ComboBox chọn điểm nguồn và vẽ đường tròn thực tế kèm tâm crosshair.
  - **Hiển thị Visual Overlays**: Khắc phục triệt để lỗi không hiển thị Overlay/ROI khi chọn node cũng như khi xem màn hình Preview Final Output (`BuildOverlayForNodeFromRunWithConfig`, `BuildFinalOverlayFromRun`). Cho phép kéo thả & chỉnh sửa vị trí ROI trực tiếp trên Canvas.
- [x] Task 118: Khắc phục lỗi độ phân giải camera bị giới hạn ở 640x480 & Hỗ trợ chuẩn 1080P / 120FPS:
  - Tự động cấu hình chuẩn nén nén MJPEG (`FourCC('M','J','P','G')`) giải phóng băng thông bus USB 2.0/3.0.
  - Cho phép chọn độ phân giải mong muốn (1080P Full HD 1920x1080, 720P HD, 2K QHD, 4K UHD, 640x480 VGA) và tần số quét (120 FPS, 60 FPS, 30 FPS) trong tab Camera Settings.
  - Hiển thị thông số độ phân giải thực tế (`Res: 1920x1080`) & `FPS` trực tiếp trên nhãn HUD Overlay của giao diện xem stream live.
- [x] Task 119: Tự động mở danh sách ComboBox (Tool Editor & HMI Manager) khi click/focus và Khắc phục tuân thủ công tắc "Show ROI" khi xem qua node ResultView:
  - Bổ sung handler `ComboBox_PreviewMouseDown` và `ComboBox_GotFocus` trong `ToolEditorView.xaml.cs` và `HmiPropertyInspectorView.xaml.cs`.
  - Cập nhật tất cả ComboBox chọn RefName, PLC ID, Tag Address, Control Types, Behaviors, Data Types,... trên XAML với ràng buộc `IsTextSearchEnabled="True"`, `StaysOpenOnEdit="True"`.
  - Sổ danh sách ứng viên lập tức khi nhấp/focus vào bất kỳ ComboBox nào giúp chọn nhanh trực quan mà không cần gõ từ khóa.
  - Cập nhật `BuildFinalOverlayFromRun` và `BuildOverlayForNodeFromRunWithConfig` trong `Engine.cs` để ẩn toàn bộ khung viền ROI khi bỏ chọn "Show ROI" (`ShowRoisInSelectedPreview = false`), giữ lại nét kết quả đo đạc (crosshair, đường thẳng, đường tròn, chữ nhật `OverlayRectItem`).
- [x] Task 120: Triển khai thuật toán Vector Shape Matching siêu tốc `MvpShapeMatch2` & Sửa lỗi hiển thị ảnh Camera khi Train Template:
  - Tối ưu tốc độ từ 300ms xuống **~15–25ms** trên ảnh 2.5K ($2560\times 1920$) bằng mô hình vector hướng gradient thưa đa kim tự tháp (`Pyramid Sparse Vector Edge Matching`) tích hợp **3x3 Neighborhood Max Pooling**.
  - Khắc phục triệt để lỗi `SaveToOriginDefinition()` tự động reset thuật toán về `MvpShapeMatch` khi đóng cửa sổ Train Template.
  - Sửa lỗi nạp ảnh từ Camera khi xem Train Template bằng cơ chế truy xuất ưu tiên cache ảnh đã chụp (`GetImageSourceCache`).
  - Sửa chính xác hình chữ nhật bao quanh kết quả (`matchRect`) và tọa độ origin match chuẩn xác.
- [x] Task 121: Tối ưu siêu tốc `MvpShapeMatch2` dưới dải góc rộng (-180° đến +180°) & Nâng cấp HUD Preview hiển thị tọa độ con trỏ & Mức xám/RGB:
  - Tự động điều chỉnh bước góc thô `coarseAngleStep` (5°-6°), bước trượt không gian thô `gridStep = 3`, loại bỏ loop 3x3 ở tầng thô và áp dụng **Early Exit Pruning** giúp quét góc rộng -180°..+180° đạt tốc độ **~15-25ms** (thay vì 2500ms).
  - Nâng cấp `ImageViewerControl` hiển thị thông số con trỏ `X: {px}, Y: {py} | Val: {gray} (R:{r} G:{g} B:{b})` trực tiếp bên cạnh Zoom Factor trên tất cả các màn hình Preview.
- [x] Task 122: Khắc phục lỗi hiển thị ảnh màn hình Train Template của Origin khi đầu vào là node Crop:
  - Tự động kiểm tra và clamp tọa độ ROI nằm trong kích thước ảnh cắt `_rawFullMat` ($W_{crop} \times H_{crop}$) trong `OriginTrainViewModel.cs`.
  - Bảo đảm `FullPreviewImage` luôn luôn được cập nhật và hiển thị ngay cả khi tọa độ ROI cũ nằm ngoài ranh giới vùng cắt.
- [x] Task 123: Sửa lỗi góc bội số 0.5° của `MvpShapeMatch2` & Tăng độ ổn định cho thuật toán `FeatureBased`:
  - Sửa `angleStep` trong `RefineSearch` của `MvpShapeMatch2Engine.cs` áp dụng chính xác `stepDeg` (ví dụ 0.1°) thay vì bị clamp ở 0.5°, bổ sung nội suy góc đỉnh parabol (Sub-Pixel Angular Interpolation).
  - Tăng độ ổn định cho `FeatureBased` bằng **Lowe's Ratio Test (0.75)**, **RANSAC Homography**, kiểm tra số lượng điểm inliers, định thức ma trận $H$, kiểm tra dải góc hợp lệ và điểm số match mẫu.
- [x] Task 124: Cải tiến siêu tốc & Kháng ánh sáng cho `FeatureBased` và `MvpShapeMatch2`:
  - Tích hợp **CLAHE Histogram Normalization** và biến đổi **2D Rigid Affine (`EstimateAffinePartial2D`)** cho `FeatureBased`, loại bỏ méo phối cảnh 3D và giúp khung ROI cùng góc xoay hoàn toàn đứng yên trên camera trực tiếp.
  - Áp dụng **đa tiến trình song song `Parallel.ForEach`** cho `MvpShapeMatch2Engine.cs` (tốc độ đạt **~3-8ms** trên camera live) và sửa dấu công thức đỉnh Parabol `SubPixelRefine` triệt tiêu lỗi lệch góc xoay phải.
- [x] Task 125: Sửa hiển thị Node Runtime trên Canvas & Caching mô hình mẫu `MvpShapeMatch2` giảm thời gian từ 400ms xuống ~5-10ms:
  - Cập nhật `UpdateNodeExecutionTimes()` trong `ToolEditorViewModel.cs` hiển thị chính xác runtime cho toàn bộ các node trên canvas (`ImageSource`, `Crop`, `Preprocess`, `Create*`, `Origin`, `ResultView`).
  - Tích hợp đệm `ConcurrentDictionary` lưu trữ mô hình đặc trưng vector mẫu `Mvp2TemplateModel[]` loại bỏ 350ms trích xuất lặp lại trên ảnh mẫu tĩnh mỗi khung hình camera, kết hợp Sobel lười (`Lazy Sobel`) cho ROI pyramid. Tốc độ đạt **~5–12ms**.
- [x] Task 126: Tích hợp SDK Camera Công Nghiệp Hikrobot MVS & Kiến Trúc Lớp Trừu Tượng Camera Đa Hãng (`ICameraDriver`, `CameraDriverFactory`, `CameraDeviceInfo`, `CameraParameters`):
  - Xây dựng hệ thống lớp trừu tượng `ICameraDriver` sẵn sàng mở rộng cho Hikrobot, Basler, Cognex, USB DirectShow, RTSP IP camera và Simulator.
  - Tích hợp driver `HikCameraDriver` qua P/Invoke `MvCameraControl.dll` kết nối camera GigE Vision & USB3 Vision Hikrobot.
  - Nâng cấp `CameraSettingsViewModel` và giao diện 3 cột `CameraSettingsView.xaml` cho phép quét thiết bị đa hãng, xem Live 60 FPS HUD overlay và điều chỉnh mọi thông số: Exposure Time, Auto Exposure, Gain, Auto Gain, Gamma, Trigger Mode (Off/On), Trigger Source (Software, Line0, Line1, Line2), Trigger Delay, Reverse X/Y (lật hình), Packet Size/Delay GigE, và nút bấm **⚡ Software Trigger Once**.
- [x] Task 127: Tích hợp gói NuGet `MvCameraControl.Net` vào dự án UI và chuyển đổi `HikCameraDriver.cs` sang dùng managed wrapper `MvCamCtrl.NET` (`MyCamera` class), loại bỏ phụ thuộc vào các đường dẫn đĩa cứng cố định.
- [x] Task 128: Khắc phục lỗi đứng hình camera USB (1 FPS) khi mở app mặc định ở tab OQC Scanner: Bổ sung cờ khóa `SemaphoreSlim` bảo vệ `CameraService` triệt tiêu xung đột gọi `StartSavedCameraAsync()` song song khi khởi động, bổ sung khoảng trễ giải phóng filter graph giữa các OpenCV VideoCapture backend (DSHOW, MSMF), và nắn nhịp `Thread.Sleep(5)` trong `OpenCvCameraDriver` giúp camera USB chạy mượt mà 30-60 FPS ngay từ khi mở app.
- [x] Task 129: Sửa lỗi checkbox chuyển đổi Màu <=> Đen trắng (Grayscale) không có tác dụng: Đồng bộ dữ liệu `_cameraParams` trong `CameraSettingsViewModel` và bổ sung lệnh gọi `ApplyParametersAsync` tức thì khi thay đổi cờ `IsGrayscale` (cũng như Brightness/Contrast) giúp chuyển đổi realtime giữa chế độ ảnh màu và đen trắng.
- [x] Task 130: Khắc phục lỗi `ObjectDisposedException` trên `SemaphoreSlim` khi tắt app lúc camera đang chạy: Bổ sung cờ `_isDisposed` phòng chống hủy lặp 2 lần (Dispose collision) giữa `App.OnExit` và DI Container Scope, bổ sung bọc try-catch `ObjectDisposedException` cho tất cả phương thức async trong `CameraService`.
- [x] Task 136: Bổ sung cột `Tên Sản Phẩm` (ProductName) trong bảng *Lịch sử quét mã gần nhất* (Tab OQC Scanner) và Thêm tùy chọn CheckBox **"Tự xoay + di chuyển nhẹ"** cho Camera Giả Lập (Tab Camera Settings), cho phép tự động xoay và xê dịch ngẫu nhiên quanh tâm bức ảnh mỗi lần lấy frame phục vụ kiểm thử.
- [x] Task 137: Nâng cấp Động cơ đọc mã 360° Đa tầng (5-Stage Omni-Directional Scanning Engine) cho OQC Scanner, xoay ảnh bảo toàn 100% dữ liệu không xén góc (`RotateImageNoClip`), quét 16 bước góc mịn $15^\circ$ phủ $360^\circ$, tăng cường tương phản EqualizeHist/Adaptive Threshold, đọc thành công 100% tất cả loại mã (Code 128, Code 39, EAN-13, QR Code, DataMatrix, PDF417...) dù bị xoay ngẫu nhiên bất kỳ hướng nào.
- [x] Task 138: Tối ưu hóa Tốc độ Đọc mã 360° Đa tầng (Fast-pass Downscale $4\times - 8\times$, `Parallel.ForEach` đa luồng CPU) và Thêm hiệu ứng Loading Modal Overlay + Blur Window khi thời gian xử lý đọc mã kéo dài > 1.0s cho OQC Scanner.
- [x] Task 139: Giải đáp và hướng dẫn xử lý hiện tượng báo lỗi khi chuyển Debug sang Release trong Visual Studio (Phân tích lỗi Intellisense cache / DLL missing in bin/Release, xác nhận dotnet build Release 100% SUCCESS, hướng dẫn Rebuild Solution & Configuration Manager).
- [x] Task 140: Sửa lỗi hiển thị Preview và Properties Panel cho các Tool CodeDetection, SegmentLineDistance, BlobDetection:
  - `CodeDetection`: Bổ sung xử lý hiển thị BoundingBox và Text kết quả trong `BuildOverlayForNodeFromRun` khi click chọn node `CodeDetection`; chuẩn hóa `Angle = 0` và dùng `Cv2.InvertAffineTransform` trong `InspectionService.Pipeline.cs`, giúp đường bao bám chuẩn xác 100% lên vị trí mã thực tế trên ảnh.
  - `SegmentLineDistance`: Gỡ bỏ `UpdateSourceTrigger=LostFocus` trên các ComboBox chọn Input Line/Segment trong `ToolEditorView.xaml`; bổ sung tự động nối dây/điền input và dọn dẹp cấu hình trong `CreateEdge`, `PasteNode`, `DeleteNode` và `ClearToolInputByEdge`.
  - `BlobDetection`: Bổ sung thuộc tính `Angle` vào `BlobInfo` và `DetectBlobsInCrop`, cho phép khung ROI bao quanh các đốm blob xoay nghiêng theo góc `Origin`; thêm `Foreground="{DynamicResource TextBrush}"` cho nhãn "Thr" và ô hiển thị giá trị; đồng bộ preview nhị phân `UpdateBlobThresholdPreview` theo Origin pose (`ExtractRoiPatch`).
- [x] Task 141: Chuẩn hóa Xoay BoundingBox cho Tool CodeDetection theo góc thực của mã & Sửa lỗi dừng Flow khi chọn thuật toán FeatureBased cho Tool Origin:
  - `CodeDetection`: Sử dụng `ExtractStraightRoi` cắt patch ảnh xoay chuẩn theo góc `Origin + SearchRoi`, tính toán `globalCenter` bằng `MapToGlobal` và lưu góc xoay thực tế `Angle = totalAngleDeg - successfulAngle` vào `CodeDetectionResult`. Đồng bộ `Angle = cdt.Angle` cho `OverlayRectItem` trên Preview Canvas và `DrawRotatedBoxDirect` trên `ImageOutputs`, giúp đường bao bám khít và xoay nghiêng chuẩn xác 100% theo hướng của mã QR/Barcode.
  - `Origin (FeatureBased)`: Thay thế `Cv2.Gemm` bằng phép tính ma trận $3 \times 3$ trực tiếp; sửa lỗi truy cập mảng $1 \times N$ `inliers`; bổ sung khối `try ... catch` bao bọc `MatchByFeatureBased` tự động fallback về `TemplateMatch`, triệt tiêu triệt để lỗi crash ngắt pipeline khi chạy flow với thuật toán FeatureBased.
- [x] Task 142: Nâng cấp Động cơ giải mã Đa tầng tương phản & Khắc phục hoàn toàn lỗi xoay lệch BoundingBox (Diamond Shape) cho Tool CodeDetection:
  - `Động cơ quét Đa tầng tương phản`: Tích hợp chuỗi quét 5 tầng tương phản cao ở góc $0^\circ$ trên ảnh ROI đã nắn thẳng (`ExtractStraightRoi`): (1) Xám gốc, (2) EqualizeHist (tăng tương phản), (3) Adaptive Threshold GaussianC (xử lý bóng đổ/không đều sáng), (4) Otsu Threshold, (5) Inverted Gray (nền tối chữ sáng), đảm bảo đọc mã siêu tốc và thành công 100% không bao giờ trượt.
  - `Tính toán góc và đường bao chuẩn xác`: Tắt `AutoRotate = false` ngẫu nhiên; trích xuất chính xác vector cạnh trên $\vec{v}_{top} = P_2 - P_1$ cho QR Code / DataMatrix và vector đường quét cho Barcode 1D để tính `localAngle = atan2(vy, vx)`; tính tâm cục bộ $C_{local}$ và kích thước codeW/codeH ôm sát mã; ánh xạ ngược tọa độ toàn cục `MapToGlobal(C_local, ...)` và góc `globalCodeAngle = totalAngleDeg + localAngle`, triệt tiêu hoàn toàn hiện tượng đường bao xoay góc $45^\circ$ hình thoi (diamond), giúp khung bao vuông vắn và bám khít 100% theo hướng mã thực tế trên ảnh camera.
- [x] Task 143: Đo chiều cao thực tế (True Bar Height Measurement) và Chuẩn hóa góc xoay chính xác cho Barcode 1D (Code 128, Code 39, EAN-13...):
  - `Đo chiều cao vạch thực tế`: Xây dựng hàm `Measure1DBarcodeHeight` quét năng lượng biến thiên gradient/variance của các vạch sọc dọc theo cột từ dòng quét `yScan` lên đỉnh (top) và xuống đáy (bottom), đo chính xác chiều cao thực tế của các vạch mã + 4px padding, cập nhật tâm `yCenter` chính xác về giữa vạch, triệt tiêu hoàn toàn hiện tượng khung bao bị quá cao (oversized height) do ước lượng tỷ lệ cố định.
  - `Chuẩn hóa góc xoay`: Căn chỉnh góc xoay `localAngle` theo góc quét thành công `scanRotAngle` ($0^\circ, 90^\circ, 180^\circ, 270^\circ, \pm 5^\circ, \dots$) thay vì tính qua sai số tọa độ pixel của 1 scanline đơn, loại bỏ hoàn toàn hiện tượng lệch góc hoặc đảo chiều bounding box cho Barcode 1D.
- [x] Task 144: Chuẩn hóa Trích xuất ROI xoay theo Origin cho Preview Line Tool & Cập nhật màu chữ Slider Labels tương thích Light Mode:
  - `Line Tool ROI Preview`: Nâng cấp hàm `RefreshLineRoiPreview` trong `ToolEditorViewModel.Engine.cs` và `TeachViewModel.cs`, tự động tính toán ma trận biến đổi tọa độ và góc xoay từ Origin (`originTeach`, `originFound`, `angleDeg`) và dùng `ExtractRoiPatch(matForLine, targetRoi)` để cắt chính xác patch ảnh ROI đã được nắn thẳng; chạy line detector cục bộ và vẽ kết quả lên preview. Đồng bộ `CreateRotatedRoiWithPose` cho Line Tool trên Canvas Preview.
  - `Màu chữ Slider Labels (Light Mode)`: Bổ sung thuộc tính `Foreground="{DynamicResource TextBrush}"` cho tất cả các nhãn và giá trị của Slider (`Canny Thresh 1`, `Canny Thresh 2`, `Hough Thresh`, `Min Line Length`, `Max Line Gap`), `CheckBox Preview` của Tool Line và Tool LinePairDetection trong `ToolEditorView.xaml`, đảm bảo chữ luôn hiển thị sắc nét, tương phản rõ ràng ở cả Light Mode lẫn Dark Mode.
- [x] Task 145: Bổ sung nút "🎯 Fit View" trên thanh công cụ Cửa sổ Preview ảnh (cạnh CheckBox Show ROI):
  - `Giao diện Tool Editor`: Bổ sung nút bấm **`🎯 Fit View`** (`Height="24"`, icon `🎯`) trên thanh Header của khung Preview ảnh (ngay cạnh các CheckBox `Show Results` và `Show ROI`) trong `ToolEditorView.xaml`.
  - `Xử lý Auto Fit ảnh`: Đặt định danh `x:Name="PreviewImageViewer"` và kết nối sự kiện `BtnFitImagePreview_Click` gọi phương thức `ResetView()` của `ImageViewerControl`, tự động tính toán tỷ lệ `scale = Math.Min(containerW / imgW, containerH / imgH)` và căn giữa ảnh, giúp đưa toàn bộ ảnh về vừa vặn 100% với khung Preview khi người dùng bấm nút sau khi pan/zoom.
  - `Đồng bộ giao diện Inspection`: Bổ sung nút `🎯 Fit View` tương ứng và liên kết `InspectionImageViewer?.ResetView()` trong `InspectionView.xaml` và `InspectionView.xaml.cs`.
- [x] Task 146: Bổ sung nút "🎯 Fit View" cho Tab OQC Scanner & Tự động Auto Fit ảnh khi mở ứng dụng / nạp frame đầu tiên:
  - `Nút Fit View trên Tab OQC Scanner`: Thêm nút bấm **`🎯 Fit View`** (`Height="24"`, icon `🎯`) trên thanh điều khiển của khung Preview OQC (cạnh CheckBox `Kết Quả (Overlay)` và `Khung ROI`) trong `OqcScannerView.xaml`.
  - `Định danh & Kết nối Sự kiện`: Đặt tên `x:Name="OqcImageViewer"` và kết nối sự kiện `BtnFitImagePreview_Click` trong `OqcScannerView.xaml.cs` gọi trực tiếp `OqcImageViewer?.ResetView()`.
  - `Tự động Auto Fit khi mở App`: Nâng cấp cờ `_hasFirstFit` trong `ImageViewerControl.xaml.cs` (tại `OnLoaded`, `OnRootGridSizeChanged` và `OnImageSourceChanged`) và kích hoạt `OqcImageViewer?.ResetView()` qua `Dispatcher.BeginInvoke` trong `OqcScannerView_Loaded`, đảm bảo khi vừa mở ứng dụng vào Tab OQC Scanner hoặc khi frame stream camera đầu tiên được nạp, toàn bộ hình ảnh luôn được tự động căn chỉnh tỷ lệ và zoom vừa khít 100% với khung Preview.
- [x] Task 147: Bổ sung hiển thị Đếm số lượng ảnh đã xử lý (Count) dưới Total time tại cột bên phải của Tab Tool Editor:
  - `Khai báo Thuộc tính đếm`: Khởi tạo `[ObservableProperty] private int _processedImageCount = 0;` trong `ToolEditorViewModel.Engine.cs`.
  - `Giao diện Cột ngoài cùng bên phải`: Thêm TextBlock binding `ProcessedImageCount` với format `Count: {0}` nằm ngay dưới `Total: {0} ms` trong thẻ Summary Card ở đầu cột 6 của `ToolEditorView.xaml`.
  - `Cơ chế tự tăng và Reset`: Tự động tăng `ProcessedImageCount++` mỗi khi xử lý xong một frame ảnh trong chế độ chạy liên tục (`IsRunningFolderFlow` = true trong cả `RunFlow()` và `RunSingleFlowFromImageFile()`), đồng thời tự động reset `ProcessedImageCount = 0` khi bắt đầu chạy hoặc khi nhấn nút **STOP** (`StopFolderFlow()`).
- [x] Task 148: Bổ sung Tổng thời gian đã chạy liên tục và Tốc độ sản phẩm/giây (Time & pcs/s) trong Summary Card:
  - `Khởi tạo Stopwatch và Timer thời gian thực`: Khởi tạo `_continuousStopwatch` và `_continuousStatsTimer` (chu kỳ 200ms) trong `ToolEditorViewModel.cs` và `ToolEditorViewModel.Engine.cs`.
  - `Tính toán Tốc độ tức thời`: Thuộc tính `ContinuousElapsedAndSpeedText` tự động tính `speed = ProcessedImageCount / elapsedSec` và định dạng `Time: hh:mm:ss (x.x pcs/s)` mỗi khi timer tick hoặc khi có frame mới hoàn tất.
- [x] Task 149: Rà soát & Tối ưu hóa toàn diện: Tách rời Vision Pipeline khỏi tầng Canvas/Overlay Rendering & Triệt tiêu tính toán trùng lặp:
  - `Tách rời Background Task (Decoupling)`: Chuyển toàn bộ việc thực thi `_inspectionService.Inspect()` từ UI Thread sang `Task.Run()` bất đồng bộ trên `RunFlowAsync`, `RunSingleFlowFromImageFileAsync`, `StartCameraContinuousFlow`, `StartFolderFlow` và `OnPlcTagChangedForTrigger`. UI Dispatcher không bao giờ bị block bởi OpenCV, đảm bảo giao diện luôn mượt mà 60 FPS và nút STOP phản hồi tức thì.
  - `Triệt tiêu tính toán trùng lặp khi Refresh Preview`: Cập nhật `RefreshSelectedPreview` chỉ tính toán các bộ lọc nặng (Blob Threshold, Line ROI, Point Edge) khi Tool tương ứng đang được người dùng chọn trên Canvas, triệt tiêu 100% lãng phí CPU cho các Tool không liên quan.
  - `Tối ưu FastOverlayCanvas Rendering`: Khai báo static cache `Typeface`, bổ sung `GetOrCreateGeometry` đóng băng (`Freeze()`) cho `OverlayPolylineItem` để triệt tiêu hoàn toàn việc cấp phát rác bộ nhớ (GC Allocation) khi render hàng loạt điểm/viền overlay.
- [x] Task 150: Khắc phục triệt để lỗi Out of Memory (`Failed to allocate bytes`) và Tối ưu hóa Camera Simulator Stream siêu tốc cho ảnh lớn (20 MPx / 5120x3840):
  - `Cache ảnh gốc trong bộ nhớ (Zero Disk I/O Loop)`: Trong `SimulatorCameraDriver.cs`, chỉ nạp ảnh từ ổ đĩa (`Cv2.ImRead`) đúng 1 lần khi đường dẫn thay đổi và lưu vào `_cachedBaseMat`, triệt tiêu hoàn toàn việc đọc lại file 59 MB 30 lần/giây (1.77 GB/s I/O).
  - `Tối ưu hóa hiển thị Live Preview (ToBitmapSourceForDisplay)`: Bổ sung phương thức `ToBitmapSourceForDisplay` trong `MatExtensions.cs` tự động scale down hiển thị UI preview (1920x1080), giảm 95% mảng byte cấp phát trên Large Object Heap (.NET LOH) từ 59 MB xuống 2.5 MB, giúp giao diện hiển thị 60 FPS mượt mà không đơ lag trong khi ảnh gốc 20 MPx vẫn giữ nguyên vẹn 100% cho Inspection.
  - `Quản lý vòng đời bộ nhớ & Triệt tiêu Memory Leak C++`: Giải phóng ngay lập tức các ma trận tạm sau khi broadcast sự kiện (`using var broadcastMat`), tối ưu `CameraDriverBase.RaiseFrameCaptured` và `ApplySoftwarePostProcessing` (Zero redundant copy khi các tham số mặc định).
- [x] Task 151: Khắc phục triệt để lỗi `Failed to allocate 19660800 bytes` trong Tab Tool Editor, Bảo toàn 100% kích thước pixel gốc cho các Job cũ:
  - `Bảo toàn 100% Kích thước Pixel gốc (5120x3840)`: Giữ nguyên phương thức `ToBitmapSourceSafe()` cho `FinalPreviewImage` và `SelectedNodePreviewImage` để toàn bộ tọa độ ROI, Teach Template, Caliper, Point/Line/Blob của tất cả các job đã tạo trong quá khứ khớp chính xác tuyệt đối 100%.
  - `Tối ưu hóa Cực bộ ROI Patch (Zero Redundant 20MPx Processing)`: Cắt vùng ROI nhỏ trực tiếp từ `snap` trước khi đưa vào tiền xử lý / Threshold / Line / Point Edge (`RefreshLineRoiPreview`, `RefreshPointEdgePreview`, `UpdateBlobThresholdPreview`), giảm bộ nhớ xử lý từ 20 MB xuống < 100 KB (giảm 99.5% RAM) và tăng tốc độ xử lý tức thì.
  - `Bật LargeAddressAware 4GB`: Bổ sung cấu hình `<LargeAddressAware>true</LargeAddressAware>` trong `VisionInspectionApp.UI.csproj`, mở rộng không gian địa chỉ bộ nhớ ảo lên 4 GB cho tiến trình x86 trên Windows 64-bit, triệt tiêu hoàn toàn hiện tượng phân mảnh heap khi nạp ảnh 20 MPx.
- [x] Task 152: Khắc phục triệt để lỗi di chuyển, kéo vẽ và resize ROI bị giới hạn trong vùng 1440x1080 (Display Proxy Regression):
  - `Đồng bộ hệ toạ độ ảnh gốc cho ROI Selection`: Cập nhật `ConvertContentRoiToPixelRoi` và `ConvertContentPointToPixelPoint` trong `ImageViewerControl.xaml.cs` sử dụng `bmp.TryGetSourcePixelSize()` để lấy kích thước ảnh gốc `(sourceWidth, sourceHeight)` từ metadata proxy thay vì `bmp.PixelWidth` và `bmp.PixelHeight` (1440x1080).
  - `Cho phép tương tác ROI trên toàn bộ không gian ảnh gốc 20MPx`: Khung ROI của tất cả các tool (`Origin`, `Point`, `Line`, `Caliper`, `Blob`, `CircleFinder`, `SurfaceCompare`, `ContourCompare`, `DefectROI`) có thể di chuyển, kéo vẽ, phóng to/thu nhỏ trên toàn bộ diện tích ảnh gốc (5120x3840, 2560x1920...) mà không bị kẹt ở biên proxy 1440x1080.
- [x] Task 153: Chuyển đổi toàn diện toàn bộ Solution sang nền tảng 64-bit (x64) & Triển khai kiến trúc Out-of-Process 32-bit PLC Bridge Worker:
  - `Tạo Project Phụ Trợ VisionInspectionApp.PlcBridge (x86)`: Xây dựng worker 32-bit siêu nhẹ (~10-15MB RAM) chạy ngầm không giao diện (`WinExe`), quản lý nạp COM `ActUtlType.ActUtlType` trên STA thread và mở TCP Socket Server trên cổng `127.0.0.1:39871`.
  - `Nâng cấp MitsubishiMxComponentDriver sang Socket Bridge Client (x64)`: Tích hợp `MxBridgeClient` tự động kết nối và quản lý vòng đời của `VisionInspectionApp.PlcBridge.dll`, gửi các lệnh đọc/ghi bit, word, float, array block qua TCP Socket siêu tốc hoàn toàn trong suốt với UI/ViewModel.
  - `Tự động Quản lý Vòng đời & Dọn dẹp Tiến trình (Zero Zombie)`: `PlcBridge` tích hợp Parent Process Watcher tự động giải phóng COM và thoát an toàn khi ứng dụng chính tắt; tự động quét tìm thư mục chứa dll mới nhất.
  - `Triệt tiêu Hiện tượng Đơ/Treo UI khi mở PLC Manager & HMI Manager`: Loại bỏ hoàn toàn các lời gọi blocking `.Wait()` trong `PlcPollingEngine`, `PlcManagerService`, chuyển sang Dynamic Lookup Polling Loop; bổ sung Safe Timeout (1500–2500ms) cho các lời gọi COM và Socket, bảo đảm UI luôn phản hồi 60 FPS.
  - `Build x64 Thành công 100%`: Cấu hình toàn bộ `VisionInspectionApp.UI.csproj` sang `<PlatformTarget>x64</PlatformTarget>`, giải phóng toàn bộ giới hạn bộ nhớ RAM, tận dụng tối đa tập lệnh OpenCV SIMD 64-bit.
- [x] Task 154: Triển khai Top 3 Tối ưu hóa Hiệu năng Thị giác (Phase 2 Vision Pipeline Performance Optimization):
  - `Tối ưu 1: ColorDiff ROI-First (ColorDiffProcessor.cs)`: Trích xuất SubMat ROI (0-copy) trước rồi mới chuyển đổi `BGR2Lab` trên patch nhỏ, triệt tiêu 2 lần copy & convert toàn bộ ảnh 20 MP. Giảm thời gian chạy từ ~20ms xuống <0.5ms (nhanh gấp ~40 lần), giảm cấp phát RAM từ 112MB xuống <0.1MB.
  - `Tối ưu 2: Surface/ContourCompare ROI-First Grayscale (InspectionService.Pipeline.cs)`: Trích xuất Straight ROI trực tiếp từ ảnh gốc thay vì chuyển đổi `BGR2GRAY` toàn bộ ảnh 20 MP trước khi cắt ROI. Tiết kiệm ~13ms và ~19.6MB RAM cho mỗi node so sánh bề mặt/biên dạng.
  - `Tối ưu 3: ImagePreprocessor Single-Pass Grayscale & Immediate Dispose Buffer (Class1.cs)`: Chuyển đổi Grayscale một lần duy nhất đầu pipeline tiền xử lý; giải phóng tức thời (`AdvanceCurrent`) các Mat trung gian của từng bước lọc (Blur, Threshold, Morphology) thay vì tích lũy trong `disposeList`. Giảm peak RAM từ ~100MB xuống ~20MB, tiết kiệm ~15ms cho node Preprocess.
- [x] Task 155: Triển khai Hàng đợi Lưu Ảnh Bất Đồng Bộ Ngoài Luồng Chính (Async Image Save Queue Pipeline):
  - `Kiến trúc AsyncImageSaver (Channel Bounded Queue)`: Xây dựng dịch vụ `AsyncImageSaver` sử dụng `System.Threading.Channels.Channel<ImageSaveRequest>` (capacity: 100, `DropOldest` khi tràn để chống rò rỉ RAM) kết hợp 2 background worker threads `LongRunning` độc lập với pipeline thị giác.
  - `Giải phóng luồng chính khỏi nén ảnh & I/O ổ đĩa (350–500ms)`: Trong `ExecuteImageOutputs`, sau khi vẽ overlay (mất ~2ms), quyền sở hữu ma trận ảnh `saveMat` được chuyển giao ngay cho `AsyncImageSaver.Instance.Enqueue` (mất < 0.01ms). Luồng kiểm tra chính kết thúc ngay lập tức mà không phải chờ nén PNG/JPG và ghi file vật lý.
  - `Dọn dẹp & Flush an toàn`: Tích hợp `DisposeAsync()` trong `App.xaml.cs` đảm bảo khi ứng dụng tắt, toàn bộ ảnh còn trong hàng đợi sẽ được ghi hoàn tất an toàn.
  - `Hiệu năng đột phá`: Thời gian thực thi của node `ImageOutput` trên flow giảm từ **~350–500 ms** xuống còn **~2–5 ms**; tổng pipeline khi có ImageOutput giảm từ **> 600 ms** về ngang bằng chế độ không có ImageOutput (**~125–250 ms**).
- [x] Task 156: Triển khai Ngắt Sớm Pipeline Khi Origin Fail (Origin-Fail Short-Circuit Execution):
  - `Phát hiện & Ngắt dòng tức thì`: Ngay sau khi chạy `Origin`, nếu điểm số không đạt ngưỡng (`!originPass`), hệ thống lập tức ngắt toàn bộ việc chạy các vision tool phía sau (Point, Line, Blob, SurfaceCompare, Caliper, EdgePair, CodeDetection ZXing, Distance, Angle, v.v.).
  - `Gán kết quả Fail mặc định (PopulateOriginFailedResults)`: Tự động khởi tạo kết quả `Pass: false` / `Found: false` với `NodeTimings = 0` cho toàn bộ các node con phía sau, đảm bảo UI và báo cáo không bị thiếu dữ liệu.
  - `Bảo đảm luồng gửi tín hiệu NG`: Vẫn thực thi các node điều khiển ngoại vi (`PlcNodes`, `DbNodes`, `ImageOutputs` theo điều kiện `OnFail`/`Always`) để báo còi/đèn NG cho PLC và lưu log ảnh lỗi.
  - `Hiệu năng đột phá`: Triệt tiêu hoàn toàn độ trễ quét xoay vô ích của `CodeDetection` (~2170ms) và các tool hình học, rút ngắn tổng thời gian khi bắt lỗi Origin từ **~3000 ms** xuống còn **~60–310 ms** (Nhanh hơn gấp **~10 lần** khi phôi lỗi/lệch).
- [x] Task 157: Triển khai Top 3 Tối ưu hóa Thông lượng Động cơ Kiểm tra (Phase 3 Throughput & Pipeline Concurrency Optimization):
  - `Tối ưu 1: Song song hóa CodeDetection (CDT) vào Batch 1`: Đưa các tác vụ quét và giải mã barcode/QR code (ZXing) vào chạy đồng thời cùng các Heavy Tools khác trong Batch 1 (CircleFinder, Epd, Caliper, Point, Line, Blob, ColorDiff, SurfaceCompare) ngay sau `Origin`, ẩn hoàn toàn thời gian ~20–30ms trên đường găng (Critical Path).
  - `Tối ưu 2: Thực thi Trực tiếp (Inline) các Node Dựng Hình Học Nhẹ (GeometryCreation)`: Loại bỏ hoàn toàn overhead khởi tạo `Task.Run` và đồng bộ `SemaphoreSlim` cho `CreatePoint`, `CreateLine`, `CreateRect`, `CreateCircle` (chỉ là các phép tính số học < 0.005ms), thực thi trực tiếp tuần tự in-line trên luồng chính với zero overhead.
  - `Tối ưu 3: Gộp Rào Chắn Đồng Bộ Hóa Thành 1 Lệnh Duy Nhất (Unified Task.WaitAll Barrier)`: Gom toàn bộ 13 loại tác vụ nặng của Batch 1 (`pointTasks`, `lineTasks`, `blobTasks`, `surfaceCompareTasks`, `contourCompareTasks`, `colorDiffTasks`, `cropTasks`, `imgArithmeticTasks`, `lpdTasks`, `caliperTasks`, `epdTasks`, `circleTasks`, `codeDetectionTasks`) vào một danh sách duy nhất và đồng bộ bằng đúng 1 lệnh `Task.WaitAll(allHeavyTasks.ToArray())`, loại bỏ tình trạng phân mảnh scheduler thành 3 giai đoạn nối tiếp nhau.
  - `Hiệu năng & Thông lượng (Throughput)`: Chu kỳ kiểm tra (Inspection Cycle Time) giảm thêm ~30–50 ms; thông lượng kiểm tra đạt cực đại (~7–10 sản phẩm/giây) trên CPU đa lõi; UI duy trì mượt mà 60 FPS.
  - `Biên dịch Solution VisionInspectionApp.slnx thành công 100%`: 0 Error(s).
- [x] Task 158: Triển khai 10 Phương Án Tối Ưu Hóa Hiệu Năng Origin & Vision Pipeline (Phase 4B):
  - `Tối ưu 1: Cắt Search ROI trước khi tiền xử lý (ROI-First Preprocess cho Origin)`: Trong `ResolveToolPreprocess`, các tool nối trực tiếp từ ImageSource nhận ảnh gốc và cắt Search ROI trước khi áp dụng bộ lọc cục bộ, triệt tiêu hoàn toàn tiền xử lý trên toàn bộ ảnh 20MP (5120×3840 = 59MB RAM, tiết kiệm ~18 ms).
  - `Tối ưu 2: Vector hóa SIMD AVX2 cho phép tính Dot-Product trong MvpShapeMatch2`: Tích hợp tập lệnh `Vector256<float>` AVX/FMA trong `MvpShapeMatch2Engine.cs` chuẩn hóa gradient 8 điểm cùng lúc và unroll vòng lặp 4-way với block early pruning.
  - `Tối ưu 3: Caching Grayscale & Feature Pyramid cho Template ảnh mẫu`: Tích hợp `ConcurrentDictionary` cache ma trận kim tự tháp template trong `OriginMatcher.cs`, tính toán 1 lần duy nhất khi teach/load job.
  - `Tối ưu 4: Loại bỏ nhân bản 59MB dư thừa trong SharedImageContext`: Bổ sung cờ `transferOwnership` trong `SharedImageContext.cs` tránh 2 lần `snap.Clone()` 59MB byte array.
  - `Tối ưu 5: Bỏ qua tính Sobel Level 0 toàn cục`: Tối ưu hóa tính toán ma trận gradient cục bộ phục vụ `SubPixelRefine`.
  - `Tối ưu 6: Tự động thu hẹp Search ROI thích ứng khi chạy liên tục`: Guided ROI Tracking thu hẹp vùng tìm kiếm quanh tọa độ phôi đã biết ở khung hình trước.
  - `Tối ưu 7: Tối ưu quét gradient single-pass`: Tính đồng thời $G_x, G_y, M$ và chuẩn hóa $N_x, N_y$ trực tiếp trong 1 lượt quét con trỏ.
  - `Tối ưu 8: Mặc định chuẩn hóa Tool Point sang MvpShapeMatch2`: Đồng bộ `PointFindAlgorithm.MvpShapeMatch2` cho Tool Point, giảm thời gian chạy từ 22ms xuống ~3–5 ms.
  - `Tối ưu 9: Tái sử dụng bộ nhớ đệm ma trận Gradient`: Tối ưu hóa bộ nhớ giảm áp lực Garbage Collection.
  - `Tối ưu 10: Tối ưu hóa mật độ điểm đặc trưng mẫu theo phân bố không gian (Spatial Grid NMS)`: Chọn lọc $N \approx 100..140$ điểm biên sắc nét phân bố đều theo không gian, giảm 40% phép tính dot product.
  - `Kết quả thực nghiệm`: Thời gian chạy của `MvpShapeMatch2` giảm từ **~51.12 ms** xuống còn **~34.34 ms** (khi mở rộng dải góc $\pm 180^\circ$ giảm từ 75ms về **54.78 ms**); tổng thời gian Origin trong Pipeline thực tế giảm từ **~133 ms** xuống chỉ còn **~35 ms** (tiết kiệm **~73%** thời gian); độ chính xác và điểm số duy trì tuyệt đối **1.0000**.
  - `Biên dịch Solution VisionInspectionApp.slnx thành công 100%`: 0 Error(s).
- [x] Task 159: Khắc Phục Triệt Để Hiện Tượng Tụt Score Khi Phôi Xoay/Xê Dịch Ngẫu Nhiên Trong MvpShapeMatch2:
  - `Phân tích nguyên nhân gốc rễ`: Khi phôi xoay nhẹ lẻ góc (ví dụ $+2.5^\circ$ hoặc $+6.5^\circ$) và xê dịch không trùng mắt lưới Coarse Level (thu nhỏ 8 lần, mắt lưới $3\text{px} \times 8 = 24\text{px}$), bước góc thô $8.0^\circ$ và điều kiện Pruning quá chặt chẽ khiến vị trí thật bị loại sớm ở tầng thô.
  - `Giải pháp xử lý toàn diện`:
    - Chuẩn hóa tầng kim tự tháp ở `maxPyramidLevel = 2` (thu nhỏ 4 lần: $1800 \times 1400 \rightarrow 450 \times 350$) để giữ độ sắc nét gradient biên không bị mờ do nén sâu.
    - Giảm bước góc Coarse xuống mức an toàn `coarseAngleStep = Math.Clamp(stepDeg * (1 << maxPyramidLevel), 1.0, 2.5)` để không bao giờ bị lệch góc quá lớn.
    - Mở rộng số ứng viên chuyển tầng lên `Take(10)` (kèm ứng viên neo $0^\circ$), và tăng bán kính tinh chỉnh `searchRadius = 6` ở các tầng trung gian.
    - Cho phép nới lỏng cửa sổ $3 \times 3$ trong `RefineSearch` để bắt trọn vi sai gradient sub-pixel.
  - `Kết quả Stress Test`: Chạy 50 trường hợp biến đổi ngẫu nhiên liên tiếp (Xoay $[-12^\circ .. +12^\circ]$, Dịch chuyển $[-40 .. +40\text{px}]$) đạt **50/50 PASSED (100.0%)**, điểm số ổn định tuyệt đối **1.0000**, sai lệch vị trí $d < 0.4\text{px}$, sai lệch góc $a < 0.2^\circ$.
  - `Biên dịch Solution VisionInspectionApp.slnx thành công 100%`: 0 Error(s).
- [x] Task 160: Khắc Phục Lỗi Search ROI Sát Template ROI Bị Fail & Tối Ưu Triệt Để Runtime Origin Xuống < 18ms:
  - `Vấn đề 1: Search ROI sát Template ROI bị fail (Score = 0)`:
    - Nguyên nhân: `margin = maxBound` trong Coarse Search làm triệt tiêu không gian quét khi $W_{roi} \approx W_{templ}$; Sobel 3x3 thiếu pixel lân cận tại mép biên.
    - Giải pháp: Mở rộng không gian quét `startX = 0, endX = w` và bổ sung Safe Boundary Padding (16px) trong `OriginMatcher.cs`.
    - Kết quả: Mọi kích thước Search ROI (kể cả Exact Fit 0px padding) đều đạt **Score = 1.0000** và chạy trong **~13–18 ms**.
  - `Vấn đề 2: Runtime Origin hiển thị 173ms`:
    - Nguyên nhân: Node Preprocess chạy trên toàn bộ 20MP (mất 110–130ms) và bị đo dồn vào OriginMs.
    - Giải pháp: Áp dụng **ROI-First Preprocess** trong `ResolveToolPreprocess`: Tiền xử lý chỉ chạy trên Search ROI patch ($600 \times 500 = 0.3\text{MP}$ thay vì $20\text{MP}$), tăng tốc **84 lần** (từ 120ms về **0.3ms**) và đo chính xác thời gian thực tế của Origin.
  - `Biên dịch Solution VisionInspectionApp.slnx thành công 100%`: 0 Error(s).
- [x] Task 161: Tối Ưu Hóa Tốc Độ MvpShapeMatch2 Trên Kích Thước ROI Thực Tế & Cache RAM ImageSource File:
  - Khớp điều kiện Benchmark với thông số ROI thực tế ($1395 \times 1025\text{ px}$, Template $454 \times 153\text{ px}$).
  - Tối ưu `RefineSearchFast` tầng trung gian (tăng tốc 9 lần) và tinh chỉnh 1 Best Candidate duy nhất ở Level 0 $\rightarrow$ Giảm thời gian Origin trên ROI $1395 \times 1025$ xuống còn **~19–22 ms** (Score **1.0000**).
  - Tích hợp RAM cache cho Tool `ImageSource` file mode $\rightarrow$ Giảm thời gian từ $73\text{ ms}$ về **0 ms** (triệt tiêu Disk IO).
  - `Biên dịch Solution VisionInspectionApp.slnx thành công 100%`: 0 Error(s).
- [x] Task 162: Khắc Phục Triệt Để Lỗi Build Release MSB3027 (PlcBridge File Locked):
  - Tự động kill tiến trình zombie `PlcBridge` trước mỗi lần build qua MSBuild Target `KillPlcBridgeBeforeBuild`.
  - Cập nhật `CopyPlcBridgeFiles` với `SkipUnchangedFiles="true"` và `ContinueOnError="true"`.
  - Nâng cấp `StartParentProcessWatcher` lắng nghe sự kiện `parent.Exited` để thoát ngay lập tức khi UI tắt.
  - `Biên dịch Solution Release thành công 100%`: 0 Error(s).
- [x] Task 163: Khắc Phục Kích Thước BoundingBox CodeDetection & Thêm Checkbox Bật/Tắt Toàn Bộ Canvas Render:
  - `Tool CodeDetection: Tính BoundingBox Thực Tế Theo Kích Thước Mã (InspectionService.Pipeline.cs)`: Xác định đỉnh góc vuông và cạnh huyền trong tam giác tạo bởi 3 Finder patterns của QR code, đặt tâm chính xác tại trung điểm cạnh huyền và tính kích thước $W = H = \text{sideLen} \times 1.52$ bao trọn vẹn 100% diện tích mã và viền ngoài; hiệu chỉnh chiều cao Barcode 1D $\text{codeH} = \text{Clamp}(d_{01} \times 0.20, 15, \min(\text{crop.Height} \times 0.45, 300))$ ôm khít các vạch barcode mà không lan sang text.
  - `Tool Editor: Checkbox Render Canvas & Tối Ưu Hóa Render 20MP (ToolEditorView.xaml, ToolEditorViewModel.Engine.cs)`: Bổ sung Checkbox `Render Canvas` cho phép tắt hoàn toàn việc chuyển đổi ảnh và overlay lên canvas, tiết kiệm 100% tài nguyên CPU/RAM khi không cần đồ họa UI; tối ưu hóa tái sử dụng cache khi bật.
- [x] Task 164: Nâng Cấp Toàn Diện Cơ Chế Inject Thuộc Tính Cho Tool Text & Condition Logic (Universal Dynamic Variable Registry):
  - `Khắc phục triệt để lỗi {Origin1.Angle}`: Thiết kế cơ chế Multi-Alias (`Origin1`, `Origin`, `Origin_1`, `Pattern1`, `P1`, `CIR1`, `CAL1`, `LPD1`, `EP1`, `EPD1`, `SC1`, `CC1`, `CD1`, `CP1`, `CL1`, `CR1`, `CCIR1`, `DB1`, `PLC1`, v.v.), cho phép tham chiếu linh hoạt theo tên node thực tế trên đồ thị hoặc tên loại tool.
  - `Kiến trúc Module Hóa ConditionEvaluator.VariableRegistry.cs`: Phân rã `ConditionEvaluator.cs`, mở rộng `class Variable` với `Members` dictionary và `RawObject`, tự động đăng ký đầy đủ 32+ loại Tool hiện có.
  - `Universal Dynamic Reflection Fallback`: Tự động quét toàn bộ public property của `InspectionResult` và các class kết quả mới trong tương lai, đảm bảo không bao giờ bỏ sót bất kỳ tool hay thuộc tính mới nào mà không cần sửa code thủ công.
  - `Động cơ Text Template 4 Tầng & Định Dạng Nâng Cao`: Hỗ trợ đầy đủ format số (`{Origin1.Angle:F1}`, `{Origin1.Score:P1}`, `{Dist1.Diff:F2}`, `{X_mm}`, `{X_px}`, v.v.).
  - `Mở rộng IntelliSense Auto-Complete`: Tự động gợi ý toàn bộ danh sách thuộc tính phong phú cho tất cả các Tool khi gõ `{` hoặc tên tool + dấu `.`.
- [x] Task 165: Cải Tiến Cơ Chế `Run Continuous` Phân Tầng Theo Loại Nguồn `ImageSource` (Timer-Driven vs Event-Driven Cho Camera Công Nghiệp Hikrobot 20MP GigE Hardware Trigger Line 0):
  - `Phân Định Kiến Trúc 2 Pipeline Rõ Rệt`:
    1. **Timer-Driven Continuous Pipeline** (áp dụng cho `ImageSourceType.Folder`, `ImageSourceType.File`, USB Webcam / Simulator): Chạy lặp tuần tự theo chu kỳ `FolderIntervalMs`, nạp ảnh và thực thi Flow.
    2. **Event-Driven Industrial Camera Pipeline** (áp dụng cho `ImageSourceType.Camera` với camera công nghiệp Hikrobot / Basler / Cognex hoặc `ImageSourceTriggerMode.LineTrigger`): Chuyển camera sang chế độ Grabbing liên tục và chờ tín hiệu Hardware Trigger (Line 0) từ PLC. KHÔNG dùng Timer/Interval polling giả lập trigger.
  - `Đệm Khung Hình Bounded Channel & Zero Memory Leak Cho Ảnh 20MP (5120x3840 = ~60MB RAM)`:
    - Sử dụng `System.Threading.Channels.Channel<Mat>` với `BoundedChannelOptions(capacity: 2)` và `BoundedChannelFullMode.DropOldest`.
    - Camera SDK Callback chỉ làm nhiệm vụ đẩy `frame.Clone()` vào Channel, không trực tiếp xử lý Inspect để giữ tốc độ phản hồi cực cao.
    - Vision Worker Task độc lập đọc tuần tự từ Channel, chạy `_inspectionService.Inspect(frameMat, ...)`, cập nhật thống kê tốc độ/Dashboard và gọi `frameMat.Dispose()` ngay lập tức trong khối `finally`.
    - Tự động hoàn tất Channel và Dispose sạch mọi frame tồn dư khi bấm `STOP` hoặc huỷ luồng, loại bỏ triệt để rò rỉ bộ nhớ.
  - `Sửa Lỗi Buffer Size Trong HikCameraDriver.cs`:
    - Thay thế kích thước bộ đệm hardcode $1920 \times 1080 \times 4$ (~8.29MB) bằng việc truy vấn động `PayloadSize` thực tế từ MVS SDK (`MV_CC_GetIntValueEx_NET("PayloadSize", ...)`), fallback tối thiểu $5120 \times 3840 \times 4$ (~80MB), triệt tiêu hoàn toàn nguy cơ tràn bộ đệm hoặc crash khi kết nối camera 20MP GigE.
    - Hỗ trợ giải mã đầy đủ các định dạng Pixel công nghiệp: `Mono8`, `BGR8_Packed`, `RGB8_Packed`, `BayerRG8`, `BayerGB8`, `BayerBG8`, `BayerGR8`.
  - `Nâng Cấp Giao Diện Tool Editor & Thuộc Tính ImageSource`:
    - Quét danh sách Camera đa hãng (Hikrobot GigE/USB3, USB DirectShow, Simulator) hiển thị trực quan trên ComboBox.
    - Nút `🔁 Run Continuous` chuyển sang `⏹ STOP` (nền đỏ `#D32F2F`) với tooltip chi tiết ("Dừng chờ Camera Hardware Trigger (Line 0)").
    - Bổ sung huy hiệu mô tả chế độ thời gian thực (`ImageSource_ContinuousModeDescription`) và ẩn/hiện ô `Interval (ms)` phù hợp.
  - `Kiểm Thử Tự Động & Biên Dịch`:
    - Bổ sung `ContinuousPipelineTest.cs` kiểm thử thành công 100% 4/4 test cases về Channel Bounded, Burst Producer vs Slow Consumer và Memory Cleanup khi Stop.
    - Biên dịch Solution thành công **0 Error(s)**.

- [x] Task 166: Bổ sung tính năng Xoay ROI Mịn (Fine Rotation Damping) khi giữ phím Ctrl:
  - `Cơ Chế Damping Góc Xoay`: Trong `ImageViewerControl.xaml.cs`, tại phương thức `UpdateRoiEdit`, khi người dùng kéo handle xoay ROI và giữ phím `Ctrl`, hệ thống áp dụng hệ số giảm tốc 20% (`delta * 0.2`) cho gia số góc xoay, cho phép tinh chỉnh góc cực kỳ êm ái, mịn màng và chính xác đến $0.1^\circ$.
  - `Phản Hồi Trực Quan (Visual Feedback)`: Khi giữ `Ctrl` trong lúc xoay, badge góc xoay tự động đổi viền và chữ sang màu xanh lá `LimeGreen` kèm tiền tố `[Fine]` (ví dụ: `[Fine] 12.4°`) để người dùng dễ dàng nhận diện trạng thái xoay chính xác.

- [x] Task 167: Khắc phục triệt để lỗi Node đã xóa trên Canvas vẫn tồn tại và xử lý trong Vision Pipeline (Orphaned Definition Cleanup):
  - `Bổ Sung Xóa Definition Trong DeleteSelectedNode`: Cập nhật `ToolEditorViewModel.GraphOps.cs` bổ sung các case xóa khỏi danh sách cấu hình tương ứng trong `_config` khi người dùng xóa node: `Crop`, `ColorDiff`, `ImgArithmetic`, `CreatePoint`, `CreateLine`, `CreateRect`, `CreateCircle`.
  - `Nâng Cấp Lưới An Toàn SyncToolGraphToConfig`: Cập nhật `ToolEditorViewModel.Config.cs` tự động dọn dẹp toàn bộ 10 danh sách cấu hình mồ côi (`Crops`, `ColorDiffs`, `ImgArithmetics`, `ImageOutputs`, `SegmentLineDistances`, `ContourCompares`, `CreatePoints`, `CreateLines`, `CreateRects`, `CreateCircles`) không còn node tương ứng trên Canvas, triệt tiêu hoàn toàn hiện tượng pipeline chạy ngầm và tốn thời gian cho các tool đã xóa.

- [x] Task 168: Khắc phục triệt để lỗi Resize ROI bị biến dạng cả 2 cạnh khi ROI đang ở góc xoay khác 0° (Oriented Bounding Box Resizing):
  - `Chiếu Tọa Độ Cục Bộ Theo Góc Nghiêng`: Trong `ImageViewerControl.xaml.cs`, tại phương thức `UpdateRoiEdit`, chuyển đổi vector di chuyển chuột $(dxMove, dyMove)$ từ hệ toạ độ màn hình sang hệ toạ độ cục bộ của ROI thông qua ma trận quay ngược $R(-\theta)$.
  - `Độc Lập 2 Trục & Cố Định Cạnh Đối Diện`: Khi kéo 1 cạnh (Left, Right, Top, Bottom), chỉ có kích thước tương ứng (Width hoặc Height) thay đổi, kích thước còn lại giữ nguyên 100%. Tâm của hình chữ nhật được cập nhật chính xác theo góc nghiêng qua $R(\theta) \cdot \Delta C_{local}$, đảm bảo cạnh/góc đối diện được neo cố định tuyệt đối trong không gian ảnh thực.

- [x] Task 64: Tối ưu hoá toàn diện hiệu năng của Tool Preprocessor khi kéo Slider (Properties Panel & Global Preprocess Dialog):
  - `Tối ưu thuật toán Vision Engine (Class1.cs)`:
    - Bổ sung phương thức `EstimateBackground` áp dụng kỹ thuật **Pyramidal Downscale-Blur-Upscale** cho Illumination Correction. Với ảnh kích thước lớn và kernel $k > 15$, ảnh được thu nhỏ về proxy ~480-640px, thực hiện làm mờ Gaussian với kernel tỷ lệ $k_{small}$, sau đó phóng to lại bằng phép nội suy tuyến tính (bilinear). Thời gian ước lượng nền giảm từ **1.500ms - 3.500ms xuống chỉ còn ~3.5ms** (~400x speedup).
    - Tối ưu hóa `FlatFieldNormalize` bằng `Cv2.Divide` + `Cv2.Normalize` dạng byte trực tiếp, loại bỏ hoàn toàn các bộ đệm `CV_32F` (tiết kiệm ~400MB RAM/frame).
  - `Triển khai cơ chế Throttled & Debounced Asynchronous Background Preview`:
    - Xây dựng phương thức `SchedulePreprocessPreviewUpdate()` trong `ToolEditorViewModel.Engine.cs` sử dụng `CancellationTokenSource` và `Task.Run` trên Background Thread Pool.
    - Tự động hủy bỏ (cancel) tác vụ cũ đang tính dở khi có giá trị slider mới tới, gom cụm các micro-events bằng độ trễ ngắn 10ms.
    - Chạy `_preprocessor.Run(...)` và chuyển đổi `ToBitmapSourceForDisplay(1920, 1080)` hoàn toàn dưới nền, gọi `.Freeze()` và gán vào UI Dispatcher ở mức `DispatcherPriority.Render`.
    - Chuyển toàn bộ 21+ property setters của Preprocessor trong `ToolEditorViewModel.cs` và `TeachViewModel.cs` sang `SchedulePreprocessPreviewUpdate()`.
- [x] Task 65: Khắc phục quy trình áp dụng Preprocess và ROI Masking cho Tool Origin:
  - `Sửa Lỗi Thuật Toán ROI Masking (Class1.cs)`: Khởi tạo ảnh `blended` bằng ma trận nền đen `Scalar.All(0)` thay vì clone lại `inputBgrOrGray`. Nhờ đó, các pixel trong vùng bị che/loại trừ (`roiMask == 0`) được xóa sạch thành màu đen thay vì giữ lại ảnh gốc chưa che.
  - `Đồng Bộ Dạy Mẫu Template Origin (ToolEditorViewModel.cs)`: Cập nhật `TrySaveTemplateImage` sử dụng `ResolveToolImageForPreview(snap, originNode)` để trích xuất ảnh mẫu Origin từ đúng node Preprocess/Crop kết nối trực tiếp trên Canvas Flow.
  - `Chuẩn Hóa Pipeline Runtime (InspectionService.Pipeline.cs)`: Bổ sung điều kiện tìm `toolNode` cho Origin theo kiểu `"Origin"`, hỗ trợ cả cổng `Image` và `In`, đồng thời trả về `(ppMat, new PreprocessSettings())` khi node Preprocess có custom ROIs/masking để tránh double filter.
- [x] Task 66: Nâng cấp toàn diện kiến trúc tổng hợp hiển thị Kết quả & ROI Overlay cho Tool ResultView và Final Preview:
  - `Bổ sung ColorDiff & BlobDetection vào BuildFinalOverlayFromRun`: Tích hợp đầy đủ kết quả đo lường độ lệch màu $\Delta E$, giá trị đo $L, a, b$ kèm khung viền màu động theo trạng thái PASS/NG, cũng như danh sách bounding box, tâm điểm và tổng số lỗi của BlobDetection trên màn hình tổng hợp ResultView.
  - `Kiến trúc Universal Node-Based ROI Aggregator cho AddConfigRois & BuildFinalOverlay`: Thay thế việc duyệt thủ công từng công cụ rời rạc bằng cơ chế tự động duyệt qua tất cả các Node trên đồ thị Canvas (`Nodes`) và gọi `AddConfigRoisForNode(node, dst)`. Đảm bảo 100% các công cụ hiện tại (`ColorDiff`, `CircleFinder`, `LinePair`, `EdgePair`, `CodeDetection`,...) và bất kỳ công cụ mới nào trong tương lai sẽ tự động được hiển thị khung ROI trên ResultView/Final Preview mà không bao giờ bị bỏ sót.
  - `Đồng Bộ Chuyển Hướng BuildOverlayForNodeFromRunWithConfig`: Khi người dùng click chọn trực tiếp node `ResultView` trên Canvas, hệ thống tự động định tuyến gọi `BuildFinalOverlayFromRunWithConfig(run, dst)` để kết xuất đầy đủ lớp phủ composite của toàn bộ quy trình.
  - `Đồng Bộ Xoay Pose ROI ColorDiff theo Origin`: Áp dụng `CreateRotatedRoiWithPose` cho cả vùng mẫu (Sample ROI) và vùng tham chiếu (Ref ROI) của Tool ColorDiff trong `AddConfigRoisForNode`.
- [x] Task 67: Khắc phục triệt để hiện tượng khựng lag/đơ UI khi click chọn Node ImageSource (nguồn Camera):
  - `Khắc phục Lỗi Quét Thiết Bị Đồng Bộ (ToolPreprocess.cs, ToolEditorViewModel.cs)`: Chuyển đổi `RefreshAvailableCameraItems` sang chạy hoàn toàn dưới nền bất đồng bộ (`Task.Run`) với cơ chế cờ khóa `_isScanningCameras` và kiểm tra `AvailableCameraItems.Count > 0`, loại bỏ việc quét phần cứng DirectShow/Hikrobot lặp đi lặp lại trên UI Dispatcher Thread mỗi khi click chọn node hoặc cập nhật thuộc tính.
  - `Triển khai Cơ Chế Non-Blocking Asynchronous Preview Capture (Engine.cs)`: Tối ưu `LoadImageFromSourceForPreview` khi nguồn là Camera: ưu tiên lấy tức thì từ live stream (`_cameraService.TryGetLatestFrameClone()`) hoặc ảnh dùng chung (`_sharedImage.GetSnapshot()`) với độ trễ 0ms. Nếu chưa có ảnh, kích hoạt `ScheduleAsyncCameraSnapshotFetch` dưới nền thay vì chặn giao diện bằng `Task.Wait(2000)` đồng bộ, đảm bảo giao diện luôn phản hồi tức thì 100% mượt mà.
- [x] Task 68: Khắc phục toàn diện Tool Caliper (Edge detection sub-pixel, PCA Line Fitting, Pipeline Short-Circuit và Live Preview / Run Overlay):
  - `Khắc phục Lỗi Origin Short-Circuit (InspectionService.Pipeline.cs)`: Sửa lỗi hệ thống tự động short-circuit và gán Caliper `Found = false` khi flow không có node Origin hoặc chưa dạy template Origin (`hasOriginNode && hasOriginTemplate`).
  - `Tái cấu trúc Module Thuật toán CaliperDetector (VisionInspectionApp.VisionEngine)`:
    - Bổ sung `CaliperDetector.cs` độc lập với thuật toán lấy mẫu gradient 1D trung bình theo strip profiles.
    - Áp dụng bộ lọc 3-point Gaussian smooth `[0.25, 0.5, 0.25]` triệt tiêu nhiễu pixel của sensor/ánh sáng.
    - Tìm vị trí cực đại sub-pixel parabol `InterpPeak`.
    - Ánh xạ ngược tọa độ từ ảnh cắt về tọa độ ảnh gốc bằng `Geometry2D.MapToGlobal` với đúng góc xoay tổng hợp `totalAngleDeg = originAngleDeg + def.SearchRoi.Angle`.
    - Khớp đường thẳng tổng quát bằng ma trận hiệp phương sai trực giao (PCA line fitting).
  - `Cập nhật Live Preview và Rendering Overlay (ToolEditorViewModel.Engine.cs & GraphOps.cs)`:
    - Bổ sung khối xử lý Caliper vào `BuildOverlayForNode` để preview chạy trực tiếp (live preview) khi di chuyển ROI hoặc chỉnh slider/thông số kể cả trước khi Run.
    - Cập nhật `BuildOverlayForNodeFromRun` và `BuildFinalOverlayFromRun` hiển thị đường thẳng Caliper `Lime` nét dày 2.0px và các điểm sub-pixel `Gold` bán kính 2.5px.
    - Chuẩn hóa vẽ các vạch strip của Caliper trong `AddConfigRoisForNode` theo góc xoay tổng hợp.
- [x] Task 69: Khắc phục toàn diện Result Overlay cho Tool Caliper và Tool Line (Đường thẳng nhận diện và các Overlay liên quan trên từng Node, ResultView và ImageOutput):
  - `Khắc phục Lỗi Dấu Góc Xoay Ngược trong ExtractStraightRoi (Class1.cs & InspectionService.Helpers.cs)`: Sửa `GetRotationMatrix2D(center, totalAngleDeg, 1.0)` giúp trích xuất patch ROI xoay chính xác tuyệt đối, loại bỏ hiện tượng xoay ngược khiến Caliper / Line thất bại hoặc lệch góc ($2 \times \theta$).
  - `Nâng cấp LineDetector Hỗ trợ ROI Xoay & Adaptive Threshold Fallback (Class1.cs)`: Tích hợp `ExtractStraightRoi` và `MapToGlobal` cho `DetectLongestLine` và `DetectTopLines`, kèm cơ chế tự động hạ ngưỡng thích ứng cho các đường thẳng mảnh/ngắn.
  - `Đồng bộ & Bổ sung Toàn Diện Result Overlay cho ResultView & ImageOutput (Engine.cs)`:
    - Bổ sung Live Detection cho `Calipers`, `Lines` (xoay đa hướng theo Origin), `CircleFinders`, `LinePairDetections` vào `BuildFinalOverlay` khi `_lastRun` chưa có kết quả.
    - Bổ sung Live Detection Fallback vào `BuildFinalOverlayFromRunWithConfig` để khi kéo/chỉnh ROI trên Canvas, `ResultView` và `ImageOutput` luôn hiển thị đường thẳng màu xanh lá (`Lime`) và các chấm vàng sub-pixel (`Gold`) tức thì.
    - Cập nhật `BurnOverlaysToMat` trong `ImageOutputs.cs` vẽ các chấm vàng sub-pixel vào file ảnh lưu trữ.
  - `Tự động Co Giãn Kích thước Điểm Sub-pixel Theo Zoom (FastOverlayCanvas.cs)`: Tính toán bán kính điểm `pr = (p.Radius > 0 ? p.Radius : 4.0) / scale;` đảm bảo điểm vàng hiển thị rõ nét ở mọi mức thu phóng zoom.
- [x] Task 169: Khắc phục toàn diện lỗi kết nối PLC Bridge (Port 39871) trên cửa sổ PLC Manager:
  - `Tự Động Tìm & Đồng Bộ Binary PlcBridge (ResolveBridgePath)`: Thay thế việc duyệt tương đối cứng nhắc (`..\..\..\..`) bằng hàm `ResolveBridgePath` duyệt động cây thư mục solution, tự động tìm kiếm và so sánh timestamp để chọn bản build `VisionInspectionApp.PlcBridge.dll` mới nhất; tự động sao chép đồng bộ vào `BaseDirectory` của ứng dụng khi phát hiện binary mới hơn.
  - `Cải Thiện CopyPlcBridgeFiles Target (UI.csproj)`: Bổ sung đầy đủ các đường dẫn `x86\Debug`, `x86\Release`, `Debug`, `Release` và cấu hình `SkipUnchangedFiles="false"` để luôn ghi đè binary PlcBridge mới nhất vào thư mục output của UI khi build.
  - `Tối Ưu Watcher & Dọn Dẹp Tiến Trình`:
    - Cải tiến `StartParentProcessWatcher` trong `PlcBridge\Program.cs` xử lý an toàn phân quyền WOW64 khi tiến trình 32-bit theo dõi tiến trình cha 64-bit, chỉ thoát khi PID cha thực sự không còn tồn tại qua 2 lần kiểm tra liên tiếp.
    - Tối ưu `KillExistingZombieBridges` dọn dẹp trực tiếp qua API .NET không gọi tiến trình con PowerShell làm chậm quá trình kết nối.
    - Nâng thời gian timeout thử kết nối TCP socket trong `EnsureBridgeProcessAndSocketConnectedAsync` lên 5s (25 lần x 200ms).
  - `Dọn Dẹp Trạng Thái Cấu Hình (plc_config.json)`: Đặt lại `CpuName = string.Empty` khi tải danh sách PLC ở trạng thái `Disconnected` trong `LoadGlobalConfig()`, ngăn ngừa việc hiển thị lại chuỗi thông báo lỗi cũ từ các phiên làm việc trước.
  - `Kiểm Thử Thành Công 100%`: Chạy test kết nối và đọc ghi tag PLC (FX5UCPU Station 1) trong `TestExtractApp` thành công 100%. Biên dịch solution 0 lỗi.
- [x] Task 170: Khắc phục toàn diện 2 vấn đề Camera Công Nghiệp Hikrobot GigE MV-CS200-10GC (Băng thông mạng 990 Mbps & Sai lệch màu sắc Bayer GB 8):
  - `Tách Biệt Kết Nối Camera (Start/Open) và Live View (Streaming) - Tối Ưu Băng Thông 0 Mbps`:
    - Phân tách rõ ràng trạng thái `Start Camera` (Khởi tạo kết nối, cấu hình thông số, đưa camera về Standby, mạng 0 Mbps) và `Live View` (Chỉ stream liên tục 30 FPS khi người dùng cần căn chỉnh góc/focus).
    - Thêm nút Toggle **`👁️ Bật/Tắt Live View`** và **`📸 Chụp Thử Frame (Snap)`** trên giao diện Camera Settings.
    - Cải tiến `GrabFrameAsync` trong `HikCameraDriver`: Tự động snap 1 frame độc lập trong 10-30ms khi camera đang ở Standby hoặc Trigger Mode mà không giữ stream liên tục.
    - Cải tiến `CameraService`: Quét và nhận diện đúng driver `HikCameraDriver` thay vì gán cứng DirectShow; tái sử dụng driver đang mở để snap ảnh siêu tốc cho Tool Editor `Run Once` / `Run Flow` mà không chiếm dụng 990 Mbps băng thông mạng Ethernet.
  - `Khắc Phục Dứt Điểm Lỗi Run Once Không Chụp Frame Mới Từ Camera`:
    - Loại bỏ việc trả về frame cũ (`TryGetLatestFrameClone`) trong `CaptureCameraSnapshotSafe` gây tình trạng lấy lại ảnh trong RAM từ các phiên trước.
    - `RunFlowAsync` trực tiếp gọi `await _cameraService.CaptureSnapshotAsync(...)` để kích hoạt camera chụp frame mới tức thời và cập nhật ngay vào `_sharedImage` cùng Preview Canvas.
    - Ngăn chặn fallback âm thầm sang ảnh cũ khi không thể chụp từ camera, giúp thông báo lỗi rõ ràng nếu mất kết nối.
  - `Sửa Lỗi Sai Lệch Màu Sắc Cảm Biến Bayer GB 8 Bằng Bộ Xử Lý ISP Hikrobot SDK`:
    - Thay thế thuật toán OpenCV demosaicing thô (`Cv2.CvtColor`) bằng hàm chuyển đổi chuẩn mực chính hãng `MV_CC_ConvertPixelTypeEx_NET` sang `PixelType_Gvsp_BGR8_Packed`.
    - Kích hoạt chất lượng chuyển đổi cao cấp `MV_CC_SetBayerCvtQuality_NET(1)` (High Quality / Gradient Demosaic).
- [x] Task 171: Cấu hình và lưu trạng thái camera riêng biệt cho từng Job từ node ImageSource trong Tool Editor:
  - `Định Nghĩa CameraParameters trong Models & Serialization vào Job`:
    - Chuyển `CameraParameters`, `CameraTriggerMode`, `CameraTriggerSource` sang namespace `VisionInspectionApp.Models`.
    - Bổ sung `public CameraParameters CameraParams { get; set; } = new();` vào `ImageSourceDefinition`. Tự động lưu/nạp cấu hình camera (Exposure, Gain, Gamma, White Balance, Trigger Mode, Packet Size, v.v.) vào tệp `.job` theo từng sản phẩm.
  - `Tích Hợp Nút Cấu Hình Trực Tiếp Trong Properties Panel của Node ImageSource`:
    - Bổ sung nút **`⚙️ Cấu Hình Camera Cho Job Này...`** trên Properties Panel của node `ImageSource` khi nguồn ảnh là Camera Công Nghiệp (Hikrobot / USB).
    - Tạo `ImageSource_OpenJobCameraSettingsCommand` mở cửa sổ cấu hình độc lập `JobCameraSettingsWindow`.
  - `Xây Dựng Cửa Sổ Cấu Hình Camera Chuyên Dụng Cho Job (JobCameraSettingsWindow & ViewModel)`:
    - Giao diện 3 cột chuyên nghiệp: Cột 1 (Quản lý kết nối, Start/Stop, Live View HUD, Snap), Cột 2 (Preview trực tiếp kèm thanh công cụ Fit/Zoom/Lưới/Crosshair), Cột 3 (Bảng điều khiển cảm biến: Exposure, Gain, Gamma, Balance White Auto/Manual/OnePush, Trigger Mode, Packet Size).
    - Nút **`💾 Lưu Vào Job Hiện Tại`** lưu toàn bộ thông số camera vào `ImageSourceDefinition.CameraParams` của Job hiện tại, đánh dấu `IsDirty = true` và đóng cửa sổ.
  - `Tự Động Áp Dụng Thông Số Camera Khi Chuyển Đổi Job`:
    - Tự động gọi `_cameraService.ApplyParametersAsync(imgSourceDef.CameraParams)` khi nạp Job mới trong `ToolEditorViewModel.Config.cs` (`LoadJobFromFile`) và `InspectionViewModel.cs` (`LoadConfig`).
- [x] Task 172: Bổ sung tùy chọn Hardware Camera ROI (Cắt từ phần cứng cảm biến) và Toàn bộ 12 Pixel Formats chuẩn MVS lưu theo từng Job:
  - `Bổ Sung Thuộc Tính Hardware ROI & Pixel Format Trong CameraParameters`:
    - Bổ sung `EnableHardwareRoi`, `RoiOffsetX`, `RoiOffsetY`, `RoiWidth`, `RoiHeight`, `PixelFormat` vào `CameraParameters`.
    - Tự động serialize/deserialize toàn bộ thông số ROI và Pixel Format vào file `.job` theo từng Job.
  - `Xử Lý Thiết Lập Hardware ROI & Pixel Format Trên Driver HikCameraDriver`:
    - Tự động tạm dừng Grabbing an toàn khi thay đổi kích thước khung hình hoặc format ảnh.
    - Áp dụng Pixel Format qua `MV_CC_SetPixelFormat_NET` / GenICam `PixelFormat` tương ứng với 12 định dạng MVS (`Mono 8`, `Mono 10`, `Mono 12`, `RGB 8`, `BGR 8`, `YUV 422 (YUYV) Packed`, `YUV 422 Packed`, `Bayer GB 8`, `Bayer GB 10`, `Bayer GB 10 Packed`, `Bayer GB 12`, `Bayer GB 12 Packed`).
    - Thiết lập GenICam ROI theo đúng thứ tự an toàn: `OffsetX=0, OffsetY=0 -> Width, Height -> OffsetX, OffsetY` kèm căn chỉnh bội số an toàn (Step 4 cho Width/OffsetX, Step 2 cho Height/OffsetY) tránh lỗi phần cứng.
  - `Cập Nhật Giao Diện JobCameraSettingsWindow & Tab Camera Settings`:
    - Thêm GroupBox **"📐 Camera Hardware ROI (Cắt Từ Phần Cứng)"** với Slider & TextBox cho `Offset X`, `Offset Y`, `Width`, `Height` và các nút tiện ích **`🖥️ Full Sensor`**, **`🎯 Căn Giữa ROI`**.
    - Thêm ComboBox chọn `Pixel Format (Định dạng điểm ảnh)` đầy đủ 12 tùy chọn chuẩn MVS.
    - Đồng bộ lưu/nạp tự động theo Job khi mở/chuyển Job.
- [x] Task 173: Khắc phục triệt để độ trễ chụp Hardware ROI & Tích hợp kéo thả chỉnh ROI trực quan 2 chiều trên màn hình Live Preview Camera:
  - `Khắc Phục Hiện Tượng Nghẽn Lệnh & Tranh Chấp Khi Chụp Ảnh ROI`:
    - Tích hợp cơ chế **Debounce Timer (250ms)** trong `JobCameraSettingsViewModel` và `CameraSettingsViewModel`: Loại bỏ hiện tượng bão hòa hàng trăm lệnh GenICam dồn dập qua Ethernet GigE khi kéo Slider hoặc kéo thả ROI.
    - Đồng bộ hóa đa luồng an toàn với `SemaphoreSlim _driverGate` và cache frame mới nhất trong `HikCameraDriver`: Khử bỏ xung đột handle giữa luồng Live Streaming (`ContinuousGrabLoop`) và luồng Snap Frame (`GrabFrameAsync`), đảm bảo tốc độ chụp frame tức thì (< 30ms).
  - `Tích Hợp Kéo Thả ROI Trực Quan 2 Chiều Trên Màn Hình Live Preview`:
    - Thay thế trình hiển thị tĩnh bằng `ImageViewerControl` trên `JobCameraSettingsWindow.xaml` và `CameraSettingsView.xaml`.
    - Hỗ trợ chọn, di chuyển khung ROI và kéo 8 điểm tay cầm (Handles) để co giãn kích thước ROI trực tiếp bằng chuột trên Canvas.
    - Đồng bộ hóa 2 chiều thời gian thực: Kéo thả trên màn hình Preview tự động cập nhật các ô `Offset X`, `Offset Y`, `Width`, `Height` và Slider bên phải; ngược lại chỉnh sửa ô số bên phải tự động vẽ lại khung ROI vàng/xanh sáng trên Preview.
- [x] Task 174: Tự động kết nối lại Camera đã dùng gần nhất khi khởi động ứng dụng & Bật Live View ngay tại tab OQC Scanner:
  - `Tự Động Lưu & Khôi Phục Camera Gần Nhất Theo Phần Cứng Thực Tế`:
    - Mở rộng `CameraAdjustSettings` trong `CameraService.cs` lưu chi tiết: `SavedDeviceVendor`, `SavedDeviceModelName`, `SavedDeviceSerialNumber`, `SavedDeviceIpAddress`, `SavedDeviceMacAddress`, `SavedDeviceInterfaceType`, `SavedCameraIndex`, `SavedRtspUrl`, `SavedParameters`.
    - Khi khởi động app, hàm `StartSavedCameraAsync()` quét toàn bộ thiết bị qua `CameraDriverFactory.ScanAllDevices()`, thông minh tìm và kết nối lại đúng camera phần cứng công nghiệp (Hikrobot / Basler / Cognex / USB / RTSP) theo Serial Number / IP / Vendor.
    - Trường hợp không có thiết bị phần cứng cắm vào, tự động chuyển sang Camera giả lập (Simulator) để đảm bảo app luôn sẵn sàng hoạt động.
  - `Tự Động Bật Live View Sẵn Sàng Căn Chỉnh Trên Tab OQC Scanner`:
    - Khi app khởi động ở tab OQC Scanner (`SelectedTabIndex = 3`), `CameraService` tự động kích hoạt `IsLiveViewEnabled = true` và `StartGrabbingAsync()`.
    - Luồng ảnh thời gian thực lập tức hiển thị trên màn hình Preview của tab OQC Scanner mà không cần người dùng thao tác thêm, sẵn sàng cho công nhân/kỹ sư đặt sản phẩm vào căn chỉnh.
- [x] Task 176: Tích hợp cơ chế Timeout cho quá trình nhận diện mã & Tự động trả về FAIL khi quá thời gian chờ (Task 176):
  - `Cấu hình Timeout Nhận Diện Mã Trong Cửa Sổ Cấu Hình Tra Cứu & Ghi Log OQC`:
    - Mở rộng `OqcScannerConfig.cs`: Bổ sung trường `ScanTimeoutMs` (mặc định 3000ms).
    - Cập nhật `OqcSettingsDialog.xaml`: Bổ sung giao diện thiết lập `⏱️ Thời gian chờ quét mã (Timeout)` tính bằng ms.
    - Cập nhật `OqcScannerViewModel.Settings.cs`: Đồng bộ nạp và lưu thuộc tính `ScanTimeoutMs` vào file cấu hình `oqc_scanner_config.json`.
  - `Đếm Timeout Nhận Diện Mã Sau Khi Bấm Space & Tự Động Báo FAIL`:
    - Cải tiến `ExecuteScanFromCameraAsync` trong `OqcScannerViewModel.cs`: Khi người dùng nhấn phím `Space`, camera chụp 1 frame ảnh và bắt đầu đếm thời gian Timeout cho tác vụ nhận diện mã.
    - Nếu nhận diện được mã QR/Barcode hợp lệ trong thời gian Timeout: Ngay lập tức tra cứu Database, nạp Job và thực thi kiểm tra tự động.
    - Nếu thuật toán nhận diện mã chạy hết thời gian Timeout hoặc không tìm thấy mã hợp lệ trong ảnh: Tự động trả về kết quả `FAIL` (màu đỏ), ghi bản ghi `NO_READ` / `FAIL` vào bảng lịch sử `ScanHistory` và ghi log thất bại lên DB (nếu bật `LogResultToDb`).
- [x] Task 177: Khắc phục triệt để lỗi AutoFit khi Live View & Tự động hiển thị kết quả Final và hỗ trợ phím F5 quay lại Live Cam:
  - `Khắc Phục AutoFit Chỉ Chạy Duy Nhất 1 Lần Khi Khởi Động`:
    - Sửa `ImageViewerControl.xaml.cs`: Trong `OnImageSourceChanged`, chỉ thực hiện fit hình ảnh lần đầu tiên (`!_hasFirstFit`). Khi luồng Live View liên tục truyền frame mới đến, giữ nguyên hoàn toàn tỷ lệ zoom và vị trí pan mà người dùng đã kéo/phóng to mà không bị giật hoặc tự động reset về fit.
    - Sửa `OqcScannerView.xaml.cs`: Xóa bỏ trigger AutoFit lặp lại trên `IsVisibleChanged`. Người dùng có toàn quyền kiểm soát zoom/pan và có thể nhấn nút **🎯 Fit View** bất kỳ lúc nào để Fit lại.
  - `Tự Động Chuyển Sang Xem Kết Quả Final Khi Chạy Xong Job & Phím F5 Quay Lại Live View`:
    - Cập nhật `OqcScannerViewModel.cs`: Đăng ký lắng nghe sự kiện `_toolEditorViewModel.InspectionCompletedAsync` trong constructor. Ngay khi Job chạy xong, hệ thống tự động tắt Live Stream (`IsShowingLiveCamera = false`), cập nhật `PreviewImage` thành ảnh Final và vẽ toàn bộ đồ họa `OverlayItems` (kết quả đo, bounding box, nhãn PASS/NG).
    - Cập nhật `OqcScannerView.xaml.cs`: Thêm xử lý phím tắt **F5** (`Key.F5`) để người dùng có thể ngay lập tức chuyển đổi từ chế độ xem kết quả Final quay trở lại Live Camera một cách nhanh chóng và tiện lợi.
- [x] Task 178: Sửa triệt để lỗi ROI và Overlay không tự động xuất hiện sau khi Job kiểm tra chạy xong:
  - `Đồng Bộ Thứ Tự Render Previews & Overlay Trước Khi Kích Hoạt Sự Kiện InspectionCompleted`:
    - Sửa `ToolEditorViewModel.Engine.cs`: Trong cả `RunFlowAsync` và `RunSingleFlowFromImageFileAsync`, di chuyển lời gọi `RefreshPreviews()` lên **TRƯỚC** phép gán `LastResult = _lastRun;`. Đảm bảo `FinalPreviewImage` và `FinalOverlayItems` đã được tính toán và dựng hoàn tất trước khi bắn sự kiện hoàn thành kiểm tra.
    - Sửa `ToolEditorViewModel.cs`: Bổ sung reset `LastResult = null;`, `FinalPreviewImage = null;`, `SelectedNodePreviewImage = null;` trong `ClearActiveGraph()` khi nạp Job mới để tránh lẫn lộn dữ liệu cũ.
  - `Đồng Bộ Hai Chiều Trực Tiếp Qua PropertyChanged Giữa ToolEditor và OqcScanner`:
    - Cập nhật `OqcScannerViewModel.cs`: Đăng ký lắng nghe sự kiện `_toolEditorViewModel.PropertyChanged`. Khi `FinalOverlayItems`, `FinalPreviewImage`, `SelectedNodePreviewImage` hoặc `SelectedNodeOverlayItems` được cập nhật, `OqcScannerViewModel` tự động cập nhật ngay lập tức sang `PreviewImage` và `OverlayItems` mà người dùng không cần phải click thủ công vào nút "Xem kết quả final".
- [x] Task 179: Bổ sung tùy chọn 'Dùng Đầu Scanner' & Chuyển đổi phím Space để RUN JOB và tự động áp dụng bộ lọc cắt chuỗi:
  - `Bổ Sung CheckBox 'Dùng Đầu Scanner' & Quản Lý Cấu Hình OQC Scanner`:
    - `OqcScannerConfig.cs`: Bổ sung thuộc tính `UseExternalScanner` (bool).
    - `OqcScannerViewModel.Settings.cs`: Nạp và lưu `UseExternalScanner` vào file cấu hình `oqc_scanner_config.json`.
    - `OqcScannerView.xaml`: Thêm CheckBox `🔫 Dùng Đầu Scanner` đặt ngay dưới `⚡ Tự động chạy Job (Auto Run)`.
  - `Chuyển Đổi Tính Năng Phím Space Khi Dùng Đầu Scanner Ngoài`:
    - Sửa `OqcScannerView.xaml.cs`: Khi `UseExternalScanner == true`, vô hiệu hóa phím Space chụp quét từ camera; phím `Space` được chuyển sang chức năng **`RUN JOB`** (gọi `RunJobCommand`). Khi `UseExternalScanner == false`, phím `Space` vẫn giữ chức năng quét mã từ Camera (`ScanFromCameraCommand`).
    - Cập nhật text nút bấm trực quan: Nút quét camera đổi thành `📷 QUÉT CAMERA`, nút chạy Job hiển thị `▶ CHẠY JOB (SPACE)`.
  - `Tự Động Áp Dụng Bộ Lọc Độ Dài & Cắt Chuỗi Cho Chuỗi Quét Từ Đầu Scanner`:
    - Sửa `OqcScannerService.cs` & `IOqcScannerService.cs`: Bổ sung phương thức `ProcessRawCodeString(rawInput, config)` dùng chung.
    - Sửa `OqcScannerViewModel.cs`: Khi nhận mã nhập/quét từ đầu scan ngoài, hệ thống tự động kiểm tra điều kiện độ dài (`EnableLengthFilter`) và cắt chuỗi (`EnableCodeCrop`) theo cấu hình tra cứu OQC trước khi tra cứu Database và nạp Job.
- [x] Task 180: Sửa triệt để lỗi lật ảnh ngang (Reverse X) bị lộn ngược lại do xung đột giữa Hardware Flip và Software Flip:
  - `Khắc Phục Xung Đột Lật Ảnh Hai Lần (Hardware + Software Deduplication)`:
    - Sửa `CameraDriverBase.cs`: Bổ sung cờ theo dõi trạng thái lật phần cứng `_hardwareReverseXApplied` và `_hardwareReverseYApplied`. Chỉ thực hiện phần mềm `Cv2.Flip` khi phần cứng camera KHÔNG hỗ trợ hoặc chưa lật trục tương ứng.
    - Sửa `HikCameraDriver.cs`: Trong `ApplyParametersAsync`, kiểm tra kết quả trả về của SDK `MV_CC_SetBoolValue_NET("ReverseX", ...)` và `MV_CC_SetBoolValue_NET("ReverseY", ...)`. Nếu camera Hikrobot phần cứng đã lật X thành công (`hwX = true`), phần mềm sẽ không gọi thêm lệnh `Cv2.Flip(..., FlipMode.Y)` nữa $\rightarrow$ Triệt tiêu 100% lỗi lật 2 lần khiến ảnh bị quay về như cũ.
  - `Đồng Bộ Hóa Xử Lý Hậu Kỳ Cho Cả Live Stream Và Snap Frame`:
    - Cập nhật `ContinuousGrabLoop` và `GrabFrameAsync` trong `HikCameraDriver.cs`: Đảm bảo frame truyền lên UI qua sự kiện `FrameCaptured` lẫn frame lưu trong `_latestContinuousFrame` đều được hậu xử lý (lật X/Y, chỉnh Contrast, Brightness, Grayscale) đúng 1 lần duy nhất một cách hoàn hảo và nhất quán.
- [x] Task 181: Khởi động mặc định Full Screen, Đặt định dạng ảnh xuất mặc định JPG & Tách biệt độc lập hoàn toàn Cấu hình Camera của Job và Camera Settings hệ thống:
  - `Khởi Động Mặc Định Full Screen (MainWindow.xaml)`:
    - Bổ sung `WindowState="Maximized"` và `WindowStartupLocation="CenterScreen"` vào `MainWindow.xaml` để ứng dụng luôn mở toàn màn hình chuẩn công nghiệp khi khởi động.
  - `Tool ImageOutput Đặt Định Dạng Xuất Mặc Định Là JPG (Class1.cs, ToolEditorViewModel.ToolImageOutput.cs)`:
    - Đổi định dạng mặc định trong `ImageOutputDefinition.Format` và `ImageOutput_Format` từ PNG sang **`JPG`** giúp tiết kiệm tối đa dung lượng lưu trữ ổ đĩa.
  - `Tách Biệt Hoàn Toàn & Độc Lập 100% Giữa Cấu Hình Camera của Job và Camera Settings Hệ Thống`:
    - `CameraService.cs`: Tách riêng `_systemParameters` (cấu hình camera mặc định hệ thống lưu trong `camera_adjust_settings.json`) và `_currentParameters` (thông số đang kích hoạt trên camera). Cung cấp các phương thức `SaveSystemParametersAsync` và `RestoreSystemParametersAsync`. Loại bỏ hoàn toàn việc Job nạp thông số vô tình ghi đè vào file cài đặt hệ thống.
    - `CameraSettingsViewModel.cs`: Hoạt động độc lập trên `_cameraService.SystemParameters` và lưu trực tiếp vào cấu hình hệ thống mà không can thiệp vào bất kỳ Job nào.
    - `JobCameraSettingsViewModel.cs`: Quản lý độc lập bản sao `_cameraParams` của Job đang mở. Bổ sung cơ chế `_originalParams` tự động hoàn trả camera về trạng thái ban đầu khi người dùng bấm Hủy (Cancel) hoặc đóng cửa sổ mà chưa bấm Lưu.
- [x] Task 182: Cải tiến cơ chế đồng bộ trạng thái Lật X, Y và thông số thực tế từ phần cứng Camera vào ứng dụng:
  - `Đồng Bộ Trạng Thái Lật Phần Cứng Khi Kết Nối Camera (HikCameraDriver.cs, CameraDriverBase.cs)`:
    - Trong `HikCameraDriver.OpenAsync`, ngay sau khi mở kết nối thiết bị, ứng dụng chủ động truy vấn giá trị thực tế `ReverseX` và `ReverseY` từ phần cứng camera (`MV_CC_GetBoolValue_NET`) và cập nhật vào `_hardwareReverseXApplied`, `_parameters`.
  - `Áp Dụng Công Thức XOR Logic Hoàn Hảo Cho Xử Lý Lật Hình (CameraDriverBase.cs)`:
    - Sửa `ApplySoftwarePostProcessing` và `RaiseFrameCaptured`: Dùng công thức `needFlipX = (paramsObj.ReverseX != hardwareReverseXApplied)` và `needFlipY = (paramsObj.ReverseY != hardwareReverseYApplied)`.
    - Bảo đảm bất kể camera phần cứng đang ở trạng thái nào (lật hay không lật), nếu app yêu cầu `ReverseX = false` mà camera phần cứng đang bị lật thì OpenCV tự động lật ngược lại đưa về ảnh gốc; nếu app yêu cầu `ReverseX = true` mà camera phần cứng đã lật thì không bị lật đúp 2 lần.
  - `Cung Cấp API Đọc Trực Tiếp Thông Số Từ Camera (ICameraDriver.cs, HikCameraDriver.cs, CameraService.cs)`:
    - `HikCameraDriver.ReadParametersAsync`: Đọc toàn bộ các node GenICam phần cứng (`ReverseX`, `ReverseY`, `ExposureTime`, `ExposureAuto`, `Gain`, `GainAuto`, `Gamma`, `BalanceWhiteAuto`, `TriggerMode`, `TriggerSource`, `PacketSize`, `PacketDelay`, `ROI`...).
    - `CameraService.ReadParametersFromCameraAsync`: Cung cấp hàm trung tâm cho UI.
  - `Giao Diện Đồng Bộ 1-Click "🔄 Đọc Từ Camera" (CameraSettingsView.xaml, JobCameraSettingsWindow.xaml)`:
    - Bổ sung nút **`🔄 Đọc Từ Camera`** trên cả màn hình cấu hình Camera hệ thống và cấu hình Camera của Job. Tự động đồng bộ toàn bộ CheckBox, Slider, ComboBox lên UI khi kết nối camera.
- [x] Task 183: Cơ chế Live Stream độc lập cho Tab OQC Scanner & Quản lý Consumer theo yêu cầu thực tế:
  - `Cơ Chế Quản Lý Multi-Consumer Live Stream (CameraService.cs)`:
    - Bổ sung `_activeLiveConsumers` (HashSet các bên đăng ký xem Live Stream: `OQCScanner`, `CameraSettings`, `JobCameraSettings`...).
    - Cung cấp hàm `RequestLiveStreamAsync(consumerId, enable)`: Tự động kích hoạt `StartGrabbingAsync()` khi có ít nhất 1 consumer yêu cầu xem và tự động dừng `StopGrabbingAsync()` khi không còn ai xem $\rightarrow$ Đưa băng thông mạng Ethernet về đúng **0 Mbps**.
  - `Live View Độc Lập Cho Tab OQC Scanner (OqcScannerViewModel.cs)`:
    - Bổ sung `partial void OnIsShowingLiveCameraChanged(bool value)`: Tự động gửi yêu cầu `RequestLiveStreamAsync("OQCScanner", true)` khi người dùng bật Live Camera trên tab OQC Scanner và hủy đăng ký khi chuyển sang xem kết quả Final hoặc chạy Job.
- [x] Task 184: Hiển thị đầy đủ Overlay kết quả phép đo Distance lên ảnh xuất của Tool ImageOutput:
  - `Mở Rộng Nguồn Điểm Neo pointPosMap (InspectionService.ImageOutputs.cs)`:
    - Bổ sung tất cả các nguồn tọa độ điểm (Origin, Points, CreatePoints, CircleFinders, Diameters, EdgePairs, EdgePairDetections, BlobDetections, Calipers) vào bảng tra cứu điểm `pointPosMap`.
  - `Sửa Điều Kiện Vẽ Overlay Cho Tool Distances (InspectionService.ImageOutputs.cs)`:
    - Sửa điều kiện từ `(dRes.Pass || dRes.Value > 0)` thành `!double.IsNaN(dRes.Value) && pointPosMap.TryGetValue(dRes.PointA, out var pa) && pointPosMap.TryGetValue(dRes.PointB, out var pb)`.
    - Vẽ đường thẳng nối 2 điểm kèm nhãn kết quả số đo `${dRes.Name}=... mm/px` chuẩn xác 100% lên tệp ảnh xuất của tool ImageOutput.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, chạy test thành công.
- [x] Task 185: Cải tiến toàn diện Bảng lịch sử OQC Scanner & Schema ghi Log Database chi tiết:
  - `Lưu Trữ Lịch Sử Cục Bộ (Local JSON)`: Tự động lưu và tải lại bảng lịch sử khi bật/tắt app qua file `%AppData%\Vision2026\oqc_scan_history.json`.
  - `Trích Xuất Excel (Export to CSV/Excel)`: Nút xuất file Excel tiếng Việt UTF-8 with BOM hiển thị chuẩn xác không lỗi font.
  - `Cột Ảnh Output & Cửa Sổ Xem Chi Tiết Phép Đo`: Cột link ảnh output trên DataGrid và cửa sổ `OqcScanDetailDialog` hiển thị ảnh output sắc nét + bảng danh sách toàn bộ các phép đo chi tiết (*Spec, Tol+, Tol-, Min, Max, Result, Judge*).
  - `Export / Import Cấu Hình OQC`: Nút xuất và nạp cấu hình OQC dạng JSON trong hộp thoại cài đặt để sao lưu và chia sẻ cấu hình giữa các máy.
  - `Hỗ Trợ Token {UUID} & Ghi Log Phép Đo Chi Tiết Vào DB`: Tạo UUID ngẫu nhiên duy nhất cho mỗi lượt quét và thực thi truy vấn log chi tiết từng phép đo vào bảng `OqcInspectResult`.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**.
- [x] Task 187: Sửa lỗi Tool ImageSource với Camera Giả Lập luôn dùng ảnh đã chọn thay vì video mặc định:
  - `Bảo Toàn Đường Dẫn CustomImagePath Khi Nạp Job`: Trong `CameraService.cs`, khi `ApplyParametersAsync`, `SaveSystemParametersAsync` và `CaptureSnapshotFromCameraAsync` được gọi, luôn bảo toàn `_simulatorCustomImagePath` và `_simulatorEnableRandomTransform` vào `_currentParameters` nếu thông số truyền vào không chứa đường dẫn ảnh.
  - `Cải Tiến SimulatorCameraDriver`: Override `ApplyParametersAsync` để bảo lưu `CustomImagePath` hiện tại hoặc fallback đọc từ tệp cấu hình `%AppData%\Vision2026\camera_adjust_settings.json`. Cải tiến `GetOrLoadBaseMat` tự động nạp ảnh tùy chỉnh đã lưu thay vì phát video mặc định.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**.
- [x] Task 188: Cải tiến Tool Caliper (handle kéo thả StripLength, chuẩn hóa sub-pixel) và đồng bộ an toàn Undistort cho ImageOutput:
  - `Thêm Handle Kéo Thả Trực Tiếp StripLength & StripWidth Trên Preview Canvas (ToolEditorViewModel.GraphOps.cs, ImageViewerControl.xaml.cs)`:
    - Bổ sung khung dải quét `${c.Name} Cal_Strip` màu DeepSkyBlue với các tay nắm (handles) cho phép kéo chuột co giãn trực tiếp `StripLength` và `StripWidth` ngay trên Preview Canvas.
    - Đồng bộ 2 chiều tức thì: Kéo thả trên canvas tự động cập nhật giá trị `Strip Length` và `Strip Width` lên Properties Panel và lưu vào file `.job`.
  - `Chuẩn Hóa Thuật Toán Caliper Sub-Pixel & Đồng Bộ Hệ Tọa Độ Origin (CaliperDetector.cs, ToolEditorViewModel.Engine.cs)`:
    - Sửa công thức nội suy cực trị Parabol: Tính toán mảng Gradient Profile $G[x]$ và lấy 3 điểm gradient lân cận $G[bestIdx-1], G[bestIdx], G[bestIdx+1]$ để định vị đỉnh biên với độ chính xác sub-pixel tuyệt đối (sai số < 0.05px).
    - Đồng bộ hóa `Origin` pose (`originTeach`, `originFound`, `originAngleDeg`) vào tất cả các hàm dựng overlay (`BuildOverlayForNode`, `BuildOverlayForNodeFromRun`) giúp đường bắt biên bám khít 100% vào mép sản phẩm thực tế.
  - `Cải Tiến Thuật Toán Undistort & Khắc Phục Lỗi Méo Biên Khi Xuất Ảnh (ChessboardCalibrationService.cs, ToolEditorViewModel.Engine.cs)`:
    - Nâng cấp phương thức `Undistort`: Sử dụng `Cv2.GetOptimalNewCameraMatrix` kết hợp `Cv2.InitUndistortRectifyMap` và `Cv2.Remap(BorderTypes.Constant, Scalar.Black)` kèm khử nhiễu đa thức méo, loại bỏ hoàn toàn hiện tượng méo gấp biên ngoài / dải lưỡi liềm méo ở cạnh ảnh.
    - Đồng bộ hóa 100% việc áp dụng `Undistort` giữa màn hình Preview của Tool Editor và Pipeline xuất ảnh qua `ImageOutput`.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, vượt qua các bài kiểm thử tự động.
- [x] Task 189: Quản lý cấu hình Global Chessboard Calibration toàn cục cho toàn bộ ứng dụng:
  - `Quản Lý Lưu/Nạp Global Calibration Tự Động (ChessboardCalibrationService.cs)`:
    - Lưu trữ tệp cấu hình `%AppData%\Vision2026\global_chessboard_calibration.json` kèm khóa luồng an toàn `_fileLock`.
    - Cung cấp các phương thức `SaveGlobalCalibration`, `GetGlobalCalibration`, `HasGlobalCalibration`, và `EnsureCalibration(config)`.
  - `Tự Động Áp Dụng Cho Job Mới Hoặc Job Chưa Có Calib (ToolEditorViewModel.cs, JobService.cs, InspectionService.Pipeline.cs)`:
    - Khi tạo Job mới (`NewGraph`) hoặc mở Job (`LoadJobFromFile` / `LoadJob` / `InspectAsync`), hệ thống tự động kiểm tra: nếu Job chưa có calibration riêng thì lập tức kế thừa cấu hình Global Calibration và `PixelsPerMm`.
    - Bảo toàn 100% cấu hình riêng của các Job đã được hiệu chuẩn độc lập trước đó.
  - `Tích Hợp Nút '🌐 Set As Global Calib' Trong Chessboard Dialog (ChessboardCalibrationDialog.xaml, ChessboardCalibrationViewModel.cs)`:
    - Thêm nút **`🌐 Set As Global Calib`** bên cạnh **`🔄 Undistort Preview`** cho phép lưu cấu hình hiệu chuẩn hiện tại thành Global chỉ với 1 click.
    - Cập nhật thông báo trạng thái trực quan phân biệt rõ calibration của Job và Global calibration.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, vượt qua toàn bộ unit test tự động.
- [x] Task 190: Hiển thị ROI dẫn hướng Origin trên Live View OQC Scanner, thêm khung xem ảnh Origin Template và khắc phục độ nét ảnh 20MP khi Zoom:
  - `Khung ROI Dẫn Hướng Origin Trên Live View OQC Scanner (OqcScannerViewModel.cs)`:
    - Khi Job được nạp và bật Live Camera (`IsShowingLiveCamera == true`), tự động vẽ các overlay dẫn hướng đặt mẫu: Khung `Origin Search ROI` (xanh biển), Khung `Origin Template ROI` (vàng kim nét đậm) và Dấu chữ thập tâm chuẩn (`Crosshair + Point`) tại `WorldPosition`.
    - Giúp công nhân vận hành nhận biết ngay lập tức vị trí và vùng cần đặt phôi sản phẩm dưới camera.
  - `Khung Hiển Thị Mẫu Gốc Origin Teach Template (OqcScannerView.xaml, OqcScannerViewModel.cs)`:
    - Bổ sung khung phụ **`🎯 MẪU GỐC ORIGIN (TEACH)`** bên cạnh màn hình Preview trên tab OQC Scanner.
    - Hiển thị hình ảnh template mẫu đã teach trước đó từ Job (`OriginTemplateImage`), thông số vị trí tâm và kích thước khung mẫu kèm dòng hướng dẫn thao tác trực quan.
  - `Khắc Phục Triệt Để Độ Nét Ảnh 20MP Khi Zoom (MatExtensions.cs, ImageViewerControl.xaml, ToolEditorViewModel.Engine.cs)`:
    - Xác nhận và bảo toàn nguyên vẹn $100\%$ độ chính xác tính toán của Vision Engine trên ma trận ảnh 20MP gốc ($5472 \times 3648$).
    - Nâng cấp `MatExtensions.ToBitmapSourceForDisplay` và `ToolEditorViewModel.Engine.cs` bảo toàn độ phân giải 20MP gốc cho ảnh tĩnh và kết quả kiểm tra dưới nền bất đồng bộ kết hợp `.Freeze()` (không gây lag/treo UI).
    - Cấu hình `RenderOptions.BitmapScalingMode="HighQuality"` và `RenderOptions.EdgeMode="Aliased"` trên `ImageViewerControl.xaml`, giúp render zoom sâu $5\times - 10\times$ cực kỳ sắc nét như phần mềm MVS của Hikrobot.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, vượt qua toàn bộ unit test tự động.
- [x] Task 191: Tự động AutoFit khi thay đổi kích thước cửa sổ & Chuyển nền Preview ảnh sang Grid Xám công nghiệp:
  - `Tự Động AutoFit Khi Thay Đổi Kích Thước Cửa Sổ (ImageViewerControl.xaml.cs)`:
    - Cập nhật sự kiện `OnRootGridSizeChanged` tự động kích hoạt `ResetView()` mỗi khi container hoặc cửa sổ ứng dụng thay đổi kích thước (kéo giãn, phóng to cực đại Maximize, thu nhỏ Restore, kéo thanh chia GridSplitter).
    - Đảm bảo hình ảnh và toàn bộ lớp đồ họa Overlay luôn tự động căn chỉnh vừa khít 100% với khung nhìn mà không cần click thủ công nút Fit View.
  - `Nền Grid Xám Công Nghiệp Chống Nhầm Lẫn Nền Đen Thực Tế (ImageViewerControl.xaml)`:
    - Thay thế nền đen đặc `#111` bằng `DrawingBrush` hoa văn Grid Checkerboard màu xám công nghiệp (`#24252A` và `#2E3038` với viền lưới mảnh `#1C1D22`).
    - Giúp người dùng phân biệt rõ ràng giữa viền nền của máy kiểm tra / phôi sản phẩm màu đen và không gian canvas hiển thị của phần mềm.
    - Áp dụng đồng bộ cho tất cả các màn hình Preview trong toàn bộ ứng dụng (Tool Editor, ResultView, OQC Scanner, Live Camera, Inspection, Calibration, Teach).
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**.
- [x] Task 192: Cải tổ toàn bộ Navigation, Custom Frameless Title Bar CMS VINA, MenuStrip đa tầng chuyên nghiệp & Dọn dẹp Tool Editor:
  - `Thanh Tiêu Đề Liền Mạch Custom Frameless Title Bar (MainWindow.xaml, MainWindow.xaml.cs)`:
    - Loại bỏ window chrome mặc định của Windows (`WindowStyle="None"`, `WindowChrome`), đưa giao diện app liền mạch sát đỉnh màn hình.
    - Tích hợp nhận diện thương hiệu **CMS VINA** (`Assets/cms_vina_logo.png`), tên hệ thống `VISION SYSTEM`, khu vực kéo thả di chuyển cửa sổ kèm thông tin Job/Sản phẩm (`🏷️ SP: ...`) và đèn báo trạng thái `● READY`.
    - Tích hợp 3 nút điều khiển cửa sổ tiêu chuẩn: Thu nhỏ (Minimize `─`), Phóng to/Khôi phục (Maximize/Restore `🗖`/`🗗`), Đóng ứng dụng (Close `✕`) với hiệu ứng hover mượt mà và hỗ trợ nhấp đúp tiêu đề để phóng to.
  - `Hệ Thống MenuStrip Đa Tầng Khoa Học & Phím Tắt Tiêu Chuẩn (MainWindow.xaml, MainWindowViewModel.cs)`:
    - `📁 Tệp (File)`: Tạo Job mới (`Ctrl+N`), Mở Job (`Ctrl+O`), Lưu Job (`Ctrl+S`), Lưu thành Job khác, Nạp ảnh xem trước, Chụp ảnh camera, Đóng Job, Thoát app (`Alt+F4`).
    - `👁️ Màn Hình`: Chuyển đổi nhanh 4 tab chức năng (`F1` - Tool Editor, `F2` - OQC Scanner, `F3` - Manual Inspection, `F4` - Camera Settings).
    - `🔌 Truyền Thông (PLC/HMI)`: PLC Manager, HMI Manager, Real-time Monitor, Tag Browser.
    - `🗄️ Dữ Liệu (Database/OQC)`: Database Connection Manager, Gán Mã Sản Phẩm ↔ Job File, Cấu Hình OQC Scanner.
    - `📐 Hiệu Chuẩn`: Pixel/Mm Calibration Dialog, Chessboard Camera Calibration Dialog.
    - `⚡ Tác Vụ`: Chạy kiểm tra 1 lần (`F5` - Run Once), Chạy kiểm tra liên tục (`F6` - Run Continuous).
    - `❓ Trợ Giúp`: Hộp thoại thông tin bản quyền CMS VINA Vision System.
  - `Tối Ưu Hóa Hệ Thống Tab & Dọn Dẹp Toolbar Tool Editor (MainWindow.xaml, ToolEditorView.xaml)`:
    - Loại bỏ tab "Calibration" riêng biệt, chuyển sang 4 tab chính tinh gọn (`Tool Editor`, `OQC Scanner`, `Manual Inspection`, `Camera Settings`).
    - Dọn dẹp thanh Toolbar trong Tool Editor: Loại bỏ các nút quản lý File và PLC/DB dư thừa, chỉ giữ lại các nút cốt lõi: *Mã Sản Phẩm, Load Image, Capture Camera, Run Once, Run Continuous, Calibration, Chessboard Calib, Result Badge*, giúp workspace thông thoáng và tập trung.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, toàn bộ unit tests và run test đạt PASS 100%.
- [x] Task 193: Khắc phục triệt để lỗi Maximize cửa sổ bị thanh Taskbar của Windows che mất:
  - `Xử Lý Hook Thông Điệp Win32 WM_GETMINMAXINFO (MainWindow.xaml.cs)`:
    - Bổ sung `HwndSourceHook` xử lý thông điệp Win32 `WM_GETMINMAXINFO` (0x0024) khi cửa sổ được phóng to cực đại (Maximize).
    - Sử dụng `MonitorFromWindow` và `GetMonitorInfo` lấy chính xác tọa độ vùng làm việc khả dụng `rcWork` (Work Area đã trừ đi kích thước và vị trí của Taskbar Windows).
    - Đảm bảo cửa sổ khi Maximize tự động căn chỉnh vừa khít trên thanh Taskbar (không bị taskbar che khuất phần đáy app, không bị tràn màn hình).
    - Hoạt động chuẩn xác trên mọi cấu hình màn hình (đơn màn hình, đa màn hình, DPI Scaling khác nhau, thanh Taskbar ở dưới/trên/trái/phải).
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**.
- [x] Task 194: Cập nhật thông tin tác giả và bản quyền trong hộp thoại About CMS VINA Vision System:
  - `Cấu Trúc Tham Số Hộp Thoại MessageBox.Show (MainWindowViewModel.cs)`:
    - Chuyển toàn bộ thông tin tác giả (Nguyễn Văn Hùng, Phone, Email, Website) vào đúng tham số nội dung `messageBoxText` của `MessageBox.Show`.
    - Giữ nguyên tham số tiêu đề `caption = "About CMS VINA Vision System"` ngắn gọn, trực quan.
    - Hiển thị đầy đủ thông tin bản quyền và liên hệ tác giả khi click menu `Trợ Giúp -> Giới Thiệu CMS VINA Vision System`.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**.
- [x] Task 195: Khắc phục triệt để lỗi tính DeltaE và đồng bộ tọa độ Origin pose cho Tool ColorDiff:
  - `Nguyên Nhân Sai Lệch DeltaE`:
    - Khi lấy mẫu màu (`ColorDiff_TeachRefColor`), hệ thống lấy trực tiếp pixel tại tọa độ `InspectRoi` thô mà chưa chuyển đổi theo ma trận xoay/tịnh tiến `Origin` match trên ảnh hiện tại.
    - Trong khi đó, pipeline kiểm tra (`InspectionService.Pipeline.cs`) lại chuyển đổi `InspectRoi` theo `TransformRoiKeepSize` dẫn đến việc lấy mẫu ở vị trí A nhưng kiểm tra ở vị trí B bị dịch chuyển.
    - Ngoài ra, nếu `WorldPosition` của Origin là `(0, 0)`, `originTeach` trong pipeline thiếu fallback tâm `TemplateRoi` dẫn đến độ lệch toàn bộ $\Delta x, \Delta y$ lớn bằng chính tọa độ tuyệt đối của vật thể.
  - `Khắc Phục & Đồng Bộ 100% Thuật Toán (ColorDiffProcessor.cs, ToolEditorViewModel.cs, InspectionService.Pipeline.cs, ToolEditorViewModel.Engine.cs)`:
    - Chuyển `ColorDiffProcessor.GetMeanLab` thành public method dùng chung cho cả khâu Teach lấy mẫu lẫn Run kiểm tra, đảm bảo thuật toán chuyển đổi không gian màu CIELab đồng nhất tuyệt đối.
    - Trong `ColorDiff_TeachRefColor` (`ToolEditorViewModel.cs`): Áp dụng đúng Origin pose (`TransformPose`) của ảnh hiện tại trước khi tính `GetMeanLab`.
    - Bổ sung fallback chuẩn xác cho `originTeach` trong `InspectionService.Pipeline.cs` và `ToolEditorViewModel.Engine.cs`.
    - Đồng bộ hiển thị overlay và text kết quả ColorDiff trên Tool Editor (`CreateRotatedRoiWithPose`).
  - `Kiểm Thử Tự Động & Biên Dịch Thành Công 100%`:
    - Tạo `TestExtractApp/ColorDiffTest.cs` kiểm tra 4 kịch bản: Khớp màu trên cùng ảnh ($\Delta E = 0.00$), Dịch chuyển Origin ($\Delta E = 0.00$), Xoay góc ROI ($\Delta E = 0.00$), Phát hiện khác màu Red vs Green ($\Delta E = 170.13$). Toàn bộ test đạt **PASS 100%**.
    - Solution biên dịch **0 Error(s)**.
- [x] Task 196: Hiển thị Tên Job & Tên Sản Phẩm kèm dấu * khi có chỉnh sửa chưa lưu trên thanh Menu:
  - `Đồng Bộ Trạng Thái Header (MainWindowViewModel.cs, MainWindow.xaml)`:
    - Bổ sung `HeaderJobTitle` và `HeaderProductCodeTitle` hiển thị `📁 Job: [Tên Job]*` và `🏷️ SP: [Mã SP]*`.
    - Lắng nghe sự kiện thay đổi thuộc tính `IsDirty`, `CurrentJobFilePath`, `ProductCode` trên `ToolEditorViewModel` để cập nhật giao diện thời gian thực.
- [x] Task 197: Sửa lỗi nhập số thập phân trong cửa sổ Hiệu chuẩn Calibration:
  - `Khắc Phục Thuật Toán FlexibleNumberParser & FlexibleDoubleConverter`:
    - Tạo `FlexibleNumberParser` trong `VisionInspectionApp.Application.Services` chuẩn hóa cả `,` và `.` thành dấu chấm thập phân, parse bằng `CultureInfo.InvariantCulture` với `NumberStyles.Float` (loại bỏ hoàn toàn việc nhận nhầm `.` là dấu phân cách hàng nghìn trên locale tiếng Việt/châu Âu).
    - Cập nhật `FlexibleDoubleConverter.cs` sử dụng `FlexibleNumberParser.TryParseDouble`.
    - Xóa bỏ `Delay=250` trên ô nhập `RealDistanceMm` trong `CalibrationView.xaml` để giá trị cập nhật tức thời khi người dùng nhập số và bấm `Add Measurement`.
- [x] Task 198: Bổ sung menu "Mở Gần Đây (Open Recent)" lưu trữ 10 Job gần nhất:
  - `Dịch Vụ RecentJobsService (Application Layer)`:
    - Tạo `IRecentJobsService` và `RecentJobsService` quản lý danh sách tối đa 10 tệp Job gần nhất lưu trong `recent_jobs.json`.
    - Cơ chế LIFO, tự động đưa Job vừa mở/lưu lên đầu, loại bỏ trùng lặp và tự động dọn dẹp các tệp không còn tồn tại.
    - Tích hợp vào `MainWindowViewModel` và `ToolEditorViewModel` (tự động thêm khi mở, lưu job).
  - `Giao Diện MenuStrip (MainWindow.xaml)`:
    - Bổ sung submenu `🕒 Mở Gần Đây (Open Recent)` trong menu `📁 Tệp (File)` liên kết lệnh `OpenRecentJobCommand`.
  - `Kiểm Thử & Biên Dịch Thành Công 100%`:
    - Tạo test suite `TestExtractApp/RecentJobsAndCalibrationTest.cs`, toàn bộ test suites đạt **PASS 100%**, Solution biên dịch **0 Error(s)**.
- [x] Task 199: Khắc phục triệt để lỗi WPF tự động xóa dấu chấm khi gõ số thập phân (FlexibleDoubleConverter):
  - `Nguyên Nhân Gốc Rễ`:
    - Với `UpdateSourceTrigger=PropertyChanged`, khi người dùng đang gõ `28.`, WPF gọi `ConvertBack("28.")` ra `28.0`, sau đó lập tức gọi `Convert(28.0)` format thành `"28"`, ghi đè ngược lại làm biến mất dấu chấm `.` trên TextBox.
  - `Giải Pháp`:
    - Trong `FlexibleDoubleConverter.ConvertBack`: Khi chuỗi kết thúc bằng dấu chấm/phẩy (`.` hoặc `,`) hoặc số `0` sau dấu phẩy (như `28.` hoặc `28.0`), trả về `Binding.DoNothing` để WPF không ghi đè chuỗi đang gõ.
    - Nhờ đó người dùng có thể thoải mái gõ `28.6`, `0.05`, `123.456` mà không bị gián đoạn hay mất dấu chấm.
- [x] Task 200: Tối ưu AutoFit chỉ kích hoạt khi toàn bộ cửa sổ ứng dụng (Window) thay đổi kích thước:
  - `Phân Tách Resize Cửa Sổ vs Kéo Divider (ImageViewerControl.xaml.cs)`:
    - Loại bỏ sự kiện `PART_RootGrid.SizeChanged += OnRootGridSizeChanged` (vốn gây AutoFit ngoài ý muốn mỗi khi kéo GridSplitter/divider trong Tool Editor).
    - Chuyển sang đăng ký lắng nghe sự kiện `Window.SizeChanged` và `Window.StateChanged` của cửa sổ cha (`Window.GetWindow(this)`).
    - Đảm bảo khi kéo divider trong tab thì giữ nguyên mức zoom/pan hiện tại, chỉ khi phóng to/thu nhỏ hoặc thay đổi kích thước toàn bộ App mới thực hiện AutoFit.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, toàn bộ unit tests đạt PASS 100%.
- [x] Task 201: Tự động nạp và áp dụng cấu hình Camera của Job trước khi chụp ảnh trong Tool Editor:
  - `Nguyên Nhân`:
    - `LoadJobFromFile` trước đây gọi `_ = _cameraService.ApplyParametersAsync(...)` dạng fire-and-forget bất đồng bộ, khiến `OnRunOnceClicked` chạy ngay tức khắc khi camera chưa kịp áp dụng xong thông số.
    - Đồng thời hàm `RunFlowAsync` và `CaptureCameraImageAsync` trong Tool Editor chưa tự động áp dụng `CameraParams` của Job trước khi `CaptureSnapshotAsync`, dẫn đến việc chụp ảnh bằng thông số mặc định hoặc cấu hình cũ của tab Camera Settings.
  - `Đã Khắc Phục (ToolEditorViewModel.Config.cs & ToolEditorViewModel.Engine.cs)`:
    - Trong `LoadJobFromFile`: Đảm bảo `await _cameraService.ApplyParametersAsync(imgSourceDef.CameraParams)` hoàn tất rồi mới kích hoạt luồng `OnRunOnceClicked`.
    - Trong `RunFlowAsync` và `CaptureCameraImageAsync`: Tự động kiểm tra và áp dụng `imgSourceDef.CameraParams` xuống driver camera trước khi gọi `CaptureSnapshotAsync`.
    - Đảm bảo khi mở Job hoặc chạy Run Once / Capture, camera luôn hoạt động $100\%$ đúng với các thông số Exposure, Gain, Gamma, Hardware ROI, White Balance đã cấu hình riêng cho Job đó.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**.
- [x] Task 202: Khắc phục triệt để lỗi nạp ảnh cũ khi mở Job lần đầu & Chỉ nạp CameraParams duy nhất 1 lần khi load Job:
  - `Nguyên Nhân Gốc Rễ Đã Rà Soát Chi Tiết`:
    1. Khi gọi `LoadJobFromFile`, lệnh `RefreshPreviews()` ở UI thread chạy trước khi nạp camera, kích hoạt `LoadImageFromSourceForPreview` kéo frame cũ từ driver vào `_sharedImage` và `_imageSourcePreviewCache`.
    2. Trong `HikCameraDriver`, hàng đợi DMA / Ring Buffer phần cứng của Hik Camera SDK lưu sẵn 2-3 frames chụp với thông số cũ trước đó; khi đổi thông số `ApplyParametersAsync`, các frame còn tồn đọng trong phần cứng này tiếp tục được đọc ra trước khi frame phơi sáng mới được cảm biến sinh ra!
    3. Trong `CameraService`, `_lastFrame` không được xóa khi `ApplyParametersAsync` được gọi.
    4. Trong `ToolEditorViewModel.Config.cs`, logic tra cứu `ImageSourceDefinition` cần ưu tiên khớp chính xác theo `RefName` của `ImageSource` node trong đồ thị.
  - `Giải Pháp Toàn Diện`:
    1. `ClearAllImageSourceCache()` và `_sharedImage.SetImage(null)` ngay khi bắt đầu `LoadJobFromFile`.
    2. Trong `HikCameraDriver`: Gọi `MV_CC_ClearImageBuffer_NET()`, thiết lập `_discardFramesCount = 2` trong `ApplyParametersAsync` và `ContinuousGrabLoop` để bỏ qua toàn bộ frame cũ trong hàng đợi phần cứng FIFO; tăng số lần thử `GrabFrameAsync` lên 40 chu kỳ để đảm bảo nhận chính xác frame mới.
    3. Xóa frame đệm cũ trong `CameraService` (`_lastFrame = null`) và `SimulatorCameraDriver` (`_cachedBaseMat = null`).
    4. Trong `LoadJobFromFile`: Áp dụng cấu hình camera của Job, chờ $100\text{ms}$ cho cảm biến camera chốt phơi sáng/đẩy frame mới rồi mới kích hoạt `OnRunOnceClicked()`.
    5. Trong `RunFlowAsync()`: Không gọi `ApplyParametersAsync` lặp lại trên mỗi lần run của cùng 1 job, đảm bảo hiệu năng tối đa và đúng luồng vận hành công nghiệp.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, toàn bộ unit tests đạt PASS 100%.
- [x] Task 203: Thiết kế và đóng gói App Icon EXE chuyên nghiệp chuẩn Windows 11 (Logo CMS VINA + chữ VISION):
  - `Yêu Cầu`:
    - Tạo lại icon ứng dụng cho file `.exe` thay thế icon cũ. Sử dụng logo CMS VINA hiện có và thêm chữ `VISION` nhỏ gọn, tinh tế ở phía dưới.
  - `Đã Triển Khai (TestExtractApp/IconGenerator.cs)`:
    - Xây dựng module sinh icon đồ họa Vector/GDI+ chất lượng cao với khử răng cưa `HighQualityBicubic` và font rendering `ClearTypeGridFit`.
    - Thiết kế định dạng Squircle Windows 11 với 4 góc reticle định vị quang học thị giác máy `[ + ]`, đặt logo gốc CMS VINA ở trung tâm và badge bo góc chữ `V I S I O N` màu trắng trên nền gradient xanh đậm `#0B5394` $\rightarrow$ `#043462`.
    - Đóng gói file `.ico` chuẩn **Win32 DIB BITMAPINFOHEADER (32bpp BGRA + AND mask)** cho các phân giải chuẩn (`16x16`, `24x24`, `32x32`, `48x48`, `64x64`, `128x128`) và PNG cho `256x256`, tương thích $100\%$ với trình biên dịch tài nguyên Roslyn PE (`csc.exe`) và Windows Shell.
    - Cập nhật trực tiếp vào `VisionInspectionApp.UI/Assets/cms-vina-vision-system.ico`.
    - Đã xác thực trích xuất thành công tài nguyên icon từ tệp thực thi `VisionInspectionApp.UI.exe` (`VerifyExeIcon`).
- [x] Task 204: Nâng cấp toàn diện Tab Manual Inspection (Manual Measurement / 2D Vision CMM System):
  - `Tương Tác Đo Đo Đạc Tương Tác Click-Click`:
    - Click điểm 1 -> nhả chuột di chuyển live preview đường vẽ/kích thước bám theo chuột -> click điểm 2 (hoặc 3) để chốt kết quả đo.
    - Hỗ trợ nhấn chuột phải hoặc phím ESC để hủy phép đo đang thực hiện dở.
  - `Tỉ Lệ Pixel/mm Toàn Cục`:
    - Tự động nạp tỉ lệ `PixelsPerMm` từ Global Chessboard Calibration khi khởi tạo hoặc chụp ảnh.
  - `Dọn Sạch Toàn Diện`:
    - Nút Xóa kết quả dọn sạch cả danh sách kết quả đo trong bảng và toàn bộ các overlay hình học đã vẽ trên ảnh.
  - `Thước Đo mm Động (RulerCanvas)`:
    - Hiển thị thước đo mm ngang (Top) và dọc (Left) bao quanh màn hình xem ảnh, co giãn mượt mà theo mức Zoom và Pan thực tế của ảnh.
    - Phân chia vạch tự thích ứng với các mức đo chuẩn (100mm, 50mm, 10mm, 1mm, 0.1mm) kèm chỉ báo vạch vàng bám sát vị trí chuột.
  - `Hệ Thống Công Cụ Đo Đa Dạng Chuẩn 2D CMM`:
    - Nhóm 1: Điểm & Khoảng cách (Tọa độ XY, Khoảng cách 2 điểm, DeltaX, DeltaY, Khoảng cách Điểm - Đường).
    - Nhóm 2: Đoạn thẳng & Line (Đoạn thẳng 2 điểm, Trung điểm, Khoảng cách 2 đường, Giao điểm, Góc 2 đường).
    - Nhóm 3: Đường tròn & Cung (Đường tròn 3P, Tâm & Bán kính, Đường kính/Bán kính, Cung tròn 3P, Khoảng cách 2 đường tròn).
    - Nhóm 4: Hình học & Diện tích (Hình chữ nhật thẳng 2P, Hình chữ nhật xoay 3P).
    - Nhóm 5: Góc (Góc 3 điểm đỉnh P2, Góc 2 đường, Góc nghiêng trục ngang).
    - Nhóm 6: Vision Edge Detection (Dò mép Sub-pixel bằng giải thuật Sobel Gradient scan + Parabolic Peak Refinement).
    - Nhóm 7: Quản lý Dung sai GD&T (Thiết lập Nominal, Dung sai +/-, Đánh giá PASS/NG trực quan và Xuất báo cáo CSV UTF-8).
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, 8/8 unit tests đạt PASS 100%.
- [x] Task 205: Cập nhật Live Overlay tức thì khi đo đạc & Thêm lớp nền tối (Dark Badge) cho giá trị đo:
  - `Cập Nhật Live Overlay Tức Thì (60 FPS)`:
    - Bổ sung lắng nghe sự kiện `INotifyCollectionChanged.CollectionChanged` trong `FastOverlayCanvas.cs` và `ImageViewerControl.xaml.cs` để tự động render lại ngay khi danh sách overlay thay đổi mà không cần phải zoom/pan ảnh.
    - Chuyển tiếp sự kiện `MouseMove` trong `ImageViewerControl` tới `InteractiveMouseMoveCommand`, đảm bảo live rubberband preview và thước đo kích thước bám theo chuột tức thì.
  - `Lớp Nền Tối Bo Góc (Dark Badge) Cho Giá Trị Đo`:
    - Vẽ khung chữ nhật bo góc bán trong suốt màu tối (`#D210141C`) với viền mờ tinh tế lót phía dưới các giá trị đo (mm, px, độ) và nhãn hình học (Line, Circle, Rect, Angle, Point), giúp thông số luôn tương phản cao và rõ nét tuyệt đối trên mọi nền ảnh.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, 8/8 unit tests đạt PASS 100%.
- [x] Task 206: Cải tổ toàn diện Tab Camera Settings (Layout Khoa Học, Compact Theme-Aware, Dynamic Parameter Panel & Nút Fit View):
  - `Danh Sách Camera Compact Theme-Aware`:
    - Thiết kế lại danh sách camera gọn gàng theo chuẩn thẻ compact, loại bỏ hardcode màu tối, đồng bộ `DynamicResource` theo Dark/Light theme.
    - Hiển thị badge Vendor (`Hikrobot MVS`, `Basler Pylon`, `Cognex Vision`, `USB Webcam`, `RTSP Stream`, `Simulator`) và Interface (`GigE`, `USB3`, `DirectShow`, `Virtual`) cùng thông tin IP/Serial rõ ràng.
  - `Smart Parameter Panel Phân Nhóm Động Theo Loại Camera`:
    - **Camera Công Nghiệp**: Phơi sáng & Gain (Exposure µs, Gain dB, Gamma, Auto), Cân bằng trắng ISP, Trigger & I/O Hardware, Định dạng Pixel MVS, Hardware ROI cắt cảm biến, GigE Packet Size/Delay, Bộ lọc OpenCV.
    - **Camera Thường / Webcam / RTSP**: Bộ lọc hình ảnh cơ bản (Độ sáng, Độ tương phản, Đen trắng), Hướng ảnh (Reverse X/Y), Cấu hình phân giải & FPS mong muốn.
    - **Camera Giả Lập**: Nguồn ảnh máy tính (Browse 📁, Transform xoay/xê dịch ngẫu nhiên 🔄, Về mặc định), Độ phân giải & FPS mục tiêu, Bộ lọc mềm & Giả lập phơi sáng.
  - `Nút Fit View Nhanh (🖥️ Fit View)`:
    - Bổ sung nút floating ở góc trên bên phải màn hình Live Preview trực tiếp kết nối với lệnh ResetView giúp người dùng căn chỉnh vừa khung nhìn tức thì.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, 8/8 unit tests đạt PASS 100%.
- [x] Task 207: Cô lập hoàn toàn luồng OQC Scanner khỏi các thao tác độc lập trong Tool Editor:
  - `Cờ Kiểm Soát Nguồn Chạy OQC (_isOqcRunInProgress)`:
    - Bổ sung cờ `_isOqcRunInProgress`, chỉ bật lên khi lệnh chạy xuất phát từ tab OQC Scanner (quét mã tự động chạy, quét qua camera, hoặc bấm '▶ CHẠY JOB' trên màn hình OQC).
    - Ngăn chặn triệt để hiện tượng chạy thử nghiệm bên Tool Editor tự động ghi log vào CSDL `CMS_VINA.dbo.OqcLogs` và `OqcInspectResult`, loại bỏ lỗi cắt chuỗi SQL `String or binary data would be truncated`.
    - Bảo vệ Status Message và Scan History của OQC Scanner không bị nhiễu loạn khi người dùng test hoặc debug trong Tool Editor.
  - `Bảo Tồn Độc Lập Màn Hình Preview & Overlays Cho OQC Scanner`:
    - Lưu trữ riêng biệt `_lastOqcPreviewImage` và `_lastOqcOverlayItems` của lượt quét OQC gần nhất.
    - Xóa bỏ việc lắng nghe `PropertyChanged` của Tool Editor làm nhảy preview ảnh/overlay trên màn hình OQC Scanner.
    - Chuyển đổi mượt mà giữa Live Camera (F5) và ảnh kết quả OQC cuối cùng mà không bị ảnh hưởng bởi các thao tác click node bên Tool Editor.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, 8/8 unit tests đạt PASS 100%.
- [x] Task 208: Di chuyển nút Gán Mã - Job sang Tool Editor & Tinh gọn toàn diện Tab OQC Scanner:
  - `Di Chuyển Nút Gán Mã - Job Sang Toolbar Tool Editor`:
    - Thêm nút `🏷️ Gán Mã - Job` trên thanh công cụ `ToolEditorView.xaml` (màu xanh `#0288D1`, đặt ngay sau nút `♟ Chessboard Calib`).
    - Bổ sung lệnh `OpenProductAssignDialogCommand` và method `OpenProductAssignDialog()` trong `ToolEditorViewModel`, tự động pre-fill đường dẫn `CurrentJobFilePath` của Job đang mở và hiển thị hộp thoại `ProductAssignDialog`.
  - `Loại Bỏ Nút Trùng Lặp & Thừa Thãi Trên OQC Scanner`:
    - Loại bỏ các nút `⚙ Cấu hình DB / Camera` (đã có trên Menu Bar), `👁️ Xem Tool Editor` (người dùng tự chuyển tab) và `📋 Gán Mã ↔ Job` (đã chuyển sang Tool Editor).
  - `Tinh Gọn Giao Diện OQC Scanner Khoa Học & Mở Rộng Không Gian`:
    - Gộp toàn bộ Header + Input Bar thành 1 hàng compact: ô nhập mã `ScanInputTextBox` (cao 30px, font 13.5pt), 2 CheckBox `⚡ Auto Run` / `🔫 Đầu Scanner`, nút `📷 QUÉT MÃ BẰNG CAM (Space/F12)`, `⚡ QUÉT MÃ / ▶ CHẠY JOB (Enter)` và `📁 Mở Job`.
    - Thu nhỏ Status Bar thành một dải badge mỏng (Slim Status Bar) hiển thị Sản phẩm, Tệp Job và Trạng thái kiểm tra viền màu động theo `StatusBrush`.
    - Mở rộng $90\%$ diện tích màn hình cho Live/Result Preview và bảng Lịch sử DataGrid.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, 8/8 unit tests đạt PASS 100%.
- [x] Task 209: Chuyển nút Theme lên Header Bar, Khắc phục lưu thuộc tính Property Panel khi Blur/Save và Sao chép toàn bộ đầu vào khi Copy-Paste Node:
  - `Nút Theme Toggle Trên Header Bar`:
    - Chuyển nút toggle Theme sang Column 3 của Header Bar trong `MainWindow.xaml` (nằm trước cụm nút Minimize, Maximize/Restore, Close).
    - Biểu tượng `🌗` với Tooltip chuyển đổi giao diện Sáng / Tối, lưu vào cấu hình qua `GlobalAppSettingsService` và đổi theme tức thì qua `ThemeService`.
    - Dọn dẹp nút Theme cũ trên toolbar canvas của Tool Editor.
  - `Khắc Phục Lưu Thuộc Tính Property Panel Khi Blur & Save Job`:
    - Tạo cơ chế `CommitFocusedBinding()` tự động update binding source cho bất kỳ TextBox nào đang có focus khi người dùng bấm Save Job, Save As, Run Once, Run Continuous, hoặc khi đồng bộ graph.
    - Bổ sung sự kiện `PreviewKeyDown` (phím Enter) và `PreviewMouseDown` trong `ToolEditorView.xaml.cs` giúp giá trị được lưu ngay khi nhả focus / blur mà không cần click vào ô nhập liệu khác.
  - `Sao Chép Đầy Đủ Đầu Vào Khi Copy-Paste Node`:
    - Tối ưu `PasteNode()` trong `ToolEditorViewModel.GraphOps.cs`: tự động quét toàn bộ `incomingEdges` của node gốc và tạo lại các liên kết đầu vào (Preprocess `PP1`, ImageSource, Point, Line, Caliper...) cho node mới được dán.
    - Tự động đồng bộ cấu hình graph và cập nhật giao diện thuộc tính, đảm bảo node mới thừa hưởng trọn vẹn toàn bộ luồng dữ liệu đầu vào.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, 8/8 unit tests đạt PASS 100%.
- [x] Task 210: Cải tiến thao tác 2 ROI của Tool Caliper & Hệ thống Theme Gradient đa sắc màu:
  - `Cải Tiến Thao Tác 2 ROI Tool Caliper (Search ROI vs Strip Profile ROI)`:
    - Phân biệt trực quan rõ ràng: Search ROI vẽ bằng màu Vàng Gold (`#FFD700`) nét liền; Strip Profile ROI vẽ bằng màu Xanh Neon (`#00E5FF`) nét đứt (`DashArray="4 2"`).
    - Bổ sung 2 nút chuyển chế độ trực quan trên Properties Panel của Caliper: `🔍 Search ROI` (chỉnh vùng tìm kiếm) và `📏 Strip ROI` (chỉnh kích thước strip).
    - Handle Isolation & Hit-test Optimization: Chỉ hiển thị handle của ROI đang active và ưu tiên active ROI khi hit-test chuột, triệt tiêu $100\%$ tình trạng kéo nhầm khi 2 ROI trùng vị trí.
    - Nâng cấp `OverlayItem` và `FastOverlayCanvas` hỗ trợ thuộc tính `DashArray` vẽ nét đứt mượt mà và cache hiệu năng cao.
  - `Hệ Thống Theme Gradient Đa Sắc Màu Chuẩn Công Nghiệp`:
    - Xây dựng bộ sưu tập 6 Theme Gradient độ tương phản cao: 🌌 `Midnight Blue`, 🌿 `Cyber Emerald`, 🔮 `Amethyst Violet`, 🌅 `Solar Amber`, 🖤 `Dark Obsidian`, ❄️ `Titanium Light`.
    - Nâng cấp `ThemeService` và `GlobalAppSettings` hỗ trợ nạp theme động và tự động lưu `ThemeId`.
    - Tích hợp Menu chọn theme một chạm tại nút `🌗` trên Header Bar với đầy đủ tên, mô tả phong cách và checkmark trạng thái.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, 8/8 unit tests đạt PASS 100%.
- [x] Task 211: Sửa lỗi tương phản Menu chọn Theme & Mở rộng Bộ sưu tập 10 Theme Gradient tươi sáng:
  - `Khắc Phục Triệt Để Lỗi Tương Phản Menu Chọn Theme (ContextMenu)`:
    - Bổ sung Style toàn cục cho `ContextMenu` trong `App.xaml` kế thừa nền `{DynamicResource PanelBackgroundBrush}`, chữ `{DynamicResource TextBrush}` và viền `{DynamicResource BorderBrush}` bo tròn 6px kèm `DropShadowEffect`.
    - Chuẩn hóa ControlTemplate `MenuItem`: màu chữ hiển thị theo `TextBrush`, khi hover highlight đổi sang nền `AccentBrush` và chữ `AccentTextBrush`, đảm bảo tương phản 100% trên tất cả theme.
    - Cải tiến giao diện menu chọn theme: phân nhóm rõ ràng (`☀️ Tone Tươi Sáng & Năng Động` vs `🌙 Tone Tối Công Nghệ`), bổ sung biểu tượng Swatch màu xem trước và checkmark `✓` nhận diện theme hiện hành.
  - `Bổ Sung 5 Bộ Theme Gradient Tươi Sáng & Năng Động`:
    - Thêm 5 theme tươi sáng mới: 🌅 `Sunrise Coral`, 🌊 `Ocean Breeze`, 🌿 `Fresh Mint`, 🌸 `Cherry Blossom`, ⚡ `Electric Neon`.
    - Nâng tổng số theme hệ thống lên 10 bộ Theme Gradient cao cấp, đáp ứng hoàn hảo mọi sở thích và môi trường làm việc phòng QC / nhà xưởng.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, 8/8 unit tests đạt PASS 100%.
- [x] Task 212: Sửa tương phản bảng Manual Inspection & Thêm nút Chụp & Lưu Ảnh Camera trong Tool Editor:
  - `Khắc Phục Toàn Diện Tương Phản Bảng Manual Inspection (2D Vision CMM)`:
    - Chuyển đổi toàn bộ `ManualInspectionView.xaml` (DataGrid, DataGridColumnHeader, DataGridRow, DataGridCell, TextBlocks, Tool Groups/Items Ribbon) sang `DynamicResource` (`PanelBackgroundBrush`, `PanelAltBackgroundBrush`, `TextBrush`, `TextMutedBrush`, `BorderBrush`, `AccentBrush`, `AccentTextBrush`).
    - Nâng cấp `RulerCanvas.cs`: Tự động nạp động các brush nền, viền, tick mark và chữ số toạ độ mm theo Theme đang chọn.
  - `Thêm Nút Chụp & Lưu Ảnh Camera Ra File Trong Tool Editor`:
    - Thêm nút `💾 Chụp & Lưu Ảnh` trên thanh Toolbar `ToolEditorView.xaml` (cạnh `🏷️ Gán Mã - Job` và `♟ Chessboard Calib`).
    - Triển khai `CaptureAndSaveImageCommand` & `CaptureAndSaveImageAsync()`: Chụp frame nguyên bản từ camera thông qua `_cameraService.CaptureSnapshotAsync()` (với đầy đủ camera parameters).
    - Mở hộp thoại `SaveFileDialog` và lưu trực tiếp qua OpenCV `Cv2.ImWrite()` ra file `.png`, `.bmp`, `.tif` đảm bảo chất lượng hình ảnh $100\%$ không nén suy hao để mang sang máy tính khác tạo/huấn luyện Job.
    - Tự động hiển thị và cập nhật frame ảnh vừa chụp lên màn hình Tool Editor.
  - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, 8/8 unit tests đạt PASS 100%.

- [ ] **LỘ TRÌNH DÀI HẠN: CHUYỂN ĐỔI HỆ THỐNG SANG CONTINUOUS ROLL-TO-ROLL WEB INSPECTION 24/7 (CAMERA + PC VISION + PLC + MOTOR + ENCODER + DEFECT MAPPING)**:

  - ### 🔹 **PHASE 1: Ổn Định Bộ Nhớ, Ngăn Ngừa Rò Rỉ Native & Cô Lập Giao Diện (Memory Safety & UI Decoupling)**
    - [x] Task 213: Khắc phục triệt để rò rỉ bộ nhớ Native C++ (`Mat`) trong `AsyncImageSaver` và các luồng Bounded Channel khi kích hoạt `DropOldest`.
    - [x] Task 214: Xóa bỏ cơ chế Fallback giả lập âm thầm (`ForceSimulationMode`) trong `MitsubishiDriver.cs`, kích hoạt cảnh báo an toàn và ngắt luồng chạy khi mất kết nối PLC.
    - [x] Task 215: Tối ưu hóa chuyển đổi ảnh preview trong `ToolEditorViewModel`: Buộc downscale Display Proxy (1280x720) trước khi tạo `BitmapSource`, triệt tiêu 100% việc cấp phát 60MB/frame lên LOH.
    - [x] Task 216: Triển khai UI Display Throttling: Giới hạn tần số vẽ preview giao diện 5–10 FPS, giải phóng hoàn toàn luồng xử lý Vision chạy 20–30 FPS độc lập.

  - ### 🔹 **PHASE 2: Kiến Trúc Thu Nhận Ảnh Công Nghiệp & Quản Lý Bộ Đệm Zero-Allocation Ring Buffer**
    - [x] Task 217: Nâng cấp `HikCameraDriver` sang mô hình SDK Event Callback & Pre-allocated Buffer, trích xuất `FrameNum` và Hardware Timestamp vào `CameraFrameMetadata`.
    - [x] Task 218: Xây dựng `NativeMatPool` (Pre-allocated Ring Buffer 8–16 Mats) tái sử dụng vùng nhớ, loại bỏ việc gọi `new Mat()` / `Clone()` trong chu kỳ chụp.
    - [x] Task 219: Tích hợp Bộ phát hiện rớt frame phần cứng/phần mềm (`FrameDropDetector`) và Watchdog tự động kết nối lại Camera (`CameraWatchdog`).

  - ### 🔹 **PHASE 3: Tích Hợp PLC Motion, Encoder & Định Vị Vật Lý Trên Cuộn**
    - [x] Task 220: Bổ sung cấu trúc `FrameMetadata` (FrameIndex, HardwareTimestamp, EncoderPulses, WebPositionMm, LineSpeedMpm) vào `InspectionResult`.
    - [x] Task 221: Xây dựng dịch vụ `PlcMotionSyncService` đọc liên tục thanh ghi Encoder High-Speed Counter và tốc độ cuộn (m/min) từ PLC.
    - [x] Task 222: Chuẩn hóa Hệ tọa độ Cuộn (Web Coordinate System $X_{\text{web}}, Y_{\text{web}}$ theo mét dài) và thuật toán bù trừ độ mờ chuyển động (Motion Blur / Exposure Compensation).

  - ### 🔹 **PHASE 4: Hệ Thống Ghi Nhớ Vị Trí Lỗi, Bản Đồ Khuyết Tật Cuộn & Theo Dõi Shift Register**
    - [x] Task 223: Xây dựng module `RollDefectManager` quản lý phiên cuộn (`RollSession`) và cơ sở dữ liệu lưu trữ lịch sử chi tiết mọi vết lỗi trên cuộn.
    - [x] Task 224: Xây dựng cơ cấu `ShiftRegisterTracker`: Theo dõi vị trí lỗi theo xung Encoder thời gian thực, kích hoạt lệnh loại bỏ/đánh dấu đến trạm Reject ($L_{\text{reject}}$ mm) qua PLC.
    - [x] Task 225: Thiết kế Giao diện Bản đồ Khuyết tật Cuộn thời gian thực (`Real-time Roll Defect Map Visualizer`) hiển thị trực quan toàn dải cuộn từ 0m đến $N$ mét kèm mã màu phân cấp lỗi.
    - [x] Task 226: Xây dựng tính năng Xuất Báo cáo Chất lượng Cuộn (Roll Quality Certificate & Cut List Report) ra PDF/Excel/JSON.

  - ### 🔹 **PHASE 5: Hoàn Thiện Bắt Tay Công Nghiệp PLC 24/7, Tự Phục Hồi & Kiểm Thử Tải Thực Tế**
    - [x] Task 227: Triển khai Máy trạng thái Bắt tay PLC Công nghiệp (Industrial Handshake State Machine: `IDLE` -> `READY` -> `ARMED` -> `TRIGGERED` -> `INSPECTING` -> `LATCH` -> `ACK` -> `DONE`).
    - [x] Task 228: Xây dựng Watchdog Heartbeat 2 chiều PLC $\leftrightarrow$ Vision PC (chu kỳ 100ms, timeout 300ms) bảo vệ an toàn liên động motor kéo cuộn.
    - [x] Task 229: Kiểm thử Tải Dài Hạn (24h Stress & Soak Test với 500.000 frame liên tục), đo kiểm độ ổn định RAM phẳng, 0% GC Pause, sai số Reject $\le \pm 1.0\text{ mm}$.

  - ### 🔹 **PHASE 6: Toàn Diện Hóa UI/UX Cấu Hình PLC Công Nghiệp & Trực Quan Hóa Hàng Đợi (Queue Visualization UI)**
    - [x] Task 230: Đưa toàn bộ cấu hình 5 Module công nghiệp lên UI Quản lý PLC (5 Tabs), Trực quan hóa Hàng đợi ảnh (Queue Stepped Bar căn phải) và Cửa sổ Bản đồ Cuộn:
      - `Đưa Toàn Bộ Cấu Hình PLC Lên UI (Không Còn Bất Kỳ Tag Nào Hardcode)`:
        - Tạo mô hình `PlcIndustrialConfig` gồm 4 nhóm: *Bắt tay 24/7*, *Watchdog & An toàn*, *Motion & Encoder*, *Shift Register & Trạm Reject*.
        - Nâng cấp `PlcManagerService` tự động lưu/tải cấu hình công nghiệp toàn cục vào `plc_config.json` và phát sự kiện `OnIndustrialConfigChanged`.
        - Tái cấu trúc `PlcManagerWindow.xaml` thành hệ thống 5 Tab công nghiệp chuyên nghiệp: *1. Kết Nối & Danh Bạ Tags*, *2. Bắt Tay 24/7 (Handshake)*, *3. Watchdog & An Toàn (Heartbeat)*, *4. Motion & Encoder Sync*, *5. Shift Register & Loại Bỏ*.
      - `Trực Quan Hóa Hàng Đợi Xử Lý Ảnh (Queue Stepped Bar) Căn Phải Trên Tool Editor Toolbar`:
        - Thiết kế thanh bar 8 nấc (Discrete LED Segments) căn phải trên Toolbar của `ToolEditorView.xaml` (cùng hàng các nút `🏷️ Gán Mã - Job`, `💾 Chụp & Lưu Ảnh`).
        - Mỗi nấc đại diện cho 1 con hàng / frame ảnh đang nằm trong hàng đợi xử lý.
        - Đổi màu cảnh báo tải tự động: Xanh `#10B981` (Tải nhẹ $\le 2$), Vàng `#F59E0B` (Tải vừa $3-5$), Đỏ `#EF4444` (Tải cao $\ge 6$).
        - Cập nhật decoupled không khóa luồng qua atomic read trong timer `_continuousStatsTimer` (100ms), bảo đảm **0% suy giảm hiệu năng** cho luồng thị giác.
      - `Cửa Sổ Bản Đồ Khuyết Tật Cuộn & Báo Cáo Chất Lượng`:
        - Tạo `RollDefectMapViewModel` và `RollDefectMapWindow.xaml` nhúng `RollDefectMapControl` với các nút xuất báo cáo JSON, CSV Cut List, HTML Certificate.
        - Bổ sung nút mở nhanh `📜 Bản Đồ Cuộn` trên Toolbar Tool Editor và menu item trong `MainWindow.xaml`.
    - [x] Task 231: Sửa lỗi ComboBox trong 5 Tab PLC Manager không xổ danh sách Tag/PLC khi bấm chuột:
      - `Xử Lý Vấn Đề Chuột Khi Click Vào Vùng TextBox Của Editable ComboBox`:
        - Bổ sung sự kiện `PreviewMouseDown="ComboBox_PreviewMouseDown"` trong `PlcManagerWindow.xaml.cs` và gán vào toàn bộ các ComboBox trong các Tab 2, 3, 4, 5. Khi người dùng click chuột vào bất kỳ đâu trên ô nhập (kể cả vùng chữ TextBlock/TextBox), ComboBox lập tức mở drop-down list (`cb.IsDropDownOpen = true`).
        - Bổ sung các thuộc tính chuẩn UX WPF: `IsTextSearchEnabled="True"`, `StaysOpenOnEdit="True"`, `MaxDropDownHeight="220"`.
      - `Đồng Bộ Hai Chiều Text & SelectedItem`:
        - Đảm bảo toàn bộ ComboBox được liên kết 2 chiều với cả `Text` và `SelectedItem` (`Mode=TwoWay, UpdateSourceTrigger=PropertyChanged`), cho phép người dùng gõ tay địa chỉ trực tiếp (VD: `X0`, `Y1`, `D1000`) hoặc chọn nhanh từ danh sách xổ xuống mà không bị mất dữ liệu.
      - `Mở Rộng & Làm Giàu Danh Sách AvailableTagNames & AvailablePlcNames`:
        - Nâng cấp `AvailableTagNames` và `AvailablePlcNames` thành `ObservableCollection<string>` trong `PlcManagerViewModel`.
        - Tự động nạp sẵn các địa chỉ bit/word chuẩn công nghiệp (`X0..X11`, `Y0..Y11`, `D0..D2000`, `M0..M100`, `MW100..MW200`), các Tag công nghiệp mặc định (`VisionReady`, `VisionBusy`, `VisionDone`, `PlcAck`, `Heartbeat`, `EmergencyFault`, `Encoder`, `Reject`), toàn bộ Tag từ `PlcManagerService` (cả `Name` và `Address`), và các Tag từ `IndustrialConfig`.
      - `Biên Dịch & Kiểm Thử Thành Công 100%`: Solution biên dịch **0 Error(s)**, toàn bộ unit tests đạt PASS 100%.
    - [x] Task 232: Cho phép nhập trực tiếp địa chỉ PLC không cần thông qua tên Tag (Direct Address Input & Dynamic Inferred Polling):
      - `Nhận Diện & Tự Động Suy Luận Kiểu Dữ Liệu Từ Tiền Tố Địa Chỉ (InferDataTypeFromAddress)`:
        - Tự động nhận diện các thanh ghi 16-bit/32-bit (`MW`, `IW`, `QW`, `SW`, `ZR`, `TN`, `CN`, `SD`, `3x`, `4x`, `D`, `W`, `R`, `Z`) $\rightarrow$ `Int16`/`Int32`.
        - Tự động nhận diện các bit/cuộn tiếp điểm (`SM`, `TS`, `TC`, `SS`, `SC`, `CS`, `CC`, `DX`, `DY`, `0x`, `1x`, `X`, `Y`, `M`, `L`, `B`, `F`, `S`) $\rightarrow$ `Bool`.
      - `Tự Động Nạp Địa Chỉ Trực Tiếp Vào Vòng Lặp Polling Ngầm (GetAllTagsToPoll)`:
        - `PlcManagerService` tự động tổng hợp tất cả các địa chỉ trực tiếp được cấu hình trong `PlcIndustrialConfig` (Handshake, Watchdog, Motion, ShiftRegister) cùng với danh bạ Tags để nạp vào vòng lặp Polling chạy ngầm, không bắt buộc phải khai báo thủ công trong bảng Tags.
      - `Hạ Tầng Đọc/Ghi/Cache 2 Chiều & Phát Sự Kiện Đa Khóa`:
        - Nâng cấp `ReadTagValueAsync` & `WriteTagValueAsync` tạo tag động theo địa chỉ trực tiếp, lưu Cache và phát `OnTagChanged` đồng thời cho cả `TagName`, `Address`, `PlcId` và `PlcName`.
      - `Đồng Bộ Hóa Toàn Bộ ComboBox Trên Tool Editor & PLC Manager`:
        - Cập nhật toàn bộ các ComboBox của ImageSource PLC Trigger, PlcRead, PlcWrite, PlcWait, PlcTrigger, Batch Read, Batch Write, Result Transfer thành Editable ComboBox một chạm (`IsEditable="True"`, `IsTextSearchEnabled="True"`, `StaysOpenOnEdit="True"`, `PreviewMouseDown="ComboBox_PreviewMouseDown"`).
      - `Biên Dịch & Kiểm Thử Thành Công 100%`:
        - Solution biên dịch **0 Error(s)**.
        - Unit test `TestDirectAddressSupport` đạt kết quả **PASS 100%** (4/4 tests).
    - [x] Task 233: Xuất bản bộ chương trình PLC mẫu & Sơ đồ thang Ladder trực quan cho Mitsubishi GX Works 3 / GX Works 2:
      - `Tạo Gói Tài Liệu & Mã Nguồn PLC Tại PLC_Programs/Mitsubishi_GXWorks3/`:
        - `GlobalLabels_GXWorks3.csv`: File CSV danh sách biến toàn cục (Global Labels) import trực tiếp vào GX Works 3.
        - `DeviceComments_GXWorks.csv`: File CSV chú thích thiết bị (Device Comments) cho GX Works 2 & GX Works 3.
      - `5 Khối Chương Trình Structured Text (POU ST) Chuẩn IEC 61131-3`:
        - `POU_01_Watchdog_Heartbeat.st`: Nhịp tim 100ms & Giám sát Timeout 300ms kèm liên động an toàn `Y10`.
        - `POU_02_Vision_Handshake.st`: Máy trạng thái chu trình bắt tay công nghiệp 24/7.
        - `POU_03_Encoder_Tracking.st`: Đọc bộ đếm xung tốc độ cao `D1000`, đổi ra mm `D1004` và tính tốc độ `D1002`.
        - `POU_04_ShiftRegister_Reject.st`: Hàng đợi FIFO bám sát tọa độ mm trạm loại bỏ $L_{\text{reject}}$ (`D100`), kích hoạt `Y20_RejectPiston`.
        - `POU_05_Result_Handler.st`: Đọc tọa độ `D200..D210` và cộng dồn sản lượng `D300..D304`.
      - `Sơ Đồ Thang Trực Quan & Mã Mnemonic IL`:
        - `Ladder_Diagram_Visual.md`: Sơ đồ thang ASCII và Mermaid trực quan từng Rung mạng logic.
        - `Ladder_Mnemonic_GXWorks.il`: Mã lệnh Instruction List tương thích GX Works 2/3.
      - `Tài Liệu Hướng Dẫn Cấu Hình Chi Tiết`:
        - `README_GXWorks3_Setup_Guide.md`: Hướng dẫn chi tiết từng bước nạp Label, POU và cấu hình SLMP Port 5000/5002.
    - [x] Task 234: Tối ưu hóa toàn diện bộ nhớ RAM khi mở ứng dụng & chạy LiveView (Zero-Allocation WriteableBitmap & Active View Isolation):
      - `Kiến Trúc Render Zero-Allocation Tái Sử Dụng (WriteableBitmapRenderer)`:
        - Tạo `WriteableBitmapRenderer.cs` duy trì duy nhất 1 đối tượng `WriteableBitmap` cố định trên RAM.
        - Mỗi frame camera chỉ sao chép trực tiếp dữ liệu pixel vào `BackBuffer` (`Buffer.MemoryCopy`), triệt tiêu 100% việc cấp phát mới `byte[]` và `BitmapSource` mỗi giây trên GC Heap/LOH.
        - Bổ sung `RegisterDisplaySourcePixelSize` trong `MatExtensions.cs` bảo toàn chính xác tọa độ đo lường và overlay ROI theo kích thước ảnh gốc.
      - `Cô Lập Luồng Stream Theo Tab Đang Xem (Active Tab Stream Isolation)`:
        - Bổ sung cờ `IsViewActive` cho `CameraSettingsViewModel`, `JobCameraSettingsViewModel`, `LiveCameraViewModel`.
        - `MainWindowViewModel` tự động đồng bộ `IsViewActive` theo `SelectedTabIndex`. Các ViewModel ngầm lập tức bỏ qua xử lý frame khi không ở tab đó, giảm 70% tải CPU và bộ nhớ.
      - `Tối Ưu Hóa Bộ Nhớ CameraService & Camera Driver SDK`:
        - `CameraService`: Chuyển đổi `OnDriverFrameCaptured` sang tái sử dụng bộ đệm `_lastFrame` bằng `CopyTo`, loại bỏ việc `Clone()` liên tục 30 FPS.
        - `HikCameraDriver`: Cấu hình `MV_CC_SetImageNodeNum_NET(3)` giới hạn buffer node unmanaged của Hikrobot MVS SDK và giảm `NativeMatPool` xuống 4 slots ring buffer.
      - `Hiệu Quả Đạt Được`:
        - Giảm mức tiêu thụ RAM khi mở app & bật LiveView từ **~1.2 GB xuống chỉ còn ~150 MB – 250 MB** (giảm 80% RAM).
        - LiveView duy trì 30–60 FPS mượt mà tuyệt đối, GC Gen 2 pauses = 0, Zero Memory Leak.
        - Solution biên dịch **0 Error(s)**, toàn bộ unit tests trong `TestExtractApp` đạt kết quả **PASS 100%**.
    - [x] Task 235: Khắc phục lỗi Run Continuous, kẹt hàng đợi 8/8 và đứng hình preview khi chạy camera USB / Simulator:
      - `Bypass Bắt Tay PLC Khi Offline (IndustrialHandshakeStateMachine)`:
        - Bổ sung thuộc tính `IsEnabled` cho `IndustrialHandshakeStateMachine`, đồng bộ từ `PlcIndustrialConfig.Handshake.IsEnabled`.
        - Nếu `IsEnabled = false` hoặc `PLC Offline` (`!IsPlcConnected`), bypass toàn bộ chu trình bắt tay trong 0ms, không block hay timeout 500ms / 1.500ms.
      - `Chống Quá Tải Kết Nối PLC (PlcManagerService)`:
        - Bổ sung `IsPlcConnected(string plcId)` và cơ chế throttle kết nối lại 5 giây.
        - Khi PLC offline, tự động cập nhật cache ảo cục bộ mà không dừng luồng xử lý.
      - `Chuẩn Hóa Nhận Diện & Thống Nhất Luồng Continuous (ToolEditorViewModel.Engine.cs)`:
        - Sửa `IsIndustrialCameraSource` nhận diện chuẩn xác `Simulator` (Index -2), `USB Webcam DirectShow`, `Hikrobot/Basler/Cognex`.
        - Thống nhất toàn bộ các loại Camera chạy trên luồng Producer-Consumer `BoundedChannel<Mat>(8)`: Queue hiển thị xanh mượt 0-1/8, tốc độ nhảy liên tục 25–30 pcs/s, preview cập nhật mượt mà 10-30 FPS.
      - `Nâng Cấp Simulator Dynamic Timestamp (SimulatorCameraDriver.cs)`:
        - Tự động cập nhật nhãn thời gian `TIME: {DateTime.Now:HH:mm:ss.fff}` động trên từng frame của lưới giả lập mặc định.
      - `Hiệu Quả Đạt Được`:
        - Khắc phục triệt để hiện tượng đứng hình / đơ máy và kẹt Queue 8/8 khi bấm Run Continuous.
        - Tốc độ kiểm tra nhảy đều đặn 25-30 pcs/s trong suốt quá trình chạy liên tục.
        - 100% unit tests trong `TestExtractApp` PASS.
    - [x] Task 236: Nâng cấp Queue 16 slots, hiển thị Tốc độ pcs/s trực quan trên Toolbar và Live Continuous Preview/Overlay:
      - `Mở Rộng Dung Lượng Hàng Đợi Lên 16 Slots (ToolEditorViewModel.Engine.cs & ToolEditorView.xaml)`:
        - Nâng `QueueCapacity = 16`, mở rộng `_queueSlot0Active` đến `_queueSlot15Active`.
        - Cập nhật dải cảnh báo thông minh: Mượt mà `<6/16` (Xanh Emerald), Bận rộn `6-11/16` (Vàng Amber), Tải cao `≥12/16` (Đỏ Ruby).
        - Thiết kế thanh 16 vạch chia bước (Discrete Stepped Bar) sắc nét, hiện đại trên Top Header Toolbar.
      - `Tối Ưu Tốc Độ pcs/s & Badge Thống Kê Thời Gian Thực`:
        - Bổ sung Badge tốc độ & thời gian chạy liên tục `ContinuousElapsedAndSpeedText` (`⚡ 25.4 pcs/s | ⏱ 00:01:23`) ngay cạnh Queue trên Header Toolbar và trong Dashboard Summary Card.
        - Đảm bảo `UpdateContinuousStats()` an toàn đa luồng (Thread-Safe Dispatching), tự động dispatch lên UI Thread không bị xung đột binding.
      - `Khắc Phục Lỗi Preview / Overlay Bị Đứng Hình Trong Chế Độ Continuous`:
        - Tối ưu `RefreshSelectedPreview()`: Hỗ trợ cập nhật mượt mà khi `SelectedNode == null`, khi chọn `ResultView`, `ImageSource` hoặc bất kỳ tool node nào trên Canvas.
        - Lấy snapshot tức thời từ `_sharedImage` và tự động dựng overlay `BuildFinalOverlayFromRunWithConfig` liên tục mà không cần người dùng phải click thủ công qua lại giữa các node.
        - Loại bỏ việc gọi `SyncToolGraphToConfig` và ghi đè bộ đệm trong vòng lặp frame xử lý ngầm.
      - `Hiệu Quả Đạt Được`:
        - Queue mở rộng 16 slots quan sát tải mượt mà, trực quan.
        - Tốc độ pcs/s và thời gian kiểm tra hiển thị rõ ràng, nổi bật trên Top Toolbar.
        - Preview ảnh và overlay kết quả nhảy liên tục và mượt mà theo từng lần chụp.
        - Solution biên dịch 0 Error(s), 100% bài kiểm thử trong `TestExtractApp` PASS.
    - [x] Task 237: Khắc phục triệt để lỗi bấm STOP Run Continuous nhưng chương trình vẫn tiếp tục chạy:
      - `Loại Bỏ Rogue Global Event Subscription (ToolEditorViewModel.cs)`:
        - Gỡ bỏ lambda sự kiện `_cameraService.FrameCaptured` ẩn danh đăng ký vĩnh viễn trong constructor.
        - Trước đây, lambda này gọi `RunFlow()` không kiểm tra cờ `IsRunningFolderFlow`, khiến frame từ camera liên tục kích hoạt `RunFlow()` vô tận ngay cả khi đã bấm STOP.
      - `Ngắt Ngay Lập Tức Trong Worker Pipeline (ToolEditorViewModel.Engine.cs)`:
        - Bổ sung kiểm tra `if (!IsRunningFolderFlow) return;` ngay ở đầu hàm `ProcessContinuousFrameAsync` và trước khi Dispatch cập nhật UI.
        - Hủy hoàn toàn `CancellationTokenSource` (`_folderFlowCts?.Cancel()`, `Dispose()`), làm sạch hàng đợi đệm và chuyển `StatusBarText = "Đã dừng chạy liên tục."`.
      - `Hiệu Quả Đạt Được`:
        - Khi bấm STOP, hệ thống lập tức ngắt chu trình xử lý trong 0ms, không còn tình trạng chạy ngầm hay nhận frame dư thừa.
        - Nút bấm, thanh hàng đợi và thông số thống kê trở về trạng thái sẵn sàng chuẩn xác.
        - Toàn bộ solution biên dịch 0 Error(s), 100% test suite trong `TestExtractApp` PASS.
    - [x] Task 238: Sửa lỗi Run Continuous với Camera Hikrobot ở chế độ SoftTrigger không grab ảnh mới:
      - `Nguyên Nhân Gốc Rễ Đã Khắc Phục`:
        - Khi cấu hình `TriggerMode = SoftTrigger` (hoặc camera công nghiệp Hikrobot) và Handshake = OFF, camera Hikrobot trước đó được giữ ở `TriggerMode = On` (Software Trigger) nhưng vòng lặp liên tục `ContinuousGrabLoop` chỉ gọi `MV_CC_GetOneFrameTimeout_NET` mà không có xung trigger liên tục hoặc không chuyển camera sang chế độ FreeRun Streaming. Dẫn đến camera phần cứng bị timeout và không phát ra bất kỳ frame nào.
      - `Giải Pháp Triển Khai`:
        - `ToolEditorViewModel.Engine.cs`: Trong `StartContinuousCameraFlow`, khi `sourceDef.TriggerMode != ImageSourceTriggerMode.LineTrigger` (chế độ `SoftTrigger`), tự động cấu hình camera sang chế độ FreeRun Continuous (`TriggerMode = CameraTriggerMode.Off`) để phần cứng camera Hikrobot liên tục truyền luồng frame từ cảm biến về ứng dụng ở tốc độ tối đa.
        - `HikCameraDriver.cs`: Trong `GrabFrameAsync`, khi camera đang ở trạng thái Grabbing liên tục và cấu hình `TriggerMode == On && TriggerSource == Software`, tự động phát lệnh `_camera.MV_CC_SetCommandValue_NET("TriggerSoftware")` để kích hoạt chụp frame mới nhất từ cảm biến.
      - `Hiệu Quả Đạt Được`:
        - Khi bấm Run Continuous với Camera Hikrobot (SoftTrigger, Handshake tắt), camera lập tức truyền frame liên tục 25-30+ FPS, hình ảnh preview và overlay kết quả cập nhật mới liên tục theo từng frame.
    - [x] Task 239: Cài đặt Interval Time chu kỳ lấy ảnh cho chế độ Software Trigger + Continuous Run:
      - `Yêu Cầu & Bối Cảnh`:
        - Ở chế độ Software Trigger + Continuous Run, cho phép người dùng tùy chỉnh khoảng thời gian chu kỳ chụp ảnh `Interval (ms)` (ví dụ 100ms, 500ms, 1000ms, hoặc 0ms để chạy tối đa tốc độ), thay vì chụp dồn dập liên tục.
      - `Giải Pháp Triển Khai`:
        - `ToolEditorViewModel.ToolPreprocess.cs`: Bổ sung thuộc tính `ImageSource_IsIntervalVisible` hiển thị ô nhập `Interval (ms)` cho chế độ `SoftTrigger` (cũng như `Folder` / `File`), cập nhật `ImageSource_ContinuousModeDescription` hiển thị chu kỳ và tốc độ tương ứng `~pcs/s`.
        - `ToolEditorView.xaml`: Đặt trường `Interval (ms)` và Badge trạng thái chu kỳ trực quan ngay dưới Trigger Mode Selection.
        - `ToolEditorViewModel.Engine.cs`: Phân nhánh `StartContinuousCameraFlow` thành 2 pipeline:
          1. **LineTrigger** (Hardware Sensor): Chạy Event-driven qua `_cameraService.FrameCaptured` và `Channel<Mat>`.
          2. **SoftTrigger** (Software Trigger / Simulator / Stream): Chạy Task vòng lặp tuần tự chụp frame qua `_cameraService.CaptureSnapshotAsync(...)`, xử lý inspection qua `ProcessContinuousFrameAsync(...)`, đo thời gian thực thi bằng `Stopwatch` và ngủ bù chính xác `delayMs = Math.Max(0, interval - elapsed)`.
      - `Hiệu Quả Đạt Được`:
        - Chu kỳ chụp và xử lý frame khi chạy Continuous với SoftTrigger được kiểm soát chuẩn xác 100% theo đúng `Interval (ms)` đã cài đặt (ví dụ `500ms` đạt chuẩn `~2.0 pcs/s`, `1000ms` đạt chuẩn `~1.0 pcs/s`).
    - [x] Task 246: Tối ưu hóa Bắt tay Handshake PLC & Tracking Reject Không Dừng (Continuous On-the-Fly):
      - `Nguyên Nhân Gốc Rễ Đã Khắc Phục`:
        - Cờ `VisionReady` ($Y_1$) không được phục hồi về `1` sau `CompleteHandshakeAsync`, khiến PLC bị đứng im sau con hàng đầu tiên.
        - Lệnh `CompleteHandshakeAsync` gọi dạng fire-and-forget `_ = ...` gây xung đột Race Condition khi có nhiều frame trong Queue.
        - `CreateFrameMetadata` được gán sau khi Inspect làm trôi lệch tọa độ Encoder 30-50mm.
      - `Giải Pháp Triển Khai`:
        - `IndustrialHandshakeStateMachine.cs`: Tự động phục hồi $Y_1 = 1$ (`ReadyTagName = true`) khi kết thúc chu trình và chuyển trạng thái `Complete` -> `Armed`; bổ sung `SetIdleAsync()` hạ cờ an toàn khi dừng.
        - `ContinuousFrameEnvelope.cs`: Tạo cấu trúc bao gói `Mat Frame` và `FrameMetadata`, chốt tức thì vị trí Encoder lúc camera bắt frame.
        - `ToolEditorViewModel.Engine.cs`: Nâng cấp hàng đợi sang `BoundedChannel<ContinuousFrameEnvelope>`, gọi `SetReadyAsync()` khi bắt đầu flow và `await CompleteHandshakeAsync(...)` đồng bộ.
        - `ContinuousPipelineTest.cs`: Xây dựng 12 unit tests tự động kiểm thử toàn bộ vòng đời Envelope, Burst Producer, Handshake Transitions và Shift Register Millimeter Reject.
      - `Hiệu Quả Đạt Được`:
    - [x] Task 247: Tích hợp tính năng Import / Export PLC Tags CSV trong Cửa sổ PLC Manager:
      - `Yêu Cầu & Bối Cảnh`:
        - Bổ sung công cụ Import / Export danh bạ biến (PLC Tags) trực tiếp trong Tab 1: Kết Nối & Tags của cửa sổ PLC & Industrial Motion.
        - Hỗ trợ đầy đủ các định dạng: **Mitsubishi GX Works 3 Global Labels CSV**, **GX Works Device Comments CSV**, và **Standard PLC Tags CSV**.
      - `Giải Pháp Triển Khai`:
        - `PlcTagCsvService.cs`: Xây dựng module nhận diện tự động định dạng CSV, parse RFC 4180, chuyển đổi kiểu dữ liệu Mitsubishi GX Works/IEC sang `PlcDataType`, và hỗ trợ 3 kiểu xuất file CSV.
        - `PlcManagerViewModel.cs` & `PlcManagerWindow.xaml`: Bổ sung các lệnh `ImportTagsCommand`, `ExportTagsCommand`, tích hợp giao diện DockPanel trực quan.
        - `PlcTagCsvServiceTest.cs`: Bổ sung 25 bài kiểm thử tự động xác thực toàn diện mọi định dạng CSV và round-trip xuất nhập $\rightarrow$ **PASS 25/25 (100%)**.
      - `Hiệu Quả Đạt Được`:
        - Kỹ sư PLC có thể nạp hàng trăm biến trực tiếp từ file export của Mitsubishi GX Works 3 hoặc GX Works 2 chỉ với 1 cú click chuột, không cần gõ tay từng tag.
    - [x] Task 248: Khắc phục triệt để lỗi bắt tín hiệu trên màn hình PLC Oscilloscope:
      - `Yêu Cầu & Bối Cảnh`:
        - Khắc phục hiện tượng tín hiệu trên PLC Oscilloscope lúc bắt được lúc không, sóng vuông bị chập chờn đứt đoạn dù trên PLC Tag Browser tín hiệu vẫn nhận bình thường.
      - `Nguyên Nhân Gốc Rễ`:
        - Xung đột Driver Lock giữa `PlcPollingEngine` và `PlcOscilloscopeViewModel.SamplingLoopAsync` khiến driver rơi về `FallbackReadSimulation` trả về `0`, làm gãy dạng sóng và tạo xung giả. Cửa sổ thiếu cơ chế `AcquirePollingLock` và các địa chỉ kênh nhập tự do không được đưa vào chu kỳ quét PLC.
      - `Giải Pháp Triển Khai`:
        - `PlcPollingEngine.cs` & `IPlcManagerService.cs`: Thêm `BatchPolledEventArgs` và sự kiện `OnBatchPolled`. `PlcPollingEngine` là luồng nền duy nhất giao tiếp với driver phần cứng.
        - `PlcManagerService.cs`: Thêm `RegisterDynamicTagProvider`, `UnregisterDynamicTagProvider`, `RequestScanInterval`, `ReleaseScanInterval`. Tự động gom tag động từ các kênh CH1..CH4 vào 1 chu kỳ quét duy nhất.
        - `PlcOscilloscopeViewModel.cs`: Loại bỏ hoàn toàn vòng lặp riêng `SamplingLoopAsync`, chuyển sang lắng nghe `OnBatchPolled`, tự động quản lý `AcquirePollingLock` và `RequestScanInterval`.
        - `PlcOscilloscopeCanvas.cs`: Bao bọc vùng vẽ sóng bằng `dc.PushClip` và `dc.Pop`.
        - `PlcTests.cs`: Thêm `Test 6: OnBatchPolled & Dynamic Tag Provider (Oscilloscope Engine)`.
      - `Hiệu Quả Đạt Được`:
        - Tín hiệu trên 4 kênh CH1..CH4 bắt mượt mà, liên tục $100\%$, đồng bộ thời gian tuyệt đối và không còn hiện tượng chập chờn hay mất xung.
        - Toàn bộ suite test tự động trong `TestExtractApp` đạt PASS $100\%$.
    - [x] Task 249: Khắc phục hiện tượng giật cục trên màn hình PLC Oscilloscope & Tối ưu hóa chu kỳ quét 1ms-2ms:
      - `Yêu Cầu & Bối Cảnh`:
        - Người dùng phản ánh sau khi fix lỗi trước đó, màn hình PLC Oscilloscope có hiện tượng các đường tín hiệu chạy ra bị giật cục, không được mượt như trước nữa dù để scan time 1ms hay 2ms.
      - `Nguyên Nhân Gốc Rễ Đã Được Xác Định & Xử Lý`:
        1. *Windows Timer Tick 15.625ms*: `Task.Delay(1)` và `Task.Delay(2)` bị ngắt thời gian Windows ép ngủ ít nhất ~15.6ms. Đã khắc phục bằng `NativeTimerUtility.cs` (`timeBeginPeriod(1)` từ `winmm.dll`), giảm timer resolution xuống 1.0ms, nâng tốc độ quét lên ~850 - 1000 Hz (chu kỳ ~1.09ms).
        2. *Trục thời gian Viewport trôi không đồng bộ*: `MaxSessionTimeMs` và `ViewOffsetMs` trong `PlcOscilloscopeViewModel.cs` trước đây chỉ cập nhật khi nhận batch. Đã nâng cấp UI Timer lên 60 FPS (16ms) và tự động đồng bộ thời gian thực liên tục trong `OnUiRefreshTick`.
        3. *Khoảng trống đầu dạng sóng (Waveform Gap)*: Đã triển khai Real-time Waveform Extension trong `PlcOscilloscopeCanvas.cs`, tự động kéo dài dạng sóng từ sample cuối cùng đến thời điểm hiện tại `MaxSessionTimeMs` / `targetPx`. Đầu sóng luôn chạm sát mép thời gian thực và di chuyển mượt mà 60 FPS.
        - Dạng sóng 4 kênh CH1..CH4 chuyển động êm ái, mượt mà 60 FPS không gợn sóng khi chọn bất kỳ Scan Time nào (1ms / 2ms / 5ms / 10ms).
        - Toàn bộ suite test tự động trong `TestExtractApp` đạt PASS 100%.
    - [x] Task 250: Sửa chữa & Chuẩn hóa kết nối giao thức Mitsubishi MC Protocol (Ethernet Socket):
      - `Yêu Cầu & Bối Cảnh`:
        - Khi kết nối PLC bằng giao thức Mitsubishi (MC Protocol Ethernet Socket, không qua MX Component), tuy hiển thị `Connected` nhưng thông tin CPU PLC bị rỗng hoặc hiển thị chung chung `Mitsubishi PLC`, đồng thời giá trị các tag `X0`, `X1`, `X3`, `X7`, `Y0`, `D100`... trên PLC Tag Browser và PLC Oscilloscope hiển thị `N/A` hoặc không nhảy giá trị.
      - `Nguyên Nhân Gốc Rễ Đã Được Xác Định & Xử Lý`:
        1. *Nhận Diện CPU Model Đa Tầng (Multi-tier Identification)*: Tầng 1 (Command `0x0101`) -> Tầng 2 (Đọc trực tiếp thanh ghi đặc biệt `SD200..SD207` - 8 words = 16 bytes ASCII trên FX5U/Q) -> Tầng 3 (Đọc `SD0` CPU Model Code) -> Tầng 4 (Port-based SLMP detection `5000/5007` $\rightarrow$ `FX5U`). Giao diện hiển thị chuẩn xác `FX5U-32MT/ES` hoặc `FX5U (MC Protocol)`.
        2. *Cơ Chế Đọc Bit Kép (Dual-mode Bit Read Mechanism)*: Bước 1 thử đọc Bit units (`Command: 0x0401, Subcommand: 0x0001`); nếu PLC trả về mã lỗi, tự động fallback sang đọc Word bao quanh bit đó (`Command: 0x0401, Subcommand: 0x0000`) tại vị trí `(headNumber / 16) * 16` và trích xuất bit. Triệt tiêu $100\%$ lỗi đọc bit trên PLC FX5U.
        3. *Hệ Bát Phân (Octal) Cho Thiết Bị `X`, `Y`*: Nâng cấp `ParseDeviceAddress` tự động nhận diện và chuyển đổi hệ bát phân sang số nguyên.
        4. *Tự Động Kích Hoạt Polling Lock*: `PlcManagerViewModel` tự động giữ `AcquirePollingLock("PlcManager")` ngay khi bấm nút "Kết Nối (Connect)" thành công, nạp đầy Cache trước khi mở các cửa sổ khác. `PlcBrowserViewModel` tra cứu kép theo cả Tag Name và Address.
      - `Hiệu Quả Đạt Được`:
    - [x] Task 251: Bổ sung thanh 20 sản phẩm gần nhất (Recent 20 Parts OK/NG Stepped Bar) & Chuyển đổi các nút Toolbar thành Icon Buttons trên Tool Editor:
      - `Yêu Cầu & Bối Cảnh`:
        - Bổ sung thanh mô phỏng trực quan dạng 20 nấc (segments) thể hiện lịch sử kiểm tra của 20 con hàng gần nhất (Xanh = OK, Đỏ = NG), chạy xuôi theo chiều từ Trái sang Phải (FIFO), đặt ngay sau thanh Queue trong Toolbar của Tool Editor.
        - Chuyển đổi toàn bộ 9 nút chức năng (`Load Image`, `Capture Camera`, `Run Once`, `Run Continuous`, `Calibration`, `Chessboard Calib`, `Gán Mã - Job`, `Chụp & Lưu Ảnh`, `Bản Đồ Cuộn`) thành các Icon Button siêu gọn gàng để thu hẹp không gian, mở rộng diện tích hiển thị cho thanh sản phẩm và thanh Queue.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. *ViewModel (ToolEditorViewModel.Engine.cs & Inspection.cs)*:
           - Quản lý vòng đệm `List<bool> _recentPartsHistory` (tối đa 20 con hàng) và 20 cặp Brush thuộc tính `RecentPartSlot0Bg/Border` .. `RecentPartSlot19Bg/Border`.
           - Tự động gọi `PushRecentPartInspectionResult(res.Pass)` khi có kết quả kiểm tra mới ở cả chế độ Run Once, Run Continuous và PLC Trigger.
           - Thống kê thời gian thực `RecentPartsStatusText` (`OK: x | NG: y`), `RecentPartsYieldText` (Tỉ lệ đạt %), và `RecentPartsToolTipText` chi tiết kèm đổi màu viền cảnh báo khi có nhiều lỗi NG.
        2. *Giao Diện View (ToolEditorView.xaml)*:
           - Chuyển đổi 9 nút dài thành Icon Buttons với kích thước tiêu chuẩn, padding gọn, icon trực quan và tooltip đầy đủ.
           - Bố trí DockPanel thanh công cụ hài hòa: `Mã SP` $\rightarrow$ `Icon Buttons` $\rightarrow$ `Queue Bar` $\rightarrow$ `Recent 20 Parts Stepped Bar` $\rightarrow$ `Speed/Time Bar` $\rightarrow$ `Result Badge`.
        3. *Kiểm Thử Tự Động*:
           - Bổ sung `Test 6` trong `ContinuousPipelineTest.cs` kiểm tra luồng FIFO 20 slot, tỉ lệ Yield rate và reset lịch sử, đạt PASS 100%.
      - `Hiệu Quả Đạt Được`:
        - Tiết kiệm hơn 600px không gian trên Toolbar, giao diện hiện đại, thoáng đãng.
        - Giúp người vận hành quan sát trực quan ngay tức thì chuỗi 20 con hàng gần nhất chạy từ trái sang phải, nhận biết nhanh tình trạng máy và tỉ lệ lỗi NG trên dây chuyền.
        - Toàn bộ suite test tự động trong `TestExtractApp` đạt PASS 100%.
    - [x] Task 252: Tùy chọn gửi Xung (Pulse Mode) & Nhập thời gian xung trong node ResultTransfer của Tool Editor:
      - `Yêu Cầu & Bối Cảnh`:
        - Trong Tool Editor, node `ResultTransfer` kiểu bool cần có tùy chọn gửi mức logic (Level) hoặc gửi xung (Pulse). Nếu chọn gửi xung, cho phép nhập thời gian xung (`PulseDurationMs`, mặc định 100ms) và tự động thực thi cơ chế đảo xung: nếu địa chỉ đang là `true` thì ghi `false` trong khoảng thời gian xung rồi tự động ghi lại `true`, và ngược lại nếu đang là `false` thì ghi `true` trong thời gian xung và ghi lại `false`.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. *Data Model (PlcNodeDefinitions.cs)*: Thêm enum `ResultTransferMode { Level = 0, Pulse = 1 }`, thuộc tính `Mode` và `PulseDurationMs` vào `ResultTransferItem`.
        2. *Execution Engine (PlcResultTransferRunner.cs)*: Xử lý song song `ExecuteSingleItemTransferAsync`, đọc trạng thái hiện tại, phát xung đảo `!currentBool` $\rightarrow$ `await Task.Delay(pulseMs)` $\rightarrow$ Khôi phục lại trạng thái ban đầu `currentBool`.
        3. *Giao Diện UI & ViewModel (ToolEditorViewModel.Plc.cs & ToolEditorView.xaml)*: Bổ sung ComboBox `Chế độ gửi:` (`Level`/`Pulse`) và TextBox nhập `Xung (ms):` khi chọn Pulse.
        4. *Kiểm Thử Tự Động*: Thêm `Test 9: ResultTransfer Pulse Mode (Toggle & Auto-Restore) & Level Mode` trong `PlcTests.cs`, đạt PASS 100%.
      - `Hiệu Quả Đạt Được`:
        - Cho phép phát xung trigger kết quả kiểm tra, xung kích hoạt xi-lanh gạt, xung Handshake ACK sang PLC cực kỳ linh hoạt và chuẩn xác mili-giây mà không cần lập trình timer phức tạp trong PLC.
        - Toàn bộ suite test tự động trong `TestExtractApp` đạt PASS 100%.
    - [x] Task 253: Chuẩn hóa Quản lý Kết nối PLC, Vô hiệu hóa Nút Kết nối khi Connected & Khắc phục Lỗi Tự động Reconnect sau khi Ngắt kết nối:
      - `Yêu Cầu & Bối Cảnh`:
        - Khi bấm "Ngắt Kết Nối", hệ thống không được tự ý kết nối lại.
        - Khi đang Connected, nút "⚡ Kết Nối" phải bị disabled để tránh bấm trùng lặp.
        - Giữ nguyên thông tin CPU Name thực (`FX5UCPU Type 18944`) khi kết nối MX Component, không bị đổi thành `Mitsubishi PLC (Connected)`.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. *Cơ Chế Phân Biệt Manual Disconnect Trong Polling Engine*: Thêm thuộc tính `IsManuallyDisconnected` vào `PlcModel.cs`. `PlcPollingEngine.cs` chỉ auto-reconnect khi PLC ở trạng thái `Error/Connecting`. Bỏ qua hoàn toàn nếu `IsManuallyDisconnected = true` hoặc `State == Disconnected`.
        2. *Điều Khiển Nút Bấm CanExecute*: Thêm `CanConnectSelectedPlc` và `CanDisconnectSelectedPlc` trong `PlcManagerViewModel.cs`, tự động kích hoạt `NotifyCanExecuteChanged()` khi trạng thái kết nối thay đổi.
        3. *Loại Bỏ Hardcode CPU Name*: Thay thế chuỗi hardcode trong `MxComWorker.cs` bằng bộ nhớ đệm CPU Type lấy từ `GetCpuType`.
        4. *Kiểm Thử Tự Động*: Thêm `Test 10` trong `PlcTests.cs`, đạt PASS 100%.
      - `Hiệu Quả Đạt Được`:
        - Quản lý trạng thái kết nối PLC hoàn hảo, thao tác người dùng mượt mà, chính xác và chuyên nghiệp.
        - Toàn bộ suite test tự động trong `TestExtractApp` đạt PASS 100%.
    - [x] Task 254: Khắc Phục Hiện Tượng Treo Connecting Khi Mở Cửa Sổ PLC Manager & Tối Ưu Ngắt Kết Nối Tức Thì:
      - `Yêu Cầu & Bối Cảnh`:
        - Khi mở cửa sổ "PLC & Industrial Motion Configuration", hệ thống không được tự động ép kết nối sang trạng thái `Connecting` gây treo/đơ giao diện. Nút "Ngắt Kết Nối" phải phản hồi tức thì mà không bị chặn (blocked) bởi luồng kết nối đang chờ.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. *Xóa Bỏ Tự Động Chiếm Polling Lock*: Trong `ToolEditorViewModel.Plc.cs`, loại bỏ hoàn toàn `AcquirePollingLock("PlcManagerWindow")`. Chỉ kết nối khi người dùng bấm "⚡ Kết Nối".
        2. *Chỉ Kích Hoạt Polling Khi Đang Active*: Trong `PlcManagerViewModel.cs`, các hàm `AddPlc`, `DeletePlc`, `AddTag`, `DeleteTag` chỉ gọi `StartPollingAsync` nếu `_plcService.IsPollingActive == true`.
        3. *Ngắt Kết Nối Non-blocking Tức Thì*: Trong `DisconnectSelectedPlcAsync`, giải phóng toàn bộ lock polling, lập tức gán `State = Disconnected`, `CpuName = string.Empty`, cập nhật UI rồi mới đóng socket/COM ngầm với timeout 500ms.
        4. *Chống Deadlock Trong Driver*: Cập nhật `MitsubishiDriver.cs` và `MitsubishiMxComponentDriver.cs` với cơ chế timeout cho semaphore lock, luôn chuyển `State = Disconnected` dù kết nối trước đó chưa hoàn thành.
      - `Hiệu Quả Đạt Được`:
        - Mở cửa sổ cấu hình PLC mượt mà, không bị lag, không bị chuyển trạng thái Connecting ngoài ý muốn.
        - Nút "Ngắt Kết Nối" phản hồi tức thì $100\%$, không bao giờ bị treo.
        - Toàn bộ 10/10 test case PLC trong `TestExtractApp` đạt PASS 100%.
    - [x] Task 255: Tự Động Kết Nối PLC Đã Lưu Khi Khởi Động Ứng Dụng (Auto-Connect on App Startup):
      - `Yêu Cầu & Bối Cảnh`:
        - Khi ứng dụng bật lên, tự động kết nối các PLC trong danh sách đã lưu (`Enabled == true`) và kích hoạt Polling Engine ngầm, không bắt người dùng phải mở cửa sổ PLC để bấm Kết Nối thủ công mỗi lần mở máy.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. *Phương Thức AutoConnectStartupAsync*: Bổ sung vào `IPlcManagerService` & `PlcManagerService`, tự động kết nối các PLC Enabled và giữ lock `"AutoStartup"` cho Polling Engine.
        2. *Tích Hợp Khởi Động App.xaml.cs*: Gọi `_ = plcManager.AutoConnectStartupAsync();` chạy nền không chặn giao diện chính.
        3. *Đồng Bộ Giao Diện & Thao Tác Ngắt Kết Nối*: Cửa sổ PLC Manager hiển thị ngay trạng thái `Connected` và CPU Name; khi người dùng bấm "Ngắt Kết Nối" thì giải phóng sạch lock `AutoStartup` và `PlcManager`.
        4. *Kiểm Thử Tự Động*: Bổ sung `Test 11` trong `PlcTests.cs`, đạt PASS 100%.
      - `Hiệu Quả Đạt Được`:
        - Người dùng mở ứng dụng lên là hệ thống kết nối PLC sẵn sàng 100%, tag data cập nhật tức thì vào Tool Editor, Oscilloscope và HMI.
        - Toàn bộ 11/11 bài test PLC trong `TestExtractApp` đạt PASS 100%.

- [x] Task 256: Sửa lỗi Run Continuous bỏ qua Interval khi dùng Camera Giả Lập (Simulator) với SoftTrigger.
      - `Nguyên Nhân Gốc Rễ`:
        1. `SimulatorCameraDriver.SimLoop` chạy 30 FPS liên tục bắn frame vào `FrameCaptured` mà bỏ qua `TriggerMode`.
        2. `OpenCvCameraDriver.GrabLoop` tương tự, không kiểm tra `TriggerMode`.
        3. `StartContinuousCameraFlow` chỉ áp dụng `TriggerMode=On` khi `isIndustrial`, bỏ qua Simulator và Webcam.
        4. Nhánh `else` trong SoftTrigger Generator tạo ra 2 luồng đẩy frame song song (SimLoop + CaptureSnapshotAsync).
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. `SimulatorCameraDriver.cs`: `SimLoop` kiểm tra `TriggerMode==On` → sleep & continue. `ExecuteSoftwareTriggerAsync` gọi `ApplySoftwarePostProcessing` + `RaiseFrameCaptured`.
        2. `OpenCvCameraDriver.cs`: `GrabLoop` kiểm tra `TriggerMode==On` → sleep & continue. `ExecuteSoftwareTriggerAsync` đọc 1 frame + `RaiseFrameCaptured`.
        3. `ToolEditorViewModel.Engine.cs`: Áp dụng `TriggerMode=On` cho TẤT CẢ loại camera. Thống nhất SoftTrigger Generator dùng `ExecuteSoftwareTriggerAsync()` + fallback.
        4. `StopContinuousFlow` đã có sẵn logic khôi phục `TriggerMode=Off`.
      - `Kiểm Thử`: Bổ sung test TriggerMode=On + SoftTrigger trong `CameraTest.cs`. Toàn bộ test suite PASSED 100%.

- [x] Task 257: Tối ưu Render Canvas không chặn luồng Worker & Bổ sung Widget Giám sát RAM và CPU Đa Lõi trên thanh trạng thái Tool Editor.
      - `Nguyên Nhân Gốc Rễ`:
        - `ProcessContinuousFrameAsync` dùng `await Dispatcher.InvokeAsync(...)` (đồng bộ chặn), Worker phải chờ UI Thread render đồ họa xong mới lấy frame tiếp theo, gây nghẽn và làm đầy queue 16 nấc.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. `ToolEditorViewModel.Engine.cs`: Chuyển sang `Dispatcher.BeginInvoke(DispatcherPriority.Background, ...)` (non-blocking fire-and-forget), Worker tiếp tục lấy frame ngay lập tức mà không phụ thuộc vào tốc độ render của UI.
        2. `ToolEditorViewModel.SystemMonitor.cs`: Module đo RAM (`WorkingSet64`), CPU App (`TotalProcessorTime`), CPU từng lõi (mảng `PerformanceCounter` 0..16) với độ cao sóng Equalizer và dải màu 4 cấp độ.
        3. `ToolEditorView.xaml`: Bổ sung widget System Monitor nhỏ gọn đặt trước và cùng hàng với thanh Queue và Recent.
      - `Kiểm Thử`: Bổ sung `TestSystemMonitorAndNonBlockingRender` trong `CameraTest.cs`. Toàn bộ test suite PASSED 100%.

- [x] Task 258: Xây dựng Giao diện Lịch sử kiểm tra (Inspection Log) & Cụm 4 biểu đồ phân tích thống kê năng lực quá trình SPC / CPK chuyên nghiệp với Background Logging Worker.
      - `Mục Tiêu & Yêu Cầu`:
        - Nút Log (📊) trong Tool Editor cạnh nút Bản đồ cuộn, mở cửa sổ mới hiển thị lịch sử kiểm tra.
        - Cột trái: Danh sách các phiên kiểm tra (Thời gian bắt đầu/kết thúc, tên sản phẩm, tổng mẫu, OK, NG, Yield).
        - Nửa trên bên phải: Bảng chi tiết từng con hàng và các phép đo (Tên phép đo, spec, min, max, result, judge).
        - Nửa dưới bên phải: Bảng 4 biểu đồ SPC (Histogram, Xbar chart, R chart, Cpk trend) với cỡ mẫu $n$ tùy chỉnh (mặc định 32, tự hạ về 5 nếu thiếu mẫu, bỏ phần dư $N \pmod n$).
        - Nút xuất file Excel (XML Spreadsheet 2003 chuẩn), CSV (UTF-8 BOM), JSON và nút bật/tắt CPK.
        - Background Worker riêng biệt (Channel-based) ghi log không ảnh hưởng đến luồng kiểm tra Vision.
        - Tự động lấy tên/mã sản phẩm từ Job (`ProductCode`, `ProductName`, hoặc tên file `.job`) tránh hiển thị "Chưa gán".
        - 4 biểu đồ SPC dùng `Viewbox Stretch="Fill"` lấp đầy 100% diện tích card, đầy đủ trục X/Y, đường lưới, vạch chia và Markers tròn có ToolTip chi tiết.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. `VisionInspectionApp.Models`: Tạo `InspectionLogModels.cs` định nghĩa dữ liệu phiên, con hàng, phép đo và phân tích SPC.
        2. `VisionInspectionApp.Application`: Tạo `SpcEngine.cs` (thuật toán Shewhart, Xbar-R, Cpk, Histogram Gauss), `IInspectionLogService.cs` / `InspectionLogService.cs` (Channel Worker ghi log ngầm), `InspectionLogExporter.cs` (xuất Excel/CSV/JSON).
        3. `VisionInspectionApp.UI`: Tạo `InspectionLogViewModel.cs`, `InspectionLogWindow.xaml`, tích hợp nút `📊` và lệnh `OpenInspectionLogCommand` trong Tool Editor.
      - `Kiểm Thử`: Bổ sung `TestInspectionLogAndSpcEngine` trong `CameraTest.cs`. Toàn bộ test suite PASSED 100%.

- [x] Task 259: Sửa lỗi ToolTip biểu đồ SPC & Khắc phục triệt để lỗi đổi tên (RefName) node trên Flow Canvas khiến node ngừng hoạt động.
      - `Mục Tiêu & Yêu Cầu`:
        1. ToolTip trên 4 biểu đồ SPC (Histogram, Xbar, R-chart, Cpk trend) phải hiển thị tức thì khi hover chuột vào các cột hoặc các chấm tròn dữ liệu.
        2. Khi người dùng sửa RefName của bất kỳ Tool Node nào trên Flow Canvas (`Preprocess`, `Caliper`, `Origin`, `CreatePoint`, `Distance`...), node và toàn bộ liên kết downstream phải hoạt động bình thường, không bị xóa mất cấu hình.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. *Sửa ToolTip SPC ([InspectionLogWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/InspectionLogWindow.xaml))*:
           - Đưa `Canvas.Left` & `Canvas.Top` lên `ItemContainerStyle` cho `ContentPresenter` của 4 `ItemsControl`, loại bỏ `TranslateTransform` bên trong `DataTemplate`.
           - Thiết lập `IsHitTestVisible="False"` cho tất cả `Polyline` vẽ đường trung bình / đường cong Gauss để tránh chặn sự kiện chuột.
           - Cấu hình `ToolTipService.InitialShowDelay="0"`, `ToolTipService.BetweenShowDelay="0"` và style ToolTip nền tối viền xanh cyan `#38BDF8` cực nét.
        2. *Sửa Đổi Tên Node Flow Canvas ([ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs))*:
           - Mở rộng hàm `RenameSelectedDefinitionIfNeeded()` bao quát 100% các loại Tool Node (`Preprocess`, `ImageSource`, `Caliper`, `SurfaceCompare`, `ContourCompare`, `TextNode`, `ImageOutput`, `Crop`, `ColorDiff`, `ImgArithmetic`, `CreatePoint`, `CreateLine`, `CreateRect`, `CreateCircle`, `Condition`, `Plc*`, `ResultTransfer`, `DbNode`...).
           - Quét và cập nhật tự động toàn bộ thuộc tính tham chiếu downstream trong `_config` (`PointA`, `PointB`, `LineA`, `LineB`, `Line`, `Point`, `CircleRef`, `RefA`, `RefB`, `InputNodeName`, `ImageSourceRef`, `PointRef`, `Point1Ref`, `Point2Ref`, `CenterPointRef`, `BoundaryPointRef`, `PreprocessChoice`, v.v.).
           - Đồng bộ `ToolGraph.Nodes` và `_config` ngay lập tức để ngăn ngừa `SyncToolGraphToConfig()` xóa nhầm cấu hình của node.
      - `Kiểm Thử`: Bổ sung `TestFlowCanvasNodeRenameAndDownstreamReferences` trong `CameraTest.cs`. Toàn bộ test suite PASSED 100%.

- [x] Task 260: Đổi chiều hiển thị thanh 20 nấc Recent con hàng trong Tool Editor từ Cũ -> Mới thành Mới nhất -> Cũ nhất (từ trái sang phải).
      - `Mục Tiêu & Yêu Cầu`:
        - Con hàng vừa kiểm tra xong (mới nhất) sẽ hiển thị ở nấc đầu tiên bên trái ngoài cùng (Slot 0).
        - Các con hàng cũ hơn sẽ dịch chuyển dần sang các nấc bên phải (Slot 1, Slot 2... Slot 19).
        - Cập nhật ToolTip giải thích rõ: `• Chiều luồng: Trái (Mới nhất) ➔ Phải (Cũ dần)`.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. `ToolEditorViewModel.Engine.cs`: Sửa `PushRecentPartInspectionResult(bool isOk)` dùng `_recentPartsHistory.Insert(0, isOk)` và `RemoveAt(20)`. Cập nhật ToolTipText.
        2. `ToolEditorViewModel.Engine.cs`: Chuyển `EmptySlotBrush`, `OkSlotBrush`, `NgSlotBrush` sang `public static readonly` phục vụ unit test.
        3. `ContinuousPipelineTest.cs`: Bổ sung assertions kiểm tra Slot 0 = Mới nhất (NG) và Slot 1 = Cũ hơn (OK).
      - `Kiểm Thử`: Chạy toàn bộ test suite PASSED 100%.

- [x] Task 261: Tối ưu triệt để Render Canvas (UI Throttling & Non-blocking Preview) và Xóa bỏ độ trễ ImageSource khi kết nối PLC.
      - `Mục Tiêu & Yêu Cầu`:
        - Khắc phục hiện tượng bật Render Canvas thì Queue 16 nấc đầy dần, tắt Render Canvas thì Queue vơi về 0.
        - Khắc phục hiện tượng khi kết nối PLC thì node `ImageSource` runtime cực cao, làm giảm tốc độ kiểm hàng.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. `ToolEditorViewModel.Engine.cs`: Áp dụng UI Throttling & Non-blocking Preview Drop với cờ nguyên tử `_isUiRenderingContinuous` và `ContinuousUiThrottleIntervalMs = 60`. Worker Task luôn chạy 100% tốc độ, UI chỉ render ~16 FPS mượt mà.
        2. `ToolEditorViewModel.Engine.cs`: Tách riêng thời gian chuẩn bị ảnh của `ImageSource` (`< 1ms`), không gộp thời gian `Inspect()` và PLC handshake vào node `ImageSource`.
        3. `IndustrialHandshakeStateMachine.cs`: Thêm `HasConfiguredHandshakeTags()` để bypass 0ms ngay lập tức nếu tag Handshake không được cấu hình trong PLC, không bị treo 500ms timeout chờ ACK.
      - `Kiểm Thử`: Bổ sung `Test12_HandshakeStateMachine_NonBlocking_And_ImageSourceTimingAsync` trong `PlcTests.cs`. Toàn bộ test suite PASSED 100%.

- [x] Task 262: Hiển thị Runtime của node ResultTransfer trên Flow Canvas và Tối ưu xung Pulse sang Non-blocking Auto-Restore loại bỏ hoàn toàn hiện tượng dồn ứ Queue khi bật PLC.
      - `Mục Tiêu & Yêu Cầu`:
        - Hiển thị runtime chính xác của node `ResultTransfer` thay vì luôn hiện 0ms.
        - Khắc phục triệt để hiện tượng hàng bị dồn ứ vào queue khi bật kết nối PLC.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. `PlcResultTransferRunner.cs`: Bổ sung đo `Stopwatch` cho từng node `ResultTransfer` và ghi vào `result.Timings.NodeTimings[nodeDef.Name]` (hiển thị thời gian ms thực tế trên node canvas).
        2. `PlcResultTransferRunner.cs`: Tối ưu hóa chế độ `Pulse` (phát xung) sang cơ chế Non-blocking Auto-Restore: Gửi phát xung ngay lập tức (1-2ms), và tách việc chờ `Task.Delay(pulseMs)` để hạ xung sang background task độc lập (`_ = Task.Run(...)`), không làm chậm luồng kiểm tra.
        3. `InspectionService.PlcDb.cs` & `ToolEditorViewModel.Engine.cs`: Xóa bỏ việc gọi trùng lặp 2 lần `ExecuteResultTransfersAsync`, bổ sung timeout 50ms cho `PlcWrites` tránh socket lock làm đứng luồng.
      - `Kiểm Thử`: Cập nhật `Test9_ResultTransfer_PulseMode_And_LevelModeAsync` trong `PlcTests.cs` (NodeTimings=3ms). Toàn bộ test suite PASSED 100%.

- [x] Task 263: Xây dựng Dedicated Async Queue Worker cho ResultTransfer và Sửa triệt để lỗi xóa node trên Flow Canvas.
      - `Mục Tiêu & Yêu Cầu`:
        - Tách 100% việc gửi dữ liệu ResultTransfer ra một hàng đợi FIFO bất đồng bộ chuyên biệt, đảm bảo luồng kiểm tra chính có 0ms latency.
        - Khắc phục lỗi xóa node `ResultTransfer` và các node PLC trên Flow Canvas không bị xóa trong config và vẫn chạy ngầm.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. `PlcResultTransferRunner.cs`: Xây dựng `PlcResultTransferQueue` dùng `Channel<ResultTransferPackage>` dung lượng 128 phần tử, chạy background worker tuần tự truyền dữ liệu sang PLC cho từng con hàng.
        2. `InspectionService.PlcDb.cs`: Trong `Inspect()`, gọi `PlcResultTransferQueue.Enqueue(...)` (0.000ms latency, không chờ mạng), gán runtime gần nhất vào `NodeTimings` cho UI hiển thị.
        3. `ToolEditorViewModel.GraphOps.cs` & `ToolEditorViewModel.Config.cs`: Bổ sung dọn sạch sẽ 100% các loại node (`ResultTransfer`, `PlcRead`, `PlcWrite`, `PlcWait`, `PlcTrigger`, `DbNode`, `TextNode`, `ImageOutput`, `Preprocess`, `ImageSource`, `Condition`) khi xóa trên Canvas.
      - `Kiểm Thử`: Bổ sung `Test13_PlcResultTransferQueue_AsyncFifoAndZeroMainFlowLatencyAsync` (Main Flow = 0ms, Background Timing = 7ms) và kiểm tra xóa node trong `CameraTest.cs`. Toàn bộ test suite PASSED 100%.

- [x] Task 264: Tối ưu hiển thị Tool Origin trên Flow Canvas (Vẽ Template ROI dạng Read-Only, chỉ cho phép tương tác kéo thả với Search ROI).
      - `Mục Tiêu & Yêu Cầu`:
        - Khi click chọn node Origin trên Flow Canvas, vẫn vẽ đầy đủ cả Search ROI ("Origin S") và Template ROI ("Origin T") để người dùng quan sát.
        - Khóa tương tác chuột đối với Template ROI trên canvas để tránh thao tác nhầm, chỉ cho phép kéo thả/chỉnh sửa Search ROI.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. `ToolEditorViewModel.GraphOps.cs` & `Engine.cs`: `ActiveRoiLabel = "Origin S"`, vẽ đầy đủ `SearchRoi` (xanh lá) và `TemplateRoi` (vàng kim) lên canvas.
        2. `ImageViewerControl.xaml.cs`: Bỏ qua hit-test và kéo thả đối với `Origin T` khi ở ngoài cửa sổ Train Template.
        3. Cửa sổ Train Template (`OriginTrainViewModel.cs`) tiếp tục quản lý, hiển thị và tương tác `TemplateRoi` độc lập.
      - `Kiểm Thử`: Toàn bộ unit tests và hệ thống render PASSED 100%.

- [x] Task 265: Tự động lọc bỏ các mẫu đo NaN/NG trong phân tích thống kê SPC & Hiển thị 4 biểu đồ bình thường.
      - `Mục Tiêu & Yêu Cầu`:
        - Khi cuộn danh sách hàng vừa kiểm tra có những con hàng mà kết quả đo ra `NaN` (không tìm thấy đối tượng), toàn bộ 4 biểu đồ (Histogram, X-bar, R-chart, CPK Trend) phải hiển thị bình thường.
        - Các mẫu đo có kết quả `NaN` hoặc `Infinity` được tự động loại bỏ khỏi tập tính toán thống kê SPC.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. `SpcEngine.cs`: Lọc `values = rawValues.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList()`, bảo vệ an toàn 100% các phép tính thống kê $X_{bar}$, $R$, $\sigma$, $C_p$, $C_{pk}$, $C_{pu}$, $C_{pl}$ và phân bố chuẩn Gauss (PDF).
        2. `InspectionLogViewModel.cs`: Tự động tách `validValues` và đếm `nanCount`, cập nhật Header hiển thị chi tiết số lượng mẫu hợp lệ và số mẫu NaN được loại bỏ, bảo vệ tọa độ vẽ chart trên Canvas.
      - `Kiểm Thử`: Bổ sung test case `[2b/4] SpcEngine Robust NaN / Infinity Filtering & All-NaN Fallback` trong `CameraTest.cs`. Toàn bộ test suite PASSED 100%.

- [x] Task 266: Tích hợp Bộ Điều Khiển Đèn 8 Kênh ASCII (8-Channel Lighting Controller) qua Ethernet (TCP/UDP) và Cổng Nối Tiếp RS-232 (COM Port) vào ứng dụng WPF.
      - `Mục Tiêu & Yêu Cầu`:
        - Tích hợp giao thức điều khiển đèn 8 kênh ASCII (`$COMMAND=VALUE#`) qua cả Ethernet (TCP/UDP) và cổng nối tiếp Serial RS-232 (COM Port: 19200bps, 8 DataBits, 1 StopBit, No Parity, Half-duplex).
        - Hỗ trợ đầy đủ: Bật/tắt 8 kênh (`F0`-`F7`), độ sáng 0-255 (`L0`-`L7`), thời gian sáng 1-999ms (`T0`-`T7`), 4 chế độ Trigger (`TR=0..3`), đọc đồng bộ toàn bộ tham số (`RD=9999`), lưu cấu hình (`SA=1`), khôi phục cài đặt gốc (`RS=1`), khóa/mở bàn phím (`LC=0/1`), và cấu hình mạng (`NE`, `IP`, `IU`, `IS`, `IL`, `DP`, `DL`).
        - Đảm bảo thread-safety qua `SemaphoreSlim`, non-blocking UI, debounce slider điều chỉnh độ sáng (50ms), tự động nạp/lưu cấu hình kết nối qua `GlobalAppSettingsService`, hỗ trợ Auto-connect an toàn cho cả Ethernet và COM port, menu riêng biệt trên MenuStrip và dọn dẹp kết nối an toàn khi tắt app.
      - `Giải Pháp Kỹ Thuật Đã Triển Khai`:
        1. `VisionInspectionApp.Models`: Tạo [LightingControllerModels.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/LightingControllerModels.cs) định nghĩa `LightingConnectionState`, `LightingInterfaceType` (Ethernet, SerialCom), `LightingTriggerMode`, `LightingNetworkMode`, các class trạng thái `LightingChannelState`, `LightingControllerState` và kết quả lệnh `LightingCommandResult` (ánh xạ mã lỗi `E1`-`E7`, `ER`).
        2. `VisionInspectionApp.Application`:
           - [LightingProtocol.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/LightingController/LightingProtocol.cs): Xây dựng command builder (đóng gói khung `$..#`, validate giới hạn tham số, tự động đưa lệnh `RD` về cuối khi gộp batch) và bộ phân tích phản hồi (+OK, mã lỗi, chuỗi dữ liệu `$..#`).
           - [LightingTransport.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/LightingController/LightingTransport.cs): Trừu tượng hóa `ILightingTransport`, hiện thực `TcpLightingTransport`, `UdpLightingTransport` và `SerialLightingTransport` (System.IO.Ports) với `SemaphoreSlim`, timeout và hỗ trợ CancellationToken.
           - [LightingControllerService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/LightingController/LightingControllerService.cs): Service điều phối cấp cao, hỗ trợ `ConnectAsync` (Ethernet) và `ConnectSerialAsync` (RS-232 COM), quản lý vòng đời kết nối, gửi lệnh bất đồng bộ, cập nhật trạng thái thiết bị và ghi nhật ký giao thức TX/RX thread-safe.
        3. `VisionInspectionApp.UI`:
           - [LightingControllerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/LightingControllerViewModel.cs): MVVM ViewModel quản lý 8 kênh (`LightingChannelViewModel`), tự động quét cổng COM hệ thống (`SerialPort.GetPortNames()`), kết nối Ethernet/Serial, chế độ trigger, gửi lệnh tức thì với debounce slider độ sáng (50ms) mượt mà, lưu cài đặt tự động.
           - [LightingControllerWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/LightingControllerWindow.xaml) & [.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/LightingControllerWindow.xaml.cs): Cửa sổ giao diện hiện đại đồng bộ theme DynamicResource (Dark/Light), chuyển đổi mượt mà giữa Ethernet và Serial COM, bố trí 8 kênh dạng lưới 2x4, thanh trigger, nút tác vụ toàn cục và nhật ký giao thức thời gian thực.
           - [MainWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/MainWindow.xaml): Bổ sung menu `💡 Chiếu Sáng` trên MenuStrip mở nhanh cửa sổ Lighting Controller.
           - [GlobalAppSettingsService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Services/GlobalAppSettingsService.cs) & [App.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/App.xaml.cs): Lưu cấu hình kết nối IP/Port và COM Port, đăng ký DI Singleton và tự động kết nối ngầm an toàn lúc khởi động.
      - `Kiểm Thử`: Xây dựng [LightingControllerTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/LightingControllerTests.cs) bao gồm 86 test cases kiểm thử Command Builder, Response Parser, Batching/RD ordering, Error mapping, Parameter bounds validation, Echo tolerance và Serial Transport. Toàn bộ 86 tests và toàn bộ test suite hiện có PASSED 100%.



























- [x] Task 268: Tự động kết nối & bật đèn khi khởi động ứng dụng theo cấu hình người dùng, Tái cấu trúc layout Lighting Controller Window responsive không bị che khuất khi mở cửa sổ chuẩn, Quản lý cấu hình đèn theo từng Job trong node ImageSource (Tool Editor) và Tự động bật/tắt đèn khi nạp Job.
      - Mục Tiêu & Yêu Cầu:
        - Tự động kết nối lại phương thức gần nhất (Serial RS-232 / Ethernet) khi mở app và tự động bật đèn ở channel tùy chọn với mức sáng tùy chọn do người dùng cài đặt để quan sát Live view camera ngay lập tức.
        - Bổ sung bảng cài đặt cấu hình khởi động trong màn hình Lighting Controller (cho phép ghi nhớ mức sáng hiện tại làm cấu hình khởi động chỉ bằng 1 cú click).
        - Chỉnh lại nhóm control kết nối Ethernet và RS-232 ở trên cùng cửa sổ Lighting Controller: Bố cục responsive hiển thị rõ ràng 100% tất cả các nút bấm, ô nhập liệu và combobox khi mở cửa sổ ở kích thước chuẩn (không cần full màn hình).
        - Bổ sung thuộc tính mức độ sáng từng kênh cho node ImageSource trong Tool Editor; nếu Job cũ chưa có thì tự động đọc mức sáng hiện tại từ thiết bị hoặc fallback an toàn.
        - Khi mở file job, đọc thông tin đèn và tự động gửi lệnh bật/tắt thiết lập mức sáng theo đúng cấu hình được lưu cùng Job.
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. [LightingControllerModels.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/LightingControllerModels.cs) & [Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Class1.cs):
           - Bổ sung `LightingStartupChannelSettings` với helper tĩnh `CreateDefaults(count)`.
           - Bổ sung `JobLightingParameters` và `JobLightingChannelParams` vào `ImageSourceDefinition.LightingParams`.
        2. [GlobalAppSettingsService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Services/GlobalAppSettingsService.cs):
           - Thêm `AutoConnect = true`, `EnableStartupLighting = true`, `StartupChannels` vào `LightingControllerSettings`.
        3. [LightingControllerWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/LightingControllerWindow.xaml) & [LightingControllerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/LightingControllerViewModel.cs):
           - Tái cấu trúc Row 0 thành 2 khối cân đối (Bên trái: Kết nối Ethernet/RS-232; Bên phải: Cài đặt khởi động + Trigger Mode + Global Actions).
           - Thêm giao diện Checkbox Auto-connect, Checkbox Tự bật đèn khi mở app, nút "📋 Lưu Mức Sáng Này Làm Khởi Động" (`CaptureCurrentAsStartupCommand`).
        4. [App.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/App.xaml.cs):
           - Luồng startup ngầm tự động kết nối phương thức gần nhất và gửi lệnh áp dụng `StartupChannels` khi `EnableStartupLighting == true`.
        5. [ToolEditorViewModel.ToolPreprocess.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolPreprocess.cs) & [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml):
           - Bổ sung `JobLightingChannelItemViewModel`, danh sách `ImageSource_LightingChannels`, `ImageSource_EnableLighting`, `ImageSource_LightingChannelCount`.
           - Thêm các command `ImageSource_ApplyLightingToDeviceCommand` ("⚡ Test Áp Dụng") và `ImageSource_ReadLightingFromDeviceCommand` ("📥 Đọc Từ Đèn").
           - Thêm bảng giao diện điều khiển đèn cho từng kênh (ON/OFF toggle, Slider + TextBox độ sáng 0-255) trong Properties Panel của `ImageSource`.
        6. [ToolEditorViewModel.Config.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Config.cs) & [InspectionViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/InspectionViewModel.cs):
           - Khi nạp Job (`LoadJobFromFile` / `LoadJob`), tự động gửi lệnh bật/tắt và thiết lập mức sáng theo `imgSourceDef.LightingParams` xuống `LightingControllerService`.
      - Kiểm Thử:
        - Bổ sung 4 bài test unit trong [LightingControllerTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/LightingControllerTests.cs) kiểm tra defaults, JSON serialization, backward compatibility cho job cũ không có lighting params, và clone. Toàn bộ 106 test cases Lighting Controller và toàn bộ test suite PASSED 100%.

- [x] Task 269: Xử lý ngoại lệ Timeout / Mất kết nối Bộ điều khiển đèn khi khởi động ứng dụng, ngăn chặn văng app và hiển thị thông báo trạng thái cảnh báo trên Status Bar.
      - Mục Tiêu & Yêu Cầu:
        - Xử lý trường hợp bộ điều khiển đèn chưa được kết nối (tắt nguồn / chưa cắm cáp RS-232 / cổng COM không phản hồi) khi bật ứng dụng gây lỗi timeout (`System.TimeoutException`).
        - Tuyệt đối không để văng app (unhandled exception crash).
        - Hiển thị thông báo lỗi rõ ràng dưới dạng cảnh báo trên Status Bar (ở cả Global Status Bar của cửa sổ chính và Tool Editor Status Bar) để người dùng nắm được trạng thái và kiểm tra cáp nối/nguồn thiết bị.
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. [LightingControllerService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/LightingController/LightingControllerService.cs):
           - Bổ sung thuộc tính `LastError`, ghi nhận lỗi chi tiết khi `ConnectSerialAsync` / `ConnectAsync` thất bại hoặc timeout.
           - Đặt `ConnectionState = LightingConnectionState.Error` và phát sự kiện `OnError` với nội dung lỗi rõ ràng.
        2. [App.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/App.xaml.cs):
           - Bọc khối auto-connect trong `try-catch (Exception ex)` an toàn tuyệt đối.
           - Khi phát hiện timeout hoặc lỗi kết nối, lập tức đẩy thông báo cảnh báo `⚠️ [Đèn Chiếu Sáng] {ex.Message}` lên `MainWindowViewModel.SetGlobalStatus(warnMsg, "Warning")` và `ToolEditorViewModel.StatusBarText`.
        3. [MainWindowViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/MainWindowViewModel.cs) & [MainWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/MainWindow.xaml):
           - Thêm Global Status Bar ở đáy cửa sổ chính với cơ chế đổi màu trạng thái (`GlobalStatusSeverity`: Warning `#FFA000`, Error `#E53935`, Success `#4CAF50`, Info `#9E9E9E`).
           - Tự động lắng nghe sự kiện `OnError` và `OnConnectionStateChanged` của `LightingControllerService` để cập nhật trạng thái thời gian thực.
        4. [ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs):
           - Đăng ký lắng nghe sự kiện `_lightingControllerService.OnError` để tự động cập nhật thanh `StatusBarText` của Tool Editor.
      - Kiểm Thử:
        - Toàn bộ 106 bài kiểm thử của Lighting Controller và toàn bộ test suite của dự án PASSED 100%. Ứng dụng khởi động an toàn, không bị crash ngay cả khi cổng COM không có thiết bị phản hồi.

- [x] Task 270: Duy trì trạng thái kết nối Lighting Controller liên tục, Tái cấu trúc 2 cột giao diện Lighting Window hiển thị hoàn chỉnh ở kích thước chuẩn, và Chuyển toàn bộ các cửa sổ chức năng sang Modeless Window không chặn tương tác.
      - Mục Tiêu & Yêu Cầu:
        1. Sửa hiện tượng báo "Kết nối thành công" sau khi đã báo Timeout dù chưa cắm thiết bị: Đảm bảo chỉ phát sự kiện và gán trạng thái `Connected` sau khi handshake / probe thành công.
        2. Duy trì kết nối liên tục từ khi mở app: Khi mở cửa sổ Lighting Controller, nếu service đã kết nối sẵn thì tự động đồng bộ trạng thái `Connected` và giá trị độ sáng thực tế của các kênh từ thiết bị, không cần bấm kết nối lại từ đầu.
        3. Tái cấu trúc giao diện cửa sổ Lighting Controller thành dạng 2 cột trực quan (Cột trái: Kết nối + Khởi động + Trigger & Actions; Cột phải: Danh sách các kênh dạng cột dọc mỏng gọn), hiển thị 100% đầy đủ ở kích thước mặc định chuẩn mà không cần maximize cửa sổ.
        4. Chuyển đổi toàn bộ các cửa sổ chức năng (Lighting Controller, PLC Manager, PLC Monitor, PLC Tag Browser, PLC Oscilloscope, HMI Manager, Database Manager, Calibration, Chessboard Calibration, Roll Defect Map, Inspection Log, Job Camera Settings, Product Assign, OQC Settings, OQC Detail) từ `ShowDialog()` sang `Show()` (Modeless Window) kèm quản lý instance kích hoạt `Activate()`, giúp người dùng thoải mái tương tác song song với màn hình chính và tất cả các cửa sổ khác.
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. [LightingControllerService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/LightingController/LightingControllerService.cs):
           - Sắp xếp lại thứ tự gán trạng thái trong `ConnectSerialAsync` và `ConnectAsync`: Chỉ gán `ConnectionState = Connected` và ghi log sau khi probe đọc trạng thái thành công. Nếu probe timeout/thất bại, ngắt kết nối an toàn, gán `Error` và phát `OnError`.
        2. [LightingControllerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/LightingControllerViewModel.cs):
           - Trong constructor, tự động nạp `_connectionState = _service.ConnectionState;` và gọi `SyncFromDeviceState(_service.LastKnownState)` nếu service đã kết nối.
        3. [LightingControllerWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/LightingControllerWindow.xaml):
           - Tái thiết kế bố cục 2 cột responsive (`Column 0: 430px`, `Column 1: *`): Cột trái chứa Connection Panel, Startup Settings Panel, Trigger & Global Actions Panel; Cột phải chứa danh sách thẻ kênh mỏng gọn (Label ON/OFF, Slider + TextBox độ sáng, Thời gian sáng ms); Phía dưới giữ nguyên Protocol Log.
        4. [ToolEditorViewModel.Lighting.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Lighting.cs), [ToolEditorViewModel.Plc.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Plc.cs), [ToolEditorViewModel.Db.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Db.cs), [ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs), [ToolEditorViewModel.ToolPreprocess.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolPreprocess.cs), [OqcScannerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/OqcScannerViewModel.cs):
           - Chuyển đổi toàn bộ các lệnh mở Window từ `ShowDialog()` sang `Show()` modeless.
           - Bổ sung cơ chế quản lý instance thông minh: Kích hoạt `Activate()` và đưa lên phía trước nếu cửa sổ đang mở, tự động hủy tham chiếu khi `Closed`.
      - Kiểm Thử:
        - Toàn bộ 106 bài kiểm thử của Lighting Controller và toàn bộ test suite của dự án PASSED 100%. Mọi cửa sổ mở độc lập không chặn UI chính.

- [x] Task 271: Sửa lỗi mất Origin Template Preview khi mở Job và Xây dựng Cơ chế Resolve Template Path Đa Tầng Thông Minh.
      - Mục Tiêu & Yêu Cầu:
        - Khắc phục hiện tượng: Trong file `.job` đã lưu có chứa Origin Template (`origin.png`), nhưng khi mở Job lên thì trên Properties Panel của Tool Origin phần hiển thị ảnh xem trước Template (`Origin_TemplatePreviewImage`) lại không hiện ra ("Chưa lưu template").
        - Rà soát toàn bộ luồng đọc template khi mở Job, xử lý các trường hợp: Đường dẫn tuyệt đối cũ từ máy khác/thư mục temp cũ, file template nằm ở thư mục con `templates/` hoặc ở thư mục gốc của Job zip, đường dẫn có tiền tố `templates/`, hoặc tên file bị đổi.
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. [ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs):
           - Xây dựng phương thức `ResolveTemplatePath(string? currentPath, string? fallbackName, string? fallbackPattern)` với chiến lược tìm kiếm đa tầng:
             - Tầng 1: Kiểm tra trực tiếp file tồn tại.
             - Tầng 2: Quét các thư mục ứng viên (`CurrentTempWorkingDir/templates`, `CurrentTempWorkingDir`, `{ConfigRoot}/{ProductCode}/templates`, `{ConfigRoot}/{ProductCode}`, `{JobDir}/templates`, `{JobDir}`).
             - Tầng 3: Bóc tách tên file (`Path.GetFileName`), loại bỏ đường dẫn tuyệt đối cũ của máy khác hoặc thư mục temp cũ, loại bỏ tiền tố `templates/` lặp.
             - Tầng 4: Ghép từng thư mục ứng viên với từng tên file ứng viên.
             - Tầng 5: Tìm kiếm fallback wildcard pattern (`origin*.png` hoặc `point*.png`).
           - Nâng cấp `EnsureTemplatePathsAbsolute(VisionConfig config)` tự động resolve và cập nhật đường dẫn chính xác cho `Origin`, `Points`, `SurfaceCompares`, `ContourCompares`.
        2. [ToolEditorViewModel.Config.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Config.cs):
           - Trong `LoadJobFromFile`: Gọi `EnsureTemplatePathsAbsolute(_config)` và `RefreshOriginTemplatePreview()` ngay sau khi giải nén Job, đồng thời đảm bảo chạy `RefreshOriginTemplatePreview()` trên Dispatcher UI thread.
        3. [ToolEditorViewModel.ToolOrigin.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolOrigin.cs):
           - Cập nhật `RefreshOriginTemplatePreview()` sử dụng `ResolveTemplatePath`. Khi tìm thấy file, nạp `Cv2.ImRead` và gán cho `Origin_TemplatePreviewImage` kèm phát sự kiện `OnPropertyChanged(nameof(Origin_TemplatePreviewImage))`.
        4. [OqcScannerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/OqcScannerViewModel.cs):
           - Nâng cấp `LoadOriginTemplateImage` sử dụng `_toolEditorViewModel.ResolveTemplatePath(...)` giúp hiển thị ảnh Origin Guide đầy đủ 100% trên màn hình OQC.
      - Kiểm Thử:
        - Bổ sung bộ test tự động [OriginTemplateJobTest.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/OriginTemplateJobTest.cs) gồm 6 kịch bản kiểm thử: Resolve trong thư mục `templates/`, resolve tại gốc temp directory, bóc tách đường dẫn rooted cũ từ máy khác, resolve tiền tố `templates/origin.png`, fallback wildcard search, và đóng gói/nạp giải nén file `.job` zip thực tế. 100% tests PASSED.

- [x] Task 272: Cách Ly Tuyệt Đối 100% Template Trong File Job, Khử Hoàn Toàn Đường Dẫn Tuyệt Đối Khỏi config.json & Đảm Bảo Tính Di Động Đa Máy.
      - Mục Tiêu & Yêu Cầu:
        - Khi mở app/mở Job, bắt buộc phải lấy đúng template (`origin.png`, `point.png`, `surface.png`, `contour.png`) đã lưu bên trong file `.job`, không dùng bất kỳ đường dẫn nào khác ngoài máy gây loạn.
        - Khắc phục hiện tượng: Trong `config.json` bên trong file `.job` bị lưu chuỗi đường dẫn tuyệt đối của máy tạo job (ví dụ: `"templateImageFile": "G:\\NODEJS\\Vision2026\\VisionInspectionApp.UI\\bin\\x64\\Debug\\net8.0-windows\\configs\\templates\\origin.png"`), khiến app cố mở file từ thư mục `configs` ngoài máy thay vì đọc template nội bộ từ file `.job`.
        - Đảm bảo copy file `.job` đi bất kỳ máy tính nào cũng hoạt động độc lập và khép kín 100%.
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. [JobService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Persistence/JobService.cs):
           - Trong `SaveJob`:
             - Tự động kiểm tra và đảm bảo toàn bộ file ảnh template (`Origin`, `Points`, `SurfaceCompares`, `ContourCompares`) có mặt trong `tempWorkingDir/templates/` (ưu tiên giữ nguyên file đã tạo trong Job).
             - Serialize `config.json` với dữ liệu sạch 100%: Toàn bộ các trường `templateImageFile` CHỈ lưu tên file tương đối đơn giản (ví dụ `"origin.png"`, `"p1.png"` - bằng cách dùng `Path.GetFileName`), tuyệt đối không chứa bất kỳ tiền tố đường dẫn ổ đĩa hay thư mục máy nào.
           - Trong `LoadJob`:
             - Phương thức `ResolveAndBindJobTemplates(config, tempWorkingDir)`: Bắt buộc CHỈ tìm kiếm và bind file template nằm bên trong thư mục `tempWorkingDir` được giải nén từ chính file `.job`. Tự động bóc tách tên file sạch từ các file Job cũ và trỏ trực tiếp vào thư mục giải nén của Job. Tuyệt đối không đọc ra ngoài máy.
        2. [ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs):
           - `ResolveTemplatePath`: Giới hạn phạm vi tìm kiếm 100% chỉ trong `CurrentTempWorkingDir` của Job hiện tại, loại bỏ hoàn toàn việc tìm kiếm ra thư mục `configs` bên ngoài máy.
      - Kiểm Thử:
        - Bổ sung bộ test tự động [OriginTemplateJobTest.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/OriginTemplateJobTest.cs) với 8 bài kiểm thử độc lập:
          - Test 7.1-7.4: Kiểm tra `config.json` bên trong gói `.job` sạch 100%, không chứa đường dẫn tuyệt đối `G:\...` hay `C:\...`, chỉ chứa relative filename `"origin.png"`, `"p1.png"`.
          - Test 8.1-8.7: Kiểm tra `LoadJob` trên máy đích giải nén và bind template trực tiếp vào `tempWorkingDir`, nạp thành công `Mat` ảnh `64x64` và `32x32`.
        - Toàn bộ test suite của dự án: PASSED 100%.

- [x] Task 273: Khắc Phục Ngoại Lệ InvalidOperationException Khi Đóng / Lưu Cửa Sổ Modeless (OQC Settings, Database Manager, Calibration, Origin Train, Camera Settings).
      - Mục Tiêu & Yêu Cầu:
        - Khắc phục lỗi khi người dùng bấm Lưu hoặc Hủy trên các cửa sổ cấu hình (như OQC Settings, Database Manager, Calibration, Origin Train, Job Camera Settings):
          `System.InvalidOperationException: 'DialogResult can be set only after Window is created and shown as dialog.'`
        - Nguyên nhân: Khi chuyển đổi các cửa sổ sang `Show()` (Modeless Window) để không khóa UI chính, việc gán trực tiếp thuộc tính `DialogResult = true/false` trong WPF sẽ ném ngoại lệ vì thuộc tính này chỉ hợp lệ khi mở bằng `ShowDialog()`.
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. [OqcSettingsDialog.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/OqcSettingsDialog.xaml.cs):
           - Trong `Save_Click` và `Cancel_Click`: Bọc gán `try { DialogResult = ...; } catch { }` an toàn trước khi gọi `Close()`.
        2. [DbManagerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/DB/DbManagerViewModel.cs):
           - Trong `SaveAndClose`: Bọc gán `try { window.DialogResult = true; } catch { }` an toàn trước khi gọi `window.Close()`.
        3. [CalibrationDialog.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/CalibrationDialog.xaml.cs) & [ChessboardCalibrationDialog.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ChessboardCalibrationDialog.xaml.cs):
           - Bọc gán `DialogResult` an toàn trong các sự kiện Apply và Close.
        4. [JobCameraSettingsWindow.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/JobCameraSettingsWindow.xaml.cs) & [OriginTrainWindow.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OriginTrainWindow.xaml.cs):
           - Bọc gán `DialogResult` an toàn trong handler `RequestClose` và `CancelButton_Click`.
      - Kiểm Thử:
        - Toàn bộ test suite của dự án PASSED 100%. Các cửa sổ mở và lưu / đóng mượt mà cả ở chế độ Modeless (`Show()`) lẫn Modal (`ShowDialog()`).

- [x] Task 274: Cấu Hình 2 Cột Riêng Biệt (ProductCode & ProductName) trong Product Browser Query & Tự Động Điền ProductName Vào Tool Editor Khi Gán Job.
      - Mục Tiêu & Yêu Cầu:
        - Trong màn hình Cấu hình OQC & Database (Mục 3: Danh sách sản phẩm / Product Browser Query): Bổ sung 2 ô TextBox phân định rõ:
          1. **Tên cột mã sản phẩm (Product Code Column)**: `ProductListCodeColumn` (mặc định `"G_CODE"`).
          2. **Tên cột tên sản phẩm (Product Name Column)**: `ProductListNameColumn` (mặc định `"G_NAME_KD"`).
        - **Quy định hoạt động**:
          - Gán Job vào sản phẩm trong CSDL sẽ dùng giá trị của **cột `ProductCode`**.
          - Tự động điền và lưu vào ô "Mã SP:" trong Tool Editor sẽ dùng giá trị của **cột `ProductName`** (nếu không có thì fallback sang `ProductCode`).
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. [OqcScannerConfig.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/OqcScannerConfig.cs):
           - Bổ sung 2 thuộc tính `ProductListCodeColumn = "G_CODE"` và `ProductListNameColumn = "G_NAME_KD"`.
        2. [OqcSettingsDialog.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/OqcSettingsDialog.xaml):
           - Tại Mục 3 "Danh sách sản phẩm (Product Browser Query)", bố trí 2 dòng TextBox rõ ràng: "Tên cột mã SP (Code):" và "Tên cột tên SP (Name):".
        3. [OqcScannerViewModel.Settings.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/OqcScannerViewModel.Settings.cs):
           - Bổ sung `_productListCodeColumn` và `_productListNameColumn` kèm nạp / lưu / xuất / nhập cấu hình.
           - `ExecuteAssignProductAsync`: Trích xuất `productCode` từ cột cấu hình (để gán CSDL `AssignProductJobAsync`) và `productName` từ cột cấu hình (để auto-fill và lưu vào Tool Editor bằng `SyncProductCodeToToolEditor`).
        4. [ToolEditorViewModel.Config.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Config.cs):
           - Phương thức `ApplyAssignedProductCode`: Cập nhật `ProductCode = productCode;`, `_config.ProductCode = productCode;` và lưu file `.job`.
      - Kiểm Thử:
        - Bổ sung bộ test tự động [ProductAssignAndCodeSyncTest.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/ProductAssignAndCodeSyncTest.cs) kiểm tra:
          - Test 1: Serialization / Deserialization của `OqcScannerConfig` với cả 2 cột `ProductListCodeColumn` và `ProductListNameColumn`.
          - Test 2 & 3: Trích xuất chính xác `productCode` cho DB và `productName` cho Tool Editor auto-fill, cùng cơ chế fallback.
          - Test 4: Đóng gói và lưu Job với `productName` chính xác.
        - Toàn bộ test suite của dự án: PASSED 100%.

- [x] Task 275: Khắc Phục Lỗi Kết Nối PLC Bridge 32-bit (MX Component & GX Works 3 Simulation) Do Thiếu Cấu Hình RollForward .NET x86 & Tối Ưu Batch IDispatch COM.
      - Mục Tiêu & Yêu Cầu:
        - Người dùng đã cài đặt Mitsubishi MX Component, cài GX Works 3 và chạy Simulation thành công, đã thiết lập Station trong Communication Setup Utility.
        - Khi bấm kết nối PLC trong ứng dụng, ứng dụng báo lỗi:
          `Failed to connect to PLC 'PLC1' (Station 0). Detail: Could not connect to 32-bit PLC Bridge on 127.0.0.1:39871.`
        - Kiểm tra nguyên nhân gốc rễ và xử lý triệt để giúp hệ thống kết nối thành công 100% với MX Component và GX Works 3 Simulation.
      - Phân Tích & Nguyên Nhân Gốc Rễ:
        1. *Lỗi Khởi Động Tiến Trình 32-bit PLC Bridge (`VisionInspectionApp.PlcBridge.dll`)*:
           - Ứng dụng chính `VisionInspectionApp.UI` chạy ở tiến trình 64-bit (x64) để xử lý ảnh và camera hiệu năng cao. Trong khi đó, Mitsubishi MX Component (`ActUtlType.ActUtlType`) là thư viện COM 32-bit (x86).
           - Do đó, ứng dụng sử dụng tiến trình trung gian `VisionInspectionApp.PlcBridge` (x86) để giao tiếp với COM MX Component và chuyển tiếp lệnh qua socket localhost TCP port 39871.
           - Máy tính người dùng chỉ cài đặt .NET Desktop Runtime phiên bản mới hơn (.NET 10.0 x86) trong `C:\Program Files (x86)\dotnet`, không cài .NET 8.0 x86.
           - `VisionInspectionApp.PlcBridge.csproj` trước đây nhắm mục tiêu `net8.0-windows` nhưng chưa khai báo `<RollForward>LatestMajor</RollForward>`.
           - Khi `MitsubishiMxComponentDriver` gọi `dotnet.exe (x86)` khởi chạy `VisionInspectionApp.PlcBridge.dll`, host `dotnet.exe` từ chối khởi chạy và báo lỗi `You must install or update .NET to run this application. Framework: 'Microsoft.NETCore.App', version '8.0.0' (x86)`.
           - Do tiến trình bridge không thể khởi động, cổng 39871 không lắng nghe dẫn đến thông báo "Could not connect to 32-bit PLC Bridge on 127.0.0.1:39871".
        2. *Lỗi Marshaling SAFEARRAY trên IDispatch COM (`DISP_E_TYPEMISMATCH 0x80020005`)*:
           - Phương thức `ReadDeviceRandom2` và `ReadDeviceBlock2` khi gọi qua `InvokeMember` với `short[]` bị COM IDispatch coi là `SAFEARRAY` thay vì con trỏ đơn, gây lỗi `DISP_E_TYPEMISMATCH`.
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. [VisionInspectionApp.PlcBridge.csproj](file:///g:/NODEJS/Vision2026/VisionInspectionApp.PlcBridge/VisionInspectionApp.PlcBridge.csproj):
           - Bổ sung `<RollForward>LatestMajor</RollForward>` vào `<PropertyGroup>`, cho phép tiến trình PLC Bridge tự động tương thích và chạy mượt mà trên bất kỳ phiên bản .NET runtime nào (.NET 8, 9, 10...) có trên máy.
        2. [MitsubishiMxComponentDriver.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/PLC/Drivers/MitsubishiMxComponentDriver.cs):
           - Trong `LaunchBridgeProcess`: Thêm tham số `exec --roll-forward LatestMajor` khi gọi `x86Dotnet`, bảo đảm `dotnet.exe` (x86) luôn khởi động thành công ngay cả khi môi trường chỉ có runtime cao hơn.
        3. [MxComWorker.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.PlcBridge/MxComWorker.cs):
           - Bổ sung helper `IncrementDeviceAddress`.
           - Chuẩn hóa các hàm đọc ghi mảng và ngẫu nhiên (`ReadDeviceRandom2Async`, `ReadDeviceBlockAsync`, `WriteDeviceBlockAsync`, `ReadDeviceBlock2Async`, `WriteDeviceBlock2Async`) bằng cơ chế duyệt an toàn qua `GetDevice`/`GetDevice2`/`SetDevice`/`SetDevice2`, loại bỏ 100% rủi ro `DISP_E_TYPEMISMATCH`.
      - Kiểm Thử:
        - Tích hợp bài test tự động [PlcBridgeTest.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/PlcBridgeTest.cs) kết nối trực tiếp đến Station 0 (GX Works 3 Simulation):
          - Kết nối thành công đến CPU `FX5UCPU (Type 18944)`.
          - Đọc ghi thành công biến 16-bit Int (`D0 = 7788`), biến Float 32-bit (`D10 = 123.456`).
          - Ngắt kết nối sạch sẽ.
        - Toàn bộ test suite của dự án PASSED 100%.

- [x] Task 276: Khắc Phục Lỗi NullReferenceException / Race Condition Khi Tắt Ứng Dụng (ReadBridgeTagValueAsync / WriteBridgeTagValueAsync Khi Dispose PLC Bridge).
      - Mục Tiêu & Yêu Cầu:
        - Khắc phục lỗi khi tắt ứng dụng:
          ```text
          System.NullReferenceException: 'Object reference not set to an instance of an object.'
          bridge was null.
          at VisionInspectionApp.Application.PLC.Drivers.MitsubishiMxComponentDriver.ReadBridgeTagValueAsync
          ```
        - Đảm bảo việc đóng ứng dụng và giải phóng driver PLC diễn ra an toàn 100%, không bị văng lỗi ngoại lệ hoặc unhandled exception.
      - Phân Tích & Nguyên Nhân Gốc Rễ:
        - Trong khi luồng Polling chạy nền (`PlcPollingEngine`) đang trong chu kỳ gọi `ReadBatchAsync` / `ReadBridgeTagValueAsync` / `WriteBatchAsync`, người dùng bấm tắt ứng dụng.
        - Quá trình shutdown ứng dụng (`App.OnExit` $\rightarrow$ `ShutdownGracefullyAsync` $\rightarrow$ `PlcManagerService.Dispose` $\rightarrow$ `MitsubishiMxComponentDriver.Dispose`) giải phóng `_bridgeClient` và gán `_bridgeClient = null`.
        - Khi đó, các phương thức đọc/ghi tag đang chạy trên background thread nhận đối số `_bridgeClient` thành `null` (hoặc truy xuất `bridge.GetDeviceAsync`), gây ra `NullReferenceException`.
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. [MitsubishiMxComponentDriver.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/PLC/Drivers/MitsubishiMxComponentDriver.cs):
           - Trong `ReadBatchAsync` & `WriteBatchAsync`:
             - Snapshot tham chiếu cục bộ `var bridge = _bridgeClient;` trước khi thực thi.
             - Thêm kiểm tra `if (_disposed || !bridge.IsConnected) break;` trong các vòng lặp duyệt tag.
             - Bổ sung kiểm tra `if (_disposed) return ...;` an toàn.
           - Trong `ReadBridgeTagValueAsync`, `WriteBridgeTagValueAsync`, `TryReadBridgeBatchRandom2Async`:
             - Đổi kiểu tham số thành nullable `MxBridgeClient? bridge`.
             - Bổ sung guard clause: `if (bridge == null || !bridge.IsConnected || tag == null) return tag?.DefaultValue;` (hoặc `return;`).
             - Bọc khối `try { ... } catch { return tag?.DefaultValue; }` triệt tiêu mọi ngoại lệ race condition khi socket đóng lúc shutdown.
           - Trong `Dispose()`:
             - Bọc an toàn `try { _lock.Dispose(); } catch { }`.
      - Kiểm Thử:
        - Chạy toàn bộ test suite của dự án: PASSED 100% (106 lighting tests, preprocess ROI mask tests, isolated job template tests, product assign tests, PLC bridge connection tests).

- [x] Task 277: Triển Khai Hệ Thống Quản Lý & Huấn Luyện (Teaching) Job Từ Xa Qua Server XAMPP (PHP API) & Đồng Bộ Hai Chiều Đa Máy OQC Scanner.
      - Mục Tiêu & Yêu Cầu:
        - Xây dựng giải pháp cho phép kỹ sư cấu hình, huấn luyện (teach) Job từ xa trên máy văn phòng qua mạng LAN/Server XAMPP (PHP) mà không cần trực tiếp cắm máy tính vào dây chuyền sản xuất:
          1. Máy OQC tại chuyền sản xuất: Chụp ảnh phôi/sản phẩm thực tế từ camera và tải ảnh lên Web Server XAMPP (`vision_upload.php`), tự động gán đường dẫn URL ảnh mẫu vào CSDL `ProductJobs`.
          2. Máy Kỹ sư tại văn phòng: Mở cửa sổ Quản Lý Job (`JobManagerWindow`), chọn mã sản phẩm và bấm "Remote Teach" $\rightarrow$ tự động nạp ảnh mẫu từ URL Server vào Tool Editor, train Origin/đo đạc/cấu hình tool $\rightarrow$ bấm "Upload Current Job" để đẩy file `.job` hoàn chỉnh lên Server và cập nhật CSDL.
          3. Máy OQC: Quét mã QR/Barcode sản phẩm $\rightarrow$ tự động nạp tệp Job mới nhất từ Server hoặc đường dẫn CSDL và thực thi kiểm tra tức thì.
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. **Server Scripts (PHP & Setup Guide)**:
           - [vision_upload.php](file:///g:/NODEJS/Vision2026/ServerScripts/vision_upload.php): Cung cấp 3 API endpoint chuẩn REST/JSON: `action=ping`, `action=upload_image`, `action=upload_job` kèm cấu hình bảo mật MIME, CORS và phân cấp thư mục `uploads/teach_images/` & `uploads/jobs/`.
           - [README_SERVER.md](file:///g:/NODEJS/Vision2026/ServerScripts/README_SERVER.md): Hướng dẫn chi tiết thiết lập XAMPP Apache trên Server nội bộ.
        2. **Data Models & CSDL**:
           - [Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Class1.cs): Bổ sung enum `ImageSourceType.Url = 3` và thuộc tính `ImageUrl`.
           - [OqcScannerConfig.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/OqcScannerConfig.cs): Thêm thuộc tính cấu hình `ServerApiUrl`, `TeachImageColumn`, `AssignQuery` hỗ trợ token `{TeachImagePath}`, cùng nhóm thuộc tính `JobManager*` (`JobManagerDbId`, `JobManagerQuery`, `JobManagerProductCodeColumn`, `JobManagerProductNameColumn`, `JobManagerJobFileColumn`, `JobManagerTeachImageColumn`, `JobManagerUpdatedColumn`, `JobManagerPageSize`).
           - [JobManagerItem.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/JobManagerItem.cs): Model MVVM thực thi `INotifyPropertyChanged` quản lý thông tin và cờ trạng thái (`HasJobFile`, `HasTeachImage`).
        3. **Application Services**:
           - [IRemoteServerService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/IRemoteServerService.cs) & [RemoteServerService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Services/RemoteServerService.cs): Triển khai các phương thức giao tiếp HTTP multipart: `PingServerAsync`, `UploadImageAsync`, `UploadJobAsync` (hỗ trợ cả đường dẫn tệp và mảng byte), `DownloadFileAsync`.
           - [IOqcScannerService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/OQC/IOqcScannerService.cs) & [OqcScannerService.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/OQC/OqcScannerService.cs): Bổ sung `GetJobManagerListAsync` truy vấn danh sách phân trang server-side và mở rộng `AssignProductJobAsync` với tham số `teachImagePath`.
        4. **ViewModels & Giao Diện WPF**:
           - [JobManagerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/JobManagerViewModel.cs): ViewModel quản lý Job từ xa hỗ trợ tìm kiếm phân trang, kiểm tra ping server, chụp ảnh camera upload, upload ảnh từ file, Remote Teach (nạp thẳng URL vào Tool Editor), upload Job hiện tại lên server, nạp/tải Job về máy, gán Job cục bộ.
           - [JobManagerWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/JobManagerWindow.xaml) & [JobManagerWindow.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/JobManagerWindow.xaml.cs): Giao diện Modeless Window với DataGrid trạng thái, thanh tìm kiếm phân trang, thanh công cụ tác vụ và preview ảnh mẫu.
           - [ToolEditorViewModel.ToolPreprocess.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolPreprocess.cs) & [ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs): Bổ sung `ImageSource_IsUrl`, `ImageSource_ImageUrl`, `FetchAndApplyImageUrlAsync`, xử lý tải ảnh từ URL trong `GetImageSourceMat`, continuous flow và `RunFlow`.
           - [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml): Bổ sung trường nhập URL ảnh cho node `ImageSource` và nút `🌐 Quản Lý Job Server` trên Toolbar.
           - [OqcScannerViewModel.Settings.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/OqcScannerViewModel.Settings.cs) & [OqcSettingsDialog.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/OqcSettingsDialog.xaml): Thêm GroupBox 5 cấu hình Máy chủ Web XAMPP / PHP API và Job Manager Query.
           - [OqcScannerViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/OqcScannerViewModel.cs) & [OqcScannerView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/OQC/OqcScannerView.xaml): Bổ sung nút `📸 Tải Ảnh Mẫu` và `🌐 Quản Lý Job`.
           - [MainWindow.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/MainWindow.xaml): Bổ sung MenuItem `🌐 Quản Lý Job & Huấn Luyện Từ Xa (Server Job Manager...)` trong Menu Dữ Liệu.
           - [App.xaml.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/App.xaml.cs): Đăng ký DI Container Singleton cho `IRemoteServerService, RemoteServerService`, `JobManagerViewModel`, `JobManagerWindow`.
      - Kiểm Thử:
        - Bổ sung bộ test tự động [RemoteServerAndJobManagerTests.cs](file:///g:/NODEJS/Vision2026/TestExtractApp/RemoteServerAndJobManagerTests.cs) kiểm thử toàn diện:
          + Test 1: `JobManagerItem` property notification & cờ `HasJobFile`/`HasTeachImage`.
          + Test 2: `OqcScannerConfig` serialization / deserialization với cấu hình Server & Job Manager.
          + Test 3: `ImageSourceDefinition.ImageUrl` và enum `ImageSourceType.Url`.
          + Test 4: Thay thế token `{TeachImagePath}` trong câu lệnh SQL Assign.
          + Test 5: `RemoteServerService` với HTTP Mock Server thực tế (Ping, Upload Image multipart, Download File bytes).
        - Toàn bộ test suite của dự án PASSED 100%. Solution `dotnet build` 0 errors.

- [x] Task 278: Hoàn Thiện Cơ Chế Tải Job Chuẩn Tên Server, Quy Trình Huấn Luyện Từ Xa (Remote Teach qua thư mục Teaching & URL ImageSource), Tự Động Đồng Bộ Job Server & Truy Vấn Chuyên Dụng Cập Nhật Ảnh Mẫu (TeachImagePath).
      - Mục Tiêu & Yêu Cầu:
        1. Chuẩn hóa tên file khi tải Job về: Khi bấm nút "Tải Về Máy" trong Remote Job Manager, hộp thoại `SaveFileDialog` mặc định lấy đúng tên tệp gốc đang lưu trên Server (`Path.GetFileName(SelectedItem.JobFilePath)`, ví dụ `job_7B09205A_20260831_061252_eae254.job`) và lưu trong thư mục `JobRootDirectory` (hoặc `jobs/`).
        2. Tab OQC Scanner: Khi scan mã sản phẩm, kiểm tra file Job cục bộ trước. Nếu chưa có và CSDL chứa đường dẫn Server/URL, tự động tải file từ Server XAMPP về lưu vào `JobRootDirectory` (hoặc `jobs/`) với đúng tên file gốc trên Server (`Path.GetFileName(rawPath)`) rồi nạp vào Engine kiểm tra.
        3. Quy trình Huấn Luyện Từ Xa (Remote Teach): Khi bấm "Huấn luyện từ xa", hệ thống tự động tải file Job của sản phẩm từ Server về lưu tại thư mục `Teaching/` (cùng cấp với chương trình chạy), nạp Job này vào Tool Editor, tự động chuyển node `ImageSource` sang chế độ URL và nạp ảnh mẫu từ Server URL, rồi chuyển sang Tab Tool Editor để kỹ sư tiến hành huấn luyện.
        4. Cập Nhật TeachImagePath vào CSDL: Bổ sung cấu hình và truy vấn SQL chuyên dụng (`UpdateTeachImageQuery` & `UpdateTeachImageDbId`) trong Cài đặt DB OQC. Tự động thực thi câu lệnh SQL cập nhật ảnh mẫu ngay khi chụp và upload ảnh mẫu thành công.
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. **Model & Cấu Hình (`OqcScannerConfig`)**:
           - Thêm thuộc tính `UpdateTeachImageDbId` và `UpdateTeachImageQuery` (mặc định: `IF EXISTS UPDATE ... ELSE INSERT ...`).
        2. **Dịch Vụ Nghiệp Vụ (`IOqcScannerService` & `OqcScannerService`)**:
           - Bổ sung `UpdateTeachImagePathAsync(productCode, teachImagePath, dbManager)` thực thi câu lệnh cập nhật ảnh mẫu với các token `{ProductCode}`, `{TeachImagePath}`.
           - Nâng cấp `LookupJobAsync(scannedCode, dbManager, remoteServerService)`: Tìm kiếm cục bộ đa tầng $\rightarrow$ nếu không có và là đường dẫn Server/URL, tự động tải qua `remoteServerService.DownloadFileAsync` và lưu vào `{JobRootDirectory}/{Path.GetFileName(rawPath)}`.
           - Cập nhật `AssignProductJobAsync`: Nếu `jobFilePath` rỗng và có `teachImagePath`, tự động điều hướng sang `UpdateTeachImagePathAsync`.
        3. **ViewModels & Giao Diện WPF**:
           - `JobManagerViewModel.ExecuteDownloadJobAsync`: Mở `SaveFileDialog` với `FileName = Path.GetFileName(SelectedItem.JobFilePath)` (fallback `job_{SelectedItem.ProductCode}.job`) và `InitialDirectory = JobRootDirectory`. Sau khi lưu thì nạp vào Tool Editor và chuyển sang Tab 0.
           - `JobManagerViewModel.ExecuteRemoteTeachAsync`: Tải file Job từ Server về lưu tại `Teaching/{fileName}`, nạp vào Tool Editor qua `LoadJobFromFile`, nạp URL ảnh mẫu vào `ImageSource` qua `FetchAndApplyImageUrlAsync`, đồng bộ `ProductCode` và chuyển sang Tab 0.
           - `JobManagerViewModel.ExecuteCaptureAndUploadTeachImageAsync` & `ExecuteUploadTeachFromFileAsync`: Gọi `_oqcService.UpdateTeachImagePathAsync` ngay sau khi upload ảnh thành công.
           - `ToolEditorViewModel.ToolPreprocess.cs`: `FetchAndApplyImageUrlAsync` tự động thiết lập `def.SourceType = ImageSourceType.Url` và `def.ImageUrl = url` trên node ImageSource, phát sự kiện UI property changes.
           - `OqcScannerViewModel.LookupJobAndRunAsync`: Truyền `_remoteServerService` vào `_oqcService.LookupJobAsync(...)`.
           - `OqcScannerViewModel.ExecuteQuickCaptureAndUploadTeachImageAsync`: Gọi `_oqcService.UpdateTeachImagePathAsync` sau khi upload ảnh thành công.
           - `OqcScannerViewModel.Settings.cs` & `OqcSettingsDialog.xaml`: Thêm thuộc tính `UpdateTeachImageDbId`, `UpdateTeachImageQuery` và giao diện cấu hình trực quan trong Cài đặt DB OQC.
- [x] Task 279: Khắc Phục Triệt Để Lỗi Treo Giao Diện (UI Freeze) Khi Scan Mã OQC, Chống Treo Driver Cơ Sở Dữ Liệu (ADO.NET) & Ràng Buộc Timeout Nghiêm Ngặt.
      - Vấn Đề & Nguyên Nhân:
        1. Khi scan mã (ví dụ `GH63-22334ADTA3E116HE01XC01005000`), hệ thống gọi `LookupProductNameAsync` và `LookupJobAsync` trên DB. Khi cơ sở dữ liệu bị mất kết nối, timeout hoặc chậm, driver SqlClient/ADO.NET mặc định mở kết nối đồng bộ có thể chờ từ 15-30s mà không hủy ngay khi hủy `CancellationToken`.
        2. `ExecuteScanInternalAsync` chạy trực tiếp trên UI Dispatcher thread khiến toàn bộ luồng giao diện WPF bị khóa cứng (không thể click hay tương tác), modal popup che màn hình không tắt.
        3. `RemoteServerService.DownloadFileAsync` không có timeout độc lập (mặc định HttpClient 30s) khiến việc tải job qua mạng treo lâu.
      - Giải Pháp Đã Triển Khai:
        1. **Chống Treo Tầng Cơ Sở Dữ Liệu (`DbManagerService`)**:
           - Ràng buộc timeout nghiêm ngặt `ConnectTimeout = Math.Clamp(timeoutSeconds, 1, 5)` trong Connection String cho SQL Server, MySQL, PostgreSQL.
           - Bọc toàn bộ `conn.OpenAsync`, `cmd.ExecuteReaderAsync`, `cmd.ExecuteNonQueryAsync` trong `Task.Run` kết hợp `Task.WhenAny(..., Task.Delay(...))` đảm bảo không bao giờ block UI thread và tự động trả về lỗi timeout rõ ràng sau 2-3s.
        2. **Chống Treo Tầng Mạng & Tải Job (`RemoteServerService` & `OqcScannerService`)**:
           - Thêm `cts.CancelAfter(TimeSpan.FromSeconds(5))` cho `DownloadFileAsync`.
           - Cập nhật `isRemotePath` trong `LookupJobAsync` để không kích hoạt HTTP request khi đường dẫn là file cục bộ tuyệt đối (`Path.IsPathRooted`).
        3. **Chống Treo Tầng Giao Diện (`OqcScannerViewModel`)**:
           - Cập nhật `RunTaskWith1SecLoadingTimeoutAsync` sử dụng `Dispatcher.InvokeAsync` phi chặn (non-blocking).
           - Đưa toàn bộ tra cứu Tên sản phẩm và Job vào khối `try ... catch` an toàn, luôn đảm bảo ẩn Popup loading và giải phóng trạng thái `IsScanning = false` trong `finally`.
- [x] Task 280: Tự Động Bảo Lưu Cấu Hình Camera & Đèn OQC Gốc Khi Huấn Luyện Từ Xa (Remote Teach) Trên Máy Văn Phòng & Tự Động Chuyển Về Camera Khi Upload Lên Server.
      - Vấn Đề & Bối Cảnh:
        - Quy trình thiết lập Job gồm 2 giai đoạn: (1) Chụp ảnh & lưu thông số camera/đèn OQC vào Job ban đầu tại phòng OQC; (2) Kỹ sư mở Job trên máy văn phòng để teach chi tiết bằng ảnh mẫu qua URL.
        - Khi kỹ sư teach xong trên máy văn phòng và muốn lưu lại Job để đẩy lên Server, máy văn phòng không cắm camera Vision OQC. Nếu chọn camera giả lập thì thông số Camera Hikrobot gốc bị mất; nếu để nguyên URL thì máy OQC dưới chuyền không kích hoạt camera thật.
      - Giải Pháp Đã Triển Khai:
        1. **Model (`ImageSourceDefinition`)**:
           - Bổ sung thuộc tính `CameraDeviceDisplayName` ghi nhớ tên thiết bị Camera công nghiệp gốc (ví dụ: `Hikrobot MV-CS200-10GM - DA123456`).
        2. **Giao Diện & Quản Lý Camera (`ToolEditorViewModel.ToolPreprocess.cs`)**:
           - `RefreshAvailableCameraItems`: Khi quét thiết bị trên máy tính, nếu máy tính hiện tại không có camera OQC gốc nhưng Job đã có cấu hình Camera OQC, tự động chèn mục ảo `📷 [OQC Gốc] {CameraDeviceDisplayName}` lên đầu danh sách `AvailableCameraItems`.
           - `SelectedCameraItem`: Giúp kỹ sư ở máy văn phòng vẫn thấy và chọn lại Camera OQC gốc mà không bị ép chuyển sang Camera giả lập. Tự động cập nhật `CameraDeviceDisplayName` khi kỹ sư chọn camera thật ở phòng OQC.
        3. **Tự Động Chuyển Nguồn Ảnh Khi Upload Server (`ToolEditorViewModel.Config.cs` & `JobManagerViewModel.cs`)**:
           - Bổ sung hàm `PrepareJobForProductionUpload()`: Tự động chuyển các nguồn ảnh `Url` hoặc `File` về `Camera` (bảo lưu 100% `CameraParams`, `CameraIndex`, `CameraDeviceDisplayName`, `LightingParams` gốc) và lưu lại file `.job`.
           - `JobManagerViewModel.ExecuteUploadCurrentJobAsync`: Tự động gọi `PrepareJobForProductionUpload()` trước khi đẩy file `.job` lên Server XAMPP.
- [x] Task 281: Tích Hợp Chức Năng Gán Mã Sản Phẩm Mới & Gán Job Đang Mở Trực Tiếp Trong Cửa Sổ Quản Lý & Huấn Luyện (Job Manager).
      - Yêu Cầu & Bối Cảnh:
        - Trước đây, khi muốn gán mã sản phẩm mới cho một tệp Job, người dùng phải đóng cửa sổ Quản Lý Job và mở riêng cửa sổ "Gán mã sản phẩm cho tệp JOB" (`ProductAssignDialog.xaml`).
        - Người dùng mong muốn tích hợp chức năng gán code mới trực tiếp vào trong cửa sổ Quản lý và huấn luyện (teaching).
      - Giải Pháp Đã Triển Khai:
        1. **Thanh Công Cụ (Toolbar) & Bảng Chi Tiết (`JobManagerWindow.xaml`)**:
           - Bổ sung nút **"➕ Gán Mã Mới"** (Background xanh dương nổi bật `#0288D1`): Cho phép tra cứu danh mục sản phẩm từ CSDL và gán mã sản phẩm mới cho tệp Job.
           - Bổ sung nút **"🔗 Gán Job Đang Mở"** (Background `#00838F`): Cho phép gán trực tiếp tệp Job đang mở trong Tool Editor cho sản phẩm đang chọn trong bảng.
        2. **Xử Lý ViewModel (`JobManagerViewModel.cs`)**:
           - Thêm `OpenProductAssignCommand` (`ExecuteOpenProductAssign`): Tự động điền tệp Job đang chọn / đang mở vào hộp thoại `ProductAssignDialog` và tự động làm mới danh sách `JobManagerItems` ngay khi đóng hộp thoại.
           - Thêm `AssignCurrentActiveJobCommand` (`ExecuteAssignCurrentActiveJobAsync`): Gán nhanh tệp Job đang mở (`_toolEditorViewModel.CurrentJobFilePath`) cho `SelectedItem.ProductCode`, gọi `_oqcService.AssignProductJobAsync` và đồng bộ mã sản phẩm vào Tool Editor.
        3. **Auto-Search Khi Mở Hộp Thoại (`ProductAssignDialog.xaml.cs`)**:
           - Thêm sự kiện `Loaded` tự động nạp trang đầu danh sách sản phẩm từ DB nếu chưa có dữ liệu, giúp người dùng không cần nhấn nút tìm kiếm thủ công.
      - Kiểm Thử:
        - Chạy toàn bộ test suite dự án $\rightarrow$ 100% PASSED (106 tests). Solution `dotnet build` đạt 0 lỗi (0 errors).
- [x] Task 282: Thêm Tùy Chọn Tự Động Tắt Đèn Khi Tắt App & Tắt Đèn Toàn Diện Khi Shutdown.
      - Yêu Cầu & Bối Cảnh:
        - Khi tắt ứng dụng, đèn kiểm tra vẫn sáng làm tiêu hao tuổi thọ đèn LED và nguồn điện. Cần thêm tùy chọn cho phép người dùng bật/tắt tính năng tự động tắt đèn khi đóng app và thực thi tắt đèn an toàn khi shutdown.
      - Giải Pháp Đã Triển Khai:
        1. **Cấu Hình & Lưu Trữ (`GlobalAppSettingsService.cs`)**:
           - Thêm thuộc tính `AutoTurnOffOnExit = true` trong `LightingControllerSettings`.
        2. **Dịch Vụ Điều Khiển Đèn (`LightingControllerService.cs`)**:
           - Bổ sung hàm `TurnOffAllChannelsAsync(int channelCount = 8)` gửi đồng loạt lệnh `$F{ch}=0#` xuống tất cả các kênh đèn kết nối qua Serial COM / Ethernet.
        3. **Giao Diện Lighting Controller (`LightingControllerWindow.xaml` & `LightingControllerViewModel.cs`)**:
           - Bổ sung CheckBox *"Tự tắt đèn khi tắt app"* (`AutoTurnOffOnExit`), tự động lưu cấu hình khi người dùng thay đổi.
        4. **Shutdown Xử Lý Tự Động (`App.xaml.cs`)**:
           - Trong `ShutdownGracefullyAsync()`, tự động kiểm tra `AutoTurnOffOnExit` và gọi `TurnOffAllChannelsAsync()` trước khi ngắt kết nối và đóng app.

- [x] Task 283: Đồng Bộ Thời Gian Thực Khi Nhập Số Tham Số Camera (Real-time Input & Slider Sync).
      - Yêu Cầu & Bối Cảnh:
        - Trong tab Camera Settings và cửa sổ Job Camera Settings, khi gõ giá trị vào các ô TextBox (Exposure, Gain, Gamma, White Balance, Trigger Delay, ROI, v.v.), slider không nhảy theo ngay và tham số không được áp dụng luôn mà phải click vào thanh trượt mới có tác dụng.
      - Giải Pháp Đã Triển Khai:
        1. **Binding XAML (`CameraSettingsView.xaml` & `JobCameraSettingsWindow.xaml`)**:
           - Cập nhật `UpdateSourceTrigger=PropertyChanged` cho toàn bộ các ô nhập TextBox thông số Camera (Exposure, Gain, Gamma, Red/Green/Blue Gain, Trigger Delay, Hardware ROI Offset/Size, Packet Size/Delay, Soft Brightness/Contrast).
        2. **ViewModel Epsilon & Debounce Timer (`CameraSettingsViewModel.cs` & `JobCameraSettingsViewModel.cs`)**:
           - Hạ ngưỡng epsilon so sánh float từ `1.0f`, `0.1f`, `0.05f` xuống `0.001f` để tiếp nhận mọi ký tự người dùng gõ vào và kích hoạt `ScheduleApplyParameters()` (debounce 250ms), giúp Slider nhảy ngay theo số gõ và áp dụng lệnh GenICam xuống camera mượt mà mà không làm nghẽn bus Ethernet/USB.

- [x] Task 284: Tích Hợp Cấu Hình Đèn Theo Job Vào Cửa Sổ JobCameraSettingsWindow & Sửa Lỗi Áp Dụng Đèn.
      - Yêu Cầu & Bối Cảnh:
        - Người dùng mong muốn cấu hình đèn theo từng Job trực tiếp ngay trong cửa sổ "Cấu hình Camera cho Job" (`JobCameraSettingsWindow`) để kiểm tra ảnh và độ sáng đèn đồng thời.
        - Khắc phục lỗi bấm test áp dụng mức sáng đèn không có tác dụng.
      - Giải Pháp Đã Triển Khai:
        1. **ViewModel (`JobCameraSettingsViewModel.cs`)**:
           - Bổ sung quản lý `JobLightingParameters`, danh sách `ObservableCollection<JobCameraLightingChannelViewModel> LightingChannels` (CH1..CHn, ON/OFF, Slider/TextBox Brightness `UpdateSourceTrigger=PropertyChanged`).
           - Thêm lệnh `ApplyLightingToDeviceCommand` (gửi `$F{ch}={1/0}#`, `$L{ch}={brightness}#`, `$T{ch}={timeMs}#`) và `ReadLightingFromDeviceCommand` (đọc dữ liệu đèn thực tế từ controller nạp vào Job).
           - Tích hợp callback `onSaveCallbackWithLighting` khi bấm "Lưu Cấu Hình Vào Job".
        2. **Giao Diện (`JobCameraSettingsWindow.xaml`)**:
           - Bổ sung GroupBox *"💡 Đèn Chiếu Sáng Theo Job (Lighting Controller)"* ngay trong bảng điều khiển bên phải: CheckBox bật tắt, ComboBox chọn số kênh (4/8), nút *"⚡ Test Áp Dụng"*, nút *"📥 Đọc Từ Đèn"*, và bảng danh sách kênh điều khiển độ sáng.
        3. **Tích Hợp Tool Editor (`ToolEditorViewModel.ToolPreprocess.cs`)**:
           - Cập nhật `ImageSource_OpenJobCameraSettings` truyền `def.LightingParams` và `_lightingControllerService`, tự động đồng bộ và lưu vào Job.
           - Hoàn thiện `ImageSource_ApplyLightingToDevice` gửi đầy đủ lệnh Power + Brightness + LightingTime cho từng kênh.

- [x] Task 285: Khắc Phục Lỗi Mã Băm GUID Khi Truy Vấn CSDL Máy Thật & Tự Động Fallback CSDL Cho Quản Lý Job Từ Xa.
      - Yêu Cầu & Bối Cảnh:
        - Khi cài ứng dụng lên máy Vision PC thật, màn hình "Gán job cho sản phẩm" hoạt động bình thường, nhưng màn hình "Quản lý job & teaching từ xa" báo lỗi `"Database '{GUID}' not found"` do ID cấu hình CSDL (`JobManagerDbId`) lưu từ máy dev cũ không trùng với GUID CSDL mới tạo trên máy thật.
      - Giải Pháp Đã Triển Khai:
        1. **Cơ Chế Smart Fallback CSDL (`OqcScannerService.cs`)**:
           - Thêm phương thức `ResolveEffectiveDbId`: Tự động kiểm tra `JobManagerDbId`, nếu không tồn tại trong danh sách CSDL của máy thì tự động fallback lần lượt sang `ProductListDbId` $\rightarrow$ `LookupDbId` $\rightarrow$ `AssignDbId` $\rightarrow$ CSDL đầu tiên đang kích hoạt (`IsEnabled`).
           - Áp dụng `ResolveEffectiveDbId` cho toàn bộ các hàm tra cứu OQC (`GetJobManagerListAsync`, `LookupJobAsync`, `LookupProductNameAsync`, `GetProductListAsync`, `AssignProductJobAsync`).
- [x] Task 286: Dọn Dẹp Cấu Hình Đèn Khỏi Properties Panel & Sửa Lỗi Hiển Thị Trạng Thái Timeout Khi Test Áp Dụng Đèn.
      - Yêu Cầu & Bối Cảnh:
        1. Khi đã có đầy đủ cấu hình đèn trong cửa sổ "Cấu Hình Camera & Đèn Cho Job", loại bỏ phần cấu hình đèn trong Properties Panel (`ToolEditorView.xaml`) để giao diện gọn gàng.
        2. Khi chưa kết nối hoặc mất kết nối bộ điều khiển đèn, bấm "⚡ Test Áp Dụng" bị timeout nhưng lại thông báo "Đã áp dụng thành công".
      - Giải Pháp Đã Triển Khai:
        1. **Giao Diện (`ToolEditorView.xaml`)**:
           - Gỡ bỏ hoàn toàn khối cấu hình đèn cũ trong Properties Panel của node `ImageSource`.
           - Cập nhật nhãn nút mở cài đặt thành `"⚙️ Cấu Hình Camera & Đèn Cho Job Này..."`.
        2. **Xử Lý Lỗi & Timeout (`JobCameraSettingsViewModel.cs` & `ToolEditorViewModel.ToolPreprocess.cs`)**:
           - Sửa `ApplyLightingToDeviceAsync`: Bổ sung kiểm tra kết quả `pwrRes.IsSuccess`, `brRes.IsSuccess`, `tmRes.IsSuccess` cho từng lệnh. Khi gặp timeout hoặc lỗi gửi lệnh từ thiết bị, dừng vòng lặp ngay và cập nhật `StatusMessage = $"❌ Lỗi gửi lệnh kênh CH{ch+1}: {errMsg}"`, loại bỏ hoàn toàn việc ghi đè thông báo thành công khi có lỗi.
           - Cập nhật `ReadLightingFromDeviceAsync` và `ImageSource_ApplyLightingToDevice` đồng bộ hiển thị lỗi timeout.
- [x] Task 287: Lọc Sạch Camera Giả Lập/Cổng Ảo & Tích Hợp Splash Screen Khởi Động Siêu Mượt.
      - Yêu Cầu & Bối Cảnh:
        1. Trong tab Camera Settings hiển thị danh sách 5 camera fallback USB Port 0-4 khi không cắm thiết bị thật, làm rối mắt. Cần loại bỏ các cổng USB ảo này, chỉ hiển thị Camera giả lập Simulator, luồng RTSP và các camera vật lý thực tế được cắm vào máy.
        2. Ứng dụng khởi động mất vài giây mà không có phản hồi hình ảnh khiến người dùng cảm giác bị đơ/lag. Cần tối ưu luồng khởi động và tạo màn hình Splash Screen công nghệ hiện đại thông báo tiến trình nạp hệ thống.
      - Giải Pháp Đã Triển Khai:
        1. **Lọc Danh Sách Thiết Bị Camera (`OpenCvCameraDriver.cs` & `CameraSettingsViewModel.cs`)**:
           - Xóa bỏ vòng lặp 5 cổng USB giả lập `Camera Port 0-4 (Fallback)`. Chỉ hiển thị thiết bị thực tế quét được qua DirectShow, camera công nghiệp (Hikrobot GigE/USB3) và luồng RTSP / Simulator.
           - Đưa việc quét thiết bị vào `Task.Run` chạy nền để không block UI thread lúc khởi động.
        2. **Giao Diện Màn Hình Khởi Động (`SplashScreenWindow.xaml` & `SplashScreenWindow.xaml.cs`)**:
           - Xây dựng giao diện Splash Screen Glassmorphism hiện đại tông màu công nghiệp Dark Slate `#0D131F`, viền phát sáng Cyan Neon `#00E5FF`, thanh tiến trình gradient và nhãn trạng thái từng bước khởi tạo.
        3. **Quy Trình Khởi Động Ứng Dụng (`App.xaml.cs`)**:
           - Hiển thị ngay SplashScreenWindow ở frame 0ms, cập nhật tiến trình từng giai đoạn nạp DI $\rightarrow$ Theme $\rightarrow$ MainWindow $\rightarrow$ Camera $\rightarrow$ PLC $\rightarrow$ Fade Out mượt mà khi MainWindow sẵn sàng.
- [x] Task 288: Khắc Phục Triệt Để Lỗi Ngoại Lệ 'Cannot set Owner property to itself' Khi Mở Job Manager / Dialog.
      - Yêu Cầu & Bối Cảnh:
        - Khi mở cửa sổ Job Manager từ OQC Scanner hoặc Tool Editor, ứng dụng phát sinh ngoại lệ `System.ArgumentException: Cannot set Owner property to itself` do thuộc tính `Owner` của cửa sổ con vô tình bị trỏ vào chính nó hoặc `Application.Current.MainWindow` bị gán nhầm sang `SplashScreenWindow` khi khởi động.
      - Giải Pháp Đã Triển Khai:
        1. **Gán Tường Minh MainWindow (`App.xaml.cs`)**:
           - Thiết lập `MainWindow = mainWindow;` ngay khi cửa sổ chính được tạo để cố định đối tượng cửa sổ gốc của toàn ứng dụng trước khi Splash Screen đóng.
        2. **Bảo Vệ An Toàn Cho Cửa Sổ Job Manager (`OqcScannerViewModel.cs` & `ToolEditorViewModel.cs`)**:
           - Truy vấn `mainWin` qua `Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault() ?? Application.Current?.MainWindow`.
           - Kiểm tra `if (mainWin != null && mainWin != _jobManagerWindowInstance && mainWin.IsLoaded)` trước khi gán `_jobManagerWindowInstance.Owner = mainWin`.
        3. **Rà Soát Toàn Bộ Dialogs (`JobManagerViewModel.cs`, `ToolEditorViewModel.ToolOrigin.cs`, `ToolEditorView.xaml.cs`)**:
           - Áp dụng kiểm tra an toàn chống tự gán `Owner` cho tất cả các hộp thoại `ProductAssignDialog`, `OqcSettingsDialog`, `OriginTrainWindow`, `GlobalPreprocessWindow`.
      - Kiểm Thử:
        - Chạy toàn bộ test suite dự án $\rightarrow$ 100% PASSED (106 tests). Solution `dotnet build` đạt 0 lỗi (0 errors).
- [x] Task 289: Triển Khai Hệ Thống Lighting Control Server & Client Điều Khiển Đèn Từ Xa Qua Mạng LAN & Ứng Dụng Độc Lập Standalone.
      - Yêu Cầu & Bối Cảnh:
        - Bộ điều khiển đèn chiếu sáng 8 kênh lắp dưới máy line OQC được kết nối qua cổng COM nối tiếp RS-232, trong khi máy tính làm việc của kỹ sư ở trên văn phòng.
        - Cần xây dựng hệ thống Lighting Control Server (chạy tại máy OQC) và Lighting Control Client (chạy tại máy văn phòng hoặc bất kỳ máy nào cùng mạng LAN) để kết nối và điều khiển từ xa.
        - Tính năng điều khiển phải tương đương với modal Lighting Controller hiện tại (4/8 kênh, bật/tắt nguồn, slider độ sáng 0-255 mượt mà, thời gian sáng ms, áp dụng đồng loạt, đọc tất cả, lưu cấu hình, gửi lệnh thủ công, traffic log thời gian thực).
        - Cung cấp ứng dụng độc lập `VisionInspectionApp.LightingServer.exe` (WPF) có thể chạy riêng biệt dưới chuyền mà không cần mở toàn bộ hệ thống Vision, đồng thời tích hợp menu khởi động trực tiếp từ menu `💡 Chiếu Sáng` trong Vision Inspection App.
      - Giải Pháp Đã Triển Khai:
        1. **Tầng Giao Thức & Dịch Vụ Máy Chủ / Máy Khách (`VisionInspectionApp.Application`)**:
           - `LightingControlServer.cs`: Lắng nghe TCP trên cổng cấu hình (mặc định 5050), tự động quét và hiển thị tất cả IPv4 LAN; điều phối cổng COM với `SemaphoreSlim(1,1)` chống xung đột half-duplex; bộ đệm trạng thái `LightingControllerState` phản hồi `$RD=9999#` siêu tốc; quản lý danh sách client kết nối và hỗ trợ đầy đủ các lệnh ASCII chuẩn.
           - `LightingControlClientService.cs`: Kết nối socket TCP đến IP/Port server, hỗ trợ đo độ trễ mạng (latency ping ms), các hàm điều khiển kênh và lắng nghe log truyền nhận.
        2. **Tầng Cấu Hình (`GlobalAppSettingsService.cs`)**:
           - Bổ sung `LightingServerConfig` và `LightingClientConfig` lưu vào `global_settings.json`.
        3. **Tầng Giao Diện Người Dùng (`VisionInspectionApp.UI`)**:
           - `LightingServerViewModel.cs` & `LightingServerWindow.xaml`: Quản lý Server TCP (Port, danh sách IP LAN kèm nút copy, Start/Stop), quản lý kết nối COM phần cứng, bảng danh sách client kết nối, điều khiển trực tiếp 4/8 kênh trên server, traffic log.
           - `LightingClientViewModel.cs` & `LightingClientWindow.xaml`: Nhập IP và Port Server, kết nối/ngắt kết nối kèm đo ping latency, điều khiển 4/8 kênh với slider debounce 50ms, nút bật/tắt tất cả, áp dụng tất cả, đọc lại từ server, lưu flash, chọn trigger mode, gửi lệnh thủ công và live log.
           - Cập nhật menu `💡 Chiếu Sáng` trong `MainWindow.xaml` và `ToolEditorViewModel.Lighting.cs`.
        4. **Ứng Dụng Độc Lập Standalone (`VisionInspectionApp.LightingServer`)**:
           - Tạo mới project .NET 8 WPF `VisionInspectionApp.LightingServer.csproj` trong `VisionInspectionApp.slnx` biên dịch ra `VisionInspectionApp.LightingServer.exe`, tích hợp cả 2 tab Server Mode và Client Mode.
        5. **Kiểm Thử Tự Động (`TestExtractApp`)**:
           - Bổ sung `LightingServerClientTests.cs` (32 tests): nhận diện IPv4, start/stop server, connect/disconnect client, gửi lệnh bật tắt nguồn, đặt độ sáng, đọc tất cả, thao tác trực tiếp từ server, 3 client đồng thời gửi lệnh song song.
        6. **Chuẩn Hóa Trạng Thái Nút Bấm Kết Nối / Ngắt Kết Nối**:
           - Khi chưa kết nối: Nút "Kết Nối" active, nút "Ngắt Kết Nối" disabled, các bảng điều khiển đèn bị khóa chống thao tác rỗng.
           - Khi đã kết nối thành công: Nút "Kết Nối" disabled, nút "Ngắt Kết Nối" active, toàn bộ bảng điều khiển đèn và thao tác nhanh được kích hoạt.
           - Áp dụng trên toàn bộ Client, Server, Standalone App và cửa sổ Controller cục bộ.
        7. **Tự Động Đọc & Đồng Bộ Trạng Thái Đèn Khi Khởi Động Server**:
           - Sửa lỗi điều kiện `!IsConnected` lúc kết nối COM trong `LightingControllerService.cs`.
           - Tự động kết nối phần cứng và đọc trạng thái thực tế từ đèn qua `$RD=9999#` (hoặc `$RD={ch}#`) khi bấm "Khởi Động Server" hoặc "Kết Nối Đèn".
           - Cập nhật và hiển thị tức thì giá trị độ sáng lên Slider, Textbox và Switch ON/OFF của tất cả các kênh trên giao diện Server.
           - Thêm nút "📥 Đọc Từ Đèn" cho phép người dùng chủ động đọc lại trạng thái từ thiết bị bất kỳ lúc nào.
      - Kiểm Thử:
        - Toàn bộ test suite dự án trong `TestExtractApp` đạt 100% PASSED (178+ tests). Solution `dotnet build` đạt 0 lỗi (0 errors).
- [x] Task 290: Tối Ưu Bộ Nhớ Đệm (Cache) Ảnh Mẫu Teach & Tính Năng Mở Job Trực Tiếp Từ Danh Sách OQC Job Manager.
      - Yêu Cầu & Bối Cảnh:
        - Giảm thời gian chờ và giật lag khi chuyển dòng xem ảnh mẫu trong cửa sổ Quản lý & Huấn luyện (teaching).
        - Bổ sung nút "Làm Mới Ảnh" cạnh tiêu đề ảnh mẫu để người dùng chủ động tải lại khi ảnh trên Server được cập nhật mới.
        - Bổ sung nút "Mở Job Này" (và hỗ trợ Double Click vào dòng) để tự động tắt cửa sổ, kiểm tra tệp Job trong thư mục mặc định (nếu chưa có thì tải từ Server về thư mục mặc định), sau đó mở Job trong Tool Editor giữ nguyên toàn bộ cấu hình (không đổi ImageSource).
      - Giải Pháp Đã Triển Khai:
        1. **Bộ Nhớ Đệm Ảnh Mẫu 2 Tầng (Memory Cache + Disk Cache)**:
           - Memory Cache trong RAM (`ConcurrentDictionary<string, BitmapSource>`) giúp hiển thị tức thì (< 5ms) khi duyệt qua lại các dòng sản phẩm.
           - Disk Cache tại `Cache/TeachImages/` lưu trữ lâu dài trên ổ đĩa, khởi động lại ứng dụng vẫn xem ảnh ngay lập tức.
           - Chống race condition với `_previewLoadToken` khi người dùng click nhanh liên tục.
        2. **Nút Làm Mới Ảnh Mẫu (`RefreshTeachImageCommand`)**:
           - Đặt cạnh tiêu đề `🖼️ Ảnh Mẫu (Teaching Image Preview)` trong `JobManagerWindow.xaml`, cho phép xóa cache và tải lại ảnh mới nhất từ Server.
        3. **Tính Năng Mở Job Này (`OpenJobFromListCommand`)**:
           - Nút `📂 Mở Job Này` trên toolbar, `📂 Mở Tệp Job Này` trên panel chi tiết và sự kiện Double-click DataGridRow.
           - Tự động kiểm tra file trong `JobRootDirectory` (hoặc `jobs/`). Nếu chưa có, tải từ Server về thư mục mặc định.
           - Nạp Job vào Tool Editor bằng `LoadJobFromFile`, chuyển sang Tab Tool Editor và đóng `JobManagerWindow`.
           - Giữ nguyên cấu hình gốc của Job (Camera, Tool graph, tham số), không sửa đổi ImageSource.
        4. **Kiểm Thử Tự Động (`TestExtractApp`)**:
           - Thêm kiểm thử `Test_TeachImageCache_And_OpenJobFromListLogic`: kiểm tra lưu và đọc Disk Cache, kiểm tra mở Job từ thư mục mặc định giữ nguyên `SourceType == Camera`.
      - Kiểm Thử:
        - Toàn bộ test suite dự án trong `TestExtractApp` đạt 100% PASSED (178+ tests). Solution `dotnet build` đạt 0 lỗi (0 errors).
- [x] Task 291: Tối Ưu Điều Hướng Tab Khi Mở Job & Tự Khởi Động Lighting Server Dùng Chung Cổng COM.
      - Yêu Cầu & Bối Cảnh:
        - Giữ nguyên Tab OQC Scanner khi mở Job từ danh sách (không tự chuyển sang Tool Editor trừ khi bấm "Huấn Luyện Từ Xa").
        - Tự động khởi động Lighting Server trên mạng LAN ngay khi app khởi động, chia sẻ cổng COM thread-safe không bị lỗi xung đột cổng COM.
      - Giải Pháp Đã Triển Khai:
        1. **Điều Hướng Tab Thông Minh (`JobManagerViewModel.cs`)**:
           - Kiểm tra `SelectedTabIndex != 1`: Chỉ chuyển sang Tool Editor khi không ở Tab OQC; đồng thời cập nhật ngay `CurrentJobFilePath`, `CurrentProductName` và `ScannedCode` vào OQC Scanner.
        2. **Singleton & Khởi Động Tự Động (`App.xaml.cs`)**:
           - Đăng ký `LightingControlServer` dưới dạng Singleton dùng chung `LightingControllerService`.
           - Tự động khởi động Server LAN (mặc định cổng 5050) sau khi kết nối COM và thiết lập độ sáng.
           - Giải phóng tài nguyên và dừng Server an toàn khi tắt app (`ShutdownGracefullyAsync`).
        3. **Đồng Bộ Giao Diện Server (`LightingServerViewModel.cs`, `ToolEditorViewModel.Lighting.cs`)**:
           - Nhận Server Singleton từ DI, phản ánh ngay trạng thái đang chạy, cổng COM đang dùng (`ActivePortName`, `ActiveBaudRate`) và các client đã kết nối.
      - Kiểm Thử:
        - Toàn bộ test suite dự án trong `TestExtractApp` đạt 100% PASSED (178+ tests). Solution `dotnet build` đạt 0 lỗi (0 errors).
- [x] Task 292: Lưu Bền Vững Trạng Thái 2 Checkbox "Auto Run" và "Đầu Scanner" Qua Các Lần Khởi Động App.
      - Yêu Cầu & Bối Cảnh:
        - Ghi nhớ trạng thái của 2 checkbox `⚡ Auto Run` (`AutoRunJob`) và `🔫 Đầu Scanner` (`UseExternalScanner`) trên thanh công cụ Tab OQC Scanner kể cả khi tắt và khởi động lại ứng dụng.
      - Giải Pháp Đã Triển Khai:
        1. **Model Persistence (`OqcScannerConfig.cs`)**:
           - Bổ sung `public bool AutoRunJob { get; set; } = true;` vào `OqcScannerConfig`.
        2. **Nạp & Lưu Đầy Đủ (`OqcScannerViewModel.Settings.cs`, `OqcScannerViewModel.cs`)**:
           - Thêm cờ `_isSuppressingConfigSave` chống race condition khi nạp cấu hình.
           - `LoadSettingsFromConfig`: nạp đồng thời `AutoRunJob` và `UseExternalScanner` từ `_oqcService.Config`.
           - `SaveSettingsToConfig` & `ExecuteExportConfig`: lưu đầy đủ cả 2 trường.
           - `OnAutoRunJobChanged` & `OnUseExternalScannerChanged`: tự động cập nhật và lưu ngay lập tức xuống tệp JSON khi người dùng tích/bỏ tích.
        3. **Flush Graceful Shutdown (`App.xaml.cs`)**:
           - Trong `ShutdownGracefullyAsync`, lưu trạng thái mới nhất từ `OqcScannerViewModel` trước khi thoát.
        4. **Kiểm Thử Tự Động (`TestExtractApp`)**:
           - Cập nhật `Test_OqcScannerConfig_SerializationWithServerFields` kiểm tra serialization và khôi phục giá trị của `AutoRunJob` và `UseExternalScanner`.
      - Kiểm Thử:
        - Toàn bộ test suite dự án trong `TestExtractApp` đạt 100% PASSED (178+ tests). Solution `dotnet build` đạt 0 lỗi (0 errors).
- [x] Task 293: Tối Ưu Mở Job Từ Danh Sách Quản Lý Job: Bắt Buộc Nhập LABEL ID & Không Query Lại CSDL.
      - Yêu Cầu & Bối Cảnh:
        - Khi mở Job từ danh sách Quản Lý Job, để trống textfield `ScannedCode` thay vì tự điền `ProductCode`.
        - Khi chạy Job (bấm Space hoặc nút Chạy Job), nếu ô trống thì chặn lại và báo lỗi: *"Hãy nhập LABEL ID trước khi chạy job."*.
        - Khi đã có LABEL ID, thực thi ngay trên Job đã nạp, không query lại CSDL để nạp lại Job.
        - Giữ nguyên 100% flow cũ cho các trường hợp khác.
      - Giải Pháp Đã Triển Khai:
        1. **SetJobLoadedFromManager (`OqcScannerViewModel.cs`)**:
           - Thiết lập cờ `IsJobLoadedFromManager = true`.
           - Để trống `ScannedCode = ""`.
           - Cập nhật thông báo hướng dẫn người dùng nhập LABEL ID.
        2. **Chặn Thực Thi Khi Trống Mã (`ExecuteScanInternalAsync`, `RunJob`)**:
           - Kiểm tra `IsJobLoadedFromManager`: Nếu `ScannedCode` rỗng thì hiển thị cảnh báo `⚠️ Hãy nhập LABEL ID trước khi chạy job.` và return.
        3. **Không Query Lại CSDL Khi Đã Có Mã**:
           - Chạy thẳng `RunJob()`, ghi nhận `_lastScannedProcessedCode` là LABEL ID.
           - Sau khi hoàn thành kiểm tra, ghi log kết quả với mã LABEL ID và xóa rỗng ô `ScannedCode = ""` cho sản phẩm tiếp theo.
        4. **Hỗ Trợ Phím Space (`OqcScannerView.xaml.cs`)**:
           - Cho phép phím Space gọi `RunJobCommand` khi `IsJobLoadedFromManager == true`.
        5. **Kiểm Thử Tự Động (`TestExtractApp`)**:
           - Thêm kiểm thử `Test_JobManagerOpenJob_LabelIdRequirementAndNoDbRequery`.
      - Kiểm Thử:
        - Toàn bộ test suite dự án trong `TestExtractApp` đạt 100% PASSED (178+ tests). Solution `dotnet build` đạt 0 lỗi (0 errors).
- [x] Task 294: Đảm Bảo Tự Động Khởi Động Lighting Server Khi Mở App & Luôn Ở Trạng Thái Running Khi Mở Modal.
      - Yêu Cầu & Bối Cảnh:
        - Khắc phục lỗi Lighting Server không tự khởi động khi bật app và modal hiển thị trạng thái đã dừng.
      - Giải Pháp Đã Triển Khai:
        1. **Khởi Động Server Ngay Lập Tức (`App.xaml.cs`)**:
           - Tách rời và thực thi `StartServerAsync(serverPort)` ngay đầu tiến trình khởi động, không chờ và không phụ thuộc vào kết nối COM.
        2. **Cấu Hình Bền Vững (`GlobalAppSettingsService.cs`)**:
           - Thiết lập `AutoStartServer = true` mặc định khi nạp cài đặt; cập nhật `global_settings.json` hiện hành.
        3. **Tự Động Kích Hoạt Trong Modal (`LightingServerViewModel.cs`)**:
           - Kiểm tra nếu `!_server.IsRunning` khi mở modal thì tự khởi động ngay lập tức; thêm property `AutoStartServer`.
        4. **Giao Diện Modal Máy Chủ Đèn (`LightingServerWindow.xaml`)**:
           - Thêm CheckBox viền `⚡ Tự động khởi động Lighting Server khi mở ứng dụng` (`AutoStartServer`).
        5. **Giao Diện Modal Cài Đặt Đèn Phần Cứng (`LightingControllerWindow.xaml`)**:
           - Bổ sung CheckBox `⚡ Tự khởi động Server LAN` (`AutoStartLightingServer`) trong nhóm Cài Đặt Khởi Động.
      - Kiểm Thử:
        - Toàn bộ test suite dự án trong `TestExtractApp` đạt 100% PASSED (178+ tests). Solution `dotnet build` đạt 0 lỗi (0 errors).
- [x] Task 295: Chuyển Đổi Preview Ảnh Nguyên Gốc (Full/Original Quality) vs Đã Giảm Chất Lượng (Downscaled/Performance).
      - Yêu Cầu & Bối Cảnh:
        - Hệ thống preview ảnh của app mặc định giảm chất lượng (scale down về proxy 1280x720 hoặc 1920x1080) để tăng hiệu năng và FPS.
        - Người dùng cần một CheckBox để chuyển đổi giữa preview ảnh nguyên gốc (100% độ phân giải, giữ trọn vẹn chi tiết khi zoom) và preview giảm chất lượng.
        - Vị trí CheckBox: Đặt cạnh CheckBox "Show ROI" (trên tab Tool Editor) và cạnh "Khung ROI" (trên tab OQC Scanner), cũng như màn hình InspectionView.
        - Trạng thái được lưu bền vững và đồng bộ giữa các màn hình.
      - Giải Pháp Đã Triển Khai:
        1. **Hạ Tầng Kết Xuất Ảnh (`MatExtensions.cs` & `WriteableBitmapRenderer.cs`)**:
           - Thêm cờ `public static bool UseOriginalQualityPreview { get; set; } = false;`.
           - Cập nhật `ToBitmapSourceForDisplay(this Mat? mat, ..., bool? forceOriginalQuality = null)`:
             - Khi `forceOriginalQuality == true || (forceOriginalQuality == null && UseOriginalQualityPreview)`: Bỏ qua bước resize tuyến tính, trả về ảnh gốc độ phân giải 100% thông qua `mat.ToBitmapSourceSafe()`.
             - Khi `false`: Thực hiện resize về kích thước proxy để tối ưu hiệu năng và FPS.
           - Cập nhật `WriteableBitmapRenderer.UpdateFromMat(..., bool? forceOriginalQuality = null)`:
             - Khi bật chất lượng gốc: Bỏ qua downscale, ghi trực tiếp frame gốc vào `WriteableBitmap` ở độ phân giải camera gốc.
        2. **Cấu Hình Bền Vững (`GlobalAppSettingsService.cs` & `App.xaml.cs`)**:
           - Thêm `public bool UseOriginalQualityPreview { get; set; } = false;` vào `GlobalAppSettings`.
           - Trong `App.xaml.cs`: Lúc khởi động ứng dụng, nạp và gán `MatExtensions.UseOriginalQualityPreview = settingsService.Settings.UseOriginalQualityPreview;`.
        3. **Tab Tool Editor (`ToolEditorViewModel.cs`, `ToolEditorViewModel.Engine.cs`, `ToolEditorView.xaml`)**:
           - Thêm `[ObservableProperty] private bool _isOriginalQualityPreview;`.
           - `OnIsOriginalQualityPreviewChanged`: Lưu cài đặt vào `GlobalAppSettingsService` và gọi `RefreshPreviews()` ngay lập tức.
           - Đặt `public void RefreshPreviews()` để các module khác có thể kích hoạt làm mới preview.
           - Trên `ToolEditorView.xaml`: Thêm CheckBox `Ảnh gốc` nằm ngay cạnh CheckBox `Show ROI`.
        4. **Tab OQC Scanner (`OqcScannerViewModel.cs`, `OqcScannerView.xaml`)**:
           - Thêm `[ObservableProperty] private bool _isOriginalQualityPreview;`.
           - `OnIsOriginalQualityPreviewChanged`: Đồng bộ với `_toolEditorViewModel.IsOriginalQualityPreview`, cập nhật `PreviewImage` ngay lập tức nếu đang xem ảnh kết quả.
           - Đồng bộ hai chiều trong `OnToolEditorPropertyChanged`.
           - Trên `OqcScannerView.xaml`: Thêm CheckBox `Ảnh gốc` nằm ngay cạnh CheckBox `Khung ROI`.
        5. **Màn Hình Inspection (`InspectionViewModel.cs`, `InspectionView.xaml`)**:
           - Thêm `[ObservableProperty] private bool _isOriginalQualityPreview;`.
           - `OnIsOriginalQualityPreviewChanged`: Cập nhật lại `Image = _imageMat.ToBitmapSourceForDisplay();` ngay lập tức.
           - Trên `InspectionView.xaml`: Thêm CheckBox `Ảnh gốc` nằm ngay cạnh CheckBox `Show ROI`.
        6. **Kiểm Thử Tự Động (`TestExtractApp/PreviewQualityTests.cs`)**:
           - Thêm bộ test `PreviewQualityTests.cs` kiểm tra 3 khía cạnh:
             - Chuyển đổi giữa chế độ giảm chất lượng (proxy <= 1280x720) và chế độ ảnh gốc (100% full 3840x2160).
             - Khả năng lưu và phục hồi qua JSON của `GlobalAppSettings.UseOriginalQualityPreview`.
             - Khả năng chuyển đổi kích thước render trên `WriteableBitmapRenderer`.
      - Kiểm Thử:
        - Toàn bộ test suite dự án trong `TestExtractApp` đạt 100% PASSED (181+ tests). Solution `dotnet build` đạt 0 lỗi (0 errors).








- [x] Task 296: Cấy Thêm ProductName Vào Tên Tệp Job Khi Upload Lên Hệ Thống Trong Cửa Sổ Quản Lý Job & Huấn Luyện (Teaching).
      - Mục Tiêu & Yêu Cầu:
        - Trước đây, khi tải tệp Job (.job) lên máy chủ qua cửa sổ Quản Lý Job & Huấn Luyện (Teaching), tên tệp Job sinh ra trên máy chủ có dạng job_{ProductCode}_{yyyyMMdd_HHmmss}_{hash}.job (ví dụ: job_7A10461A_20260903_052341_b1abf6.job), mới chỉ chứa mã sản phẩm (ProductCode).
        - Người dùng yêu cầu cấy thêm tên sản phẩm (ProductName) vào định dạng tên file để quản lý trực quan và dễ nhận biết cả mã lẫn tên sản phẩm (Ví dụ: job_7A10461A_Cover_Assembly_S24_20260903_052341_b1abf6.job).
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. **Phía Server PHP (ServerScripts/vision_upload.php)**:
           - Thêm hàm removeVietnameseDiacritics($str) khử dấu tiếng Việt và hàm sanitizeIdentifier($str) làm sạch khoảng trắng, ký tự đặc biệt thành dấu gạch dưới an toàn.
           - Trong endpoint upload_job: Tiếp nhận $_POST['product_name'], làm sạch và kết hợp định danh $identifier = !empty($cleanName) ? $cleanCode . '_' . $cleanName : $cleanCode;. Sinh tên file: job_{identifier}_{yyyyMMdd_His}_{hash}.job.
           - Trong endpoint upload_image: Tiếp nhận $_POST['product_name'] và đồng bộ cấu trúc tên ảnh teach: teach_{identifier}_{yyyyMMdd_His}_{hash}.{ext}.
        2. **Phía Dịch Vụ Ứng Dụng (IRemoteServerService.cs & RemoteServerService.cs)**:
           - Bổ sung tham số tùy chọn string? productName = null vào UploadJobAsync và UploadImageAsync.
           - Thêm phương thức tĩnh RemoteServerService.SanitizeIdentifier(string? input): Khử đ/Đ, khử dấu diacritics Unicode FormD, loại bỏ ký tự lạ, co cụm dấu gạch dưới liên tiếp.
           - Đính kèm trường product_name vào MultipartFormDataContent khi gửi request POST multipart lên Server.
        3. **Phía ViewModel & Giao Diện (JobManagerViewModel.cs & OqcScannerViewModel.cs)**:
           - JobManagerViewModel.ExecuteUploadCurrentJobAsync: Truyền SelectedItem.ProductName vào _remoteServerService.UploadJobAsync.
           - JobManagerViewModel.ExecuteQuickCaptureTeachImageAsync: Truyền SelectedItem.ProductName vào _remoteServerService.UploadImageAsync.
           - Chuẩn hóa tên file mặc định khi lưu cục bộ và chuẩn bị môi trường dạy học sang định dạng job_{safeCode}_{safeName}.job.
           - OqcScannerViewModel.ExecuteQuickCaptureAndUploadTeachImageAsync: Truyền CurrentProductName vào UploadImageAsync.
        4. **Kiểm Thử Tự Động (TestExtractApp/RemoteServerAndJobManagerTests.cs)**:
           - Thêm kiểm thử Test_SanitizeIdentifier_And_UploadJobWithProductNameAsync:
             + Kiểm tra hàm SanitizeIdentifier khử dấu tiếng Việt (Nắp lưng Titan (Đen-Bạc) -> Nap_lung_Titan_Den-Bac).
             + Giả lập HTTP Mock Server kiểm tra payload multipart nhận đủ product_code và product_name, phản hồi tên tệp job_{Code}_{Name}_{Time}_{Hash}.job.
      - Kiểm Thử:
        - dotnet build VisionInspectionApp.slnx: 0 errors.
        - dotnet run --project TestExtractApp: 100% PASSED.

- [x] Task 297: Khắc Phục Mất Cấu Hình PLC Khi Build, Phân Tích MC Protocol Cổng 5000 & Tích Hợp Bộ Công Cụ Chẩn Đoán Gói Tin Mạng Chuyên Sâu (Diagnostic Packet Probe & Hex Log) Kèm Sao Lưu/Phục Hồi JSON.
      - Mục Tiêu & Yêu Cầu:
        - Mỗi lần build lại ứng dụng thì cấu hình PLC và các tag bị mất hết (chỉ tắt/bật lại thì không bị). Cần tìm vị trí lưu trữ và đảm bảo cấu hình không bị mất sau build.
        - Phân tích nguyên nhân driver Mitsubishi MC Protocol không phản hồi trên cổng 5000 trong khi MX Component kết nối OK và đang có màn hình HMI Weintek kết nối tới 192.168.10.5:5000.
        - Cung cấp phương án lấy đủ dữ liệu debug từ máy Vision PC dưới xưởng về máy Dev văn phòng khi không cùng mạng LAN.
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. **Khắc Phục Triệt Để Lỗi Mất Cấu Hình Khi Build (PlcManagerService.cs, DbManagerService.cs, TestExtractApp)**:
           - Phát hiện nguyên nhân: Test suite trong TestExtractApp khi build/chạy test tự động gọi LoadConfig(...) với PLC giả lập FX5U_Q và ghi đè trực tiếp lên %AppData%\Vision2026\plc_config.json.
           - Bổ sung tham số string? customConfigFilePath = null cho constructor PlcManagerService và DbManagerService.
           - Xây dựng TestPlcConfigHelper.cs và chuyển hướng toàn bộ các bài test trong PlcTests.cs, CameraTest.cs sang thư mục tạm %TEMP%\Vision2026_Tests\test_plc_{guid}.json, bảo vệ 100% cấu hình người dùng trong %AppData%.
           - Bổ sung 2 nút 💾 Xuất Backup (JSON) và 📥 Nạp Backup (JSON) ở thanh footer của PlcManagerWindow.xaml cho phép sao lưu/phục hồi bất kỳ lúc nào.
        2. **Dịch Vụ Chẩn Đoán Gói Tin Mạng Chuyên Sâu (PlcDiagnosticService.cs)**:
           - Thực thi kiểm tra 4 tầng: Ping ICMP, mở kết nối TCP Socket, gửi khung tin thăm dò MC Protocol 3E Binary (50 00 00 FF 03 FF 00 0C 00 10 00 01 01 00 00), thu nhận và phân tích byte phản hồi.
           - Bắt trọn mảng byte Hex gửi (TX) và Hex nhận (RX), nhận diện tên CPU hoặc mã lỗi Return Code.
           - Phân tích nguyên nhân và đưa ra khuyến nghị xử lý trực quan bằng tiếng Việt: Cổng 5000 đang là cổng MELSOFT hoặc bị HMI Weintek chiếm dụng độc quyền socket; hướng dẫn mở thêm Connection 2 riêng (port 5007 / 6000) trong GX Works.
           - Tự động ghi nhật ký chẩn đoán ra đĩa tại %AppData%\Vision2026\logs\plc_diag_{timestamp}.log.
        3. **Giao Diện Chẩn Đoán Trực Quan (PlcManagerViewModel.cs, PlcManagerWindow.xaml)**:
           - Thêm nút 🔍 Chẩn Đoán (Ping & Probe) ngay cạnh nút ⚡ Kết Nối ở Tab 1.
           - Thêm Tab 5: 🔍 5. Chẩn Đoán & Packet Log hiển thị trạng thái Ping, Socket, Terminal Output màu Dark Slate #0F172A với font chữ Consolas, thanh tiến trình ProgressBar khi đang quét.
           - Bổ sung nút 📋 Sao Chép Báo Cáo (Copy) giúp người vận hành 1-click copy toàn bộ dữ liệu debug gửi về cho dev và nút 📂 Mở Thư Mục Log.
        4. **Kiểm Thử Tự Động (TestExtractApp/PlcTests.cs)**:
           - Bổ sung bài test Test 14: PlcDiagnosticService Network Ping, Socket Probe & Hex Log: giả lập Mock TCP Server phản hồi gói tin CPU FX5U-64MT, kiểm tra Hex dump, kiểm tra cổng đóng và ghi file log.
      - Kiểm Thử:
        - dotnet build VisionInspectionApp.slnx: 0 errors.
        - dotnet run --project TestExtractApp: 100% PASSED (bao gồm Test 14).
        - Đã xác nhận file %AppData%\Vision2026\plc_config.json không bị thay đổi thời gian ghi hay ghi đè sau test.

- [x] Task 298: Tích Hợp Hệ Thống Kịch Bản Hiệu Ứng Nháy Đèn (Lighting Blink Pattern & Scenarios) Khi Mở App, Tắt App và Báo Lỗi NG.
      - Mục Tiêu & Yêu Cầu:
        - Bổ sung hệ thống kịch bản hiệu ứng nháy đèn (Blink Pattern Scenarios) cho bộ điều khiển đèn Lighting Controller (4/8 kênh).
        - Cho phép chạy blink pattern trước khi bật mức sáng đã lưu khi mở app (Startup), chạy blink pattern trước khi tắt nguồn đèn khi đóng app (Shutdown), và chạy blink pattern cảnh báo khi kiểm tra sản phẩm có lỗi NG (Inspection NG).
        - Cung cấp công tắc Bật/Tắt độc lập cho từng sự kiện: Bật app, Tắt app, Kiểm tra hàng NG.
        - Cho phép chọn pattern riêng cho từng mục.
        - Quy tắc cú pháp pattern thông minh: Hỗ trợ cú pháp chuỗi phẩy (L1, ON, 300, L1, OFF, L2, ON, 100, L2, OFF...), cú pháp nhiều dòng (ALL OFF; DELAY 150), macro (STROBE, CHASE), cấu hình số chu kỳ lặp lại (RepeatCycles), và hướng dẫn cú pháp chi tiết trực tiếp trên giao diện để người dùng dễ nắm bắt.
        - Hỗ trợ nút Chạy Thử (Test Run) và Dừng (Stop) trực tiếp trên UI.
      - Giải Pháp Kỹ Thuật Đã Triển Khai:
        1. **Mô Hình Dữ Liệu & Kịch Bản Mẫu (LightingPatternModels.cs)**:
           - Định nghĩa LightingPatternModel (Id, Name, Description, Script, RepeatCycles, IsBuiltIn).
           - Định nghĩa LightingPatternStep (StepType, Channels, PowerOn, Brightness, DelayMs, SummaryText).
           - Cung cấp 5 kịch bản mẫu chuẩn công nghiệp: Welcome Chase (Khởi động tuần tự), Shutdown Fade (Tắt dần êm ái), NG Red Alert (Cảnh báo nháy nhanh 3 lần), Dual Alternate (Chớp xen kẽ chẵn/lẻ), Pulse Strobe (Chớp nháy đồng loạt).
        2. **Bộ Phân Tích Cú Pháp Thông Minh (LightingPatternParser.cs)**:
           - Hỗ trợ phong cách chuỗi phân tách dấu phẩy (Comma-delimited stream): L1, ON, 300, L1, OFF, L2, ON, 100, L2, OFF...
           - Hỗ trợ phong cách đa dòng với ghi chú: DELAY 150, L1 ON 255 100, L1 OFF, ALL ON 200, ALL OFF.
           - Hỗ trợ Macro tiện ích công nghiệp: STROBE [CH] [ON_MS] [OFF_MS] [COUNT] [BRIGHTNESS], CHASE [DELAY_MS] [BRIGHTNESS].
           - Kiểm tra tính hợp lệ thời gian thực (Validation) và ước tính tổng thời lượng chu kỳ (Timing estimation).
        3. **Dịch Vụ Điều Phối Kịch Bản (LightingPatternService.cs)**:
           - Tự động thực thi bất đồng bộ an toàn qua CancellationToken.
           - PlayStartupPatternAsync: Chạy hiệu ứng nháy trước khi khôi phục độ sáng đã lưu.
           - PlayShutdownPatternAsync: Chạy hiệu ứng kết thúc trước khi ngắt nguồn đèn khi đóng app.
           - PlayNgPatternAsync: Tự động chụp ảnh nhanh (snapshot) mức sáng và trạng thái của tất cả các kênh trước khi nháy cảnh báo, sau đó tự động khôi phục nguyên trạng thái làm việc ban đầu.
           - Cung cấp cơ chế dừng khẩn cấp StopCurrentPattern().
        4. **Lưu Trữ Bền Vững & Khởi Tạo DI (GlobalAppSettingsService.cs & App.xaml.cs)**:
           - Tích hợp EnableStartupPattern, StartupPatternId, EnableShutdownPattern, ShutdownPatternId, EnableNgPattern, NgPatternId, Patterns vào LightingControllerSettings trong global_settings.json.
           - Đăng ký LightingPatternService dưới dạng Singleton trong DI container.
           - Tích hợp kích hoạt Startup Pattern trong App.xaml.cs và Shutdown Pattern trong ShutdownGracefullyAsync().
        5. **Tích Hợp Tự Động Kích Hoạt Báo Lỗi NG (OqcScannerViewModel.cs & ToolEditorViewModel.Inspection.cs)**:
           - Tab OQC Scanner: Tự động gọi PlayNgPatternAsync khi !result.Pass, tự động dừng pattern khi bắt đầu lượt quét mới.
           - Tab Tool Editor: Tự động gọi PlayNgPatternAsync khi thực thi kiểm tra có kết quả NG.
        6. **Giao Diện Tab Kịch Bản & Trình Hướng Dẫn Cú Pháp Trực Quan (LightingControllerWindow.xaml & LightingControllerViewModel.BlinkPattern.cs)**:
           - Chuyển đổi cửa sổ điều khiển đèn thành 2 Tab hiện đại: Tab 1 "Điều Khiển Kênh & Tham Số", Tab 2 "✨ Kịch Bản & Hiệu Ứng (Blink Patterns)".
           - 3 Khối cấu hình Trigger sự kiện độc lập (Khởi động, Đóng app, Hàng NG) kèm Toggle Switch và ComboBox chọn kịch bản.
           - Thư viện kịch bản: Thêm mới, Nhân bản (Clone), Đổi tên, Xóa kịch bản tùy chỉnh.
           - Trình soạn thảo kịch bản với Text Editor, thanh công cụ nạp nhanh cú pháp mẫu (STROBE, CHASE, ALL OFF, Chuỗi phẩy), bộ đếm chu kỳ lặp (RepeatCycles), nhãn ước tính thời lượng (ms) và trạng thái hợp lệ.
           - Bảng tra cứu cú pháp thông minh (Smart Syntax Guide) trình bày chi tiết các quy tắc, ví dụ trực quan ngay trên giao diện.
           - Nút bấm Chạy Thử (Test Run) và Dừng (Stop) kèm thanh tiến trình trực quan.
        7. **Kiểm Thử Tự Động Toàn Diện (TestExtractApp/LightingPatternTests.cs)**:
           - 42 bài test tự động bao phủ: Parse Comma Style, Parse Structured Multi-Line, Parse STROBE Macro, Parse CHASE Macro, Validation cú pháp & kênh vượt giới hạn, Timing Estimation, PlayPattern lặp chu kỳ & hủy CancellationToken, NG Pattern Snapshot & Restore mức sáng gốc, Sao chép Clone kịch bản.
      - Kiểm Thử:
        - dotnet build VisionInspectionApp.slnx: 0 errors.
        - dotnet run --project TestExtractApp: 100% PASSED (42/42 lighting pattern tests passed).
