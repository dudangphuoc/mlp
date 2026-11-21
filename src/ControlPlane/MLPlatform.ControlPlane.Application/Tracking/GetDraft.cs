using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>The caller's draft of a project recipe; made from the recipe's defaults the first time.</summary>
[RequiresPermission("run.read")]
public sealed record GetDraftCommand(Guid WorkspaceId, Guid ProjectRecipeId) : ICommand<DraftView>;

/// <summary>Creates the draft on first use so the canvas always has something to show.</summary>
public sealed class GetDraftCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<GetDraftCommand, DraftView>
{
    public async ValueTask<DraftView> Handle(GetDraftCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        var pr = await db.GetProjectRecipeAsync(c.ProjectRecipeId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_RECIPE_NOT_FOUND", "Project recipe not found");
        var project = (await db.GetProjectAsync(pr.ProjectId, c.WorkspaceId, ct))!;
        var draft = await db.GetDraftAsync(pr.Id, uid, c.WorkspaceId, ct);
        if (draft is null)
        {
            var manifest = JsonNode.Parse(pr.OriginManifest)!.AsObject();
            var gpu = manifest["environment"]?["gpu"]?.ToString() == "required" ? 1 : 0;
            await db.InsertDraftAsync(Ids.New(), c.WorkspaceId, pr.Id, uid, null, RecipeDrafts.FirstVariant(manifest), "{}", null, "pretrained", "fine_tune", null, null,
                gpu, 4, "", clock.UtcNow, ct);
            draft = (await db.GetDraftAsync(pr.Id, uid, c.WorkspaceId, ct))!;
        }
        return await RecipeDrafts.StateAsync(db, mediator, c.WorkspaceId, pr, draft, project.Task, ct);
    }
}
