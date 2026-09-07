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

# 🚀 Hướng Dẫn Cài Đặt OTA Server (ota_server.php)

Script `ota_server.php` hỗ trợ đầy đủ các chức năng:
1. **Kiểm tra trạng thái máy chủ OTA**: `action=ping`
2. **Cung cấp manifest version.json cho các máy IPC**: `action=get_version` (hoặc truy cập trực tiếp `http://<IP>/version.json`)
3. **Tiếp nhận gói nén zip và tự động cập nhật version.json**: `action=publish`
4. **Tải lên phân đoạn (Chunked Upload)**: `action=upload_chunk`
   - Vượt qua hoàn toàn mọi giới hạn `post_max_size` (kể cả 2M, 8M, 40M) trên mọi hosting (cPanel, DirectAdmin, Apache, LiteSpeed, Nginx).
   - Client tự động cắt nhỏ gói cập nhật thành các phân đoạn 6MB và gửi lần lượt, máy chủ tự động ghép nối lại nguyên vẹn và xác thực SHA-256.
5. **Hỗ trợ tùy chọn thư mục lưu trữ trên server** từ giao diện Desktop App (ví dụ: `uploads/ota_packages` hoặc `update`).

---

## 🛠️ Các Bước Cài Đặt OTA Server (Áp dụng cho XAMPP, cPanel, Apache, Web Hosting):

1. Mở thư mục gốc web trên máy chủ (ví dụ: `C:\xampp\htdocs\` hoặc `public_html/` trên cPanel).
2. Copy các tệp sau từ thư mục `ServerScripts/` lên máy chủ:
   - `ota_server.php` *(Bắt buộc — phiên bản mới nhất)*
   - `.htaccess` *(Tùy chọn cho Apache/LiteSpeed — tự động nâng giới hạn upload lên 1024M)*
   - `.user.ini` *(Tùy chọn cho PHP-FPM/cPanel — tự động nâng giới hạn upload lên 1024M)*
3. Kiểm tra trên trình duyệt hoặc dòng lệnh:
   - Truy cập: `https://<DOMAIN_HOAC_IP>/ota_server.php?action=ping`
   - Phản hồi mẫu:
     ```json
     {
         "success": true,
         "message": "CMS VINA OTA Update Server is ONLINE.",
         "server_time": "2026-09-07 16:00:00",
         "base_url": "https://cmsvina4285.com",
         "post_max_size": "40M",
         "upload_max_filesize": "40M",
         "chunked_upload_supported": true,
         "latest_version": null,
         "has_active_release": false
     }
     ```
4. Trong ứng dụng Vision (Cửa sổ **Cập Nhật Phần Mềm Từ Xa — OTA Update**):
   - **Tab "📦 Đóng Gói & Tải Lên (Publish)"**:
     - Thư mục nguồn: Chọn thư mục build chứa file chạy ứng dụng (ví dụ: `bin\x64\Release\net8.0-windows`).
       *(Hệ thống đã tự động lọc bỏ các thư viện không cần thiết của iOS, Android, Linux, macOS, Cache ảnh mẫu và PDB, giảm dung lượng gói từ 320MB xuống chỉ còn ~35-45MB!)*
     - Phiên bản mới: Nhập hoặc bấm các nút tăng nhanh (+0.0.0.1, +0.0.1.0, +0.1.0.0).
     - Đường dẫn script máy chủ: `https://cmsvina4285.com/ota_server.php`
     - Thư mục trên server: Nhập thư mục lưu trữ mong muốn (mặc định: `uploads/ota_packages` hoặc `update`).
     - Bấm nút **🚀 Đóng Gói Zip & Tải Lên Server**.
   - **Tab "⚙️ Cài Đặt Máy Chủ OTA"** (Trên các máy client IPC):
     - Loại nguồn: `🏢 Máy chủ nội bộ LAN / HTTP Manifest (version.json)`
     - Đường dẫn: `https://cmsvina4285.com/version.json` (hoặc `https://cmsvina4285.com/ota_server.php?action=get_version`)
     - Bấm **💾 Lưu Cấu Hình OTA** và bấm **🔍 Kiểm Tra Ngay**.

---

## 📂 Cấu Trúc Thư Mục Tự Động Sinh Ra Trên Server:
```text
htdocs/ (hoặc public_html/)
├── ota_server.php
├── .htaccess                        (Cấu hình 1024M cho Apache)
├── .user.ini                        (Cấu hình 1024M cho PHP-FPM)
├── version.json                     (Tệp manifest phiên bản mới nhất công khai)
└── uploads/
    ├── temp/                        (Thư mục tạm ghép các chunk upload)
    └── ota_packages/                (Gói nén cập nhật .zip & bản sao version.json)
        ├── VisionUpdate_v1.0.0.2_20260907_155210.zip
        └── version.json
```
