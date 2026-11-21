# 10/02 — Khảo sát Ultralytics On Premise (cắm máy)

> **Tham khảo, không phải spec.** Phần khảo sát (mục 1–5, 7) là sự thật quan sát được; phần đề xuất (mục 6) đã được thay bởi [20/04 Agent](../20-kien-truc/04-agent.md), [M02 Fleet](../40-module/M02-fleet.md), [M03](../40-module/M03-scheduler.md).

> Nguồn: trang `Settings › Integrations › On Premise`, tài liệu chính thức (On Premise, Cloud/Remote Training) và **nội dung script cài đặt công khai** `GET /api/workers/install?os=linux|windows` (chỉ đọc, không chạy). Khảo sát 17/11/2026.
> Gói Free chỉ hiện nút "Request a demo" thay vì sinh lệnh cài, nên chưa thấy được giao diện danh sách host thật.

## 1. Ý tưởng cốt lõi

Platform là **control plane** (UI, metadata, nhãn, hàng đợi job, metric, registry). Việc nặng chạy trên **worker** — máy của người dùng cắm vào bằng một lệnh. Dữ liệu gốc **không rời máy**.

```
 Trình duyệt ──UI / nhãn / tiến độ──► Platform (control plane)
      │                                  ▲   │
      │  preview ảnh trực tiếp           │   │ job, cấu hình
      │  (localhost:8765 trên máy worker)│   ▼
      └──────────────────────────────► Worker (Docker, máy của bạn)
                                         │  outbound HTTPS only, không cần mở cổng vào
                          ┌──────────────┴───────────────┐
                    Dataset folder (mount read-only)   Models folder (ghi run, weights)
```

## 2. Luồng "join" một máy (6 bước, UI theo dõi realtime)

1. Ở `Settings › Integrations › On Premise`: chọn OS (Linux / macOS / Windows), nhập **thư mục dataset** và **thư mục models** trên máy đó → *Create install command*.
2. Platform sinh **connection token** dùng **1 lần, hết hạn sau 10 phút**, nhúng sẵn vào lệnh:
   - Linux/macOS: `curl -fsSL '<platform>/api/workers/install?os=linux' | sudo sh -s -- "/datasets" "/models" "<TOKEN>"`
   - Windows: script PowerShell tương đương (Docker Desktop + WSL 2).
3. Script trên máy:
   1. Cài/khởi động Docker nếu thiếu (Linux: get.docker.com + systemd; macOS: Docker Desktop + login item; Windows: Docker Desktop + Startup shortcut).
   2. Thu thập **hardware fingerprint**: hostname, OS, kiến trúc, số CPU, RAM.
   3. **Enroll**: `PUT /api/workers/enroll {token, name, previousKey?}` → nhận **API key riêng cho host** (`ul_<40 hex>`). Nếu máy đã từng enroll, gửi `previousKey` để **giữ nguyên host** (không tạo bản trùng) và key cũ bị thu hồi.
   4. Ghi `.env` (chmod 600): key, URL platform, thông tin phần cứng, đường dẫn, image.
   5. Tải các module Python của worker từ `GET /api/workers/script?module=…` (`worker`, `coords_codec`, `dataset_formats`, `embed_model`, `ssrf`) — có auth bằng key.
   6. Chọn base image theo phần cứng: ARM64 → `latest-arm64`; x86 có NVIDIA + Docker CDI → `latest` (GPU); còn lại → `latest-cpu`. Một container duy nhất/máy, tự phát hiện CUDA.
   7. Sinh `Dockerfile` + `compose.yaml` (+ `compose.override.yaml` cấp GPU qua CDI/nvidia) rồi `docker compose up -d`, `restart: unless-stopped`.
4. Mỗi giai đoạn báo về `PUT /api/workers/heartbeat {stage}`: `downloading → pulling → building → starting` → platform hiện **Connected**.

**Container worker**:
- Mount: `DATA_PATH → /workspace/data:ro`, `MODELS_PATH → /workspace/models` (rw), volume `cache` (dữ liệu dẫn xuất: giải nén, frame video, NDJSON).
- Mở `127.0.0.1:8765` → trình duyệt **trên cùng máy** lấy ảnh preview trực tiếp từ worker (không qua cloud, không cần VPN/cert).
- `start.py` **tự cập nhật**: lấy danh sách module, so ETag = sha256 file hiện có, chỉ tải module thay đổi, rồi `exec worker.py --on-premise`. Lặp lại khi container khởi động và mỗi 10 phút lúc rảnh; **không restart khi đang chạy job**.

## 3. Vận hành host

| Chức năng | Hành vi |
|---|---|
| Danh sách host | hostname, phần cứng, CPU/GPU online?, last seen |
| Offline | máy tắt/mất mạng → *Offline*; dataset tạm không dùng được; **không** chuyển job sang cloud; worker tự kết nối lại khi máy lên |
| Chạy lại lệnh cài | đổi thư mục / làm mới image, **giữ nguyên host** |
| Reconnect | sinh lệnh mới cho host bị ngắt; dataset cũ dùng lại không cần import lại |
| Disconnect | thu hồi key ngay; huỷ job đang chờ/chạy trên host; ingest dở thì fail; không xoá gì trên máy |
| Ràng buộc | 1 máy = 1 worker = 1 workspace (cài từ workspace khác thì máy chuyển sang đó) |
| Quyền | cần vai trò Editor trở lên |
| Log | `docker compose logs -f` trong thư mục cài |

## 4. Dataset & training trên máy đã cắm

- **New Dataset › On Premise** → chọn máy → duyệt thư mục → chọn task → worker **index tại chỗ** (nhận layout YOLO/COCO/NDJSON/LabelMe/thư mục classify, split, class).
- **Ranh giới dữ liệu**: pixel ở lại máy; nhãn/annotation/metric/checkpoint tốt nhất lên platform.
- Gán nhãn trên platform **không sửa** file gốc.
- Train: hộp thoại "New Model" đổi tab thành **On Premise** (job chỉ chạy được trên đúng host có dataset). Worker dùng GPU nếu có, không thì CPU.
- Trong khi chạy: metric + console + system stream về; `best.pt` upload định kỳ; artifact khác ở lại `models/`.
- Trạng thái job: `Starting → Running → Completed | Failed | Cancelled`; im lặng 4 giờ → tự Failed. Lỗi Python nghiêm trọng (traceback, CUDA OOM) → fail ngay, hiện banner + *Retry*.
- Cancel: báo process dừng ở ranh giới epoch, upload phần đã có.

**Hạn chế hiện tại của họ**: không rescan dataset; tắt các tính năng AI cần pixel (SAM, auto-label, tìm ảnh tương tự, clustering); không version snapshot; job **không di chuyển** sang máy khác khi worker chết; xoá model trên platform không xoá file trên máy.

## 5. Hai kiểu "máy ngoài" khác

| Kiểu | Cách hoạt động |
|---|---|
| **Remote / Local training** | Không cần worker. Chạy `yolo train … project=<owner>/<project> name=<model>` với `ULTRALYTICS_API_KEY`; callback của thư viện stream metric + upload checkpoint. Data có thể kéo từ platform bằng URI `ul://owner/datasets/name`. Resume giữ cùng model nhờ `platform_model_id.txt`. |
| **Cloud training** | Platform tự cấp GPU, có danh sách GPU theo tồn kho thật và **tự failover** sang GPU gần nhất (VRAM → tốc độ → giá). |

## 6. Áp dụng cho MLPlatform: "cụm máy cá nhân" của bạn

Mục tiêu của bạn: **nhiều PC cùng join vào một portal**, portal điều phối job. Giữ khung trên, cải tiến những chỗ Ultralytics còn yếu:

### 6.1 Giữ nguyên
- Lệnh join 1 dòng, token 1 lần + hết hạn ngắn, enroll đổi lấy **credential riêng từng máy**, re-enroll giữ danh tính host.
- Worker **chỉ gọi ra ngoài** (outbound), không cần mở port/VPN.
- Docker + chọn image theo phần cứng; GPU qua NVIDIA Container Toolkit/CDI; macOS chạy CPU (cân nhắc thêm MPS chạy native ngoài Docker).
- Tự cập nhật code worker, không restart giữa job.
- Mount dữ liệu read-only, ranh giới dữ liệu rõ ràng, theo dõi tiến trình cài realtime trên UI.

### 6.2 Cần làm tốt hơn (vì bạn có nhiều máy)
| Vấn đề ở Ultralytics | Đề xuất cho MLPlatform |
|---|---|
| Job gắn cứng vào đúng host chứa dataset | **Scheduler** chọn máy theo yêu cầu (GPU/VRAM/RAM/tag) + vị trí dữ liệu; dataset có thể nằm trên shared storage (NAS/MinIO/S3) để chạy trên bất kỳ máy nào |
| 1 container/máy, 1 job/lúc | **Slot theo tài nguyên**: máy nhiều GPU chạy nhiều job, hoặc gán từng GPU |
| Job chết khi worker chết | **Lease + heartbeat + checkpoint** → tự re-queue và resume trên máy khác |
| Không multi-node | Tuỳ chọn job phân tán (torchrun/DeepSpeed) gom nhiều máy — giai đoạn sau |
| Chỉ YOLO image | Worker chạy **runtime plugin** bất kỳ (container image theo task: YOLO, HF Transformers, sklearn/XGBoost, custom Docker) |
| Chỉ preview khi mở browser trên chính máy đó | Preview qua tunnel do worker mở ngược về portal (hoặc LAN direct khi cùng mạng) |
| 1 máy chỉ thuộc 1 workspace | Pool máy dùng chung có **label/quota** theo team/project |
| Không thấy GPU util tổng | Dashboard **fleet**: online/offline, GPU/VRAM/nhiệt độ, job đang chạy, hàng đợi |

### 6.3 Thành phần cần có (đầu vào cho spec)
1. **Enrollment service**: tạo token (TTL, 1 lần, phạm vi workspace/pool), endpoint enroll, cấp/thu hồi credential máy, script cài theo OS.
2. **Worker agent**: báo phần cứng, heartbeat, nhận job (long-poll/WebSocket/gRPC stream), kéo image runtime, mount dữ liệu, stream log/metric/system, upload artifact, tự cập nhật, xử lý cancel.
3. **Scheduler/Queue**: hàng đợi job, khớp yêu cầu ↔ khả năng máy, lease, retry, ưu tiên, giới hạn đồng thời.
4. **Fleet UI**: danh sách máy, trạng thái, phần cứng, job, nút reconnect/disconnect/drain, theo dõi cài đặt realtime.
5. **Data plane**: chế độ dữ liệu *local-only* (như On Premise) và *shared storage*; index dataset ngay trên máy.
6. **Telemetry**: metric train, console (giữ N dòng cuối), system (CPU/RAM/GPU/nhiệt/IO).

### 6.4 Điều chỉnh sau đối chiếu MLOps: dùng Ray làm lớp thực thi

Sau khi đối chiếu ([04](03-doi-chieu-mlops.md)), agent **không tự viết scheduler phân tán** mà dựng **Ray cluster** trên các máy đã join:

| Ultralytics làm | MLPlatform làm |
|---|---|
| Worker gọi ra ngoài, nhận job qua API platform | Agent vẫn gọi ra ngoài cho **control channel** (enroll, heartbeat, lệnh, update) |
| Không có mạng giữa các máy | Agent dựng **overlay WireGuard** khi enroll → các node gọi được nhau |
| 1 container chạy train trực tiếp | Container chạy **Ray node** (`ray start --address=<head>`), khai báo resource + label |
| Job gắn cứng vào host | Ray xếp task theo resource/label; multi-node bằng Ray Train; HPO bằng Ray Tune |
| Worker chết → job fail | Ray Train khôi phục từ checkpoint; scheduler re-queue job một node |

Lưu ý bắt buộc: Ray không có xác thực mặc định → cổng Ray chỉ mở trên overlay; mọi node cùng phiên bản image; Ray head chạy trên server control plane với 0 CPU/0 GPU (chỉ điều phối), các PC chỉ là worker node. Chế độ "data không rời máy" vẫn giữ bằng label `has_dataset:<id>` để Ray chỉ xếp job lên đúng máy chứa dữ liệu.

Chi tiết module: [05 — M2 Fleet Manager, M3 Job Scheduler](../README.md); kỹ thuật: [06](../20-kien-truc/02-backend-dotnet-aot.md).

## 7. API worker quan sát được

```
GET  /api/workers/install?os=linux|macos|windows   # script cài (công khai)
PUT  /api/workers/enroll      {token, name, previousKey?} -> api key của host
PUT  /api/workers/heartbeat   {stage}  (Bearer key) — cũng dùng báo tiến trình cài
GET  /api/workers/script                -> {modules:[…]}
GET  /api/workers/script?module=<name>  (If-None-Match: "<sha256>") -> 200 | 304
```

Phần giao thức nhận job/stream metric nằm trong `worker.py` (chỉ tải được sau khi enroll), chưa xem được. Dependency trong image gợi ý worker dùng MongoDB driver, Google Cloud Storage/Logging, mã hoá, xxhash, Sentry.
