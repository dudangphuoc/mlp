using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Durable checkpoint (20/10 §4.2): presigned PUT to
/// `mlp-artifacts/{ws}/jobs/{job}/checkpoints/{step}/{name}`.</summary>
[Access(AccessKind.WorkloadToken)]
public sealed record CreateCheckpointUploadCommand(string Token, string Name, long Step) : ICommand<(string Url, string Key)>, IInternalCommand;

public sealed class CreateCheckpointUploadCommandHandler(IReadSession db, TokenService tokens, S3Client s3) : ICommandHandler<CreateCheckpointUploadCommand, (string Url, string Key)>
{
    public async ValueTask<(string Url, string Key)> Handle(CreateCheckpointUploadCommand c, CancellationToken ct)
    {
        var a = await Workload.ResolveAsync(db, tokens, c.Token, ct);
        var key = $"{a.WorkspaceId}/jobs/{a.JobId}/checkpoints/{c.Step}/{Buckets.SafePath(c.Name)}";
        return (s3.Presign(HttpMethod.Put, Buckets.Artifacts, key, TimeSpan.FromHours(6), Audience.Workload), key);
    }
}
