# 08 — Kiểm các điểm [CHƯA CHẮC] trên repo D-FINE (2026-11-20)

Nguồn: `github.com/Peterande/D-FINE` ở đúng commit recipe đang ghim (`956d1709…`, xem `recipes/d-fine/Dockerfile`), cấu hình `dfine_hgnetv2_s_coco.yml`. **Chỉ đọc mã, chưa chạy.** Đường dẫn dưới đây là trong repo D-FINE trừ khi ghi khác. Các repo cùng họ (DEIM, RT-DETRv4) chưa kiểm.

Kết luận ngắn: không điểm nào bị bác. Bốn trên năm thứ "job soi" hứa làm được ngay; ảnh batch thì repo tự lưu sẵn sau mỗi lần chạy. Hai núm chỉnh nông (kích thước ảnh, bật/tắt từng phép tăng cường) cần sửa bộ dựng cấu hình vì chúng nằm trong danh sách. Có một phát hiện làm đổi hình vẽ: vòng huấn luyện của D-FINE có **hai giai đoạn**.

## A. Tên khoá cho chỉnh nông

| Núm trên giao diện | Khoá thật | Kết luận |
|---|---|---|
| LR đỉnh | `optimizer.lr` (S: 0.0002) | CÓ — khoá dạng dict, đi được qua `--extra-json` |
| LR của backbone | `optimizer.params[0].lr`, `[1].lr` (S: 0.0001, tức ×0,5) | CÓ, nhưng `optimizer.params` là danh sách: repo gộp cấu hình bằng cách thay cả danh sách (`src/core/yaml_utils.py:51-66`) nên phải gửi nguyên danh sách nhóm |
| Khoá backbone | `HGNetv2.freeze_at` (−1 = không khoá; k ≥ 0 khoá stem, và **chỉ khi `freeze_stem_only: false`** mới khoá thêm các stage 0…k — mặc định là `true`), `HGNetv2.freeze_norm` (`src/nn/backbone/hgnetv2.py:442, 494-501`) | CÓ. *Đính chính 2026-11-20: bản đầu bỏ sót `freeze_stem_only`; khoá cả backbone cần `freeze_at: 3`, `freeze_stem_only: false`* |
| Encoder/decoder học chậm | thêm nhóm regex vào `optimizer.params` | CÓ theo cùng cơ chế nhóm. Khoá hẳn encoder/decoder: repo không có khoá cấu hình |
| Kích thước ảnh | bốn chỗ phải đổi cùng lúc: `Resize` trong `train_dataloader…transforms.ops` và `val_dataloader…ops` (phần tử của danh sách), `train_dataloader.collate_fn.base_size`, `eval_spatial_size` | CẦN XÂY nhỏ: `make_config.py` phải biết sửa phần tử trong danh sách; một khoá `--extra-json` không đủ |
| Bật/tắt từng phép tăng cường | phần tử của `…transforms.ops` và `…policy.ops` | CẦN XÂY nhỏ, cùng lý do |
| Nhiều cỡ ảnh (multi-scale) | `train_dataloader.collate_fn.base_size_repeat` | CÓ |
| Khác (đều là khoá dict) | `clip_max_norm`, `use_ema`, `ema.decay`, `lr_warmup_scheduler.warmup_duration`, `checkpoint_freq`, `print_freq`, `DFINECriterion.weight_dict` (trọng số các thành phần loss) | CÓ |

"Train từ đầu": không truyền `-t` thì backbone vẫn nạp trọng số ImageNet có sẵn trong image; `train.py:62-64` chỉ tắt việc đó khi có `-t`/`-r`. Nhãn "chỉ giữ backbone đã học sẵn" là đúng. Từ đầu thật sự: `HGNetv2.pretrained: false`.

## B. Ba hình của đợt đầu

1. **Đường LR — CÓ, tính bằng công thức, không cần GPU.** Tăng tuyến tính trong 500 *bước* đầu (`src/optim/warmup.py`, `configs/dfine/include/optimizer.yml`), rồi **đi ngang**: `MultiStepLR` có mốc ở epoch 500 nên không bao giờ giảm. Hệ quả cho thiết kế: đường LR của D-FINE là một đoạn dốc rồi một đường thẳng — hình đáng vẽ hơn là dải giai đoạn (mục D). Với dữ liệu nhỏ, 500 bước khởi động kéo dài nhiều epoch (ví dụ 107 bước/epoch → gần 5 epoch); nên hiện con số này.
2. **Batch sau tăng cường — có sẵn sau mỗi lần chạy.** Repo tự lưu batch train đầu tiên và batch val đầu tiên thành ảnh đã vẽ khung vào `{output}/train_samples/` và `{output}/val_samples/` (`src/solver/det_engine.py:66-67, 187-188`; `src/misc/visualizer.py:17`). Không cần dựng dataloader riêng: chạy thử xong là có. CẦN XÂY: thêm hai thư mục này vào danh sách file shim tải lên. Trước lần chạy đầu tiên thì chưa có hình này.
3. **Sơ đồ khối và số tham số — CÓ với một script nhỏ chạy CPU trong image.** Model có đúng ba khối `backbone`, `encoder`, `decoder` (`src/zoo/dfine/dfine.py:29-31`); repo chỉ in tổng số tham số lúc bắt đầu (`src/misc/profiler_utils.py:11-30`). Đếm theo khối và theo "được học / bị khoá" phải tự viết; `train.py` có `--device` nên dựng trên CPU được. Chưa chạy thử.

## C. Log, điểm lưu, nạp trọng số

| Điểm | Kết quả đọc mã | Hệ quả |
|---|---|---|
| % và ETA | Mỗi `print_freq` bước (mặc định 100) in `Epoch: [e/E]  [i/n]  eta: …  lr: …  loss: …  time: …  data: …  max mem: …` (`src/misc/logger.py:190-203`) | % trong epoch, ETA và **VRAM đỉnh** đọc được từ log. Cần khai `progress` trong recipe và hạ `print_freq` |
| Đang train hay đang kiểm định | Lúc kiểm định in dòng `Test:  [i/n]` mỗi 10 bước | Phân biệt được hai pha → nhãn [CHƯA CHẮC] của "pha suy từ log" thành CÓ |
| Metric mỗi epoch | 12 số COCO vào `log.txt` và TensorBoard; ngoài ra in `Metrics: {f1, precision, recall, iou, TPs, FPs, FNs}` toàn cục, ngưỡng tin cậy 0,5 (`src/solver/validator.py:34-39`, `det_engine.py:232-233`) | Precision/recall/F1 **toàn cục** mỗi epoch có trong log. *Đính chính 2026-11-20: bản đầu ghi có số theo từng lớp — sai; `compute_metrics` xoá phần theo lớp trước khi in* |
| Loss val | Kiểm định gọi `model(samples)`, không qua criterion (`det_engine.py:193`) | Không có loss val — đúng như thiết kế đã rút |
| Trục của biểu đồ | `Loss/*`, `Lr/pg_j` ghi mỗi 10 bước theo *bước* (`det_engine.py:126-130`); `Test/*` ghi theo *epoch* (`det_solver.py:116`) | Loss và mAP đang nằm trên hai trục khác nhau; trang Metric phải quy đổi bằng số bước/epoch |
| Loss hỏng | Repo tự in `Loss is nan, stopping training` rồi thoát mã 1 (`det_engine.py:117-120`) | Lần train **đã tự dừng**. Việc còn lại: nền tảng nhận dòng này làm lý do lỗi và vẽ vạch trên đường loss |
| Điểm lưu | `last.pth` mỗi epoch, nhưng chỉ khi epoch < `stop_epoch`; `checkpoint{epoch:04}.pth` mỗi `checkpoint_freq` (12) epoch; `best_stg1.pth`, `best_stg2.pth` (`det_solver.py:93-99, 127-150`). Trong file có `last_epoch` (`_solver.py:207`) | Có sẵn bản mang số trong tên (recipe chưa khai); số bước thật đọc được từ nội dung file. Sự cố trong giai đoạn 2 chỉ chạy nốt được từ trước `stop_epoch` |
| Handler nạp điểm lưu giữa chừng | Handler đọc `ckpt["ema"]["module"]` hoặc `ckpt["model"]` (`recipes/_shared/detr_family/handler.py:45-46` trong repo nền tảng); điểm lưu của solver chứa cả hai | "Mẫu theo dõi qua từng epoch" làm được từ bản đã tải lên. Chưa chạy thử |
| Báo cáo nạp trọng số | In `Load model.state_dict, {'missed': […], 'unmatched': […]}` (`_solver.py:274`) | Có danh sách tầng thiếu/lệch, không có tổng; câu "khớp x/y tầng" phải tự đếm |
| Ghép lớp khi fine-tune | Hàm ghép chỉ biết ánh xạ Objects365 ↔ COCO (`_solver.py:321-336`). Với số lớp khác, tôi suy ra nó lỗi rồi rơi về nhánh "tầng lệch kích thước thì bỏ", tức đầu ra phân lớp được tạo mới — **suy luận, chưa chạy** | Bảng ghép lớp cũ → mới (đợt 3) phải tự xây hoàn toàn |
| Giới hạn số bước cho chạy khói | Không có cờ giới hạn số bước | Chỉ giới hạn được bằng số ảnh (tập con), như thiết kế đã ghi |

## D. Phát hiện làm đổi hình vẽ: hai giai đoạn

Tại epoch = `stop_epoch`, D-FINE **nạp lại bản tốt nhất của giai đoạn 1** (`best_stg1.pth`) và đặt lại EMA; từ đó tắt tăng cường mạnh và multi-scale. Trong giai đoạn 2, epoch nào không khá hơn thì nó lại nạp `best_stg1.pth` và giảm hệ số EMA (`det_solver.py:66-70, 152-159`). Tức giai đoạn 2 là một chuỗi thử lại từ bản tốt nhất, không phải train nối tiếp.

- Recipe mặc định 72 epoch trong khi cấu hình gốc của S là 132; bộ dựng co mốc theo tỉ lệ nên `stop_epoch` ≈ 65: giai đoạn 2 là 7 epoch cuối.
- Canvas nên vẽ vòng huấn luyện của D-FINE thành hai đoạn có tên (ví dụ "Học với tăng cường" và "Tinh chỉnh từ bản tốt nhất"), và dải thời gian chỉ rõ mốc chuyển.
- Chạy thử "lịch nén": `epochs=1` chỉ đi giai đoạn 1. Để đi qua nhánh nạp lại, cần `epochs=2` với `stop_epoch=1` và `policy.epoch=1` — cả ba là khoá dict, đặt được ngay.

## E. Đợt 1 giữ được gì

| Thứ đợt 1 hứa | Sau khi kiểm |
|---|---|
| Xem file công thức và cấu hình đã gộp | Giữ |
| Đường LR, dải giai đoạn | Giữ; vẽ thành dải hai giai đoạn + đoạn khởi động |
| Sơ đồ khối + số tham số | Giữ, cần script đếm |
| Batch sau tăng cường | Giữ, nhưng chỉ có **sau lần chạy thử đầu tiên** |
| Chỉnh LR đỉnh, khoá backbone, multi-scale, EMA, trọng số loss | Giữ, đi qua `--extra-json` |
| Chỉnh LR backbone | Giữ, gửi nguyên danh sách nhóm |
| Chỉnh kích thước ảnh, bật/tắt từng phép tăng cường | Giữ nếu mở rộng `make_config.py` để sửa phần tử danh sách |
| % và ETA, pha train/kiểm định, VRAM đỉnh | Giữ; đọc từ log |
| Tự dừng khi loss hỏng | Đã có ở repo; chỉ cần hiển thị đúng lý do |

Thứ đợt 1 nhận thêm mà thiết kế chưa tính: precision/recall/F1 toàn cục mỗi epoch, và điểm lưu có số mỗi `checkpoint_freq` epoch.

> Hai đính chính ở trên đến từ phản biện spec (`09-phan-bien-spec-dot-1.md`, mục 2 và 4), đã kiểm lại trong mã.
