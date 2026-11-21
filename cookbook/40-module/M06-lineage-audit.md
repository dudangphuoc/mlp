# M06 — Lineage & Audit

> **Phase:** P1 (lineage tới prediction P2) · **Phụ thuộc:** mọi module (nghe sự kiện) · **Proto:** `lineage.proto` (`LineageService`), `audit.proto` (`AuditService`) · **Schema DB:** `lineage`, `audit`

## 1. Mục tiêu & phạm vi
- **Lineage**: tài nguyên nào sinh từ tài nguyên nào → tái lập và truy vết.
- **Audit**: ai làm gì, lúc nào, từ đâu; không sửa/xoá được.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Lineage tự động dataset version ↔ label set ↔ run ↔ model version ↔ deployment | ✔ | | |
| Lineage tới từng prediction, data pipeline, feature view | | ✔ | ✔ |
| Audit mọi lệnh ghi + tải dữ liệu, chuỗi hash chống sửa | ✔ | | |
| Xuất OpenLineage | | | ✔ |

## 2. Khái niệm
**Node** (một phiên bản tài nguyên), **edge** (quan hệ có tên), **audit event**.

## 3. Dữ liệu

| Bảng | Cột chính |
|---|---|
| `lineage.nodes` | id, workspace_id, type (`dataset_version/label_set/schema_version/run/model_version/deployment/environment/recipe_build/pretrained_weights/job/external_source/code_commit`), ref_id, ref_version, display_name, uri, deleted |
| `lineage.edges` | id, workspace_id, from_node, to_node, relation (`derived_from/labeled_by/uses_schema/trained_on/fine_tuned_from/used_recipe/produced/deployed_as/ran_in/used_code/used_env/assisted_by`), job_id, created_at, data jsonb |
| `audit.events` | id (bigserial), at, workspace_id, actor_type (`user/api_key/service_account/agent/job/system`), actor_id, actor_display, action (`{module}.{entity}.{verb}`), resource_type, resource_id, resource_uri, changes jsonb (đã lọc bí mật), request jsonb (ip, user_agent, rpc, trace_id, request_id), outcome (`success/denied/failed`), prev_hash, hash |

`audit.events` phân vùng theo tháng, giữ tối thiểu 1 năm.

## 4. Quy tắc
- **R1** Lineage ghi **tự động** từ sự kiện, không gọi tay:

| Sự kiện | Cạnh |
|---|---|
| `DatasetVersionCreated` | version —labeled_by→ label set; label set —uses_schema→ schema version; version —derived_from→ nguồn |
| `RunStarted` | run —trained_on→ dataset version; run —fine_tuned_from→ model version gốc hoặc trọng số pretrained; run —used_recipe→ recipe build (repo + commit + digest, license); run —used_code→ commit; run —ran_in→ job |
| `ModelVersionCreated` | run —produced→ model version |
| `DeploymentRevisionCreated` | model version —deployed_as→ deployment |
| `PrelabelCompleted` | label set (sau đó) —assisted_by→ model version |

- **R2** Node không bị xoá khi tài nguyên bị xoá mềm; đánh dấu `deleted`.
- **R3** Audit ghi trong **cùng transaction** với thay đổi (`AuditBehavior`).
- **R4** Từ chối quyền cũng ghi (`denied`), gộp tối đa 1 bản/phút/actor/tài nguyên.
- **R5** User DB của ứng dụng chỉ có `INSERT, SELECT` trên `audit.events`.
- **R6** `hash = SHA-256(prev_hash ‖ canonical(row))` theo workspace; job kiểm toàn vẹn hằng ngày.
- **R7** Không ghi nội dung file, mật khẩu, token, secret.

## 5. Luồng chính
1. Module phát sự kiện → handler Lineage tạo node/edge.
2. Người dùng mở đồ thị trên trang tài nguyên → `GetLineage(uri, hướng, độ sâu)` → recursive CTE, tối đa 500 node.
3. Admin tra audit → lọc → xuất CSV/JSONL qua job.

## 6. Hợp đồng

| RPC | Quyền | Mô tả |
|---|---|---|
| `LineageService.GetLineage` | đọc tài nguyên gốc | `uri`, `direction` (up/down/both), `depth` ≤ 10 |
| `AuditService.ListEvents` | `audit.read`; mọi người: `actor = mình` | Lọc actor, action, tài nguyên, thời gian, outcome |
| `AuditService.GetEvent` | `audit.read` | |
| `AuditService.ExportEvents` | `audit.read` | Job → link tải |
| `AuditService.GetIntegrity` | `audit.read` | Kết quả kiểm chuỗi hash gần nhất |

## 7. Màn hình

### S-06-1 Nhật ký audit — `/w/{ws}/settings?tab=audit`
```
┌ Nhật ký audit ─────────────────────── Toàn vẹn: ✔ kiểm lúc 03:00 hôm nay  [Xuất…] ┐
│ Người [Tất cả▾] Hành động [Tất cả▾] Tài nguyên [ ] Thời gian [7 ngày▾] Kết quả [▾] │
│ Thời gian      │ Người     │ Hành động              │ Tài nguyên        │ Kết quả │
│ 10:42 17/11    │ binh      │ label.item.reject      │ img_0042          │ ✔       │
│ 10:40 17/11    │ an        │ fleet.host.disconnect  │ pc-03             │ ✔       │
│ 10:31 17/11    │ chi       │ data.dataset.download  │ pcb-defects@v3    │ ⛔ denied│
└──────────────────────────────────────────────────────────────────────────────────┘
```
Click dòng → panel bên: diff trước/sau, IP, user agent, RPC, trace id. 

### S-06-3 Tab Hoạt động (thành phần dùng ở Dataset, Labeling project, Run, Model, Deployment, Máy, Recipe) — `…?tab=activity`
Dòng thời gian audit lọc theo tài nguyên (`AuditService.ListEvents(resource=…)`): thời gian, người, hành động (nhãn tiếng Việt), kết quả; click → cùng panel chi tiết như S-06-1. Người có quyền đọc tài nguyên thấy hành động trên chính tài nguyên đó, trừ dữ liệu nhạy cảm (IP, user agent chỉ Owner/Admin).

### S-06-2 Đồ thị lineage (thành phần dùng ở Dataset version, Run, Model version, Deployment)
```
[S3: pcb-raw] → [dataset pcb@v3] → [run deimv2-s-640] → [model pcb-det@v2] → [deploy pcb-api r4]
                   ↑ labeled_by              ↑ used_recipe DEIMv2 @3f2a91c (Apache-2.0)
             [label set #5] ← uses_schema [schema v2]
```
`LineageGraph` trái → phải, chọn hướng (lên/xuống/cả hai), độ sâu; click node mở tài nguyên; node đã xoá hiện mờ. Node run có nút "Copy lệnh tái lập".

## 8. Sự kiện
Module này chủ yếu **nghe**; phát `audit.IntegrityBroken` khi chuỗi hash đứt (M16 báo Owner/Admin).

## 9. Lỗi
`LINEAGE_URI_INVALID` (INVALID_ARGUMENT), `LINEAGE_DEPTH_TOO_LARGE` (INVALID_ARGUMENT), `AUDIT_RANGE_TOO_LARGE` (RESOURCE_EXHAUSTED, xuất > 10 triệu dòng).

## 10. Nghiệm thu
- [ ] Từ một deployment, upstream hiện đúng model version → run → dataset version → label set → schema → nguồn, kèm commit.
- [ ] `UPDATE audit.events` bằng user ứng dụng bị từ chối.
- [ ] Sửa tay một dòng audit bằng superuser → job toàn vẹn phát hiện và thông báo.
- [ ] Trả lại item, đổi vai trò, disconnect máy, tải dataset đều có audit với diff/IP.
- [ ] Không bản ghi audit nào chứa API key hay secret.
