# 20/08 — Recipe và runtime: re-train mọi model đơn giản, trực quan

Quyết định nền: D-20 (recipe, không khoá hệ sinh thái), D-21 (mỗi recipe một image, Ray chỉ giữ tài nguyên), D-22 (danh mục task mở), D-23 (license), D-25 (nghiệm thu không gắn model), D-26 (mạng `mlp-net`), D-28 (thư mục model chuẩn), D-30/D-31 (build và phân phối image), D-32 (Ray head khởi động lại). Nghiệp vụ và giao diện: [M17 Model Catalog](../40-module/M17-model-catalog.md). Manifest thật: [`recipes/`](../../recipes/README.md).

## 1. Mục tiêu
Người dùng chọn **bất kỳ** họ model (dựng sẵn hoặc nhập từ repo), chọn dữ liệu, bấm train. Nền tảng không viết code riêng cho từng họ model: mỗi họ là một **recipe** (khai báo), một chương trình chung trong container (**shim**) làm phần còn lại. Recipe dựng sẵn chỉ là ví dụ; hợp đồng trong tài liệu này mới là chuẩn.

## 2. Khái niệm
| Khái niệm | Nghĩa |
|---|---|
| **Recipe** | Manifest `mlp-recipe.yaml` mô tả một họ model (mục 3) |
| **Biến thể** | Kích cỡ/cấu hình trong họ (`dfine-s`, `rfdetr-medium`…) |
| **Recipe image** | Image Docker của recipe ở một commit + shim; có digest |
| **Shim** (`mlp-shim`) | Binary .NET AOT, entrypoint của mọi container workload (mục 5) |
| **Task** | Bản ghi trong [`recipes/_tasks/tasks.yaml`](../../recipes/_tasks/tasks.yaml) — nguồn sự thật duy nhất về khoá task |
| **Bộ chuyển định dạng** | Biến dataset version (dạng chuẩn platform) thành cây thư mục recipe cần (mục 7) |
| **Model Zoo** | Kho file dùng chung: trọng số pretrained, backbone, file bị gate do Admin tải lên (M17) |
| **Thư mục model** | Bố cục chuẩn của mọi model version (mục 8) |

## 3. Manifest — tham chiếu trường (chuẩn; schema: [`mlp-recipe.schema.json`](../../recipes/_schema/mlp-recipe.schema.json))

| Trường | Bắt buộc | Ý nghĩa |
|---|---|---|
| `metadata.name/displayName/family/description` | ✔ | Định danh, hiển thị |
| `metadata.maturity` | | `stable` (dùng được để train/serve khi verified) · `experimental` (hiện cảnh báo) · `draft` (chỉ notebook) |
| `metadata.verification` | | `sourcesCheckedAt`, `built`, `smokeTested`, `notes` |
| `source` | ✔ | Một trong: `git` + `commit` (40 hex) · `pip: [gói chính==phiên bản, gói phụ…]` (gói phụ chưa ghim → cảnh báo; build ghi lock file) · `hf: <repo id>` + `revision` |
| `license.code/weights/notes/requiresAck` | ✔ | D-23; `requiresAck: true` hoặc `notes` khác rỗng → phải xác nhận lần đầu |
| `tasks` | ✔ | Khoá trong `tasks.yaml` |
| `environment.dockerfile` **hoặc** `environment.base` + `setup` | ✔ | Môi trường |
| `environment.gpu/minVramGb/minComputeCapability/python/shmSizeGb` | | Yêu cầu phần cứng; scheduler lọc máy theo đây |
| `data.input` | ✔ | Khoá bộ chuyển định dạng, **hoặc** map `task → khoá` khi recipe nhiều task |
| `data.splitNames` | | Đổi tên split chuẩn `train/val/test` (vd. `{val: valid}`) |
| `converters` | | Bộ chuyển định dạng do recipe cung cấp: `[{key, tasks, from, script, output}]` — `from` là bộ chuyển chuẩn mà script đọc đầu ra |
| `variants[]` | ✔ | `name`, `config`, `weights` (chuỗi hoặc map `task → nguồn`; `hf://repo@rev`, `gdrive://`, URL, `zoo://<khoá>`), `classes` (lớp của trọng số pretrained: danh sách hoặc khoá danh sách dùng chung, vd. `coco80`), `requiresFiles: [{zoo\|url, path}]`, `defaults` (giá trị tham số riêng), `params` (số tham số, triệu), `reported`, `minVramGb`, `license`, `requiresAck`, `tasks` |
| `params` | | JSON Schema từng tham số; `from:` gắn giá trị ngữ cảnh (vd. `dataset.classes.count`); `required: true` |
| `entrypoints.train/eval/export/predict/inspect` | ít nhất một entrypoint bất kỳ (kể cả chỉ `serve`/`assist`) | `command`, `workdir`, `env`, `distributed`, `resume`, `checkpoints`, `metrics` (ghi đè nguồn metric cho entrypoint); riêng: `train.fromScratch` (mặc định `false`, mục 4.2), `export.formats` + `export.outputs` (mục 5.4), `predict.inputs` (mục 5.4), `inspect` (mục 5.5) |
| `entrypoints.serve` | | `handler: module:Class`, `batching`, `stream` |
| `data.generatedFiles` | | `[{path, template}]` — file dựng từ template (giá trị thô) trước khi chạy, vd. `data.yaml` |
| `entrypoints.assist` | | `kinds: [segment|prelabel|transcribe|score]`, `handler`. Recipe có `assist` mà không có `serve` (vd. `sam2`) chỉ dùng cho deployment `assist`, không hiện ở hộp Deploy/Train |
| `distributed` | | `none` · `torchrun` · `accelerate` · `internal` (framework tự phân tán, shim chỉ đưa danh sách GPU). `ray` (container recipe tự nối cụm Ray) **để P3**: image phải đúng phiên bản Ray/Python của cụm — validator P1 từ chối |
| `resume` | | `{mode: append, args: "…"}` · `{mode: replace, from: "…", to: "…"}` · `{mode: command, command: "…"}` |
| `entrypoints.train.resumeFidelity` | ✔ khi có `checkpoints` | `exact` · `epoch` · `none` — hộp Train cảnh báo khi `none` hoặc không có `checkpoints` ([20/10 §4.2](10-quan-sat-va-khoi-phuc.md)) |
| `entrypoints.train.checkpointEveryMin` | | Chu kỳ checkpoint gợi ý (nền tảng khuyến nghị ≤ 30) |
| `entrypoints.train.progress` | | `{metric, total, unit, stallAfterMin}` — shim tính %, ETA và phát hiện treo từ metric sẵn có ([20/10 §2.2–2.3](10-quan-sat-va-khoi-phuc.md)) |
| `metrics.sources/primary/map` | ✔ | Mục 5.3; `primary` nên là tên chuẩn của task; recipe không đo được thì dùng metric gần nhất (vd. `train/loss`, UI ghi chú) hoặc `none` (chỉ suy luận) |
| `artifacts.best` | ✔ khi có `train` | `{from: [glob…], as: <tên>}` — glob tương đối theo `{output}`; mục đầu tiên tồn tại được chép vào `weights/<as>`; mục là thư mục (vd. `.`) → chép cả thư mục, **bỏ** những gì khớp `checkpoints` và thư mục log metric. Recipe chỉ suy luận vẫn khai `{as: <tên>}` (không `from`) để import/biến thể đặt file chính đúng chỗ (mục 5.5, 8) |
| `artifacts.files` | | `[{from, to}]` — chép thêm vào thư mục model (config, tokenizer…) |
| `artifacts.baseWeights` | | Template trả về **ref** (thường `{weights.ref}`), không phải đường dẫn; có giá trị → model là **adapter**, `recipe.lock.json` ghi base (mục 8) |
| `openItems` | | Điểm chưa xác minh |

## 4. Ngữ pháp template (chuẩn)

Áp dụng cho `command`, `resume`, `env`, `requiresFiles.path`, `converters.script`, `artifacts.baseWeights`, đường dẫn trong `metrics.sources`, `metrics.primary`, `variants.weights` và `data.generatedFiles` — **không** áp cho `regex.pattern`.

| Cú pháp | Kết quả |
|---|---|
| `{name}` | Giá trị của placeholder |
| `{name?TEXT}` | `TEXT` nếu giá trị *truthy* (true, chuỗi khác rỗng, số khác 0, danh sách khác rỗng); ngược lại rỗng. `TEXT` được chứa placeholder, không lồng điều kiện |
| `{name\|a=TEXT1\|b=TEXT2\|*=TEXT}` | Chọn theo giá trị; `*` là mặc định; không khớp và không có `*` → lỗi |
| `{{` · `}}` | Dấu ngoặc nhọn thật (vd. JSON trong lệnh) |

**Hiển thị giá trị:** bool → `true`/`false` · số giữ nguyên · danh sách → nối bằng dấu phẩy · object → JSON gọn. **Rỗng/null:** trong `{name?…}` và `{name|…}` là giá trị falsy hợp lệ; ở dạng trần `{name}` là **lỗi** `placeholder_empty` (mã 2) — giá trị tuỳ chọn phải viết `{name?--flag {name}}`, không bao giờ để cờ mồ côi.
**Thực thi:** `command` sau khi dựng chạy bằng `/bin/sh -c`; giá trị placeholder được **shell-quote** nếu chứa ký tự ngoài `[A-Za-z0-9_./:=@%+,-]`; chữ tĩnh của template không bị quote. File template (`data.generatedFiles`) chèn giá trị thô.
**Lỗi:** placeholder không tồn tại, rỗng ở dạng trần, hoặc tham số `required` thiếu → shim thoát mã 2 với `MLP_ERROR {"reason":"placeholder_missing|placeholder_empty","name":…}` trước khi chạy gì. Validator (`mlp-shim --selfcheck`, M17 R2a) dựng mọi template với giá trị mẫu, kể cả trường hợp `{weights}` rỗng khi `fromScratch: true`.

### 4.1 Danh mục placeholder
| Nhóm | Placeholder |
|---|---|
| Dữ liệu | `{data}` (gốc dữ liệu đã chuyển định dạng), `{data.train}`, `{data.val}`, `{data.test}` (thư mục/tệp split sau `splitNames`), `{dataset.classes.count}`, `{dataset.classes.names_csv}`, `{dataset.classes.json_path}`, `{dataset.target_column}` |
| Đầu ra | `{output}` (thư mục làm việc của attempt), `{checkpoint}` (checkpoint để resume) |
| Trọng số | `{weights}` (đường dẫn cục bộ của điểm xuất phát khi train: pretrained của biến thể hoặc model version; rỗng khi train từ đầu), `{weights.ref}` (nguồn của điểm xuất phát dạng ref: `hf://repo@rev`, `zoo://<khoá>`, `model://{model}@v{n}`, URL), `{model}` (thư mục model chuẩn — eval/export/serve/predict), `{model.weights}` (`{model}/weights/<artifacts.best.as>`), `{base_weights}` (thư mục base model cho adapter), `{zoo.<key>}` (đường dẫn file Model Zoo đã mount) |
| Recipe | `{recipe.src}` (`/opt/recipe/src`), `{variant.<trường>}`, `{task}`, `{format}` (export) |
| Bộ chuyển của recipe | `{converter.src}` (cây đầu ra của bộ chuyển chuẩn `from`), `{converter.dst}` (thư mục ghi) |
| Tham số | `{params.<tên>}`, `{params.json}` (toàn bộ tham số dạng JSON) |
| Đầu vào predict | `{input.<tên>}` — chỉ các tên khai trong `predict.inputs` (mục 5.4) |
| Phần cứng & phân tán | `{nnodes}`, `{node_rank}`, `{gpus_per_node}`, `{gpu_ids_csv}` (chỉ số **trong container**: `0,1,…,n-1` — container chỉ thấy GPU được cấp), `{master_addr}`, `{master_port}`, `{device}` (`cuda:0`/`cpu`), `{gpu.compute_capability}`, `{gpu.supports_bf16}` |

### 4.2 Train từ đầu
UI (M09) chỉ cho chọn "Từ đầu" khi `train.fromScratch: true`; khi đó `{weights}` và `{weights.ref}` rỗng, lệnh phải dùng dạng có điều kiện (vd. `{weights?-t {weights}}`). Recipe không khai `fromScratch` luôn cần điểm xuất phát (pretrained hoặc model version).

## 5. Shim

### 5.1 Vòng đời một attempt
1. Đọc job spec (`MLP_JOB_SPEC_URI`) + token `mlpj_`.
2. **Dữ liệu**: dataset version đã được chuyển định dạng **trước** bởi job `convert` (M03 tạo phụ thuộc tự động qua `DataService.EnsureConverted`, cache theo `(dataset_version, converter)` — dùng chung giữa các recipe, luôn dùng tên split chuẩn); shim tải/mount cây thư mục chỉ-đọc rồi dựng `{data}` là một thư mục symlink áp `data.splitNames` (vd. `valid → val`), không sửa cache. `host_local`: job `convert` chạy trên chính máy chứa dữ liệu, ghi vào volume cache của máy.
3. **File**: tải `{weights}`, `{base_weights}`, `{model}` và mount `requiresFiles` từ Model Zoo vào `path` khai trong biến thể; thiếu file → thoát mã 2.
4. Ghi `classes.json` (từ dataset version) vào `{output}`.
5. Dựng lệnh (mục 4), chạy tiến trình con; stdout/stderr của container được **agent** đọc và gửi qua kênh agent (log không đi qua shim).
6. Thu metric (5.3) → `TrackingService.LogMetrics` (token `mlpj_`, kèm `attempt`); checkpoint (glob `checkpoints`) → đăng ký local rồi upload nền thành checkpoint **bền** (`RuntimeService.RegisterCheckpoint`); tiến độ theo bước chuẩn + heartbeat 30 s → `RuntimeService`; trạng thái riêng ở `{output}/.mlp/state.json`. `{output}` là volume `mlp-run-{run}` trên máy, không nằm trong filesystem container ([20/10 §4.2](10-quan-sat-va-khoi-phuc.md)).
7. Kết thúc: train → dựng **thư mục model** (mục 8) từ `artifacts`; export/predict → đầu ra theo mục 5.4; inspect → mục 5.5; upload; `RuntimeService.ReportExit` (mục 12).
8. Nhận dừng: SIGTERM cho tiến trình con → chờ 60 s → SIGKILL.

### 5.2 Resume
`append`: thêm `args` vào cuối lệnh · `replace`: thay đoạn `from` bằng `to` (vd. `-t {weights}` → `-r {checkpoint}`) · `command`: chạy lệnh khác hẳn.
Có `MLP_RESUME_FROM` → shim khôi phục checkpoint (từ volume local hoặc tải từ kho S3) vào `{checkpoint}` trước khi dựng lệnh; báo bước `restoring_checkpoint` ([20/10 §4.6](10-quan-sat-va-khoi-phuc.md)).

### 5.3 Metric
| Nguồn | Cách đọc |
|---|---|
| `tensorboard: <thư mục>` | Đọc đệ quy file sự kiện; khoá = tag |
| `jsonl: <tệp>` | Mỗi dòng một object; khoá mảng dạng `a[0]` lấy phần tử |
| `csv: <tệp>` | Header là khoá; mỗi dòng một step (cột `epoch`/`step` nếu có) |
| `json: <tệp>#<đường dẫn>` | Mảng object (vd. `trainer_state.json#log_history`) |
| `regex: {stream, pattern}` | Nhóm có tên → khoá |
| SDK | `mlp.log()` từ mã repo/handler |
`metrics.map` đổi khoá nguồn → **tên chuẩn của task** (`tasks.yaml`); khoá không có trong map vẫn được lưu với tên gốc. Entrypoint có thể khai `metrics` riêng (vd. eval ghi `metrics.jsonl`).

### 5.4 Đầu ra của export và predict
- **export**: sau khi lệnh thoát 0, shim upload các file khớp `export.outputs` (glob theo `{output}`, mặc định mọi thứ trong `{output}`; thư mục được nén `.tar` khi upload) vào `mlp-exports/{ws}/models/{m}/v{n}/{format}/`; URI này là `registry.exports.artifact_uri` (M11). Lệnh export phải tự dọn file tạm (vd. bản sao trọng số).
- **predict** (theo lô): `predict.inputs` khai từng đầu vào `{<tên>: {type: file|text|string|number, description, required}}`; UI/SDK thu theo khai báo, shim đặt file vào `/mnt/input/` và cấp `{input.<tên>}`. Mọi file trong `{output}` được upload làm kết quả lần chạy.

### 5.5 Import model (job `inspect_model`)
Dùng cho M11 S-11-4 (upload file/thư mục, repo HF, file trong Model Zoo):
1. Chạy trong **image của recipe người dùng chọn** (hoặc `generic-onnx`/`hf-auto`), ghim máy bất kỳ có đủ RAM; không cần GPU.
2. Shim đặt nguồn vào thư mục model nháp: một file → `weights/<artifacts.best.as>`; thư mục/repo HF → `weights/<as>/`; biến thể đã chọn → `config/variant.json`.
3. Nếu recipe khai `entrypoints.inspect` (`command` + `configSchema` tuỳ chọn): chạy lệnh với `{model}` = thư mục nháp; lệnh ghi **đề xuất** `{output}/signature.json`, `{output}/classes.json`, `{output}/config/*` (vd. `generic-onnx` ghi `config/onnx.json` từ graph). Không khai `inspect`: signature lấy theo hợp đồng của task (`tasks.yaml`), classes lấy từ `config.json` (`id2label`) nếu là thư mục HF, còn lại để người dùng nhập.
4. UI hiện đề xuất ở bước ③ của S-11-4: bảng signature, danh sách lớp (sửa được), và form sinh từ `configSchema` cho từng file `config/*.json`. Người dùng xác nhận → shim ghi thư mục model cuối cùng + `recipe.lock.json` (`source: import`, `weights.ref` của nguồn) → version `ready`.
5. Smoke: `Handler.load` + `predict` một mẫu ví dụ của task trước khi `ready`; lỗi → version `failed` kèm log.

## 6. Môi trường và build (D-30, D-31)
- Image = Dockerfile (hoặc `base` + `setup`) của recipe + lớp cuối platform thêm: `mlp-shim`, `mlplatform-sdk`, `mlp-serve`. Build context là thư mục `recipes/` (để chép `_shared/`).
- **Recipe builtin**: CI của nhà phát hành build và đẩy lên **registry phân phối** (`recipes/{recipe}:{commit}-{hash}`), chạy smoke test trên máy CI có GPU; mỗi instance đồng bộ về registry riêng bằng việc nền `sync_images` (D-31).
- **Recipe của workspace**: job `build_recipe` chạy trên một máy có Docker (agent lệnh `BuildImage`, `docker buildx build --platform <environment.platforms, mặc định linux/amd64>`; scheduler ưu tiên máy cùng kiến trúc), push `registry/recipes/ws-{ws}/{recipe}:{commit}-{hash}` của instance. Máy GPU chỉ kéo image có biến thể kiến trúc của mình.
- Digest được ghi vào build; job/run/model version ghi digest đã dùng.
- Agent kéo trước image recipe hay dùng khi rảnh; notebook mở được trong môi trường của recipe (M08).

## 7. Dữ liệu: dạng chuẩn và bộ chuyển định dạng
Nhãn lưu ở dạng chuẩn theo task (M05). Split chuẩn: `train`, `val`, `test` (recipe đổi tên bằng `data.splitNames`). Mọi bộ chuyển định dạng ghi thêm `{data}/classes.json`. Khoá chuẩn của từng task: [`tasks.yaml`](../../recipes/_tasks/tasks.yaml) (`converters`).

| Khoá | Task | Cây thư mục đầu ra |
|---|---|---|
| `coco` | detect, segment | `{split}/images/*`, `{split}/annotations.json` (category_id 0..N-1 liên tục) |
| `coco-keypoints` | pose | như `coco` + `keypoints`, `skeleton` |
| `roboflow-coco` | detect, segment | `{split}/*.jpg`, `{split}/_annotations.coco.json` |
| `yolo` | detect | `images/{split}/*`, `labels/{split}/*.txt` (`cls cx cy w h`), `data.yaml` (`train, val, test, names`; **không** ghi `path` — ultralytics lấy thư mục chứa file) |
| `yolo-seg` · `yolo-pose` · `yolo-obb` | segment · pose · obb | như `yolo`, nhãn polygon / keypoints (`kpt_shape` trong `data.yaml`) / 4 góc |
| `voc` | detect | `{split}/JPEGImages/*`, `{split}/Annotations/*.xml` |
| `dota` | obb | `{split}/images/*`, `{split}/labelTxt/*.txt` (`x1 y1 x2 y2 x3 y3 x4 y4 class difficult`) |
| `mask-png` | semantic | `{split}/images/*`, `{split}/masks/*.png` (pixel = class id, 255 = bỏ qua) |
| `depth-png` | depth | `{split}/images/*`, `{split}/depth/*.png` (uint16, đơn vị mm) |
| `cls-folders` | classify | `{split}/{class}/*` |
| `hf-imagefolder` | classify | `{split}/metadata.jsonl` (`file_name`, `label`) + ảnh |
| `hf-jsonl` | text-classify | `{split}.jsonl`: `{"sentence", "label"}` |
| `hf-jsonl-ner` | ner | `{split}.jsonl`: `{"tokens": [...], "ner_tags": [...]}` + `labels.json` |
| `conll` | ner | `{split}.conll` (token TAB nhãn BIO, câu cách nhau bằng dòng trống) |
| `chat-jsonl` · `prompt-completion-jsonl` · `preference-jsonl` | generate | `{split}.jsonl`: `{"messages"}` · `{"prompt","completion"}` · `{"prompt","chosen","rejected"}` |
| `hf-audiofolder` | asr, audio-classify | `{split}/metadata.jsonl` (`file_name`, `text` hoặc `label`) + file audio |
| `audio-folders` | audio-classify | `{split}/{class}/*.wav` |
| `asr-manifest` | asr | `{split}.jsonl`: `{"audio","text","speaker"?,"segments"?}` |
| `tts-manifest` | tts | `{split}.jsonl`: `{"audio","text","speaker","voice_prompt"?}` |
| `parquet` | tabular-classify, tabular-regress | `{split}.parquet` + `schema.json` (`task`, `target`, `features`) |
| `none` | (recipe chỉ suy luận) | — |

Recipe cần định dạng khác khai trong `converters` (script Python chạy trong image recipe, đọc dạng chuẩn `asr-manifest`/`tts-manifest`/… và ghi cây riêng) — vd. `vibevoice-asr-dir`, `vibevoice-tts-jsonl`.

## 8. Thư mục model (D-28)
```
model/
  weights/<as>            # file chính (artifacts.best) + file phụ cần cho serve
  config/                 # artifacts.files (config train, args.yaml, tokenizer…)
  classes.json            # tên lớp theo thứ tự id
  signature.json          # task + kiểu vào/ra (M11)
  recipe.lock.json        # recipe, commit, imageDigest, variant, task, params, datasetVersion,
                          # weights: {ref} (điểm xuất phát), baseWeights: {ref: "hf://…@rev"|"zoo://<khoá>"|"model://…", sha256} (adapter)
```
Model **adapter** (LoRA…) ghi `baseWeights`; khi eval/serve, shim tải base vào `{base_weights}` và `Handler.load` nhận `base_dir`.

**Thư mục model tạm từ biến thể catalog** (deployment `assist`, pre-label từ biến thể — M05 R6, M12 R9): shim tải trọng số biến thể vào **`weights/<artifacts.best.as>`** (cùng chỗ với model đã train), ghi `config/variant.json` (mọi trường của biến thể), `classes.json` từ `variants.classes` (danh sách dùng chung ở `recipes/_shared/classes/`), `signature.json` theo task. **Handler phải chạy được ở cả hai chế độ**: có `config/variant.json` mà không có config train → dựng model từ config gốc của biến thể (vd. DETR dùng `/opt/recipe/src/{variant.config}`). Biến thể không có file trọng số (thư viện tự tải, vd. RF-DETR) → shim chỉ ghi `config/variant.json`, handler tự khởi tạo theo tên biến thể. Chỉ biến thể **có khai `classes`** mới được chọn làm nguồn pre-label (model nền chưa có đầu phân loại, vd. encoder HF, bị ẩn).

## 9. Phân tán, tài nguyên, mạng (D-21, D-26, D-29)
1. Scheduler tạo **placement group** (một bundle mỗi máy; `STRICT_PACK` một máy, `STRICT_SPREAD` nhiều máy); mỗi bundle một **holder** actor trong `mlp-node`.
2. Agent khởi chạy `mlp-ray-node` với `CUDA_VISIBLE_DEVICES=<UUID…>` nên Ray coi UUID là ID GPU; holder đọc `get_accelerator_ids()` → UUID, gọi `RuntimeService.ReportAllocation(attempt, host, gpu_uuids)` (kiểm ở P0-15; nếu Ray trả chỉ số thì holder ánh xạ qua danh sách `CUDA_VISIBLE_DEVICES`). Bundle xin `host:<id>: 0.001`, `pool:<p>: 0.001` (số lẻ — mỗi node khai `1`, nên nhiều bundle cùng máy vẫn xếp được) cùng CPU/GPU/bộ nhớ thật.
3. api gửi `StartWorkload` cho agent từng máy: image digest, `--gpus device=<UUID…>`, `--network container:mlp-net`, env, mount, rank.
4. Nhiều máy: shim rank 0 mở cổng rendezvous trên IP overlay; các máy khác chờ rồi chạy lệnh phân tán của recipe (`torchrun`/`accelerate`).
5. Một máy chết → dừng container còn lại → retry/resume (M03).
6. Ray head khởi động lại → agent giữ container tối đa 15 phút; attempt còn sống thành `detached`, `mlp-ray-node` khởi động lại chỉ với GPU rảnh; khi hết attempt `detached` thì khởi động lại với đủ GPU (D-32, M03 R6b).

## 10. Giao thức phục vụ và kiểu dữ liệu theo task
`mlp-serve` (Python) nạp handler và phơi gRPC `ModelServer` (`Predict`, `PredictStream`, `Health`, `Metadata`) trên IP overlay; gateway (M12) chuyển sang REST/gRPC công khai; adapter pre-label (M05) chuyển sang geometry chuẩn.

```python
class Handler(Protocol):
    def load(self, model_dir: str, base_dir: str | None, device: str, signature: Signature) -> None: ...
    # signature: task, inputs/outputs (signature.json) + params (từ recipe.lock.json) — handler đọc tham số ở đây
    def predict(self, batch: list[TaskInput]) -> list[TaskOutput]: ...
    def stream(self, inp: TaskInput) -> Iterator[TaskChunk]: ...   # tuỳ chọn (generate, tts)
```
Kiểu trong gói `mlp_serve.types` (dataclass):

| Task | `TaskInput` | `TaskOutput` |
|---|---|---|
| detect · segment · pose · obb | `ImageInput(path, conf=0.25, iou=0.7, imgsz=None)` | `DetectOutput(width, height, objects=[Obj(class_id, class_name, score, x1, y1, x2, y2, polygon?, mask_rle?, keypoints?, angle?)])` — **toạ độ pixel** |
| classify | `ImageInput` | `ClassifyOutput(labels=[(label, score)])` |
| semantic | `ImageInput` | `SemanticOutput(width, height, mask_png: bytes)` (pixel = class id) |
| depth | `ImageInput` | `DepthOutput(width, height, depth_png: bytes, unit="mm")` |
| audio-classify | `AudioInput` | `ClassifyOutput` |
| assist `segment` | `SegmentInput(path, points=[(x, y, label)], box?)` | `DetectOutput` (một `Obj` có `polygon`/`mask_rle`) |
| text-classify | `TextInput(text)` | `ClassifyOutput` |
| ner | `TextInput(text)` | `NerOutput(entities=[(label, start, end, text, score)])` |
| generate | `ChatInput(messages, max_tokens, temperature)` | `GenerateOutput(text, prompt_tokens, completion_tokens)`; chunk `TextChunk(text)` |
| asr | `AudioInput(path, language?)` | `AsrOutput(text, segments=[(start, end, text, speaker?)])` |
| tts | `TtsInput(text, speaker?, voice_prompt_path?, format="wav")` | `TtsOutput(audio_path, sample_rate, duration_ms)`; chunk `AudioChunk(bytes)` |
| tabular-classify · tabular-regress | `TabularInput(row: dict)` | `TabularOutput(label?, probabilities?, value?)` |
Adapter pre-label đổi pixel → chuẩn hoá 0–1 (M05 §3.1); gateway đổi sang hợp đồng JSON/proto của M12.

## 11. Hỗ trợ gán nhãn (`assist`)
`segment` (phân vùng tương tác, chạy như deployment `assist` M12; vào `SegmentInput`) · `prelabel` (job `label_assist` dùng `predict` của handler) · `transcribe` (ASR nháp) · `score` (độ bất định cho active learning: handler trả thêm `score` trên mỗi đầu ra). Mọi recipe có `serve` dùng được cho `prelabel` qua adapter.

## 12. Mã thoát
| Mã | Nghĩa | Scheduler |
|---|---|---|
| 0 | Thành công | `succeeded` |
| 2 | Lỗi người dùng (tham số, dữ liệu, file thiếu) — `MLP_ERROR {json}` ở stderr | `failed (user_error)`, không retry |
| 3 | Lỗi recipe/repo | `failed (recipe_error)`, không retry |
| 137 / dấu OOM | Hết bộ nhớ | `failed (oom)` + gợi ý |
| khác | Lỗi hạ tầng | retry theo chính sách |

## 13. Catalog dựng sẵn (ví dụ, không phải giới hạn — D-25)
Manifest ở [`recipes/`](../../recipes/README.md) (đối chiếu nguồn 2026-11-17; **chưa build/smoke test**).

| Recipe | Task | Nguồn ghim | License / cảnh báo |
|---|---|---|---|
| RT-DETRv4 | detect | AIMIA-PKU/RT-DETRv4 @55fefaa | Apache-2.0; checkpoint không ghi; cần teacher DINOv3 (Model Zoo) cho train/eval |
| D-FINE | detect | Peterande/D-FINE @956d170 | Apache-2.0; biến thể Objects365: điều khoản dataset |
| DEIMv2 | detect | Intellindust-AI-Lab/DEIMv2 @f005f50 | **Chỉ phi thương mại** |
| RF-DETR | detect, segment | `rfdetr==1.12.1` | Apache-2.0; XL/2XL: PML-1.0 |
| Ultralytics YOLO | detect, segment, classify, pose, obb | `ultralytics==8.4.215` | AGPL-3.0 |
| HF Transformers text | text-classify, ner | transformers 5.19.0 (script ghim tag) | Apache-2.0; model theo card |
| HF Auto | mọi task có pipeline HF (serve/import) | `transformers==5.19.0` | theo model card |
| LLM SFT (TRL) | generate | `trl[peft]==1.18.1` | Apache-2.0; LLM theo card |
| Whisper | asr | transformers 5.19.0 | MIT |
| VibeVoice-ASR / TTS / Realtime | asr / tts | microsoft/VibeVoice, fork cộng đồng | MIT; TTS: cảnh báo nghiên cứu · maturity `experimental` |
| SAM 2.1 | assist `segment` | facebookresearch/sam2 @2b90b9f | Apache-2.0 |
| Generic ONNX | classify, detect, bảng (serve/import) | `onnxruntime-gpu==1.32.0` | MIT (runtime); model theo người import |
| Tabular GBDT | tabular-classify, tabular-regress | sklearn, xgboost, lightgbm | BSD/Apache/MIT |

Ghi chú khi đọc nguồn để viết recipe (chưa kiểm bằng build): repo hay tự tải trọng số lúc train (tải trước trong image), thiếu phụ thuộc trong requirements, cờ AMP phải truyền tường minh, transformers ≥5.15 bỏ `--logging_dir`, script ví dụ của transformers bị xoá khỏi `main` (ghim tag), license đổi giữa các commit (DEIMv2) → ghim commit và ghi license theo commit.
