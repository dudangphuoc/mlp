using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Issues a token to the container at launch (the shim uses it to log metrics/progress).</summary>
[Access(AccessKind.WorkloadToken)]
public sealed record MintWorkloadTokenCommand(Guid AttemptId) : ICommand<string>;

public sealed class MintWorkloadTokenCommandHandler(IWriteSession db, TokenService tokens) : ICommandHandler<MintWorkloadTokenCommand, string>
{
    public async ValueTask<string> Handle(MintWorkloadTokenCommand c, CancellationToken ct)
    {
        var t = TokenService.New("mlpj_");
        await db.SetWorkloadTokenAsync(c.AttemptId, tokens.Hash(t), ct);
        return t;
    }
}
