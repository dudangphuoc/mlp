using System.IO.Compression;
using System.Text;

namespace MLPlatform.Api.Http.HostOps;

/// <summary>Records both directions of a terminal session as asciicast v2 lines (gzip), capped at 5 MB
/// compressed.</summary>
public sealed class TerminalRecorder : IDisposable
{
    public const long MaxBytes = 5 * 1024 * 1024;
    private readonly MemoryStream _buffer = new();
    private readonly GZipStream _gzip;
    private readonly long _start = System.Diagnostics.Stopwatch.GetTimestamp();
    private readonly Lock _gate = new();

    public bool Truncated { get; private set; }

    public TerminalRecorder(int cols, int rows)
    {
        _gzip = new GZipStream(_buffer, CompressionLevel.Fastest, leaveOpen: true);
        Write($"{{\"version\":2,\"width\":{cols},\"height\":{rows}}}\n");
    }

    public void Output(ReadOnlySpan<byte> data) => Event("o", data);

    public void Input(ReadOnlySpan<byte> data) => Event("i", data);

    private void Event(string kind, ReadOnlySpan<byte> data)
    {
        var t = System.Diagnostics.Stopwatch.GetElapsedTime(_start).TotalSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        var text = System.Text.Json.JsonEncodedText.Encode(Encoding.UTF8.GetString(data)).ToString();
        Write($"[{t},\"{kind}\",\"{text}\"]\n");
    }

    private void Write(string line)
    {
        lock (_gate)
        {
            if (Truncated) return;
            if (_buffer.Length > MaxBytes) { Truncated = true; return; }
            _gzip.Write(Encoding.UTF8.GetBytes(line));
        }
    }

    public byte[] Finish()
    {
        lock (_gate)
        {
            _gzip.Dispose();
            return _buffer.ToArray();
        }
    }

    public void Dispose()
    {
        _gzip.Dispose();
        _buffer.Dispose();
    }
}
