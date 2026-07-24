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









