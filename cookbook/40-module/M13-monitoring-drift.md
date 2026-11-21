# M13 — Monitoring, Drift & Continuous Training

> **Phase:** P2 · **Phụ thuộc:** M11, M12, M14 · **Proto:** `monitoring.proto`  (`MonitoringService`) · **Schema DB:** `monitor`
> **Trạng thái spec:** lát cắt đầu (prediction log, nhãn thật đến muộn, baseline bảng, drift PSI/KS, chất lượng thật, cảnh báo) đã hiện thực và mô tả ở mục 2–10; drift embedding/ảnh/văn bản, concept drift có kiểm định, continuous training (cần M14) và đẩy item sang M05 viết tiếp khi làm.

## 1. Mục tiêu & phạm vi
Theo dõi chất lượng model đang phục vụ, phát hiện trôi dạt và tự kích hoạt huấn luyện lại.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Prediction log đầy đủ + nối ground truth đến muộn |  | ✔ |  |
| Metric chất lượng thật theo thời gian |  | ✔ |  |
| Data drift (PSI, KS, chi-square, khoảng cách embedding) so với baseline dataset train |  | ✔ |  |
| Concept drift khi có nhãn |  | ✔ |  |
| Ngưỡng + cảnh báo (M16) |  | ✔ |  |
| Continuous training: trigger → pipeline retrain → eval → approval → canary |  | ✔ |  |
| Item drift/độ tin cậy thấp đẩy vào hàng đợi gán nhãn M05 |  | ✔ |  |

## 2. Khái niệm
- **Prediction log**: gateway (M12) ghi mỗi dự đoán — đặc trưng tóm tắt (bảng: giá trị cột; văn bản/ảnh: chỉ độ dài/kích thước, không lưu nội dung) + kết quả tóm tắt (`label`+`score` hoặc `value`) + độ trễ. Id trả qua header `X-Mlp-Prediction-Ids`.
- **Nhãn thật đến muộn**: client gửi `POST /v1/deployments/{ws}/{slug}/feedback` cùng khoá endpoint, nối vào dự đoán theo id.
- **Baseline**: thống kê của dataset version dùng để train (P2 lát đầu: CSV dạng bảng ≤ 50 MB) — cột số: 10 bin theo decile + mẫu ≤ 5 000 giá trị + trung bình; cột chữ: tần suất top 50; cột nhãn: phân bố nhãn.
- **Drift**: PSI theo bin baseline (số) hoặc theo tần suất (chữ, có nhóm "khác"); KS hai mẫu cho cột số; drift phân bố dự đoán = PSI giữa nhãn dự đoán và phân bố nhãn baseline. Mức: `ok` < `psi_warn` (0.1) ≤ `warn` < `psi_alert` (0.25) ≤ `alert`.
- **Chất lượng thật**: trên các dự đoán đã có nhãn — accuracy (phân lớp), MAE (hồi quy); theo ảnh chụp và theo ngày.
- **Ảnh chụp (snapshot)**: kết quả một lần tính trên `window_size` dự đoán gần nhất.

## 3. Dữ liệu (schema `monitor`)
| Bảng | Cột chính |
|---|---|
| `predictions` | id, deployment_id, model_version, at, latency_ms, features jsonb, prediction jsonb, ground_truth jsonb, gt_at |
| `configs` | deployment_id, workspace_id, dataset_version_id, label_column, baseline jsonb, baseline_rows, psi_warn, psi_alert, window_size, min_samples, enabled |
| `snapshots` | id, deployment_id, at, samples, status, drift jsonb, quality jsonb |

## 4. Trạng thái & quy tắc
- **R1** Vòng giám sát trong control plane mỗi `Monitoring:IntervalSeconds` (mặc định 300 s) tính lại cho deployment đang phục vụ có baseline và có ≥ `min_samples` dự đoán mới kể từ ảnh chụp trước.
- **R2** Chuyển sang `alert` (lần trước không phải alert) → thông báo M16 `deployment.drift` cho người tạo deployment + Owner/Admin, nêu feature có PSI lớn nhất.
- **R3** Prediction log không lưu nội dung văn bản/ảnh (20/06); chỉ đặc trưng tóm tắt.
- **R4** Feedback chỉ nối vào dự đoán của đúng deployment mà khoá endpoint thuộc về; deployment đã dừng vẫn nhận feedback.

## 5. Luồng chính
1. Người dùng mở tab Giám sát của deployment → chọn dataset version train làm baseline + ngưỡng → `ConfigureMonitor`.
2. Client gọi `/predict` như thường → gateway ghi log; sau đó gửi nhãn thật qua `/feedback`.
3. Vòng giám sát (hoặc nút "Tính drift ngay") tạo ảnh chụp → bảng drift theo feature, biểu đồ PSI theo thời gian, accuracy thật theo ngày → cảnh báo khi báo động.

## 6. Hợp đồng (`monitoring.proto` — `MonitoringService`; REST cho client suy luận)
| RPC / HTTP | Quyền | Mô tả |
|---|---|---|
| `GetMonitor` / `ListSnapshots` / `GetQuality` / `ListPredictions` | `run.read` | |
| `ConfigureMonitor` | `deployment.manage` | Đặt baseline + ngưỡng |
| `ComputeDrift` | `run.read` | Tính ngay trên `last_n` dự đoán |
| `POST …/predict` | khoá `mlpd_` | Trả thêm header `X-Mlp-Prediction-Ids` |
| `POST …/feedback` | khoá `mlpd_` | `{"feedback":[{"prediction_id","label"|"value"}]}` → `{"accepted":n}` |

## 7. Màn hình (phác thảo)
Tab Giám sát của deployment: biểu đồ chất lượng, drift theo feature/embedding, cảnh báo; cấu hình baseline, ngưỡng, trigger retrain.

## 8. Sự kiện
`deployment.drift` → M16. (P2 tiếp: `RetrainTriggered` → M14.)

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `MONITOR_NO_BASELINE` | FAILED_PRECONDITION | Chưa đặt baseline cho deployment |
| `MONITOR_NOT_ENOUGH_SAMPLES` | FAILED_PRECONDITION | Chưa đủ dự đoán để tính |
| `MONITOR_BASELINE_UNSUPPORTED` | FAILED_PRECONDITION | Dataset version không có CSV dạng bảng |
| `MONITOR_BASELINE_TOO_LARGE` | RESOURCE_EXHAUSTED | Baseline CSV > 50 MB |

## 10. Nghiệm thu
- [x] Dữ liệu cùng phân bố với baseline → mọi feature số `ok` (PSI < 0.1) (kịch bản M12+M13).
- [x] Dữ liệu lệch (trung bình ×12) → `alert`, KS ≈ 1, thông báo `deployment.drift`.
- [x] 40 nhãn thật gửi muộn → accuracy thật đúng (0.5) trong ảnh chụp và chuỗi theo ngày.
- [ ] Drift embedding cho ảnh/văn bản.
- [ ] Trigger retrain qua pipeline M14 → eval → duyệt → canary.
- [ ] Dự đoán độ tin cậy thấp / drift đẩy vào hàng gán nhãn M05.
