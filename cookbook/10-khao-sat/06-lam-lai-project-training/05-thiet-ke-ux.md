# 05 — Thiết kế UX: Project · Recipe · Chạy thử · Train

Nhãn nguồn: **[R]** tôi đã đọc trong repo · **[01]…[04]** theo báo cáo phản biện tương ứng · **[G]** giả định của tôi, chưa ai kiểm. Tôi không chạy giao diện, chỉ đọc mã; mọi con số thời gian là ước lượng, phải đo lại khi dựng.

Sáu quyết định gốc, phần còn lại của tài liệu là hệ quả:

1. **Canvas có khung, do recipe khai, vẽ theo ranh giới thực thi thật.** Bảy bước chủ sản phẩm nêu vẫn hiện đủ, nhưng các bước chạy chung một tiến trình được vẽ thành **một khối vòng lặp có các ngăn chỉnh được**, không phải các node nối tiếp.
2. **"Giống n8n" được giữ ở bốn cơ chế**: thấy dữ liệu thật vào/ra của từng node, chạy riêng một bước ở ranh giới thật, ghim mẫu, đánh dấu "đã cũ" kèm lý do và chi phí bằng phút. Kéo dây tự do thì không.
3. **Một bản nháp cho mỗi công thức của project; mỗi lần chạy thử/train tự chốt một bản bất biến.** "Nấu lại" = lấy bản chốt của một lần train cũ làm nháp.
4. **Thử là hành động hạng nhất có tên và giá**: hai nút cạnh nhau, mỗi nút ghi thời lượng dạng khoảng. Cạnh nút Train luôn có danh sách "chạy thử không bảo đảm điều gì".
5. **Kiểu train do nền tảng suy ra từ câu trả lời đời thường**; người rành mở được sơ đồ khối và hệ số LR.
6. **Nói thật trước, đẹp sau.** Giai đoạn đầu chưa có canvas chỉnh sửa; nó vá những chỗ mù mà canvas không chữa được (tiến độ, pha build, NaN, checkpoint).

## 1. Người dùng và việc cần làm

| | **Hà — kỹ sư phần mềm mới làm ML** (người dùng chính) | **Minh — kỹ sư ML tự chủ** | **Lan — trưởng nhóm gán nhãn** |
|---|---|---|---|
| Mục tiêu | Có model dùng được cho bài của nhóm mà không phải đọc hết repo gốc | Kiểm soát đúng thứ mình đổi, so được hai lần train, tái lập được | Biết nhãn của nhóm đã đủ tốt để ra model chưa |
| Nỗi sợ | Bấm Train, sáng hôm sau mới biết sai; không biết sai ở đâu | Nền tảng giấu config, tự đổi lịch học, nuốt lỗi | Làm hỏng máy đang phục vụ; bị hỏi những câu không hiểu |
| Đọc được | Python, YAML, log | Mọi thứ | Ảnh có khung, con số 0–100 |
| Mù ở đâu hôm nay | Xem dưới | Xem dưới | Toàn bộ form |

Chỗ mù cụ thể trong mã hiện tại **[R]**:

- `TrainSheet.tsx:226` — mục Dữ liệu chỉ in "n files · dung lượng · expected layout: coco". Không ảnh, không phân bố lớp, không biết tập val lấy ở đâu. Thực tế val bị cắt ngẫu nhiên 15 % trong container theo seed của lần train (đã xác nhận).
- `TrainSheet.tsx:240` — "Start from" là ô chọn hai giá trị kèm một câu gợi ý. Hà không biết phần nào của model được học; với D-FINE, "Train from scratch" thực ra vẫn dùng backbone học sẵn [01][02].
- `TrainSheet.tsx:259` — tham số hiện bằng tên khoá (`epochs`, `batch`, `seed`, `use_amp`). D-FINE chỉ lộ 5 khoá; LR, tăng cường, kích thước ảnh nằm trong file Minh không mở được.
- `TrainSheet.tsx:188, 298` — "First run builds its image (10–30 min)" chỉ là một dòng chữ; `JobProgress.tsx:12` không có pha build trong thanh bước, nên 10–30 phút đó hiện thành "đang giữ tài nguyên".
- `TrainSheet.tsx:319` — một nút duy nhất "Start training". Không có thử, không có ước tính thời gian.
- `Run.tsx:138` + `JobProgress.tsx:71` — pha `running` là một ô; vì không recipe nào khai `progress` [02], người dùng thấy "Working — this step reports no percentage" suốt nhiều giờ. `Run.tsx:151`: kết quả "appears after the first evaluation" — tức sau ít nhất một epoch.
- `Run.tsx:362` — tab Configuration liệt kê tham số và tên recipe; không có lệnh đã dựng, config đã sinh, bản recipe, image.
- `Projects.tsx:185, 212` — hành động sau khi chạy chỉ có "Train again" (mở lại form) và "Stop" (đánh dấu cancelled, không chạy lại được). Không có tạm dừng, không có rẽ nhánh. `Projects.tsx:119` chỉ so tối đa 4 tham số khác nhau.
- `Catalog.tsx:56–93` — trang recipe chỉ có bảng biến thể và một khối lệnh. Không Dockerfile, không file, không sửa.

**Chọn Hà làm người dùng chính cho bản đầu.** (a) Hà chịu đúng nỗi đau chủ sản phẩm mô tả: đủ sức sửa code nhưng không biết sửa gì và lúc nào thì sai. (b) Thiết kế cho Hà buộc mọi thứ có lời giải thích đời thường *và* vẫn xuống được tới code, nên phục vụ luôn Minh. (c) Lan không sửa code, không tự quyết LR — Lan đi "đường mặc định" (chọn dữ liệu → trả lời ba câu → Chạy thử → Train), bản đầu không tối ưu màn hình cho Lan. Cái giá: Minh thấy vài lời giải thích thừa; xử lý bằng công tắc **Gọn / Compact** ẩn câu giải thích và hiện tên khoá kỹ thuật.

## 2. Mô hình khái niệm

Chín từ, dùng nhất quán ở mọi màn hình (khoá i18n là cột tiếng Anh):

| Tiếng Việt | English | Là gì, nói với người dùng thế nào | Sửa được? |
|---|---|---|---|
| **Công thức gốc** | Base recipe | Cách train một họ model, do nền tảng hoặc workspace cung cấp, nằm ở Catalog | Không (chỉ xem) |
| **Công thức của project** | Project recipe | Bản sao của công thức gốc dành riêng cho project này; ghi nhớ nó tách ra từ bản gốc nào | Qua bản nháp |
| **Bản nháp** | Draft | Bản đang sửa. Mỗi công thức của project có đúng một bản nháp, tự lưu | Có |
| **Bản chốt** `r8` | Revision | Ảnh chụp bất biến của bản nháp, tự tạo khi bấm Chạy thử hoặc Train | Không bao giờ |
| **Lần chạy thử** | Trial | Một lượt kiểm rẻ trên một bản chốt: kiểm tĩnh, chạy khói, thuộc lòng | — |
| **Lần train** `#14` | Run | Bản chốt + phiên bản dữ liệu + điểm xuất phát + máy và môi trường | Không; chỉ nấu lại hoặc rẽ nhánh |
| **Điểm lưu** | Checkpoint | Trạng thái lưu giữa chừng để chạy nốt hoặc rẽ nhánh | — |
| **Kế hoạch thực thi** | Execution plan | Đúng lệnh và file cấu hình máy sẽ chạy, sinh từ bản chốt | Chỉ xem |
| **Model** `v3` | Model version | Kết quả đã đăng ký từ một lần train; nhớ bản chốt đã sinh ra nó | Không |

```
 CATALOG                         PROJECT
 Công thức gốc ──tách (fork)──▶ Công thức của project
 D-FINE @956d170                 │ nhớ: tách từ gốc nào ("gốc đã đổi" → huy hiệu + diff, không tự gộp)
                                 ▼
                              Bản nháp ◀───────────── "Nấu lại từ #12" (lấy r5 làm nháp)
                                 │ bấm Chạy thử / Train                      ▲
                                 ▼                                           │
                              Bản chốt r8 ──▶ Lần chạy thử (tĩnh ✔ khói ✔)   │
                                 │  + dữ liệu pcb v5 (đã chia tập)           │
                                 │  + điểm xuất phát + máy/môi trường        │
                                 ▼                                           │
                              Lần train #14 ──▶ Điểm lưu e10, e20… ──rẽ nhánh──▶ Lần train mới (cha: #14@e20)
                                 │
                                 ▼
                              Model v3 ──(sau này)──▶ Lưu thành công thức của workspace
```

Ba quy tắc người dùng phải tin được: (1) lần train đã chạy không bao giờ đổi; (2) sửa luôn là sửa trên nháp; (3) hai người cùng sửa một nháp → người lưu sau nhận thông báo xung đột theo mẫu `etag` sẵn có (30/01 §5), không ghi đè lặng lẽ [03].

Câu hỏi mở "fork theo project hay theo lần train": **cả hai** [04] — project giữ nháp (thứ đang làm), mỗi lần train giữ bản chốt (thứ đã làm). Một project có thể có nhiều công thức (D-FINE và YOLO cùng bài); bộ chọn ở góc trái canvas.

## 3. Hình dạng đúng của canvas

### 3.1 Khung: tối đa bảy node ngoài, một trong số đó là vòng lặp

Node chỉ được tồn tại khi nó là thứ **chạy riêng được, lưu được kết quả, và có trạng thái thật** [02 §3d][04 §4.1].

| # | Node | Ranh giới thật | Kết quả lưu được |
|---|---|---|---|
| 1 | **Môi trường & máy** / Environment | Build image trên từng máy; chọn máy, GPU | Image + digest, danh sách gói |
| 2 | **Dữ liệu** / Data | Tải, chuyển định dạng, chia tập, kiểm dữ liệu | Thư mục dữ liệu đã chuyển + báo cáo |
| 3 | **Điểm xuất phát** / Starting point | Tải trọng số; (sau này) ghép lớp cũ → mới | File trọng số + sha |
| 4 | **Huấn luyện ⟲** / Training loop | **Một tiến trình** của repo gốc | Điểm lưu, metric, log |
| 5 | **Đóng gói** / Package | Chọn bản tốt nhất, gom thư mục model, thử nạp lại | Thư mục model |
| 6 | **Đánh giá độc lập** / Evaluation | D-39, chạy qua handler | Bảng điểm, ảnh sai |
| 7 | **Xuất** / Export | Entrypoint `export` | File ONNX… |

Bên trong node 4 là **các ngăn** (compartment) xếp trên một vòng có mũi tên quay lại. Ngăn chỉnh được như node, có hình xem trước riêng, **nhưng không có trạng thái chạy riêng** — chỉ cả vòng có trạng thái. Với họ phát hiện vật thể, năm ngăn: **Batch & tăng cường → Phần được học → Lịch học**, rồi nhánh "mỗi epoch": **Kiểm định → Điểm lưu**.

Đối chiếu bảy bước của chủ sản phẩm: Dữ liệu = node 2; Chia tập = mặt trong của node 2 (kết quả ghi vào phiên bản dữ liệu, không thuộc lần train); Tăng cường, Model, Huấn luyện = ngăn của node 4 cộng node 3; Đánh giá tách làm hai — *Kiểm định* trong vòng (số của repo, trên tập val) và *Đánh giá độc lập* node 6 (số chính thức của nền tảng); Xuất = node 5 + 7. Không bước nào biến mất; cái thay đổi là hình vẽ không còn nói rằng chúng chạy lần lượt.

### 3.2 Khung thay đổi theo họ model

Recipe khai danh sách node và ngăn (trường mới `stages` trong manifest [01][02]); nền tảng chỉ cố định thứ tự khung ngoài. Node recipe không có thì **không vẽ**, kèm một dòng mờ "công thức này chưa khai bước xuất" — không vẽ node giả.

| Họ | Node ngoài | Ngăn trong vòng |
|---|---|---|
| Phát hiện/phân vùng (d-fine, deimv2, rt-detrv4, rf-detr, yolo) | 1–7 | Batch & tăng cường · Phần được học · Lịch học · Kiểm định (mAP + ảnh ghim) · Điểm lưu |
| Văn bản (hf-text) | 1–6 (chưa khai export) | Tách từ & độ dài (bao nhiêu % mẫu bị cắt) · Phần được học · Lịch học · Kiểm định (F1 + ma trận nhầm lẫn) · Điểm lưu |
| LLM (hf-llm-sft) | 1–5 (chưa có eval, export) | Mẫu hội thoại & độ dài · Adapter LoRA (thay "Phần được học") · Lịch học · Kiểm định (loss + câu trả lời cho prompt ghim) · Điểm lưu |
| ASR (whisper, vibevoice-asr) | whisper 1–6; vibevoice-asr 1–5 (chưa khai eval) | Âm thanh (16 kHz, lọc thời lượng: n mẫu bị loại) · Chuẩn hoá văn bản · Phần được học · Lịch học · Kiểm định (WER + cặp chuẩn/nhận dạng) · Điểm lưu |
| TTS (vibevoice-tts) | 1–5 (`predict` thay cho eval) | Giọng mẫu · Lịch học · Kiểm định = **nghe thử**, không có số · Điểm lưu |
| Bảng (tabular-gbdt) | 1, 2, 4, 5, 6 (không có Điểm xuất phát) | Đặc trưng · Tham số cây · Kiểm định theo vòng boosting |
| Chỉ suy luận (sam2, hf-auto, generic-onnx, vibevoice-realtime) | Môi trường → Model → Thử dự đoán | Không có vòng; chỉ hiện ở trang Recipe, không hiện ở Project |

### 3.3 Trên node và trong ngăn chi tiết

**Trên thân node** (tối đa bốn thứ, không có ô nhập): biểu tượng trạng thái · một dòng tóm tắt bằng giá trị thật ("pcb v5 · 2 480 ảnh") · một hình nhỏ (lưới 4 ảnh, sparkline, thanh chia tập, đường LR) · các huy hiệu: chấm xanh "đã sửa so với gốc", tam giác vàng "đã cũ", khoá "cần quyền".

Trạng thái node, dùng token sẵn có: xám *chưa cấu hình* · viền thường *sẵn sàng* · `info` *đang chạy* · `success` *xong* · `danger` *lỗi* · `warning` tam giác *đã cũ* (rê chuột thấy lý do) · biểu tượng tia chớp *lấy từ bộ đệm* (image đã có, dữ liệu đã chuyển).

**Ngăn chi tiết** trượt từ phải, **không che canvas** (canvas co lại; rộng 560 px, nút mở rộng toàn chiều ngang cho diff và code). Bố cục ba cột khi mở rộng, ba khối xếp dọc khi hẹp: **Vào** (dữ liệu thật đi vào node) · **Chỉnh** (ba lớp, mục 7) · **Ra** (dữ liệu thật đi ra, từ lần chạy thử gần nhất của đúng bản này; nếu đã cũ thì phủ mờ và ghi "kết quả của r6, chưa chạy lại").

### 3.4 "Đổi cái này thì phải chạy lại gì, tốn bao lâu"

Mỗi ô chỉnh mang sẵn một **nhãn giá** trước khi người dùng đụng vào, và sau mỗi thay đổi **thanh hậu quả** ở đáy canvas cộng dồn:

| Mức | Sửa gì | Phải làm lại | Giá hiển thị |
|---|---|---|---|
| 0 | Tên, ghi chú, ngân sách giờ | Không gì | — |
| 1 | Số epoch, LR đỉnh, tần suất lưu, dừng sớm | Không cần thử lại; đường LR vẽ lại | "0 phút" |
| 2 | Batch, kích thước ảnh, biến thể, dữ liệu, điểm xuất phát, phần được học, máy khác loại GPU | Chạy khói | "~4–8 phút" |
| 3 | Manifest, cấu hình repo, script keo, bước code | Chạy khói (bắt buộc, không vượt được) | "~4–8 phút" |
| 4 | Vài file trong mã repo gốc | Lớp image mỏng + chạy khói | "~6–10 phút" [02, chưa đo] |
| 5 | Dockerfile, gói pip, commit repo | Dựng lại môi trường **trên từng máy** + chạy khói | "10–30 phút mỗi máy" |

Node bị ảnh hưởng và mọi node phía sau chuyển sang "đã cũ"; lý do ghi bằng câu ("batch 16 → 8 làm đổi bộ nhớ cần dùng"). Con số phút lấy từ lần chạy thử gần nhất trên đúng máy đó; chưa từng chạy thì ghi "chưa đo" thay vì bịa.

### 3.5 Mức tự do

| Được | Không được | Vì sao |
|---|---|---|
| Kéo, phóng to, thu gọn vòng lặp | Nối lại dây giữa các node | Luồng train một model là đường thẳng có một vòng; cho nối dây là mời tạo đồ thị không chạy được [04] |
| Chèn **bước code** vào ba điểm mở (dấu ⊕): sau Dữ liệu, sau Đóng gói, và trong vòng *nếu recipe khai hook* | Chèn code giữa hai ngăn bất kỳ | Giữa "Tăng cường" và "Model" không có chỗ cắm ở repo gốc [01][02] |
| Bật/tắt node 6, 7 | Xoá node 1–5 | Đó là việc máy luôn làm |
| Chạy riêng: Dựng môi trường · Chuẩn bị dữ liệu · Xem batch · Chạy khói · Đánh giá lại · Xuất lại | Chạy riêng một ngăn của vòng | Một tiến trình |
| Ghim mẫu để theo dõi | Ghim kết quả để lần train thật dùng lại | Bài học "thử một đằng, chạy một nẻo" [04] |

**Xung đột với ý chủ sản phẩm.** Yêu cầu là "workflow như n8n, chỉnh từng node, từng step, từng dòng code". Thiết kế giữ *chỉnh từng node, từng bước, tới từng dòng* nhưng bỏ *kéo dây tự do*. Cái giá: trông kém tự do hơn n8n, không dựng được luồng nhiều nhánh trong một lần train. Phương án thay thế: (a) canvas tự do thật ở **tầng nối nhiều lần train** (train → cổng chất lượng → đăng ký → triển khai) trên engine Pipelines sẵn có, nơi mỗi node đúng là một job độc lập — tôi khuyến nghị, làm sau; (b) canvas tự do ngay trong một lần train — không khuyến nghị: chưa công cụ train nào làm vậy thành công [04], và trạng thái từng node phải dựng giả [03].

Về kỹ thuật dựng: khung cố định nên vẽ bằng CSS grid + SVG như `Pipelines.tsx` đang làm **[R]**, chưa cần thư viện canvas; lớp cấu hình và code cần thêm CodeMirror (đã có trong danh mục 30/01 §4 nhưng chưa có trong `web/package.json` **[R]**).

### 3.6 Wireframe ba thời điểm

**(a) Thiết kế** — bản nháp có ba thay đổi, một thay đổi làm cũ kết quả thử:

```
PCB defects ›  [Công thức] Lần train(14) Models(3)        [▷ Chạy thử ~4–8 ph]  [▶ Train thật  cần thử lại]
[D-FINE ▾] nháp · 3 sửa so với r8 · tách từ D-FINE@956d170          Đang train #14 · epoch 12/72 [Mở]
┌1 Môi trường & máy ──┐
│ ⚡ pc-02 · RTX 3060  │──┐   ┌4 HUẤN LUYỆN ⟲  72 epoch × 248 bước ──────────────────────────┐
│ image a41c… đã có   │  │   │ ┌Batch & tăng cường┐  ┌Phần được học┐  ┌Lịch học ●─────┐     │
└─────────────────────┘  │   │ │ [▦][▦][▦][▦]      │─▶│ ◐▒ ●▓ ●▓ ✚█ │─▶│  ╱‾‾╲___      │──┐  │   ┌5 Đóng gói ──┐  ┌6 Đánh giá ─┐
┌2 Dữ liệu ───────────┐  │   │ │ batch 8 ● ⚠       │  │ học 10/10 M │  │ đỉnh 4e-4     │  │  │   │ bản tốt nhất│  │ test 248   │
│ pcb v5 · 2 480 ảnh  │──┼──▶│ └───────────────────┘  └─────────────┘  └───────────────┘  │  │──▶│ + thử nạp   │─▶│ ảnh        │─▶ 7 Xuất
│ ▮▮▮▮▮▮▮▮▯▯ 80/10/10 │  │   │        ▲            mỗi epoch                              ▼  │   └─────────────┘  └────────────┘   (tắt)
│ ⚠ 2 lưu ý           │  │   │        └──── ┌Điểm lưu: 30 ph┐ ◀── ┌Kiểm định: 248 ảnh val┐ ◀┘  │
└─────────────────────┘  │   │              └───────────────┘     └──────────────────────┘    │
┌3 Điểm xuất phát ────┐  │   └──────────────────────────────────────────────────────────────┘
│ dfine-s · đã học sẵn│──┘         ⊕ bước code                                   ⊕ bước code
└─────────────────────┘
─ Thanh hậu quả ─────────────────────────────────────────────────────────────────────────────────────
 Bạn đổi batch 16 → 8. Đã cũ: Huấn luyện, Đóng gói. Phải làm lại: Chạy khói ~4–8 ph. Không dựng lại môi trường.
 [Xem 3 thay đổi]  [Hoàn tác]                                     Chạy thử KHÔNG bảo đảm: 5 điều ▸
```

**(b) Chạy thử** — cùng khung, mỗi node tự báo kết quả thật; một node lỗi:

```
 Chạy khói r9 trên pc-02 · 5 ph 12 s · 64 ảnh (gồm 6 ca nặng nhất + 8 ảnh ghim)            [Dừng]  [Chạy lại]
┌1 ✔ 0:02 ────────────┐      ┌4 ✔ 3:40  HUẤN LUYỆN ⟲  lịch nén: 2 giai đoạn × 30 bước ───────┐
│ ⚡ image sẵn có      │──┐   │ Batch ✔ [▦][▦][▦][▦] xem 2 batch   Phần được học ✔ 10 M    │   ┌5 ✔ 0:31 ─────┐  ┌7 ✖ 0:09 ───┐
└─────────────────────┘  │   │ 3,1 bước/s · bộ nhớ GPU đỉnh 7,4 / 12 GB · loss 41 → 29    │   │ nạp lại ✔    │  │ lỗi xuất:  │
┌2 ✔ 0:41 ────────────┐  ├──▶│ Kiểm định ✔ metric chính có giá trị (chưa có ý nghĩa)      │──▶│ dự đoán 8 ảnh│─▶│ thiếu gói  │
│ 64 ảnh · bỏ 3 nhãn  │──┤   │ Điểm lưu ✔ lưu → ngắt → chạy nốt ✔ (mất 0 bước)            │   │ ghim [Xem]   │  │ onnxsim    │
└─────────────────────┘  │   └──────────────────────────────────────────────────────────────┘   └──────────────┘  └────────────┘
┌3 ✔ 0:06 ────────────┐  │    Ước tính lần train thật: 11–16 giờ · bộ nhớ GPU 7,4–8,9 GB · đĩa ~14 GB
│ khớp 412/418 lớp    │──┘    (tính lại sau epoch thật đầu tiên)
│ 6 lớp tạo mới (head)│       Kết luận: Train được. Xuất ONNX sẽ hỏng ở cuối — sửa node 1 (thêm gói, ~10–30 ph) hoặc tắt Xuất.
└─────────────────────┘       Node 6 không chạy khi thử: đánh giá độc lập cần model đã đăng ký; handler đã thử ở node 5.
```

**(c) Đang train thật** — khung thu gọn thành dải trên đầu trang Run; vòng lặp là node duy nhất "sống":

```
 #14 dfine-s-14 · r8 · pcb v5                                  [⏸ Tạm dừng] [⑂ Rẽ nhánh] [■ Dừng, giữ bản tốt nhất]
 1 ✔ 0:02 ─ 2 ✔ 1:10 ─ 3 ✔ 0:06 ─▶┌4 ● HUẤN LUYỆN ⟲ epoch 12/72 · bước 41/124 · đang: Kiểm định ┐─▶ 5 ○ ─ 6 ○ ─ 7 tắt
                                  │ ▁▂▃▅▆▆▇ mAP 0,41   loss ▇▅▄▃▃▂   LR ╱‾‾╲__ ▲ đang ở đây     │
                                  │ còn 9–12 giờ (ước tính ban đầu 11–16 giờ; đang nhanh hơn)   │
                                  │ Điểm lưu gần nhất 7 ph trước · sập bây giờ mất tối đa 7 ph  │
                                  └────────────────────────────────────────────────────────────┘
 Mốc: ✔ kiểm sớm 0:05 (8 ảnh ghim có khung)  ✔ điểm kiểm sau epoch 1  ○ ngân sách 18 giờ  ○ dừng sớm: 20 epoch không khá hơn
```

"đang: Kiểm định" là pha con suy từ log theo mẫu recipe khai [02 §3d]; recipe chưa khai thì chỉ hiện số epoch, không đoán.

## 4. Năm hành trình

Mỗi bước ghi: **thấy** · **làm** · **hệ thống trả lời** · *chờ*.

### H1. Lần đầu train trên dữ liệu vừa gán nhãn (Hà)

1. Ở ngăn "Tạo phiên bản dữ liệu từ nhãn" (`Labeling.tsx`, `ExportSheet`) **thấy** nút mới "Train với phiên bản này" · **bấm** · mở Project (tạo mới nếu chưa có), tab Công thức ở trạng thái rỗng · *tức thì*.
2. **Thấy** ba thẻ công thức hợp bài toán, mỗi thẻ ghi: đã có môi trường trên máy nào, cần GPU bao nhiêu, đã ai trong workspace train thành công chưa · **chọn** D-FINE · hệ thống tách công thức về project, vẽ khung với mặc định · *1–2 s*.
3. Node Dữ liệu tự chạy **kiểm dữ liệu** trên CPU · **thấy** lưới 16 ảnh có khung, phân bố lớp theo tập, và hai lưu ý: "Phiên bản này chưa chia tập — đề nghị 80/10/10, giữ tập test cố định cho mọi lần sau" và "Lớp *xước* chỉ có 12 mẫu" · **bấm** "Chia và lưu thành v5" · tạo phiên bản dữ liệu mới có chia tập ghi sẵn · *vài giây tới vài phút theo cỡ dữ liệu* [G].
4. Node Điểm xuất phát đã chọn sẵn "Từ model đã học sẵn (khuyên dùng)". Hà không đụng.
5. **Bấm** "Chạy thử" · kiểm tĩnh *~5 s*; nếu máy chưa có môi trường: thẻ "Dựng môi trường lần đầu trên pc-02: 10–30 phút, không giữ GPU, xem log trực tiếp" · rồi chạy khói *4–8 phút* · canvas như wireframe (b).
6. **Thấy** 8 ảnh ghim có khung dự đoán (còn sai — ghi rõ "mới học 60 bước") và ước tính 11–16 giờ · **bấm** "Train thật", đặt ngân sách 18 giờ · chuyển sang trang Run.
7. *5 phút sau*: thông báo "Kiểm sớm: model đã bắt đầu khoanh đúng 5/8 ảnh ghim". *Sau epoch 1 (~12 phút)*: thẻ "Điểm kiểm sau epoch đầu" (mục 5.4). Hà đóng máy đi về.

### H2. Sửa một tham số rồi train lại (Hà)

1. Ở bảng lần train **bấm** "Nấu lại từ #14" · nếu nháp đang có sửa đổi chưa chốt: ngăn bên phải hỏi "Thay nháp hiện tại (3 sửa) bằng r8?" kèm nút hoàn tác 10 s · canvas mở với r8.
2. Mở ngăn Lịch học, kéo epoch 72 → 120 · **thấy** đường LR mới chồng lên đường cũ, nhãn giá "0 phút — không cần thử lại", ước tính đổi thành 18–27 giờ.
3. Đổi thêm batch 16 → 8 · thanh hậu quả: "cần chạy khói ~4–8 ph"; nút Train khoá.
4. **Bấm** Chạy thử (tự chốt r9) → *4–8 phút* → Train thật → lần #15. Trên bảng, #15 mang chip `epochs=120 batch=8`; chọn #14 và #15 → So sánh (mục 8.4).

### H3. Sửa cấu hình và code sâu (Minh)

1. Mở ngăn Batch & tăng cường, lật sang lớp **Cấu hình** · **thấy** đúng file `mlp_train.yml` sẽ sinh ra, phần kế thừa từ repo gốc màu mờ, phần nền tảng ghi đè màu thường · tắt lật ngang (bài có lớp trái/phải) · nhãn giá mức 3.
2. **Bấm** "Xem batch" · máy có môi trường dựng 2 batch trên CPU · *20–60 s* [G] · **thấy** ảnh sau tăng cường, không còn ảnh lật.
3. Lật sang lớp **Code** của node Dữ liệu, mở `to_coco.py` · **thấy** cảnh báo "File này dùng chung cho mọi công thức đọc COCO; sửa ở đây tạo bản riêng cho công thức này" · sửa ngưỡng bỏ hộp nhỏ · kiểm cú pháp tại chỗ.
4. Chạy thử (bắt buộc với mức 3) · node Dữ liệu báo "bỏ 0 nhãn (trước: 37)".
5. Cần thêm gói pip: mở node Môi trường → lớp Code → Dockerfile · nhãn giá "10–30 phút mỗi máy" · **bấm** "Dựng môi trường" — job riêng không giữ GPU · khi xong, kiểm hợp đồng tự chạy (shim khởi động được, handler nạp được) [03].
6. Cần sửa sâu hơn mức trình duyệt làm tốt: nút "Mở trong Notebook với đúng môi trường này" hoặc "Nhập nhánh git làm công thức của workspace" (S-17-3).

### H4. Train tiếp model cũ với dữ liệu mới (Hà, tháng sau)

1. Dữ liệu có v6 (cũ + 600 ảnh mới). Canvas hiện huy hiệu ở node Dữ liệu: "Có v6 mới hơn" · **bấm** đổi sang v6 · node 2 trở xuống "đã cũ".
2. Mở node Điểm xuất phát · **trả lời** ba câu (mục 6.1): đã có model cho bài này → chọn `pcb-detector v3`; danh sách lớp có đổi → hệ thống tự so và báo "thêm 1 lớp: *cháy*"; dữ liệu gồm cả cũ lẫn mới → có.
3. **Thấy** kết luận: "Train tiếp từ model của bạn. Giữ 6 lớp cũ, tạo mới 1 lớp. Lịch ngắn bằng 1/3, LR đỉnh thấp hơn." kèm bảng ghép lớp cũ → mới để sửa nếu đoán sai.
4. Chạy thử · node 3 báo "khớp 417/418 lớp trọng số; hàng của lớp *cháy* tạo mới".
5. Train · xong, node 6 tự chạy trên **tập test cố định** và đặt kết quả cạnh v3: "lớp cũ: 0,61 → 0,60 · lớp mới: 0,48". Nếu lớp cũ tụt quá ngưỡng người dùng đặt → cảnh báo đỏ trước nút Đăng ký.

Nếu ở bước 2 Hà trả lời "chỉ có dữ liệu mới": thẻ đỏ "Model sẽ quên các trường hợp cũ. Thêm dữ liệu cũ vào, hoặc xác nhận bạn chấp nhận điều này."

### H5. Lần train nhiều giờ gặp sự cố (Hà)

1. *Giờ thứ 9*, điện thoại báo: "#15 dừng tự động: loss thành NaN ở epoch 44". Mở trang Run · **thấy** vạch đỏ dọc trên đường loss tại bước hỏng, node vòng lặp màu cảnh báo, dòng "Đã giữ điểm lưu tốt gần nhất: epoch 43 (22 phút trước lúc hỏng). GPU đã nhả."
2. **Thấy** ba lựa chọn, mỗi cái một câu hậu quả: **Chạy nốt** ("chạy lại y nguyên từ epoch 43 — nhiều khả năng hỏng lại") · **Rẽ nhánh từ epoch 43** ("lần train mới #16, sửa được LR; biểu đồ nối tiếp #15") · **Dừng, giữ bản tốt nhất** ("đăng ký model ở epoch 39, mAP 0,49").
3. **Bấm** Rẽ nhánh · canvas mở với nháp lấy từ r9, node Điểm xuất phát khoá ở "#15 @ epoch 43", gợi ý "giảm LR đỉnh còn một nửa" · sửa · thanh hậu quả: "điểm xuất phát đổi → chạy khói ~4–8 ph" · Chạy thử → Train · *chờ GPU*.
4. Trường hợp mất điện: trang Run hiện "Mất tín hiệu từ pc-02 lúc 02:14. Đang chờ máy quay lại (dữ liệu chỉ có trên máy này)" hoặc "Chuyển sang pc-03: dựng môi trường 10–30 ph, tải lại dữ liệu, chạy nốt từ điểm lưu gần nhất". Luôn kèm "mất tối đa X phút".

## 5. Thử trước khi train

### 5.1 Bốn tầng

| Tầng | Chạy ở đâu, bao lâu | Làm gì | Hiện trên node |
|---|---|---|---|
| **0. Kiểm dữ liệu** | CPU, tự chạy khi đổi dữ liệu | Ảnh hỏng, nhãn bị loại, phân bố lớp theo tập, vật thể quá nhỏ ở kích thước vào, ảnh trùng giữa train và val | Node 2: số lưu ý |
| **1. Kiểm tĩnh** | Không cần GPU, ~5 s | Lệnh dựng được, có đủ tập, số lớp khớp, đĩa đủ, máy có GPU đủ bộ nhớ, ai đang giữ GPU, license đã xác nhận, URL dữ liệu còn hạn | Dấu ✔/✖ từng node, không có thời gian |
| **2. Chạy khói** | GPU, 4–8 ph khi đã có môi trường | **Cùng đường mã với train thật**: nạp trọng số → vài chục bước qua *mọi* giai đoạn của lịch (lịch nén riêng [02]) → kiểm định → lưu → **ngắt rồi chạy nốt** → đóng gói → nạp lại → dự đoán ảnh ghim → xuất. Mẫu cố ý gồm ca nặng nhất [01] | Thời gian, số đo, hình thật từng node |
| **3. Thuộc lòng** (tuỳ chọn) | GPU, 5–15 ph | Train 8–16 ảnh, tắt tăng cường, xem model có thuộc nổi không | Node 4: đường loss + câu kết luận theo tiêu chí của họ model |

Chạy khói **thất bại** khi metric chính không xuất hiện, kể cả khi tiến trình thoát êm [01].

### 5.2 Nút Train mở khi nào

"Đúng bản đã thử" = cùng bản chốt (trừ các trường mức 0–1), cùng phiên bản dữ liệu, cùng điểm xuất phát, cùng môi trường (digest), cùng loại GPU [02][04]. Lệch một thứ → nút ghi rõ thứ nào lệch. Mức 3–5 (sửa cấu hình sâu, code, môi trường): khoá cứng. Mức 2: cho vượt kèm lý do gõ tay, lần train mang huy hiệu "chưa thử" (đây là câu hỏi 1 ở mục 12).

### 5.3 Điều chạy thử KHÔNG bảo đảm

Khối cố định cạnh nút Train, không đóng được:

> Chạy thử cho biết lần train **khởi động được và đi hết đường**. Nó không cho biết: (1) model có đạt chất lượng không; (2) loss có hỏng sau vài giờ không; (3) bộ nhớ có tràn ở một ảnh hiếm không; (4) nhãn có đúng không; (5) thời gian chính xác — ước tính có thể lệch gấp rưỡi. Vì vậy lần train thật có bốn chốt canh bên dưới.

Ước tính luôn là **khoảng** ("11–16 giờ", "7,4–8,9 GB"), ghi nguồn ("đo trên pc-02, 60 bước"), và tự thay bằng số mới sau epoch thật đầu tiên; lệch quá 50 % thì gửi thông báo kèm lựa chọn [03].

### 5.4 Lưới an toàn khi chạy thật

Vẽ thành **hàng mốc** dưới node vòng lặp (wireframe c), mỗi mốc bấm được:

| Mốc | Khi nào | Người dùng thấy |
|---|---|---|
| **Kiểm sớm** | Sau ~200 bước, trong 5 phút đầu | 8–16 ảnh ghim có khung dự đoán; "0/8 ảnh có khung nào" → cảnh báo đỏ |
| **Điểm kiểm sau epoch đầu** | Hết epoch 1 | Thẻ bốn dòng: điểm lưu đã bền và nạp lại được · metric val là số hữu hạn · tốc độ so với ước tính · ETA mới. Một thông báo duy nhất |
| **Canh liên tục** | Suốt lần chạy | Vạch đỏ trên biểu đồ khi: loss NaN/vô cực, loss vọt gấp k lần mức thấp nhất, metric chính = 0 sau ba lần kiểm định, val xấu dần trong khi train vẫn giảm, GPU nhàn. Mặc định: NaN → tự tạm dừng; còn lại → chỉ báo |
| **Ngân sách giờ** | Người dùng đặt | Cảnh báo ở 1,2×; trần cứng tuỳ chọn |
| **Điểm lưu** | Mỗi ≤ 30 ph | Chấm trên trục thời gian; dòng "sập bây giờ mất tối đa X phút" |

Ba hành động, tách bạch, không gộp thành "tiếp tục" [01]: **⏸ Tạm dừng → Chạy nốt** (cùng lần train, không đổi gì, nhả GPU khi dừng) · **⑂ Rẽ nhánh từ điểm lưu N** (lần train mới có cha, sửa được; "thêm epoch" cũng là rẽ nhánh) · **■ Dừng, giữ bản tốt nhất**. Người dùng sẽ hỏi "sao không tạm dừng, sửa LR rồi chạy tiếp?" — ngăn Tạm dừng trả lời thẳng: "Chạy nốt dùng lại đúng cài đặt cũ. Muốn đổi cài đặt, hãy Rẽ nhánh."

## 6. Chọn kiểu train và điều chỉnh trọng số

### 6.1 Câu hỏi thay cho thuật ngữ

Node Điểm xuất phát mở ra **tối đa bốn câu, hiện dần**, không phải danh sách bảy kiểu:

1. *Bạn đã có model của mình cho đúng bài này chưa?* — Chưa → **Học từ model có sẵn** (fine-tune; mặc định, dừng hỏi). Có → chọn model/điểm lưu.
2. *Danh sách lớp có đổi không?* — hệ thống tự so và điền sẵn; người dùng chỉ xác nhận bảng ghép.
3. *Dữ liệu lần này gồm cả cũ lẫn mới, hay chỉ phần mới?*
4. *Model mới còn phải làm tốt các trường hợp cũ không?* — mặc định Có.

Kết quả ghi thành **một câu kết luận + tên kỹ thuật trong ngoặc** để Minh nhận ra: "Train tiếp từ model của bạn với dữ liệu cũ + mới (warm-start retrain)". "Chạy nốt" (resume) không nằm ở đây — nó là nút trên trang Run. "Train từ đầu" nằm dưới mục "Lựa chọn khác", kèm câu "gần như luôn kém hơn khi có dưới 100 nghìn mẫu" [01]; với D-FINE nhãn phải ghi đúng: "**Chỉ giữ phần backbone đã học sẵn**" — không gọi là "từ đầu". Chưng cất và học tăng dần không hiện ở bản đầu.

Hai câu về số mẫu và thiết bị đích [01] không hỏi: số mẫu nền tảng tự đếm; thiết bị đích dời sang lúc chọn biến thể (hiện độ trễ công bố nếu recipe có).

### 6.2 Sơ đồ khối ("Phần được học")

```
 Ảnh ─▶ [ Backbone ❄ ]─▶[ Encoder ◐ ×0,1 ]─▶[ Decoder ● ×1 ]─▶[ Đầu ra ✚ tạo mới 1/7 lớp ]
          5,2 M             2,1 M               2,4 M              0,3 M        Được học: 4,8 / 10 M
 Mẫu sẵn:  (●) Dữ liệu ít — khoá backbone   ( ) Cân bằng — backbone học chậm   ( ) Dữ liệu nhiều — học tất cả
```

- Mỗi khối ba trạng thái bấm xoay vòng: ❄ **khoá** / ◐ **học chậm** (hệ số LR) / ● **học đủ**. ✚ **tạo mới** do nền tảng đặt khi lớp đổi, người dùng không chọn.
- Danh sách khối và số tham số do recipe khai và đo bằng một lệnh kiểm trong image [02]; chưa đo thì không vẽ số.
- **Người mới thấy**: ba mẫu sẵn + câu "nên chọn khi…"; sơ đồ chỉ để nhìn. **Bật Gọn/Nâng cao thấy**: hệ số từng khối, khoá lớp chuẩn hoá, mẫu regex nhóm tham số, số tham số khớp.
- **Bảng ghép lớp** (khi train tiếp): hai cột lớp cũ → lớp mới, mỗi dòng một trạng thái *giữ / đổi tên / gộp vào… / bỏ / mới*. Dòng "mới" và "bỏ" tô màu.
- LLM: thay sơ đồ bằng thẻ **Adapter LoRA** (bậc, module đích, % tham số được học). Bảng: không có ngăn này.

**Giới hạn phải nói thật:** hôm nay chỉ "học từ model có sẵn" chạy được; "train tiếp model của tôi" và bảng ghép lớp chưa có đường xuống mã; khoá/hệ số theo khối làm được ở họ DETR qua `--extra-json` nhưng lệnh train chưa truyền (đã xác nhận). Trước khi nền vá xong, ngăn này hiện **chỉ đọc** với dòng "công thức này chưa cho chỉnh phần được học" — không vẽ nút bấm không có gì đứng sau.

## 7. Ba lớp chỉnh

Trong cột **Chỉnh** của ngăn chi tiết, một thanh lật ba vị trí — cùng một node, không nhảy sang trang khác [04]:

```
┌ Lịch học ──────────────────────────────── ● 2 sửa so với gốc   [↶] [↷]  [⤢] ┐
│ [ Trực quan ] [ Cấu hình ] [ Code ]                    giá: 0 phút           │
│  Số epoch      [ 120 ] ●  gốc 72 [↺]      "số lượt đi hết dữ liệu"           │
│  LR đỉnh       [ 4e-4 ]                   "bước chỉnh lớn nhất mỗi lần học"  │
│  ╱‾‾‾╲______  ── bản này   ┄┄ gốc                                            │
└──────────────────────────────────────────────────────────────────────────────┘
```

- **Trực quan / Visual**: điều khiển + hình + một câu lời thường cho mỗi ô. Mọi giá trị là giá trị thật, kể cả mặc định (giữ cách `TrainSheet` đang làm).
- **Cấu hình / Config**: đúng phần file mà node này sinh ra (YAML của repo, đoạn manifest), dạng soạn thảo có kiểm schema. Dòng kế thừa từ gốc màu mờ; dòng người dùng đổi có vạch bên lề. Sửa ở lớp này cập nhật ngược lớp Trực quan; khoá nào lớp Trực quan không biểu diễn được hiện thành chip "1 chỉnh sửa chỉ có ở lớp Cấu hình".
- **Code**: các file node này thật sự chạy (script keo, bộ chuyển, bước code; về sau là file của repo gốc dưới dạng bản vá). File dùng chung với công thức khác mang nhãn cảnh báo; sửa là tạo bản riêng.

**Khác biệt so với gốc**: chấm xanh ở ô → ở ngăn → ở node → bộ đếm trên đầu canvas "3 sửa so với r8 · 5 sửa so với gốc". Bấm bộ đếm mở danh sách thay đổi dạng bảng (đường dẫn · cũ → mới · mức giá) và diff văn bản cho file. So sánh luôn bằng bảng và diff, không bằng hai hình canvas [04].

**Hoàn tác**, bốn tầng: ↺ từng ô về gốc · ↶/↷ trong phiên sửa · "Bỏ các sửa của node này" · "Bỏ nháp, về r8". Nháp tự lưu; rời trang không mất.

**Giới hạn trung thực của sửa code trong trình duyệt** — ghi ngay trong lớp Code:

1. Đây là trình soạn file, không phải IDE: có tô màu và kiểm cú pháp, không có gợi ý xuyên repo, không gỡ lỗi từng dòng. Lỗi logic chỉ lộ khi chạy khói.
2. Không sửa được mọi dòng với cùng một giá: script keo và cấu hình không cần dựng lại; file repo gốc cần lớp image mỏng; Dockerfile cần dựng lại trên từng máy.
3. Sửa code tắt kiểm tĩnh cho node đó (nền tảng không biết code mới trả về gì) → chạy khói bắt buộc.
4. Sửa handler làm đổi cách model được phục vụ; model sẽ ghi nhớ bản chốt đã sinh ra nó để phục vụ đúng code đó [02].
5. Cần quyền riêng "sửa code công thức"; không có quyền thì lớp Code chỉ đọc.

**Xung đột với "từng dòng code".** Chủ sản phẩm muốn sửa tới từng dòng; báo cáo vận hành đề nghị cắt còn cấu hình + hook [03]. Tôi chọn: **xem được mọi dòng từ giai đoạn 1**, **sửa theo bậc** — cấu hình, rồi script keo và bước code, cuối cùng file repo gốc và Dockerfile. Cái giá: các giai đoạn đầu "từng dòng code" là *xem* chứ chưa *sửa*. Phương án thay thế: mở sửa mọi file ngay — kéo theo cách ly máy, lớp phủ file cho cả train lẫn serve, dọn image phải làm trước mọi thứ khác.

## 8. Ba trang sau khi làm lại

### 8.1 Project — `/w/{ws}/projects/{p}`

Đầu trang: tên, bài toán, kết quả tốt nhất, và **dải lần train đang chạy** (hiện ở mọi tab). Ba tab, đồng bộ `?tab=`:

| Tab | Nội dung | Rỗng | Chờ / lỗi |
|---|---|---|---|
| **Công thức** / Recipe (mặc định) | Bộ chọn công thức + canvas + thanh hậu quả + hai nút | "Chọn một công thức để bắt đầu" + 3 thẻ đề xuất theo bài toán + "Xem tất cả ở Catalog" | Gốc đã đổi sau khi tách: huy hiệu + nút xem diff (không tự gộp). Công thức gốc lỗi manifest: canvas chỉ đọc + lý do |
| **Lần train** / Runs | Bảng hiện có (`Projects.tsx`) + cột **Bản** (`r8`), **Kiểu** (học từ model có sẵn / train tiếp / rẽ nhánh từ #n), huy hiệu "chưa thử"; lần rẽ nhánh thụt dưới cha; lần chạy thử nằm ở bộ lọc riêng, không lẫn vào bảng chính | "Chưa có lần train nào — mở tab Công thức" | Lý do chờ như hiện nay, thêm "đang dựng môi trường 4/18 ph" |
| **Models** | Model đã đăng ký từ project, bản chốt sinh ra nó, điểm đánh giá độc lập trên cùng tập test | "Model xuất hiện ở đây sau khi đăng ký từ một lần train" | — |

Form `TrainSheet` bị bỏ. Nút "Train again" trong bảng đổi thành "Nấu lại từ #n".

### 8.2 Recipe trong Catalog — `/w/{ws}/catalog/{recipe}`

Cùng component canvas ở chế độ chỉ đọc, là hình chiếu của công thức gốc. Tab: **Luồng** (canvas; bấm node xem ngăn chi tiết với giá trị mặc định) · **Biến thể** (bảng hiện có) · **Môi trường** (Dockerfile, gói nào ghim/không ghim, máy nào đã có image, kích thước) · **File** (cây file chỉ đọc; file dùng chung ghi "dùng chung với 3 công thức") · **Lệnh** (khối lệnh đã dựng hiện có) · **Đang dùng ở** (project đã tách, lần train, model).

Đầu trang nói thật về độ chín: "Chưa được chạy thử trên nền tảng" khi `built: false, smokeTested: false` (hiện đúng cho cả 15 công thức [01]), kèm danh sách `openItems`. Nút chính: **"Dùng cho project…"**. Rỗng không áp dụng; lỗi: banner lý do manifest không hợp lệ.

### 8.3 Run — `/w/{ws}/projects/{p}/runs/{run}`

Trên cùng: tiêu đề + ba hành động (mục 5.4) + **dải khung** (wireframe c) thay cho `PhaseBar`. Tab:

| Tab | Nội dung |
|---|---|
| **Theo dõi** / Live | Ba biểu đồ chung trục: metric chính (có vạch "mức model gốc" và "mức cần đạt" nếu người dùng đặt), loss train + val, LR. Hàng mốc. Câu lời thường dưới mỗi biểu đồ [01 §5] |
| **Mẫu dự đoán** / Samples | Lưới ảnh ghim; thanh trượt epoch để xem cùng ảnh đó qua từng lần kiểm định. ASR: cặp chuẩn/nhận dạng tô chỗ sai. LLM: câu trả lời cho prompt ghim. TTS: trình phát |
| **Metric** | Như hiện có; thêm vạch NaN, tách trục bước/epoch |
| **Log** | Như hiện có + bộ lọc theo pha (môi trường · dữ liệu · huấn luyện · đóng gói) |
| **Công thức đã dùng** / Recipe used | Bản chốt, "khác gì so với #13" (bảng + diff), **kế hoạch thực thi** (lệnh đã dựng, config đã sinh, bảng chia tập, digest môi trường, sha trọng số) |
| **Điểm lưu** / Checkpoints | Trục thời gian; mỗi điểm: Rẽ nhánh · Đăng ký làm model · Ghim (không bị dọn) |
| **File** | Như hiện có |

Trạng thái: *chờ* — dải khung dừng ở node 1 với lý do ("GPU của pc-02 đang do deployment `pcb-prod` giữ"); *lỗi* — node lỗi tô đỏ trên dải khung, banner lỗi hiện có (`errorExcerpt`) đặt ngay dưới và nói lỗi thuộc node nào; *xong* — node 5–7 mang kết quả, banner đăng ký model hiện có.

### 8.4 So sánh hai lần train — `…/compare?runs=14,15`

Thứ tự từ trên xuống: (1) **dòng điều kiện so sánh** — "cùng tập val ✔ / ✖ hai lần này dùng hai tập val khác nhau, số không so được"; (2) **bảng khác biệt** chỉ gồm dòng khác nhau, nhóm theo node: dữ liệu, điểm xuất phát, tham số, cấu hình (diff văn bản), code (diff), môi trường (digest), máy; (3) biểu đồ chồng; (4) ảnh ghim đặt cạnh nhau; (5) dải khung thu nhỏ tô node có khác biệt.

## 9. Danh mục trực quan hoá

Cột cuối theo báo cáo 02 và mã tôi đã đọc: ✔ có dữ liệu · ◐ có một phần · ✖ chưa có.

| Người dùng nhìn thấy | Ở đâu | Lúc nào | Dữ liệu từ đâu | Đã có? |
|---|---|---|---|---|
| Ảnh mẫu có khung nhãn | Node 2, cột Vào | Thiết kế | `DatasetBrowser`/`ShapeOverlay` | ✔ |
| Thanh chia tập train/val/test, số mẫu mỗi lớp mỗi tập | Thân node 2 | Thiết kế | `split_counts` của phiên bản dữ liệu (theo đặc tả M04, tôi chưa kiểm bảng thật); hiện val do `to_coco.py` cắt lúc train | ◐ |
| Số nhãn bị loại khi chuyển định dạng, lý do | Node 2, cột Ra | Sau chuẩn bị dữ liệu | Hiện chỉ là một dòng log | ✖ |
| Kích thước vật thể tại kích thước vào; ảnh trùng giữa hai tập | Node 2, kiểm dữ liệu | Thiết kế | Tính trên CPU, chưa có | ✖ |
| Pha dựng môi trường + log trực tiếp | Node 1 | Chạy thử, train | Sự kiện có; log nằm ở `agent.log` | ◐ |
| Ai đang giữ GPU | Node 1, kiểm tĩnh | Thiết kế | `getClusterMap` (đang dùng ở `TrainSheet`) | ✔ |
| Báo cáo nạp trọng số (khớp/thiếu/tạo mới) | Node 3, cột Ra | Chạy khói | Phải đọc từ tiến trình train | ✖ |
| Sơ đồ khối + số tham số được học | Ngăn Phần được học | Thiết kế | Lệnh kiểm trong image + khai báo khối | ✖ |
| Batch thật sau tăng cường | Ngăn Batch | "Xem batch", chạy khói | Cần hook theo họ model | ✖ |
| Đường lịch LR dự kiến | Ngăn Lịch học | Thiết kế | Tính từ config đã sinh; chưa có | ✖ |
| Đường LR thật, loss train/val | Trang Run, thân node 4 | Train | Metric `lr/…`, `train/loss` đã ánh xạ; hai trục hoành đang trộn | ◐ |
| Vạch NaN | Biểu đồ loss | Train | Shim đang bỏ lặng lẽ | ✖ |
| % tiến độ, ETA dạng khoảng | Thân node 4 | Train | `ProgressLine` có; không recipe nào khai `progress` | ✖ |
| Bộ nhớ GPU, tải GPU theo thời gian | Tab Theo dõi | Train | Mẫu 15 s có; đỉnh trong tiến trình chưa có | ◐ |
| Ảnh ghim có dự đoán qua từng epoch | Tab Mẫu dự đoán, mốc Kiểm sớm | Train | Chưa có cơ chế | ✖ |
| Mốc điểm lưu, "sập bây giờ mất X phút" | Trục thời gian | Train | Danh sách checkpoint có; lưu bền đang hỏng với tên file cố định | ◐ |
| Ma trận nhầm lẫn, bảng ngưỡng, ảnh sai | Node 6 | Sau train | `Evaluation.tsx` (phát hiện, phân loại) | ✔ |
| Kế hoạch thực thi | Tab Công thức đã dùng | Trước và sau train | Lệnh: `renderTemplate` có; config đã sinh chỉ nằm trong thư mục model sau khi train xong | ◐ |
| Khác biệt giữa hai bản/hai lần train | Trang So sánh | Bất kỳ | Tham số: có; bản công thức: lần train chỉ ghi tên | ◐ |
| Nghe thử, văn bản sinh ra theo điểm lưu | Tab Mẫu dự đoán | Train | Chưa có | ✖ |

Mười trên hai mươi dòng là ✖, bảy dòng ◐. Canvas dựng trước dữ liệu sẽ là khung rỗng — đây là lý do lộ trình bắt đầu từ nền.

## 10. Bảng phủ bốn mươi điều

**ĐV** = đưa vào thiết kế (mục) · **H** = hoãn (giai đoạn ở mục 11) · **B** = bác bỏ.

| # | Điều | Xử lý |
|---|---|---|
| 01-1 | Bảy node nối tiếp sai; vòng lặp; recipe khai giai đoạn | ĐV §3.1, 3.2 |
| 01-2 | Thiếu kiểm dữ liệu, nhãn, rò rỉ | ĐV §5.1 tầng 0 (GĐ1); chia theo nhóm: H sau GĐ5 |
| 01-3 | Chia tập nằm trong lần train, theo seed | ĐV §3.1, H1 bước 3 (GĐ1) |
| 01-4 | NaN bị nuốt, chưa có chốt chặn | ĐV §5.4 (GĐ0 hiện NaN, GĐ3 tự dừng) |
| 01-5 | Kiểm sớm + bộ ảnh cố định | ĐV §5.4, tab Mẫu dự đoán (GĐ3) |
| 01-6 | Ba hành động: chạy nốt / rẽ nhánh / dừng giữ bản tốt | ĐV §5.4 (GĐ3) |
| 01-7 | Bảy kiểu train, bảy câu hỏi | ĐV một phần §6.1: 4 câu, 5 kiểu (học từ model có sẵn, train tiếp, chạy nốt, rẽ nhánh, LoRA). **B** hai câu (số mẫu, thiết bị) — nền tảng tự biết. Chưng cất, học tăng dần: H sau GĐ5 |
| 01-8 | Chạy khói: ca xấu nhất, ngắt–chạy nốt, kiểm metric chính, vẽ LR thật, định nghĩa phần được khác | ĐV §5.1, 5.2, 3.4 |
| 01-9 | "Điều chỉnh trọng số" chưa có đường xuống mã | ĐV §6.2: chỉ đọc tới GĐ4 |
| 01-10 | Bốn bước sau train | ĐV node 6 + H4 bước 5 (phân tích lỗi, ngưỡng, so bản cũ). Tương đương sau xuất, độ trễ trên thiết bị đích: H sau GĐ5 |
| 02-1 | Runtime chưa làm hết hợp đồng; chỉ D-FINE chắc chạy | ĐV §3.2 (không vẽ node giả), §11 (bản đầu: D-FINE) |
| 02-2 | Chia tập, tăng cường không phải bước nền tảng | ĐV §3.1 (mặt trong node 2, ngăn của vòng) |
| 02-3 | Build image sau khi giữ GPU, không có pha; không khai `progress` | ĐV node 1, GĐ0 |
| 02-4 | Chạy khói DETR `epochs=1` đi sai nhánh | ĐV §5.1 lịch nén (GĐ2) |
| 02-5 | Tag không phải digest; lock thiếu | ĐV §2, tab Công thức đã dùng (GĐ1) |
| 02-6 | Sửa handler phải áp cho serve | ĐV §7 giới hạn 4 (GĐ5) |
| 02-7 | Lớp phủ file: phần lớn sửa không cần build | ĐV §3.4 bảng mức giá (GĐ4–5) |
| 02-8 | Ba phép đánh giá, ba con số | ĐV §3.1: Kiểm định ≠ Đánh giá độc lập; mỗi số ghi nguồn |
| 02-9 | Khoá/LR theo khối qua override; ghép lớp phải tự viết | ĐV §6.2 (GĐ4) |
| 02-10 | Dữ liệu tải lại mỗi lần; URL 24 giờ | ĐV kiểm tĩnh §5.1; GĐ0 ký lại, GĐ2 bộ đệm theo máy |
| 03-1 | Lưu điểm bền hỏng với 11 công thức | Điều kiện chặn GĐ0; §5.4 chỉ hiện "mất X phút" khi đã kiểm nạp lại |
| 03-2 | NaN + không ngân sách giờ | ĐV §5.4 |
| 03-3 | Chạy khói cần nhưng không đủ; diễn tập chạy nốt | ĐV §5.1, 5.3 |
| 03-4 | Lần train không chốt bản công thức và image | ĐV §2 (GĐ1) |
| 03-5 | Mã tuỳ ý + container không cách ly | ĐV §7 giới hạn 5; câu hỏi 3; chặn GĐ5 |
| 03-6 | Sửa code ⇒ image mới trên mỗi máy | ĐV §3.4 mức 3–5; tách môi trường khỏi mã là điều kiện GĐ5 |
| 03-7 | Tiến độ theo node là giả; cắt canvas tự do, gộp nhánh | ĐV §3.1, 3.5. Gộp nhánh với gốc: **B** — chỉ huy hiệu + diff |
| 03-8 | URL 24 giờ, dữ liệu theo từng lần | Như 02-10 |
| 03-9 | GPU nguyên chiếc, chưa nhường | ĐV một phần: kiểm tĩnh nói ai giữ GPU. Làn ưu tiên: H, câu hỏi 6 |
| 03-10 | License chưa thực thi; val theo seed | License thừa kế khi tách + chặn ở kiểm tĩnh (GĐ1); chặn lên production: H. Seed: như 01-3 |
| 04-1 | Node theo ranh giới artifact; thêm Môi trường | ĐV §3.1 |
| 04-2 | Giá trị n8n là dữ liệu thật; ngăn ba cột | ĐV §3.3 |
| 04-3 | Canvas có khung | ĐV §3.5 |
| 04-4 | Chạy thử cùng đường mã | ĐV §5.1 |
| 04-5 | "Đã cũ" + lý do + lan truyền + phút | ĐV §3.4 |
| 04-6 | Khoá "bản đã thử" đầy đủ | ĐV §5.2 |
| 04-7 | Nhân bản lần train → nháp; lần train bất biến | ĐV §2 |
| 04-8 | Công tắc cấu hình ↔ mã + diff; bước code ở ranh giới thật | ĐV §7, 3.5 |
| 04-9 | Khoá theo khối cần recipe khai | ĐV §6.2 |
| 04-10 | So sánh bằng bảng + diff văn bản | ĐV §7, 8.4 |

## 11. Lộ trình

Tôi không ước lượng công — đó là việc của nhóm phát triển. Thứ tự dưới đây là thứ tự phụ thuộc.

| GĐ | Người dùng nhận được, dùng ngay | Nền phải vá trước | Cố ý chưa làm |
|---|---|---|---|
| **0 — Nói thật** | Trang Run có **dải khung chỉ đọc** thay thanh bước: pha dựng môi trường có log, % và ETA, vạch NaN, "sập bây giờ mất X phút" đúng sự thật, kế hoạch thực thi | Lưu điểm bền cho tên file cố định và thư mục; shim phát sự kiện giá trị không hữu hạn; khai `progress`; ký lại URL mỗi lần thử; ngân sách giờ | Mọi thứ chỉnh sửa |
| **1 — Thấy công thức** | Trang Recipe có canvas + cây file chỉ đọc. Project có tab Công thức: canvas, lớp Trực quan cho tham số sẵn có, lớp Cấu hình và Code **chỉ đọc**. Kiểm dữ liệu + chia tập ghi vào phiên bản dữ liệu. Nháp/bản chốt, "Nấu lại từ #n", so sánh có điều kiện tập val | Bảng bản chốt; lần train ghi bản chốt + digest; chia tập có seed riêng, báo số nhãn bị loại; license thừa kế | Sửa cấu hình, sửa code |
| **2 — Thử** | Kiểm tĩnh, chạy khói, ước tính dạng khoảng, cổng mở nút Train, ảnh ghim, xem batch, báo cáo nạp trọng số | Chế độ chạy khói của shim + lịch nén; giới hạn mẫu ở bộ chuyển; bộ đệm dữ liệu theo máy; dựng môi trường thành job không giữ GPU | Thuộc lòng; nhiều GPU |
| **3 — Cứu** | Kiểm sớm, điểm kiểm sau epoch đầu, canh liên tục, tạm dừng/chạy nốt, rẽ nhánh, dừng giữ bản tốt, tab Mẫu dự đoán, tab Điểm lưu | Tạm dừng có nhả GPU; giữ điểm lưu theo chính sách; lần train có cha | Tự dừng theo quy tắc do người dùng viết |
| **4 — Chỉnh sâu** | Sửa lớp Cấu hình (có diff, nhãn giá); train tiếp từ model của tôi + bảng ghép lớp; sơ đồ khối với ba mẫu sẵn cho họ DETR, `freeze` cho YOLO; thuộc lòng | Truyền `--extra-json`; validator nhận tham số dạng object; recipe khai khối; bước sửa checkpoint; lớp phủ file lúc chạy | Sửa file repo gốc |
| **5 — Code** | Sửa script keo, bước code ở ba điểm mở, rồi file repo gốc và Dockerfile; lưu thành công thức của workspace | Quyền riêng; container không root, bỏ token khỏi môi trường tiến trình con, hạn mức; lớp phủ áp cho cả serve; dọn image | Gộp với gốc; canvas tự do (thuộc Pipelines) |

Phạm vi họ model: GĐ0–3 làm với D-FINE trước (công thức duy nhất chắc đủ đường chạy [02]), thêm YOLO khi đã chạy thật một lần; các họ khác vào khi công thức của chúng khai đủ `stages` và qua chạy khói. Mặc định tôi giả định mức tái lập là "cùng bản chốt + dữ liệu + seed + digest được ghi", không lưu image [03 câu 3] — nếu chủ sản phẩm cần hơn thì nói.

## 12. Sáu câu chủ sản phẩm phải quyết

1. **Cổng chạy thử cứng hay vượt được?** *Khuyến nghị*: cứng khi đổi cấu hình sâu/code/môi trường; vượt được kèm lý do khi chỉ đổi tham số. *Cứng hoàn toàn*: an toàn, nhưng người rành sẽ bực với mỗi lần đổi batch. *Mềm hoàn toàn*: sẽ lại có lần train hỏng vì thiếu gói.
2. **"Từng dòng code" theo bậc hay mở hết ngay?** *Khuyến nghị*: xem mọi dòng từ GĐ1, sửa theo bậc tới GĐ5. *Mở hết ngay*: đúng lời chủ sản phẩm sớm hơn, đổi lại phải làm cách ly, lớp phủ, dọn image trước mọi thứ khác — người dùng chờ lâu hơn mới có bản dùng được.
3. **Ai được chạy code tuỳ ý trên máy dùng chung?** *Khuyến nghị*: quyền riêng "sửa code công thức", bật theo máy. *Mọi Editor*: đơn giản, nhưng code của một người đọc được dữ liệu và token của người khác [03] — nếu chọn, ghi thành quyết định D-xx.
4. **Bản đầu phục vụ ai, họ model nào?** *Khuyến nghị*: Hà + phát hiện vật thể (D-FINE, rồi YOLO). *Mọi họ cùng lúc*: canvas phải đúng cho LLM/ASR/TTS/bảng trong khi runtime của chúng chưa chắc chạy — vẽ điều máy không làm.
5. **Chấp nhận canvas có khung, canvas tự do để ở tầng Pipelines?** *Khuyến nghị*: có. *Tự do ngay trong một lần train*: giống n8n hơn về hình thức, nhưng trạng thái từng node phải dựng giả và người dùng dựng được luồng không chạy được.
6. **Chạy thử có được chen GPU không?** *Khuyến nghị*: làn ưu tiên cho việc ≤ 10 phút, được tạm dừng bản thử model, không bao giờ đụng deployment production. *Không chen*: trên máy một GPU đang phục vụ, "chạy thử 4–8 phút" thành chờ vô hạn và người dùng sẽ bỏ qua thử. *Chen cả production*: gián đoạn dịch vụ thật.

**Chỗ tôi không chắc:** (1) "Xem batch" trên CPU trong 20–60 giây và vẽ được đường LR trước khi chạy — phụ thuộc mã repo gốc ở commit ghim, chưa ai kiểm; (2) chạy khói 4–8 phút là suy từ 12 phút/epoch, chưa đo; (3) pha con "đang: Kiểm định" cần mẫu log ổn định của từng repo; (4) một nháp dùng chung cho cả project có thể phải đổi thành nháp theo người nếu nhóm đông — cần hỏi người dùng thật.
