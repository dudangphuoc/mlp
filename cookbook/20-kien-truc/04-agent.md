# 20/04 — Agent và kênh agent

Quyết định nền: D-11, D-12, D-15, D-17, D-21, D-26, D-29, D-30, D-31, D-32. Mẫu tham khảo: `TunnelsClient` + `agent.proto` của blogic-tunnels. Nghiệp vụ quản lý máy (trạng thái host, pool, UI) nằm ở [M02 Fleet](../40-module/M02-fleet.md).

## 1. Agent là gì
Chương trình **.NET 10 Native AOT**, một binary cho mỗi RID (`linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64`), chạy như service hệ điều hành:

| OS | Service | Thư mục cài | Dữ liệu agent |
|---|---|---|---|
| Linux | systemd `mlplatform-agent` | `/opt/mlplatform-agent` | `/var/lib/mlplatform-agent` |
| macOS | launchd `io.mlplatform.agent` | `/usr/local/mlplatform-agent` | `/Library/Application Support/MLPlatform` |
| Windows | Windows Service `MLPlatformAgent` | `C:\Program Files\MLPlatform\Agent` | `C:\ProgramData\MLPlatform` |

Nhiệm vụ: giữ kênh điều khiển; báo `HostInfo`/`HostMetrics`; quản lý container mạng `mlp-net` (WireGuard, D-26); điều khiển Docker (Docker Engine API qua unix socket / named pipe); chạy container Ray worker mỏng và **container workload của recipe** (train, eval, export, serve, notebook) trên đúng GPU được cấp; quản lý cache image recipe; mở data channel; tự kiểm (doctor); tự cập nhật.

Agent **không** chạy ML, không chứa Python. Mọi tính toán nằm trong container của recipe (D-21).

> **Sửa 2026-11-18 (D-38) — agent là cầu nối container.** Lệnh chung (`ContainerApply/Stop/Remove/RunOnce`, `ImageBuild`, `PrepullImages`, `File*`, `RunScript`, `BrowsePath`, data channel); sự kiện `ContainerEvent`/`ContainerSnapshot`. Ray node, workload, mlp-net là spec do control plane ghép (`HostContainers`). OTA: `FleetService.UpgradeAgents` → `AgentUpgrader` (build theo RID → script `deploy/agent-upgrade/upgrade.{sh,ps1}` → `RunScript` detached → đọc `status.txt` → `final-ok` khi bản mới kết nối ≥ 30 s → done / tự khôi phục). Agent ghi `--service systemd|launchd|schtasks` lúc cài để script biết cách dừng/khởi động.

## 2. Cài đặt (enroll)

> **Sửa 2026-11-18 (D-37) — plug-and-play:** `curl …/install/agent.sh | sudo sh -s -- --token … --data … --models …`
> 1. **doctor** in bảng ✔/⚠/✖ (nền tảng + libc, CPU/RAM, Docker ≥ 24 và daemon chạy, GPU NVIDIA + Container Toolkit, đĩa còn trống, `/health/live` và TCP 7443). Có ✖ → dừng kèm hướng dẫn; `--doctor` chỉ kiểm.
> 2. `POST /install/agent/build` (Bearer token enroll, thân = JSON doctor) → `{status: ready|building|failed, rid, version, url, sha256}`; script hỏi lại `GET /install/agent/build/{rid}` tới khi xong (≤ 20 phút).
> 3. `agent-builder` đọc `{agents}/queue/{ver}_{rid}.json`, `dotnet publish -r {rid}` (AOT khi cùng kiến trúc, single-file khi khác), ghi `cache/{ver}/{rid}/` + `build.json`/`build.log`. Máy sau cùng nền tảng nhận ngay từ cache; đổi mã nguồn agent → phiên bản mới.
> 4. Tải `/install/agent/{ver}/{rid}/mlp-agent`, kiểm SHA-256, đăng ký service với `--pin` (SHA-256 chứng chỉ kênh agent).

1. Admin tạo lệnh cài trên UI ([M02](../40-module/M02-fleet.md)) → token `mlpe_…` dùng một lần, hết hạn 10 phút.
2. Lệnh:
   - Linux/macOS: `curl -fsSL https://<cp>/install/agent.sh | sudo sh -s -- --token mlpe_… --data /datasets --models /models`
   - Windows (PowerShell quản trị): `iwr https://<cp>/install/agent.ps1 -UseBasicParsing | iex; Install-MlpAgent -Token mlpe_… -Data C:\mlp\datasets -Models C:\mlp\models`
3. Script: kiểm/cài Docker (Linux: Docker Engine + NVIDIA Container Toolkit nếu có GPU; Windows: WSL 2 + Docker Desktop; macOS: Docker Desktop), tải binary agent đúng RID (kiểm SHA-256 + chữ ký Ed25519), đăng ký service, khởi động agent với token.
4. Agent mở kênh, gửi `ConnectionHello{enroll}` (token + `HostInfo` + public key WireGuard) → nhận `EnrollResult{host_id, credential mlpa_…, overlay_ip, wg_peer, ray_head, node_image}` và lưu credential (quyền 600 / ACL chỉ SYSTEM).
5. Agent khởi chạy container `mlp-net` (WireGuard) → kéo image `mlp-node` qua overlay → khởi chạy Ray worker → báo `SetupStage` từng bước: `agent_installed → network_configured → image_pulled → ray_node_started → connected`.
6. Chạy lại lệnh cài trên cùng máy: agent gửi credential cũ → **giữ nguyên host_id và overlay_ip**.

## 3. Kênh điều khiển
- Agent mở **TCP+TLS** tới `<cp>:7443`, frame đầu là `ConnectionHello{register | enroll | data_channel}` trong 5 s.
- Khung: varint length + message protobuf (`WriteDelimitedTo` / `ParseDelimitedFrom`).
- Envelope: `AgentToControl` / `ControlToAgent`, mỗi frame có `stream_id`; bên mở trao đổi cấp id; `0` = cấp kết nối.
- Heartbeat mỗi 15 s; 3 lần im → đóng; mọi byte đọc được tính là hoạt động; luồng tắc 15 s bị huỷ.
- Nối lại: backoff 2, 4, 8, 16, 30 s rồi 10 s, ±30 % jitter.
- Trên stream 0, agent gửi `HostMetrics` mỗi 15 s (kèm `ContainerSample` từng container và `GpuSample`), `HostInfo` khi có thay đổi, `NodeStatus` ngay sau hello và khi container đổi trạng thái, `LogChunk` của container workload liên tục (≤ 64 KB hoặc 1 s).
- **Tự hồi phục** ([20/10 §4.7](10-quan-sat-va-khoi-phuc.md)): agent chạy dưới systemd/launchd/Windows Service; container workload độc lập với tiến trình agent. Khởi động lại → liệt kê container `mlp.attempt=*`, báo `NodeStatus.running_workloads`, gắn lại `docker wait` và luồng log. Sự kiện chưa gửi được (`WorkloadExit`, `LogChunk`) ghi vào hàng đợi trên đĩa và gửi lại khi kết nối lại. Api trả attempt không còn hoạt động → agent dừng container đó (fencing).

### 3.1 Lệnh (`ControlToAgent.command`, mỗi lệnh một luồng, có `CommandProgress` và `CommandResult`)

| Lệnh | Tác dụng |
|---|---|
| `StartRayNode` / `StopRayNode` / `RestartRayNode` | Container Ray worker với tài nguyên + label (mục 5) |
| `Drain` / `Undrain` | Ngừng nhận việc mới (đặt resource `schedulable=0` qua restart node có điều kiện) |
| `StartWorkload` / `StopWorkload` | Container recipe cho một attempt: image digest, **GPU UUID** (holder báo qua `RuntimeService.ReportAllocation`), CPU/RAM, `--network container:mlp-net`, env (token, spec URI, rank, master_addr), mount dữ liệu/model/Model Zoo, cổng (ModelServer, rendezvous) |
| `StartNetwork` / `RestartNetwork` | Container `mlp-net` với cấu hình WireGuard từ `EnrollResult` (mục 5) |
| `StartNotebook` / `StopNotebook` / `SnapshotHome` | Container notebook (M08) |
| `BrowsePath` | Liệt kê thư mục con của `data_path` |
| `IndexDataset` | Chạy ingest cục bộ cho dataset `host_local` (M04) |
| `PrepullImages` | Kéo trước image recipe/notebook hay dùng khi rảnh; dọn image không dùng khi đĩa < 20 % |
| `BuildImage` | `docker buildx build --platform <đích>` + push: environment notebook (M08) hoặc recipe của workspace (job `build_recipe`, push `recipes/ws-{ws}/…`, D-30) |
| `CollectLogs` | Gửi log agent + container lên kho S3 |
| `UpdatePaths` | Đổi `data_path`/`models_path` |
| `Upgrade` | Cập nhật agent kiểu blue/green, không bao giờ khi có việc |
| `Uninstall` | Gỡ service, container (kể cả `mlp-net`); giữ dữ liệu người dùng |
| `Disconnect` | Đóng kênh, xoá credential |

Agent thoát mã **75** khi cần khởi động lại → service manager bật lại ngay.

### 3.2 Data channel
1. api gửi `OpenDataChannel{session_id, token, target}`; `target` ∈ `notebook{server_id}`, `host_file{path, range}`, `container_logs{container}`.
2. Agent mở kết nối TCP+TLS mới tới `:7443`, gửi `ConnectionHello{data_channel: DataChannelHello{session_id, token}}`.
3. api trả `DataChannelAccept`; hai bên chuyển sang byte thô; agent nối tới `127.0.0.1:<port>` của đích.
4. Mỗi host tối đa 64 data channel đồng thời; token dùng một lần, hết hạn 30 s.

Nhờ data channel, notebook và file server trên PC **chỉ lắng nghe 127.0.0.1**; control plane không gọi vào PC qua overlay. File server chạy **trong tiến trình agent** trên loopback của máy thật (container không với tới), chỉ phục vụ `data_path`, mỗi request kèm token HMAC một lần (`host_file{path, range, token}`) do api cấp sau khi kiểm quyền.

## 4. HostInfo & HostMetrics

```proto
message HostInfo {
  string machine_name = 1;
  OsFamily os_family = 2;          // LINUX / WINDOWS / MACOS
  string os_description = 3;       // "Ubuntu 24.04.1 LTS", "Windows 11 Pro 24H2", "macOS 15.2"
  string os_version = 4;
  string kernel_version = 5;
  string architecture = 6;         // x64 / arm64
  string cpu_model = 7;
  int32 physical_cores = 8;
  int32 logical_cores = 9;
  uint64 memory_bytes = 10;
  repeated Disk disks = 11;        // mount, total, free, là ổ chứa data_path/models_path?
  repeated Gpu gpus = 12;          // vendor, model, uuid, vram_bytes, driver_version, cuda_version, compute_capability
  DockerInfo docker = 13;          // version, gpu runtime (nvidia/cdi/none), giới hạn CPU/RAM của VM Docker Desktop
  WslInfo wsl = 14;                // Windows: version, distro, giới hạn RAM .wslconfig
  OverlayInfo overlay = 15;        // overlay_ip, last_handshake
  string agent_version = 16;
  string runtime_identifier = 17;
  repeated NetworkAdapter adapters = 18;
  string public_ip = 19;
  repeated DoctorFinding doctor = 20;
}

message HostMetrics {               // mỗi 15 s
  google.protobuf.Timestamp at = 1;
  double cpu_percent = 2;
  uint64 memory_used_bytes = 3;
  repeated GpuSample gpus = 4;      // index, util %, vram_used, temp °C, power W
  repeated DiskSample disks = 5;
  uint64 net_rx_bytes = 6;
  uint64 net_tx_bytes = 7;
  repeated ContainerSample containers = 8;   // ray-node, notebook-*: cpu, ram, trạng thái
}
```
Nguồn đo: Linux `/proc` + NVML; Windows WMI/CIM + NVML; macOS `host_statistics`, `sysctl` (không GPU NVIDIA).

**Doctor** (khi khởi động và mỗi giờ): driver GPU, NVIDIA Container Toolkit/CDI, Docker chạy chưa, WSL 2, giới hạn RAM VM < 50 % RAM máy, WireGuard handshake, đĩa còn < 10 %, đồng hồ lệch > 2 s. Mỗi finding: `ok / fixed / warning / failed` + hướng dẫn.

## 5. Mạng và Ray node do agent khởi chạy (D-26)
```
docker run -d --name mlp-net --restart unless-stopped --cap-add NET_ADMIN \
  --sysctl net.ipv4.conf.all.src_valid_mark=1 \
  -p 127.0.0.1:9000-9099:9000-9099 \
  -v mlp-wg:/etc/wireguard <registry>/mlp-net:<version>      # wg-quick up wg0 (overlay_ip, peer hub)
```
Image `mlp-net` kéo qua `https://<cp>/v2/` (Nginx, chỉ đọc, credential `mlpa_`) vì lúc này chưa có overlay; mọi image sau kéo từ registry riêng qua overlay (D-31).

```
docker run -d --name mlp-ray-node --network container:mlp-net --restart unless-stopped \
  --gpus all|device=… --shm-size <30% RAM> \
  -v <data_path>:/mnt/data:ro -v <models_path>:/mnt/models -v mlp-cache:/mnt/cache \
  <registry>/mlp-node:<version>-<gpu|cpu> \
  ray start --block --address=10.88.0.1:6379 --node-ip-address=<overlay_ip> \
    --num-cpus=<logical-1 hoặc giới hạn VM> --num-gpus=<n> --memory=<bytes> \
    --resources='{"host:<host_id>":1,"pool:<pool>":1,"vram_gb":<tổng>}' \   # bundle xin 0.001 host/pool → nhiều việc cùng máy
    --labels="mlp.io/host-id=<id>,mlp.io/os=<linux|windows|macos>,mlp.io/os-version=<…>,mlp.io/runtime=<native|wsl2|docker-desktop>,mlp.io/gpu=<model>,mlp.io/vram-gb=<n>,mlp.io/pool=<pool>"
```
Tài nguyên khai **tường minh** theo giới hạn thật (kể cả giới hạn VM Docker Desktop/WSL); không để Ray tự đoán.

**GPU cho workload:** agent chạy `mlp-ray-node` với `-e CUDA_VISIBLE_DEVICES=<UUID…>` (mọi GPU, hoặc chỉ GPU rảnh sau khi head khởi động lại — D-32), nên ID GPU Ray cấp cho holder là UUID; holder báo `ReportAllocation`; agent chạy container recipe với `--gpus device=<UUID,…>`. Trong container, GPU được đánh số lại `0..n-1` (`{gpu_ids_csv}`).

**Ray head khởi động lại (D-29, D-32):** khi `mlp-ray-node` mất head, agent **không** dừng container workload; giữ tối đa 15 phút (`workload_grace`) và báo `NodeStatus{head_lost, running_workloads[{attempt, gpu_uuids}]}`. Head lên lại trong hạn → api gửi `RestartRayNode{gpus: <UUID chưa bị giữ>}`; các attempt còn sống thành `detached`. Khi attempt `detached` cuối cùng của máy kết thúc và máy không còn holder → `RestartRayNode` với đủ GPU. Quá hạn → agent dừng container (SIGTERM, 60 s) và báo `CommandResult{reason: head_lost}`.

## 6. Ray có biết OS không? (chẩn đoán, đối chiếu tài liệu Ray hiện hành)

| Nguồn Ray | Có | OS? |
|---|---|---|
| State API `list_nodes` | node_id, node_ip, is_head_node, state, state_message, node_name, resources_total, labels, start/end time | Không |
| `ray.nodes()` | thêm `NodeManagerHostname` | Chỉ hostname |
| Metrics `ray_node_*` | CPU, RAM (`mem_*`, `mem_*_host`, `cgroup_mem_*`), GPU (util, temp, power, GRAM; nhãn `GpuDeviceName`, `GpuIndex`, `GpuUuid`), đĩa, mạng | Không |
| Label mặc định | `ray.io/node-id`, `ray.io/accelerator-type` | Không |
| Label tuỳ biến (`--labels`) | Do ta gắn | Có, nếu agent gắn |

Ray worker chạy trong container: trên Linux, Ray thấy OS của image chứ không phải distro host; trên Windows (WSL 2) và macOS (Docker Desktop), Ray thấy **kernel và giới hạn CPU/RAM của VM**, macOS không có GPU.

**Kết luận** (D-12): agent là nguồn sự thật về máy; Ray là nguồn sự thật về lập lịch; nối bằng `host:<id>` / `mlp.io/host-id`. Control plane đối chiếu mỗi 30 s:

| Lệch | Hành động |
|---|---|
| Agent online, Ray node không có | Gửi `StartRayNode`; 3 lần thất bại → host `degraded` |
| Ray node ALIVE, agent offline > 5 phút | Coi như mất host |
| `HostInfo.gpus` > 0 nhưng `resources_total.GPU` = 0 | Doctor: thiếu NVIDIA Container Toolkit/CDI |
| RAM VM < 50 % RAM máy | Doctor: hướng dẫn sửa `.wslconfig`/Docker Desktop |
| Label Ray khác `HostInfo` (đổi GPU, nâng OS) | `RestartRayNode` khi rảnh |

## 7. Cập nhật & gỡ
- Kiểm bản mới khi khởi động và mỗi 10 phút lúc rảnh; tải binary RID, kiểm chữ ký Ed25519; chạy bản `next` song song, bản mới kết nối thành công thì thay bản cũ (blue/green); thất bại → giữ bản cũ.
- Image `mlp-node` nâng cấp cuốn chiếu theo lệnh từ control plane, sau khi head đã lên phiên bản Ray mới.
- `Uninstall`: dừng container (kể cả `mlp-net`), gỡ service; không xoá `data_path`, `models_path`.
