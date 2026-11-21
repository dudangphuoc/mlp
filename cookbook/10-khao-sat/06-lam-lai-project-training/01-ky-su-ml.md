# 01 — Phản biện từ góc kỹ sư ML thực chiến

Nhãn nguồn: **[R]** đã kiểm trong repo (kèm đường dẫn) · **[N]** kinh nghiệm nghề · **[S]** suy đoán, cần kiểm khi build.
Mốc chi phí dùng trong bài: D-FINE-S 12 phút/epoch trên RTX 3060 (đề bài) × 72 epoch mặc định [R `recipes/d-fine/mlp-recipe.yaml`] ≈ **14,5 giờ máy** cho một lần chạy.

## 1. Quy trình thực tế (dựng trước khi đọc đề xuất)

| Giai đoạn | Quyết định của người dùng | Phải NHÌN THẤY | Lỗi hay gặp → giá nếu phát hiện muộn | Thử rẻ |
|---|---|---|---|---|
| A. Tiêu chí đạt | Metric chính, ngưỡng nghiệm thu, thiết bị và độ trễ mục tiêu | Mức của model hiện tại (baseline) trên cùng tập test | Train xong mới biết model quá nặng cho thiết bị → mất cả lần chạy | Đo độ trễ của biến thể pretrained **trước** khi train (vài phút) |
| B. Kiểm dữ liệu, nhãn | Sửa, loại hay chấp nhận | Lưới ảnh có nhãn vẽ lên; phân bố lớp; kích thước vật thể so với ảnh; ảnh không nhãn; hộp ngoài biên; ảnh trùng/gần trùng; với audio: thời lượng, tần số lấy mẫu; với văn bản: độ dài token | Nhãn lệch id lớp, hộp sai hệ toạ độ → mAP ≈ 0, chỉ thấy sau epoch đầu (12 phút) hoặc tệ hơn là mAP thấp "có vẻ hợp lý" suốt 14 giờ | Kiểm tĩnh + xem 16 ảnh sau bộ chuyển định dạng (vài giây) |
| C. Chia tập | Chia theo gì (ngẫu nhiên, theo nhóm: camera/ngày/bệnh nhân/người nói), tỉ lệ, test bất biến | Số mẫu từng lớp ở từng tập; số nhóm bị xẻ đôi; số ảnh gần trùng nằm hai bên | Rò rỉ train/val (khung hình liền nhau của một video) → val đẹp giả, hỏng khi chạy thật: mất **cả dự án**, không chỉ một lần chạy | So băm cảm quan giữa các tập (CPU, vài phút) |
| D. Tiền xử lý, kích thước vào | Kích thước ảnh, giữ tỉ lệ hay kéo giãn, chuẩn hoá; tokenizer, độ dài ngữ cảnh; 16 kHz | Vật thể nhỏ nhất còn mấy pixel sau khi thu nhỏ; bao nhiêu % mẫu bị cắt cụt | Vật thể < 8 px sau resize → không bao giờ học được; tiền xử lý lúc serve khác lúc train → rơi điểm khi triển khai | Biểu đồ kích thước vật thể tại kích thước vào đã chọn |
| E. Tăng cường | Bật/tắt, cường độ, lúc nào tắt (mosaic/mixup tắt ở cuối) | **Batch thật sau tăng cường, có nhãn** | Lật ngang với lớp trái/phải, đổi màu với lớp phân biệt bằng màu, crop làm mất vật thể | Hiện 2 batch (vài giây, CPU) |
| F. Model, điểm xuất phát | Biến thể, trọng số bắt đầu, phần nào học | Lớp nào khớp/không khớp khi nạp trọng số; head nào tạo mới; số tham số được học | Nạp trọng số "êm" nhưng 100 % khoá không khớp → thực chất train từ đầu | Báo cáo nạp trọng số (khớp/thiếu/thừa) trong chạy khói |
| G. Tối ưu | LR, warmup, lịch, batch/tích luỹ gradient, EMA, AMP, clip | Đường LR theo bước **của đúng lần chạy này**; số bước mỗi epoch | LR quá cao → phân kỳ ngay sau warmup; warmup tính theo iteration dài hơn cả lần chạy trên tập nhỏ; AMP fp16 sinh NaN | LR range test hoặc overfit một batch |
| H. Vận hành | Tần suất checkpoint, dừng sớm, số GPU, ngân sách giờ | "Sập lúc này mất X phút"; ETA; dung lượng đĩa cần | Checkpoint không đủ trạng thái (thiếu EMA/optimizer) → resume làm tụt điểm; đầy đĩa giờ thứ 10 | Kill giữa chạy khói rồi resume |
| I. Đánh giá, phân tích lỗi | Model có đạt không, hỏng ở đâu | Metric theo lớp, ma trận nhầm lẫn, ảnh sai nặng nhất, sai theo lát cắt (kích thước, camera, ngày/đêm) | Chỉ nhìn một con số → triển khai model hỏng ở lớp hiếm | Eval sớm trên 50–100 mẫu val ngay phút đầu |
| J. Ngưỡng, hiệu chỉnh | Điểm vận hành (ưu tiên bỏ sót hay báo nhầm) | Đường P–R, F1 theo ngưỡng, ngưỡng theo lớp, biểu đồ độ tin cậy | Dùng ngưỡng 0,25 mặc định cho mọi bài | Quét ngưỡng trên kết quả eval sẵn có (giây) |
| K. Xuất, lượng tử hoá | Định dạng, FP16/INT8, kích thước cố định | Chênh metric PyTorch ↔ ONNX/TensorRT; độ trễ p50/p95 trên **thiết bị đích** | Xuất lỗi op ở cuối lần chạy 14 giờ; INT8 rơi 5 điểm mAP không ai đo | Xuất checkpoint khởi đầu trong chạy khói |
| L. So sánh, HPO, tái lập | Giữ thí nghiệm nào | Diff cấu hình + dữ liệu + mã; nhiễu giữa các seed | So hai run trên hai tập val khác nhau; chênh 0,3 mAP chỉ là nhiễu seed | Chạy 2 seed ở lịch ngắn |
| M. Chạy thật, vòng dữ liệu, train lại | Khi nào train lại, lấy mẫu nào gán nhãn | Mẫu model kém tự tin/sai trên dữ liệu mới; so bản mới với bản đang chạy trên **cùng tập test vàng** | Bản mới tốt trên dữ liệu mới nhưng tụt trên trường hợp cũ | Cổng so sánh trước khi thay bản |

Đề xuất bảy node bỏ hẳn A, B, J, L, M; gộp D vào E; coi I là bước chạy xong là hết.

## 2. Bảy kiểu train — thuật ngữ và hệ quả

"Ba kiểu ở node Model" trong đề xuất gộp những việc khác nhau về bản chất và thiếu bốn kiểu.

| Kiểu | Trọng số thay đổi / trạng thái nạp lại | LR, lịch | Head, ánh xạ lớp | Quên kiến thức cũ / trộn dữ liệu cũ |
|---|---|---|---|---|
| **Train từ đầu** | Tất cả, khởi tạo ngẫu nhiên | LR đầy đủ, lịch dài (gấp 3–10 lần fine-tune) | Mới toàn bộ | Không áp dụng. Hiếm khi đúng nếu < 100 nghìn mẫu |
| **Fine-tune** từ pretrained | Tất cả hoặc đóng băng backbone; optimizer mới | LR thấp hơn 3–10 lần; backbone × 0,1; warmup ngắn | Head phân lớp tạo lại nếu số lớp khác | Quên kiến thức pretrained là chấp nhận được |
| **Resume** (chạy nốt) | Nạp đủ trọng số + optimizer + lịch + EMA + scaler AMP + RNG + epoch | **Không đổi gì** | Không đổi | Không. Chỉ để phục hồi sự cố; vẫn là cùng một run |
| **Train tiếp / nhánh từ checkpoint** | Chỉ nạp trọng số; optimizer và lịch mới | LR đỉnh thấp hơn lần trước + warmup (khởi động lại ở LR đỉnh sẽ phá cái đã học) | Giữ | Thấp nếu cùng dữ liệu. Là run **mới**, có cha |
| **Retrain với dữ liệu mới** | (a) từ pretrained gốc trên cũ + mới, hoặc (b) khởi động ấm từ bản đang chạy | (a) như fine-tune; (b) lịch ngắn 1/3, LR thấp | Giữ nếu lớp không đổi | (b) tích luỹ thiên lệch qua các đời, khó tái lập; cả hai **phải** có dữ liệu cũ và tập test vàng cố định |
| **Học tăng dần** (chỉ dữ liệu mới, hoặc thêm lớp) | Từ model cũ | LR thấp, thường đóng băng phần đầu | Giữ hàng của lớp cũ trong head, tạo hàng cho lớp mới; lớp gộp/xoá phải ánh xạ tường minh | **Quên thảm hoạ** nếu không phát lại 20–50 % dữ liệu cũ hoặc chưng cất từ model cũ |
| **Chưng cất** | Student học; teacher đóng băng | Thêm loss theo teacher; tốn VRAM và thời gian chạy teacher | Student có head riêng | Dùng được dữ liệu không nhãn; mục đích là nén để triển khai |
| **PEFT/LoRA** | Base đóng băng; chỉ ma trận adapter (0,1–1 % tham số) | LR cao hơn (cỡ 1e-4 – 2e-4) | Không đụng head; chọn module gắn adapter, rank | Quên thấp; sản phẩm là adapter + tham chiếu base |

Đối chiếu repo:
- [R] `StartRunCommand` chỉ có cờ `FromScratch` và trọng số của biến thể (`TrackHandlers.cs:66,143–150`); chưa có "bắt đầu từ model của tôi" (`M09-tracking.md:149` cũng ghi vậy). Bốn kiểu giữa bảng chưa tồn tại.
- [R] "Từ đầu" của D-FINE không phải từ đầu: `fromScratch: true # no -t: train from the pretrained backbone bundled in the image`. Nhãn UI phải nói đúng: "chỉ backbone đã học sẵn".
- [R] Resume của Ultralytics là lệnh `yolo train resume model=last.pt`; [N] lệnh này lấy lại tham số trong checkpoint, bỏ qua tham số mới. HF Trainer cũng nạp lại trạng thái lịch học. Hệ quả: **"tạm dừng – sửa – chạy tiếp" không thể làm bằng resume**; phải là "nhánh từ checkpoint".
- [R] d-fine, deimv2, rt-detrv4 chỉ lộ `epochs, batch, seed, use_amp` — không LR, kích thước ảnh, đóng băng, dừng sớm; `make_config.py` có `--extra-json` nhưng lệnh recipe không truyền. "Sơ đồ khối đóng băng/LR riêng" hiện chỉ có đường xuống mã ở ba chỗ: `rf-detr.lr_encoder`, `whisper.freeze_feature_encoder`, LoRA.

**Câu hỏi cho người không chuyên** (nền tảng tự suy ra kiểu):
1. Lần chạy trước bị dừng giữa chừng và bạn chỉ muốn chạy nốt, không đổi gì? → resume.
2. Đã có model của bạn cho đúng bài này chưa? Chưa → fine-tune.
3. Danh sách lớp có đổi không (thêm, bỏ, gộp, đổi tên)? → bảng ánh xạ, tạo lại phần head tương ứng.
4. Dữ liệu lần này gồm cả cũ lẫn mới, hay chỉ phần mới? Dữ liệu cũ còn dùng được không?
5. Model mới còn phải làm tốt các trường hợp cũ không? (gần như luôn "có" → bắt buộc trộn dữ liệu cũ, cảnh báo đỏ nếu không có)
6. Có bao nhiêu mẫu có nhãn? (< 500 → đóng băng backbone hoặc LoRA; hàng trăm nghìn + miền lạ → mới cân nhắc từ đầu)
7. Chạy trên thiết bị nào, cần nhanh cỡ nào? → kích cỡ biến thể, có cần chưng cất/lượng tử hoá.

## 3. Bảy node đối chiếu với 15 recipe thật

Sai lệch gốc [N]: lúc chạy, "Tăng cường → Model → Huấn luyện → Đánh giá" **không nối tiếp**. Chúng là một tiến trình duy nhất, lặp theo epoch; đánh giá val nằm trong vòng lặp. [R] Mọi recipe có train đều là **một lệnh CLI** (`yolo … train`, `trl sft`, `train.py -c`). Vẽ bốn node nối tiếp thì cả bốn cùng "đang chạy" suốt 14 giờ, người dùng vẫn mù. Canvas đúng là: Chuẩn bị dữ liệu → **[Vòng huấn luyện: batch → loss → cập nhật; mỗi epoch: val + checkpoint]** → Đánh giá test → Xuất → Đóng gói; bốn "node" kia là bốn ngăn cấu hình của khối vòng lặp. "Node code chèn giữa luồng" cũng không có chỗ cắm: với repo đóng gói chỉ có ba điểm sửa thật — bộ chuyển dữ liệu, file cấu hình sinh ra, và bản vá lên mã repo (cần gắn đè lúc chạy; build lại image mất 10–30 phút).

| Họ (recipe) | Khớp | Node thừa | Node thiếu |
|---|---|---|---|
| Phát hiện/phân vùng: d-fine, deimv2, rt-detrv4, rf-detr, ultralytics-yolo (5) | Tốt nhất, bảy node sinh ra từ họ này | — | Kiểm dữ liệu; kích thước vào; lịch giai đoạn (tắt tăng cường ở `stop_epoch`); ngưỡng; kiểm tương đương sau xuất. rt-detrv4/deimv2 cần file teacher DINOv3 do Admin tải lên [R openItems] → cần node "điều kiện tiên quyết" |
| Văn bản: hf-text | Một phần | Tăng cường | Tokenizer, `max_seq_length` (bao nhiêu % bị cắt), cân bằng lớp |
| LLM: hf-llm-sft | Kém | Tăng cường, Chia tập kiểu ảnh; [R] **không có `eval`, không có `export`** | Mẫu prompt/chat template, độ dài ngữ cảnh, `packing`, LoRA (rank, module đích — [R] không lộ), lượng tử hoá 4-bit, **bộ câu hỏi thử sinh văn bản**, gộp adapter. [R] `primary: val/loss` — loss không nói model trả lời đúng hay không |
| ASR: whisper, vibevoice-asr | Một phần | Tăng cường ảnh | Tần số lấy mẫu, lọc thời lượng ([R] `max_duration_in_seconds: 30`; [N] mẫu dài hơn bị loại lặng lẽ), chuẩn hoá văn bản trước khi tính WER (hoa/thường, dấu câu, số), ngôn ngữ. [R] vibevoice-asr không có eval, `primary: train/loss` |
| TTS: vibevoice-tts | Kém | Tăng cường, Đánh giá bằng số | Giọng mẫu, **nghe thử** theo checkpoint ([R] openItems: không có metric tự động), `predict` thay cho `eval` |
| Bảng: tabular-gbdt | Kém | Tăng cường, sơ đồ khối đóng băng, trọng số pretrained | Kỹ thuật đặc trưng, xử lý thiếu/biến phân loại, **kiểm định chéo** ([R] `train.py` chỉ dùng một tập val), rò rỉ theo thời gian, độ quan trọng đặc trưng, hiệu chỉnh xác suất |
| Chỉ suy luận: sam2, hf-auto, generic-onnx, vibevoice-realtime (4) | Không | Năm trong bảy node | Chỉ còn Model → Kiểm → Thử dự đoán |

Kết luận: bảy node cố định chỉ đúng cho 5/15 recipe. Danh sách node phải **do recipe khai báo** (trường `stages` mới trong manifest), nền tảng chỉ cố định khung ngoài.

## 4. Chạy thử ba tầng

**Bắt được:** thiếu gói (`pycocotools`), sai đường dẫn/định dạng, sai template lệnh, số lớp lệch head, OOM ở batch thông thường, lỗi ghi checkpoint, lỗi xuất, lỗi nạp lại handler. Nhóm lỗi "chết sau 20 phút" — đáng làm.

**Không bắt được:**
1. **Phân kỳ muộn**: sau warmup, hoặc tại lúc đổi giai đoạn tăng cường.
2. **Quá khớp, chạm trần, val dao động**: chỉ lộ sau nhiều epoch.
3. **OOM ở mẫu hiếm**: ảnh rất nhiều vật thể, ảnh lớn, audio/văn bản dài nhất. Vài chục mẫu ngẫu nhiên gần như chắc chắn không trúng. Sửa: mẫu chạy khói phải **cố ý gồm ca xấu nhất** (nhiều nhãn nhất, lớn nhất, dài nhất) và báo đỉnh VRAM + 15–20 % dự phòng.
4. **Eval ở kích thước thật**: thời gian và bộ nhớ của eval toàn tập val (Whisper sinh văn bản lúc eval có thể lâu hơn train).
5. **Nghẽn nạp dữ liệu, nhiều GPU**: chạy khói 1 GPU không lộ lỗi DDP (batch không chia hết số GPU, eval ở rank 0).
6. **Dữ liệu xấu**: rò rỉ, nhãn sai, mất cân bằng — chạy khói vẫn xanh.
7. **Resume**: đề xuất không có bước kill rồi chạy tiếp. [R] D-FINE/DEIMv2/RT-DETRv4 ghi rõ `last.pth` chỉ được lưu trước `stop_epoch`; không recipe nào khai `resumeFidelity`, `progress`, `checkpointEveryMin` (grep 0/15), dù spec 20/08 §3 ghi `resumeFidelity` bắt buộc khi có `checkpoints`.
8. **Ánh xạ metric rỗng**: [R] cả 15 recipe `built: false, smokeTested: false`, nhiều openItems "khoá metric chưa xác minh". Chạy khói phải **fail** khi metric chính không xuất hiện, không chỉ khi tiến trình lỗi.

**Ba lỗ hổng thiết kế:**
- "Chỉ mở Train thật khi đúng bản đó đã chạy thử" là ảo: bản chạy khói buộc phải khác số epoch và dữ liệu. [R] `make_config.py` co giãn lịch theo tỉ lệ epoch (`max(1, round(...))`) nhưng không đụng tới warmup, EMA và mốc LR; [N/S] ở họ D-FINE các giá trị này tính theo iteration (cần kiểm trên commit ghim) → tập nhỏ có thể warmup suốt cả lần chạy và EMA cho mAP ≈ 0 ở những epoch đầu. Phải định nghĩa rõ trường nào được khác, băm phần còn lại, và **vẽ lịch LR thật của lần chạy dài**.
- "Overfit 8 ảnh" phải tắt tăng cường và eval bằng trọng số thường (không EMA), kèm tiêu chí đạt theo họ; DETR cần vài trăm bước, 2–3 phút có thể chưa đủ. Nếu không, người dùng thấy loss không giảm và kết luận sai.
- Ước tính thời gian từ vài bước đầu lệch: chưa gồm thời gian eval, lần build image đầu (10–30 phút), tốc độ khác nhau giữa các giai đoạn tăng cường. Hiện khoảng ước tính, không hiện một con số.

**Cần thêm trong lúc chạy thật:**
- **Eval sớm**: sau ~200 bước, đánh giá 50–100 mẫu val và hiện ảnh dự đoán. Bắt được phần lớn ca "train hai ngày, mAP = 0" trong 5 phút thay vì chờ hết epoch đầu.
- **Chốt chặn tự động**: loss NaN/Inf; loss tăng quá k lần mức thấp nhất; metric chính = 0 sau 3 lần eval; val tệ dần trong khi train vẫn giảm; tốc độ tụt; GPU nhàn (nghẽn dữ liệu); đĩa sắp đầy. [R] Hiện `Metrics.cs:80` **bỏ lặng lẽ mọi giá trị không hữu hạn** → loss NaN biến mất khỏi biểu đồ, người dùng thấy đường đứng yên và máy vẫn chạy. Đây là lỗi phải sửa trước tiên.
- **Ba hành động tách bạch**: *Chạy nốt* (resume, cùng run, không đổi gì) · *Tách nhánh từ checkpoint N* (run mới có cha, sửa được LR/tăng cường/dữ liệu, biểu đồ nối vào run cha) · *Dừng và giữ bản tốt nhất* (đăng ký checkpoint best ngay). "Thêm epoch" thuộc loại tách nhánh vì lịch cosine đã về 0.
- **Chính sách checkpoint nhìn thấy được**: last + best + mỗi ≤ 30 phút, giữ K bản; hiện "sập bây giờ mất X phút".
- **Dừng sớm và ngân sách giờ máy**. [R] YOLO mặc định `patience: 100` bằng `epochs: 100` → dừng sớm thực tế tắt; HPO (M10) mới có grid/random, chưa cắt trial kém.

## 5. Người dùng cần thấy gì khi train

Chung cho mọi họ: loss train và val trên cùng một trục; đường LR; metric chính kèm vạch "mức pretrained/baseline" và "mức nghiệm thu"; tốc độ, ETA, VRAM; mốc checkpoint trên trục thời gian; **kết quả dự đoán trên cùng một bộ mẫu cố định qua từng lần eval** (thứ duy nhất người không chuyên đọc được).

| Họ | Thêm riêng | Dấu hiệu hỏng |
|---|---|---|
| Phát hiện/phân vùng | Loss thành phần (hộp, lớp), mAP50 và mAP50-95, P/R, AP theo lớp, cùng 8–16 ảnh val có hộp dự đoán | mAP = 0 sau 3 epoch; loss lớp giảm nhưng loss hộp đứng; một lớp AP = 0 |
| Phân loại ảnh/văn bản | Accuracy, F1 macro, ma trận nhầm lẫn theo epoch | Accuracy bằng tỉ lệ lớp đông nhất (model đoán một lớp) |
| LLM SFT | Loss train/val, **câu trả lời sinh ra cho 5–10 prompt cố định** ở mỗi lần eval, % mẫu bị cắt | Val loss tăng lại (thuộc lòng); câu trả lời lặp, không dừng |
| ASR | WER/CER, cặp "bản chuẩn ↔ bản nhận dạng" tô chỗ sai | WER > 100 % (lặp/ảo giác); WER cao chỉ vì dấu câu, hoa thường |
| TTS | Loss thành phần, **file nghe thử** mỗi checkpoint | Không có số nào thay được tai nghe |
| Bảng | Metric theo vòng boosting cho train và val, vòng tốt nhất, độ quan trọng đặc trưng | Một đặc trưng chiếm gần hết độ quan trọng (rò rỉ nhãn) |

Metric phải kèm một câu lời thường, ngay cạnh con số:
- **Loss**: "mức sai trên dữ liệu đang học; phải giảm dần. Chỉ so trong cùng một lần chạy."
- **Loss val tăng trong khi loss train giảm**: "model bắt đầu học thuộc — nên dừng."
- **mAP50-95**: "điểm tổng hợp 0–1: tìm đủ vật thể và khoanh sát tới đâu; 0,5 trở lên thường dùng được."
- **Precision / Recall**: "trong 100 lần báo, bao nhiêu lần đúng" / "trong 100 vật thể thật, tìm ra bao nhiêu".
- **F1 macro**: "điểm trung bình giữa các lớp, lớp hiếm tính ngang lớp đông".
- **WER**: "số từ sai trên 100 từ; càng thấp càng tốt".
- **LR**: "bước chỉnh mỗi lần học; tăng lúc đầu rồi giảm là bình thường".

## 6. Phát hiện khác trong repo có hệ quả trực tiếp

- [R] Với recipe đọc `coco` (d-fine, deimv2, rt-detrv4), `Runner.ConvertDataAsync` chạy `to_coco.py` **trong container train** với `--seed` lấy từ tham số `seed` của lần train; khi dataset không có sẵn val, script cắt ngẫu nhiên 15 % theo từng ảnh. Hệ quả: đổi seed train là đổi luôn tập val → hai run không so được; không chia theo nhóm; phép chia không được ghi vào dataset version. Spec 20/08 §5.1 lại mô tả job `convert` riêng có cache — mã và spec lệch nhau. Node "Chia tập" phải ghi kết quả vào dataset version, không nằm trong lần chạy.
- [R] Đánh giá của nền tảng (D-39, `Registry/Evaluations.cs`) chọn mẫu bằng `PathFilter` + `MaxSamples`, không theo split, và mới hỗ trợ detect/classify. Cộng với điểm trên: rất dễ chấm điểm trên ảnh đã dùng để train.
- [R] `hf-llm-sft`: `learning_rate: 2e-5` đi cùng `use_peft: true` mặc định; [N] mức này là của fine-tune toàn bộ, LoRA thường cần cao hơn khoảng 5–10 lần → chạy mặc định có thể gần như không học. `max_length: 1024` cắt lặng lẽ.

## 10 điều quan trọng nhất (xếp theo mức ảnh hưởng)

1. **Bảy node nối tiếp mô tả sai cách train chạy.** Huấn luyện là một vòng lặp trong một tiến trình; vẽ thành khối vòng lặp có ngăn cấu hình, và cho recipe tự khai danh sách giai đoạn (chỉ 5/15 recipe khớp bảy node).
2. **Thiếu hẳn giai đoạn kiểm dữ liệu, nhãn và rò rỉ.** Đây là nguồn mất thời gian lớn nhất và không tầng chạy thử nào bắt được. Đặt trước mọi thứ, chạy trên CPU.
3. **Phép chia tập đang nằm trong lần train và phụ thuộc seed train** (`Runner.cs` + `to_coco.py`). Chuyển về dataset version, hỗ trợ chia theo nhóm, giữ tập test vàng bất biến.
4. **NaN bị nuốt** (`Metrics.cs:80`) và chưa có chốt chặn nào trong lúc chạy. Thêm phát hiện NaN/phân kỳ/metric = 0/quá khớp, có quyền tự dừng theo cấu hình.
5. **Eval sớm + bộ ảnh mẫu cố định qua các lần eval.** Rẻ nhất, cứu nhiều giờ máy nhất, và là thứ người không chuyên hiểu được.
6. **Tách ba hành động: chạy nốt / tách nhánh từ checkpoint / dừng giữ bản tốt nhất.** "Tạm dừng – sửa – chạy tiếp" bằng resume không khả thi trên Ultralytics và HF.
7. **Bảy kiểu train, không phải ba**; hỏi người dùng bảy câu đời thường rồi nền tảng chọn. Retrain không trộn dữ liệu cũ phải có cảnh báo quên kiến thức. Sửa nhãn "từ đầu" của D-FINE.
8. **Chạy khói phải gồm ca xấu nhất, bước kill–resume, kiểm metric chính có xuất hiện, và vẽ lịch LR của lần chạy thật**; định nghĩa rõ phần nào của cấu hình được phép khác giữa bản thử và bản thật.
9. **"Điều chỉnh trọng số" chưa có đường xuống mã**: d-fine/deimv2/rt-detrv4 chỉ lộ 4 tham số, không LR, không kích thước ảnh, không đóng băng. Lộ tham số và truyền `--extra-json` trước khi vẽ sơ đồ khối.
10. **Sau train còn bốn bước đề xuất bỏ sót**: phân tích lỗi theo lớp/lát cắt, chọn ngưỡng, kiểm tương đương và đo độ trễ sau xuất trên thiết bị đích, so với bản đang chạy trên cùng tập test. Thiếu chúng, "model chạy thật" vẫn là phép thử mù.
