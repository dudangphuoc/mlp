using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

public sealed record SetAiLastTestCommand(Guid ModelId, Guid RunId) : ICommand<bool>, IInternalCommand;

public sealed class SetAiLastTestCommandHandler(IWriteSession db) : ICommandHandler<SetAiLastTestCommand, bool>
{
    public async ValueTask<bool> Handle(SetAiLastTestCommand c, CancellationToken ct) { await db.SetAiLastTestAsync(c.ModelId, c.RunId, ct); return true; }
}
