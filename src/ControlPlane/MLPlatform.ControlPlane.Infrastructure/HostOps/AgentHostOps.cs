using System.Buffers.Binary;
using Google.Protobuf;
using MLPlatform.AgentProtocol;
using MLPlatform.ControlPlane.Application.Fleet.Ops;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Remote ops over the agent control channel; uploads go over a data channel.</summary>
public sealed class AgentHostOps(AgentRegistry agents, DataChannelBroker broker) : IHostOps
{
    public async Task<HostOpsReply> ExecuteAsync(Guid hostId, HostOpsRequest request, CancellationToken ct)
    {
        if (!agents.TryGet(hostId, out var s)) throw Offline();
        var (cmd, limit) = HostOpsCommands.Build(request);
        var res = await s.SendCommandAsync(cmd, limit, ct);
        if (!res.Ok) throw new PreconditionException(string.IsNullOrEmpty(res.ErrorReason) ? "FLEET_COMMAND_FAILED" : res.ErrorReason, res.Message);
        var stat = res.ResultCase == CommandResult.ResultOneofCase.FsStat ? res.FsStat : null;
        return new HostOpsReply(res.ToByteArray(), string.IsNullOrEmpty(stat?.Sha256) ? null : stat.Sha256, (long)(stat?.Size ?? 0));
    }

    public async Task<HostOpsReply> UploadAsync(Guid hostId, string path, Stream content, long size, CancellationToken ct)
    {
        if (!agents.TryGet(hostId, out _)) throw Offline();
        await using var channel = await broker.OpenAsync(hostId, new OpenDataChannel { HostFile = new HostFileTarget { Path = path, Upload = true } },
            "FLEET_HOST_OFFLINE", ct);
        var header = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(header, (ulong)size);
        await channel.WriteAsync(header, ct);
        await CopyExactlyAsync(content, channel, size, ct);
        await channel.FlushAsync(ct);
        var stat = await Framing.ReadAsync(channel, FsStat.Parser, ct)
                   ?? throw new PreconditionException("FLEET_UPLOAD_FAILED", "The machine refused the upload (path outside the allowed folders?)");
        return new HostOpsReply(new CommandResult { Ok = true, FsStat = stat }.ToByteArray(), stat.Sha256, (long)stat.Size);
    }

    private static async Task CopyExactlyAsync(Stream from, Stream to, long size, CancellationToken ct)
    {
        var buf = new byte[256 * 1024];
        for (var left = size; left > 0;)
        {
            var n = await from.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, left)), ct);
            if (n == 0) throw new PreconditionException("FLEET_UPLOAD_INCOMPLETE", "The upload ended before the declared size");
            await to.WriteAsync(buf.AsMemory(0, n), ct);
            left -= n;
        }
    }

    private static UnavailableException Offline() => new("FLEET_HOST_OFFLINE", "Host is offline");
}
