using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

/// <summary>Internal (launcher, environment builds): records the state of an image on a machine.</summary>
public sealed record RecordEnvironmentCommand(Guid WorkspaceId, string RecipeName, string EnvTag, Guid HostId, string State, string Error) : ICommand<bool>;

/// <summary>Upserts on (workspace, tag, machine); finishing a build keeps its start time.</summary>
public sealed class RecordEnvironmentCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordEnvironmentCommand, bool>
{
    public async ValueTask<bool> Handle(RecordEnvironmentCommand c, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var error = c.Error.Length > 2000 ? c.Error[^2000..] : c.Error;
        DateTimeOffset? built = c.State == "ready" ? now : null;
        if (c.State != "building" && await db.GetEnvironmentAsync(c.WorkspaceId, c.EnvTag, c.HostId, ct) is not null)
            await db.FinishEnvironmentAsync(c.WorkspaceId, c.EnvTag, c.HostId, c.State, error, built, ct);
        else
            await db.UpsertEnvironmentAsync(Ids.New(), c.WorkspaceId, c.RecipeName, c.EnvTag, c.HostId, c.State, error, now, built, ct);
        return true;
    }
}
