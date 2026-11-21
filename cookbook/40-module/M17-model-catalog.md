# M17 — Model Catalog (recipe, biến thể, task, Model Zoo)

> **Phase:** P1 · **Phụ thuộc:** M01, M02, M03 · **Quyết định:** D-20, D-21, D-22, D-23, D-25, D-27, D-28, D-30 · **Proto:** `catalog.proto` (`CatalogService`) · **Schema DB:** `catalog`
> Định dạng recipe, ngữ pháp template, shim, phân tán, giao thức phục vụ: [20/08](../20-kien-truc/08-recipe-va-runtime.md). Manifest thật: [`recipes/`](../../recipes/README.md).

## 1. Mục tiêu & phạm vi
Cho phép dùng **bất kỳ họ model nào** mà không sửa code nền tảng: nhập recipe (từ repo git, gói pip hay repo HF), build môi trường, kiểm tra, rồi dùng để train / eval / export / serve / gán nhãn hỗ trợ / mở notebook. Recipe dựng sẵn chỉ là ví dụ (D-25).

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Catalog recipe dựng sẵn ([20/08 §13](../20-kien-truc/08-recipe-va-runtime.md)), image do CI build (D-30) | ✔ | thêm theo nhu cầu | |
| Nhập recipe từ git URL + commit / pip / HF repo (manifest trong repo hoặc trình hướng dẫn) | ✔ | | |
| Build recipe của workspace (job `build_recipe`), smoke test, trạng thái verified | ✔ | | |
| Biến thể + trọng số pretrained; **Model Zoo** (cache trọng số, file bị gate do Admin tải lên) | ✔ | | |
| Token Hugging Face cho trọng số gated (tích hợp M01) | ✔ | | |
| License + cảnh báo xác nhận (D-23) | ✔ | | |
| Danh mục task mở (D-22): xem; thêm task mới | xem | thêm qua UI | |
| Bộ chuyển định dạng dữ liệu do recipe cung cấp | ✔ | | |
| Nâng cấp recipe lên commit mới, so sánh build | ✔ | | |
| Chia sẻ recipe giữa workspace / catalog cộng đồng | | | ✔ |

## 2. Khái niệm
**Recipe**, **biến thể**, **recipe image**, **task**, **bộ chuyển định dạng**, **Model Zoo**, **thư mục model** — định nghĩa ở [20/08 §2](../20-kien-truc/08-recipe-va-runtime.md).
- Recipe **builtin**: thư mục trong `recipes/` của mã nguồn nền tảng; CI build image, đẩy `recipes/{recipe}:{commit}-{hash}`; Instance Admin bật/tắt toàn instance, Owner/Admin bật/tắt cho workspace.
- Recipe **của workspace**: người dùng nhập; build bằng job `build_recipe` trên một máy có Docker, đẩy `recipes/ws-{ws}/…`.
- **Model Zoo**: kho file theo phạm vi `shared` (Instance Admin) hoặc `ws-{ws}`; mỗi file định danh bằng sha256 + khoá (`dinov3_vitb16`…); biến thể tham chiếu qua `requiresFiles` / `{zoo.<key>}`.

## 3. Dữ liệu (schema `catalog`)

| Bảng | Cột chính |
|---|---|
| `tasks` | id, key (khoá `tasks.yaml`), modality_in, modality_out, label_kinds text[], converters text[], inference_contract (tên message proto), primary_metric, metrics text[], builtin |
| `recipes` | id, workspace_id (null = builtin), name, display_name, family, description, maturity (`stable/experimental/draft`), source_kind (`git/pip/hf`), source_url, commit, license_code, license_weights, license_notes, requires_ack, tasks text[], gpu (`required/optional/none`), min_vram_gb, min_compute_capability, data_input jsonb (khoá hoặc map task → khoá), manifest bytea (manifest đã kiểm), state, current_build_id, enabled, created_by |
| `recipe_builds` | id, recipe_id, commit, builder (`ci/job`), image_ref, image_digest, size_bytes, job_id, state (`building/validating/verified/failed`), smoke_report jsonb, started_at, finished_at |
| `variants` | id, recipe_id, name, display_name, config, tasks text[], params_default jsonb, weights jsonb (nguồn, hoặc map task → nguồn), weights_zoo_file_id, requires_files jsonb (`[{zoo_key, path}]`), min_vram_gb, license, metrics_reported jsonb (do tác giả công bố, có nguồn) |
| `zoo_files` | id, scope (`shared/ws`), workspace_id, key, display_name, source (`upload/url/hf/gdrive/github`), source_ref, sha256, size_bytes, uri (kho S3 `mlp-zoo`), license_note, gated, state (`pending/downloading/ready/failed`), job_id, uploaded_by, created_at |
| `converters` | id, key, task_keys text[], builtin, recipe_id (nếu do recipe cung cấp), output_tree (mô tả cây thư mục) |
| `license_acks` | workspace_id, recipe_id, variant (null = cả recipe), user_id, at |

## 4. Trạng thái & quy tắc
```
recipe:   draft → building → validating → verified ⇄ deprecated
                    └──────────┴──► failed (xem báo cáo, sửa, build lại)
zoo file: pending → downloading → ready | failed
```
- **R1** Chỉ recipe `verified` mới dùng được để train/serve; `draft/failed` dùng được trong notebook để gỡ lỗi. `maturity: experimental` dùng được nhưng hiện cảnh báo.
- **R2** Smoke test khi build: (a) `mlp-shim --selfcheck` (manifest hợp schema; mọi template dựng được với giá trị mẫu, kể cả `{weights}` rỗng nếu `fromScratch`; không còn placeholder rỗng ở dạng trần); (b) chuyển dataset mẫu nhỏ của task sang định dạng recipe; (c) `train` với tham số khói (vài bước) nếu có; (d) `predict` một mẫu qua handler nếu có `serve`/`assist` — cả với model vừa train lẫn thư mục model tạm từ một biến thể (20/08 §8); (e) thu được ít nhất một metric; (f) thư mục model đúng bố cục (D-28); (g) recipe chỉ suy luận: chạy `inspect` (nếu có) trên file mẫu. Handler chưa viết xong thì **không khai** `serve`/`assist` trong manifest, để phần train vẫn verified được. Kết quả lưu `smoke_report`. Builtin: CI chạy (a) trên mọi commit, (b)–(f) trên máy CI có GPU hoặc máy fleet do Instance Admin chỉ định.
- **R3** Mỗi build gắn commit + digest; job, run, model version ghi build đã dùng.
- **R4** Đổi commit = build mới; build cũ giữ để tái lập (dọn sau 180 ngày nếu không model nào dùng).
- **R5** Recipe/biến thể có `requires_ack` hoặc license thuộc danh sách cần chú ý (AGPL, research-only, non-commercial, PML) → lần đầu mỗi người dùng trong workspace phải xác nhận (`license_acks`, theo biến thể nếu license biến thể khác recipe).
- **R6** Recipe builtin không sửa nội dung; bật/tắt như mục 2.
- **R7** Trọng số pretrained tải **một lần** vào Model Zoo bằng job `cache_weights` (kiểm sha256; nguồn `hf://` dùng token HF của workspace); máy tải từ kho S3 và giữ trong volume cache. `EnsureWeights(variant)` được M03 gọi khi submit: chưa `ready` → tạo `cache_weights` và đặt phụ thuộc.
- **R8** `requiresFiles` của biến thể trỏ tới khoá zoo chưa có (vd. teacher DINOv3 bị gate) → job không submit được, UI chỉ đường tới S-17-5 để Admin tải lên.
- **R9** Task của dataset (M04) phải nằm trong `tasks` của recipe/biến thể; base model (M11) phải cùng recipe hoặc recipe khai tương thích.
- **R10** Máy lọc theo `min_vram_gb` (biến thể) và `min_compute_capability` (recipe) từ `HostInfo` (M02).

## 5. Luồng chính
1. **Dùng recipe dựng sẵn**: mở Catalog → chọn recipe → chọn biến thể → `Train` (M09) hoặc `Mở notebook với môi trường này` (M08).
2. **Nhập recipe**: `+ Thêm recipe` → nguồn (git URL + commit / pip / HF) → `InspectRepository` đọc `mlp-recipe.yaml`; không có thì mở **trình hướng dẫn** → lưu `draft` → `Build` (job `build_recipe`, agent `BuildImage`) → smoke test → `verified`.
3. **Nâng cấp**: đổi commit → build mới → smoke test → so sánh build → đặt làm build hiện hành.
4. **Xác nhận license**: lần đầu dùng recipe/biến thể có cảnh báo → `LicenseAckDialog`.
5. **Model Zoo**: Admin `Tải lên` file (presigned multipart) hoặc `Tải từ URL / HF` (job `cache_weights`) → gán khoá → biến thể dùng qua `{zoo.<key>}`.

## 6. Hợp đồng (`catalog.proto` — `CatalogService`)

| RPC | Quyền | Mô tả |
|---|---|---|
| `ListTasks` / `GetTask` | thành viên | Danh mục task |
| `ListRecipes` / `GetRecipe` | thành viên | Lọc task, modality, license, GPU, trạng thái, nguồn |
| `CreateRecipe` / `UpdateRecipe` / `DeleteRecipe` | `model.write` | Từ nguồn hoặc manifest |
| `InspectRepository` | `model.write` | Đọc manifest/LICENSE/README để điền sẵn trình hướng dẫn |
| `RenderTemplate` | `model.write` | Dựng thử lệnh với giá trị mẫu (trình hướng dẫn, tab Lệnh) |
| `BuildRecipe` / `ListBuilds` / `GetBuild` / `WatchBuild` | `model.write` | Job `build_recipe` + smoke test |
| `SetCurrentBuild` / `DeprecateRecipe` | `model.write` | |
| `ListVariants` / `CreateVariant` / `UpdateVariant` | đọc: thành viên; ghi: `model.write` | |
| `CacheWeights` | `model.write` | Tạo job `cache_weights` cho biến thể |
| `EnsureWeights` / `EnsureBuild` | nội bộ (M03) | Trả sẵn sàng hoặc `job_id` phụ thuộc |
| `ListZooFiles` / `GetZooFile` | thành viên | Phạm vi `shared` + workspace |
| `CreateZooUpload` / `CompleteZooUpload` | Admin (ws) / Instance Admin (shared) | Presigned multipart, kiểm sha256 |
| `ImportZooFile` | như trên | Từ URL/HF/gdrive → job `cache_weights` |
| `DeleteZooFile` | như trên | Chặn khi biến thể đang tham chiếu |
| `AcknowledgeLicense` | thành viên | |
| `EnableBuiltinRecipe` / `DisableBuiltinRecipe` | Owner, Admin (ws); Instance Admin (toàn instance) | |
| `ListConverters` | thành viên | |

## 7. Màn hình

### S-17-1 Catalog — `/w/{ws}/catalog`
```
┌ Model Catalog ───────────────────────────────────────────── [Model Zoo] [+ Thêm recipe] ┐
│ [Tìm…]  Task [Tất cả▾]  Dữ liệu [Ảnh│Văn bản│Âm thanh│Bảng]  License [▾]  GPU [▾]  Nguồn [▾]│
│ ── Phát hiện / phân vùng ──────────────────────────────────────────────────────────────── │
│ ┌ RT-DETRv4 ✔ ────────┐┌ DEIMv2 ✔ ───────────┐┌ D-FINE ✔ ───────────┐┌ RF-DETR ✔ ────────┐ │
│ │ detect · 4 biến thể  ││ detect · 8 biến thể  ││ detect · 9 biến thể  ││ detect, segment   │ │
│ │ 🔒 cần file zoo      ││ ⚠ phi thương mại     ││ Apache-2.0           ││ ⚠ PML vài biến thể│ │
│ └──────────────────────┘└──────────────────────┘└──────────────────────┘└───────────────────┘ │
│ ┌ Ultralytics YOLO ✔ ─┐┌ SAM 2.1 ✔ ──────────┐                                              │
│ │ 5 task · ⚠ AGPL-3.0  ││ hỗ trợ gán nhãn      │                                              │
│ └──────────────────────┘└──────────────────────┘                                              │
│ ── Giọng nói ── Whisper ✔ · VibeVoice-ASR ◇ · VibeVoice-TTS ◇ · VibeVoice-Realtime ◇        │
│ ── Văn bản ── HF Transformers text ✔ · LLM SFT (TRL) ✔ · HF Auto ✔                          │
│ ── Bảng ── Tabular GBDT ✔ (sklearn / XGBoost / LightGBM trong một recipe)                    │
│ ── Định dạng chung ── Generic ONNX ✔                                                         │
│ ── Của workspace ── my-segformer ◐ đang build · pcb-custom ✖ smoke test lỗi                 │
└─────────────────────────────────────────────────────────────────────────────────────────────┘
```
Thẻ: tên, task, số biến thể, yêu cầu GPU/VRAM, license (⚠ nếu cần chú ý), trạng thái build (✔ verified, ◐ đang build, ✖ lỗi, ◇ experimental), nguồn (builtin/workspace), 🔒 thiếu file zoo. Nhóm theo modality. Danh sách và trạng thái trên chỉ **minh hoạ** bố cục (hiện chưa recipe nào được build/verified — `recipes/README.md`); không phải giới hạn. Rỗng (chỉ khi lọc): "Không có recipe khớp bộ lọc."

### S-17-2 Recipe — `/w/{ws}/catalog/{recipe}`
> **Đợt 1 (D-44, D-49):** trang này dùng cùng canvas ở chế độ chỉ đọc, thêm tab Luồng / Toàn văn / Đang dùng ở và nút "Dùng cho project…" — xem `50-lo-trinh/03-dot-1-canvas-cong-thuc.md` §8.7.

```
┌ D-FINE [detect] [✔ verified] [Apache-2.0]  github.com/Peterande/D-FINE @ 956d170  [Train] [Mở notebook] [⋯] ┐
│ Real-time DETR detector…                                                                                  │
│ [Biến thể] [Môi trường] [Dữ liệu & tham số] [Lệnh] [Build] [Model đã train]                               │
│ Biến thể          │ VRAM tối thiểu │ Metric công bố  │ License                │ Trọng số          │         │
│ dfine-n           │ 4 GB           │ AP 42.8 (COCO)  │ Apache-2.0             │ ✔ trong zoo 15 MB │ [Train] │
│ dfine-s           │ 6 GB           │ AP 48.5         │ Apache-2.0             │ ⤓ Tải vào zoo     │ [Train] │
│ dfine-s-obj2coco  │ 6 GB           │ AP 50.7         │ ⚠ điều khoản Obj365    │ ✔                 │ [Train] │
│ … 9 biến thể (n/s/m/l/x COCO, s/m/l/x Objects365→COCO)                                                     │
└──────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```
- **Biến thể**: bảng trên; "metric công bố" là của tác giả, ghi nguồn; biến thể cần file zoo hiện trạng thái file.
- **Môi trường**: Dockerfile/base + setup, image digest hiện hành, builder (CI/job), kích thước, máy đã kéo image.
- **Dữ liệu & tham số**: định dạng đầu vào (theo task), bộ chuyển định dạng, cây thư mục đầu ra, xem trước form tham số sinh từ schema.
- **Lệnh**: template train/eval/export/predict/serve/assist, phân tán, resume, nguồn metric + bảng ánh xạ về tên chuẩn, checkpoint, artifact; `Xem lệnh đã dựng` (`RenderTemplate` với giá trị mẫu).
- **Build**: lịch sử build (commit, digest, trạng thái, báo cáo smoke test từng bước, log), `Build lại`, `Đặt làm hiện hành` (recipe của workspace).
- **Model đã train**: run/model version dùng recipe này.
- Recipe builtin: chỉ xem; `⋯` → Bật/Tắt cho workspace (Admin).

### S-17-3 Thêm recipe — trình hướng dẫn
1. **Nguồn**: git URL + commit/tag, gói pip (ghim phiên bản), hoặc HF repo + revision → `InspectRepository` đọc `mlp-recipe.yaml`, LICENSE, README.
2. **Task & dữ liệu**: chọn task (danh mục), định dạng dữ liệu đầu vào cho từng task (bộ chuyển định dạng, xem cây thư mục mẫu), hoặc khai bộ chuyển riêng.
3. **Môi trường**: Dockerfile trong repo / base image + lệnh cài; GPU bắt buộc?; VRAM, compute capability tối thiểu.
4. **Lệnh**: train (template + phân tán), resume, eval, export (định dạng), predict, serve (handler), assist; chèn biến từ danh mục placeholder ([20/08 §4.1](../20-kien-truc/08-recipe-va-runtime.md)); kiểm cú pháp khi gõ.
5. **Metric & artifact**: nguồn metric, ánh xạ về tên chuẩn của task, checkpoint, `best`, file phụ.
6. **Biến thể**: tên, config, trọng số (URL/HF/zoo), file zoo cần, license riêng.
7. **Xem lại**: `mlp-recipe.yaml` sinh ra (sửa trực tiếp, kiểm schema) → `Lưu & Build`.

### S-17-4 Danh mục task — `/w/{ws}/catalog?tab=tasks`
Bảng task: khoá, modality vào → ra, loại nhãn, bộ chuyển định dạng, hợp đồng suy luận, metric chính, số recipe hỗ trợ. P1 chỉ xem.

### S-17-5 Model Zoo — `/w/{ws}/catalog?tab=zoo` (phạm vi `shared`: `/admin?tab=zoo`)
```
┌ Model Zoo ─────────────────────────────────────────── [Tải lên] [Tải từ URL / HF] ┐
│ Khoá              │ Tên                       │ Phạm vi │ Dung lượng │ Trạng thái │ Dùng bởi              │
│ dinov3_vitb16     │ DINOv3 ViT-B/16 (teacher) │ shared  │ 343 MB     │ ✔ sẵn sàng │ rt-detrv4 (4 biến thể)│
│ dfine-n           │ (tự cache) D-FINE N COCO  │ ws      │ 15 MB      │ ✔          │ d-fine/dfine-n        │
│ whisper-small     │ (tự cache) openai/…       │ ws      │ 967 MB     │ ◐ 42%      │ whisper/whisper-small │
│ ⚠ Thiếu: dinov3_vits16plus (deimv2-x cần) — [Tải lên]                                                    │
└──────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```
`Tải lên`: chọn file → khoá (gợi ý từ danh sách thiếu) → ghi chú license (bắt buộc với file bị gate) → upload multipart, kiểm sha256. `Tải từ URL / HF`: nguồn + khoá → job `cache_weights` (dùng token HF ở S-01-10 nếu gated). Xoá bị chặn khi biến thể đang dùng.

### Hộp xác nhận license (`LicenseAckDialog`, dùng ở M08, M09, M11, M12)
"Recipe **Ultralytics YOLO** dùng license **AGPL-3.0**: … Bạn xác nhận đã hiểu điều kiện sử dụng?" → `Xác nhận` / `Huỷ`.

## 8. Sự kiện
`catalog.RecipeCreated`, `RecipeBuildStarted`, `RecipeVerified`, `RecipeBuildFailed`, `RecipeDeprecated`, `WeightsCached`, `ZooFileReady`, `ZooFileDeleted`, `LicenseAcknowledged`. M02 nghe `RecipeVerified` để kéo trước image lên máy phù hợp.

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `CATALOG_REPO_UNREACHABLE` | FAILED_PRECONDITION | Không truy cập được repo/commit |
| `CATALOG_MANIFEST_INVALID` | INVALID_ARGUMENT | `mlp-recipe.yaml` sai (chi tiết từng trường) |
| `CATALOG_TEMPLATE_INVALID` | INVALID_ARGUMENT | Template lệnh sai cú pháp hoặc dùng placeholder không tồn tại |
| `CATALOG_BUILD_FAILED` | FAILED_PRECONDITION | Build image lỗi (xem log) |
| `CATALOG_SMOKE_FAILED` | FAILED_PRECONDITION | Smoke test lỗi ở bước … |
| `CATALOG_RECIPE_NOT_VERIFIED` | FAILED_PRECONDITION | Recipe chưa sẵn sàng để train/serve |
| `CATALOG_TASK_UNSUPPORTED` | FAILED_PRECONDITION | Recipe không hỗ trợ task của dataset |
| `CATALOG_LICENSE_NOT_ACKNOWLEDGED` | FAILED_PRECONDITION | Cần xác nhận license |
| `CATALOG_ZOO_FILE_MISSING` | FAILED_PRECONDITION | Biến thể cần file `<khoá>` trong Model Zoo |
| `CATALOG_ZOO_FILE_IN_USE` | FAILED_PRECONDITION | File đang được biến thể dùng |
| `CATALOG_WEIGHTS_CHECKSUM` | DATA_LOSS | Trọng số tải về sai checksum |
| `CATALOG_WEIGHTS_GATED` | PERMISSION_DENIED | Trọng số bị gate — cần token HF có quyền |

## 10. Nghiệm thu (D-25: recipe chọn tại lúc test, không cố định)
- [ ] Với **mỗi nhóm task** (ảnh, văn bản, âm thanh, bảng), chọn ít nhất một recipe verified trong catalog → train trên dataset của người dùng từ UI, không viết code riêng; metric hiện dưới tên chuẩn của task.
- [ ] Cùng một dataset version detect train được với ít nhất ba recipe khác họ; không sửa mã nền tảng giữa các lần.
- [ ] Nhập một repo tự viết chỉ có `train.py` + `mlp-recipe.yaml` → build bằng `build_recipe`, smoke test, train được.
- [ ] Nhập một repo không có manifest qua trình hướng dẫn → `mlp-recipe.yaml` sinh ra hợp schema, build và train được.
- [ ] Biến thể cần file bị gate: thiếu file → UI chỉ tới S-17-5; Admin tải lên → job train chạy, file mount đúng `path`.
- [ ] Biến thể HF gated: không token → `CATALOG_WEIGHTS_GATED`; thêm token ở S-01-10 → `cache_weights` thành công.
- [ ] Hai recipe yêu cầu phiên bản torch khác nhau chạy song song trên cùng cụm không xung đột.
- [ ] Recipe/biến thể có cảnh báo license hiện hộp xác nhận lần đầu; model version mang license của recipe và trọng số.
- [ ] Mỗi run/model version ghi recipe, commit, build digest, biến thể.
- [ ] Recipe builtin mới trong `recipes/` → CI build, đẩy registry, smoke test; xuất hiện trong catalog sau khi verified.
