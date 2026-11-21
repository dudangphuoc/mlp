using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Who triggered a proposal and with what rights (checked in the api before any model is called).</summary>
public sealed record AuthorizeProposalQuery(Guid WorkspaceId, Guid ProjectId, string Path) : IQuery<HermesContext>;

public sealed class AuthorizeProposalQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<AuthorizeProposalQuery, HermesContext>
{
    public async ValueTask<HermesContext> Handle(AuthorizeProposalQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "labeling.annotate", ct);
        var s = await db.GetItemStateAsync(p.Id, q.Path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        if (!roles.Contains("lead") && !roles.Contains("reviewer") && s.AnnotatorId != uid) throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        return await LabelContextLoader.LoadAsync(db, p, q.Path, uid, ct);
    }
}
