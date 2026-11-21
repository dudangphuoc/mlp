using System.Text.Json;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Ray;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>gRPC channel to a replica, reused per endpoint.</summary>
public sealed class ModelServerClients
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Grpc.Net.Client.GrpcChannel> _channels = new();

    public ModelServer.ModelServerClient For(string endpoint) =>
        new(_channels.GetOrAdd(endpoint, e => Grpc.Net.Client.GrpcChannel.ForAddress("http://" + e)));

    public async Task<bool> HealthAsync(string endpoint, CancellationToken ct)
    {
        try
        {
            var r = await For(endpoint).HealthAsync(new HealthRequest(), deadline: TimeProvider.System.GetUtcNow().UtcDateTime.AddSeconds(3), cancellationToken: ct);
            return r.Ready;
        }
        catch (Grpc.Core.RpcException) { return false; }
    }
}
