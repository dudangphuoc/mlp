# 00/01 — Toàn cảnh MLPlatform

> Đọc trang này trước. Mọi chi tiết nằm ở tài liệu được dẫn link; lý do của từng quyết định nằm ở [sổ quyết định](03-quyet-dinh.md).

## 1. MLPlatform là gì
Portal **điều phối ML tự host**: quản lý dữ liệu, gán nhãn, notebook, huấn luyện, registry, suy luận cho **mọi loại dữ liệu và mọi họ model** (RT-DETRv4, DEIMv2, D-FINE, RF-DETR, YOLO, HF Transformers, Whisper, VibeVoice, repo tự viết…) — mỗi họ model là một **recipe** chạy trong image riêng (D-20, D-21). Portal **không tự tính toán**: việc nặng chạy trên các **PC của bạn được cắm vào bằng một lệnh**, gom thành một Ray cluster (D-01, D-04). Không thu phí (D-02).

## 2. Sơ đồ
```
 Người dùng (web / SDK / notebook)          CONTROL PLANE — 1 server                     EXECUTION PLANE — các PC
 ───────────────────────────────           ─────────────────────────────                ──────────────────────────
 Owner/Admin · Data Owner · Lead   gRPC-Web  api (.NET 10 AOT, gRPC theo module)   kênh agent ◄─ Agent (service)
 Annotator · Reviewer · Engineer  ────────►  web (React)                           data channel◄─  Ray worker mỏng + container recipe
 Client ngoài ── REST/gRPC suy luận ───────►  PostgreSQL+Timescale · kho S3 · registry             Notebook (container)
                                             Ray HEAD (0 CPU/0 GPU — chỉ điều phối) ◄═ overlay ═► /datasets (chỉ-đọc), /models
                                             WireGuard hub                          (chỉ cho Ray)
```
Chi tiết: [20/01 Kiến trúc hệ thống](../20-kien-truc/01-he-thong.md).

## 3. Một vòng đời ML

| # | Việc | Ai | Module | Chạy ở |
|---|---|---|---|---|
| 1 | Cắm PC bằng 1 lệnh | Admin | [M02](../40-module/M02-fleet.md) | PC |
| 2 | Tạo dataset: upload / S3 / thư mục trên PC | Data Owner | [M04](../40-module/M04-data.md) | Job ingest trên PC |
| 3 | Định nghĩa schema nhãn, mở labeling project, phân công | Data Owner, Lead | [M05](../40-module/M05-labeling.md) | Control plane |
| 4 | Gán nhãn có SAM / pre-label / active learning, review | Annotator, Reviewer | M05 | Trình duyệt + PC có GPU |
| 5 | Chốt label set → dataset version | Data Owner | M05, M04 | Control plane |
| 6 | Thử nghiệm trong Jupyter dựng sẵn | ML Engineer | [M08](../40-module/M08-notebook.md) | Container trên PC |
| 6b | Chọn họ model trong Catalog (hoặc nhập repo mới làm recipe) | ML Engineer | [M17](../40-module/M17-model-catalog.md) | Control plane + build image trên PC |
| 7 | Train bằng recipe đã chọn trên fleet, theo dõi realtime, máy chết thì chạy tiếp | ML Engineer | [M03](../40-module/M03-scheduler.md), [M09](../40-module/M09-tracking.md) | Container recipe trên PC |
| 8 | Đăng ký model, export, promote production | ML Engineer, Admin | [M11](../40-module/M11-registry.md) | Control plane + PC |
| 9 | Deploy endpoint, đổi version không downtime, rollback | ML Engineer | [M12](../40-module/M12-serving.md) | Container serve của recipe trên PC |
| 10 | Truy vết deployment → dữ liệu → người gán; audit | Admin | [M06](../40-module/M06-lineage-audit.md) | Control plane |
| P2 | Drift → tự train lại → canary | — | M13, M14 | — |

## 4. Bản đồ tài liệu

| Thư mục | Nội dung | Tính chất |
|---|---|---|
| [00-tong-quan](.) | Toàn cảnh, [thuật ngữ](02-thuat-ngu.md), [quyết định](03-quyet-dinh.md) | Chuẩn |
| [10-khao-sat](../10-khao-sat/) | Khảo sát Ultralytics (chức năng, on-prem, UI bằng REA), đối chiếu MLOps | **Tham khảo**, không phải spec |
| [20-kien-truc](../20-kien-truc/) | Hệ thống, .NET AOT, proto, agent, dữ liệu, bảo mật, quy ước, recipe & runtime (mọi họ model), vận hành | Chuẩn |
| [30-ui](../30-ui/) | Hệ thống thiết kế, sơ đồ màn hình | Chuẩn |
| [40-module](../40-module/) | 17 module, mỗi module một file, template 10 mục | Chuẩn |
| [50-lo-trinh](../50-lo-trinh/) | Lộ trình P0–P3, spec P0 | Chuẩn |

## 5. Rủi ro chính

| Rủi ro | Giảm thiểu |
|---|---|
| Control plane là điểm chết (kể cả Ray head) | Job đang chạy tiếp tục khi api dừng; sao lưu/khôi phục ≤ 1 giờ ([20/09](../20-kien-truc/09-van-hanh.md)); HA ở P3 |
| PC sau NAT, Ray cần kết nối hai chiều | WireGuard overlay do agent dựng; mọi thứ khác đi qua kênh agent ra ngoài |
| Windows/macOS chạy Ray trong VM Docker | Agent đo giới hạn VM, khai tài nguyên tường minh, doctor cảnh báo; Mac chỉ CPU |
| Thư viện không chạy được Native AOT | Spike AOT ở P0 trước khi viết module, mỗi mục có dự phòng |
| Lệch phiên bản Ray/Python giữa máy | Image `mlp-node` (Ray worker mỏng) do platform quản lý; phụ thuộc của model nằm trong image recipe riêng |
| Repo model khác nhau, hay hỏng khi build | Recipe ghim commit, build có smoke test, chỉ recipe verified mới train/serve |
| License model (AGPL, research-only) | Hiển thị và bắt xác nhận lần đầu, ghi vào model version (D-23) |
| Ray không có xác thực | Cổng Ray chỉ trên overlay/localhost |
| Đầy đĩa PC/kho S3 | Quota + dọn rác hằng đêm + cảnh báo |
