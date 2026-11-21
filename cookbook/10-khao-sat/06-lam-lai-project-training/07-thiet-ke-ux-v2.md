# 07 — Thiết kế UX bản 2: Project · Recipe · Chạy thử · Train

## Tóm tắt cho chủ sản phẩm

**Đề xuất.** Bỏ form "New training". Mỗi project có một **canvas công thức**: hình vẽ đúng những việc máy làm khi train; bấm vào đâu thấy dữ liệu thật ở đó và chỉnh ở đó. Canvas có khung sẵn theo recipe, gồm ba đầu vào, một **vòng huấn luyện** mở thành sáu ngăn, ba đầu ra:

```
 ┌1 Môi trường & máy ─┐      ┌4 HUẤN LUYỆN ⟲ ────────────────────────────────────────┐      ┌5 Đóng gói ─┐
 ├2 Dữ liệu ──────────┤ ───▶ │ Batch & tăng cường ▸ Phần được học ▸ Mục tiêu & tối ưu │ ───▶ ├6 Đánh giá ─┤
 └3 Model & điểm      ┘      │ ▸ Lịch học        ⟲ mỗi epoch: Kiểm định ▸ Điểm lưu    │      └7 Xuất ─────┘
    xuất phát                └────────────────────────────────────────────────────────┘
            [▷ Chạy thử · vài phút]        [▶ Train thật · nhiều giờ, ghi khoảng ước tính]
```

Một canvas cho ba lúc: thiết kế, chạy thử (mỗi node báo kết quả thật), train thật (vòng lặp vẫn mở, từng ngăn hiện số liệu sống). Recipe là trung tâm: project tách một bản từ recipe ở Catalog; sửa trên **nháp**; mỗi lần chạy thử hay train tự **chốt** một bản bất biến; "Nấu lại từ #14" lấy bản chốt cũ làm nháp mới. Ở node 3, **Fine-tune** và **Train lại (re-training)** là hai thẻ có tên, có hình, nói rõ trọng số nào đổi.

**Đợt đầu nhận được** (làm với D-FINE): canvas thay form; ảnh thật sau bộ chuyển, phân bố lớp, tập val/test cố định; hai batch sau tăng cường, đường LR, sơ đồ khối của model (ba hình này còn phải kiểm là dựng được từ repo D-FINE); chỉnh ngay được LR, kích thước ảnh, tăng cường, khoá backbone — đi thẳng xuống config của repo; nút Chạy thử đi hết đường train trên vài chục ảnh trước khi mở nút Train; lần train thật có % và ETA, tự dừng khi loss hỏng, chạy nốt được sau sự cố. Xem được mọi file của recipe; chưa sửa code.

**Hai chỗ không làm đúng nguyên văn.**
1. *"Như n8n"*: giữ việc xem dữ liệu thật ở từng node, chạy riêng từng bước thật, đánh dấu chỗ đã cũ; **không** cho kéo dây tự do. Train là một tiến trình có vòng lặp, không phải chuỗi bước rời; cho nối dây là vẽ điều máy không làm.
2. *"Từng dòng code"*: xem từng dòng từ đợt đầu; **sửa** mở theo ba bậc — tham số (đợt 1), file cấu hình (đợt 3), script và code (đợt 4). Sửa code là chạy mã tuỳ ý trên máy dùng chung; cần cách ly trước, trừ khi chủ sản phẩm quyết khác (quyết định 3).

**Sáu quyết định cần đưa ra.**
1. Nút Train khoá cứng khi chưa chạy thử? — *Nên: cứng.*
2. Mở sửa theo thứ tự tham số → cấu hình → code? — *Nên: đúng thứ tự này.*
3. Mọi Editor được chạy code tuỳ ý trên máy chung? — *Nên: không; quyền riêng và cách ly.* Nếu có: sửa script lên sớm một đợt.
4. Bản đầu chỉ họ phát hiện vật thể (D-FINE, rồi YOLO)? — *Nên: có.*
5. Canvas tự do để dành cho tầng nối nhiều lần train (Pipelines)? — *Nên: có.*
6. Chạy thử được chen GPU của bản thử model, không đụng production? — *Nên: có.*

---

## 1. Quy ước của tài liệu

Bản này thay bản 05 sau thẩm định 06 (xử lý từng lỗi ở mục 12). Tôi chỉ đọc repo, không chạy gì.

**Ba nhãn**, gắn vào mọi lời hứa: **[CÓ SẴN]** hệ thống đã có dữ liệu hoặc cơ chế (tôi đã thấy trong mã) · **[CẦN XÂY: …]** nêu điều kiện nền · **[CHƯA CHẮC]** phụ thuộc mã repo nguồn ở commit đã chốt, chưa ai kiểm. Trong bảng, cột "Nền" mang các nhãn này. `Đ1…Đ4` là đợt giao (mục 9). Giao diện thì chỗ nào cũng phải dựng; nhãn nói về dữ liệu và cơ chế đứng sau nó.

**Một bộ số minh hoạ cho mọi wireframe — ước lượng, chưa đo:** dữ liệu pcb v5 có 12 400 ảnh, chia 9 920 / 1 240 / 1 240; D-FINE-S, batch 16 → 620 bước/epoch; ~1,0 bước/s; 12 phút/epoch gồm kiểm định (số của đề bài); 72 epoch ≈ 14,4 giờ. Khoảng hiển thị trước khi train là −20 %…+50 % → **12–22 giờ**; sau epoch thật đầu tiên thu về ±15 %. Chạy thử khi đã có môi trường: **4–8 phút**; lần đầu trên một máy cộng 10–30 phút dựng môi trường.

**Mỗi ký hiệu một nghĩa:** ✔ đã chạy xong · ◇ đã kiểm (không chạy riêng) · ✖ lỗi · ▶ đang chạy · ↻ đã cũ · ✎ đã sửa so với công thức gốc · ⚠ lưu ý.

## 2. Người dùng

| | **Hà — kỹ sư phần mềm mới làm ML** (chính) | **Minh — kỹ sư ML** | **Lan — trưởng nhóm gán nhãn** |
|---|---|---|---|
| Mục tiêu | Có model dùng được mà không phải đọc hết repo nguồn | Kiểm soát đúng thứ mình đổi, so được hai lần train | Biết nhãn của nhóm đã đủ ra model chưa |
| Nỗi sợ | Bấm Train, sáng hôm sau mới biết sai | Nền tảng giấu config, nuốt lỗi | Bị hỏi câu không hiểu |

Chỗ mù hôm nay **[CÓ SẴN — đã đọc mã]**: mục Dữ liệu chỉ in số file và "expected layout: coco" (`TrainSheet.tsx:226`); "Start from" là ô chọn hai giá trị (`:240`); tham số là tên khoá, D-FINE lộ đúng `epochs, batch, seed, use_amp` (`:259`); một nút "Start training", không thử, không ước tính (`:319`); thanh bước không có pha dựng image (`JobProgress.tsx:12`); pha `running` hiện "Working — this step reports no percentage" (`:71`); tab Configuration không có lệnh, config, bản recipe (`Run.tsx:362`); sau khi chạy chỉ có "Train again" và "Stop" (`Projects.tsx:185, 212`); trang recipe chỉ có bảng biến thể và một khối lệnh (`Catalog.tsx:56–93`).

Hà là người dùng chính: đủ sức sửa nhưng không biết sửa gì và lúc nào thì sai; thiết kế cho Hà buộc có lời giải thích *và* đường xuống config, nên phục vụ được Minh. Lan đi **trạng thái lần đầu** của canvas (mục 4.4): ba thứ sáng, phần còn lại thu gọn.

## 3. Từ ngữ

Tám từ chính; mỗi từ một nghĩa:

| Từ / English | Nghĩa | Nền |
|---|---|---|
| **Công thức** / Recipe — *gốc* ở Catalog, *của project* sau khi tách | Cách train một họ model | CÓ SẴN (gốc) · CẦN XÂY: recipe gắn project, nhớ gốc (Đ1) |
| **Nháp** / Draft | Bản đang sửa, **của một người**; một công thức có thể có nhiều nháp | CẦN XÂY: Đ1 |
| **Bản chốt** `r8` / Revision | Ảnh chụp bất biến của nháp, tự tạo khi Chạy thử hoặc Train; dùng chung cả project | CẦN XÂY: bảng bản chốt; lần train ghi bản chốt (Đ1) |
| **Chạy thử** / Trial | Lượt kiểm rẻ trên một bản chốt | CẦN XÂY: Đ1 |
| **Lần train** `#14` / Run | Bản chốt + phiên bản dữ liệu + điểm xuất phát + máy | CÓ SẴN, thêm trường (Đ1) |
| **Điểm lưu** / Checkpoint | Trạng thái lưu giữa chừng | CÓ SẴN nhưng lưu bền hỏng với `last.pth` → CẦN XÂY (Đ1) |
| **Tập test của project** / Project test set | Danh sách mẫu cố định để chấm mọi model của project | CẦN XÂY: Đ1 (lập), Đ3 (chấm theo tập) |
| **Model** `v3` | Kết quả đã đăng ký từ một lần train | CÓ SẴN |

Từ dành riêng, không dùng nghĩa khác: **lớp** = class (như UI Datasets hiện có); **tầng** = layer của model ("khớp 412/418 tầng trọng số"); **nhãn** = annotation; **gốc** = công thức gốc (repo upstream gọi là *repo nguồn*, trọng số pretrained gọi là *model có sẵn*); **ghim** = chỉ cho mẫu theo dõi (điểm lưu thì *giữ lại*); ba **mức chỉnh** Trực quan / Cấu hình / Code (không gọi là "lớp").

```
 Công thức gốc ──tách──▶ Công thức của project ──▶ Nháp của Hà ◀── "Nấu lại từ #14" (mở nháp MỚI từ r8)
 (Catalog)                                          Nháp của Minh
                                                       │ Chạy thử / Train
                                                       ▼
                         Bản chốt r8 ──▶ Chạy thử ✔ ──▶ Lần train #14 ──▶ Điểm lưu ──rẽ nhánh──▶ lần train mới
                         + dữ liệu v5 (val/test cố định) + điểm xuất phát + máy          │
                                                                                         ▼
                                                              Model v3 ── chấm trên Tập test của project
```

Luật: lần train đã chạy không bao giờ đổi; "Nấu lại" không ghi đè nháp nào; lần train cũ từ trước khi có bản chốt hiện "chỉ có tham số, không có bản chốt" **[CÓ SẴN: `paramsJson`]**.

**Tập test của project**: lập một lần ở lần chia tập đầu tiên. Mẫu đã vào test thì ở test mãi qua các phiên bản dữ liệu; dữ liệu mới chỉ chia giữa train và val. Muốn thêm mẫu vào test là hành động riêng, tạo "tập test T2"; điểm cũ ghi rõ "chấm trên T1" và có nút chấm lại.

## 4. Canvas

### 4.1 Bảy node, theo ranh giới thực thi thật

Node tồn tại khi nó chạy riêng được, lưu được kết quả, có trạng thái thật.

| # | Node | Thân node hiện | Nền |
|---|---|---|---|
| 1 | **Môi trường & máy** | Máy, GPU, ai đang giữ GPU; môi trường đã dựng chưa | CÓ SẴN: `getClusterMap`. CẦN XÂY: dựng thành job không giữ GPU, log lên UI (Đ1) |
| 2 | **Dữ liệu** | Phiên bản, số ảnh, thanh chia tập, số lưu ý | CÓ SẴN: phiên bản, số file. CẦN XÂY: chia tập ghi vào phiên bản; báo cáo bộ chuyển (Đ1) |
| 3 | **Model & điểm xuất phát** | Biến thể, kiểu train (mục 5) | CÓ SẴN: biến thể, model có sẵn. CẦN XÂY: từ model của tôi (Đ3) |
| 4 | **Huấn luyện ⟲** | Số epoch × số bước; khi chạy: mục 4.6 | CÓ SẴN: một tiến trình, metric, log |
| 5 | **Đóng gói** | Bản được chọn, kết quả thử nạp lại | CÓ SẴN: đóng gói. CẦN XÂY: thử nạp trong chạy thử (Đ1) |
| 6 | **Đánh giá** | Điểm trên tập test, ảnh sai, ngưỡng | CÓ SẴN: `EvaluateSheet` sau khi đăng ký, chọn mẫu theo đường dẫn. CẦN XÂY: chấm ứng viên chưa đăng ký, theo tập (Đ3) |
| 7 | **Xuất** | File ONNX | CẦN XÂY: control plane chưa từng gọi `export` (Đ3). Trước Đ3 **không vẽ** |

Sáu **ngăn** trong node 4 — chỉnh được, có hình riêng, **không có trạng thái chạy riêng** (dùng ◇, không dùng ✔):

| Ngăn | Hình trên ngăn | Chỉnh được ở Đ1 (tham số khai báo, mục 6) | Nền của hình |
|---|---|---|---|
| **Batch & tăng cường** | Lưới 4 ảnh của một batch thật | batch **[CÓ SẴN]**; kích thước ảnh, bật/tắt lật ngang, tắt tăng cường mạnh **[CHƯA CHẮC tên khoá]** | CẦN XÂY: job soi (4.2) · CHƯA CHẮC: dựng được dataloader trên CPU |
| **Phần được học** | Sơ đồ khối + số tham số | Ba mẫu khoá/học chậm backbone **[CHƯA CHẮC tên khoá]** | CẦN XÂY: job soi · CHƯA CHẮC |
| **Mục tiêu & tối ưu** | Bảng: hàm loss và trọng số, optimizer, EMA, AMP, clip, seed | `use_amp`, `seed` **[CÓ SẴN]**; còn lại chỉ đọc tới Đ3 | CẦN XÂY: job soi đọc config đã gộp |
| **Lịch học** | Đường LR, vạch mốc đổi giai đoạn tăng cường | epoch **[CÓ SẴN]**; LR đỉnh, hệ số LR backbone **[CHƯA CHẮC tên khoá]** | CẦN XÂY: job soi · CHƯA CHẮC: tính được đường LR không cần GPU |
| **Kiểm định** (mỗi epoch) | Metric chính mới nhất + mẫu theo dõi | — | CÓ SẴN: `val/mAP50-95`. Mẫu theo dõi: CẦN XÂY (Đ2) |
| **Điểm lưu** (mỗi epoch) | "Lưu lần cuối x phút trước" | — (D-FINE không có núm tần suất) | CÓ SẴN: danh sách điểm lưu đã tải lên |

Công thức khai danh sách node và ngăn **[CẦN XÂY: trường `stages` trong manifest]**; không khai thì không vẽ. Bản đầu chỉ dựng khung cho họ phát hiện vật thể. Các họ khác đổi ngăn: văn bản thay "Batch & tăng cường" bằng "Tách từ & độ dài"; LLM thay "Phần được học" bằng "Adapter LoRA" và chưa có node 6, 7; ASR thêm "Âm thanh" và "Chuẩn hoá văn bản"; TTS kiểm định bằng nghe thử; bảng không có node 3; công thức chỉ suy luận không có vòng và không hiện ở Project.

### 4.2 Job "soi công thức" — thứ làm canvas có dữ liệu thật

Một job CPU chạy trong image của công thức, kết quả đệm theo (môi trường, biến thể, tham số) **[CẦN XÂY: Đ1; cần môi trường đã dựng]**. Nó trả về năm thứ, mỗi thứ hỏng thì ngăn tương ứng ghi "chưa soi được" chứ không vẽ hình giả:

1. Cây file và nội dung của repo nguồn, script của công thức (chỉ đọc) — lấp lời hứa "xem được mọi dòng".
2. Config đã gộp mà `make_config.py` sẽ sinh **[CÓ SẴN: script chạy không cần GPU]**.
3. Danh sách khối và số tham số **[CHƯA CHẮC]**.
4. Đường LR và các mốc lịch theo epoch **[CHƯA CHẮC]**; chín khoá lịch co theo epoch thì đọc thẳng từ config đã sinh **[CÓ SẴN: `make_config.py:29–39`]**.
5. Hai batch sau tăng cường **[CHƯA CHẮC]**.

Chưa có môi trường trên máy nào: canvas vẫn mở với phần **[CÓ SẴN]** (dữ liệu, biến thể, tham số), các ngăn ghi "dựng môi trường để xem (10–30 phút, không giữ GPU)".

### 4.3 Ngăn chi tiết

Trượt từ phải, không che canvas trên màn hình ≥ 1440 px (hẹp hơn thì đè lên cột đầu ra). Trên cùng là **Chỉnh** và ngay bên cạnh là **Ra** (kết quả thật của lần soi hoặc chạy thử gần nhất) để sửa xong thấy luôn; **Vào** nằm dưới, thu gọn. Kết quả của bản chốt khác thì phủ mờ, ghi "kết quả của r7". Thân node không có ô nhập.

### 4.4 Trạng thái lần đầu

Project chưa có lần train nào: chỉ **Dữ liệu**, **Model & điểm xuất phát** và nút **Chạy thử** sáng; vòng lặp thu thành một dòng "Huấn luyện: 72 epoch, theo mặc định của công thức · Mở"; node 1 là một chip; không hiện mã băm, không hiện số bản chốt. Sau lần chạy thử đầu, vòng lặp tự mở. Công tắc **Giải thích** (bật sẵn) hiện câu lời thường dưới mỗi ô; mục **Nâng cao** trong từng ngăn hiện tên khoá kỹ thuật. Hai thứ này độc lập.

### 4.5 "Đổi cái này thì sao" — ba hệ quả, kèm hệ quả ngữ nghĩa

| Hệ quả | Sửa gì | Giá hiển thị |
|---|---|---|
| **Không cần thử lại** | Tên, ngân sách giờ, số epoch | — |
| **Cần chạy thử** | Mọi thứ khác ở ba mức chỉnh; đổi máy | Số đo của lượt thử trước trên máy đó **[CẦN XÂY: bảng lần thử]**; chưa từng đo thì ghi "chưa đo" |
| **Cần dựng lại môi trường** | Dockerfile, gói, commit repo nguồn (Đ4) | "10–30 phút mỗi máy" |

Node bị ảnh hưởng và các node sau nó mang ↻ kèm lý do bằng câu. **Thanh hậu quả** dưới canvas nói hai điều: giá chạy lại và **điều kéo theo**: "epoch 72 → 96 kéo theo: tắt tăng cường ở epoch 85 (trước: 64); đường LR dài ra" **[CẦN XÂY: so hai config đã sinh — job soi]**. Kết quả chạy thử có hiệu lực **theo từng máy** vì image dựng tại máy và gói chưa cố định phiên bản; chuyển máy là chạy trên môi trường chưa thử, màn hình nói đúng câu đó.

### 4.6 Mức tự do

Bố cục cố định ba cột (đầu vào · vòng lặp · đầu ra), vẽ bằng CSS grid + SVG như `Pipelines.tsx` **[CÓ SẴN]**; không kéo, không phóng to — chỉ thu/mở vòng lặp. Không nối lại dây. Chạy riêng được: Dựng môi trường · Chuẩn bị dữ liệu · Soi · Chạy thử · Đánh giá lại; không chạy riêng một ngăn. Điểm chèn "bước code" chỉ hiện từ Đ4.

**Xung đột với "như n8n":** nêu ở Tóm tắt. Cái giá: không dựng được luồng nhiều nhánh trong một lần train. Phương án thay thế: canvas tự do ở tầng Pipelines, nơi mỗi node đúng là một job.

### 4.7 Wireframe

**(a) Thiết kế, Đ1** — nháp có hai thay đổi; máy duy nhất đang bận:

```
 PCB defects ›  Công thức │ Lần train (14) │ Models (3)                 nháp của Hà · 2 ✎ so với r8
 [D-FINE ▾]                          [▷ Chạy thử · chờ GPU ▾]   [▶ Train thật · cần chạy thử]
                                      └ pc-02 đang train #14, còn 10–14 giờ:
                                        ○ làm ngay việc không cần GPU (kiểm nhanh, soi lại)
                                        ○ xếp hàng chạy thử sau #14     ○ tạm dừng #14 để thử (Đ2)
 ┌1 pc-02 · RTX 3060 12 GB ───┐   ┌4 HUẤN LUYỆN ⟲  72 epoch × 620 bước ─────────────────────────────┐   ┌5 Đóng gói ────┐
 │ môi trường đã dựng         │   │ ┌Batch & tăng cường┐ ┌Phần được học ──┐ ┌Mục tiêu & tối ưu ┐     │   │ bản tốt nhất  │
 └────────────────────────────┘   │ │ [▦][▦][▦][▦]     │ │ [chậm][đủ][đủ] │ │ AdamW · EMA bật  │     │   └───────────────┘
 ┌2 pcb v5 · 12 400 ảnh ──────┐   │ │ ảnh 640 → 800 ✎ ↻│ │ [mới: 7 lớp]   │ │ AMP bật          │     │   ┌6 Đánh giá ────┐
 │ ▮▮▮▮▮▮▮▮▯▯ 9 920/1 240/1 240│──▶│ └──────────────────┘ └────────────────┘ └──────────────────┘     │──▶│ sau khi đăng  │
 │ ⚠ 2 lưu ý                  │   │ ┌Lịch học ─────────────────┐  ⟲ mỗi epoch                        │   │ ký model      │
 └────────────────────────────┘   │ │ ╱‾‾‾╲____ ┆tắt tăng cường│  ┌Kiểm định ────┐ ┌Điểm lưu ───┐    │   └───────────────┘
 ┌3 dfine-s · FINE-TUNE ──────┐   │ │ LR đỉnh 2e-4 → 1e-4 ✎    │  │ 1 240 ảnh val│ │ mỗi epoch  │    │
 │ từ model có sẵn (COCO)     │   │ └──────────────────────────┘  └──────────────┘ └────────────┘    │
 └────────────────────────────┘   └──────────────────────────────────────────────────────────────────┘
 Thanh hậu quả: ảnh 640 → 800 cần chạy thử (4–8 phút); kéo theo: bộ nhớ GPU tăng, chưa đo.   [Xem 2 thay đổi] [Hoàn tác]
```

**(b) Sau chạy thử, Đ1:**

```
 Chạy thử r9 trên pc-02 · 6 ph 05 s · 64 ảnh (8 mẫu theo dõi + 6 ảnh nhiều vật thể nhất + ngẫu nhiên)
 1 ✔ 0:02 môi trường có sẵn        ┌4 ✔ 4:10  lịch nén: 2 giai đoạn × 30 bước ─────────────────────────┐   5 ✔ 0:35
 2 ✔ 0:48 64 ảnh · bỏ 3 nhãn       │ ◇ Batch: 2 batch thật [Xem]   ◇ Phần được học: 10,2 M tham số     │   nạp lại được;
   16 ảnh sau bộ chuyển [Xem]      │ ◇ Tối ưu: loss 41 → 29, không có giá trị hỏng                     │   dự đoán 8 mẫu
 3 ✔ 0:06 khớp 412/418 tầng        │ ◇ Lịch: đi qua cả 2 giai đoạn  ◇ Kiểm định: có val/mAP50-95       │   theo dõi: hợp lệ
   6 tầng tạo mới (đầu ra 7 lớp)   │ ◇ Điểm lưu: lưu → ngắt → chạy nốt được                            │   (0 khung là bình
                                   │ 0,7 bước/s · bộ nhớ GPU đỉnh 9,1 / 12 GB (dư 2,9 GB)              │   thường lúc này)
                                   └───────────────────────────────────────────────────────────────────┘
 Train thật: 16–30 giờ (ước lượng từ 0,7 bước/s + kiểm định; thu hẹp sau epoch đầu)   [▶ Train thật]
 Chưa bảo đảm: chất lượng model · loss hỏng muộn · ảnh nặng hơn 6 ảnh đã thử · thời gian (có thể lệch gấp rưỡi)
```

**(c) Đang train thật** — trang Run, cùng canvas, vòng lặp mở; mỗi ngăn hiện dữ liệu sống:

```
 #15 dfine-s-15 · r9 · pcb v5 · pc-02     [■ Dừng]   Đ2: [⏸ Tạm dừng] [⑂ Rẽ nhánh] [Dừng, giữ bản tốt nhất]
 1 ✔  2 ✔ 1:40  3 ✔ 0:06
 ┌4 ▶ HUẤN LUYỆN  epoch 12/72 · bước 310/620 · còn 14–19 giờ ───────────────────────────────────────┐   5 chờ
 │ ┌Batch & tăng cường ──┐ ┌Mục tiêu & tối ưu ─────┐ ┌Lịch học ────────────────────────┐          │   6 sau khi
 │ │ giai đoạn 1/2:      │ │ loss ▇▆▅▄▄▃  29,4     │ │ ╱‾‾‾●╲_____  ┆ epoch 64: tắt    │          │     đăng ký
 │ │ tăng cường mạnh     │ │ bước gần nhất: 0,7/s  │ │ LR hiện tại 9,6e-5  tăng cường  │          │
 │ └─────────────────────┘ └───────────────────────┘ └─────────────────────────────────┘          │
 │ ⟲ ┌Kiểm định ───────────────────────────┐ ┌Điểm lưu ─────────────────────────┐                 │
 │   │ val/mAP50-95 ▁▂▃▅▆ 0,41 (epoch 11)  │ │ đã tải lên 9 phút trước (epoch 11)│                 │
 │   │ mẫu theo dõi [▦][▦][▦][▦] (Đ2)      │ │ sập bây giờ: mất tối đa 9 phút    │                 │
 │   └─────────────────────────────────────┘ └──────────────────────────────────┘                 │
 └──────────────────────────────────────────────────────────────────────────────────────────────────┘
 ✔ loss 5 phút đầu giống lần chạy thử   ✔ báo cáo epoch đầu   ○ ngân sách 24 giờ (Đ2)
```

Tín hiệu trong (c) và nền của chúng: số epoch **[CẦN XÂY: khai `progress` trong manifest; cơ chế shim CÓ SẴN, `Runner.cs:65–86`]** · bước trong epoch = bước toàn cục chia số bước/epoch **[CHƯA CHẮC: bước của TensorBoard có khớp]** · loss, LR hiện tại, mAP **[CÓ SẴN: `train/loss`, `lr/pg0`, `val/mAP50-95` trong manifest D-FINE]** · giai đoạn tăng cường suy từ epoch hiện tại và mốc trong config đã sinh **[CÓ SẴN sau job soi]** · "đang train hay đang kiểm định" suy từ log **[CHƯA CHẮC — không vẽ cho tới khi kiểm]** · điểm lưu đã tải lên **[CẦN XÂY: sửa lưu bền, Đ1]**.

## 5. Fine-tune, Train lại, và điều chỉnh trọng số

### 5.1 Hai thẻ có tên ở node 3

Mở node **Model & điểm xuất phát** thấy, từ trên xuống: (1) bộ chọn **biến thể** — bảng n/s/m/l/x với số tham số, bộ nhớ GPU tối thiểu, điểm công bố **[CÓ SẴN: manifest]**, và tốc độ đo trên máy này nếu đã từng chạy thử **[CẦN XÂY: Đ1]**; (2) hai thẻ cạnh nhau:

```
┌ FINE-TUNE  ·  tinh chỉnh ───────────────────────┐  ┌ TRAIN LẠI  ·  re-training ─────────────────────┐
│ Bắt đầu từ trọng số đã học, chỉnh cho dữ liệu   │  │ Làm lại lần train #14 trên dữ liệu đã cập nhật │
│ của bạn.                                        │  │ — cùng công thức r8, cùng điểm xuất phát.      │
│ Từ:  (●) model có sẵn của biến thể (COCO)       │  │ Dữ liệu: pcb v6 (toàn bộ cũ + mới)             │
│      ( ) model của tôi: pcb-detector v3  (Đ3)   │  │                                                │
│ Trọng số: [khoá][chậm ×0,1][đủ][mới: 1/7 lớp]   │  │ Trọng số: học lại tất cả như lần #14           │
│ Lịch: ngắn hơn, LR thấp hơn                     │  │ Lịch: đủ 72 epoch                              │
│ Rủi ro: quên trường hợp cũ nếu thiếu dữ liệu cũ │  │ Rủi ro: tốn giờ máy nhất; đổi lại sạch, so     │
│                                                 │  │ được với #14, tái lập được                     │
│ ~4–7 giờ (ước lượng, chưa đo)                   │  │ ~12–22 giờ (ước lượng, chưa đo)                │
└─────────────────────────────────────────────────┘  └────────────────────────────────────────────────┘
  Khác: Train từ đầu — với D-FINE ghi đúng là "chỉ giữ backbone đã học sẵn"
```

| Lựa chọn | Nghĩa trên máy | Nền |
|---|---|---|
| **Fine-tune từ model có sẵn** (mặc định lần đầu) | Nạp trọng số của biến thể, học tất cả | CÓ SẴN |
| **Fine-tune từ model của tôi** | Nạp trọng số model đã đăng ký hoặc một điểm lưu; optimizer và lịch mới | CẦN XÂY: `StartTraining` chỉ nhận trọng số `https://` của biến thể; bước ghép lớp trên checkpoint (Đ3). Trước Đ3 lựa chọn hiện mờ, ghi "chưa có" |
| **Train lại** | Bản chốt của lần train được chọn + phiên bản dữ liệu mới, điểm xuất phát giữ nguyên | CẦN XÂY: bản chốt (Đ1). Phần máy CÓ SẴN |
| **Train từ đầu** | Không nạp trọng số của biến thể | CÓ SẴN: `fromScratch`; nhãn sửa theo từng công thức |

Thẻ Train lại mờ khi project chưa có lần train nào, ghi "chưa có lần train để làm lại". **Chạy nốt** và **Rẽ nhánh** không nằm ở đây — chúng là hành động trên một lần train (mục 7.3). Cột "Kiểu" của bảng lần train dùng đúng các tên này.

**Hai câu hỏi dẫn đường** hiện phía trên hai thẻ khi project đã có model, chỉ để tô sáng một thẻ, không thay thẻ: *"Dữ liệu lần này là toàn bộ cũ + mới, hay chỉ phần mới?"* và *"Model mới còn phải làm tốt các trường hợp cũ không?"*. Toàn bộ + cần giữ cái cũ → tô Train lại, ghi "an toàn nhất"; thiếu giờ máy → Fine-tune từ model của tôi; chỉ có phần mới + cần giữ cái cũ → thẻ đỏ "model sẽ quên trường hợp cũ; thêm dữ liệu cũ hoặc xác nhận chấp nhận". Danh sách lớp đổi thì nền tảng tự so và báo, không hỏi **[CÓ SẴN: lớp của phiên bản dữ liệu; CẦN XÂY: so với lớp của model, Đ3]**.

### 5.2 Điều chỉnh trọng số (ngăn Phần được học)

```
 Ảnh ─▶ [ Backbone: chậm ×0,1 ]─▶[ Encoder: đủ ]─▶[ Decoder: đủ ]─▶[ Đầu ra: mới cho 7 lớp ]
          5,2 M                     2,1 M             2,6 M            0,3 M
 Mẫu:  ( ) Dữ liệu ít — khoá backbone   (●) Cân bằng — backbone học chậm   ( ) Dữ liệu nhiều — học tất cả
```

- **Đ1**: ba mẫu, đi xuống config qua tham số khai báo **[CẦN XÂY: mục 6 bậc 1 · CHƯA CHẮC: khoá `freeze_at`, nhóm LR backbone của D-FINE ở commit đã chốt]**. Dưới 500 ảnh train thì chọn sẵn "Dữ liệu ít" **[CÓ SẴN: số mẫu]**. Số tham số từng khối từ job soi **[CHƯA CHẮC]**; chưa soi được thì vẽ khối không có số.
- **Đ3**: bấm từng khối đổi khoá / chậm (hệ số) / đủ; "mới" do nền tảng đặt khi lớp đổi. Kèm **bảng ghép lớp** cũ → mới (giữ / đổi tên / gộp / bỏ / mới) khi fine-tune từ model của tôi **[CẦN XÂY: Đ3]**.
- Kiểm chứng: chạy thử báo "khớp 412/418 tầng trọng số; 6 tầng tạo mới" **[CẦN XÂY: đọc báo cáo nạp từ log · CHƯA CHẮC: repo nguồn có in]**.

## 6. Ba bậc chỉnh

Trong ngăn chi tiết, thanh lật ba mức — **Trực quan · Cấu hình · Code** — trên cùng một node. Xem được cả ba từ Đ1 (nhờ job soi); **sửa** mở theo bậc:

| Bậc | Sửa gì | Đường xuống mã | Rủi ro | Đợt |
|---|---|---|---|---|
| **1. Tham số khai báo** | Ô ở mức Trực quan: LR đỉnh, hệ số LR backbone, kích thước ảnh, bật/tắt phép tăng cường, mẫu khoá | Manifest khai thêm tham số kiểu giá trị đơn; lệnh train chèn vào `make_config.py --extra-json '{"a.b": giá trị}'` | Không phải mã tuỳ ý: chỉ khoá đã khai, có kiểu, có khoảng | **Đ1** |
| **2. File cấu hình** | Mức Cấu hình: sửa YAML sẽ sinh, mọi khoá của repo nguồn | File phủ áp sau `--extra-json` | Vẫn là dữ liệu, không phải lệnh; khoá sai → hỏng lúc chạy thử | **Đ3** |
| **3. Lệnh và code** | `command` trong manifest, script của công thức, bước code, file repo nguồn, Dockerfile | Lớp phủ file lúc chạy; dựng lại môi trường | Mã tuỳ ý trên máy dùng chung | **Đ4**, sau cách ly |

Nền bậc 1: `--extra-json` đã có trong `make_config.py:94, 127` nhận đường dẫn dạng chấm tới giá trị đơn **[CÓ SẴN]**; lệnh train của d-fine chưa truyền **[CẦN XÂY]**; tên khoá config **[CHƯA CHẮC]** — phải soi config của commit đã chốt trước khi khai. Vì là giá trị đơn nên không vướng việc validator từ chối tham số dạng object. Với YOLO bậc 1 phần lớn **[CÓ SẴN]**: manifest đã khai `imgsz, lr0, optimizer, patience`.

Ở mức Cấu hình, dòng kế thừa từ repo nguồn màu mờ, dòng nền tảng đặt màu thường, dòng người dùng đổi có ✎. Khoá chỉ sửa được ở bậc 2 hiện ở mức Trực quan thành chip "1 chỉnh sửa ở mức Cấu hình". Khác biệt xem bằng **bảng và diff văn bản** ("2 ✎ so với r8 · 5 ✎ so với công thức gốc"). Hoàn tác hai kiểu: đưa một ô về giá trị gốc; bỏ nháp.

**Xem trọn công thức như một tài liệu**: trang Recipe có tab **Toàn văn** — manifest đầy đủ **[CÓ SẴN: `manifestJson`]**, Dockerfile và script **[CẦN XÂY: job soi hoặc đọc từ đĩa control plane]**.

**Giới hạn của sửa code trong trình duyệt** (ghi trong mức Code): trình soạn file, không phải IDE; lỗi logic chỉ lộ khi chạy thử; sửa code tắt kiểm nhanh của node đó nên chạy thử là bắt buộc; sửa handler đổi cách model được phục vụ nên model phải nhớ bản chốt sinh ra nó; cần quyền riêng. "Bước code" có hợp đồng cố định: nhận một thư mục vào, ghi một thư mục ra, không mạng — định nghĩa chi tiết để Đ4.

## 7. Thử trước, canh trong lúc chạy

### 7.1 Một nút Chạy thử, hai phần

| Phần | Bao lâu | Làm gì | Nền |
|---|---|---|---|
| **Kiểm nhanh** | vài giây, không GPU | Lệnh dựng được · có đủ tập · số lớp khớp · máy đủ bộ nhớ GPU theo `minVramGb` · ai giữ GPU · license đã xác nhận | CÓ SẴN: `renderTemplate`, `getClusterMap`, `requiresAck`. CẦN XÂY: gom thành một lượt (Đ1) |
| **Chạy khói** | 4–8 phút | Cùng đường mã với train thật: tải mẫu con → nạp trọng số → vài chục bước qua mọi giai đoạn của lịch → kiểm định → lưu → **ngắt rồi chạy nốt** → đóng gói → nạp lại bằng handler → dự đoán mẫu theo dõi | CẦN XÂY (Đ1): chế độ chạy khói của shim; giới hạn mẫu trong bộ chuyển; **lịch nén** cho họ DETR (`epochs=1` làm mọi mốc lịch về 1); bảng lần thử |

Mẫu con gồm 8 mẫu theo dõi (nền tảng tự chọn: rải đều các lớp; đổi được), các ảnh nhiều vật thể nhất, phần còn lại ngẫu nhiên. **Thất bại** khi một bước bắt buộc (node 1–5) hỏng hoặc metric chính không xuất hiện. Sau vài chục bước, mẫu theo dõi **không** dùng để chấm chất lượng: tiêu chí chỉ là handler trả kết quả hợp lệ.

**Kiểm dữ liệu** thuộc node 2, tự chạy trên CPU khi đổi phiên bản: phân bố lớp theo tập, số nhãn bị bộ chuyển loại kèm lý do, **16 ảnh sau bộ chuyển** (bắt lệch id lớp), cảnh báo khi tên file cho thấy ảnh liền nhau từ cùng video ("chia ngẫu nhiên sẽ rò rỉ; chia theo thư mục") **[CẦN XÂY: Đ1 — chuẩn bị dữ liệu thành bước riêng có báo cáo]**. Ảnh gần trùng và vật thể quá nhỏ ở kích thước vào: Đ3.

**Thuộc lòng** (train 8–16 ảnh xem model có học nổi): Đ3.

### 7.2 Cổng và lời nói thật

Nút Train mở khi có lượt chạy thử đạt với cùng bản chốt (trừ nhóm "không cần thử lại"), cùng phiên bản dữ liệu, cùng điểm xuất phát, **trên cùng máy**. Lệch gì thì nút ghi thứ đó **[CẦN XÂY: bảng lần thử, Đ1]**. Dòng "chưa bảo đảm" (wireframe b) viết theo ngữ cảnh từ số đo của lượt thử, không phải khối chữ cố định. Ước tính luôn là khoảng, ghi nguồn, rộng đúng bằng sai số đã công bố ở mục 1.

Máy duy nhất đang bận: nút ghi thật và đưa ba lối (wireframe a).

### 7.3 Lưới an toàn khi train thật

| Thứ | Người dùng thấy | Nền | Đợt |
|---|---|---|---|
| % và ETA | "epoch 12/72 · còn 14–19 giờ" | CẦN XÂY: khai `progress` | Đ1 |
| Loss hỏng | Vạch đỏ trên đường loss; lần train **tự dừng**, giữ điểm lưu cuối | CẦN XÂY: shim phát sự kiện thay vì bỏ lặng lẽ (`Metrics.cs:80`). Lệnh Stop CÓ SẴN | Đ1 |
| So với chạy thử | "Loss 5 phút đầu giống lần chạy thử" hoặc "khác hẳn — xem" | CÓ SẴN: `train/loss` theo bước. CẦN XÂY: lưu đường loss của lượt thử | Đ1 |
| Báo cáo epoch đầu | Bốn dòng: metric chính là số hữu hạn · điểm lưu đã tải lên và nạp lại được · tốc độ so với ước tính · ETA mới | CẦN XÂY | Đ1 |
| Chạy nốt sau sự cố | Nút "Resume from checkpoint" hiện có hoạt động thật | CẦN XÂY: khoá lưu bền theo thứ tự tải lên, kiểm sha khi khôi phục | Đ1 |
| Mẫu theo dõi qua từng epoch | Cùng 8 ảnh, thanh trượt epoch | CẦN XÂY: tiến trình phụ nạp điểm lưu mới nhất bằng handler, tính cả bộ nhớ GPU của nó · CHƯA CHẮC: `last.pth` nạp được giữa chừng | Đ2 |
| Tín hiệu mềm | "Metric chính không khá hơn 12 lần kiểm định"; "GPU nhàn"; "chậm hơn ước tính 50 %" | CÓ SẴN: metric, mẫu GPU 15 s. CẦN XÂY: luật. D-FINE không ghi loss val nên **không có** luật "val xấu dần trong khi train giảm" | Đ2 |
| Dừng sớm, ngân sách giờ | Do nền tảng thực thi: hết kiên nhẫn hoặc quá trần thì dừng, giữ bản tốt nhất | CẦN XÂY: `TimeoutSeconds` chưa được thực thi; D-FINE không có núm dừng sớm | Đ2 |
| Tạm dừng → Chạy nốt | Cùng lần train, không đổi gì, nhả GPU | CẦN XÂY: dừng có chờ điểm lưu | Đ2 |
| Rẽ nhánh từ điểm lưu | Lần train mới có cha; sửa được tham số; biểu đồ nối tiếp | CẦN XÂY: lần train có cha; nạp điểm lưu làm trọng số; giữ lại điểm lưu | Đ2 |
| Thông báo khi vắng mặt | Trong app **[CÓ SẴN]**; email | CẦN XÂY: M16 mới có kênh trong app | Đ2 |

Điểm lưu nào còn để rẽ nhánh: D-FINE chỉ giữ bản cuối và bản tốt nhất; nền tảng giữ 3 bản bền gần nhất [03]. Tab Điểm lưu liệt kê **đúng những bản còn**, mỗi bản có nút "Giữ lại"; không hứa rẽ nhánh từ epoch tuỳ ý.

**Thẻ "Từ lúc bạn rời đi"** (đầu trang Run, Đ2) **[CÓ SẴN: sự kiện, metric; CẦN XÂY: tổng hợp]**: ba dòng sự kiện và một đề nghị có số, ví dụ "Bản tốt nhất ở epoch 52 (mAP 0,53). 13 epoch sau không khá hơn. Chạy hết còn ~1,4 giờ. Đề nghị: Dừng, giữ bản tốt nhất." Hành động hợp với từng tín hiệu:

| Tín hiệu | Đề nghị |
|---|---|
| Loss hỏng | Rẽ nhánh từ điểm lưu cuối với LR đỉnh thấp hơn. Chạy nốt nhiều khả năng hỏng lại |
| Metric đứng hoặc giảm | Dừng, giữ bản tốt nhất; hoặc rẽ nhánh từ bản tốt nhất với LR thấp hơn |
| Quá ngân sách, chưa tới trần | Mốc ngân sách màu cảnh báo: "quá 40 phút; trần cứng ở 1,2×" |
| Máy mất tín hiệu | "Chờ máy quay lại" hoặc "chuyển máy: môi trường ở máy đó chưa được thử" |

## 8. Ba trang

**Project.** Ba tab: **Công thức** (canvas) · **Lần train** · **Models**. Vào project: có lần train đang chạy thì mở thẳng trang của nó; không thì mở Công thức. Tab Lần train là bảng hiện có **[CÓ SẴN: `Projects.tsx`]** thêm cột Bản chốt và Kiểu; "Train again" đổi thành "Nấu lại từ #n"; lần rẽ nhánh thụt dưới cha (Đ2). Rỗng: ba thẻ công thức xếp theo "đã chạy được trên nền tảng" trước — bản đầu chỉ D-FINE mang dấu này, các thẻ khác ghi "chưa kiểm trên nền tảng" **[CÓ SẴN: `verification` trong manifest]**; mỗi thẻ ghi bộ nhớ GPU cần so với máy đang có **[CÓ SẴN: `minVramGb`, `getClusterMap`]**. `TrainSheet` bị bỏ ở Đ1.

**Recipe trong Catalog.** Cùng canvas, chỉ đọc. Tab: **Luồng** · **Biến thể** **[CÓ SẴN]** · **Toàn văn** (mục 6) · **Đang dùng ở**. Đầu trang ghi độ chín thật và danh sách `openItems` **[CÓ SẴN]**. Nút chính: "Dùng cho project…".

**Run.** Trên cùng: thẻ "Từ lúc bạn rời đi" (Đ2), các hành động, rồi **canvas với vòng lặp mở** (wireframe c) thay `PhaseBar`. Tab: **Tổng quan** — giữ nguyên biểu đồ metric chính, dòng sự kiện, các lần thử lại **[CÓ SẴN: `Overview` trong `Run.tsx`]** · **Metric** **[CÓ SẴN]** thêm vạch loss hỏng · **Mẫu theo dõi** (Đ2) · **Log** **[CÓ SẴN]** · **Máy đã chạy gì** — bản chốt, khác gì so với lần trước, lệnh đã dựng, config đã sinh, bảng chia tập, tag môi trường **[CÓ SẴN một phần: lệnh; CẦN XÂY: lưu config và bản chốt theo lần train, Đ1]** · **Điểm lưu** · **File** **[CÓ SẴN]**. Chờ: node 1 ghi lý do ("đang dựng môi trường, đã 4 phút; lần đầu thường 10–30 phút"). Lỗi: node hỏng tô đỏ; banner lỗi hiện có nói lỗi thuộc node nào. Xong: banner đăng ký model hiện có; node 6 dẫn tới Đánh giá.

**Hai con số của một model.** Mọi nơi hiện điểm đều kèm nhãn nguồn: `val · lúc train` hoặc `test · đánh giá độc lập · ngưỡng 0,25`. Bảng lần train xếp theo số thứ nhất, tab Models theo số thứ hai; trang Run đặt hai số cạnh nhau với một câu: "số test thấp hơn vì chấm qua đường phục vụ thật". Ngăn chi tiết node 6 dùng lại nội dung `EvaluateSheet`: bảng ngưỡng, ma trận nhầm lẫn, ảnh sai **[CÓ SẴN cho phát hiện và phân loại]**.

**So sánh hai lần train.** (1) dòng điều kiện "cùng tập val ✔ / ✖ số không so được" **[CẦN XÂY: chia tập ghi vào phiên bản, Đ1]**; (2) bảng chỉ gồm dòng khác nhau, nhóm theo node; diff văn bản cho config; (3) biểu đồ chồng **[CÓ SẴN]**; (4) mẫu theo dõi cạnh nhau (Đ2).

## 9. Lộ trình bốn đợt — mỗi đợt kết thúc bằng một hành trình đi trọn

Tôi không ước lượng công. Vá nền đi cùng đợt cần nó; không có đợt nào chỉ vá nền.

| Đợt | Người dùng nhận được | Nền đi kèm | Hành trình đi trọn |
|---|---|---|---|
| **1. Canvas thật, chỉnh nông, chạy thử** (D-FINE) | Tab Công thức thay `TrainSheet`. Canvas node 1–5 có hình thật ở các ngăn. Kiểm dữ liệu, tập val/test cố định. Chọn biến thể. Thẻ Fine-tune (từ model có sẵn) và Train lại. Sửa tham số bậc 1. Chạy thử và cổng nút Train. Trang Run dùng cùng canvas, có % và ETA, tự dừng khi loss hỏng, báo cáo epoch đầu, chạy nốt sau sự cố. Nháp, bản chốt, Nấu lại, So sánh. Xem mọi file (chỉ đọc) | Bảng bản chốt; lần train ghi bản chốt · chia tập ghi vào phiên bản dữ liệu bằng seed riêng, bộ chuyển trả báo cáo · dựng môi trường thành job không giữ GPU, log lên UI · job soi · manifest D-FINE khai tham số bậc 1, truyền `--extra-json`, khai `progress` · shim: chế độ chạy khói, lịch nén, sự kiện giá trị không hữu hạn · lưu bền điểm lưu theo thứ tự tải lên, kiểm sha · bảng lần thử | **H1, H2** |
| **2. Theo dõi và cứu** (thêm YOLO) | Mẫu theo dõi qua từng epoch. Tín hiệu mềm, dừng sớm, ngân sách giờ. Tạm dừng → Chạy nốt. Rẽ nhánh. Dừng, giữ bản tốt nhất. Thẻ "Từ lúc bạn rời đi". Email. Tạm dừng lần train để chạy thử | Tiến trình phụ dự đoán từ điểm lưu · luật canh · thực thi ngân sách · dừng có chờ điểm lưu · lần train có cha, nạp điểm lưu làm trọng số, giữ lại điểm lưu · ký lại URL dữ liệu mỗi lần thử lại (đang hết hạn sau 24 giờ) · bộ đệm dữ liệu theo máy · kênh email của M16 | **H5** |
| **3. Model của tôi, cấu hình sâu, sau train** | Fine-tune từ model của tôi, bảng ghép lớp, chỉnh từng khối. Sửa file cấu hình (bậc 2). Node 6 tự chấm ứng viên trên Tập test của project, đặt cạnh model đang dùng. Node 7 Xuất. Thuộc lòng. Ảnh gần trùng. Mở các họ khác khi công thức khai `stages` và qua chạy thử | `StartTraining` nhận model hoặc điểm lưu · bước ghép lớp trên checkpoint · file phủ cấu hình · đánh giá theo tập, không cần đăng ký · chuỗi sau train gọi `export` · điểm lưu dạng thư mục (họ HF) | **H4** |
| **4. Lệnh và code** | Sửa `command`, script, bước code, file repo nguồn, Dockerfile (bậc 3). Lưu thành công thức của workspace | Quyền riêng · container không root, bỏ token và URL khỏi môi trường tiến trình con, hạn mức · lớp phủ file cho cả train lẫn serve; model nhớ bản chốt · image theo digest, cố định phiên bản gói, dọn image · license thừa kế | **H3** |

**Nếu Đ1 quá lớn**, cắt theo thứ tự: hai batch sau tăng cường và số tham số theo khối (đều [CHƯA CHẮC]) → thẻ Train lại (vẫn làm được bằng Nấu lại rồi đổi dữ liệu). Không cắt: canvas thay form, tham số bậc 1, chạy thử, tập val cố định, bản chốt, sửa lưu bền.

**Biến thể theo quyết định 3.** Nếu chủ sản phẩm chọn "mọi Editor được chạy code tuỳ ý" (ghi thành D-xx): sửa script của công thức và bước code lên **Đ3**, không chờ cách ly; file repo nguồn và Dockerfile vẫn ở Đ4 vì cần cơ chế image. Nếu chọn quyền riêng và cách ly (khuyến nghị): như bảng.

**Cố ý ngoài bốn đợt:** canvas tự do (thuộc Pipelines) · gộp với công thức gốc · chưng cất, học tăng dần · nối HPO (M10) với bản chốt · gửi ảnh sai về gán nhãn · bước Triển khai ngay trên canvas (node 7 chỉ dẫn link sang Deployments) · kiểm tương đương sau xuất, đo độ trễ trên thiết bị đích · nhập nhánh git làm công thức · chia tập theo nhóm đầy đủ · chạy thử nhiều GPU · lưu image để tái lập giống hệt · chính sách đĩa (tạm lấy: bản cuối + bản tốt nhất + 3 bản bền).

### H1 (Đ1) — Hà, lần đầu, dữ liệu vừa gán nhãn

1. Ở ngăn tạo phiên bản dữ liệu từ nhãn (`Labeling.tsx`, `ExportSheet`) **bấm** "Train với phiên bản này" **[CẦN XÂY]** → project mở tab Công thức, rỗng · *tức thì*.
2. **Thấy** ba thẻ; chỉ D-FINE mang dấu "đã chạy được trên nền tảng" → **chọn** → canvas ở trạng thái lần đầu · *1–2 s*.
3. Node Dữ liệu tự kiểm → **thấy** 16 ảnh sau bộ chuyển, phân bố lớp, lưu ý "chưa chia tập: đề nghị 80/10/10; phần test thành Tập test của project" → **bấm** "Chia và lưu thành v5" · *vài giây tới vài phút, chưa đo*.
4. Node 3: `dfine-s` chọn sẵn theo bộ nhớ GPU của máy; thẻ Fine-tune sáng, Train lại mờ. Không đụng.
5. **Bấm** Chạy thử → kiểm nhanh *vài giây* → máy chưa có môi trường: node 1 ghi "đang dựng, lần đầu 10–30 phút, không giữ GPU", log chạy trực tiếp → dựng xong, job soi làm các ngăn sáng dần → chạy khói *4–8 phút*. Lần đầu tổng cộng *15–40 phút*; từ lần sau *4–8 phút*.
6. **Thấy** màn hình như wireframe (b), ở đây đo được 1,0 bước/s nên ước tính 12–22 giờ → **bấm** Train thật → sang trang Run.
7. *Phút thứ 5*: "loss giống lần chạy thử". *Khoảng phút 12*: báo cáo epoch đầu. Hà rời máy; thông báo nằm trong app chờ Hà quay lại (email: Đ2). Máy sập giữa chừng: tự chạy nốt từ điểm lưu cuối.
8. Xong: banner Đăng ký model **[CÓ SẴN]**; node 6 mở Đánh giá **[CÓ SẴN]**.

### H2 (Đ1) — sửa tham số rồi train lại

1. Bảng lần train → "Nấu lại từ #14" → nháp mới từ r8; nháp khác còn nguyên.
2. Ngăn Lịch học: LR đỉnh 2e-4 → 1e-4 → job soi chạy lại *10–30 s, chưa đo* → đường LR mới chồng đường cũ; thanh hậu quả "cần chạy thử".
3. Ngăn Batch & tăng cường: ảnh 640 → 800 → lưới batch vẽ lại; "kéo theo: bộ nhớ GPU tăng, chưa đo".
4. Chạy thử (tự chốt r9) *4–8 phút* → bộ nhớ đỉnh 9,1/12 GB, 0,7 bước/s, ước tính 16–30 giờ → Train → #15.
5. Chọn #14 và #15 → So sánh: "cùng tập val ✔", bảng hai dòng khác nhau.

Nhánh "dữ liệu có v6": node 3 → thẻ **Train lại** "làm lại #14 trên v6" → node 2 mang ↻ → Chạy thử → Train.

### H5 (Đ2) — lần train nhiều giờ gặp sự cố

*Ca A, loss hỏng.* (1) Email và trong app: "#15 tự dừng: loss hỏng ở epoch 45". Trang Run: thẻ "Từ lúc bạn rời đi", vạch đỏ, "điểm lưu cuối: epoch 44, đã tải lên; GPU đã nhả". (2) Ba lựa chọn kèm hậu quả (mục 7.3). (3) **Bấm** Rẽ nhánh → nháp mới từ r9, node 3 khoá ở "#15 @ epoch 44" → giảm LR đỉnh (tham số bậc 1) → Chạy thử *4–8 phút* vì điểm xuất phát đổi → Train #16; biểu đồ #16 nối tiếp #15.

*Ca B, một lần train khác (12 phút/epoch), quay lại sau 13 giờ, không có gì đỏ.* Thẻ ghi "bản tốt nhất ở epoch 52; 13 epoch sau không khá hơn; chạy hết còn ~1,4 giờ" → **bấm** Dừng, giữ bản tốt nhất → đăng ký model từ bản tốt nhất. Muốn rẽ nhánh từ epoch 52: được, vì đó là bản tốt nhất còn giữ; từ epoch 40: không, bản đó đã bị ghi đè — màn hình nói đúng như vậy.

*Ca C, muốn thử ý mới khi máy bận.* Tạm dừng #15 → Chạy thử nháp mới → Chạy nốt #15.

### H4 (Đ3) — model cũ, dữ liệu mới, thêm một lớp

1. Dữ liệu có v6 (cũ + 600 ảnh mới, thêm lớp *cháy*). Node 2 báo "có v6" → đổi.
2. Node 3: trả lời hai câu → cả hai thẻ sáng với giá: Train lại "an toàn nhất, 12–22 giờ", Fine-tune từ model của tôi "4–7 giờ". Hà chọn Fine-tune từ `pcb-detector v3`.
3. Bảng ghép lớp tự điền: 6 lớp giữ, 1 lớp mới. Phần được học: mẫu Cân bằng.
4. Chạy thử → "khớp 417/418 tầng; hàng của lớp *cháy* tạo mới".
5. Train. Xong, node 6 tự chấm trên Tập test T1 cạnh v3: "lớp cũ 0,61 → 0,60". Kèm lưu ý: "T1 không có lớp *cháy* — mở rộng thành T2 để chấm lớp mới, v3 sẽ được chấm lại trên T2".
6. Minh mở mức Cấu hình của ngăn Mục tiêu & tối ưu, đổi trọng số một thành phần loss (bậc 2) → Chạy thử.

### H3 (Đ4) — sửa code

1. Minh có quyền "sửa code công thức". Node Dữ liệu → mức Code → `to_coco.py`: cảnh báo "file dùng chung cho mọi công thức đọc COCO; sửa ở đây tạo bản riêng" → sửa ngưỡng bỏ hộp nhỏ.
2. Chạy thử (bắt buộc) → node 2 "bỏ 0 nhãn (trước: 37)".
3. Thêm gói: node 1 → Dockerfile → "dựng lại 10–30 phút mỗi máy" → kiểm hợp đồng sau khi dựng (shim khởi động được, handler nạp được) → soi lại → Chạy thử.
4. Model đăng ký từ lần train này được phục vụ bằng đúng handler của bản chốt.

## 10. Danh mục trực quan hoá

| Người dùng nhìn thấy | Ở đâu | Nền | Đợt |
|---|---|---|---|
| Ảnh có khung nhãn (trước bộ chuyển) | Node 2, Vào | CÓ SẴN: `DatasetBrowser` | 1 |
| 16 ảnh sau bộ chuyển; số nhãn bị loại | Node 2, Ra | CẦN XÂY: báo cáo bộ chuyển | 1 |
| Thanh chia tập, số mẫu mỗi lớp mỗi tập | Thân node 2 | CẦN XÂY: chia tập ghi vào phiên bản | 1 |
| Ai giữ GPU; pha dựng môi trường và log | Node 1 | CÓ SẴN: `getClusterMap`. CẦN XÂY: log build lên UI | 1 |
| Bảng biến thể: tham số, bộ nhớ, điểm công bố | Node 3 | CÓ SẴN | 1 |
| Hai thẻ Fine-tune / Train lại | Node 3 | Mục 5.1 | 1, 3 |
| Batch thật sau tăng cường | Ngăn Batch | CẦN XÂY: job soi · CHƯA CHẮC | 1 |
| Sơ đồ khối, số tham số | Ngăn Phần được học | CẦN XÂY · CHƯA CHẮC | 1 |
| Loss, optimizer, EMA | Ngăn Mục tiêu & tối ưu | CẦN XÂY: job soi | 1 |
| Đường LR dự kiến + mốc lịch | Ngăn Lịch học | CẦN XÂY · CHƯA CHẮC (mốc lịch: CÓ SẴN từ config) | 1 |
| Báo cáo nạp trọng số | Node 3 sau chạy thử | CẦN XÂY · CHƯA CHẮC | 1 |
| Tốc độ, bộ nhớ GPU đỉnh của lượt thử | Node 4 sau chạy thử | CẦN XÂY: đo đỉnh trong tiến trình (mẫu 15 s của agent quá thưa) | 1 |
| Loss, LR hiện tại, metric chính lúc train | Các ngăn, trang Run | CÓ SẴN | 1 |
| Vạch loss hỏng | Biểu đồ loss | CẦN XÂY | 1 |
| % epoch, ETA dạng khoảng | Thân node 4 | CẦN XÂY: khai `progress` | 1 |
| Điểm lưu đã tải lên, "mất tối đa X phút" | Ngăn Điểm lưu | CẦN XÂY: sửa lưu bền | 1 |
| Lệnh đã dựng, config đã sinh, khác biệt giữa hai bản | Tab Máy đã chạy gì; So sánh | CÓ SẴN: lệnh. CẦN XÂY: lưu theo lần train | 1 |
| Mẫu theo dõi qua từng epoch | Ngăn Kiểm định; tab Mẫu theo dõi | CẦN XÂY · CHƯA CHẮC | 2 |
| Bảng ngưỡng, ma trận nhầm lẫn, ảnh sai | Node 6 | CÓ SẴN sau đăng ký; theo tập: CẦN XÂY | 1; 3 |
| Bảng ghép lớp | Node 3 | CẦN XÂY | 3 |

## 11. Bảng phủ bốn mươi điều

**Đủ** · **Một phần** (ghi thiếu gì) · **Hoãn** (đợt) · **Bác** (lý do).

| # | Điều | Mức |
|---|---|---|
| 01-1 | Vòng lặp thay bảy node; recipe khai giai đoạn | Đủ (§4.1) |
| 01-2 | Kiểm dữ liệu, nhãn, rò rỉ | Một phần: Đ1 có phân bố lớp, nhãn bị loại, ảnh sau bộ chuyển, cảnh báo ảnh từ video. Thiếu: ảnh gần trùng, vật thể nhỏ (Đ3); chia theo nhóm (ngoài bốn đợt) |
| 01-3 | Chia tập nằm trong lần train | Đủ (§3, §7.1; Đ1) |
| 01-4 | NaN bị nuốt; chốt chặn | Một phần: NaN hiện và tự dừng Đ1; tín hiệu mềm Đ2. Thiếu luật "val xấu, train tốt" vì D-FINE không ghi loss val |
| 01-5 | Kiểm sớm + bộ ảnh cố định | Một phần: mẫu theo dõi theo epoch Đ2. **Rút** kiểm định trước khi hết epoch 1 (repo không có hook); thay bằng so loss với lượt thử |
| 01-6 | Chạy nốt / rẽ nhánh / dừng giữ bản tốt | Đủ về thiết kế; chạy nốt sau sự cố Đ1, phần còn lại Đ2 |
| 01-7 | Bảy kiểu train, bảy câu hỏi | Một phần: bốn lựa chọn + hai hành động; hai câu hỏi; số mẫu dùng để chọn sẵn mẫu khoá. Chưng cất, học tăng dần: ngoài bốn đợt. **Bác** câu hỏi thiết bị đích — thay bằng bảng biến thể |
| 01-8 | Chạy khói: ca xấu nhất, ngắt–chạy nốt, metric chính, LR thật, phần được khác | Một phần: thiếu ảnh lớn nhất và đa tỉ lệ trong mẫu thử |
| 01-9 | "Điều chỉnh trọng số" chưa có đường xuống mã | Một phần: ba mẫu Đ1 (CHƯA CHẮC tên khoá); từng khối Đ3 |
| 01-10 | Bốn bước sau train | Một phần: ngưỡng và phân tích lỗi CÓ SẴN sau đăng ký; so với model đang dùng Đ3. Tương đương sau xuất, độ trễ thiết bị: ngoài bốn đợt |
| 02-1 | Runtime chưa làm hết hợp đồng | Đủ: bản đầu chỉ D-FINE; node 7 không vẽ trước Đ3 |
| 02-2 | Chia tập, tăng cường không phải bước nền tảng | Đủ về hình; chỉnh tăng cường Đ1 còn CHƯA CHẮC |
| 02-3 | Build giữ GPU, không có pha; không khai `progress` | Đủ (Đ1) |
| 02-4 | Chạy khói DETR đi sai nhánh lịch | Đủ: lịch nén (Đ1) |
| 02-5 | Tag không phải digest; lock thiếu | Một phần: bản chốt và hiệu lực thử theo máy Đ1; digest, cố định gói Đ4. Trước Đ4, lần thử lại có thể chạy image khác — chưa xử lý |
| 02-6 | Sửa handler phải áp cho serve | Hoãn Đ4 |
| 02-7 | Lớp phủ file | Hoãn: file cấu hình Đ3, script Đ4 |
| 02-8 | Ba phép đánh giá | Đủ cho hai số hiện ra (§8); entrypoint `eval` của recipe không dùng |
| 02-9 | Khoá/LR theo khối; ghép lớp | Một phần: mẫu Đ1; từng khối, ghép lớp Đ3 |
| 02-10 | Dữ liệu tải lại; URL 24 giờ | Hoãn Đ2. Ở Đ1 chạy thử rồi train tải dữ liệu hai lần — chấp nhận, có nói |
| 03-1 | Lưu bền điểm lưu hỏng | Một phần: tên file cố định Đ1, có diễn tập trong chạy thử; dạng thư mục (HF) Đ3 |
| 03-2 | NaN; không ngân sách giờ | Một phần: NaN Đ1; ngân sách Đ2 |
| 03-3 | Chạy khói cần nhưng không đủ | Đủ (§7) |
| 03-4 | Lần train không chốt recipe và image | Một phần: bản chốt Đ1; image theo digest Đ4 |
| 03-5 | Mã tuỳ ý, container không cách ly | Đủ về thứ tự: `command` và code chỉ ở Đ4 sau cách ly; quyết định 3 |
| 03-6 | Sửa code ⇒ image mới mỗi máy | Hoãn Đ4 |
| 03-7 | Tiến độ theo node là giả; cắt tự do, gộp nhánh | Đủ. Gộp với công thức gốc: **Bác** — chỉ báo "gốc đã đổi" kèm diff |
| 03-8 | URL 24 giờ, dữ liệu theo lần | Như 02-10 |
| 03-9 | GPU nguyên chiếc | Một phần: nói ai giữ GPU và ba lối Đ1; tạm dừng để thử Đ2; chen bản thử model: quyết định 6 |
| 03-10 | License chưa thực thi; val theo seed | Một phần: kiểm nhanh chặn khi chưa xác nhận Đ1 (cần kiểm phía server); thừa kế khi lưu công thức Đ4; chặn lên production: ngoài bốn đợt. Seed: đủ |
| 04-1 | Node theo ranh giới artifact; có Môi trường | Đủ |
| 04-2 | Dữ liệu thật vào/ra từng node | Đủ, nhờ job soi và chạy thử (§4.2, 4.3) |
| 04-3 | Canvas có khung | Đủ |
| 04-4 | Chạy thử cùng đường mã | Đủ |
| 04-5 | "Đã cũ" + lý do + giá | Đủ, thêm "điều kéo theo" |
| 04-6 | Khoá "bản đã thử" đầy đủ | Một phần: theo máy thay cho digest tới Đ4 |
| 04-7 | Lần train bất biến; nhân bản thành nháp | Đủ (nháp theo người) |
| 04-8 | Công tắc cấu hình ↔ code, diff; bước code có hợp đồng | Một phần: hợp đồng bước code mới ở mức nguyên tắc (Đ4) |
| 04-9 | Khối do recipe khai | Một phần: như 02-9 |
| 04-10 | So sánh bằng bảng và diff | Đủ (§6, §8) |

## 12. Xử lý 22 lỗi thẩm định

| Lỗi | Xử lý |
|---|---|
| C1 khung rỗng | Nhận. Đ1 gồm canvas + job soi + tham số bậc 1 + chạy thử. Khác đề nghị của 06 ở một điểm: chạy thử vào Đ1 chứ không phải đợt hai, theo yêu cầu của người điều phối |
| C2 hành trình vượt đợt | Nhận. Mỗi đợt một hành trình; wireframe gắn (Đn) |
| C3 tám lời hứa thiếu nền | Nhận cả tám: a, h → job soi; b → rút kiểm định giữa epoch, mẫu theo dõi bằng tiến trình phụ Đ2; c, d → Đ3; e → rút "điện thoại báo", email Đ2; f → dừng sớm do nền tảng Đ2, rút "lưu mỗi ≤ 30 phút"; g → rút biểu đồ loss val |
| C4 lựa chọn giả hai ngả | Nhận. Ba bậc; `command` chuyển xuống bậc 3; hai biến thể lộ trình |
| L1 fine-tune / train lại | Nhận. Tên thẻ theo đúng từ của chủ sản phẩm: **Fine-tune** và **Train lại**; "model của tôi" là nguồn của Fine-tune, không thành thẻ "Train tiếp" như 06 đề nghị — để chỉ có hai tên |
| L2 canvas nghèo lúc train | Nhận (wireframe c) |
| L3 loss/optimizer, biến thể, hệ quả ngữ nghĩa | Nhận (ngăn Mục tiêu & tối ưu; node 3; "điều kéo theo") |
| L4 một GPU | Nhận (wireframe a) |
| L5 số mâu thuẫn | Nhận: một bộ số ở mục 1; kiểm sớm thành so sánh với lượt thử |
| L6 quay lại sau nhiều giờ | Nhận (§7.3, H5 ca B) |
| L7 quá tải | Nhận: tám từ; trạng thái lần đầu |
| L8 từ và ký hiệu đụng nhau | Nhận, **trừ một điểm**: giữ "lớp" = class, không đổi thành "nhãn". "Nhãn" đã là annotation trong cả sản phẩm ("gán nhãn", "nhãn bị loại"); đổi sẽ tạo va chạm mới. Layer = "tầng" như đề nghị |
| L9 hai con số; tập test | Nhận (§3, §8) |
| L10 nháp dùng chung | Nhận: nháp theo người |
| N1–N7 | Nhận. N3: bỏ luôn "vượt được kèm lý do"; cổng cứng thành khuyến nghị ở quyết định 1 |
| N8 thứ bỏ được | Nhận phần lớn (bỏ tia chớp, hai tầng hoàn tác, tab Lệnh và Môi trường, dải khung ở So sánh, ⊕ trước Đ4). **Giữ** tab Metric riêng — đã có sẵn và chứa hàng chục loss thành phần. **Giữ** thanh hậu quả dù là chữ — nó là chỗ duy nhất nói giá trước khi bấm |

## 13. Sáu quyết định, kèm hậu quả

1. **Cổng chạy thử.** *Cứng (khuyến nghị)*: mỗi lần đổi batch tốn thêm 4–8 phút, đổi lại không còn lần train hỏng vì lỗi lộ ngay phút đầu. *Vượt được kèm lý do*: người rành đỡ bực; sẽ có lần train "chưa thử" hỏng giữa đêm.
2. **Thứ tự mở sửa.** *Tham số → cấu hình → code (khuyến nghị)*: Đ1 đã chỉnh được thật, code tới Đ4. *Code trước*: đúng nguyên văn sớm hơn, nhưng cách ly, lớp phủ, dọn image phải xong trước mọi thứ người dùng thấy.
3. **Ai được chạy code tuỳ ý trên máy chung.** *Quyền riêng + cách ly (khuyến nghị)*. *Mọi Editor*: sửa script lên Đ3; đổi lại code của một người đọc được dữ liệu và token của người khác [03] — phải ghi thành quyết định.
4. **Phạm vi bản đầu.** *D-FINE rồi YOLO (khuyến nghị)*. *Mọi họ*: canvas phải vẽ cho LLM, ASR, TTS khi runtime của chúng chưa chắc chạy [02].
5. **Canvas có khung.** *Có, tự do để ở Pipelines (khuyến nghị)*. *Tự do trong một lần train*: giống n8n hơn về hình, nhưng trạng thái từng node phải dựng giả.
6. **Chen GPU để chạy thử.** *Được tạm dừng bản thử model, không đụng production (khuyến nghị)*. *Không chen*: trên máy một GPU đang phục vụ, chạy thử thành chờ vô hạn và người dùng sẽ bỏ thử.

**Việc nên làm trước khi dựng:** một buổi đọc repo D-FINE ở commit đã chốt để chuyển mọi [CHƯA CHẮC] thành có/không: tên khoá config cho LR, khoá backbone, kích thước ảnh, tăng cường; dựng dataloader và tính đường LR trên CPU; đếm tham số theo khối; repo có in báo cáo nạp trọng số; bước của TensorBoard; `last.pth` nạp được bằng handler giữa chừng. Kết quả buổi đó quyết định Đ1 giữ được mấy trong năm thứ job soi hứa.
