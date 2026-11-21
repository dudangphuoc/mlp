# 20/09 — Vận hành control plane

## 1. Triển khai
- `deploy/compose.yml` trên một server: `web`, `api` (AOT, image `runtime-deps` chiseled, ghim digest), `postgres` (TimescaleDB), `s3` (SeaweedFS, D-34) + `s3-init`, `registry`, `db-migrate`, `ray-head`, `wireguard`.
- Mạng compose (D-26): `web` (Nginx) publish 443 và chuyển tiếp `/v2/mlp/mlp-net` chỉ-đọc tới registry (D-31); service `wireguard` giữ `wg0` (10.88.0.1) và publish 7443, 51820/udp; `api` và `ray-head` khai `network_mode: service:wireguard` (`depends_on: wireguard`). `postgres`, `s3`, `registry` ở mạng nội bộ compose; registry còn lắng nghe trên 10.88.0.1:5000 qua proxy trong namespace `wireguard`.
- Registry phân phối: `MLP__Distribution__Registry` (mặc định của nhà phát hành), `MLP__Distribution__PullSecretFile` (tuỳ chọn). Việc nền `sync_images` chạy khi khởi động api, khi nâng cấp, và khi Instance Admin bật một recipe builtin mới.
- Cấu hình qua biến môi trường `MLP__*`; bí mật qua file mount (`Secrets:Kek`, TLS, khoá ký agent).
- Email (D-27): `MLP__Smtp__Host`, `MLP__Smtp__Port` (587), `MLP__Smtp__User`, `MLP__Smtp__PasswordFile`, `MLP__Smtp__From`, `MLP__Smtp__UseStartTls`. Không cấu hình → email tắt, UI cho Admin copy link xác minh/mời/đặt lại mật khẩu.

### 1.1 Cài đặt lần đầu (D-27)
1. `docker compose up -d` → `db-migrate` áp schema.
2. DB chưa có người dùng: `api` in **token bootstrap một lần** (hết hạn 24 giờ) ra log; hoặc chạy `docker compose exec api mlp admin bootstrap --email … ` (in link đặt mật khẩu).
3. Mở `https://<cp>/setup?token=…` → tạo **Instance Admin** đầu tiên, đặt tên instance, chọn cho phép tự đăng ký hay chỉ mời, nhập SMTP (tuỳ chọn), kiểm tra Ray head + WireGuard + registry.
4. Instance Admin tạo workspace đầu tiên và lệnh cắm máy (M02).
- Health: `/health/live`, `/health/ready` (Postgres, kho S3, Ray head, WireGuard, registry, SMTP nếu cấu hình).
- Áp migration là bước riêng (`db-migrate` chạy `mlp db apply`) trước khi khởi động api mới.

## 2. Nâng cấp
1. Sao lưu (mục 3).
2. `db-migrate` (chỉ expand) → khởi động api mới → kiểm health.
3. `sync_images` kéo bộ image của phiên bản mới (theo digest) về registry riêng trước khi nâng.
4. Ray: nâng head trước (workload đang chạy được giữ tối đa 15 phút, D-29 — nâng head phải xong trong thời gian đó), rồi lệnh `RestartRayNode` cuốn chiếu từng máy rảnh với image mới; máy bận chờ hết việc.
5. Agent tự cập nhật (blue/green) sau khi bản mới được phát hành.
6. File migration `contract` chỉ áp ở bản phát hành sau khi không còn mã dùng cột cũ.

## 3. Sao lưu & khôi phục
| Thành phần | Cách | Tần suất | Giữ |
|---|---|---|---|
| PostgreSQL | `pg_basebackup` + WAL archive lên đích ngoài server | liên tục + đầy đủ hằng ngày | 14 ngày |
| kho S3 | `mc mirror` sang ổ/máy khác (hoặc replication) | hằng giờ | theo dung lượng |
| Registry | Image nền tảng + recipe builtin đồng bộ lại được từ registry phân phối (`sync_images`); sao lưu volume registry (tag `ws/…`, `recipes/ws-…`) | hằng ngày | — |
| Cấu hình + bí mật | Sao lưu mã hoá riêng | khi đổi | — |
Khôi phục thử mỗi tháng; mục tiêu ≤ 1 giờ. Mất control plane: các máy giữ dữ liệu local, agent tự nối lại khi server lên.

## 4. Giám sát chính control plane
Góc nhìn cho người dùng (trạng thái thành phần, vòng nền, sự cố, banner) ở [20/10 §2.6](10-quan-sat-va-khoi-phuc.md); mục này là góc nhìn của người vận hành.
OpenTelemetry → collector (tuỳ chọn Grafana stack); cảnh báo: api 5xx > 1 %, p95 RPC > 500 ms, outbox tồn > 1 000, Ray head mất, đĩa > 80 %, chứng chỉ sắp hết hạn, chuỗi hash audit đứt.

## 5. Quota lưu trữ & dọn rác
| Hạng mục | Mặc định | Ai đổi |
|---|---|---|
| Dung lượng kho S3 theo workspace | không giới hạn (cảnh báo 80 %, 95 % dung lượng đĩa) | Owner |
| Home notebook/người | 20 GiB | Admin |
| Artifact/run | 50 GiB | Admin |
| Dataset upload một lần | 10 GiB/archive | Admin |

Vượt 80 % / 95 % dung lượng → phát `workspace.StorageThresholdReached{workspace, percent}` (một lần mỗi ngưỡng, reset khi xuống dưới). Việc nền hằng đêm: xoá cứng mục Thùng rác > 30 ngày; xoá checkpoint của job đã xong trừ checkpoint cuối và `best`; xoá export > 7 ngày; xoá log job > 90 ngày; dọn image environment không dùng > 90 ngày; báo cáo đĩa từng máy (từ HostInfo) và cảnh báo máy còn < 10 %.

### Màn hình S-OPS-1 Dung lượng — `/w/{ws}/settings?tab=storage`
Thanh dung lượng theo loại (dataset, artifact, checkpoint, notebook, log), top 10 mục lớn nhất (link), quota, máy sắp đầy đĩa. Nút "Dọn checkpoint cũ ngay".

### Màn hình S-OPS-2 Thùng rác — `/w/{ws}/settings?tab=trash`
Tab Tất cả / Datasets / Projects / Models / Notebooks; mỗi dòng: tên, loại, người xoá, ngày xoá, còn N ngày; `Khôi phục`, `Xoá vĩnh viễn` (xác nhận).

## 5b. Hợp đồng (`WorkspaceService`)
`GetStorageUsage`, `ListTrash`, `RestoreFromTrash`, `PurgeFromTrash`, `RunCleanupNow` — quyền: Owner/Admin (Thùng rác: Editor với mục của mình).
