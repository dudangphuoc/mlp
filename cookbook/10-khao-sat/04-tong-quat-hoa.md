# 10/04 — Tổng quát hoá cho MLPlatform

> **Tham khảo, không phải spec.** Ý tưởng tổng quát hoá đã được đưa vào [20/08 Recipe & runtime](../20-kien-truc/08-recipe-va-runtime.md) và [sổ quyết định](../00-tong-quan/03-quyet-dinh.md); các "câu hỏi mở" ở cuối là lịch sử.

Ultralytics Platform gắn chặt với **computer vision + YOLO**. MLPlatform giữ nguyên vòng đời nhưng tách phần "lõi" khỏi phần "theo loại dữ liệu/bài toán", để train **bất kỳ loại model nào**.

## 1. Cái gì là lõi chung (giữ nguyên ý tưởng)

| Lõi | Từ Ultralytics | Tổng quát hoá |
|---|---|---|
| Workspace, Team, RBAC, API key, Billing/credit, Usage, Trash | như nhau | giữ nguyên |
| Dataset + Version bất biến | ảnh + annotation, snapshot NDJSON | **Dataset = tập Item + Label theo schema**, snapshot bất biến định dạng trung lập |
| Training run | YOLO trên GPU | **Job chạy một "Trainer plugin"** với config + compute |
| Model registry | weights `.pt`, metric CV | artifact bất kỳ + metric khai báo bởi plugin |
| Export | 23 format CV | **Exporter plugin** theo framework (ONNX, TorchScript, GGUF, safetensors, TFLite, PMML…) |
| Deploy + monitoring | endpoint ảnh | **Serving runtime** theo modality (REST/gRPC/stream) |
| Compute | cloud GPU / local / on-prem worker | giữ cả 3, worker trung lập framework |
| Agents | YOLO + LLM + Gate + Webhook | block = mọi model trong registry + công cụ |
| Explore | cộng đồng CV | marketplace dataset/model đa modality |

## 2. Cái gì phải thành "plugin theo modality/task"

| Điểm mở rộng | Ví dụ |
|---|---|
| **Modality** (kiểu Item) | image, video, text, audio, tabular, time-series, document, 3D/point cloud, multimodal |
| **Task** (kiểu Label + metric) | detect/segment/pose/OBB/classify (ảnh); NER, classification, QA, SFT/RLHF cho LLM (text); ASR, audio classification; regression/classification (bảng); forecasting, anomaly (chuỗi thời gian) |
| **Importer** | YOLO, COCO, NDJSON, CSV/Parquet, JSONL, HF datasets, WAV+transcript… |
| **Annotation UI** | bbox/polygon/keypoint (ảnh), span/label (text), segment theo thời gian (audio/video), sửa ô (bảng) |
| **Smart labeling** | SAM (ảnh), LLM pre-label (text), Whisper (audio), model của chính user |
| **EDA charts** | phân bố class, kích thước ảnh, độ dài văn bản, phân phối cột, mùa vụ chuỗi thời gian… |
| **Trainer** | Ultralytics, HF Transformers/PEFT, PyTorch Lightning, scikit-learn, XGBoost/LightGBM, custom Docker |
| **Evaluator** | mAP, F1, BLEU/ROUGE, WER, RMSE/MAPE, AUC… |
| **Exporter / Runtime** | xem bảng trên |
| **Predict playground** | upload ảnh/webcam, ô nhập text/chat, ghi âm, form dòng dữ liệu |

Mỗi plugin khai báo một **manifest**: modality hỗ trợ, schema label, schema config (để sinh form + editor YAML như "Advanced Settings"), metric phát ra, artifact tạo ra, yêu cầu compute (để ước tính chi phí).

## 3. Những ý tưởng hay nên giữ

1. **Version dataset bất biến** gắn với mọi training run → tái lập được.
2. **Lệnh tái lập** hiển thị ngay trên trang model (config đầy đủ).
3. **Local training stream metric về platform** bằng API key + URI `mlp://owner/datasets/name`.
4. **Data stays put**: train trực tiếp trên bucket S3/GCS/Azure hoặc thư mục on-prem mount read-only.
5. **Ước tính chi phí trước khi train**, tính tiền theo compute thật.
6. **Embeddings cho dataset** → tìm kiếm ngữ nghĩa + clustering (áp dụng được cho mọi modality).
7. **Auto-label bằng model đã train** → vòng lặp active learning.
8. **Agents** = nối model + LLM + logic + webhook, xuất ra code.
9. Đo latency tới từng region trước khi deploy.

## 4. Câu hỏi mở (cần bạn quyết khi viết spec)

- Phạm vi MVP: modality nào làm trước (ảnh + text + bảng?).
- ~~Hạ tầng compute~~ → **Đã chốt:** portal chỉ điều phối; compute = các PC worker được cắm vào (xem [03](02-ultralytics-on-premise.md)).
- ~~Billing/credit~~ → **Đã chốt:** tạm bỏ qua.
- **Đã chốt:** lớp thực thi = Ray cluster trên các PC join qua overlay WireGuard; bổ sung 7 năng lực MLOps còn thiếu (xem [04](03-doi-chieu-mlops.md), [05](../README.md)).
- **Đã chốt:** Ray head nằm trong control plane (0 CPU/0 GPU, chỉ điều phối); backend control plane viết bằng **.NET**; tên dự án **MLPlatform**.
- Còn mở: WireGuard hub tự quản hay dùng Headscale/Netbird; dùng Feast/MLflow có sẵn hay tự viết; framework Web UI.
- Multi-tenant SaaS hay triển khai nội bộ một tổ chức?
- Stack ưu tiên (gợi ý từ quan sát: Next.js frontend; backend + worker Python cho ML).
