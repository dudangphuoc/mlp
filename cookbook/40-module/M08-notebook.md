# M08 — Jupyter Notebook (môi trường dựng sẵn)

> **Phase:** P1 · **Phụ thuộc:** M01, M02, M03, M04 · **Quyết định:** D-11, D-15 · **Proto:** `notebook.proto` (`NotebookService`) · **Schema DB:** `nb`

## 1. Mục tiêu & phạm vi
Mở JupyterLab hoặc VS Code (code-server) trên máy có tài nguyên yêu cầu, ngay trong portal, với môi trường đầy đủ sẵn.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| 7 profile image dựng sẵn (CPU/GPU) | ✔ | | |
| **Mở notebook trong môi trường của một recipe** (đúng torch/CUDA/phụ thuộc của repo, mã repo có sẵn ở `/opt/recipe`) | ✔ | | |
| SDK + Ray client cấu hình sẵn, mount dataset version | ✔ | | |
| Home lưu bền, mở lại được trên máy khác | ✔ | | |
| Lưu môi trường thành image mới | ✔ | | |
| Chạy notebook như job (papermill) | ✔ | lập lịch qua Pipeline | |
| Tự tắt khi rảnh | ✔ | | |
| Cộng tác realtime nhiều người, chia sẻ công khai | | | ✔ |

## 2. Khái niệm
- **Profile**: image dựng sẵn chung (không gắn họ model); build `FROM mlp-base` (Python 3.11 + Ray client cùng phiên bản cụm).
- **Môi trường recipe**: image build của một recipe (M17) + JupyterLab/code-server + SDK được thêm ở lớp cuối; mã repo ở `/opt/recipe`. Ray client chỉ có khi Python của recipe tương thích với cụm.

| Profile | Nội dung chính |
|---|---|
| `base` | numpy, pandas, polars, pyarrow, scipy, scikit-learn, matplotlib, seaborn, plotly |
| `pytorch-gpu` | base + PyTorch, CUDA/cuDNN, torchvision, torchaudio, Lightning |
| `cv` | pytorch-gpu + opencv, albumentations, timm, torchmetrics, pycocotools, supervision (không gắn họ model nào; dùng môi trường recipe để có repo cụ thể) |
| `nlp-llm` | pytorch-gpu + transformers, datasets, peft, trl, accelerate, bitsandbytes, sentence-transformers, vLLM |
| `tabular` | base + xgboost, lightgbm, catboost, optuna, shap |
| `audio` | pytorch-gpu + torchaudio, librosa, soundfile, jiwer |
| `tensorflow` | base + TensorFlow/Keras GPU |

Mọi profile có: JupyterLab 4 (git, LSP, theo dõi CPU/RAM/GPU, TensorBoard, variable inspector), code-server, `mlplatform-sdk`, Ray client, git, papermill, `nvtop`, template notebook theo task. Mỗi profile có biến thể CPU.
- **Environment**: image tuỳ biến do người dùng lưu.
- **Holder** (D-15): Ray job giữ CPU/GPU cho container notebook chạy ngoài Ray node.

## 3. Dữ liệu (schema `nb`)

| Bảng | Cột chính |
|---|---|
| `profiles` | id, name, display_name, description, image_gpu, image_cpu, version, default_resources, enabled, builtin |
| `environments` | id, workspace_id, name, base_profile_id, version, requirements_txt, apt_packages, image_ref, image_digest, build_job_id, state (`building/ready/failed`), created_by |
| `servers` | id, workspace_id, user_id, name, profile_id / environment_id, image_digest, ide (`jupyterlab/vscode`), resources (cpus, memory_gb, gpus, min_vram_gb), host_id, local_port, gpu_uuids text[], job_id, state, state_reason, dataset_mounts jsonb, idle_timeout_min, last_activity_at, started_at, stopped_at, jupyter_token_enc |
| `home_volumes` | id, workspace_id, user_id, host_id, size_bytes, quota_gb (20), snapshot_uri, snapshot_at |
| `runs` | id, workspace_id, user_id, notebook_path, parameters jsonb, environment, resources, job_id, output_uri, state |

## 4. Trạng thái & quy tắc

```
requested → queued → allocating (holder) → starting (pull image, restore home, mount) → running
running ⇄ idle_warning (còn 10 phút tới timeout) → stopping (snapshot home) → stopped
bất kỳ → failed (pull lỗi, máy offline, OOM…)
```
- **R1** Notebook chạy trên đúng GPU mà holder được Ray cấp (`--gpus device=…`); Ray không đặt việc khác lên GPU đó.
- **R2** Notebook và code-server chỉ nghe `127.0.0.1` trên máy; truy cập qua **data channel** ([20/04](../20-kien-truc/04-agent.md)).
- **R3** Ray Client (D-24, D-33): notebook nhận `RAY_ADDRESS=ray://10.88.0.1:10001`, nối thẳng qua overlay (không proxy). **Không cách ly** giữa workspace và **không ép quota** (rủi ro chấp nhận, [20/06 §8](../20-kien-truc/06-bao-mat.md)); UI hiện cảnh báo một lần và gợi ý gắn `resources={"pool:<pool>": 0.001}`. Audit và thống kê GPU dùng là best-effort từ Ray State API (M03 R8). Instance Admin tắt Ray Client → biến không được đặt và service `wireguard` chặn cổng 10001 trên `wg0`.
- **R4** Rảnh = không hoạt động kernel/terminal/WebSocket và không kernel nào đang chạy cell; rảnh quá timeout → dừng, cảnh báo trước 10 phút.
- **R5** Home snapshot lên kho S3 mỗi 15 phút và khi dừng; mở trên máy khác → khôi phục từ snapshot.
- **R6** Dataset `host_local` chỉ mount được khi notebook chạy trên đúng máy đó.
- **R7** Quota: `max_notebooks`, `max_gpus`, `notebook_idle_timeout_min` đọc từ `sched.quotas` (M03, nguồn duy nhất).

## 5. Luồng chính
1. **Mở**: chọn profile/environment, IDE, tài nguyên, máy (tự động/chọn), dataset version, timeout → `CreateServer` → job `notebook` (holder) → holder báo GPU → api cấp port cục bộ, sinh token Jupyter, tạo API key ngắn hạn cho người dùng → lệnh agent `StartNotebook` (restore home, mount dataset, `docker run` đúng GPU) → `running` → UI mở iframe `/nb/{id}/lab`.
2. **Truy cập**: trình duyệt → `/nb/{id}/…` → api kiểm chủ sở hữu → mở data channel tới `127.0.0.1:<port>` trên máy → chuyển tiếp HTTP + WebSocket; api tự gắn token Jupyter.
3. **Dùng Ray**: trong notebook `ray.init()` → task chạy trên máy khác.
4. **Dừng**: `StopServer` hoặc hết timeout → snapshot home → dừng container → holder thoát → thu hồi API key ngắn hạn.
5. **Lưu môi trường**: `DraftEnvironment` (agent so `pip freeze` với lockfile profile) → người dùng duyệt danh sách → `CreateEnvironment` → job `build_env` (`FROM profile` + `pip install`) → push registry → `ready`.
6. **Chạy như job**: `CreateRun(notebook, tham số, environment, tài nguyên)` → job `notebook_run` (papermill) → output notebook + artifact lên kho S3.

## 6. Hợp đồng (`notebook.proto` — `NotebookService`)

| RPC | Quyền | Mô tả |
|---|---|---|
| `ListProfiles` | thành viên | |
| `ListServers` / `GetServer` / `WatchServer` | chủ; Admin xem tất cả | Tiến độ khởi động |
| `CreateServer` | `notebook.use` | `request_id` |
| `StopServer` / `RestartServer` / `ExtendIdle` | chủ, Admin | `ExtendIdle` hoãn 1 giờ |
| `UpdateServer` | chủ | Tên, timeout |
| `ListEnvironments` / `GetEnvironment` / `DraftEnvironment` / `CreateEnvironment` / `DeleteEnvironment` | `notebook.use` | |
| `ListRuns` / `CreateRun` / `GetRun` | `notebook.use` | |
| `GetHomeVolume` / `SnapshotHome` | chủ | |
| HTTP `/nb/{id}/…` | chủ | Proxy Jupyter/code-server (ngoại lệ không phải RPC) |

## 7. Màn hình

### S-08-1 Notebooks — `/w/{ws}/notebooks`
Bảng/thẻ: tên, trạng thái, profile, IDE, máy, GPU (model × số), đã chạy, rảnh bao lâu, nút `Mở` / `Dừng`. Tab `Của tôi` / `Tất cả` (Admin) / `Environments` (S-08-4) / `Lần chạy` (S-08-5). Rỗng: "Chưa có notebook. Tạo notebook với môi trường dựng sẵn." + `Tạo notebook`.

### S-08-2 Tạo notebook (hộp thoại)
```
┌ Tạo notebook ─────────────────────────────────────────────────────────────┐
│ Môi trường  [Profile] [Recipe] [Của tôi]                                  │
│             ┌ cv ✓ ────────┐┌ nlp-llm ────┐┌ audio ──────┐┌ tabular ──┐   │
│             │ opencv, timm,││ transformers,││ torchaudio, ││ xgboost,  │   │
│             │ pycocotools  ││ peft, vLLM   ││ librosa     ││ lightgbm  │   │
│             └──────────────┘└─────────────┘└─────────────┘└───────────┘   │
│             Tab Recipe: mọi recipe đã build trong catalog (RecipePicker)   │
│ IDE         (● JupyterLab) (○ VS Code)                                    │
│ Tài nguyên  [1 GPU ≥ 8 GB ▾]   CPU 4 · RAM 16 GB      Máy [Tự động ▾]     │
│             3 máy đáp ứng · ước tính chờ < 1 phút                          │
│ Dataset     [+ Gắn dataset version]  pcb@v3 → /data/pcb@v3                │
│ Tự tắt sau  [60 phút rảnh ▾]                                              │
│                                              [Huỷ] [Tạo notebook]         │
└───────────────────────────────────────────────────────────────────────────┘
```
`ResourcePicker` hiện số máy đáp ứng; dataset `host_local` chỉ chọn được khi máy cố định là máy chứa dữ liệu.

### S-08-3 Khởi động + IDE — `/w/{ws}/notebooks/{nb}`
Trong lúc khởi động: `ProgressSteps` (Xếp hàng → Cấp phát GPU → Tải image → Khôi phục home → Gắn dataset → Mở Jupyter). Khi chạy: thanh mỏng trên cùng (tên, máy, GPU, đồng hồ rảnh, `Lưu môi trường`, `Dừng`) + iframe IDE toàn trang. Banner "Notebook sẽ tự tắt sau 10 phút" với nút `Giữ thêm 1 giờ`.

### S-08-4 Environments — `/w/{ws}/notebooks?tab=environments`
Bảng: tên, profile gốc, version, trạng thái build, gói thêm (rút gọn), người tạo; chi tiết: requirements, log build, `Dùng cho notebook mới`.

### S-08-5 Lần chạy — `/w/{ws}/notebooks?tab=runs`
```
┌ Lần chạy ─────────────────────────────────────────────────── [+ Chạy notebook] ┐
│ Notebook                 │ Tham số          │ Môi trường │ Trạng thái │ Thời gian │ Kết quả │
│ eda/pcb-stats.ipynb      │ version=3        │ cv:4       │ ✔ xong     │ 4:12      │ [Mở] ⤓  │
│ train/sweep.ipynb        │ lr=1e-4, bs=16   │ recipe…    │ ● chạy 31% │ 12:03     │ job ↗   │
└──────────────────────────────────────────────────────────────────────────────────┘
```
`+ Chạy notebook` → `FormDialog`: chọn file `.ipynb` từ home, tham số (bảng khoá/giá trị, đọc cell `parameters`), môi trường, tài nguyên → `CreateRun`. `Mở` hiển thị notebook output (HTML tĩnh) trong trang; `⤓` tải `.ipynb` output; `job ↗` tới S-03-2.

## 8. Sự kiện
`nb.ServerStarted`, `ServerIdleWarning`, `ServerStopped`, `ServerFailed`, `EnvironmentReady`, `EnvironmentFailed`, `RunCompleted`, `RunFailed`.

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `NB_QUOTA_EXCEEDED` | RESOURCE_EXHAUSTED | Vượt số notebook/GPU cho phép |
| `NB_NO_ELIGIBLE_HOST` | FAILED_PRECONDITION | Không máy nào đáp ứng tài nguyên |
| `NB_IMAGE_PULL_FAILED` | UNAVAILABLE | Không tải được image |
| `NB_HOST_LOST` | UNAVAILABLE | Máy chạy notebook đã offline |
| `NB_HOME_QUOTA_EXCEEDED` | RESOURCE_EXHAUSTED | Thư mục home vượt hạn mức |
| `NB_ENV_BUILD_FAILED` | FAILED_PRECONDITION | Build môi trường thất bại (xem log) |
| `NB_DATASET_NOT_ON_HOST` | FAILED_PRECONDITION | Dataset local nằm ở máy khác |
| `NB_RAY_CLIENT_DISABLED` | FAILED_PRECONDITION | Instance Admin đã tắt Ray Client |

## 10. Nghiệm thu
- [ ] Mở notebook `cv` 1 GPU trên máy đã kéo trước image → Jupyter dùng được ≤ 60 s.
- [ ] `torch.cuda.is_available()` đúng, `nvidia-smi` chỉ thấy GPU được cấp; `import mlplatform` chạy ngay.
- [ ] Mở notebook trong môi trường của một recipe bất kỳ đã build → mã repo ở `/opt/recipe/src`, chạy được script train của repo trên dataset đã chuyển định dạng.
- [ ] Notebook giữ GPU 0 trên máy 2 GPU → job train mới chỉ dùng GPU 1.
- [ ] `mlp.datasets.get("mlp://ws/datasets/x@v2")` trả đường dẫn đã mount, đọc được.
- [ ] `ray.init()` (không tham số) + task `num_gpus=1` chạy trên máy khác; trong ≤ 60 s có bản ghi audit `nb.ray_client.session` gắn notebook nguồn, và S-03-3 hiện GPU đang dùng ngoài nền tảng.
- [ ] Chạy notebook như job với tham số → S-08-5 hiện kết quả, mở được output.
- [ ] Dừng rồi mở lại trên máy khác → file home còn nguyên.
- [ ] Không truy cập được Jupyter của người khác qua `/nb/{id}` (404); cổng notebook không nghe trên interface nào ngoài `127.0.0.1`.
- [ ] Rảnh quá timeout → tự dừng, có cảnh báo trước 10 phút; cell chạy dài không bị dừng.
- [ ] Lưu môi trường có `lightgbm==4.5.0` → notebook mới từ environment đó có sẵn gói.
