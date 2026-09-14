# Vision2026 Standalone License Server 🛡️

Máy chủ quản lý bản quyền độc lập trên Internet cho giải pháp thị giác công nghiệp `Vision2026`.

## 1. Tính Năng Nổi Bật
- **Độc Lập 100%**: Không phụ thuộc bất kỳ hệ thống mạng nội bộ hay ERP nào của công ty. Có thể deploy lên VPS, Cloud, Docker bất kỳ.
- **Ràng Buộc Phần Cứng (Hardware Binding)**: Mỗi máy tính được định danh duy nhất bằng mã băm CPU, Motherboard, BIOS, Ổ đĩa cài HĐH. Chống 100% sao chép và sử dụng chùa.
- **Kích Hoạt Online & Offline**:
  - Máy có Internet: Kích hoạt tự động 1-click, gửi heartbeat định kỳ.
  - Máy cô lập mạng (Air-gapped): Ký file `.req` để tạo file bản quyền `.lic`.
- **Quản Lý Client Từ Xa**: Xem trạng thái online, thu hồi bản quyền (Revoke), tạm dừng (Suspend), chuyển đổi máy trạm (Transfer).
- **Mật Mã Học Bất Đối Xứng RSA-2048**: Server giữ Private Key ký số, Client chỉ giữ Public Key để xác thực.

## 2. Cách Chạy License Server Tại Local / VPS

### Yêu Cầu
- Node.js >= 20 (Khuyên dùng v22+ hoặc v26+)

### Cài Đặt & Khởi Chạy
```bash
cd LicenseServer
npm install
npm run dev
```

Server sẽ tự động:
1. Sinh cặp khóa RSA-2048 (`keys/private.pem` & `keys/public.pem`) nếu chưa có.
2. Khởi tạo cơ sở dữ liệu SQLite (`data/license.db`).
3. Tạo sẵn 1 License Key Enterprise mẫu: `V26-ENT-DEMO-2026-8888`.
4. Mở Web Admin Dashboard tại: **`http://localhost:4000/`** (Tài khoản: `admin` / `admin@vision2026`).

## 3. Triển Khai Lên Internet (Docker / VPS)

### Sử Dụng Docker
```bash
docker build -t vision2026-license-server .
docker run -d -p 4000:4000 -v ./data:/app/data -v ./keys:/app/keys --name vision-license vision2026-license-server
```

### Sử Dụng Nginx Làm Reverse Proxy + SSL (HTTPS)
Cấu hình mẫu Nginx trỏ vào cổng 4000:
```nginx
server {
    server_name license.yourdomain.com;

    location / {
        proxy_pass http://127.0.0.1:4000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }

    listen 443 ssl;
    ssl_certificate /etc/letsencrypt/live/license.yourdomain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/license.yourdomain.com/privkey.pem;
}
```

## 4. API Endpoints Tham Khảo

- `POST /api/v1/license/activate`: Kích hoạt máy mới.
- `POST /api/v1/license/heartbeat`: Heartbeat định kỳ.
- `GET  /api/v1/license/public-key`: Tải Public Key PEM.
- `POST /api/v1/license/offline-sign`: Ký file `.req` offline.
- `POST /api/v1/admin/login`: Đăng nhập quản trị.
- `GET  /api/v1/admin/clients`: Danh sách máy trạm.
- `POST /api/v1/admin/machine/revoke`: Thu hồi bản quyền từ xa.
- `POST /api/v1/admin/machine/transfer`: Chuyển máy.
