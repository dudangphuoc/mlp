# 20/07 — Quy ước chung

## 1. Định danh
| Loại | Quy ước | Ví dụ |
|---|---|---|
| ID | UUIDv7 | `0193a1c2-…` |
| ID ngắn hiển thị | 8 ký tự đầu, font mono | `0193a1c2` |
| Slug | `^[a-z0-9]([a-z0-9-]{0,62}[a-z0-9])?$`, duy nhất trong phạm vi cha | `vision-team`, `pcb-defects` |
| Tên tài nguyên (RPC) | `workspaces/{ws}/datasets/{ds}` | |
| URI hiển thị / SDK | `mlp://{ws}/{kind}/{slug}[@v{n}|@{alias}]` | `mlp://vision/datasets/pcb@v3`, `mlp://vision/models/pcb-detector@production` |
| Version | số nguyên tăng dần trong phạm vi tài nguyên, bất biến | `v3` |
| Token | tiền tố + 40 hex: `mlp_`, `mlpa_`, `mlpj_`, `mlpe_`, `mlpd_` | |

## 2. Thời gian
UTC ở mọi nơi; nguồn "bây giờ" duy nhất là `IClock` (cấm `DateTime.UtcNow`); proto dùng `google.protobuf.Timestamp`; UI hiển thị theo múi giờ người xem.

## 3. Mã lỗi
`ErrorInfo.reason` dạng `<MODULE>_<REASON>` (`FLEET_HOST_OFFLINE`); tiền tố module: `IAM`, `FLEET`, `SCHED`, `DATA`, `LABEL`, `LINEAGE`, `AUDIT`, `NB`, `TRACK`, `REGISTRY`, `SERVE`, `NOTIFY`, `CATALOG`, `RUNTIME` (lỗi shim/recipe), chung: `CONCURRENCY_CONFLICT`, `VALIDATION_FAILED`, `RATE_LIMITED`. Bảng mã nằm ở mục 9 của từng module.

## 4. Sự kiện
- Tên: `{module}.{Entity}{PastTense}` (`data.DatasetVersionCreated`), định nghĩa trong `events.proto`.
- Ghi outbox trong cùng transaction; handler phải **idempotent** (dựa vào `event_id`).
- Sự kiện mang ID và thông tin tối thiểu; người nghe gọi lại module nguồn nếu cần chi tiết.

## 5. Thay đổi và xoá
- Sửa có `etag`; xoá mềm vào Thùng rác 30 ngày; xoá cứng dọn cả object kho S3.
- Tài nguyên có version: version bất biến.

## 6. Phi chức năng (P1)
| Hạng mục | Mục tiêu |
|---|---|
| Quy mô | 1 control plane · ≤ 50 máy · ≤ 100 người dùng · ≤ 10 triệu item/dataset (metadata) |
| Độ trễ RPC đọc | p95 < 200 ms |
| Realtime (metric, log, trạng thái) | < 2 s tới UI |
| Khả dụng | Control plane dừng → job đang chạy vẫn chạy; khôi phục từ backup ≤ 1 giờ |
| Tài nguyên control plane | 8 vCPU, 16 GB RAM, SSD 200 GB + dung lượng kho S3 |
| Quan sát | OpenTelemetry trace xuyên api → Ray job (`TRACEPARENT` trong env job) |

## 7. Tài liệu trong mã
Như Cortext: mỗi thư mục mã có `index.md`; phần "Thông tin build" do DocGen sinh; đổi mã làm mô tả sai thì sửa `index.md` trong cùng commit. Mã viết tiếng Anh; tài liệu `cookbook/` tiếng Việt.
