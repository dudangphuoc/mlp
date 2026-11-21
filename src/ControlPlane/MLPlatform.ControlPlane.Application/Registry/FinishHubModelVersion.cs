using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Worker finished writing files → update the version's file count + size.</summary>
public sealed record FinishHubModelVersionCommand(Guid VersionId, long FileCount, long SizeBytes) : ICommand<bool>, IInternalCommand;

/// <summary>Handles <see cref="FinishHubModelVersionCommand"/>.</summary>
public sealed class FinishHubModelVersionCommandHandler(IWriteSession db) : ICommandHandler<FinishHubModelVersionCommand, bool>
{
    public async ValueTask<bool> Handle(FinishHubModelVersionCommand c, CancellationToken ct)
    {
        await db.SetModelVersionSizeAsync(c.VersionId, c.FileCount, c.SizeBytes, ct);
        return true;
    }
}
