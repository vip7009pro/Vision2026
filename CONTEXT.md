# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC & Database).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, nạp bản vẽ kỹ thuật PDF, quản lý CSDL MES/ERP và giao tiếp PLC đa hãng.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (8/8 tests PDF Source, 6/6 tests Release Config, 12/12 Calib tests).

## 3. Ô Nhập Số Đo Thực Tế Riêng Cho Hiệu Chuẩn Calib (Task 376)
- **Vấn đề**: Trước đây muốn hiệu chuẩn, kỹ sư phải nhập số đo thực tế của cữ mẫu vào chính ô `Nominal` (phần Spec) rồi bấm `🎯 Đặt làm Hệ Số Calib`; rất nhiều trường hợp quên nhập lại Spec ban đầu => Job bị sai kích thước chuẩn/dung sai (báo NG giả).
- **Giải pháp**: Thêm ô nhập riêng `Đo thực tế (mm)` nằm ngay BÊN TRÁI nút `🎯 Đặt Hệ Số Calib`, hoàn toàn tách biệt khỏi ô Nominal:
  - Áp dụng cho cả 3 vị trí nút Calib: khối `Distance Spec` dùng chung (Distance, SegmentLineDistance, LineLineDistance, PointLineDistance, EdgePair, EdgePairDetect, Diameter), khối `Circle Finder` (nhãn `Đo thực tế Ø (mm)`) và khối `Edge Pair Detect`.
  - Dòng ghi chú ✅ hiện ngay dưới nút để xác nhận: "Calib theo số đo thực tế X mm — ô Nominal (Spec) được giữ nguyên".
- **Quy tắc tính toán** (`CalibFactorMath` - lớp toán học thuần, có test tự động):
  - Ưu tiên số đo thực tế; nếu ô để trống (hoặc dữ liệu không hợp lệ) mới dùng `Nominal`/`Nom Dia` trong Spec (tương thích ngược 100%).
  - Chấp nhận cả dấu chấm (`50.02`) lẫn dấu phẩy (`50,02`); tự chặn giá trị âm/rỗng/NaN.
  - `ppm = measuredPx / kích thước thực tế (mm)`.
- **An toàn thao tác**: ô "Đo thực tế (mm)" tự xóa khi chuyển sang công cụ khác; hộp thoại `CalibAxisSelectionDialog` hiển thị rõ nguồn kích thước (`Số đo THỰC TẾ (mm)` nổi màu cam hay `Nominal Spec`); Status Bar ghi rõ nguồn đã dùng.

## 4. Các sự kiện & thay đổi gần đây
- Task 376: Ô nhập SỐ ĐO THỰC TẾ (mm) riêng cạnh nút "Đặt Hệ Số Calib" (không ghi đè Spec), hỗ trợ nhập `50,02`, tự xóa khi đổi tool, 12/12 Calib tests PASS.
- Task 375: Kéo chuột trực tiếp trên Canvas xem trước để Pan vùng hiển thị PDF (60 FPS in-memory, HUD, Esc cancel).
- Task 374: Khắc phục triệt để mất cấu hình OTA, OQC Scanner & Database khi build Release (Kiến trúc 2 tầng AppStoragePaths & MSBuild Sync).
- Task 373: Dải ô vuông Timing Breakdown nằm gọn trên 1 hàng ngang có thể cuộn ngang mượt mà bằng thanh cuộn hoặc con lăn chuột.
- Task 372: Nâng cấp toàn diện hệ thống Calib sang cơ chế 2 trục độc lập ($X$ và $Y$), giải quyết triệt để lỗi đo phôi chữ nhật.
- Task 371: Nút "Set as Calib Factor" trong Properties của các tool đo.
- Task 370: Tự động khớp ComboBox CSDL OQC & Trung tâm Xuất/Nạp toàn bộ cấu hình hệ thống (1-click migrate).
- Task 369: Cải thiện nút Pan hiển thị rõ nét & sửa lỗi Origin Train Template từ nguồn PDF.
