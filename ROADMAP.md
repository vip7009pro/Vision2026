# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

## Các nhiệm vụ gần đây & Đang triển khai

- [x] Task 372: Nâng cấp hệ thống Calib sang cơ chế 2 trục độc lập ($X$ và $Y$), giải quyết triệt để lỗi đo phôi chữ nhật.
- [x] Task 373: Tối ưu khối Timing Breakdown Tool Editor thành 1 hàng ngang cuộn ScrollViewer.
- [x] Task 374: Khắc phục triệt để mất cấu hình OTA, OQC Scanner, Database & Toàn bộ cấu hình hệ thống khi build Release (AppStoragePaths & MSBuild Sync).
- [x] Task 375: Bổ sung thao tác kéo chuột trực tiếp trên Canvas xem trước để Pan vùng hiển thị PDF (60 FPS in-memory, HUD, Esc cancel).
- [x] Task 376: Tách ô nhập SỐ ĐO THỰC TẾ (mm) thành field riêng cạnh nút "Đặt Hệ Số Calib" để không ghi đè Spec (nhận dấu phẩy `50,02`, tự xóa khi đổi tool).
- [x] Task 377: Sửa lỗi Copy/Paste nhiều node trong Tool Editor & thêm nút Auto Tune cho công cụ nhận diện (PreprocessAutoTuner).
- [x] Task 378: Tối ưu hiệu năng/UX Tool Editor (cache preview Final, chống rò rỉ RAM Mat, thuật toán Coordinate Descent cho Auto Tune).
- [x] Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN (`Parallel.For`, cap 4-8 luồng, đếm tiến trình Interlocked).
- [x] Task 380: OQC Scanner thao tác 1 nút Space / Ctrl+F8 luân phiên & Bật tắt Crosshair căn tâm trong Cửa sổ Cấu hình Camera/Đèn Job:
  - [x] Thêm `TriggerInspectOrLiveCommand` vào `OqcScannerViewModel`:
    - Khi đang Live View (`IsShowingLiveCamera = true`): phím `Space` hoặc `Ctrl + F8` kích hoạt kiểm tra hàng (`RunJob` hoặc `ScanFromCamera`).
    - Khi đang hiển thị kết quả kiểm tra (`IsShowingLiveCamera = false`): bấm `Space` hoặc `Ctrl + F8` kích hoạt quay lại Live View (`EnableLiveCamera`).
    - Kịch bản phối hợp: nếu vừa kiểm tra xong mà bấm `F5` về Live View rồi, thì lần bấm Space tiếp theo sẽ là kiểm tra hàng (không bị nhảy về Live View lần nữa).
  - [x] Phím `F5` giữ nguyên chức năng chuyển về Live View.
  - [x] Thêm tổ hợp phím `Ctrl + F8` với chức năng tương tự Space trên toàn màn hình OQC Scanner.
  - [x] Cập nhật giao diện: Tooltip, ScanButtonText và PreviewHeaderTitle gợi ý phím tắt `SPACE / Ctrl+F8`.
  - [x] Cửa sổ Cấu hình Camera & Đèn cho Job (`JobCameraSettingsWindow`):
    - Bổ sung `ShowCrosshair` trong `JobCameraSettingsViewModel` (mặc định BẬT = `true`).
    - Gắn `ShowCrosshair="{Binding ShowCrosshair}"` vào `ImageViewerControl`.
    - Thêm ô tích chọn CheckBox `✛ Crosshair` (màu cyan `#00E5FF`) và nút `Fit View` trên thanh Floating Panel góc trên bên phải Live Preview.
  - [x] Bổ sung kiểm thử tự động `TestOqcTriggerInspectOrLiveToggleSequence` và `TestJobCameraSettingsCrosshairDefaultAndToggle` — toàn bộ test suite PASS 100%.

## Định hướng tiếp theo
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
- [ ] Tối ưu hóa render đa luồng (Multi-threaded Rendering) cho tài liệu PDF kích thước lớn >50MB.
