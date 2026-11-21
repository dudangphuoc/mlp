# 09 — Phản biện spec đợt 1 từ mã thật (2026-11-20)

Đối tượng: `50-lo-trinh/03-dot-1-canvas-cong-thuc.md`. Chỉ đọc mã, không build, không chạy. **[MÃ]** = thấy trực tiếp trong mã; **[SUY]** = suy ra từ mã, chưa chạy. `dfine/` = repo D-FINE ở commit đã ghim. Đường dẫn rút gọn: `App/` = `src/ControlPlane/MLPlatform.ControlPlane.Application/`, `Api/` = `src/Hosts/MLPlatform.Api/`, `Shim/` = `src/Hosts/MLPlatform.Shim/`.

Kết luận: 9 mục chặn, 12 mục lớn, 11 mục nhỏ. Hai kết luận của tài liệu 08 sai (khoá backbone, precision/recall theo lớp) và spec đã thừa kế cả hai.

## Chặn — làm đúng như viết sẽ hỏng hoặc không làm được

**1. Ba công tắc boolean không xuống tới config** (§7.2, §7.3). [MÃ] `Template.Truthy` coi `false` và số `0` là giả (`src/Framework/MLPlatform.Recipes/Template.cs:130-139`). Mẫu `{params.multiscale?--multiscale {params.multiscale}}` với `false` render ra rỗng, `make_config.py` nhận mặc định `true`. Tắt `multiscale`, `aug_strong`, `aug_flip` không có tác dụng; `backbone_lr_scale = 0` cũng bị nuốt. Không nghiệm thu nào thử tắt nên không ai thấy.
*Sửa:* "Cờ boolean và số truyền dạng trần `--multiscale {params.multiscale}` (Template in `true`/`false`). Dạng `{x?…}` chỉ dùng cho tham số `default: null`. `backbone_lr_scale` khai `exclusiveMinimum: 0`."

**2. `freeze = backbone` chỉ khoá stem** (§7.2, nghiệm thu 7). [MÃ] `HGNetv2` mặc định `freeze_stem_only=True`; các stage chỉ bị khoá khi `not freeze_stem_only` (`dfine/src/nn/backbone/hgnetv2.py:442, 494-498`). Mọi config có `freeze_at ≥ 0` đều kèm `freeze_stem_only: True` (`dfine/configs/dfine/dfine_hgnetv2_l_coco.yml:15-17`). Tài liệu 08 mục A bỏ sót điều kiện này.
*Sửa:* "`backbone` → `HGNetv2.freeze_at: 3`, `freeze_stem_only: false`, `freeze_norm: true`; `none` → `freeze_at: -1`, `freeze_norm: false`." Nghiệm thu 7 viết lại: "số tham số được học của khối backbone bằng 0" (`freeze_norm` đổi BatchNorm thành buffer nên tổng tham số backbone cũng đổi).

**3. Diễn tập resume của chạy khói hỏng theo cách §7.5 mô tả** (R18). [MÃ] D-FINE ghi `last.pth` *trước* kiểm định và `best_stg1.pth` *sau* kiểm định (`dfine/src/solver/det_solver.py:99-105`, `151-162`). Ngắt "khi `last.pth` xuất hiện lần đầu" thì lượt resume có `last_epoch = 0`, vào epoch 1 = `stop_epoch` và nạp `best_stg1.pth` chưa tồn tại (`:70-71`). Nếu dựa vào watcher (file ổn định ≥ 10 s, vòng 5 s — `Shim/Recovery.cs:129-147`) thì với 10 bước/epoch tiến trình có thể đã xong epoch 1 [SUY]. Thêm: shim chỉ gửi SIGTERM cho `/bin/sh` (`Shim/Runner.cs:42-48, 61-62`), lệnh là `make_config … && torchrun …` (`recipes/d-fine/mlp-recipe.yaml:50-56`) nên torchrun có thể sống tiếp, giữ GPU khi lệnh resume khởi động [SUY].
*Sửa:* "Chặng `resume` không dùng tín hiệu. Lượt 1 chạy với `epochs = 1, stop_epoch = 1` tới khi thoát mã 0 (đã có `last.pth` và `best_stg1.pth`). Lượt 2 chạy lệnh resume với `epochs = 2`: vào thẳng nhánh `epoch == stop_epoch`." Thứ tự trong R18 sửa thành train → `last.pth` → kiểm định → `best_stg1.pth`.

**4. Bảng precision/recall theo lớp không có dữ liệu** (§7.2, §8.2, §8.5, lát 4). [MÃ] `compute_metrics(extended=False)` xoá `extended_metrics` — nơi duy nhất chứa `precision_<lớp>`, `recall_<lớp>` — trước khi in (`dfine/src/solver/validator.py:34-39, 73-82`; `det_engine.py:233-234`). Dòng in chỉ có `f1, precision, recall, iou, TPs, FPs, FNs`, là `repr` của dict Python (nháy đơn), ngưỡng tin cậy cố định 0,5. Tài liệu 08 mục C sai.
*Sửa:* "Đợt 1 không có bảng theo lớp. Ánh xạ `val/precision`, `val/recall`, `val/f1` toàn cục từ dòng `Metrics:`."

**5. R28 thiếu migration; UNIQUE hiện tại chặn nhiều bản `last.pth`** (§3, R28). [MÃ] `sched.checkpoints` có `UNIQUE (job_id, name, location)` và lệnh ghi là upsert (`db/ops/202611181800_ops.sql:38`, `App/Sql/Ops/InsertCheckpoint.sql`). §3 không nêu migration nào ở `db/ops`. Sắp thuần theo `seq` còn làm hỏng recipe có số trong tên: watcher trả file theo thứ tự chuỗi (`Shim/Runner.cs:470`), `ckpt-10` đứng trước `ckpt-2`.
*Sửa:* "Migration `db/ops/…_checkpoint_seq.sql`: thêm `seq bigint`, UNIQUE mới `(job_id, name, location, seq)`. `step` = số trong tên file nếu có, không thì số epoch đã xong. Chọn bản khôi phục và dọn theo `(step, seq)`."

**6. Cổng "cùng máy" không có trong dữ liệu** (§3, R16, R21, R23). [MÃ] `drafts` không có `host_id`; `trials` không có `env_tag`, `gpus`; `StartTrial`/`StartTraining` không nhận máy. Scheduler lấy máy đầu tiên vừa (`Api/Infrastructure/Scheduler.cs:50-61`) trừ khi `spec.Hosts` được ghim (`App/Scheduling/JobSpecDto.cs:14`). Kiểm nhanh không biết "máy đó" là máy nào; lần train có thể rơi vào máy chưa thử.
*Sửa:* "`drafts`, `trials`, `runs` thêm `host_id`, `gpus`; `trials` thêm `env_tag`. Job `trial` và job train của canvas đặt `Hosts = [host_id]`."

**7. `runs.kind` đã tồn tại** (§3). [MÃ] Cột `kind text NOT NULL`, mọi run ghi `'train'` (`db/track/202611180010_track.sql:20`, `App/Sql/Track/InsertRun.sql:4`); MLPMIG001 cấm DROP. `backbone_only` còn trùng nghĩa với `start_kind = backbone` và cột `from_scratch` đang có.
*Sửa:* "Thêm `train_kind` (`fine_tune`·`retrain`). Run cũ: `start_kind` suy từ `from_scratch`. Bỏ `backbone_only`."

**8. Job không thuộc một run không có đường báo kết quả; §6 bỏ sót `runtime.proto`** (R13, R18, R19, §7.5). [MÃ] Shim chỉ gửi metric khi có `MLP_RUN_ID` (`Shim/Tracking.cs:30-35`); volume `/mnt/output` chỉ gắn khi job có run (`Scheduler.cs:228-238`); `CreateArtifactUploads` đòi `run_id` (`proto/mlplatform/v1/runtime.proto`). `stages`, `loss`, ảnh mẫu của trial, `config.yaml`/`blocks.json` của inspect không có RPC chở về. Kết thúc job được báo cho run/deployment/notebook bằng lời gọi viết tay (`Scheduler.cs:316, 364-371`).
*Sửa:* thêm vào §6 mục `runtime.proto`: "`ReportTrialStage`, `ReportMetricInvalid`, `CreateResultUploads(kind)`, `CompleteInspection`; `ProgressReport` thêm `epoch, epochs, step, steps, sub_phase`. `OnExitAsync`/`FailAsync` đồng bộ thêm trial, environment, inspection."

**9. Bỏ nhánh cắt ngẫu nhiên và cấm launcher dựng image làm gãy đường cũ** (R10, R12, §6, §7.6). [MÃ] `to_coco.py` dùng chung cho `d-fine`, `deimv2`, `rt-detrv4` (`data.input: coco`; `Api/Infrastructure/HostContainers.cs:83`). Pipeline và HPO gọi thẳng `StartTrainingCommand` (`App/Pipelines/Pipelines.cs:424`, `App/Tuning/Tuning.cs:244`): với phiên bản chưa chia sẽ chết ở `split_missing` (`recipes/_shared/detr_family/make_config.py:110-111`); với máy chưa có image sẽ hỏng lúc launch. Deployment cũng dựng image qua đúng đường đó (`Scheduler.cs:195-199, 288-293`).
*Sửa:* "Không có `mlp-split.json` và không có thư mục val: `to_coco.py` chia bằng hàm băm của R8 (seed 0, 85/15) và in cảnh báo; canvas vẫn đòi phiên bản đã chia. Thiếu môi trường lúc launch: job của canvas hỏng với `CATALOG_ENV_MISSING`; job đường cũ và deployment giữ cách dựng hiện tại."

## Lớn

**10. Lần train NaN bị chạy lại ba lần** (R26, nghiệm thu 10). [MÃ] D-FINE `sys.exit(1)` (`det_engine.py:125-128`); shim trả nguyên mã (`Runner.cs:120`); scheduler coi `failed` là thử lại được (`App/Scheduling/SchedHandlers.cs:195-203`). Dòng in cũng có thể là `Loss is inf`. *Sửa:* "Manifest khai `failures: [{pattern, reason, retry: false}]`; shim gặp mẫu thì thoát mã 2 kèm thông điệp. D-FINE: `^Loss is (nan|inf)`."

**11. Nghiệm thu 9 không đo được điều nó nói.** [MÃ] Giết container cho mã 137 → `oom`, không thử lại (`SchedHandlers.cs:180, 195`; `Scheduler.cs:354`). Cùng máy thì bản cục bộ luôn thắng (`App/Ops/Ops.cs:255-258`), bản bền không được dùng. *Sửa:* "Dừng agent quá hạn chờ (`node_lost`), xoá volume `mlp-run-*` rồi để attempt mới tải bản bền; kiểm sha trong log."

**12. `best_stg1.pth` không bền; R29 lệch một epoch** (R28, R29). [MÃ] Chỉ `last.pth` được tải lên (`mlp-recipe.yaml:58`). Sau resume ở máy khác, `best_stg1.pth` chỉ được ghi lại khi có epoch vượt AP lúc resume (`det_solver.py:43-58, 135-162`); không có thì tới `stop_epoch` lỗi thiếu file [SUY]. `last.pth` cuối mang `last_epoch = stop_epoch − 1` nên resume bắt đầu ở `stop_epoch`. *Sửa:* "`checkpoints: [last.pth, best_stg1.pth]`. R29: 'chạy lại từ đầu giai đoạn 2 (epoch <stop_epoch>)'."

**13. Đổi `lr` làm lệch tỉ lệ backbone** (§7.2, H2). [MÃ] Biến thể S: `optimizer.lr: 0.0002`, hai nhóm backbone `lr: 0.0001` tuyệt đối (`dfine_hgnetv2_s_coco.yml`). Đổi `lr` còn `backbone_lr_scale` để trống thì backbone giữ 0.0001. `Lr/pg_0` là nhóm backbone (`det_engine.py:135-136`) nên "LR hiện tại" trên canvas là LR backbone. *Sửa:* "Có `--lr` mà không có `--backbone-lr-scale`: `make_config.py` giữ tỉ lệ của biến thể. Ánh xạ thêm nhóm cuối thành `lr/main`."

**14. Số đo của chạy khói lệch có hệ thống** (R19, R20). [MÃ] Multi-scale chọn ngẫu nhiên từng batch (`dfine/src/data/dataloader.py:110-115`); S có 30 cỡ, cỡ 800 chiếm 1/30 → 10 bước giai đoạn 1 gặp cỡ lớn nhất khoảng 29 %. `max mem` là `max_memory_allocated` (`dfine/src/misc/logger.py:233`), thấp hơn bộ nhớ GPU thực chiếm. "Trung vị nửa sau" rơi vào giai đoạn 2 (không tăng cường mạnh, không multi-scale). Val batch 64 nên 32 ảnh là một batch: `val_seconds × ảnh val / 32` nhân chi phí cố định lên. `drop_last: True` (`dfine/configs/dataset/coco_detection.yml:22`): `batch` lớn hơn tập con cho 0 bước. *Sửa:* "Tốc độ lấy từ giai đoạn 1, bỏ 3 bước đầu. VRAM chưa gặp cỡ lớn nhất thì nhân (cỡ lớn nhất / cỡ đã gặp)² và ghi 'ước lượng'. Tập con train = max(160, 10 × batch), val ≥ 2 batch. Ước tính dùng ⌊ảnh / batch⌋."

**15. Manifest gốc và nháp** (R6, §3). [MÃ] `UpsertBuiltinRecipe` ghi đè manifest (`App/Sql/Catalog/UpsertBuiltinRecipe.sql`): trước bản chốt đầu không còn bản cũ để diff. `drafts` không ghi nháp dùng manifest nào; "tạo nháp mới" mâu thuẫn UNIQUE một nháp. Bản chốt chụp manifest nhưng image luôn dựng từ thư mục recipe hiện hành (`HostContainers.cs:99-111`): manifest cũ chạy với script mới. *Sửa:* "`project_recipes` thêm `origin_manifest jsonb`; `drafts` thêm `manifest_sha256`. 'Lấy theo gốc' thay nháp hiện có và cập nhật `origin_*`. Sửa `_shared/` phải tương thích ngược với manifest đã chốt."

**16. `env_tag` đổi theo mỗi bản phát hành** (R21, §12). [MÃ] Tag băm mọi file của recipe, toàn bộ `_shared/`, lớp serve và sha của shim (`HostContainers.cs:102-114`). Mỗi lần đổi shim: mọi môi trường thành `missing`, mọi trial và bản soi hết hiệu lực. Cả bốn lát đầu đều đụng shim hoặc `_shared`, không riêng lát 3. *Sửa:* "Sau bản phát hành đổi `env_tag`, nút Train ghi 'môi trường đổi do bản nền tảng mới — dựng lại và chạy thử'. Mỗi lát gom thay đổi shim/`_shared` vào một lần dựng."

**17. Run không có bản chốt** (R4, R5, §8.1). Run cũ và run từ pipeline/HPO có `revision_id NULL`; Nấu lại và Train lại chưa định nghĩa cho chúng. *Sửa:* "Nấu lại từ run không có bản chốt: dựng nháp từ `recipe, variant, params, dataset_version_id, from_scratch` trên manifest hiện hành. Train lại bị khoá, ghi lý do."

**18. `runs.plan` không có nguồn** (R23, §8.5). [MÃ] Lệnh thật do shim dựng trong container (`Runner.cs:30-35`), config do `make_config.py` sinh lúc chạy. `RenderTemplate` hiện có chỉ dùng mặc định của manifest hiện hành và in `<params.x>` cho `default: null` (`App/Catalog/CatalogHandlers.cs:106-118`). *Sửa:* "Shim tải `.mlp/command.txt` và `mlp_train.yml` ngay sau khi sinh; `plan` điền lúc đó. Kiểm nhanh dùng renderer nhận (manifest của bản chốt, biến thể, tham số)."

**19. Báo cáo phiên bản dùng bộ đọc khác bộ chuyển** (R11, nghiệm thu 4). [MÃ] Bộ đọc D-39 bỏ dòng hỏng im lặng, không trả lý do (`App/Registry/EvalMath.cs:74-100`); luật của nó khác `to_coco.py` (`recipes/_shared/converters/to_coco.py:171-183`). Nhãn lệch id: `to_coco.py` bỏ id ngoài khoảng rồi chạy tiếp (`:176-178`) nên trial không `failed`. Thiết kế (§7.1) đòi bộ chuyển trả báo cáo và 16 ảnh sau bộ chuyển. *Sửa:* "`to_coco.py --report` ghi JSON (đếm theo lớp và tập, dòng bị loại kèm lý do); báo cáo phiên bản chạy chính nó trong job 0 GPU. Chặng `data` hỏng khi hơn 5 % dòng bị loại."

**20. Lát phụ thuộc lát sau** (§12). [MÃ] Lát 1 gỡ `TrainSheet` trong khi "Train again" dùng nó (`web/src/pages/Projects.tsx:185-190`, `Run.tsx:187`), còn Nấu lại ở lát 5. Lát 3 cần bộ đọc stdout (`progress.pattern`, `max mem`) xếp ở lát 4. Lát 1 vẽ trạng thái môi trường, bảng ở lát 2. Lát 2 cấm launcher dựng image, kiểm nhanh báo thiếu ở lát 3. *Sửa:* "Lát 1 gồm `ResetDraft(run)`. Bộ đọc stdout sang lát 3. Launcher đổi hành vi ở lát 3."

**21. §8 hiển thị thứ không có dữ liệu.** [MÃ] (a) VRAM theo biến thể: manifest chỉ có `minVramGb` cấp recipe (`mlp-recipe.yaml:25, 31-39`). (b) Tab "Đang dùng ở": không có RPC. (c) Tô màu dòng nền tảng/người dùng: bản soi chỉ có config đã gộp. (d) Dockerfile, `mlp-recipe.yaml`, `to_coco.py` không nằm trong `/opt/recipe` (`recipes/d-fine/Dockerfile:20`, `HostContainers.cs:83`). (e) License: không có bảng xác nhận. *Sửa:* "Thêm `ListRecipeUsage(recipe_name)`; bản soi lưu cả `overlay_yaml` (file `make_config.py` ghi) lẫn config đã gộp; file của recipe đọc từ `Catalog:RecipesDir`, inspect chỉ gói `/opt/recipe/src` trừ `.git`; bỏ cột VRAM theo biến thể; bỏ kiểm license ở R16 cho tới khi có bảng."

## Nhỏ

22. `mlp_serve.selftest` phải thêm vào danh sách file nhúng (`Api/MLPlatform.Api.csproj:11-13`, `HostContainers.cs:108`), nếu không image không có nó.
23. Tập con trên dataset dạng COCO: `from_coco` link mọi ảnh trong json, không kiểm tồn tại (`to_coco.py:198-199`). R8 phải ghi "chỉ băm file ảnh; nhãn theo ảnh".
24. Chặng `weights`: dòng có `, {…}` chỉ in khi dùng `-t` (`dfine/src/solver/_solver.py:273-274`); resume in `Load model.state_dict` không có phần sau (`:226`). `start_kind = backbone` bỏ chặng. Nghiệm thu 3: log không nhắc `best_stg1.pth`; dùng dòng `Refresh EMA at epoch 1` (`det_solver.py:74`).
25. `print_freq` mặc định 100 (`dfine/configs/runtime.yml:1`): train thật đặt 10. Mẫu tiến độ phải nhận khoảng trắng đệm `[ 5/107]` và thêm nhóm `loss` (TensorBoard chỉ ghi mỗi 10 bước, `det_engine.py:133`).
26. R25: `train_loss` và `Loss/total` cùng ánh xạ `train/loss` (`mlp-recipe.yaml:94, 98`), shim khử trùng theo (khoá, bước) (`Shim/Metrics.cs:80-83`). Đổi thành `train/loss_epoch`. Run cũ giữ trục epoch: ghi `metric_axis` trên run để So sánh không chồng hai trục.
27. `Run.phase` trùng tên `progress.phase` (`proto/mlplatform/v1/scheduler.proto:55`): đổi `train_phase`. §6 trả `Inspection.schedule` trong khi §7.4 nói inspect không tính lịch.
28. `default: null`: validator từ chối giá trị null (`App/Tracking/TrackHandlers.cs:425`); nháp phải bỏ khoá, chuẩn hoá coi thiếu ≡ null.
29. Bảng §7.2 chưa gán `affectsInspect`; `epochs` phải `true`. Khoá đệm bản soi phải gồm số lớp.
30. Nghiệm thu 2: `val_sha256` là cột của phiên bản nên luôn bằng nhau; so danh sách val bộ chuyển in ra. Nghiệm thu 5: `train_samples` lưu sau multi-scale (`det_engine.py:65-66`), cỡ ngẫu nhiên; thử với `multiscale = false`. Tập nhỏ: băm có thể cho val rỗng dù tỉ lệ hợp lệ; lấy ảnh có băm nhỏ nhất thay vì trả `DATA_SPLIT_EMPTY`.
31. Shim không truyền `--val-fraction`, chỉ `--seed` (`Runner.cs:269-270`). Entrypoint `eval` (`--val-split test`) chưa được control plane gọi: chỉ `"train"` được gửi (`TrackHandlers.cs:147`).
32. Thiết kế bản 2 xếp đợt 1 nhưng spec bỏ hoặc làm yếu: Tập test của project (§3 thiết kế); mẫu con có chủ đích (8 mẫu theo dõi, ảnh nhiều vật thể); "số lớp khớp" thành "số lớp > 0"; nhãn nguồn cho hai con số val/test; "điều kéo theo" trên thanh hậu quả. Spec thêm ngoài thiết kế: R30 (thiết kế xếp đợt 2 — nên giữ), precision/recall theo lớp (bỏ, mục 4). Ghi các mục bị bỏ vào §13.

## Các [KIỂM] và câu hỏi đã trả lời được bằng đọc mã

- **§13.5 định dạng `Metrics:`** — `repr` dict Python, bảy khoá toàn cục, không có theo lớp (mục 4).
- **§13.6 log `ImageBuild`** — agent đã nhận từng dòng của `docker build --progress=plain` nhưng chỉ ghi `agent.log` (`src/Hosts/MLPlatform.Agent/CommandHandler.cs:167-177`); cần thêm `attempt_id` vào `ImageBuild` và phát `LogChunk`.
- **§13.3 (một phần)** — 160 ảnh / batch 16 = 10 bước mỗi epoch; khởi động tuyến tính 500 bước (`dfine/src/optim/warmup.py`) nên LR chạy khói không quá 4 % LR đỉnh. Độ ổn định tốc độ: chưa trả lời được.
- **§13.4 (một phần)** — dựng model không cần GPU, nhưng trọng số backbone nằm ở đường dẫn tương đối `weight/hgnetv2/`: inspect phải đặt `workdir: /opt/recipe/src`, nếu không sẽ tải mạng. Thời gian: chưa đo.
- **Template** — số thực in đúng (`Show`, định dạng `R`); `false`, `0`, `null` bị coi là rỗng trong dạng `?` (mục 1).
- **Validator** — nhận `number`, `boolean`, `enum`; từ chối null (mục 28).
- **`--extra-json`** — áp sau bước co mốc (`make_config.py:117-128`), không xung đột; khoá của D-FINE là `epochs`. Lệnh train chưa có chỗ chèn: cần placeholder mới và thêm vào danh sách tên hợp lệ của `RecipeChecker.cs:12`.
- **Bốn chỗ cỡ ảnh, `base_size_repeat: null`, bỏ phép tăng cường mà `policy.ops` còn nhắc tên** — đúng (`dataloader.py:99-101`, `dfine/src/data/transforms/container.py:68-78`); handler serve đọc `eval_spatial_size` (`recipes/_shared/detr_family/handler.py:51`).
- **URL `s3://`** — `ResolveVersionFiles`, Browse/ReadFile, gán nhãn, Hermes đều hỗ trợ (`App/Common/SourceUrls.cs`, `App/Registry/Evaluations.cs:68-75`); `Features.cs:167` và `Monitoring.cs:266` thì không. Xuất nhãn đã tạo phiên bản trộn file tham chiếu và file riêng (`App/Labeling/LabelProgress.cs:117-149`): R9 làm được.
- **R30** — chưa có mã ký lại; chỗ đặt là `WorkloadLauncher.LaunchAsync` trước `ContainerApply` (`Scheduler.cs:219-239`), cạnh `ResolveResume`.
- **Job 0 GPU** — scheduler cho phép (`Scheduler.cs:83-88`), nhưng `env_build` không phải container: launcher cần rẽ nhánh theo loại job.
