using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>SDK: record the parameters the code declared (merged into what the run already has); from the developer's
/// machine (member) or from the run's own container (workload token).</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record SetCodeRunParamsCommand(Guid WorkspaceId, Guid RunId, string ParamsJson, string? WorkloadToken = null) : ICommand<bool>;

public sealed class SetCodeRunParamsCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, TokenService tokens) : ICommandHandler<SetCodeRunParamsCommand, bool>
{
    public async ValueTask<bool> Handle(SetCodeRunParamsCommand c, CancellationToken ct)
    {
        var workspaceId = c.WorkloadToken is { Length: > 0 } t
            ? (await WorkloadAuth.RequireRunAsync(db, tokens, t, c.RunId, ct)).WorkspaceId
            : (await CodeRunRules.RequireAsync(db, auth, c.WorkspaceId, c.RunId, ct)).WorkspaceId;
        if (System.Text.Json.Nodes.JsonNode.Parse(c.ParamsJson) is not System.Text.Json.Nodes.JsonObject)
            throw new ValidationException(new Dictionary<string, string> { ["params_json"] = "Must be a JSON object" });
        await db.SetCodeRunParamsAsync(c.RunId, workspaceId, c.ParamsJson, ct);
        return true;
    }
}
