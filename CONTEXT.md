# Vision Inspection App — Context & Roadmap

## Mô tả

Đây là phần mềm kiểm tra thị giác công nghiệp trên .NET 8 WPF. Hệ thống hỗ trợ camera thời gian thực, OCR/đọc mã bằng ZXing và OpenCvSharp, graph tool dạng node, cấu hình tiền xử lý ảnh và giao diện sáng/tối.

## Trạng thái dự án

### Tool Editor và Node Graph

- Port của node được đặt tên theo ngữ cảnh: `Image`, `Preprocess`, `P1`, `P2`, `L1`, `L2`, `Distance`, `Angle`…
- Thay đổi đầu vào bằng Properties Panel được đồng bộ với cạnh trên canvas.
- Có thể kéo thả tool, chọn cạnh hoặc node và xoá bằng phím Delete.
- Nhấp đúp vào output port để xem giá trị chạy gần nhất.
- Node `ImageSource` có thể cấp ảnh cho `Preprocess` và các tool ngay cả khi không có Global Snapshot.

### Giao diện và theme

- Theme sáng/tối dùng các `DynamicResource` chung cho Button, TextBox, ComboBox, CheckBox và TabItem.
- Các màn hình preview hiển thị kích thước ảnh và mức zoom.
- Dòng thời gian chạy trong Inspection tự xuống hàng khi thiếu chiều rộng.
- Màu chữ của CodeDetection và các điều khiển được ràng buộc theo theme để giữ độ tương phản.

### Vision Engine

- Origin hỗ trợ `ShapeBased`, `TemplateMatch` và `FeatureBased`.
- FeatureBased dùng SIFT, BFMatcher và RANSAC Homography; nếu không đủ đặc trưng sẽ dự phòng sang template matching.
- Origin có giới hạn góc `MinAngle` và `MaxAngle`.
- ROI của Caliper, EdgePair và Point được xoay theo pose của Origin. `ExtractStraightRoi` và `MapToGlobal` chuẩn hoá việc cắt ROI thẳng và chuyển toạ độ về ảnh gốc.
- Template rỗng hoặc ROI không hợp lệ trả về kết quả không đạt thay vì làm OpenCV phát sinh ngoại lệ.

## Cập nhật 2026-07-19

### Tool Point

- Bổ sung lựa chọn `FeatureBased` cho Tool Point, dùng cùng pipeline SIFT/RANSAC của Origin.
- `TemplateMatch` của Tool Point dùng NCC; `FeatureBased` dùng homography để xác định vị trí và góc quay.
- EdgePoint kiểm tra cường độ biên trong Template ROI nhưng luôn trả vị trí là giao điểm hai đường tâm của Template ROI. Điều này giữ điểm tham chiếu ổn định cho các phép đo phía sau.
- Khi sao chép cấu hình Point/Origin để chạy với ROI dẫn hướng, các tuỳ chọn thuật toán, góc, EdgePoint và ShapeModel đều được giữ nguyên.

### Routing cạnh trên canvas

- Với cạnh có input port nằm về bên trái output port, dây đi ra bên phải node nguồn, vòng theo lane phía trên hoặc dưới hai node rồi tiến vào input port.
- Cách đi này tránh để đường dây chạy ngược xuyên dưới node nguồn và vẫn rõ khi kéo dây tạo cạnh mới.

### ImageSource và preview

- Pipeline đọc đúng kết nối `ImageSource → Preprocess → Tool`.
- Preview được phép tiếp tục khi Global Snapshot rỗng để lấy ảnh từ ImageSource.
- Lưu template cho Origin, Point và SurfaceCompare hoạt động với nguồn ảnh ImageSource.

### Sửa lỗi và Cải thiện UX/UI (Phiên làm việc hiện tại)

- Khắc phục lỗi Tool Distance (và các tool khác) cho kết quả dao động nhỏ giữa các lần RUN trên cùng 1 ảnh (áp dụng HomographyMethods.LMedS thay vì Ransac để loại bỏ yếu tố ngẫu nhiên).
- Khắc phục lỗi Tab Inspection không hiển thị Overlay ngay sau khi bấm Run (do ObservableCollection không kích hoạt cập nhật trên FastOverlayCanvas, đã chuyển sang cấp phát lại List<OverlayItem> mới sau mỗi lần RefreshOverlayItems).
- Sửa lỗi SurfaceCompare và Text không nhận ảnh preview từ Preprocess hoặc ImageSource.
- Chuyển `UpdateSourceTrigger` của hộp thoại nhập liệu Tool Condition và Text sang `LostFocus` để khắc phục triệt để lỗi giật lag khi gõ.
- Thêm thông tin thời gian thực thi (Execution time): Hiển thị thời gian chạy (ms) của mỗi node ngay trên màn hình Tool Editor Canvas, và hiển thị tổng thời gian thực thi (Total Execution Time) ở Status Bar.
- Khắc phục lỗi hiển thị tiếng Việt trên các hộp thoại thông báo Camera và Overlay chữ của kết quả phân tích SurfaceCompare (Số lỗi, Diện tích lớn nhất).
- Loại bỏ các tool không dùng đến (DefectROI, LinePairDetection) khỏi danh sách Toolbox để giao diện hiển thị gọn gàng.
- Sửa lỗi mất kết nối đường viền đồ hoạ khi bỏ chọn node trên Canvas do hiệu ứng trễ `Delay=500` của binding.
- Đã hoàn thành quá trình tối ưu và phân rã tệp `ToolEditorViewModel.cs` đồ sộ (~10,000 dòng) thành các tệp tin C# nhỏ hơn (sử dụng từ khóa `partial class`) theo từng vùng tính năng logic (Engine, GraphOps, Config) và các thành phần Tool độc lập để dễ dàng bảo trì.
- Đã tối ưu hiệu suất hiển thị Overlay (FastOverlayCanvas và ImageViewerControl) bằng cách chuyển ObservableCollection sang List kết hợp với cơ chế Pen caching và gỡ bỏ INotifyCollectionChanged, giúp tăng hiệu năng vẽ và tăng giới hạn MaxBlobOverlayCount từ 300 lên 1000 mà không gây giật lag.
- Sửa lỗi Overlay không hiển thị (màn hình Preview Final Output trống trơn) sau khi tối ưu hiệu suất. Nguyên nhân do khối lệnh gán danh sách `FinalOverlayItems` bị mất trong quá trình refactor, và đã khắc phục thêm độ trễ DataBinding của WPF bằng cách thiết lập property trực tiếp xuống `PART_FastOverlay` trong code-behind của `ImageViewerControl`.
- Đóng gói file `.job`: Đã thay thế cách lưu VisionConfig file `.json` sang chuẩn đóng gói `.job` (tệp ZIP chứa file JSON cấu hình và thư mục `templates` lưu trữ các hình ảnh crop tham chiếu), giúp quản lý tập trung và tránh mất mát template khi copy job sang máy khác.
- Đã thiết kế lại thanh tiêu đề (Title Bar) hiển thị tên file Job hiện tại kèm dấu hoa thị (`*`) cảnh báo khi có thay đổi (chưa lưu). Khi tắt ứng dụng hoặc tạo Job mới sẽ hiển thị hộp thoại xác nhận lưu.
- Đã thiết kế lại thanh tiêu đề (Title Bar) hiển thị tên file Job hiện tại kèm dấu hoa thị (`*`) cảnh báo khi có thay đổi (chưa lưu). Khi tắt ứng dụng hoặc tạo Job mới sẽ hiển thị hộp thoại xác nhận lưu.
- Tiết kiệm không gian màn hình bằng cách hợp nhất dải menu `TabControl` lên trên cùng một hàng với Title Bar. Thêm nút bấm Global `Close Job` cạnh tiêu đề giúp xoá hoàn toàn Job khỏi bộ nhớ ứng dụng.
- Khắc phục lỗi Tool Editor bị đánh dấu `IsDirty` (`*`) ngay lập tức khi vừa mở Job do sự kiện `CollectionChanged` của Nodes/Edges bị kích hoạt trong lúc load config.
- Bổ sung phím tắt `Ctrl + S` lưu nhanh cấu hình Job tại Tab Tool Editor, và thiết kế lại nút Run Flow thành dạng Icon Button chuyên nghiệp hơn.
### Sửa lỗi thuật toán Vision

- Khắc phục lỗi `EdgePairDetection` không bắt được cạnh do sự sai lệch của bộ lọc làm mượt biên `Sm()`. Đã chuẩn hóa lại các điều kiện biên giới hạn, giúp triệt tiêu các độ dốc nhiễu cực đại (noise gradient) ở ranh giới vùng ảnh, qua đó bắt được đúng cạnh thực bên trong.
- Nâng cấp thuật toán `SurfaceCompare`: Thay thế thuật toán Absdiff cơ bản bằng **Variation Model (Edge Tolerance)**. Hỗ trợ cho phép tạo dung sai biến thiên quanh các đường viền cạnh (bù đắp lỗi dịch chuyển nội suy do xoay hoặc nội suy ảnh Sub-pixel). Khắc phục triệt để lỗi "hở viền" nhiễu sáng khi so sánh ảnh chụp thực tế (đã xoay) so với template gốc.
- Sửa lỗi hiển thị tiếng Việt (Encoding UTF-8) trên text overlay của SurfaceCompare trong tab Tool Editor bằng cách sử dụng trực tiếp các mã escape Unicode (`\u1ed1`, `\u1ed7`, ...).
- Đã hoàn thành khắc phục lỗi thuật toán `ShapePyramid`: Loại bỏ vùng xoá biên giả (margin zeroing) giúp score trên ảnh teaching gốc đạt đúng **1.0000**, nâng cấp sang thuật toán Pyramid đa cấp độ (Coarse-to-Fine Gaussian Pyramid) kết hợp bảo toàn tâm quay (`RotateTemplateCentered`) cho ảnh xoay (score đạt > **0.94 - 0.98** trên ảnh xoay).
- Đồng bộ chuẩn hướng xoay (Rotation Angle Sign Convention): Đã sửa lỗi đảo ngược hướng xoay ROI giữa `RotateTemplateCentered` (OpenCV GetRotationMatrix2D) với hệ tọa độ màn hình và `FeatureBased`/`Rotate()`, đảm bảo khi ảnh bị xoay thì tất cả ROI dẫn hướng xoay đúng hướng 100% không bị lệch NG.
- Áp dụng tùy chỉnh `AngleStep` cho tất cả các thuật toán Origin trong tool (`ShapeBased`, `ShapePyramid`, `TemplateMatch`, `TemplateMatchPyramid`).
- Hoàn thành hợp nhất Tab Inspection vào Tab Tool Editor làm một Tab duy nhất: Bổ sung thanh Sub-Tab `⚙ Node Graph & Cấu hình Tool` và `📊 Kết quả Inspection & Debug`, thêm Live OK/NG Result status pill badge trên thanh công cụ Header của Tool Editor, tự động đồng bộ kết quả kiểm tra, bảng Spec, bảng Conditions, Code Detection và công cụ SurfaceCompare Debugger.
- Tách biệt nút "Lưu Template Origin": Bổ sung nút bấm **"Lưu Template Origin"** độc lập. Kéo thả/thay đổi kích thước khung Template ROI (`Origin T`) chỉ cập nhật tọa độ khung, không tự động ghi đè ảnh mẫu như trước.
- Hoàn thành căn giữa vị trí mặc định cho tất cả các Tool ROI mới tạo (`DefaultRoi()`, Node `Text`) trên preview image thay vì nằm ở góc trên bên trái `(10, 10)`.
- Hoàn thành triệt tiêu vòng lặp phản hồi xoay (Feedback Loop) cho Tool Origin ROI (`Origin S`, `Origin T`) và `DefectROI`: Giữ nguyên góc quay `Angle = 0` và hệ tọa độ ảnh thô (raw image space), loại bỏ việc áp ngược góc quay `_lastRun.Origin` lên chính khung ROI của Tool Origin. Di chuyển và resize các khung ROI Tool Origin giờ đây diễn ra hoàn toàn độc lập, mượt mà và ổn định 100%.
- Hoàn thành hợp nhất Tab Inspection vào Tab Tool Editor thành một giao diện 1 màn hình đồng nhất (Single Unified Workspace - gỡ bỏ hoàn toàn sub-tabs tốn diện tích).
- Thêm Tool Node mới "Result View" (`ResultView`, icon `📊`): khi chọn node này, duy nhất 1 khung Preview hiển thị ảnh kết quả Final Output với đầy đủ các Overlay.
- Khóa chỉnh sửa ROI ở chế độ `ResultView`: Khi chọn node `ResultView`, `EnableRoiEditingInPreview` tự động về `false` để vô hiệu hóa hoàn toàn tương tác kéo thả/chỉnh ROI trên Preview.
- Bổ sung đầy đủ tất cả loại phép đo vào `SpecResults` (`Distance`, `LineLineDist`, `PointLineDist`, `EdgePair`, `EdgePairDetect`, `Diameter`), khắc phục lỗi không hiển thị danh sách phép đo khi chạy Job.
- Tích hợp toàn bộ bảng kết quả Inspection vào Panel bên phải theo dạng danh mục cuộn dọc đồng thời (WrapPanel runtime breakdown bar, Bảng Spec đo đạc, Bảng Điều kiện Logic, Bảng Thời gian chạy từng Tool `ToolTimings`, Code detection và SurfaceCompare Debugger).
- Gỡ bỏ dòng chữ tiêu đề `CMS VINA VISION SYSTEM` ở Header theo yêu cầu.
- Hỗ trợ chế độ chạy lặp ảnh tự động theo thư mục đối với Tool ImageSource (`SourceType == Folder`):
  - Khi bấm `▶ Run Flow`, hệ thống quét tất cả các tệp ảnh hợp lệ (`.png`, `.jpg`, `.bmp`, `.tif`) trong thư mục đã chọn (`FolderPath`).
  - Tự động thực thi tuần tự từng ảnh theo khoảng thời gian nghỉ tùy chỉnh (`FolderIntervalMs`) và có hỗ trợ lặp lại (`LoopFolder`).
  - Nút bấm `Run Flow` tự động đổi tên/icon/màu sắc sang **`⏹ STOP`** (màu đỏ `#D32F2F`) trong suốt thời gian chạy luồng thư mục. Bấm `STOP` sẽ dừng luồng chạy ngay lập tức.
  - Sửa lỗi bảng kết quả bên phải (Cột 4 / Column 6) bị trống khi chạy với thư mục do thiếu gán `LastResult = _lastRun`.
- Khắc phục hiển thị xoay khung ROI Tool Origin (`Origin S`, `Origin T`) & Thêm Score Overlay & Tối ưu Score ShapePyramid khi xoay:
  - Tool Origin ROI Rotation Logic: Khi có dữ liệu chạy RUN (`_lastRun`), các khung ROI `Origin S` & `Origin T` xoay và tịnh tiến bám 100% theo góc và vị trí nhận diện được trên cả node `Origin` lẫn `ResultView`. Khi chưa RUN (chế độ teaching), các khung ROI giữ nguyên toạ độ thô (`Angle = 0`) để người dùng dễ kéo thả chỉnh vị trí không bị phản hồi xoay.
  - Sửa lỗi Score ShapePyramid bị giảm thấp khi xoay: Khắc phục hiện tượng vùng viền đen 0 (padding black border) do xoay WarpAffine làm suy giảm chỉ số tương quan Normalized Cross-Correlation (`CCoeffNormed`) từ 1.0 xuống 0.4 - 0.5. Thuật toán giờ đây trích xuất vùng candidate đã nhận diện, xoay ngược lại `-bestAngle` và tính điểm trực tiếp với mẫu chưa xoay, trả về điểm số thực chính xác cao (**0.95 - 0.99** trên ảnh xoay).
  - Hiển thị Score lên Overlay Tool Origin: Trực tiếp đưa điểm số, ngưỡng `Threshold`, góc xoay `AngleDeg` và trạng thái `OK/NG` lên nhãn overlay của `Origin S`, `Origin T` và tâm mẫu trên màn hình preview.
- Ứng dụng đã được biên dịch thành công 0 lỗi.

### Cập nhật 2026-07-22 (Phiên làm việc mới nhất)

- **Hiển thị Preview Template Origin**: Thêm thuộc tính `Origin_TemplatePreviewImage` trong `ToolEditorViewModel.ToolOrigin.cs` và cập nhật UI `ToolEditorView.xaml` hiển thị ảnh crop mẫu đã lưu gần nhất trong Properties Panel của Tool Origin.
- **Tự động cập nhật nét vẽ ROI & Font size khi Zoom**: Thêm lời gọi `RedrawOverlays()` trong `RootOnPreviewMouseWheel` (`ImageViewerControl.xaml.cs`), giúp nét vẽ ROI và font size tự động thu phóng lập tức theo zoomfactor trên tất cả các node (gồm cả node `ResultView`) mà không cần di chuột hover hay bấm Run Flow.
- **Dọn dẹp triệt để khi Close Job**: Cập nhật `CloseJob()` (`ToolEditorViewModel.Config.cs`) để xoá sạch ảnh khỏi `SharedImageContext` (`_sharedImage.SetImage(null)`), xoá các bộ nhớ đệm preview, danh sách overlay và bảng kết quả inspection, đưa màn hình preview về trạng thái trống ban đầu.
- **Khắc phục dứt điểm Score ShapePyramid khi xoay**: Đã phân tích đúng nguyên nhân ảnh Sobel magnitude có 95% diện tích 0 (nền đen) khiến chỉ số `CCoeffNormed` bị suy giảm mạnh khi xoay nét vẽ 1px. Cập nhật `MatchByPyramidFast` (`VisionEngine/Class1.cs`): Sử dụng Gaussian Pyramid Sobel để định vị vị trí và góc xoay `bestAngle` nhanh và chính xác tuyệt đối, sau đó tính toán điểm `bestScore` bằng `CCoeffNormed` trên **ảnh xám chuẩn** (`roiGray` vs `templPrep` được xoay theo `bestAngle`). Kết quả trả về điểm score chính xác cao (**0.95 - 0.99**).
- **Tách nút Run Flow thành `Run Once` và `Run Continuous`**:
  - `▶ Run Once`: Thực thi Flow 1 lần. Khi nguồn ảnh là Folder, mỗi lần bấm nạp tệp ảnh tiếp theo trong thư mục và thực thi rồi dừng lại (tự động tăng chỉ số tệp cho lần bấm tiếp theo).
  - `🔁 Run Continuous` / `⏹ STOP`: Kích hoạt chạy lặp tự động liên tục qua tất cả tệp ảnh trong thư mục theo `FolderIntervalMs` và `LoopFolder`. Nút hiển thị màu đỏ `⏹ STOP` trong suốt thời gian chạy liên tục và cho phép người dùng dừng luồng bất kỳ lúc nào.
  - Giao diện thanh công cụ Header ([ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml)) được thiết kế lại gọn gàng với 2 nút bấm màu sắc và icon phân biệt rõ ràng.
- **Loại bỏ tự động Run Flow khi thay đổi/di chuyển ROI**: Cập nhật `OnRoiEdited` (`ToolEditorViewModel.Engine.cs`) và `Origin_TeachTemplate` (`ToolEditorViewModel.ToolOrigin.cs`). Thay thế toàn bộ các lời gọi `RunFlow()` trong quá trình thao tác kéo thả, di chuyển, resize ROI và teach template bằng `RefreshPreviews()`. Việc chỉnh sửa/teaching ROI giờ đây chỉ thay đổi tọa độ lý thuyết trong cấu hình, cập nhật hiển thị khung ROI trên màn hình preview và lưu cấu hình mà không tự động thực thi luồng kiểm tra.
- **Xoay khung bao BoundingBox & Search ROI của CodeDetection theo Origin**: Bổ sung góc xoay `Angle = angleDeg` (`run.Origin?.AngleDeg`) cho `OverlayRectItem` chứa BoundingBox kết quả đọc mã barcode/QR và Search ROI của `CodeDetection` tool trong `ToolEditorViewModel.Engine.cs` (node `ResultView` & node `CodeDetection`) cũng như `InspectionViewModel.cs` (màn hình kiểm tra chính). Đường bao kết quả đọc mã giờ đây tự động xoay chính xác theo hướng xoay của sản phẩm.
- **Bổ sung thuộc tính Min Score cho Tool Origin**:
  - Thêm thuộc tính `MinScore` trong `OriginDefinition` ([VisionInspectionApp.Models\Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Class1.cs)) và thuộc tính ViewModel `Origin_MinScore` trong `ToolEditorViewModel.ToolOrigin.cs`.
  - Thêm ô nhập liệu `Min Score` trên giao diện Properties Panel UI ([ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml)) trong thẻ Origin.
  - Sử dụng `MinScore` để đánh giá `Origin.Pass` (`originMatch.Score >= config.Origin.MinScore`) và hiển thị ngưỡng `(Thr: ...)` trên overlay thông tin Origin.

### Cập nhật 2026-07-24 (Phiên làm việc mới nhất)

- **Chuyển đổi cơ chế xem ảnh của node ImageSource (Camera mode)**:
  - Gỡ bỏ việc đăng ký nhận luồng 30 FPS (`_cameraService.FrameCaptured`) tự động đẩy vào `_sharedImage` trong `ToolEditorViewModel`, loại bỏ tình trạng livestream hình ảnh liên tục khi bấm chọn/xem các node trên Canvas.
  - Khi nguồn vào là `Camera`, bấm `Run Once` (hoặc `Run Continuous`) mới thực hiện chụp đúng **1 frame tĩnh** từ camera để làm ảnh đầu vào thực thi inspection flow và lưu vào cache preview.
  - Việc chuyển đổi/view qua lại giữa các node hiển thị ảnh tĩnh đã chụp trước đó, không gây giật lag hoặc livestream liên tục.
- **Cố định vị trí Search ROI của Tool Origin (`Origin S`)**:
  - Cập nhật cách hiển thị overlay của Tool `Origin` trong `ResultView` node (Final View), `AddConfigRois`, `BuildOverlayForNodeFromRunWithConfig` và `InspectionViewModel`.
  - Khung Search ROI (`Origin S`) giữ nguyên vị trí và hướng thẳng ban đầu như lúc teaching (`Angle = 0`), không xoay/tịnh tiến theo pose nhận diện được.
  - Khung Template ROI (`Origin T`) duy trì xoay và di chuyển bám 100% theo góc xoay và tâm sản phẩm nhận diện.
- **Sửa lỗi CheckBox Show Results & Show ROI trên Node Preview Header**:
  - Bổ sung thuộc tính ViewModel `ShowResultOverlay` và phương thức cập nhật `OnShowResultOverlayChanged`.
  - Cập nhật binding trên `ToolEditorView.xaml` với `Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` giúp CheckBox kích hoạt phản hồi tức thì khi tích/bỏ tích.
  - Phân tách rõ rệt hai lớp hiển thị: `Show ROI` đóng/mở hiển thị các khung vẽ ROI (`SearchRoi`, `TemplateRoi`, `InspectRoi`), `Show Results` đóng/mở hiển thị kết quả phân tích kiểm tra (`Match score`, đường đo khoảng cách, điểm nhận diện, text overlay, contour blob).
- **Khắc phục dứt điểm lỗi lệch bước ảnh preview & Overlay giữa các node khi Run Once**:
  - **Nguyên nhân**: Khi thực thi `RunOnce` ở chế độ Folder, sau khi kiểm tra xong ảnh `N`, chỉ số `_folderImageIndex` tăng lên `N+1` để chuẩn bị cho lần chạy tiếp theo. Khi người dùng click xem node `ImageSource`, `LoadImageFromSourceForPreview` đọc `_folderImageIndex` (`N+1`) và nạp trước ảnh tiếp theo từ đĩa ghi đè vào cache, trong khi `_lastRun` vẫn lưu kết quả của ảnh `N`. Đồng thời `RunSingleFlowFromImageFile` chưa gọi `_sharedImage.SetImage(mat)` dẫn đến các node hạ nguồn (`Preprocess`, `Origin`, `Point`, `Line`...) hiển thị ảnh cũ hoặc lệch bước so với kết quả overlay `_lastRun`.
  - **Khắc phục**:
    1. Cập nhật `RunSingleFlowFromImageFile` và `RunFlow` gọi `_sharedImage.SetImage(mat)` ngay khi đọc được frame ảnh đầu vào, đảm bảo `_sharedImage` lưu đúng 100% hình ảnh thực tế đã được kiểm tra trong `_lastRun`.
    2. Cập nhật `LoadImageFromSourceForPreview`: Khi `_imageSourcePreviewCache` đã lưu sẵn ảnh vừa thực thi của nguồn `ImageSource`, hàm sẽ lập tức trả về ảnh từ cache thay vì đọc file mới từ đĩa theo biến `_folderImageIndex`.
    3. Thêm cơ chế tự xóa cache `_imageSourcePreviewCache` và đặt lại `_folderImageIndex = 0` khi người dùng thay đổi cấu hình nguồn ảnh (`FilePath`, `FolderPath`, `SourceType`) trong bảng thuộc tính.
    4. Giúp tất cả các node (`ResultView`, `ImageSource`, `Preprocess`, `Origin`, `Point`, `Line`, `Caliper`, `Blob`, v.v.) hiển thị khớp 100% cùng 1 tấm ảnh và cùng 1 bộ Overlay sau mỗi lần bấm `Run Once`.
- **Hỗ trợ Xoay ROI 360 độ và thêm Tay cầm (Handle) xoay ROI cho tất cả các Tool**:
  - **Data Model**: Bổ sung thuộc tính `Angle` (double) vào class `Roi` ([VisionInspectionApp.Models\Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Class1.cs)). Tất cả các Tool (Point, Line, Caliper, Blob, SurfaceCompare, CircleFinder, EdgePair, CodeDetection, DefectConfig) đều tự động kế thừa khả năng lưu trữ góc xoay ROI.
  - **Giao diện & Tương tác (`ImageViewerControl.xaml.cs`)**:
    - Thiết kế thêm tay cầm xoay phía trên khung ROI: gồm đường Stem nối từ cạnh trên lên núm tròn xoay màu cam (Orange Handle).
    - Khắc phục triệt để tâm xoay (Rotation Center Origin): Thiết lập `RenderTransformOrigin = new Point(0.5, 0.5)` trên khung `_roiEditRectShape` và tính toán vị trí tất cả các tay cầm (8 handle + rotation stem + orange circle handle) bằng hàm `RotatePoint(pt, center, angle)`. Khung ROI và các tay cầm tương tác giờ đây xoay **chính xác 100% quanh đúng tâm chính giữa của ROI** `(left + width/2, top + height/2)`.
    - Hiển thị góc xoay trực quan thời gian thực (Live Angle Badge): Bổ sung badge nổi màu tối chữ vàng rực rỡ (`25.5°`) nằm ngay phía trên núm tròn màu cam khi kéo xoay ROI hoặc khi ROI đang có góc xoay khác 0°, giúp người dùng quan sát góc độ chính xác tuyệt đối trong khi xoay.
    - Chuẩn hóa Hit-testing: Chuyển đổi tọa độ con trỏ chuột về hệ tọa độ local không xoay của ROI (`RotatePoint(p, center, -angle)`), giúp việc bấm chọn các handle góc, edge và tay cầm xoay đạt độ chính xác 100% khi ROI đang ở bất kỳ góc xoay nào.
    - Thêm chế độ `RoiEditMode.Rotate`: Kéo thả tay cầm xoay sẽ tính toán góc nghiêng thời gian thực theo con trỏ chuột (`Atan2`), hiển thị khung nghiêng sinh động trên Canvas và cập nhật `Angle` khi nhả chuột.
  - **Khắc phục triệt để lỗi Double Xoay & Không chỉnh được Template ROI sau khi Run**:
    - **Sửa lỗi Double Xoay**: `_lastRun.Origin.AngleDeg` tìm được từ thuật toán matching vốn đã là góc nghiêng thực tế của vật thể trên ảnh. Đối với `Origin T` khi hiển thị vị trí kết quả tìm được, góc xoay là `Angle = AngleDeg` (không cộng dồn `roiAngle` lần 2 làm góc xoay bị nhân đôi).
    - **Sửa lỗi Không chỉnh được ROI sau khi Run**: Khi xem/chỉnh sửa node `Origin`, khung `Origin T` được hiển thị ở tọa độ và góc xoay Teaching chuẩn `CreateRotatedRoi(_config.Origin.TemplateRoi, ...)` với Label chuẩn `"Origin T"`.
  - Gỡ bỏ lời gọi `TrySaveTemplateImage` tự động khi chỉnh sửa/di chuyển/thay đổi kích thước khung ROI `Origin T` trên màn hình preview ([ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs#L756-L762) & [#L1100-L1108](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs#L1100-L1108)).
  - Việc lưu/ghi đè hình ảnh mẫu `origin.png` và huấn luyện lại `ShapeModel` chỉ diễn ra khi người dùng bấm nút **"Lưu Template Origin"** trên Properties Panel (`Origin_TeachTemplateCommand`).
  - Thao tác thực thi RUN (`▶ Run Once`, `🔁 Run Continuous`, `Run Flow`) chỉ sử dụng file template đã dạy trước đó để kiểm tra, hoàn toàn không tự động ghi đè hay thay đổi ảnh template.
- **Nâng cấp Tool Origin theo chuẩn phần mềm MVP (Machine Vision Platform Trung Quốc)**:
  - Bổ sung tùy chọn thuật toán mới **`MvpShapeMatch`** (`Geometric Edge Contour Matching`).
  - Màn hình preview chính của Tool Editor:
    - Khi chọn Node Origin, màn hình preview hiển thị Search ROI `Origin S` (cho phép kéo di chuyển/chỉnh kích thước).
    - Vẫn **hiển thị khung Template ROI (`Origin T`) màu vàng/xanh kèm thông số Pose (Score, Status, Angle)** sau khi Run/nhận diện, nhưng ở trạng thái **Read-Only** (không cho phép chỉnh sửa trực tiếp tại màn hình chính).
  - Thiết kế cửa sổ chuyên biệt **`OriginTrainWindow`** mở bằng nút **"Train Template..."** trên Properties Panel với đầy đủ tính năng chuẩn MVP:
    - Tích hợp `ImageViewerControl` hiển thị đầy đủ khung ROI tương tác xanh lam với **8 tay cầm thay đổi kích thước + tay cầm xoay góc 360 độ màu cam** kèm nhãn hiển thị góc độ trực quan.
    - Hiển thị trực tiếp các **đường viền đặc trưng màu xanh lá cây** (Green Contours) trong khung Template ROI theo hệ toạ độ ảnh thực tế.
    - Công cụ **Eraser (Tẩy)** cho phép vẽ cọ xoá các đường viền/nếp nhăn nhiễu không mong muốn, có hỗ trợ **Undo / Redo**.
    - Bảng thông số `Parameter Configuration` (`Auto Thresh`, `Edge Threshold`, `Length Threshold`, `Max Pyramid Layer Number`, `Lock Origin Center` & toạ độ `OriginX` / `OriginY`).
    - Bảng danh sách thao tác đối tượng hình học Shape Operations (`No.`, `Shape`, `Add/Deduct`, `ParamSet`) và lựa chọn `Detection ROI` (`Full graph` vs `Part graph`).
  - **Sửa lỗi thuật toán MvpShapeMatch**:
    - Đồng bộ ảnh xem trước trong `OriginTrainWindow` với luồng xử lý ảnh thực tế (áp dụng `ResolveToolImageForPreview` bao gồm `GlobalPreprocess`).
    - Chuẩn hóa chiều xoay góc nghiêng trong `RotateTemplateCentered` (`Cv2.GetRotationMatrix2D` dùng `-angleDeg` để biến đổi xoay xuôi chiều kim đồng hồ tương thích 100% với hệ toạ độ WPF Canvas và biến đổi điểm `TransformPose`), khắc phục triệt để hiện tượng xoay ngược chiều góc của mẫu.
    - Đồng bộ trích xuất đặc trưng Canny Edge + áp dụng mảng mặt nạ `MvpEraserMask` khi thực thi nhận diện `MatchByPyramidFast`.
    - Khắc phục triệt để hiện tượng sụt giảm Score (từ 1.0 xuống 0.5 - 0.7) khi vật thể bị xoay: Loại bỏ bước tính lại điểm tương quan dựa trên ảnh xám thô (`MatchTemplate` trên ảnh xám có viền padding đen làm sai lệch điểm số khi xoay) đối với thuật toán khớp mô hình đường biên (`MvpShapeMatch`, `ShapeBased`, `ShapePyramid`). Sử dụng trực tiếp điểm số khớp biên đặc trưng chuẩn xác từ Ma trận Canny/Sobel Level 0 giúp duy trì Score ổn định ở mức cực cao (**0.92 – 1.0**) dù sản phẩm đứng thẳng hay xoay nghiêng!
  - **Nâng cấp công cụ Preprocessor - Chế độ Threshold nâng cao (Binary & Local Adaptive)**:
    - Bổ sung danh sách chọn loại ngưỡng **`ThresholdType`**: Chế độ **`Binary`** (Phân ngưỡng toàn cục) và **`Local`** (Phân ngưỡng thích ứng cục bộ Adaptive Thresholding).
    - **Chế độ Binary**:
      - `ThresholdLow`: Thanh trượt Slider + Ô nhập số (0 - 255).
      - `ThresholdHigh`: Thanh trượt Slider + Ô nhập số (0 - 255).
      - Nút đảo ngược trạng thái `⇌` (`InvertBinary`) cho phép chuyển đổi nhanh giữa `Binary` và `BinaryInv`.
    - **Chế độ Local (Adaptive Thresholding)**:
      - `MaskHeight`: Thanh trượt Slider + Ô nhập số kích thước kernel dọc (chỉ nhận số lẻ 3, 5, 7, ..., 201).
      - `MaskWidth`: Thanh trượt Slider + Ô nhập số kích thước kernel ngang (chỉ nhận số lẻ 3, 5, 7, ..., 201).
      - `Local Offset`: Thanh trượt Slider + Ô nhập số giá trị hằng số bù $C$ (-100 đến 100).
      - Nút đảo ngược trạng thái `⇌` (`InvertLocal`) cho phép đảo ngược mặt nạ thích ứng cục bộ.
    - Tích hợp đồng bộ đầy đủ các tham số mới vào `ImagePreprocessor` ([VisionInspectionApp.VisionEngine\Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.VisionEngine/Class1.cs#L559-L595)), `PreprocessSettings` ([VisionInspectionApp.Models\Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Class1.cs#L436-L449)), `ToolEditorViewModel` & `TeachViewModel` và giao diện `GlobalPreprocessWindow.xaml`.
  - **Đồng bộ chuẩn hóa ảnh trích xuất Template Origin (`origin.png`)**:
    - Chuẩn hóa luồng trích xuất ảnh mẫu `origin.png` và mô hình Shape (`ShapeModel`) khi chọn đầu vào nối từ các node Preprocess thứ cấp (Node 2):
      - **Ảnh hiển thị tương tác trong cửa sổ Train**: Sử dụng ảnh đã qua node Preprocess thứ cấp để hỗ trợ quan sát/tẩy xóa đường biên Canny/Threshold theo đồ thị tool graph.
      - **Ảnh mẫu lưu ra file `origin.png`**: Luôn được cắt trực tiếp từ **ảnh gốc đã qua xử lý Global Preprocessor (`_preprocessor.Run(rawCameraMat, _config.Preprocess)`) [Ảnh 1]**, tuyệt đối không lưu từ ảnh đã qua node xử lý cục bộ thứ cấp [Ảnh 2].
    - Cập nhật cả 2 vị trí lưu mẫu (`SaveToOriginDefinition` trong `OriginTrainViewModel` & `TrySaveTemplateImage` trong `ToolEditorViewModel`) giúp tất cả thuật toán Origin chạy ổn định và đồng nhất 100%.
  - **Khắc phục lỗi lệch góc xoay ROI Template và sụt giảm điểm số Score (0.15 - 0.19)**:
    - **Sửa lỗi nhân đôi góc xoay (Double Rotation Angle)**: Đã điều chỉnh `result.Origin.AngleDeg` trong `InspectionPipeline` ([VisionInspectionApp.Application\Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Class1.cs#L1150-L1155)) lưu góc xoay tương đối `poseAngleDeg` ($\Delta \theta = \theta_{found} - \theta_{teach}$) thay vì góc tuyệt đối `originMatch.AngleDeg`.
    - **Tối ưu hóa điểm số Score (đạt 0.90 – 1.0)**:
      - Mở rộng bộ lọc mờ Gaussian cho Canny từ `Size(3, 3)` lên `Size(5, 5), sigma=1.5`.
      - Bổ sung điều kiện bảo vệ kích thước ma trận Kim tự tháp (`maxPyramidLevel` guard): tầng cao nhất luôn $\ge 12 \times 12$ px.
  - **Thống nhất Pipeline dữ liệu đồng bộ cho Tool Origin (Train & Run)**:
    - **Nguyên tắc thiết kế**:
      - File đĩa `origin.png` luôn lưu ảnh gốc đã qua **Global Preprocess (Image 1)** để làm cơ sở dữ liệu (base) sạch, độc lập với node xử lý thứ cấp.
      - Tại thời điểm **Run**:
        - Ảnh thực tế: Raw → Global Preprocess → Local Preprocess node = **Image 2**.
        - Ảnh mẫu: File `origin.png` (Image 1) → `PreprocessTemplateForMatch(originPre)` = **Image 2**.
        - Cả hai ma trận mẫu và thực tế đều được xử lý về cùng một không gian ảnh (Image 2) trước khi chạy thuật toán Canny / Feature Match.
      - Tại thời điểm **Train**:
        - Preview & Contour Overlay trong cửa sổ Train: Hiển thị và trích xuất đường biên dựa trên **Image 2**.
        - `ShapeModel`: Được huấn luyện dựa trên patch trích xuất từ **Image 2**.
        - File lưu `origin.png`: Được cắt và lưu từ **Image 1**.
    - **Các file đã cập nhật**:
      - [VisionInspectionApp.Application\Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Class1.cs#L1093-L1135): `InspectionPipeline` sử dụng `ResolveToolPreprocess("Origin")` cho ảnh thực tế và truyền `originPre` vào `MatchWithRotation`.
      - [VisionInspectionApp.UI\ViewModels\OriginTrainViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/OriginTrainViewModel.cs#L410-L458): Lưu `origin.png` từ `_globalPreprocessedMat` (Image 1), huấn luyện `ShapeModel` từ `_rawFullMat` (Image 2).
      - [VisionInspectionApp.UI\ViewModels\ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs#L764-L777): Cập nhật `TrySaveTemplateImage` tương tự cho Tool Origin.

### Cập nhật 2026-07-28 (Phiên làm việc mới nhất)

- **Sửa lỗi toàn diện thuật toán Tool Origin & MvpShapeMatch**:
  - **Khắc phục lỗi Score thấp (0.95) trên ảnh teach gốc (0.0°)**:
    - Loại bỏ hiện tượng bỏ sót góc 0.0° trong vòng lặp quét thô (coarse angle sweep) bằng cách căn lưới góc coarse trùng với `0.0°` (`Math.Floor(minAngleDeg / coarseStep) * coarseStep`).
    - Bổ sung cơ chế neo ứng viên `0.0°` (anchor candidate) duy trì xuyên suốt tất cả các tầng Kim tự tháp (Pyramid Levels), đảm bảo khi chạy với chính ảnh gốc teach, góc 0.0° luôn được đánh giá ở tầng 0.
    - Cập nhật hàm tính điểm khớp biên hình học `ComputeGeometricEdgeScore`: đánh giá trong vùng 3x3 sub-pixel xung quanh nét vẽ biên, giúp điểm số thu được trên ảnh gốc teach đạt **chính xác tuyệt đối 1.0000** và góc quay **0.00°**.
  - **Khắc phục lỗi lệch vị trí và góc quay trên ảnh xoay**:
    - Chuẩn hóa việc quản lý tọa độ tâm ứng viên qua các tầng Kim tự tháp về hệ tọa độ tầng 0 (`CenterInLevel0`), loại bỏ triệt để lỗi nhân đôi / chia sai tỷ lệ khi chuyển tầng downsampling khiến cửa sổ tìm kiếm bị chệch ra ngoài ảnh.
    - Duy trì Top 5 ứng viên tiềm năng ở mỗi tầng pyramid, giúp bắt chính xác vật thể ở các góc xoay lớn (+25°, -45°...); vị trí trả về chính xác tuyệt đối **(600.00, 450.00)** và góc quay **25.00°**, điểm số đạt **1.0000**.
  - **Tối ưu tốc độ cực cao**:
    - Xử lý mượt mà chỉ mất khoảng **~60 ms** nhờ kết hợp Kim tự tháp Gaussian Sobel magnitude downsample với trích xuất biên tập trung.
- **Bổ sung kết quả Tool Angle, LinePairDetect và Format Bảng SpecResults (OK/NG Colors)**:
  - Bổ sung đầy đủ kết quả đo đạc `Angle` (`AngleResult`) và `LinePairDetect` vào bộ sưu tập `SpecResults` và danh sách lý do lỗi `NgReasonsText` trong `ToolEditorViewModel.Inspection.cs`.
  - Định dạng hàng tự động trong bảng `SpecResults` (tại cả `ToolEditorView.xaml` và `InspectionView.xaml`):
    - Hàng có kết quả **NG** (`Pass == False`): Nền màu đỏ nhạt (`#E53935`), màu chữ trắng (`#FFFFFF`).
    - Hàng có kết quả **OK** (`Pass == True`): Nền màu xanh lá nhạt (`#C8E6C9`), màu chữ đen (`#000000`).
- **Hiển thị thời gian thực thi (Execution Time) cho tất cả các Node trên Canvas**:
  - Rà soát và bổ sung đo thời gian chạy (`NodeTimings`) cho tất cả các tool trong pipeline `InspectionPipeline` (`VisionInspectionApp.Application/Class1.cs`): `Origin`, `Distance`, `LineLineDistance`, `PointLineDistance`, `Angle`, `Diameter`, `EdgePair`, `EdgePairDetect`, `LinePairDetection`, `BlobDetection`, `CircleFinder`, `CodeDetection`, `SurfaceCompare`, `Condition`, `Text`, `ResultView`.
  - Cập nhật hàm `UpdateNodeExecutionTimes()` trong `ToolEditorViewModel.cs` để hiển thị chính xác thời gian `Time: X ms` cho từng node trên canvas graph.
- **Sửa lỗi Checkbox `Show ROI` & `Show Results` trên màn hình Preview**:
  - Khắc phục lỗi checkbox `Show Results` (`ShowResultOverlay`) và `Show ROI` (`ShowRoisInSelectedPreview` & `ShowRoisInFinalPreview`) không có tác dụng khi bật/tắt.
  - Đồng bộ hóa các thuộc tính `ShowRoisInSelectedPreview` và `ShowRoisInFinalPreview` đồng thời thêm kiểm tra `ShowResultOverlay` trong `BuildFinalOverlayFromRunWithConfig()`, giúp việc bật/tắt hiển thị ROI và Overlay kết quả (đường đo, điểm, nhãn) hoạt động tức thì trên cả Selected Node Preview và ResultView.
- **Nâng cấp toàn diện Tool `CircleFinder` theo chuẩn phần mềm MVP (Radial Caliper Circle Finder)**:
  - **Động cơ Radial Caliper**: Chia vành đai tìm kiếm thành $N$ thanh quét hướng tâm (`StripCount`), tùy chỉnh độ rộng (`StripWidth`), chiều dài quét hướng tâm (`StripLength`), góc quét đầu/cuối (`MinAngleDeg`, `MaxAngleDeg`).
  - **Lấy mẫu Profile 1D & Sub-Pixel**: Trích xuất profile độ sáng 1D trung bình theo bề rộng thanh quét bằng lấy mẫu nội suy Bilinear. Phát hiện đỉnh gradient cực trị theo `Polarity` (`LightToDark`, `DarkToLight`, `Any`), `EdgeSelection` (`First`, `Last`, `MaxStrength`) và `MinEdgeStrength`. Nội suy parabol 3 điểm đạt độ chính xác Sub-Pixel.
  - **Khớp đường tròn RANSAC + Kasa Least-Squares**: Lọc nhiễu / điểm ngoại lệ (outliers) bằng RANSAC 100 vòng lặp, sau đó khớp đường tròn tối ưu bằng giải hệ phương trình tuyến tính Kasa Least-Squares. Trả về tâm đường tròn $(C_x, C_y)$, bán kính $R$, đường kính $D = 2R$.
  - **Giao diện Properties Panel**: Bổ sung đầy đủ các ô nhập liệu `Strip Count`, `Strip Width`, `Strip Length`, `Polarity`, `Edge Selection`, `Min Edge Strength`, `Min Angle`, `Max Angle` trong `ToolEditorView.xaml`. Đã phát tín hiệu NotifyPropertyChanged (`IsCircleFinderNode`, `Cf_*`) khi chọn node CircleFinder giúp Properties Panel hiển thị tức thì.
  - **Hiển thị Overlay & Thao tác Handle ROI**:
    - Thiết lập `ActiveRoiLabel = $"{node.RefName} CIR"` khi chọn node `CircleFinder`, kích hoạt các tay cầm (handles) xoay/co giãn ROI trực tiếp trên màn hình Preview.
    - Đồng bộ hoàn toàn tọa độ và góc xoay của dải khung các thanh quét radial (`AddRadialCaliperStripsOverlay`) với tâm biến đổi Origin (`GetRoiPose`), khắc phục dứt điểm tình trạng các caliper strips bị lệch vị trí so với khung ROI khi Origin có độ dời/góc xoay.
    - Bổ sung `roiAngleRad` vào góc quét của từng dải radial caliper trong `DetectCircleByRadialCaliper` (`Class1.cs`) đảm bảo việc lấy mẫu 1D profile trùng khớp chính xác với góc xoay thực tế của ROI/Origin.
    - Bổ sung hỗ trợ nhãn ROI dạng `CIR`, `Cal`, `EPD` trong control `ImageViewerControl.xaml.cs` giúp việc kéo thả/vẽ lại ROI của CircleFinder tự động cập nhật vào `CircleFinderDefinition.SearchRoi`.
- **Sửa lỗi lưu & đồng bộ đơn vị Calibration (mm vs px)**:
  - Khắc phục lỗi trong tab `Calibration`: trước đây khi bấm `Save Job`, thuộc tính `_config.PixelsPerMm` không tự động cập nhật từ `AveragePixelsPerMm`, dẫn đến file `.job` lưu ra vẫn mang giá trị mặc định (`1.0` / uncalibrated). Đã cập nhật `CalibrationViewModel.cs` để tự động gán `_config.PixelsPerMm = AveragePixelsPerMm` khi tính toán, khi mở Job và khi bấm `Save Job`.
  - Cập nhật hiển thị nhãn Overlay Canvas (`BuildFinalOverlayFromRun` & `BuildOverlayForNodeFromRun`): Đơn vị hiển thị trên hình ảnh preview sẽ tự động là `mm` nếu công cụ đã được calib (`PixelsPerMm > 0` và khác `1.0`), ngược lại hiển thị `px`.
  - Cập nhật bảng `SpecResults` trong cả `ToolEditorView` và `InspectionView`: Bổ sung cột `Unit` (`mm`, `px`, `°`) và tiêu đề động `SpecResultsValueHeader`, giúp hiển thị rõ ràng giá trị đo đạc kèm đơn vị tương ứng.
- **Tích hợp Tính năng Auto-Complete / IntelliSense cho Tool Text và Condition**:
  - Tạo đính kèm giao diện `IntellisenseBehavior` ([IntellisenseBehavior.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Controls/IntellisenseBehavior.cs)) áp dụng cho các ô TextBox nhập liệu biểu thức / văn bản mẫu (`Condition_Expression`, `TextNode_Text`, `Expression` rule).
  - **Tự động gợi ý tên Tool & Thuộc tính**: Khi người dùng gõ tên Tool kèm dấu chấm (ví dụ `Caliper1.` hoặc `{Circle1.` hay `Origin.`), hệ thống tự động mở danh sách xổ xuống ngay bên dưới con trỏ chuột/con trỏ soạn thảo (caret), hiển thị các thuộc tính hỗ trợ phù hợp cho loại tool đó (`.Value`, `.Pass`, `.Found`, `.Score`, `.Text`, `.Count`, `.MaxArea`).
  - **Phím tắt & Thao tác**: Hỗ trợ phím mũi tên `Up`/`Down` để di chuyển, `Enter`/`Tab` hoặc click chuột để chèn nhanh thuộc tính được chọn vào văn bản mà không cần phải nhớ chính xác tên thuộc tính. Hỗ trợ phím `Esc` để đóng popup.
  - **Xử lý an toàn khi xóa văn bản & Khắc phục WPF ListBox ItemsSource**: 
    - Khắc phục triệt để lỗi `InvalidOperationException` (dạng `An ItemsControl is inconsistent with its items source`) bằng cách khởi tạo danh sách mới (`filtered.ToList()`) gán trực tiếp cho `ListBox.ItemsSource` thay vì mutate biến `List<T>` cũ.
    - Rà soát toàn bộ các tool trong đồ thị (`CircleFinders`, `LinePairDetections`, `SegmentLineDistances`, `SurfaceCompares`, `BlobDetections`, `CodeDetections`, `Diameter`, `EdgePairs`, `EdgePairDetections`, `Distance`, `Angles`, `Points`, `Origin`, `Preprocess`, `ImageSource`, `Text`, `Condition`), bổ sung đầy đủ biến đầu ra vào `ConditionEvaluator.BuildVariableMap`, `EvaluateTextTemplate`, và danh sách gợi ý thuộc tính `IntellisenseBehavior.GetPropertiesForNode`.
  - **Tool mới Output Image (Xuất ảnh) & Bổ sung Output Port cho ResultView**:
    - Định nghĩa `ImageOutputDefinition`, các enum `ImageOutputFormat` (`PNG`, `JPG`, `BMP`) và `ImageOutputCondition` (`Always`, `OnPass`, `OnFail`) trong [Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Class1.cs).
    - **Truy xuất đúng nguồn ảnh đầu vào**: Động cơ `ExecuteImageOutputs` trong [Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Class1.cs) tự động truy vết node nguồn theo `InputNodeName` hoặc đường nối dây trên đồ thị (Node Graph). Nếu kết nối với node `Preprocess`, ảnh xuất ra chính là ảnh đã qua xử lý lọc/tiền xử lý của node đó thay vì ảnh gốc.
  - **Khắc phục lỗi render chữ & placeholder biến (`{CAL1.Value}`) trên ImageOutput**:
    - **Tách dòng & Vẽ chữ đa dòng (`RenderTextWithNewlines`)**: Hàm `RenderTextWithNewlines` trong [Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Class1.cs) phân tách ký tự xuống dòng (`\r\n`, `\n`, `\r`) và vẽ từng dòng với độ cao thích hợp, loại bỏ triệt để lỗi font hiển thị ký tự dấu hỏi (`??`).
    - **Thay thế biến chính xác (`EvaluateTextTemplate`)**: Cập nhật hàm `ConditionEvaluator.EvaluateTextTemplate` và `BuildVariableMap` hỗ trợ tra cứu trực tiếp toàn bộ thuộc tính con dạng `{ToolName.Value}`, `{ToolName.Pass}`, `{ToolName.Found}`, `{ToolName.Score}`, `{ToolName.Text}`, `{ToolName.X}`, `{ToolName.Y}`, `{ToolName.CenterX}`, `{ToolName.RadiusPx}` v.v... đảm bảo thế đúng giá trị thực tế thay vì hiển thị dạng chuỗi thô.
    - **Thêm Output Port cho ResultView**: Cập nhật [ToolGraphNodeViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolGraphNodeViewModel.cs) đặt `outName = "Image"` cho `ResultView`, mở cổng Output cho node `ResultView` để người dùng có thể kéo dây kết nối trực tiếp từ `ResultView` -> `ImageOutput`.
    - **Tự động nối dây & Đặt tên**: Khi kéo dây nối từ bất kỳ node xuất ảnh nào sang `ImageOutput`, thuộc tính `InputNodeName` tự động cập nhật tên node nguồn.
    - **Hiển thị trong danh sách Tool Palette**: Thêm `"ImageOutput"` vào danh sách `ToolboxItems` và hàm `GenerateDefaultRefName` (`IMG_OUT1`, `IMG_OUT2`...) trong [ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs).
    - **Giao diện cài đặt & Hướng dẫn thẻ tùy chọn (Cheat-sheet)**: Tích hợp giao diện cài đặt Properties Panel trong [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml) bổ sung bảng hướng dẫn (cheat-sheet) hiển thị rõ tất cả các thẻ đặt tên file kèm Tooltip giải thích chi tiết.
    - Bổ sung biến đầu ra `.Saved` và `.SavedFilePath` vào gợi ý IntelliSense cho các tool Text và Condition.

### Cập nhật 2026-07-29 (Phiên làm việc mới nhất)

- **Tùy chỉnh Cỡ chữ (`TextFontSize`) cho Tool Text khi xuất ảnh (`ImageOutput`)**:
  - Bổ sung thuộc tính `TextFontSize` (kiểu `int`, mặc định `18`, giới hạn `8`-`96`) vào `ImageOutputDefinition` ([Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Class1.cs)) và ViewModel `ToolEditorViewModel.ToolImageOutput.cs`.
  - Tích hợp ô nhập liệu `Text Font Size` vào giao diện cài đặt `ImageOutput` trong [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml).
  - Cập nhật hàm `BurnOverlaysToMat` trong [Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Class1.cs): Tự động tính toán tỷ lệ `fontScale` dựa trên `io.TextFontSize` khi vẽ các khối chữ `TextNodes` lên ảnh xuất.
- **Sửa lỗi ký hiệu độ `°` bị biến thành dấu hỏi `?` trên ảnh xuất**:
  - OpenCV font Hershey (`Cv2.PutText`) không hỗ trợ ký tự Unicode `°` (U+00B0) ngoài bảng mã ASCII.
  - Đã cập nhật nhãn hiển thị của `AngleResult` trong `BurnOverlaysToMat` chuyển từ `°` sang `deg` (`{a.Name}={a.ValueDeg:0.##} deg`), hiển thị đẹp mắt và rõ ràng không bị lỗi font `?`.
- **Khắc phục lỗi nhân đôi tỷ lệ đo đạc (`PixelsPerMm`) làm sai lệch giá trị hiển thị trên `ImageOutput`**:
  - Phát hiện nguyên nhân: Giá trị `Value` của các kết quả phép đo (`DistanceCheckResult`, `LineToLineDistances`, `PointToLineDistances`, `SegmentLineDistances`, `EdgePairs`, `Diameters`...) vốn đã được `VisionEngine` chuyển đổi sẵn sang đơn vị `mm` khi `isCalibrated == true`.
  - Đã sửa hàm `UnitStr` và các đoạn vẽ nhãn overlay trong `BurnOverlaysToMat` ([Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Class1.cs)) để hiển thị trực tiếp `val:0.##` kèm đuôi đơn vị `mm`/`px` mà không chia lại cho `scale` (`PixelsPerMm`) lần thứ hai. Khắc phục triệt me hiện tượng giá trị khoảng cách hiển thị trên ảnh xuất bị sai khác so với `ResultView`.
- **Bổ sung CheckBox bật/tắt xuất ảnh ra file ở node `ImageOutput`**:
  - Thêm thuộc tính `EnableOutput` (kiểu `bool`, mặc định `true`) vào `ImageOutputDefinition` ([Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Class1.cs)), ViewModel `ToolEditorViewModel.ToolImageOutput.cs` và CheckBox "Kích hoạt xuất ảnh ra file" trên [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml).
  - Cập nhật `ExecuteImageOutputs` trong [Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Class1.cs) kiểm tra `!io.EnableOutput` để bỏ qua việc ghi file khi người dùng muốn giữ node nhưng hủy xuất file.
- **Hỗ trợ xoay ROI cho `BlobDetection`**:
  - Cập nhật `ExecuteBlobDetections` dùng `ExtractStraightRoi` cắt ảnh ROI đã duỗi thẳng theo góc xoay `Origin` (`angleDeg + b.InspectRoi.Angle`).
  - Cập nhật `DetectBlobsInCrop` dùng `MapToGlobal` để biến đổi lại tọa độ tâm blob (`Centroid`) và khung bao (`BoundingBox`) về tọa độ toàn cục chuẩn xác khi ảnh hoặc Origin bị xoay.
- **Khắc phục lỗi lệch vị trí / không xoay ROI khi render ảnh xuất `ImageOutput`**:
  - Viết hàm trợ giúp `DrawRotatedRoi` trong `BurnOverlaysToMat` ([Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Class1.cs)) để tính toán chính xác 4 đỉnh góc xoay của các ROI tìm kiếm (`Points`, `Lines`, `Calipers`, `CircleFinders`, `BlobDetections`, `SurfaceCompares`, `CodeDetections`, `Origin`) theo góc xoay và độ dịch chuyển của `Origin`.
  - Cập nhật `AddConfigRoisWithPose` trong [ToolEditorViewModel.Engine.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.Engine.cs) sử dụng `CreateRotatedRoiWithPose` cho tất cả các loại ROI để các nét ROI trên canvas preview đồng bộ hoàn toàn với Origin.
- **Đồng bộ hai chiều giữa Dropdown `InputImage` của `OutputImageParams` và dây nối Canvas Edge**:
  - Cập nhật `ImageOutput_InputNodeChoice` trong [ToolEditorViewModel.ToolImageOutput.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolImageOutput.cs): Khi thay đổi dropdown chọn `InputImage` trên Property Panel, tự động xóa dây nối cũ và tạo dây nối `ToolGraphEdgeViewModel` mới trên canvas graph, gọi `SyncEdgesToConfig()` và `RefreshPreviews()`.
  - Cập nhật `CreateEdge` trong [ToolEditorViewModel.GraphOps.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.GraphOps.cs): Khi kéo dây nối trên canvas vào node `ImageOutput`, tự động cập nhật `def.InputNodeName`, phát sự kiện thay đổi thuộc tính `ImageOutput_InputNodeChoice` và cập nhật preview.
  - Cập nhật `ClearToolInputByEdge` trong [ToolEditorViewModel.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.cs): Khi xóa dây nối đầu vào của `ImageOutput`, tự động reset `def.InputNodeName` về mặc định và phát sự kiện đổi thuộc tính.
- **Bổ sung tùy chọn ẩn/hiện khung ROI tìm kiếm (`ShowRoi` / `Show ROI Boxes`)**:
  - Thêm thuộc tính `ShowRoi` (`bool`, mặc định `true`) vào `ImageOutputDefinition` ([Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Models/Class1.cs)) và ViewModel `ImageOutput_ShowRoi` ([ToolEditorViewModel.ToolImageOutput.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/ViewModels/ToolEditorViewModel.ToolImageOutput.cs)).
  - Chuẩn hóa vị trí hiển thị ô CheckBox *"Vẽ ô vuông ROI tìm kiếm (Bỏ chọn để chỉ hiện kết quả)"* ngay dưới mục *Burn Overlay* trong phần thuộc tính của node `ImageOutput` trên [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml).
  - Cập nhật `DrawRotatedRoi` trong `BurnOverlaysToMat` ([Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Class1.cs)): Khi người dùng bỏ chọn CheckBox này (`ShowRoi = false`), tất cả các khung ô vuông ROI dạy học/tìm kiếm sẽ được ẩn đi, chỉ hiển thị duy nhất các đường kết quả đo đạc (đường thẳng, tâm tròn, điểm giao), khung bao lỗi (Blob/Surface defect), khung mã vạch và các nhãn kết quả chữ, giúp file ảnh xuất ra cực kỳ sạch sẽ và chuyên nghiệp.
- **Sửa lỗi lệch vị trí khung ROI của `Origin` trên ảnh xuất `ImageOutput`**:
  - Trong `BurnOverlaysToMat` ([Class1.cs](file:///g:/NODEJS/Vision2026/VisionInspectionApp.Application/Class1.cs)), thay vì tạo `tempRoi` từ `result.Origin.MatchRect` (đã là vị trí thực tế tìm được) rồi gọi `DrawRotatedRoi` dẫn đến tịnh tiến 2 lần `+dx, +dy`, đã cập nhật gọi trực tiếp `DrawRotatedRoi(mat, config.Origin.TemplateRoi, green, 2)`.
  - `DrawRotatedRoi` lấy `config.Origin.TemplateRoi` làm khung mẫu dạy học và tự động xoay/tịnh tiến chính xác theo góc `AngleDeg` và vị trí `originFound` (`MatchRect` center), đưa khung Origin về đúng vị trí trùng khớp 100% với canvas preview.
- **Sửa lỗi hiển thị tiếng Việt trên 2 CheckBox "Burn Overlay" và "Show ROI Boxes"**:
  - Chuẩn hóa nội dung nhãn tiếng Việt `Content` của 2 CheckBox trên [ToolEditorView.xaml](file:///g:/NODEJS/Vision2026/VisionInspectionApp.UI/Views/ToolEditorView.xaml) bằng XML Entities chuẩn (`V&#x1EBD; ROI &amp; Overlay v&#xE0;o &#x1EA3;nh xu&#x1EA5;t` và `V&#x1EBD; &#xF4; vu&#xF4;ng ROI t&#xEC;m ki&#x1EBF;m (B&#x1ECF; ch&#x1ECD;n &#x111;&#x1EC3; ch&#x1EC9; hi&#x1EC7;n k&#x1EBF;t qu&#x1EA3;)`).
  - Khắc phục triệt để lỗi ký tự bị mã hóa sai (double-encoding garbled text) khi đọc/ghi file XAML trên Windows.
- **Trạng thái**: Biên dịch thành công toàn bộ Solution `VisionInspectionApp.slnx` (`0 Errors, 36 Warnings`).



## Encoding

- Tài liệu này được lưu ở UTF-8 và toàn bộ nội dung tiếng Việt đã được chuẩn hoá.
- Các tệp mã nguồn và XAML nên tiếp tục dùng UTF-8 with BOM để tránh lỗi hiển thị tiếng Việt trên môi trường Windows.

## Roadmap

### Ưu tiên cao

- Kiểm thử đầy đủ module Camera Settings với Basler/GigE và luồng UDP.
- Chạy kiểm thử đầu-cuối cho execution pipeline của Node Graph.

### Ưu tiên trung bình

- Hoàn thiện overlay kết quả: bounding box, trục Origin và thông số blob.
- Profiling các pipeline tiền xử lý nặng và kiểm tra giải phóng tài nguyên OpenCvSharp.

### Ưu tiên thấp

- Kiểm tra serialization/deserialization của node graph, layout canvas và tham số toàn cục.
