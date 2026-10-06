# Vision Inspection App — Context & State

## 1. Giới thiệu dự án
Ứng dụng kiểm tra thị giác công nghiệp (.NET 8 WPF, MVVM, OpenCvSharp4, Docnet.Core PDFium, Hikrobot MVS SDK, Industrial PLC Mitsubishi FX5U/Siemens/Modbus & Database MES/ERP).
Hỗ trợ Camera GigE/USB3/USB/RTSP, nạp ảnh tệp/thư mục, bản vẽ kỹ thuật PDF, đồng bộ bắt tay và quản lý phôi nối tiếp In-Flight FIFO 24/7.

## 2. Trạng thái mã nguồn gần nhất
- **Phiên bản hiện tại**: .NET 8 WPF, x64/x86 Multi-targeting, C# 12.
- **Biên dịch**: 0 Errors toàn solution (`VisionInspectionApp.slnx`) cả Debug lẫn Release.
- **Kiểm thử tự động**: PASSED 100% (Toàn bộ test suite `TestExtractApp`, Continuous Flow, Pipeline Tracking, PLC Tags CSV, Handshake Bypass, OQC Scanner, Crosshair Overlay).

## 3. Hoàn thành Task 381: Bắt tay PLC FX5U, Dừng NG Ngoài Buồng & Xem Lại Ảnh 20 Nấc
1. **Rà soát & Chuẩn hóa Handshake PLC FX5U**:
   - Khắc phục lỗi PLC cũ (ghi vùng `X` và đọc vùng `Y`). Chuẩn hóa giao thức Ethernet MC Protocol/SLMP qua bit nội bộ `M` (`M101`..`M105` cho PC->PLC, `M10`, `M11` cho PLC->PC).
   - Nâng cấp `IndustrialHandshakeStateMachine.cs`: Tích hợp Dynamic Tag Resolution tự động nhận diện cả tag FX5U mới (`M101_VisionReady`..) và tag chuẩn (`Y1_VisionReady`..).
2. **Cơ chế Dừng NG ngoài buồng kiểm tra & Kiểm tra nối tiếp (In-Flight Pipelined Tracking)**:
   - Viết lại `POU_04_ShiftRegister_Reject.st`: Hàng đợi tracking 20 phôi FIFO độc lập theo dõi từ camera đến trạm dừng ngoài buồng.
   - Khi phát hiện NG, băng chuyền KHÔNG dừng trong buồng chụp. Các phôi tiếp theo đi vào buồng vẫn trigger và kiểm tra bình thường.
   - Khi phôi NG di chuyển tới cảm biến `X3` / tọa độ ngoài buồng, PLC kích hoạt Stopper `Y21`, còi đèn `Y22`, dừng băng tải `Y0` (tùy chọn `M120`). Công nhân lấy phôi nhấn nút `X4` để reset và tiếp tục vận hành tự động.
   - Cung cấp sơ đồ bộ nhớ, timing diagram và ladder diagram chi tiết trong `Ladder_Diagram_Visual.md`.
3. **Xem lại ảnh OK/NG trên thanh 20 nấc lịch sử (UI/UX)**:
   - Giãn kích thước 20 nấc lên `12x18px` với style viền và hiệu ứng tương tác chuột/cảm ứng mượt mà.
   - Bấm vào từng nấc hiển thị ảnh kiểm tra tương ứng: tự động phân giải ảnh file đĩa hoặc ảnh snapshot từ RAM (khi Image Output đặt `OnFail` cho phôi OK).
   - Thêm banner cảnh báo màu hổ phách phía trên khung Preview kèm nút "📁 Mở thư mục ảnh" và nút "✕ Quay lại Live".
   - Thêm nút "✕ Live" nhanh cạnh thanh 20 nấc.

## 4. Các sự kiện & thay đổi gần đây
- Task 381: PLC FX5U Handshake, cơ chế dừng NG ngoài buồng In-Flight Tracking & xem lại ảnh 20 nấc lịch sử.
- Task 380: OQC Scanner 1 nút Space / Ctrl+F8 luân phiên Kiểm tra & Live View, Crosshair căn tâm mặc định bật cho Job Camera Settings.
- Task 379: Tăng tốc Auto Tune bằng ĐA LUỒNG ĐA NHÂN (`Parallel.For`, cap 4-8 luồng).
- Task 378: Tối ưu hiệu năng/UX Tool Editor (cache preview Final, chống rò rỉ RAM Mat, thuật toán Coordinate Descent cho Auto Tune).
- Task 377: Sửa lỗi Copy/Paste nhiều node trong Tool Editor & thêm Preprocess Auto Tuner.
- Task 376: Ô nhập SỐ ĐO THỰC TẾ (mm) riêng cạnh nút "Đặt Hệ Số Calib" (không ghi đè Spec).
