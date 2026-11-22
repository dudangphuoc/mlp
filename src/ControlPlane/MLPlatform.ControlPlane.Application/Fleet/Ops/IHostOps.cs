namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

/// <summary>Sends remote ops to a connected agent; throws Unavailable when offline and Precondition on agent
/// errors.</summary>
public interface IHostOps
{
    Task<HostOpsReply> ExecuteAsync(Guid hostId, HostOpsRequest request, CancellationToken ct);

    Task<HostOpsReply> UploadAsync(Guid hostId, string path, Stream content, long size, CancellationToken ct);
}
