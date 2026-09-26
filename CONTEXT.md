# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC & Database).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, nạp bản vẽ kỹ thuật PDF, quản lý CSDL MES/ERP và giao tiếp PLC đa hãng.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (8/8 tests PDF Source, 6/6 tests Release Config, 10/10 Calib tests).

## 3. Thao Tác Kéo Chuột Trực Tiếp Trên Canvas Để Pan PDF (Task 375)
- **Tính năng mới**: Trong tab Tool Editor, ở tool `ImageSource` với nguồn bản vẽ kỹ thuật PDF, người dùng có thể kéo chuột trái trực tiếp trên Canvas xem trước (`PreviewImageViewer`) để dịch chuyển (Pan) bản vẽ vào đúng vị trí mong muốn trên khung hình cảm biến Camera.
- **Hiệu năng 60 FPS in-memory**:
  - `IPdfDocumentService.RenderRotatedPage` và `PlacePageOnCameraCanvas`: Trong suốt quá trình rê chuột, chỉ thực hiện phép ghép Rect OpenCV in-memory (<1ms), không gọi lại PDFium và không ghi file đĩa PNG liên tục.
  - Cập nhật tức thời số Offset X và Y trong bảng thuộc tính thời gian thực.
  - Khi nhả chuột (MouseUp): Tự động chốt vị trí, kết xuất ảnh chuẩn tỉ lệ lưu đĩa và nạp Teach cho toàn Job.
  - Phím `Escape`: Hủy thao tác kéo và hoàn trả lại vị trí offset ban đầu.
- **Giao diện & Cử chỉ**:
  - `ImageViewerControl`: Hỗ trợ `EnablePdfPan`, `PdfPanChangedCommand`, con trỏ `SizeAll` / `Hand` và hiển thị HUD hướng dẫn trên `PART_InfoText`.
  - Nút ToggleButton `🖐️ Kéo Pan` trong panel thuộc tính Pan và `🖐️ Pan PDF` trên thanh công cụ xem trước Preview Header.

## 4. Các sự kiện & thay đổi gần đây
- Task 375: Kéo chuột trực tiếp trên Canvas xem trước để Pan vùng hiển thị PDF (60 FPS in-memory, HUD, Esc cancel).
- Task 374: Khắc phục triệt để mất cấu hình OTA, OQC Scanner & Database khi build Release (Kiến trúc 2 tầng AppStoragePaths & MSBuild Sync).
- Task 373: Dải ô vuông Timing Breakdown nằm gọn trên 1 hàng ngang có thể cuộn ngang mượt mà bằng thanh cuộn hoặc con lăn chuột.
- Task 372: Nâng cấp toàn diện hệ thống Calib sang cơ chế 2 trục độc lập ($X$ và $Y$), giải quyết triệt để lỗi đo phôi chữ nhật.
- Task 371: Nút "Set as Calib Factor" trong Properties của các tool đo.
- Task 370: Tự động khớp ComboBox CSDL OQC & Trung tâm Xuất/Nạp toàn bộ cấu hình hệ thống (1-click migrate).
- Task 369: Cải thiện nút Pan hiển thị rõ nét & sửa lỗi Origin Train Template từ nguồn PDF.
