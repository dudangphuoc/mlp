# 10/05 — Khảo sát giao diện Ultralytics bằng REA

> **Tham khảo, không phải spec.** Ngày 2026-11-17. Công cụ: REA 6.9.0 (`inspect_web_page` đọc cây accessibility, `capture_web_screenshot`) trên Edge mở cổng CDP `127.0.0.1:9222`, tài khoản Free của người dùng tự đăng nhập. Mở trang bằng tab mới qua CDP; 3 hộp thoại mở bằng một lệnh `click()` qua CDP; **không bấm nút gửi/lưu nào**.
> Bằng chứng: outline accessibility từng trang trong [`rea-ui-ultralytics/`](rea-ui-ultralytics/) (đã che tên, email). Mỗi dòng là `vai trò: tên` đúng như trình duyệt báo.

## 1. Khung ứng dụng (mọi trang)

Nguồn: `01-home.txt`, ảnh chụp Home.

```
┌──────────────┬────────────────────────────────────────────────────────────────────────┐
│ Logo         │ [≡] Breadcrumb: Người dùng › Datasets › Example Dataset 🔒Private      │
│ [Search… ⌘K] │                                  $5.00  🌐  ⋮⋮⋮  🔔  ☾                  │
│ Home         ├────────────────────────────────────────────────────────────────────────┤
│ Explore      │                                                                        │
│ Agents       │                         Vùng nội dung trang                            │
│ ▾ Annotate (+)│                                                                       │
│    dataset…  │                                                                        │
│ ▾ Train (+)  │                                                                        │
│    project…  │                                                                        │
│ ▾ Deployments│                                                                        │
│ ─────────────│                                                                        │
│ Help         │                                                          [Ask AI]      │
│ Settings     │                                                                        │
│ 👤 tên, email ⋮│                                                                      │
└──────────────┴────────────────────────────────────────────────────────────────────────┘
```
- Sidebar: ô tìm kiếm (`⌘K`), Home / Explore / Agents, ba nhóm theo vòng đời **Annotate** (datasets) · **Train** (projects) · **Deployments**, mỗi nhóm có nút `Create …`, liệt kê tài nguyên gần đây kèm số đếm (superscript) và nút xoá; cuối là Help, Settings, thẻ người dùng.
- Thanh trên: `Toggle Sidebar`, breadcrumb (có badge `Private`), số dư, `Select language`, `Switch apps`, `Activity notifications`, `Toggle theme`.
- Nút nổi `Ask AI` góc dưới phải.
- Giao diện tối, thẻ bo góc, nút chính nền sáng.

## 2. Trang theo từng khu vực

| Trang | Thành phần quan sát được (REA) |
|---|---|
| **Home** | Banner thông báo có nút đóng; thẻ người dùng + 7 ô đếm (Datasets, Images, Annotations, Projects, Models, Exports, Deployments — mỗi ô là link); video Quickstart; 3 thẻ: **Datasets** (`New Dataset`, vùng thả file, dataset gần đây), **Projects** (`New Project`, thả `.pt`), **Storage** (thanh dung lượng theo loại, Resources, Largest items); bảng **Recent Activity** (tìm kiếm, cột Name/Type/Description/Status/Updated, phân trang) |
| **Explore** | Tab `Datasets · Images · Models · Projects`; ô `Search datasets...`, `Search by image`, sắp xếp `Relevance ↓`, 3 chế độ xem `Grid / Compact / Table`; thẻ dataset có ảnh, chủ sở hữu, task, số class, dung lượng, ngày, tag class, split |
| **Agents** | Thanh trên: combobox chọn workflow, `New workflow`, `Save workflow`, `Run workflow`; canvas có `Zoom in/out`, `Reset view`, `Add block`, `Python` (xuất mã) |
| **Hồ sơ `/<user>`** | `Share`; tab `Datasets{n} · Projects{n} · Deployments`; `New Dataset`, tìm kiếm, sắp xếp `Created ↓`, 3 chế độ xem |
| **Dataset** | Header: icon tuỳ biến, tên, badge task (`Detect`), license (`CC BY 4.0`), trạng thái (`Ready`), dòng thống kê (ảnh, đã gán, annotation, dung lượng, cập nhật), mô tả; hành động `New Model`, `Upload images`, `Download dataset`, `Star`, `More dataset actions`. Tab `Images · Classes{n} · Charts · Models · Versions` + tab split `All{n} · Train{n} · Val{n} · Test` có thanh tỉ lệ màu |
| Dataset › Images | `Annotation visibility`, `Clustering`, bộ lọc `All · Created ↓`, ô tìm `Search images by name, class, metadata or what they show`, Grid/Compact/Table; lưới ảnh có overlay nhãn; mỗi ảnh: xem thông tin, tải, xoá |
| Dataset › Classes | Biểu đồ phân bố (`Switch to log scale`, `Fullscreen`, `More options`); `Search classes...`; `Merge into one`, `Delete` (bật khi chọn); bảng có checkbox, cột **Index · Name · Annotations · Images** |
| Dataset › Charts | 10 biểu đồ: Split Distribution, Top Classes, Image Dimensions, Image File Size, Image Dimensions 2D (heatmap), Annotation Locations, Image Formats, Bounding Box Dimensions, Objects per Image (mỗi biểu đồ có dòng tóm tắt: số lượng, trung bình) |
| Dataset › Models | Trạng thái rỗng: "No models trained yet" + lời giải thích |
| Dataset › Versions | "Export immutable NDJSON snapshots…", ô `Version description (optional)...`, `New Version`; rỗng: "No dataset versions yet" |
| **Hộp New Dataset** | Tab nguồn `Upload · URL · Cloud · On Premise`; vùng thả (Images/Videos/Datasets + giới hạn); `Dataset Name`, `URL` (kiểm "URL is available"), `Description (optional)`, `Task Type` (nút chọn), `License`, switch `Blur faces`, switch `Visibility`; `Cancel` / `Create Dataset` |
| **Trình gán nhãn** (hộp thoại theo tên ảnh) | Thanh công cụ: `Draw`, `Smart`, `Undo`, `Redo`, `Delete selected`, `Save`, `Keyboard shortcuts`, `Annotation visibility`, `View image information`, `Hide classes`, `Rotate image clockwise`, `Download image`, `Delete image`, `Close`; điều hướng `Zoom out / 100% / Zoom in / Reset to fit`, `Next image`; panel class: `Search classes...`, mỗi class có số đếm, ẩn/hiện, `Rename` |
| **Project** | `New Model`, `Upload models`, `Star`, `More project actions`; `Search models...`, nhóm `by task · Created ↓`, 3 chế độ xem; danh sách model nhóm theo task (`Detect 1`), checkbox chọn model để so sánh, đổi màu, tải, xoá; vùng thả `.pt`; biểu đồ Metrics/Loss/Learning Rate cho model được chọn |
| **Model** | Header: tên, `Download model`, `Star`, `More model actions`; 4 thẻ KPI (mAP50, mAP50-95, Precision, Recall), thẻ dataset; tab `Overview · Train · Predict · Export · Deploy` |
| Model › Overview | Run Information (trạng thái, Runtime, phiên bản, Documentation, Parent Model, Command + nút cài đặt); **Training Configuration** (`Search parameters...`, bảng Parameter/Value, phân trang, `Export data`); **Performance Metrics** (`Search metrics...`, bảng Metric/Value) |
| Model › Train | Tab con `Metrics · Console · System`; nhóm biểu đồ thu gọn được `Metrics (6/7)`, `Loss (3/3)`, `Learning Rate (4/4)` |
| Model › Predict | Nguồn `Image / Webcam`, vùng thả (định dạng, ≤ 10 MB), `Dataset image (Val)`, ảnh ví dụ; slider `Confidence`, `IoU`, `Image Size` (có nút giải thích); kết quả tab `Results · Response · Docs` |
| Model › Export | `Export All`; 23 thẻ định dạng, mỗi thẻ nút `Export` (một số bị tắt) |
| Model › Deploy | Bản đồ (zoom), `Ultralytics Cloud`, `Rescan`, `Search regions...`, bảng **Location · Zone · Latency · Distance** + nút `Deploy` mỗi dòng, phân trang |
| **Hộp Train New Model** | `Base Model` (combobox có icon), `Dataset` (combobox + dải ảnh xem trước), `Epochs`, `Batch Size` (auto), `Image Size` (slider), `Name` — mỗi trường có nút `About …`; `Advanced Settings` (mở rộng) + `Reset`; tab `Cloud Training · Local Training`; `Save Dataset Version`; `GPU Type`; ước tính; `Close` / `Start Training` |
| **Settings** | Tab `Profile · API Keys · Plans · Billing · Usage · Referrals · Teams · Integrations · Trash` |
| Settings › Profile | Display name, Username (khoá), Company, Primary use case, Bio, 7 link mạng xã hội, switch visibility mặc định Projects/Datasets, switch Early access, đổi email (`Send code`), `Export All Data`, tài khoản liên kết (`Disconnect`, `Connect GitHub`), `Delete My Account` |
| Settings › API Keys | `Add Key`; key tự sinh có `Copy API key`, `Remove` |
| Settings › Teams | `Search members...`; bảng **User · Role · Joined**; ma trận quyền **Feature × Owner/Admin/Editor/Viewer** |
| Settings › Integrations | Danh sách nhóm `Infrastructure` (On Premise, S3, GCS, Azure) · `Notifications` (Slack) · `Imports` (W&B, Roboflow, Labelbox, LabelMe, CVAT, Label Studio); panel On Premise: bảng **Host · Actions**, tab OS `Linux · macOS · Windows`, 2 ô thư mục, nút hành động |
| Settings › Trash | `Refresh`; tab `All · Datasets · Projects · Models` |
| **Help** | Video theo vòng đời, link tài liệu, cộng đồng, form feedback |

## 3. Mẫu UI lặp lại (đầu vào cho [30-ui](../30-ui/01-he-thong-thiet-ke.md))

| Mẫu | Xuất hiện ở |
|---|---|
| Danh sách có **3 chế độ xem** Grid / Compact / Table + tìm kiếm + sắp xếp | Explore, hồ sơ, Dataset Images, Project |
| **Tab có số đếm** (`Classes80`, `Train4`) | Dataset, hồ sơ |
| **Header tài nguyên**: icon + tên + badge (task, license, trạng thái, Private) + dòng thống kê + mô tả + nhóm nút hành động + menu `More …` | Dataset, Project, Model |
| **Thẻ KPI** | Home, Model |
| **Vùng thả file** kèm giới hạn kích thước/định dạng | Home, Dataset, Project, Predict, New Dataset |
| **Trạng thái rỗng** có câu giải thích + hành động | Dataset Models/Versions, Deployments, Integrations |
| **Bảng dữ liệu**: tìm kiếm, cột sắp xếp được, chọn nhiều + hành động hàng loạt, phân trang, `Export data` | Classes, Overview, Teams, Deploy, Recent Activity |
| **Biểu đồ nhóm thu gọn được** + chọn series hiển thị | Project, Model Train |
| **Hộp thoại form** nhiều nhóm trường, mỗi trường có nút giải thích `About …`, phần nâng cao thu gọn | Train New Model, New Dataset |
| **Trình soạn thảo toàn màn hình** có thanh công cụ + panel phải | Trình gán nhãn |
| **Canvas node** có zoom/reset | Agents, bản đồ Deploy |
| **Switch** cho cài đặt bật/tắt | Profile, New Dataset |
