# M11 — Model Registry

> **Phase:** P1 (approval P2) · **Phụ thuộc:** M01, M03, M09, M17 · **Proto:** `registry.proto` (`RegistryService`) · **Schema DB:** `registry`

## 1. Mục tiêu & phạm vi
Kho trung tâm cho mọi model đã train hoặc import: version bất biến, metadata đầy đủ, stage/alias, export, thử nhanh.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Registered model + version bất biến | ✔ | | |
| Đăng ký từ run, upload file, HF Hub | ✔ | | |
| Signature vào/ra theo task, classes, metric, lineage | ✔ | | |
| Stage `none/staging/production/archived` + alias | ✔ | | |
| Export theo **lệnh export của recipe** (định dạng do recipe khai: ONNX, TensorRT, OpenVINO, CoreML, TFLite, GGUF…) | ✔ | | |
| Thử nhanh (playground) | ✔ | | |
| Quy trình phê duyệt nhiều bước + kiểm thử tự động trước promote | | ✔ | |
| Model card tự sinh, quét an toàn file pickle | | ✔ | |

## 2. Khái niệm
**Registered model** (tên), **model version** (bất biến), **signature**, **stage**, **alias**, **export** (bản chuyển đổi định dạng của một version).

Signature ví dụ:
```proto
// ModelSignature
inputs  { name: "image" type: IMAGE shape: [-1,3,640,640] dtype: FLOAT32 }
outputs { name: "detections" type: DETECTIONS }
task: "detect"  preprocess { imgsz: 640 letterbox: true }
```
Kiểu vào P1: `IMAGE`, `TEXT`, `CHAT`, `TABULAR_ROW`, `AUDIO`, `TENSOR`; kiểu ra: `CLASS_PROBS`, `DETECTIONS`, `ORIENTED_DETECTIONS` (obb), `MASKS`, `SEMANTIC_MASK`, `KEYPOINTS`, `DEPTH_MAP`, `ENTITIES`, `TEXT`, `TRANSCRIPT` (văn bản + đoạn thời gian), `AUDIO`, `VALUE`. Mỗi kiểu ứng với một `TaskOutput` của handler ([20/08 §10](../20-kien-truc/08-recipe-va-runtime.md)). M12 và M05 đọc signature để biết cách gọi.

**Thư mục model (D-28):** mọi version lưu theo bố cục chuẩn `weights/`, `config/`, `classes.json`, `signature.json`, `recipe.lock.json` ([20/08 §8](../20-kien-truc/08-recipe-va-runtime.md)). Model adapter (LoRA…) chỉ chứa adapter và khai **`base_weights_ref`** (`hf://…@rev`, `zoo://<khoá>`, `model://{model}@v{n}`; sha256 ghi riêng trong `recipe.lock.json`); serve/eval/export tải base vào `{base_weights}`.

## 3. Dữ liệu (schema `registry`)

| Bảng | Cột chính |
|---|---|
| `models` | id, workspace_id, slug, name, task, modality, description_md, tags, visibility, created_by |
| `versions` | id, model_id, version, source (`run/upload/hf_hub`), run_id, **recipe_id, recipe_build_id, variant**, license jsonb, framework, format, artifact_uri (gốc thư mục model), base_weights_ref, base_weights_sha256, files jsonb, size_bytes, signature bytea, metrics jsonb, params jsonb, classes jsonb, input_example_uri, state (`uploading/inspecting/ready/failed`), stage, description_md, created_by |
| `aliases` | model_id, alias, version_id, updated_by, updated_at |
| `stage_transitions` | id, version_id, from_stage, to_stage, actor_id, reason, at |
| `exports` | id, version_id, format, params jsonb, job_id, artifact_uri, size_bytes, state |

## 4. Trạng thái & quy tắc
```
version: uploading → inspecting → ready | failed
stage:   none → staging → production → archived   (chuyển bất kỳ hướng nào, có lý do)
```
- **R1** Version bất biến (file + metadata cốt lõi); chỉ sửa mô tả, tag, stage, alias.
- **R2** Mỗi model tối đa 1 version `production` và 1 `staging`; promote version mới → version cũ cùng stage thành `archived` (trừ khi chọn giữ).
- **R3** Promote lên `production` cần `model.promote_production` + lý do; stage khác: Editor.
- **R4** Version đang được deployment dùng không xoá được.
- **R5** Đăng ký từ run: copy server-side artifact sang `mlp-artifacts/{ws}/models/{model}/v{n}/`; metric/params/classes/inputs lấy từ run.
- **R6** Upload/HF import → người dùng chọn recipe tương ứng (hoặc recipe `generic-onnx`/`hf-auto`) → job `inspect_model` chạy trong image của recipe đó theo hợp đồng [20/08 §5.5](../20-kien-truc/08-recipe-va-runtime.md): đặt file chính vào `weights/<artifacts.best.as>`, chạy `entrypoints.inspect` nếu có, trả **đề xuất** signature, classes và file `config/*` để người dùng xác nhận.
- **R7** Model version luôn gắn recipe + build: eval, export, serve, pre-label đều chạy bằng đúng recipe build đó (D-21).
- **R8** Version import chỉ `ready` khi thư mục model đủ năm thành phần và smoke `load` + `predict` một mẫu thành công (20/08 §5.5 bước 5).
- **R9** Version có `base_weights_ref` trỏ model version khác → version base không xoá được khi còn adapter dùng (`REGISTRY_IN_USE`).

## 5. Luồng chính
1. **Đăng ký từ run**: trang run → `Đăng ký model` → chọn model (hoặc tạo), artifact (mặc định `best`) → `CreateVersion` → `ready`.
2. **Import** (S-11-4): upload file/thư mục (≤ 20 GB, presigned multipart) hoặc HF repo + revision → chọn task + recipe (gợi ý theo đuôi file / `config.json` HF) + biến thể → (adapter) chọn base → job `inspect_model`/`hf_import` → kiểm signature + classes → `ready`.
3. **Promote**: `TransitionStage(to=production, reason)` → ghi audit, thông báo.
4. **Export**: chọn định dạng (danh sách từ recipe) + tham số → job `export` chạy lệnh export của recipe trên máy phù hợp (TensorRT cần GPU NVIDIA) → artifact.
5. **Thử nhanh**: tab Thử → nếu chưa có deployment playground của version → tạo deployment tạm (M12, 1 replica, tự tắt sau 15 phút không dùng) → gọi `InferenceService.Predict`.

## 6. Hợp đồng (`registry.proto` — `RegistryService`)

| RPC | Quyền | Mô tả |
|---|---|---|
| `ListModels` / `GetModel` / `CreateModel` / `UpdateModel` / `DeleteModel` | đọc: `run.read`; ghi: `model.write` | |
| `ListVersions` / `GetVersion` | `run.read` | |
| `CreateVersion` | `model.write` | Nguồn run / upload / HF |
| `CreateVersionUpload` / `CompleteVersionUpload` | `model.write` | Presigned multipart |
| `UpdateVersion` | `model.write` | Mô tả, tag |
| `TransitionStage` | `model.write`; production: `model.promote_production` | |
| `SetAlias` / `DeleteAlias` | `model.write` | |
| `ListVersionFiles` / `GetFileDownload` | `run.read` | |
| `ListExportFormats` | `run.read` | Theo recipe của version |
| `CreateExport` / `ListExports` / `GetExport` | `model.write` / `run.read` | Job |
| `ServingService.StartTest` / `TryPredict` | `deployment.manage` / `run.read` | Thử model (S-11-5): dùng lại replica đang chạy của version, hoặc bật **replica thử** (`purpose=test`) tự tắt sau 30 phút không gọi; gọi bằng phiên đăng nhập, không cần khoá `mlpd_`, không ghi log giám sát M13 |
| `StartEvaluation` / `ListEvaluations` / `GetEvaluation` / `ListEvaluationSamples` / `CancelEvaluation` | `deployment.manage` / `run.read` | Đánh giá trên dataset version (S-11-6, D-39): nền tảng gọi replica từng mẫu, tự tính metric; kết quả từng mẫu; `test/*` gộp vào metric của version |
| `ResolveModelUri` | `run.read` | `mlp://ws/models/m@production|@champion|@v3` |

## 7. Màn hình

### S-11-1 Models — `/w/{ws}/models`
Bảng: tên, task, version production / staging (badge), metric chính của production, số version, deployment đang dùng, cập nhật. `+ Import model`.

### S-11-2 Model — `/w/{ws}/models/{m}`
- Header: tên, task, alias; nút `Import version`, `⋯`.
- Bảng version: version, stage (badge), nguồn (link run), metric chính, kích thước, ngày, người; chọn → so sánh.
- Biểu đồ metric chính qua các version.

### S-11-3 Model version — `/w/{ws}/models/{m}/versions/{v}`
```
┌ pcb-detector  v7  [Detect] [● production]          [Promote ▾] [Deploy] [⤓] [⋯]      ┐
│ mAP50 0.81 · mAP50-95 0.63 · Precision 0.84 · Recall 0.77 · recipe DEIMv2 (deimv2-s) · Apache-2.0 · từ run deimv2-s-640 │
│ [Tổng quan] [Thử] [Export] [Deployments 2] [Files] [Lineage] [Lịch sử stage]            │
└──────────────────────────────────────────────────────────────────────────────────────────┘
```
- **Tổng quan**: recipe + biến thể + build (link M17), license, signature (bảng vào/ra), classes, tham số, metric, mô tả (Markdown sửa được).
- **Thử**: nguồn (Ảnh / Webcam / Item trong dataset val / Văn bản / Dòng bảng — theo signature), tham số suy luận (Confidence, IoU, Kích thước ảnh với ảnh; max tokens, temperature với text), kết quả tab `Kết quả` (vẽ lên ảnh / bảng) · `Response` (JSON) · `Mã gọi` (curl/Python/JS). Lần đầu hiện "Đang khởi động playground…".
- **Export**: lưới thẻ định dạng do recipe khai trong `entrypoints.export.formats` (tên, mô tả, nền tảng đích, nút `Export`); thẻ không chạy được trên fleet hiện tại bị ẩn; danh sách bản đã export (định dạng, tham số, kích thước, tải).
- **Deployments**: deployment dùng version này + `Deploy`.
- **Files**: cây file + tải.
- **Lineage**: `LineageGraph`.
- **Lịch sử stage**: bảng chuyển stage (ai, khi nào, lý do).

Promote production → `ConfirmDialog` yêu cầu lý do, nêu version production hiện tại sẽ bị lưu trữ.

### S-11-4 Import model (hộp thoại nhiều bước) — từ S-11-1 `+ Import model` hoặc S-11-2 `Import version`
```
┌ Import model ───────────────────────────── ① Nguồn ② Recipe ③ Kiểm tra ┐
│ ① Nguồn: [Tải file/thư mục] [Hugging Face] [Model Zoo]                  │
│    HF: repo [org/model            ] revision [main ▾]  token: hf-main ▾ │
│ ② Task [Detect ▾]  Recipe [RecipePicker — gợi ý: D-FINE ✓ khớp config] │
│    Biến thể [dfine-s ▾]   Đây là adapter? [☐]  Base [chọn …]           │
│    LicenseBadge + LicenseAckDialog nếu recipe/biến thể yêu cầu         │
│ ③ Kiểm tra (job inspect_model): signature  image→detections ✔          │
│    classes (80)  [Sửa]    kích thước 312 MB                            │
│    Cấu hình recipe (form từ inspect.configSchema, vd. onnx.json):       │
│      Input [images] Kích thước [640]×[640] Layout [NCHW▾] Scale [255]   │
│      Kiểu đầu ra detect [boxes_scores_labels▾]  Tên output […]         │
│    Smoke: load + predict 1 mẫu ✔                 [Huỷ] [Hoàn tất]      │
└──────────────────────────────────────────────────────────────────────────┘
```
Không recipe nào khớp → gợi ý `generic-onnx` (file `.onnx`) hoặc `hf-auto` (repo HF có pipeline), hoặc "Nhập recipe từ repo" (M17 S-17-3).

## 8. Sự kiện
`registry.ModelCreated`, `ModelVersionCreated`, `ModelVersionReady`, `ModelVersionStageChanged`, `AliasUpdated`, `ExportCompleted`.

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `REGISTRY_NAME_TAKEN` | ALREADY_EXISTS | Tên model đã dùng |
| `REGISTRY_VERSION_IMMUTABLE` | FAILED_PRECONDITION | Không sửa được nội dung version |
| `REGISTRY_IN_USE` | FAILED_PRECONDITION | Version đang được deployment dùng |
| `REGISTRY_PROMOTE_FORBIDDEN` | PERMISSION_DENIED | Cần quyền Admin để lên production |
| `REGISTRY_ARTIFACT_MISSING` | FAILED_PRECONDITION | Run không có artifact đã chọn |
| `REGISTRY_UNSUPPORTED_FRAMEWORK` | INVALID_ARGUMENT | Framework chưa hỗ trợ |
| `REGISTRY_EXPORT_UNAVAILABLE` | FAILED_PRECONDITION | Không có máy chạy được định dạng này |
| `REGISTRY_BASE_MISSING` | FAILED_PRECONDITION | Model adapter thiếu hoặc không tải được base weights |

## 10. Nghiệm thu
- [ ] Đăng ký từ run → version có metric, params, classes, link run, dataset version (lineage hiện đủ).
- [ ] Editor không promote được production; Admin promote → production cũ thành archived.
- [ ] `@production` luôn trỏ version production hiện tại; `@v3` cố định.
- [ ] Import `.onnx` 2 GB qua trình duyệt thành công, signature suy ra từ graph.
- [ ] Tab Thử chạy được với một model mỗi nhóm task (ảnh, text, audio, bảng), recipe chọn tại lúc test.
- [ ] Đánh giá một model detect và một model classify trên dataset version có nhãn → mAP50/mAP50-95 (hoặc accuracy/F1 + ma trận nhầm lẫn), bảng theo lớp, ngưỡng tin cậy tốt nhất, ảnh sai nhiều nhất có khung nhãn và khung dự đoán.
- [ ] Replica thử không được gọi trong 30 phút tự tắt, GPU được giải phóng.
- [ ] Export ONNX một model ảnh bằng lệnh export của recipe của nó.
- [ ] Import repo HF (adapter LoRA) với base `hf://…` → serve tải base vào `{base_weights}` và trả lời được.
- [ ] Mọi version (từ run hay import) có đủ năm thành phần của thư mục model chuẩn.
- [ ] Xoá version đang deploy bị chặn.
