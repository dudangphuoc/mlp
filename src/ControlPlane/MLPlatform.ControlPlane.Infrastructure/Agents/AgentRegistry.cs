using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using MLPlatform.AgentProtocol;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

public sealed class AgentRegistry
{
    private readonly ConcurrentDictionary<Guid, AgentSession> _sessions = new();
    public bool TryGet(Guid hostId, out AgentSession session) => _sessions.TryGetValue(hostId, out session!);
    public void Add(AgentSession s) => _sessions[s.HostId] = s;
    public void Remove(AgentSession s) => _sessions.TryRemove(new KeyValuePair<Guid, AgentSession>(s.HostId, s));
    public int Count => _sessions.Count;
    public IReadOnlyList<AgentSession> All => _sessions.Values.ToList();
}
