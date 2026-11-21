# M04 — Data Management

> **Phase:** P1 (connector nâng cao P2/P3) · **Phụ thuộc:** M01, M02, M03, M17 · **Quyết định:** D-17, D-20, D-22 · **Proto:** `data.proto` (`DataService`) · **Schema DB:** `data`

## 1. Mục tiêu & phạm vi
Tạo và quản lý dataset đa modality từ nhiều nguồn, chia split, thống kê, và **version bất biến** để train tái lập được.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Modality image, video (trích frame), text, **audio (kể cả cặp audio + transcript + người nói cho ASR/TTS)**, tabular | ✔ | time-series | document |
| Nguồn upload, URL, S3/kho S3 ngoài, thư mục trên máy (local-only) | ✔ | SQL/warehouse | Kafka |
| Import COCO, YOLO, Pascal VOC, NDJSON, thư mục classify, CSV/Parquet, JSONL, manifest audio (JSONL `audio, text, speaker`), thư mục kiểu LJSpeech, ảnh + mask PNG (semantic), ảnh + depth PNG 16-bit (depth) | ✔ | | |
| Chuyển dataset version sang **định dạng recipe cần** (cache theo định dạng) — [20/08 §7](../20-kien-truc/08-recipe-va-runtime.md) | ✔ | | |
| Split train/val/test (theo thư mục, ngẫu nhiên có seed, thủ công) | ✔ | | |
| EDA tự động theo modality | ✔ | | |
| Version dataset (manifest bất biến) | ✔ | | |
| Export theo mọi bộ chuyển định dạng có sẵn (COCO, YOLO, VOC, NDJSON, JSONL, manifest audio, CSV/Parquet) | ✔ | | |
| Embedding search, clustering, data pipeline (Ray Data), rescan | | ✔ | |

## 2. Khái niệm
- **Item**: 1 ảnh / 1 video / 1 văn bản / 1 clip audio / 1 dòng bảng.
- **Storage kind**: `managed` (kho S3), `external` (S3 ngoài, không copy), `host_local` (trên máy, **không bao giờ rời máy** — D-17).
- **Dataset version**: manifest Parquet bất biến gồm item, sha256, split, **label set version** ([M05](M05-labeling.md)).
- Bảng (tabular): lưu Parquet; chỉ tạo bản ghi item khi bật gán nhãn theo dòng (tối đa 1 triệu dòng ở P1).

## 3. Dữ liệu (schema `data`)

| Bảng | Cột chính |
|---|---|
| `datasets` | id, workspace_id, slug, name, description, owner_id (Data Owner, D-16), modality, task (khoá `tasks.yaml`), storage_kind, integration_id (S3, M01), host_id, root_path, label_schema_id, visibility, license, status (`creating/importing/ready/unavailable/error`), item_count, labeled_count, size_bytes, current_version, tags text[], icon |
| `items` | id, dataset_id, external_key (unique trong dataset), uri, sha256, size_bytes, mime, split (`train/val/test/none`), width, height, duration_ms, frame_count, text_preview, row_index, metadata jsonb, thumbnail_uri, status (`ok/corrupt/missing`), deleted_at |
| `tabular_schemas` | dataset_id, version, columns jsonb (`name, dtype, role: feature/target/id/ignore`), target_column, problem (`classify/regress`) — chỉ dataset bảng |
| `converted` | dataset_version_id, converter, uri (kho S3 `mlp-converted` hoặc `host://`), host_id (khi `host_local`), sha256, job_id, state (`pending/ready/failed`), created_at — pk (dataset_version_id, converter, host_id) |
| `uploads` | id, dataset_id, file_name, size, s3_upload_id, parts jsonb, state |
| `imports` | id, dataset_id, source, format, options jsonb, job_id, state, counters jsonb (items_added, labels_added, skipped, errors), error_report_uri |
| `dataset_versions` | id, dataset_id, number, label_set_version_id, schema_version, manifest_uri, manifest_sha256, item_count, split_counts jsonb, size_bytes, note, created_by, created_at |
| `dataset_stats` | dataset_id, kind, payload jsonb, computed_at |
| `exports` | id, dataset_id, version_id, format, job_id, uri, state |

Chỉ mục: `items(dataset_id, split)`, `items(dataset_id, external_key)` unique, GIN `items.metadata`.

## 4. Trạng thái & quy tắc

```
creating → importing → ready ⇄ importing (import thêm)
ready → unavailable (máy chứa dữ liệu offline/disconnected) → ready
importing → error (import hỏng toàn bộ)
```
- **R1** Task của dataset là **khoá trong [`tasks.yaml`](../../recipes/_tasks/tasks.yaml)** (M17, D-22), không cố định. P1 có sẵn: ảnh `classify, detect, segment, semantic, pose, obb, depth`; text `text-classify, ner, generate` (SFT/preference theo định dạng nhãn); audio `audio-classify, asr, tts`; bảng `tabular-classify, tabular-regress`. Video: import tách frame thành item ảnh (giữ `frame_index`, `source_video`) rồi dùng task ảnh. Dataset `tts`/`asr` là cặp audio + văn bản (+ người nói).
- **R1b** Dataset bảng phải có `tabular_schemas` với đúng một cột `target` trước khi tạo version; ingest tự đoán dtype, người dùng chọn target + loại bỏ cột ở S-04-9; `tabular-classify` khi target là phân loại, `tabular-regress` khi số liên tục.
- **R2** Đổi task khi đã có nhãn chỉ khi tương thích (segment → detect suy bbox); ngược lại tạo dataset mới.
- **R3** `host_local`: mount chỉ-đọc; xoá item chỉ xoá metadata/nhãn; thumbnail sinh và lưu trên máy; byte phục vụ qua **data channel**.
- **R4** Trùng sha256 trong dataset → bỏ qua, ghi vào counters.
- **R5** Tạo version khi `ready`; manifest không đổi so với bản trước → trả lại version cũ.
- **R6** Train chỉ nhận **dataset version**, không nhận dataset đang sống.
- **R7** Xoá dataset có version đang được model tham chiếu → chặn, chỉ cho "lưu trữ".
- **R8** `EnsureConverted(version, converter, host?)`: bản `ready` trong `converted` → trả URI; chưa có → tạo job `convert` (image `mlp-tools` cho bộ chuyển chuẩn, image recipe cho bộ chuyển của recipe — [20/08 §7](../20-kien-truc/08-recipe-va-runtime.md)) và trả `job_id` để M03 đặt phụ thuộc. `host_local` → job ghim máy chứa dữ liệu, ghi vào cache trên máy (D-17).
- **R9** Data Owner (`owner_id` hoặc `resource_roles.owner`) có `dataset.own`: đổi visibility, duyệt export, giao quyền trên dataset.

## 5. Luồng chính
1. **Upload**: `CreateDataset` → `CreateUpload` (multipart kho S3, presigned từng part) → trình duyệt upload thẳng kho S3 (4 part song song, tiếp tục khi rớt mạng) → `CompleteUpload` → `StartImport` → job `ingest` trên máy: giải nén, nhận dạng format, tạo item theo lô 1 000, tạo nhãn (M05), thumbnail, thống kê → `ready`.
2. **S3 ngoài**: chọn tích hợp S3 của workspace (M01 S-01-10) → chọn bucket/prefix → import đọc trực tiếp; thumbnail lưu kho S3 platform.
3. **Thư mục trên máy**: chọn máy → `BrowseHostPath` → chọn thư mục → import job ghim `host:<id>` → item `uri = host://{hostId}/{relpath}`.
4. **Xem item**: `GetItemContent` → managed/external: presigned URL 5 phút; host_local: api mở **data channel** tới file server của agent, stream byte về trình duyệt, không ghi đĩa.
5. **Tạo version**: `CreateVersion(label_set_version?)` → xuất manifest Parquet lên kho S3 → lineage (M06).
6. **Export**: `CreateExport(version, format)` → job đóng gói → link tải; host_local ghi ra thư mục trên chính máy.

### EDA (tính khi ingest và khi tạo version)
- Chung: số item theo split, % đã gán, phân bố class, số nhãn/item.
- Ảnh/video: kích thước, tỉ lệ khung, định dạng, heatmap tâm bbox, kích thước bbox, số đối tượng/ảnh.
- Văn bản: độ dài, ngôn ngữ, phân bố nhãn span.
- Audio: tổng thời lượng, phân bố độ dài clip, sample rate, số người nói và thời lượng theo người nói, độ dài transcript, tỉ lệ ký tự/giây.
- Bảng: kiểu cột, % null, min/max/mean, top giá trị, phân bố target.

## 6. Hợp đồng (`data.proto`)

| RPC | Quyền | Mô tả |
|---|---|---|
| `ListDatasets` / `GetDataset` | `dataset.read` | |
| `CreateDataset` / `UpdateDataset` / `DeleteDataset` | `dataset.write` / `dataset.delete` | `etag` khi sửa |
| `CreateUpload` / `CompleteUpload` / `AbortUpload` | `dataset.write` | Presigned multipart |
| `StartImport` / `GetImport` / `WatchImport` | `dataset.write` | `request_id` |
| `ListItems` / `GetItem` / `UpdateItem` / `BatchUpdateItems` / `DeleteItems` | đọc: `dataset.read` (Annotator: chỉ item được giao); ghi: `dataset.write` | Lọc split, class, đã gán, metadata, text |
| `GetItemContent` | như `GetItem` | Presigned URL hoặc URL proxy data channel |
| `RandomizeSplits` | `dataset.write` | Tỉ lệ + seed |
| `GetDatasetStats` | `dataset.read` | |
| `ListVersions` / `GetVersion` / `CreateVersion` | đọc / `dataset.write` | |
| `CreateExport` / `GetExport` | `dataset.read` | Job đóng gói |
| `GetTabularSchema` / `UpdateTabularSchema` | đọc / `dataset.write` | Cột, dtype, vai trò, target |
| `EnsureConverted` | nội bộ (M03) / `job.submit` | R8; trả URI hoặc `job_id` |
| `ListConverted` | `dataset.read` | Các bản đã chuyển của một version (định dạng, kích thước, máy) |
| `ResolveUri` | `dataset.read` | `mlp://…` → manifest (SDK) |
| `RuntimeService.AddItems` / `ReportStats` / `CompleteImport` | token job | Dùng trong job ingest |

## 7. Màn hình

### S-04-1 Datasets — `/w/{ws}/datasets`
`CollectionView` thẻ dataset: ảnh đại diện (lưới 4 ảnh có overlay nhãn) hoặc biểu tượng modality, tên, badge task + modality + nguồn (Upload/S3/Máy), số item, % đã gán, version mới nhất, cập nhật. Lọc modality, task, nguồn. Badge "Dùng được với" liệt kê số recipe verified hỗ trợ task của dataset. Rỗng: "Chưa có dataset. Tạo dataset từ file, S3 hoặc thư mục trên máy." + `Tạo dataset`.

### S-04-2 Tạo dataset (hộp thoại)
```
┌ Tạo dataset ──────────────────────────────────────────────────────────────┐
│ Nguồn: [Tải lên] [URL] [S3] [Thư mục trên máy]                            │
│ ┌ Kéo thả file/thư mục/archive hoặc bấm để chọn ──────────────────────────┐ │
│ │ Ảnh <50 MB · Video <1 GB · Archive ZIP/TAR <10 GB · CSV/Parquet · JSONL │ │
│ └─────────────────────────────────────────────────────────────────────────┘ │
│ Tên            [                         ]                                │
│ Đường dẫn      /w/vision/datasets/[slug      ]  ✔ dùng được               │
│ Mô tả          [                         ]                                │
│ Loại dữ liệu   [Ảnh ▾]   Bài toán [Detect ▾]   Định dạng [Tự nhận ▾]       │
│ License        [CC BY 4.0 ▾]   Hiển thị  (○ Riêng tư ● Workspace)         │
│ ▸ Nâng cao: chia split ngẫu nhiên, seed, làm mờ khuôn mặt (P2)            │
│                                               [Huỷ] [Tạo dataset]         │
└───────────────────────────────────────────────────────────────────────────┘
```
- Tab **S3**: chọn tích hợp S3 (link "Thêm tích hợp" tới S-01-10), bucket, prefix, nút "Kiểm tra".
- Tab **Thư mục trên máy**: chọn máy (chỉ máy online có `data_path`), cây thư mục từ `BrowseHostPath`, ghi chú "Dữ liệu không rời máy này; AI hỗ trợ chỉ chạy trên máy này".
- Bài toán lọc theo loại dữ liệu (R1). Sau tạo → chuyển S-04-7 theo dõi import.

### S-04-3 Dataset — Items — `/w/{ws}/datasets/{ds}?tab=items`
```
┌ [ảnh] PCB defects  [Detect] [Ảnh] [CC BY 4.0] [● Ready]      [Train] [⤒ Thêm dữ liệu] [⤓] [⋯] ┐
│ 12 480 item · 9 102 đã gán · 41 233 nhãn · 3.2 GiB · cập nhật 2 giờ trước                     │
│ Mô tả…                                                                                         │
│ [Items] [Classes 7] [Charts] [Gán nhãn] [Versions 3] [Imports] [Models 2]                      │
│ [Tất cả 12480][● Train 9984][● Val 1248][● Test 1248]  ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬ (thanh tỉ lệ)          │
│ 👁 nhãn  Lọc: [Class ▾][Đã gán ▾][Metadata]  Sắp xếp [Mới nhất ▾]  [Tìm theo tên/class/…] [▦▤☰] │
│ ┌────┐┌────┐┌────┐┌────┐┌────┐┌────┐                                                          │
│ │img ││img ││img ││img ││img ││img │  mỗi ô: overlay nhãn, badge split, ☐ chọn, ⓘ ⤓ 🗑          │
│ └────┘└────┘└────┘└────┘└────┘└────┘                                                          │
│ Đã chọn 24: [Đổi split ▾] [Giao gán nhãn] [Xoá]                                                │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```
- Hiển thị theo modality: ảnh/video lưới; văn bản danh sách (đoạn đầu + nhãn); audio danh sách + sóng âm; bảng = lưới dòng.
- Click item → trình xem (ảnh + overlay, video + frame, text, audio player + transcript + người nói, dòng bảng); nút "Mở trong trình gán nhãn" nếu có quyền.
- `[Train]` mở hộp Train (M09) với dataset này; `[⋯]`: Đổi tên, Đổi bài toán, Chia split ngẫu nhiên, Xoá.
- Dataset `unavailable`: banner "Máy chứa dữ liệu đang offline".

### S-04-4 Classes
Biểu đồ phân bố (log scale, toàn màn hình) + bảng: chọn, Màu, Chỉ số, Tên, Số nhãn, Số item; hành động: Thêm class, Đổi tên, **Gộp vào một**, Xoá (đi qua label schema M05).

### S-04-5 Charts
Lưới biểu đồ theo modality (mục 5 EDA), mỗi thẻ có tiêu đề + dòng tóm tắt (vd. "8 ảnh · W trung bình 587 px").

### S-04-6 Versions
Bảng: số version, mô tả, label set version, số item theo split, dung lượng, người tạo, ngày, model đã dùng; `+ Tạo version` (mô tả, chọn label set version); mỗi dòng: Tải manifest, Export, Xem lineage.

### S-04-7 Imports
Bảng lần import: nguồn, định dạng, trạng thái (`ProgressSteps`: tải → giải nén → nhận dạng → tạo item → nhãn → thumbnail → thống kê), số liệu, link báo cáo lỗi.

### S-04-8 Models
Danh sách run/model dùng dataset (từ lineage); rỗng: "Chưa có model nào train trên dataset này" + `Train`.

### S-04-9 Cột (chỉ dataset bảng) — `/w/{ws}/datasets/{ds}?tab=columns`
Bảng cột: tên, dtype (sửa được), % null, số giá trị khác nhau, mẫu giá trị, **vai trò** (`feature/target/id/ignore`, chọn trong ô). Thanh trên: "Target: `churn` · Bài toán: Phân loại (2 lớp)" — đổi target tự đề xuất `tabular-classify`/`tabular-regress`. Chưa chọn target → banner "Chọn cột target để tạo version". Hành động: `UpdateTabularSchema`.

### Thẻ "Bản đã chuyển định dạng" (trong S-04-6, mở rộng từng version)
Danh sách bản chuyển (`ListConverted`): định dạng, dung lượng, nơi lưu (kho S3 / máy), ngày, job; dùng để thấy vì sao lần train sau nhanh hơn.

## 8. Sự kiện
`data.DatasetCreated`, `DatasetReady`, `DatasetUnavailable`, `DatasetDeleted`, `ImportCompleted`, `ImportFailed`, `DatasetVersionCreated`, `ItemsAdded`, `ItemsRemoved`, `DatasetConverted`.

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `DATA_SLUG_TAKEN` | ALREADY_EXISTS | Đường dẫn đã được dùng |
| `DATA_UNSUPPORTED_FORMAT` | INVALID_ARGUMENT | Không nhận dạng được định dạng |
| `DATA_TASK_INCOMPATIBLE` | FAILED_PRECONDITION | Bài toán không hợp với loại dữ liệu/nhãn hiện có |
| `DATA_TOO_LARGE` | RESOURCE_EXHAUSTED | Vượt giới hạn kích thước/quota |
| `DATA_HOST_OFFLINE` | UNAVAILABLE | Máy chứa dữ liệu offline |
| `DATA_CONNECTION_FAILED` | FAILED_PRECONDITION | Không truy cập được bucket |
| `DATA_IN_USE` | FAILED_PRECONDITION | Version đang được model dùng |
| `DATA_NOT_READY` | FAILED_PRECONDITION | Dataset chưa sẵn sàng |
| `DATA_PATH_OUTSIDE_ROOT` | PERMISSION_DENIED | Đường dẫn ngoài thư mục dữ liệu của máy |
| `DATA_TARGET_REQUIRED` | FAILED_PRECONDITION | Dataset bảng cần chọn cột target |
| `DATA_CONVERTER_UNSUPPORTED` | FAILED_PRECONDITION | Không có bộ chuyển từ task này sang định dạng recipe cần |

## 10. Nghiệm thu
- [ ] Upload ZIP 5 GB dạng YOLO, rớt mạng giữa chừng vẫn tiếp tục; import đủ item, class, split.
- [ ] COCO JSON và NDJSON import đúng bbox/polygon/keypoint.
- [ ] Dataset audio dạng manifest JSONL (audio + transcript + speaker) import được; Charts hiện thời lượng theo người nói.
- [ ] Cùng một dataset version chuyển được sang **mọi** định dạng mà các recipe verified của task đó khai (`EnsureConverted`); cây thư mục đúng [20/08 §7](../20-kien-truc/08-recipe-va-runtime.md); lần chuyển thứ hai dùng cache.
- [ ] Dataset CSV: chọn target ở S-04-9 → tạo version → train bằng một recipe bảng bất kỳ.
- [ ] Video import tách frame thành item ảnh, gán nhãn và train như dataset ảnh.
- [ ] Dataset từ thư mục trên máy: không byte ảnh nào xuất hiện trên kho S3/đĩa control plane; vẫn xem được ảnh trên portal.
- [ ] CSV 500 000 dòng import được, Charts hiện thống kê cột.
- [ ] Tạo version hai lần liên tiếp không đổi dữ liệu → cùng một version.
- [ ] Sửa nhãn sau khi tạo v3 không đổi manifest v3.
- [ ] Ảnh hỏng nằm trong báo cáo lỗi, không làm hỏng cả import.
