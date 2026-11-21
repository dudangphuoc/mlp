# 20/10 — Quan sát tiến trình nền và khôi phục việc dở

> **Quyết định:** D-35 (mô hình quan sát chung), D-36 (khôi phục bằng checkpoint hai lớp) · **Liên quan:** [M02](../40-module/M02-fleet.md), [M03](../40-module/M03-scheduler.md), [M08](../40-module/M08-notebook.md), [M09](../40-module/M09-tracking.md), [M16](../40-module/M16-notifications.md), [20/04](04-agent.md), [20/08](08-recipe-va-runtime.md), [20/09](09-van-hanh.md)

Việc nền (train, chuyển dữ liệu, build image, serve, notebook, việc định kỳ của control plane) có thể chạy nhiều ngày trên PC của người dùng. Độ tin cậy của nền tảng phụ thuộc vào hai điều:
1. **Thấy được:** người dùng luôn biết việc của mình đang ở bước nào, còn sống không, bao giờ xong, và hệ thống có đang ổn không.
2. **Không mất công:** máy mất điện, agent chết, OOM… thì phần việc đã xong được giữ lại và việc tiếp tục từ đó.

Tài liệu này là nguồn chuẩn cho cả hai. Các module chỉ tham chiếu tới đây, không định nghĩa lại.

## 1. Nguyên tắc

| # | Nguyên tắc | Hệ quả |
|---|---|---|
| N1 | **Mọi việc nền là một tiến trình quan sát được, cùng một mô hình** | Job (train/eval/export/serve/notebook/convert/build), lệnh gửi agent và việc định kỳ của control plane đều có: bước, tiến độ, nhịp tim, sự kiện, log |
| N2 | **Im lặng là tin xấu** | Quá hạn nhịp tim → UI ghi "mất tín hiệu", không giữ "running" |
| N3 | **Luôn kèm lý do và bước hiện tại** | "Chờ: cần 1 GPU ≥ 12 GB, hiện 0 trống", "Tải dữ liệu 812/2 000 file", "OOM ở epoch 14" |
| N4 | **Có nguồn gốc** | Mỗi sự kiện ghi nguồn: `api`, `scheduler`, `agent:<máy>`, `holder`, `shim`, `ray` — biết lỗi ở tầng nào |
| N5 | **Việc đã xong không làm lại** | Bước pipeline cache theo nội dung; train tiếp tục từ checkpoint bền gần nhất |
| N6 | **Không bao giờ hai bản cùng ghi một run** | Fencing bằng token workload (mục 4.5) |

## 2. Mô hình quan sát (D-35)

### 2.1 Dòng sự kiện vận hành
Bảng chỉ-thêm `ops.events` (thay `sched.job_events` của M03), giữ 90 ngày:

| Cột | Ý nghĩa |
|---|---|
| id, at | UUIDv7, thời điểm ở nguồn (agent/shim gửi kèm; api ghi thêm `received_at`) |
| workspace_id | null với sự kiện toàn instance |
| source | `api` · `scheduler` · `agent:<host_id>` · `holder` · `shim` · `ray` · `system:<vòng nền>` |
| resource_type, resource_id | `job`, `attempt`, `host`, `notebook`, `deployment`, `run`, `system` |
| kind | `job.queued`, `attempt.reserving`, `image.pulled`, `data.downloaded`, `checkpoint.durable`, `attempt.suspect`, `attempt.node_lost`, `attempt.resumed`, `progress.stalled`, `host.offline`, `system.loop_stalled`… |
| severity | `info` · `success` · `warning` · `error` |
| message | Tiếng Việt, đọc được ngay, ví dụ "Tải checkpoint epoch 6 (1,2 GB, 42 giây)" |
| data jsonb | Chi tiết máy đọc (mã thoát, step, kích thước…) |

Dòng thời gian trên UI, thông báo M16 và báo cáo sự cố đều đọc từ bảng này. Audit (M06) vẫn ghi riêng hành động của người dùng.

### 2.2 Bước và tiến độ chuẩn
Mọi job đi qua cùng một danh sách bước; bước nào không áp dụng thì bỏ qua:

```
queued → scheduling → reserving (placement group) → pulling_image (MB, %) → preparing_data (n/N file, MB/s)
→ restoring_checkpoint (khi tiếp tục) → running (đơn vị của recipe: epoch/step, %, tốc độ, ETA)
→ uploading (model, checkpoint cuối) → finishing
```

- `RuntimeService.ReportProgress{phase, current, total, unit, percent, rate, rate_unit, eta_seconds, message}` — shim gửi khi đổi bước và tối đa 1 lần/5 s trong bước.
- Bước `running`: recipe khai `entrypoints.train.progress` ([20/08 §3](08-recipe-va-runtime.md)), ví dụ `{metric: epoch, total: "{params.epochs}", unit: epoch}`. Shim tính % và ETA từ metric nó vốn đã đọc — **không sửa repo**. Recipe không khai → hiện thời gian đã chạy và metric mới nhất, không có %.
- ETA = trung bình trượt tốc độ 10 phút gần nhất; hiện khoảng ("3–4 giờ") khi dao động lớn.

### 2.3 Nhịp tim ba tầng và bộ phát hiện bất thường

| Tầng | Nhịp | Quá hạn | Trạng thái hiện |
|---|---|---|---|
| agent → api | 15 s ([20/04 §3](04-agent.md)) | 45 s | máy **mất kết nối**; attempt trên máy → `suspect` (4.4) |
| shim → api (`Heartbeat{phase, percent, last_log_at, gpu_util, vram_used}`) | 30 s | 2 phút, trong khi agent vẫn online | job **mất tín hiệu** |
| tiến độ thực | — | không đổi `current`, không log mới, GPU ≈ 0 % trong `progress.stallAfterMin` (mặc định 30 phút, recipe ghi đè) | job **có thể bị treo** |

Cảnh báo sớm (sự kiện `warning` + thông báo M16): VRAM ≥ 98 % kéo dài 1 phút (sắp OOM), đĩa máy < 10 %, 2 attempt liên tiếp thất bại, checkpoint bền cuối cũ hơn 2 × chu kỳ checkpoint.

Xử lý:
- Mất tín hiệu shim mà agent còn báo container sống → chỉ cảnh báo. Quá 30 phút → dừng container và thử lại (M03 "Runtime treo").
- Có thể bị treo → chỉ cảnh báo, kèm nút `Dừng & tiếp tục từ checkpoint`. Không tự dừng: nhiều bước hợp lệ im lặng lâu (đánh giá cuối, lưu model lớn).

### 2.4 Log liên tục
Agent theo dõi stdout/stderr container (`docker logs -f`), gửi `LogChunk` theo khối ≤ 64 KB hoặc 1 s; khi mất kết nối thì đệm ra đĩa (giới hạn 100 MB/attempt) và gửi bù. Api giữ 2 000 dòng gần nhất/attempt trong bộ nhớ, lưu toàn bộ lên kho S3 theo chunk 1 MB ([M03 §3](../40-module/M03-scheduler.md)).

### 2.5 Metric máy và container
Agent gửi `HostMetrics` mỗi 15 s, gồm `ContainerSample` từng container (`docker stats`) và `GpuSample` (`nvidia-smi`). Api gắn mẫu GPU với attempt đang giữ GPU đó (theo `gpu_uuids` của attempt) → tab Hệ thống của job và `system_metrics` của run (M09). Giữ chi tiết 7 ngày, gộp theo phút 90 ngày.

### 2.6 Sức khoẻ hệ thống
Hai nhóm, mỗi mục có trạng thái `ok/degraded/down`, lần kiểm cuối, thông điệp:

| Nhóm | Mục | Cách kiểm |
|---|---|---|
| Thành phần | PostgreSQL, kho S3, Ray head, kênh agent :7443, registry, SMTP | `/health/ready` ([20/09 §1](09-van-hanh.md)) mỗi 15 s |
| Vòng nền | scheduler (2 s), thăm dò deployment (5 s), dọn notebook rảnh (60 s), đồng bộ catalog, `sync_images`, dọn rác hằng đêm, outbox | mỗi vòng ghi `last_run_at`, thời lượng, kết quả; quá 3 × chu kỳ không chạy → `down` + sự kiện `system.loop_stalled` |

Sự cố mở → banner toàn cục cho mọi người trong workspace bị ảnh hưởng, nói rõ hệ quả ("Ray head mất kết nối — job mới tạm xếp hàng, job đang chạy không bị ảnh hưởng"). Chi tiết kỹ thuật (độ trễ, lỗi) chỉ Admin thấy. Người vận hành vẫn dùng OpenTelemetry/Grafana (20/09 §4); trang này là góc nhìn cho người dùng.

### 2.7 Một kênh đẩy: `WatchWorkspace`
Một server stream mỗi tab trình duyệt (`WorkspaceService.WatchWorkspace`) thay cho mỗi trang một `Watch*`:
- Loại bản tin: `job_progress`, `job_state`, `attempt_state`, `host_state`, `notification`, `system_health`, `event` (dòng sự kiện của tài nguyên đang mở).
- Client đăng ký quan tâm thêm (`subscribe{resource}`) khi mở trang chi tiết để nhận log và sự kiện của riêng tài nguyên đó.
- Gộp: tiến độ mỗi job tối đa 1 bản tin/s; nối lại thì gửi ảnh chụp trạng thái hiện tại trước.
- Backend: `StreamBroker` nhận từ outbox + LISTEN/NOTIFY của PostgreSQL ([20/02](02-backend-dotnet-aot.md)), chạy được khi có nhiều bản sao api.

## 3. Bề mặt UI

| Mã | Route / vị trí | Nội dung |
|---|---|---|
| **S-00-3** | Khay "Đang chạy" trên thanh trên (mọi trang) | Mọi tiến trình của tôi: tên, bước, thanh %, ETA; chấm đỏ khi lỗi/treo/mất tín hiệu. Bấm → chi tiết |
| **S-03-2** (bổ sung) | `/w/{ws}/jobs/{job}` | Thanh bước (2.2) có thời lượng từng bước; **ô sức khoẻ** ("nhịp tim 4 giây trước · log 2 giây trước · GPU 97 % · 3,1 it/s · ETA 4 giờ 12"); **khối Điểm khôi phục** (4.2); dòng thời gian đa nguồn tô màu theo nguồn; bảng attempt (máy, lý do, tiếp tục từ checkpoint nào, thời gian mất); banner `suspect` có đếm ngược |
| **S-02-5** | `/w/{ws}/fleet?tab=map` | Bản đồ cụm: mỗi máy một thẻ, từng GPU đang bị job/notebook/replica nào giữ, container đang chạy, nhịp tim agent, "đang khôi phục n job". Bấm GPU → job |
| **S-OPS-3** | `/w/{ws}/status` | Trạng thái hệ thống (2.6): đèn từng mục, sự cố đang mở và 30 ngày gần nhất |
| S-09-x (bổ sung) | Biểu đồ metric của run | Liền một mạch qua các attempt, có vạch "attempt 2 tiếp tục từ epoch 6" |
| S-03-1 (bổ sung) | Tab Hàng đợi | Vị trí, lý do chờ cụ thể, ước tính bắt đầu |

Khi control plane vừa mất kết nối rồi có lại, UI ghi "dữ liệu trễ do mất kết nối từ 10:02" thay vì hiện như việc đã dừng; sự kiện agent/shim đệm được gửi bù và chèn đúng thời điểm ở nguồn.

## 4. Khôi phục việc dở (D-36)

### 4.1 Hai tầng "bước"
1. **Bước pipeline** (`convert`, `cache_weights`, `build_recipe`, train, eval, export, đăng ký): đầu ra định danh theo nội dung và cache (M03 R7 `Ensure*`); chạy lại bỏ qua bước đã có đầu ra.
2. **Bước trong train** (epoch/step): khôi phục bằng checkpoint (4.2).

### 4.2 Checkpoint hai lớp

| Lớp | Ở đâu | Dùng khi |
|---|---|---|
| Local | Volume `mlp-run-{run}` trên máy, mount vào `{output}`; còn sau khi container chết và máy reboot | Tiếp tục **trên cùng máy**, không phải tải |
| Bền | Kho S3 `mlp-artifacts/{ws}/runs/{run}/checkpoints/{step}/` | Tiếp tục **trên máy khác** |

Quy trình của shim:
1. Theo dõi glob `checkpoints` của recipe; file không đổi kích thước 10 s → coi là ghi xong.
2. Đăng ký ngay checkpoint local (`RegisterCheckpoint{location: local}`), rồi upload nền lên S3 (multipart, sha256), xong thì `RegisterCheckpoint{location: durable}`. Chỉ checkpoint `durable` dùng được để tiếp tục trên máy khác.
3. Ghi trạng thái riêng `{output}/.mlp/state.json` (checkpoint đã upload, metric đã gửi tới step nào) để chạy lại tại chỗ không gửi trùng.
4. Giữ 3 checkpoint bền mới nhất + checkpoint `best` (theo metric chính); phần còn lại dọn theo 20/09 §5.

Recipe khai thêm ([20/08 §3](08-recipe-va-runtime.md)):
- `resumeFidelity`: `exact` (đủ optimizer/scheduler/RNG), `epoch` (chạy lại từ đầu epoch), `none` (không tiếp tục được).
- `checkpointEveryMin` (gợi ý; nền tảng khuyến nghị ≤ 30 phút cho recipe đặt được chu kỳ lưu).

Recipe không có `checkpoints` hoặc `resumeFidelity: none` → hộp Train báo trước "sập sẽ chạy lại từ đầu".

**Khối Điểm khôi phục** trên S-03-2: checkpoint bền cuối (epoch, tuổi, kích thước), checkpoint local cuối, "nếu sập lúc này mất khoảng X phút", `resumeFidelity` của recipe.

### 4.3 Phân loại sự cố và cách xử lý

| Sự cố | Dấu hiệu | Xử lý tự động | Mất |
|---|---|---|---|
| Chỉ tiến trình agent chết, container còn | Mất kênh agent; agent lên lại báo `NodeStatus.running_workloads` còn attempt | Agent nhận lại container (4.7), attempt về `running` | 0 |
| Mất mạng tới control plane | Agent và shim im rồi quay lại | Như trên; gửi bù sự kiện/metric/log đã đệm (M03 R6) | 0 |
| Máy mất điện/reboot, quay lại trong thời gian chờ | Agent lên lại, container không còn | Tiếp tục **trên cùng máy** từ checkpoint mới hơn giữa local và bền | tới checkpoint cuối |
| Máy chết hẳn / offline quá thời gian chờ (mặc định 5 phút) | Hết thời gian chờ | `node_lost` → attempt mới trên máy phù hợp khác, từ checkpoint bền | tới checkpoint bền cuối |
| OOM (137) | `WorkloadExit{reason: oom}` | **Không** tự thử lại (M03 R4); đưa nút "Tiếp tục từ epoch N với batch nhỏ hơn", điền sẵn tham số | tới checkpoint cuối |
| Ray head khởi động lại | Đã có (M03 R6b, D-32) | `detached` | 0 |
| Lỗi người dùng / recipe (2/3) | Mã thoát | Không thử lại; chỉ ra log | — |
| Shim treo (mất tín hiệu > 30 phút, container còn) | 2.3 | Dừng container, thử lại từ checkpoint | tới checkpoint cuối |

Ngân sách thử lại tự động: 3 attempt/job (đã có), giãn cách 30 s, 2 phút, 5 phút. Hết ngân sách → `failed`, giữ checkpoint để tiếp tục thủ công (`ResumeJob`).

### 4.4 Trạng thái attempt

```
running ──mất tín hiệu agent──► suspect (đếm ngược thời gian chờ)
  ▲                                │ agent quay lại, container còn ──────────────► running
  │                                │ agent quay lại, container mất ──► resuming (cùng máy)
  │                                └ hết giờ / "Chuyển máy ngay" ───► node_lost ──► resuming (máy khác)
  └──────────────── resuming: kéo image → tải/khôi phục checkpoint → running (attempt n+1)
```

Trong `suspect`: GPU của máy đó vẫn tính là bị giữ (không cấp cho việc khác), người dùng có nút `Chuyển máy ngay` / `Chờ thêm 15 phút` / `Huỷ`.

### 4.5 Chống chạy song song (fencing)
Khi bỏ attempt cũ (`node_lost`, chuyển máy, huỷ), api **thu hồi token `mlpj_`** của attempt đó trong cùng transaction tạo attempt mới:
- Metric, checkpoint, tiến độ gửi bằng token cũ bị từ chối (`SCHED_ATTEMPT_FENCED`).
- Máy cũ quay lại: agent gửi danh sách container đang chạy; attempt không còn hoạt động → api gửi `StopWorkload`, agent dừng container và giữ volume local để dọn sau.

### 4.6 Tiếp tục đúng nghĩa
1. Attempt mới nhận `MLP_RESUME_FROM` (presigned GET checkpoint bền, hoặc đường dẫn volume local nếu cùng máy và local mới hơn).
2. Shim khôi phục vào `{checkpoint}` rồi áp `resume` của recipe ([20/08 §5.2](08-recipe-va-runtime.md)).
3. Dữ liệu: dataset version bất biến, bản chuyển định dạng có cache → không làm lại.
4. Metric cùng run: bảng `metrics` có thêm `attempt`; điểm của attempt cũ có step lớn hơn step của checkpoint được đánh dấu `superseded` và ẩn khỏi biểu đồ mặc định. Ghi metric idempotent theo `(run, key, step, attempt)`.
5. Thư mục model chỉ do attempt thành công tạo ra.

### 4.7 Agent tự hồi phục
- Agent chạy dưới trình quản lý dịch vụ của OS (systemd / launchd / Windows Service) với tự khởi động lại.
- Container workload **độc lập** với tiến trình agent (agent chết thì container vẫn chạy).
- Khi khởi động: liệt kê container nhãn `mlp.attempt=*`, gửi `NodeStatus.running_workloads` ngay sau hello, gắn lại `docker wait` và luồng log, tiếp tục gửi metric container.
- Sự kiện chưa gửi được (`WorkloadExit`, `LogChunk`, `SetupStage`) ghi vào hàng đợi trên đĩa (`<state>/spool/`) và gửi lại theo thứ tự khi kết nối lại.

### 4.8 Server sập hoàn toàn: Fleet tự chủ (D-40)
Control plane (api, Ray head, DB) có thể mất hàng giờ. Trong lúc đó **mỗi máy tiếp tục việc của mình, không giới hạn thời gian**; khi server sống lại, không việc nào bị phạt vì khoảng thời gian server vắng mặt.

| Trên máy, khi server mất | Hành vi |
|---|---|
| Container workload (train, serve, notebook) | Tiếp tục chạy; agent không dừng gì khi mất kênh |
| Agent | Nối lại theo backoff vô hạn; log và sự kiện container ghi đệm ra đĩa, gửi bù theo thứ tự |
| Shim | Đệm metric; checkpoint vẫn ghi local (upload bền thử lại sau); **train xong mà chưa gọi được server → giữ kết quả trên volume của run và thử upload lại** (backoff tới 60 s, tối đa `MLP_UPLOAD_WAIT_HOURS`, mặc định 24 giờ) thay vì báo lỗi |
| `mlp-ray-node` | Tự nối lại Ray head khi head sống lại (D-32) |

| Khi server khởi động lại (deploy, crash, mất điện) | Hành vi |
|---|---|
| Đồng hồ giám sát | **Đặt lại từ thời điểm khởi động** trước khi các vòng nền chạy: thời gian chờ máy (`suspect_until`), nhịp tim shim, tiến độ đứng yên, notebook rảnh, replica thử rảnh |
| Attempt đang chạy | Chuyển `suspect` với thời gian chờ `MLP__Recovery__RestartGraceMinutes` (mặc định 10 phút) cho agent nối lại và báo container thật sự đang chạy → `running`, không mất gì |
| Máy | Hiện `offline` với lý do "đang chờ agent nối lại" tới khi agent kết nối (vài giây); không sinh thông báo "máy offline" |
| Container cũ của attempt đã bị thay thế thoát muộn | Bỏ qua: không đổi trạng thái job, run, deployment, notebook |

Giới hạn đã biết khi server mất: không gọi được suy luận qua gateway công khai và không mở được notebook qua proxy (cả hai đi qua server); job mới không được xếp lịch; máy reboot trong lúc server mất thì container không tự chạy lại (việc tiếp tục từ checkpoint local khi server trở lại).

## 5. Hợp đồng (bổ sung vào proto)

| Service | Thay đổi |
|---|---|
| `RuntimeService` | `Heartbeat` thêm `phase, percent, last_log_at, gpu_util, vram_used`; `ReportProgress` theo 2.2; `RegisterCheckpoint{step, epoch, location (local/durable), uri, size_bytes, sha256, metrics}` |
| `SchedulerService` | `ResumeJob{job_id, checkpoint_id?, param_overrides}` (tiếp tục thủ công, kể cả sau OOM); `MoveAttemptNow{job_id}`; `ExtendGrace{job_id, minutes}`; `ListCheckpoints{job_id}`; `ListEvents{resource, after}` |
| `WorkspaceService` | `WatchWorkspace` (2.7); `GetSystemStatus` (2.6) |
| Kênh agent | `NodeStatus` gửi ngay sau hello; `LogChunk` theo 2.4; `HostMetrics.containers` bắt buộc khi có container |

## 6. Lỗi

| reason | status | Thông điệp |
|---|---|---|
| `SCHED_RESUME_NOT_SUPPORTED` | FAILED_PRECONDITION | Recipe này không hỗ trợ tiếp tục từ checkpoint |
| `SCHED_CHECKPOINT_NOT_FOUND` | NOT_FOUND | Không có checkpoint bền để tiếp tục |
| `SCHED_ATTEMPT_FENCED` | FAILED_PRECONDITION | Lần chạy này đã bị thay thế bởi lần chạy mới |
| `SCHED_NOT_SUSPECT` | FAILED_PRECONDITION | Job không ở trạng thái chờ máy quay lại |

## 7. Thông báo (M16)
Thêm loại: `job.recovering` (chuyển máy/tiếp tục), `job.resumed`, `job.recovery_failed`, `job.stalled`, `job.signal_lost`, `job.oom` (kèm nút tiếp tục), `host.disk_low`, `system.incident` (Owner/Admin + người có việc bị ảnh hưởng).

## 8. Nghiệm thu (kịch bản tích hợp, D-19)
Recipe đồ chơi của kịch bản ghi checkpoint mỗi epoch và khai `progress`.
- [x] Trong khi train: thanh bước đi đúng thứ tự; % và ETA cập nhật ≤ 2 s trên UI qua `WatchWorkspace`; log realtime < 2 s. _(kịch bản 20/10)_
- [x] Kill tiến trình agent → job không gián đoạn; agent lên lại nhận lại container, log/metric chạy tiếp, dòng thời gian có "agent mất kết nối" và "agent nhận lại container". _(kịch bản D-36, 2026-11-18)_
- [x] `docker kill` container giữa chừng → tiếp tục trên cùng máy từ checkpoint local; metric nối tiếp, không trùng step; bảng attempt ghi điểm tiếp tục. _(kịch bản D-36)_
- [x] Tắt agent + container → `suspect` có đếm ngược → (`Chuyển máy ngay` hoặc hết giờ) `node_lost` → tiếp tục trên máy khác từ checkpoint bền. _(kịch bản D-36, 2 agent trên cùng Docker)_
- [ ] Máy cũ quay lại sau khi job đã chuyển → container cũ bị dừng; mọi lần ghi bằng token cũ bị từ chối `SCHED_ATTEMPT_FENCED`.
- [ ] OOM → không tự thử lại; `ResumeJob` với batch nhỏ hơn chạy tiếp từ checkpoint.
- [ ] Recipe `resumeFidelity: none` → hộp Train cảnh báo; sập thì chạy lại từ đầu và UI nói rõ.
- [ ] Container im lặng quá `stallAfterMin` → sự kiện + thông báo "có thể bị treo"; shim chết mà container sống quá 30 phút → tự dừng và thử lại.
- [ ] Dừng vòng scheduler (giả lập) → S-OPS-3 chuyển `down` sau 3 chu kỳ và hiện banner.
- [ ] Tắt hẳn control plane 30 phút khi một job train và một replica đang chạy → cả hai chạy tiếp; bật lại → attempt `suspect` rồi `running`, không attempt mới, không container nào bị dừng, metric/log được gửi bù.
- [ ] Train xong đúng lúc control plane tắt → shim chờ, upload model khi server trở lại, run `succeeded`.

## 9. Thứ tự hiện thực

> **Trạng thái (2026-11-18):** đã hiện thực bước 1–3 và phần lớn bước 4 — `ops.events` có nguồn; tiến độ chuẩn + nhịp tim shim; volume output theo run; checkpoint hai lớp + giữ 3 bản bền mới nhất; `MLP_RESUME_FROM`; `suspect` → `node_lost`, `MoveAttemptNow`, `ExtendGrace`; fencing; agent nhận lại container, giữ Ray node đúng máy (tên theo máy); **log liên tục** (`docker logs -f`, khối 64 KB/1 s, spool đĩa 100 MB/attempt, gửi bù, api giữ 2 000 dòng + chunk lên kho S3, bỏ trùng theo thời điểm nguồn); **metric container + GPU** mỗi 15 s gắn với attempt; **mất tín hiệu** (cảnh báo + thông báo; quá ngưỡng → dừng và thử lại) và **có thể bị treo** (`stallAfterMin`); **sức khoẻ** thành phần (PostgreSQL, S3, Ray head, kênh agent) + nhịp từng vòng nền, `system.loop_stalled`/`component_down`; `WatchWorkspace` (một stream/tab: việc đang chạy, hệ thống, sự kiện + log của job đang mở); web S-00-3 (khay Đang chạy), S-02-5 (bản đồ cụm), S-03-2 (log realtime, chuyển máy/chờ thêm), S-OPS-3 + banner sự cố. **Còn lại:** `StreamBroker` qua LISTEN/NOTIFY cho nhiều bản sao api (hiện hỏi DB mỗi giây), metric có cột `attempt` + `superseded`, gộp metric theo phút và giữ 7/90 ngày, cảnh báo sớm VRAM/đĩa, OOM đề xuất batch nhỏ hơn, cảnh báo `resumeFidelity: none` trong hộp Train, hàng đợi spool cho `WorkloadExit`.

1. **Lõi:** volume output theo run; shim upload + đăng ký checkpoint; `MLP_RESUME_FROM`; metric có `attempt`; nhịp tim + `ReportProgress`; `ops.events`; log liên tục; agent nhận lại container khi khởi động.
2. **Khôi phục:** `suspect` + thời gian chờ; fencing; tiếp tục cùng máy / máy khác; `ResumeJob`; thanh bước + Điểm khôi phục trên S-03-2.
3. **Máy và kênh đẩy:** `HostMetrics` + container; S-02-5; S-00-3; `WatchWorkspace`.
4. **Hệ thống:** sức khoẻ vòng nền, S-OPS-3, banner sự cố; phát hiện treo; checkpoint theo thời gian; dọn checkpoint.

## 10. Tham số mặc định
Cấu hình được ở mức instance (`MLP__Recovery__*`) và ghi đè theo recipe khi có ghi chú:

| Tham số | Mặc định | Ghi chú |
|---|---|---|
| Thời gian chờ máy quay lại (`suspect`) | 5 phút | trùng ngưỡng `node_lost` của M03 |
| Checkpoint bền giữ lại | 3 mới nhất + `best` | |
| OOM | không tự thử lại; chỉ đưa nút tiếp tục | |
| Chu kỳ checkpoint khuyến nghị | ≤ 30 phút | recipe ghi đè bằng `checkpointEveryMin` |
| Ngưỡng "có thể bị treo" | 30 phút | recipe ghi đè bằng `progress.stallAfterMin` |
| Shim mất tín hiệu → tự dừng và thử lại | 30 phút | |
| Giữ sự kiện / log / metric máy chi tiết | 90 ngày / 90 ngày / 7 ngày (gộp phút 90 ngày) | |
