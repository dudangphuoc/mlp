# 02 — Recipe và runtime: phản biện đề xuất canvas từ mã thật

Ký hiệu: **[R]** đã đọc trong repo (kèm file:dòng) · **[N]** biết từ nghề, chưa kiểm ở commit ghim (mã repo gốc không nằm trong repo này, chỉ được clone lúc build image) · **[S]** suy đoán. Không build, không chạy gì. Đường dẫn rút gọn: `Shim/` = `src/Hosts/MLPlatform.Shim/`, `Api/` = `src/Hosts/MLPlatform.Api/Infrastructure/`, `App/` = `src/ControlPlane/MLPlatform.ControlPlane.Application/`.

## 0. Ba sự thật làm đổi đề bài

1. **Runtime hiện chỉ hiện thực một phần hợp đồng recipe.** Shim làm đúng năm việc: tải file dataset, chạy `to_coco.py` khi `data.input == coco`, tải trọng số `https://`, khôi phục checkpoint, chạy lệnh (`Shim/Runner.cs:25-30`). Các mục `data.generatedFiles`, `requiresFiles`/zoo, `data.splitNames`, `converters` của recipe, trọng số `hf://`/`gdrive://`/`zoo://`, "train tiếp từ model version" chỉ xuất hiện trong bộ kiểm manifest (`src/Framework/MLPlatform.Recipes/RecipeChecker.cs:82-86`), không có trong shim hay `StartTraining` (`App/Tracking/TrackHandlers.cs:143-150` chỉ nhận trọng số của biến thể bắt đầu bằng `https://`). Hệ quả: canvas vẽ theo manifest sẽ vẽ ra những thứ máy không làm; kiểu train thứ ba ("train tiếp model của tôi") chưa có đường chạy nào. Theo mã đọc được (chưa chạy để kiểm), chỉ D-FINE chắc chắn đủ đường; RT-DETRv4 cần zoo, DEIMv2 cần `gdrive://`, các recipe khác chỉ chạy nếu dataset đã sẵn đúng định dạng và trọng số do thư viện tự tải.
2. **Control plane chỉ từng gửi entrypoint `train`** (`TrackHandlers.cs:147` là nơi duy nhất đặt `entrypoint`). `eval`, `export`, `predict` có trong manifest nhưng chưa bao giờ được chạy. Hai node "Đánh giá" và "Xuất model" hiện không ứng với mã nào trong luồng train.
3. **Không recipe nào khai `entrypoints.train.progress`** (grep `progress:` trong `recipes/` chỉ ra schema dòng 745). Pha `running` vì thế luôn báo `total = 0`: không %, không ETA (`Runner.cs:66-76,85-86`). "Người dùng mù" có một phần nguyên nhân là thiếu ba dòng YAML, chưa cần tới canvas.

## 1. Giải phẫu: thứ quyết định kết quả mà người dùng không thấy (mẫu: D-FINE)

Form hiện phơi 5 tham số (`recipes/d-fine/mlp-recipe.yaml:41-45`). Phần còn lại:

| # | Thứ ẩn | Bằng chứng | Hậu quả |
|---|---|---|---|
| 1 | Môi trường: torch 2.5.1, `transformers==4.48.3`, còn lại pip **không ghim** | `recipes/d-fine/Dockerfile:3,9,13` | Hai máy build khác ngày ra môi trường khác nhau dưới cùng một tag (mục 3a) |
| 2 | Commit repo gốc khai **hai nơi**, không ràng buộc nhau | manifest `:15` và `Dockerfile:6`; lock chép từ manifest (`Runner.cs:436`) | Lock có thể ghi sai commit thật |
| 3 | Backbone HGNetv2 pretrained nướng sẵn trong image | `Dockerfile:17-18`, manifest `:59` | "Train từ đầu" thực ra là từ backbone ImageNet |
| 4 | Config gốc của biến thể (augment, optimizer, LR backbone, EMA, kích thước ảnh) | chỉ là một đường dẫn `variant.config` (`:31-39`) | Toàn bộ node "Tăng cường" nằm trong file người dùng không mở được |
| 5 | `make_config.py`: co giãn 9 khoá lịch theo tỉ lệ epoch, tắt `remap_mscoco_category`, đặt batch tổng | `recipes/_shared/detr_family/make_config.py:29-39,75-80,104,115,117-125` | Đổi epochs là đổi cả lịch augment |
| 6 | Chia tập: cắt 15 % làm val khi dataset không có val; hạt giống = `seed` của train; không có test nếu dataset không có thư mục test | `recipes/_shared/converters/to_coco.py:151-159,201-212,221`; `Runner.cs:269-270` không truyền `--val-fraction` | Đổi seed train là đổi luôn tập val → hai run không so được |
| 7 | Lọc nhãn âm thầm: bỏ box < 1 px, bỏ class id lạ | `to_coco.py:176-178,182` | Mất nhãn không ai báo ngoài một dòng log |
| 8 | Mẫu lệnh và resume (`-t` ↔ `-r`), chỉ `last.pth` | manifest `:50-58`, `openItems :105` | Hỏng ở giai đoạn cuối thì lùi về trước `stop_epoch` |
| 9 | Ánh xạ metric: TensorBoard (theo iteration) và `log.txt` (theo epoch) cùng đổ về `train/loss`, `val/mAP50-95`; trùng `(key, step)` thì điểm đến trước thắng | manifest `:86-98`; `Shim/Metrics.cs:82-85` | Một đường cong trộn hai trục hoành; metric chính quyết định "run tốt nhất" |
| 10 | Chọn artifact `best_stg2` trước `best_stg1`; handler ưu tiên trọng số `ema`; bỏ thư mục tên `runs`/`summary` | manifest `:100`; `_shared/detr_family/handler.py:46`; `Runner.cs:479` | Model đem serve không hẳn là model của điểm metric tốt nhất |
| 11 | Mặc định của shim: tham số thiếu lấy `default`; `master_port 29500`; chờ upload 24 giờ; `ModuleNotFoundError` → lỗi recipe | `Shim/JobSpec.cs:93-103,121-131`; `Runner.cs:129,336-338` | |
| 12 | Biến môi trường: `MLP_JOB_SPEC` (nhúng **toàn bộ manifest**), `MLP_SHM_GB`, `MLP_RESUME_FROM`; URL dữ liệu ký 24 giờ | `TrackHandlers.cs:147,162-163`; `Api/Scheduler.cs:221-237`; `App/Data/DataHandlers.cs:163` | Resume sang máy khác sau 24 giờ không tải lại được dữ liệu — đúng kịch bản "treo máy vài ngày" |
| 13 | Tiền xử lý lúc serve: resize thẳng về `eval_spatial_size`, chuẩn hoá ImageNet nếu config có chữ "dinov3" | `handler.py:51-54` | Số đo D-39 khác val trong lúc train |
| 14 | Tên metric chuẩn và bộ chuyển theo task | `recipes/_tasks/tasks.yaml:9-15` | Toàn cục, không thuộc recipe nào |

## 2. Dòng đời thật và đối chiếu bảy node

| Bước thật | Mã | Báo tiến độ |
|---|---|---|
| A. Kiểm đồng bộ, chụp manifest vào spec, tạo job + run | `TrackHandlers.cs:71-175` | Lỗi tức thì |
| B. Xếp hàng → giữ tài nguyên (Ray) | `Scheduler.cs:14-39` | Có pha |
| C. **Build image trên máy, sau khi đã giữ GPU** | `Scheduler.cs:195-200,271-303`; `src/Hosts/MLPlatform.Agent/CommandHandler.cs:154-175` | Chỉ 2–3 sự kiện chữ; log build nằm ở `agent.log`; thanh bước không có pha này (`web/src/components/JobProgress.tsx:12-15`) |
| D. Tải dataset | `Runner.cs:153-234` | `preparing_data` n/N file |
| E. Chuyển COCO + cắt val | `Runner.cs:261-281` | 0/1, không số liệu split |
| F. Tải trọng số | `Runner.cs:237-255` | 0/1 |
| G. Khôi phục checkpoint | `Runner.cs:287-319` | Có pha |
| H. Dựng lệnh, sinh config | `Runner.cs:365-391` | Không |
| I. `train.py`: dựng model, nạp trọng số, augment, train, val mỗi epoch, lưu best/last | mã repo gốc | Một pha `running`, không % (mục 0.3) |
| J. Checkpoint bền | `Shim/Recovery.cs:80-106` | Sự kiện |
| K. Đóng gói thư mục model + lock, upload | `Runner.cs:103-114,394-449` | `uploading`, `finishing` |
| L. Đăng ký model → replica → đánh giá D-39 | `Api/EvaluationRunner.cs:17-60` | Ngoài run |

| Node đề xuất | Thực tế |
|---|---|
| Dữ liệu | D + E. Có thật |
| Chia tập | **Không là bước riêng**: ẩn trong E, cố định 15 %, không chỉnh được |
| Tăng cường | **Không tồn tại ở nền tảng**: nằm trong config repo gốc, chạy bên trong I |
| Model | F + một phần I. Chỉ hai kiểu chạy được (pretrained, "từ đầu" giả) |
| Huấn luyện | I |
| Đánh giá | Val mỗi epoch nằm trong I; đánh giá độc lập là L, cần model version (`db/registry/202611191600_evaluations.sql:6`) |
| Xuất model | K là **đóng gói**, không phải export; export ONNX chưa ai gọi |
| *(không có node)* | B, C (10–30 phút), G, H, J, K, đăng ký model |

## 3. Khả thi và cái giá

### a. Fork + phiên bản
- **Hiện có [R]**: `catalog.recipes` một dòng mỗi tên; đồng bộ ghi đè, không lịch sử (`App/Catalog/CatalogHandlers.cs:32-50`); DB chỉ giữ manifest (`db/catalog/202611170040_catalog.sql:13-36`), còn Dockerfile, handler, script nằm trên đĩa control plane (`Api/HostContainers.cs:99-107`). Không có lệnh ghi recipe của workspace (`App/Catalog/CatalogSql.cs` chỉ có `UpsertBuiltinRecipe`), không có bảng build dù M17 đã tả (`cookbook/40-module/M17-model-catalog.md:34`).
- **Tag image** đã là hash nội dung đầu vào (`HostContainers.cs:112-115`) nên fork tự có tag riêng. Hai lỗi: (1) hash gồm **toàn bộ `_shared`** (`:102`) — sửa handler DETR làm đổi tag cả 15 recipe; (2) tag không phải digest: pip không ghim nên cùng tag, khác máy, khác môi trường.
- **Cache build**: theo từng máy. Ở D-FINE lớp nặng (`Dockerfile:5-18`) đứng trước `COPY _shared` (`:20`) nên sửa script chỉ build lại từ dòng 20 cộng lớp nền tảng (cài lại grpcio, cần mạng — `HostContainers.cs:66`): cỡ 1–2 phút **[S]**. Sửa Dockerfile hay commit: 10–30 phút **trên mỗi máy**, trong lúc GPU đã bị giữ.
- **Đĩa**: không có mã dọn image (grep `prune|rmi` trong `src/` không ra). Fork theo từng lần train sẽ tích tag vô hạn.
- **Tái lập**: `recipe.lock.json` thiếu `imageDigest` và `datasetVersion` mà spec yêu cầu (`Runner.cs:433-441` so với `cookbook/20-kien-truc/08-recipe-va-runtime.md:164`). Retry dùng manifest đã chụp trong job nhưng image `:current` được tính lại lúc launch (`Scheduler.cs:195-197`): recipe đổi giữa hai attempt thì manifest cũ chạy trên image mới.
- **Cần thêm**: bảng revision + cây file theo sha; `runs.recipe_revision_id`, `runs.image_digest`; lock ghi digest thật và `pip freeze`; `RecipeImage` đọc từ revision thay cho thư mục và thay mẫu `recipes/<tên>:current` (`HostContainers.cs:96`).

### b. Sửa ở ba lớp
| Thứ được sửa | Build lại? | Cơ chế đề nghị |
|---|---|---|
| Tham số, biến thể, dataset, trọng số | Không | Spec của job |
| Manifest: lệnh, map metric, artifact, resume, progress | Không — manifest đi trong spec (`TrackHandlers.cs:147`) | Revision mới, cùng image |
| Config của repo (augment, optimizer) | Không | File phủ trong `{output}`; `make_config.py:101` đã `__include__` config gốc, `--extra-json` đã có (`:94,127-128`) |
| Script keo (`make_config.py`, `handler.py`, `run_rfdetr.py`, `to_coco.py`), node code | Không, nếu shim tải cây file của revision vào thư mục phủ đặt trước `PYTHONPATH` (`HostContainers.cs:72`) | Phải làm cho **cả `mlp-serve`**, nếu không model train bằng handler A sẽ được serve bằng handler B |
| Vài file trong `/opt/recipe/src` | Lớp mỏng `FROM <tag nền>` + `COPY` | 1–2 phút |
| Dockerfile, gói pip, commit | Toàn bộ | 10–30 phút mỗi máy; phải thành job riêng không giữ GPU |

`_shared/detr_family` được COPY trong ba Dockerfile (`d-fine:20`, `deimv2:15`, `rt-detrv4:19`); `_shared/converters` trong hai Dockerfile VibeVoice (`:15`) **và** trong lớp nền tảng của mọi image train (`HostContainers.cs:83`) — `to_coco.py` thuộc nền tảng, không thuộc recipe.

### c. Chạy thử
- **Cờ giới hạn**: không recipe nào khai tham số giới hạn mẫu. **[N]** ultralytics có `fraction`; script HF có `--max_steps`, `--max_train_samples`, `--max_eval_samples` (Whisper đã khai `max_steps`, `recipes/whisper/mlp-recipe.yaml:47`); họ DETR và `rfdetr` không có cờ số bước → chỉ còn `epochs=1` + tập con.
- **Bẫy riêng của họ DETR [R]**: `scale()` làm tròn `max(1, round(v·tỉ lệ))` (`make_config.py:75-80`). Với `epochs=1`, mọi khoá lịch thành 1 → chạy khói không đi qua giai đoạn augment và giai đoạn 2 như chạy thật; artifact `best_stg2`/`best_stg1` cũng khác. Chạy khói cần lịch nén riêng, không phải "chạy thật với epochs nhỏ".
- **Tập con**: cắt danh sách file ở control plane (`TrackHandlers.cs:121-123`) không đúng với COCO (một file json cho cả split). Phải thêm `--limit` vào bộ chuyển.
- **Shim cần chế độ mới**: một attempt = một entrypoint (`JobSpec.cs:13,62`). Chạy khói cần chuỗi train rút gọn → đóng gói → nạp handler dự đoán trong một container, không đăng ký model.
- **Dữ liệu bị tải lại**: cache nằm trên volume của từng run (`Runner.cs:167`), xoá khi job xong → chạy thử rồi chạy thật tải dataset hai lần. Cần cache theo dataset version trên máy.
- **"2–3 phút" chỉ đúng khi image đã có**; lần đầu vẫn 10–30 phút build.
- **Độ tin cậy ước tính [N]**: tốc độ iteration sau khởi động tin được trong khoảng ±20 % nếu cùng batch, kích thước ảnh, máy; thời gian tổng còn gồm val mỗi epoch và các giai đoạn augment khác nhau. VRAM đỉnh của DETR phụ thuộc số vật thể mỗi ảnh và multi-scale — vài chục ảnh không đại diện. Mẫu GPU của agent 15 giây một lần (`cookbook/20-kien-truc/10-quan-sat-va-khoi-phuc.md` §2.5) quá thưa cho 2 phút; phải đọc đỉnh trong tiến trình. Hiển thị **khoảng**, không hiển thị một con số.
- Chạy khói bắt chắc được: thiếu gói (ca `pycocotools`), lỗi template, thiếu split, lệch lớp, glob artifact, handler không nạp được.

### d. Thực thi theo node
- Config → model → augment → train → val là **một tiến trình của repo gốc**. Tách thành job riêng nghĩa là viết lại repo. Giữ một container cho train.
- Ranh giới job có thật: build image · chuẩn bị dữ liệu · train · đóng gói/đăng ký · đánh giá qua replica · export. Spec đã tả job `convert` có cache theo `(dataset_version, converter)` (`08-recipe-va-runtime.md:86`) nhưng mã đang chuyển lại trong mỗi attempt (`Runner.cs:261-281`).
- Chạy lại từ node: chỉ ở các ranh giới trên; bên trong train chỉ có resume từ checkpoint.
- Log/metric theo node: thứ duy nhất có là `phase` của shim. Cần gắn `phase` vào dòng log và sự kiện; trong pha train, manifest khai `stages: [{name, startPattern}]` để shim suy pha con từ log. Metric đã có tiền tố `train/`, `val/`, `lr/`, `test/` — đủ để chia theo node.

### e. Đóng băng / LR theo khối
| Recipe | Bằng cấu hình sẵn có? |
|---|---|
| D-FINE, DEIMv2, RT-DETRv4 | **[N]** config có `freeze_at`/`freeze_norm` của backbone và `optimizer.params` nhóm theo regex với LR riêng → làm được bằng override. `--extra-json` đã có nhưng chỉ `rt-detrv4` dùng với JSON tĩnh (`recipes/rt-detrv4/mlp-recipe.yaml:56,68`). Vướng: validator từ chối tham số không phải giá trị đơn (`TrackHandlers.cs:425`) |
| RF-DETR | `lr_encoder` đã khai (`recipes/rf-detr/mlp-recipe.yaml:48`); đóng băng encoder **[N]** |
| YOLO | **[N]** `freeze=N` lớp đầu; không có LR theo khối; chưa khai |
| Whisper / HF text | Whisper có `freeze_feature_encoder` (`:54`); còn lại phải sửa script |
| LLM SFT | Mặc định đã đóng băng (LoRA); sơ đồ khối không hợp, cần núm rank/target |
| GBDT | Không áp dụng |

Manifest cần thêm: danh sách khối theo biến thể (`blocks: [{name, match}]`), cách dịch `freeze`/`lrScale` sang cấu hình của repo, cờ năng lực. Sơ đồ khối và số tham số phải sinh bằng một lệnh `inspect` chạy trong image — không có sẵn tĩnh. "Bảng ánh xạ lớp cũ → mới" không có cơ chế nào ở repo gốc: phải viết bước sửa checkpoint riêng.

### f. D-39 và node "Đánh giá"
Có ba phép đo dễ bị gộp thành một: (i) val mỗi epoch trong train — pipeline của repo, tập val do bộ chuyển cắt; (ii) entrypoint `eval` trên split `test` — chưa ai gọi, và báo `split_missing` khi dataset không có test (`make_config.py:110-111`); (iii) D-39 qua handler — tiền xử lý khác (bảng 1 dòng 13) và ngưỡng `conf` mặc định 0,25 cắt đuôi điểm thấp nên mAP thấp hơn **[N]**. Canvas phải tách "Kiểm định trong lúc train" (thuộc node Huấn luyện) khỏi "Đánh giá độc lập" (sau đóng gói, cần replica — tức thêm một lượt GPU). Trong chạy khói: gọi thẳng handler trong container, không bật replica.

## 4. Chỗ "canvas chính là recipe" sai
1. Recipe là hợp đồng bảy entrypoint; canvas chỉ vẽ `train`. Sửa handler trong fork làm đổi cách serve và pre-label của **model cũ**, vì deployment lấy recipe theo tên và image `:current` (`App/Serving/ServingHandlers.cs:32-38`).
2. `_shared/detr_family` chung ba recipe; `to_coco.py` chung mọi recipe đọc COCO. "Sửa node của recipe này" thực ra sửa của recipe khác, trừ khi fork chép riêng.
3. `tasks.yaml` là toàn cục. Fork đổi tên metric chuẩn thì bảng so sánh run gãy.
4. `from: dataset.classes.count` do ngữ cảnh điền (`TrackHandlers.cs:139-140`): một cạnh ngầm Dữ liệu → Model, không phải núm vặn.
5. Ngoài manifest: Dockerfile, config repo, backbone nướng sẵn, file zoo, bước build, máy.
6. Bảy node cố định chỉ khớp họ detect. Whisper/HF không có "Tăng cường", GBDT không có "khối model", LLM SFT không chia tập kiểu ảnh; YOLO một recipe năm task. Danh sách node phải do recipe khai.
7. "Node code chèn giữa luồng" chỉ có ba điểm chèn thật: sau chuẩn bị dữ liệu, sau train (trên thư mục model), và trong vòng train (không có cơ chế chung giữa các repo).

## 5. Mô hình dữ liệu tối thiểu và thứ tự xây

- `catalog.recipes` + `project_id`, `forked_from_revision_id` (fork theo project).
- `catalog.recipe_revisions(id, recipe_id, parent_id, number, manifest, env_hash, overlay_hash, state draft|frozen, note, created_by)` — bất biến khi `frozen`.
- `catalog.revision_files(revision_id, path, sha256, layer env|overlay)` + kho blob theo sha.
- `catalog.recipe_images(env_hash, host_id, tag, digest, pip_freeze_uri, size_bytes, built_at, last_used_at)` — build theo máy, có cái để dọn.
- `track.runs` + `recipe_revision_id`, `image_digest`, `start_kind`, `start_ref`, `plan` (lệnh và config đã dựng), `trial_id`.
- `track.trials(id, project_id, recipe_revision_id, dataset_version_id, variant, params_hash, host_id, gpu_model, kind static|smoke|overfit, state, stages jsonb, it_per_s, peak_vram_bytes, est_low_s, est_high_s)`; nút "Train thật" mở khi có trial `smoke` đạt với cùng revision, dataset version, biến thể, digest và loại GPU.
- `registry.versions` + `recipe_revision_id` để serve đúng handler.

Thứ tự ít rủi ro nhất:
1. **Nói thật trước, chưa cần fork**: khai `progress` cho từng recipe; thêm pha build image vào thanh bước và đưa log build ra UI; lock đủ digest + dataset version; lưu và hiển thị "kế hoạch thực thi" chỉ đọc (lệnh đã dựng, config đã sinh, split, artifact).
2. Tách chuẩn bị dữ liệu thành bước có cache theo máy; chia tập có tham số riêng, hạt giống riêng, báo số ảnh từng split và số nhãn bị loại.
3. Chạy thử: chế độ `smoke` của shim + bảng `trials` + cổng mở nút.
4. Revision + lớp phủ file lúc chạy (sửa manifest, config, script mà không build).
5. Đóng băng/LR theo khối cho họ DETR và YOLO.
6. Sửa môi trường bằng job build không giữ GPU; dọn image.
7. Nối sang Pipelines (M14) — loại bước hiện có `dataset, train, job, gate, register, promote` (`App/Pipelines/Pipelines.cs:75`).

## 10 điều quan trọng nhất

1. Runtime chưa làm hết hợp đồng recipe; theo mã đọc được (chưa chạy để kiểm), chỉ D-FINE chắc chắn đủ đường chạy. Vẽ canvas theo manifest là vẽ điều máy không làm.
2. "Chia tập" và "Tăng cường" không phải bước của nền tảng: một cái ẩn trong `to_coco.py` (15 %, dùng chung seed với train), một cái nằm trong config repo gốc. Phải lộ ra và cho chỉnh trước khi gọi là node.
3. Build image chạy sau khi đã giữ GPU, 10–30 phút, không có pha trên thanh tiến độ; không recipe nào khai `progress`. Sửa hai điều này giảm "mù" nhiều nhất với chi phí thấp nhất.
4. Chạy khói họ DETR bằng `epochs=1` đi sai nhánh lịch (làm tròn về 1) — cần lịch nén riêng và giới hạn mẫu ở bộ chuyển.
5. Tag image là hash đầu vào, không phải digest; pip không ghim; lock thiếu digest và dataset version. Chưa tái lập được một run, kể cả khi chưa có fork.
6. Sửa handler/script trong fork phải áp cho cả serve; nếu không, model cũ đổi hành vi hoặc model mới được serve bằng handler khác lúc train.
7. Phần lớn chỉnh sửa (manifest, config, script keo) không cần build lại nếu có lớp phủ file lúc chạy; chỉ Dockerfile/commit mới cần, và phải là job riêng.
8. Ba phép "đánh giá" (val trong train, `eval` của recipe, D-39 qua replica) cho ba con số khác nhau; canvas phải tách và ghi rõ nguồn.
9. Đóng băng/LR theo khối làm được bằng override config ở họ DETR (cơ chế `--extra-json` đã có), nhưng validator đang chặn tham số dạng object và manifest chưa khai khối; "ánh xạ lớp cũ → mới" là việc phải tự viết.
10. Dataset bị tải lại mỗi run và URL ký hết hạn sau 24 giờ: chạy thử tốn gấp đôi băng thông, và resume sang máy khác ở ngày thứ hai sẽ hỏng.
