using System.Collections.Concurrent;

namespace MLPlatform.Api.Http.HostOps;

/// <summary>Live terminal sessions; caps 4 per machine and 2 per user (docs/agent-upgrade.md §1.1).</summary>
public sealed class TerminalSessions
{
    public const int PerHost = 4, PerUser = 2;
    private readonly ConcurrentDictionary<Guid, (Guid Host, Guid User)> _live = new();
    private readonly Lock _gate = new();

    public bool TryAdd(Guid session, Guid host, Guid user)
    {
        lock (_gate)
        {
            if (_live.Values.Count(v => v.Host == host) >= PerHost || _live.Values.Count(v => v.User == user) >= PerUser) return false;
            return _live.TryAdd(session, (host, user));
        }
    }

    public void Remove(Guid session) => _live.TryRemove(session, out _);
}
