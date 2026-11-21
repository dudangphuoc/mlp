# M10 — HPO & AutoML

> **Phase:** P2 · **Phụ thuộc:** M03, M09 · **Proto:** `tuning.proto`  (`TuningService`) · **Schema DB:** `tune`
> **Trạng thái spec:** lát cắt đầu (grid/random) đã hiện thực và mô tả ở mục 2–10; Bayesian (Optuna), ASHA/PBT, early stopping, ngân sách thời gian/GPU-giờ, AutoML preset viết tiếp khi làm.

## 1. Mục tiêu & phạm vi
Tự thử nhiều tổ hợp tham số/kiến trúc để tìm cấu hình tốt nhất. Theo D-21, mỗi trial là một job `train` M03 chạy trong image của recipe; bộ điều phối sweep (Optuna/ASHA) chạy trong control plane hoặc một job điều phối nhỏ, **không** chạy trial bên trong Ray Tune.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Sweep: không gian tìm kiếm YAML, grid/random/Bayesian (Optuna)/ASHA/PBT |  | ✔ |  |
| Metric mục tiêu, ngân sách (số trial, thời gian, GPU-giờ), early stopping |  | ✔ |  |
| Mỗi trial là một run M09 |  | ✔ |  |
| AutoML preset theo task: tabular, CV, text |  | ✔ |  |

## 2. Khái niệm
- **Sweep**: một lần tìm tham số cho (project, recipe, biến thể, task) với **không gian tìm kiếm**, **thuật toán**, **mục tiêu** và **ngân sách**.
- **Không gian tìm kiếm** (JSON; YAML sau): mỗi tham số của recipe là `{"values":[…]}` (rời rạc) hoặc `{"min":a,"max":b,"log":bool,"int":bool}` (liên tục). Tham số không có trong `params` của recipe bị từ chối.
- **Trial**: một tổ hợp tham số = **một run M09** (job `train` M03 trong image recipe, D-21). Tham số cố định chung cho mọi trial đặt ở `base_params`.
- **Mục tiêu**: khoá metric (mặc định metric chính của recipe) + hướng `maximize`/`minimize`; giá trị trial = giá trị cuối của metric đó.
- **Thuật toán**: `grid` (duyệt hết tổ hợp, chỉ với `values`), `random` (tất định theo `seed` + số thứ tự trial). Bayesian/ASHA/PBT: sau.

## 3. Dữ liệu (schema `tune`)
| Bảng | Cột chính |
|---|---|
| `sweeps` | id, workspace_id, project_id, name, recipe, variant, task, algorithm, space jsonb, base_params jsonb, objective, direction, max_trials, parallel, seed, cpus, gpus, dataset_version_id, state, state_reason, best_trial, best_value, created_by, created_at, finished_at |
| `trials` | sweep_id, number, params jsonb, run_id, state, value, error, created_at |

## 4. Trạng thái & quy tắc
```
sweep: running → completed (đủ ngân sách, mọi trial kết thúc, có giá trị) | failed | stopped
trial: queued → running → succeeded | failed | cancelled   (theo run M09)
```
- **R1** Bộ điều phối chạy trong control plane mỗi 3 s, mang danh người tạo sweep (audit ghi `StartTraining` của người đó); không chạy trial trong Ray Tune.
- **R2** Số trial đang chạy ≤ `parallel`; tổng trial ≤ `max_trials` (grid: ≤ số tổ hợp).
- **R3** 3 trial đầu đều lỗi → sweep `failed` (tránh đốt ngân sách vì cấu hình sai).
- **R4** Dừng sweep: không sinh trial mới, huỷ job của trial đang chạy (không thử lại).

## 5. Luồng chính
1. Người dùng chọn project, recipe, biến thể, không gian tìm kiếm, thuật toán, ngân sách, mục tiêu → `CreateSweep` (kiểm tham số theo recipe).
2. Bộ điều phối: đồng bộ trạng thái/giá trị trial từ run → cập nhật trial tốt nhất → sinh trial mới đến khi đủ `parallel`.
3. Đủ ngân sách và mọi trial xong → `completed`, thông báo M16 `sweep.completed` kèm trial tốt nhất.

## 6. Hợp đồng (`tuning.proto` — `TuningService`)
| RPC | Quyền | Mô tả |
|---|---|---|
| `CreateSweep` | `job.submit` | Lỗi đầu vào trả trước khi chạy trial nào |
| `ListSweeps` / `GetSweep` | `run.read` | GetSweep kèm bảng trial |
| `StopSweep` | `job.submit` | R4 |

## 7. Màn hình (phác thảo)
Danh sách sweep; tạo sweep (không gian tìm kiếm, thuật toán, ngân sách); trang sweep: bảng trial, parallel coordinates, biểu đồ metric tốt nhất theo thời gian, nút dừng/thêm ngân sách.

## 8. Sự kiện
`sweep.completed`, `sweep.failed` → M16 (người tạo sweep).

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `TUNE_SWEEP_NOT_FOUND` | NOT_FOUND | — |
| `TUNE_ALGORITHM_UNSUPPORTED` | INVALID_ARGUMENT | Thuật toán chưa hỗ trợ |
| `TUNE_GRID_NEEDS_VALUES` | INVALID_ARGUMENT | Grid cần mọi tham số dạng values |
| `VALIDATION_FAILED` (`space`) | INVALID_ARGUMENT | Tham số không có trong recipe / khoảng sai |

## 10. Nghiệm thu
- [x] Grid 3 tổ hợp → 3 run M09, chọn đúng trial tốt nhất theo metric chính (kịch bản M10).
- [x] Tham số lạ / grid với tham số liên tục bị từ chối trước khi chạy.
- [x] Dừng sweep → không sinh thêm trial, trial đang chạy bị huỷ.
- [ ] Bayesian (Optuna) hội tụ nhanh hơn random trên cùng ngân sách.
- [ ] ASHA dừng sớm trial kém theo metric trung gian.
- [ ] Ngân sách thời gian / GPU-giờ dừng sweep đúng hạn.
