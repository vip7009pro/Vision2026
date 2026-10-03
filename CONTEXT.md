# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC & Database).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, nạp bản vẽ kỹ thuật PDF, quản lý CSDL MES/ERP và giao tiếp PLC đa hãng.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (OQC 1-nút Space/Ctrl+F8, Job Camera Crosshair, 8/8 tests PDF, 6/6 tests Release Config, 12/12 Calib tests).

## 3. OQC Scanner 1 Phím & Crosshair Căn Tâm Job Camera (Task 380)
- **Thao tác 1 nút Space & Ctrl+F8 trên OQC Scanner**:
  - Khi đang ở chế độ Live View (`IsShowingLiveCamera = true`): phím `Space` hoặc `Ctrl + F8` kích hoạt **KIỂM TRA HÀNG** (RunJob hoặc ScanFromCamera). Sau khi kiểm tra, hệ thống dừng Live và hiển thị kết quả.
  - Khi đang ở chế độ hiển thị kết quả (`IsShowingLiveCamera = false`): bấm `Space` hoặc `Ctrl + F8` lập tức kích hoạt quay về **LIVE VIEW** (`EnableLiveCamera`).
  - Kịch bản phối hợp với F5: Nếu vừa bấm Space kiểm tra xong mà bấm `F5` về Live View rồi, thì lần bấm Space tiếp theo sẽ là **KIỂM TRA HÀNG**, không bị chuyển sang Live View nữa.
  - Phím `F5` giữ nguyên chức năng chuyển về Live View.
  - Hỗ trợ tổ hợp phím **`Ctrl + F8`** tương đương phím Space (tiện lợi cho bàn đạp chân Foot-Switch / Barcode Scanner macro).
- **Ô tích chọn Crosshair trong Cửa sổ Cấu hình Camera & Đèn cho Job (`JobCameraSettingsWindow`)**:
  - Bổ sung `ShowCrosshair` trong `JobCameraSettingsViewModel` (mặc định BẬT = `true`).
  - Giao diện Live Preview: tích hợp thanh điều khiển Floating góc trên bên phải gồm CheckBox `✛ Crosshair` nổi bật màu cyan `#00E5FF` và nút `🎯 Fit View`.
  - Giúp công nhân căn chỉnh sản phẩm vào tâm camera chuẩn xác trước khi lưu thông số cho Job.

## 4. Các sự kiện & thay đổi gần đây
- Task 380: OQC Scanner 1 nút Space / Ctrl+F8 luân phiên Kiểm tra & Live View (F5 giữ nguyên), Crosshair căn tâm mặc định bật cho Job Camera Settings.
- Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN (`Parallel.For`, cap 4-8 luồng).
- Task 378: Tối ưu hiệu năng/UX Tool Editor (cache preview Final, chống rò rỉ RAM Mat, thuật toán Coordinate Descent cho Auto Tune).
- Task 377: Sửa lỗi Copy/Paste nhiều node trong Tool Editor & thêm Preprocess Auto Tuner.
- Task 376: Ô nhập SỐ ĐO THỰC TẾ (mm) riêng cạnh nút "Đặt Hệ Số Calib" (không ghi đè Spec).
- Task 375: Kéo chuột trực tiếp trên Canvas xem trước để Pan vùng hiển thị PDF (60 FPS in-memory, HUD, Esc cancel).
- Task 374: Khắc phục triệt để mất cấu hình OTA, OQC Scanner & Database khi build Release (AppStoragePaths & MSBuild Sync).
- Task 373: Dải ô vuông Timing Breakdown nằm gọn trên 1 hàng ngang cuộn mượt mà.
- Task 372: Nâng cấp toàn diện hệ thống Calib sang cơ chế 2 trục độc lập ($X$ và $Y$).
