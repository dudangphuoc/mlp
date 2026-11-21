using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>What changed between two revisions of a project recipe.</summary>
public sealed record DiffRevisionsQuery(Guid WorkspaceId, Guid ProjectRecipeId, int From, int To) : IQuery<RevisionDiffView>;

/// <summary>Same rows as the canvas's change list (variant, parameters by pane); a changed manifest is flagged, not
/// diffed as text.</summary>
public sealed class DiffRevisionsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<DiffRevisionsQuery, RevisionDiffView>
{
    public async ValueTask<RevisionDiffView> Handle(DiffRevisionsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var a = await db.GetRevisionByNumberAsync(q.ProjectRecipeId, q.WorkspaceId, q.From, ct) ?? throw new NotFoundException("TRACK_REVISION_NOT_FOUND", $"Revision r{q.From} not found");
        var b = await db.GetRevisionByNumberAsync(q.ProjectRecipeId, q.WorkspaceId, q.To, ct) ?? throw new NotFoundException("TRACK_REVISION_NOT_FOUND", $"Revision r{q.To} not found");
        var schema = RecipeDrafts.Schema(JsonNode.Parse(b.Manifest)!.AsObject());
        return new RevisionDiffView(a.Number, b.Number, a.ManifestSha256 != b.ManifestSha256,
            RecipeDrafts.Diff(a.Variant, RecipeDrafts.Parse(a.Params), b.Variant, RecipeDrafts.Parse(b.Params), schema));
    }
}
