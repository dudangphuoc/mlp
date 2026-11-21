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

/// <summary>Delete a finished run with its metrics and file list.</summary>
public sealed record DeleteRunCommand(Guid WorkspaceId, Guid RunId) : ICommand<string>;

/// <summary>Deletes the run records after the finished / no-registered-model checks.</summary>
public sealed class DeleteRunCommandHandler(IWriteSession db, WorkspaceAuthorizer auth, IEnumerable<IRunDeletionGuard> guards) : ICommandHandler<DeleteRunCommand, string>
{
    public async ValueTask<string> Handle(DeleteRunCommand c, CancellationToken ct)
    {
        var r = await RunRules.RequireAsync(db, auth, c.WorkspaceId, c.RunId, "job.submit", ct);
        if (!RunRules.Finished(r.State)) throw new PreconditionException("TRACK_RUN_ACTIVE", "Stop the run before deleting it");
        foreach (var guard in guards) await guard.EnsureDeletableAsync(r.Id, ct);
        await db.DeleteRunAsync(r.Id, r.WorkspaceId, ct);
        return RunArtifacts.Prefix(r.WorkspaceId, r.Id);
    }
}
