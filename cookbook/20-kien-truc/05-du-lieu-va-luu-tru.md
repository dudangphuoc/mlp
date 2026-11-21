# 20/05 — Dữ liệu và lưu trữ

Quyết định nền: D-08, D-14, D-17. Cách viết truy vấn và migration: [20/02 §5](02-backend-dotnet-aot.md).

## 1. PostgreSQL
PostgreSQL 17 + TimescaleDB, một database `mlplatform`, **mỗi module một schema**:

| Schema | Module | Bảng chính |
|---|---|---|
| `platform` | nền | `migrations`, `outbox`, `jobs_internal` (việc nền), `settings` |
| `identity` | M01 | users, workspaces, memberships, resource_roles, api_keys, instance_settings, email_outbox, integrations… |
| `fleet` | M02 | hosts, enrollments, pools, host_commands, host_metrics* |
| `sched` | M03 | jobs, job_dependencies, job_attempts, job_events, job_log_chunks, job_checkpoints, quotas |
| `data` | M04 | datasets, items, imports, dataset_versions, tabular_schemas, converted (kết nối S3: `identity.integrations`, M01) |
| `label` | M05 | schemas, annotations, item_states, projects, project_members, suggestions, assist_settings, label_sets… |
| `lineage`, `audit` | M06 | nodes, edges / events (phân vùng tháng) |
| `nb` | M08 | profiles, environments, servers, home_volumes, runs |
| `track` | M09 | projects, runs, metrics*, system_metrics*, artifacts |
| `registry` | M11 | models, versions, aliases, exports |
| `serving` | M12 | deployments, revisions, endpoint_keys, request_metrics* |
| `notify` | M16 | notifications, preferences |
| `catalog` | M17 | tasks, recipes, recipe_builds, variants, converters, license_acks, zoo_files |
`*` = hypertable TimescaleDB.

### 1.1 Cột chung
`id uuid` (UUIDv7) · `workspace_id uuid` (bảng thuộc workspace) · `created_at`, `updated_at` (lấy từ `IClock`, không `DEFAULT now()`) · `created_by` · `deleted_at` (xoá mềm) · concurrency bằng cột hệ thống `xmin`.

### 1.2 Quy tắc
- Module không đọc/ghi schema của module khác (SqlGate).
- Truy vấn bảng có `workspace_id` phải lọc theo nó (SqlGate `MLPSQL010`).
- `jsonb` chỉ cho dữ liệu tự do (tham số, metadata, geometry); dữ liệu cần truy vấn thì thành cột.
- Message proto lưu `bytea` khi chỉ cần đọc nguyên khối (HostInfo, JobSpec, signature).

### 1.3 Giữ dữ liệu
| Dữ liệu | Giữ |
|---|---|
| `host_metrics` | 30 ngày (gộp 1 phút sau 24 giờ) |
| `metrics`, `system_metrics` của run | theo vòng đời run |
| `request_metrics` | 90 ngày (gộp 1 giờ sau 7 ngày) |
| `request_samples` | 7 ngày |
| `audit.events` | ≥ 1 năm (cấu hình) |
| `notifications` | 90 ngày |
| Xoá mềm | 30 ngày trong Thùng rác rồi xoá cứng |

## 2. kho S3 (S3)

| Bucket | Nội dung | Đường dẫn |
|---|---|---|
| `mlp-datasets` | item upload, thumbnail, manifest version | `{ws}/datasets/{ds}/…` |
| `mlp-artifacts` | artifact run, file model version, export | `{ws}/runs/{run}/…`, `{ws}/models/{m}/v{n}/…` |
| `mlp-zoo` | Model Zoo: trọng số pretrained đã cache (job `cache_weights`) và file do Admin tải lên (teacher bị gate, backbone) | `zoo/{shared|ws-{ws}}/{sha256}` (metadata ở `catalog.zoo_files`) |
| `mlp-converted` | dataset version đã chuyển định dạng (cache) | `{ws}/datasets/{ds}/v{n}/{format}/…` |
| `mlp-checkpoints` | checkpoint job | `{ws}/jobs/{job}/{attempt}/…` |
| `mlp-logs` | log job/agent | `{ws}/jobs/{job}/{attempt}/{seq}.log` |
| `mlp-notebooks` | snapshot home, output notebook run | `{ws}/users/{user}/home/…` |
| `mlp-exports` | gói export dataset/audit | `{ws}/exports/{id}/…` (tự xoá sau 7 ngày) |

- Byte lớn đi thẳng trình duyệt/SDK/job ↔ kho S3 qua **presigned URL** (5 phút; multipart cho file > 64 MiB). api không chuyển byte (trừ dữ liệu local-only qua data channel).
- Bật versioning cho `mlp-artifacts` (chống ghi đè nhầm).

## 3. Dữ liệu trên máy (local-only)
- `data_path` mount chỉ-đọc vào container; thumbnail, frame trích, archive giải nén nằm trong volume cache trên chính máy.
- Control plane chỉ giữ metadata, nhãn, thống kê; byte phục vụ qua data channel, không ghi đĩa control plane.
- Checkpoint của job trên dữ liệu local-only ghi vào `models_path` của máy; chỉ artifact `best` được upload (người dùng chọn tắt được).

## 4. Docker registry
`registry:2` riêng của instance (D-31). Đồng bộ từ registry phân phối bằng `sync_images` (theo digest của phiên bản instance): `mlp/mlp-node:{ver}` (Ray worker mỏng), `mlp/mlp-tools:{ver}` (ingest, convert, inspect), `mlp/notebook-{profile}:{ver}-{cpu|gpu}`, `mlp/mlp-net:{ver}` (WireGuard sidecar), `recipes/{recipe}:{commit}-{hash}` (recipe builtin). Build tại instance: `recipes/ws-{ws}/{recipe}:{commit}-{hash}` (job `build_recipe`), `ws/{ws}/env-{name}:{n}` (job `build_env`). Truy cập qua TLS từ overlay và mạng nội bộ; riêng `/v2/mlp/mlp-net` mở chỉ-đọc qua 443 cho bootstrap. Dọn tag environment không dùng sau 90 ngày.
