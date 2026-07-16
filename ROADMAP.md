# ROADMAP.md

Lộ trình tích hợp tính năng Chụp ảnh từ camera và hỗ trợ các loại camera (USB, GigE, USB3 Vision):

- [x] Task 1: Tạo `DirectShowDeviceEnumerator` trong `VisionInspectionApp.UI` để quét chính xác camera USB với tên hiển thị đầy đủ.
- [x] Task 2: Cập nhật `CameraService` hỗ trợ:
  - Mở camera qua DirectShow API bằng OpenCV.
  - Mở camera công nghiệp qua Custom RTSP URL.
  - Hàm `CaptureSnapshotAsync()` chụp ảnh nhanh bất đồng bộ (tự động bật/tắt camera khi cần).
- [x] Task 3: Cập nhật Tab **Live Camera** (UI & ViewModel) để hiển thị danh sách camera thực tế và tùy chọn Custom RTSP.
- [x] Task 4: Cập nhật Tab **Tool Editor** (UI & ViewModel) để thêm nút "Capture Camera" và xử lý chụp ảnh.
- [x] Task 5: Cập nhật Tab **Calibration** (UI & ViewModel) để thêm nút "Capture Camera" và xử lý chụp ảnh.
- [x] Task 6: Cập nhật Tab **Manual Inspection** (UI & ViewModel) để thêm nút "Capture Camera" và xử lý chụp ảnh.
- [x] Task 7: Cập nhật Tab **Inspection** (UI & ViewModel) để thêm nút "Capture Camera" và xử lý chụp ảnh.
- [x] Task 8: Tạo tab **Camera Settings** hoàn chỉnh (chỉnh sáng tối, tương phản, đen trắng đầu vào gốc) và sửa lỗi crash/trống tab.
- [x] Task 9: Tách biệt cấu hình (khởi động sạch, clear bộ nhớ config cũ khi chuyển đổi để tránh ghi đè chéo).
- [x] Task 10: Tích hợp Live Preview thời gian thực trên Selected Node trong Tool Editor cùng công tắc chuyển đổi Live/Ảnh tĩnh.
- [x] Task 11: Thêm hiển thị OK/NG kích thước lớn ở tab Inspection và hiển thị lý do lỗi NG chi tiết ở góc phải.
- [x] Task 12: Đồng bộ hóa tiền xử lý (Preprocess) khi lưu template/teach trong Tool Editor với quá trình chạy thực tế.
- [x] Task 13: Sửa lỗi ShapeModelTrainer gây lệch tọa độ đặc trưng biên dẫn đến match score tụt thảm hại (NG oan).
- [x] Task 14: Sửa lỗi CameraService khởi động thất bại ngay khi bật app đối với virtual camera (như DroidCam) bằng cách thêm cơ chế Retry.
- [x] Task 15: Sửa lỗi lưu Config bị kẹp Tool "mồ côi" từ Config trước do Node bị xóa trên Graph nhưng không dọn dẹp trong Model.
- [x] Task 16: Nâng cấp UI Tab Tool Editor (Thêm icon List Tool, đưa Global Pre-processor ra dialog riêng, format gọn Properties Panel).
