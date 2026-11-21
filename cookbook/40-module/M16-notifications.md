# M16 — Thông báo

> **Phase:** P1 (in-app) · P2 (email, Slack, webhook) · **Phụ thuộc:** M01 · **Proto:** `notify.proto` (`NotificationService`) · **Schema DB:** `notify`

## 1. Mục tiêu & phạm vi
Báo cho đúng người khi có việc cần họ hoặc khi việc của họ xong/lỗi.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Trung tâm thông báo trong app, realtime, đánh dấu đã đọc | ✔ | | |
| Tuỳ chọn nhận theo loại | ✔ | | |
| Email giao dịch (xác minh, đặt lại mật khẩu, lời mời) — do M01 gửi qua SMTP (D-27) | ✔ | | |
| Email thông báo theo loại, Slack, webhook | | ✔ | |
| Tóm tắt hằng ngày | | ✔ | |

## 2. Khái niệm
**Notification** (gửi cho một người), **loại** (`kind`), **kênh** (`in_app` P1; `email/slack/webhook` P2), **tuỳ chọn** (người dùng bật/tắt theo loại).

## 3. Dữ liệu (schema `notify`)
| Bảng | Cột chính |
|---|---|
| `notifications` | id, workspace_id, user_id, kind, title, body, link, resource_uri, severity (`info/success/warning/error`), created_at, read_at |
| `preferences` | user_id, workspace_id, kind, channel, enabled |
| `channels` (P2) | id, workspace_id, kind (`slack/webhook/email`), config_enc, created_by |

Giữ 90 ngày.

## 4. Quy tắc — loại thông báo P1

| kind | Người nhận | Nguồn sự kiện |
|---|---|---|
| `labeling.assigned` | Annotator | `label.ItemsAssigned` (gộp: "Bạn được giao 50 item") |
| `labeling.rejected` | Annotator | `label.ItemRejected` |
| `labeling.lock_expiring` | Annotator | `label.LockExpiring` (chỉ in-app) |
| `labeling.project_completed` | Lead, Data Owner | `label.ProjectCompleted` |
| `job.failed` / `job.succeeded` | Người tạo job | M03 (succeeded chỉ cho job > 10 phút) |
| `run.finished` / `run.failed` | Người tạo run | M09 |
| `notebook.idle_warning` | Chủ notebook | M08 |
| `model.promoted` | Owner, Admin, người tạo model | M11 |
| `deployment.degraded` / `failed` | Người tạo deployment, Admin | M12 |
| `fleet.host_offline` / `host_doctor_failed` | Owner, Admin | `fleet.HostOffline` / `fleet.DoctorFindingFailed` |
| `catalog.recipe_verified` / `recipe_build_failed` | Người tạo recipe | M17 |
| `audit.integrity_broken` | Owner, Admin | M06 |
| `job.recovering` / `job.resumed` / `job.recovery_failed` | Người tạo job | M03 — chuyển máy / tiếp tục từ checkpoint ([20/10 §7](../20-kien-truc/10-quan-sat-va-khoi-phuc.md)) |
| `job.stalled` / `job.signal_lost` / `job.oom` | Người tạo job | Bộ phát hiện bất thường 20/10 §2.3; `job.oom` kèm nút tiếp tục |
| `host.disk_low` / `system.incident` | Owner, Admin (+ người có việc bị ảnh hưởng với `system.incident`) | 20/10 §2.3, §2.6 |
| `quota.storage_80` / `storage_95` | Owner, Admin | `workspace.StorageThresholdReached{percent}` (việc nền dọn rác hằng đêm + sau mỗi upload, 20/09 §5) |

- **R1** Gộp thông báo cùng loại cùng tài nguyên trong 5 phút.
- **R2** Thông báo chỉ chứa link, không chứa dữ liệu nhạy cảm.
- **R3** Người dùng tắt được từng loại, trừ `audit.integrity_broken`.

## 5. Luồng chính
Sự kiện domain → handler M16 xác định người nhận theo bảng → ghi `notifications` → đẩy qua `WatchNotifications` → chuông trên thanh trên tăng số.

## 6. Hợp đồng (`NotificationService`)
| RPC | Quyền | Mô tả |
|---|---|---|
| `ListNotifications` / `WatchNotifications` | chủ | |
| `MarkRead` / `MarkAllRead` | chủ | |
| `GetPreferences` / `UpdatePreferences` | chủ | |
| `ListChannels` / `CreateChannel` / `DeleteChannel` (P2) | Admin | |

## 7. Màn hình

### S-16-1 Trung tâm thông báo (panel thả xuống từ chuông)
```
┌ Thông báo                    [Đánh dấu đã đọc hết] ⚙ ┐
│ [Tất cả] [Chưa đọc 3]                                │
│ ● ✖ Run "rtdetrv4-l" thất bại · 5 phút  → Xem        │
│ ● ✎ Bạn được giao 50 item (Dự án PCB) · 1 giờ        │
│ ● ⚠ pc-03 offline · 2 giờ                            │
│   ✔ Model pcb-detector v7 lên production · hôm qua   │
└──────────────────────────────────────────────────────┘
```
⚙ mở S-16-2.

### S-16-2 Tuỳ chọn thông báo — `/w/{ws}/settings?tab=notifications`
Bảng: dòng = loại thông báo (nhóm: Job, Gán nhãn, Model, Máy, Hệ thống), cột = kênh (`Trong app` ở P1; `Email`, `Slack`, `Webhook` hiện mờ với nhãn P2). Mỗi ô một công tắc; nút `Bật tất cả` / `Tắt tất cả` theo nhóm. Admin thấy thêm tab con "Kênh của workspace" (P2). Hành động: `GetPreferences`, `UpdatePreferences`.

## 8. Sự kiện
Chỉ nghe sự kiện của module khác.

## 9. Lỗi
`NOTIFY_CHANNEL_INVALID` (P2, INVALID_ARGUMENT).

## 10. Nghiệm thu
- [ ] Reviewer trả lại item → Annotator thấy thông báo ≤ 2 s.
- [ ] 50 lần giao việc trong 5 phút gộp thành một thông báo.
- [ ] Tắt loại `job.succeeded` → không nhận nữa.
