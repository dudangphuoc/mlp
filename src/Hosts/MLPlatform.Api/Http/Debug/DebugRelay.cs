using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace MLPlatform.Api.Http.Debug;

/// <summary>Pairs each debugger connection of a code run's container with a data socket the developer opens when it is
/// announced on the developer's control socket.</summary>
public sealed class DebugRelay
{
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();

    /// <summary>Holds the developer's control socket; true when the developer said the run ended by itself.</summary>
    public async Task<bool> ControlAsync(Guid runId, WebSocket control, CancellationToken ct)
    {
        var session = new Session(control);
        _sessions[runId] = session;
        try
        {
            var buffer = new byte[256];
            while (control.State == WebSocketState.Open)
            {
                var r = await control.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close) return false;
                if (Encoding.UTF8.GetString(buffer, 0, r.Count) == "done") return true;
            }
            return false;
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException) { return false; }
        finally { _sessions.TryRemove(new KeyValuePair<Guid, Session>(runId, session)); }
    }

    public async Task DeveloperAsync(Guid runId, Guid connection, WebSocket socket, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(runId, out var session) || !session.Pending.TryRemove(connection, out var pending)) return;
        pending.Developer.TrySetResult(socket);
        await pending.Done.Task.WaitAsync(ct);
    }

    public async Task RemoteAsync(Guid runId, WebSocket remote, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(runId, out var session)) return;
        var connection = Guid.NewGuid();
        var pending = new Pending();
        session.Pending[connection] = pending;
        try
        {
            await session.SendAsync($"open {connection}", ct);
            var developer = await pending.Developer.Task.WaitAsync(AnswerTimeout, ct);
            using var both = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await Task.WhenAny(PumpAsync(remote, developer, both.Token), PumpAsync(developer, remote, both.Token));
            await both.CancelAsync();
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or TimeoutException) { }
        finally
        {
            session.Pending.TryRemove(connection, out _);
            pending.Done.TrySetResult();
        }
    }

    private static async Task PumpAsync(WebSocket from, WebSocket to, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (from.State == WebSocketState.Open && to.State == WebSocketState.Open)
            {
                var r = await from.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close) return;
                await to.SendAsync(buffer.AsMemory(0, r.Count), WebSocketMessageType.Binary, r.EndOfMessage, ct);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
    }

    private sealed class Session(WebSocket control)
    {
        private readonly SemaphoreSlim _send = new(1, 1);

        public ConcurrentDictionary<Guid, Pending> Pending { get; } = new();

        public async Task SendAsync(string text, CancellationToken ct)
        {
            await _send.WaitAsync(ct);
            try { await control.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct); }
            finally { _send.Release(); }
        }
    }

    private sealed class Pending
    {
        public TaskCompletionSource<WebSocket> Developer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
