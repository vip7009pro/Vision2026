# Hướng Dẫn Cài Đặt PHP Script Lên Máy Chủ XAMPP

Script `vision_upload.php` hỗ trợ ứng dụng Vision thực hiện các chức năng:
1. **Tải lên ảnh Teach Image** từ máy Vision OQC lên máy chủ (`action=upload_image`).
2. **Tải lên tệp Job (.job)** từ máy Vision lên máy chủ (`action=upload_job`).
3. **Kiểm tra kết nối Server** từ ứng dụng (`action=ping`).

---

## 🛠️ Các Bước Cài Đặt Trên Máy Chủ XAMPP

1. Mở thư mục gốc của XAMPP trên máy chủ (mặc định là `C:\xampp\htdocs\`).
2. Copy tệp `vision_upload.php` vào thư mục `C:\xampp\htdocs\`.
3. Khởi động dịch vụ **Apache** trong bảng điều khiển **XAMPP Control Panel**.
4. Kiểm tra trên trình duyệt:
   - Truy cập: `http://localhost/vision_upload.php?action=ping` (hoặc `http://<IP_MAY_CHU>/vision_upload.php?action=ping`)
   - Kết quả hiển thị JSON:
     ```json
     {
         "success": true,
         "message": "CMS VINA Vision Upload Server is ONLINE.",
         "server_time": "2026-08-31 10:00:00",
         "base_url": "http://localhost"
     }
     ```
5. Trong ứng dụng Vision (Cửa sổ **Cấu hình OQC Scanner & Tra cứu Database**):
   - Nhập **Địa chỉ máy chủ**: `http://<IP_MAY_CHU>/vision_upload.php`
   - Bấm nút **⚡ Kiểm tra kết nối** để xác nhận kết nối thành công.

---

## 📂 Cấu Trúc Thư Mục Upload Tự Động Sinh Ra Trên Server:
```text
C:\xampp\htdocs\
├── vision_upload.php
└── uploads\
    ├── teach_images\     (Lưu trữ các ảnh mẫu teaching chụp từ máy OQC)
    └── jobs\             (Lưu trữ các tệp .job được đồng bộ)
```
