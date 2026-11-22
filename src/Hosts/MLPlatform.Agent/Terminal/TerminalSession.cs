using MLPlatform.AgentProtocol;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Terminal;

/// <summary>Bridges a data channel to a pseudo terminal until the shell exits or the channel closes.</summary>
public sealed class TerminalSession(Stream remote)
{
    private const int ChunkSize = 16 * 1024;
    private readonly SemaphoreSlim _send = new(1, 1);

    public static string DefaultShell(string requested)
    {
        if (requested.Length > 0 && (OperatingSystem.IsWindows() || File.Exists(requested))) return requested;
        if (OperatingSystem.IsWindows()) return "powershell.exe";
        return new[] { "/bin/bash", "/bin/zsh", "/bin/sh" }.First(File.Exists);
    }

    public static IPty Open(TerminalTarget t, string fallbackCwd)
    {
        var shell = DefaultShell(t.Shell);
        var cwd = t.Cwd.Length > 0 && Directory.Exists(t.Cwd) ? t.Cwd : fallbackCwd;
        int cols = (int)Math.Clamp(t.Cols, 20, 1000), rows = (int)Math.Clamp(t.Rows, 5, 1000);
        return OperatingSystem.IsWindows() ? WindowsPty.Start(shell, cwd, cols, rows) : UnixPty.Start(shell, cwd, cols, rows);
    }

    public async Task RunAsync(IPty pty, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var exit = pty.WaitForExitAsync(cts.Token);
        var output = PumpOutputAsync(pty, cts.Token);
        var input = PumpInputAsync(pty, cts.Token);
        var first = await Task.WhenAny(exit, input);
        if (first == exit)
        {
            await Task.WhenAny(output, Task.Delay(TimeSpan.FromMilliseconds(500), ct));
            await SendAsync(ChannelFrameKind.Exit, ChannelFrame.Exit(await exit), ct);
        }
        await cts.CancelAsync();
        pty.Kill();
    }

    private async Task PumpOutputAsync(IPty pty, CancellationToken ct)
    {
        var buf = new byte[ChunkSize];
        try
        {
            int n;
            while ((n = await pty.Output.ReadAsync(buf, ct)) > 0) await SendAsync(ChannelFrameKind.Data, buf.AsMemory(0, n), ct);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task PumpInputAsync(IPty pty, CancellationToken ct)
    {
        try
        {
            while (await ChannelFrame.ReadAsync(remote, ct) is { } f)
            {
                if (f.Kind == ChannelFrameKind.Data) { await pty.Input.WriteAsync(f.Payload, ct); await pty.Input.FlushAsync(ct); }
                else if (f.Kind == ChannelFrameKind.Resize) { var (c, r) = f.ReadResize(); pty.Resize(c, r); }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or InvalidDataException) { }
    }

    private async Task SendAsync(ChannelFrameKind kind, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        await _send.WaitAsync(ct);
        try { await ChannelFrame.WriteAsync(remote, kind, payload, ct); }
        finally { _send.Release(); }
    }
}
