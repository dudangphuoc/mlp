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

/// <summary>Delete a run and its files (versions already saved from it stay).</summary>
public sealed record DeleteOptimizationCommand(Guid WorkspaceId, Guid Id) : ICommand<bool>;

/// <summary>Removes every object under the run's folder, then the row.</summary>
public sealed class DeleteOptimizationCommandHandler(IWriteSession db, S3Client s3, WorkspaceAuthorizer auth) : ICommandHandler<DeleteOptimizationCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteOptimizationCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var o = await db.GetOptimizationAsync(c.WorkspaceId, c.Id, ct) ?? throw new NotFoundException("OPT_NOT_FOUND", "Optimization not found");
        foreach (var f in await s3.ListAsync(Buckets.Artifacts, OptimizationRules.Prefix(c.WorkspaceId, o.Id), ct)) await s3.DeleteAsync(Buckets.Artifacts, f.Key, ct);
        await db.DeleteOptimizationAsync(c.WorkspaceId, o.Id, ct);
        return true;
    }
}
