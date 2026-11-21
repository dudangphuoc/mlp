using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Features;

// ───────────── Entity + view ─────────────

[RequiresPermission("dataset.write")]
public sealed record CreateEntityCommand(Guid WorkspaceId, string Name, string JoinKey, string Description) : ICommand<EntityRow>;

public sealed class CreateEntityCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreateEntityCommand, EntityRow>
{
    public async ValueTask<EntityRow> Handle(CreateEntityCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        if (!Identity.IdentityRules.Slug().IsMatch(c.Name)) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Name may only contain lowercase letters, numbers and hyphens" });
        if (string.IsNullOrWhiteSpace(c.JoinKey)) throw new ValidationException(new Dictionary<string, string> { ["join_key"] = "A key column is required (e.g. customer_id)" });
        var id = Ids.New();
        try { await db.InsertEntityAsync(id, c.WorkspaceId, c.Name, c.JoinKey.Trim(), c.Description ?? "", uid, clock.UtcNow, ct); }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("FEATURE_ENTITY_NAME_TAKEN", "Entity name is already in use"); }
        return (await db.GetEntityAsync(c.WorkspaceId, id.ToString(), ct))!;
    }
}
