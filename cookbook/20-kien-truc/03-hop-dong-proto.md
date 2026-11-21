# 20/03 — Hợp đồng `.proto` và quy ước API

Quyết định nền: D-10, D-13, D-26. Mẫu tham khảo: blogic-tunnels (`src/proto/tunnels/v1`, luật "protobuf only").

## 1. Luật
- **Mọi message giữa các thành phần của MLPlatform là Protocol Buffers.** Không JSON trên dây, không "JSON tạm", không chuỗi JSON nhét trong field.
- Ngoại lệ được phép JSON: API của bên thứ ba (Ray Jobs/Serve/State, S3/kho S3, OAuth Google/GitHub), **endpoint suy luận REST công khai**, file cấu hình cục bộ và manifest `mlp-recipe.yaml`, cột `jsonb` lưu tham số tự do trong DB.
- `.proto` là **hợp đồng duy nhất**; không viết tay DTO cho message trên dây.
- Tiến hoá: chỉ thêm field number mới; không tái dùng, không đánh số lại; field bỏ đi khai `reserved`.

## 2. Bố cục

```
proto/mlplatform/v1/
  common.proto     PageRequest/Response, ResourceRef, Etag, Progress, ErrorReason
  iam.proto        IamService
  fleet.proto      FleetService (UI/Admin)
  agent.proto      kênh agent + data channel (20/04)
  scheduler.proto  SchedulerService
  data.proto       DataService
  labeling.proto   LabelingService
  lineage.proto    LineageService
  audit.proto      AuditService
  notebook.proto   NotebookService
  tracking.proto   TrackingService
  registry.proto   RegistryService
  serving.proto    ServingService
  inference.proto  InferenceService (công khai)
  runtime.proto    RuntimeService (shim/holder → control plane: heartbeat, tiến độ, checkpoint, ReportAllocation, ReportExit; metric đi TrackingService, log đi kênh agent)
  notify.proto     NotificationService
  catalog.proto    CatalogService (recipe, biến thể, task, Model Zoo — M17)
  model_server.proto ModelServer (container serve của recipe ↔ gateway)
  events.proto     domain event lưu outbox
  # P2/P3: tuning.proto (M10), monitoring.proto (M13), pipeline.proto (M14), feature.proto (M07), llmops.proto (M15)
```
`iam.proto` chứa `IamService` (người dùng, phiên), `WorkspaceService` (workspace, thành viên, key, tích hợp, tổng quan, dung lượng, thùng rác) và `InstanceService` (cài đặt lần đầu, cấu hình instance, người dùng, email chờ copy — D-27).
Package `mlplatform.v1`, `option csharp_namespace = "MLPlatform.Contracts.V1"`.

| Đích | Công cụ | Nơi đặt |
|---|---|---|
| C# | `Grpc.Tools` lúc build | `src/Framework/MLPlatform.Contracts` |
| TypeScript | `buf generate` + `protoc-gen-es` (Connect) | `web/packages/proto` (commit mã sinh) |
| Python | `grpcio-tools` + `mypy-protobuf` | `python/mlplatform-sdk/mlplatform/_proto` |

CI: `buf lint` (STANDARD) + `buf breaking` so với nhánh chính.

## 3. Kênh

| Kênh | Transport |
|---|---|
| Trình duyệt ↔ api | gRPC-Web (Connect-ES): unary + server streaming |
| SDK / CLI / notebook ↔ api | gRPC HTTP/2 |
| Job runtime ↔ api | gRPC `RuntimeService`, token `mlpj_` trong metadata |
| Gateway ↔ container serve | gRPC `ModelServer` qua overlay (D-26) |
| Agent ↔ api | TCP+TLS dài hạn, protobuf varint-delimited, ghép luồng (20/04) |
| Data channel | TCP+TLS ngắn hạn: 1 frame hello → byte thô |
| Module ↔ module (cùng tiến trình) | Domain event = message trong `events.proto`, lưu outbox dạng `bytea` |
| Client ngoài ↔ suy luận | REST JSON **và** gRPC `InferenceService` |
| Byte lớn | Presigned URL kho S3 (không phải message) |

## 4. Quy ước thiết kế RPC (theo Google AIP)

| Chủ đề | Quy ước |
|---|---|
| Tên tài nguyên | `workspaces/{ws}/hosts/{host}`, `workspaces/{ws}/datasets/{ds}/versions/{n}` |
| Thao tác chuẩn | `GetX`, `ListX`, `CreateX`, `UpdateX` (`update_mask`), `DeleteX` |
| Thao tác riêng | Động từ + danh từ: `DrainHost`, `SubmitItem`, `PromoteModelVersion` |
| Phân trang | `page_size` (≤ 200), `page_token` → `next_page_token`; `filter` (cú pháp AIP-160), `order_by` |
| Concurrency | Field `etag` trên tài nguyên sửa được; sai → `ABORTED` |
| Idempotency | Field `request_id` (UUID) trên RPC tạo/khởi chạy; giữ 24 giờ |
| Việc dài | Trả tài nguyên có trạng thái (`Job`, `Import`, `Export`) và theo dõi bằng `Watch*` |
| Thời gian | `google.protobuf.Timestamp`, UTC |
| Tiền tố ID | ID là UUIDv7 dạng chuỗi; URI hiển thị `mlp://{ws}/{kind}/{slug}[@v{n}]` |

### 4.1 Lỗi
gRPC status + chi tiết:
- `google.rpc.ErrorInfo{ domain: "mlplatform", reason: "<MODULE>_<REASON>", metadata }` — `reason` là mã lỗi ghi trong spec module.
- `google.rpc.BadRequest` cho lỗi field.
- Ánh xạ: không tìm thấy hoặc không có quyền đọc → `NOT_FOUND` (không lộ tồn tại); đọc được nhưng không được ghi → `PERMISSION_DENIED` kèm lý do; xung đột trạng thái → `FAILED_PRECONDITION`; sai `etag` → `ABORTED`; quota → `RESOURCE_EXHAUSTED`.
- Nhiều vi phạm trong một request → trả tất cả (như Cortext).

### 4.2 Stream
- `Watch*` server streaming (ví dụ `SchedulerService.WatchJob`, `FleetService.WatchHosts`, `LabelingService.WatchProject`, `NotificationService.WatchNotifications`).
- Stream mở bằng snapshot rồi gửi sự kiện thay đổi; gửi message keepalive rỗng sau 20 s im lặng; client mở lại stream im 45 s và gửi `resume_token` để nhận tiếp (buffer 1 000 sự kiện/kênh).
- Quyền kiểm khi mở stream và khi quyền thay đổi (đóng stream với `PERMISSION_DENIED`).

## 5. Endpoint REST còn lại (ngoại lệ)
| Đường dẫn | Mục đích |
|---|---|
| `POST /v1/deployments/{ws}/{slug}/predict`, `GET …/{slug}`, `GET …/{slug}/health` | Suy luận cho client ngoài (M12) |
| `/health/live`, `/health/ready` | Health |
| `/oauth/{provider}/callback` | OAuth đăng nhập |
| `/install/agent.sh`, `/install/agent.ps1`, `/install/agent/{rid}` | Tải script và binary agent (M02) |
