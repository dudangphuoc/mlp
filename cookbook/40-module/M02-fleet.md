# M02 — Fleet (cắm máy)

> **Phase:** P1 · **Phụ thuộc:** M01 · **Quyết định:** D-01, D-05, D-11, D-12, D-26 · **Proto:** `fleet.proto` (`FleetService`), `agent.proto` · **Schema DB:** `fleet`
> Giao thức agent, cài đặt theo OS, HostInfo, chẩn đoán Ray/OS: [20/04 Agent](../20-kien-truc/04-agent.md). Tài liệu này là phần nghiệp vụ và giao diện.

## 1. Mục tiêu & phạm vi
Cho phép cắm PC (Linux, Windows + WSL 2, macOS) vào workspace bằng một lệnh; mỗi PC thành một Ray worker node; theo dõi và điều khiển từ portal.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Lệnh cài một dòng, theo dõi cài đặt realtime | ✔ | | |
| Trạng thái máy, phần cứng thật, metric 15 s, doctor | ✔ | | |
| Pool (nhóm máy); giới hạn tài nguyên theo pool đặt ở `sched.quotas` (M03) | ✔ | | |
| Drain / restart node / nâng cấp / disconnect / reconnect | ✔ | | |
| Đối chiếu agent ↔ Ray, cảnh báo lệch | ✔ | | |
| Lịch "giờ được dùng máy", wake-on-LAN | | ✔ | |
| Nhiều cụm, nhiều địa điểm | | | ✔ |

## 2. Khái niệm
**Host** (máy đã cắm), **pool** (nhóm máy chung giới hạn), **enrollment** (một lần cài), **doctor finding** (kết quả tự kiểm), **node image** (`mlp-node` cpu/gpu do platform quản lý).

## 3. Dữ liệu (schema `fleet`)

| Bảng | Cột chính |
|---|---|
| `enrollments` | id, workspace_id, pool_id, token_hash, os, data_path, models_path, created_by, expires_at (10 phút), used_at, host_id, last_stage, last_stage_at |
| `hosts` | id, workspace_id, pool_id, name, state, state_reason, overlay_ip (unique), wg_public_key, credential_hash, agent_version, node_image, ray_node_id, data_path, models_path, labels jsonb, host_info bytea (`HostInfo` proto mới nhất), last_seen_at, created_at, disconnected_at |
| `host_gpus` | host_id, index, uuid, model, vram_bytes, driver_version, cuda_version — dẫn xuất từ HostInfo để truy vấn |
| `host_doctor` | host_id, check, status, detail, fixed, at |
| `host_commands` | id, host_id, kind, payload bytea, state (`pending/sent/running/succeeded/failed/expired`), progress, created_by, created_at, finished_at, result bytea |
| `pools` | id, workspace_id, name, description, labels jsonb (giới hạn job/GPU/notebook: `sched.quotas`, M03) |
| `host_metrics` (hypertable) | host_id, at, cpu_pct, mem_used, gpu_index, gpu_util, gpu_mem_used, gpu_temp, gpu_power, disk_free, net_rx, net_tx |

Pool `default` tạo cùng workspace. Giữ `host_metrics` 30 ngày (gộp 1 phút sau 24 giờ).

## 4. Trạng thái & quy tắc

```
enrolling ─(connected)─► online ⇄ busy          (busy = có job / notebook / replica)
    │                     │  ▲
    └─(lỗi cài)─► failed  ▼  │ undrain
                       draining ─(hết việc)─► drained ─(undrain)─► online
failed ─(chạy lại lệnh cài, token mới)─► enrolling
online|busy|draining|drained ─(mất kênh 60 s)─► offline ─(kết nối lại)─► trạng thái trước
online|busy ─(agent sống, Ray node chết, 3 lần khởi động lại thất bại)─► degraded ─(Ray node ALIVE lại / RestartRayNode thành công)─► online
* ─(Disconnect)─► disconnected
```

| Chuyển | Ai | Điều kiện |
|---|---|---|
| tạo enrollment | Owner, Admin | pool tồn tại |
| → draining / drained / undrain | Owner, Admin | — |
| restart node, upgrade | Owner, Admin | upgrade chỉ chạy khi máy rảnh |
| → disconnected | Owner, Admin | xác nhận; huỷ việc trên máy |
| reconnect | Owner, Admin | host `disconnected`/`offline` |

- **R1** `offline` khi không nhận gì trên kênh 60 s; M03 coi job trên máy offline > 5 phút là mất node.
- **R2** `draining`: scheduler không đặt việc mới; việc đang chạy tiếp tục.
- **R3** Disconnect: thu hồi credential, xoá peer WireGuard, huỷ job/notebook/replica (M03/M08/M12 nghe sự kiện), dataset `host_local` của máy chuyển `unavailable`. Không xoá gì trên máy.
- **R4** Một máy thuộc một workspace; enroll từ workspace khác → host cũ bị disconnect.
- **R5** Cài lại trên cùng máy với credential cũ → giữ `host_id`, `overlay_ip`.
- **R6** Đối chiếu agent ↔ Ray mỗi 30 s theo bảng ở [20/04 §6](../20-kien-truc/04-agent.md); lệch sinh `doctor` + thông báo cho Admin.

## 5. Luồng chính
1. **Cắm máy**: Admin chọn OS, pool, `data_path`, `models_path` → `CreateEnrollment` → lệnh cài (một lần, 10 phút) → chạy trên máy → agent enroll → các bước `agent_installed → network_configured → image_pulled → ray_node_started → connected` hiện realtime → host `online`.
2. **Theo dõi**: agent gửi `HostMetrics` 15 s, `HostInfo` khi đổi → api lưu, đẩy `WatchHosts`.
3. **Drain trước bảo trì**: Admin `DrainHost` → chờ `drained` → bảo trì → `UndrainHost`.
4. **Nâng cấp**: Admin `UpgradeHosts(pool)` → agent tải bản mới khi rảnh, blue/green → báo phiên bản.
5. **Gỡ máy**: `DisconnectHost` → agent nhận `Disconnect` (nếu còn kết nối) → dọn dẹp.

## 6. Hợp đồng (`fleet.proto`)

| RPC | Quyền | Mô tả |
|---|---|---|
| `CreateEnrollment` | `fleet.manage` | Trả lệnh cài cho Linux/macOS/Windows + `expires_at` |
| `GetEnrollment` / `WatchEnrollment` | `fleet.manage` | Tiến độ cài |
| `ListHosts` / `GetHost` / `WatchHosts` | `fleet.read` | Lọc theo pool, trạng thái, GPU |
| `UpdateHost` | `fleet.manage` | Tên, pool, label tuỳ biến |
| `DrainHost` / `UndrainHost` / `RestartNode` / `UpgradeHosts` / `DisconnectHost` / `ReconnectHost` | `fleet.manage` | Lệnh tới agent; trả `HostCommand` |
| `ListHostCommands` | `fleet.read` | Lịch sử lệnh |
| `GetHostMetrics` | `fleet.read` | Chuỗi thời gian theo khoảng + bước |
| `ListDoctorFindings` | `fleet.read` | Kết quả tự kiểm |
| `BrowseHostPath` | `dataset.write` | Liệt kê thư mục con trong `data_path` (dùng khi tạo dataset local) |
| `ListPools` / `CreatePool` / `UpdatePool` / `DeletePool` | đọc: `fleet.read`; ghi: `fleet.manage` | |
| `GetFleetSummary` | `fleet.read` | Tổng CPU/GPU/VRAM, online/offline, đang dùng |

## 7. Màn hình

### S-02-1 Danh sách máy — `/w/{ws}/fleet`
```
┌ Máy (4) · 3 online · GPU 2/5 đang dùng ───────────────── [Pool: Tất cả ▾] [+ Cắm máy] ┐
│ [Tìm…]  Trạng thái [Tất cả▾]  GPU [Tất cả▾]                   [Lưới│Gọn│Bảng]            │
│ ┌──────────────────────────┐ ┌──────────────────────────┐ ┌──────────────────────────┐   │
│ │ ● pc-01      Ubuntu 24.04│ │ ● pc-02   Windows 11+WSL2│ │ ○ mac-01      macOS 15.2 │   │
│ │ 2× RTX 4090 · 24 GB      │ │ RTX 3090 · 24 GB         │ │ CPU · M3 Pro · 36 GB     │   │
│ │ GPU ▇▇▇▇▇▁ 82%  44°C     │ │ GPU ▁▁▁▁▁▁ 0%            │ │ offline 2 giờ            │   │
│ │ 1 job · 1 notebook       │ │ rảnh                     │ │                          │   │
│ │ ⚠ RAM VM WSL = 8/64 GB    │ │ pool: gpu   v1.4.2       │ │ pool: cpu   v1.4.2       │   │
│ └──────────────────────────┘ └──────────────────────────┘ └──────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────────────────────────┘
```
- Chế độ Bảng: Tên, Trạng thái, OS, CPU/RAM, GPU (model × số, VRAM), % GPU, Việc đang chạy, Pool, Phiên bản agent, Lần cuối thấy.
- Badge cảnh báo khi có doctor `warning/failed`.
- Rỗng: "Chưa có máy nào. Cắm máy đầu tiên để bắt đầu train." + nút (chỉ Admin).
- Realtime qua `WatchHosts`.

### S-02-2 Cắm máy — `/w/{ws}/fleet/new`
```
┌ Cắm máy mới ──────────────────────────────────────────────────────────────┐
│ 1. Hệ điều hành   (● Linux) (○ Windows) (○ macOS)                         │
│ 2. Pool           [gpu ▾]                                                 │
│ 3. Thư mục dữ liệu trên máy   [/datasets          ]  (mount chỉ-đọc)      │
│    Thư mục model trên máy     [/models            ]                       │
│ [Tạo lệnh cài]                                                            │
│ ┌ Chạy lệnh này trên máy (Terminal, quyền sudo) ─────── còn 09:41 [Copy] ┐ │
│ │ curl -fsSL https://cp…/install/agent.sh | sudo sh -s -- --token mlpe_… │ │
│ └────────────────────────────────────────────────────────────────────────┘ │
│ Tiến độ:  ✔ Cài agent  ✔ Mạng overlay  ◐ Tải image (1.2/6.8 GB)  ○ Ray node  ○ Kết nối │
└───────────────────────────────────────────────────────────────────────────┘
```
Windows hiện lệnh PowerShell + ghi chú "chạy với quyền Administrator, có thể cần khởi động lại để bật WSL 2". Hết hạn → nút "Tạo lệnh mới". Hoàn tất → chuyển tới S-02-3. RPC: `CreateEnrollment`, `WatchEnrollment`.

### S-02-3 Chi tiết máy — `/w/{ws}/fleet/{host}`
- `PageHeader`: tên, trạng thái, OS, pool, phiên bản agent; nút `Drain`, `Khởi động lại node`, `Nâng cấp`, `⋯` (Đổi tên, Đổi pool, Thu log, Disconnect).
- Tab **Tổng quan**: `HardwareChip` (CPU model, lõi, RAM, GPU + driver + CUDA, đĩa), Docker/WSL và giới hạn VM, overlay IP + handshake, label Ray hiện tại.
- Tab **Giám sát**: `MetricChartGroup` CPU, RAM, GPU util/VRAM/nhiệt/điện, đĩa, mạng (1 giờ / 24 giờ / 7 ngày).
- Tab **Việc đang chạy**: bảng job, notebook, replica trên máy (link).
- Tab **Doctor**: danh sách kiểm tra với trạng thái và hướng dẫn sửa.
- Tab **Lệnh**: lịch sử `HostCommand` (ai, khi nào, kết quả).
- Offline: banner "Máy offline từ …, việc trên máy đã được chuyển/huỷ".

### S-02-4 Pool — `/w/{ws}/fleet?tab=pools`
Bảng pool: tên, số máy, tổng GPU, quota áp dụng (đọc từ M03, link tới S-03-3); tạo/sửa tên, mô tả, label bằng `FormDialog`; xoá chỉ khi không còn máy.

## 8. Sự kiện
`fleet.HostEnrolled`, `HostOnline`, `HostOffline`, `HostBusy`, `HostDraining`, `HostDegraded`, `HostDisconnected` (M03/M04/M08/M12 nghe), `HostHardwareChanged`, `HostCommandFailed`, `DoctorFindingFailed` (finding mức `failed`, M16 nghe). Nghe `catalog.RecipeVerified` để gửi `PrepullImages` tới máy phù hợp (GPU/VRAM).

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `FLEET_TOKEN_EXPIRED` / `FLEET_TOKEN_USED` | FAILED_PRECONDITION | Lệnh cài đã hết hạn / đã dùng — tạo lệnh mới |
| `FLEET_OVERLAY_EXHAUSTED` | RESOURCE_EXHAUSTED | Hết địa chỉ overlay |
| `FLEET_HOST_OFFLINE` | FAILED_PRECONDITION | Máy đang offline |
| `FLEET_HOST_BUSY` | FAILED_PRECONDITION | Máy đang có việc (nâng cấp sẽ chờ) |
| `FLEET_UNSUPPORTED_PLATFORM` | INVALID_ARGUMENT | Hệ điều hành/kiến trúc chưa hỗ trợ |
| `FLEET_POOL_NOT_EMPTY` | FAILED_PRECONDITION | Pool còn máy |

## 10. Nghiệm thu
- [ ] Ubuntu có NVIDIA GPU: chạy 1 lệnh → máy `online` đúng GPU/VRAM ≤ 10 phút (không tính tải image lần đầu trên mạng chậm).
- [ ] Windows 11 + WSL 2 và macOS Apple silicon cắm được; Mac ở chế độ CPU; S-02-1 hiển thị đúng OS thật (không phải OS của container).
- [ ] Lệnh cài dùng lần 2 hoặc sau 10 phút bị từ chối.
- [ ] Cài lại trên cùng máy không tạo host trùng.
- [ ] Rút mạng → `offline` ≤ 75 s; cắm lại → `online` không cần thao tác.
- [ ] `ray status` trên head luôn 0 CPU/0 GPU; job thử không chạy trên head.
- [ ] Máy có GPU nhưng thiếu NVIDIA Container Toolkit → doctor `failed` + cảnh báo trên thẻ máy.
- [ ] Disconnect → credential bị từ chối ngay, job trên máy bị huỷ, peer WireGuard bị gỡ.
