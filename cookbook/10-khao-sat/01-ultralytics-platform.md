# 10/01 — Khảo sát chức năng Ultralytics Platform

> **Tham khảo, không phải spec.** Khảo sát chức năng lần 1 (17/11/2026); khảo sát giao diện chi tiết bằng REA ở [05](05-ui-ultralytics-rea.md).

> Nguồn: khảo sát trực tiếp `platform.ultralytics.com` bằng tài khoản Free (17/11/2026), chỉ đọc giao diện và luồng, không tạo/huỷ/train gì.
> Mục đích: hiểu **chức năng + ngữ cảnh nghiệp vụ** để thiết kế MLPlatform. Không sao chép thương hiệu, nội dung hay mã nguồn.

## 1. Định vị sản phẩm

Một nền tảng **end-to-end MLOps cho computer vision**, gói trọn vòng đời:

```
Dữ liệu ──► Gán nhãn ──► Phiên bản dataset ──► Train (cloud / local / on-prem)
   ▲                                                   │
   │                                                   ▼
Explore (cộng đồng)        Model ──► Predict thử ──► Export (23 định dạng) ──► Deploy (endpoint 42 vùng) ──► Monitoring
                                                   │
                                                   └──► Agents (workflow kéo-thả nối model + LLM + logic)
```

Người dùng mục tiêu: kỹ sư ML / dev ứng dụng không muốn tự dựng hạ tầng GPU, labeling, serving.
Mô hình kinh doanh: **credit trả theo GPU-giờ** + gói thuê bao (Free / Pro / Enterprise) giới hạn tài nguyên.

## 2. Bản đồ điều hướng (URL)

| Khu vực | URL | Ghi chú |
|---|---|---|
| Home (dashboard) | `/home` | Thống kê tài nguyên, upload nhanh, storage, activity |
| Explore | `/explore` | Dataset / Images / Models / Projects công khai |
| Agents | `/agents` | Workflow builder dạng node graph |
| Hồ sơ / workspace | `/{user}` `?tab=datasets|projects|deployments` | Trang công khai của user/team, followers |
| Dataset | `/{user}/datasets/{slug}` `?tab=images|classes|charts|models|versions` | |
| Project | `/{user}/{project}` | Nhóm nhiều model, so sánh metric |
| Model | `/{user}/{project}/{model}` `?tab=overview|train|predict|export|deploy` | |
| Settings | `/settings?tab=profile|api-keys|plans|billing|usage|referrals|teams|integrations|trash` | |
| Help | `/help` | Video, docs, feedback form |

Sidebar nhóm theo **vòng đời**: *Annotate* (datasets) → *Train* (projects) → *Deployments*. Thanh trên: ô tìm kiếm (⌘K), số dư credit, ngôn ngữ, chuyển app, thông báo, theme. Nút nổi **Ask AI** (trợ lý trong app).

## 3. Chức năng chi tiết

### 3.1 Home (dashboard)
- Thẻ người dùng: plan, username, email; đếm Datasets / Images / Annotations / Projects / Models / Exports / Deployments.
- Ô thả file nhanh: ảnh <50 MB, video <1 GB, dataset <10 GB (ZIP, TAR, NDJSON); thả `.pt` (≤1 GB) để import model.
- Storage: dung lượng dùng/hạn mức, theo loại (datasets, models), item lớn nhất, hạn mức models (1/100) và deployments (0/3).
- Recent Activity: bảng có tìm kiếm, sort, phân trang.
- Banner: "Dữ liệu ở yên chỗ cũ — liên kết GCS/S3/Azure và train không cần di chuyển dữ liệu".

### 3.2 Dataset
**Tạo dataset** (dialog): nguồn = *Upload* | *URL* | *Cloud* (GCS/S3/Azure) | *On Premise*. Hỗ trợ format YOLO, COCO, NDJSON. Trường: tên, slug URL (kiểm tra unique), mô tả, **Task Type**, **License** (Creative Commons), **Blur faces** (ẩn danh hoá), **Visibility** public/private.

**Task types** (đặt ở cấp dataset, đổi được nhưng cảnh báo mất annotation không tương thích):
Detection · Segmentation (instance) · Semantic Segmentation · Depth estimation · Classification · Pose (keypoints) · OBB (oriented bbox).

**Tab Images**: grid/compact/table view; lọc theo split (All/Train/Val/Test); tìm kiếm theo tên, class, metadata *và nội dung ảnh* (semantic search nhờ embeddings); **Clustering** (gom cụm theo embedding); bật/tắt hiển thị annotation; thả thêm ảnh.

**Annotation editor** (mở 1 ảnh):
- Chế độ *Draw* (thủ công) và *Smart* (click-to-segment bằng SAM 3.1).
- Auto-annotate bằng model YOLO đã train ("YOLO Smart annotation").
- Undo/redo, xoá chọn, Save, xoay ảnh, zoom, next/prev, đổi màu class, ẩn class, xem thông tin ảnh, tải/xoá ảnh, phím tắt.

**Tab Classes**: biểu đồ phân bố class; bảng index/tên/số annotation/số ảnh; *merge* nhiều class, xoá, thêm class.

**Tab Charts** (EDA tự động): phân bố split, top classes, kích thước ảnh, dung lượng file, heatmap W×H & aspect ratio, heatmap vị trí tâm bbox, định dạng ảnh, kích thước bbox, số object/ảnh.

**Tab Models**: các model train trên dataset này.

**Tab Versions**: snapshot **bất biến** (NDJSON gồm ảnh + class + annotation) để train tái lập được.

**Menu khác**: Batch Annotation, Blur faces, Information, Refresh, Delete; Download dataset; Star; nút **New Model** (train từ dataset).

### 3.3 Project
- Gom nhiều model cùng mục tiêu; license riêng (vd. AGPL).
- Danh sách model nhóm theo task, chọn nhiều model để **so sánh biểu đồ** Metrics / Loss / Learning rate (chọn series hiển thị).
- Thả `.pt` để import model có sẵn.

### 3.4 Train New Model (dialog)
- **Base model**: model chính thức (YOLO26 n/s/m/l/x, YOLO11, YOLOv8, YOLOv5…) hoặc "My Models" (fine-tune tiếp).
- **Dataset**: chọn dataset của mình hoặc dataset công khai (vd. COCO8).
- Tham số chính: epochs, batch (auto), image size, tên run.
- **Advanced Settings**: editor YAML đầy đủ hyperparameter + augmentation; thả file `.yaml/.json` hoặc dán vào; nút reset.
- **Cloud Training**: tuỳ chọn "Save Dataset Version"; chọn **GPU type** (~24–26 loại, mỗi loại ghi VRAM + $/giờ); hiển thị số dư, Top Up; **ước tính chi phí** (thời gian/epoch × số ảnh × đơn giá) — tính tiền theo GPU-time thực.
- **Local Training**: sinh lệnh CLI có API key + URI dạng `ul://owner/datasets/name`; chạy trên máy riêng và **stream metric về platform**.

### 3.5 Model
Header: task, phiên bản framework, license, KPI (mAP50, mAP50-95, Precision, Recall), dataset đã dùng (số ảnh, số class, train/val).
- **Overview**: run info (trạng thái, runtime, thời điểm, parent model, lệnh tái lập đầy đủ), bảng hyperparameter (export được), bảng metric cuối.
- **Train**: biểu đồ realtime Metrics / Loss / LR; tab **Console** (log) và **System** (GPU/CPU/RAM).
- **Predict**: upload ảnh / webcam / ảnh mẫu; slider confidence, IoU, image size; kết quả dạng ảnh + JSON response + snippet docs gọi API.
- **Export**: 23 định dạng (TorchScript, ONNX, OpenVINO, TensorRT, CoreML, TF SavedModel/GraphDef/Edge TPU, LiteRT, Paddle, MNN, NCNN, IMX500, RKNN, ExecuTorch, các NPU Axelera/DeepX/Qualcomm/Hailo/Huawei/AMD/NXP…); mỗi format export nhiều lần với cấu hình khác nhau; "Export All".
- **Deploy**: bản đồ 42 region, đo latency từ trình duyệt tới từng region, nút Deploy tạo **dedicated inference endpoint**; sau đó có monitoring (request, latency, lỗi, log).

### 3.6 Explore (cộng đồng)
Tab Datasets / Images / Models / Projects công khai; thẻ dataset có task, số class, dung lượng, split, tag, mô tả tự sinh; sort, tìm kiếm; clone về workspace.

### 3.7 Agents (workflow builder)
- Canvas node-graph, có template và "Open workflow…", chạy thử (trial 60s), **xuất ra Python**.
- Block: *Inputs* (Dataset, Image) · *Models* (YOLO, LLM vision) · *Logic* (Gate: lọc theo điều kiện/nhịp) · *Platform* (Output, Webhook POST JSON, Export, Deployment).
- Ví dụ mặc định: Ảnh → Detect người → Gate "person ≥ 1" → LLM mô tả cảnh → Output.

### 3.8 Settings
- **Profile**: avatar, tên, tổ chức, use case, bio, mạng xã hội; *visibility mặc định* cho project/dataset; early access; email (xác minh bằng mã); **data region** (chọn lúc onboarding); xuất toàn bộ metadata JSON; liên kết Google/GitHub; xoá tài khoản.
- **API Keys**: tạo key cho app và khoá của provider (mã hoá AES-256-GCM).
- **Plans**: Free / Pro ($29/seat/tháng) / Enterprise — khác nhau ở storage, upload tối đa, số model, số training đồng thời, số deployment, loại GPU, cloud bucket, số thành viên, SSO/SAML, on-prem, HIPAA.
- **Billing**: credit, top-up, auto top-up, promo code, thẻ, địa chỉ, lịch sử giao dịch.
- **Usage**: số dư, tổng chi, số usage event, chi theo ngày/hạng mục, storage.
- **Referrals**: giới thiệu nhận credit.
- **Teams**: thành viên + RBAC 4 vai trò **Owner / Admin / Editor / Viewer** (ma trận quyền rõ ràng).
- **Integrations**: hạ tầng (On Premise worker, S3, GCS, Azure Blob), thông báo (Slack), import (W&B, Roboflow, Labelbox, LabelMe, Label Studio).
  *On Premise*: cài worker CPU/GPU bằng lệnh dùng 1 lần, mount thư mục dataset **read-only**, model ở lại máy khách.
- **Trash**: xoá mềm, tự xoá vĩnh viễn sau 30 ngày.

### 3.9 Help
Video hướng dẫn theo vòng đời, link docs, cộng đồng, form feedback (loại, nội dung, rating, ảnh chụp).

## 4. Kiến trúc quan sát được (từ network)

- Frontend **Next.js (App Router, RSC, Turbopack)**; Sentry; analytics.
- Auth kiểu session: `GET /api/auth/get-session` (đăng nhập Google/GitHub OAuth).
- File/ảnh qua CDN với **signed URL có hạn** (`cdn…?Expires&Signature`), lưu theo vùng (`/eu/…`).
- REST API theo tài nguyên, địa chỉ hoá bằng `owner/slug`:

```
GET  /api/users?username={u}
GET  /api/teams
GET  /api/settings
GET  /api/billing/balance
GET  /api/datasets/{owner}?limit=
GET  /api/datasets/{owner}/{ds}
GET  /api/datasets/{owner}/{ds}/images?limit&offset&sort&includeLabels
GET  /api/datasets/{owner}/{ds}/class-stats
GET  /api/datasets/{owner}/{ds}/embeddings
GET  /api/datasets/{owner}/{ds}/predict/batch
HEAD /api/datasets/{owner}/{ds}/models
GET  /api/projects/{owner}?limit=
GET  /api/projects/{owner}/{project}
GET  /api/models/{owner}/{project}/{model}
GET  /api/models/{owner}/{project}/{model}/files
GET  /api/models/{owner}/{project}/{model}/exports
GET  /api/deployments/{owner}?model={project}/{model}
GET  /api/training/{runId}
```

- Định danh tài nguyên dạng URI `ul://{owner}/datasets/{name}`, `ul://{owner}/{family}/{model}` — SDK/CLI phân giải URI này qua API key.
- Training run là thực thể riêng (`/api/training/{id}`) gắn với model.

## 5. Khái niệm lõi (domain model sơ bộ)

| Thực thể | Thuộc tính chính | Quan hệ |
|---|---|---|
| Workspace (User/Team) | slug, plan, region, credit | có Members(role), ApiKeys, Integrations |
| Dataset | task, license, visibility, splits, classes | có Items (ảnh/video), Annotations, Versions, Embeddings |
| DatasetVersion | snapshot bất biến (NDJSON) | được TrainingRun tham chiếu |
| Project | license, visibility | có Models |
| Model | task, framework version, metrics, weights | sinh từ TrainingRun hoặc import; có Exports, Deployments |
| TrainingRun | base model, dataset version, config YAML, compute (cloud GPU/local/on-prem), status, logs, metric series, system stats, cost | |
| Export | format, tham số, file | |
| Deployment | region, endpoint, trạng thái, metrics/log | |
| Workflow (Agent) | graph block + cạnh | dùng Model/Deployment/LLM/Webhook |
| UsageEvent / Transaction | loại, số tiền | billing |

## 6. Chưa khảo sát được / cần làm thêm

- Nội dung phản hồi API (schema JSON) — trình duyệt chặn đọc khi có dữ liệu phiên.
- Phân tích tĩnh bundle JS bằng REA (`analyze_web_bundle`) — cần Chrome mở cổng CDP (`--remote-debugging-port`).
- Luồng thật của: upload/ingest, training đang chạy (Console/System), deployment + monitoring, Agents chạy thử, tạo API key — cần thao tác có tác dụng phụ/tốn credit, chưa làm.
