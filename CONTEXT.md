# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC & Database).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, nạp bản vẽ kỹ thuật PDF, quản lý CSDL MES/ERP và giao tiếp PLC đa hãng.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`).
- **Kiểm thử tự động**: PASSED 100% (10/10 tests Calib 1D & 2D độc lập X/Y, toàn bộ regression suite vượt qua).

## 3. Hệ Thống Calib 2 Trục Độc Lập X & Y (Task 372)
- **Vấn đề giải quyết**: Khi đo phôi chữ nhật (chiều dài >> chiều rộng), do ống kính méo quang học phi tuyến hoặc góc nghiêng camera (perspective foreshortening), nếu dùng chung 1 hệ số calib thì chiều ngang PASS nhưng chiều dọc bị NG (hoặc ngược lại).
- **Cơ chế kỹ thuật**:
  - Hỗ trợ `PixelsPerMmX` và `PixelsPerMmY` trong `VisionConfig`. Tự động fallback tương thích ngược 100% sang `PixelsPerMm` nếu chưa thiết lập.
  - Quy đổi khoảng cách vector 2D chuẩn xác: $\text{dist}_{\text{mm}} = \sqrt{(\Delta x / \text{ppm}_X)^2 + (\Delta y / \text{ppm}_Y)^2}$.
  - Tự động nhận diện hướng đo $\theta = \text{atan2}(|\Delta y|, |\Delta x|)$: $\theta < 45^\circ \rightarrow$ gợi ý Trục X, $\theta \ge 45^\circ \rightarrow$ gợi ý Trục Y, đường tròn $\rightarrow$ đồng hướng 2 trục.
  - Dialog chọn trục `CalibAxisSelectionDialog` hiển thị góc đo, độ lệch % và cảnh báo nếu lệch > 10%.
  - Đồng bộ tức thì toàn bộ engine đo lường (`CheckDistance`, `LineToLineDistances`, `PointToLineDistances`, `SegmentLineDistances`, `EdgePairs`, `EdgePairDetections`, `LinePairDetections`, `Diameters`) và toàn bộ overlays.

## 4. Các sự kiện & thay đổi gần đây
- Task 372: Nâng cấp toàn diện hệ thống Calib sang cơ chế 2 trục độc lập ($X$ và $Y$), giải quyết triệt để lỗi đo phôi chữ nhật.
- Task 371: Nút "Set as Calib Factor" trong Properties của các tool đo (Distance, SegmentLineDistance, LineLineDistance, EdgePairDetect, Circle Finder, Diameter).
- Task 370: Tự động khớp ComboBox CSDL OQC & Trung tâm Xuất/Nạp toàn bộ cấu hình hệ thống (1-click migrate).
- Task 369: Cải thiện nút Pan hiển thị rõ nét & sửa lỗi Origin Train Template từ nguồn PDF báo "Chưa lưu template".
- Task 368: Nhập tỉ lệ PixelsPerMm, Căn lề, Pan dịch chuyển & Xoay bản vẽ 90° trên Canvas Camera.
- Task 367: Tự động khớp bản vẽ PDF 1:1 theo Camera & Khung hình cảm biến.
- Task 366: Khắc phục triệt để lỗi Nền Đen & Ảnh Vỡ khi trích xuất bản vẽ PDF.
- Task 365: Bổ sung chế độ nguồn ảnh từ bản vẽ PDF (`ImageSourceType.Pdf`) cho Tool Editor.
- Task 364: Kế toán thời gian tường minh trong Tool Editor (Timing Breakdown động).
