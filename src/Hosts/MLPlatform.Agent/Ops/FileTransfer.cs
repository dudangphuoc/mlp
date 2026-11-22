using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Ops;

/// <summary>One file over a data channel: download sends [u64 size][bytes]; upload reads [u64 size][bytes], swaps the
/// target atomically and answers with a framed FsStat.</summary>
public static class FileTransfer
{
    private const int BufferSize = 256 * 1024;

    public static async Task RunAsync(IReadOnlyList<string> roots, HostFileTarget t, Stream remote, CancellationToken ct)
    {
        if (PathGuard.Resolve(roots, t.Path) is not { } path || PathGuard.IsRoot(roots, path)) return;
        if (t.Upload) await UploadAsync(path, remote, ct);
        else await DownloadAsync(path, t, remote, ct);
    }

    private static async Task DownloadAsync(string path, HostFileTarget t, Stream remote, CancellationToken ct)
    {
        var header = new byte[8];
        if (!File.Exists(path))
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(header, ulong.MaxValue);
            await remote.WriteAsync(header, ct);
            return;
        }
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, useAsync: true);
        var offset = Math.Min((long)t.Offset, fs.Length);
        var length = t.Length > 0 ? Math.Min((long)t.Length, fs.Length - offset) : fs.Length - offset;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(header, (ulong)length);
        await remote.WriteAsync(header, ct);
        fs.Seek(offset, SeekOrigin.Begin);
        var buf = new byte[BufferSize];
        for (long left = length; left > 0;)
        {
            var n = await fs.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, left)), ct);
            if (n == 0) break;
            await remote.WriteAsync(buf.AsMemory(0, n), ct);
            left -= n;
        }
        await remote.FlushAsync(ct);
    }

    private static async Task UploadAsync(string path, Stream remote, CancellationToken ct)
    {
        var header = new byte[8];
        await remote.ReadExactlyAsync(header, ct);
        var size = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(header);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".mlp-upload";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            var buf = new byte[BufferSize];
            for (var left = size; left > 0;)
            {
                var n = await remote.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, left)), ct);
                if (n == 0) throw new IOException("Upload ended early");
                await fs.WriteAsync(buf.AsMemory(0, n), ct);
                left -= n;
            }
        }
        File.Move(tmp, path, overwrite: true);
        await MLPlatform.AgentProtocol.Framing.WriteAsync(remote, await HostFs.HashedStatAsync(path, ct), ct);
    }
}
