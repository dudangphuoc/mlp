# Nâng cấp agent: vận hành từ xa, GPU, ưu tiên GPU prod/dev

Trạng thái: **đã duyệt 2026-11-22** theo đề xuất:
- Q1: job dev đang chạy thì chạy tiếp, không dừng.
- Q2: terminal chạy root, chỉ owner được mở, bắt buộc nhập lý do.
- Q3: có làm service và log hệ thống, làm sau cùng.
- Q4: lưu bản ghi phiên terminal (input và output) vào S3, giữ 30 ngày.

## 0. Hiện trạng (đọc từ code)

- Kênh agent: một kết nối TLS tới :7443 (`ControlToAgent`/`AgentToControl`, `AgentCommand` oneof) cộng **kênh dữ liệu** (`OpenDataChannel` → agent mở TCP mới, `DataChannelHello`, rồi nối byte hai chiều). Hiện kênh dữ liệu chỉ nối tới cổng `127.0.0.1` cho notebook (`DataChannel.cs`, `DataChannelBroker.cs`). Api đã bật WebSocket (`NotebookProxy`).
- File: `BrowsePath` chỉ liệt kê trong `data_path`. `FileRead/Write/Delete` chỉ chạy trong thư mục state của agent.
- GPU: `MetricsSampler` 15 s một lần gọi `nvidia-smi` lấy 5 trường (uuid, util, VRAM dùng, nhiệt độ, công suất). `RecordHostMetrics` lưu **bản mới nhất** dạng JSON và gán util/VRAM cho attempt **theo GPU được cấp**, không theo mức dùng thật. Không có lịch sử theo GPU, không có VRAM tổng trong mẫu, không biết tiến trình nào chiếm GPU, không có cảnh báo. Trang Fleet chỉ hiện chip "tên GPU · VRAM". Vì vậy không giúp ra quyết định được.
- Hai agent trên một máy: nhãn `mlp.agent=<hostId>` đã có (`HostOwnership`). Bộ cài cố định đường dẫn và tên service.
- Dự án tunnels (tham khảo UX): **không có PTY**, chỉ chạy script một lần (60 s). File browser dùng Monaco, chia khung, đọc 4 MiB/khối, ghi 1 MiB/khối, giới hạn editor 20 MiB, **không chặn đường dẫn**. Danh sách tiến trình: top 200 theo RAM, kill cả cây. Có quản lý service (systemd/Windows). Phân quyền Viewer < Operator < Admin. Thao tác nhạy cảm bắt nhập lý do. Thao tác file không được audit.

Ta lấy UX của tunnels, nhưng bổ sung bốn điểm mà tunnels không có: PTY thật, giới hạn thư mục gốc, audit thao tác ghi, và ghi lại phiên terminal.

## 1. Vận hành từ xa

### 1.1 Terminal tương tác (PTY)

```
trình duyệt (xterm.js) ──WS /api/hosts/{id}/terminal──▶ api ──OpenDataChannel{terminal}──▶ agent
                                                            ◀── kênh dữ liệu TCP+TLS ──── PTY (bash / pwsh)
```

- Proto: thêm `TerminalTarget { string shell; uint32 cols; uint32 rows; string cwd; }` vào `OpenDataChannel.target`.
- Khung byte trên kênh dữ liệu (cả WS lẫn TCP): `[1 byte loại][u32 độ dài][payload]`. Các loại:
  - `0`: dữ liệu;
  - `1`: resize, payload là cols/rows u16;
  - `2`: thoát, payload là exit code i32;
  - `3`: ping.
- Agent mở PTY theo hệ điều hành, tất cả bằng P/Invoke, AOT-safe, không thêm thư viện:
  - Linux/macOS: `openpty` cộng `posix_spawn` với `POSIX_SPAWN_SETSID`, slave làm stdin/out/err, `ioctl(TIOCSWINSZ)` khi resize. Không dùng `fork()` trong .NET.
  - Windows: ConPTY (`CreatePseudoConsole`, `ResizePseudoConsole`), shell mặc định `powershell.exe`.
- Giới hạn:
  - tối đa 4 phiên mỗi máy, 2 phiên mỗi người;
  - đóng phiên khi không có I/O 30 phút;
  - đóng phiên khi WS đứt (phiên không sống tiếp, không chạy nền).
- Audit:
  - bản ghi `terminal.open` / `terminal.close` gồm người dùng, máy, shell, thời lượng, số byte;
  - api lưu **bản ghi phiên** (luồng output, nén, ≤ 5 MB mỗi phiên) vào S3 để xem lại.
- Web: tab **Terminal** trong slide-over của máy, dùng `@xterm/xterm` + `addon-fit`.

### 1.2 Trình duyệt file

- Thư mục gốc được phép:
  - `data_path`, `models_path`, thư mục state của agent;
  - các thư mục thêm bằng tham số `--fs-root` khi cài.
- Mọi đường dẫn đều được chuẩn hoá (`GetFullPath`, giải symlink bằng `ResolveLinkTarget`), rồi kiểm tra tiền tố có dấu phân cách cuối. Ra ngoài thì trả `FLEET_PATH_OUTSIDE_ROOT`.
- Lệnh mới trong `AgentCommand`:
  - `FsList`;
  - `FsStat`;
  - `FsRead` (offset + length, ≤ 4 MiB);
  - `FsWrite` (offset, ≤ 1 MiB, offset 0 thì tạo hoặc ghi đè);
  - `FsMkdir`;
  - `FsMove` (không ghi đè);
  - `FsDelete` (đệ quy, cần cờ xác nhận).
- `BrowsePath` cũ giữ nguyên để không phá tương thích.
- Tải xuống/lên file lớn đi qua kênh dữ liệu, stream bằng HTTP `GET/PUT /api/hosts/{id}/files?path=`. Proto đã khai báo sẵn `HostFileTarget`, nhưng chưa có code nào xử lý: agent bỏ qua mọi target không phải notebook. Vì vậy phải làm mới hai phần:
  - agent: phân nhánh theo loại target;
  - broker: hàm chung `OpenAsync(target)`.
- Web:
  - cột điều hướng kiểu tunnels: breadcrumb, danh sách, kéo-thả để upload;
  - editor CodeMirror 6, nhẹ hơn Monaco; giới hạn 20 MiB, file nhị phân chỉ cho tải xuống;
  - panel tiến trình truyền file.
- Audit: mọi thao tác ghi (`write`, `mkdir`, `move`, `delete`, `upload`) đều có bản ghi: đường dẫn, kích thước, sha256 sau khi ghi.

### 1.3 Tiến trình

- `ProcessList`: pid, ppid, tên, user, CPU %, RSS, thời điểm bắt đầu, dòng lệnh rút gọn. Top 300 theo CPU hoặc RAM.
- Cột **container**: trên Linux đọc `/proc/<pid>/cgroup` rồi tra nhãn Docker; trên Windows để trống.
- Cách đo CPU %: lấy hai mẫu cách nhau 500 ms.
- `ProcessKill {pid, signal: term|kill, tree}`.
- Agent từ chối kill chính nó, PID 1 và dockerd.
- Kill được audit.

### 1.4 Thêm từ tunnels (đề xuất, chờ duyệt)

- **Service**: liệt kê và thao tác start/stop/restart.
  - Phạm vi: systemd và Windows Service.
  - Quyền: chỉ admin.
  - Audit: có.
- **Log hệ thống**: `journalctl -u <unit> -n` / Event Log, chỉ đọc.
- **Không làm**: remote desktop, chạy assembly.

### 1.5 Quyền

Mọi request mới đều mang `[RequiresPermission]`:

| Quyền | Request |
|---|---|
| `fleet.ops.read` | ProcessList, FsList/Stat/Read, tải xuống, xem log, service list |
| `fleet.ops.write` | FsWrite/Mkdir/Move/Delete, upload, ProcessKill, service start/stop |
| `fleet.terminal` | mở terminal (mặc định chỉ admin) |

## 2. Giám sát GPU (làm lại từ đầu)

### 2.1 Thu thập (agent)

Agent gọi `nvidia-smi --query-gpu=…` mỗi 5 s. Cùng một tiến trình được giữ cho cả Linux và Windows, dùng `-lms 5000` để không phải fork lại mỗi lần. Các trường lấy:

- index, uuid;
- utilization.gpu, utilization.memory;
- memory.used, memory.total;
- temperature.gpu;
- power.draw, power.limit;
- clocks.sm, clocks.max.sm;
- pstate, fan.speed;
- clocks_throttle_reasons.active;
- ecc.errors.uncorrected.volatile.total;
- pcie.link.gen.current.

Thêm `--query-compute-apps=gpu_uuid,pid,used_memory` mỗi 15 s. Từ pid suy ra container (Linux: qua cgroup). Trên Windows/WSL không có pid thật, nên dùng GPU được cấp trong spec của container (`--gpus device=`) và đánh dấu "ước tính".

Proto: `GpuSample` thêm trường (không đổi số trường cũ) và thêm `repeated GpuProcess processes`:

```
GpuProcess { gpu_uuid; pid; container; attempt_id; owner_agent; used_memory_bytes; }
```

`owner_agent` lấy từ nhãn `mlp.agent`. Nhờ vậy dev nhìn thấy cả container của prod.

Máy không có GPU, hoặc `nvidia-smi` thiếu hay lỗi: `HostMetrics.gpu_state`:

- `NONE`: không có GPU;
- `DRIVER_ERROR` kèm thông báo: trước đây có GPU, giờ `nvidia-smi` lỗi. Trường hợp này phát cảnh báo "mất GPU".

### 2.2 Lưu trữ (control plane)

- Bảng `host_gpu_samples (host_id, gpu_uuid, at, util, mem_util, vram_used, vram_total, temp, power, power_limit, sm_clock, throttle, ecc)`:
  - mẫu thô giữ 24 h;
  - gộp 1 phút giữ 30 ngày, do job dọn dẹp chạy mỗi giờ.
- Bảng `host_gpu_processes`: chỉ giữ ảnh chụp mới nhất.
- `attempt_metrics` dùng **VRAM thật của các tiến trình trong container attempt**, thay cho tổng VRAM của GPU được cấp.

### 2.3 Cảnh báo

Dùng lại kênh notify sẵn có, có độ trễ chống nhiễu 2 phút:

| Điều kiện | Mức |
|---|---|
| nhiệt độ ≥ 85 °C | warn |
| nhiệt độ ≥ 92 °C | crit |
| VRAM ≥ 95 % trong 2 phút | warn |
| throttle do nhiệt hoặc công suất (HW slowdown) | warn |
| ECC uncorrected tăng | crit |
| GPU biến mất / driver lỗi | crit |
| GPU được cấp cho job nhưng util = 0 % trong 30 phút | info, gợi ý job treo |

### 2.4 Web

Trang **GPU**, đồng thời là tab trong slide-over của máy, dựng bằng echarts có sẵn.

- **Lưới thẻ GPU**: thanh util, VRAM dùng/tổng, nhiệt độ, công suất/giới hạn, xung SM/max, trạng thái throttle, các chip cảnh báo.
- **Bảng "ai đang dùng"**: container → job/attempt → VRAM, kèm cột môi trường (prod/dev, theo `owner_agent`).
- **Biểu đồ lịch sử**: chọn 1 h / 24 h / 7 d / 30 d; các đường util, VRAM, nhiệt độ, công suất; vạch đánh dấu lúc job bắt đầu và kết thúc.
- **"VRAM còn trống để xếp lịch"** cho từng GPU: tổng, trừ VRAM đang dùng thật, trừ VRAM đã đặt chỗ. Con số này giúp quyết định có nhét thêm job vào được không.

## 3. Ưu tiên GPU prod > dev (phương án b)

### 3.1 Bộ cài `--instance`

- `agent.sh --instance dev`:
  - `PREFIX=/opt/mlplatform-agent-dev`, `STATE=/var/lib/mlplatform-agent-dev`;
  - service `mlplatform-agent-dev`;
  - launchd `io.mlplatform.agent.dev`.
- `agent.ps1 -Instance dev`: `C:\ProgramData\MLPlatform-dev`, task "MLPlatform Agent (dev)".
- Không có tham số thì giữ nguyên đường dẫn hiện tại, để các máy đang chạy không bị ảnh hưởng.
- Agent ghi `Instance` vào `AgentOptions`.
- Lệnh cài sinh từ control plane tự kèm `--instance` khi cấu hình `Fleet:Instance` khác rỗng.
- Phạm vi lần này **chỉ gồm đường dẫn, service và ưu tiên GPU**. Phần overlay `10.89/16` và tiền tố tên container trong vps-plan §3 làm riêng.

### 3.2 Quy tắc

Agent có tuỳ chọn `GpuPriority = normal | yield`. Bản cài `--instance` khác rỗng mặc định là `yield`.

"Có GPU container lạ" nghĩa là: một container đang chạy có `HostConfig.DeviceRequests` chứa GPU, và có nhãn `mlp.agent` khác hostId của mình. Agent kiểm tra điều này bằng `docker ps` cộng `inspect` trong vòng lấy mẫu, thêm sự kiện docker start/die để cập nhật ngay.

Hai lớp chặn:

1. **Control plane dev, phòng ngừa.** `HostMetrics.gpu_yield = {active, foreign_containers}`. `SchedulerLoop.PickHostsAsync` bỏ qua máy đang `yield.active` cho job cần GPU. Job được requeue với lý do "Đang nhường GPU cho prod trên <máy>".
2. **Agent, chốt chặn chống race.** `ContainerApply` / `ContainerRunOnce` có `gpus` mà agent đang ở `yield` thì trả `FLEET_GPU_YIELDED`. `WorkloadLauncher` coi lỗi này là **requeue**, không làm fail attempt, và giải phóng phần cấp phát.

Job dev đã chạy rồi mà prod mới bắt đầu: **không dừng** (chờ duyệt, xem câu hỏi Q1). Web hiển thị chip "dev đang chờ GPU" ở trang Jobs của dev.

## 4. Kiểm chứng

- Proto: `buf lint` và `buf breaking` qua; chỉ thêm trường, thêm oneof case.
- Gates `gates/run.sh` xanh. AOT publish cả api, agent và shim.
- Scenario mới trên verify stack:
  - `ops-files`: list, ghi, đọc, move, xoá trong root; ghi ra ngoài root bị từ chối; có bản ghi audit;
  - `ops-terminal`: mở WS, gửi `echo mlp-ok`, nhận output, resize, đóng; có bản ghi audit;
  - `ops-processes`: liệt kê, kill tiến trình `sleep` do scenario tạo, từ chối kill PID 1;
  - `ops-permissions`: user không có quyền bị 403 trên từng request;
  - `gpu-metrics`: verify stack không có GPU, nên agent dùng `nvidia-smi` giả (biến `MLP_NVIDIA_SMI` trỏ tới script trả CSV mẫu). Kiểm tra lịch sử, tiến trình→attempt, cảnh báo nhiệt độ, trạng thái `DRIVER_ERROR`;
  - `gpu-priority`: hai agent (prod, dev) trên cùng một daemon Docker. Container GPU giả của prod đang chạy thì job GPU dev nằm hàng chờ với lý do nhường GPU. Prod dừng thì job dev chạy. Có ca race: lệnh apply bị agent trả `FLEET_GPU_YIELDED` và job được requeue chứ không fail.

  Container "GPU giả" không chạy được `--gpus` khi không có GPU. Cách xử lý: biến môi trường `MLP_FAKE_GPU=1` làm agent coi nhãn `mlp.gpus` như một DeviceRequest. Chỉ dùng trong verify.

## 5. Thứ tự làm (commit nhỏ)

1. Proto cùng khung kênh dữ liệu.
2. `--instance` trong bộ cài và `AgentOptions`.
3. GPU: agent, lưu trữ, cảnh báo, web.
4. Ưu tiên GPU và scenario hai agent.
5. File ops.
6. Tiến trình.
7. Terminal.
8. Service/log, nếu được duyệt.

## 6. Câu hỏi cần chủ dự án quyết

- **Q1.** Khi prod cần GPU mà job dev đang chạy: (a) để job dev chạy hết, prod vẫn chạy chung GPU (đề xuất); hay (b) dừng job dev (gửi SIGTERM, job dev quay lại hàng chờ)?
- **Q2.** Agent chạy bằng root/SYSTEM, nên terminal cũng có quyền root. Chấp nhận, chỉ admin được mở (đề xuất)? Hay hạ quyền xuống một user riêng `mlp-ops`?
- **Q3.** Mục 1.4 (service, log hệ thống): có làm đợt này không?
- **Q4.** Có lưu bản ghi output phiên terminal vào S3 không (đề xuất có, giữ 30 ngày)?

## 7. Chỉnh sửa sau review (ghi đè các mục trên nếu mâu thuẫn)

Review gồm hai phần: chuyên gia (bảo mật/hệ thống, GPU, UX) và đối chiếu với code thật.

### 7.1 Lỗi trong hiện trạng (§0)

- **Container của hai agent không tách nhau khi đo.**
  - `MetricsSampler` chỉ lọc `mlp.managed=1`, nên prod sẽ ghi metric cho attempt của dev.
  - `HostOwnership.Owns` coi container **không có nhãn** là của mình.
  - Hai lỗi này phải sửa trước §3.
- VRAM tổng đã có sẵn trong `HostInfo` (`SchedulerLoop.GpuTotalMb`).

### 7.2 Ưu tiên GPU (§3)

- **Ray quảng bá GPU hai lần.** Ray worker của mỗi agent chạy với `--num-gpus=N`, nên cả prod lẫn dev đều quảng bá cùng các GPU vật lý.
  - Khi dev đang nhường GPU, control plane dev bỏ qua máy đó cho job cần GPU (lớp 1).
  - Lớp 2 (agent trả `FLEET_GPU_YIELDED`) chỉ còn là chốt chặn khi xảy ra race.
- **Luồng mới cho `FLEET_GPU_YIELDED`.** Hiện nay mọi lỗi đều đi vào `FailAsync`, và `FinishAttempt` chỉ thử lại tối đa 3 lần với một số mã lỗi nhất định. Luồng mới:
  1. dừng các container đã apply cho rank trước;
  2. `ray.StopAsync` để giải phóng placement group;
  3. đánh dấu attempt `yielded`, **không tính** vào giới hạn 3 lần;
  4. `RequeueJobCommand`;
  5. với notebook và deployment, đặt trạng thái `waiting`.

### 7.3 Quyền (§1.5)

- Theo đúng kiểu đặt tên `resource.verb` trong `Permissions.cs`: `fleet.ops_read`, `fleet.ops_write`, `fleet.terminal`.
- Role tính theo workspace. Nếu cho workspace admin mở terminal thì họ có **shell root** trên máy. Vì vậy:
  - `fleet.terminal` và `fleet.ops_write` chỉ dành cho **owner**;
  - `fleet.ops_read` dành cho admin trở lên.

### 7.4 Audit

- `AuditBehavior` hiện chỉ lưu tên lệnh, kết quả và lý do. Thêm cột `details` (jsonb) bằng migration để lưu đường dẫn, kích thước, sha256, shell, thời lượng.
- Terminal chạy qua WebSocket, không phải command. Endpoint WS sẽ gửi tường minh hai command `OpenTerminalSession` và `CloseTerminalSession`, nhờ đó có bản ghi audit.
- Mở terminal bắt buộc nhập lý do. Bản ghi phiên lưu **cả hai chiều**: input và output.

### 7.5 Kỹ thuật

- **PTY Linux.** `posix_spawn` không gán được controlling TTY, nên Ctrl-C và job control sẽ hỏng. Thay bằng: spawn `setsid -c <shell>` có sẵn trong util-linux, với slave PTY làm stdio.
  - Không có `setsid` thì dùng `script -qfc`.
  - macOS chỉ để dev nội bộ.
- **ConPTY.** Đóng pipe phía con ngay sau `CreateProcess`.
- **Khung kênh dữ liệu.** Payload tối đa 64 KiB, vượt thì đóng kết nối. Resize dùng `u16`; proto cũng dùng `uint32` nhưng giá trị bị chặn ≤ 1000.
- **Path guard.**
  - Giải symlink cho **từng thành phần** đường dẫn. Mở file trước, rồi kiểm tra đường dẫn thật của handle (chống TOCTOU).
  - So sánh không phân biệt hoa thường trên Windows và macOS.
  - Dùng chung một mã lỗi `FLEET_PATH_OUTSIDE_ROOT`.

### 7.6 GPU (§2)

- Lấy mẫu 15 s (không phải 5 s). Mẫu thô giữ 24 h, mẫu gộp 1 phút giữ 30 ngày.
- Bỏ `pcie.link.gen.current`. Dùng `clocks_event_reasons.*`, tự rơi về `clocks_throttle_reasons.*` với driver cũ. Mọi trường đều có thể là `[N/A]` và được hiểu là "không có".
- Tiến trình `-lms` có watchdog: không nhận dòng nào trong 3 chu kỳ thì đánh dấu `DRIVER_ERROR` và khởi động lại với backoff.
- Ngưỡng nhiệt tính theo từng GPU, dựa vào `temperature.gpu.tlimit` (warn khi còn ≤ 5 °C, crit khi chạm). Card không có `tlimit` thì dùng 85/92 °C.
- **Cảnh báo.**
  - Thêm loại `gpu.hot`, `gpu.vram_full`, `gpu.throttle`, `gpu.ecc` (bắt buộc), `gpu.lost` (bắt buộc), `gpu.idle_allocated` vào `NotificationKinds`.
  - Chống nhiễu theo mẫu `SystemIncidents` có sẵn.
- **UI.** Không có chữ giải thích:
  - "job treo" hiển thị bằng badge `idle`;
  - job dev chờ GPU hiển thị bằng badge trạng thái `yielded`;
  - máy không có GPU hiển thị ô `no GPU`.

### 7.7 Kiểm chứng

- `MLP_NVIDIA_SMI` dùng cho cả `HostProbe` lẫn `MetricsSampler`. `MLP_FAKE_GPU=1` làm cả hai việc:
  - Ray worker nhận `--num-gpus` từ GPU giả;
  - Docker bỏ `--gpus`.
- Hai agent trên một daemon: scenario hiện có đã chạy được nhiều agent với `--state` riêng.
- Cột container lấy từ cgroup không kiểm thử được trên verify stack macOS, nên chỉ có unit test với file cgroup mẫu.

### 7.8 Bộ cài

- `--instance` đổi thêm các đường dẫn sau:
  - `C:\Program Files\MLPlatform`;
  - `C:\ProgramData\MLPlatform\docker`;
  - `AgentOptions.DefaultStateDir`;
  - tên unit/task trong resource OTA của `AgentUpgrader`.

## 8. Ghi chú khi làm (2026-11-22)

Các chỗ làm khác bản đã duyệt:

- **Editor file**: dùng `textarea` mono thay cho CodeMirror 6, để không thêm 5–6 gói vào web. Giới hạn 20 MB và cách đọc/ghi theo khối vẫn giữ như thiết kế.
- **`nvidia-smi`**: gọi mỗi 15 s, mỗi lần giới hạn 10 s. Không dùng `-lms`: lệnh treo thì sau 10 s bị kill và đọc ra lỗi, nên không cần watchdog riêng.
- **Phần GPU nằm trong module Ops** (`Application/Ops/Gpu`), bảng lưu ở schema `ops`. Lý do: CodeGate cấm Fleet (lớp Foundation) gọi lên Scheduling (lớp Core). Riêng cờ `gpu_yield` vẫn nằm trên `fleet.hosts`.
- **"Container GPU lạ"** không tính container hạ tầng (`mlp.role=ray-node|net`). Ray node của prod luôn chạy với `--gpus all`; nếu tính nó thì dev sẽ nhường GPU mãi mãi.
- **Terminal trên Unix**: đọc/ghi pty bằng `read`/`write` của libc (`FdStream`). `FileStream` của .NET làm hỏng input trên Linux: bash chỉ nhận được phần đuôi lệnh.
  - Linux: chạy `setsid -c`, đã thử trong container Linux: có prompt, resize, `tty`.
  - macOS (chỉ để dev): shell chạy trong session mới, không có controlling terminal.
  - Khi đóng phiên, gửi SIGHUP cho cả process group.
- **Terminal trên Windows (ConPTY)**: mới compile, chưa chạy thử trên máy Windows.

Cách kiểm chứng (verify stack chạy trên macOS):

- Scenario `Ops:*` và `GPU:*` nằm ở `tools/MLPlatform.Scenarios/RemoteOpsScenarios.cs` và `GpuScenarios.cs`.
- Không có GPU thật. Dùng `fixtures/fake-gpu/nvidia-smi` (biến `MLP_NVIDIA_SMI`) và `MLP_FAKE_GPU=1`: container nhận nhãn `mlp.gpus` thay cho `--gpus`.
- Cột container lấy từ cgroup chỉ chạy được trên Linux, nên verify stack không kiểm thử phần này.
