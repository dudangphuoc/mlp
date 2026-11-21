using System.Collections.Concurrent;
using System.Threading.Channels;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using MLPlatform.Contracts.V1;
using Environment = System.Environment;   // Contracts.V1 also has an Environment message (catalog)

namespace MLPlatform.AgentProtocol;

/// <summary>Tracks commands awaiting results by command_id (control side).</summary>
public sealed class PendingCommands
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandResult>> _pending = new();

    public Task<CommandResult> Register(string commandId, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[commandId] = tcs;
        var cts = new CancellationTokenSource(timeout);
        cts.Token.Register(() =>
        {
            if (_pending.TryRemove(commandId, out var t))
                t.TrySetResult(new CommandResult { CommandId = commandId, Ok = false, ErrorReason = "FLEET_COMMAND_TIMEOUT" });
            cts.Dispose();
        });
        return tcs.Task;
    }

    public void Complete(CommandResult r)
    {
        if (_pending.TryRemove(r.CommandId, out var t)) t.TrySetResult(r);
    }

    public void FailAll(string reason)
    {
        foreach (var id in _pending.Keys)
            if (_pending.TryRemove(id, out var t))
                t.TrySetResult(new CommandResult { CommandId = id, Ok = false, ErrorReason = reason });
    }
}
