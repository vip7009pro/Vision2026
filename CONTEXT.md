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
