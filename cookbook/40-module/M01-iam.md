# M01 — Workspace & IAM

> **Phase:** P1 · **Phụ thuộc:** — · **Quyết định:** D-16, D-27 · **Proto:** `iam.proto` (`IamService`, `WorkspaceService`) · **Schema DB:** `identity`

## 1. Mục tiêu & phạm vi
Cài đặt lần đầu, Instance Admin, người dùng, workspace, thành viên, vai trò, quyền theo tài nguyên, token truy cập, tích hợp (token HF, S3 ngoài) và email giao dịch.

| Năng lực | P1 | P2 | P3 |
|---|---|---|---|
| Cài đặt lần đầu (`/setup`), Instance Admin, bật/tắt tự đăng ký (D-27) | ✔ | | |
| Đăng ký/đăng nhập email + mật khẩu, quên mật khẩu, xác minh email — email qua SMTP, thiếu SMTP thì Admin copy link | ✔ | | |
| Đăng nhập Google/GitHub (OAuth2 + PKCE tự hiện thực) | ✔ | | |
| 2FA TOTP (tự chọn) | ✔ | bắt buộc theo chính sách | |
| Workspace cá nhân + team, mời thành viên | ✔ | | |
| 6 vai trò workspace + vai trò theo tài nguyên | ✔ | | |
| API key, service account | ✔ | | |
| Tích hợp workspace: token Hugging Face, kho S3 ngoài | ✔ | | |
| SSO doanh nghiệp (OIDC/SAML), SCIM | | ✔ | |

## 2. Khái niệm
- **Instance Admin**: cờ trên user (không thuộc workspace); quản lý cấu hình instance (SMTP, tự đăng ký, Ray Client bật/tắt), recipe builtin, Model Zoo dùng chung, cụm Ray. Người đầu tiên tạo qua `/setup` (D-27).
- **Vai trò workspace**: Owner, Admin, Editor, Reviewer, Annotator, Viewer.
- **Vai trò theo tài nguyên**: cộng thêm quyền trên một dataset/project/model (`owner`, `editor`, `viewer`). Không bớt quyền. **Data Owner** (D-16) = `owner` trên dataset → quyền `dataset.own`. Vai trò gán nhãn (Lead/Annotator/Reviewer) **chỉ lưu ở `label.project_members`** (M05); M01 đọc qua interface `ILabelingMembership` khi kiểm quyền.
- **Token**: `mlp_` API key người dùng, `mlpa_` máy, `mlpj_` job, `mlpe_` enroll, `mlpd_` key endpoint (chi tiết [bảo mật](../20-kien-truc/06-bao-mat.md)).

## 3. Dữ liệu (schema `identity`)

| Bảng | Cột chính |
|---|---|
| `users` | id, email (unique), email_verified_at, username (slug unique), display_name, avatar_uri, password_hash (PBKDF2 có version), totp_secret_enc, status (`active/disabled`), is_instance_admin, last_login_at |
| `instance_settings` | id = 1, name, allow_signup, ray_client_enabled, smtp jsonb (mật khẩu trong `ISecretBox`), bootstrap_token_hash, bootstrap_expires_at, bootstrapped_at |
| `email_outbox` | id, to, template, payload jsonb, link (cho Admin copy khi không có SMTP), status (`pending/sent/failed/manual`), attempts, sent_at |
| `integrations` | id, workspace_id, kind (`huggingface/s3`), name, config jsonb, secret_enc, created_by, last_checked_at, status |
| `external_logins` | user_id, provider (`google/github`), provider_key — unique (provider, provider_key) |
| `sessions` | id, user_id, created_at, last_seen_at, ip, user_agent, revoked_at |
| `workspaces` | id, slug (unique), name, kind (`personal/team`), owner_id, default_visibility (`private/workspace`), settings jsonb |
| `memberships` | workspace_id, user_id, role, joined_at, invited_by — pk (workspace_id, user_id) |
| `invitations` | id, workspace_id, email, role, token_hash, expires_at (7 ngày), status |
| `resource_roles` | workspace_id, resource_type (`dataset/project/model`), resource_id, user_id, role (`owner/editor/viewer`) |
| `api_keys` | id, workspace_id, owner_type (`user/service_account`), owner_id, name, prefix, hash, scopes text[], expires_at, last_used_at, last_used_ip, revoked_at |
| `service_accounts` | id, workspace_id, name, role, created_by |

## 4. Trạng thái & quy tắc

### 4.1 Ma trận quyền

| Permission | Owner | Admin | Editor | Reviewer | Annotator | Viewer |
|---|:-:|:-:|:-:|:-:|:-:|:-:|
| `workspace.manage` | ✔ | | | | | |
| `member.manage` | ✔ | ✔¹ | | | | |
| `fleet.manage` | ✔ | ✔ | | | | |
| `fleet.read` | ✔ | ✔ | ✔ | | | ✔ |
| `dataset.write` / `dataset.delete` | ✔ | ✔ | ✔ | | | |
| `dataset.own` (duyệt export, đổi visibility, giao quyền trên dataset) | ✔ | ✔ | ³ | | | |
| `integration.manage` | ✔ | ✔ | | | | |
| `dataset.read` | ✔ | ✔ | ✔ | ✔² | ✔² | ✔ |
| `labeling.manage` | ✔ | ✔ | ✔ | | | |
| `labeling.annotate` | ✔ | ✔ | ✔ | ✔² | ✔² | |
| `labeling.review` | ✔ | ✔ | ✔ | ✔² | | |
| `job.submit` / `job.cancel` | ✔ | ✔ | ✔ | | | |
| `notebook.use` | ✔ | ✔ | ✔ | | | |
| `run.write` | ✔ | ✔ | ✔ | | | |
| `run.read` | ✔ | ✔ | ✔ | | | ✔ |
| `model.write` | ✔ | ✔ | ✔ | | | |
| `model.promote_production` | ✔ | ✔ | | | | |
| `deployment.manage` | ✔ | ✔ | ✔ | | | |
| `audit.read` | ✔ | ✔ | | | | |

¹ Không đổi/xoá Owner hay Admin khác. ² Chỉ trong labeling project được thêm vào và chỉ item được giao / cần review ([M05](M05-labeling.md)). ³ Khi có `resource_roles.owner` trên dataset đó (Data Owner).

### 4.2 Quy tắc
- **R1** Mỗi workspace đúng 1 Owner; chuyển Owner cần người nhận xác nhận.
- **R2** Không tự hạ vai trò nếu là Owner cuối cùng.
- **R3** Xoá thành viên → thu hồi API key của họ trong workspace, trả item labeling đang khoá về hàng đợi, dừng notebook của họ.
- **R4** API key hiển thị một lần; scope ⊆ quyền chủ sở hữu **tại thời điểm dùng**.
- **R5** Không có quyền đọc → `NOT_FOUND`; đọc được nhưng không được ghi → `PERMISSION_DENIED` nêu rõ quyền thiếu.
- **R6** Đổi vai trò có hiệu lực với request tiếp theo (cache xác thực tối đa 30 s, thu hồi xoá cache ngay).
- **R7** `/setup` chỉ mở khi chưa có Instance Admin và token bootstrap còn hạn; sau khi tạo xong, token bị vô hiệu.
- **R8** Email: không có SMTP → `email_outbox.status = manual`. **Lời mời:** `CreateInvitation` trả link trong response và S-01-3 hiện nút `Copy link` cho Owner/Admin của workspace. **Xác minh email, đặt lại mật khẩu:** link nằm ở S-01-9 tab emails (Instance Admin). Link hết hạn như email.
- **R9** `allow_signup = false` → chỉ đăng ký được qua lời mời.
- **R10** Secret tích hợp không bao giờ trả về client; job nhận qua env (`HF_TOKEN`, `AWS_*`) khi recipe/dataset cần.

## 5. Luồng chính
0. **Cài đặt lần đầu**: xem [20/09 §1.1](../20-kien-truc/09-van-hanh.md) → S-01-8.
1. **Đăng ký**: email + mật khẩu → mail xác minh (R8) → tạo user + workspace cá nhân (slug = username).
2. **OAuth**: `/oauth/{provider}/start` → nhà cung cấp → `/oauth/{provider}/callback` → khớp `external_logins` hoặc email đã xác minh → tạo phiên.
3. **Mời**: Admin nhập email + vai trò → mail chứa link → người nhận đăng nhập/đăng ký → tạo membership.
4. **Tạo API key**: chọn tên, scope preset (`full`, `read-only`, `sdk-tracking`, `labeling`), hạn (mặc định 90 ngày) → hiển thị key một lần.
5. **Kiểm quyền mỗi RPC**: chủ thể → workspace từ tên tài nguyên → vai trò + resource_roles → permission → `AuthorizationBehavior`.

## 6. Hợp đồng (`iam.proto`)

| RPC | Quyền tối thiểu | Mô tả |
|---|---|---|
| `IamService.Register` / `Login` / `Logout` | khách / phiên | Đăng ký, đăng nhập (đặt cookie), đăng xuất |
| `IamService.VerifyEmail` / `RequestPasswordReset` / `ResetPassword` | khách | |
| `IamService.EnrollTotp` / `ConfirmTotp` / `DisableTotp` | phiên | 2FA |
| `IamService.GetMe` / `UpdateMe` | phiên | Hồ sơ + danh sách workspace + vai trò |
| `IamService.ListSessions` / `RevokeSession` | phiên | Phiên đăng nhập của mình |
| `WorkspaceService.ListWorkspaces` / `CreateWorkspace` / `GetWorkspace` / `UpdateWorkspace` / `DeleteWorkspace` | phiên / `workspace.manage` | |
| `WorkspaceService.GetSummary` / `WatchWorkspaceSummary` | thành viên | Số liệu trang Tổng quan |
| `WorkspaceService.ListMembers` / `UpdateMemberRole` / `RemoveMember` | `member.manage` (xem: thành viên) | |
| `WorkspaceService.CreateInvitation` / `RevokeInvitation` / `AcceptInvitation` | `member.manage` / người được mời | |
| `WorkspaceService.ListResourceRoles` / `GrantResourceRole` / `RevokeResourceRole` | `member.manage` hoặc `labeling.manage` (cho labeling) | |
| `WorkspaceService.ListApiKeys` / `CreateApiKey` / `RevokeApiKey` | chủ key; Admin xem tất cả | |
| `WorkspaceService.ListServiceAccounts` / `CreateServiceAccount` / `DeleteServiceAccount` | `member.manage` | |
| `WorkspaceService.GetMyPermissions` | thành viên | Danh sách permission để UI ẩn/hiện |
| `WorkspaceService.ListIntegrations` / `CreateIntegration` / `UpdateIntegration` / `DeleteIntegration` / `TestIntegration` | `integration.manage` (xem tên: thành viên) | Token HF, S3 ngoài |
| `InstanceService.GetSetupState` / `CompleteSetup` | khách + token bootstrap | `/setup` |
| `InstanceService.GetSettings` / `UpdateSettings` / `SendTestEmail` | Instance Admin | SMTP, tự đăng ký, Ray Client |
| `InstanceService.ListUsers` / `SetInstanceAdmin` / `DisableUser` | Instance Admin | |
| `InstanceService.ListManualEmails` | Instance Admin | Link chờ copy khi không có SMTP |

## 7. Màn hình

### S-01-1 Đăng nhập — `/login`
```
┌──────────── MLPlatform ────────────┐
│ Email      [                     ] │
│ Mật khẩu   [                     ] │
│ [Đăng nhập]          Quên mật khẩu? │
│ ───────────── hoặc ──────────────── │
│ [G Tiếp tục với Google]             │
│ [  Tiếp tục với GitHub]             │
│ Chưa có tài khoản? Đăng ký          │
└─────────────────────────────────────┘
```
Sau mật khẩu đúng mà bật 2FA → ô mã 6 số. Lỗi: "Email hoặc mật khẩu không đúng" (không phân biệt hai trường hợp). Hành động: `Login`, OAuth start.

### S-01-2 Hồ sơ — `/w/{ws}/settings?tab=profile`
Trường: tên hiển thị, username (khoá), ảnh đại diện, email (đổi qua mã xác minh), đổi mật khẩu, 2FA (bật/tắt + QR), tài khoản liên kết Google/GitHub, phiên đăng nhập (bảng: thiết bị, IP, lần cuối, `Thu hồi`). Hành động: `UpdateMe`, `EnrollTotp`, `RevokeSession`.

### S-01-3 Thành viên — `/w/{ws}/settings?tab=members`
```
┌ Thành viên (12) ──────────────────────────────────── [Tìm…] [+ Mời thành viên] ┐
│ Người dùng           │ Vai trò      │ Vai trò theo tài nguyên │ Tham gia │ ⋯    │
│ an@…  (bạn)          │ Owner        │ —                        │ 01/11    │      │
│ binh@…               │ [Editor   ▾] │ Lead: Dự án PCB          │ 03/11    │ ⋯    │
│ chi@…                │ [Annotator▾] │ —                        │ 05/11    │ ⋯    │
│ Lời mời đang chờ: dung@… (Reviewer) – hết hạn 7 ngày   [Thu hồi]                │
└─────────────────────────────────────────────────────────────────────────────────┘
[Xem ma trận quyền]  → bảng mục 4.1
```
Đổi vai trò ngay trong ô (`UpdateMemberRole`); `⋯` → Xoá khỏi workspace (`ConfirmDialog` nêu R3). Chỉ Owner/Admin thấy trang này.

### S-01-4 API key — `/w/{ws}/settings?tab=api-keys`
Bảng: tên, prefix (`mlp_3f9a…`), scope, hạn, lần dùng cuối + IP, `Thu hồi`. `+ Tạo key` → `FormDialog` (tên, scope preset, hạn) → hộp hiển thị key một lần với `CommandBlock` + cảnh báo. Admin có thêm tab "Tất cả key" và "Service account".

### S-01-5 Workspace — `/w/{ws}/settings?tab=workspace`
Tên, slug (đổi được, cảnh báo link cũ), visibility mặc định, chuyển Owner, xoá workspace (gõ slug để xác nhận).

### S-01-6 Chuyển / tạo workspace — menu ở góc trái app shell + `/workspaces/new`
Menu thả xuống: danh sách workspace (cá nhân trước, team sau, ô tìm), `+ Tạo workspace`. Trang tạo: tên, slug (tự sinh, kiểm trùng ngay), loại team. Hành động: `ListWorkspaces`, `CreateWorkspace`; tạo xong chuyển tới `/w/{slug}/home`.

### S-01-7 Nhận lời mời — `/invite/{token}`
Chưa đăng nhập → thẻ "<người mời> mời bạn vào <workspace> với vai trò <role>" + `Đăng nhập` / `Đăng ký` (email điền sẵn, khoá). Đã đăng nhập đúng email → `Tham gia` (`AcceptInvitation`) → `/w/{ws}/home`. Sai email → thông báo + `Đăng xuất`. Hết hạn → `IAM_INVITATION_EXPIRED`.

### S-01-8 Cài đặt lần đầu — `/setup?token=…`
Ba bước: (1) Tài khoản Instance Admin (email, tên, mật khẩu); (2) Instance (tên, cho phép tự đăng ký, SMTP tuỳ chọn + `Gửi email thử`); (3) Kiểm tra hệ thống (Postgres, kho S3, registry, Ray head, WireGuard — mỗi dòng ok/lỗi + hướng dẫn). `Hoàn tất` → `CompleteSetup` → đăng nhập → tạo workspace (S-01-6).

### S-01-9 Quản trị instance — `/admin?tab=settings|users|emails`
Chỉ Instance Admin. **settings**: tên, tự đăng ký, SMTP, Ray Client bật/tắt (cảnh báo D-24). **users**: bảng người dùng (email, workspace, Instance Admin, trạng thái, `Vô hiệu`). **emails**: link chờ copy khi không có SMTP (người nhận, loại, hạn, `Copy link`). Recipe builtin và Model Zoo dùng chung nằm ở M17 (S-17-5).

### S-01-10 Tích hợp — `/w/{ws}/settings?tab=integrations`
```
┌ Tích hợp ────────────────────────────────────────────── [+ Thêm tích hợp ▾] ┐
│ Hugging Face  hf-main    token ••••a91c   ✔ hợp lệ (kiểm 10:02)  [Kiểm] [⋯] │
│ S3            s3-lab     bucket lab-data · ap-southeast-1   ✔      [Kiểm] [⋯] │
└──────────────────────────────────────────────────────────────────────────────┘
```
Thêm: HF (token, tuỳ chọn tổ chức) · S3 (endpoint, bucket, region, access key, secret). `Kiểm` gọi `TestIntegration` (HF: whoami; S3: list 1 object). Dùng ở: tải trọng số gated (M17), dataset từ S3 (M04), import model HF (M11).

## 8. Sự kiện
`identity.InstanceBootstrapped`, `UserRegistered`, `IntegrationChanged`, `MemberAdded`, `MemberRoleChanged`, `MemberRemoved` (M05 trả item, M08 dừng notebook nghe), `ApiKeyCreated`, `ApiKeyRevoked`, `WorkspaceDeleted`.

## 9. Lỗi
| reason | status | Thông điệp |
|---|---|---|
| `IAM_EMAIL_TAKEN` / `IAM_USERNAME_TAKEN` | ALREADY_EXISTS | Email/username đã được dùng |
| `IAM_INVALID_CREDENTIALS` | UNAUTHENTICATED | Email hoặc mật khẩu không đúng |
| `IAM_EMAIL_NOT_VERIFIED` | FAILED_PRECONDITION | Cần xác minh email |
| `IAM_TOTP_REQUIRED` / `IAM_TOTP_INVALID` | UNAUTHENTICATED | Nhập mã 2FA / mã sai |
| `IAM_LAST_OWNER` | FAILED_PRECONDITION | Workspace cần ít nhất một Owner |
| `IAM_INVITATION_EXPIRED` | FAILED_PRECONDITION | Lời mời đã hết hạn |
| `IAM_SCOPE_EXCEEDS_ROLE` | PERMISSION_DENIED | Scope vượt quyền của bạn |
| `IAM_TOKEN_REVOKED` | UNAUTHENTICATED | Token đã bị thu hồi |
| `IAM_SETUP_DONE` | FAILED_PRECONDITION | Instance đã được cài đặt |
| `IAM_SIGNUP_DISABLED` | PERMISSION_DENIED | Chỉ đăng ký được qua lời mời |
| `IAM_INTEGRATION_INVALID` | FAILED_PRECONDITION | Token/khoá tích hợp không hợp lệ |

## 10. Nghiệm thu
- [ ] Annotator gọi `DataService.ListItems` ngoài phạm vi được giao nhận `NOT_FOUND`.
- [ ] Hạ Editor → Viewer: API key của người đó bị từ chối `SchedulerService.SubmitJob` trong ≤ 30 s, không cần đăng nhập lại.
- [ ] API key chỉ hiện một lần; DB chỉ có hash; log không chứa key.
- [ ] Xoá thành viên trả item đang khoá về hàng đợi ≤ 5 s và dừng notebook của họ.
- [ ] Annotator đăng nhập thấy đúng một mục sidebar "Gán nhãn — Việc của tôi".
- [ ] Mọi thay đổi thành viên/vai trò/key có bản ghi audit.
- [ ] Stack mới: token bootstrap trong log `api` → `/setup` tạo Instance Admin; mở lại `/setup` nhận `IAM_SETUP_DONE`.
- [ ] Không cấu hình SMTP: Admin workspace mời thành viên → `Copy link` ở S-01-3; người nhận dùng link tham gia được. Người dùng quên mật khẩu → link nằm ở S-01-9 tab emails cho Instance Admin.
- [ ] Có SMTP (MailHog trong stack test): email xác minh tới hộp thư, link hoạt động một lần.
- [ ] Token HF lưu mã hoá; `ListIntegrations` không trả secret; job recipe gated nhận `HF_TOKEN` trong env.
