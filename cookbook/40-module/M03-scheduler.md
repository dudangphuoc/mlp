# M03 — Job Scheduler

> **Phase:** P1 · **Phụ thuộc:** M01, M02, M17 · **Quyết định:** D-04, D-05, D-15, D-21, D-24, D-29, D-30 · **Proto:** `scheduler.proto` (`SchedulerService`), `runtime.proto` (`RuntimeService`) · **Schema DB:** `sched`
> Recipe, shim, biến môi trường, mã thoát, phân tán: [20/08](../20-kien-truc/08-recipe-va-runtime.md).

## 1. Mục tiêu & phạm vi
Nhận yêu cầu chạy việc từ các module, xếp hàng theo ưu tiên/quota, giữ tài nguyên bằng **placement group + holder của Ray**, cho **agent chạy container của recipe** trên đúng GPU được cấp (D-21), theo dõi trạng thái/log, retry và resume khi máy chết.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Loại job: `ingest`, `convert` (chuyển định dạng dữ liệu), `export`, `train`, `eval`, `serve` (replica), `label_assist`, `batch_inference`¹, `notebook`, `notebook_run`, `build_env`, `build_recipe`, `cache_weights` (tải trọng số vào Model Zoo), `inspect_model`, `hf_import` | ✔ | | |
| Mỗi job chạy trong **image của recipe** tương ứng (job nền tảng như ingest/convert dùng image `mlp-tools`) | ✔ | | |
| Job một máy và nhiều máy (`torchrun`/`accelerate` của repo, rendezvous qua overlay; `internal` khi framework tự phân tán) | ✔ | tối ưu mạng LAN | `distributed: ray` (container recipe nối cụm Ray) |
| Hàng đợi ưu tiên, quota theo pool/workspace/người (job, GPU, notebook); GPU dùng qua Ray Client chỉ **quan sát + cảnh báo** (D-33) | ✔ | | |
| Phụ thuộc giữa job (`depends_on`): `convert` trước `train/eval`, `cache_weights` trước job cần trọng số, `build_recipe` trước job dùng recipe chưa build | ✔ | | |
| Cancel, timeout, retry, resume từ checkpoint | ✔ | | |
| `tune`, `data_pipeline` | | ✔ | |
| Preemption theo ưu tiên | | | ✔ |

¹ Nội bộ cho pre-label (M05); batch inference cho người dùng ở P2.

## 2. Khái niệm
- **Job**: yêu cầu chạy, có spec, ưu tiên, nguồn (`source_ref`, vd. `run:<id>`).
- **Attempt**: một lần chạy = một **placement group** Ray (một bundle mỗi máy) + một **holder** trong mỗi bundle + một **container** recipe mỗi máy do agent khởi chạy. `attempt.id` là tên placement group.
- **Placement**: pool, máy cụ thể, label, `min_vram_gb`, `data_locality`.

## 3. Dữ liệu (schema `sched`)

| Bảng | Cột chính |
|---|---|
| `jobs` | id, workspace_id, pool_id, kind, name, owner_id, source_ref, spec bytea (`JobSpec` proto), spec_uri, priority (0–100, mặc định 50), state, state_reason, max_attempts (3), timeout_s, created_at, queued_at, started_at, finished_at, attempt_count |
| `job_dependencies` | job_id, depends_on_job_id — job chờ ở `waiting` tới khi mọi phụ thuộc `succeeded`; phụ thuộc lỗi → job `failed (dependency_failed)` |
| `job_attempts` | id, job_id, number, placement_group_id, host_ids uuid[], gpu_uuids jsonb (theo máy, từ `ReportAllocation`), image_digest, state (`reserving/launching/running/suspect/resuming/detached/succeeded/failed/stopped`), exit_reason (`succeeded/failed/node_lost/head_lost/timeout/cancelled/oom/user_error/recipe_error/dependency_failed/fenced`), started_at, finished_at, resumed_from_checkpoint, suspect_since, phase, progress jsonb (bước + tiến độ gần nhất, [20/10 §2.2](../20-kien-truc/10-quan-sat-va-khoi-phuc.md)), last_heartbeat_at |
| (sự kiện) | Dùng chung `ops.events` ([20/10 §2.1](../20-kien-truc/10-quan-sat-va-khoi-phuc.md)) — mọi nguồn: api, scheduler, agent, holder, shim, ray |
| `job_log_chunks` | attempt_id, seq, uri (kho S3), lines, from_at, to_at |
| `job_checkpoints` | id, job_id, attempt_id, run_id, step, epoch, location (`local/durable`), uri, size_bytes, sha256, metrics jsonb, at, deleted_at — giữ 3 bền mới nhất + `best` ([20/10 §4.2](../20-kien-truc/10-quan-sat-va-khoi-phuc.md)) |
| `quotas` | id, pool_id, subject_type (`pool/workspace/user`), subject_id, max_running_jobs, max_gpus, max_notebooks, notebook_idle_timeout_min, ray_client_gpu_warn (ngưỡng cảnh báo, không chặn — D-33) — **nguồn duy nhất về giới hạn** (M02 pool, M08 notebook đọc ở đây) |

Log realtime: bộ đệm vòng 2 000 dòng/attempt trong bộ nhớ api; toàn bộ log lưu kho S3 theo chunk 1 MB.

## 4. Trạng thái & quy tắc

```
job:     waiting (phụ thuộc) → queued → scheduling → starting → running → succeeded
           ▲          │            │          │  ├─► failed
           │          │            │          │  └─► cancelled
           └──────────┴── retrying ┴──────────┘   (attempt mới)
attempt: reserving (placement group) → launching (agent kéo image, khởi container) → running → succeeded | failed | stopped
         running ─(Ray head khởi động lại, container còn sống)─► detached → succeeded | failed | stopped   (R6b)
         running ─(mất tín hiệu agent)─► suspect ─► running | resuming (cùng máy) | node_lost → resuming (máy khác)   (R5b)
```
Trạng thái lấy từ: placement group (Ray), container (agent báo `NodeStatus`), shim (heartbeat, mã thoát).

- **R1 Không chạy trên head**: bundle luôn xin ≥ 1 CPU (head có 0 CPU); ghim máy/pool bằng resource `host:<id>: 0.001` / `pool:<p>: 0.001` trong bundle (số lẻ để nhiều bundle cùng máy; node khai `1`); `STRICT_PACK` cho job một máy, `STRICT_SPREAD` cho job nhiều máy.
- **R1b Container theo recipe**: image = recipe build của job (M17); agent chạy container với đúng GPU id holder nhận được; container dừng → holder thoát → Ray giải phóng tài nguyên; holder bị huỷ → agent dừng container.
- **R2 Không đói**: không cho job ưu tiên thấp nhảy trước job ưu tiên cao đang chờ cùng loại tài nguyên, trừ khi job thấp chỉ cần CPU.
- **R3 Dữ liệu local-only** (`data_locality=required`): chỉ chạy trên máy chứa dữ liệu; máy đó offline → job chờ với lý do `data_host_offline`.
- **R4 Retry**: chỉ khi `node_lost`, `head_lost`, `timeout`, runtime treo; không retry `oom`, `user_error`, `recipe_error`. Tối đa 3 attempt, giãn cách 30 s / 2 phút / 5 phút. OOM và hết ngân sách → người dùng tiếp tục thủ công bằng `ResumeJob` (có thể đổi tham số).
- **R5 Resume** (D-36): attempt mới nhận `MLP_RESUME_FROM` = checkpoint mới nhất dùng được — checkpoint local nếu chạy lại trên cùng máy và local mới hơn, ngược lại checkpoint **bền** trên kho S3; job dừng chủ động (`CancelJob`, dừng notebook/deployment) không bao giờ thử lại. Chi tiết: [20/10 §4](../20-kien-truc/10-quan-sat-va-khoi-phuc.md).
- **R5b Mất tín hiệu máy**: attempt chuyển `suspect` trong thời gian chờ (mặc định 5 phút), GPU vẫn tính là bị giữ; agent quay lại còn container → `running`, mất container → tiếp tục cùng máy; hết giờ → `node_lost` → tiếp tục máy khác. Bỏ attempt cũ thì thu hồi token `mlpj_` của nó (fencing, `fenced`); máy cũ quay lại → agent dừng container cũ.
- **R6 Control plane mất**: container và holder tiếp tục chạy; shim đệm metric/log; khi api lên lại, đồng bộ theo `attempt.id` (placement group) và báo cáo của agent.
- **R6b Ray head khởi động lại (D-29, D-32)**: placement group và holder mất theo head, container vẫn chạy (agent giữ ≤ 15 phút). Head lên trong hạn → attempt mà agent còn báo container sống chuyển `detached` (giữ `gpu_uuids`, tính vào quota từ DB, không cần Ray); api gửi `RestartRayNode{gpus: UUID chưa bị giữ}` cho máy đó, nên Ray chỉ cấp GPU rảnh cho việc mới. Attempt `detached` vẫn báo heartbeat/metric/checkpoint qua shim như thường; huỷ → agent dừng container. Khi attempt `detached` cuối cùng của máy kết thúc và máy không còn holder → `RestartRayNode` đủ GPU. Quá 15 phút chưa có head → `head_lost` → retry/resume.
- **R7 Phụ thuộc tự tạo**: `SubmitJob` cho train/eval gọi `DataService.EnsureConverted(dataset_version, converter)` (M04) — có sẵn trong cache thì bỏ qua, chưa có thì tạo job `convert` (trên máy chứa dữ liệu nếu `host_local`) và thêm vào `depends_on`. Tương tự `CatalogService.EnsureWeights` (M17) → `cache_weights`, `EnsureBuild` → `build_recipe`.
- **R8 Quota tính cả** notebook đang mở (GPU giữ bởi holder) và attempt `detached`. GPU dùng qua Ray Client (D-33) không chặn được: scheduler đọc Ray State API (tài nguyên đang dùng trừ phần holder của nền tảng), hiển thị ở S-03-3 và cảnh báo khi vượt `ray_client_gpu_warn`; khi tính chỗ trống, scheduler trừ phần này để không xếp việc lên GPU đang bị chiếm.
- **R9 Build theo kiến trúc**: `build_recipe`/`build_env` ghim máy cùng kiến trúc đích (`linux/amd64` mặc định) hoặc dùng `buildx --platform`.

| Tình huống | Phát hiện | Xử lý |
|---|---|---|
| Agent mất kết nối | Quá 45 s không nhịp tim | `suspect` (R5b) |
| Máy offline > 5 phút / Ray node chết | Hết thời gian chờ `suspect` + placement group mất bundle | `node_lost` → `retrying` → resume trên máy khác từ checkpoint bền |
| OOM | Runtime thoát mã 137 / dấu OOM | `failed` + nút "Tiếp tục từ checkpoint với batch nhỏ hơn" (`ResumeJob`) |
| Lỗi người dùng | Runtime thoát mã 2 + `MLP_ERROR` | `failed (user_error)` ngay |
| Lỗi recipe | Runtime thoát mã 3 | `failed (recipe_error)` ngay; thông báo người phụ trách recipe |
| Ray head khởi động lại | Mất placement group, agent báo `head_lost` kèm workload còn sống | R6b (`detached`) |
| Timeout | Vượt `timeout_s` | Dừng container + xoá placement group, `failed (timeout)` |
| Runtime treo | Shim không heartbeat 30 phút trong khi agent báo container còn sống | Dừng + retry từ checkpoint |
| Có thể bị treo | Tiến độ/log/GPU đứng yên quá `progress.stallAfterMin` | Chỉ cảnh báo + nút `Dừng & tiếp tục` ([20/10 §2.3](../20-kien-truc/10-quan-sat-va-khoi-phuc.md)) |

- **R1b Giữ chỗ lại sau khi head khởi động lại (2026-11-20)**: workload sống tiếp khi Ray head chết (D-40) nhưng placement group thì mất, nên head mới coi mọi CPU/GPU là trống. `ReservationKeeper` (trong vòng xếp lịch, 10 s/lần) so các attempt `running`/`suspect` với Ray Jobs; attempt nào Ray không còn biết (hoặc holder đã dừng) thì nộp lại `holder.py --rehold` với đúng CPU/RAM và **đúng UUID GPU** đã cấp (lấy từng GPU, giữ cái trùng UUID, trả cái thừa; thử lại ~10 phút khi máy chưa nối lại). Không báo `ReportAllocation`, không chạy lại container. Trong lúc Ray không tới được và 20 s sau một lần giữ lại, vòng xếp lịch **không nhận job mới**. Sự kiện `attempt.reheld` trên timeline của job. Tối đa 5 lần nộp lại mỗi attempt.

## 5. Luồng chính
1. Module gọi `SubmitJob(spec, request_id)` → tạo phụ thuộc (R7) → `waiting` hoặc `queued`.
2. `SchedulerService` (vòng lặp 2 s) lấy job theo `priority desc, queued_at asc`, kiểm quota, lấy ảnh chụp tài nguyên (Ray State API + trạng thái máy từ M02), lọc máy thoả placement.
3. Không đủ → giữ `queued` + lý do ("cần 1 GPU ≥ 12 GB, hiện 0 trống").
4. Đủ → tạo attempt, ghi spec lên kho S3, cấp token `mlpj_`, tạo placement group + holder qua Ray (job điều phối nhỏ submit bằng Ray Jobs API, chạy trên worker).
5. Holder gọi `RuntimeService.ReportAllocation(attempt, host, gpu_uuids)` → api gửi lệnh `StartWorkload` tới agent từng máy (image recipe, GPU UUID, env, mount, vai trò rank) → agent khởi container `--network container:mlp-net` với entrypoint `mlp-shim`.
6. Shim báo heartbeat, tiến độ, checkpoint qua `RuntimeService`; log container đi qua agent → bộ đệm + `WatchJob`.
7. Job nhiều máy: shim rank 0 công bố `master_addr:port` (IP overlay); shim các máy khác chờ rồi chạy lệnh phân tán của recipe.
8. Kết thúc → agent dừng container, holder thoát, placement group bị xoá → sự kiện cho module nguồn.

## 6. Hợp đồng

### `SchedulerService`
| RPC | Quyền | Mô tả |
|---|---|---|
| `SubmitJob` | `job.submit` (thường gọi nội bộ từ module) | `request_id` idempotency |
| `ListJobs` / `GetJob` | `job.submit` hoặc chủ job; Viewer: không | Lọc kind, trạng thái, owner, máy |
| `WatchJob` / `WatchJobs` | như trên | Trạng thái, sự kiện, dòng log mới |
| `CancelJob` / `RetryJob` | `job.cancel` / `job.submit` | |
| `ResumeJob` | `job.submit` | Tiếp tục từ checkpoint (mặc định mới nhất), tuỳ chọn đổi tham số |
| `MoveAttemptNow` / `ExtendGrace` | `job.submit` | Khi `suspect`: chuyển máy ngay / chờ thêm |
| `ListCheckpoints` / `ListEvents` | như GetJob | Điểm khôi phục, dòng sự kiện đa nguồn |
| `UpdateJobPriority` | Admin | |
| `GetJobLogs` | như GetJob | Đoạn log hoặc presigned URL toàn bộ |
| `GetQueue` | `job.submit` | Hàng đợi theo pool + lý do chờ |
| `ListQuotas` / `SetQuota` / `DeleteQuota` | đọc: Editor; ghi: Admin | S-03-3 |

### `RuntimeService` (token `mlpj_`)
`Heartbeat` (bước, %, log cuối, GPU), `ReportAllocation` (holder: GPU UUID theo máy), `ReportProgress` (bước chuẩn + tiến độ, [20/10 §2.2](../20-kien-truc/10-quan-sat-va-khoi-phuc.md)), `RegisterCheckpoint` (local/bền), `ReportEvent`, `ReportExit` (mã thoát + `MLP_ERROR`); cùng service này có các RPC ghi hàng loạt cho job (`AddItems`, `ReportStats`, `CompleteImport` — M04; `AddAnnotations`, `AddSuggestions`, `SetPriorityScores` — M05). Metric/artifact của run đi qua `TrackingService` (M09) với cùng token.

## 7. Màn hình

### S-03-1 Jobs — `/w/{ws}/jobs`
```
┌ Jobs ──────────────────────────── [Tab: Tất cả │ Đang chạy (2) │ Hàng đợi (3) │ Lỗi]┐
│ [Tìm…] Loại [Tất cả▾] Người tạo [Tất cả▾] Máy [Tất cả▾]                            │
│ Tên            │ Loại    │ Trạng thái    │ Máy       │ GPU │ Chờ  │ Chạy  │ Người │ │
│ deimv2-s-640   │ train   │ ● running 42% │ pc-01     │ 1   │ 0:12 │ 1:03h │ an    │⋯│
│ prelabel-pcb   │ label…  │ ◌ queued      │ —         │ 1   │ 3:20 │ —     │ binh  │⋯│
│                │         │  cần 1 GPU ≥12GB, hiện 0 trống                          │ │
│ ingest-coco    │ ingest  │ ✔ succeeded   │ pc-02     │ 0   │ 0:01 │ 4:10  │ an    │⋯│
└────────────────────────────────────────────────────────────────────────────────────┘
```
Tab **Hàng đợi** sắp theo pool, hiện thứ tự và lý do chờ; Admin kéo đổi ưu tiên (`UpdateJobPriority`). `⋯`: Huỷ, Thử lại, Xem log.

### S-03-2 Chi tiết job — `/w/{ws}/jobs/{job}`
- `PageHeader`: tên, loại, trạng thái, nguồn (link run/import/notebook), nút `Huỷ`, `Thử lại`.
- Tab **Tổng quan**: thanh bước (thời lượng từng bước), **ô sức khoẻ** (nhịp tim, log cuối, GPU, tốc độ, ETA), **khối Điểm khôi phục** (checkpoint bền/local cuối, việc có thể mất nếu sập lúc này), spec (xem YAML), tài nguyên, placement, dòng thời gian sự kiện đa nguồn, bảng attempt (số, máy, kết quả, lý do, tiếp tục từ checkpoint nào, thời gian mất). Khi `suspect`: banner đếm ngược + `Chuyển máy ngay` / `Chờ thêm` / `Huỷ` ([20/10 §3](../20-kien-truc/10-quan-sat-va-khoi-phuc.md)).
- Tab **Console**: `LogViewer` theo attempt, realtime.
- Tab **Hệ thống**: biểu đồ CPU/RAM/GPU của máy trong thời gian chạy (từ M02).
- Banner lỗi khi `failed`: thông điệp trích từ runtime + gợi ý (OOM → giảm batch).

- Tab **Phụ thuộc** (khi có): danh sách job `convert`/`cache_weights`/`build_recipe` đứng trước, trạng thái, link.

Các trang module (run, import, notebook) nhúng thẻ trạng thái job và link tới đây.

### S-03-3 Quota — `/w/{ws}/jobs?tab=quotas`
Bảng: phạm vi (pool / workspace / người), pool, job chạy đồng thời, GPU tối đa, notebook tối đa, timeout notebook rảnh, ngưỡng cảnh báo GPU Ray Client, đang dùng (thanh). Khối riêng "GPU đang dùng ngoài nền tảng (Ray Client)": máy, số GPU, notebook/người dùng ứng viên (D-33). `+ Thêm quota` → `FormDialog`. Chỉ Admin sửa; Editor xem.

## 8. Sự kiện
`sched.JobQueued`, `JobWaiting`, `JobStarted`, `JobStateChanged`, `JobSucceeded`, `JobFailed`, `JobCancelled`, `AttemptNodeLost`.

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `SCHED_SPEC_INVALID` | INVALID_ARGUMENT | Cấu hình job không hợp lệ (chi tiết từng trường) |
| `SCHED_QUOTA_EXCEEDED` | RESOURCE_EXHAUSTED | Vượt giới hạn của pool |
| `SCHED_NO_ELIGIBLE_HOST` | FAILED_PRECONDITION | Không máy nào trong pool đáp ứng (kể cả khi rảnh) |
| `SCHED_DATA_HOST_OFFLINE` | UNAVAILABLE | Máy chứa dữ liệu đang offline |
| `SCHED_ALREADY_FINISHED` | FAILED_PRECONDITION | Job đã kết thúc |
| `SCHED_RAY_UNAVAILABLE` | UNAVAILABLE | Cụm Ray tạm không sẵn sàng |
| `SCHED_DEPENDENCY_FAILED` | FAILED_PRECONDITION | Bước chuẩn bị (chuyển dữ liệu/tải trọng số/build) lỗi |
| `SCHED_ALLOCATION_MISMATCH` | INTERNAL | Holder báo GPU không thuộc danh sách Ray được phép dùng trên máy |
| `SCHED_RESUME_NOT_SUPPORTED` / `SCHED_CHECKPOINT_NOT_FOUND` / `SCHED_ATTEMPT_FENCED` / `SCHED_NOT_SUSPECT` | — | Xem [20/10 §6](../20-kien-truc/10-quan-sat-va-khoi-phuc.md) |

## 10. Nghiệm thu
- [ ] Job `min_vram_gb: 20` không bao giờ đặt lên máy GPU 12 GB; pool không có máy đủ → `SCHED_NO_ELIGIBLE_HOST` ngay khi gửi.
- [ ] Tắt nguồn PC đang train → job tự chạy lại trên PC khác từ checkpoint gần nhất, metric nối tiếp cùng run.
- [ ] Các kịch bản khôi phục và quan sát ở [20/10 §8](../20-kien-truc/10-quan-sat-va-khoi-phuc.md) đạt (agent chết, container chết, máy quay lại muộn, fencing, OOM, treo).
- [ ] Huỷ job → container dừng ≤ 30 s, placement group bị xoá, GPU được giải phóng.
- [ ] Hai job của hai recipe yêu cầu phiên bản torch/CUDA khác nhau (chọn bất kỳ hai recipe trong catalog) chạy cùng lúc trên cụm, mỗi job trong image riêng, không xung đột.
- [ ] Train trên dataset version chưa chuyển định dạng → job `convert` tự xuất hiện ở tab Phụ thuộc, chạy trước; lần train thứ hai dùng lại cache.
- [ ] Khởi động lại container `ray-head` khi đang train → container train không dừng; attempt thành `detached`, job mới trên cùng máy chỉ nhận GPU còn rảnh; train xong → `mlp-ray-node` khởi động lại với đủ GPU.
- [ ] Notebook giữ 1 GPU và job train 1 GPU chạy cùng lúc trên máy 2 GPU (resource `host:` số lẻ).
- [ ] Recipe thoát mã 3 → `failed (recipe_error)`, không retry.
- [ ] Quota `max_running=2` được tôn trọng; cùng ưu tiên thì vào trước chạy trước.
- [ ] Khởi động lại api khi 3 job đang chạy → không job nào dừng; trạng thái đồng bộ ≤ 30 s.
- [ ] Console realtime trễ < 2 s; log đầy đủ tải được sau khi job kết thúc.
