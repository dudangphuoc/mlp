# 03 — Phản biện vận hành, độ tin cậy, bảo mật, chi phí

Ký hiệu: **[R]** đã đọc trong repo (kèm đường dẫn) · **[N]** thực tế nghề · **[S]** suy ra từ đọc mã, chưa chạy kiểm. Tôi không build, không chạy gì.

**Kết luận gộp:** đề xuất vẽ một canvas lên trên một nền chưa giữ được lời hứa "không mất vài ngày treo máy". Ba lỗ nền (checkpoint bền, NaN bị nuốt, run không chốt bản recipe/image) gây thiệt hại lớn hơn mọi thứ canvas giải quyết, và phải sửa trước.

## 0. Sự thật nền tìm thấy khi đọc mã

| # | Phát hiện | Bằng chứng |
|---|---|---|
| F1 | **Checkpoint bền hỏng với recipe ghi đè một file.** Step = số cuối trong tên file; `last.pth` → 0. Khoá S3 = `…/checkpoints/{step}/{name}` nên mọi lần upload trùng khoá. Bộ dọn giữ 3 bản theo `step DESC` rồi xoá khoá S3 của bản "cũ" — chính là khoá bản mới nhất. Từ checkpoint thứ 4, kho S3 không còn bản nào **[S]** | `Shim/Recovery.cs` (`StepOf`), `Application/Ops/Ops.cs:152`, `Ops2.cs` (`CheckpointRetention`), `Sql/Ops/ListCheckpoints.sql`; tên cố định `last.pth`/`last.pt`/`last.ckpt`/`model.joblib` ở d-fine, deimv2, rt-detrv4, rf-detr, ultralytics-yolo, tabular-gbdt |
| F2 | Recipe HF ghi `checkpoint-*` là **thư mục**; watcher lọc `File.Exists` → không bao giờ upload **[S]** | `Recovery.cs` (`CheckpointWatcher.Ready`); hf-text, hf-llm-sft, whisper, vibevoice |
| F3 | Khôi phục không kiểm sha256; hash tính trước khi PUT nên file bị ghi đè lúc đang upload vẫn được đăng ký; PUT một phát, không multipart (spec ghi multipart) **[R]** | `Recovery.cs`, `Runner.cs` (`RestoreCheckpointAsync`) |
| F4 | **NaN/Inf bị bỏ im lặng** — biểu đồ loss chỉ "đứng", không có sự kiện **[R]** | `Shim/Metrics.cs:80` |
| F5 | Không có ngân sách thời gian: `TimeoutSeconds = 86_400` nằm trong DTO, không thấy mã nào thực thi **[R]** | `Scheduling/JobSpecDto.cs:18` |
| F6 | Image recipe **build tại từng máy** từ Dockerfile có `pip install` không ghim; tag = băm *ngữ cảnh build*, không phải digest nội dung. Cùng tag, khác máy/khác ngày = khác gói. `_shared/` nằm trong băm của **mọi** recipe **[R]** | `Api/Infrastructure/HostContainers.cs:94-119`, `Scheduler.cs:193`, `recipes/d-fine/Dockerfile` |
| F7 | Run chỉ ghi tên recipe + variant + params; `recipe.lock.json` không có image digest, dataset version (D-28 yêu cầu digest) **[R]** | `db/track/*.sql`, `Runner.cs:433-441` |
| F8 | URL dữ liệu ký sẵn **24 giờ** lúc gửi job, nằm trong spec; không thấy chỗ ký lại khi tạo attempt mới **[R/S]** | `Data/DataHandlers.cs:163`, `Tracking/TrackHandlers.cs:114-162` |
| F9 | Dataset tải vào volume **của từng run** (`{output}/.mlp/data`), bỏ qua file đã có mà không kiểm hash **[R]** | `Runner.cs:167,192` |
| F10 | Container: root, không `--pids-limit`, không giới hạn đĩa, không chặn mạng ra; `MLP_JOB_TOKEN` và URL ký sẵn nằm trong env mà tiến trình người dùng thừa kế **[R]** | `Agent/Docker.cs:67-90`, `Scheduler.cs:225`, `Runner.cs:42-50` |
| F11 | Không có bảng `license_acks`, `recipe_builds`; `StartTraining` không kiểm license (D-23 chưa hiện thực) **[R]** | `db/catalog/202611170040_catalog.sql`, `TrackHandlers.cs` |
| F12 | Chia train/val nằm trong bộ chuyển, gieo bằng **seed của lần train** → đổi seed là đổi tập val **[R]** | `recipes/_shared/converters/to_coco.py:142-153`, `Runner.cs:269` |

Không recipe nào khai `resumeFidelity`, `checkpointEveryMin`, `stallAfterMin` **[R]**. Kịch bản nghiệm thu D-36 dùng recipe đồ chơi `ckpt-6.json` nên không lộ F1.

## 1. Kịch bản thất bại

| Kịch bản | Diễn biến → người dùng thấy → mất | Cần thiết kế |
|---|---|---|
| Chết ở giờ 30 | Máy mất điện quá 5 phút → `node_lost` → attempt mới ở máy khác: checkpoint 404 (F1), dữ liệu 403 (F8), image build lại 10–30 phút và có thể khác gói (F6). Thấy 3 attempt đỏ. Mất 30 giờ | Sửa F1–F3; khoá checkpoint theo số thứ tự upload; **diễn tập resume trong chạy khói**; ký lại URL mỗi attempt; chốt digest |
| Đĩa đầy | Recipe lưu mỗi epoch; volume run + dataset mỗi run (F9) + image 5 GB mỗi bản. Train chết `No space left`, kéo theo replica cùng máy. Cảnh báo đĩa <10 % ghi "còn lại" (20/10 §9) **[R]** | Ước tính đĩa trước khi chạy (checkpoint × số bản + dataset + image); agent từ chối khởi chạy dưới ngưỡng; cache dataset theo version dùng chung trên máy |
| Dataset đổi giữa chừng | Version `reference` chỉ lưu URL nguồn (`db/data/202611190800…`) **[R]**; nguồn đổi → attempt 2 học dữ liệu khác, không ai biết | Lưu sha256 từng file trong version, shim kiểm khi tải; lệch → dừng, nói rõ |
| GPU chung với deployment | GPU cấp nguyên chiếc; replica giữ vô hạn; preemption để P3 (M03) **[R]**. Máy một GPU: chạy khói "2–3 phút" xếp hàng vô thời hạn. Ray Client ngoài nền tảng (D-33) ăn VRAM → OOM oan | Trước khi bấm, nói ai đang giữ GPU; làn chạy khói ưu tiên; cho phép tạm dừng replica thử; chủ sản phẩm quyết ưu tiên |
| Hai người sửa một recipe | `catalog.recipes` một dòng, không revision → ghi sau thắng **[R]**; A chạy khói bản mình, B lưu đè, A "Train thật" bản của B | Revision bất biến có băm cha; lưu kèm băm đang thấy (sai → báo xung đột); nút Train gắn revision id |
| Gốc cập nhật sau fork | Nâng cấp nền tảng đổi `recipes/`; job vẫn trỏ `recipes/<tên>:current` → attempt 2 build image khác attempt 1 trong **cùng một run** **[S]**; fork không nhận bản vá | Fork ghi băm gốc; huy hiệu "gốc đã đổi" + diff; không gộp tự động; attempt sau dùng đúng digest attempt đầu |
| Máy offline rồi quay lại | D-36/D-40 xử lý tốt **[R]**; mục fencing chưa tick. Dữ liệu local-only (D-17): không chuyển máy được nhưng vẫn đốt 3 attempt | Local-only: chỉ "chờ máy", không `node_lost` |
| ETA sai gấp 3 | Khói đo vài chục ảnh: không có eval toàn tập, I/O thật, đổi giai đoạn (D-FINE tắt augment, stg2), nghẽn nhiệt **[N]** | Hiện **khoảng**; tính lại sau epoch thật đầu; lệch >50 % → thông báo kèm lựa chọn |
| Khói qua, thật hỏng | Xem mục 2 | Lưới an toàn lúc chạy |
| Code tự viết phá máy | F10: đọc `/mnt/data`, gọi cổng cùng namespace `mlp-net` (20/06 §8), gửi dữ liệu ra ngoài, ghi metric giả bằng token, fork-bomb, đào coin (GPU 100 % nên bộ phát hiện treo — cần GPU≈0 — không kêu) | Mục 4 |
| License | F11. Fork AGPL/"research-only" rồi lưu thành recipe workspace, model đi production không ai xác nhận | License **thừa kế bắt buộc** qua fork; chặn `promote_production` khi chưa xác nhận |
| Image × fork × máy | Sửa một dòng code = băm mới = image mới 5 GB mỗi máy; sửa `_shared/make_config.py` (đề xuất cần cho freeze/LR) → build lại cả họ DETR trên mọi máy. Agent không dọn image **[R]** | Tách **image môi trường** (ít đổi) khỏi **lớp mã** (mount/patch lúc khởi chạy); dọn image theo LRU |
| Sửa Dockerfile hỏng shim | Lớp nền tảng nối **sau** Dockerfile: cần `pip`, `python`, glibc, root; `pip install grpcio protobuf` không ghim có thể nâng đè gói recipe **[R]** (`HostContainers.cs:61-86`) | Người dùng chỉ sửa phần môi trường; kiểm hợp đồng sau build (`mlp-shim --selfcheck`, import handler, `pip check`) |

## 2. "Chạy thử qua mới được train thật" — an toàn giả

Chạy khói vài phút **không thể** bắt **[N]**:
1. Phân kỳ/NaN muộn (sau warmup, khi LR đạt đỉnh). Bộ dựng cấu hình co lịch học theo `epochs` → lần khói 1 epoch chạy **một lịch khác** lần thật (`d-fine/mlp-recipe.yaml`, mô tả `epochs`) **[R]**.
2. OOM ở batch lớn nhất, multi-scale; rò RAM, `/dev/shm` qua nhiều giờ.
3. Ảnh hỏng/nhãn lỗi nằm ngoài mẫu vài chục ảnh; lớp hiếm vắng trong val.
4. Chuyển giai đoạn: `last.pth` không được ghi sau `stop_epoch` (openItems của d-fine) **[R]**.
5. Đường resume, upload checkpoint lớn, upload model khi mạng rớt.
6. Khác máy/khác image (F6), đĩa, nhiệt, tranh GPU.
7. Hội tụ kém: 8 ảnh overfit tốt không nói gì về 50 000 ảnh.

Nút Train khoá theo "đúng bản đã chạy thử" còn chưa định nghĩa: đổi batch, đổi máy, đổi dataset version có làm mất hiệu lực không? Khoá chặt thì phiền; khoá lỏng thì giả.

| Lưới lúc chạy thật | Đã có | Cần thêm |
|---|---|---|
| Canh NaN/loss | Không (F4) | Shim phát `metric.nonfinite`; K lần liên tiếp → dừng, giữ checkpoint tốt cuối |
| Điểm kiểm sau epoch đầu | Không | Checkpoint đã bền **và nạp lại được**, metric val hữu hạn, tốc độ so với ước tính, ETA mới → một thông báo |
| Ngân sách thời gian | Không (F5) | Người dùng đặt; cảnh báo ở 1,2×; trần cứng tuỳ chọn |
| Tự dừng | Chỉ treo/mất tín hiệu (20/10 §2.3) | Dừng sớm theo plateau |
| Thông báo | stall, oom, signal_lost, recovering (20/10 §7) | NaN, lệch ETA, báo cáo epoch đầu |
| Tạm dừng → chạy tiếp | `ResumeJob` sau lỗi; `CancelJob` không bao giờ chạy lại (M03 R5) | "Tạm dừng" = SIGTERM, chờ checkpoint, nhả GPU |
| Rẽ nhánh từ checkpoint | Không; job xong chỉ giữ bản cuối + best | Run mới từ run X @ checkpoint N, đổi tham số, có cạnh lineage; ghim checkpoint |

Còn dở theo 20/10 §9: cột `attempt` + `superseded` cho metric, cảnh báo VRAM/đĩa, gợi ý batch khi OOM, cảnh báo `resumeFidelity: none` **[R]**.

## 3. Tái lập sau 6 tháng

Hôm nay: **không** (F6, F7, F12). M09 và M06 mô tả `recipe_build_id`, `env.image_digest`, cạnh `used_recipe` (repo + commit + digest) nhưng bảng thật không có **[R]**. Manifest có được chụp vào spec của job (`TrackHandlers.cs:147`), nhưng không thành revision tra cứu được.

Mỗi run phải ghi: revision recipe (băm manifest + file kèm + **bản vá code**), băm gốc của fork; commit repo; **digest image thật** và `pip freeze`; dataset version + băm danh sách file + bảng chia train/val **đã vật chất hoá**; sha256 trọng số xuất phát; seed; lệnh đã dựng (đã có `command.txt`); máy, GPU, driver; id lần chạy khói. Tái lập bit-giống-bit cần **lưu image vào registry** (D-30/D-31), tốn đĩa — chủ sản phẩm phải chọn mức.

## 4. Quyền và quản trị

Ma trận M01 §4.1 không có quyền `recipe.*`; M17 dùng `model.write` cho nhập/build recipe → mọi Editor đã chạy được mã tuỳ ý **[R]**. "Node code" đưa việc đó lên thành nút bấm.

- Tách quyền: xem; fork + sửa cấu hình (Editor); **sửa code/Dockerfile** (quyền riêng); **lưu thành recipe workspace** (Admin duyệt); bật máy nhận mã tuỳ biến (`fleet.manage`, theo máy).
- Revision đổi code chạy trên pool dùng chung hoặc máy có dữ liệu local-only của người khác → cần người thứ hai duyệt diff.
- Kỹ thuật: bỏ token và URL khỏi env tiến trình con; chạy không root, `--pids-limit`, hạn mức đĩa; mặc định chặn mạng ra khi train; audit ghi băm revision (M06 R7 cấm ghi nội dung).
- Nếu chủ sản phẩm coi cả nhóm là tin cậy: ghi thành quyết định D-xx như D-24, đừng để ngầm.

## 5. Chi phí: cắt gì, giữ gì

| Phần | Nhận định | Đề nghị |
|---|---|---|
| Canvas tự do kiểu n8n | Đắt nhất, lợi ít: một lần train là **một tiến trình** `train.py` | Cắt. Sơ đồ bước cố định theo recipe |
| Thực thi/tiến độ riêng từng node | **Tự mâu thuẫn**: Tăng cường/Model/Huấn luyện/Đánh giá cùng một lệnh; trạng thái từng node là dựng giả | Suy bước từ `ReportProgress` + log; chỉ dữ liệu, train, xuất là bước thật |
| Sửa mọi file repo trong trình duyệt | Kéo theo editor, lưu bản vá, image theo fork, duyệt bảo mật | Bản đầu: cấu hình + hook ở điểm khai báo. Sửa sâu: Notebook (M08) hoặc nhập fork git làm recipe workspace (M17 đã có luồng) |
| Gộp nhánh với gốc | Gộp ba chiều YAML + code | Cắt; huy hiệu + diff |
| Sơ đồ khối đóng băng/LR | Phải viết riêng từng họ; `make_config.py` chưa có | 3 preset cho DETR + `freeze` của Ultralytics |
| Lưu ngược thành recipe workspace | Cần license, duyệt, build | Sau |
| **Giữ, làm trước** | Revision bất biến; chạy khói có diễn tập resume; lưới lúc chạy | Rẻ, đúng nỗi đau "vài ngày treo máy" |

Mâu thuẫn với sổ quyết định: **D-28** (lock file thiếu digest); **D-30/31** (image qua registry theo digest, thực tế build tại máy; fork nhân vấn đề lên); **D-39** (node Đánh giá trong canvas là eval của repo, điểm chính thức do nền tảng tính → hai con số mAP); **D-25** (bảy node giả định bài toán ảnh; LLM, TTS, GBDT không có Tăng cường); **D-41** (canvas + editor + revision là module mới); **D-20** (recipe = repo@commit; sửa file gốc cần khái niệm bản vá); **D-17** (chạy khói phải chạy trên máy giữ dữ liệu; số đo không chuyển sang máy khác).

Thứ tự có lợi sớm nhất: (0) vá F1–F5, F8 + diễn tập resume; (1) recipe xem được + lớp cấu hình có diff + run chốt revision và digest; (2) chạy khói hai tầng + ước tính dạng khoảng + kiểm đĩa; (3) lưới lúc chạy, tạm dừng, rẽ nhánh; (4) ba kiểu train + preset; (5) hook code qua lớp phủ; (6) canvas đẹp hơn; (7) lưu ngược, chia sẻ.

## 6. Câu hỏi chủ sản phẩm phải trả lời (xếp theo mức chặn)

1. Mô hình tin cậy: mọi Editor được chạy mã tuỳ ý trên máy dùng chung, cạnh dữ liệu của người khác — chấp nhận (ghi D-xx) hay phải cách ly?
2. "Sửa từng dòng code" = cấu hình + hook, hay mọi file repo trong trình duyệt?
3. Mức tái lập: cùng revision + dữ liệu + seed, hay image giống hệt sau 6 tháng (lưu image, tốn đĩa)?
4. Train/chạy khói có được giành GPU của replica thử, của deployment không?
5. Cổng chạy thử cứng hay vượt được kèm lý do? Thay đổi nào làm mất hiệu lực?
6. Ngân sách đĩa: giữ bao nhiêu checkpoint mỗi run, bao lâu, ai dọn?
7. Fork recipe AGPL/phi thương mại: ai được chia sẻ, ai duyệt lên production?
8. Bản đầu phủ recipe nào — chỉ họ DETR + YOLO, hay "bất kỳ" như D-25?

## 10 điều quan trọng nhất

1. **Checkpoint bền không dùng được cho cả 11 recipe có khai `checkpoints`** (F1, F2; tên file không mang số hoặc là thư mục) — lời hứa "chuyển máy, chạy tiếp" hiện chỉ đúng với recipe đồ chơi. Kiểm bằng một kịch bản D-36 với `last.pth` trước mọi việc khác.
2. **NaN bị nuốt** (F4) + không ngân sách thời gian (F5): người dùng vẫn "mù" đúng chỗ đắt nhất, canvas không chữa được.
3. **Chạy khói là điều kiện cần, không đủ**; thiếu lưới lúc chạy thì nút "Train thật" tạo niềm tin sai. Thêm diễn tập resume vào chạy khói.
4. **Run không chốt bản recipe và image** (F6, F7); `:current` cho phép hai attempt của một run chạy hai image khác nhau.
5. **Mã tuỳ ý + F10** = rò dữ liệu/token, phá máy, đào coin không bị phát hiện; cần quyền riêng và quyết định tin cậy rõ ràng.
6. **Sửa code ⇒ image mới 5 GB × mỗi máy, build 10–30 phút**: mâu thuẫn với "chạy thử vài phút"; phải tách môi trường khỏi mã trước khi mở sửa code.
7. **Tiến độ theo node là giả** khi train là một tiến trình; canvas tự do và gộp nhánh là phần đắt, nên cắt.
8. **URL dữ liệu 24 giờ + dataset theo từng run** (F8, F9): job xếp hàng lâu hoặc chuyển máy sẽ hỏng; đĩa đầy nhanh.
9. **GPU nguyên chiếc, chưa preemption**: trên máy một GPU đang serve, "chạy thử 2–3 phút" là chờ vô hạn.
10. **License chưa được thực thi** (F11) và chia val theo seed (F12): fork/chia sẻ làm sai license lan đi; so sánh run khác seed là so trên hai tập val.
