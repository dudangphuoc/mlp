# 30/02 — Sơ đồ màn hình và điều hướng

Mỗi màn hình có mã `S-<module>-<số>`; đặc tả chi tiết nằm ở mục **7** của module tương ứng. Route có tiền tố workspace `/w/{ws}`.

## 1. Danh sách màn hình

| Mã | Route | Màn hình | Module | Ai thấy | Phase |
|---|---|---|---|---|---|
| S-01-1 | `/login`, `/register`, `/forgot` | Đăng nhập / đăng ký / quên mật khẩu | [M01](../40-module/M01-iam.md) | Khách | P1 |
| S-01-2 | `/w/{ws}/settings?tab=profile` | Hồ sơ cá nhân | M01 | Mọi người | P1 |
| S-01-3 | `/w/{ws}/settings?tab=members` | Thành viên & vai trò | M01 | Owner, Admin | P1 |
| S-01-4 | `/w/{ws}/settings?tab=api-keys` | API key | M01 | Mọi người (key của mình) | P1 |
| S-01-5 | `/w/{ws}/settings?tab=workspace` | Cài đặt workspace | M01 | Owner | P1 |
| S-01-6 | menu `[Workspace ▾]`, `/workspaces/new` | Chuyển / tạo workspace | M01 | Mọi người | P1 |
| S-01-7 | `/invite/{token}` | Nhận lời mời | M01 | Người được mời | P1 |
| S-01-8 | `/setup?token=…` | Cài đặt lần đầu (Instance Admin) | M01 | Khách có token bootstrap | P1 |
| S-01-9 | `/admin?tab=settings\|users\|emails\|zoo` | Quản trị instance | M01, M17 | Instance Admin | P1 |
| S-01-10 | `/w/{ws}/settings?tab=integrations` | Tích hợp (token HF, S3) | M01 | Owner, Admin (xem: thành viên) | P1 |
| S-00-2 | (panel bên) | Trợ giúp: phím tắt, tài liệu, phiên bản | [30/01](01-he-thong-thiet-ke.md) | Mọi người | P1 |
| S-00-1 | `/w/{ws}` | **Tổng quan** (dashboard) | nhiều module | Mọi người (khối theo quyền) | P1 |
| S-00-3 | (khay trên thanh trên) | **Đang chạy**: mọi tiến trình của tôi, bước, %, ETA, lỗi/treo | [20/10 §3](../20-kien-truc/10-quan-sat-va-khoi-phuc.md) | Mọi người | P1 |
| S-02-1 | `/w/{ws}/fleet` | Danh sách máy | [M02](../40-module/M02-fleet.md) | Editor+, Viewer (xem) | P1 |
| S-02-2 | `/w/{ws}/fleet/new` | Cắm máy mới | M02 | Owner, Admin | P1 |
| S-02-3 | `/w/{ws}/fleet/{host}` | Chi tiết máy | M02 | Editor+, Viewer | P1 |
| S-02-4 | `/w/{ws}/fleet?tab=pools` | Pool | M02 | Owner, Admin | P1 |
| S-02-5 | `/w/{ws}/fleet?tab=map` | Bản đồ cụm: GPU nào đang bị việc nào giữ | M02, [20/10 §3](../20-kien-truc/10-quan-sat-va-khoi-phuc.md) | Editor+, Viewer | P1 |
| S-03-1 | `/w/{ws}/jobs` | Danh sách job + hàng đợi | [M03](../40-module/M03-scheduler.md) | Editor+ | P1 |
| S-03-2 | `/w/{ws}/jobs/{job}` | Chi tiết job | M03 | Editor+ | P1 |
| S-03-3 | `/w/{ws}/jobs?tab=quotas` | Quota tài nguyên | M03 | Admin (xem: Editor) | P1 |
| S-04-1 | `/w/{ws}/datasets` | Danh sách dataset | [M04](../40-module/M04-data.md) | Editor+, Viewer | P1 |
| S-04-2 | `/w/{ws}/datasets/new` (hộp thoại) | Tạo dataset | M04 | Editor+ | P1 |
| S-04-3 | `/w/{ws}/datasets/{ds}` (tab Nội dung) | Dataset — **trình duyệt nội dung**: thư mục, tìm, lọc loại file, lưới ảnh kèm nhãn (YOLO + nhãn nền tảng), xem trước ảnh/văn bản/JSON/CSV/âm thanh/video | M04 | Editor+, Viewer | P1 |
| S-04-4 | `…?tab=classes` | Dataset — Classes | M04/M05 | Editor+, Viewer | P1 |
| S-04-5 | `…?tab=charts` | Dataset — Charts (EDA) | M04 | Editor+, Viewer | P1 |
| S-04-6 | `…?tab=versions` | Dataset — Versions | M04 | Editor+, Viewer | P1 |
| S-04-7 | `…?tab=imports` | Dataset — Imports | M04 | Editor+ | P1 |
| S-04-8 | `…?tab=models` | Dataset — Models dùng dataset | M04 | Editor+, Viewer | P1 |
| S-04-9 | `…?tab=columns` | Dataset — Cột (dataset bảng) | M04 | Editor+, Viewer | P1 |
| S-05-1 | `…?tab=labeling` | Dataset — Labeling projects | [M05](../40-module/M05-labeling.md) | Data Owner, Lead | P1 |
| S-05-2 | `/w/{ws}/labeling/{project}` | Bảng điều khiển của Lead | M05 | Lead, Data Owner | P1 |
| S-05-3 | `/w/{ws}/labeling` | **Việc của tôi** (Annotator/Reviewer) | M05 | Annotator, Reviewer | P1 |
| S-05-4 | `/w/{ws}/labeling/{project}/annotate/{item}` | **Trình gán nhãn** (toàn màn hình) | M05 | Annotator, Editor+ | P1 |
| S-05-4b | (cùng route, dataset văn bản) | Trình gán nhãn — văn bản | M05 | Annotator, Editor+ | P1 |
| S-05-4c | (cùng route, dataset bảng) | Trình gán nhãn — bảng | M05 | Annotator, Editor+ | P1 |
| S-05-4d | (cùng route, dataset âm thanh) | Trình gán nhãn — âm thanh | M05 | Annotator, Editor+ | P1 |
| S-05-5 | `/w/{ws}/labeling/{project}/review/{item}` | Trình review | M05 | Reviewer | P1 |
| S-05-6 | `/w/{ws}/datasets/{ds}?tab=labeling&sub=schema` | Label schema | M05 | Data Owner, Lead (đề xuất) | P1 |
| S-06-1 | `/w/{ws}/settings?tab=audit` | Nhật ký audit | [M06](../40-module/M06-lineage-audit.md) | Owner, Admin | P1 |
| S-06-2 | (thành phần) | Đồ thị lineage | M06 | theo tài nguyên | P1 |
| S-06-3 | `…?tab=activity` (trong trang tài nguyên) | Tab Hoạt động | M06 | Người đọc được tài nguyên | P1 |
| S-17-1 | `/w/{ws}/catalog` | **Model Catalog** (recipe theo task) | [M17](../40-module/M17-model-catalog.md) | Mọi thành viên (xem) | P1 |
| S-17-2 | `/w/{ws}/catalog/{recipe}` | Recipe — biến thể, môi trường, lệnh, build | M17 | Mọi thành viên; sửa: Editor+ | P1 |
| S-17-3 | `/w/{ws}/catalog/new` | Thêm recipe (trình hướng dẫn) | M17 | Editor+ | P1 |
| S-17-4 | `/w/{ws}/catalog?tab=tasks` | Danh mục task | M17 | Mọi thành viên | P1 |
| S-17-5 | `/w/{ws}/catalog?tab=zoo` | Model Zoo | M17 | Mọi thành viên (xem); Admin tải lên | P1 |
| S-08-1 | `/w/{ws}/notebooks` | Danh sách notebook | [M08](../40-module/M08-notebook.md) | Editor+ | P1 |
| S-08-2 | `/w/{ws}/notebooks/new` (hộp thoại) | Tạo notebook | M08 | Editor+ | P1 |
| S-08-3 | `/w/{ws}/notebooks/{nb}` | Khởi động + IDE (iframe) | M08 | Chủ notebook | P1 |
| S-08-4 | `/w/{ws}/notebooks?tab=environments` | Environments | M08 | Editor+ | P1 |
| S-08-5 | `/w/{ws}/notebooks?tab=runs` | Lần chạy notebook (job) | M08 | Editor+ | P1 |
| S-09-1 | `/w/{ws}/projects` | Danh sách project | [M09](../40-module/M09-tracking.md) | Editor+, Viewer | P1 |
| S-09-2 | `/w/{ws}/projects/{p}` | Project — bảng run + biểu đồ so sánh | M09 | Editor+, Viewer | P1 |
| S-09-3 | `/w/{ws}/projects/{p}/runs/{run}` | Chi tiết run | M09 | Editor+, Viewer | P1 |
| S-09-4 | (hộp thoại) | Train mới | M09 | Editor+ | P1 |
| S-09-5 | `/w/{ws}/projects/{p}/compare?runs=…` | So sánh run | M09 | Editor+, Viewer | P1 |
| S-11-1 | `/w/{ws}/models` | Danh sách model | [M11](../40-module/M11-registry.md) | Editor+, Viewer | P1 |
| S-11-2 | `/w/{ws}/models/{m}` | Model — các version | M11 | Editor+, Viewer | P1 |
| S-11-3 | `/w/{ws}/models/{m}/versions/{v}` | Model version | M11 | Editor+, Viewer | P1 |
| S-11-4 | (hộp thoại) | Import model | M11 | Editor+ | P1 |
| S-11-5 | (sheet bên phải) | **Thử model**: tải ảnh/văn bản/âm thanh/dòng bảng → kết quả vẽ theo task | M11, M12 | Editor+ | P1 |
| S-11-6 | (sheet bên phải) | **Đánh giá model** trên dataset version: metric, theo lớp, ngưỡng tốt nhất, ảnh sai | M11 | Editor+ (xem: Viewer) | P1 |
| S-12-1 | `/w/{ws}/deployments` | Danh sách deployment | [M12](../40-module/M12-serving.md) | Editor+, Viewer | P1 |
| S-12-2 | (hộp thoại) | Deploy model version | M12 | Editor+ | P1 |
| S-12-3 | `/w/{ws}/deployments/{d}` | Chi tiết deployment + playground | M12 | Editor+, Viewer | P1 |
| S-16-1 | (panel thả xuống) | Trung tâm thông báo | [M16](../40-module/M16-notifications.md) | Mọi người | P1 |
| S-16-2 | `/w/{ws}/settings?tab=notifications` | Tuỳ chọn thông báo | M16 | Mọi người | P1 |
| S-OPS-1 | `/w/{ws}/settings?tab=storage` | Dung lượng & quota | [20/09](../20-kien-truc/09-van-hanh.md) | Owner, Admin | P1 |
| S-OPS-2 | `/w/{ws}/settings?tab=trash` | Thùng rác | 20/09 | Editor+ | P1 |
| S-OPS-3 | `/w/{ws}/status` | Trạng thái hệ thống: thành phần, vòng nền, sự cố | [20/10 §2.6](../20-kien-truc/10-quan-sat-va-khoi-phuc.md) | Mọi người (chi tiết: Admin) | P1 |
| S-10-x | `/w/{ws}/sweeps…` | HPO / sweep | M10 | — | P2 |
| S-13-x | `/w/{ws}/deployments/{d}?tab=monitoring` | Giám sát chất lượng, drift | M13 | — | P2 |
| S-14-x | `/w/{ws}/pipelines…` | Pipeline + Agents canvas | M14 | — | P2 |
| S-07-x, S-15-x | — | Feature store, LLMOps | M07, M15 | — | P3 |

## 2. Điều hướng theo vai trò (P1)

| Vai trò | Sidebar thấy | Trang đích sau đăng nhập |
|---|---|---|
| Instance Admin | Như vai trò trong workspace + mục `Quản trị` (S-01-9) | Tổng quan (lần đầu: S-01-8) |
| Owner / Admin | Tất cả | Tổng quan |
| Editor (ML Engineer) | Tất cả trừ cài đặt workspace/thành viên | Tổng quan |
| Labeling Lead | Datasets, Gán nhãn, (Projects, Models nếu Editor) | Bảng điều khiển project đang chạy |
| Annotator | **Chỉ** "Gán nhãn — Việc của tôi" | S-05-3 |
| Reviewer | "Gán nhãn — Việc của tôi" (tab Review) | S-05-3 |
| Viewer | Tổng quan, Datasets, Projects, Models, Deployments, Máy (chỉ đọc) | Tổng quan |

## 3. Tổng quan (S-00-1)

```
┌ Tổng quan ───────────────────────────────────────────────────────────────────────────┐
│ Xin chào, {tên}                                         [+ Dataset] [+ Notebook] [+ Train]│
│ ┌Máy online┐┌GPU dùng/tổng┐┌Job chạy/chờ┐┌Notebook mở┐┌Item chờ gán┐┌Deployment khoẻ┐ │
│ │ 3 / 4    ││  2 / 5      ││  1 / 2     ││   1       ││  1 240     ││   2 / 2       │ │
│ └──────────┘└─────────────┘└────────────┘└───────────┘└────────────┘└───────────────┘ │
│ ┌ Việc của tôi ─────────────────┐ ┌ Hoạt động gần đây ────────────────────────────────┐ │
│ │ • 120 item cần gán (Dự án A)  │ │ Thời gian │ Ai │ Hành động │ Tài nguyên │ Trạng thái │ │
│ │ • 15 item chờ review          │ │ …                                                 │ │
│ │ • Run "rtdetrv4-l" thất bại    │ └───────────────────────────────────────────────────┘ │
│ └───────────────────────────────┘ ┌ Dung lượng ─────────────┐ ┌ Máy ──────────────────┐ │
│                                    │ kho S3 120/500 GiB …     │ │ pc-01 ● RTX4090 80%   │ │
│                                    └─────────────────────────┘ │ pc-02 ● RTX3090 idle  │ │
│                                                                │ mac-01 ○ offline      │ │
│                                                                └───────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────────────┘
```
- Ô số liệu là link tới trang lọc sẵn; khối "Việc của tôi" gom từ M05 (gán/review), M03 (job lỗi), M16.
- Mỗi khối chỉ hiện khi có quyền; dữ liệu cập nhật qua `WatchWorkspaceSummary`.
- RPC: `WorkspaceService.GetSummary`, `WatchWorkspaceSummary`, `AuditService.ListEvents(page_size=10)`.
