using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Formats a model version can be converted to, from its recipe.</summary>
public sealed record ListExportFormatsQuery(Guid WorkspaceId, Guid ModelVersionId) : IQuery<IReadOnlyList<string>>;

/// <summary>Reads <c>entrypoints.export.formats</c> of the version's recipe.</summary>
public sealed class ListExportFormatsQueryHandler(IReadSession db, WorkspaceAuthorizer auth, IMediator mediator) : IQueryHandler<ListExportFormatsQuery, IReadOnlyList<string>>
{
    public async ValueTask<IReadOnlyList<string>> Handle(ListExportFormatsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var v = await db.GetModelVersionAsync(q.ModelVersionId, q.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        try { return ExportRules.AllFormats(JsonNode.Parse((await mediator.Query(new GetRecipeQuery(q.WorkspaceId, v.Recipe), ct)).Manifest)!.AsObject()); }
        catch (NotFoundException) { return []; }
    }
}
