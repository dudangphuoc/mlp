using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>
/// Registers a model version from a run (M11 §5.1, R5): server-side copy of the run's model directory to
/// `mlp-artifacts/{ws}/models/{model}/v{n}/` (immutable, R1); final metrics and recipe/variant are recorded on the version (R7).
/// </summary>
public sealed record RegisterFromRunCommand(Guid WorkspaceId, Guid RunId, string ModelName, string Description) : ICommand<ModelVersionRow>;

public sealed class RegisterFromRunCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth, IMediator mediator)
    : ICommandHandler<RegisterFromRunCommand, ModelVersionRow>
{
    public async ValueTask<ModelVersionRow> Handle(RegisterFromRunCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var name = c.ModelName.Trim();
        if (name.Length is 0 or > 128) throw new ValidationException(new Dictionary<string, string> { ["model_name"] = "Model name must be 1–128 characters" });
        var info = await mediator.Query(new GetRunForRegistryQuery(c.RunId), ct);
        if (info is not { } x || x.Run.WorkspaceId != c.WorkspaceId) throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
        if (x.Run.State != "succeeded") throw new PreconditionException("REGISTRY_RUN_NOT_SUCCEEDED", "Only succeeded runs can be registered");
        if (x.Artifacts.Count == 0) throw new PreconditionException("REGISTRY_ARTIFACT_MISSING", "Run has no model directory");

        var now = clock.UtcNow;
        var model = await db.GetModelByNameAsync(c.WorkspaceId, name, ct);
        Guid modelId;
        if (model is null)
        {
            modelId = Ids.New();
            await db.InsertModelAsync(modelId, c.WorkspaceId, name, x.Run.Task, c.Description, uid, now, ct);
        }
        else
        {
            if (model.Task != x.Run.Task) throw new PreconditionException("REGISTRY_TASK_MISMATCH", $"Model '{name}' has task {model.Task}, but the run has task {x.Run.Task}");
            modelId = model.Id;
        }
        var n = await db.NextModelVersionAsync(modelId, ct);
        var prefix = $"{c.WorkspaceId}/models/{modelId}/v{n}/";
        var src = RunArtifacts.Prefix(c.WorkspaceId, x.Run.Id);
        foreach (var a in x.Artifacts) await s3.CopyAsync(Buckets.Artifacts, src + a.Path, Buckets.Artifacts, prefix + a.Path, ct);
        var metrics = new JsonObject();
        foreach (var m in x.Metrics) metrics[m.Key] = m.Value;
        var id = Ids.New();
        await db.InsertModelVersionAsync(id, modelId, n, x.Run.Id, x.Run.Recipe, x.Run.Variant, x.Run.Task, prefix, x.Artifacts.Count,
            x.Artifacts.Sum(a => a.SizeBytes), metrics.ToJsonString(), uid, now, ct);
        return (await db.GetModelVersionAsync(id, c.WorkspaceId, ct))!;
    }
}

public sealed record ListModelsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ModelListRow>>;

public sealed class ListModelsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListModelsQuery, IReadOnlyList<ModelListRow>>
{
    public async ValueTask<IReadOnlyList<ModelListRow>> Handle(ListModelsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListModelsAsync(q.WorkspaceId, ct);
    }
}

public sealed record ListModelVersionsQuery(Guid WorkspaceId, Guid ModelId) : IQuery<IReadOnlyList<ModelVersionRow>>;

public sealed class ListModelVersionsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListModelVersionsQuery, IReadOnlyList<ModelVersionRow>>
{
    public async ValueTask<IReadOnlyList<ModelVersionRow>> Handle(ListModelVersionsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListModelVersionsAsync(q.ModelId, q.WorkspaceId, ct);
    }
}

/// <summary>Change stage (M11 R2, R3): production requires Owner/Admin + a reason; the previous version in the same stage → archived.</summary>
public sealed record TransitionStageCommand(Guid WorkspaceId, Guid VersionId, string Stage, string Reason) : ICommand<ModelVersionRow>;

public sealed class TransitionStageCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<TransitionStageCommand, ModelVersionRow>
{
    private static readonly string[] Stages = ["none", "staging", "production", "archived"];

    public async ValueTask<ModelVersionRow> Handle(TransitionStageCommand c, CancellationToken ct)
    {
        if (!Stages.Contains(c.Stage)) throw new ValidationException(new Dictionary<string, string> { ["stage"] = "Invalid stage" });
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        if (c.Stage == "production")
        {
            try { await auth.RequireAsync(c.WorkspaceId, "model.promote_production", ct); }
            catch (ForbiddenException) { throw new ForbiddenException("REGISTRY_PROMOTE_FORBIDDEN", "Admin permission is required to promote to production"); }
            if (string.IsNullOrWhiteSpace(c.Reason)) throw new ValidationException(new Dictionary<string, string> { ["reason"] = "A reason is required to promote to production" });
        }
        var v = await db.GetModelVersionAsync(c.VersionId, c.WorkspaceId, ct)
                ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var now = clock.UtcNow;
        if (c.Stage is "production" or "staging")
            foreach (var old in await db.ArchiveOtherInStageAsync(v.ModelId, c.Stage, v.Id, ct))
                await db.InsertStageTransitionAsync(Ids.New(), old.Id, c.Stage, "archived", uid, $"Replaced by v{v.Version}", now, ct);
        await db.SetStageAsync(v.Id, c.Stage, ct);
        await db.InsertStageTransitionAsync(Ids.New(), v.Id, v.Stage, c.Stage, uid, c.Reason, now, ct);
        if (c.Stage == "production")
            await MLPlatform.ControlPlane.Application.Notifications.Notifier.SendAsync(db, clock, c.WorkspaceId,
                await MLPlatform.ControlPlane.Application.Notifications.Notifier.AdminsAsync(db, c.WorkspaceId, ct), "model.promoted",
                $"Model {v.ModelName} v{v.Version} promoted to production", "#/models", $"model/{v.ModelId}", "success", ct);
        return (await db.GetModelVersionAsync(v.Id, c.WorkspaceId, ct))!;
    }
}

/// <summary>Queries for versions imported from HF Hub.</summary>
public static partial class HubVersionSql
{
    [SqlQuery("Registry/InsertHubModelVersion.sql")]
    public static partial Task InsertHubModelVersionAsync(this IWriteSession db, Guid id, Guid modelId, int version, string task, string prefix, Guid createdBy,
        DateTimeOffset now, string sourceRef, string source, CancellationToken ct);
    [SqlQuery("Registry/InsertVersionFileRef.sql")]
    public static partial Task InsertVersionFileRefAsync(this IWriteSession db, Guid versionId, string path, long sizeBytes, string url, CancellationToken ct);
    [SqlQuery("Registry/ListVersionFileRefs.sql")]
    public static partial Task<List<MLPlatform.ControlPlane.Application.Data.VersionFileRow>> ListVersionFileRefsAsync(this IReadSession db, Guid versionId, CancellationToken ct);
    [SqlQuery("Registry/SetVersionReference.sql")]
    public static partial Task SetVersionReferenceAsync(this IWriteSession db, Guid id, CancellationToken ct);
    [SqlQuery("Registry/SetModelVersionSize.sql")]
    public static partial Task SetModelVersionSizeAsync(this IWriteSession db, Guid id, long fileCount, long sizeBytes, CancellationToken ct);
}

/// <summary>M11 R6: create the model (if missing) + an hf_hub-sourced version (recipe hf-auto) — returns the model directory prefix for the worker to write files.</summary>
public sealed record ReserveHubModelVersionCommand(Guid WorkspaceId, string ModelName, string Task, string SourceRef, string Source = "hf_hub") : ICommand<(Guid VersionId, string Prefix)>;
/// <summary>Handles <see cref="ReserveHubModelVersionCommand"/>.</summary>
public sealed class ReserveHubModelVersionCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<ReserveHubModelVersionCommand, (Guid VersionId, string Prefix)>
{
    public async ValueTask<(Guid VersionId, string Prefix)> Handle(ReserveHubModelVersionCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var name = c.ModelName.Trim();
        if (name.Length is 0 or > 128) throw new ValidationException(new Dictionary<string, string> { ["model_name"] = "Model name must be 1–128 characters" });
        var now = clock.UtcNow;
        var model = await db.GetModelByNameAsync(c.WorkspaceId, name, ct);
        Guid modelId;
        if (model is null) { modelId = Ids.New(); await db.InsertModelAsync(modelId, c.WorkspaceId, name, c.Task, "Imported from " + c.SourceRef, uid, now, ct); }
        else
        {
            if (model.Task != c.Task) throw new PreconditionException("REGISTRY_TASK_MISMATCH", $"Model '{name}' has task {model.Task}");
            modelId = model.Id;
        }
        var n = await db.NextModelVersionAsync(modelId, ct);
        var prefix = $"{c.WorkspaceId}/models/{modelId}/v{n}/";
        var id = Ids.New();
        await db.InsertHubModelVersionAsync(id, modelId, n, c.Task, prefix, uid, now, c.SourceRef, c.Source, ct);
        return (id, prefix);
    }
}

/// <summary>Worker finished writing files → update the version's file count + size.</summary>
public sealed record FinishHubModelVersionCommand(Guid VersionId, long FileCount, long SizeBytes) : ICommand<bool>, IInternalCommand;
/// <summary>Handles <see cref="FinishHubModelVersionCommand"/>.</summary>
public sealed class FinishHubModelVersionCommandHandler(IWriteSession db) : ICommandHandler<FinishHubModelVersionCommand, bool>
{
    public async ValueTask<bool> Handle(FinishHubModelVersionCommand c, CancellationToken ct)
    {
        await db.SetModelVersionSizeAsync(c.VersionId, c.FileCount, c.SizeBytes, ct);
        return true;
    }
}

/// <summary>Reference model version: store the source address of each file instead of copying (import worker, internal).</summary>
public sealed record SetModelVersionReferenceCommand(Guid VersionId, IReadOnlyList<(string Path, long Size, string Url)> Files) : ICommand<bool>, IInternalCommand;
/// <summary>Handles <see cref="SetModelVersionReferenceCommand"/>.</summary>
public sealed class SetModelVersionReferenceCommandHandler(IWriteSession db) : ICommandHandler<SetModelVersionReferenceCommand, bool>
{
    public async ValueTask<bool> Handle(SetModelVersionReferenceCommand c, CancellationToken ct)
    {
        foreach (var f in c.Files) await db.InsertVersionFileRefAsync(c.VersionId, MLPlatform.ControlPlane.Application.Data.Buckets.SafePath(f.Path), f.Size, f.Url, ct);
        await db.SetVersionReferenceAsync(c.VersionId, ct);
        await db.SetModelVersionSizeAsync(c.VersionId, c.Files.Count, c.Files.Sum(f => f.Size), ct);
        return true;
    }
}
