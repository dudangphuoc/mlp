using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/// <summary>Health from a real call on a model that was tried but is not the run's final model.</summary>
public sealed record RecordAiModelOutcomeCommand(Guid ModelId, bool Ok, string? Reason) : ICommand<bool>, IInternalCommand;

public sealed class RecordAiModelOutcomeCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordAiModelOutcomeCommand, bool>
{
    public async ValueTask<bool> Handle(RecordAiModelOutcomeCommand c, CancellationToken ct) { await db.RecordAiModelOutcomeAsync(c.ModelId, c.Ok, c.Reason, clock.UtcNow, ct); return true; }
}
