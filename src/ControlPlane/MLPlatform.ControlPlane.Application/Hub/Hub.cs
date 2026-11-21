using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>Workspace integration (secret is encrypted — only the api decrypts it when calling HF).</summary>
public sealed record IntegrationRow(Guid WorkspaceId, string Kind, string SecretEnc, string? Account, string Status, DateTimeOffset UpdatedAt, DateTimeOffset? LastCheckedAt);
/// <summary>HF import job (read by the worker).</summary>
public sealed record HubImportJob(Guid Id, Guid WorkspaceId, string Kind, string Repo, string Revision, string[] Include, Guid? TargetId, string TargetName,
    string Task, string State, Guid CreatedBy, string Source, string Mode);
/// <summary>Import history row (UI).</summary>
public sealed record HubImportRow(Guid Id, string Kind, string Repo, string Revision, Guid? TargetId, string TargetName, string Task, string State,
    int FilesTotal, int FilesDone, long BytesTotal, long BytesDone, string Message, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, string Source, string Mode);

/// <summary>Hub module queries (hub schema).</summary>
public static partial class HubSql
{
    [SqlQuery("Hub/UpsertIntegration.sql")]
    public static partial Task UpsertIntegrationAsync(this IWriteSession db, Guid workspaceId, string kind, string secretEnc, string? account, string status, Guid createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Hub/GetIntegration.sql")]
    public static partial Task<IntegrationRow?> GetIntegrationAsync(this IReadSession db, Guid workspaceId, string kind, CancellationToken ct);
    [SqlQuery("Hub/DeleteIntegration.sql")]
    public static partial Task DeleteIntegrationAsync(this IWriteSession db, Guid workspaceId, string kind, CancellationToken ct);
    [SqlQuery("Hub/InsertImport.sql")]
    public static partial Task InsertImportAsync(this IWriteSession db, Guid id, Guid workspaceId, string kind, string repo, string revision, string[] include,
        Guid? targetId, string targetName, string task, Guid createdBy, DateTimeOffset now, string source, string mode, CancellationToken ct);
    [SqlQuery("Hub/GetImport.sql")]
    public static partial Task<HubImportJob?> GetImportAsync(this IReadSession db, Guid id, CancellationToken ct);
    [SqlQuery("Hub/UpdateImport.sql")]
    public static partial Task UpdateImportAsync(this IWriteSession db, Guid id, string state, Guid? targetId, int filesTotal, int filesDone, long bytesTotal, long bytesDone,
        string message, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Hub/ListImports.sql")]
    public static partial Task<List<HubImportRow>> ListImportsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Hub/FailStaleImports.sql")]
    public static partial Task<long> FailStaleImportsAsync(this IWriteSession db, DateTimeOffset now, CancellationToken ct);
}

/// <summary>Saves the HF token (already encrypted + whoami-checked by the api). Owner/Admin.</summary>
public sealed record SetHubTokenCommand(Guid WorkspaceId, string SecretEnc, string? Account, bool Valid) : ICommand<bool>;
/// <summary>Handles <see cref="SetHubTokenCommand"/>.</summary>
public sealed class SetHubTokenCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<SetHubTokenCommand, bool>
{
    public async ValueTask<bool> Handle(SetHubTokenCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "member.manage", ct);
        await db.UpsertIntegrationAsync(c.WorkspaceId, "huggingface", c.SecretEnc, c.Account, c.Valid ? "ok" : "invalid", uid, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>Deletes the HF token.</summary>
public sealed record DeleteHubTokenCommand(Guid WorkspaceId) : ICommand<bool>;
/// <summary>Handles <see cref="DeleteHubTokenCommand"/>.</summary>
public sealed class DeleteHubTokenCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<DeleteHubTokenCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteHubTokenCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "member.manage", ct);
        await db.DeleteIntegrationAsync(c.WorkspaceId, "huggingface", ct);
        return true;
    }
}

/// <summary>HF token status (visible to members, no secret — M01 R10).</summary>
public sealed record GetHubTokenStatusQuery(Guid WorkspaceId) : IQuery<IntegrationRow?>;
/// <summary>Handles <see cref="GetHubTokenStatusQuery"/>.</summary>
public sealed class GetHubTokenStatusQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetHubTokenStatusQuery, IntegrationRow?>
{
    public async ValueTask<IntegrationRow?> Handle(GetHubTokenStatusQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.GetIntegrationAsync(q.WorkspaceId, "huggingface", ct);
    }
}

/// <summary>HF token secret for the api to call the Hub (internal — not exposed over RPC).</summary>
public sealed record GetHubSecretQuery(Guid WorkspaceId) : IQuery<string?>;
/// <summary>Handles <see cref="GetHubSecretQuery"/>.</summary>
public sealed class GetHubSecretQueryHandler(IReadSession db) : IQueryHandler<GetHubSecretQuery, string?>
{
    public async ValueTask<string?> Handle(GetHubSecretQuery q, CancellationToken ct) => (await db.GetIntegrationAsync(q.WorkspaceId, "huggingface", ct))?.SecretEnc;
}

/// <summary>
/// Starts an import from HF Hub. Dataset: creates a new dataset if none is selected (task required); Model: checks model.write permission.
/// A worker in the api downloads files from HF and streams them straight to S3 storage (no temporary copy).
/// </summary>
/// <summary>
/// Import into a dataset or model. Source: "hf" (Repo = org/name, Include = globs), "url" (Include = https URLs, Repo unused),
/// "s3" (Repo = s3://bucket/prefix in our storage). Mode: "copy" = files are copied into our storage now; "reference" = only the
/// addresses are recorded and machines download from the source when a job uses the version.
/// </summary>
public sealed record StartHubImportCommand(Guid WorkspaceId, string Kind, string Repo, string Revision, string[] Include, Guid? DatasetId,
    string TargetName, string Task, string Source = "hf", string Mode = "copy") : ICommand<Guid>;
/// <summary>Handles <see cref="StartHubImportCommand"/>.</summary>
public sealed class StartHubImportCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<StartHubImportCommand, Guid>
{
    public async ValueTask<Guid> Handle(StartHubImportCommand c, CancellationToken ct)
    {
        if (c.Source is not ("hf" or "url" or "s3")) throw new ValidationException(new Dictionary<string, string> { ["source"] = "hf | url | s3" });
        if (c.Mode is not ("copy" or "reference")) throw new ValidationException(new Dictionary<string, string> { ["mode"] = "copy | reference" });
        var repo = c.Repo.Trim();
        var include = c.Include.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
        switch (c.Source)
        {
            case "hf":
                repo = repo.Trim('/');
                if (!System.Text.RegularExpressions.Regex.IsMatch(repo, @"^[A-Za-z0-9][\w.-]*(/[\w.-]+)?$"))
                    throw new ValidationException(new Dictionary<string, string> { ["repo"] = "Use the form org/name, e.g. facebook/detr-resnet-50" });
                break;
            case "url":
                if (include.Length == 0 || include.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out var x) || x.Scheme is not ("https" or "http")))
                    throw new ValidationException(new Dictionary<string, string> { ["urls"] = "Enter one or more http(s) file URLs" });
                repo = include.Length == 1 ? include[0] : $"{include.Length} URLs";
                break;
            case "s3":
                if (!System.Text.RegularExpressions.Regex.IsMatch(repo, @"^s3://[a-z0-9][a-z0-9.-]{1,62}(/.*)?$"))
                    throw new ValidationException(new Dictionary<string, string> { ["repo"] = "Use the form s3://bucket/prefix" });
                break;
        }
        if (c.Kind is not ("dataset" or "model")) throw new ValidationException(new Dictionary<string, string> { ["kind"] = "dataset | model" });
        if (string.IsNullOrWhiteSpace(c.Task)) throw new ValidationException(new Dictionary<string, string> { ["task"] = "Select a task" });
        var uid = await auth.RequireAsync(c.WorkspaceId, c.Kind == "dataset" ? "dataset.write" : "model.write", ct);
        var name = string.IsNullOrWhiteSpace(c.TargetName) ? repo.TrimEnd('/').Split('/')[^1].Split('?')[0] : c.TargetName.Trim();
        Guid? target = c.DatasetId;
        if (c.Kind == "dataset" && target is null)
        {
            var slug = new string(name.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
            var ds = await mediator.Send(new CreateDatasetCommand(c.WorkspaceId, slug.Length >= 2 ? slug : "import-" + slug, name, c.Task), ct);
            target = ds.Id;
        }
        var id = Ids.New();
        await db.InsertImportAsync(id, c.WorkspaceId, c.Kind, repo, c.Source != "hf" ? "" : string.IsNullOrWhiteSpace(c.Revision) ? "main" : c.Revision.Trim(),
            include, target, name, c.Task, uid, clock.UtcNow, c.Source, c.Mode, ct);
        return id;
    }
}

/// <summary>Hands a queued import to the worker in the api host (the pipeline engine starts imports too).</summary>
public interface IHubImportQueue
{
    void Enqueue(Guid importId);
}

/// <summary>Worker updates progress (internal).</summary>
public sealed record UpdateHubImportCommand(Guid Id, string State, Guid? TargetId, int FilesTotal, int FilesDone, long BytesTotal, long BytesDone, string Message)
    : ICommand<bool>, IInternalCommand;
/// <summary>Handles <see cref="UpdateHubImportCommand"/>.</summary>
public sealed class UpdateHubImportCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<UpdateHubImportCommand, bool>
{
    public async ValueTask<bool> Handle(UpdateHubImportCommand c, CancellationToken ct)
    {
        await db.UpdateImportAsync(c.Id, c.State, c.TargetId, c.FilesTotal, c.FilesDone, c.BytesTotal, c.BytesDone, c.Message, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>Import job by id (worker).</summary>
public sealed record GetHubImportJobQuery(Guid Id) : IQuery<HubImportJob?>;
/// <summary>Handles <see cref="GetHubImportJobQuery"/>.</summary>
public sealed class GetHubImportJobQueryHandler(IReadSession db) : IQueryHandler<GetHubImportJobQuery, HubImportJob?>
{
    public async ValueTask<HubImportJob?> Handle(GetHubImportJobQuery q, CancellationToken ct) => await db.GetImportAsync(q.Id, ct);
}

/// <summary>Import history of the workspace.</summary>
public sealed record ListHubImportsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<HubImportRow>>;
/// <summary>Handles <see cref="ListHubImportsQuery"/>.</summary>
public sealed class ListHubImportsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListHubImportsQuery, IReadOnlyList<HubImportRow>>
{
    public async ValueTask<IReadOnlyList<HubImportRow>> Handle(ListHubImportsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListImportsAsync(q.WorkspaceId, ct);
    }
}

/// <summary>api startup: unfinished jobs → failed.</summary>
public sealed record FailStaleHubImportsCommand : ICommand<int>, IInternalCommand;
/// <summary>Handles <see cref="FailStaleHubImportsCommand"/>.</summary>
public sealed class FailStaleHubImportsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<FailStaleHubImportsCommand, int>
{
    public async ValueTask<int> Handle(FailStaleHubImportsCommand c, CancellationToken ct) => (int)await db.FailStaleImportsAsync(clock.UtcNow, ct);
}
