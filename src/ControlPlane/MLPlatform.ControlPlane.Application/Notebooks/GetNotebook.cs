using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

/// <summary>Owner or Admin; anyone else → NOT_FOUND (existence is not revealed).</summary>
public sealed record GetNotebookQuery(Guid WorkspaceId, Guid ServerId) : IQuery<ServerRow>;

public sealed class GetNotebookQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetNotebookQuery, ServerRow>
{
    public async ValueTask<ServerRow> Handle(GetNotebookQuery q, CancellationToken ct) => await NotebookAccess.LoadAsync(db, auth, q.WorkspaceId, q.ServerId, ct);
}
