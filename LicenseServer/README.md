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
- **Sao Lưu & Di Trú 1-Click**: Xuất toàn bộ dữ liệu (License, Máy trạm, Đăng ký, Nhật ký và cặp khóa RSA) ra 1 file JSON duy nhất, nhập lại trên máy chủ mới để chuyển server trong vài giây.

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
- `GET  /api/v1/admin/data/export`: Xuất toàn bộ dữ liệu (CSDL + cặp khóa RSA) ra JSON.
- `POST /api/v1/admin/data/import`: Phục hồi toàn bộ dữ liệu từ file JSON (`replace`, `restoreKeys`).

## 5. Sao Lưu & Di Trú Máy Chủ (Backup / Migrate)

Mở Web Admin Dashboard ➜ tab **🗄️ Sao Lưu & Di Trú**.

1. **Xuất toàn bộ dữ liệu (Export)**: Tải về file `vision2026-license-backup-<timestamp>.json` chứa toàn bộ bảng `licenses`, `machines`, `client_registrations`, `audit_logs` **và cặp khóa RSA** (`private.pem`/`public.pem`).
2. **Nhập & Phục hồi (Import)**: Chọn/kéo-thả file `.json` vừa xuất, rồi bấm nhập.
   - `Ghi đè toàn bộ` (mặc định): xóa sạch dữ liệu hiện có trên máy chủ rồi phục hồi (dùng khi chuyển server).
   - Bỏ chọn để **nhập bổ sung** (chỉ thêm bản ghi chưa tồn tại).
   - `Phục hồi cả cặp khóa RSA`: bắt buộc chọn nếu muốn các file `.lic` đã cấp trước đó còn hợp lệ trên máy chủ mới.

> ⚠️ File sao lưu chứa Private Key ký bản quyền — hãy lưu trữ ở nơi an toàn.
> Sau khi phục hồi có khóa RSA, nên **khởi động lại** License Server để áp dụng hoàn toàn.
>
> Quy trình chuyển server gọn nhất: (1) Export trên server cũ ➜ (2) cài đặt server mới ➜ (3) Import file JSON trên server mới ➜ (4) restart server.
