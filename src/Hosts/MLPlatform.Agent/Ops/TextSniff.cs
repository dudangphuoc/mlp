namespace MLPlatform.Agent.Ops;

/// <summary>Heuristic text detection: valid UTF-8 without NUL bytes in the first 8 KiB.</summary>
public static class TextSniff
{
    private const int Window = 8192;

    public static bool IsText(ReadOnlySpan<byte> data)
    {
        var head = data[..Math.Min(data.Length, Window)];
        if (head.Contains((byte)0)) return false;
        return System.Text.Unicode.Utf8.IsValid(head.Length < data.Length ? WithoutPartialTail(head) : head);
    }

    private static ReadOnlySpan<byte> WithoutPartialTail(ReadOnlySpan<byte> head)
    {
        var cut = head.Length;
        while (cut > 0 && head.Length - cut < 3 && (head[cut - 1] & 0xC0) == 0x80) cut--;
        return cut > 0 && head[cut - 1] >= 0xC0 ? head[..(cut - 1)] : head;
    }
}
