using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record ListSuggestionSetsQuery(Guid WorkspaceId, Guid ProjectId, string Path, Guid? JobId, int Limit) : IQuery<List<SuggestionSetRow>>;

public sealed class ListSuggestionSetsQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<ListSuggestionSetsQuery, List<SuggestionSetRow>>
{
    public async ValueTask<List<SuggestionSetRow>> Handle(ListSuggestionSetsQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        Guid? only = roles.Count > 0 && roles.All(r => r == "annotator") ? uid : null;
        return await db.ListSuggestionSetsAsync(p.WorkspaceId, p.Id, q.Path ?? "", q.JobId, only, Math.Clamp(q.Limit <= 0 ? 50 : q.Limit, 1, 500), ct);
    }
}
