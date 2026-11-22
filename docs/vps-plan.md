# Kế hoạch: chuyển MLPlatform lên VPS `cenos` (prod + dev)

Trạng thái: **bản 3 — đã chốt mục 10, trừ câu GPU.** Chưa thay đổi gì trên server.

## 0. Quyết định đã chốt

| # | Quyết định |
|---|---|
| D1 | prod và dev có **database riêng** (cùng Postgres trên `cenos`, khác database và user) |
| D2 | Bộ cài phải chạy được **trọn vẹn trên một PC** (control plane + agent) và được kiểm chứng tự động |
| D3 | Domain dev: `mlp-dev.hexbox.vn` (+ `*.mlp-dev.hexbox.vn`) |
| D4 | Gỡ `ollama` |
| D5 | Deploy bằng **ArgoCD đã có**, repo `dudangphuoc/gitops-org`; chỉnh ArgoCD cho nhẹ hơn |
| D6 | Runner phải nhẹ nhất: không có gì chạy ngầm khi không có job |

Fleet vẫn dùng chung cho hai môi trường. Với DB riêng, nghĩa là **mỗi máy chạy hai agent** (mục 3).

## 1. Hiện trạng (đo ngày 2026-11-22)

**Stack MLP**
- `mlp.hexbox.vn` → Traefik trên `cenos` → overlay `10.88.255.1` → stack docker trên **máy Mac**.
- DB `MLPlatformDb` và MinIO đã nằm trên `cenos`.
- Agent nối qua `agent.mlp.hexbox.vn:443` (TLS passthrough).
- Hub WireGuard `mlp-net-hub` chạy trong k3s, namespace `mlp`.

**Tài nguyên trên `cenos`**

| | |
|---|---|
| CPU | Intel N100, 4 nhân |
| RAM | 30 GB, trống khoảng 15 GB |
| Disk | 70 GB, chỉ còn **19 GB** |
| GPU | không có |
| CPU requests | **91%** |

Ứng viên giải phóng tài nguyên:

| Thành phần | Chiếm |
|---|---|
| `ollama` | requests 1 CPU / 4 GiB; model 2,8 GB tại `/home/raiden/k3s/storage/ollama/models` |
| `open-webui` | requests 250m / 512 MiB; chỉ trỏ vào `ollama`, gỡ ollama thì nó vô dụng |

**ArgoCD**
- Mô hình app-of-apps: `gitops-root-app` đọc thư mục gốc repo, mỗi app là một Helm chart trong `apps/<tên>`.
- Project `production-project`, sync tự động (prune + selfHeal).
- Image tag do "GitOps Bot" commit từ CI của từng repo.
- Secret (ví dụ `lottery-db`) tạo ngoài Git.
- 7 thành phần ArgoCD, **không có requests/limits**. `application-controller` dùng khoảng 576 MB.
- `dex` (SSO), `notifications`, `applicationset` không có việc gì: không có ApplicationSet nào.
- App `lottery-client` đang `OutOfSync`.

**Runner (ARC v2, `arc-systems`)**
- 7 scale set, mỗi repo một set, `min 0 / max 5`, mỗi runner kèm `docker:dind`, không có requests/limits, image `latest`.
- Lúc nhàn rỗi vẫn có **1 controller + 7 listener** chạy liên tục.
- Lúc có job, runner và dind không được giới hạn, có thể tới 35 cái cùng lúc trên 4 nhân. Khi node đã giữ 91% CPU, pod runner bị kẹt Pending: đây là lý do "runner không khởi động được".
- Mỗi job dind bắt đầu với cache rỗng, nên tải lại image, NuGet, npm từ đầu.
- ARC v1 (summerwind) còn sót CRD và Service trong `actions-runner-system`.

## 2. Kiến trúc đích

```
GitHub (repo mlp) ──push──▶ GitHub-hosted runner: gates → build image → push GHCR
                                    │ commit tag (GitOps Bot)
                                    ▼
                      gitops-org: apps/mlp (prod), apps/mlp-dev (dev)
                                    │ ArgoCD sync
                                    ▼
cenos k3s ─ ns mlp      : control (api + ray-head + wireguard), web, agent-builder  → DB MLPlatformDb
          └ ns mlp-dev  : control (api + ray-head + wireguard), web, agent-builder  → DB MLPlatformDevDb
Fleet: mỗi máy chạy agent "prod" + agent "dev" (hai service, hai overlay, chung Docker)
```

**Không có runner tự host nào cho MLP.** Build chạy trên GitHub-hosted (tạo khi có job, xong thì huỷ). Deploy do ArgoCD kéo từ Git.

## 3. Hai môi trường, DB riêng, Fleet chung

Với DB riêng, hai control plane hoàn toàn độc lập: không cần `MLP__Role`, không có vòng lặp chạy trùng, migration của dev chỉ đụng DB dev.

**Fleet chung = hai agent trên một máy.** Code đã có sẵn phần quan trọng nhất: mỗi agent gắn nhãn `mlp.agent=<hostId>` lên container của mình và chỉ quản lý container mang nhãn của nó (`HostOwnership`). Cần bổ sung:

| Việc | Hiện tại | Cần |
|---|---|---|
| Tên bản cài | đường dẫn cố định: `/opt/mlplatform-agent`, `/var/lib/mlplatform-agent`, service `mlplatform-agent`; Windows `C:\ProgramData\MLPlatform`, task cố định | tham số `--instance dev`: đường dẫn, service hoặc task, thư mục Docker đều có hậu tố `-dev`. Lệnh cài sinh từ control plane tự kèm tham số |
| Overlay WireGuard | mỗi control plane một hub, dải `10.88.0.0/16` | dev dùng dải `10.89.0.0/16`, interface và port riêng, để hai đường hầm trên một máy không đụng nhau |
| Container / mạng Docker / cache dữ liệu | tên có thể trùng | thêm tiền tố môi trường cho tên container, network, thư mục cache |
| GPU | một GPU | hai môi trường **tranh nhau GPU**: job dev có thể làm job prod chờ. Chấp nhận, hoặc đặt chính sách sau (ví dụ dev chỉ chạy khi máy rảnh) |

**Kiểm chứng:** một scenario mới chạy hai control plane (prod, dev) và **một** máy cài hai agent. Job của mỗi bên chạy xong mà không đụng container của bên kia. Restart agent dev không ảnh hưởng attempt prod.

## 4. Cài đặt trọn vẹn trên một PC (D2)

Mục tiêu: một lệnh dựng đủ control plane, DB, S3 và agent trên một máy sạch (Linux, hoặc macOS/Windows có Docker), không lỗi.

- `deploy/compose` hiện thiếu Postgres và MinIO (đang dùng DB và S3 bên ngoài). Thêm profile `standalone` gồm postgres + minio + tạo bucket. Verify stack đã có sẵn phần này, chỉ cần chuyển sang.
- Script `deploy/install/server.sh`:
  1. kiểm tra Docker, RAM, disk, port;
  2. sinh `.env` (mật khẩu ngẫu nhiên, admin);
  3. `compose up` từng service một;
  4. chờ health;
  5. in URL đăng nhập và lệnh cài agent cho chính máy này.
- **Kiểm chứng tự động:** job CI `install-smoke` trên GitHub-hosted:
  1. máy Ubuntu sạch chạy `server.sh`, cài agent trên cùng máy;
  2. chạy bộ scenario hiện có;
  3. dọn sạch rồi chạy `server.sh` lần hai: phải không lỗi (cài lặp lại an toàn).

  Chạy khi đổi `deploy/**` và mỗi đêm.
- Cũng dùng cho môi trường trên k8s: cùng image, cùng biến môi trường. Chỉ khác cách khai báo (Helm chart).

## 5. Bố trí trên k3s (Helm chart trong gitops-org)

Chart `apps/mlp` dùng chung, `values` khác nhau cho prod và dev:

| Thành phần | Ghi chú | prod requests / limits | dev requests / limits |
|---|---|---|---|
| pod `control`: api + ray-head + sidecar WireGuard | chung network namespace như compose: api gọi Ray ở `127.0.0.1:8265`, ray-head quảng bá IP overlay | api 200m/1 · 256Mi/512Mi; ray 250m/1 · 1,5Gi/2Gi | api 50m/500m · 128Mi/256Mi; ray 100m/500m · 1Gi/1,5Gi |
| web (nginx) | | 10m/200m · 16Mi/64Mi | 10m/100m · 16Mi/64Mi |
| agent-builder | build agent cho máy mới cắm vào; PVC cache khoảng 2 GB | 0/1 · 64Mi/1Gi | 0/500m · 64Mi/512Mi |
| Job `db-migrate` | ArgoCD hook `PreSync` | — | — |
| Hub WireGuard | đã có (`mlp-net-hub`, dải `10.88`) | — | thêm hub `10.89` |

- Đặt rõ `ApiAddressForRay` / `ApiAddressForWorkloads` theo IP overlay của mỗi môi trường. Mặc định `10.88.0.1` là IP của hub, sai.
- **prod giữ nguyên cert kênh agent và khoá WireGuard:** chép `pfx` và `wg-state` từ volume trên Mac vào Secret, để agent hiện có không phải cài lại. Dev sinh mới.
- Code Python của ray-head và recipes builtin đang mount từ máy Mac. Phải đóng vào image.
- Secret (DB, S3, admin, token peer) tạo ngoài Git như các app khác. Danh sách khoá cần bổ sung sẽ được liệt kê trong `apps/mlp/README.md`.
- Ingress:
  - prod: giữ `mlp.hexbox.vn`, `*.mlp.hexbox.vn`, `agent.mlp.hexbox.vn`;
  - dev: `mlp-dev.hexbox.vn`, `*.mlp-dev.hexbox.vn`, `agent.mlp-dev.hexbox.vn`; cert mới qua cert-manager.
- Gỡ route `api.mlp.hexbox.vn` (không cần nữa).

Tài nguyên: MLP thêm khoảng 0,7 CPU requests cho hai môi trường. Gỡ `ollama` + `open-webui` trả lại 1,25 CPU, 4,5 GiB RAM và 2,8 GB disk.

## 6. CI/CD — không runner tự host

Mỗi image được đánh tag theo commit sha.

| Bước | Ở đâu | Kích hoạt |
|---|---|---|
| gates + AOT + build + push GHCR | GitHub-hosted `ubuntu-24.04` | push `develop` → tag `dev-<sha>`; push `main` → tag `<sha>` |
| cập nhật tag | cùng job: commit `values-dev.yaml` / `values-prod.yaml` vào `gitops-org` bằng deploy key (giống GitOps Bot hiện có) | sau khi push image xong |
| deploy | ArgoCD tự sync. Dev sync tự động; prod sync tự động hoặc bấm tay (chủ dự án chọn) | — |

- Cache build bằng `actions/cache` (NuGet, npm) và cache BuildKit `type=gha`: build lần sau nhanh hơn nhiều so với dind cache rỗng.
- **Dung lượng GHCR:** dọn tag cũ hằng tuần, giữ 10 tag gần nhất mỗi image.
- **Phút GitHub-hosted:** repo private có hạn mức phút miễn phí mỗi tháng. Đo thời gian một lần chạy và báo lại trước khi tắt runner tự host của các repo khác.

## 7. Runner nhẹ nhất cho các repo khác (D6)

Không có runner tự host thì không tốn gì khi nhàn rỗi. Đề xuất theo thứ tự ưu tiên:

1. **Chuyển hết sang GitHub-hosted + GitOps Bot** (như MLP). Sau đó gỡ toàn bộ ARC:
   - controller + 7 listener;
   - CRD v2;
   - phần sót của v1.
   - Tài nguyên nhàn rỗi về 0, không còn pod runner Pending.
2. **Repo nào bắt buộc tự host** (cần mạng nội bộ của cluster): giữ **một** scale set cho repo đó với:
   - `maxRunners: 1`, image ghim phiên bản;
   - **không dind** (build image bằng BuildKit rootless hoặc build trên GitHub-hosted);
   - requests nhỏ (100m/256Mi) để luôn xếp lịch được, limits 2 CPU / 3 GiB;
   - `priorityClass` thấp, để không đẩy dịch vụ prod.

   Listener của ARC vẫn chạy ngầm (khoảng 12 MB/listener): đây là cái giá tối thiểu nếu còn tự host.
3. Gom repo vào một GitHub **Organization** (nếu muốn) để một scale set phục vụ nhiều repo, thay vì mỗi repo một set.

Danh sách repo đang dùng ARC: `agentic-assistant`, `lottery-system`, `lottery-system-client`, `maestro-agent`, `HermesAgent`, `podcast-backend`, `podcast-frontend`. Phải kiểm tra từng workflow (`runs-on`) trước khi gỡ.

## 8. Chỉnh ArgoCD trên `cenos`

- Thêm requests/limits cho 7 thành phần, để ArgoCD không lấn dịch vụ khác và cũng không bị đẩy ra.
- Tắt `dex` nếu không dùng SSO; đăng nhập admin vẫn giữ.
- Tắt `applicationset-controller` (không có ApplicationSet); tắt `notifications` nếu không cấu hình thông báo.
- `application-controller`: tăng `timeout.reconciliation` lên 5 phút. Thay vì hỏi Git liên tục, dùng **webhook GitHub → ArgoCD** để sync ngay khi có commit.
- Sửa app `lottery-client` đang `OutOfSync`.
- Thêm `AppProject` `mlp`: chỉ được deploy vào `mlp` và `mlp-dev`, chỉ đọc repo `gitops-org`.

ArgoCD được cài bằng gì (Helm hay manifest) cần xác nhận trước khi chỉnh, để thay đổi không bị ghi đè.

## 9. Các bước thực hiện

| # | Việc | Nơi | Kiểm chứng |
|---|---|---|---|
| 0 | **Bảo mật:** remote git của repo mlp chứa GitHub PAT trong URL → thu hồi token, đổi sang `gh auth` | Mac, GitHub | `git remote -v` sạch |
| 1 | Backup `MLPlatformDb`; tạo `MLPlatformDevDb` + user riêng | `cenos` | dump khôi phục được vào DB tạm |
| 2 | Gỡ `ollama` + `open-webui` (manifest, PV, ingress `ollama.hexbox.vn`); xoá model 2,8 GB sau khi xác nhận | `cenos` | requests node giảm khoảng 30% |
| 3 | Code: `--instance` cho agent (Linux/macOS/Windows), dải overlay cấu hình được, tiền tố tên container/network | repo mlp | scenario hai control plane + một máy hai agent |
| 4 | Code: profile `standalone` + `server.sh` + job CI `install-smoke` | repo mlp | CI xanh trên máy Ubuntu sạch, chạy hai lần liên tiếp |
| 5 | Image: đóng ray-head Python + recipes; CI build cho `develop` và `main`, cache, commit tag sang gitops | repo mlp | image `dev-<sha>` trên GHCR |
| 6 | Chart `apps/mlp` + `mlp.yaml` / `mlp-dev.yaml` (Application) + AppProject; danh sách Secret cần tạo | gitops-org | `helm template` + `kubectl apply --dry-run=server` |
| 7 | Dựng **dev** (DB, Secret, hub `10.89`, cert) → ArgoCD sync | `cenos` | đăng nhập `mlp-dev.hexbox.vn`; cài agent dev trên một máy; chạy job toy |
| 8 | **Chuyển prod:** dừng stack Mac → Secret với cert/khoá cũ → ArgoCD sync → đổi EndpointSlice sang pod | `cenos` | mọi máy online lại, không cài lại agent; health 200; job toy chạy xong |
| 9 | Chỉnh ArgoCD (mục 8) | `cenos` | các app vẫn `Synced`/`Healthy` |
| 10 | Runner các repo khác (mục 7), từng repo một, có đồng ý | GitHub, `cenos` | workflow xanh trên GitHub-hosted, rồi gỡ ARC |

**Khôi phục nếu lỗi ở bước 8:** bật lại stack trên Mac và trả EndpointSlice về `10.88.255.1`. Agent tự nối lại, vì cert và khoá không đổi.

## 10. Quyết định bổ sung (2026-11-22)

| # | Câu hỏi | Chốt |
|---|---|---|
| 1 | Prod sync | **tự động** (prune + selfHeal như các app khác) |
| 2 | GPU dùng chung | **(b) prod ưu tiên:** agent dev chỉ nhận job GPU khi trên máy không có container GPU nào của agent prod. Làm cùng đợt nâng cấp agent (terminal, files, process, giám sát GPU) |
| 3 | Secret | **tạo tay trên `cenos` từ file env** (`kubectl create secret … --from-env-file`). Repo có `apps/mlp/secrets.example.env` liệt kê khoá |
| 4 | Commit tag sang gitops | **GitOps Bot** (cùng cơ chế các repo khác) |
| 5 | Runner các repo khác | **làm luôn** |

### Khảo sát workflow 7 repo dùng ARC

Cả 7 workflow chỉ làm hai việc:
- `build-and-push` (docker build → GHCR);
- `update-gitops` (commit tag sang `gitops-org`).

Không bước nào cần mạng nội bộ cluster (không `kubectl`, không địa chỉ `10.43.*`/`*.svc`). → Chuyển được hết sang `ubuntu-24.04` chỉ bằng cách đổi `runs-on`, thêm cache `type=gha` cho build.

| Repo | Nhánh kích hoạt | Ghi chú |
|---|---|---|
| dudangphuoc/agentic-assistant | `main` | app đang scale 0 |
| phuc-nguyenit49/lottery-system | `prod`, tag `v*` | repo của owner khác (có quyền push) |
| phuc-nguyenit49/lottery-system-client | `prod` | repo của owner khác |
| dudangphuoc/maestro-agent | `main` | |
| dudangphuoc/HermesAgent | `prod` (mặc định) | build arg lấy từ secrets |
| dudangphuoc/podcast-backend | `prod` | mặc định `main` |
| dudangphuoc/podcast-frontend | `prod` | mặc định `main` |

**Đã làm (2026-11-22):** 7 repo đã chuyển `runs-on: ubuntu-24.04`, thêm `setup-buildx` + cache `type=gha` (trừ HermesAgent, maestro-agent, podcast-backend đang cố ý đặt `no-cache: true`, giữ nguyên). Commit dùng `[skip ci]` nên không kích hoạt build. Chưa gỡ ARC.

Commit đổi workflow dùng `[skip ci]` để không kích hoạt build/deploy ngoài ý muốn. Sau đó gỡ ARC:
- 7 scale set + controller (`arc-systems`);
- CRD v2;
- phần sót v1 (`actions-runner-system` + CRD `summerwind`).
