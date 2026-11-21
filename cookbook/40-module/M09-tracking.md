# M09 — Experiment Tracking & Train

> **Phase:** P1 (MLflow-compatible P2) · **Phụ thuộc:** M01, M03, M04, M11, M17 · **Quyết định:** D-20, D-21, D-23 · **Proto:** `tracking.proto` (`TrackingService`) · **Schema DB:** `track`
> Train chạy bằng **recipe** của bất kỳ họ model nào ([M17](M17-model-catalog.md), [20/08](../20-kien-truc/08-recipe-va-runtime.md)); tracking không phụ thuộc framework.

## 1. Mục tiêu & phạm vi
Khởi chạy train/eval với bất kỳ recipe nào trên fleet và ghi lại mọi lần chạy (tham số, code, môi trường, dữ liệu, metric, artifact, log) để so sánh và tái lập.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Project → run; train/eval từ UI với **mọi recipe verified** — dựng sẵn hay nhập từ repo, không giới hạn họ model (D-25) | ✔ | | |
| Thu metric không cần sửa repo: TensorBoard, regex log, file kết quả (qua shim) | ✔ | | |
| SDK Python (`mlp.init/log/…`) cho script tự viết, notebook, máy ngoài fleet | ✔ | | |
| Metric theo step, system metrics, console, artifact, checkpoint, resume | ✔ | | |
| So sánh run **khác recipe** trên cùng dataset version | ✔ | | |
| Tái lập run (recipe + commit + build digest + biến thể + tham số + dataset version) | ✔ | | |
| API tương thích MLflow | | ✔ | |
| Sweep/HPO | | ✔ (M10) | |

## 2. Khái niệm
- **Project**: nhóm run cùng mục tiêu (vd. "Phát hiện lỗi PCB"); không gắn với một framework.
- **Run**: một lần train/eval; ghi recipe, biến thể, build digest.
- **Metric chính**: `primaryMetric` của task trong [`tasks.yaml`](../../recipes/_tasks/tasks.yaml) — vd. detect `val/mAP50-95`, segment `val/mask_mAP50-95`, classify `val/accuracy`, text-classify/ner `val/f1`, generate `val/loss`, asr `val/wer`, tts `val/loss` (kèm nghe mẫu), tabular-classify `val/f1_macro`, tabular-regress `val/rmse`. Recipe ánh xạ khoá riêng về tên chuẩn bằng `metrics.map` ([20/08 §5.3](../20-kien-truc/08-recipe-va-runtime.md)); khoá không ánh xạ giữ tên gốc. Eval ghi khoá `test/…` (cùng hậu tố).

## 3. Dữ liệu (schema `track`)

| Bảng | Cột chính |
|---|---|
| `projects` | id, workspace_id, slug, name, description, task, visibility, created_by |
| `runs` | id, project_id, workspace_id, name, kind (`train/eval/notebook/external`), state, job_id, user_id, **recipe_id, recipe_build_id, variant**, task, params jsonb, tags, notes_md, git jsonb (cho script/notebook), env jsonb (image_digest, python, packages_uri), inputs jsonb (dataset_version, label_set, base_model_version, pretrained_weights), summary jsonb, primary_metric_key, primary_metric_value, license jsonb, started_at, ended_at, heartbeat_at, resumed_count |
| `metrics` (hypertable) | run_id, key, step, attempt, at, value, superseded — điểm của attempt cũ có step lớn hơn checkpoint dùng để tiếp tục bị đánh dấu `superseded` và ẩn khỏi biểu đồ mặc định; biểu đồ có vạch ranh giới attempt ([20/10 §4.6](../20-kien-truc/10-quan-sat-va-khoi-phuc.md)) |
| `system_metrics` (hypertable) | run_id, host_id, at, cpu_pct, mem_used, gpu_index, gpu_util, gpu_mem_used, gpu_temp, gpu_power |
| `artifacts` | id, run_id, path, kind (`checkpoint/model/plot/table/audio/file/notebook`), uri, size_bytes, sha256, metadata |
| `console_chunks` | run_id, seq, uri, lines, from_at, to_at |

Giới hạn P1: ≤ 2 000 metric key/run, ≤ 10 triệu điểm/run, artifact ≤ 50 GB/run.

## 4. Trạng thái & quy tắc
```
created → running → finished | failed | killed ; running → crashed (mất heartbeat 30 phút) ; resume → running
```
- **R1** Run từ UI luôn gắn một job (M03) chạy container của recipe; run ngoài fleet không có job, không có recipe (kind `external`).
- **R2** Chỉ recipe `verified` (M17 R1); task của dataset phải thuộc recipe (M17 R8).
- **R3** Run train chỉ nhận **dataset version** (M04).
- **R4** Base model: trọng số pretrained của biến thể, hoặc model version trong Registry **cùng recipe** (fine-tune tiếp).
- **R5** Form tham số sinh từ schema `params` của recipe; giá trị ngoài khoảng bị chặn kèm lỗi từng trường.
- **R6** Recipe có cảnh báo license → phải xác nhận trước khi train (M17 R5); run ghi license.
- **R7** Ghi vào run đã kết thúc bị từ chối; resume giữ nguyên run.

## 5. Luồng chính
1. **Train từ UI**: chọn dataset version → recipe (lọc theo task) → biến thể → điểm xuất phát (pretrained / model trong Registry / từ đầu — chỉ khi recipe khai `train.fromScratch: true`, [20/08 §4.2](../20-kien-truc/08-recipe-va-runtime.md)) → tham số → tài nguyên (gợi ý theo `min_vram_gb` của biến thể, số máy nếu recipe hỗ trợ phân tán) → `StartTraining` → tạo run + job `train` (M03) → M03 tạo phụ thuộc `convert` (`EnsureConverted`), `cache_weights`, `build_recipe` nếu cần (tab Phụ thuộc của job) → shim tải dữ liệu đã chuyển định dạng, chạy lệnh train của repo, thu metric/checkpoint/artifact → kết thúc → tuỳ chọn tự đăng ký model version (M11).
2. **Eval**: chọn model version + dataset version → job `eval` bằng lệnh eval của recipe → run kind `eval`.
3. **Script/notebook/ngoài fleet**: SDK
```python
import mlplatform as mlp
run = mlp.init(project="pcb-defects", name="my-segformer-512",
               config={"epochs": 50, "lr": 6e-5}, dataset="mlp://vision/datasets/pcb@v4")
for step, batch in enumerate(loader):
    ...
    mlp.log({"train/loss": loss}, step=step)
mlp.log_artifact("out/model.safetensors", kind="model")
mlp.finish()
```
Gửi bất đồng bộ theo lô, đệm khi mất mạng, tự ghi git, `pip freeze`, máy, GPU, system metrics.

## 6. Hợp đồng (`tracking.proto` — `TrackingService`)

| RPC | Quyền | Mô tả |
|---|---|---|
| `ListProjects` / `GetProject` / `CreateProject` / `UpdateProject` / `DeleteProject` | đọc: `run.read`; ghi: `run.write` | |
| `ListRuns` / `GetRun` / `WatchRun` | `run.read` | Lọc trạng thái, recipe, biến thể, tag; sắp theo metric chính |
| `StartTraining` / `StartEvaluation` | `job.submit` | Recipe + biến thể + điểm xuất phát + tham số + tài nguyên; `request_id` |
| `CreateRun` / `UpdateRun` / `FinishRun` / `KillRun` / `DeleteRun` | `run.write` | SDK. Đã có: `UpdateRun` (đổi tên), `DeleteRun` (chỉ run đã kết thúc và chưa có version model nào đăng ký từ nó; xoá metric, danh sách file và file trên kho) |
| `StopRun` | `job.submit` | Người dùng dừng run đang chờ/đang train: run → `cancelled`, container được 30 giây để lưu checkpoint rồi dừng, GPU được trả lại |
| `LogMetrics` / `LogSystemMetrics` / `LogConsole` / `Heartbeat` | `run.write` hoặc token job | Theo lô; shim cũng dùng |
| `GetMetrics` / `ListMetricKeys` | `run.read` | `GetMetrics` lọc theo `keys` và rút gọn về tối đa `max_points` điểm mỗi metric (mặc định 600; chia step thành các khoảng đều và lấy trung bình). `ListMetricKeys` trả mỗi metric một dòng — số điểm, step cuối, giá trị cuối, nhỏ nhất, lớn nhất — không kèm điểm, để trang run liệt kê mọi metric và chỉ tải biểu đồ của nhóm đang mở |
| `CreateArtifactUpload` / `ListArtifacts` / `GetArtifactDownload` | ghi/đọc | Presigned. Đã có: `ListArtifacts` trả các file trong thư mục model của run kèm link tải có hạn 1 giờ |
| `CompareRuns` | `run.read` | Diff tham số + chuỗi metric (kể cả khác recipe) |
| `GetReproduceSpec` | `run.read` | Job spec tái lập |

## 7. Màn hình

### S-09-1 Projects — `/w/{ws}/projects`
`CollectionView` thẻ project: tên, task, số run, run tốt nhất (metric chính + recipe), cập nhật.

### S-09-2 Project — `/w/{ws}/projects/{p}`
```
┌ PCB defects  [detect]                                       [Train mới] [Đánh giá] [⋯]  ┐
│ 24 run · tốt nhất mAP50-95 0.634 (DEIMv2-S) · dataset pcb@v4                            │
│ ┌ Run ─────────────────────────────────────┐ ┌ So sánh (4 run đã chọn) ───────────────┐ │
│ │ [Tìm…] Nhóm theo [recipe ▾]               │ │ ▾ Metric chính                          │ │
│ │ ▾ DEIMv2                                  │ │   val/mAP50-95  [line chart 4 màu]      │ │
│ │   ☑ ● deimv2-s-640      0.634  ✔ 3h       │ │ ▾ Loss (theo từng recipe)               │ │
│ │ ▾ D-FINE                                  │ │ ▾ Learning rate                         │ │
│ │   ☑ ● dfine-s-640       0.612  ✔ 3h       │ │                                         │ │
│ │ ▾ RF-DETR                                 │ │ Bảng: run │ recipe │ biến thể │ metric  │ │
│ │   ☑ ● rfdetr-base       0.605  ✔ 2h       │ │       chính │ VRAM đỉnh │ thời gian    │ │
│ │ ▾ RT-DETRv4                               │ │                                         │ │
│ │   ☐ ● rtdetrv4-l        —      ◐ 41%      │ └─────────────────────────────────────────┘ │
│ │ ▾ Ultralytics YOLO                        │                                             │
│ │   ☑ ● yolo-s-640        0.588  ✔ 2h       │                                             │
│ └───────────────────────────────────────────┘                                             │
└──────────────────────────────────────────────────────────────────────────────────────────┘
```
Nhóm theo recipe / biến thể / người / tag. So sánh chéo recipe dùng tên metric chuẩn của task; metric riêng từng recipe hiện trong nhóm riêng.

### S-09-3 Run — `/w/{ws}/projects/{p}/runs/{run}`
- Header: tên, trạng thái, **recipe + biến thể + commit** (link M17), task; `KpiStrip` metric theo khai báo của task; thẻ dataset version; badge license; nút `Đăng ký model`, `Train lại`, `⋯`.
- Tab **Tổng quan**: thông tin chạy (máy, job, build digest, điểm xuất phát, lệnh thực thi đã dựng từ template, lệnh tái lập), bảng **Tham số**, bảng **Metric cuối**, lineage.
- Tab **Metric**, **Console**, **Hệ thống**, **Artifact** (xem ảnh/plot, nghe audio mẫu với TTS/ASR, tải file).
- Run lỗi: banner thông điệp (từ `MLP_ERROR` hoặc dòng lỗi cuối log) + `Xem console` + `Train lại`.

### S-09-4 Train mới (hộp thoại)
> **Bị thay (D-42, D-49):** form này bị loại. Thay bằng tab Công thức (canvas) — xem `50-lo-trinh/03-dot-1-canvas-cong-thuc.md` §8. Phần dưới giữ để đối chiếu tới khi đợt 1 xong.

```
┌ Train mới ──────────────────────────────────────────────────────────────────────────┐
│ Project   [PCB defects ▾]                                                           │
│ Dataset   [pcb ▾]  version [v4 ▾]   12 480 item · detect · 7 class                  │
│ Recipe    [Tìm recipe cho task detect…]                                             │
│           (● DEIMv2) (○ D-FINE) (○ RT-DETRv4) (○ RF-DETR) (○ YOLO ⚠AGPL) (○ Của tôi…)│
│ Biến thể  [deimv2-s ▾]   ~10 M tham số · ≥ 6 GB VRAM · AP 50.9 COCO (tác giả)        │
│ Bắt đầu từ (● trọng số pretrained) (○ model trong Registry ▾) (○ từ đầu*)            │
│   * chỉ hiện khi recipe hỗ trợ train từ đầu                                         │
│ ── Tham số (từ recipe) ──────────────────────────────────────────────────────────── │
│ Epochs ⓘ [72]   Batch ⓘ [16]   LR ⓘ [0.0004]   Kích thước ảnh ⓘ [640]                │
│ ▸ Nâng cao (YAML theo schema recipe)                          [Đặt lại] [Đã sửa]    │
│ ── Chạy ở đâu ───────────────────────────────────────────────────────────────────── │
│ [Trên fleet] [Ngoài fleet]                                                          │
│  Tài nguyên [1 GPU ≥ 8 GB ▾]  Số máy [1 ▾] (recipe hỗ trợ torchrun)  Pool [gpu ▾]   │
│  4 máy đáp ứng · image recipe đã có trên 3 máy · vị trí hàng đợi 1                   │
│  ☑ Tự đăng ký model khi xong → [pcb-detector ▾]                                     │
│                                                        [Huỷ] [Bắt đầu train]        │
└─────────────────────────────────────────────────────────────────────────────────────┘
```
- Danh sách recipe chỉ gồm recipe `verified` hỗ trợ task của dataset; recipe có cảnh báo license hiện ⚠ và mở hộp xác nhận khi chọn lần đầu.
- Form tham số và YAML nâng cao sinh từ schema `params` của recipe.
- "Số máy" chỉ hiện khi recipe khai `distributed` khác `none`.
- Tab **Ngoài fleet**: hướng dẫn chạy script với SDK + API key.
- Mở từ trang dataset → dataset khoá sẵn; mở từ trang recipe (M17) → recipe khoá sẵn.

### Hiện trạng S-09-1 … S-09-4 (2026-11-20, lượt làm sâu 2 — D-41)

Đã làm và kiểm trên trình duyệt:
- **Run có danh tính**: số thứ tự `#n` trong project (`runs.number`, cấp khi tạo, khoá dòng project để không trùng); không đặt tên → `{biến thể}-{n}`; đổi tên được. Run ghi lại `dataset_version_id`, `from_scratch`, `gpus`, `cpus` để hiển thị và để "Train lại" điền sẵn form.
- **S-09-1**: thẻ project có số lần train, số đang chạy, kết quả tốt nhất (metric chính + run; metric kiểu loss lấy giá trị nhỏ nhất), lần train gần nhất.
- **S-09-2**: bảng run có số thứ tự + tên, recipe/biến thể, trạng thái kèm **thanh tiến độ + ETA** khi đang chạy hoặc **lý do** khi chờ/lỗi, metric chính (đánh dấu run tốt nhất), dataset + version, các tham số **khác nhau giữa các run**, thời lượng, người tạo; lọc theo trạng thái; tick để chọn run lên biểu đồ so sánh (mặc định 5 run gần nhất có kết quả); dừng run và "Train lại" ngay trên dòng.
- **S-09-3**: banner lỗi có **dòng lỗi gốc của chương trình** (bỏ qua lớp bọc của `torchrun`/`subprocess`, chỉ xét attempt cuối) + đoạn traceback dẫn tới nó; server cũng đọc dòng này khi container thoát và ghi làm lý do của job/run + `Mở toàn bộ log` / `Tiếp tục từ checkpoint` / `Train lại`; khi đang chạy: thanh bước chuẩn ([20/10 §2](../20-kien-truc/10-quan-sat-va-khoi-phuc.md)), tiến độ, nhịp tim, máy, CPU/GPU của container; bốn ô tóm tắt (metric chính + tốt nhất, thời lượng, dữ liệu, máy); tab **Tổng quan** (biểu đồ metric chính, metric chính yếu, log mới nhất, dòng thời gian), **Metric** (gom nhóm theo tiền tố, chỉ nhóm đang mở mới tải điểm, tìm, làm mượt), **Log** (lọc, tự cuộn, tải về), **Cấu hình** (mọi tham số: giá trị dùng, mặc định, ý nghĩa; tham số đã đổi được tô), **File** (thư mục model, tải từng file); nút Dừng, Train lại, Đăng ký model, Xoá.
- **S-09-4**: một lượt từ trên xuống — Recipe → Dữ liệu → Model → Tham số → Máy và tên. Mặc định của recipe hiện thành giá trị thật; sai kiểu/ngoài khoảng báo ngay tại ô (cùng quy tắc với server: kiểu, `minimum`, `exclusiveMinimum`, `maximum`, `multipleOf`, `enum`); dataset tự chọn version mới nhất, dataset chưa có version thì dẫn sang trang dataset; ghi rõ GPU đang trống hay đang bận bởi việc nào; nút Bắt đầu nói còn thiếu gì; bắt đầu xong mở thẳng trang run. Server từ chối khi recipe cần dữ liệu mà không chọn version (`TRACK_DATASET_REQUIRED`) hoặc cần GPU mà xin 0 (`TRACK_GPU_REQUIRED`).

Chưa làm so với mô tả ở trên: nhóm run theo recipe/người/tag; tab Hệ thống (biểu đồ GPU/CPU theo thời gian); lệnh đã dựng + lệnh tái lập; bắt đầu từ model trong Registry; tự đăng ký model khi xong; số máy (`torchrun` nhiều node) và pool; tab "Ngoài fleet"; tag, ghi chú; S-09-5.

### S-09-5 So sánh — `/w/{ws}/projects/{p}/compare?runs=…`
Biểu đồ chồng theo metric chuẩn; bảng: recipe, biến thể, tham số khác nhau, metric chính, VRAM đỉnh, thời gian train, kích thước model.

## 8. Sự kiện
`track.RunStarted` (M06 lineage: run → recipe build, dataset version, base model), `RunFinished`, `RunFailed`, `RunBestMetricImproved`, `ArtifactUploaded`.

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `TRACK_RUN_FINISHED` | FAILED_PRECONDITION | Run đã kết thúc |
| `TRACK_TOO_MANY_KEYS` | RESOURCE_EXHAUSTED | Quá số metric cho phép |
| `TRACK_PARAMS_INVALID` | INVALID_ARGUMENT | Tham số sai (chi tiết từng trường) |
| `TRACK_DATASET_REQUIRED` | FAILED_PRECONDITION | Recipe train trên dữ liệu nhưng chưa chọn dataset version |
| `TRACK_GPU_REQUIRED` | FAILED_PRECONDITION | Recipe cần GPU nhưng yêu cầu 0 GPU |
| `TRACK_RESOURCES_INVALID` / `TRACK_NAME_INVALID` | INVALID_ARGUMENT | Số GPU ngoài 0–16; tên run rỗng hoặc dài quá 80 ký tự |
| `TRACK_RUN_ACTIVE` | FAILED_PRECONDITION | Xoá run chưa kết thúc — phải dừng trước |
| `TRACK_RUN_HAS_MODEL` | FAILED_PRECONDITION | Xoá run đã có version model đăng ký từ nó |
| `TRACK_BASE_MODEL_INCOMPATIBLE` | FAILED_PRECONDITION | Base model khác recipe |
| `CATALOG_RECIPE_NOT_VERIFIED` / `CATALOG_TASK_UNSUPPORTED` / `CATALOG_LICENSE_NOT_ACKNOWLEDGED` | FAILED_PRECONDITION | (từ M17) |

## 10. Nghiệm thu
- [ ] Cùng một dataset version: train **ít nhất ba recipe khác họ** cùng task (chọn tại lúc test trong catalog) từ UI; mọi run hiện metric realtime dưới tên chuẩn của task và so sánh được trên một biểu đồ.
- [ ] Recipe vừa nhập từ repo (M17 S-17-3) train được ngay sau khi verified, không sửa mã nền tảng.
- [ ] Lệnh đã dựng từ template hiển thị ở tab Tổng quan khớp lệnh thực thi trong log.
- [ ] (Khi catalog có recipe TTS verified) fine-tune trên dataset audio + transcript, chạy `predict` theo lô với model vừa train; tab Artifact phát được file audio kết quả.
- [ ] Recipe dùng `torchrun` chạy 2 máy × 1 GPU; tắt một máy → job resume từ checkpoint, metric nối tiếp.
- [ ] Script PyTorch tự viết chạy trên laptop ngoài fleet ghi đủ git + metric qua SDK.
- [ ] Rút mạng 10 phút khi train → không mất điểm metric.
- [ ] "Tái lập" sinh job spec cùng recipe build digest, biến thể, tham số, dataset version.
