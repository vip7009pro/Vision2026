# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC & Database).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, nạp bản vẽ kỹ thuật PDF, quản lý CSDL MES/ERP và giao tiếp PLC đa hãng.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`).
- **Kiểm thử tự động**: PASSED 100% (6/6 tests ToolCalibFactor mới, 6/6 tests Backup & OqcDbMatch, cùng toàn bộ regression tests).

## 3. Hiệu Chuẩn Tỉ Lệ Pixels/Mm Trực Tiếp Từ Tool Đo Cho Job (Task 371)
- **Vấn đề giải quyết**: Khắc phục sai số do ống kính quang học méo hình (hàng nhỏ đo chuẩn, hàng to bị lệch do biến dạng phi tuyến ra vùng rìa cảm biến), hỗ trợ hiệu chuẩn thích ứng riêng biệt theo từng Job.
- **Cơ chế hoạt động**:
  - Thêm nút `🎯 Đặt làm Hệ Số Calib (Set as Calib Factor)` trực tiếp trong bảng thuộc tính của các công cụ đo lường: `Distance`, `SegmentLineDistance`, `LineLineDistance`, `PointLineDistance`, `EdgePairDetect`, `EdgePair`, `Diameter`, và `CircleFinder`.
  - Tự động trích xuất khoảng cách đo pixel subpixel thực tế (`measuredPx`) và kích thước danh định (`Nominal mm`).
  - Đối với `CircleFinder`: Bổ sung trường `NominalDiameter` và ô nhập `Nom Dia (mm)` trực tiếp trên giao diện để hỗ trợ lấy mẫu calib từ đường tròn/lỗ chuẩn.
  - Tính toán tỉ lệ mới: `newPixelsPerMm = measuredPx / nominalMm`.
  - Hiển thị hộp thoại xác nhận chuyên nghiệp: Báo cáo công cụ, kích thước pixel, kích thước nominal, hệ số cũ -> mới, % độ lệch và cảnh báo vàng nếu chênh lệch > 10%.
  - Khi xác nhận: Tự động ghi vào `VisionConfig.PixelsPerMm`, kích hoạt AutoSave và gọi `RunFlow()` để toàn bộ các công cụ đo lường trong Job cập nhật nóng tức thì sang mm.

## 4. Các sự kiện & thay đổi gần đây
- Task 371: Nút "Set as Calib Factor" trong Properties của các tool đo (Distance, SegmentLineDistance, LineLineDistance, EdgePairDetect, Circle Finder, Diameter).
- Task 370: Tự động khớp ComboBox CSDL OQC & Trung tâm Xuất/Nạp toàn bộ cấu hình hệ thống (1-click migrate).
- Task 369: Cải thiện nút Pan hiển thị rõ nét & sửa lỗi Origin Train Template từ nguồn PDF báo "Chưa lưu template".
- Task 368: Nhập tỉ lệ PixelsPerMm, Căn lề, Pan dịch chuyển & Xoay bản vẽ 90° trên Canvas Camera.
- Task 367: Tự động khớp bản vẽ PDF 1:1 theo Camera & Khung hình cảm biến.
- Task 366: Khắc phục triệt để lỗi Nền Đen & Ảnh Vỡ khi trích xuất bản vẽ PDF.
- Task 365: Bổ sung chế độ nguồn ảnh từ bản vẽ PDF (`ImageSourceType.Pdf`) cho Tool Editor.
- Task 364: Kế toán thời gian tường minh trong Tool Editor (Timing Breakdown động).
