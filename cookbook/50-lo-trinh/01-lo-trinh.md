# 50/01 — Lộ trình

## 1. Các phase

| Phase | Mục tiêu | Nội dung | Spec |
|---|---|---|---|
| **P0 — Nền tảng** | Có bộ khung chạy được, chứng minh AOT và chạy được repo bất kỳ | Repo theo Cortext, generator CQRS/SQL, migration, gate, proto + codegen 3 ngôn ngữ, kênh agent mẫu, **shim + recipe + placement/holder**, image nền, compose, khung web + Python, spike AOT | [50/02](02-p0-nen-tang.md) ✅ |
| **P1 — Lõi chạy được** | Một vòng đời ML trọn vẹn trên nhiều PC, **với nhiều họ model** | M01, M02, M03, M04, M05, M06, M08, M09, M11, M12, M16, **M17 Model Catalog** + vận hành ([20/09](../20-kien-truc/09-van-hanh.md)) + UI ([30-ui](../30-ui/)) | ✅ đầy đủ |
| **P2 — MLOps đầy đủ** | Tự động hoá và chất lượng | M10 HPO/AutoML, M13 giám sát/drift/CT, M14 pipelines + agents; mở rộng M04 (SQL, data pipeline, embedding), M05 (video tracking, audio, consensus/QA), M11 (approval), M12 (canary, batch, autoscale theo latency), M16 (email thông báo, Slack, webhook), SSO, MLflow-compatible | 🟡 khung |
| **P3 — Nâng cao** | Quy mô và LLM | M07 feature store, M15 LLMOps, streaming, document labeling, edge OTA, khám phá/chia sẻ giữa workspace, HA control plane, nhiều cụm | 🟡 khung |

## 2. Thứ tự P1 và phụ thuộc
```
P0 ─► M01 ─► M02 ─► M03 ─► M17 ─┬─► M04 ─► M05 ──────────┐
                                  ├─► M09 ─────────────────┼─► M11 ─► M12
                                  └─► M08                  │
      M06 (audit có từ P0; lineage + UI cuối P1)   M16 (song song)   Vận hành 20/09 (song song)
      Quan sát & khôi phục 20/10: bước 1–2 cần trước nghiệm thu M03/M09; bước 3–4 song song
```

## 3. Tiêu chí xong P1 (kịch bản tích hợp)
**Mục tiêu (D-25):** người dùng **re-train bất kỳ model nào** trên dữ liệu của họ một cách **đơn giản và trực quan** — chọn dữ liệu, chọn recipe, bấm Train, xem kết quả, dùng model. Kịch bản **không gắn với model cụ thể**: recipe được chọn tại thời điểm nghiệm thu từ catalog (dựng sẵn hoặc vừa nhập), ít nhất một recipe cho **mỗi nhóm task** — ảnh, văn bản, âm thanh, bảng. Recipe dựng sẵn hiện có ([20/08 §13](../20-kien-truc/08-recipe-va-runtime.md)) chỉ là ứng viên.

Chạy trên 2 PC GPU (Linux, Windows + WSL 2) + 1 Mac, control plane dựng bằng `docker compose`, mọi binary .NET là Native AOT; không unit test — nghiệm thu bằng kịch bản tích hợp này (D-19):
1. Cài đặt lần đầu qua `/setup`, tạo Instance Admin và workspace; email mời gửi qua SMTP test (hoặc copy link) (M01, D-27).
2. Admin cắm 3 máy (M02); Mac hiện đúng OS thật và chế độ CPU; overlay `mlp-net` lên trên cả ba (D-26).
3. Data Owner tạo **một dataset mỗi nhóm task**: ảnh (upload), văn bản, âm thanh + transcript, bảng (CSV, chọn target); ít nhất một dataset từ thư mục trên một PC (local-only) (M04).
4. Lead tạo labeling project, pre-label bằng một biến thể pretrained trong catalog (ánh xạ lớp) hoặc một model trong registry; phân vùng tương tác qua deployment `assist`; 2 Annotator gán, 1 Reviewer duyệt; chốt label set v1 → dataset v1 (M05, M04).
5. ML Engineer mở notebook trong môi trường của một recipe, đọc dataset v1, `ray.init()` chạy task trên máy khác (M08, D-24).
6. **Re-train**: với mỗi dataset, chọn một recipe verified cùng task từ UI → Train; dataset ảnh train thêm bằng ít nhất ba recipe khác họ và so sánh trên một biểu đồ (metric tên chuẩn của task) (M17, M09).
7. **Nhập model mới**: thêm một repo chưa có trong catalog (có hoặc không có `mlp-recipe.yaml`) → build → smoke test → train trên dataset của mình, không sửa mã nền tảng (M17).
8. Tắt một PC giữa chừng → job resume ở PC còn lại; khởi động lại `ray-head` khi đang train → job không dừng (M03, D-29).
9. Đăng ký model, export theo định dạng recipe khai, Admin promote production; import một model HF (`hf-auto`) và một file `.onnx` (`generic-onnx`) qua `inspect_model` (M11, 20/08 §5.5).
10. Deploy một endpoint mỗi nhóm task đã train mà recipe có `serve` (streaming nghiệm thu bằng task `generate`), gọi `curl`, đổi version không lỗi, rollback được, autoscale thêm replica khi tải tăng (M12).
11. Từ deployment truy ngược lineage tới label set v1, người gán và recipe build; audit đủ hành động (kể cả phiên Ray Client); thông báo tới đúng người (M06, M16).

## 4. Năng lực MLOps theo phase
Ánh xạ 18 năng lực trong [10/03 Đối chiếu MLOps](../10-khao-sat/03-doi-chieu-mlops.md):

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Kết nối & tiền xử lý dữ liệu | upload/S3/PC, import | SQL, Ray Data pipeline | streaming |
| Versioning & lineage | manifest + lineage | tới prediction | — |
| Feature store | — | — | M07 |
| Môi trường + cấp phát tài nguyên | notebook dựng sẵn, fleet | environment cho job | — |
| Experiment tracking | M09 | MLflow-compatible | — |
| HPO/AutoML | — | M10 | — |
| Registry & vòng đời | stage/alias | approval + kiểm thử | — |
| Đóng gói & suy luận | image theo recipe, export theo recipe, REST/gRPC realtime + streaming | batch | edge |
| Autoscale & phát hành an toàn | autoscale theo request đang xử lý, rolling + rollback | autoscale theo latency, canary/A-B/shadow | — |
| Giám sát, drift, retrain | vận hành cơ bản | M13 | — |
| Pipeline | — | M14 | — |
| Bảo mật & audit | RBAC, audit hash-chain | SSO | — |
| LLMOps | nhãn SFT/preference, serve text | — | M15 |

> **2026-11-20:** Project + Training được làm lại theo D-42 … D-49. Spec đợt 1: [03-dot-1-canvas-cong-thuc.md](03-dot-1-canvas-cong-thuc.md); hồ sơ phản biện: `10-khao-sat/06-lam-lai-project-training/`.
