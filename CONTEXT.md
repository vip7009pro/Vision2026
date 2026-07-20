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
