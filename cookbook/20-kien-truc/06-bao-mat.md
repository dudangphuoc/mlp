# 20/06 — Bảo mật

## 1. Chủ thể và xác thực

| Chủ thể | Cơ chế | Dùng cho |
|---|---|---|
| Người dùng trên web | Cookie phiên `HttpOnly`, `Secure`, `SameSite=Lax` + chống CSRF (cookie đôi + header) | gRPC-Web |
| Người dùng qua SDK/CLI | Metadata `authorization: Bearer mlp_…` | gRPC |
| Agent | Credential `mlpa_…` trong `ConnectionHello` | Kênh agent |
| Job đang chạy | `mlpj_…` cấp khi submit, hết hạn khi job kết thúc, phạm vi = job | `RuntimeService` + RPC ghi của module |
| Cắm máy | `mlpe_…` một lần, 10 phút | Enroll |
| Client gọi suy luận | `mlpd_…` theo deployment | Gateway suy luận |
| Proxy notebook | Phiên web + kiểm chủ sở hữu; token Jupyter do api tự gắn | `/nb/{id}` |

- Token: 160 bit ngẫu nhiên + tiền tố; lưu **SHA-256 + pepper**; hiển thị một lần; cache xác thực 30 s, thu hồi xoá cache ngay.
- Mật khẩu: PBKDF2-SHA256 600 000 vòng, salt 16 byte, chuỗi có version để nâng cấp.
- 2FA: TOTP (RFC 6238), mã khôi phục 10 cái.
- OAuth Google/GitHub: authorization code + PKCE, `state` chống CSRF.
- Khoá đăng nhập sai: 10 lần/15 phút/tài khoản + giới hạn theo IP.

## 2. Phân quyền
Ma trận quyền ở [M01 §4](../40-module/M01-iam.md). Thực thi trong `AuthorizationBehavior`; không có quyền đọc → `NOT_FOUND`; đọc được không được ghi → `PERMISSION_DENIED` nêu quyền thiếu. Stream `Watch*` kiểm lại khi quyền đổi.

## 3. Bí mật
- `ISecretBox` AES-256-GCM với khoá `Secrets:Kek` (file mount, không nằm trong DB/ảnh); mỗi bản mã có `key_id` để xoay khoá.
- Áp cho: secret S3 ngoài, token HF, cấu hình kênh thông báo, private key WireGuard hub, token Jupyter.
- Không ghi bí mật vào log, audit, sự kiện; danh sách trường bị che cấu hình tập trung.

## 4. Mạng
| Bề mặt | Bảo vệ |
|---|---|
| 443 (web, gRPC-Web, gRPC, suy luận) | TLS 1.3 (chứng chỉ do người vận hành cấp hoặc ACME), HSTS |
| 7443 (agent) | TLS; ghim chứng chỉ control plane trong cấu hình agent khi enroll |
| WireGuard 51820/udp | Peer theo từng máy, gỡ ngay khi disconnect; xoay khoá máy khi cài lại |
| Cổng Ray (6379, 10001; cổng worker động) | **Chỉ overlay**; firewall chặn interface public — Ray không có xác thực; Ray Client: xem mục 8 |
| ModelServer 8500–8599, rendezvous 29500–29599 | **Chỉ overlay** (namespace `mlp-net`); ModelServer chỉ nhận request có header nội bộ do gateway ký (HMAC, khoá theo deployment) |
| Ray Jobs/Serve API 8265 | **Chỉ 127.0.0.1**, chỉ api gọi |
| Notebook, file server trên máy | **Chỉ 127.0.0.1** của máy (`mlp-net` publish ra loopback máy); vào qua data channel |
| SMTP | STARTTLS bắt buộc; mật khẩu SMTP trong `ISecretBox` |
| PostgreSQL, kho S3 (quản trị), registry | Mạng nội bộ compose; registry có TLS + tài khoản pull riêng cho agent |

## 5. Chuỗi cung ứng
- Binary agent ký Ed25519; agent kiểm chữ ký trước khi cài/cập nhật.
- Image ghim digest. Registry nhận push từ: **CI** (image nền tảng + recipe builtin, `recipes/{recipe}`), job **`build_env`** (`ws/{ws}/…`) và job **`build_recipe`** (`recipes/ws-{ws}/…`) (D-30). Mỗi job build nhận credential push ngắn hạn chỉ ghi được đúng tiền tố của workspace; agent chỉ có quyền pull.
- Recipe nhập từ repo của người dùng chạy mã tuỳ ý trong container: không mount Docker socket, không `--privileged`, file chỉ thấy mount của attempt; **mạng** dùng chung namespace `mlp-net` với các container khác trên máy (rủi ro ở mục 8).
- Registry phân phối: `sync_images` chỉ kéo digest nằm trong manifest phát hành đã ký của phiên bản instance (D-31).
- Script cài tải qua HTTPS từ control plane, kèm SHA-256.

## 6. Audit
Mọi lệnh ghi, từ chối quyền, tải dữ liệu đi vào `audit.events` có chuỗi hash ([M06](../40-module/M06-lineage-audit.md)).

## 7. Dữ liệu local-only
Byte không rời máy (D-17); xử lý AI trên dữ liệu đó chỉ chạy trên đúng máy; data channel không lưu byte trên control plane.

## 8. Ray Client từ notebook — rủi ro được chấp nhận (D-24)
Ray Client (`10.88.0.1:10001`, nối thẳng qua overlay — D-33) bật ở P1 để notebook dùng GPU của các máy khác. Ray không có xác thực hay cách ly theo người dùng; các container trên cùng máy dùng chung namespace mạng `mlp-net` (D-26):

| Rủi ro | Hệ quả | Giảm thiểu ở P1 |
|---|---|---|
| Mọi notebook trên overlay nối được Ray Client | Người dùng workspace A gửi task lên máy của workspace B; dùng GPU ngoài quota | **Không chặn được ở P1.** api đọc Ray State API mỗi 30 s, hiển thị GPU đang bị dùng ngoài nền tảng theo máy/notebook (M03 S-03-3), cảnh báo Admin khi vượt ngưỡng; tài liệu SDK hướng dẫn gắn `resources={"pool:<p>": 0.001}` (tự nguyện) |
| Task chạy mã tuỳ ý trong `mlp-node` của máy khác | Đọc được `/mnt/data` (read-only) và `/mnt/cache` của máy đó | `mlp-node` chỉ mount `data_path` read-only; dữ liệu local-only của workspace khác nằm ngoài `data_path` dùng chung khi có thể; cảnh báo trong UI notebook |
| Đọc/huỷ job Ray của người khác qua API Ray | Lộ tên job, làm hỏng việc người khác | Job của nền tảng chạy trong container recipe, không phải task Ray; holder chỉ giữ tài nguyên |
| Không truy được ai gửi task | Khó điều tra | Audit best-effort: driver Ray Client trong Ray State API mang IP overlay của máy nguồn → api ánh xạ ra notebook/người dùng đang chạy trên máy đó và ghi `audit.events` (`nb.ray_client.session`); nhiều notebook cùng máy → ghi tất cả ứng viên |
| Chung namespace mạng trên một máy | Container recipe/task Ray Client với tới cổng loopback của notebook khác và `ModelServer`, rendezvous cùng máy | Jupyter luôn bật token (api tự gắn); `ModelServer` chỉ nhận request có chữ ký HMAC của gateway (mục 4); rendezvous chỉ mở trong thời gian job; file server **không** nằm trong namespace (20/04 §3.2) |

Instance Admin tắt được Ray Client toàn instance. Cách ly đúng nghĩa (cụm Ray riêng mỗi workspace hoặc namespace + xác thực) để P3.
