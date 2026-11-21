# 20/01 — Kiến trúc hệ thống

Quyết định nền: D-01, D-04, D-05, D-10, D-11, D-12, D-14, D-21, D-24, D-26, D-27, D-29, D-31, D-32, D-33 ([sổ quyết định](../00-tong-quan/03-quyet-dinh.md)).

## 1. Ba mặt phẳng

```
                      ┌──────────────────────────── CONTROL PLANE (1 server) ────────────────────────────┐
 Trình duyệt ─gRPC-Web─►  web (Nginx, React)                                                             │
 SDK/CLI/notebook ─gRPC─►  api (MLPlatform.Api, .NET 10 AOT)                                             │
 Client ngoài ─REST/gRPC─►   ├─ gRPC services theo module (M01…M17)                                       │
                      │      ├─ Agent listener  :7443 (TCP+TLS, protobuf delimited)  ◄──── kênh agent ─────┤
                      │      ├─ Data-channel listener :7443 (cùng cổng, hello khác loại) ◄── data channel ─┤
                      │      ├─ Inference gateway  /v1/deployments/…                                       │
                      │      └─ Background: scheduler, outbox, đồng bộ Ray, dọn rác                       │
                      │  postgres (+TimescaleDB) · s3 (SeaweedFS) · registry · db-migrate                    │
                      │  ray-head (0 CPU/0 GPU) :6379 GCS · :8265 Jobs/Serve API (localhost) · :10001 Client│
                      │  wireguard hub wg0 10.88.0.1/16 · UDP 51820 (api, ray-head dùng chung namespace)  │
                      └──────────────────────────────────────▲───────────────────────────────────────────┘
                                                             │ overlay: Ray, registry pull, gateway→ModelServer, rendezvous, Ray Client (D-26)
                      ┌──────────────── EXECUTION PLANE (mỗi PC đã cắm) ──────────────────────────────────┐
                      │ agent (service hệ điều hành)  ── kênh agent ra ngoài :7443                         │
                      │ container mlp-net: WireGuard 10.88.x.y (NET_ADMIN) — namespace mạng chung          │
                      │ container mlp-node: Ray worker mỏng (giữ tài nguyên, holder)                      │
                      │ container recipe-*: train / eval / export / serve (ModelServer) — image theo recipe│
                      │ container notebook-<profile> (JupyterLab/code-server, chỉ nghe 127.0.0.1)          │
                      │ file server agent (chỉ nghe 127.0.0.1) cho dữ liệu local-only                      │
                      │ /datasets (read-only)  /models  · volume cache · volume home                      │
                      └──────────────────────────────────────────────────────────────────────────────────┘
```

| Mặt phẳng | Giữ sự thật về |
|---|---|
| Control plane | Người dùng, quyền, metadata (dataset, nhãn, job, run, model, deployment), lineage, audit (PostgreSQL); byte upload, artifact, checkpoint, log (kho S3); image (registry) |
| Execution plane | Tính toán; **thông tin máy thật** do agent báo (D-12); dữ liệu `host_local` |
| Ray (head + worker) | Trạng thái lập lịch: node sống/chết, tài nguyên, task/actor/job — không có OS |

## 2. Thành phần & cổng (nguồn duy nhất về cổng)

| Thành phần | Cổng | Lắng nghe trên | Ai gọi |
|---|---|---|---|
| web (Nginx) | 443 | public | Trình duyệt |
| api — gRPC, gRPC-Web, inference REST | 443 (sau Nginx) | public | Trình duyệt, SDK, client ngoài |
| api — agent & data channel | 7443 (TLS) | public | Agent |
| postgres | 5432 | mạng nội bộ compose | api, db-migrate |
| s3 (SeaweedFS, D-34) | 8333 (API S3), presign qua 443 | public cho presigned URL; overlay cho container | api, trình duyệt, job |
| registry (riêng của instance) | 5000 (TLS) | overlay + nội bộ | agent (pull qua overlay), job `build_recipe`/`build_env` (push), việc nền `sync_images` (D-31) |
| registry — đường bootstrap `/v2/` | 443 (sau Nginx) | public, **chỉ đọc**, credential `mlpa_` | agent kéo `mlp-net` trước khi có overlay (D-31) |
| registry phân phối của nhà phát hành | 443 | internet | `sync_images` (kéo image nền tảng + recipe builtin) |
| ray-head GCS | 6379 | **chỉ overlay** | Ray worker |
| ray-head Jobs/Serve/Dashboard | 8265 | **127.0.0.1** | chỉ api |
| ray-head Ray Client | 10001 | **chỉ overlay** | notebook (D-24, không cách ly) |
| ModelServer của replica (container recipe) | 8500–8599 | **chỉ overlay** | inference gateway |
| Rendezvous phân tán (`torchrun`) | 29500–29599 | **chỉ overlay** | container recipe các máy khác |
| wireguard hub | 51820/udp | public | agent |
| notebook trên PC | 9000–9099 | **127.0.0.1 của PC** | agent (qua data channel) |
| file server (trong tiến trình agent) | 8766 | **127.0.0.1 của máy thật**, ngoài namespace `mlp-net`; mỗi request cần token HMAC một lần do api cấp | agent (qua data channel) |
| SMTP ra ngoài | 587 (STARTTLS) | — | api gửi email giao dịch (D-27) |

**Không gian mạng trên PC (D-26):** container `mlp-net` giữ interface WireGuard; `mlp-ray-node`, container recipe và notebook chạy `--network container:mlp-net` nên dùng chung IP overlay và `127.0.0.1` của nhau. Cổng notebook "127.0.0.1 của PC" ở trên là loopback của namespace này (mọi container trong namespace đều thấy — Jupyter luôn bật token, xem 20/06 §8); agent (service hệ điều hành) vào được nhờ `mlp-net` publish `127.0.0.1:9000-9099` ra máy. File server **không** nằm trong namespace nên container không gọi tới được. Chạy giống nhau trên Linux, WSL 2 và Docker Desktop.

**Control plane:** service `wireguard` giữ `wg0`; `api` và `ray-head` khai `network_mode: service:wireguard` nên nghe trực tiếp trên `10.88.0.1`; cổng public (443, 7443, 51820/udp) publish ở service `wireguard`.

## 3. Đường đi dữ liệu theo kênh

| Luồng | Đường đi |
|---|---|
| Thao tác UI | Trình duyệt → gRPC-Web → api → PostgreSQL |
| Cập nhật realtime | api → `Watch*` server stream → trình duyệt |
| Upload/download lớn | Trình duyệt/SDK ↔ kho S3 (presigned URL); api chỉ cấp URL |
| Lệnh cho máy | api → kênh agent → agent → Docker/WireGuard/Ray |
| Gửi job | api → Ray (placement group + holder) → agent `StartWorkload` → container recipe trên PC (nhiều máy: rendezvous qua overlay) |
| Metric job | shim → gRPC `TrackingService.LogMetrics` (token `mlpj_`) → api |
| Log job | stdout/stderr container → agent → kênh agent → api (bộ đệm + kho S3) |
| Tiến độ, checkpoint, cấp phát GPU, mã thoát | shim/holder → gRPC `RuntimeService` → api |
| Mở Jupyter | Trình duyệt → api (kiểm quyền) → data channel do agent mở → 127.0.0.1:port notebook |
| Xem ảnh local-only | Trình duyệt → api → data channel → file server agent (byte không lưu trên control plane) |
| Suy luận | Client → inference gateway → overlay → `ModelServer` trong container recipe trên PC |
| Notebook dùng Ray | Notebook → overlay → Ray Client `10.88.0.1:10001` → task chạy trên PC khác |

## 4. Khi một phần hỏng

| Sự cố | Ảnh hưởng | Hệ thống làm gì |
|---|---|---|
| api dừng | Không thao tác UI/SDK; job đang chạy **vẫn chạy** | Runtime buffer metric/log; khi api lên, scheduler đồng bộ lại theo `submission_id` |
| Ray head dừng/khởi động lại (D-29, D-32) | Không lập lịch mới; container workload **vẫn chạy** (agent giữ tối đa 15 phút) | Container `restart: always`; head lên trong hạn → attempt còn sống thành `detached`, `mlp-ray-node` khởi động lại chỉ với GPU rảnh (M03 R6b); quá 15 phút → agent dừng container, job resume từ checkpoint |
| Một PC tắt | Mất job/replica/notebook trên PC đó | Host `offline` sau 60 s; job resume trên PC khác từ checkpoint; scheduler đặt replica mới ở PC khác |
| Mất kênh agent nhưng overlay còn | Không nhận lệnh, Ray vẫn chạy | Agent tự nối lại (backoff + jitter) |
| Mất overlay nhưng kênh agent còn | Ray node mất kết nối; replica không nhận request; train nhiều máy lỗi rendezvous | Agent khởi động lại `mlp-net`, báo doctor finding; gateway bỏ replica khỏi định tuyến |
| Service `wireguard` của control plane dừng | api và ray-head mất mạng overlay (cùng namespace) | Compose khởi động lại cả nhóm (`depends_on` + `restart: always`); health `/health/ready` báo WireGuard |
