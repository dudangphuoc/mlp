using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>Worker updates progress (internal).</summary>
[Access(AccessKind.Internal)]
public sealed record UpdateHubImportCommand(Guid Id, string State, Guid? TargetId, int FilesTotal, int FilesDone, long BytesTotal, long BytesDone, string Message)
    : ICommand<bool>, IInternalCommand;

/// <summary>Handles <see cref="UpdateHubImportCommand"/>.</summary>
public sealed class UpdateHubImportCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<UpdateHubImportCommand, bool>
{
    public async ValueTask<bool> Handle(UpdateHubImportCommand c, CancellationToken ct)
    {
        await db.UpdateImportAsync(c.Id, c.State, c.TargetId, c.FilesTotal, c.FilesDone, c.BytesTotal, c.BytesDone, c.Message, clock.UtcNow, ct);
        return true;
    }
}
