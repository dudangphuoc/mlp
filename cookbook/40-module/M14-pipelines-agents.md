# M14 — Pipelines & Agents

> **Phase:** P2 · **Phụ thuộc:** M03, M04, M05, M09, M11, M12 · **Proto:** `pipeline.proto`  (`PipelineService`) · **Schema DB:** `pipeline`
> **Trạng thái spec:** lát cắt đầu của Pipelines (DAG JSON, engine trong .NET, trigger thủ công/chu kỳ/sự kiện drift, chạy lại từ bước lỗi) đã hiện thực và mô tả ở mục 2–10; định nghĩa YAML/Python, cron, cache theo hash, bước notebook/deploy/eval, canvas kéo-thả và **Agents** viết tiếp khi làm.

## 1. Mục tiêu & phạm vi
Điều phối chuỗi việc ML (ingest → tiền xử lý → gán nhãn hỗ trợ → train → eval → đăng ký → deploy) và workflow suy luận dạng khối (Agents) trên cùng một canvas.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| DAG khai báo YAML/Python; bước = job M03 hoặc notebook M08 |  | ✔ |  |
| Lập lịch cron / sự kiện / thủ công; retry; cache theo hash input; chạy lại từ bước lỗi |  | ✔ |  |
| Agents: khối Input (dataset, ảnh, text), Model (registry, LLM), Logic (gate), Output (webhook, export, deployment); chạy thử; xuất Python |  | ✔ |  |
| Engine DAG trong .NET, mỗi bước là một job M03 (`SubmitJob` + `depends_on`), chạy trong image của recipe/`mlp-tools` (D-21) |  | ✔ |  |

## 2. Khái niệm
- **Pipeline**: định nghĩa DAG `{"params":{…mặc định}, "steps":[{"id","kind","needs":[…],"config":{…},"retries":0}]}` + **trigger**.
- **Bước** (`kind`): `train` (run M09 qua `StartTraining`), `job` (job M03 tuỳ ý: image + command), `gate` (so sánh số `left op right`), `register` (M11 từ run), `promote` (đổi stage M11). Bước `train`/`job` bất đồng bộ; các bước còn lại chạy ngay.
- **Tham chiếu**: chuỗi trong `config` dùng `${params.x}` hoặc `${steps.<id>.<trường>}`; cả chuỗi là một tham chiếu thì giữ kiểu gốc (số…). Chỉ được tham chiếu bước nằm phía trên trong DAG.
- **Output bước**: train `{run_id, job_id, project_id, metric, metric_key}`; gate `{passed, left, right}`; register `{version_id, version, model}`; promote `{version_id, stage}`; job `{job_id}`.
- **Trigger**: `manual`; `interval {minutes ≥ 5}`; `event {event: deployment.drift}` — M13 báo động thì pipeline chạy với tham số `deployment_id`, `deployment` (continuous training).
- **Lần chạy** mang danh người khởi chạy (trigger tự động: người tạo pipeline) — mọi lệnh con được audit như người đó.

## 3. Dữ liệu (schema `pipeline`)
| Bảng | Cột chính |
|---|---|
| `pipelines` | id, workspace_id, name, definition jsonb, trigger jsonb, enabled, last_triggered_at, created_by |
| `runs` | id, pipeline_id, number, state, params jsonb, trigger, error, created_by, created_at, finished_at |
| `steps` | run_id, step_id, kind, state, attempt, ref_id, output jsonb, error, started_at, finished_at |

## 4. Trạng thái & quy tắc
```
run:  running → succeeded | gated (có gate đóng, không bước nào lỗi) | failed | cancelled
step: pending → running → succeeded | failed ;  pending → gated (gate không thoả) | skipped (bước trước lỗi/gated)
```
- **R1** Định nghĩa được kiểm khi tạo: id bước, kind, config bắt buộc, `needs` tồn tại, không chu trình, tham chiếu chỉ tới bước phía trên.
- **R2** Engine trong control plane mỗi 3 s: trigger chu kỳ đến hạn → tiến từng lần chạy (kiểm bước đang chạy, chạy bước đủ điều kiện, bỏ qua nhánh sau bước lỗi/gated, chốt khi mọi bước kết thúc). Không chạy DAG trong Ray.
- **R3** `retries` (0–5) cho từng bước: run/job lỗi còn lượt → chạy lại bước.
- **R4** Chạy lại từ bước lỗi: giữ output bước đã thành công, đặt lại bước lỗi + bị bỏ qua, cho phép đổi tham số.
- **R5** Huỷ: dừng job của bước đang chạy (không thử lại).

## 5. Luồng chính
1. Tạo pipeline (định nghĩa + trigger) → `CreatePipeline`.
2. `StartRun` (hoặc trigger) → tạo lần chạy + bước `pending` → engine tiến dần → thông báo M16 `pipeline.succeeded/failed`.
3. Continuous training: M13 báo động drift → pipeline trigger `deployment.drift` chạy (vd. train lại trên dataset mới → gate → register → promote staging).

## 6. Hợp đồng (`pipeline.proto` — `PipelineService`)
| RPC | Quyền | Mô tả |
|---|---|---|
| `CreatePipeline` | `job.submit` | Kiểm định nghĩa (R1) + trigger |
| `ListPipelines` / `ListRuns` / `GetRun` | `run.read` | GetRun kèm bước, `needs`, output |
| `StartRun` | `job.submit` | Tham số ghi đè `params` mặc định |
| `RetryRun` | `job.submit` | R4 |
| `CancelRun` | `job.submit` | R5 |

## 7. Màn hình (phác thảo)
Danh sách pipeline/agent; canvas node (thêm khối, nối cạnh, cấu hình khối ở panel phải, zoom/reset — như Agents của Ultralytics); lịch sử lần chạy với trạng thái từng bước, log, artifact.

## 8. Sự kiện
Nghe `deployment.drift` (M13). Phát `pipeline.succeeded`, `pipeline.failed` → M16.

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `VALIDATION_FAILED` (`definition`) | INVALID_ARGUMENT | Định nghĩa sai (R1) |
| `PIPELINE_TRIGGER_INVALID` | INVALID_ARGUMENT | Trigger không hợp lệ |
| `PIPELINE_NAME_TAKEN` | ALREADY_EXISTS | Tên pipeline đã dùng |
| `PIPELINE_NOT_FOUND` / `PIPELINE_RUN_NOT_FOUND` | NOT_FOUND | — |
| `PIPELINE_RUN_NOT_FAILED` | FAILED_PRECONDITION | Chỉ chạy lại lần chạy lỗi/đã huỷ |
| `PIPELINE_REF_INVALID` / `PIPELINE_GATE_NOT_NUMBER` | INVALID_ARGUMENT (ghi vào bước) | Tham chiếu chưa có giá trị |

## 10. Nghiệm thu
- [x] train → gate → register → promote: model version lên staging (kịch bản M14).
- [x] Gate không thoả → lần chạy `gated`, bước sau `skipped`.
- [x] Train lỗi → `failed`; chạy lại từ bước lỗi với tham số mới → `succeeded`.
- [x] DAG có chu trình / tham chiếu bước không phía trên bị từ chối.
- [x] Drift M13 báo động → pipeline trigger `deployment.drift` tự chạy với `deployment_id`.
- [ ] Cron, cache theo hash input, bước notebook/eval/deploy, định nghĩa YAML/Python.
- [ ] Agents: canvas khối Input/Model/Logic/Output, chạy thử, xuất Python.
