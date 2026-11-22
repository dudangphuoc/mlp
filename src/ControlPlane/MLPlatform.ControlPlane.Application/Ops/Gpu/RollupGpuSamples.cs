using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

/// <summary>Rolls raw GPU samples into 1-minute rows and prunes raw (24 h) and rollups (30 days).</summary>
[Access(AccessKind.Internal)]
public sealed record RollupGpuSamplesCommand : ICommand<bool>, IInternalCommand;

public sealed class RollupGpuSamplesCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RollupGpuSamplesCommand, bool>
{
    public static readonly TimeSpan RawRetention = TimeSpan.FromHours(24), RollupRetention = TimeSpan.FromDays(30), Lookback = TimeSpan.FromMinutes(30);

    public async ValueTask<bool> Handle(RollupGpuSamplesCommand c, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var to = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        await db.RollupGpuSamplesAsync(to - Lookback, to, ct);
        await db.PruneGpuSamplesAsync(now - RawRetention, now - RollupRetention, ct);
        return true;
    }
}
