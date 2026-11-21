# M07 — Feature Store

> **Phase:** P3 · **Phụ thuộc:** M04, M12 · **Proto:** `feature.proto`  (`FeatureService`) · **Schema DB:** `feature`
> **Trạng thái spec:** lát cắt đầu đã hiện thực và mô tả ở mục 2–10. **Lệch có chủ đích so với mục 1:** offline/online store tạm đặt trong PostgreSQL (`feature.offline`, `feature.online`), materialize chạy trong control plane, nguồn là CSV ≤ 50 MB — đủ để chốt hợp đồng và ngữ nghĩa (point-in-time, TTL, version, binding). Parquet trên kho S3, Redis và Ray Data thay vào sau mà không đổi `FeatureService`. Feast: chưa tích hợp (xem §2).

## 1. Mục tiêu & phạm vi
Kho đặc trưng dùng chung giữa train (offline) và suy luận (online) để tránh training/serving skew.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Entity + feature view (nguồn, biến đổi, TTL) có version |  |  | ✔ |
| Offline store Parquet trên kho S3 + point-in-time join |  |  | ✔ |
| Online store (Redis) + materialize bằng Ray Data |  |  | ✔ |
| Lấy feature trong serving theo cùng định nghĩa |  |  | ✔ |
| Cân nhắc tích hợp Feast thay vì tự viết |  |  | ✔ |

## 2. Khái niệm
- **Entity**: đối tượng có đặc trưng (khách hàng, máy…), có **cột khoá** (`join_key`, vd. `customer_id`).
- **Feature view**: tên + version (cùng tên tạo lại → version mới, bản cũ giữ nguyên để train cũ tái lập), entity, **nguồn** = dataset version dạng bảng (M04), **cột thời gian sự kiện**, danh sách feature (kiểu `number`/`string` tự suy), **TTL**, bật online, lịch materialize (phút, tuỳ chọn).
- **Offline store**: toàn bộ lịch sử `(entity, event_ts, values)` của view.
- **Online store**: bản mới nhất mỗi entity — dùng lúc suy luận.
- **Point-in-time join**: với mỗi dòng (entity, mốc t) của bảng train lấy bản ghi mới nhất có `event_ts ≤ t` và `t − event_ts ≤ TTL` → không rò rỉ dữ liệu tương lai.
- **Tham chiếu feature**: `view:feature` (bản mới nhất sẵn sàng) hoặc `view@2:feature` (ghim version).
- **Binding**: deployment (M12) gắn entity + danh sách tham chiếu → gateway nhận `{"entity":"c1"}` và tự lấy đúng các feature đó từ online store — tránh lệch train/serving.
- **Vì sao chưa dùng Feast**: Feast cần Python/registry riêng, không chạy được trong mlp-api AOT; hợp đồng `FeatureService` giữ ngữ nghĩa tương đương (entity, feature view, PIT, online) để có thể thay backend bằng Feast sau nếu cần.

## 3. Dữ liệu (schema `feature`)
| Bảng | Cột chính |
|---|---|
| `entities` | id, workspace_id, name, join_key, description |
| `views` | id, workspace_id, name, version, entity_id, source_version_id, timestamp_column, features jsonb, ttl_hours, online, materialize_every_min, state, state_reason, rows, entities, last_materialized_at, max_event_ts |
| `offline` | view_id, entity_key, event_ts, values jsonb (index PIT: view, entity, event_ts desc) |
| `online` | view_id, entity_key, event_ts, values jsonb, updated_at |
| `bindings` | deployment_id, entity_id, features jsonb |

## 4. Trạng thái & quy tắc
```
view: pending → ready | failed (lỗi dữ liệu nguồn: thiếu cột, thời gian/ số sai) ; materialize lại → ready | failed
```
- **R1** Materialize làm lại toàn bộ từ nguồn (idempotent); online = bản mới nhất mỗi entity.
- **R2** PIT không bao giờ trả giá trị có `event_ts > t`; quá TTL → null.
- **R3** Online quá TTL → coi là thiếu: RPC báo `missing`, gateway trả 422 `FEATURE_MISSING` (không suy luận im lặng trên giá trị cũ).
- **R4** Binding chỉ nhận view bật online và cùng entity; input gateway có thể ghi đè từng giá trị bằng `tabular`.
- **R5** Lịch materialize ≥ 5 phút, chạy bởi vòng nền mỗi phút.

## 5. Luồng chính
1. Tạo entity → tạo feature view từ dataset version (chọn cột thời gian, TTL, online, lịch) → materialize ngay.
2. Train: dựng bảng (entity, mốc thời gian, nhãn) → `GetHistoricalFeatures` → bảng không rò rỉ → dataset version (M04) → train (M09).
3. Serving: `BindDeployment` → client gọi `/predict` với `{"entity":"c1"}` → gateway lấy feature online → model.

## 6. Hợp đồng (`feature.proto` — `FeatureService`)
| RPC | Quyền | Mô tả |
|---|---|---|
| `CreateEntity` / `CreateView` / `Materialize` | `dataset.write` | |
| `ListEntities` / `ListViews` / `SampleOnline` | `dataset.read` | |
| `GetHistoricalFeatures` | `dataset.read` | ≤ 10 000 dòng spine |
| `GetOnlineFeatures` | `dataset.read` | ≤ 1 000 entity; trả `missing` |
| `BindDeployment` / `GetDeploymentFeatures` | `deployment.manage` / `run.read` | |
| REST `POST …/predict` với `{"entity": …, "tabular"?: {…}}` | khoá `mlpd_` | Lấy feature theo binding |

## 7. Màn hình (phác thảo)
Danh sách feature view, chi tiết view (nguồn, schema, lịch materialize, độ tươi), trình duyệt giá trị online theo entity.

## 8. Sự kiện
_Chưa có_ (sau: `FeatureViewStale` khi nguồn không có dữ liệu mới quá TTL → M16).

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `FEATURE_ENTITY_NOT_FOUND` / `FEATURE_VIEW_NOT_FOUND` / `FEATURE_NOT_FOUND` | NOT_FOUND | — |
| `FEATURE_SOURCE_COLUMN_MISSING` | INVALID_ARGUMENT | Nguồn thiếu cột khoá/thời gian/feature |
| `FEATURE_SOURCE_UNSUPPORTED` / `FEATURE_SOURCE_TOO_LARGE` | FAILED_PRECONDITION / RESOURCE_EXHAUSTED | Nguồn không phải CSV / > 50 MB |
| `FEATURE_VIEW_NOT_READY` / `FEATURE_VIEW_NOT_ONLINE` | FAILED_PRECONDITION | — |
| `FEATURE_REF_INVALID` / `FEATURE_ENTITY_MISMATCH` | INVALID_ARGUMENT | — |
| `FEATURE_NOT_BOUND` / `FEATURE_MISSING` | 400 / 422 (REST) | Chưa gắn / thiếu hoặc quá TTL |

## 10. Nghiệm thu
- [x] PIT đúng mốc: trước sự kiện đầu → null; giữa hai sự kiện → bản cũ; sau → bản mới; quá TTL → null (kịch bản M12+M13+M15+M07).
- [x] Online trả bản mới nhất; entity quá TTL / không có → `missing`.
- [x] Gateway `{"entity":"c1"}` dùng đúng feature đã gắn; ghi đè bằng `tabular`; entity quá TTL → 422.
- [x] Tạo lại cùng tên → version 2, tham chiếu `@1` vẫn dùng bản cũ.
- [ ] Offline Parquet trên kho S3, online Redis, materialize bằng Ray Data cho nguồn lớn.
- [ ] Biến đổi (transformation) trong định nghĩa view; nguồn streaming.
