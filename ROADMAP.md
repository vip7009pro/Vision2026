# ROADMAP.md — Lộ trình phát triển & Trạng thái nhiệm vụ

## Các nhiệm vụ gần đây & Đang triển khai

- [x] Task 376: Tách ô nhập SỐ ĐO THỰC TẾ (mm) thành field riêng cạnh nút "Đặt Hệ Số Calib" để không ghi đè Spec.
- [x] Task 377: Sửa lỗi Copy/Paste nhiều node trong Tool Editor & thêm nút Auto Tune cho công cụ nhận diện.
- [x] Task 378: Tối ưu hiệu năng/UX Tool Editor (cache preview Final, chống rò rỉ RAM Mat, thuật toán Coordinate Descent).
- [x] Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN (`Parallel.For`, cap 4-8 luồng).
- [x] Task 380: OQC Scanner thao tác 1 nút Space / Ctrl+F8 luân phiên & Bật tắt Crosshair căn tâm trong Cửa sổ Cấu hình Camera/Đèn Job.
- [x] Task 381: Rà soát & Chuẩn hóa Handshake PLC FX5U, Cơ chế Dừng NG Ngoài Buồng & Xem Lại Ảnh 20 Nấc:
  - [x] Rà soát và sửa máy trạng thái Handshake FX5U: Khắc phục lỗi đọc/ghi vùng `X`/`Y`, chuyển sang bit nội bộ `M101`..`M105`, `M10`, `M11` giao thức MC Protocol.
  - [x] Nâng cấp `IndustrialHandshakeStateMachine.cs`: Dynamic Tag Resolution tự động tương thích cả bit `M101`..`M105` và tag chuẩn `Y1`..`Y5`.
  - [x] Lập trình `POU_04_ShiftRegister_Reject.st`: Hàng đợi tracking 20 phôi FIFO độc lập (In-Flight Pipelined Tracking). Hàng NG không dừng trong buồng; các phôi kế tiếp vẫn trigger kiểm tra bình thường; dừng chính xác phôi NG tại trạm chỉ định ngoài buồng bằng Stopper `Y21`, còi đèn `Y22`, dừng băng tải `Y0` (tùy chọn `M120`), reset tự động qua nút `X4`.
  - [x] Cung cấp bản đồ bộ nhớ và sơ đồ Ladder logic mạng chi tiết trong `Ladder_Diagram_Visual.md` và `DeviceComments_GXWorks.csv`.
  - [x] Giãn kích thước 20 nấc sản phẩm gần nhất lên `12x18px`, hỗ trợ hover và click chuột/cảm ứng mượt mà.
  - [x] Bấm vào từng nấc trên thanh 20 nấc để xem lại ảnh OK/NG: Tự động phân giải ảnh từ tệp trên đĩa hoặc ảnh snapshot trong RAM khi Image Output đặt `OnFail`.
  - [x] Thêm banner màu hổ phách khi xem ảnh lịch sử kèm nút "📁 Mở thư mục ảnh" và nút "✕ Quay lại Live".
  - [x] Kiểm thử toàn bộ regression test suite: 100% test PASSED.

## Định hướng tiếp theo
- [ ] Bổ sung tính năng tự động phát hiện khung tên bản vẽ kỹ thuật (Title Block) trên PDF.
- [ ] Tích hợp trích xuất lớp vector nguyên bản từ PDF dạng DXF/SVG phục vụ so khớp đường biên CAD.
- [ ] Tối ưu hóa render đa luồng (Multi-threaded Rendering) cho tài liệu PDF kích thước lớn >50MB.
