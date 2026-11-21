# 06 — Thẩm định bản thiết kế UX (05)

Đọc toàn bộ 05, đối chiếu 00–04, kiểm vài khẳng định trong repo (chỉ đọc). Nhãn: **[R]** tôi tự kiểm trong repo · **[0x]** theo báo cáo · không nhãn = suy ra từ chính văn bản 05.

## 1. Đối chiếu lời chủ sản phẩm

| Lời nguyên văn | Đáp ứng ở | Có từ GĐ | Nhận định |
|---|---|---|---|
| "đập đi xây lại Project và New training" | §8.1 | 1 | GĐ0 không đụng tới cả hai. GĐ1 có canvas nhưng chưa có Chạy thử; nút Train ở GĐ1 làm gì không được tả |
| "biết flow training vận hành và process (metric)" | §3.6c, §8.3 | 0 | Đạt một nửa: lúc train canvas co thành dải, nhiều giờ chỉ một hộp sống (L2) |
| "mọi thứ phải trực quan hoá" | §9 | 1–4 | 10/20 dòng chưa có dữ liệu; canvas GĐ1 gần như rỗng (C1) |
| "custom lại code và test trước khi chính thức training" | §5, §7 | test: 2 · code: 5 | Hai vế chỉ gặp nhau ở giai đoạn cuối (C4) |
| "không… phép thử mà cái giá là vài ngày treo máy" | §5 | 2–3 | Đạt về thiết kế; NaN vẫn đốt máy tới GĐ3 (N6) |
| "tách rõ re-training và fine tune, điều chỉnh trọng số" | §6 | 4 | Bị làm yếu: thuật ngữ bị giấu, hai kiểu train lại bị gộp (L1) |
| "tinh chỉnh từng node, từng step, từng dòng code" như n8n | §3.5, §7 | 5 tham số cũ: 1 · cấu hình: 4 · code: 5 | Tới GĐ4 ông ấy vẫn chỉ gõ 5 tham số như hôm nay |
| "xem và tinh chỉnh recipe… tách từ một recipe và cook lại" | §2, §8.2 | xem, tách, nấu lại: 1 · chỉnh: 4–5 | Mô hình nháp/bản chốt đúng ý. Thiếu chỗ xem trọn manifest như một tài liệu (N7) |
| "Tôi không tin bạn đã liệt kê đủ" | §10 | — | Bảng phủ ghi "ĐV" cho nhiều dòng mới xử lý một phần (mục 4) |

## 2. Bốn lượt đi bộ — chỗ kẹt

**a. Chủ sản phẩm.** Mở project ở GĐ1, thấy khung bảy node, nghĩ "đúng hướng". Bấm ngăn Batch & tăng cường: chưa có ảnh (hook là GĐ2), chưa có ô chỉnh (GĐ4). Bấm Phần được học: "công thức này chưa cho chỉnh". Lật lớp Code: chỉ đọc. Bấm Train: như cũ. Kết luận của ông: "form cũ vẽ thành hộp". Mở lần train đang chạy: một dải mỏng, một hộp, ba đường nhỏ → "trong vòng lặp vẫn mù". Hài lòng ở: thanh hậu quả, kế hoạch thực thi, "Nấu lại từ #n".

**b. Hà, lần đầu (H1).** Bước 2: ba thẻ công thức, cả ba đều "chưa ai train thành công" (15 công thức chưa từng chạy thử [01]), không có tiêu chí chọn → đoán. Vào canvas: khoảng 30 khái niệm và ký hiệu trong phút đầu (L7). Bước 3 trơn. Tìm chỗ chọn cỡ model (n/s/m/l/x): không thấy (L3). Bước 5: nút ghi "~4–8 ph" nhưng lần đầu là 10–30 phút dựng môi trường. Bước 6: ảnh ghim — Hà chưa ghim gì, ai chọn? Sau 60 bước lưới ảnh rất có thể không có khung nào → tưởng hỏng (L5). Bước 7: "đóng máy đi về", thông báo tới bằng đường nào? (C3e).

**c. Minh.** *Đổi hàm loss*: không ngăn nào chứa loss, optimizer, EMA; `use_amp` và `seed` cũng không có chỗ (L3). Đường duy nhất là sửa file repo gốc — bước cuối của GĐ5. *Lịch LR*: wireframe §7 có ô "LR đỉnh", nhưng D-FINE chỉ khai `epochs, batch, seed, use_amp` **[R]** → ô không có gì đứng sau tới GĐ4. Đổi epoch thì lịch tăng cường đổi theo ở ngăn khác mà không ai báo [02 §1 dòng 5]. *Đóng băng*: chỉ đọc tới GĐ4 — thiết kế nói thật, chấp nhận được. *So với lần trước*: §8.4 tốt.

**d. Người quay lại sau 20 giờ, dấu hiệu hỏng không phải NaN** (mAP giảm dần từ epoch 52). Vào project rơi vào tab Công thức (bản nháp), phải tìm dải "đang train". Trang Run: thấy vạch đỏ, nhưng không có tóm tắt "từ lúc bạn rời đi", không có đề nghị hành động kèm số cho tín hiệu mềm (§5.4 chỉ tả ba lựa chọn cho ca NaN), tab Overview hiện có (sự kiện, các attempt) biến mất khỏi §8.3 **[R `Run.tsx:161`]**. Muốn rẽ nhánh từ epoch 52: điểm lưu đó còn không? D-FINE chỉ giữ `last` và `best`, nền tảng giữ 3 bản bền [03]. Lần train đã quá ngân sách 18 giờ nhưng chưa tới 1,2× — mốc hiện gì? (L6).

## 3. Danh sách lỗi

### Chặn

**C1. Hai giai đoạn đầu không cho chủ sản phẩm thứ gì khác hẳn; canvas GĐ1 là khung rỗng.** *Mục:* §11, §9. *Vấn đề:* GĐ0 chỉ thay thanh bước trên trang Run. GĐ1 vẽ canvas nhưng năm ngăn của vòng lặp gần như không có hình và không có ô chỉnh: batch thật, sơ đồ khối, đường LR đều là ✖ và thuộc GĐ2–4. Chính §9 đã viết "canvas dựng trước dữ liệu sẽ là khung rỗng", rồi GĐ1 làm đúng điều đó. *Ai:* chủ sản phẩm, Hà. *Sửa:* gộp phần nhìn của GĐ0 và GĐ1 thành đợt đầu, làm song song với vá nền:
1. Tab Công thức thay TrainSheet ngay; cùng một canvas đầy đủ dùng cho trang Run.
2. Thêm vào nền đợt đầu một job "soi công thức" chạy CPU trong image, đệm theo môi trường: cây mã và config của repo gốc, config đã sinh, danh sách khối và số tham số, đường LR, hai batch sau tăng cường. Một job lấp bốn dòng ✖/◐ của §9. Kéo "dựng môi trường không giữ GPU" từ GĐ2 lên theo.
3. Ước tính thời gian đợt đầu lấy từ lần train trước trên cùng máy, chưa cần chạy khói.
4. Đợt hai: chạy khói **cùng lúc** với chỉnh nông qua tham số khai trong manifest đi đường `--extra-json` (LR đỉnh, kích thước ảnh, bật/tắt phép tăng cường, mẫu khoá backbone). Sau đó: Cứu → train tiếp, ghép lớp, sửa file cấu hình → code. Điều kiện chặn "lưu điểm bền" giữ nguyên, chỉ chặn dòng "mất X phút".

**C2. Hành trình dùng tính năng của giai đoạn sau; không giai đoạn nào kết thúc bằng một hành trình trọn vẹn.** *Mục:* §4, §7, §11. *Vấn đề:* H1 cần GĐ1+2+3. H5 là hành trình "Cứu" (GĐ3) nhưng bước 3 "giảm LR đỉnh còn một nửa" cần GĐ4; ở GĐ3 người rẽ nhánh chỉ đổi được batch và `use_amp`. H3 toàn GĐ5, H4 toàn GĐ4. *Ai:* nhóm phát triển, chủ sản phẩm. *Sửa:* gắn nhãn GĐ vào từng bước hành trình; viết cho mỗi GĐ một hành trình đi trọn bằng đúng thứ GĐ đó có; wireframe chỉ vẽ ô đã có đường xuống mã, hoặc đánh dấu "GĐ4".

**C3. Tám lời hứa không có trong cột "Nền phải vá".** *Mục:* §11 so với §3–8.

| | Hứa ở | Thực tế | Thiếu |
|---|---|---|---|
| a | §7 "xem mọi dòng từ GĐ1"; lớp Cấu hình hiện file sẽ sinh | Mã và config repo gốc chỉ có trong image trên máy GPU [02]; 15 công thức chưa build | Bước trích file từ image |
| b | Kiểm sớm, ảnh ghim qua từng epoch | Vòng train là tiến trình của repo gốc, không có hook; D-FINE chỉ lưu theo epoch | Hook hoặc tiến trình phụ, kèm tính bộ nhớ GPU |
| c | Node 6 trên tập test, cảnh báo "trước nút Đăng ký" | D-39 cần model đã đăng ký và chọn mẫu theo đường dẫn, không theo tập [01 §6, 02] | Đánh giá ứng viên chưa đăng ký, theo tập |
| d | Node 6, 7 tự chạy sau train | Control plane chỉ từng gửi `train` [02 §0.2] | Chuỗi sau train |
| e | "Điện thoại báo", "đóng máy đi về" | M16 P1 chỉ có thông báo trong app **[R]** | Kênh ngoài app |
| f | Dừng sớm, lưu mỗi ≤ 30 phút | Manifest D-FINE không khai núm nào cho hai thứ này **[R]** | Dừng do shim; tần suất lưu |
| g | Biểu đồ "loss train + val", luật "val xấu dần" | D-FINE không ghi loss val, chỉ mAP **[R manifest:86–98]** | Luật dựa trên metric chính |
| h | Đổi epoch/LR: "0 phút, đường LR vẽ lại" | Đường LR phải tính bằng mã repo trong image (§12 tự nhận chưa kiểm) | Job soi ở C1 |

*Ai:* nhóm phát triển, rồi người dùng khi tính năng ra thiếu. *Sửa:* thêm tám dòng này vào §11, hoặc rút lời hứa.

**C4. "Tinh chỉnh" bị đẩy về GĐ4–5 bằng một lựa chọn giả hai ngả.** *Mục:* §7, §12 câu 2. *Vấn đề:* câu 2 chỉ cho chọn "theo bậc tới GĐ5" hoặc "mở hết ngay". Có đường giữa rẻ hơn: thêm tham số khai báo qua `--extra-json` (đã có sẵn trong `make_config.py:94` **[R]**) không phải mã tuỳ ý, không cần cách ly. Ngược lại, GĐ4 cho sửa "đoạn manifest" — trong đó có `command`, tức shell tuỳ ý — một giai đoạn **trước** khi GĐ5 dựng cách ly. Thứ tự GĐ4–5 còn phụ thuộc câu 3 mà lộ trình trình bày như đã cố định. *Ai:* chủ sản phẩm, Minh, người vận hành. *Sửa:* tách ba bậc: tham số khai báo (đợt hai) · file cấu hình repo (GĐ4) · `command`, script, code (GĐ5, sau cách ly). Vẽ hai biến thể lộ trình theo câu trả lời của câu 3.

### Lớn

**L1. "Tách rõ re-training và fine-tune" bị làm mờ.** *Mục:* §6.1. *Vấn đề:* trả lời "đã có model" là bị dẫn thẳng vào khởi động ấm; phương án "train lại từ model có sẵn gốc trên dữ liệu cũ + mới" — thứ [01 §2] coi là sạch và tái lập được hơn — không còn là lựa chọn. Từ của chủ sản phẩm chỉ còn trong ngoặc đơn. Tới hết GĐ3 node Điểm xuất phát chỉ có một đáp án, nhưng câu hỏi 1 vẫn mời trả lời "Có". Số kiểu lệch nhau: 3 (§8.1), 5 (§10), 6 nếu tính "từ đầu"; "ba câu" (§1, H4) và "bốn câu" (§6.1). *Ai:* chủ sản phẩm, Hà. *Sửa:* sau câu 1, hai thẻ cạnh nhau "Train lại (retrain)" / "Train tiếp (fine-tune từ model của bạn)", mỗi thẻ ghi trọng số nào đổi, lịch dài bao nhiêu, rủi ro, thời gian ước tính; trước GĐ4 hiện thẻ ở trạng thái "chưa có" thay vì câu hỏi.

**L2. Lúc train thật — lúc đắt nhất — là lúc canvas nghèo nhất.** *Mục:* §3.6c, §8.3. *Vấn đề:* khung co thành dải, các ngăn biến mất. "Không có trạng thái chạy riêng" bị hiểu thành "không có gì để hiện". *Ai:* chủ sản phẩm, người quay lại. *Sửa:* giữ vòng lặp mở trên trang Run; mỗi ngăn hiện **dữ liệu sống**, không hiện trạng thái: Lịch học có dấu "đang ở đây" và ranh giới giai đoạn tăng cường; Kiểm định có ảnh ghim và số mới nhất; Điểm lưu có "7 phút trước"; Batch có batch gần nhất nếu hook cho phép.

**L3. Ba quyết định thật không có chỗ trên canvas.** *Mục:* §3.1–3.2. (i) Loss, optimizer, EMA, AMP, clip, seed không thuộc ngăn nào. (ii) Chọn biến thể — quyết định ảnh hưởng thời gian và bộ nhớ nhiều nhất — không có wireframe, không rõ thuộc node nào. (iii) Đổi epoch kéo theo chín khoá lịch, gồm mốc tắt tăng cường [02]; thanh hậu quả chỉ nói giá chạy lại, không nói hệ quả ngữ nghĩa. *Ai:* Minh, Hà. *Sửa:* thêm ngăn "Mục tiêu & tối ưu"; đặt bộ chọn biến thể ở node Điểm xuất phát kèm bộ nhớ, tốc độ, điểm công bố; thanh hậu quả thêm dòng "kéo theo: tắt tăng cường ở epoch 110 (trước 66)".

**L4. Một GPU: đang train thì không thử được.** *Mục:* §3.6a, §12 câu 6. *Vấn đề:* wireframe (a) vừa hiện "Đang train #14 · pc-02" vừa hiện "Chạy thử ~4–8 ph" trên chính pc-02. Câu 6 chỉ bàn deployment, không bàn lần train của chính người dùng. *Ai:* mọi người dùng cụm nhỏ. *Sửa:* nút ghi thật "chờ GPU: #14 còn 9–12 giờ", kèm ba lối: việc chạy CPU (kiểm tĩnh, xem batch), xếp hàng thử sau #14, tạm dừng #14 để thử (từ GĐ3).

**L5. Con số tự mâu thuẫn.** *Mục:* §3.6b, §5.3–5.4, H1. (i) "3,1 bước/s" × 248 bước × 72 epoch ≈ 1,6 giờ, nhưng cùng wireframe ghi 11–16 giờ. (ii) "Kiểm sớm sau ~200 bước, trong 5 phút đầu": theo 12 phút/epoch và 124 bước/epoch, 200 bước là phút thứ 19, sau cả "điểm kiểm sau epoch đầu". (iii) Khoảng 11–16 giờ là ±18 %, trong khi §5.3 nói có thể lệch gấp rưỡi. (iv) "0/8 ảnh có khung → cảnh báo đỏ" ở vài chục bước đầu của họ DETR là báo động giả gần như chắc chắn [01 §4]. (v) "Dựng môi trường 4/18 ph": mẫu số lấy từ đâu? *Ai:* Hà; uy tín của chính tính năng ước tính. *Sửa:* dựng mọi số trong wireframe từ một bộ giả định ghi ở đầu; kiểm sớm định theo thời gian và so với lần chạy khói thay vì ngưỡng tuyệt đối; khoảng ước tính phải rộng đúng bằng sai số đã công bố.

**L6. Hành trình "quay lại sau nhiều giờ" chỉ viết cho ca NaN.** *Mục:* §5.4, §8.3, H5. *Sửa:* (i) thẻ "Từ lúc bạn rời đi" đầu trang Run: ba dòng sự kiện và một đề nghị có số ("bản tốt nhất ở epoch 52; 36 epoch sau không khá hơn; chạy tiếp tốn thêm ~4 giờ"); (ii) với mỗi tín hiệu mềm, nói hành động nào hợp; (iii) giữ dòng sự kiện và attempt của Overview; (iv) nói rõ điểm lưu nào còn để rẽ nhánh; (v) project có lần train đang chạy thì mở vào đó.

**L7. Quá tải ở màn hình đầu; "chín từ" thực ra hơn ba mươi.** *Mục:* §2, §3.6a. *Vấn đề:* ngoài chín từ còn: ngăn, chạy khói, thuộc lòng, kiểm tĩnh, kiểm sớm, điểm kiểm, mốc, đã cũ, nhãn giá, thanh hậu quả, rẽ nhánh, chạy nốt, nấu lại, tách, ảnh ghim, phần được học, bước code, sáu mức giá… Ba loại mã (`956d170`, `a41c…`, `r8`) nằm ngay hàng đầu. "Đường mặc định" hứa cho Lan (§1) không có màn hình nào. *Ai:* Hà, Lan. *Sửa:* trạng thái "lần đầu" của canvas: ba thứ sáng (Dữ liệu, Điểm xuất phát, Chạy thử), vòng lặp thu gọn một dòng, ẩn mã băm và ⊕; mở dần sau lần chạy thử đầu.

**L8. Từ và ký hiệu đụng nhau.** *Mục:* toàn văn. "Lớp" mang bốn nghĩa (class, layer trong "khớp 412/418 lớp", ba lớp chỉnh, lớp image) — ở node 3 hai nghĩa đầu đứng cạnh nhau. "Gốc": công thức gốc, repo gốc, model gốc, "so với gốc". "Ghim": ảnh, điểm lưu, gói pip, commit. "Điểm kiểm" cạnh "Điểm lưu" (tiếng Anh đều thành checkpoint). ⚠ vừa là "đã cũ" vừa là "2 lưu ý"; ● vừa là "đã sửa", "học đủ", "đang chạy". "Gọn/Compact" (§1: ẩn bớt) với "Gọn/Nâng cao" (§6.2: hiện thêm) là hai thứ ngược nhau. *Sửa:* class = "nhãn", layer = "tầng"; "điểm kiểm" → "báo cáo epoch đầu"; mỗi ký hiệu một nghĩa; tách công tắc Gọn khỏi Nâng cao.

**L9. Hai con số cho một model, và "tập test cố định" chưa là một đối tượng.** *Mục:* §3.1, §8.1, H4. *Vấn đề:* bảng lần train xếp theo mAP kiểm định, tab Models theo điểm đánh giá độc lập (thấp hơn do ngưỡng 0,25 và tiền xử lý khác [02 §3f]); không nơi nào nói hai số gặp nhau thế nào. Khi dữ liệu lên v6, tập test "cố định" lớn lên hay giữ nguyên; v3 có được chấm lại không? Ngăn chi tiết node 6 (chọn ngưỡng, phân tích lỗi) không được thiết kế. Ảnh ở node 2 là nhãn **trước** bộ chuyển (`DatasetBrowser`), không bắt được lệch id nhãn sau chuyển [01-B]. *Sửa:* thêm "tập test của project" vào §2 với luật kế thừa qua phiên bản; mọi nơi hiện điểm ghi một nhãn nguồn thống nhất; cột Ra của node 2 hiện 16 ảnh sau bộ chuyển.

**L10. Nháp dùng chung, tự lưu, và "Nấu lại" ghi đè.** *Mục:* §2, H2. *Vấn đề:* tự lưu cộng "người lưu sau nhận xung đột" là xung đột theo từng phím gõ; "Nấu lại từ #14" xoá sửa đổi của đồng nghiệp với 10 giây hoàn tác. §12 đã ngờ điều này. *Sửa:* nháp theo người ngay từ đầu; "Nấu lại" mở nháp mới, không thay nháp cũ.

### Nhỏ

**N1.** Wireframe (b) đánh ✔ từng ngăn trong khi §3.1 nói ngăn không có trạng thái; node 7 "tắt" ở (a) lại chạy ở (b); node 6 vắng ở (b); node 7 lỗi mà "Train được" — §5.2 chưa định nghĩa "qua một phần". Dùng ký hiệu "đã kiểm" khác "đã chạy".
**N2.** §5.3 "không đóng được" nhưng wireframe thu thành "5 điều ▸". Khối cố định sẽ thành mù biểu ngữ; thay bằng nội dung theo ngữ cảnh ("bộ nhớ: đã thử 6 ca nặng nhất, dư 4,6 GB").
**N3.** Sáu mức giá chỉ có ba hệ quả với người dùng; gộp còn ba. Mức 2 "vượt được" gộp đổi batch với đổi biến thể và dữ liệu — quá thô [04-6].
**N4.** §3.5 cho "kéo, phóng to" nhưng chọn CSS grid + SVG; ngăn 560 px xếp dọc Vào/Chỉnh/Ra nên sửa xong phải cuộn mới thấy Ra; canvas năm cột không vừa phần màn hình còn lại.
**N5.** Cổng "cùng digest" thực chất là theo từng máy (image build tại máy, pip không ghim [02]); H5 bước 4 chuyển sang pc-03 là chạy trên môi trường chưa thử — phải nói ra.
**N6.** NaN: GĐ0 chỉ vẽ vạch, tới GĐ3 mới tự dừng. Dùng lệnh Stop sẵn có **[R `Projects.tsx`]** để tự dừng ngay từ GĐ0.
**N7.** Vắng mặt, không ghi "hoãn": HPO (M10) nối với nháp/bản chốt thế nào; gửi ảnh sai về gán nhãn; bước Triển khai sau node 7 (D-41: bước kế tiếp tại chỗ); lần train cũ không có bản chốt hiện ra sao; xem trọn manifest; hợp đồng vào/ra của "bước code" [04]; đường "nhập nhánh git" ở H3 bước 6 không có trong lộ trình; câu hỏi ngân sách đĩa [03 câu 6].
**N8.** Bỏ được mà không ai tiếc: biểu tượng tia chớp; hai trong bốn tầng hoàn tác; tab Lệnh và Môi trường ở trang Recipe (trùng node 1 và kế hoạch thực thi); dải khung thu nhỏ ở trang So sánh; tab Metric tách khỏi Theo dõi; ⊕ hiện trước GĐ5. Gọi là trực quan nhưng vẫn là chữ và ô nhập: bốn câu hỏi của node 3, thanh hậu quả, ngăn Kiểm định và Điểm lưu (mỗi ngăn một con số), hàng mốc, node Môi trường.

## 4. Kiểm 12 dòng của bảng phủ

| Dòng | Bảng ghi | Thực tế |
|---|---|---|
| 01-2 | ĐV tầng 0 | Một phần: thiếu ảnh sau bộ chuyển và ảnh gần trùng. Hoãn chia theo nhóm nhưng lại đề nghị "80/10/10" ngẫu nhiên, không cảnh báo rò rỉ với ảnh cắt từ video |
| 01-3 | ĐV | Một phần: chia tập có, luật giữ tập test qua phiên bản không có (L9) |
| 01-4 | GĐ0 hiện, GĐ3 dừng | Đúng như ghi; nhưng khoảng trống ba giai đoạn (N6) |
| 01-5 | ĐV, GĐ3 | Thiếu điều kiện nền (C3b); ngưỡng báo động giả (L5) |
| 01-7 | ĐV một phần | Gộp hai kiểu train lại (L1); bỏ câu hỏi số mẫu nhưng không nơi nào dùng số mẫu để chọn sẵn mẫu khoá |
| 01-10 | ĐV node 6 | Chỉ có tên node; ngưỡng và phân tích lỗi chưa thiết kế (L9) |
| 02-3 | ĐV node 1, GĐ0 | GĐ0 mới *hiện* pha build; build vẫn giữ GPU tới GĐ2 |
| 02-8 | ĐV §3.1 | Tách trên canvas; chưa xử lý nơi hai số gặp nhau (L9) |
| 03-5 | chặn GĐ5 | Sửa `command` ở GĐ4 đi trước cách ly (C4) |
| 03-10 | license chặn ở kiểm tĩnh (GĐ1) | Kiểm tĩnh thuộc GĐ2 |
| 04-6 | ĐV §5.2 | Có, nhưng mức 2 vượt được quá rộng (N3); digest theo máy (N5) |
| 04-8 | ĐV §7, 3.5 | Điểm chèn có; hợp đồng của bước code không có (N7) |

Ba dòng tôi kiểm và thấy đạt đúng như ghi: 03-1, 04-5, 04-7.

## 5. Năm điểm làm đúng, phải giữ

1. Vòng lặp là **một khối có ngăn**, vẽ theo ranh giới thực thi thật; công thức không khai thì không vẽ node giả.
2. Nháp → bản chốt bất biến → lần train; "Nấu lại từ #n"; kế hoạch thực thi chỉ đọc.
3. "Đã cũ" kèm lý do và giá bằng phút; chưa đo thì ghi "chưa đo".
4. Chạy khói cùng đường mã, có ngắt–chạy nốt, thất bại khi metric chính vắng; ba hành động tách bạch chạy nốt / rẽ nhánh / dừng giữ bản tốt.
5. Thói quen nói thật: cột "đã có?" ở §9, ngăn chỉ đọc khi chưa có đường xuống mã, các đoạn "xung đột với ý chủ sản phẩm", dòng "cùng tập val" ở trang So sánh.

## 6. Phản ứng nhiều khả năng nhất của chủ sản phẩm

Ông ấy sẽ nhận hướng đi và bác lộ trình. Phần hình dạng — vòng lặp có ngăn, thử có giá, nháp và bản chốt — trả lời đúng ba câu ông hỏi, và các đoạn nói thẳng chỗ trái ý sẽ được ông tôn trọng. Nhưng ông đã nói "đập đi xây lại" và "từng dòng code"; đọc §11 ông thấy hai giai đoạn đầu vẫn gõ năm tham số cũ, thứ ông gọi là n8n tới GĐ2 mới bấm chạy được từng bước, GĐ4 mới sửa được cấu hình, GĐ5 mới sửa được code, kèm sáu câu hỏi hỏi ngược lại ông. Câu nhiều khả năng nhất: "Hướng đúng, nhưng tôi phải chờ bao lâu mới thấy khác? Đưa canvas thật và chỉnh được ra trước." Và vì ông đã nói không tin danh sách đủ, ông sẽ kiểm ngẫu nhiên bảng phủ; gặp một dòng "ĐV" mới làm một nửa (như 01-10 hay 03-10) là mất lòng tin vào cả bảng. Sửa C1–C4 và ghi lại bảng phủ theo ba mức "đủ / một phần / hoãn" trước khi đưa ông đọc.
