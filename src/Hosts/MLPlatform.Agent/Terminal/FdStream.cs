using System.Runtime.InteropServices;

namespace MLPlatform.Agent.Terminal;

/// <summary>Unbuffered stream over a pseudo terminal descriptor: plain read/write, no file offsets.</summary>
internal sealed class FdStream(int fd) : Stream
{
    private const int Eintr = 4;
    private int _fd = fd;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override unsafe int Read(byte[] buffer, int offset, int count)
    {
        fixed (byte* p = &buffer[offset])
            while (true)
            {
                var n = UnixNative.Read(_fd, p, count);
                if (n >= 0) return (int)n;
                if (Marshal.GetLastPInvokeError() == Eintr) continue;
                return 0;
            }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        Task.Factory.StartNew(() => Read(buffer, offset, count), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (!MemoryMarshal.TryGetArray<byte>(buffer, out var seg)) throw new NotSupportedException();
        return new ValueTask<int>(ReadAsync(seg.Array!, seg.Offset, seg.Count, ct));
    }

    public override unsafe void Write(byte[] buffer, int offset, int count)
    {
        fixed (byte* p = &buffer[offset])
            for (var done = 0; done < count;)
            {
                var n = UnixNative.Write(_fd, p + done, count - done);
                if (n > 0) done += (int)n;
                else if (Marshal.GetLastPInvokeError() != Eintr) throw new IOException("write to terminal failed: " + Marshal.GetLastPInvokeError());
            }
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        Write(buffer.ToArray(), 0, buffer.Length);
        return ValueTask.CompletedTask;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _fd, -1) is >= 0 and var fd) UnixNative.Close(fd);
        base.Dispose(disposing);
    }
}
