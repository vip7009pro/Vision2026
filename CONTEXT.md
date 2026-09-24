# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC & Database).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, và nạp bản vẽ kỹ thuật PDF để dạy học (teach) và kiểm tra tự động khớp 100% với sản phẩm thật.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch Release**: 0 Errors, toàn bộ hệ thống kiểm thử tự động PASSED 100%.

## 3. Bản Vẽ PDF: Nhập Tỉ Lệ PixelsPerMm, Căn Lề, Pan & Xoay Khớp Khung Hình (Task 368)
- **Nhập tỉ lệ PixelsPerMm linh hoạt**:
  - Hỗ trợ nhập tay trực tiếp giá trị `PixelsPerMm` trên panel thuộc tính ImageSource.
  - Tự động fallback lấy từ Job/Global Calib nếu để trống hoặc bằng 0.
  - Cung cấp nút `⚡ Từ Calib` (đồng bộ từ Job/Global vào PDF) và `💾 Cho Job` (áp dụng ngược lại cho Job).
  - Tự động hiển thị DPI tương đương theo công thức $\text{DPI} = \text{PixelsPerMm} \times 25.4$.
- **Căn lề, Pan dịch chuyển & Xoay bản vẽ (Rotation 90°)**:
  - Khắc phục triệt để tình huống bản vẽ PDF đứng (portrait) nhưng Camera ngang (landscape) bị mất phần trên/dưới.
  - Nút `🔄 Xoay 90°` xoay luân phiên $0^\circ \rightarrow 90^\circ \rightarrow 180^\circ \rightarrow 270^\circ$ bằng `Cv2.Rotate`.
  - Bộ chọn Căn Lề: `Top-Center`, `Center`, `Bottom-Center`, `Top-Left`, `Top-Right`, `Custom`.
  - Cụm điều hướng Pan: Nhập trực tiếp tọa độ offset $X, Y$ (pixel) hoặc dùng các nút `⬅`, `➡`, `⬆`, `⬇` (bước nhảy 200px) và nút `🎯` Reset về $(0, 0)$.
  - Tự động kết xuất lại và cập nhật xem trước ngay lập tức khi thay đổi.
- **Kiểm thử tự động**:
  - `PdfSourceTests.cs` (6 tests bao gồm Test 6 Pan & Xoay 90°): Toàn bộ test suite `TestExtractApp` đạt **100% PASSED**.

## 4. Các sự kiện & thay đổi gần đây
- Task 368: Nhập tỉ lệ PixelsPerMm (nhập tay & đồng bộ 2 chiều Calib), Pan dịch chuyển bản vẽ và Xoay 90° trên Canvas Camera.
- Task 367: Tự động khớp bản vẽ PDF 1:1 theo Camera (20MP & tùy biến) & Khung hình cảm biến.
- Task 366: Khắc phục triệt để lỗi Nền Đen (Unsafe Alpha Blending) & Ảnh Vỡ (300 DPI chuẩn công nghiệp).
- Task 365: Bổ sung chế độ nguồn ảnh từ bản vẽ PDF (`ImageSourceType.Pdf`) cho Tool Editor.
- Task 364: Kế toán thời gian tường minh trong Tool Editor (Timing Breakdown động).
- Task 363: Nút "Đóng Job" và Xóa chọn lọc từng dòng trong Lịch sử quét mã OQC.
- Task 362: Nút Reset phiên & hàng đợi + sửa lỗi tương phản giao diện tối.
