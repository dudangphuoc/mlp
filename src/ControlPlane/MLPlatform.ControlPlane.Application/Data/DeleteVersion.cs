using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Delete a dataset version that nothing uses (no run trained on it, no labeling project, draft or evaluation
/// points at it).</summary>
public sealed record DeleteVersionCommand(Guid WorkspaceId, Guid VersionId) : ICommand<bool>;

/// <summary>Refuses with what still uses the version.</summary>
public sealed class DeleteVersionCommandHandler(IWriteSession db, S3Client s3, WorkspaceAuthorizer auth) : ICommandHandler<DeleteVersionCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteVersionCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        var v = await db.GetVersionAsync(c.VersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var users = new List<string>();
        if (await db.RunsUsingVersionAsync(v.Id, c.WorkspaceId, ct) is > 0 and var r) users.Add($"{r} training run(s)");
        if (await db.ProjectsUsingVersionAsync(v.Id, c.WorkspaceId, ct) is > 0 and var p) users.Add($"{p} labeling project(s)");
        if (await db.DraftsUsingVersionAsync(v.Id, c.WorkspaceId, ct) is > 0 and var d) users.Add($"{d} recipe draft(s)");
        if (await db.EvaluationsUsingVersionAsync(v.Id, c.WorkspaceId, ct) is > 0 and var e) users.Add($"{e} evaluation(s)");
        if (users.Count > 0) throw new PreconditionException("DATA_VERSION_IN_USE", $"Version v{v.Number} is used by " + string.Join(", ", users));
        var keepObjects = v.Prefix.Length == 0 || await db.VersionFilesReferencedAsync(v.Id, $"s3://{Buckets.Datasets}/{v.Prefix}", ct) > 0;
        await db.DetachDeletedProjectsAsync(v.Id, c.WorkspaceId, ct);
        await db.DeleteVersionAsync(v.Id, ct);
        if (!keepObjects)
            foreach (var o in await s3.ListAsync(Buckets.Datasets, v.Prefix, ct)) await s3.DeleteAsync(Buckets.Datasets, o.Key, ct);
        return true;
    }
}
