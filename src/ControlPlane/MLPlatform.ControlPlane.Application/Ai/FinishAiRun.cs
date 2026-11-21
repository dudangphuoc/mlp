using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

[Access(AccessKind.Internal)]
public sealed record FinishAiRunCommand(Guid Id, string State, string? Fallback, Guid? ModelRef, string? ModelLabel, string OutputJson, string? DecidedBy,
    double? Confidence, int? LatencyMs, int? TokensIn, int? TokensOut, double? CostUsd, string AttemptsJson, string? ErrorCode, string? Error,
    bool UpdateHealth, string? HealthReason) : ICommand<bool>, IInternalCommand;

public sealed class FinishAiRunCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<FinishAiRunCommand, bool>
{
    public async ValueTask<bool> Handle(FinishAiRunCommand c, CancellationToken ct)
    {
        await db.FinishAiRunAsync(c.Id, c.State, c.Fallback, c.ModelRef, c.ModelLabel, c.OutputJson, c.DecidedBy, c.Confidence, c.LatencyMs, c.TokensIn,
            c.TokensOut, c.CostUsd, c.AttemptsJson, c.ErrorCode, c.Error, clock.UtcNow, ct);
        if (c.UpdateHealth && c.ModelRef is { } mid)
            await db.RecordAiModelOutcomeAsync(mid, c.State == "succeeded", c.HealthReason ?? c.Error, clock.UtcNow, ct);
        return true;
    }
}
