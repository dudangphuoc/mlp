# M05 — Labeling (gán nhãn dataset)

> **Phase:** P1 (đội nhóm nâng cao P2) · **Phụ thuộc:** M01, M03, M04, M11 · **Quyết định:** D-16, D-17 · **Proto:** `labeling.proto` (`LabelingService`) · **Schema DB:** `label`

## 1. Mục tiêu & phạm vi
Gán nhãn đa modality với AI hỗ trợ, có vai trò, phân công, review và version nhãn.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Label schema có version (class, thuộc tính, skeleton keypoint, loại quan hệ) | ✔ | | |
| Ảnh: classify, bbox, OBB, polygon/instance mask, semantic brush, keypoint | ✔ | | |
| Văn bản: classify, NER span + quan hệ, SFT (prompt/response), preference | ✔ | | |
| Bảng: target theo dòng; video: nhãn theo từng frame | ✔ | | |
| **Âm thanh: phiên âm (transcript), người nói, sửa transcript có đồng bộ phát lại** (phục vụ ASR/TTS) | ✔ | | |
| Video tracking + nội suy keyframe, audio theo đoạn thời gian (segment + nhãn), time-series | | ✔ | |
| Document (layout, key-value, bảng) | | | ✔ |
| AI hỗ trợ bằng **recipe có khai `assist`** (vd. SAM cho phân vùng, model registry bất kỳ cho pre-label, Whisper/VibeVoice-ASR cho phiên âm nháp, LLM cho văn bản), active learning | ✔ | | |
| Labeling project, vai trò Lead/Annotator/Reviewer, phân công, review 1 cấp | ✔ | | |
| Consensus + IAA, QA sampling, phân công theo kỹ năng, quiz guideline | | ✔ | |
| Label set version, diff, import/export | ✔ | | |

## 2. Khái niệm
**Label schema**, **annotation**, **suggestion** (nhãn do model đề xuất, chưa chấp nhận), **labeling project**, **item label state**, **label set version** — xem [thuật ngữ](../00-tong-quan/02-thuat-ngu.md).

### 2.1 Vai trò con người

| Vai trò | Ai thường đảm nhận | Được làm | Không được làm |
|---|---|---|---|
| **Data Owner** (Owner/Admin hoặc Editor sở hữu dataset) | Trưởng nhóm ML | Định nghĩa schema, tạo project, chỉ định Lead, **chốt label set version** | — |
| **Labeling Lead** (vai trò theo project) | Người điều phối | Viết guideline, chia việc, phân công, chạy pre-label, mở lại item, xem dashboard | Sửa schema (đề xuất cho Data Owner) |
| **Annotator** | Người gán nhãn | Chỉ thấy item được giao; gán, sửa nhãn của mình, submit, bỏ qua có lý do | Xem item không được giao, export, xoá item, review nhãn của mình |
| **Reviewer** | Người giàu kinh nghiệm | Duyệt item `submitted`: chấp nhận, trả lại có comment, sửa trực tiếp | Review item mình đã gán |
| **ML Engineer** (Editor) | Người train | Đăng ký model dùng cho pre-label, dùng label set để train | Sửa nhãn đã chấp nhận (phải qua Lead mở lại) |
| **Viewer** | Stakeholder | Xem tiến độ | Mọi thao tác ghi |

Một người có thể giữ nhiều vai trò; **không ai vừa gán vừa review cùng một item**.

### 2.2 Hệ thống xử lý phần nào

| Việc | Ai/cái gì | Ở đâu |
|---|---|---|
| Lưu schema, nhãn, trạng thái, phân công, audit | Module Labeling (.NET) | Control plane (PostgreSQL) |
| Phát việc, khoá item, hết hạn khoá | Module Labeling (`SKIP LOCKED`), đẩy qua `WatchProject` | Control plane |
| Vẽ/sửa nhãn | Trình duyệt | Máy người dùng |
| Byte dữ liệu | kho S3 (managed) hoặc data channel tới agent (host_local) | Control plane / máy chứa dữ liệu |
| Phân vùng tương tác, pre-label, phiên âm nháp, active learning | Deployment nội bộ của recipe assist (vd. SAM), job `label_assist` chạy recipe của model được chọn | Máy có GPU (đúng máy chứa dữ liệu nếu local-only) |
| Thống kê tiến độ, chốt label set | Module Labeling | Control plane + kho S3 |

## 3. Dữ liệu (schema `label`)

| Bảng | Cột chính |
|---|---|
| `schemas` | id, dataset_id, version, task, classes jsonb (`id, name, color, parent_id, attributes[]`), item_attributes jsonb, keypoint_skeleton jsonb, relation_types jsonb, rules jsonb, is_current, created_by |
| `annotations` | id, dataset_id, item_id, schema_version, kind, class_id, geometry jsonb, attributes jsonb, source (`human/model/import`), model_version_id, confidence, created_by, updated_by, revision, deleted_at |
| `item_states` | item_id, dataset_id, project_id, status, annotator_id, reviewer_id, priority_score, lock_owner, lock_expires_at, draft_by, draft_annotations jsonb, draft_saved_at, skip_reason, submitted_at, reviewed_at, revision |
| `annotation_history` | id, item_id, annotation_id, action, before jsonb, after jsonb, actor_id, at |
| `projects` | id, workspace_id, dataset_id, name, guideline_md, schema_version, scope_filter jsonb, assignment_mode (`manual/pull/round_robin`), order_mode (`sequential/random/active_learning`), review_mode (`none/all/percent`), review_percent, lock_ttl_min (10), state (`draft/active/paused/completed`) |
| `project_members` | project_id, user_id, role (`lead/annotator/reviewer`), max_open_items (50) — **nơi duy nhất lưu vai trò gán nhãn**; M01 đọc qua `ILabelingMembership` |
| `comments` | id, item_id, annotation_id, author_id, body, resolved, created_at |
| `suggestions` | id, item_id, source_kind (`model_version/catalog_variant`), model_version_id, recipe, variant, job_id, kind, class_id, geometry, confidence, state (`pending/accepted/rejected`) |
| `assist_settings` | project_id, segment_deployment_id (M12 kind `assist`), segment_recipe (mặc định `sam2`), transcribe_source, class_mapping jsonb (lớp của model → lớp schema) |
| `label_sets` | id, dataset_id, number, schema_version, snapshot_uri (Parquet), counts jsonb, include_status (`accepted_only/all_labeled`), created_by, created_at |

### 3.1 Geometry (ảnh: toạ độ chuẩn hoá 0–1; văn bản: offset ký tự)

| kind | geometry |
|---|---|
| `tag` | `{}` |
| `bbox` | `{x, y, w, h}` |
| `obb` | `{cx, cy, w, h, angle}` |
| `polygon` | `{points:[[x,y]…], holes?:[…]}` |
| `mask` | `{rle:{size:[h,w], counts}}` (COCO RLE) |
| `keypoints` | `{points:[[x,y,v]…], bbox?}` theo skeleton |
| `span` | `{start, end, text}` |
| `relation` | `{from, to, type}` |
| `text_pair` | `{prompt, response}` |
| `preference` | `{prompt, chosen, rejected, reason?}` |
| `value` | `{value}` |
| `depth_map` | `{uri, unit: "mm"}` — chỉ import/pre-label, không có công cụ vẽ ở P1; trình gán nhãn hiện lớp phủ màu để duyệt |
| `transcript` | `{text, speaker?, language?}` (cả clip) |
| `audio_segment` (P2) | `{start_ms, end_ms, text?, speaker?}` |
| theo frame | thêm `frame` |

## 4. Trạng thái & quy tắc

```
unlabeled → assigned → in_progress → submitted → in_review → accepted
                 ▲          │  ▲           │           │
                 │          │  └─rejected◄─┴───────────┘ (Reviewer trả lại + comment)
                 │          └─► skipped ─► (Lead: giao lại / loại / sửa schema)
                 └──────────── accepted ◄─ (Lead mở lại có lý do)
submitted → accepted trực tiếp khi review_mode = none hoặc không lọt mẫu
```

| Chuyển | Ai | Điều kiện |
|---|---|---|
| `unlabeled → assigned` | Lead (manual), hệ thống (pull/round-robin) | annotator còn chỗ (`max_open_items`) |
| `assigned → in_progress` | Annotator được giao | mở item |
| `in_progress → submitted` | Annotator | qua kiểm tra schema (`rules`, bbox trong ảnh, đủ keypoint bắt buộc) |
| `in_progress → skipped` | Annotator | bắt buộc lý do |
| `in_review → accepted / rejected` | Reviewer ≠ annotator | reject bắt buộc comment |
| `rejected → in_progress` | hệ thống | trả đúng annotator cũ, ưu tiên cao |
| `accepted → in_progress` | Lead | có lý do |

- **R1 Khoá**: mở item → khoá `lock_ttl`; UI gia hạn mỗi 60 s khi còn thao tác; auto-save ghi nháp vào `draft_annotations` (`draft_by`, `draft_saved_at`); hết hạn → về hàng đợi, giữ nháp; người kế tiếp mở item thấy banner "Có nháp của <tên> lúc …" + `Dùng nháp` / `Bỏ nháp`.
- **R2 Lưu**: `SaveAnnotations` gửi **toàn bộ nhãn của item** + `etag` (= revision); server diff, ghi history; auto-save 5 s hoặc khi rời item.
- **R3 Đổi schema khi project active**: chỉ thêm class/thuộc tính; xoá/gộp → schema version mới + migrate nhãn, Data Owner xác nhận.
- **R4** Gán ngoài project (Editor gán trực tiếp) cho phép; nếu dataset không có project active thì item `accepted` luôn.
- **R5 Local-only**: phân vùng tương tác/pre-label chỉ chạy trên đúng máy chứa dữ liệu; máy không có GPU → tắt AI hỗ trợ cho dataset đó.
- **R6 Nguồn pre-label**: (a) model version trong registry cùng task, hoặc (b) **biến thể pretrained trong catalog có khai `classes`** (M17, vd. detector COCO) — khi đó Lead ánh xạ lớp của model sang lớp schema (`class_mapping`), lớp không ánh xạ bị bỏ. Kết quả handler (toạ độ pixel, [20/08 §10](../20-kien-truc/08-recipe-va-runtime.md)) được adapter chuẩn hoá về geometry 0–1 ở mục 3.1.

### 4.1 Phân công
| Chế độ | Cách | Phase |
|---|---|---|
| Thủ công | Lead chọn item → giao người | P1 |
| Hàng đợi chung (pull) | "Lấy việc tiếp theo" → `SELECT … FOR UPDATE SKIP LOCKED`; trả item dở/bị trả lại trước | P1 |
| Round-robin / cân tải | Chia đều theo số item đang mở | P1 |
| Ưu tiên active learning | `ORDER BY priority_score DESC` | P1 |
| Theo kỹ năng, consensus k người, lấy mẫu QA | | P2 |
Review: item `in_review` phát cho reviewer ít việc nhất hoặc reviewer tự "Lấy việc review".

### 4.2 AI hỗ trợ
| Tính năng | Thực hiện |
|---|---|
| Phân vùng tương tác (ảnh) | Deployment nội bộ (M12, kind `assist`) của một recipe có `entrypoints.assist.kinds: [segment]` — mặc định recipe `sam2`, đổi được trong cài đặt project; 1 replica GPU, bật khi có project ảnh active, tắt sau 30 phút không dùng; `Segment(item, points, box?)` → polygon/mask |
| Pre-label hàng loạt | Job `label_assist` chạy **recipe của nguồn được chọn** (R6: model version hoặc biến thể catalog, bất kỳ họ nào cùng task) → `suggestions` (nét đứt); chấp nhận → annotation `source=model` |
| Phiên âm nháp (audio) | Job `label_assist` với model ASR (Whisper, VibeVoice-ASR, model đã fine-tune) → transcript nháp để người gán sửa |
| Active learning | Job `label_assist` chế độ `score` → `priority_score` (entropy / margin / 1 − max conf) |
| LLM cho văn bản | LLM nội bộ (deployment M12 của một recipe LLM) hoặc LLM ngoài do Admin cấu hình; dữ liệu local-only chỉ dùng LLM chạy trên đúng máy |

## 5. Luồng chính
1. Data Owner định nghĩa schema → tạo project (phạm vi, chế độ phân công/thứ tự/review, guideline, thành viên + vai trò) → kích hoạt.
2. Lead chạy pre-label / chấm điểm active learning (tuỳ chọn) → phân công.
3. Annotator mở "Việc của tôi" → `Bắt đầu` → gán → `Gửi` → item tiếp theo.
4. Reviewer mở hàng review → chấp nhận / trả lại có comment.
5. Lead theo dõi dashboard, xử lý item bỏ qua, mở lại item sai.
6. Data Owner **chốt label set version** → tạo dataset version (M04) → train (M09).

## 6. Hợp đồng (`labeling.proto` — `LabelingService`)

| RPC | Quyền | Mô tả |
|---|---|---|
| `GetSchema` / `ListSchemaVersions` / `CreateSchemaVersion` | đọc: `dataset.read`; ghi: Data Owner | |
| `GetItemAnnotations` | annotate/review item đó, hoặc `dataset.read` | Nhãn + suggestion + trạng thái + `etag` |
| `SaveAnnotations` | người đang giữ khoá | Toàn bộ nhãn, `etag` |
| `LockItem` / `HeartbeatLock` / `UnlockItem` | `labeling.annotate` | |
| `SubmitItem` / `SkipItem` | Annotator được giao | |
| `AcceptItem` / `RejectItem` | Reviewer (≠ annotator) | Reject cần comment |
| `ReopenItem` | Lead | |
| `ListComments` / `AddComment` / `ResolveComment` | người tham gia item | |
| `AcceptSuggestion` / `RejectSuggestion` | người đang giữ khoá | |
| `GetItemHistory` | Lead, Data Owner | |
| `ListProjects` / `CreateProject` / `UpdateProject` / `ActivateProject` / `PauseProject` / `CompleteProject` | `labeling.manage` | |
| `ListProjectMembers` / `AddProjectMember` / `RemoveProjectMember` | `labeling.manage` | Gán vai trò lead/annotator/reviewer |
| `NextItem` / `NextReviewItem` | Annotator / Reviewer | Lấy việc |
| `AssignItems` | Lead | Thủ công |
| `ListMyItems` | thành viên | Theo trạng thái |
| `ListItems` | thành viên (Lead thấy mọi item, người khác chỉ item mình gán/review) | Lọc theo trạng thái + tên file, phân trang; kèm tên người gán/review, số nhãn |
| `ExportLabels` | `dataset.write` | Nhãn của project → **version dataset train được**: file đã gán + `.txt` YOLO cạnh ảnh + `data.yaml`/`classes.txt` + `labels.jsonl`; mặc định chỉ item `accepted`, `all_labeled` gồm cả `in_review` |
| `GetProjectStats` / `WatchProject` | Lead, Data Owner, Viewer | Tiến độ, theo người |
| `Segment` | `labeling.annotate` | Gọi deployment `assist` (mặc định `sam2`) |
| `GetAssistSettings` / `UpdateAssistSettings` | Lead | Recipe phân vùng, nguồn phiên âm, ánh xạ lớp |
| `StartPrelabel` / `StartScoring` | Lead | Tạo job `label_assist` |
| `ListLabelSets` / `CreateLabelSet` / `DiffLabelSets` | đọc: `dataset.read`; tạo: Data Owner | |
| `ImportAnnotations` / `ExportAnnotations` | `dataset.write` | Qua job |
| `RuntimeService.AddAnnotations` / `AddSuggestions` / `SetPriorityScores` | token job | |

## 7. Màn hình

### S-05-1 Dataset › Gán nhãn — `…/datasets/{ds}?tab=labeling`
Danh sách labeling project của dataset: tên, trạng thái, tiến độ (thanh màu theo trạng thái item), số thành viên, Lead, ngày. `+ Tạo project` → `FormDialog` 4 bước: (1) phạm vi item (split/class/tag/đã gán?), (2) phân công (`manual/pull/round_robin`) + thứ tự (`tuần tự/ngẫu nhiên/active learning`), (3) review (`không/tất cả/%`), (4) thành viên + vai trò + guideline (Markdown, có ảnh ví dụ).

### S-05-2 Bảng điều khiển Lead — `/w/{ws}/labeling/{project}`
```
┌ Dự án PCB — vòng 2  [● Active]                [Pre-label] [Chấm điểm AL] [Tạm dừng] [⋯] ┐
│ ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬                                                   │
│ chưa gán 3 120 · đã giao 400 · đang làm 52 · đã gửi 80 · đang review 41 · chấp nhận 8 690 · trả lại 12 · bỏ qua 9 │
│ ┌ Thành viên ─────────────────────────────────────────────────────────────────────────┐ │
│ │ Người  │ Vai trò   │ Đang mở │ Hôm nay │ TB/item │ Bị trả lại │ Hành động          │ │
│ │ chi    │ Annotator │ 48/50   │ 312     │ 18 s    │ 2.1 %      │ [Giao thêm] [⋯]    │ │
│ │ dung   │ Reviewer  │ 12      │ 205     │ 9 s     │ —          │ [⋯]                │ │
│ └─────────────────────────────────────────────────────────────────────────────────────┘ │
│ ┌ Item bỏ qua (9) ──────────────┐ ┌ Biểu đồ: hoàn thành theo ngày ─────────────────────┐ │
│ │ img_0042 "không rõ lỗi" [Giao lại][Loại] │ │ ▁▃▅▇▆▇█                                        │ │
│ └───────────────────────────────┘ └──────────────────────────────────────────────────┘ │
│ [Items] (lưới item lọc theo trạng thái/người → chọn nhiều → Giao cho… / Mở lại)            │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```
Pre-label → hộp chọn nguồn: tab **Model của tôi** (model version, lọc theo task) / tab **Catalog** (biến thể pretrained, `RecipePicker` lọc theo task) → nếu từ catalog: bảng ánh xạ lớp (tự gợi ý theo tên) → ngưỡng confidence, phạm vi → tạo job (link M03). Realtime qua `WatchProject`.

### S-05-3 Việc của tôi — `/w/{ws}/labeling`
Thẻ theo project: tên, guideline, số item đang giữ / được giao / bị trả lại, nút **Bắt đầu** (hoặc **Tiếp tục**). Tab `Gán nhãn` / `Review` (Reviewer) / `Đã làm`. Item bị trả lại hiện trước kèm comment. Rỗng: "Bạn chưa được giao việc nào."

**Hiện trạng S-05-1…S-05-4 (2026-11-20, lượt làm sâu 3 — D-41).**
- `ListProjects`/`GetProjectStats` trả kèm tiến độ (`total`, `accepted`, `in_review`, `unlabeled`), phần việc của người gọi (`my_open`, `my_returned`, `my_roles`) và `dataset_name` — một truy vấn gộp theo workspace, không join schema khác.
- **S-05-3**: thẻ project có thanh tiến độ, "đã chấp nhận x/y", số chưa gán / đang review, vai trò và số item đang mở / bị trả lại của mình; nút **Gán nhãn / Làm tiếp / Review** ngay trên thẻ; project có việc của mình xếp trước; lọc "Chỉ project của tôi"; trạng thái rỗng dẫn tới Datasets.
- **S-05-2**: đầu trang có dataset (bấm mở), vai trò của mình, nút theo vai trò (Lead không phải annotator được nhắc tự thêm mình); thanh tiến độ bấm vào trạng thái để lọc; thẻ **Dùng nhãn để train** (`ExportLabels`); thẻ Hướng dẫn; bảng thành viên có đang mở / đã gửi / đã chấp nhận / bị trả lại / lần gửi gần nhất + gỡ thành viên; **Đánh dấu hoàn thành** khi mọi item đã chấp nhận hoặc bỏ qua.
- **Tab Item** (mới): bảng item (file, trạng thái, số nhãn, người gán, reviewer, lúc gửi), lọc trạng thái + tìm tên, phân trang; bấm một dòng mở ngăn bên phải: ảnh kèm khung nhãn, danh sách nhãn, bình luận, lịch sử (Lead), **Mở lại** item đã chấp nhận (cần lý do).
- **S-05-4**: đầu trình gán nhãn hiện "còn n cần gán / review", nút Hướng dẫn mở ngăn bên phải; trạng thái item hiển thị theo ngôn ngữ giao diện.
- Chưa có: giao việc thủ công (`AssignItems`), mở thẳng một item bất kỳ để sửa (hiện chỉ xem), sửa hướng dẫn sau khi tạo, `WatchProject`, xuất COCO/segmentation, chia train/val khi xuất (bộ chuyển `to_coco` tự cắt val).

### S-05-4 Trình gán nhãn — ảnh (toàn màn hình)
```
┌ ← Dự án PCB · img_00061.jpg (12/50)   [● Đang giữ 08:42]          [Guideline] [?] [✕] ┐
│ [✋ Chọn][▭ Bbox][⬠ Polygon][✦ Keypoint][🖌 Mask][✨ Smart] │ ↶ ↷ 🗑 │ 👁 ⓘ ⟳ │ [Lưu]│
├───────────────────────────────────────────────────────────────┬──────────────────────┤
│                                                               │ [Tìm class…]         │
│                    CANVAS (zoom/pan)                          │ ● short       3  👁 ✎ │
│        ┌──────┐                                               │ ● open        1  👁 ✎ │
│        │short │   ┄┄┄┄┄┄ (đề xuất model, nét đứt)              │ ● missing     0  👁 ✎ │
│        └──────┘   [✔ Nhận][✕ Bỏ]                               │ ─────────────────────│
│                                                               │ Nhãn trên ảnh (4)    │
│                                                               │  short · bbox  ⋯     │
│                                                               │  open · polygon ⋯    │
│                                                               │ Thuộc tính: ☐ occluded│
│                                                               │ ─────────────────────│
│                                                               │ Comment (1)          │
├───────────────────────────────────────────────────────────────┴──────────────────────┤
│ [－] 100% [＋] [⤢ Vừa khung]        ◀ Trước   [Bỏ qua…]   [Gửi ⏎]   Tiếp ▶              │
└───────────────────────────────────────────────────────────────────────────────────────┘
```
- Thanh công cụ theo task của schema (chỉ hiện công cụ hợp lệ).
- Phím tắt: `1–9` chọn class, `B` bbox, `P` polygon, `K` keypoint, `M` mask, `S` phân vùng thông minh, `V` chọn, `Del` xoá, `Ctrl+Z/Y`, `H` ẩn nhãn, `Enter` gửi, `→/←` item tiếp/trước, `?` bảng phím tắt.
- Đồng hồ khoá; còn 1 phút → cảnh báo; hết → chuyển chỉ đọc.
- `Gửi` bị khoá khi vi phạm `rules` (chỉ ra nhãn lỗi).
- Annotator không thấy nút xoá item/tải ảnh.

### S-05-4b Trình gán nhãn — văn bản
```
┌ Thanh trên như trên ─────────────────────────────────────────────────────────────────┐
│ Văn bản:                                                       │ Nhãn: [PER 1][ORG 2][LOC 3] │
│ "Ông [Nguyễn Văn A]PER làm việc tại [Công ty X]ORG ở [Hà Nội]LOC." │ Quan hệ: works_at …     │
│ (bôi đen để gán; kéo từ span này sang span kia để tạo quan hệ)  │ Phân loại văn bản: [▾]  │
└───────────────────────────────────────────────────────────────────────────────────────┘
```
SFT: hai ô prompt/response có đề xuất LLM; preference: prompt + hai câu trả lời A/B, chọn tốt hơn + lý do.

### S-05-4d Trình gán nhãn — âm thanh
```
┌ Thanh trên như trên ─────────────────────────────────────────────────────────────────────┐
│ ▶ ❚❚  00:03.2 / 00:07.9   tốc độ [1.0×▾]   ▁▂▅▇▅▃▂▁▂▅▇▆▃▁ (sóng âm, kéo để tua)            │
│ Người nói [Speaker 01 ▾] [+ người nói mới]    Ngôn ngữ [vi ▾]                               │
│ Transcript (nháp từ ASR, nét đứt):                                                         │
│ ┌───────────────────────────────────────────────────────────────────────────────────────┐ │
│ │ xin chào các bạn, hôm nay chúng ta sẽ…                                                │ │
│ └───────────────────────────────────────────────────────────────────────────────────────┘ │
│ ☐ Có tạp âm  ☐ Nhiều người nói  ☐ Không dùng được   Chất lượng [★★★★☆]                     │
└──────────────────────────────────────────────────────────────────────────────────────────┘
```
Phím tắt: `Space` phát/dừng, `Ctrl+←/→` tua 2 s, `Ctrl+Enter` gửi. Thuộc tính item (tạp âm, chất lượng) dùng để lọc dữ liệu khi train TTS.

### S-05-4c Trình gán nhãn — bảng
Lưới dòng: cột đặc trưng chỉ đọc, cột target sửa được (chọn class hoặc nhập số), cờ "dòng lỗi"; phím `↓` dòng tiếp.

### S-05-5 Review
Giống trình gán nhãn ở chế độ review: nhãn chỉ sửa khi bấm `Sửa`; nút **Chấp nhận (A)**, **Trả lại (R)** (bắt buộc comment, có thể ghim comment vào nhãn cụ thể); bật "So với đề xuất model" để xem khác biệt.

### S-05-6 Label schema — `/w/{ws}/datasets/{ds}?tab=labeling&sub=schema`
Danh sách class (màu, tên, class cha, thuộc tính), thuộc tính item, skeleton keypoint (vẽ điểm + nối cạnh), loại quan hệ, luật; lịch sử version. Lưu → schema version mới (R3).

### Label set (trong S-04-6 Versions)
`Chốt label set` (chỉ Data Owner): chọn `chỉ item đã chấp nhận` hoặc `mọi item đã gán`, mô tả; xem diff giữa hai label set (bảng theo class: thêm/sửa/xoá + danh sách item).

## 8. Sự kiện
`label.ItemsAssigned` (gộp theo người nhận), `LockExpiring` (còn 1 phút), `ItemSubmitted`, `ItemAccepted`, `ItemRejected`, `ItemSkipped`, `ProjectActivated`, `ProjectCompleted`, `LabelSetCreated`, `SchemaVersionCreated`, `PrelabelCompleted`. M16 tạo thông báo: được giao việc, bị trả lại, khoá sắp hết, project hoàn tất.

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `LABEL_ITEM_LOCKED` | FAILED_PRECONDITION | Item đang được người khác gán |
| `LABEL_LOCK_EXPIRED` | FAILED_PRECONDITION | Hết thời gian giữ item |
| `LABEL_REVISION_CONFLICT` | ABORTED | Nhãn đã thay đổi, tải lại |
| `LABEL_SCHEMA_VIOLATION` | INVALID_ARGUMENT | Nhãn không hợp lệ (chi tiết từng nhãn) |
| `LABEL_NOT_ASSIGNED` | NOT_FOUND | — |
| `LABEL_SELF_REVIEW` | PERMISSION_DENIED | Không thể review item mình đã gán |
| `LABEL_REJECT_COMMENT_REQUIRED` | INVALID_ARGUMENT | Cần lý do khi trả lại |
| `LABEL_ASSIST_UNAVAILABLE` | UNAVAILABLE | AI hỗ trợ không khả dụng cho dataset này |
| `LABEL_PROJECT_NOT_ACTIVE` | FAILED_PRECONDITION | Project chưa kích hoạt / đã tạm dừng |
| `LABEL_MAX_OPEN_REACHED` | RESOURCE_EXHAUSTED | Đã giữ tối đa số item |
| `LABEL_EXPORT_EMPTY` | FAILED_PRECONDITION | Chưa có item nào được chấp nhận / được gửi để xuất |
| `LABEL_EXPORT_TOO_LARGE` | FAILED_PRECONDITION | Hơn 50 000 item — xuất từng phần |

## 10. Nghiệm thu
- [ ] Hai Annotator bấm "Tiếp" cùng lúc 1 000 lần không bao giờ nhận cùng item.
- [ ] Đóng tab giữa chừng → sau `lock_ttl` item về hàng đợi, nhãn nháp còn; người kế tiếp chọn `Dùng nháp` được.
- [ ] Reviewer không thấy item do mình gán; trả lại bắt buộc comment; item về đúng Annotator.
- [ ] Phân vùng tương tác trả polygon ≤ 1 s (ảnh ≤ 4 MP, GPU tầm RTX 3060) sau lần nạp đầu.
- [ ] Pre-label bằng hai model của hai họ recipe khác nhau (chọn tại lúc test) đều tạo suggestion cùng định dạng geometry.
- [ ] Pre-label bằng một biến thể pretrained trong catalog với ánh xạ lớp → chỉ lớp đã ánh xạ thành suggestion.
- [ ] Phiên âm nháp 1 giờ audio bằng một recipe ASR bất kỳ trong catalog; người gán sửa transcript và chốt label set dùng được để fine-tune một recipe ASR/TTS.
- [ ] Pre-label 10 000 ảnh bằng một model registry tạo suggestion; nhận một đề xuất ghi `source=model`.
- [ ] Active learning phát item điểm cao nhất trước.
- [ ] Diff label set v2/v1 đúng số thêm/sửa/xoá.
- [ ] Export YOLO/COCO từ label set rồi import lại ra cùng số nhãn.
- [ ] Mọi thay đổi nhãn có trong history và audit.
