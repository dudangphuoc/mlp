using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Latest machine metrics + per-attempt metrics (container `mlp-w-{attempt}-{rank}`; GPUs by the attempt's
/// `gpu_uuids`) — 20/10 §2.5.</summary>
[Access(AccessKind.Internal)]
public sealed record RecordHostMetricsCommand(Guid HostId, string MetricsJson, IReadOnlyList<(string Container, double Cpu, long Memory)> Containers,
    IReadOnlyList<(string Uuid, double Util, long Vram)> Gpus) : ICommand<int>, IInternalCommand;

public sealed class RecordHostMetricsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordHostMetricsCommand, int>
{
    public async ValueTask<int> Handle(RecordHostMetricsCommand c, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.SetHostMetricsAsync(c.HostId, c.MetricsJson, now, ct);
        var gpusOf = (await db.AttemptGpusAsync(c.HostId, ct)).ToDictionary(a => a.AttemptId, a => a.GpuUuids);
        var n = 0;
        foreach (var (name, cpu, mem) in c.Containers)
        {
            if (!name.StartsWith("mlp-w-", StringComparison.Ordinal) || name.Length < 6 + 36 || !Guid.TryParse(name.AsSpan(6, 36), out var attempt)) continue;
            double? util = null; long? vram = null;
            if (gpusOf.TryGetValue(attempt, out var uuids) && uuids.Length > 0)
            {
                var mine = c.Gpus.Where(g => uuids.Contains(g.Uuid)).ToList();
                if (mine.Count > 0) { util = mine.Average(g => g.Util); vram = mine.Sum(g => g.Vram); }
            }
            await db.InsertAttemptMetricAsync(attempt, now, cpu, mem, util, vram, ct);
            n++;
        }
        return n;
    }
}
