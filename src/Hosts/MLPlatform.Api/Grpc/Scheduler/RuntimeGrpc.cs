using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Middleware;
using MLPlatform.Api.Http.Inference;
using MLPlatform.Api.Http.Install;
using MLPlatform.Api.Http.Mcp;
using MLPlatform.Api.Http.Notebooks;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Empty = MLPlatform.Contracts.V1.Empty;

namespace MLPlatform.Api.Grpc;

/// <summary>RuntimeService (token `mlpj_`): the holder reports allocation → start containers.</summary>
public sealed class RuntimeGrpc(IMediator mediator, WorkloadLauncher launcher) : RuntimeService.RuntimeServiceBase
{
    public override async Task<Empty> ReportAllocation(ReportAllocationRequest r, ServerCallContext ctx)
    {
        var token = ctx.RequestHeaders.GetValue("authorization") is { Length: > 7 } h && h.StartsWith("Bearer ") ? h[7..] : "";
        if (!Guid.TryParse(r.AttemptId, out var aid) || !Guid.TryParse(r.HostId, out var hid))
            throw new ValidationException(new Dictionary<string, string> { ["attempt_id"] = "Invalid UUID" });
        var done = await mediator.Send(new ReportAllocationCommand(token, aid, hid, [.. r.GpuUuids], r.NodeIp, r.Ok, r.Message), ctx.CancellationToken);
        if (done is not null) _ = Task.Run(() => launcher.LaunchAsync(done, CancellationToken.None));
        return new Empty();
    }

    private static string Token(ServerCallContext ctx) =>
        ctx.RequestHeaders.GetValue("authorization") is { Length: > 7 } h && h.StartsWith("Bearer ") ? h[7..] : "";

    public override async Task<Empty> Heartbeat(RuntimeHeartbeat r, ServerCallContext ctx)
    {
        await mediator.Send(new MLPlatform.ControlPlane.Application.Ops.WorkloadHeartbeatCommand(Token(ctx)), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<Empty> ReportProgress(ProgressReport r, ServerCallContext ctx)
    {
        await mediator.Send(new MLPlatform.ControlPlane.Application.Ops.ReportProgressCommand(Token(ctx), r.Phase, r.Current, r.Total, r.Unit, r.Percent, r.Rate,
            r.EtaSeconds, r.Message, r.StallAfterMin, r.DetailJson), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<CheckpointUpload> CreateCheckpointUpload(CheckpointUploadRequest r, ServerCallContext ctx)
    {
        var (url, key) = await mediator.Send(new MLPlatform.ControlPlane.Application.Ops.CreateCheckpointUploadCommand(Token(ctx), r.Name, r.Step), ctx.CancellationToken);
        return new CheckpointUpload { Url = url, Key = key };
    }

    public override async Task<Empty> RegisterCheckpoint(RegisterCheckpointRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new MLPlatform.ControlPlane.Application.Ops.RegisterCheckpointCommand(Token(ctx), r.Name, r.Step, r.Location,
            string.IsNullOrEmpty(r.Key) ? null : r.Key, r.SizeBytes, r.Sha256), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<ArtifactUploads> CreateArtifactUploads(CreateArtifactUploadsRequest r, ServerCallContext ctx)
    {
        if (!Guid.TryParse(r.RunId, out var run)) throw new ValidationException(new Dictionary<string, string> { ["run_id"] = "Invalid UUID" });
        var urls = await mediator.Send(new MLPlatform.ControlPlane.Application.Tracking.CreateArtifactUploadsCommand(WorkloadTokenOrNull(ctx), run, [.. r.Paths]), ctx.CancellationToken);
        var res = new ArtifactUploads();
        res.Urls.AddRange(urls);
        return res;
    }

    public override async Task<Empty> CompleteArtifacts(CompleteArtifactsRequest r, ServerCallContext ctx)
    {
        if (!Guid.TryParse(r.RunId, out var run)) throw new ValidationException(new Dictionary<string, string> { ["run_id"] = "Invalid UUID" });
        await mediator.Send(new MLPlatform.ControlPlane.Application.Tracking.CompleteArtifactsCommand(WorkloadTokenOrNull(ctx), run,
            r.Files.Select(f => (f.Path, f.SizeBytes)).ToList()), ctx.CancellationToken);
        return new Empty();
    }

    private static string? WorkloadTokenOrNull(ServerCallContext ctx) => Token(ctx) is { } t && t.StartsWith("mlpj_", StringComparison.Ordinal) ? t : null;
}
