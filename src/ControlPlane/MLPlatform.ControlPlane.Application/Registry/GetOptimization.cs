using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>One optimization run (settled first).</summary>
public sealed record GetOptimizationCommand(Guid WorkspaceId, Guid Id) : ICommand<OptimizationRow>;

/// <summary>Reads, settles if running, reads again.</summary>
public sealed class GetOptimizationCommandHandler(IWriteSession db, S3Client s3, WorkspaceAuthorizer auth) : ICommandHandler<GetOptimizationCommand, OptimizationRow>
{
    public async ValueTask<OptimizationRow> Handle(GetOptimizationCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        var o = await db.GetOptimizationAsync(c.WorkspaceId, c.Id, ct) ?? throw new NotFoundException("OPT_NOT_FOUND", "Optimization not found");
        if (o.State == "running" && await ListOptimizationsCommandHandler.Settle(db, s3, o, ct)) o = (await db.GetOptimizationAsync(c.WorkspaceId, c.Id, ct))!;
        return o;
    }
}
