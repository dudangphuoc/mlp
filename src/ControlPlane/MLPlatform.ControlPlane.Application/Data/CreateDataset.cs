using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

public sealed record CreateDatasetCommand(Guid WorkspaceId, string Slug, string Name, string Task) : ICommand<DatasetRow>;

public sealed class CreateDatasetCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreateDatasetCommand, DatasetRow>
{
    public async ValueTask<DatasetRow> Handle(CreateDatasetCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        if (!IdentityRules.Slug().IsMatch(c.Slug))
            throw new ValidationException(new Dictionary<string, string> { ["slug"] = "Invalid slug" });
        var id = Ids.New();
        try { await db.InsertDatasetAsync(id, c.WorkspaceId, c.Slug, c.Name, c.Task, uid, clock.UtcNow, ct); }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("DATA_SLUG_TAKEN", "URL is already in use"); }
        return (await db.GetDatasetAsync(id, c.WorkspaceId, ct))!;
    }
}
