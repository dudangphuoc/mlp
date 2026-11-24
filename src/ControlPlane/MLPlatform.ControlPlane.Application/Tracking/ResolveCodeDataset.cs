using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>SDK: a platform dataset for code: from the developer's machine (member) or a run's container (workload
/// token).</summary>
[Access(AccessKind.ScopedPermission, "dataset.read")]
public sealed record ResolveCodeDatasetCommand(Guid WorkspaceId, Guid? RunId, string? WorkloadToken, string Reference) : ICommand<DatasetFiles>, IInternalCommand;

public sealed class ResolveCodeDatasetCommandHandler(IReadSession db, WorkspaceAuthorizer auth, TokenService tokens, IMediator mediator)
    : ICommandHandler<ResolveCodeDatasetCommand, DatasetFiles>
{
    public async ValueTask<DatasetFiles> Handle(ResolveCodeDatasetCommand c, CancellationToken ct)
    {
        var inJob = c is { WorkloadToken: { Length: > 0 }, RunId: not null };
        var workspaceId = inJob ? (await WorkloadAuth.RequireRunAsync(db, tokens, c.WorkloadToken!, c.RunId!.Value, ct)).WorkspaceId : c.WorkspaceId;
        if (!inJob) await auth.RequireAsync(workspaceId, "dataset.read", ct);
        return await mediator.Query(new ResolveDatasetFilesQuery(workspaceId, inJob, c.Reference.Trim()), ct);
    }
}
