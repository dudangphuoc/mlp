# 30/01 — Hệ thống thiết kế Web UI

Nguồn mẫu: [khảo sát UI bằng REA](../10-khao-sat/05-ui-ultralytics-rea.md). Mỗi màn hình cụ thể nằm trong mục **7. Màn hình** của từng [module](../40-module/).

## 1. Công nghệ
| Hạng mục | Chọn |
|---|---|
| Khung | React 19 + Vite + TypeScript |
| Gọi API | Connect-ES (gRPC-Web) từ mã sinh `web/packages/proto` |
| Định tuyến | TanStack Router (route có kiểu, tham số URL là nguồn trạng thái tab/bộ lọc) |
| Trạng thái server | TanStack Query (cache theo RPC + tham số); stream `Watch*` cập nhật cache |
| Giao diện | Tailwind CSS 4 + bộ component tự xây trên Radix UI primitives |
| Bảng | TanStack Table (ảo hoá khi > 200 dòng) |
| Biểu đồ | ECharts (biểu đồ metric, phân bố, heatmap) |
| Canvas gán nhãn ảnh | Konva (vẽ bbox/polygon/keypoint, zoom/pan) |
| Notebook | iframe tới `/nb/{id}/` |
| Đa ngôn ngữ | Tiếng Việt + tiếng Anh, chuỗi trong file JSON theo khoá |
| Chủ đề | Tối (mặc định) và sáng |

## 2. Khung ứng dụng

```
┌───────────────┬──────────────────────────────────────────────────────────────────────┐
│ MLPlatform    │ [≡] Breadcrumb › Breadcrumb › Tài nguyên [badge]   🔔(n) 🌐 ☾  👤        │
│ [Tìm… ⌘K]     ├──────────────────────────────────────────────────────────────────────┤
│ ▣ Tổng quan   │                                                                      │
│ ◎ Khám phá(P3)│                       NỘI DUNG TRANG                                 │
│ ─ DỮ LIỆU ───  │                                                                      │
│ ▤ Datasets   +│                                                                      │
│ ✎ Gán nhãn    │                                                                      │
│ ─ PHÁT TRIỂN ─ │                                                                      │
│ ⌨ Notebooks  +│                                                                      │
│ ⚗ Projects   +│                                                                      │
│ ⚙ Jobs        │                                                                      │
│ ─ MODEL ─────  │                                                                      │
│ ▦ Model Catalog│                                                                      │
│ ◈ Models      │                                                                      │
│ ⇪ Deployments+│                                                                      │
│ ─ HẠ TẦNG ───  │                                                                      │
│ 🖥 Máy (Fleet) │                                                                      │
│ ───────────── │                                                                      │
│ ? Trợ giúp    │                                                                      │
│ ⚙ Cài đặt     │                                                                      │
│ [Workspace ▾] │                                                                      │
└───────────────┴──────────────────────────────────────────────────────────────────────┘
```
- Sidebar nhóm theo **vòng đời** (Dữ liệu → Phát triển → Model → Hạ tầng), mỗi nhóm có nút `+` tạo nhanh và tối đa 5 tài nguyên gần đây.
- Mục chỉ hiện khi người dùng có quyền (Annotator chỉ thấy "Gán nhãn — việc của tôi").
- `? Trợ giúp` mở **S-00-2** (panel bên): bảng phím tắt của trang hiện tại, link tài liệu SDK/CLI (`/docs`, phục vụ tĩnh từ control plane), phiên bản control plane + agent, trạng thái hệ thống (từ `/health/ready`).
- `[Workspace ▾]` là S-01-6 (chuyển / tạo workspace).
- Bộ chọn workspace ở đáy sidebar; đổi workspace giữ nguyên trang nếu tồn tại.
- Thanh trên: breadcrumb (badge `Private`/trạng thái), chuông thông báo (M16), ngôn ngữ, chủ đề, menu người dùng.
- `⌘K` mở **bảng lệnh**: tìm dataset/model/run/máy theo tên, nhảy trang, chạy lệnh nhanh ("Tạo notebook", "Cắm máy").
- Responsive: ≥ 1280 px đầy đủ; 768–1279 px sidebar thu gọn thành icon; < 768 px chỉ hỗ trợ xem (không gán nhãn, không notebook).

## 3. Token thiết kế
| Nhóm | Token |
|---|---|
| Màu nền | `bg` (trang), `surface` (thẻ), `surface-2` (thẻ lồng), `overlay` (hộp thoại) |
| Chữ | `fg`, `fg-muted`, `fg-subtle` |
| Nhấn | `primary` (hành động chính), `accent` |
| Trạng thái | `success` (healthy/ready/accepted), `warning` (degraded/draining/in_review), `danger` (failed/offline/rejected), `info` (running/queued) |
| Split | `split-train` xanh lá, `split-val` xanh dương, `split-test` tím (như Ultralytics) |
| Bo góc | 6 px (input, nút), 10 px (thẻ), 14 px (hộp thoại) |
| Khoảng cách | thang 4 px |
| Chữ | Inter (UI), JetBrains Mono (mã, ID, log) |

## 4. Component dùng chung

| Component | Mô tả | Dùng ở |
|---|---|---|
| `PageHeader` | Icon + tên + badge (task, trạng thái, visibility, license) + dòng thống kê + mô tả + nhóm nút + menu `⋯` | Dataset, Project, Model, Deployment, Máy |
| `ResourceTabs` | Tab có số đếm, đồng bộ `?tab=` trên URL | Mọi trang chi tiết |
| `CollectionView` | Tìm kiếm + lọc + sắp xếp + chuyển **Lưới / Gọn / Bảng** + chọn nhiều + thanh hành động hàng loạt | Datasets, Items, Projects, Models, Máy, Jobs |
| `DataTable` | Cột sắp xếp, ẩn/hiện cột, phân trang `page_token`, xuất CSV | Mọi bảng |
| `StatCard` / `KpiStrip` | Ô số liệu có link | Tổng quan, Model, Máy |
| `StatusBadge` | Màu theo token trạng thái + tooltip lý do | Mọi trạng thái |
| `DropZone` | Kéo-thả, liệt kê giới hạn, tiến độ từng file, tiếp tục khi rớt mạng | Upload dataset, import model, predict |
| `EmptyState` | Icon + một câu giải thích + hành động chính | Mọi danh sách rỗng |
| `FormDialog` | Nhóm trường, nút `?` giải thích từng trường, phần **Nâng cao** thu gọn, kiểm lỗi tại chỗ, nút chính bị khoá tới khi hợp lệ | Tạo dataset, train, notebook, deploy |
| `YamlEditor` | CodeMirror, kiểm schema, nhận dán YAML/JSON, kéo thả file, badge "Đã sửa", nút đặt lại | Tham số train nâng cao |
| `MetricChartGroup` | Nhóm biểu đồ thu gọn được, chọn series, làm mượt, trục step/thời gian, kéo-thả bố cục | Run, Project, Máy |
| `LogViewer` | ANSI, tìm kiếm, cuộn theo, bật timestamp, tải xuống | Job, Run, Notebook, Deployment |
| `CommandBlock` | Khối lệnh có nút copy, che bí mật | Cắm máy, chạy ngoài fleet, gọi endpoint |
| `ProgressSteps` | Các bước có trạng thái realtime | Cài máy, khởi động notebook, import |
| `ConfirmDialog` | Hành động nguy hiểm: gõ tên tài nguyên để xác nhận, nêu hậu quả | Xoá, disconnect, promote production |
| `LineageGraph` | Đồ thị trái → phải, click node mở tài nguyên | Dataset version, Run, Model version, Deployment |
| `HardwareChip` | GPU model + VRAM, CPU, RAM, OS icon | Máy, chọn tài nguyên |
| `ResourcePicker` | Chọn preset tài nguyên + "Tuỳ chỉnh", hiện số máy đủ điều kiện, máy đã có image recipe, thời gian chờ ước tính | Train, notebook, deploy |
| `RecipePicker` | Chọn recipe theo task (thẻ: tên, biến thể, VRAM, license ⚠, trạng thái build) rồi biến thể | Train, eval, notebook, import model |
| `LicenseBadge` + `LicenseAckDialog` | Hiện license recipe/trọng số; hộp xác nhận lần đầu khi có cảnh báo (D-23) | Catalog, Train, Deploy |
| `AudioPlayer` | Phát audio + sóng âm + tua, hiển thị transcript đồng bộ | Item audio, trình gán nhãn audio, playground TTS/ASR, artifact |

## 5. Mẫu hành vi

| Tình huống | Quy định |
|---|---|
| **Đang tải** | Skeleton đúng hình khối nội dung; không spinner toàn trang |
| **Rỗng** | `EmptyState` + hành động tạo (ẩn nếu không có quyền) |
| **Lỗi** | Banner trong vùng nội dung với `reason` dịch sang câu dễ hiểu + `traceId` + nút thử lại |
| **Không có quyền** | Trang `404` chung (không lộ tồn tại); nút không được phép thì **ẩn**, không hiện xám |
| **Mất stream realtime** | Chip "Đang kết nối lại…" ở thanh trên; tự mở lại stream im 45 s |
| **Thao tác dài** | Chuyển thành job: toast "Đã bắt đầu" + link tới job; tiến độ trong `ProgressSteps` |
| **Xung đột `etag`** | Hộp thoại "Dữ liệu đã thay đổi" với lựa chọn tải lại hoặc ghi đè (nếu có quyền) |
| **Xác nhận** | Xoá, disconnect, promote production, dừng job dài → `ConfirmDialog` |
| **Hoàn tác** | Xoá mềm vào Thùng rác 30 ngày; toast có nút "Hoàn tác" 10 s |
| **Phím tắt** | `⌘K` bảng lệnh; `?` danh sách phím tắt của trang; trình gán nhãn có bộ riêng (M05) |
| **Trạng thái trên URL** | Tab, bộ lọc, sắp xếp, chế độ xem nằm trong query string để chia sẻ link |

## 6. Quy ước văn bản
- Nút: động từ + danh từ ("Tạo dataset", "Bắt đầu train"), không "OK".
- Thời gian: tương đối ("9 giờ trước") + tooltip thời điểm đầy đủ theo múi giờ người xem.
- Số: phân tách hàng nghìn, đơn vị nhị phân cho dung lượng (KiB, MiB, GiB).
- ID dài: hiển thị 8 ký tự đầu bằng font mono + nút copy.
