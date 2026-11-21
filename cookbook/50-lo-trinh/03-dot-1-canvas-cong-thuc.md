# Đợt 1 — Canvas công thức, chỉnh nông, chạy thử (D-FINE)

> **Trạng thái:** spec bản 2 (2026-11-20), đã sửa theo phản biện từ mã (`10-khao-sat/06-lam-lai-project-training/09-phan-bien-spec-dot-1.md`). Chưa hiện thực.
> **Thay thế:** S-09-4 "Train mới" (form) và bố cục S-09-2 trong M09.
> **Căn cứ:** thiết kế `…/07-thiet-ke-ux-v2.md`, kiểm repo D-FINE `…/08-kiem-dfine.md`, quyết định D-42 … D-49.
> **Module đụng tới:** M09 (project, run, chạy thử), M17 (công thức, môi trường, soi), M04 (chia tập, báo cáo phiên bản), M03 và 20/10 (loại job, điểm lưu), 20/08 (manifest, shim).

Nhãn: **[CÓ]** đã có trong mã · **[XÂY]** phải xây ở đợt này · **[KIỂM]** phải đo hoặc chạy thử khi hiện thực.

## 1. Mục tiêu & phạm vi

Người dùng mở một project và **thấy** những việc máy sẽ làm khi train, **chỉnh** được các núm chính ngay trên hình, **chạy thử vài phút** trước khi giữ máy nhiều giờ, và theo dõi lần train thật trên cùng hình đó.

| Trong đợt 1 | Ngoài đợt 1 |
|---|---|
| Họ phát hiện vật thể, recipe `d-fine` (D-46) | YOLO và các họ khác (đợt 2, 3) |
| Canvas có khung: 5 node + vòng huấn luyện 6 ngăn (D-44) | Node Xuất; node Đánh giá tự chấm ứng viên (đợt 3) |
| Công thức của project, nháp, bản chốt bất biến (D-49) | Sửa file cấu hình (đợt 3); sửa lệnh, script, code (đợt 4) — D-45 |
| Chỉnh nông: 11 tham số khai báo của D-FINE | Chỉnh từng khối, bảng ghép lớp, fine-tune từ model của tôi (đợt 3) |
| Chạy thử (kiểm nhanh + chạy khói) và cổng nút Train (D-43) | Mẫu theo dõi qua từng epoch, tạm dừng, rẽ nhánh, ngân sách giờ (đợt 2) |
| Chia tập cố định ghi vào phiên bản dữ liệu; báo cáo phiên bản | Ảnh gần trùng; Tập test của project dùng để chấm (đợt 3) |
| Dựng môi trường thành job riêng không giữ GPU | Dọn image, image theo digest (đợt 4) |
| Trang Run: % và ETA, hai giai đoạn, lý do loss hỏng, báo cáo epoch đầu, chạy nốt đúng | Email (đợt 2) |

Hai hành trình phải đi trọn khi đợt 1 xong: **H1** lần đầu train trên dữ liệu vừa gán nhãn; **H2** sửa tham số rồi train lại và so với lần trước (mục 5).

**Đường cũ giữ nguyên.** Pipeline (M14), HPO (M10), SDK gọi `StartTraining` bằng các trường cũ vẫn chạy như hôm nay: không qua cổng, không cần bản chốt, môi trường tự dựng lúc launch. Mọi thay đổi ở bộ chuyển, shim, launcher phải giữ đường này (R10, R12).

## 2. Khái niệm

| Từ (UI Việt / Anh) | Nghĩa | Lưu ở |
|---|---|---|
| Công thức gốc / Recipe | Recipe trong Catalog | `catalog.recipes` [CÓ] |
| Công thức của project / Project recipe | Bản tách từ một công thức gốc cho một project; giữ bản sao manifest gốc lúc tách | `track.project_recipes` [XÂY] |
| Nháp / Draft | Bản đang sửa của **một người** trên một công thức của project (đợt 1: mỗi người một nháp) | `track.drafts` [XÂY] |
| Bản chốt `r8` / Revision | Ảnh chụp bất biến: manifest + biến thể + giá trị tham số. Tự tạo khi Chạy thử hoặc Train; nội dung trùng thì dùng lại số cũ | `track.revisions` [XÂY] |
| Chạy thử / Trial | Lượt kiểm rẻ của (bản chốt, phiên bản dữ liệu, điểm xuất phát, máy, môi trường) | `track.trials` [XÂY] |
| Lần train `#14` / Run | Bản chốt + phiên bản dữ liệu + điểm xuất phát + máy | `track.runs` [CÓ, thêm cột] |
| Điểm xuất phát / Starting point | `pretrained` (model có sẵn của biến thể) · `backbone` (chỉ backbone học sẵn — cờ `from_scratch` hiện có) | `start_kind` |
| Kiểu train / Train kind | `fine_tune` · `retrain` (làm lại lần train #n trên phiên bản dữ liệu khác, giữ bản chốt và điểm xuất phát) | `train_kind` |

Từ dành riêng trong UI: **lớp** = class, **tầng** = layer, **nhãn** = annotation, **mức chỉnh** = Trực quan / Cấu hình / Code.

## 3. Dữ liệu

Mọi bảng mới có `workspace_id` và được lọc theo nó (MLPSQL010). Không join khác schema (MLPSQL020): handler ghép bằng tra cứu theo lô. Không DROP cột (MLPMIG001).

**Schema `track`** — `db/track/…_recipe_canvas.sql`

| Bảng / cột | Cột chính |
|---|---|
| `project_recipes` | id, workspace_id, project_id, origin_recipe_id, origin_name, origin_manifest jsonb, origin_manifest_sha256, created_by, created_at · UNIQUE (project_id, origin_name) |
| `drafts` | id, workspace_id, project_recipe_id, user_id, base_revision_id NULL, manifest_sha256, variant, params jsonb (chỉ khoá người dùng đã đặt; thiếu khoá ≡ theo mặc định), dataset_version_id NULL, start_kind, train_kind, retrain_of_run_id NULL, host_id NULL, gpus, cpus, run_name, etag, updated_at · UNIQUE (project_recipe_id, user_id) |
| `revisions` | id, workspace_id, project_recipe_id, number, parent_revision_id NULL, manifest jsonb, manifest_sha256, variant, params jsonb, content_sha256, trial_key, created_by, created_at · UNIQUE (project_recipe_id, number), UNIQUE (project_recipe_id, content_sha256) |
| `trials` | id, workspace_id, project_id, revision_id, trial_key, dataset_version_id, start_kind, host_id, gpus, env_tag, job_id NULL, state (`checking`·`queued`·`running`·`passed`·`failed`·`cancelled`), quick jsonb, stages jsonb, steps_per_s, peak_vram_bytes, vram_estimated bool, val_seconds_per_batch, est_low_s, est_high_s, loss jsonb (≤ 200 điểm), error, created_by, created_at, finished_at |
| `runs` (thêm) | revision_id NULL, trial_id NULL, train_kind NULL, start_kind NULL, retrain_of_run_id NULL, host_id NULL, plan jsonb NULL, first_epoch jsonb NULL, metric_axis text NULL (`step`; NULL = run cũ, trục lẫn lộn) |

- Cột `runs.kind` [CÓ] (luôn `'train'`) không dùng lại. Run cũ: `start_kind` suy từ `from_scratch` khi đọc.
- `content_sha256` = băm (manifest_sha256, variant, params đã chuẩn hoá). `trial_key` = như trên nhưng **bỏ** tham số có `affectsTrial: false` (đợt 1: `epochs`, `seed`).

**Schema `catalog`** — `db/catalog/…_environments.sql`

| Bảng | Cột chính |
|---|---|
| `environments` | id, workspace_id, recipe_name, env_tag, host_id, state (`missing`·`building`·`ready`·`failed`), job_id NULL, built_at, last_used_at, error · UNIQUE (workspace_id, env_tag, host_id) |
| `inspections` | id, workspace_id, env_tag, variant, params_sha256, num_classes, state, job_id NULL, overlay_yaml (file `make_config.py` ghi), config_yaml (đã gộp), blocks jsonb, files_key, error, created_at · UNIQUE (workspace_id, env_tag, variant, params_sha256, num_classes) |

`env_tag` là tag image hiện có: băm mọi file của recipe, `_shared/`, lớp serve và shim [CÓ, `HostContainers.cs`]. Hệ quả: mỗi bản phát hành đổi shim hoặc `_shared` làm mọi môi trường thành `missing` và mọi trial, bản soi hết hiệu lực (R21).

**Schema `data`** — `db/data/…_version_report.sql`

| Bảng / cột | Cột chính |
|---|---|
| `dataset_versions` (thêm) | split_state (`none`·`folders`·`file`), split_counts jsonb, val_sha256 (băm danh sách đường dẫn val) |
| `version_reports` | version_id PK, workspace_id, state, job_id NULL, classes jsonb (theo tập), unlabeled bigint, rejected jsonb (≤ 50 dòng kèm lý do), rejected_count, notes jsonb, created_at |

**Schema `sched`** — `db/ops/…_checkpoint_seq.sql`: `checkpoints` thêm `seq bigint`; UNIQUE đổi từ `(job_id, name, location)` sang `(job_id, name, location, seq)`.

## 4. Trạng thái & quy tắc

**Công thức, nháp, bản chốt**
- **R1** Chọn một công thức gốc cho project lần đầu tạo `project_recipes` (kèm bản sao manifest gốc) và một nháp. Đợt 1 chỉ `d-fine` chọn được; thẻ khác hiện mờ kèm lý do "chưa khai `canvas`".
- **R2** Nháp tự lưu sau mỗi thay đổi (debounce 800 ms, kiểm `etag`). Ô đã đổi so với bản chốt gốc của nháp mang ✎ và nút "về giá trị cũ". Tham số `default: null` để trống thì **không lưu khoá** (validator từ chối null).
- **R3** Bấm Chạy thử hoặc Train: server chuẩn hoá nháp → `content_sha256` → dùng lại bản chốt trùng hoặc tạo số mới. Bản chốt không bao giờ bị sửa.
- **R4** "Nấu lại từ #n": nạp bản chốt, phiên bản dữ liệu, điểm xuất phát, máy của #n vào nháp của người bấm; nháp đang có thay đổi chưa chốt thì hỏi trong ngăn bên phải. Run không có bản chốt (run cũ, run từ pipeline/HPO): dựng nháp từ `recipe, variant, params, dataset_version_id, from_scratch` trên manifest hiện hành, ghi rõ "lần train này có trước bản chốt".
- **R5** "Train lại" (re-training): chỉ chọn được với lần train đã xong **có bản chốt**; đặt `train_kind = retrain`, khoá bản chốt và điểm xuất phát theo #n, bắt buộc phiên bản dữ liệu khác #n.
- **R6** Manifest gốc trong Catalog khác `origin_manifest_sha256`: thẻ công thức mang huy hiệu "gốc đã đổi", mở diff giữa `origin_manifest` và manifest hiện hành. "Lấy theo gốc" **thay nháp hiện có** bằng manifest mới (giữ giá trị tham số còn hợp lệ) và cập nhật `origin_*`. Không gộp.
- **R6b** Bản chốt chụp manifest, nhưng image luôn dựng từ thư mục recipe hiện hành. Vì vậy trong đợt 1–3 mọi thay đổi ở `_shared/` phải **tương thích ngược** với manifest đã chốt (chỉ thêm cờ, không đổi nghĩa cờ cũ).

**Dữ liệu**
- **R7** Canvas đòi phiên bản **đã chia tập**: có thư mục train + val (`folders`) hoặc file `mlp-split.json` (`file`). Chưa chia: node Dữ liệu đề nghị "Chia và lưu thành phiên bản mới".
- **R8** Chia tập xác định theo đường dẫn, chỉ băm **file ảnh** (file nhãn đi theo ảnh): `bucket = sha256(seed ‖ khoá nhóm) mod 10 000`. Khoá nhóm = đường dẫn ảnh, hoặc thư mục cha khi chọn "chia theo thư mục". Một ảnh không đổi tập khi phiên bản sau thêm ảnh. Mặc định 80/10/10. Tập nhỏ làm val hoặc test rỗng: lấy ảnh có băm nhỏ nhất bù vào thay vì báo lỗi.
- **R9** Phiên bản mới tham chiếu file của phiên bản cũ bằng URL `s3://` (không sao chép; cơ chế đã dùng ở xuất nhãn [CÓ]) và thêm `mlp-split.json` `{ seed, ratios, group_by, files: { "<ảnh>": "train|val|test" } }`.
- **R10** `to_coco.py`: có `mlp-split.json` thì theo file; không thì theo thư mục; **không có cả hai** (đường cũ) thì chia bằng hàm băm của R8 với seed 0, tỉ lệ 85/15, in cảnh báo — thay cho cắt ngẫu nhiên theo seed của lần train. Tập `test` không đưa vào container train.
- **R11** Báo cáo phiên bản là job `data_check`, 0 GPU, image Python gọn, chạy **chính `to_coco.py --report`** trên file nhãn (không tải ảnh): đếm lớp theo tập, ảnh chưa có nhãn, dòng bị loại kèm lý do (id lớp ngoài khoảng, sai số trường, toạ độ ngoài 0–1), cảnh báo tên file giống khung hình liên tiếp. Kết quả đệm theo phiên bản.

**Môi trường và soi**
- **R12** Dựng môi trường là job `env_build`, 0 GPU: launcher rẽ nhánh theo loại job, gửi `ImageBuild` kèm `attempt_id`; agent phát từng dòng build thành `LogChunk` (hiện chỉ ghi `agent.log`) [XÂY ở agent, phát hành qua OTA]. Job của **canvas** (trial, train có bản chốt) thiếu môi trường thì hỏng ngay với `CATALOG_ENV_MISSING`; job đường cũ và deployment vẫn tự dựng lúc launch như hiện tại.
- **R13** Soi công thức là job `inspect`, 0 GPU, chạy `entrypoints.inspect` trong image với `workdir: /opt/recipe/src`. Đệm theo (env_tag, biến thể, băm tham số có `affectsInspect`, số lớp).
- **R14** Ngăn chưa có dữ liệu soi ghi "chưa soi được" kèm lý do; không vẽ hình thay thế.

**Chạy thử và cổng**
- **R15** Chạy thử = **kiểm nhanh** (trong api, vài giây) rồi **chạy khói** (job `trial`, cùng đường mã với train). Kiểm nhanh hỏng thì không gửi job.
- **R16** Kiểm nhanh: lệnh dựng được từ (manifest của bản chốt, biến thể, tham số) [XÂY: renderer nhận tham số; `RenderTemplate` hiện chỉ dùng mặc định]; phiên bản đã chia, đủ `train` và `val`; báo cáo phiên bản không có quá 5 % dòng bị loại; đã chọn máy, máy đủ `minVramGb`; môi trường `ready` trên máy đó; ai đang giữ GPU [CÓ: `getClusterMap`].
- **R17** Tập con do control plane chọn theo thứ tự băm: train = max(160, 10 × batch) ảnh, val = 128 ảnh (ít hơn nếu tập nhỏ; `batch` lớn hơn số ảnh train thì kiểm nhanh từ chối vì `drop_last`). Job `trial` ghim `Hosts = [host_id]`.
- **R18** Chạy khói gồm **hai lượt chạy tự nhiên**, không ngắt bằng tín hiệu:

  | Chặng | Việc | Đạt khi |
  |---|---|---|
  | `data` | tải tập con, chạy bộ chuyển kèm `--report` | ≤ 5 % dòng bị loại |
  | `stage1` | lượt 1: `epochs = 1`, `stop_epoch = 1`, `policy.epoch = 1`, `print_freq = 1`; batch và tham số khác như bản chốt | thoát mã 0; có `last.pth` và `best_stg1.pth`; metric chính hữu hạn |
  | `weights` | (trong lượt 1, chỉ khi `start_kind = pretrained`) đọc dòng `Load model.state_dict, {missed, unmatched}` | đọc được dòng |
  | `resume` | lượt 2: lệnh resume với `epochs = 2` → vào thẳng nhánh `epoch == stop_epoch` | log có `Refresh EMA at epoch 1`; thoát mã 0 |
  | `package` | đóng gói như train thật | có `model.pth` |
  | `reload` | `python -m mlp_serve.selftest` nạp model bằng handler serve, dự đoán 8 ảnh val | trả kết quả hợp lệ (không chấm chất lượng) |

- **R19** Số đo: `steps_per_s` = trung vị của **lượt 1** bỏ 3 bước đầu; `val_seconds_per_batch`; `peak_vram_bytes` từ `max mem:` (là bộ nhớ đã cấp phát, thấp hơn bộ nhớ GPU thực chiếm). Khi `multiscale` bật mà lượt 1 chưa gặp cỡ ảnh lớn nhất: nhân theo (cỡ lớn nhất / cỡ lớn nhất đã gặp)² và đặt `vram_estimated = true`; UI ghi "ước lượng".
- **R20** Ước tính lần train thật: `epochs × (⌊ảnh train / batch⌋ / steps_per_s + ⌈ảnh val / 64⌉ × val_seconds_per_batch)`, hiển thị thành khoảng **−20 % … +50 %**, ghi nguồn. Sau epoch thật đầu tiên thay bằng số đo, ±15 %.
- **R21** Cổng (D-43): nút Train mở khi có trial `passed` với cùng `trial_key`, phiên bản dữ liệu, điểm xuất phát, `host_id`, `gpus`, `env_tag`. Lệch gì, nút ghi đúng thứ đó; riêng `env_tag` lệch do bản phát hành: "môi trường đổi do bản nền tảng mới — dựng lại và chạy thử".
- **R22** Chen GPU (D-48): GPU chỉ bị giữ bởi bản thử model (deployment có `idle_stop_min`) thì chạy thử dừng bản thử đó, ghi sự kiện và thông báo cho người đã bật. Deployment khác và lần train khác: không đụng; chạy thử xếp hàng, nút ghi đang chờ ai.

**Train thật**
- **R23** `StartTraining` với `project_recipe_id`: server chốt bản, kiểm cổng (`TRACK_TRIAL_REQUIRED`), ghim `Hosts = [host_id]`. `runs.plan` do shim điền: shim tải `.mlp/command.txt` và `mlp_train.yml` ngay sau khi sinh.
- **R24** Tiến độ đọc từ stdout theo `progress.pattern`: epoch e/E, bước i/n, loss của dòng; pha con `train` hay `validate` (dòng `Test:`). Lần train thật đặt `print_freq = 10`. ETA = thời gian mỗi epoch đã đo × số epoch còn lại.
- **R25** Trục biểu đồ: metric ghi theo epoch (`metrics.epochKeys`) được shim quy về bước toàn cục `(epoch + 1) × số bước mỗi epoch`; run mới ghi `metric_axis = step`. Loss theo epoch đổi tên thành `train/loss_epoch` (hiện trùng khoá `train/loss` với loss theo bước). So sánh không chồng run có `metric_axis` khác nhau.
- **R26** Thất bại không thử lại: manifest khai `failures: [{ pattern, reason, retry: false }]`; shim gặp mẫu thì kết thúc với lý do đó và attempt **không** được chạy lại (hiện mã thoát 1 bị thử lại 3 lần). D-FINE: `^Loss is (nan|inf)` → `loss_invalid`. Metric không hữu hạn: shim gửi sự kiện kèm bước thay vì bỏ lặng lẽ; trang Run vẽ vạch đỏ tại bước đó.
- **R27** Báo cáo epoch đầu (`runs.first_epoch`), lập khi điểm metric chính đầu tiên tới: metric chính hữu hạn · điểm lưu đã tải lên, sha khớp · tốc độ đo so với ước tính · ETA mới.
- **R28** Điểm lưu bền: recipe khai `checkpoints: [last.pth, best_stg1.pth]` (schema nhận danh sách [XÂY]). Mỗi lần tải lên một `seq` mới, khoá S3 `checkpoints/{seq:D6}/{tên}`; `step` = số trong tên file nếu có, không thì số epoch đã xong. Chọn bản khôi phục và dọn (giữ 3 bản mỗi tên) theo `(step, seq)`; khôi phục kiểm sha. (Hiện `last.pth` luôn nhận step 0 và ghi đè một khoá.)
- **R29** Giai đoạn 2 của D-FINE không ghi `last.pth`: trang Run ghi "sự cố trong giai đoạn 2 sẽ chạy lại từ đầu giai đoạn 2 (epoch <stop_epoch>)".
- **R30** URL dữ liệu ký lại mỗi lần launch attempt, trong `WorkloadLauncher.LaunchAsync` trước `ContainerApply` (hiện ký một lần lúc gửi job, hết hạn sau 24 giờ).

## 5. Luồng chính

**H1 — lần đầu, dữ liệu vừa gán nhãn**
1. Ngăn "Tạo version dataset từ nhãn" (M05) có nút **Train với phiên bản này** → chọn hoặc tạo project → tab Công thức, nháp đặt sẵn phiên bản đó.
2. Chọn thẻ D-FINE → canvas ở trạng thái lần đầu.
3. Node Dữ liệu: báo cáo phiên bản chạy; chưa chia tập → **Chia và lưu thành v(n+1)** → nháp chuyển sang phiên bản mới.
4. Node Môi trường & máy: chọn máy (chọn sẵn máy có GPU lớn nhất). Node Model: biến thể mặc định; thẻ Fine-tune sáng.
5. **Chạy thử** → kiểm nhanh → thiếu môi trường: "Dựng môi trường (10–30 phút, không giữ GPU)", log chạy trực tiếp → dựng xong tự soi → chạy khói.
6. Kết quả chạy thử trên canvas + khoảng ước tính → **Train thật** → trang Run.
7. Trang Run: tiến độ theo epoch, báo cáo epoch đầu; xong thì banner Đăng ký model [CÓ].

**H2 — sửa tham số rồi train lại**
1. Bảng Lần train → **Nấu lại từ #n** → tab Công thức, nháp nạp từ bản chốt của #n.
2. Ngăn Lịch học: đổi LR đỉnh → soi lại → đường LR mới chồng lên cũ; thanh hậu quả ghi "cần chạy thử" và các khoá config đã đổi.
3. Ngăn Batch & tăng cường: đổi kích thước ảnh → thanh hậu quả thêm "bộ nhớ GPU thay đổi, chưa đo".
4. Chạy thử → bản chốt mới → kết quả → Train.
5. Chọn hai lần train → So sánh: dòng "cùng tập val ✔", bảng chỉ gồm tham số khác nhau, biểu đồ chồng.

## 6. Hợp đồng

Quyền: đọc `run.read`; ghi nháp, chạy thử, train, dựng môi trường `job.submit` [CÓ, như `StartTraining`]; chia tập `dataset.write`.

**`tracking.proto` — TrackingService**

| RPC | Mô tả |
|---|---|
| `AdoptRecipe(project_id, recipe_name) → ProjectRecipe` | R1 |
| `ListProjectRecipes(project_id)` | kèm cờ "gốc đã đổi" |
| `GetDraft(project_recipe_id) → DraftState` | nháp của người gọi (tạo nếu chưa có) + `changes[]` + `consequences[]` + `gate` |
| `UpdateDraft(Draft, etag) → DraftState` | R2 |
| `ResetDraft(project_recipe_id, from: revision_id \| run_id \| origin)` | R4, R6 |
| `ListRevisions`, `GetRevision`, `DiffRevisions(a, b) → rows[]` | bảng khác biệt nhóm theo ngăn; manifest khác thì kèm diff văn bản |
| `StartTrial(project_recipe_id) → Trial` | chốt bản (R3), kiểm nhanh, gửi job |
| `GetTrial`, `ListTrials(project_id)`, `CancelTrial` | |
| `StartTraining` (đổi) | thêm `project_recipe_id`. Có trường này: đi đường canvas (R23). Không có: đường cũ, giữ nguyên hành vi |
| `Run` (thêm trường) | `revision_number`, `train_kind`, `start_kind`, `retrain_of_number`, `trial_id`, `plan_json`, `first_epoch`, `train_phase` (`train`·`validate`), `epoch`, `epochs`, `step_in_epoch`, `steps_per_epoch`, `stage`, `metric_axis` |
| `CompareRuns(run_ids[]) → Comparison` | `same_val`, `same_axis`, `rows[]` khác nhau |

`gate` trong `DraftState`: `can_train`, `reasons[]`, `estimate { low_s, high_s, source }`.

**`catalog.proto` — CatalogService**

| RPC | Mô tả |
|---|---|
| `ListEnvironments(recipe_name) → Environment[]` | theo máy: trạng thái, lúc dựng |
| `BuildEnvironment(recipe_name, host_id) → Environment` | job `env_build` (R12) |
| `GetInspection(recipe_name, variant, params_json, num_classes) → Inspection` | trả bản đệm hoặc gửi job `inspect`; `state`, `overlay_yaml`, `config_yaml`, `blocks[]` (lịch do web tính, mục 7.4) |
| `ListRecipeFiles(recipe_name, env_tag, path)`, `ReadRecipeFile(…)` | hai nguồn: file của recipe (manifest, Dockerfile, `_shared`, bộ chuyển) đọc từ `Catalog:RecipesDir`; mã repo nguồn từ bản soi |
| `ListRecipeUsage(recipe_name)` | các project đang dùng (tab "Đang dùng ở") |

**`data.proto` — DataService**: `GetVersionReport(version_id)` (gửi job `data_check` nếu chưa có) · `SplitVersion(version_id, ratios, group_by, note) → DatasetVersion` (R8, R9).

**`runtime.proto` — RuntimeService (token job)** — job `trial`, `inspect`, `data_check` không thuộc run nên cần đường báo riêng:

| RPC / trường | Mô tả |
|---|---|
| `ReportTrialStage(trial_id, stage, state, seconds, detail_json)` | từng chặng R18; chặng cuối kèm số đo R19 |
| `CreateResultUploads(kind: trial\|inspect\|data_check, id, paths[])` | URL ký sẵn để tải ảnh mẫu, config, báo cáo (hiện `CreateArtifactUploads` đòi `run_id`) |
| `CompleteInspection`, `CompleteDataCheck` | nộp kết quả có cấu trúc |
| `ReportMetricInvalid(key, step)` | R26 |
| `ProgressReport` (thêm) | `epoch`, `epochs`, `step`, `steps`, `sub_phase` |

Kết thúc job: `WorkloadLauncher.OnExitAsync` / `FailAsync` đồng bộ thêm trial, environment, inspection, version report (hiện chỉ đồng bộ run, deployment, notebook). Volume kết quả gắn cho job `trial` theo `trial_id`.

## 7. Manifest và runtime

**7.1 Manifest (`recipes/_schema/mlp-recipe.schema.json`)**
- `params.<tên>` thêm: `pane` (`batch`·`learn`·`objective`·`schedule`), `label`, `help`, `affectsTrial` (mặc định true), `affectsInspect` (mặc định false); `default: null` = "theo cấu hình của biến thể" (giá trị thật lấy từ bản soi).
- `canvas`: `{ phases: [{ key, label, untilKey }], blocks: [{ key, label, match }] }`.
- `entrypoints.inspect`; `entrypoints.train.trial` (`overrides` cho lượt 1, `resumeOverrides` cho lượt 2); `entrypoints.train.progress.pattern` + `validatePattern`; `entrypoints.train.checkpoints` nhận danh sách; `failures[]`; `metrics.epochKeys`; nguồn metric mới `stdout: { pattern }`; `artifacts.samples`.
- Placeholder mới `{overrides}` trong mẫu lệnh (JSON của các ghi đè do nền tảng đặt), thêm vào danh sách tên hợp lệ của `RecipeChecker`.

**7.2 Recipe `d-fine`**

Cách truyền: tham số có mặc định truyền **dạng trần** `--multiscale {params.multiscale}` (Template in `true`/`false`); dạng có điều kiện `{params.x?--x {params.x}}` chỉ dùng cho tham số `default: null`, vì Template coi `false` và `0` là rỗng.

| Tham số | Kiểu, mặc định | Ngăn | Xuống config | Soi lại |
|---|---|---|---|---|
| `epochs` [CÓ] | integer ≥ 1, 72 · `affectsTrial: false` | schedule | `epochs` + co các mốc lịch [CÓ] | có |
| `batch` [CÓ] | integer, 16 | batch | `train_dataloader.total_batch_size` | không |
| `seed` [CÓ] | `affectsTrial: false` | objective | cờ CLI | không |
| `use_amp` [CÓ] | boolean | objective | cờ CLI | không |
| `lr` | number > 0, null | schedule | `optimizer.lr`; khi không đặt `backbone_lr_scale` thì **giữ tỉ lệ backbone của biến thể** | có |
| `backbone_lr_scale` | number, 0 < x ≤ 1, null | learn | viết lại `lr` của mọi nhóm trong `optimizer.params` có regex chứa `backbone` thành `lr × scale` | có |
| `freeze` | `none`·`backbone`, `none` | learn | `backbone` → `HGNetv2.freeze_at: 3`, `freeze_stem_only: false`, `freeze_norm: true`; `none` → `freeze_at: -1`, `freeze_norm: false` | có |
| `img_size` | integer bội số 32, 320–1280, 640 | batch | bốn chỗ: `Resize` trong ops của train và val, `collate_fn.base_size`, `eval_spatial_size` (handler serve đọc khoá này) | có |
| `multiscale` | boolean, true | batch | false → `collate_fn.base_size_repeat: null` | có |
| `aug_strong` | boolean, true | batch | false → bỏ `RandomPhotometricDistort`, `RandomZoomOut`, `RandomIoUCrop` khỏi `ops` | có |
| `aug_flip` | boolean, true | batch | false → bỏ `RandomHorizontalFlip` | có |

Ba mẫu ở ngăn Phần được học là bộ giá trị đặt sẵn: *Dữ liệu ít* (`freeze = backbone`) · *Cân bằng* (mặc định của biến thể) · *Dữ liệu nhiều* (`backbone_lr_scale = 1`). Dưới 500 ảnh train thì đề nghị *Dữ liệu ít*.

Khai thêm:
- `canvas.phases`: `stage1` "Học với tăng cường" (`untilKey: train_dataloader.collate_fn.stop_epoch`), `stage2` "Tinh chỉnh từ bản tốt nhất". `canvas.blocks`: backbone, encoder, decoder.
- `progress.pattern` cho dòng `Epoch: [e/E]  [ i/n]  …  loss: x` (nhận khoảng trắng đệm trong `[ 5/107]`); `validatePattern: ^Test:`.
- `trial.overrides`: `{ epochs: 1, "train_dataloader.collate_fn.stop_epoch": 1, "train_dataloader.dataset.transforms.policy.epoch": 1, print_freq: 1 }`; `resumeOverrides`: như trên với `epochs: 2`. Lần train thật: `{ print_freq: 10 }`. `--extra-json` áp sau bước co mốc nên không xung đột [CÓ].
- `failures: [{ pattern: "^Loss is (nan|inf)", reason: loss_invalid, retry: false }]`.
- `metrics.epochKeys: ["Test/*", "test_*", "train_*"]`; `train_loss` → `train/loss_epoch`; nhóm LR cuối → `lr/main` (`Lr/pg_0` là nhóm backbone); nguồn `stdout` cho dòng `Metrics: {…}` → `val/precision`, `val/recall`, `val/f1` **toàn cục** (ngưỡng tin cậy cố định 0,5; repo không in số theo lớp).
- `artifacts.samples: [train_samples, val_samples]`; `checkpoints: [last.pth, best_stg1.pth]`.

**7.3 `recipes/_shared/detr_family/make_config.py`** — thêm cờ `--lr`, `--backbone-lr-scale`, `--freeze`, `--img-size`, `--multiscale`, `--aug-strong`, `--aug-flip`, `--no-data` (cho soi). Phép sửa danh sách đọc config đã gộp của biến thể, sửa phần tử, ghi **nguyên danh sách** (repo nguồn thay cả danh sách khi gộp). Chỉ thêm cờ, không đổi nghĩa cờ cũ (R6b): deimv2 và rt-detrv4 dùng chung script.

**7.4 `recipes/_shared/detr_family/inspect.py`** [XÂY] — chạy CPU, `workdir /opt/recipe/src` (trọng số backbone nằm ở đường dẫn tương đối): gọi `make_config.py --no-data`, nạp bằng `YAMLConfig` của repo nguồn, ghi `overlay.yaml`, `config.yaml` (đã gộp), `blocks.json` (mỗi khối: số tham số, số được học), và gói file văn bản của `/opt/recipe/src` (bỏ `.git`, file > 512 KB, file trọng số). Lịch (đường LR, mốc giai đoạn) do **web** tính từ `config.yaml`: LR tăng tuyến tính trong `lr_warmup_scheduler.warmup_duration` bước rồi đi ngang; mốc giai đoạn từ `stop_epoch`.

**7.5 `to_coco.py`** — đọc `mlp-split.json` (R10); cờ `--report` ghi JSON (đếm theo lớp và tập, dòng bị loại kèm lý do) và `--labels-only` cho job `data_check`; nhận danh sách file tập con (dataset dạng COCO: chỉ link ảnh có trong danh sách).

**7.6 `mlp-shim`**
- Chế độ `MLP_TRIAL=1`: nhận tập con; chạy lượt 1 rồi lượt 2 theo R18; gọi `mlp_serve.selftest` [XÂY trong `python/mlp_serve`, thêm vào danh sách file nhúng của api]; báo từng chặng qua `ReportTrialStage`.
- Bộ đọc stdout: `progress.pattern`, `max mem:`, `failures[]`, nguồn metric `stdout`. Quy đổi trục (R25); báo metric không hữu hạn (R26); tải `.mlp/command.txt`, `mlp_train.yml`, `artifacts.samples`; điểm lưu theo `seq`, nhiều tên (R28).
- Dừng tiến trình: gửi tín hiệu cho cả nhóm tiến trình (hiện chỉ `/bin/sh` nhận, `torchrun` có thể sống tiếp).

**7.7 Control plane** — loại job `env_build`, `inspect`, `data_check`, `trial`; launcher rẽ nhánh theo loại job; ký lại URL (R30); không thử lại khi lý do có `retry: false` (R26).

## 8. Màn hình

Quy ước: không hộp thoại giữa màn hình; chuỗi UI là khoá tiếng Anh qua `t()` + `vi.json`; không thêm thư viện canvas (CSS grid + SVG như `Pipelines.tsx`).

**8.1 Project — `/w/{ws}/projects/{p}`** (thay S-09-2). Ba tab: **Công thức** (`?tab=recipe`, mặc định) · **Lần train** (bảng hiện có, thêm cột Bản chốt và Kiểu; "Train again" đổi thành "Nấu lại từ #n") · **Models**. Có lần train đang chạy: đầu tab Công thức có dải "đang train #n — mở".

**8.2 Tab Công thức — canvas (S-09-4 mới)**

```
 [D-FINE ▾]   nháp của bạn · 2 ✎ so với r8        [▷ Chạy thử]   [▶ Train thật · cần chạy thử]
 ┌1 Môi trường & máy ─┐   ┌4 HUẤN LUYỆN ⟲  72 epoch × 106 bước ──────────────────────────┐   ┌5 Đóng gói ──┐
 ├2 Dữ liệu ──────────┤ ─▶│ Batch & tăng cường │ Phần được học │ Mục tiêu & tối ưu       │ ─▶│ bản tốt nhất│
 └3 Model & điểm      ┘   │ Lịch học: [giai đoạn 1 ········|gđ 2]   ⟲ Kiểm định │ Điểm lưu│   ├6 Đánh giá ──┤
    xuất phát             └──────────────────────────────────────────────────────────────┘   │ sau đăng ký │
 Thanh hậu quả: ảnh 640 → 800 cần chạy thử; bộ nhớ GPU thay đổi, chưa đo.   [Xem 2 thay đổi] [Hoàn tác]
```

| Phần | Hiện trên thân | Ngăn chi tiết (luôn là ngăn trượt phải dùng chung `Dialog`, đè lên trang; không bao giờ là cột chen vào bố cục) |
|---|---|---|
| 1 Môi trường & máy | máy đã chọn, GPU, ai giữ GPU; môi trường `ready`/`building`/`missing` | chọn máy, GPU, CPU; Dựng môi trường + log dựng; Toàn văn: cây file chỉ đọc |
| 2 Dữ liệu | phiên bản, số ảnh, thanh chia tập, số lưu ý | chọn dataset/phiên bản; phân bố lớp theo tập; dòng bị loại kèm lý do; Chia tập; sau chạy thử: ảnh `val_samples` |
| 3 Model & điểm xuất phát | biến thể, kiểu train | bảng biến thể (số tham số, điểm công bố, tốc độ đã đo nếu có); thẻ **Fine-tune** và **Train lại**; dòng "Khác: chỉ giữ backbone học sẵn"; ô xác nhận license như hiện có |
| 4 · Batch & tăng cường | batch, cỡ ảnh; sau chạy thử: 4 ảnh `train_samples` | `batch`, `img_size`, `multiscale`, `aug_strong`, `aug_flip`; lưới ảnh batch (ghi rõ "sau tăng cường, cỡ ảnh đổi theo từng batch khi multiscale bật") |
| 4 · Phần được học | ba khối: khoá / học chậm / học đủ, số tham số | ba mẫu; `freeze`, `backbone_lr_scale` |
| 4 · Mục tiêu & tối ưu | optimizer, EMA, AMP | `use_amp`, `seed`; phần còn lại chỉ đọc từ config đã soi |
| 4 · Lịch học | dải hai giai đoạn + đoạn khởi động | `epochs`, `lr`; đường LR của nhóm chính và nhóm backbone; câu "khởi động 500 bước ≈ n epoch với dữ liệu này" |
| 4 · Kiểm định | metric chính, số ảnh val | giải thích metric bằng lời thường; khi chạy: precision, recall, F1 toàn cục |
| 4 · Điểm lưu | "mỗi epoch trong giai đoạn 1" | điểm lưu đã tải lên; câu R29 |
| 5 Đóng gói | bản được chọn | file sẽ đóng gói; sau chạy thử: kết quả nạp lại |
| 6 Đánh giá | "sau khi đăng ký model" | dẫn sang Đánh giá hiện có (D-39) |

Trạng thái lần đầu (project chưa có lần train): chỉ node 1–3 và nút Chạy thử sáng; vòng huấn luyện thu thành một dòng "72 epoch, theo mặc định của công thức · Mở". Công tắc **Giải thích** (bật sẵn) hiện câu lời thường dưới mỗi ô; **Nâng cao** trong từng ngăn hiện tên khoá kỹ thuật. Ký hiệu: ✔ chạy xong · ◇ đã kiểm · ✖ lỗi · ▶ đang chạy · ↻ đã cũ · ✎ đã sửa.

Mỗi ngăn có thanh lật **Trực quan · Cấu hình · Code**. Đợt 1: Cấu hình và Code chỉ đọc. Mức Cấu hình tô khác màu dòng có trong `overlay_yaml` (nền tảng và người dùng đặt) so với dòng kế thừa từ repo nguồn.

Thanh hậu quả: giá chạy lại (số đo của lượt thử trước trên máy đó, chưa có thì "chưa đo") và **các khoá config đã đổi** (diff `overlay_yaml` của bản soi cũ và mới).

**8.3 Nút Chạy thử khi máy bận.** Ghi ai đang giữ GPU và đưa lối đi: làm ngay phần không cần GPU (kiểm nhanh, soi) · xếp hàng chạy thử · (R22) dừng bản thử model.

**8.4 Sau chạy thử.** Mỗi node mang ✔/✖ và thời lượng chặng; ngăn mang ◇ kèm một dòng kết quả ("6 tầng tạo mới cho 2 lớp", "đi qua cả 2 giai đoạn", "chạy nốt từ điểm lưu được"). Dưới canvas: tốc độ, bộ nhớ GPU đỉnh (kèm "ước lượng" khi R19 phải nội suy) so với bộ nhớ của máy, khoảng ước tính, và dòng **chưa bảo đảm**: chất lượng model · loss hỏng muộn · ảnh nặng hơn tập đã thử · thời gian có thể lệch gấp rưỡi. LR trong chạy thử không quá vài phần trăm LR đỉnh (đang khởi động) — ghi rõ để người dùng không đọc loss của chạy thử như chất lượng.

**8.5 Run — `/w/{ws}/projects/{p}/runs/{run}`** (S-09-3). Canvas với vòng huấn luyện mở thay `PhaseBar`: "epoch 12/72 · bước 54/106 · đang train | đang kiểm định · còn 14–19 giờ"; dải giai đoạn có con trỏ; ngăn Mục tiêu hiện loss gần nhất; ngăn Kiểm định hiện metric chính và precision/recall/F1; ngăn Điểm lưu hiện "đã tải lên x phút trước". Dưới canvas: báo cáo epoch đầu. Mọi con số metric kèm nhãn nguồn `val · lúc train`. Tab giữ nguyên, tab Configuration đổi thành **Máy đã chạy gì** (bản chốt, khác gì so với lần trước, lệnh, config đã sinh, tóm tắt chia tập, tag môi trường).

**8.6 So sánh.** Dòng điều kiện "cùng tập val ✔ / ✖ số không so được" và "cùng trục ✔ / ✖"; bảng chỉ gồm dòng khác nhau, nhóm theo ngăn; biểu đồ chồng [CÓ].

**8.7 Recipe trong Catalog (S-17-2).** Cùng canvas ở chế độ chỉ đọc; tab **Luồng** · **Biến thể** [CÓ] · **Toàn văn** · **Đang dùng ở**; nút chính "Dùng cho project…".

## 9. Sự kiện

`trial.passed`, `trial.failed` (thông báo trong app cho người bấm) · `environment.ready`, `environment.failed` · `run.metric_invalid` · `run.first_epoch` · `deployment.stopped_for_trial` (R22).

## 10. Lỗi

| reason | status | Thông điệp |
|---|---|---|
| `TRACK_TRIAL_REQUIRED` | FAILED_PRECONDITION | Cần một lần chạy thử đạt với đúng cấu hình này: <thứ đã lệch> |
| `TRACK_DRAFT_CONFLICT` | ABORTED | Nháp đã đổi ở tab khác, tải lại |
| `TRACK_RETRAIN_SAME_DATA` | INVALID_ARGUMENT | Train lại cần một phiên bản dữ liệu khác lần #n |
| `TRACK_RETRAIN_NO_REVISION` | FAILED_PRECONDITION | Lần train #n có trước bản chốt — dùng Nấu lại |
| `TRACK_RECIPE_NOT_SUPPORTED` | FAILED_PRECONDITION | Công thức này chưa dùng được với canvas (chưa khai `canvas`) |
| `TRACK_BATCH_TOO_LARGE` | INVALID_ARGUMENT | Batch lớn hơn số ảnh train |
| `DATA_NOT_SPLIT` | FAILED_PRECONDITION | Phiên bản chưa chia tập train/val |
| `CATALOG_ENV_MISSING` | FAILED_PRECONDITION | Máy <tên> chưa có môi trường của công thức — dựng trước (10–30 phút, không giữ GPU) |
| `CATALOG_INSPECT_FAILED` | UNAVAILABLE | Không soi được công thức: <dòng lỗi> |

## 11. Nghiệm thu

Chạy trên máy GPU thật với `d-fine` / `dfine-s` và một dataset phát hiện vật thể đã có nhãn.

1. **H1 đi trọn** từ phiên bản chưa chia tập tới lần train đang chạy; `TrainSheet.tsx` đã bị gỡ.
2. Chia tập hai lần cùng tham số cho cùng kết quả; thêm ảnh rồi chia lại: không ảnh cũ nào đổi tập. Hai lần train khác `seed` trên cùng phiên bản: danh sách ảnh val mà bộ chuyển in ra giống hệt nhau.
3. Chạy thử trên máy đã có môi trường đi đủ các chặng R18; log lượt 2 có `Refresh EMA at epoch 1`. Thời gian đo được ghi vào tài liệu này (mục tiêu ≤ 10 phút [KIỂM]).
4. Cố ý làm hỏng: (a) image thiếu một gói → `stage1` hỏng; (b) hơn 5 % dòng nhãn có id lớp ngoài khoảng → `data` hỏng. Cả hai: nút Train vẫn khoá.
5. `multiscale = false`, đổi `img_size` 640 → 800: thanh hậu quả báo cần chạy thử; config đã sinh có 800 ở cả bốn chỗ; ảnh `train_samples` cỡ 800.
6. Tắt `aug_flip`, `aug_strong`, `multiscale`: config đã sinh phản ánh đúng cả ba (kiểm việc truyền `false`).
7. Đổi `epochs`: nút Train **không** khoá lại; dải giai đoạn và ước tính đổi theo.
8. `freeze = backbone`: bản soi báo số tham số **được học** của khối backbone bằng 0.
9. Đổi `lr` mà không đặt `backbone_lr_scale`: LR các nhóm backbone trong config đã sinh giữ đúng tỉ lệ của biến thể.
10. Train thật: % và ETA xuất hiện trong epoch đầu; pha "đang kiểm định" hiện đúng lúc; loss và mAP cùng trục; vạch chuyển giai đoạn đúng epoch `stop_epoch`.
11. Khôi phục từ bản bền: làm máy mất kết nối quá hạn chờ (`node_lost`), xoá volume `mlp-run-*` của run, để attempt mới tải điểm lưu bền; log có dòng kiểm sha; lần train chạy nốt từ epoch gần nhất và qua được mốc `stop_epoch` (cần `best_stg1.pth`). Trên S3 có nhiều khoá `checkpoints/{seq}/…`, mỗi tên giữ 3 bản.
12. Ép loss hỏng (LR rất lớn): lần train dừng **một lần** (không thử lại), lý do hiện "Loss is nan…", biểu đồ có vạch tại bước đó.
13. **H2 đi trọn**: Nấu lại từ #n → đổi `lr` → chạy thử → train → So sánh hiện "cùng tập val ✔" và đúng các dòng khác nhau.
14. Train lại trên phiên bản mới: cột Kiểu ghi "Train lại từ #n"; cùng phiên bản bị từ chối; run không có bản chốt bị từ chối kèm lý do.
15. GPU bị bản thử model giữ: chạy thử dừng nó và ghi sự kiện. GPU bị deployment thường giữ: chạy thử xếp hàng, nút ghi tên deployment.
16. **Đường cũ**: một pipeline có bước `train` và một sweep HPO chạy `d-fine` trên phiên bản chưa chia tập vẫn chạy được (bộ chuyển chia bằng hàm băm, in cảnh báo; môi trường tự dựng).
17. `mlp-sqlgate` không có vi phạm mới; giao diện không còn chuỗi lẫn Việt/Anh.

## 12. Thứ tự xây

Mỗi lát kết thúc bằng thứ người dùng thấy được. Build theo quy tắc bảo vệ máy: một image mỗi lần, gom thay đổi cho một lần build. **Mỗi lát đụng shim hoặc `_shared` làm đổi `env_tag`** → phải dựng lại image recipe trên máy GPU (10–30 phút) và mọi trial cũ hết hiệu lực; gom các thay đổi đó vào một lần cho mỗi lát.

| Lát | Nội dung | Người dùng thấy |
|---|---|---|
| **1. Canvas thay form** ✔ 2026-11-20 | `project_recipes`, `drafts`, `revisions`; cột mới của `runs`; `AdoptRecipe`, nháp, `ResetDraft` (gồm từ run — "Nấu lại"); `SplitVersion`, `mlp-split.json`, `to_coco.py` đọc split; tab Công thức với node 2, 3, 5, 6, node 1 chỉ chọn máy, vòng huấn luyện hiển thị 4 tham số đã có; Train đi qua bản chốt (chưa có cổng); gỡ `TrainSheet` | Canvas thay form; tập val cố định; mỗi lần train ghi bản chốt; Nấu lại |
| **2. Chỉnh nông + soi** | `to_coco.py --report` + job `data_check` + `version_reports` (dời từ lát 1: job không thuộc run cần `runtime.proto` của lát này); 7 tham số mới, `make_config.py`, `inspect.py`; `environments`, `inspections`; job `env_build`, `inspect`; `runtime.proto` cho job không thuộc run; agent phát log dựng; trạng thái môi trường ở node 1; dải lịch, sơ đồ khối; mức Cấu hình/Code chỉ đọc; thanh hậu quả | Chỉnh LR, cỡ ảnh, tăng cường, khoá backbone; xem config và mã; dựng môi trường không giữ GPU |
| **3. Chạy thử + cổng** | job `trial`, chế độ thử của shim, **bộ đọc stdout** (tiến độ, `max mem`, `failures`), `mlp_serve.selftest`, `trials`, ước tính, cổng, launcher đòi môi trường cho job canvas, chen GPU, ghim máy | Nút Chạy thử; kết quả theo node; nút Train có cổng |
| **4. Trang Run sống** | quy đổi trục, metric `stdout`, sự kiện metric hỏng, không thử lại khi `loss_invalid`, báo cáo epoch đầu, điểm lưu theo `seq` + migration, ký lại URL, `plan` từ shim, canvas trên trang Run | % và ETA, hai giai đoạn, chạy nốt đúng |
| **5. So sánh và trang recipe** | `CompareRuns`, `DiffRevisions`, Train lại, canvas chỉ đọc ở Catalog, `ListRecipeUsage`, nút "Train với phiên bản này" ở M05 | H1 và H2 đi trọn |

### Hiện trạng lát 1 (2026-11-20)

Đã hiện thực: migration `db/track/202611201400_recipe_canvas.sql`, `db/data/202611201410_version_split.sql`; `Tracking/RecipeCanvas.cs` (nhận công thức, nháp, bản chốt, train theo nháp), `Data/VersionSplit.cs` (chia tập, phát hiện thư mục train/val); RPC `AdoptRecipe`, `ListProjectRecipes`, `GetDraft`, `UpdateDraft`, `ResetDraft`, `ListRevisions`, `SplitVersion`, `StartTraining.project_recipe_id`; `to_coco.py` đọc `mlp-split.json`, không có thì chia theo băm đường dẫn (bỏ cắt ngẫu nhiên theo seed); web `components/RecipeCanvas.tsx`, tab Công thức / Lần train / Models, "Nấu lại từ #n", cột Bản chốt và Kiểu; `TrainSheet.tsx` đã gỡ.

Đã kiểm trên trình duyệt và API (stack dev): nhận D-FINE về project; nháp tự lưu, đánh dấu ✎ và đếm thay đổi; chia `fire-wildfire` v1 → v2 (1 460 / 174 / 180 ảnh; lần đầu mất 46 giây vì ghi từng file, sau khi đổi sang một lệnh sao chép thì 1 624 file của `fire-smoke-test` chia trong chưa tới 1 giây); Train tạo run #8 với bản chốt r1, job xếp hàng đúng lý do khi GPU bận; Nấu lại từ run cũ không có bản chốt; bảng Lần train có cột mới. Bộ chuyển thử ngoài container trên dữ liệu giả: hai seed cho cùng tập val, file chia tập được tôn trọng, tập test bị giữ lại.

**Chưa kiểm:** một lần train thật chạy bằng bộ chuyển mới bên trong container (GPU đang bị deployment `owlv2-gpu` giữ); lần đầu chạy sẽ dựng lại image recipe trên máy GPU vì `to_coco.py` đổi. Chưa kiểm "Lấy theo gốc", đổi máy/GPU, chọn "chỉ giữ backbone", `ListRevisions` trên giao diện (chưa có màn hình).

Khác spec: thay đổi của nháp chỉ so `variant` và tham số (dữ liệu, điểm xuất phát, máy không tính là thay đổi của công thức); `runs` mới thêm `revision_id, train_kind, start_kind, retrain_of_run_id, host_id` (các cột `trial_id, plan, first_epoch, metric_axis` để lát dùng tới chúng); khi mở ngăn chi tiết ở màn 1440 px canvas xếp thành một cột.

### Hiện trạng lát 2 (2026-11-20)

Đã hiện thực: 7 tham số chỉnh nông của D-FINE (`lr`, `backbone_lr_scale`, `freeze`, `img_size`, `multiscale`, `aug_strong`, `aug_flip`) khai trong manifest kèm `pane`/`affectsInspect`; `make_config.py` nhận chúng (sửa phần tử danh sách, ghi nguyên danh sách; đổi `lr` giữ tỉ lệ backbone); `inspect_recipe.py` (chạy CPU trong image, in `MLP_INSPECT`); migration `db/catalog/202611201500_environments.sql`; `Catalog/Environments.cs`, `Infrastructure/RecipeEnvironments.cs`; RPC `ListEnvironments`, `BuildEnvironment`, `GetInspection`, `ListRecipeFiles`, `ReadRecipeFile`; web `components/RecipeInspect.tsx` (môi trường ở node 1, dải hai giai đoạn + đường LR, sơ đồ khối + ba mẫu, bảng tối ưu, danh sách phép tăng cường, mức Cấu hình/Code chỉ đọc, thanh hậu quả theo khoá config).

Khác spec: dựng môi trường và soi chạy bằng lệnh có sẵn của agent (`ImageBuild`, `ContainerRunOnce`) thay vì loại job mới, nên không cần sửa agent/shim hay OTA; log dựng chưa hiện theo dòng. Báo cáo phiên bản (`data_check`) dời sang lát 3. Bản soi nhận cả giá trị mặc định của manifest (nếu không, lịch hiện theo 132 epoch của biến thể thay vì 72).

Đã kiểm qua API trên máy GPU thật: dựng môi trường D-FINE (ready); soi `dfine-s` với `freeze = backbone`, `img_size = 800`, 2 lớp → backbone 1,84 M tham số, 0 được học; encoder 4,02 M; decoder 4,35 M; config đã gộp có `epochs 72`, `stop_epoch 65`, `eval_spatial_size [800, 800]`; liệt kê file recipe. Bộ dựng cấu hình thử ngoài container trên config D-FINE-S gốc.

Đã kiểm trên trình duyệt: node 1 "môi trường sẵn sàng"; vòng huấn luyện "đã soi"; ngăn Lịch học có dải hai giai đoạn (epoch 1–22 / 23–24 với 24 epoch), khởi động 500 bước ≈ 2,2 epoch, đường LR hai nhóm (đỉnh 0,0002 và 0,0001); ngăn Phần được học có ba khối; mức Cấu hình hiện phần nền tảng đặt; "Lấy bản trong Catalog" nhận manifest mới; mẫu "Ít dữ liệu" → nháp lưu → soi lại → backbone "khoá", 8,5 / 10,3 M tham số được học. Hai lỗi hiển thị đã sửa: khối decoder ghi "học một phần" vì 2 tham số cố định (giờ chỉ ghi khi dưới 99 %); thân ngăn in tên khoá thay vì nhãn.

**Chưa kiểm:** thanh hậu quả liệt kê khoá config đã đổi (cần bản soi của bản chốt gốc; project này chưa có bản chốt nào được soi nên hiện câu dự phòng); mức Code; nút Dựng môi trường trên giao diện (đã kiểm qua API); lần train thật với tham số mới (GPU bị `owlv2-gpu` giữ).

### Hiện trạng lát 3 (2026-11-20)

Đã hiện thực: migration `db/track/202611201600_trials.sql` (`runs.is_trial`, `runs.trial_id`, `track.trials`); `Tracking/RecipeTrials.cs` (tập con theo băm: max(160, 10 × batch) ảnh train, 128 ảnh val; đọc dòng `[mlp-trial]`; Start/Get/Cancel/FinishTrial); cổng trong `RecipeDrafts.StateAsync` (khớp nội dung bản chốt bỏ `epochs`/`seed`, phiên bản dữ liệu, điểm xuất phát, máy nếu ghim) + ước tính −20 %…+50 %; train ghim máy của lần thử đạt; shim `Trial.cs` (hai lượt tự nhiên: lượt 1 `epochs=1, stop_epoch=1`, lượt 2 resume với `epochs=2`; đo tốc độ, VRAM, giây kiểm định từ stdout); `make_config.py` đọc `MLP_EXTRA_JSON`; `python/mlp_serve/selftest.py`; giao diện: nút Chạy thử / Huỷ chạy thử, dải trạng thái, huy hiệu theo node, thẻ kết quả 6 chặng + "Chạy thử không bảo đảm".

Khác spec: chạy thử là một lần train ẩn (`is_trial`, số âm) để dùng lại job, log, metric, tải kết quả — không có loại job `trial` và RPC `ReportTrialStage` riêng; chặng báo qua dòng `[mlp-trial]` trong log. Launcher vẫn tự dựng môi trường lúc launch (chưa đòi môi trường sẵn); chen GPU (R22) và `failures[]` không thử lại (R26) chưa làm.

Sửa kèm: **lỗi tự khoá trong audit** (có từ trước): lệnh ném lỗi nghiệp vụ sau khi lệnh con đã lấy khoá chuỗi audit → bản ghi "thất bại" ghi bằng kết nối riêng chờ chính giao dịch đó → treo 30 s, trả "Internal error" che lỗi thật. Giờ lệnh ngoài cùng rollback trước khi ghi, lệnh con ghi trong giao dịch cha.

Đã kiểm: chạy thử tạo qua API (bản chốt r2, 160/128 ảnh), lần train ẩn không hiện trong bảng Lần train, canvas hiện "chạy thử đang xếp hàng", nút Train khoá với lý do "Cần chạy thử với đúng cấu hình này trước", thẻ Chạy thử liệt kê 6 chặng. Lỗi bản công thức cũ (không khai chạy thử) giờ báo ngay.

**Đã chạy thật trên RTX 3060** (sau khi dừng `owlv2-gpu` theo đồng ý của chủ sản phẩm): chạy thử `dfine-s`, khoá backbone, batch 8, 160/128 ảnh — đạt cả 6 chặng trong khoảng 2 phút sau khi có image: dữ liệu 56 s; giai đoạn 1 27 s (val/mAP50-95 = 0,0002); nạp trọng số "9 tầng được tạo mới cho 2 lớp"; chạy nốt 37 s, log có `Refresh EMA at epoch 1`; đóng gói 15 s; nạp lại và dự đoán 8 ảnh 6 s. Số đo 1,89 bước/s, bộ nhớ GPU đỉnh 4,6 GB (ước lượng do multi-scale). Cổng mở, ước tính 24 epoch: 34–63 phút.

Bài học vận hành: agent-builder đóng sẵn mã agent/shim trong image của nó — sửa shim thì phải build lại image `agent-builder` (lần chạy thử đầu dùng shim cũ nên train thường 24 epoch). Chi tiết chặng do shim in bằng tiếng Anh, giao diện dịch theo mẫu trong `vi.json`.

Lần train thật sau khi thử đạt: #9 `demo-5-epochs` (r3, 5 epoch — đổi `epochs` không đòi chạy thử lại), ghim máy của lần thử, xong sau 15 phút 32 giây: tải dữ liệu ~8,5 phút (phiên bản tham chiếu, tải 3 625 file từ nguồn) + train ~6,5 phút; ước tính 7–13 phút chỉ tính phần train → **ước tính phải cộng thời gian chuẩn bị dữ liệu** (lát 4). val/mAP50-95 0,2065 sau 5 epoch. Trong lúc train chưa có % (recipe chưa khai `progress` — lát 4).

**Chưa kiểm:** chạy thử hỏng ở từng chặng; huỷ chạy thử. `owlv2-gpu` đang dừng; cấu hình lưu ở scratchpad để tạo lại.

### Hiện trạng lát 4 (2026-11-20)

Đã hiện thực:
- **Tiến độ thật (R24).** Recipe khai `entrypoints.train.progress.pattern` (nhóm có tên `epoch`, `epochs`, `step`, `steps`, `loss`) và `validatePattern`; shim (`Progress.cs`) đọc từng dòng, báo `running` với bước toàn cục / tổng bước, ETA = thời gian mỗi epoch đã đo (train + kiểm định) × số epoch còn lại (trước epoch thứ hai: tốc độ bước của epoch đang chạy). Chi tiết đi qua trường mới `detail_json` của `ProgressReport`/`JobProgress` (epoch, bước, đang train hay kiểm định, loss, giây mỗi epoch, các giai đoạn).
- **Dải giai đoạn.** `canvas.phases[].untilKey` đọc từ config đã sinh (`progress.config`, theo tên khoá cuối, ví dụ `stop_epoch`).
- **Trục metric (R25).** `metrics.epochKeys` (glob trên khoá gốc) → shim đổi bước = (epoch + 1) × số bước mỗi epoch; điểm tới trước khi biết số bước thì chờ. `train_loss` → `train/loss_epoch`. Chưa có cột `runs.metric_axis` (để lát 5 khi so sánh).
- **Metric từ stdout.** Nguồn `regex` giờ ghi theo bước hiện tại thay vì bước 0; D-FINE đọc dòng `Metrics: {…}` → `val/precision`, `val/recall`, `val/f1` (toàn cục, ngưỡng 0,5).
- **Metric không hữu hạn (R26).** Không còn bỏ lặng lẽ: shim ghi một dòng log mỗi khoá/bước. Chưa có sự kiện `run.metric_invalid` riêng.
- **Không thử lại (R26).** `failures: [{pattern, reason, message, retry: false}]` ở gốc manifest; shim gặp mẫu thì kết thúc mã 2 và in `MLP_FAIL {reason, message}`; scheduler đọc dòng đó làm lý do (mã 2 không bao giờ thử lại). D-FINE: `^Loss is (nan|inf)` → `loss_invalid` kèm lời khuyên hạ LR hoặc tắt AMP.
- **Điểm lưu nhiều file (R28, bản gọn).** `checkpoints` nhận danh sách (D-FINE: `last.pth`, `best_stg1.pth`); bước của file không có số trong tên = số epoch đã train xong (trước đây luôn 0, và "1" trong `best_stg1` bị đọc nhầm là bước). Khôi phục tải **mọi** tên cùng lúc, kiểm sha256 từng file, chạy tiếp từ tên đứng đầu danh sách. Mỗi tên giữ một bản bền: tải bản mới thì xoá object cũ trên S3. **Không làm** cột `seq` + migration: một hàng mỗi (tên, nơi lưu) đã đủ để chạy tiếp đúng.
- **Ký lại URL dữ liệu (R30).** Mỗi lần launch attempt, `RefreshRunDataUrlsCommand` ký lại link của đúng tập file run đã nhận (kể cả tập con của chạy thử; danh sách lớn ghi lại vào S3).
- **Ước tính gồm tải dữ liệu.** Thời gian chặng `data` của lần chạy thử nhân theo số ảnh của cả phiên bản / số ảnh của tập con. Nguồn ước tính hiện bằng tiếng Việt (máy, có gồm tải dữ liệu, có gồm kiểm định), không còn chuỗi tiếng Anh từ server.
- **Trang Run.** Khi recipe báo chi tiết: khung "4 · Huấn luyện" thay thanh tiến độ: epoch e/E · bước i/n · đang train | đang kiểm định, %, thời gian còn lại; dải giai đoạn có con trỏ; ba ô Mục tiêu (loss gần nhất, đỏ khi NaN), Kiểm định (metric chính, tốt nhất, precision/recall/F1, nhãn "val · lúc train"), Điểm lưu ("tải lên x trước", "nếu máy gặp sự cố, chạy tiếp từ epoch n" — ở giai đoạn 2 con số này tự đứng ở mốc `stop_epoch`, thay câu R29); khối "Kiểm tra sau epoch đầu" (R27, lập trên web từ dữ liệu sẵn có, không lưu `runs.first_epoch`). Lý do lỗi dạng `mã: lời nhắn` được dịch cả hai phần.

**Đã kiểm trên RTX 3060** (sau khi dừng `owlv2-gpu`): lấy lại bản Catalog → chạy thử r4 đạt 6 chặng (image dựng lại vì shim mới); ước tính cho 3 epoch 9–17 phút, gồm ~6 phút tải dữ liệu. Run #10 `lat4-3-epochs`: trang Run hiện "epoch 1/3 · bước 71/182 · đang train · 13 % · còn khoảng 3 phút", loss trực tiếp, ô Kiểm định/Điểm lưu chờ đúng câu. Xong: `val/*` và `train/loss_epoch` nằm ở bước 546 = 3 × 182 (cùng trục với `train/loss`); `val/precision` 0,547, `val/recall` 0,210, `val/f1` 0,304; `val/mAP50-95` 0,138; điểm lưu bền `last.pth` và `best_stg1.pth` đều có bước 2 (trước đây 0 và 1).

Phát hiện: với ít epoch, `make_config.py` co `stop_epoch` theo tỉ lệ rồi làm tròn lên bằng `epochs` (3 epoch → `stop_epoch` 3) → giai đoạn 2 biến mất, dải giai đoạn chỉ còn một khúc. Cần quyết định cách co mốc cho lần train ngắn (lát sau).

**Chưa kiểm:** khôi phục từ bản bền trên máy khác (cần làm mất máy), `loss_invalid` thật, ký lại URL sau 24 giờ.

Chưa làm trong lát 4: `plan` do shim tải lên (`command.txt`, `mlp_train.yml` trong lúc chạy) và tab "Máy đã chạy gì"; canvas đầy đủ trên trang Run (mới là khung vòng huấn luyện); dừng cả nhóm tiến trình khi huỷ.

### Hiện trạng lát 5 (2026-11-21)

Chủ sản phẩm chốt: **"Train lại" = làm lại #n trên phiên bản dữ liệu khác** (giữ bản chốt và điểm xuất phát), đúng R5.

Đã hiện thực:
- **Train lại (R5).** Nút "Train lại từ #n" trên trang Run (chỉ run đã xong và có bản chốt). `ResetDraft` thêm `retrain`: nạp bản chốt của #n, đặt `train_kind = retrain`, `retrain_of_run_id`, bỏ trống phiên bản dữ liệu. Từ chối: run chưa xong (`TRACK_RETRAIN_NOT_FINISHED`), run không có bản chốt (`TRACK_RETRAIN_NO_REVISION`), công thức của project đã đổi từ sau #n (`TRACK_RETRAIN_RECIPE_CHANGED`). Khi đang Train lại: server khoá biến thể, tham số, điểm xuất phát (`TRACK_RETRAIN_LOCKED`); cổng ghi "cần phiên bản dữ liệu khác" nếu trùng #n; `StartTraining` chặn lần cuối (`TRACK_RETRAIN_SAME_DATA`) và ghi `retrain_of_run_id` cho run. Canvas: dải "Train lại từ #n" kèm nút "Chuyển sang Fine-tune", thẻ Train lại sáng ở node 3, ô sửa bị khoá. Vẫn phải chạy thử lại vì dữ liệu đổi (D-43).
- **So sánh (§8.6).** `CompareRuns`: "cùng tập kiểm định" = cùng phiên bản dữ liệu; "cùng trục" = điểm cuối của metric chính nằm sau số epoch (trục bước, R25) hay không (trục epoch, run cũ). Bảng chỉ gồm dòng khác nhau (công thức, biến thể, bản chốt, dữ liệu, điểm xuất phát, kiểu, GPU, từng tham số theo giá trị hiệu lực), nhóm theo ngăn. Hiện dưới biểu đồ ở tab Lần train.
- **`DiffRevisions`.** RPC trả các dòng khác nhau giữa hai bản chốt và cờ "công thức gốc đổi". Chưa có màn hình riêng (bảng so sánh run đã gồm phần này).
- **Recipe trong Catalog (§8.7).** Bốn tab: **Luồng** (canvas chỉ đọc dựng từ manifest: 6 node, các ngăn với tham số và mặc định, dải giai đoạn, khối model, điểm lưu, điều kiện dừng hẳn) · **Biến thể** (bảng + lệnh như trước) · **Toàn văn** (cây file chỉ đọc, dùng lại của lát 2) · **Đang dùng ở** (`ListRecipeUsage`: project, bản chốt mới nhất, số lần train, lần cuối). Nút chính "Dùng cho project…" (ngăn phải) → chọn project cùng bài toán → mở tab Công thức.
- **H1 bước 1.** Sau khi tạo phiên bản từ nhãn: ô "Train với phiên bản này" chọn project train cùng bài toán → mở `projects/{p}?tab=recipe&version={v}`; canvas đặt phiên bản đó vào nháp một lần rồi bỏ tham số khỏi URL.

Đã kiểm trên trình duyệt: so sánh #10/#9/#6/#5 hiện "khác tập kiểm định" và "khác trục x" (#5, #6 ghi theo epoch) và đúng các dòng khác (bản chốt, phiên bản dữ liệu, `freeze`, `epochs`); trang D-FINE trong Catalog đủ bốn tab; "Train lại từ #10" mở canvas khoá với dải thông báo; qua API: chọn lại đúng phiên bản của #10 → cổng chặn; sửa `epochs` khi đang Train lại → từ chối; Train lại từ #5 (trước khi có bản chốt) → từ chối đúng lý do; `?version=` đặt đúng phiên bản vào nháp.

**Chưa kiểm:** một lần Train lại chạy thật tới hết (cần phiên bản dữ liệu thứ hai đã chia tập + chạy thử + GPU); nút ở trang Gán nhãn bấm thật (không tạo phiên bản thử chỉ để kiểm; đã kiểm đích của nút).

## 13. Điểm còn mở và phần đã bớt so với thiết kế

**Cần chủ sản phẩm xác nhận**
1. ~~Định nghĩa "Train lại"~~ — **đã chốt 2026-11-21**: làm lại #n trên phiên bản dữ liệu khác, giữ công thức và điểm xuất phát. Train tiếp từ trọng số của #n là "Fine-tune từ model của tôi" ở đợt 3.

**Bớt so với thiết kế bản 2 (ghi lại để không mất)**
2. Một nháp mỗi người mỗi công thức (thiết kế cho nhiều nháp).
3. Tập con chạy thử chọn theo băm; chưa có "8 mẫu theo dõi" do người dùng ghim và "ảnh nhiều vật thể nhất".
4. Chưa có "Tập test của project" như một đối tượng riêng: đợt 1 chỉ tạo tập `test` cố định trong phiên bản; chấm theo tập ở đợt 3.
5. Không có "16 ảnh sau bộ chuyển" trước khi chạy thử: ảnh `train_samples`/`val_samples` chỉ có sau lần chạy thử đầu.
6. Thanh hậu quả hiện các khoá config đã đổi, chưa diễn giải thành câu ("tắt tăng cường dời từ epoch 64 sang 85").
7. Không có precision/recall **theo lớp** (repo D-FINE không in); chỉ có số toàn cục.
8. Không chồng loss của chạy thử lên loss thật: LR của chạy thử còn trong đoạn khởi động nên hai đường không so được.
9. Chưa kiểm license phía server (chưa có bảng xác nhận); giữ ô xác nhận trên giao diện như hiện có.

**Phải đo khi hiện thực [KIỂM]**
10. Thời gian chạy thử và độ ổn định của `steps_per_s` với tập con max(160, 10 × batch).
11. Thời gian `inspect.py` dựng model trên CPU.
12. Mọi con số thời gian trong tài liệu (10–30 phút dựng, ≤ 10 phút chạy thử) là ước lượng.
