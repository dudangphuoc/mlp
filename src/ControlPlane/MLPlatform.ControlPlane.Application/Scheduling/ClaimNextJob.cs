using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed record ClaimNextJobCommand : ICommand<ClaimedJob?>;

public sealed class ClaimNextJobCommandHandler(IWriteSession db) : ICommandHandler<ClaimNextJobCommand, ClaimedJob?>
{
    public async ValueTask<ClaimedJob?> Handle(ClaimNextJobCommand c, CancellationToken ct) =>
        await db.ClaimNextJobAsync(ct) is { } j ? new ClaimedJob(j, JobSpecDto.Parse(j.Spec)) : null;
}
