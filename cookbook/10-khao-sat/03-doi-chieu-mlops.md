# 10/03 — Đối chiếu yêu cầu MLOps

> **Tham khảo, không phải spec.** Bảng chẩn đoán giữ làm lịch sử; phân bổ năng lực theo phase hiện hành ở [50/01 Lộ trình](../50-lo-trinh/01-lo-trinh.md), quyết định ở [sổ quyết định](../00-tong-quan/03-quyet-dinh.md).

Đầu vào: danh sách 6 nhóm / 18 năng lực MLOps (bạn cung cấp) + tham khảo **Ray Cluster**.
Đối chiếu với: [01 khảo sát](01-ultralytics-platform.md), [02 tổng quát hoá](04-tong-quat-hoa.md), [03 cắm máy](02-ultralytics-on-premise.md).

Ký hiệu: ✅ đã có trong spec · 🟡 có một phần · ❌ chưa có.

## 1. Bảng chẩn đoán

| # | Yêu cầu | Trạng thái | Spec đang có | Thiếu gì | Ray giúp gì |
|---|---|---|---|---|---|
| **1.1** | Kết nối & tiền xử lý dữ liệu (lake, warehouse, streaming) | 🟡 | Upload, URL, S3/GCS/Azure, thư mục on-prem, importer theo format | Connector warehouse (Postgres, BigQuery, Snowflake…), streaming (Kafka), **pipeline làm sạch/biến đổi** chạy trên worker | **Ray Data**: đọc parquet/CSV/SQL/S3, map/filter song song trên nhiều máy |
| **1.2** | Versioning & lineage dữ liệu | 🟡 | Snapshot bất biến (chỉ dữ liệu host trên platform) | Version cho dữ liệu ngoài (S3/on-prem → manifest + hash), **đồ thị lineage** dữ liệu → feature → run → model → deployment → prediction | — |
| **1.3** | Feature Store (offline/online, chống skew) | ❌ | — | Định nghĩa feature, materialize offline/online, point-in-time join, phục vụ online | Ray Data cho job materialize |
| **2.1** | Môi trường lập trình + cấp phát tài nguyên | 🟡 | Worker cắm máy, chọn CPU/GPU | **Workspace JupyterLab / VS Code** chạy trên máy worker, cấp/thu hồi tài nguyên theo yêu cầu, tự tắt khi rảnh | Workspace có thể làm Ray client nối vào cluster |
| **2.2** | Experiment tracking | 🟡 | Metric, hyperparam, console, system, lệnh tái lập, so sánh trong project | **Phiên bản mã nguồn** (git commit/diff), artifact tuỳ ý, tag, **SDK log chung** cho mọi framework, so sánh chéo run, bảng parallel-coordinates | — |
| **2.3** | Hyperparameter tuning & AutoML | ❌ | — | Sweep (grid/random/Bayes/ASHA/PBT), early stopping, ngân sách, AutoML theo task | **Ray Tune** (search algos + schedulers, phân tán trên nhiều máy) |
| **3.1** | Model registry tập trung | 🟡 | Model + weights + metric + liên kết dataset/run | Registry **tách khỏi project**: tên model → nhiều version, đính kèm lineage + code version + signature input/output | — |
| **3.2** | Vòng đời & phê duyệt | ❌ | — | Stage/alias (`staging`, `production`, `archived`), quy trình **review/approval**, gate kiểm thử tự động trước khi promote | — |
| **4.1** | Đóng gói tiêu chuẩn | 🟡 | Export ONNX/TensorRT/… (CV) | **Build image Docker** model + runtime + deps, chữ ký model, export plugin cho mọi framework | — |
| **4.2** | Đa phương thức inference | 🟡 | Endpoint realtime (cloud), predict thử, export edge | REST **+ gRPC**, **batch inference** trên fleet, đóng gói edge (OTA) | **Ray Serve** (realtime), **Ray Data** `map_batches` (batch) |
| **4.3** | Autoscaling & phát hành an toàn | ❌ | — | Autoscale replica theo tải, **canary / blue-green / A/B / shadow**, rollback 1 click | Ray Serve autoscaling replica; định tuyến traffic tự làm ở gateway |
| **5.1** | Giám sát vận hành & chất lượng | 🟡 | Monitoring endpoint (latency, lỗi, log), system stats lúc train | **Log prediction**, thu ground-truth trễ, metric chất lượng theo thời gian, GPU/RAM serving | Ray Dashboard + Prometheus metrics |
| **5.2** | Drift detection | ❌ | — | Data drift (PSI, KS, embedding drift), concept drift (khi có nhãn), ngưỡng + cảnh báo | — |
| **5.3** | Tái huấn luyện tự động | ❌ | (Auto-label bằng model đã train – mới là active learning thủ công) | Trigger (drift, lịch, metric < ngưỡng, dữ liệu mới) → pipeline retrain → eval → gate → promote | — |
| **6.1** | Điều phối pipeline | 🟡 | Agents (DAG suy luận), training job queue | **Pipeline DAG cho ML** (ingest → features → train → eval → register → deploy), lập lịch cron/sự kiện, retry, cache bước | Ray Workflows/Jobs làm runtime cho từng bước |
| **6.2** | Bảo mật & audit | 🟡 | RBAC 4 vai trò, API key mã hoá, token enroll 1 lần | **Audit log bất biến**, mã hoá dữ liệu nghỉ/truyền, quản lý secret, **truy vết prediction → model → run → data** | ⚠️ Ray không có auth mặc định → phải cô lập mạng |
| **6.3** | LLMOps | ❌ | Block LLM trong Agents | Prompt registry có version, eval (hallucination, LLM-as-judge), guardrails, **đếm token/chi phí** (theo dõi, không thu phí), fine-tune LLM (SFT/LoRA) | Ray Train + vLLM trên Ray Serve |
| **—** | Cụm máy cá nhân (yêu cầu riêng của bạn) | 🟡 | Enroll/worker/scheduler (03) | Job **nhiều node**, chịu lỗi, tận dụng nhiều GPU | **Ray cluster** là lớp thực thi |

**Tổng:** 0 ✅ · 11 🟡 · 7 ❌ (Feature store, HPO/AutoML, Lifecycle & approval, Safe rollout, Drift, Retraining, LLMOps).

## 2. Ray Cluster — vai trò trong MLPlatform

Ray cung cấp sẵn lớp **thực thi phân tán** mà spec đang thiếu:

| Thành phần Ray | Lấp chỗ trống |
|---|---|
| Ray Core (head + worker nodes, resource `num_cpus/num_gpus` + custom resources) | Scheduler cấp task/actor trong cụm máy |
| Ray Jobs API | Gửi job (entrypoint + runtime_env) từ control plane |
| Ray Train | Train phân tán (PyTorch DDP, HF, XGBoost, LightGBM…) + checkpoint + chịu lỗi worker |
| Ray Tune | HPO / AutoML (2.3) |
| Ray Data | Tiền xử lý (1.1), materialize feature (1.3), batch inference (4.2) |
| Ray Serve | Serving realtime, autoscale replica, multiplexing model, LLM (4.2, 4.3, 6.3) |
| Dashboard + metrics Prometheus | Giám sát cụm (5.1) |

**Ràng buộc quan trọng khi dùng Ray với PC cá nhân:**
1. **Mạng**: worker phải kết nối được tới head (GCS) và các node phải gọi được nhau qua nhiều cổng → không hợp với máy sau NAT. Mô hình "outbound-only" của Ultralytics không áp dụng thẳng được.
   → Giải pháp: agent dựng **mạng overlay WireGuard** (tự host, kiểu Headscale/Tailscale/Netbird) khi enroll; Ray chỉ lắng nghe trên IP overlay.
2. **Bảo mật**: Ray Dashboard/Jobs API **không có xác thực mặc định** (đã có lỗ hổng bị khai thác thực tế). → Không bao giờ mở ra Internet; chỉ control plane gọi Jobs API qua overlay; tách cluster theo workspace.
3. **Đồng nhất phiên bản**: mọi node cùng phiên bản Ray + Python → agent chạy **image chuẩn** do platform quản lý, nâng cấp cuốn chiếu.
4. **macOS/Windows**: Ray trên macOS hỗ trợ ở mức thử nghiệm cho cluster nhiều node; Windows chạy qua Docker/WSL 2. → Node chính nên là Linux; Mac tham gia như node CPU/MPS cho job một máy.
5. **Head node**: đã chốt đặt **trong control plane**, khai báo 0 CPU/0 GPU nên chỉ điều phối, không train (xem [06](../20-kien-truc/02-backend-dotnet-aot.md)). Head là điểm chết của cluster → bật GCS fault tolerance (Redis ngoài) khi cần.

## 3. Quyết định đề xuất

1. **Hai tầng**: *Control plane* (portal: metadata, UI, registry, lineage, quyền, pipeline) + *Execution plane* (Ray cluster gồm các PC đã join).
2. **Agent MLPlatform** trên mỗi máy vẫn giữ (theo mẫu 03): enroll, overlay network, chạy container Ray node, báo phần cứng, tự cập nhật, drain/disconnect. Ray **không** thay agent; agent quản lý vòng đời Ray node.
3. Mọi job (train, tune, batch, data pipeline, workspace) được control plane gửi qua **Ray Jobs API**; serving qua **Ray Serve** sau một **gateway** của MLPlatform (auth, quota, định tuyến canary/A-B, log prediction).
4. Thêm 7 module còn thiếu vào spec tổng thể → [05 — Spec module tổng thể](../README.md).
5. Không làm: thu phí. Theo dõi token LLM chỉ để quan sát, không tính tiền.
