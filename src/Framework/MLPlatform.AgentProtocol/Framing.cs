using Google.Protobuf;

namespace MLPlatform.AgentProtocol;

/// <summary>Asynchronous varint-delimited framing (20/04 §3). Limit 16 MiB/frame.</summary>
public static class Framing
{
    public const int MaxFrame = 16 * 1024 * 1024;

    public static async ValueTask WriteAsync(Stream s, IMessage message, CancellationToken ct)
    {
        var size = message.CalculateSize();
        var buffer = new byte[CodedOutputStream.ComputeUInt32Size((uint)size) + size];
        var cos = new CodedOutputStream(buffer);
        cos.WriteUInt32((uint)size);
        message.WriteTo(cos);
        cos.Flush();
        await s.WriteAsync(buffer, ct);
        await s.FlushAsync(ct);
    }

    /// <summary>Read one frame; null when the other end closes cleanly between frames.</summary>
    public static async ValueTask<T?> ReadAsync<T>(Stream s, MessageParser<T> parser, CancellationToken ct)
        where T : class, IMessage<T>
    {
        var len = await ReadVarintAsync(s, ct);
        if (len is null) return null;
        if (len > MaxFrame) throw new InvalidDataException($"Frame of {len} bytes exceeds the limit");
        var buf = new byte[len.Value];
        await s.ReadExactlyAsync(buf, ct);
        return parser.ParseFrom(buf);
    }

    private static async ValueTask<int?> ReadVarintAsync(Stream s, CancellationToken ct)
    {
        var one = new byte[1];
        int result = 0, shift = 0;
        for (var i = 0; i < 5; i++)
        {
            var n = await s.ReadAsync(one, ct);
            if (n == 0)
            {
                if (i == 0) return null;
                throw new EndOfStreamException("Stream ended in the middle of a varint");
            }
            result |= (one[0] & 0x7F) << shift;
            if ((one[0] & 0x80) == 0) return result;
            shift += 7;
        }
        throw new InvalidDataException("Varint is too long");
    }
}
