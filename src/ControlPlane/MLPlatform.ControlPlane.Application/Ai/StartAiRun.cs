using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/// <summary>Run recording (internal: written by the api runtime around every AI call).</summary>
[Access(AccessKind.Internal)]
public sealed record StartAiRunCommand(Guid Id, Guid? WorkspaceId, string Kind, string Purpose, string? Subject, string Trigger, Guid? ActorId,
    Guid? ModelRef, string? ModelLabel, string SelectionJson, string InputJson) : ICommand<bool>, IInternalCommand;

public sealed class StartAiRunCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<StartAiRunCommand, bool>
{
    public async ValueTask<bool> Handle(StartAiRunCommand c, CancellationToken ct)
    {
        await db.InsertAiRunAsync(c.Id, c.WorkspaceId, c.Kind, c.Purpose, c.Subject, c.Trigger, c.ActorId, c.ModelRef, c.ModelLabel, c.SelectionJson, c.InputJson, clock.UtcNow, ct);
        return true;
    }
}
