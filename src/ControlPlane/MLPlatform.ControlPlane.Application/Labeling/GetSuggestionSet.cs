using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record GetSuggestionSetQuery(Guid WorkspaceId, Guid ProjectId, Guid Id) : IQuery<SuggestionSetRow>;

public sealed class GetSuggestionSetQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<GetSuggestionSetQuery, SuggestionSetRow>
{
    public async ValueTask<SuggestionSetRow> Handle(GetSuggestionSetQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        var s = await db.GetSuggestionSetAsync(q.Id, p.WorkspaceId, ct);
        if (s is null || s.ProjectId != p.Id) throw new NotFoundException("LABEL_SUGGESTION_NOT_FOUND", "Suggestion not found");
        if (roles.Count > 0 && roles.All(r => r == "annotator") && (await db.GetItemStateAsync(p.Id, s.Path, ct))?.AnnotatorId != uid)
            throw new NotFoundException("LABEL_SUGGESTION_NOT_FOUND", "Suggestion not found");
        return s;
    }
}
