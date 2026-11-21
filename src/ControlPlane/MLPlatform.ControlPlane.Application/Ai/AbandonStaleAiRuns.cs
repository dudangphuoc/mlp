using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/// <summary>Startup: runs left "running" by a previous process are marked failed (visible, never stuck).</summary>
public sealed record AbandonStaleAiRunsCommand : ICommand<bool>, IInternalCommand;

public sealed class AbandonStaleAiRunsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<AbandonStaleAiRunsCommand, bool>
{
    public async ValueTask<bool> Handle(AbandonStaleAiRunsCommand c, CancellationToken ct) { await db.AbandonStaleAiRunsAsync(clock.UtcNow, clock.UtcNow, ct); return true; }
}
