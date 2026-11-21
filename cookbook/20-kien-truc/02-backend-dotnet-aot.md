# 20/02 — Backend .NET 10 Native AOT (không EF Core)

Quyết định nền: D-07, D-08, D-09, D-10, D-14. Skeleton tham khảo: Cortext (`/Users/raiden/Workspace/2026/b-platform`).

## 1. Lấy gì từ Cortext, đổi gì

| Hạng mục | Cortext | MLPlatform | Lý do |
|---|---|---|---|
| Chia tầng | Framework / Sdk / Business / Hosts / tools; Domain → Application ← Infrastructure; Host chỉ ráp | Giữ; bỏ tầng Business nạp động (module là thư mục trong ControlPlane) | Không cần gói động |
| CQRS | `Cortext.Cqrs` tự viết, handler `ValueTask`, behaviors, exception có kiểu | Giữ API; **đăng ký + dispatch bằng source generator** | `Assembly.GetTypes()` không AOT |
| Truy cập DB | EF Core qua `IReadDb<T>`/`IWriteDb<T>` | **Npgsql** qua `IReadSession`/`IWriteSession`; SQL trong file `.sql` + generator | D-08 |
| Schema | Script `db/<chủ>/yyyyMMddHHmm_<slug>.sql` + `down/`, lint, CLI | Giữ, CLI `mlp db …` | Đã chạy tốt |
| API | MVC Controllers | **gRPC + gRPC-Web** từ `.proto` | D-10, MVC không AOT |
| Serialization | System.Text.Json | **Protobuf**; STJ source-gen chỉ cho API bên thứ ba | D-10 |
| Bus | CAP + RabbitMQ | **Outbox Postgres** + dispatcher trong tiến trình | D-14 |
| Log | Serilog đọc cấu hình | **Microsoft.Extensions.Logging** (JSON) + **OpenTelemetry** | Reflection |
| Auth | JWT HS256 | Cookie (UI) + **token mờ có hash** | Thu hồi tức thì, không cần IdentityModel |
| Bí mật | `ISecretBox` AES-GCM | Giữ | — |
| Thời gian | `IClock` | Giữ (bọc `TimeProvider`) | — |
| Gate | BannedSymbols, SqlGate, CommentGate, DocGen, `index.md`, CPM + lock file | Giữ, thêm **gate AOT** | — |

## 2. Ràng buộc AOT

`Directory.Build.props` (mọi project trong `src/`):
```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <IsAotCompatible>true</IsAotCompatible>
  <InvariantGlobalization>true</InvariantGlobalization>
  <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
  <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
  <WarningsAsErrors>$(WarningsAsErrors);RS0030;IL2026;IL2046;IL2057;IL2067;IL2070;IL2072;IL2075;IL2087;IL2091;IL3050;IL3051</WarningsAsErrors>
  <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
</PropertyGroup>
```
Host (`MLPlatform.Api`, `MLPlatform.Agent`, `MLPlatform.Cli`): `<PublishAot>true</PublishAot>`.

`BannedSymbols.txt` = bộ của Cortext (SQL thô) + `Assembly.GetTypes`, `Activator.CreateInstance(Type)`, `Type.GetType(string)`, namespace `Microsoft.EntityFrameworkCore`, `Microsoft.AspNetCore.Mvc`, `Newtonsoft.Json`, `JsonSerializer` không kèm `JsonTypeInfo`. Mã sinh (`*.g.cs`, `[GeneratedCode]`) được miễn.

CI publish AOT mọi host trên mọi RID đích; **một cảnh báo IL = build đỏ**.

## 3. Cấu trúc solution

```
MLPlatform.slnx · Directory.Build.props · Directory.Packages.props · BannedSymbols.txt · global.json
proto/mlplatform/v1/*.proto                 # hợp đồng duy nhất (20/03)
src/
  Framework/
    MLPlatform.Contracts/                   # mã C# sinh từ proto (Grpc.Tools)
    MLPlatform.Cqrs/ + .Generators/         # ICommand/IQuery/IMediator, behaviors; generator đăng ký + dispatch
    MLPlatform.Data/ + .Generators/         # session, UoW, outbox, ScriptRunner; generator .sql → method
    MLPlatform.Hosting/                     # gRPC wiring, auth handlers, stream broker, health, OTel
    MLPlatform.Security/                    # ISecretBox, hash token, PBKDF2, HMAC
    MLPlatform.AgentProtocol/               # khung delimited, ghép luồng, data channel (dùng chung api + agent)
    MLPlatform.Ray/                         # RayJobs/Serve/State client (HttpClient + STJ source-gen)
    MLPlatform.Storage/                     # S3/kho S3: presign, multipart, copy
  ControlPlane/
    MLPlatform.ControlPlane.Domain/         # thư mục theo module: Identity, Fleet, Scheduling, Data, Labeling, …
    MLPlatform.ControlPlane.Application/    # use case (mỗi type một file), port, Sql/<Module>/*.sql
    MLPlatform.ControlPlane.Infrastructure/ # Ray, WireGuard, kho S3, registry, mail, installer
  Hosts/
    MLPlatform.Api/                         # ráp: gRPC services, agent listener, gateway, background
    MLPlatform.Agent/                       # agent cho PC (linux-x64/arm64, osx-arm64, win-x64)
tools/
  MLPlatform.Cli/                           # mlp db …, mlp sql verify, mlp agent release
  MLPlatform.SqlGate/ · MLPlatform.CommentGate/ · MLPlatform.DocGen/
db/<schema>/                                # platform, identity, fleet, sched, data, label, lineage, audit, nb, track, registry, serving, notify, catalog
python/ mlplatform-sdk/ · mlplatform-runtime/ · images/
web/ (React + Vite + Connect-ES)
deploy/ (compose, production, backup)
```

Quy tắc phụ thuộc như Cortext. Module gọi nhau qua interface trong Application hoặc domain event; **không đọc schema của module khác** (SqlGate kiểm thư mục `Sql/Fleet/` chỉ chạm schema `fleet`).

## 4. CQRS sinh mã

```csharp
public sealed record DrainHostCommand(Guid WorkspaceId, Guid HostId) : ICommand<DrainHostCommand, HostDto>;

public sealed class DrainHostCommandHandler(IWriteSession db, IClock clock)
    : ICommandHandler<DrainHostCommand, HostDto>
{
    public async ValueTask<HostDto> Handle(DrainHostCommand cmd, CancellationToken ct) { … }
}
```
- Generator sinh `AddGeneratedHandlers()` (đăng ký tường minh) và dispatcher `switch` theo kiểu.
- Pipeline: `Activity` (span OTel) → `Authorization` (permission khai bằng attribute) → `Validation` (validator sinh mã) → `UnitOfWork` → `Audit` → handler → `Outbox`.
- Exception có kiểu (`NotFound`, `Conflict`, `Forbidden`, `ConcurrencyConflict`, `BadRequest`) → interceptor gRPC đổi sang status + `ErrorInfo` (20/03).
- Lớp gRPC service chỉ ánh xạ message ↔ command/query rồi `mediator.Send(...)` (như controller Cortext).

## 5. Truy cập dữ liệu

### 5.1 Session
- `NpgsqlSlimDataSourceBuilder` (chỉ bật tính năng cần) → AOT-safe.
- `IReadSession`: kết nối đọc (`default_transaction_read_only=on`, có thể trỏ replica).
- `IWriteSession`: kết nối + transaction do `UnitOfWorkBehavior` quản lý; audit và outbox chung transaction.
- Query handler chỉ nhận `IReadSession`; command handler chỉ nhận `IWriteSession` (gate).

### 5.2 Truy vấn = file `.sql` + generator
`Application/Sql/Fleet/GetHost.sql`
```sql
-- mlp:query GetHost :one
-- mlp:returns HostRow
SELECT id, workspace_id, name, state, overlay_ip, last_heartbeat_at, xmin AS row_version
FROM fleet.hosts
WHERE id = @id AND workspace_id = @workspace_id AND deleted_at IS NULL;
```
```csharp
public sealed record HostRow(Guid Id, Guid WorkspaceId, string Name, HostState State,
                             IPAddress OverlayIp, DateTimeOffset? LastHeartbeatAt, uint RowVersion);

public static partial class FleetSql
{
    [SqlQuery("Fleet/GetHost.sql")]
    public static partial Task<HostRow?> GetHostAsync(this IReadSession db, Guid id, Guid workspaceId, CancellationToken ct);
}
```
- Tham số `@snake_case` ↔ tham số method; thiếu/thừa → lỗi biên dịch `MLPSQL001`.
- Cột ↔ tham số constructor theo tên, getter có kiểu; enum ↔ `text`; `jsonb` ↔ kiểu có `JsonTypeInfo`; `bytea` ↔ message proto.
- Loại: `:one`, `:many`, `:exec`, `:scalar`, `:copy` (bulk `COPY … FROM STDIN BINARY`).
- `mlp sql verify` (CI, trên DB đã migrate): describe từng truy vấn, so kiểu cột với record → lệch là lỗi.

### 5.3 Concurrency, phân trang, tenant
- Optimistic concurrency bằng `xmin` (`… AND xmin = @row_version::xid`); 0 dòng → `ConcurrencyConflictException` → `ABORTED` + `ErrorInfo.reason=CONCURRENCY_CONFLICT`.
- Page token = `(created_at, id)` mã hoá.
- Bảng có `workspace_id` → mọi truy vấn phải lọc `workspace_id` (SqlGate `MLPSQL010`).

### 5.4 Outbox
`platform.outbox(id, type, payload bytea, occurred_at, dispatched_at, attempts, state)`; `OutboxDispatcher` thức bằng `LISTEN mlp_outbox` (+ poll 5 s), gọi `IEventHandler<T>` (đăng ký bằng generator), đẩy sự kiện tới `StreamBroker` cho các `Watch*` stream; lỗi retry lũy thừa, quá 10 lần → `dead`.

### 5.5 Migration
Như Cortext: file `yyyyMMddHHmm_<slug>.sql` + `down/`, sổ `platform.migrations` có checksum, `mlp db new|lint|apply|status|verify`; một file một transaction, `lock_timeout 5s`, index lớn `CONCURRENTLY` ở file riêng. Khác Cortext: được `DROP/RENAME` qua quy trình expand → contract (`-- mlp:contract <file expand>`). Áp migration là bước triển khai (`db-migrate`), không chạy khi API khởi động.

## 6. Background
`BackgroundService` + bảng việc Postgres (`FOR UPDATE SKIP LOCKED`) + Cronos cho cron: scheduler job (M03), outbox, đồng bộ Ray (M02/M03/M12), idle-cull notebook (M08), dọn thùng rác và quota (20/09).

## 7. Spike AOT ở P0

| Thư viện | Câu hỏi | Dự phòng |
|---|---|---|
| gRPC + gRPC-Web + Connect-ES | Server streaming qua Nginx/TLS ổn định trong binary AOT? | Connect protocol thuần HTTP/1.1 |
| AWSSDK.S3 v4 | Presign + multipart không cảnh báo IL? | Client S3 tối giản (SigV4) |
| YARP `IHttpForwarder` | Dùng cho inference gateway; có cảnh báo trim? | Forwarder tự viết trên `HttpClient` |
| Npgsql slim + TimescaleDB + COPY | Đủ kiểu uuid[], inet, jsonb, timestamptz? | Map jsonb ↔ string thủ công |
| Cronos | AOT sạch? | Parser cron tối giản |
| OpenTelemetry OTLP | AOT sạch? | Log JSON + metrics Prometheus tự viết |
| Agent: Docker Engine API qua unix socket / named pipe | `ConnectCallback` trên 3 OS? | Gọi `docker` CLI |

Kết quả ghi vào [50/02 P0](../50-lo-trinh/02-p0-nen-tang.md).
