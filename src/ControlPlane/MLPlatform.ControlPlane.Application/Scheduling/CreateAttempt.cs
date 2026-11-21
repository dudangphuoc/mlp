using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed record CreateAttemptCommand(Guid JobId, int Number, IReadOnlyList<BundleDto> Bundles) : ICommand<AttemptCreated>;

public sealed class CreateAttemptCommandHandler(IWriteSession db, IClock clock, TokenService tokens) : ICommandHandler<CreateAttemptCommand, AttemptCreated>
{
    public async ValueTask<AttemptCreated> Handle(CreateAttemptCommand c, CancellationToken ct)
    {
        var id = Ids.New();
        var token = TokenService.New("mlpj_");
        await db.InsertAttemptAsync(id, c.JobId, c.Number, tokens.Hash(token), c.Bundles.Select(b => b.HostId).ToArray(),
            System.Text.Json.JsonSerializer.Serialize(c.Bundles.ToList(), SchedJson.Default.ListBundleDto), clock.UtcNow, ct);
        await db.SetJobStartingAsync(c.JobId, ct);
        await MLPlatform.ControlPlane.Application.Timeline.TimelineEvents.EmitAsync(db, clock, await MLPlatform.ControlPlane.Application.Timeline.TimelineSql.JobWorkspaceAsync(db, c.JobId, ct), "scheduler", "attempt", id,
            "attempt.reserving", "info", $"Attempt {c.Number} reserving resources (placement group) on {c.Bundles.Count} machines", ct: ct);
        return new AttemptCreated(id, token);
    }
}
