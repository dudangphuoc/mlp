using System.Buffers.Binary;

namespace MLPlatform.AgentProtocol;

/// <summary>Terminal frames on a data channel: [u8 kind][u32 BE length][payload ≤ 64 KiB].</summary>
public readonly record struct ChannelFrame(ChannelFrameKind Kind, ReadOnlyMemory<byte> Payload)
{
    public const int MaxPayload = 64 * 1024;
    private const int HeaderSize = 5;

    public static async ValueTask WriteAsync(Stream s, ChannelFrameKind kind, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (payload.Length > MaxPayload) throw new InvalidDataException("Frame payload too large");
        var buf = new byte[HeaderSize + payload.Length];
        buf[0] = (byte)kind;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(1), (uint)payload.Length);
        payload.CopyTo(buf.AsMemory(HeaderSize));
        await s.WriteAsync(buf, ct);
        await s.FlushAsync(ct);
    }

    /// <summary>Next frame, or null at end of stream.</summary>
    public static async ValueTask<ChannelFrame?> ReadAsync(Stream s, CancellationToken ct)
    {
        var head = new byte[HeaderSize];
        if (await s.ReadAtLeastAsync(head, HeaderSize, throwOnEndOfStream: false, ct) < HeaderSize) return null;
        var len = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(1));
        if (len > MaxPayload) throw new InvalidDataException("Frame payload too large");
        var payload = new byte[len];
        await s.ReadExactlyAsync(payload, ct);
        return new ChannelFrame((ChannelFrameKind)head[0], payload);
    }

    public static byte[] Resize(int cols, int rows)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)Math.Clamp(cols, 1, 1000));
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(2), (ushort)Math.Clamp(rows, 1, 1000));
        return b;
    }

    public (int Cols, int Rows) ReadResize() => Payload.Length < 4 ? (80, 24)
        : (BinaryPrimitives.ReadUInt16BigEndian(Payload.Span), BinaryPrimitives.ReadUInt16BigEndian(Payload.Span[2..]));

    public static byte[] Exit(int code)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, code);
        return b;
    }
}
